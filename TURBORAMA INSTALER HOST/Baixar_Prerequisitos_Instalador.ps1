#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$AuditOnly,
    [switch]$ForBuild,
    [switch]$IncludeOptional,
    [switch]$IncludeLegacy,
    [ValidateRange(10, 600)]
    [int]$TimeoutSeconds = 120,
    [string]$TargetDirectory
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if ($DryRun -and $AuditOnly) {
    throw "Use apenas um modo: -DryRun ou -AuditOnly."
}

# PowerShell 5.1 pode escolher TLS legado por padrao. Este script nunca desativa
# a validacao de certificado e nunca executa os instaladores baixados.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$LockPath = Join-Path $ScriptRoot "InstallerHost\prerequisites.lock.json"
if (-not (Test-Path -LiteralPath $LockPath -PathType Leaf)) {
    throw "Lockfile obrigatorio nao encontrado: $LockPath"
}

try {
    $PayloadLock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
}
catch {
    throw "Lockfile invalido: $($_.Exception.Message)"
}

if ($PayloadLock.schemaVersion -ne 1 -or $null -eq $PayloadLock.payloads -or $PayloadLock.payloads.Count -ne 20) {
    throw "Lockfile recusado: esperado schemaVersion=1 e exatamente 20 payloads."
}

$lockedNames = @($PayloadLock.payloads | ForEach-Object { $_.name })
if (@($lockedNames | Sort-Object -Unique).Count -ne 20 -or
    @($lockedNames | Where-Object { [IO.Path]::GetFileName($_) -ne $_ }).Count -ne 0) {
    throw "Lockfile recusado: nomes duplicados ou caminhos nao simples."
}
if ([string]::IsNullOrWhiteSpace($TargetDirectory)) {
    $TargetDir = Join-Path $ScriptRoot "InstallerHost\resources\prerequisites"
}
else {
    $TargetDir = [IO.Path]::GetFullPath($TargetDirectory)
}

$MicrosoftHosts = @(
    "aka.ms",
    "go.microsoft.com",
    "download.microsoft.com",
    "download.visualstudio.microsoft.com",
    "builds.dotnet.microsoft.com",
    "dotnetcli.azureedge.net",
    "msedge.sf.dl.delivery.mp.microsoft.com"
)
$GitHubReleaseHosts = @(
    "github.com",
    "objects.githubusercontent.com",
    "release-assets.githubusercontent.com"
)
$MicrosoftPublishers = @("Microsoft Corporation", "Microsoft Windows")

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-InnerFileSpec {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet("Exe", "Msi", "Dll")][string]$FileType,
        [Parameter(Mandatory = $true)][long]$MinBytes,
        [Parameter(Mandatory = $true)][long]$MaxBytes,
        [string]$Sha256,
        [string[]]$PublisherTokens = @(),
        [string]$SignerSubject,
        [string]$SignerThumbprint,
        [string]$CertificatePublicKeySha256,
        [switch]$RequireSignature,
        [string]$ExpectedVersion,
        [string]$VersionPrefix
    )

    return [pscustomobject]@{
        Name = $Name
        FileType = $FileType
        MinBytes = $MinBytes
        MaxBytes = $MaxBytes
        Sha256 = $Sha256
        PublisherTokens = @($PublisherTokens)
        SignerSubject = $SignerSubject
        SignerThumbprint = $SignerThumbprint
        CertificatePublicKeySha256 = $CertificatePublicKeySha256
        RequireSignature = [bool]$RequireSignature
        ExpectedVersion = $ExpectedVersion
        VersionPrefix = $VersionPrefix
    }
}

function New-SourceSpec {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet("Exe", "Msi")][string]$FileType,
        [Parameter(Mandatory = $true)][string[]]$Urls,
        [Parameter(Mandatory = $true)][string[]]$AllowedHosts,
        [Parameter(Mandatory = $true)][long]$MinBytes,
        [Parameter(Mandatory = $true)][long]$MaxBytes,
        [string]$Sha256,
        [string[]]$PublisherTokens = @(),
        [string]$SignerSubject,
        [string]$SignerThumbprint,
        [string]$CertificatePublicKeySha256,
        [switch]$RequireSignature,
        [string]$ExpectedVersion,
        [string]$VersionPrefix
    )

    return [pscustomobject]@{
        Name = $Name
        FileType = $FileType
        Urls = @($Urls)
        AllowedHosts = @($AllowedHosts)
        MinBytes = $MinBytes
        MaxBytes = $MaxBytes
        Sha256 = $Sha256
        PublisherTokens = @($PublisherTokens)
        SignerSubject = $SignerSubject
        SignerThumbprint = $SignerThumbprint
        CertificatePublicKeySha256 = $CertificatePublicKeySha256
        RequireSignature = [bool]$RequireSignature
        ExpectedVersion = $ExpectedVersion
        VersionPrefix = $VersionPrefix
    }
}

function New-PackageSpec {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet("Download", "Wrap", "ExistingOnly")][string]$Mode,
        [Parameter(Mandatory = $true)][ValidateSet("Exe", "Msi", "Zip")][string]$FileType,
        [Parameter(Mandatory = $true)][long]$MinBytes,
        [Parameter(Mandatory = $true)][long]$MaxBytes,
        [string]$Sha256,
        [string[]]$PublisherTokens = @(),
        [string]$SignerSubject,
        [string]$SignerThumbprint,
        [string]$CertificatePublicKeySha256,
        [switch]$RequireSignature,
        [string]$ExpectedVersion,
        [string]$VersionPrefix,
        [string[]]$Urls = @(),
        [string[]]$AllowedHosts = @(),
        [object[]]$ArchiveEntries = @(),
        [long]$MaxExpandedBytes = 0,
        [object]$Source,
        [string]$SourceEntryName,
        [switch]$Required,
        [switch]$Optional,
        [switch]$Legacy,
        [string]$Note
    )

    return [pscustomobject]@{
        Name = $Name
        Mode = $Mode
        FileType = $FileType
        MinBytes = $MinBytes
        MaxBytes = $MaxBytes
        Sha256 = $Sha256
        PublisherTokens = @($PublisherTokens)
        SignerSubject = $SignerSubject
        SignerThumbprint = $SignerThumbprint
        CertificatePublicKeySha256 = $CertificatePublicKeySha256
        RequireSignature = [bool]$RequireSignature
        ExpectedVersion = $ExpectedVersion
        VersionPrefix = $VersionPrefix
        Urls = @($Urls)
        AllowedHosts = @($AllowedHosts)
        ArchiveEntries = @($ArchiveEntries)
        MaxExpandedBytes = $MaxExpandedBytes
        Source = $Source
        SourceEntryName = $SourceEntryName
        Required = [bool]$Required
        Optional = [bool]$Optional
        Legacy = [bool]$Legacy
        Note = $Note
    }
}

function Get-LegacyVcSpec {
    param(
        [Parameter(Mandatory = $true)][string]$Year,
        [Parameter(Mandatory = $true)][ValidateSet("x86", "x64")][string]$Architecture,
        [Parameter(Mandatory = $true)][long]$InnerBytes,
        [Parameter(Mandatory = $true)][string]$InnerSha256
    )

    $bundleName = "vcredist${Year}_${Architecture}.zip"
    $installerName = "vcredist${Year}_${Architecture}.exe"
    $entry = New-InnerFileSpec -Name $installerName -FileType Exe `
        -MinBytes ($InnerBytes - 1) -MaxBytes ($InnerBytes + 1) `
        -Sha256 $InnerSha256 -PublisherTokens $MicrosoftPublishers -RequireSignature

    return New-PackageSpec -Name $bundleName -Mode ExistingOnly -FileType Zip `
        -MinBytes 100000 -MaxBytes 20000000 -ArchiveEntries @($entry) `
        -MaxExpandedBytes 25000000 -Optional -Legacy `
        -Note "Legado fora de suporte: nao ha download automatico de espelho de terceiros. Uma copia existente so e aceita quando contem exatamente o EXE Microsoft assinado e com hash conhecido."
}

$packages = @(
    New-PackageSpec -Name "vc_redist.x64.exe" -Mode Download -FileType Exe `
        -MinBytes 8000000 -MaxBytes 80000000 -PublisherTokens $MicrosoftPublishers -RequireSignature `
        -Urls @("https://aka.ms/vc14/vc_redist.x64.exe") -AllowedHosts $MicrosoftHosts -Required `
        -Note "Alias oficial mutavel; Authenticode e editor sao obrigatorios."

    New-PackageSpec -Name "vc_redist.x86.exe" -Mode Download -FileType Exe `
        -MinBytes 8000000 -MaxBytes 60000000 -PublisherTokens $MicrosoftPublishers -RequireSignature `
        -Urls @("https://aka.ms/vc14/vc_redist.x86.exe") -AllowedHosts $MicrosoftHosts -Required `
        -Note "Alias oficial mutavel; Authenticode e editor sao obrigatorios."

    New-PackageSpec -Name "NDP48-x86-x64-AllOS-ENU.exe" -Mode Download -FileType Exe `
        -MinBytes 100000000 -MaxBytes 180000000 `
        -Sha256 "0A3A390C47E639D0F7FC65B21195FEE6B7F65B066F80F70C60FAB191D14B7E40" `
        -PublisherTokens $MicrosoftPublishers -RequireSignature -VersionPrefix "4.8." `
        -Urls @(
            "https://go.microsoft.com/fwlink/?linkid=2088631",
            "https://download.microsoft.com/download/f/3/a/f3a6af84-da23-40a5-8d1c-49cc10c8e76f/NDP48-x86-x64-AllOS-ENU.exe"
        ) -AllowedHosts $MicrosoftHosts -Required `
        -Note "Instalador offline fixo do .NET Framework 4.8."

    New-PackageSpec -Name "directx_Jun2010_redist.exe" -Mode Download -FileType Exe `
        -MinBytes 90000000 -MaxBytes 120000000 `
        -Sha256 "053F76DCBB28802E23341B6A787E3B0791C0FA5C8D4D011B1044172DBF89C73B" `
        -PublisherTokens $MicrosoftPublishers -RequireSignature -VersionPrefix "9.00." `
        -Urls @("https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/directx_Jun2010_redist.exe") `
        -AllowedHosts $MicrosoftHosts -Required `
        -Note "Pacote oficial fixo; adiciona apenas bibliotecas DirectX legadas lado a lado."

    New-PackageSpec -Name "windowsdesktop-runtime-8.0-win-x64.exe" -Mode Download -FileType Exe `
        -MinBytes 40000000 -MaxBytes 120000000 -PublisherTokens $MicrosoftPublishers -RequireSignature -VersionPrefix "8." `
        -Urls @("https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe") -AllowedHosts $MicrosoftHosts -Required `
        -Note "Canal oficial 8.0 mutavel; o hash obtido e sempre mostrado no relatorio."

    New-PackageSpec -Name "windowsdesktop-runtime-8.0-win-x86.exe" -Mode Download -FileType Exe `
        -MinBytes 35000000 -MaxBytes 110000000 -PublisherTokens $MicrosoftPublishers -RequireSignature -VersionPrefix "8." `
        -Urls @("https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x86.exe") -AllowedHosts $MicrosoftHosts -Required `
        -Note "Canal oficial 8.0 mutavel; o hash obtido e sempre mostrado no relatorio."

    New-PackageSpec -Name "windowsdesktop-runtime-10.0-win-x64.exe" -Mode Download -FileType Exe `
        -MinBytes 40000000 -MaxBytes 130000000 -PublisherTokens $MicrosoftPublishers -RequireSignature -VersionPrefix "10." `
        -Urls @("https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe") -AllowedHosts $MicrosoftHosts -Required `
        -Note "LTS moderno preferencial para aplicativos e front-ends Windows x64."

    New-PackageSpec -Name "windowsdesktop-runtime-10.0-win-x86.exe" -Mode Download -FileType Exe `
        -MinBytes 35000000 -MaxBytes 120000000 -PublisherTokens $MicrosoftPublishers -RequireSignature -VersionPrefix "10." `
        -Urls @("https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x86.exe") -AllowedHosts $MicrosoftHosts -Optional `
        -Note "Compatibilidade x86 sob demanda; nao e instalada indiscriminadamente."

    New-PackageSpec -Name "MicrosoftEdgeWebView2RuntimeInstallerX64.exe" -Mode Download -FileType Exe `
        -MinBytes 100000000 -MaxBytes 400000000 -PublisherTokens $MicrosoftPublishers -RequireSignature `
        -Urls @("https://go.microsoft.com/fwlink/?linkid=2124701") -AllowedHosts $MicrosoftHosts -Required `
        -Note "Evergreen Standalone x64 oficial; nao e o bootstrapper que depende de Internet."

    New-PackageSpec -Name "xnafx40_redist.msi" -Mode Download -FileType Msi `
        -MinBytes 5000000 -MaxBytes 12000000 `
        -Sha256 "2B03C130C4BB5F106B9619BEA8150201F7E982709CFE9A0CC8BDE75FF0A83B27" `
        -PublisherTokens $MicrosoftPublishers -RequireSignature `
        -Urls @("https://download.microsoft.com/download/5/3/A/53A804C8-EC78-43CD-A0F0-2FB4D45603D3/xnafx40_redist.msi") `
        -AllowedHosts $MicrosoftHosts -Optional `
        -Note "XNA 4.0 Refresh oficial, somente para jogos antigos que realmente o exigem."

    New-PackageSpec -Name "DokanSetup.zip" -Mode Wrap -FileType Zip `
        -MinBytes 1000000 -MaxBytes 30000000 `
        -ArchiveEntries @(
            (New-InnerFileSpec -Name "DokanSetup.exe" -FileType Exe -MinBytes 19000000 -MaxBytes 20000000 `
                -Sha256 "BF602263A594F595B4FDD8C4E822172B103DE93F07FD6A51A8FF69569BFD1460" `
                -PublisherTokens @("LEOSAC", "Dokan") -RequireSignature -VersionPrefix "2.3.1.")
        ) -MaxExpandedBytes 25000000 `
        -Source (New-SourceSpec -Name "DokanSetup.exe" -FileType Exe `
            -Urls @("https://github.com/dokan-dev/dokany/releases/download/v2.3.1.1000/DokanSetup.exe") `
            -AllowedHosts $GitHubReleaseHosts -MinBytes 19000000 -MaxBytes 20000000 `
            -Sha256 "BF602263A594F595B4FDD8C4E822172B103DE93F07FD6A51A8FF69569BFD1460" `
            -PublisherTokens @("LEOSAC", "Dokan") -RequireSignature -VersionPrefix "2.3.1.") `
        -SourceEntryName "DokanSetup.exe" -Optional `
        -Note "Driver opcional; fonte e release oficial fixada, validada antes de criar o ZIP compativel."

    New-PackageSpec -Name "winfsp.zip" -Mode Wrap -FileType Zip `
        -MinBytes 500000 -MaxBytes 5000000 `
        -ArchiveEntries @(
            (New-InnerFileSpec -Name "winfsp-2.1.25156.msi" -FileType Msi -MinBytes 2000000 -MaxBytes 2400000 `
                -Sha256 "073A70E00F77423E34BED98B86E600DEF93393BA5822204FAC57A29324DB9F7A" `
                -PublisherTokens @("NAVIMATICS", "WinFsp") -RequireSignature)
        ) -MaxExpandedBytes 4000000 `
        -Source (New-SourceSpec -Name "winfsp-2.1.25156.msi" -FileType Msi `
            -Urls @("https://github.com/winfsp/winfsp/releases/download/v2.1/winfsp-2.1.25156.msi") `
            -AllowedHosts $GitHubReleaseHosts -MinBytes 2000000 -MaxBytes 2400000 `
            -Sha256 "073A70E00F77423E34BED98B86E600DEF93393BA5822204FAC57A29324DB9F7A" `
            -PublisherTokens @("NAVIMATICS", "WinFsp") -RequireSignature) `
        -SourceEntryName "winfsp-2.1.25156.msi" -Optional `
        -Note "Driver opcional; fonte e release oficial fixada, validada antes de criar o ZIP compativel."

    Get-LegacyVcSpec -Year "2005" -Architecture x64 -InnerBytes 3175832 `
        -InnerSha256 "0551A61C85B718E1FA015B0C3E3F4C4EEA0637055536C00E7969286B4FA663E0"
    Get-LegacyVcSpec -Year "2005" -Architecture x86 -InnerBytes 2707352 `
        -InnerSha256 "4EE4DA0FE62D5FA1B5E80C6E6D88A4A2F8B3B140C35DA51053D0D7B72A381D29"
    Get-LegacyVcSpec -Year "2008" -Architecture x64 -InnerBytes 5207896 `
        -InnerSha256 "B811F2C047A3E828517C234BD4AA4883E1EC591D88FAD21289AE68A6915A6665"
    Get-LegacyVcSpec -Year "2008" -Architecture x86 -InnerBytes 4479832 `
        -InnerSha256 "6B3E4C51C6C0E5F68C8A72B497445AF3DBF976394CBB62AA23569065C28DEEB6"
    Get-LegacyVcSpec -Year "2010" -Architecture x64 -InnerBytes 10274136 `
        -InnerSha256 "CC7EC044218C72A9A15FCA2363BAED8FC51095EE3B2A7593476771F9EBA3D223"
    Get-LegacyVcSpec -Year "2010" -Architecture x86 -InnerBytes 8990552 `
        -InnerSha256 "67313B3D1BC86E83091E8DE22981F14968F1A7FB12EB7AD467754C40CD94CC3D"
    Get-LegacyVcSpec -Year "2012" -Architecture x64 -InnerBytes 7186992 `
        -InnerSha256 "681BE3E5BA9FD3DA02C09D7E565ADFA078640ED66A0D58583EFAD2C1E3CC4064"
    Get-LegacyVcSpec -Year "2012" -Architecture x86 -InnerBytes 6554576 `
        -InnerSha256 "B924AD8062EAF4E70437C8BE50FA612162795FF0839479546CE907FFA8D6E386"
    Get-LegacyVcSpec -Year "2013" -Architecture x64 -InnerBytes 7200744 `
        -InnerSha256 "A4BBA7701E355AE29C403431F871A537897C363E215CAFE706615E270984F17C"
    Get-LegacyVcSpec -Year "2013" -Architecture x86 -InnerBytes 6510136 `
        -InnerSha256 "53B605D1100AB0A88B867447BBF9274B5938125024BA01F5105A9E178A3DCDBD"

    New-PackageSpec -Name "openal-offline.zip" -Mode ExistingOnly -FileType Zip `
        -MinBytes 100000 -MaxBytes 1000000 `
        -ArchiveEntries @(
            (New-InnerFileSpec -Name "Win32/OpenAL32.dll" -FileType Dll -MinBytes 350000 -MaxBytes 370000 `
                -Sha256 "D8AC4A710BD3AD5F08428EF6B53B25506186B55F027A7766C7CF15BB264AB9C1" -VersionPrefix "1.23.1"),
            (New-InnerFileSpec -Name "Win64/OpenAL32.dll" -FileType Dll -MinBytes 270000 -MaxBytes 290000 `
                -Sha256 "B5F9B2FC24F89F208217EFFC793652F3303436BED13928CE75D393CD41513678" -VersionPrefix "1.23.1")
        ) -MaxExpandedBytes 1000000 -Optional `
        -Note "Mantido apenas para compatibilidade. Nao e baixado automaticamente porque as DLLs upstream nao possuem Authenticode; os hashes internos ficam fixados."
)

$blockedFiles = [ordered]@{
    "dotNetFx35_WX_10_x86_x64.exe" = ".NET Framework 3.5 deve ser ativado como recurso do Windows/DISM; o antigo pacote comunitario nao possui assinatura."
    "dotNetFx35_WX_10_x86_x64_z.exe" = ".NET Framework 3.5 deve ser ativado como recurso do Windows/DISM; o antigo pacote comunitario nao possui assinatura."
    "NDP48-Web.exe" = "Bootstrapper online nao serve para o pacote offline; use NDP48-x86-x64-AllOS-ENU.exe."
    "MicrosoftEdgeWebview2Setup.exe" = "Bootstrapper online nao serve para o pacote offline; use o Evergreen Standalone x64."
    "DokanSetup.zip" = "Driver opcional fora do pacote padrao; instale somente pela release oficial quando uma ferramenta exigir."
    "winfsp.zip" = "Driver opcional fora do pacote padrao; instale somente pela release oficial quando uma ferramenta exigir."
    "openal-offline.zip" = "OpenAL global nao faz parte do pacote; use a copia fornecida pelo jogo legitimo ou OpenAL Soft oficial."
}

function Test-AllowedHost {
    param(
        [Parameter(Mandatory = $true)][string]$HostName,
        [Parameter(Mandatory = $true)][string[]]$AllowedHosts
    )

    foreach ($allowed in $AllowedHosts) {
        if ($HostName.Equals($allowed, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Assert-TrustedHttpsUri {
    param(
        [Parameter(Mandatory = $true)][Uri]$Uri,
        [Parameter(Mandatory = $true)][string[]]$AllowedHosts
    )

    if (-not $Uri.IsAbsoluteUri -or $Uri.Scheme -ne "https") {
        throw "URL recusada (HTTPS obrigatorio): $Uri"
    }
    if (-not [string]::IsNullOrEmpty($Uri.UserInfo)) {
        throw "URL com credenciais embutidas foi recusada: $Uri"
    }
    if (-not (Test-AllowedHost -HostName $Uri.DnsSafeHost -AllowedHosts $AllowedHosts)) {
        throw "Host fora da lista permitida: $($Uri.DnsSafeHost)"
    }
}

function Receive-HttpsFile {
    param(
        [Parameter(Mandatory = $true)][Uri]$Uri,
        [Parameter(Mandatory = $true)][string[]]$AllowedHosts,
        [Parameter(Mandatory = $true)][string]$PartialPath,
        [Parameter(Mandatory = $true)][long]$MaxBytes
    )

    if (Test-Path -LiteralPath $PartialPath) {
        throw "Arquivo parcial inesperado ja existe: $PartialPath"
    }

    $current = $Uri
    for ($redirect = 0; $redirect -le 8; $redirect++) {
        Assert-TrustedHttpsUri -Uri $current -AllowedHosts $AllowedHosts

        $request = [Net.HttpWebRequest][Net.WebRequest]::Create($current)
        $request.Method = "GET"
        $request.AllowAutoRedirect = $false
        $request.UserAgent = "TurboramaPrerequisiteBuilder/3.0"
        $request.Accept = "application/octet-stream,application/x-msdownload,application/zip,*/*"
        $request.Timeout = $TimeoutSeconds * 1000
        $request.ReadWriteTimeout = $TimeoutSeconds * 1000
        if ($null -ne $request.Proxy) {
            $request.Proxy.Credentials = [Net.CredentialCache]::DefaultCredentials
        }

        $response = $null
        try {
            $response = [Net.HttpWebResponse]$request.GetResponse()
            $status = [int]$response.StatusCode

            if ($status -in @(301, 302, 303, 307, 308)) {
                $location = $response.Headers["Location"]
                if ([string]::IsNullOrWhiteSpace($location)) {
                    throw "Redirecionamento sem cabecalho Location em $current"
                }
                $next = New-Object Uri($current, $location)
                Assert-TrustedHttpsUri -Uri $next -AllowedHosts $AllowedHosts
                $current = $next
                continue
            }

            if ($status -ne 200) {
                throw "HTTP $status ao baixar $current"
            }
            if ($response.ContentLength -gt $MaxBytes) {
                throw "Content-Length excede o limite de $MaxBytes bytes."
            }

            $inputStream = $null
            $outputStream = $null
            try {
                $inputStream = $response.GetResponseStream()
                $outputStream = New-Object IO.FileStream(
                    $PartialPath,
                    [IO.FileMode]::CreateNew,
                    [IO.FileAccess]::Write,
                    [IO.FileShare]::None,
                    1048576,
                    [IO.FileOptions]::SequentialScan
                )
                $buffer = New-Object byte[] 1048576
                [long]$written = 0
                while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $written += $read
                    if ($written -gt $MaxBytes) {
                        throw "Download excedeu o limite de $MaxBytes bytes."
                    }
                    $outputStream.Write($buffer, 0, $read)
                }
                $outputStream.Flush($true)
            }
            finally {
                if ($null -ne $outputStream) { $outputStream.Dispose() }
                if ($null -ne $inputStream) { $inputStream.Dispose() }
            }

            return $current.AbsoluteUri
        }
        finally {
            if ($null -ne $response) { $response.Dispose() }
        }
    }

    throw "Limite de redirecionamentos excedido para $Uri"
}

function Assert-FileMagic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet("Exe", "Msi", "Zip", "Dll")][string]$FileType
    )

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $header = New-Object byte[] 8
        $read = $stream.Read($header, 0, $header.Length)
    }
    finally {
        $stream.Dispose()
    }

    if ($FileType -in @("Exe", "Dll")) {
        if ($read -lt 2 -or $header[0] -ne 0x4D -or $header[1] -ne 0x5A) {
            throw "Cabecalho PE/MZ invalido."
        }
        return
    }

    if ($FileType -eq "Msi") {
        [byte[]]$ole = @(0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1)
        if ($read -lt 8) { throw "Cabecalho MSI/OLE truncado." }
        for ($index = 0; $index -lt $ole.Length; $index++) {
            if ($header[$index] -ne $ole[$index]) { throw "Cabecalho MSI/OLE invalido." }
        }
        return
    }

    if ($read -lt 4 -or $header[0] -ne 0x50 -or $header[1] -ne 0x4B -or
        -not (($header[2] -eq 0x03 -and $header[3] -eq 0x04) -or
              ($header[2] -eq 0x05 -and $header[3] -eq 0x06) -or
              ($header[2] -eq 0x07 -and $header[3] -eq 0x08))) {
        throw "Cabecalho ZIP invalido."
    }
}

function Assert-FlatFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Spec
    )

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -ne $Spec.MinBytes -or $item.Length -ne $Spec.MaxBytes) {
        throw "Tamanho divergente: $($item.Length) bytes; esperado exatamente $($Spec.MinBytes)."
    }

    Assert-FileMagic -Path $Path -FileType $Spec.FileType
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($Spec.Sha256) -or $Spec.Sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw "SHA-256 esperado ausente ou invalido no lockfile."
    }
    if ($hash -ne $Spec.Sha256.ToUpperInvariant()) {
        throw "SHA-256 divergente: $hash"
    }

    $subject = ""
    if ($Spec.RequireSignature) {
        $signature = Get-AuthenticodeSignature -FilePath $Path
        if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
            $null -eq $signature.SignerCertificate) {
            throw "Authenticode invalido: $($signature.Status) - $($signature.StatusMessage)"
        }

        $certificate = $signature.SignerCertificate
        $subject = $certificate.Subject
        if ([string]::IsNullOrWhiteSpace($Spec.SignerSubject) -or
            -not $subject.Equals($Spec.SignerSubject, [StringComparison]::Ordinal)) {
            throw "Subject Authenticode inesperado: $subject"
        }
        if ([string]::IsNullOrWhiteSpace($Spec.SignerThumbprint) -or
            -not $certificate.Thumbprint.Equals($Spec.SignerThumbprint, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Thumbprint Authenticode inesperado: $($certificate.Thumbprint)"
        }

        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $publicKeyHash = [BitConverter]::ToString($sha.ComputeHash($certificate.GetPublicKey())).Replace("-", "")
        }
        finally {
            $sha.Dispose()
        }
        if ([string]::IsNullOrWhiteSpace($Spec.CertificatePublicKeySha256) -or
            -not $publicKeyHash.Equals($Spec.CertificatePublicKeySha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Chave publica Authenticode inesperada: $publicKeyHash"
        }
    }

    if ((-not [string]::IsNullOrWhiteSpace($Spec.ExpectedVersion) -or
        -not [string]::IsNullOrWhiteSpace($Spec.VersionPrefix)) -and $Spec.FileType -ne "Msi") {
        $version = (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion
        if ([string]::IsNullOrWhiteSpace($version)) {
            $version = (Get-Item -LiteralPath $Path).VersionInfo.FileVersion
        }
        if (-not [string]::IsNullOrWhiteSpace($Spec.ExpectedVersion) -and
            -not $version.Equals($Spec.ExpectedVersion, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Versao inesperada: '$version'; esperado exatamente '$($Spec.ExpectedVersion)'."
        }
        if ([string]::IsNullOrWhiteSpace($Spec.ExpectedVersion) -and
            ([string]::IsNullOrWhiteSpace($version) -or
             -not $version.StartsWith($Spec.VersionPrefix, [StringComparison]::OrdinalIgnoreCase))) {
            throw "Versao inesperada: '$version'; prefixo esperado '$($Spec.VersionPrefix)'."
        }
    }

    return [pscustomobject]@{
        Bytes = [long]$item.Length
        Sha256 = $hash
        Publisher = $subject
    }
}

function Assert-ZipContents {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Spec
    )

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $archive = $null
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read, $false)
        if ($archive.Entries.Count -gt 128) {
            throw "ZIP possui entradas demais: $($archive.Entries.Count)."
        }

        [long]$expanded = 0
        $normalizedNames = New-Object Collections.Generic.List[string]
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace("\", "/")
            if ([string]::IsNullOrWhiteSpace($name) -or $name.StartsWith("/") -or
                $name -match "^[A-Za-z]:" -or @($name.Split("/") | Where-Object { $_ -eq ".." }).Count -gt 0) {
                throw "Caminho inseguro no ZIP: '$name'."
            }
            if ($normalizedNames.Contains($name)) {
                throw "Entrada duplicada no ZIP: '$name'."
            }
            $null = $normalizedNames.Add($name)
            $expanded += $entry.Length
            if ($Spec.MaxExpandedBytes -gt 0 -and $expanded -gt $Spec.MaxExpandedBytes) {
                throw "Conteudo expandido do ZIP excede $($Spec.MaxExpandedBytes) bytes."
            }
        }

        if ($archive.Entries.Count -ne $Spec.ArchiveEntries.Count) {
            throw "ZIP deve conter exatamente $($Spec.ArchiveEntries.Count) arquivo(s), mas contem $($archive.Entries.Count)."
        }

        foreach ($innerSpec in $Spec.ArchiveEntries) {
            $wanted = $innerSpec.Name.Replace("\", "/")
            $matches = @($archive.Entries | Where-Object {
                $_.FullName.Replace("\", "/").Equals($wanted, [StringComparison]::OrdinalIgnoreCase)
            })
            if ($matches.Count -ne 1) {
                throw "Entrada obrigatoria ausente ou duplicada no ZIP: $wanted"
            }

            $tempFile = Join-Path ([IO.Path]::GetTempPath()) ("TurboramaZipAudit-" + [Guid]::NewGuid().ToString("N") + ".partial")
            try {
                $entryStream = $null
                $tempStream = $null
                try {
                    $entryStream = $matches[0].Open()
                    $tempStream = New-Object IO.FileStream(
                        $tempFile,
                        [IO.FileMode]::CreateNew,
                        [IO.FileAccess]::Write,
                        [IO.FileShare]::None
                    )
                    $entryStream.CopyTo($tempStream)
                    $tempStream.Flush($true)
                }
                finally {
                    if ($null -ne $tempStream) { $tempStream.Dispose() }
                    if ($null -ne $entryStream) { $entryStream.Dispose() }
                }
                $null = Assert-FlatFile -Path $tempFile -Spec $innerSpec
            }
            finally {
                if (Test-Path -LiteralPath $tempFile) {
                    Remove-Item -LiteralPath $tempFile -Force
                }
            }
        }
    }
    finally {
        if ($null -ne $archive) { $archive.Dispose() }
        else { $stream.Dispose() }
    }
}

function Assert-PackageFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Spec
    )

    $result = Assert-FlatFile -Path $Path -Spec $Spec
    if ($Spec.FileType -eq "Zip") {
        Assert-ZipContents -Path $Path -Spec $Spec
    }
    return $result
}

function Receive-FromApprovedSource {
    param(
        [Parameter(Mandatory = $true)][object]$Spec,
        [Parameter(Mandatory = $true)][string]$PartialPath
    )

    $errors = New-Object Collections.Generic.List[string]
    foreach ($urlText in $Spec.Urls) {
        try {
            Write-Host "    HTTPS: $urlText" -ForegroundColor DarkGray
            return Receive-HttpsFile -Uri ([Uri]$urlText) -AllowedHosts $Spec.AllowedHosts `
                -PartialPath $PartialPath -MaxBytes $Spec.MaxBytes
        }
        catch {
            $null = $errors.Add("$urlText -> $($_.Exception.Message)")
            if (Test-Path -LiteralPath $PartialPath) {
                Remove-Item -LiteralPath $PartialPath -Force -ErrorAction SilentlyContinue
            }
        }
    }

    throw "Nenhuma fonte aprovada funcionou para $($Spec.Name):`n$($errors -join "`n")"
}

function New-DeterministicSingleFileZip {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$EntryName,
        [Parameter(Mandatory = $true)][string]$PartialZipPath
    )

    $fileStream = $null
    $archive = $null
    try {
        $fileStream = New-Object IO.FileStream(
            $PartialZipPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None
        )
        $archive = New-Object IO.Compression.ZipArchive($fileStream, [IO.Compression.ZipArchiveMode]::Create, $true)
        $entry = $archive.CreateEntry($EntryName, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::Parse("2000-01-01T00:00:00Z")

        $sourceStream = $null
        $entryStream = $null
        try {
            $sourceStream = [IO.File]::Open($SourcePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            $entryStream = $entry.Open()
            $sourceStream.CopyTo($entryStream)
        }
        finally {
            if ($null -ne $entryStream) { $entryStream.Dispose() }
            if ($null -ne $sourceStream) { $sourceStream.Dispose() }
        }
        $archive.Dispose()
        $archive = $null
        $fileStream.Flush($true)
    }
    finally {
        if ($null -ne $archive) { $archive.Dispose() }
        if ($null -ne $fileStream) { $fileStream.Dispose() }
    }
}

function Install-DownloadedPackage {
    param(
        [Parameter(Mandatory = $true)][object]$Spec,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $partial = Join-Path $TargetDir ("." + $Spec.Name + "." + [Guid]::NewGuid().ToString("N") + ".partial")
    try {
        $resolvedUrl = Receive-FromApprovedSource -Spec $Spec -PartialPath $partial
        $result = Assert-PackageFile -Path $partial -Spec $Spec
        [IO.File]::Move($partial, $Destination)
        return [pscustomobject]@{
            Validation = $result
            ResolvedUrl = $resolvedUrl
        }
    }
    finally {
        if (Test-Path -LiteralPath $partial) {
            Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
        }
    }
}

function Install-WrappedPackage {
    param(
        [Parameter(Mandatory = $true)][object]$Spec,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $sourcePartial = Join-Path $TargetDir ("." + $Spec.Source.Name + "." + [Guid]::NewGuid().ToString("N") + ".partial")
    $zipPartial = Join-Path $TargetDir ("." + $Spec.Name + "." + [Guid]::NewGuid().ToString("N") + ".partial")
    try {
        $resolvedUrl = Receive-FromApprovedSource -Spec $Spec.Source -PartialPath $sourcePartial
        $null = Assert-FlatFile -Path $sourcePartial -Spec $Spec.Source
        New-DeterministicSingleFileZip -SourcePath $sourcePartial -EntryName $Spec.SourceEntryName -PartialZipPath $zipPartial
        $result = Assert-PackageFile -Path $zipPartial -Spec $Spec
        [IO.File]::Move($zipPartial, $Destination)
        return [pscustomobject]@{
            Validation = $result
            ResolvedUrl = $resolvedUrl
        }
    }
    finally {
        foreach ($partial in @($sourcePartial, $zipPartial)) {
            if (Test-Path -LiteralPath $partial) {
                Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

function Add-AuditResult {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Status,
        [string]$Sha256 = "",
        [long]$Bytes = 0,
        [string]$Detail = ""
    )

    $null = $script:results.Add([pscustomobject]@{
        Arquivo = $Name
        Status = $Status
        MB = if ($Bytes -gt 0) { [Math]::Round($Bytes / 1MB, 2) } else { $null }
        SHA256 = $Sha256
        Detalhe = $Detail
    })
}

function Apply-PayloadLock {
    $catalogSpecs = New-Object Collections.Generic.List[object]

    foreach ($entry in $PayloadLock.payloads) {
        $matches = @($packages | Where-Object { $_.Name.Equals($entry.name, [StringComparison]::OrdinalIgnoreCase) })
        if ($matches.Count -ne 1) {
            throw "Lockfile sem definicao unica no downloader: $($entry.name)"
        }

        $spec = $matches[0]
        if (-not $spec.FileType.Equals($entry.fileType, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Tipo divergente para $($entry.name): script=$($spec.FileType), lock=$($entry.fileType)."
        }
        if ([long]$entry.length -lt 4096 -or $entry.sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
            throw "Metadados de integridade invalidos no lockfile: $($entry.name)"
        }
        if ($null -eq $entry.sourceUrls -or @($entry.sourceUrls).Count -lt 1) {
            throw "Fonte HTTPS ausente no lockfile: $($entry.name)"
        }

        $spec.MinBytes = [long]$entry.length
        $spec.MaxBytes = [long]$entry.length
        $spec.Sha256 = $entry.sha256.ToUpperInvariant()
        $spec.Urls = @($entry.sourceUrls)
        $spec.AllowedHosts = @($MicrosoftHosts + $GitHubReleaseHosts | Select-Object -Unique)
        $spec.Mode = "Download"
        $spec.Required = $entry.installTier -eq "Required"
        $spec.Optional = -not $spec.Required
        $spec.Legacy = $entry.name -match '^vcredist20(05|08|10|12|13)_(x86|x64)\.zip$'
        $spec.SignerSubject = if ($null -ne $entry.PSObject.Properties["signerSubject"]) { [string]$entry.signerSubject } else { "" }
        $spec.SignerThumbprint = if ($null -ne $entry.PSObject.Properties["signerThumbprint"]) { [string]$entry.signerThumbprint } else { "" }
        $spec.CertificatePublicKeySha256 = if ($null -ne $entry.PSObject.Properties["certificatePublicKeySha256"]) { [string]$entry.certificatePublicKeySha256 } else { "" }
        $spec.ExpectedVersion = if ($null -ne $entry.PSObject.Properties["productVersion"] -and $spec.FileType -ne "Msi") { [string]$entry.productVersion } else { "" }
        $spec.RequireSignature = $spec.FileType -in @("Exe", "Msi")

        if ($spec.FileType -eq "Zip") {
            $innerSpecs = New-Object Collections.Generic.List[object]
            [long]$expandedBytes = 0
            foreach ($inner in @($entry.archiveEntries)) {
                $innerExtension = [IO.Path]::GetExtension([string]$inner.name)
                $innerType = if ($innerExtension -ieq ".msi") { "Msi" } elseif ($innerExtension -ieq ".dll") { "Dll" } else { "Exe" }
                $innerSpecs.Add((New-InnerFileSpec -Name ([string]$inner.name) -FileType $innerType `
                    -MinBytes ([long]$inner.length) -MaxBytes ([long]$inner.length) `
                    -Sha256 ([string]$inner.sha256) -RequireSignature `
                    -SignerSubject ([string]$inner.signerSubject) `
                    -SignerThumbprint ([string]$inner.signerThumbprint) `
                    -CertificatePublicKeySha256 ([string]$inner.certificatePublicKeySha256))) | Out-Null
                $expandedBytes += [long]$inner.length
            }
            if ($innerSpecs.Count -ne 1) {
                throw "ZIP catalogado deve conter exatamente uma entrada: $($entry.name)"
            }
            $spec.ArchiveEntries = $innerSpecs.ToArray()
            $spec.MaxExpandedBytes = $expandedBytes
        }

        foreach ($urlText in $spec.Urls) {
            Assert-TrustedHttpsUri -Uri ([Uri]$urlText) -AllowedHosts $spec.AllowedHosts
        }
        $catalogSpecs.Add($spec) | Out-Null
    }

    if ($catalogSpecs.Count -ne 20) {
        throw "Catalogo aplicado incompleto: $($catalogSpecs.Count)/20."
    }
    return $catalogSpecs.ToArray()
}

$packages = @(Apply-PayloadLock)

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " TURBORAMA - PRE-REQUISITOS SEGUROS E REPRODUZIVEIS" -ForegroundColor Cyan
Write-Host " Modo: $(if ($DryRun) { 'DRY-RUN' } elseif ($AuditOnly) { 'AUDITORIA' } else { 'DOWNLOAD/AUDITORIA' })$(if ($ForBuild) { ' / PERFIL BUILD 20/20' })" -ForegroundColor Cyan
Write-Host " Destino: $TargetDir" -ForegroundColor Cyan
Write-Host " Nenhum instalador sera executado." -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

if (-not $DryRun -and -not (Test-Path -LiteralPath $TargetDir)) {
    if ($AuditOnly) {
        throw "Diretorio de pre-requisitos inexistente: $TargetDir"
    }
    $null = New-Item -ItemType Directory -Path $TargetDir -Force
}

$results = New-Object Collections.Generic.List[object]
$failures = New-Object Collections.Generic.List[string]
$warnings = New-Object Collections.Generic.List[string]

foreach ($spec in $packages) {
    $destination = Join-Path $TargetDir $spec.Name
    $selected = $ForBuild -or $spec.Required -or ($spec.Optional -and $IncludeOptional)
    if ($spec.Legacy -and -not $ForBuild) {
        $selected = [bool]$IncludeLegacy
    }

    if ($DryRun) {
        if ($spec.Mode -eq "ExistingOnly") {
            $detail = if ($spec.Legacy) {
                "BLOQUEADO para download; somente copia existente com EXE Microsoft assinado/hash conhecido."
            }
            else {
                "Somente auditoria de copia existente; sem download automatico."
            }
            Add-AuditResult -Name $spec.Name -Status "NAO_BAIXA" -Detail $detail
        }
        elseif ($selected) {
            $sourceUrls = if ($spec.Mode -eq "Wrap") { $spec.Source.Urls } else { $spec.Urls }
            Add-AuditResult -Name $spec.Name -Status "PLANEJADO" -Detail ($sourceUrls -join " | ")
        }
        else {
            Add-AuditResult -Name $spec.Name -Status "OPCIONAL" -Detail $spec.Note
        }
        continue
    }

    if (Test-Path -LiteralPath $destination) {
        try {
            $validation = Assert-PackageFile -Path $destination -Spec $spec
            $status = if ($spec.Legacy) { "LEGADO_VALIDADO" } elseif ($selected) { "VALIDADO" } else { "OPCIONAL_VALIDADO" }
            Add-AuditResult -Name $spec.Name -Status $status -Sha256 $validation.Sha256 `
                -Bytes $validation.Bytes -Detail $spec.Note
            if ($spec.Legacy) {
                $null = $warnings.Add("$($spec.Name): legado fora de suporte; use somente se um jogo exigir essa versao.")
            }
        }
        catch {
            $message = "$($spec.Name): $($_.Exception.Message)"
            Add-AuditResult -Name $spec.Name -Status "INVALIDO" -Detail $message
            $null = $failures.Add($message)
        }
        continue
    }

    if ($spec.Mode -eq "ExistingOnly") {
        $status = if ($spec.Legacy) { "BLOQUEADO_AUSENTE" } else { "AUSENTE_OPCIONAL" }
        Add-AuditResult -Name $spec.Name -Status $status -Detail $spec.Note
        if ($selected) {
            $null = $warnings.Add("$($spec.Name): solicitado, mas nao possui fonte automatica aprovada.")
        }
        continue
    }

    if (-not $selected) {
        Add-AuditResult -Name $spec.Name -Status "AUSENTE_OPCIONAL" -Detail $spec.Note
        continue
    }

    if ($AuditOnly) {
        $message = "$($spec.Name): pacote obrigatorio ausente."
        Add-AuditResult -Name $spec.Name -Status "FALTA" -Detail $message
        $null = $failures.Add($message)
        continue
    }

    Write-Host "-> $($spec.Name)" -ForegroundColor Yellow
    try {
        $installed = if ($spec.Mode -eq "Wrap") {
            Install-WrappedPackage -Spec $spec -Destination $destination
        }
        else {
            Install-DownloadedPackage -Spec $spec -Destination $destination
        }
        Add-AuditResult -Name $spec.Name -Status "BAIXADO_VALIDADO" `
            -Sha256 $installed.Validation.Sha256 -Bytes $installed.Validation.Bytes `
            -Detail $installed.ResolvedUrl
        Write-Host "   OK: download validado e publicado por rename atomico." -ForegroundColor Green
    }
    catch {
        $message = "$($spec.Name): $($_.Exception.Message)"
        Add-AuditResult -Name $spec.Name -Status "FALHOU" -Detail $message
        $null = $failures.Add($message)
        Write-Host "   FALHA: $message" -ForegroundColor Red
    }
}

if (-not $DryRun -and (Test-Path -LiteralPath $TargetDir)) {
    foreach ($blockedName in $blockedFiles.Keys) {
        $blockedPath = Join-Path $TargetDir $blockedName
        if (Test-Path -LiteralPath $blockedPath) {
            $message = "$blockedName esta no diretorio incorporado pelo build. $($blockedFiles[$blockedName])"
            Add-AuditResult -Name $blockedName -Status "BLOQUEADO" -Detail $message
            $null = $failures.Add($message)
        }
    }

    $knownNames = @($packages | ForEach-Object { $_.Name }) + @($blockedFiles.Keys)
    foreach ($file in Get-ChildItem -LiteralPath $TargetDir -File) {
        if ($knownNames -notcontains $file.Name) {
            $message = if ($file.Extension -eq ".partial") {
                "$($file.Name): download parcial nao pode ser incorporado ao executavel."
            }
            else {
                "$($file.Name): arquivo sem regra de origem/integridade; o csproj incorporaria este arquivo por wildcard."
            }
            Add-AuditResult -Name $file.Name -Status "NAO_CATALOGADO" -Bytes $file.Length -Detail $message
            $null = $failures.Add($message)
        }
    }
}

Write-Host ""
Write-Host "AUDITORIA FINAL" -ForegroundColor Cyan
$results | Sort-Object Arquivo | Format-Table Arquivo, Status, MB, SHA256 -AutoSize

if ($warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "Avisos:" -ForegroundColor DarkYellow
    foreach ($warning in $warnings) {
        Write-Host "  - $warning" -ForegroundColor DarkYellow
    }
}

Write-Host ""
Write-Host ".NET Framework 3.5:" -ForegroundColor Cyan
Write-Host "  Use o recurso do Windows (DISM/Optional Features) e a midia da mesma versao do Windows quando offline." -ForegroundColor Gray
Write-Host "  O pacote comunitario dotNetFx35_WX_10_x86_x64.exe foi deliberadamente bloqueado." -ForegroundColor Gray

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Falhas de seguranca/completude:" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }
    throw "Auditoria reprovada com $($failures.Count) falha(s). Nenhum instalador foi executado."
}

Write-Host ""
if ($DryRun) {
    Write-Host "Dry-run concluido: manifesto e plano validados; rede e disco nao foram alterados." -ForegroundColor Green
}
elseif ($AuditOnly) {
    Write-Host "Auditoria concluida: todos os pacotes obrigatorios presentes passaram nas validacoes." -ForegroundColor Green
}
else {
    Write-Host "Pacote seguro pronto para compilacao. Nenhum instalador foi executado." -ForegroundColor Green
}
Write-Host ""
