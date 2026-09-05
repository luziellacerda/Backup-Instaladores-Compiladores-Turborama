#Requires -Version 5.1
[CmdletBinding()]
param()

# ReflectionOnlyLoadFrom e necessario para inspecionar os recursos do executavel
# .NET Framework sem executa-lo. No PowerShell 7 essa API nao existe, portanto
# encaminhamos a suite ao Windows PowerShell 5.1 nativo.
if ($PSVersionTable.PSEdition -ne "Desktop") {
    $WindowsPowerShellPath = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
    if (-not (Test-Path -LiteralPath $WindowsPowerShellPath -PathType Leaf)) {
        throw "Windows PowerShell 5.1 nao encontrado; ele e obrigatorio para a inspecao read-only da assembly .NET Framework."
    }

    & $WindowsPowerShellPath -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
    exit $LASTEXITCODE
}

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$InstallerRoot = $PSScriptRoot
$ProjectDirectory = Join-Path $InstallerRoot "InstallerHost"
$ProjectPath = Join-Path $ProjectDirectory "InstallerHost.csproj"
$LockFilePath = Join-Path $ProjectDirectory "prerequisites.lock.json"
$PipelinePath = Join-Path $InstallerRoot "Compilar_InstallerHost_Moderno.ps1"
$ReleaseDirectory = Join-Path $ProjectDirectory "bin\Release"
$ExePath = Join-Path $ReleaseDirectory "InstallerHost.exe"
$ManifestPath = Join-Path $ReleaseDirectory "InstallerHost-build-manifest.json"
$ExeHashPath = Join-Path $ReleaseDirectory "InstallerHost.exe.sha256"
$ManifestHashPath = Join-Path $ReleaseDirectory "InstallerHost-build-manifest.json.sha256"
$ProductPackageSecurityPath = Join-Path $ProjectDirectory "ProductPackageSecurity.cs"
$SecureProductExtractionPath = Join-Path $ProjectDirectory "SecureProductExtraction.cs"
$ProductPackageSecurityTestsPath = Join-Path $ProjectDirectory "SecurityTests\ProductPackageSecurityTests.cs"
$script:Passed = 0

function Add-Pass {
    param([Parameter(Mandatory = $true)][string]$Text)

    $script:Passed++
    Write-Host ("[PASS] {0}" -f $Text) -ForegroundColor Green
}

function Assert-Test {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
    Add-Pass -Text $Message
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
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

function Get-ExpectedHashFromFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedFileName
    )

    $content = (Get-Content -LiteralPath $Path -Raw).Trim()
    $pattern = "^(?<hash>[A-F0-9]{64}) \*" + [System.Text.RegularExpressions.Regex]::Escape($ExpectedFileName) + "$"
    if ($content -cnotmatch $pattern) {
        throw ("Formato SHA256 invalido em {0}" -f $Path)
    }
    return $Matches["hash"]
}

function Get-CertificatePublicKeySha256 {
    param([Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($algorithm.ComputeHash($Certificate.GetPublicKey())) -replace "-", "").ToUpperInvariant()
    }
    finally {
        $algorithm.Dispose()
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

try {
    Write-Host ""
    Write-Host "TURBORAMA - TESTES READ-ONLY DO INSTALLERHOST" -ForegroundColor Cyan
    Write-Host "Nenhum instalador e nenhum InstallerHost sera executado." -ForegroundColor DarkGray

    foreach ($requiredPath in @(
        $PipelinePath,
        $ProjectPath,
        $LockFilePath,
        $ExePath,
        $ManifestPath,
        $ExeHashPath,
        $ManifestHashPath,
        $ProductPackageSecurityPath,
        $SecureProductExtractionPath,
        $ProductPackageSecurityTestsPath
    )) {
        Assert-Test -Condition (Test-Path -LiteralPath $requiredPath -PathType Leaf) -Message ("arquivo existe: {0}" -f $requiredPath)
    }

    Write-Host ""
    Write-Host "-- Pacote principal/sidecar/reparse (harness isolado) --" -ForegroundColor Cyan
    $visualStudioRoot = Join-Path ${env:ProgramFiles} "Microsoft Visual Studio\2022"
    $cscPath = Get-ChildItem -LiteralPath $visualStudioRoot -Filter "csc.exe" -Recurse -File -ErrorAction Stop |
        Where-Object { $_.FullName -match "\\MSBuild\\Current\\Bin\\Roslyn\\csc\.exe$" } |
        Select-Object -First 1 -ExpandProperty FullName
    Assert-Test -Condition (-not [string]::IsNullOrWhiteSpace($cscPath)) -Message "compilador C# do Visual Studio 2022 localizado"

    $securityTestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("TurboRama-ProductSecurity-" + [guid]::NewGuid().ToString("N"))
    [System.IO.Directory]::CreateDirectory($securityTestRoot) | Out-Null
    $securityTestExe = Join-Path $securityTestRoot "ProductPackageSecurityTests.exe"
    try {
        $compilerOutput = @(& $cscPath /nologo /target:exe /define:PRODUCT_PACKAGE_SECURITY_TESTS `
            /langversion:7.3 /warn:4 /warnaserror+ "/out:$securityTestExe" `
            /reference:System.dll /reference:System.Core.dll `
            /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
            $ProductPackageSecurityPath $SecureProductExtractionPath $ProductPackageSecurityTestsPath 2>&1)
        $compilerExitCode = $LASTEXITCODE
        foreach ($line in $compilerOutput) {
            Write-Host $line
        }
        Assert-Test -Condition ($compilerExitCode -eq 0) -Message "harness de seguranca compila sem avisos"

        $securityOutput = @(& $securityTestExe $securityTestRoot 2>&1)
        $securityExitCode = $LASTEXITCODE
        foreach ($line in $securityOutput) {
            Write-Host $line
        }
        Assert-Test -Condition ($securityExitCode -eq 0) -Message "22 testes do sidecar, multipart, TOCTOU, reparse e ZIP foram aprovados"
    }
    finally {
        if ($securityTestRoot.StartsWith([System.IO.Path]::GetTempPath(), [System.StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $securityTestRoot)) {
            [System.IO.Directory]::Delete($securityTestRoot, $true)
        }
    }

    Write-Host ""
    Write-Host "-- Lockfile/payloads/certificados/ZIPs (pipeline em DryRun) --" -ForegroundColor Cyan
    $powershellCommand = Get-Command "powershell.exe" -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $pipelineOutput = @(& $powershellCommand.Source -NoProfile -ExecutionPolicy Bypass -File $PipelinePath -DryRun -SkipDownload -AllowDirty 2>&1)
    $pipelineExitCode = $LASTEXITCODE
    foreach ($line in $pipelineOutput) {
        Write-Host $line
    }
    Assert-Test -Condition ($pipelineExitCode -eq 0) -Message "DryRun criptografico do pipeline terminou com codigo 0"
    $directXSfxEvidence = @($pipelineOutput | Where-Object {
        $_.ToString().IndexOf(
            "directx_Jun2010_redist.exe: RCDATA/CABINET validou DXSETUP.exe sem executar o SFX",
            [System.StringComparison]::Ordinal) -ge 0
    })
    Assert-Test -Condition ($directXSfxEvidence.Count -eq 1) -Message "DryRun validou a ancora DXSETUP.exe sem executar o SFX DirectX"

    $lockHash = Get-Sha256 -Path $LockFilePath
    $lock = Get-Content -LiteralPath $LockFilePath -Raw | ConvertFrom-Json
    $payloads = @($lock.payloads | ForEach-Object { $_ })
    Assert-Test -Condition ([int]$lock.schemaVersion -eq 1) -Message "lockfile schemaVersion 1"
    Assert-Test -Condition ($payloads.Count -eq 20) -Message "lockfile contem exatamente 20 payloads"
    $directXPayloads = @($payloads | Where-Object { [string]$_.name -ceq "directx_Jun2010_redist.exe" })
    Assert-Test -Condition ($directXPayloads.Count -eq 1) -Message "lockfile contem exatamente um payload DirectX Jun 2010"
    $directXEntries = @($directXPayloads[0].archiveEntries | ForEach-Object { $_ })
    Assert-Test -Condition ($directXEntries.Count -eq 1 -and [string]$directXEntries[0].name -ceq "DXSETUP.exe") `
        -Message "lockfile ancora o DXSETUP.exe interno do DirectX SFX"

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    Assert-Test -Condition ([int]$manifest.SchemaVersion -eq 2) -Message "manifesto de build schemaVersion 2"
    Assert-Test -Condition (([string]$manifest.Lockfile.SHA256).Equals($lockHash, [System.StringComparison]::Ordinal)) -Message "manifesto registra o SHA256 atual do lockfile"
    Assert-Test -Condition ([int]$manifest.Lockfile.PayloadCount -eq $payloads.Count) -Message "manifesto registra a contagem exata do lockfile"
    Assert-Test -Condition ([int]$manifest.EmbeddedInputs.Count -eq $payloads.Count) -Message "manifesto registra todos os inputs incorporados"
    $manifestDirectXPayloads = @($manifest.EmbeddedInputs.Payloads | Where-Object {
        [string]$_.Name -ceq "directx_Jun2010_redist.exe"
    })
    Assert-Test -Condition ($manifestDirectXPayloads.Count -eq 1) -Message "manifesto registra o payload DirectX"
    $manifestDirectXEntries = @($manifestDirectXPayloads[0].ArchiveEntries | ForEach-Object { $_ })
    Assert-Test -Condition ($manifestDirectXEntries.Count -eq 1 -and [string]$manifestDirectXEntries[0].Name -ceq "DXSETUP.exe") `
        -Message "manifesto registra a ancora DXSETUP.exe extraida do SFX"
    $manifestDXSetup = $manifestDirectXEntries[0]
    $lockDXSetup = $directXEntries[0]
    Assert-Test -Condition (
        [long]$manifestDXSetup.Length -eq [long]$lockDXSetup.length -and
        [string]$manifestDXSetup.SHA256 -ceq [string]$lockDXSetup.sha256 -and
        [string]$manifestDXSetup.Container -ceq "PE.RT_RCDATA/CABINET") `
        -Message "manifesto fixa tamanho, SHA256 e container PE do DXSETUP.exe"
    Assert-Test -Condition (
        [string]$manifestDXSetup.Authenticode.Status -ceq "Valid" -and
        [string]$manifestDXSetup.Authenticode.Subject -ceq [string]$lockDXSetup.signerSubject -and
        [string]$manifestDXSetup.Authenticode.Thumbprint -ceq [string]$lockDXSetup.signerThumbprint -and
        [string]$manifestDXSetup.Authenticode.CertificatePublicKeySha256 -ceq [string]$lockDXSetup.certificatePublicKeySha256) `
        -Message "manifesto fixa a assinatura Authenticode do DXSETUP.exe"

    $exeHash = Get-Sha256 -Path $ExePath
    $hashFromFile = Get-ExpectedHashFromFile -Path $ExeHashPath -ExpectedFileName "InstallerHost.exe"
    Assert-Test -Condition ($exeHash -eq $hashFromFile) -Message "InstallerHost.exe coincide com InstallerHost.exe.sha256"
    Assert-Test -Condition ($exeHash -eq ([string]$manifest.InstallerHost.SHA256)) -Message "InstallerHost.exe coincide com o manifesto"
    Assert-Test -Condition ((Get-Item -LiteralPath $ExePath).Length -eq [long]$manifest.InstallerHost.Length) -Message "tamanho do InstallerHost.exe coincide com o manifesto"

    $actualManifestHash = Get-Sha256 -Path $ManifestPath
    $manifestHashFromFile = Get-ExpectedHashFromFile -Path $ManifestHashPath -ExpectedFileName "InstallerHost-build-manifest.json"
    Assert-Test -Condition ($actualManifestHash -eq $manifestHashFromFile) -Message "manifesto coincide com seu arquivo SHA256"

    $header = New-Object byte[] 2
    $exeStream = [System.IO.File]::Open($ExePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        $read = $exeStream.Read($header, 0, $header.Length)
    }
    finally {
        $exeStream.Dispose()
    }
    Assert-Test -Condition ($read -eq 2 -and $header[0] -eq 0x4D -and $header[1] -eq 0x5A) -Message "InstallerHost.exe possui cabecalho PE/MZ"

    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($ExePath)
    Assert-Test -Condition ($assemblyName.Name -eq "InstallerHost") -Message "InstallerHost.exe e assembly .NET gerenciado esperado"
    $assembly = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom((Resolve-Path -LiteralPath $ExePath).Path)
    $targetFrameworkAttribute = $assembly.GetCustomAttributesData() | Where-Object {
        $_.AttributeType.FullName -eq "System.Runtime.Versioning.TargetFrameworkAttribute"
    } | Select-Object -First 1
    Assert-Test -Condition ($null -ne $targetFrameworkAttribute) -Message "assembly declara TargetFrameworkAttribute"
    $targetFramework = [string]$targetFrameworkAttribute.ConstructorArguments[0].Value
    Assert-Test -Condition ($targetFramework -eq ".NETFramework,Version=v4.7.2") -Message "assembly foi compilado para .NET Framework 4.7.2"

    $allResourceNames = @($assembly.GetManifestResourceNames())
    $resourcePrefix = "InstallerHost.resources.prerequisites."
    $payloadResourceNames = @($allResourceNames | Where-Object { $_.StartsWith($resourcePrefix, [System.StringComparison]::Ordinal) })
    $expectedResourceNames = @($payloads | ForEach-Object { $resourcePrefix + [string]$_.name })
    $resourceDifferences = @(Compare-Object -ReferenceObject $expectedResourceNames -DifferenceObject $payloadResourceNames -CaseSensitive)
    Assert-Test -Condition ($resourceDifferences.Count -eq 0 -and $payloadResourceNames.Count -eq 20) -Message "recursos incorporados correspondem exatamente aos 20 nomes do lockfile"

    foreach ($payload in $payloads) {
        $resourceName = $resourcePrefix + [string]$payload.name
        $resourceStream = $assembly.GetManifestResourceStream($resourceName)
        if ($null -eq $resourceStream) {
            throw ("Recurso incorporado nao abriu: {0}" -f $resourceName)
        }
        try {
            $resourceLength = $resourceStream.Length
            $resourceHash = Get-StreamSha256 -Stream $resourceStream
        }
        finally {
            $resourceStream.Dispose()
        }
        if ($resourceLength -ne [long]$payload.length -or $resourceHash -ne [string]$payload.sha256) {
            throw ("Recurso incorporado diverge do lockfile: {0}" -f $payload.name)
        }
    }
    Add-Pass -Text "bytes/tamanhos/hashes dos 20 recursos incorporados coincidem com o lockfile"

    $forbiddenPattern = "(?i)(sharpzip|wget\.exe|\.partial|dotnetfx35|netfx3|openal|dokansetup|winfsp)"
    $forbiddenResources = @($allResourceNames | Where-Object { $_ -match $forbiddenPattern })
    Assert-Test -Condition ($forbiddenResources.Count -eq 0) -Message "assembly nao contem recursos proibidos ou residuais"
    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath -Raw
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($projectXml.NameTable)
    $namespaceManager.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
    $embeddedIncludes = @($projectXml.SelectNodes("//msb:EmbeddedResource[@Include]", $namespaceManager) | ForEach-Object { $_.GetAttribute("Include") })
    $forbiddenIncludes = @($embeddedIncludes | Where-Object { $_ -match $forbiddenPattern })
    Assert-Test -Condition ($forbiddenIncludes.Count -eq 0) -Message "csproj nao incorpora dependencias removidas/proibidas"
    $wildcardEmbedded = @($embeddedIncludes | Where-Object { $_.IndexOfAny([char[]]@("*", "?", "[", "]")) -ge 0 })
    Assert-Test -Condition ($wildcardEmbedded.Count -eq 0) -Message "csproj nao usa wildcard em EmbeddedResource"
    $contentIncludes = @($projectXml.SelectNodes("//msb:Content[@Include]", $namespaceManager) | ForEach-Object { $_.GetAttribute("Include") })
    $wildcardContent = @($contentIncludes | Where-Object { $_.IndexOfAny([char[]]@("*", "?", "[", "]")) -ge 0 })
    Assert-Test -Condition ($wildcardContent.Count -eq 0) -Message "csproj nao usa wildcard em arquivos copiados para Release"
    $footerBannerIncludes = @($contentIncludes | Where-Object { $_ -like "resources\lz_footer_banner*" })
    Assert-Test -Condition ($footerBannerIncludes.Count -eq 1 -and $footerBannerIncludes[0] -ceq "resources\lz_footer_banner.jpg") `
        -Message "csproj declara somente o banner Release JPG explicito"

    $signature = Get-AuthenticodeSignature -LiteralPath $ExePath
    Assert-Test -Condition ($signature.Status.ToString() -eq [string]$manifest.InstallerHost.Authenticode.Status) -Message "status Authenticode do EXE coincide com o manifesto"
    if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
        $certificate = $signature.SignerCertificate
        Assert-Test -Condition ($certificate.Subject -eq [string]$manifest.InstallerHost.Authenticode.Subject) -Message "subject do certificado do EXE coincide com o manifesto"
        Assert-Test -Condition ($certificate.Thumbprint -eq [string]$manifest.InstallerHost.Authenticode.Thumbprint) -Message "thumbprint do certificado do EXE coincide com o manifesto"
        Assert-Test -Condition ((Get-CertificatePublicKeySha256 -Certificate $certificate) -eq [string]$manifest.InstallerHost.Authenticode.CertificatePublicKeySha256) -Message "chave publica do certificado do EXE coincide com o manifesto"
    }
    else {
        Assert-Test -Condition ($signature.Status -eq [System.Management.Automation.SignatureStatus]::NotSigned) -Message "EXE local sem certificado esta explicitamente NotSigned"
        Assert-Test -Condition (-not [bool]$manifest.Build.Publishable) -Message "EXE NotSigned esta marcado Publishable=false"
        Assert-Test -Condition ([string]$manifest.Build.ReleaseChannel -eq "Prerelease") -Message "EXE NotSigned esta limitado ao canal Prerelease"
        Assert-Test -Condition (([string]$manifest.Build.EffectiveReleaseTag).EndsWith("-prerelease", [System.StringComparison]::Ordinal)) -Message "tag efetiva do EXE NotSigned possui sufixo -prerelease"
    }

    $manifestOutputs = @($manifest.Outputs | ForEach-Object { $_ })
    foreach ($output in $manifestOutputs) {
        $outputPath = Join-Path $ReleaseDirectory ([string]$output.Path)
        Assert-Test -Condition (Test-Path -LiteralPath $outputPath -PathType Leaf) -Message ("output allowlisted existe: {0}" -f $output.Path)
        Assert-Test -Condition ((Get-Item -LiteralPath $outputPath).Length -eq [long]$output.Length) -Message ("tamanho allowlisted confere: {0}" -f $output.Path)
        Assert-Test -Condition ((Get-Sha256 -Path $outputPath) -eq [string]$output.SHA256) -Message ("hash allowlisted confere: {0}" -f $output.Path)
    }

    $expectedReleaseFiles = @($manifestOutputs | ForEach-Object { [string]$_.Path }) + @(
        "InstallerHost-build-manifest.json",
        "InstallerHost-build-manifest.json.sha256",
        "InstallerHost.exe.sha256"
    )
    $actualReleaseFiles = @(Get-ChildItem -LiteralPath $ReleaseDirectory -Recurse -File | ForEach-Object {
        Get-RelativePathText -BasePath $ReleaseDirectory -FullPath $_.FullName
    })
    $releaseDifferences = @(Compare-Object -ReferenceObject $expectedReleaseFiles -DifferenceObject $actualReleaseFiles -CaseSensitive)
    Assert-Test -Condition ($releaseDifferences.Count -eq 0) -Message "Release nao possui arquivos fora da allowlist/metadata"

    Assert-Test -Condition ([int]$manifest.Build.ErrorCount -eq 0) -Message "manifesto registra zero erros de build"
    if ([bool]$manifest.Git.AllowDirty) {
        Assert-Test -Condition ([bool]$manifest.Build.NonPublishable) -Message "build com -AllowDirty esta marcado NonPublishable=true"
    }

    Write-Host ""
    Write-Host ("TESTES CONCLUIDOS: {0} verificacoes aprovadas." -f $script:Passed) -ForegroundColor Green
    Write-Host "Nenhum InstallerHost e nenhum payload/instalador foi iniciado." -ForegroundColor DarkGray
    exit 0
}
catch {
    Write-Host ""
    Write-Host ("TESTE REPROVADO: {0}" -f $_.Exception.Message) -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($_.ScriptStackTrace)) {
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    }
    exit 1
}
