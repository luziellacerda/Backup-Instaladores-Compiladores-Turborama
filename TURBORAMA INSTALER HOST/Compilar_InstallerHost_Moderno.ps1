#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$SkipDownload,
    [switch]$AllowDirty,
    [string]$MSBuildPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# O redist oficial do DirectX e um SFX IExpress. Ele e mapeado somente como
# dados (LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE | LOAD_LIBRARY_AS_IMAGE_RESOURCE),
# portanto nenhum entry point, DllMain ou instalador e executado. O recurso
# RT_RCDATA/CABINET e copiado em blocos para um CAB temporario controlado.
if (-not ("TurboramaInstallerHost.ReadOnlyPeResource" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace TurboramaInstallerHost
{
    public static class ReadOnlyPeResource
    {
        private const uint LoadLibraryAsImageResource = 0x00000020;
        private const uint LoadLibraryAsDataFileExclusive = 0x00000040;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindResource(IntPtr module, string name, IntPtr type);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr module, IntPtr resourceInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr resourceData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SizeofResource(IntPtr module, IntPtr resourceInfo);

        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr module);

        public static int CopyNamedResource(
            string sourcePath,
            string resourceName,
            ushort resourceType,
            string destinationPath)
        {
            IntPtr module = LoadLibraryEx(
                sourcePath,
                IntPtr.Zero,
                LoadLibraryAsImageResource | LoadLibraryAsDataFileExclusive);
            if (module == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "LoadLibraryEx falhou no modo exclusivo somente-dados");
            }

            try
            {
                IntPtr resourceInfo = FindResource(module, resourceName, new IntPtr(resourceType));
                if (resourceInfo == IntPtr.Zero)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Recurso PE nomeado nao encontrado");
                }

                uint unsignedSize = SizeofResource(module, resourceInfo);
                if (unsignedSize == 0 || unsignedSize > Int32.MaxValue)
                {
                    throw new InvalidDataException("Tamanho invalido do recurso PE: " + unsignedSize);
                }

                IntPtr resourceData = LoadResource(module, resourceInfo);
                if (resourceData == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadResource falhou");
                }

                IntPtr resourcePointer = LockResource(resourceData);
                if (resourcePointer == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "LockResource falhou");
                }

                int size = (int)unsignedSize;
                int offset = 0;
                byte[] buffer = new byte[1024 * 1024];
                using (FileStream output = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    while (offset < size)
                    {
                        int count = Math.Min(buffer.Length, size - offset);
                        Marshal.Copy(IntPtr.Add(resourcePointer, offset), buffer, 0, count);
                        output.Write(buffer, 0, count);
                        offset += count;
                    }
                }

                return size;
            }
            finally
            {
                FreeLibrary(module);
            }
        }
    }
}
'@
}

$InstallerRoot = $PSScriptRoot
$ThisPipelinePath = $PSCommandPath
$ProjectDirectory = Join-Path $InstallerRoot "InstallerHost"
$ProjectPath = Join-Path $ProjectDirectory "InstallerHost.csproj"
$LockFilePath = Join-Path $ProjectDirectory "prerequisites.lock.json"
$PrerequisitesDirectory = Join-Path $ProjectDirectory "resources\prerequisites"
$DownloaderPath = Join-Path $InstallerRoot "Baixar_Prerequisitos_Instalador.ps1"
$ReleaseDirectory = Join-Path $ProjectDirectory "bin\Release"
$IntermediateDirectory = Join-Path $ProjectDirectory "obj\Release"
$BuildLogPath = Join-Path $InstallerRoot "InstallerHost-build.log"
$ExpectedLockSchemaVersion = 1
$ExpectedLockPayloadCount = 20

$ForbiddenPayloadPatterns = @(
    "(?i)\.partial",
    "(?i)^dotnetfx35",
    "(?i)^netfx3",
    "(?i)^openal",
    "(?i)^dokansetup",
    "(?i)^winfsp"
)

function Write-Section {
    param([Parameter(Mandatory = $true)][string]$Text)

    Write-Host ""
    Write-Host ("== {0} ==" -f $Text) -ForegroundColor Cyan
}

function Write-Ok {
    param([Parameter(Mandatory = $true)][string]$Text)

    Write-Host ("[OK] {0}" -f $Text) -ForegroundColor Green
}

function Write-Note {
    param([Parameter(Mandatory = $true)][string]$Text)

    Write-Host ("[INFO] {0}" -f $Text) -ForegroundColor DarkGray
}

function Get-RequiredPropertyValue {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        throw ("Campo obrigatorio ausente em {0}: {1}" -f $Context, $Name)
    }

    return $property.Value
}

function Get-ArrayPropertyValue {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $value = Get-RequiredPropertyValue -Object $Object -Name $Name -Context $Context
    return @($value | ForEach-Object { $_ })
}

function Get-OptionalArrayPropertyValue {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return @()
    }

    return @($property.Value | ForEach-Object { $_ })
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-BytesSha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($algorithm.ComputeHash($Bytes)) -replace "-", "").ToUpperInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-StreamSha256 {
    param([Parameter(Mandatory = $true)][System.IO.Stream]$Stream)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($algorithm.ComputeHash($Stream)) -replace "-", "").ToUpperInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

function Test-StreamMagic {
    param(
        [Parameter(Mandatory = $true)][System.IO.Stream]$Stream,
        [Parameter(Mandatory = $true)][string]$FileType
    )

    $header = New-Object byte[] 8
    $read = $Stream.Read($header, 0, $header.Length)
    switch ($FileType) {
        { $_ -in @("Exe", "Dll") } {
            return ($read -ge 2 -and $header[0] -eq 0x4D -and $header[1] -eq 0x5A)
        }
        "Msi" {
            $expected = [byte[]](0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1)
            if ($read -lt $expected.Length) {
                return $false
            }
            for ($index = 0; $index -lt $expected.Length; $index++) {
                if ($header[$index] -ne $expected[$index]) {
                    return $false
                }
            }
            return $true
        }
        "Zip" {
            return ($read -ge 4 -and $header[0] -eq 0x50 -and $header[1] -eq 0x4B -and
                (($header[2] -eq 0x03 -and $header[3] -eq 0x04) -or
                 ($header[2] -eq 0x05 -and $header[3] -eq 0x06) -or
                 ($header[2] -eq 0x07 -and $header[3] -eq 0x08)))
        }
        "Cab" {
            return ($read -ge 4 -and $header[0] -eq 0x4D -and $header[1] -eq 0x53 -and
                $header[2] -eq 0x43 -and $header[3] -eq 0x46)
        }
        default {
            return $false
        }
    }
}

function Test-FileMagic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$FileType
    )

    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read
    )
    try {
        return (Test-StreamMagic -Stream $stream -FileType $FileType)
    }
    finally {
        $stream.Dispose()
    }
}

function Get-CertificatePublicKeySha256 {
    param([Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    return Get-BytesSha256 -Bytes $Certificate.GetPublicKey()
}

function Assert-ExpectedAuthenticode {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate) {
        throw ("{0}: Authenticode invalido ({1}: {2})." -f $Context, $signature.Status, $signature.StatusMessage)
    }

    $expectedSubject = [string](Get-RequiredPropertyValue -Object $Expected -Name "signerSubject" -Context $Context)
    $expectedThumbprint = ([string](Get-RequiredPropertyValue -Object $Expected -Name "signerThumbprint" -Context $Context)).ToUpperInvariant()
    $expectedPublicKeyHash = ([string](Get-RequiredPropertyValue -Object $Expected -Name "certificatePublicKeySha256" -Context $Context)).ToUpperInvariant()
    $actualSubject = $signature.SignerCertificate.Subject
    $actualThumbprint = $signature.SignerCertificate.Thumbprint.ToUpperInvariant()
    $actualPublicKeyHash = Get-CertificatePublicKeySha256 -Certificate $signature.SignerCertificate

    if (-not $actualSubject.Equals($expectedSubject, [System.StringComparison]::Ordinal)) {
        throw ("{0}: subject Authenticode divergente. Esperado '{1}', obtido '{2}'." -f $Context, $expectedSubject, $actualSubject)
    }
    if ($actualThumbprint -ne $expectedThumbprint) {
        throw ("{0}: thumbprint divergente. Esperado {1}, obtido {2}." -f $Context, $expectedThumbprint, $actualThumbprint)
    }
    if ($actualPublicKeyHash -ne $expectedPublicKeyHash) {
        throw ("{0}: SHA256 da chave publica divergente. Esperado {1}, obtido {2}." -f $Context, $expectedPublicKeyHash, $actualPublicKeyHash)
    }

    return [pscustomobject]@{
        Status = $signature.Status.ToString()
        Subject = $actualSubject
        Thumbprint = $actualThumbprint
        CertificatePublicKeySha256 = $actualPublicKeyHash
    }
}

function Test-ForbiddenPayloadName {
    param([Parameter(Mandatory = $true)][string]$Name)

    foreach ($pattern in $ForbiddenPayloadPatterns) {
        if ($Name -match $pattern) {
            return $true
        }
    }
    return $false
}

function Assert-SafeLeafName {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ([string]::IsNullOrWhiteSpace($Name) -or
        $Name -ne [System.IO.Path]::GetFileName($Name) -or
        $Name.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw ("{0}: nome de arquivo inseguro: {1}" -f $Context, $Name)
    }
    if (Test-ForbiddenPayloadName -Name $Name) {
        throw ("{0}: nome proibido pela politica de incorporacao: {1}" -f $Context, $Name)
    }
}

function Assert-HexValue {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][int]$Length,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Value -cnotmatch ("^[A-F0-9]{{{0}}}$" -f $Length)) {
        throw ("{0}: valor hexadecimal deve ter {1} caracteres maiusculos." -f $Context, $Length)
    }
}

function Assert-LockFileSchema {
    if (-not (Test-Path -LiteralPath $LockFilePath -PathType Leaf)) {
        throw ("Lockfile obrigatorio nao encontrado: {0}" -f $LockFilePath)
    }

    $raw = Get-Content -LiteralPath $LockFilePath -Raw
    try {
        $lock = $raw | ConvertFrom-Json
    }
    catch {
        throw ("JSON invalido em prerequisites.lock.json: {0}" -f $_.Exception.Message)
    }

    $schemaVersion = [int](Get-RequiredPropertyValue -Object $lock -Name "schemaVersion" -Context "lockfile")
    if ($schemaVersion -ne $ExpectedLockSchemaVersion) {
        throw ("schemaVersion nao suportado: {0}; esperado {1}." -f $schemaVersion, $ExpectedLockSchemaVersion)
    }

    $catalogId = [string](Get-RequiredPropertyValue -Object $lock -Name "catalogId" -Context "lockfile")
    $releaseTag = [string](Get-RequiredPropertyValue -Object $lock -Name "releaseTag" -Context "lockfile")
    if ([string]::IsNullOrWhiteSpace($catalogId) -or [string]::IsNullOrWhiteSpace($releaseTag)) {
        throw "catalogId e releaseTag nao podem ser vazios."
    }

    $payloads = @(Get-ArrayPropertyValue -Object $lock -Name "payloads" -Context "lockfile")
    if ($payloads.Count -ne $ExpectedLockPayloadCount) {
        throw ("Lockfile deve conter exatamente {0} payloads; encontrou {1}." -f $ExpectedLockPayloadCount, $payloads.Count)
    }

    $seenPayloadNames = @{}
    foreach ($payload in $payloads) {
        $name = [string](Get-RequiredPropertyValue -Object $payload -Name "name" -Context "payload")
        $context = "payload '$name'"
        Assert-SafeLeafName -Name $name -Context $context
        if ($seenPayloadNames.ContainsKey($name)) {
            throw ("Nome duplicado no lockfile: {0}" -f $name)
        }
        $seenPayloadNames[$name] = $true

        $length = [long](Get-RequiredPropertyValue -Object $payload -Name "length" -Context $context)
        if ($length -le 0) {
            throw ("{0}: length deve ser positivo." -f $context)
        }
        $sha256 = [string](Get-RequiredPropertyValue -Object $payload -Name "sha256" -Context $context)
        Assert-HexValue -Value $sha256 -Length 64 -Context ("{0}.sha256" -f $context)

        $fileType = [string](Get-RequiredPropertyValue -Object $payload -Name "fileType" -Context $context)
        if ($fileType -notin @("Exe", "Msi", "Zip")) {
            throw ("{0}: fileType invalido: {1}." -f $context, $fileType)
        }
        $expectedExtension = @{ Exe = ".exe"; Msi = ".msi"; Zip = ".zip" }[$fileType]
        if (-not $name.EndsWith($expectedExtension, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw ("{0}: extensao nao corresponde a fileType {1}." -f $context, $fileType)
        }

        $installTier = [string](Get-RequiredPropertyValue -Object $payload -Name "installTier" -Context $context)
        if ($installTier -notin @("Required", "Recommended", "Optional")) {
            throw ("{0}: installTier invalido: {1}." -f $context, $installTier)
        }

        $sourceUrls = @(Get-ArrayPropertyValue -Object $payload -Name "sourceUrls" -Context $context)
        if ($sourceUrls.Count -eq 0) {
            throw ("{0}: sourceUrls vazio." -f $context)
        }
        foreach ($sourceUrl in $sourceUrls) {
            $uri = $null
            if (-not [System.Uri]::TryCreate([string]$sourceUrl, [System.UriKind]::Absolute, [ref]$uri) -or
                $uri.Scheme -ne "https" -or -not [string]::IsNullOrEmpty($uri.UserInfo)) {
                throw ("{0}: sourceUrl deve ser HTTPS absoluto sem credenciais: {1}" -f $context, $sourceUrl)
            }
        }

        if ($fileType -in @("Exe", "Msi")) {
            $productVersion = [string](Get-RequiredPropertyValue -Object $payload -Name "productVersion" -Context $context)
            if ([string]::IsNullOrWhiteSpace($productVersion)) {
                throw ("{0}: productVersion vazio." -f $context)
            }
            $null = Get-RequiredPropertyValue -Object $payload -Name "signerSubject" -Context $context
            $thumbprint = [string](Get-RequiredPropertyValue -Object $payload -Name "signerThumbprint" -Context $context)
            $publicKeyHash = [string](Get-RequiredPropertyValue -Object $payload -Name "certificatePublicKeySha256" -Context $context)
            Assert-HexValue -Value $thumbprint -Length 40 -Context ("{0}.signerThumbprint" -f $context)
            Assert-HexValue -Value $publicKeyHash -Length 64 -Context ("{0}.certificatePublicKeySha256" -f $context)
        }

        $entries = @(Get-OptionalArrayPropertyValue -Object $payload -Name "archiveEntries")
        if ($fileType -eq "Zip" -and $entries.Count -eq 0) {
            throw ("{0}: ZIP deve declarar archiveEntries." -f $context)
        }
        if ($fileType -eq "Msi" -and $entries.Count -gt 0) {
            throw ("{0}: archiveEntries nao e suportado para MSI." -f $context)
        }

        if ($entries.Count -gt 0) {
            $seenEntryNames = @{}
            foreach ($entry in $entries) {
                $entryName = [string](Get-RequiredPropertyValue -Object $entry -Name "name" -Context $context)
                $entryContext = "{0}.archiveEntries['{1}']" -f $context, $entryName
                if ([string]::IsNullOrWhiteSpace($entryName) -or
                    $entryName.StartsWith("/", [System.StringComparison]::Ordinal) -or
                    $entryName.StartsWith("\", [System.StringComparison]::Ordinal) -or
                    $entryName.Contains("\") -or
                    $entryName.Split('/') -contains ".." -or
                    $entryName.IndexOfAny([char[]]@("*", "?", "[", "]")) -ge 0) {
                    throw ("{0}: caminho inseguro." -f $entryContext)
                }
                if ($fileType -eq "Exe" -and
                    -not $entryName.Equals([System.IO.Path]::GetFileName($entryName), [System.StringComparison]::Ordinal)) {
                    throw ("{0}: archiveEntry de SFX deve ser um nome-folha." -f $entryContext)
                }
                if ($seenEntryNames.ContainsKey($entryName)) {
                    throw ("{0}: entrada duplicada." -f $entryContext)
                }
                $seenEntryNames[$entryName] = $true

                $entryLength = [long](Get-RequiredPropertyValue -Object $entry -Name "length" -Context $entryContext)
                if ($entryLength -le 0) {
                    throw ("{0}: length deve ser positivo." -f $entryContext)
                }
                $entryHash = [string](Get-RequiredPropertyValue -Object $entry -Name "sha256" -Context $entryContext)
                $entryThumbprint = [string](Get-RequiredPropertyValue -Object $entry -Name "signerThumbprint" -Context $entryContext)
                $entryPublicKeyHash = [string](Get-RequiredPropertyValue -Object $entry -Name "certificatePublicKeySha256" -Context $entryContext)
                $null = Get-RequiredPropertyValue -Object $entry -Name "signerSubject" -Context $entryContext
                Assert-HexValue -Value $entryHash -Length 64 -Context ("{0}.sha256" -f $entryContext)
                Assert-HexValue -Value $entryThumbprint -Length 40 -Context ("{0}.signerThumbprint" -f $entryContext)
                Assert-HexValue -Value $entryPublicKeyHash -Length 64 -Context ("{0}.certificatePublicKeySha256" -f $entryContext)
            }
        }
    }

    return [pscustomobject]@{
        Document = $lock
        Payloads = $payloads
        SHA256 = Get-Sha256 -Path $LockFilePath
        CatalogId = $catalogId
        ReleaseTag = $releaseTag
    }
}

function Get-ProjectEmbeddedPrerequisiteNames {
    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath -Raw
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($projectXml.NameTable)
    $namespaceManager.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
    $nodes = @($projectXml.SelectNodes("//msb:EmbeddedResource[@Include]", $namespaceManager))
    $names = New-Object System.Collections.Generic.List[string]
    $seen = @{}

    foreach ($node in $nodes) {
        $include = $node.GetAttribute("Include").Trim().Replace("/", "\")
        if (-not $include.StartsWith("resources\prerequisites\", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if ($include.IndexOfAny([char[]]@("*", "?", "[", "]")) -ge 0 -or $include.Contains('$(')) {
            throw ("EmbeddedResource de prerequisite deve ser explicito: {0}" -f $include)
        }

        $name = $include.Substring("resources\prerequisites\".Length)
        Assert-SafeLeafName -Name $name -Context "InstallerHost.csproj"
        if ($seen.ContainsKey($name)) {
            throw ("EmbeddedResource duplicado no csproj: {0}" -f $name)
        }
        $seen[$name] = $true
        $names.Add($name) | Out-Null
    }

    return @($names | ForEach-Object { $_ })
}

function Assert-ProjectMatchesLock {
    param([Parameter(Mandatory = $true)][object[]]$Payloads)

    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
        throw ("Projeto nao encontrado: {0}" -f $ProjectPath)
    }

    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath -Raw
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($projectXml.NameTable)
    $namespaceManager.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
    $frameworkNode = $projectXml.SelectSingleNode("/msb:Project/msb:PropertyGroup/msb:TargetFrameworkVersion", $namespaceManager)
    if ($null -eq $frameworkNode -or $frameworkNode.InnerText.Trim() -ne "v4.7.2") {
        throw "InstallerHost.csproj deve usar TargetFrameworkVersion v4.7.2."
    }

    $embeddedNames = @(Get-ProjectEmbeddedPrerequisiteNames)
    $lockNames = @($Payloads | ForEach-Object { [string]$_.name })
    if ($embeddedNames.Count -ne $ExpectedLockPayloadCount) {
        throw ("csproj deve incorporar exatamente {0} prerequisites; encontrou {1}." -f $ExpectedLockPayloadCount, $embeddedNames.Count)
    }

    $differences = @(Compare-Object -ReferenceObject $lockNames -DifferenceObject $embeddedNames -CaseSensitive)
    if ($differences.Count -gt 0) {
        $details = @($differences | ForEach-Object { "{0} ({1})" -f $_.InputObject, $_.SideIndicator }) -join ", "
        throw ("Conjunto EmbeddedResource diverge do lockfile: {0}" -f $details)
    }

    Write-Ok ("csproj == lockfile: conjunto exato de {0} payloads" -f $lockNames.Count)
}

function New-SafeTemporaryDirectory {
    param([Parameter(Mandatory = $true)][string]$Prefix)

    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
    $path = Join-Path $temporaryRoot ($Prefix + [Guid]::NewGuid().ToString("N"))
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not ([System.IO.Path]::GetFileName($fullPath)).StartsWith($Prefix, [System.StringComparison]::Ordinal)) {
        throw ("Diretorio temporario inseguro recusado: {0}" -f $fullPath)
    }
    $null = [System.IO.Directory]::CreateDirectory($fullPath)
    return $fullPath
}

function Remove-SafeTemporaryDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Prefix
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not ([System.IO.Path]::GetFileName($fullPath)).StartsWith($Prefix, [System.StringComparison]::Ordinal)) {
        throw ("Remocao de diretorio temporario recusada: {0}" -f $fullPath)
    }
    [System.IO.Directory]::Delete($fullPath, $true)
}

function Get-ArchiveEntryFileType {
    param([Parameter(Mandatory = $true)][string]$Name)

    switch ([System.IO.Path]::GetExtension($Name).ToLowerInvariant()) {
        ".exe" { return "Exe" }
        ".msi" { return "Msi" }
        ".dll" { return "Dll" }
        default { throw ("Tipo de archiveEntry nao suportado: {0}" -f $Name) }
    }
}

function Assert-ZipPayload {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Payload
    )

    $payloadName = [string]$Payload.name
    $expectedEntries = @(Get-ArrayPropertyValue -Object $Payload -Name "archiveEntries" -Context $payloadName)
    $temporaryPrefix = "TurboramaInstallerHostAudit-"
    $temporaryDirectory = New-SafeTemporaryDirectory -Prefix $temporaryPrefix
    $records = New-Object System.Collections.Generic.List[object]
    $archive = $null

    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
        $actualEntries = @($archive.Entries | ForEach-Object { $_ })
        if ($actualEntries.Count -ne $expectedEntries.Count) {
            throw ("{0}: ZIP contem {1} entradas; lockfile exige exatamente {2}." -f $payloadName, $actualEntries.Count, $expectedEntries.Count)
        }

        $actualByName = @{}
        foreach ($actualEntry in $actualEntries) {
            $actualName = $actualEntry.FullName.Replace("\", "/")
            if ([string]::IsNullOrWhiteSpace($actualName) -or
                $actualName.StartsWith("/", [System.StringComparison]::Ordinal) -or
                $actualName.Split('/') -contains ".." -or
                $actualName.EndsWith("/", [System.StringComparison]::Ordinal)) {
                throw ("{0}: entrada ZIP insegura ou diretorio inesperado: {1}" -f $payloadName, $actualEntry.FullName)
            }
            if ($actualByName.ContainsKey($actualName)) {
                throw ("{0}: entrada ZIP duplicada: {1}" -f $payloadName, $actualName)
            }
            $actualByName[$actualName] = $actualEntry
        }

        $entryIndex = 0
        foreach ($expectedEntry in $expectedEntries) {
            $entryName = [string]$expectedEntry.name
            if (-not $actualByName.ContainsKey($entryName)) {
                throw ("{0}: archiveEntry ausente: {1}" -f $payloadName, $entryName)
            }
            $entry = $actualByName[$entryName]
            $actualEntryName = $entry.FullName.Replace("\", "/")
            if (-not $actualEntryName.Equals($entryName, [System.StringComparison]::Ordinal)) {
                throw ("{0}: capitalizacao/nome de archiveEntry divergente. Esperado '{1}', obtido '{2}'." -f $payloadName, $entryName, $actualEntryName)
            }
            $expectedLength = [long]$expectedEntry.length
            if ($entry.Length -ne $expectedLength) {
                throw ("{0}/{1}: tamanho divergente. Esperado {2}; obtido {3}." -f $payloadName, $entryName, $expectedLength, $entry.Length)
            }

            $entryType = Get-ArchiveEntryFileType -Name $entryName
            $magicStream = $entry.Open()
            try {
                if (-not (Test-StreamMagic -Stream $magicStream -FileType $entryType)) {
                    throw ("{0}/{1}: assinatura magic invalida para {2}." -f $payloadName, $entryName, $entryType)
                }
            }
            finally {
                $magicStream.Dispose()
            }

            $hashStream = $entry.Open()
            try {
                $actualHash = Get-StreamSha256 -Stream $hashStream
            }
            finally {
                $hashStream.Dispose()
            }
            $expectedHash = ([string]$expectedEntry.sha256).ToUpperInvariant()
            if ($actualHash -ne $expectedHash) {
                throw ("{0}/{1}: SHA256 divergente. Esperado {2}; obtido {3}." -f $payloadName, $entryName, $expectedHash, $actualHash)
            }

            $entryIndex++
            $auditPath = Join-Path $temporaryDirectory ("{0:D2}-{1}" -f $entryIndex, [System.IO.Path]::GetFileName($entryName))
            $inputStream = $entry.Open()
            $outputStream = [System.IO.File]::Create($auditPath)
            try {
                $inputStream.CopyTo($outputStream)
            }
            finally {
                $outputStream.Dispose()
                $inputStream.Dispose()
            }
            $signatureRecord = Assert-ExpectedAuthenticode -Path $auditPath -Expected $expectedEntry -Context ("{0}/{1}" -f $payloadName, $entryName)

            $records.Add([pscustomobject]@{
                Name = $entryName
                Length = $entry.Length
                SHA256 = $actualHash
                Authenticode = $signatureRecord
            }) | Out-Null
        }
    }
    finally {
        if ($null -ne $archive) {
            $archive.Dispose()
        }
        Remove-SafeTemporaryDirectory -Path $temporaryDirectory -Prefix $temporaryPrefix
    }

    return @($records | ForEach-Object { $_ })
}

function Assert-PeCabinetPayload {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Payload
    )

    $payloadName = [string]$Payload.name
    $expectedEntries = @(Get-OptionalArrayPropertyValue -Object $Payload -Name "archiveEntries")
    if ($expectedEntries.Count -eq 0) {
        return @()
    }

    $expandPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) "expand.exe"
    if (-not (Test-Path -LiteralPath $expandPath -PathType Leaf)) {
        throw ("expand.exe oficial do Windows nao encontrado: {0}" -f $expandPath)
    }
    $expandSignature = Get-AuthenticodeSignature -LiteralPath $expandPath
    if ($expandSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $expandSignature.SignerCertificate -or
        -not $expandSignature.SignerCertificate.Subject.Contains("O=Microsoft Corporation")) {
        throw ("expand.exe do Windows sem assinatura Microsoft valida: {0}" -f $expandSignature.Status)
    }

    $temporaryPrefix = "TurboramaInstallerHostSfxAudit-"
    $temporaryDirectory = New-SafeTemporaryDirectory -Prefix $temporaryPrefix
    $cabinetPath = Join-Path $temporaryDirectory "sfx-cabinet.cab"
    $records = New-Object System.Collections.Generic.List[object]

    try {
        $cabinetLength = [TurboramaInstallerHost.ReadOnlyPeResource]::CopyNamedResource(
            $Path,
            "CABINET",
            [uint16]10,
            $cabinetPath)
        $cabinetItem = Get-Item -LiteralPath $cabinetPath
        if ($cabinetItem.Length -ne $cabinetLength -or
            -not (Test-FileMagic -Path $cabinetPath -FileType "Cab")) {
            throw ("{0}: recurso PE RT_RCDATA/CABINET ausente, truncado ou sem magic MSCF." -f $payloadName)
        }

        $entryIndex = 0
        foreach ($expectedEntry in $expectedEntries) {
            $entryIndex++
            $entryName = [string]$expectedEntry.name
            if (-not $entryName.Equals([System.IO.Path]::GetFileName($entryName), [System.StringComparison]::Ordinal)) {
                throw ("{0}/{1}: archiveEntry de SFX deve ser um nome-folha." -f $payloadName, $entryName)
            }

            $entryDirectory = Join-Path $temporaryDirectory ("entry-{0:D2}" -f $entryIndex)
            $null = [System.IO.Directory]::CreateDirectory($entryDirectory)
            $expandOutput = @(& $expandPath ("-F:{0}" -f $entryName) $cabinetPath $entryDirectory 2>&1)
            $expandExitCode = $LASTEXITCODE
            if ($expandExitCode -ne 0) {
                throw ("{0}/{1}: expand.exe falhou com codigo {2}: {3}" -f
                    $payloadName,
                    $entryName,
                    $expandExitCode,
                    (@($expandOutput | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ }) -join " | "))
            }

            $expandedItems = @(Get-ChildItem -LiteralPath $entryDirectory -Force -Recurse)
            $entryPath = Join-Path $entryDirectory $entryName
            if ($expandedItems.Count -ne 1 -or $expandedItems[0].PSIsContainer -or
                -not $expandedItems[0].FullName.Equals($entryPath, [System.StringComparison]::OrdinalIgnoreCase) -or
                -not $expandedItems[0].Name.Equals($entryName, [System.StringComparison]::Ordinal)) {
                throw ("{0}/{1}: extracao seletiva produziu itens inesperados." -f $payloadName, $entryName)
            }

            $expectedLength = [long]$expectedEntry.length
            if ($expandedItems[0].Length -ne $expectedLength) {
                throw ("{0}/{1}: tamanho divergente. Esperado {2}; obtido {3}." -f
                    $payloadName, $entryName, $expectedLength, $expandedItems[0].Length)
            }

            $entryType = Get-ArchiveEntryFileType -Name $entryName
            if (-not (Test-FileMagic -Path $entryPath -FileType $entryType)) {
                throw ("{0}/{1}: assinatura magic invalida para {2}." -f $payloadName, $entryName, $entryType)
            }
            $actualHash = Get-Sha256 -Path $entryPath
            $expectedHash = ([string]$expectedEntry.sha256).ToUpperInvariant()
            if ($actualHash -ne $expectedHash) {
                throw ("{0}/{1}: SHA256 divergente. Esperado {2}; obtido {3}." -f
                    $payloadName, $entryName, $expectedHash, $actualHash)
            }

            $signatureRecord = Assert-ExpectedAuthenticode -Path $entryPath -Expected $expectedEntry -Context ("{0}/{1}" -f $payloadName, $entryName)
            $records.Add([pscustomobject]@{
                Name = $entryName
                Length = $expandedItems[0].Length
                SHA256 = $actualHash
                Authenticode = $signatureRecord
                Container = "PE.RT_RCDATA/CABINET"
            }) | Out-Null
        }
    }
    finally {
        Remove-SafeTemporaryDirectory -Path $temporaryDirectory -Prefix $temporaryPrefix
    }

    Write-Ok ("{0}: RCDATA/CABINET validou {1} sem executar o SFX" -f
        $payloadName,
        (@($records | ForEach-Object { $_.Name }) -join ", "))
    return @($records | ForEach-Object { $_ })
}

function Test-LockedPayloadSet {
    param(
        [Parameter(Mandatory = $true)][object[]]$Payloads,
        [Parameter(Mandatory = $true)][string]$Directory,
        [switch]$AllowResiduals
    )

    $records = New-Object System.Collections.Generic.List[object]
    $missing = New-Object System.Collections.Generic.List[string]
    $errors = New-Object System.Collections.Generic.List[string]
    $residuals = New-Object System.Collections.Generic.List[string]
    $lockNames = @($Payloads | ForEach-Object { [string]$_.name })

    if (Test-Path -LiteralPath $Directory -PathType Container) {
        foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
            if (-not $item.PSIsContainer -and $lockNames -ccontains $item.Name) {
                continue
            }
            $residuals.Add($item.Name) | Out-Null
            if (-not $AllowResiduals) {
                $kind = if ($item.Name -match "(?i)\.partial") { "parcial" } else { "residual nao catalogado" }
                $errors.Add(("{0}: {1} em resources\prerequisites; somente arquivos do lockfile sao aceitos." -f $item.Name, $kind)) | Out-Null
            }
        }
    }

    foreach ($payload in $Payloads) {
        $name = [string]$payload.name
        $path = Join-Path $Directory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $missing.Add($name) | Out-Null
            continue
        }

        try {
            $item = Get-Item -LiteralPath $path
            $expectedLength = [long]$payload.length
            if ($item.Length -ne $expectedLength) {
                throw ("tamanho divergente: esperado {0}; obtido {1}." -f $expectedLength, $item.Length)
            }
            $actualHash = Get-Sha256 -Path $item.FullName
            $expectedHash = ([string]$payload.sha256).ToUpperInvariant()
            if ($actualHash -ne $expectedHash) {
                throw ("SHA256 divergente: esperado {0}; obtido {1}." -f $expectedHash, $actualHash)
            }
            if (-not (Test-FileMagic -Path $item.FullName -FileType ([string]$payload.fileType))) {
                throw ("magic invalido para fileType {0}." -f $payload.fileType)
            }

            $authenticode = $null
            $archiveEntries = @()
            if ([string]$payload.fileType -in @("Exe", "Msi")) {
                $authenticode = Assert-ExpectedAuthenticode -Path $item.FullName -Expected $payload -Context $name
                if ([string]$payload.fileType -eq "Exe") {
                    $actualVersion = $item.VersionInfo.ProductVersion
                    $expectedVersion = [string]$payload.productVersion
                    if (-not $actualVersion.Equals($expectedVersion, [System.StringComparison]::Ordinal)) {
                        throw ("productVersion divergente: esperado {0}; obtido {1}." -f $expectedVersion, $actualVersion)
                    }
                    $expectedSfxEntries = @(Get-OptionalArrayPropertyValue -Object $payload -Name "archiveEntries")
                    if ($expectedSfxEntries.Count -gt 0) {
                        $archiveEntries = @(Assert-PeCabinetPayload -Path $item.FullName -Payload $payload)
                    }
                }
            }
            else {
                $archiveEntries = @(Assert-ZipPayload -Path $item.FullName -Payload $payload)
            }

            $recordedProductVersion = $null
            if ([string]$payload.fileType -eq "Exe") {
                $recordedProductVersion = $item.VersionInfo.ProductVersion
            }
            elseif ([string]$payload.fileType -eq "Msi") {
                $recordedProductVersion = [string]$payload.productVersion
            }

            $records.Add([pscustomobject]@{
                Name = $name
                FileType = [string]$payload.fileType
                InstallTier = [string]$payload.installTier
                Length = $item.Length
                SHA256 = $actualHash
                ProductVersion = $recordedProductVersion
                Authenticode = $authenticode
                ArchiveEntries = $archiveEntries
            }) | Out-Null
            Write-Ok ("{0,-52} {1,10:N2} MB" -f $name, ($item.Length / 1MB))
        }
        catch {
            $errors.Add(("{0}: {1}" -f $name, $_.Exception.Message)) | Out-Null
        }
    }

    return [pscustomobject]@{
        Records = @($records | ForEach-Object { $_ })
        Missing = @($missing | ForEach-Object { $_ })
        Errors = @($errors | ForEach-Object { $_ })
        Residuals = @($residuals | ForEach-Object { $_ })
    }
}

function Copy-FileAtomically {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256
    )

    $destinationDirectory = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
        $null = New-Item -ItemType Directory -Path $destinationDirectory -Force
    }

    $temporaryPath = Join-Path $destinationDirectory (".{0}.{1}.partial" -f ([System.IO.Path]::GetFileName($Destination)), [Guid]::NewGuid().ToString("N"))
    try {
        Copy-Item -LiteralPath $Source -Destination $temporaryPath -Force
        $temporaryHash = Get-Sha256 -Path $temporaryPath
        if ($temporaryHash -ne $ExpectedSha256) {
            throw ("Copia temporaria falhou no SHA256: {0}" -f $temporaryPath)
        }
        Move-Item -LiteralPath $temporaryPath -Destination $Destination -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-DownloaderForMissingPayloads {
    param(
        [Parameter(Mandatory = $true)][object[]]$Payloads,
        [Parameter(Mandatory = $true)][string[]]$MissingNames
    )

    if (-not (Test-Path -LiteralPath $DownloaderPath -PathType Leaf)) {
        throw ("Downloader seguro nao encontrado: {0}" -f $DownloaderPath)
    }
    $powershellCommand = Get-Command "powershell.exe" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $powershellCommand) {
        throw "powershell.exe 5.1 nao encontrado para executar o downloader seguro."
    }

    $stagePrefix = "TurboramaInstallerHostDownload-"
    $stageDirectory = New-SafeTemporaryDirectory -Prefix $stagePrefix
    try {
        if (Test-Path -LiteralPath $PrerequisitesDirectory -PathType Container) {
            foreach ($payload in $Payloads) {
                $source = Join-Path $PrerequisitesDirectory ([string]$payload.name)
                if (Test-Path -LiteralPath $source -PathType Leaf) {
                    Copy-Item -LiteralPath $source -Destination (Join-Path $stageDirectory ([string]$payload.name)) -Force
                }
            }
        }

        Write-Host ("Executando downloader seguro em staging para: {0}" -f ($MissingNames -join ", ")) -ForegroundColor Yellow
        & $powershellCommand.Source -NoProfile -ExecutionPolicy Bypass -File $DownloaderPath `
            -ForBuild -TargetDirectory $stageDirectory
        if ($LASTEXITCODE -ne 0) {
            throw ("Downloader seguro falhou com codigo {0}. Nenhum payload foi publicado no projeto." -f $LASTEXITCODE)
        }

        $stageAudit = Test-LockedPayloadSet -Payloads $Payloads -Directory $stageDirectory -AllowResiduals
        if ($stageAudit.Errors.Count -gt 0 -or $stageAudit.Missing.Count -gt 0) {
            $details = @($stageAudit.Errors) + @($stageAudit.Missing | ForEach-Object { "ausente apos downloader: $_" })
            throw ("Staging do downloader nao corresponde ao lockfile: {0}" -f ($details -join " | "))
        }

        foreach ($name in $MissingNames) {
            $payload = $Payloads | Where-Object { ([string]$_.name).Equals($name, [System.StringComparison]::Ordinal) } | Select-Object -First 1
            Copy-FileAtomically -Source (Join-Path $stageDirectory $name) `
                -Destination (Join-Path $PrerequisitesDirectory $name) `
                -ExpectedSha256 ([string]$payload.sha256)
        }
    }
    finally {
        Remove-SafeTemporaryDirectory -Path $stageDirectory -Prefix $stagePrefix
    }
}

function Get-FileVersionMajor {
    param([Parameter(Mandatory = $true)][string]$Path)

    $versionText = (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion
    if (-not [string]::IsNullOrWhiteSpace($versionText) -and $versionText -match "^(?<major>[0-9]+)\.") {
        return [int]$Matches["major"]
    }
    return $null
}

function Resolve-MSBuild17 {
    param([string]$RequestedPath)

    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add($RequestedPath) | Out-Null
    }

    foreach ($vswhere in @(
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"),
        (Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Leaf) }) {
        foreach ($found in @(& $vswhere -all -products * -version "[17.0,18.0)" -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null)) {
            if (-not [string]::IsNullOrWhiteSpace($found)) {
                $candidates.Add($found.Trim()) | Out-Null
            }
        }
    }

    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) {
        foreach ($edition in @("BuildTools", "Community", "Professional", "Enterprise")) {
            $candidates.Add((Join-Path $root ("Microsoft Visual Studio\2022\{0}\MSBuild\Current\Bin\MSBuild.exe" -f $edition))) | Out-Null
        }
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }
        $resolved = (Resolve-Path -LiteralPath $candidate).Path
        $major = Get-FileVersionMajor -Path $resolved
        if ($major -eq 17) {
            return [pscustomobject]@{
                Path = $resolved
                Major = $major
                ProductVersion = (Get-Item -LiteralPath $resolved).VersionInfo.ProductVersion
            }
        }
    }

    return $null
}

function Resolve-Net472ReferenceAssemblies {
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) {
        $candidate = Join-Path $root "Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2"
        if ((Test-Path -LiteralPath (Join-Path $candidate "mscorlib.dll") -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $candidate "RedistList\FrameworkList.xml") -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

function Get-GitMetadata {
    $repositoryRoot = Split-Path -Parent $InstallerRoot
    $git = Get-Command "git.exe" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $git) {
        throw "Git nao encontrado; proveniencia do build nao pode ser estabelecida."
    }

    $previousOptionalLocks = [Environment]::GetEnvironmentVariable("GIT_OPTIONAL_LOCKS", "Process")
    try {
        $env:GIT_OPTIONAL_LOCKS = "0"
        $commitOutput = @(& $git.Source -C $repositoryRoot rev-parse --verify "HEAD^{commit}" 2>$null)
        $commitExitCode = $LASTEXITCODE
        $commit = $commitOutput | Select-Object -First 1
        if ($commitExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($commit) -or
            $commit.Trim() -cnotmatch "^[a-f0-9]{40}$") {
            throw "Nao foi possivel obter o commit Git atual."
        }

        $branchOutput = @(& $git.Source -C $repositoryRoot branch --show-current 2>$null)
        $branchExitCode = $LASTEXITCODE
        if ($branchExitCode -ne 0) {
            throw "Nao foi possivel obter a branch Git atual."
        }
        $branch = $branchOutput | Select-Object -First 1

        $statusLines = @(& $git.Source -C $repositoryRoot status --porcelain=v1 --untracked-files=all 2>$null)
        $statusExitCode = $LASTEXITCODE
        if ($statusExitCode -ne 0) {
            throw "Nao foi possivel obter o status Git atual."
        }

        return [pscustomobject]@{
            RepositoryRoot = $repositoryRoot
            Commit = $commit.Trim()
            Branch = if ($null -eq $branch) { "" } else { $branch.Trim() }
            Dirty = ($statusLines.Count -gt 0)
            Status = @($statusLines)
        }
    }
    finally {
        if ($null -eq $previousOptionalLocks) {
            Remove-Item Env:\GIT_OPTIONAL_LOCKS -ErrorAction SilentlyContinue
        }
        else {
            $env:GIT_OPTIONAL_LOCKS = $previousOptionalLocks
        }
    }
}

function Clear-SafeReleaseOutputs {
    $projectRoot = [System.IO.Path]::GetFullPath($ProjectDirectory).TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
    foreach ($target in @($ReleaseDirectory, $IntermediateDirectory)) {
        $fullTarget = [System.IO.Path]::GetFullPath($target)
        $leaf = [System.IO.Path]::GetFileName($fullTarget)
        $parentLeaf = [System.IO.Path]::GetFileName([System.IO.Path]::GetDirectoryName($fullTarget))
        if (-not $fullTarget.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            $leaf -ne "Release" -or $parentLeaf -notin @("bin", "obj")) {
            throw ("Limpeza recusada para caminho inseguro: {0}" -f $fullTarget)
        }
        if (Test-Path -LiteralPath $fullTarget -PathType Container) {
            Remove-Item -LiteralPath $fullTarget -Recurse -Force
        }
    }

    if (Test-Path -LiteralPath $BuildLogPath -PathType Leaf) {
        Remove-Item -LiteralPath $BuildLogPath -Force
    }
    Write-Ok "bin\Release e obj\Release limpos com alvos absolutos validados"
}

function Open-ImmutableBuildInputHandles {
    param(
        [Parameter(Mandatory = $true)][object[]]$Payloads,
        [Parameter(Mandatory = $true)]$InputSnapshot
    )

    $handles = New-Object System.Collections.Generic.List[System.IO.FileStream]
    $paths = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    try {
        foreach ($payload in $Payloads) {
            $paths.Add((Join-Path $PrerequisitesDirectory ([string]$payload.name))) | Out-Null
        }
        foreach ($record in $InputSnapshot.Files) {
            $paths.Add([string]$record.FullPath) | Out-Null
        }

        foreach ($path in $paths) {
            $fullPath = [System.IO.Path]::GetFullPath($path)
            if ($seen.ContainsKey($fullPath)) {
                continue
            }
            $seen[$fullPath] = $true
            $handle = [System.IO.File]::Open(
                $fullPath,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::Read
            )
            $handles.Add($handle) | Out-Null
        }
        return $handles
    }
    catch {
        foreach ($handle in $handles) {
            $handle.Dispose()
        }
        throw
    }
}

function Close-BuildInputHandles {
    param($Handles)

    if ($null -eq $Handles) {
        return
    }
    foreach ($handle in $Handles) {
        if ($null -ne $handle) {
            $handle.Dispose()
        }
    }
}

function Get-RelativePathText {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$FullPath
    )

    $baseUri = New-Object System.Uri($BasePath.TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar)
    $fullUri = New-Object System.Uri($FullPath)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($fullUri).ToString()).Replace("/", "\")
}

function Get-BuildInputSnapshot {
    $repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $InstallerRoot)).TrimEnd("\", "/")
    $repositoryPrefix = $repositoryRoot + [System.IO.Path]::DirectorySeparatorChar
    $projectRoot = [System.IO.Path]::GetFullPath($ProjectDirectory).TrimEnd("\", "/")
    $projectPrefix = $projectRoot + [System.IO.Path]::DirectorySeparatorChar
    $candidatePaths = New-Object System.Collections.Generic.List[string]

    foreach ($fixedPath in @($ThisPipelinePath, $DownloaderPath, $ProjectPath, $LockFilePath)) {
        $candidatePaths.Add([System.IO.Path]::GetFullPath($fixedPath)) | Out-Null
    }

    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath -Raw
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($projectXml.NameTable)
    $namespaceManager.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
    $inputNodes = @($projectXml.SelectNodes(
        "//msb:Compile[@Include] | //msb:EmbeddedResource[@Include] | //msb:Content[@Include] | //msb:None[@Include]",
        $namespaceManager))
    $relativeIncludes = New-Object System.Collections.Generic.List[string]

    foreach ($node in $inputNodes) {
        $include = $node.GetAttribute("Include").Trim().Replace("/", "\")
        if ($node.LocalName -eq "EmbeddedResource" -and
            $include.StartsWith("resources\prerequisites\", [System.StringComparison]::OrdinalIgnoreCase)) {
            # Os payloads grandes sao imutabilizados e registrados separadamente pelo lockfile.
            continue
        }
        $relativeIncludes.Add($include) | Out-Null
    }

    foreach ($propertyName in @("ApplicationManifest", "ApplicationIcon")) {
        $propertyNode = $projectXml.SelectSingleNode(
            "/msb:Project/msb:PropertyGroup/msb:$propertyName[normalize-space(text()) != '']",
            $namespaceManager)
        if ($null -ne $propertyNode) {
            $relativeIncludes.Add($propertyNode.InnerText.Trim().Replace("/", "\")) | Out-Null
        }
    }

    foreach ($include in $relativeIncludes) {
        if ([string]::IsNullOrWhiteSpace($include) -or
            [System.IO.Path]::IsPathRooted($include) -or
            $include.Contains('$(') -or $include.Contains('@(') -or $include.Contains('%(') -or
            $include.IndexOfAny([char[]]@("*", "?", "[", "]")) -ge 0 -or
            $include.Split('\') -contains "..") {
            throw ("Input de build dinamico/inseguro recusado no csproj: {0}" -f $include)
        }

        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $ProjectDirectory $include))
        if (-not $fullPath.StartsWith($projectPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw ("Input de build fora do projeto recusado: {0}" -f $include)
        }
        $candidatePaths.Add($fullPath) | Out-Null
    }

    $seen = @{}
    $records = New-Object System.Collections.Generic.List[object]
    foreach ($fullPath in $candidatePaths) {
        if (-not $fullPath.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw ("Input de build fora do repositorio recusado: {0}" -f $fullPath)
        }
        if ($seen.ContainsKey($fullPath)) {
            continue
        }
        $seen[$fullPath] = $true

        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw ("Input de build declarado nao encontrado: {0}" -f $fullPath)
        }
        $item = Get-Item -LiteralPath $fullPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw ("Input de build em reparse point recusado: {0}" -f $fullPath)
        }

        $stream = [System.IO.File]::Open(
            $fullPath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read)
        try {
            $length = $stream.Length
            $sha256 = Get-StreamSha256 -Stream $stream
        }
        finally {
            $stream.Dispose()
        }

        $records.Add([pscustomobject]@{
            Path = Get-RelativePathText -BasePath $repositoryRoot -FullPath $fullPath
            FullPath = $fullPath
            Length = $length
            SHA256 = $sha256
        }) | Out-Null
    }

    $sortedRecords = @($records | Sort-Object -Property Path)
    $canonicalText = @($sortedRecords | ForEach-Object {
        "{0}`0{1}`0{2}" -f $_.Path, $_.Length, $_.SHA256
    }) -join "`n"
    $aggregateHash = Get-BytesSha256 -Bytes ([System.Text.Encoding]::UTF8.GetBytes($canonicalText))
    return [pscustomobject]@{
        Count = $sortedRecords.Count
        AggregateSHA256 = $aggregateHash
        Files = $sortedRecords
    }
}

function Assert-BuildInputsUnchanged {
    param(
        [Parameter(Mandatory = $true)]$Before,
        [Parameter(Mandatory = $true)]$After
    )

    if ($Before.Count -eq $After.Count -and
        ([string]$Before.AggregateSHA256).Equals([string]$After.AggregateSHA256, [System.StringComparison]::Ordinal)) {
        return
    }

    $beforeMap = @{}
    foreach ($record in $Before.Files) { $beforeMap[[string]$record.Path] = $record }
    $afterMap = @{}
    foreach ($record in $After.Files) { $afterMap[[string]$record.Path] = $record }
    $allPaths = @(@($beforeMap.Keys) + @($afterMap.Keys) | Sort-Object -Unique)
    $changes = New-Object System.Collections.Generic.List[string]
    foreach ($path in $allPaths) {
        if (-not $beforeMap.ContainsKey($path)) {
            $changes.Add("adicionado: $path") | Out-Null
        }
        elseif (-not $afterMap.ContainsKey($path)) {
            $changes.Add("removido: $path") | Out-Null
        }
        elseif ([long]$beforeMap[$path].Length -ne [long]$afterMap[$path].Length -or
            -not ([string]$beforeMap[$path].SHA256).Equals([string]$afterMap[$path].SHA256, [System.StringComparison]::Ordinal)) {
            $changes.Add("alterado: $path") | Out-Null
        }
    }

    throw ("Inputs do build mudaram durante a compilacao: {0}" -f (@($changes) -join "; "))
}

function Get-ReleaseAllowlist {
    $allowlist = New-Object System.Collections.Generic.List[string]
    foreach ($fixed in @("InstallerHost.exe", "InstallerHost.pdb", "InstallerHost-build.log")) {
        $allowlist.Add($fixed) | Out-Null
    }

    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath -Raw
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($projectXml.NameTable)
    $namespaceManager.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
    $contentNodes = @($projectXml.SelectNodes("//msb:Content[@Include][msb:CopyToOutputDirectory]", $namespaceManager))
    foreach ($node in $contentNodes) {
        $copyMode = $node.SelectSingleNode("msb:CopyToOutputDirectory", $namespaceManager).InnerText.Trim()
        if ($copyMode -eq "Never") {
            continue
        }
        if ($null -ne $node.SelectSingleNode("msb:Link", $namespaceManager) -or
            $null -ne $node.SelectSingleNode("msb:TargetPath", $namespaceManager)) {
            throw ("Content com Link/TargetPath exige revisao explicita da allowlist: {0}" -f $node.GetAttribute("Include"))
        }

        $include = $node.GetAttribute("Include").Trim().Replace("/", "\")
        if ($include.Contains('$(') -or $include.Contains("..")) {
            throw ("Content inseguro para allowlist: {0}" -f $include)
        }
        $directoryPart = Split-Path -Parent $include
        $leafPattern = Split-Path -Leaf $include
        $sourceDirectory = if ([string]::IsNullOrWhiteSpace($directoryPart)) { $ProjectDirectory } else { Join-Path $ProjectDirectory $directoryPart }

        if ($leafPattern.IndexOfAny([char[]]@("*", "?", "[", "]")) -ge 0) {
            throw ("Content com wildcard e proibido na allowlist publicavel: {0}" -f $include)
        }

        $source = Join-Path $ProjectDirectory $include
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw ("Content declarado nao encontrado: {0}" -f $include)
        }
        $allowlist.Add($include) | Out-Null
    }

    return @($allowlist | Select-Object -Unique)
}

function Assert-ReleaseMatchesAllowlist {
    param([Parameter(Mandatory = $true)][string[]]$Allowlist)

    $actualFiles = @(Get-ChildItem -LiteralPath $ReleaseDirectory -Recurse -File | ForEach-Object {
        Get-RelativePathText -BasePath $ReleaseDirectory -FullPath $_.FullName
    })
    $differences = @(Compare-Object -ReferenceObject $Allowlist -DifferenceObject $actualFiles -CaseSensitive)
    if ($differences.Count -gt 0) {
        $details = @($differences | ForEach-Object { "{0} ({1})" -f $_.InputObject, $_.SideIndicator }) -join ", "
        throw ("Saida Release diverge da allowlist limpa: {0}" -f $details)
    }
    Write-Ok ("Saida Release limitada a {0} arquivo(s) aprovados" -f $actualFiles.Count)
}

function Get-ArtifactAuthenticode {
    param([Parameter(Mandatory = $true)][string]$Path)

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -notin @(
        [System.Management.Automation.SignatureStatus]::Valid,
        [System.Management.Automation.SignatureStatus]::NotSigned
    )) {
        throw ("Assinatura do InstallerHost.exe esta em estado inseguro: {0} ({1})." -f $signature.Status, $signature.StatusMessage)
    }

    $certificate = $signature.SignerCertificate
    return [pscustomobject]@{
        Status = $signature.Status.ToString()
        StatusMessage = $signature.StatusMessage
        Subject = if ($null -eq $certificate) { $null } else { $certificate.Subject }
        Thumbprint = if ($null -eq $certificate) { $null } else { $certificate.Thumbprint.ToUpperInvariant() }
        CertificatePublicKeySha256 = if ($null -eq $certificate) { $null } else { Get-CertificatePublicKeySha256 -Certificate $certificate }
    }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Write-BuildManifest {
    param(
        [Parameter(Mandatory = $true)]$LockInfo,
        [Parameter(Mandatory = $true)]$PayloadAudit,
        [Parameter(Mandatory = $true)]$GitInfoBefore,
        [Parameter(Mandatory = $true)]$GitInfoAfter,
        [Parameter(Mandatory = $true)]$BuildInputSnapshot,
        [Parameter(Mandatory = $true)]$MSBuildInfo,
        [Parameter(Mandatory = $true)][string]$Net472Path,
        [Parameter(Mandatory = $true)][string[]]$ReleaseAllowlist
    )

    $exePath = Join-Path $ReleaseDirectory "InstallerHost.exe"
    $exeItem = Get-Item -LiteralPath $exePath
    $exeHash = Get-Sha256 -Path $exePath
    $exeSignature = Get-ArtifactAuthenticode -Path $exePath

    $signatureValid = $exeSignature.Status -eq "Valid"
    $headStable = ([string]$GitInfoBefore.Commit).Equals([string]$GitInfoAfter.Commit, [System.StringComparison]::Ordinal)
    $gitDirty = [bool]($GitInfoBefore.Dirty -or $GitInfoAfter.Dirty)
    $nonPublishable = [bool]($AllowDirty -or $gitDirty -or -not $headStable -or -not $signatureValid)
    $publishable = [bool](-not $nonPublishable)
    $releaseChannel = if ($publishable) { "Release" } else { "Prerelease" }
    $effectiveReleaseTag = if ($publishable) { $LockInfo.ReleaseTag } else { $LockInfo.ReleaseTag + "-prerelease" }
    $reasons = New-Object System.Collections.Generic.List[string]
    if ($AllowDirty) { $reasons.Add("AllowDirtyOverride") | Out-Null }
    if ($gitDirty) { $reasons.Add("GitWorkingTreeDirty") | Out-Null }
    if ($GitInfoBefore.Dirty) { $reasons.Add("GitPreBuildDirty") | Out-Null }
    if ($GitInfoAfter.Dirty) { $reasons.Add("GitPostBuildDirty") | Out-Null }
    if (-not $headStable) { $reasons.Add("GitHeadChangedDuringBuild") | Out-Null }
    if (-not $signatureValid) { $reasons.Add("InstallerHostNotSigned") | Out-Null }

    $buildLog = Get-Content -LiteralPath (Join-Path $ReleaseDirectory "InstallerHost-build.log") -Raw
    $warningLines = @($buildLog -split "`r?`n" | Where-Object { $_ -match "(?i)\bwarning\s+[A-Z]+[0-9]+" })
    $errorLines = @($buildLog -split "`r?`n" | Where-Object { $_ -match "(?i)\berror\s+[A-Z]+[0-9]+" })

    $outputRecords = @($ReleaseAllowlist | Sort-Object | ForEach-Object {
        $outputPath = Join-Path $ReleaseDirectory $_
        $outputItem = Get-Item -LiteralPath $outputPath
        [pscustomobject]@{
            Path = $_
            Length = $outputItem.Length
            SHA256 = Get-Sha256 -Path $outputItem.FullName
        }
    })

    $manifest = [ordered]@{
        SchemaVersion = 2
        CreatedUtc = [DateTime]::UtcNow.ToString("o")
        Pipeline = "Compilar_InstallerHost_Moderno.ps1"
        Project = "TURBORAMA INSTALER HOST\InstallerHost\InstallerHost.csproj"
        Configuration = "Release"
        Platform = "AnyCPU"
        Lockfile = [ordered]@{
            Path = "TURBORAMA INSTALER HOST\InstallerHost\prerequisites.lock.json"
            SchemaVersion = $ExpectedLockSchemaVersion
            CatalogId = $LockInfo.CatalogId
            ReleaseTag = $LockInfo.ReleaseTag
            SHA256 = $LockInfo.SHA256
            PayloadCount = $LockInfo.Payloads.Count
        }
        Git = [ordered]@{
            Commit = $GitInfoAfter.Commit
            Branch = $GitInfoAfter.Branch
            Dirty = $GitInfoAfter.Dirty
            AllowDirty = [bool]$AllowDirty
            Status = @($GitInfoAfter.Status)
            HeadStable = $headStable
            PreBuild = [ordered]@{
                Commit = $GitInfoBefore.Commit
                Branch = $GitInfoBefore.Branch
                Dirty = $GitInfoBefore.Dirty
                Status = @($GitInfoBefore.Status)
            }
            PostBuild = [ordered]@{
                Commit = $GitInfoAfter.Commit
                Branch = $GitInfoAfter.Branch
                Dirty = $GitInfoAfter.Dirty
                Status = @($GitInfoAfter.Status)
            }
        }
        Toolchain = [ordered]@{
            MSBuildPath = $MSBuildInfo.Path
            MSBuildMajor = $MSBuildInfo.Major
            MSBuildProductVersion = $MSBuildInfo.ProductVersion
            TargetFramework = "v4.7.2"
            ReferenceAssembliesPath = $Net472Path
        }
        Build = [ordered]@{
            WarningCount = $warningLines.Count
            ErrorCount = $errorLines.Count
            NonPublishable = $nonPublishable
            Publishable = $publishable
            ReleaseChannel = $releaseChannel
            EffectiveReleaseTag = $effectiveReleaseTag
            Reasons = @($reasons | ForEach-Object { $_ })
        }
        InstallerHost = [ordered]@{
            Length = $exeItem.Length
            SHA256 = $exeHash
            Authenticode = $exeSignature
        }
        EmbeddedInputs = [ordered]@{
            Count = $PayloadAudit.Records.Count
            Payloads = @($PayloadAudit.Records)
        }
        BuildInputs = [ordered]@{
            Count = $BuildInputSnapshot.Count
            AggregateSHA256 = $BuildInputSnapshot.AggregateSHA256
            Files = @($BuildInputSnapshot.Files | ForEach-Object {
                [ordered]@{
                    Path = $_.Path
                    Length = $_.Length
                    SHA256 = $_.SHA256
                }
            })
        }
        Outputs = $outputRecords
    }

    $manifestPath = Join-Path $ReleaseDirectory "InstallerHost-build-manifest.json"
    $manifestHashPath = Join-Path $ReleaseDirectory "InstallerHost-build-manifest.json.sha256"
    $exeHashPath = Join-Path $ReleaseDirectory "InstallerHost.exe.sha256"
    Write-Utf8NoBom -Path $manifestPath -Content (($manifest | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
    $manifestHash = Get-Sha256 -Path $manifestPath
    Write-Utf8NoBom -Path $manifestHashPath -Content (("{0} *InstallerHost-build-manifest.json" -f $manifestHash) + [Environment]::NewLine)
    Write-Utf8NoBom -Path $exeHashPath -Content (("{0} *InstallerHost.exe" -f $exeHash) + [Environment]::NewLine)

    Write-Ok ("InstallerHost.exe: {0} bytes; SHA256 {1}" -f $exeItem.Length, $exeHash)
    Write-Ok ("Authenticode do EXE: {0}" -f $exeSignature.Status)
    Write-Ok ("Manifesto SHA256: {0}" -f $manifestHash)
    Write-Ok ("Publicavel: {0}; canal: {1}" -f $publishable, $manifest.Build.ReleaseChannel)
}

try {
    Write-Host ""
    Write-Host "TURBORAMA - PIPELINE REPRODUZIVEL DO INSTALLERHOST" -ForegroundColor Cyan
    Write-Host "O lockfile e a unica fonte de integridade. Nenhum instalador sera executado." -ForegroundColor DarkGray
    if ($DryRun) {
        Write-Host "Modo DRY-RUN: sem download, limpeza, compilacao ou escrita de manifesto." -ForegroundColor Yellow
    }

    Write-Section "Lockfile e projeto"
    $lockInfo = Assert-LockFileSchema
    Write-Ok ("Lockfile schema {0}; {1} payloads; SHA256 {2}" -f $ExpectedLockSchemaVersion, $lockInfo.Payloads.Count, $lockInfo.SHA256)
    Assert-ProjectMatchesLock -Payloads $lockInfo.Payloads
    $preflightReleaseAllowlist = @(Get-ReleaseAllowlist)
    Write-Ok ("Content de Release explicito e sem wildcard: {0} arquivo(s)" -f ($preflightReleaseAllowlist.Count - 3))
    $buildInputSnapshotPreflight = Get-BuildInputSnapshot
    Write-Ok ("Inputs rastreados: {0} arquivo(s); SHA256 agregado {1}" -f
        $buildInputSnapshotPreflight.Count,
        $buildInputSnapshotPreflight.AggregateSHA256)

    Write-Section "Toolchain e proveniencia"
    $msbuildInfo = Resolve-MSBuild17 -RequestedPath $MSBuildPath
    if ($null -eq $msbuildInfo) {
        throw "MSBuild major 17 (Visual Studio 2022/Build Tools) nao encontrado. MSBuild 18+ e versoes antigas sao recusados."
    }
    Write-Ok ("MSBuild {0}: {1}" -f $msbuildInfo.Major, $msbuildInfo.Path)

    $net472Path = Resolve-Net472ReferenceAssemblies
    if ($null -eq $net472Path) {
        throw ".NET Framework 4.7.2 Developer/Targeting Pack nao encontrado. O runtime sozinho nao basta."
    }
    Write-Ok (".NET Framework 4.7.2 reference assemblies: {0}" -f $net472Path)

    $gitInfoPreflight = Get-GitMetadata
    if ($gitInfoPreflight.Dirty -and -not $AllowDirty) {
        throw "Git working tree esta dirty. O build padrao publicavel foi recusado; use -AllowDirty somente para build local de teste."
    }
    if ($AllowDirty) {
        Write-Host "[NAO PUBLICAVEL] -AllowDirty ativo; o manifesto registrara NonPublishable=true." -ForegroundColor Yellow
    }
    else {
        Write-Ok ("Git limpo no commit {0}" -f $gitInfoPreflight.Commit)
    }

    Write-Section "Auditoria criptografica dos payloads"
    $payloadAudit = Test-LockedPayloadSet -Payloads $lockInfo.Payloads -Directory $PrerequisitesDirectory
    if ($payloadAudit.Errors.Count -gt 0) {
        throw ("Auditoria reprovada: {0}" -f (@($payloadAudit.Errors) -join " | "))
    }

    if ($payloadAudit.Missing.Count -gt 0) {
        if ($DryRun -or $SkipDownload) {
            $mode = if ($DryRun) { "DryRun" } else { "SkipDownload" }
            throw ("Payloads ausentes ({0}); download automatico desativado por -{1}." -f (@($payloadAudit.Missing) -join ", "), $mode)
        }
        Invoke-DownloaderForMissingPayloads -Payloads $lockInfo.Payloads -MissingNames $payloadAudit.Missing
        $payloadAudit = Test-LockedPayloadSet -Payloads $lockInfo.Payloads -Directory $PrerequisitesDirectory
        if ($payloadAudit.Errors.Count -gt 0 -or $payloadAudit.Missing.Count -gt 0) {
            throw ("Auditoria apos downloader reprovada: {0}; ausentes: {1}" -f (@($payloadAudit.Errors) -join " | "), (@($payloadAudit.Missing) -join ", "))
        }
    }
    Write-Ok ("Payloads validados contra o lockfile: {0}/{0}" -f $payloadAudit.Records.Count)

    if ((Get-Sha256 -Path $LockFilePath) -ne $lockInfo.SHA256) {
        throw "prerequisites.lock.json mudou durante a auditoria; reinicie o pipeline."
    }

    if ($DryRun) {
        Write-Section "Dry-run concluido"
        Write-Ok "Lockfile, csproj, Content, inputs rastreados, Git, toolchain, payloads, certificados, ZIPs e ancoras SFX aprovados"
        exit 0
    }

    Write-Section "Build Release limpo"
    $buildInputSnapshotBefore = Get-BuildInputSnapshot
    Assert-BuildInputsUnchanged -Before $buildInputSnapshotPreflight -After $buildInputSnapshotBefore
    $gitInfoBefore = Get-GitMetadata
    if (-not $AllowDirty) {
        if (-not ([string]$gitInfoBefore.Commit).Equals([string]$gitInfoPreflight.Commit, [System.StringComparison]::Ordinal)) {
            throw ("HEAD mudou entre o preflight e o build: {0} -> {1}." -f $gitInfoPreflight.Commit, $gitInfoBefore.Commit)
        }
        if ($gitInfoBefore.Dirty) {
            throw ("Arvore Git ficou dirty antes do build: {0}" -f (@($gitInfoBefore.Status) -join " | "))
        }
    }

    $buildInputHandles = $null
    try {
        $buildInputHandles = Open-ImmutableBuildInputHandles `
            -Payloads $lockInfo.Payloads `
            -InputSnapshot $buildInputSnapshotBefore
        Clear-SafeReleaseOutputs
        $fileLoggerArgument = "/flp:logfile={0};verbosity=normal;encoding=UTF-8" -f $BuildLogPath
        & $msbuildInfo.Path $ProjectPath "/t:Rebuild" "/p:Configuration=Release" "/p:Platform=AnyCPU" "/m" "/nologo" "/verbosity:minimal" "/fl" $fileLoggerArgument
        if ($LASTEXITCODE -ne 0) {
            throw ("MSBuild falhou com codigo {0}. Log: {1}" -f $LASTEXITCODE, $BuildLogPath)
        }
        if (-not (Test-Path -LiteralPath $BuildLogPath -PathType Leaf)) {
            throw ("MSBuild nao gerou o log esperado: {0}" -f $BuildLogPath)
        }
        Copy-Item -LiteralPath $BuildLogPath -Destination (Join-Path $ReleaseDirectory "InstallerHost-build.log") -Force

        $releaseAllowlist = @(Get-ReleaseAllowlist)
        Assert-ReleaseMatchesAllowlist -Allowlist $releaseAllowlist

        if ((Get-Sha256 -Path $LockFilePath) -ne $lockInfo.SHA256) {
            throw "prerequisites.lock.json mudou durante o build; artefato recusado."
        }

        $buildInputSnapshotAfter = Get-BuildInputSnapshot
        Assert-BuildInputsUnchanged -Before $buildInputSnapshotBefore -After $buildInputSnapshotAfter
        Write-Ok ("Inputs do build permaneceram imutaveis: {0} arquivo(s); SHA256 agregado {1}" -f
            $buildInputSnapshotAfter.Count,
            $buildInputSnapshotAfter.AggregateSHA256)

        $gitInfoAfter = Get-GitMetadata
        $headStable = ([string]$gitInfoBefore.Commit).Equals([string]$gitInfoAfter.Commit, [System.StringComparison]::Ordinal)
        if (-not $AllowDirty) {
            if (-not $headStable) {
                throw ("HEAD mudou durante o build: {0} -> {1}. Artefato recusado." -f $gitInfoBefore.Commit, $gitInfoAfter.Commit)
            }
            if ($gitInfoAfter.Dirty) {
                throw ("Arvore Git ficou dirty durante o build; artefato recusado: {0}" -f (@($gitInfoAfter.Status) -join " | "))
            }
            Write-Ok ("Proveniencia pos-build confirmada: HEAD {0}; arvore limpa" -f $gitInfoAfter.Commit)
        }
        else {
            Write-Note ("Proveniencia pos-build registrada sob -AllowDirty: HEAD {0}; dirty={1}; status={2}" -f
                $gitInfoAfter.Commit,
                $gitInfoAfter.Dirty,
                (@($gitInfoAfter.Status) -join " | "))
        }

        Write-Section "Manifesto e publicabilidade"
        Write-BuildManifest -LockInfo $lockInfo -PayloadAudit $payloadAudit `
            -GitInfoBefore $gitInfoBefore -GitInfoAfter $gitInfoAfter `
            -BuildInputSnapshot $buildInputSnapshotAfter `
            -MSBuildInfo $msbuildInfo -Net472Path $net472Path -ReleaseAllowlist $releaseAllowlist
    }
    finally {
        Close-BuildInputHandles -Handles $buildInputHandles
    }

    Write-Section "Concluido"
    Write-Ok ("Release local em {0}" -f $ReleaseDirectory)
    Write-Note "O InstallerHost nao foi iniciado. Nenhum instalador foi executado."
}
catch {
    Write-Host ""
    Write-Host ("FALHA: {0}" -f $_.Exception.Message) -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($_.ScriptStackTrace)) {
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    }
    exit 1
}
