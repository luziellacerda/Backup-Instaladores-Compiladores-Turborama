#Requires -Version 5.1
<#
.SYNOPSIS
Builds the internal TurboRama PIX Commercial v25 validation candidate.

.DESCRIPTION
The candidate is not cleared for sale until the real kiosk validation passes.
The embedded theme is always generated inside the clean CMake build by the
local PowerShell/.NET packer; Python is not required. This commercial entry
    point uses the server-authoritative license/payment profile, native
    hardening, a closed SHA-256 manifest and the isolated installer smoke test.
    Authenticode is optional and is never used to authorize the product.

.PARAMETER CertificadoThumbprint
SHA-1 thumbprint of a code-signing certificate with private key in the Windows
"My" certificate store. It can also be supplied through
TURBORAMA_SIGN_CERT_THUMBPRINT.

.PARAMETER ServidorCarimboDoTempo
RFC 3161 timestamp URL. No server is assumed automatically; provide the URL
approved by the certificate issuer or TURBORAMA_SIGN_TIMESTAMP_URL.

.PARAMETER CertificadoEmissorLicencaThumbprint
SHA-1 thumbprint of the separate certificate whose private key is authorized
to issue offline kiosk licenses. It must differ from the Authenticode key and
should be non-exportable in a token or HSM. It can also be supplied through
TURBORAMA_LICENSE_CERT_THUMBPRINT.

.PARAMETER ExigirAssinatura
Fails before packaging when no usable signing certificate was provided.

.PARAMETER ProtecaoComercial
Enables the fail-closed commercial build profile. This profile requires a
usable code-signing certificate, enables the native release mitigations and
refuses to package debug symbols, source files or test material. It also
embeds only the public certificate used to verify offline TPM-bound licenses.

.PARAMETER DiretorioTemporarioBuild
Absolute directory used exclusively as the boundary for build, package and
isolated smoke-test files. It can also be supplied through
TURBORAMA_BUILD_TEMP_ROOT. When omitted, the current TEMP directory is used.
#>
param(
    [switch]$Limpar,
    [switch]$TestarInstalador,
    [switch]$SemPausa,
    [string]$CertificadoThumbprint = $env:TURBORAMA_SIGN_CERT_THUMBPRINT,
    [string]$CertificadoEmissorLicencaThumbprint = $env:TURBORAMA_LICENSE_CERT_THUMBPRINT,
    [string]$ServidorCarimboDoTempo = $env:TURBORAMA_SIGN_TIMESTAMP_URL,
    [ValidateSet('CurrentUser','LocalMachine')]
    [string]$LocalCertificado = 'CurrentUser',
    [ValidateSet('CurrentUser','LocalMachine')]
    [string]$LocalCertificadoEmissorLicenca = 'CurrentUser',
    [switch]$ExigirAssinatura,
    [switch]$ProtecaoComercial,
    [switch]$ServidorAutoritativo,
    [string]$DiretorioTemporarioBuild = $env:TURBORAMA_BUILD_TEMP_ROOT
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

# Este script publica um artefato chamado COMERCIAL/FINAL. Ele nunca pode
# degradar silenciosamente para um build de desenvolvimento com o mesmo nome.
if (-not $ProtecaoComercial) {
    throw 'Este compilador publica somente o perfil comercial protegido. Use -ProtecaoComercial.'
}
if (-not $TestarInstalador) {
    throw 'O perfil comercial exige -TestarInstalador antes de qualquer compilacao.'
}
if (-not $ServidorAutoritativo) {
    throw 'Este compilador exige -ServidorAutoritativo. A licenca local Windows foi aposentada.'
}
if ($ExigirAssinatura -and [string]::IsNullOrWhiteSpace($ServidorCarimboDoTempo)) {
    throw '-ExigirAssinatura exige -ServidorCarimboDoTempo com URL RFC 3161 aprovada.'
}

$RepoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$WorkspaceRoot = Split-Path (Split-Path $RepoRoot -Parent) -Parent
$ProjectRoot = Join-Path $RepoRoot 'TurboramaEmulationStation'
# The .NET runtime contained in the payload has legitimate deep paths. Keep
# all disposable build roots short and explicit so clean builds do not depend
# on legacy MAX_PATH behavior of the current workspace location. Never force
# LOCALAPPDATA: the operator may deliberately place every disposable byte on a
# drive with enough free space (for example H:\TurboRamaTemp).
if ([string]::IsNullOrWhiteSpace($DiretorioTemporarioBuild)) {
    $DiretorioTemporarioBuild = [IO.Path]::GetTempPath()
}
if (-not [IO.Path]::IsPathRooted($DiretorioTemporarioBuild)) {
    throw 'DiretorioTemporarioBuild deve ser um caminho absoluto.'
}
$BuildTempBoundary = [IO.Path]::GetFullPath($DiretorioTemporarioBuild).TrimEnd('\')
$buildTempDriveRoot = [IO.Path]::GetPathRoot($BuildTempBoundary).TrimEnd('\')
if ([string]::IsNullOrWhiteSpace($BuildTempBoundary) -or
    [string]::Equals($BuildTempBoundary, $buildTempDriveRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'DiretorioTemporarioBuild nao pode ser a raiz de uma unidade.'
}
[IO.Directory]::CreateDirectory($BuildTempBoundary) | Out-Null
$BuildTempRoot = Join-Path $BuildTempBoundary 'TurboRama-v25-build'
$WorkRoot = Join-Path $BuildTempRoot 'pack'
$EsBuild = Join-Path $BuildTempRoot 'es'
$EsOutput = Join-Path $BuildTempRoot 'es-output'
$BuildLockFile = Join-Path $BuildTempRoot 'build-v25.lock'
$script:BuildLockStream = $null
$script:InstallerCppSourcePin = $null
$script:BuildScriptSourcePin = $null
$script:NormalizedSigningThumbprint = ''
$script:LicenseIssuerCertificateBase64 = ''
$script:AgentBundleSha256 = ''
# Keep the installer smoke test on a deliberately short, explicit path. The
# payload carries the .NET runtime, whose valid nested files can exceed the
# legacy MAX_PATH limit when the test is rooted under the long workspace path.
$SmokeRoot = Join-Path $BuildTempBoundary 'TurboRama-v25-smoke'
$AgentOutput = Join-Path $WorkRoot 'agent-output'
$NativeOutput = Join-Path $WorkRoot 'native-output'
$ArchiveRoot = Join-Path $WorkRoot 'archive-update'
$BundleRoot = Join-Path $WorkRoot 'bundle'
# The canonical delivery directory is deliberately not used as a staging area.
# The candidate's tail mirrors the delivery path because the isolated installer
# smoke test validates that exact package suffix.
$CanonicalOutputRoot = Join-Path $ProjectRoot 'PIX-COMERCIAL\GERADO-v25'
$CandidateContainerRoot = Join-Path $WorkRoot 'PIX-COMERCIAL'
$CandidateOutputRoot = Join-Path $CandidateContainerRoot 'GERADO-v25'
$ReleaseHistoryRoot = Join-Path $WorkspaceRoot 'release-backups'
$OutputRoot = $CandidateOutputRoot
$AgentProject = Join-Path $ProjectRoot 'tools\TurboRamaPixAgent\TurboRamaPixAgent.csproj'
$AgentSettingsTemplate = Join-Path (Split-Path -Parent $AgentProject) 'appsettings.example.json'
$LicenseIssuerProject = Join-Path $ProjectRoot 'tools\TurboRamaPixLicenseIssuer\TurboRamaPixLicenseIssuer.csproj'
$InstallerSource = Join-Path $ProjectRoot 'tools\TurboRamaCommercialInstaller'
$PackScript = Join-Path $InstallerSource 'Build-TurboRamaPackage.ps1'
$ThemePacker = Join-Path $ProjectRoot 'tools\Pack-EmbeddedTheme.ps1'
$SevenZipVendorRoot = Join-Path $InstallerSource 'vendor'
$SevenZipLicense = Join-Path $SevenZipVendorRoot 'LICENSE-7ZIP-24.09.txt'
$SevenZipCopying = Join-Path $SevenZipVendorRoot 'COPYING-LGPL-2.1.txt'
$SevenZipNotice = Join-Path $SevenZipVendorRoot 'NOTICE-7ZIP-24.09.txt'
$FinalInstaller = Join-Path $OutputRoot 'INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe'
$LogFile = Join-Path $OutputRoot 'COMPILACAO-v25.log'
$ReleaseArtifactsSealed = $false
$InstallerFileName = Split-Path -Leaf $FinalInstaller
$ChecksumFileName = 'CHECKSUMS-SHA256.txt'
$RetiredRepairFileName = 'REPARAR-INSTALACAO-' + 'TURBORAMA.ps1'
$ReleaseArtifacts = @(
    $InstallerFileName,
    'CONFIGURAR-USER-TOKEN-PIX.exe',
    'CONFIGURAR-ACCESS-TOKEN-PIX.exe',
    '7za.exe',
    'LICENSE-7ZIP-24.09.txt',
    'COPYING-LGPL-2.1.txt',
    'NOTICE-7ZIP-24.09.txt',
    'ASSINATURA-AUTHENTICODE.txt',
    'COMO-CONFIGURAR-O-PIX.txt',
    'RELATORIO-COMPILACAO-v25.txt',
    'COMPILACAO-v25.log'
)

function Stage([string]$Text) {
    Write-Host "`n====================================================================" -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host '====================================================================' -ForegroundColor Cyan
    Add-Content -LiteralPath $LogFile -Value "`r`n=== $Text ===" -Encoding UTF8
}

function Enter-BuildLock {
    $tempBoundary = Assert-RegularDirectoryPath $BuildTempBoundary 'Fronteira temporaria para lock do build'
    Assert-AnchoredRegularDirectoryPath $BuildTempRoot $tempBoundary 'Raiz do lock do build' -AllowMissingTail | Out-Null
    [IO.Directory]::CreateDirectory($BuildTempRoot) | Out-Null
    Assert-AnchoredRegularDirectoryPath $BuildTempRoot $tempBoundary 'Raiz criada do lock do build' | Out-Null
    $existingLock = (Get-PathEntryState $BuildLockFile).Exists
    if ($existingLock) {
        # Um lock de execucao anterior pode permanecer. Abra-o somente para
        # leitura e com compartilhamento zero: assim ate um hardlink inesperado
        # jamais e truncado ou reescrito.
        Assert-RegularFilePath $BuildLockFile 'Lock existente do build' | Out-Null
    }
    try {
        $script:BuildLockStream = [IO.File]::Open(
            $BuildLockFile,
            $(if ($existingLock) { [IO.FileMode]::Open } else { [IO.FileMode]::CreateNew }),
            $(if ($existingLock) { [IO.FileAccess]::Read } else { [IO.FileAccess]::ReadWrite }),
            [IO.FileShare]::None)
    }
    catch [IO.IOException] {
        throw 'Ja existe outra compilacao TurboRama v25 usando as pastas temporarias. Aguarde seu termino e tente novamente.'
    }
    if (-not $existingLock) {
        $lockText = [Text.Encoding]::UTF8.GetBytes("PID=$PID`r`nInicio=$(Get-Date -Format o)`r`nRepositorio=$RepoRoot`r`n")
        $script:BuildLockStream.Write($lockText, 0, $lockText.Length)
        $script:BuildLockStream.Flush($true)
    }
}

function Exit-BuildLock {
    if ($null -eq $script:BuildLockStream) { return }
    $script:BuildLockStream.Dispose()
    $script:BuildLockStream = $null
}

function Exit-BuildSourcePins {
    foreach ($name in @('InstallerCppSourcePin','BuildScriptSourcePin')) {
        $lease = Get-Variable -Scope Script -Name $name -ValueOnly
        if ($null -ne $lease) { $lease.Dispose() }
        Set-Variable -Scope Script -Name $name -Value $null
    }
}

function Require-File([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label nao encontrado: $Path" }
}

function Require-Directory([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "$Label nao encontrado: $Path" }
}

function Get-GeneratedPathBoundary([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    # Cleanup is deliberately restricted to this closed list of exact build
    # directories. A lexical descendant of an allowed root is not sufficient.
    $allowedTargets = @(
        $WorkRoot,
        $EsBuild,
        $EsOutput,
        $AgentOutput,
        $NativeOutput,
        $ArchiveRoot,
        $BundleRoot,
        $CandidateContainerRoot,
        $CandidateOutputRoot,
        (Join-Path $WorkRoot 'agent-self-test')
    )
    foreach ($target in $allowedTargets) {
        $targetFull = [IO.Path]::GetFullPath($target).TrimEnd('\')
        if ([string]::Equals($full, $targetFull, [StringComparison]::OrdinalIgnoreCase)) {
            return [IO.Path]::GetFullPath($BuildTempBoundary).TrimEnd('\')
        }
    }
    throw "Limpeza recusada fora do mapa exato de pastas geradas: $full"
}

function Assert-GeneratedPath([string]$Path) {
    Get-GeneratedPathBoundary $Path | Out-Null
}

function Reset-IsolatedSmokeRoot {
    $expected = [IO.Path]::GetFullPath((Join-Path $BuildTempBoundary 'TurboRama-v25-smoke')).TrimEnd('\')
    $actual = [IO.Path]::GetFullPath($SmokeRoot).TrimEnd('\')
    if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Limpeza recusada fora do laboratorio isolado: $actual"
    }
    Reset-DirectoryByQuarantine $actual $BuildTempBoundary 'laboratorio isolado do instalador'
}

function Get-CanonicalPath([string]$Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd('\\')
}

function Get-PathEntryState([string]$Path) {
    $full = Get-CanonicalPath $Path
    try {
        $attributes = [IO.File]::GetAttributes($full)
        return [pscustomobject]@{ Exists = $true; FullPath = $full; Attributes = $attributes }
    }
    catch [IO.FileNotFoundException] {
        return [pscustomobject]@{ Exists = $false; FullPath = $full; Attributes = [IO.FileAttributes]0 }
    }
    catch [IO.DirectoryNotFoundException] {
        return [pscustomobject]@{ Exists = $false; FullPath = $full; Attributes = [IO.FileAttributes]0 }
    }
}

function Assert-RegularDirectoryPath([string]$Path, [string]$Label) {
    $state = Get-PathEntryState $Path
    if (-not $state.Exists) { throw "$Label nao encontrado: $($state.FullPath)" }
    if (($state.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label nao pode ser ponto de reanalise: $($state.FullPath)"
    }
    if (($state.Attributes -band [IO.FileAttributes]::Directory) -eq 0) {
        throw "$Label nao e uma pasta regular: $($state.FullPath)"
    }
    return $state.FullPath
}

function Assert-RegularFilePath([string]$Path, [string]$Label) {
    $state = Get-PathEntryState $Path
    if (-not $state.Exists) { throw "$Label nao encontrado: $($state.FullPath)" }
    if (($state.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label nao pode ser ponto de reanalise: $($state.FullPath)"
    }
    if (($state.Attributes -band [IO.FileAttributes]::Directory) -ne 0) {
        throw "$Label nao e um arquivo regular: $($state.FullPath)"
    }
    return $state.FullPath
}

function Assert-PathEntryAbsent([string]$Path, [string]$Label) {
    $state = Get-PathEntryState $Path
    if ($state.Exists) { throw "$Label ja existe ou foi redirecionado: $($state.FullPath)" }
}

function Assert-ExactPath([string]$Path, [string]$Expected, [string]$Label) {
    $full = Get-CanonicalPath $Path
    $expectedFull = Get-CanonicalPath $Expected
    if (-not [string]::Equals($full, $expectedFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label fora da fronteira esperada: $full (esperado $expectedFull)"
    }
    return $full
}

function Assert-AnchoredRegularDirectoryPath([string]$Path, [string]$Boundary, [string]$Label, [switch]$AllowMissingTail) {
    $full = Get-CanonicalPath $Path
    $boundaryFull = Assert-RegularDirectoryPath $Boundary "Fronteira de $Label"
    if ((-not [string]::Equals($full, $boundaryFull, [StringComparison]::OrdinalIgnoreCase)) -and
        (-not $full.StartsWith($boundaryFull + '\', [StringComparison]::OrdinalIgnoreCase))) {
        throw "$Label fora da fronteira permitida: $full (fronteira $boundaryFull)"
    }

    $relative = $full.Substring($boundaryFull.Length).TrimStart('\')
    if ([string]::IsNullOrWhiteSpace($relative)) { return $full }
    $current = $boundaryFull
    $missing = $false
    foreach ($segment in $relative.Split([char]'\')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -eq '.' -or $segment -eq '..') {
            throw "$Label possui componente invalido: $full"
        }
        $current = Join-Path $current $segment
        $state = Get-PathEntryState $current
        if (-not $state.Exists) {
            if (-not $AllowMissingTail) { throw "$Label nao encontrado: $current" }
            $missing = $true
            continue
        }
        if ($missing) { throw "$Label possui componente acessivel apos ancestral ausente: $current" }
        if (($state.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label atravessa ponto de reanalise: $current"
        }
        if (($state.Attributes -band [IO.FileAttributes]::Directory) -eq 0) {
            throw "$Label atravessa componente que nao e pasta: $current"
        }
    }
    return $full
}

function Assert-ReleaseAuthenticode([string]$Root) {
    if (-not $script:SigningEnabled) { return }
    $expectedThumbprint = ($CertificadoThumbprint -replace '\s','').ToUpperInvariant()
    if ($expectedThumbprint -notmatch '^[0-9A-F]{40}$') {
        throw 'A release comercial nao possui thumbprint Authenticode valido para revalidacao.'
    }
    foreach ($name in @(
        $InstallerFileName,
        'CONFIGURAR-USER-TOKEN-PIX.exe',
        'CONFIGURAR-ACCESS-TOKEN-PIX.exe')) {
        $path = Join-Path $Root $name
        Assert-RegularSingleLinkFilePath $path "Binario assinado da release ($name)" | Out-Null
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if (($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) -or
            (-not $signature.SignerCertificate) -or
            ($signature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $expectedThumbprint) -or
            (-not $signature.TimeStamperCertificate)) {
            throw "Release recusada: assinatura, editor ou carimbo RFC 3161 invalido em $name."
        }
    }
    $sevenZipPath = Join-Path $Root '7za.exe'
    Assert-RegularSingleLinkFilePath $sevenZipPath '7za.exe pinado da release' | Out-Null
    $sevenZipHash = (Get-FileHash -LiteralPath $sevenZipPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($sevenZipHash -ne '223B873C50380FE9A39F1A22B6ABF8D46DB506E1C08D08312902F6F3CD1F7AC3') {
        throw 'Release recusada: 7za.exe diverge do binario 24.09 aprovado.'
    }
}

function Test-ReleaseDirectory([string]$Root, [string[]]$ArtifactNames) {
    $rootFull = Assert-RegularDirectoryPath $Root 'Diretorio de release'
    $allowedNames = @($ArtifactNames + $ChecksumFileName)
    foreach ($item in Get-ChildItem -LiteralPath $rootFull -Force) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Release possui ponto de reanalise proibido: $($item.Name)"
        }
        if ($item.PSIsContainer -or -not ($allowedNames -contains $item.Name)) {
            throw "Release possui arquivo ou pasta inesperado: $($item.Name)"
        }
    }
    foreach ($name in $ArtifactNames) {
        if ($name -match '[\\/:]' -or [string]::IsNullOrWhiteSpace($name)) {
            throw "Nome de artefato de release invalido: $name"
        }
        Assert-RegularSingleLinkFilePath (Join-Path $rootFull $name) "Artefato de release ($name)" | Out-Null
    }

    $checksumPath = Join-Path $rootFull $ChecksumFileName
    $checksumSnapshot = Get-PinnedRegularFileSnapshot $checksumPath 'Manifesto SHA-256 da release' -IncludeBytes
    $seen = @{}
    $checksumText = [Text.Encoding]::ASCII.GetString($checksumSnapshot.Bytes)
    $checksumLines = @($checksumText -split "\r?\n")
    foreach ($line in $checksumLines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^([0-9A-Fa-f]{64})  ([^\\/:]+)$') {
            throw "Linha invalida no manifesto SHA-256: $line"
        }
        $hash = $Matches[1].ToUpperInvariant()
        $name = $Matches[2]
        if ($seen.ContainsKey($name) -or -not ($ArtifactNames -contains $name)) {
            throw "Manifesto SHA-256 possui artefato inesperado ou duplicado: $name"
        }
        $manifestArtifact = Join-Path $rootFull $name
        $artifactSnapshot = Get-PinnedRegularFileSnapshot $manifestArtifact "Artefato manifestado ($name)"
        $actual = $artifactSnapshot.Sha256
        if ($actual -ne $hash) { throw "SHA-256 invalido para artefato de release: $name" }
        $seen[$name] = $true
    }
    foreach ($name in $ArtifactNames) {
        if (-not $seen.ContainsKey($name)) { throw "Manifesto SHA-256 nao cobre o artefato: $name" }
    }
	Assert-ReleaseAuthenticode $rootFull
    return $true
}

function Write-ReleaseChecksums([string]$Root, [string[]]$ArtifactNames) {
    $lines = foreach ($name in $ArtifactNames) {
        $path = Join-Path $Root $name
        $artifactSnapshot = Get-PinnedRegularFileSnapshot $path "Artefato para checksum ($name)"
        "$($artifactSnapshot.Sha256)  $name"
    }
    $checksumPath = Join-Path $Root $ChecksumFileName
    Assert-PathEntryAbsent $checksumPath 'Novo manifesto SHA-256 da release'
    $checksumBytes = [Text.Encoding]::ASCII.GetBytes((($lines -join "`r`n") + "`r`n"))
    $checksumStream = $null
    try {
        $checksumStream = [IO.File]::Open($checksumPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $checksumStream.Write($checksumBytes, 0, $checksumBytes.Length)
        $checksumStream.Flush($true)
    }
    finally {
        if ($null -ne $checksumStream) { $checksumStream.Dispose() }
    }
    Assert-RegularSingleLinkFilePath $checksumPath 'Manifesto SHA-256 criado' | Out-Null
}

function Assert-RetiredRepairAbsent([string]$Root, [string]$Label) {
    $retired = Join-Path $Root $RetiredRepairFileName
    if ((Get-PathEntryState $retired).Exists) {
        throw "$Label ainda contem o reparador aposentado: $retired"
    }
}

function Assert-ArchiveEntries(
    [string]$SevenZip,
    [string]$Archive,
    [string[]]$ExpectedEntries,
    [string[]]$ForbiddenEntries = @()) {
    Require-File $SevenZip '7-Zip para inspecao do payload'
    Require-File $Archive 'Payload para inspecao'
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $listing = & $SevenZip l -slt $Archive 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($exitCode -ne 0) { throw "Nao foi possivel listar o payload comercial (codigo $exitCode)." }
    foreach ($entry in $ExpectedEntries) {
        $expression = '^Path = ' + [regex]::Escape($entry) + '$'
        if (-not ($listing | Where-Object { $_ -match $expression })) {
            throw "Payload comercial nao contem o arquivo exigido: $entry"
        }
    }
    foreach ($entry in $ForbiddenEntries) {
        $expression = '^Path = ' + [regex]::Escape($entry) + '$'
        if ($listing | Where-Object { $_ -match $expression }) {
            throw "Payload comercial contem arquivo proibido: $entry"
        }
    }
}

function Test-ForbiddenCommercialPayloadPath([string]$RelativePath) {
    $normalized = ($RelativePath -replace '/', '\').TrimStart('\')
    if ([string]::IsNullOrWhiteSpace($normalized)) { return $false }

    $extension = [IO.Path]::GetExtension($normalized).ToLowerInvariant()
    if ($extension -in @(
        '.pdb', '.cs', '.csproj', '.sln', '.c', '.cc', '.cpp', '.cxx',
        '.h', '.hh', '.hpp', '.hxx', '.vcxproj', '.vcxproj.filters',
        '.inl', '.ipp', '.asm', '.rc', '.resx', '.props', '.targets', '.filters',
		'.ps1', '.psm1', '.psd1', '.cmd', '.bat',
		'.pfx', '.p12', '.pem', '.key', '.snk', '.pvk', '.cer', '.crt', '.csr', '.jks')) {
        return $true
    }

    $leaf = [IO.Path]::GetFileName($normalized)
	if ($leaf -match '(?i)(TurboRamaPixLicenseIssuer|license[-_.]?issuer|emissor[-_.]?licen)') { return $true }
    if ($leaf -in @('CMakeLists.txt', 'Makefile', 'packages.lock.json')) { return $true }
    if ($leaf -match '(?i)(^|[._-])(test|tests|testing)([._-]|$)') { return $true }
    return $normalized -match '(?i)(^|\\)(test|tests|testing|samples?|examples?|obj)(\\|$)'
}

function Assert-CommercialPayloadTree([string]$Root, [string]$Label) {
    if (-not $ProtecaoComercial) { return }
    Require-Directory $Root $Label
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $forbidden = @(
        Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force -ErrorAction Stop |
            Where-Object {
                $relative = $_.FullName.Substring($rootFull.Length).TrimStart('\')
                Test-ForbiddenCommercialPayloadPath $relative
            }
    )
    if ($forbidden.Count -gt 0) {
        $relativeNames = @($forbidden | ForEach-Object {
            $_.FullName.Substring($rootFull.Length).TrimStart('\')
        })
        throw "$Label contem artefatos proibidos no modo comercial: $($relativeNames -join ', ')"
    }
}

function Assert-CommercialArchiveHygiene([string]$SevenZip, [string]$Archive) {
    if (-not $ProtecaoComercial) { return }
    Require-File $SevenZip '7-Zip para auditoria comercial'
    Require-File $Archive 'Payload para auditoria comercial'
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $listing = & $SevenZip l -slt $Archive 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($exitCode -ne 0) { throw "Nao foi possivel auditar o payload comercial (codigo $exitCode)." }

    $forbidden = @(
        $listing |
            Where-Object { $_ -is [string] -and $_ -match '^Path = (.+)$' } |
            ForEach-Object { $Matches[1] } |
            Where-Object { Test-ForbiddenCommercialPayloadPath $_ }
    )
    if ($forbidden.Count -gt 0) {
        throw "Payload comercial compactado contem artefatos proibidos: $($forbidden -join ', ')"
    }
}

function Initialize-SmokeFileIdentity {
    if ($null -ne ('TurboRama.Build.SmokeFileIdentity' -as [type])) { return }
    $identitySource = @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace TurboRama.Build
{
    public sealed class PinnedFileLease : IDisposable
    {
        private FileStream stream;

        internal PinnedFileLease(SafeFileHandle handle, string identity)
        {
            stream = new FileStream(handle, FileAccess.Read, 1024 * 1024, false);
            Identity = identity;
            using (SHA256 sha256 = SHA256.Create())
            {
                Sha256 = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", String.Empty);
            }
            stream.Position = 0;
        }

        public string Identity { get; private set; }
        public string Sha256 { get; private set; }

        public byte[] ReadAllBytes(int maximumBytes)
        {
            if (stream == null) throw new ObjectDisposedException("PinnedFileLease");
            if (maximumBytes < 0 || stream.Length > maximumBytes || stream.Length > Int32.MaxValue)
                throw new InvalidDataException("Arquivo pinado excede o limite de leitura.");
            stream.Position = 0;
            byte[] bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) throw new EndOfStreamException("Leitura incompleta do arquivo pinado.");
                offset += read;
            }
            stream.Position = 0;
            return bytes;
        }

        public void Dispose()
        {
            if (stream == null) return;
            stream.Dispose();
            stream = null;
        }
    }

    public static class SmokeFileIdentity
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_READ_ATTRIBUTES = 0x00000080;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
        private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            internal uint Low;
            internal uint High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            internal uint FileAttributes;
            internal FILETIME CreationTime;
            internal FILETIME LastAccessTime;
            internal FILETIME LastWriteTime;
            internal uint VolumeSerialNumber;
            internal uint FileSizeHigh;
            internal uint FileSizeLow;
            internal uint NumberOfLinks;
            internal uint FileIndexHigh;
            internal uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out BY_HANDLE_FILE_INFORMATION information);

        public static PinnedFileLease OpenPinned(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Caminho vazio.", "path");
            SafeFileHandle handle = CreateFileW(
                 path,
                 GENERIC_READ | FILE_READ_ATTRIBUTES,
                 FILE_SHARE_READ,
                 IntPtr.Zero,
                 OPEN_EXISTING,
                 FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN,
                 IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "Nao foi possivel fixar o arquivo para leitura: " + path);
            }
            try
            {
                BY_HANDLE_FILE_INFORMATION information;
                if (!GetFileInformationByHandle(handle, out information))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Nao foi possivel ler FileId do arquivo fixado: " + path);
                }
                if ((information.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0)
                    throw new InvalidDataException("O caminho nao e um arquivo regular: " + path);
                if (information.NumberOfLinks != 1)
                    throw new InvalidDataException("O arquivo possui hardlinks externos proibidos: " + path);
                string identity = information.VolumeSerialNumber.ToString("X8") + ":"
                    + information.FileIndexHigh.ToString("X8")
                    + information.FileIndexLow.ToString("X8") + ":links="
                    + information.NumberOfLinks.ToString();
                PinnedFileLease lease = new PinnedFileLease(handle, identity);
                handle = null;
                return lease;
            }
            finally
            {
                if (handle != null) handle.Dispose();
            }
        }

        public static string Read(string path)
        {
            using (PinnedFileLease lease = OpenPinned(path)) return lease.Identity;
        }
    }
}
'@
    Add-Type -TypeDefinition $identitySource -Language CSharp
}

function Assert-RegularSingleLinkFilePath([string]$Path, [string]$Label) {
    $full = Assert-RegularFilePath $Path $Label
    Initialize-SmokeFileIdentity
    $lease = $null
    try {
        $lease = [TurboRama.Build.SmokeFileIdentity]::OpenPinned($full)
    }
    catch {
        throw "$Label nao pode ser fixado como arquivo regular de link unico: $full. $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $lease) { $lease.Dispose() }
    }
    return $full
}

function Get-PinnedRegularFileSnapshot([string]$Path, [string]$Label, [switch]$IncludeBytes) {
    $full = Assert-RegularFilePath $Path $Label
    Initialize-SmokeFileIdentity
    $lease = $null
    try {
        $lease = [TurboRama.Build.SmokeFileIdentity]::OpenPinned($full)
        return [pscustomobject]@{
            FullPath = $full
            Identity = $lease.Identity
            Sha256 = $lease.Sha256
            Bytes = $(if ($IncludeBytes) { $lease.ReadAllBytes(1MB) } else { $null })
        }
    }
    catch {
        throw "$Label nao pode ser lido pelo mesmo handle pinado: $full. $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $lease) { $lease.Dispose() }
    }
}

function Get-Utf8TextSha256([string]$Text) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($(if ($null -eq $Text) { '' } else { $Text }))
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-','')
    }
    finally { $algorithm.Dispose() }
}

function Get-GitWorkingTreeFingerprint([string]$Git, [string]$Root) {
    $diffLines = @(& $Git -c core.quotepath=false -C $Root diff --binary --no-ext-diff HEAD --)
    if ($LASTEXITCODE -ne 0) { throw 'git diff falhou ao fotografar a fonte do build.' }
    $untracked = @(& $Git -c core.quotepath=false -C $Root ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files falhou ao fotografar arquivos novos.' }
    $entries = [Collections.Generic.List[string]]::new()
    $entries.Add('TRACKED-DIFF-SHA256|' + (Get-Utf8TextSha256 ($diffLines -join "`n")))
    foreach ($relative in @($untracked | Sort-Object)) {
        if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative)) {
            throw "Git informou caminho novo invalido: $relative"
        }
        $segments = $relative.Replace('/','\').Split([char]'\')
        if (@($segments | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -ne 0) {
            throw "Git informou caminho novo fora da fronteira: $relative"
        }
        $full = [IO.Path]::GetFullPath((Join-Path $Root $relative))
        $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
        if (-not $full.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Arquivo novo saiu da raiz Git: $relative"
        }
        $snapshot = Get-PinnedRegularFileSnapshot $full "Arquivo novo da fonte ($relative)"
        $entries.Add("UNTRACKED|$relative|$($snapshot.Identity)|$($snapshot.Sha256)")
    }
    return Get-Utf8TextSha256 ($entries -join "`n")
}

function Initialize-NativePathRename {
    if ($null -ne ('TurboRama.Build.NativePathRename' -as [type])) { return }
    $nativeSource = @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TurboRama.Build
{
    public static class NativePathRename
    {
        private const uint DELETE = 0x00010000;
        private const uint SYNCHRONIZE = 0x00100000;
        private const uint FILE_LIST_DIRECTORY = 0x00000001;
        private const uint FILE_ADD_FILE = 0x00000002;
        private const uint FILE_ADD_SUBDIRECTORY = 0x00000004;
        private const uint FILE_READ_ATTRIBUTES = 0x00000080;
        private const uint FILE_TRAVERSE = 0x00000020;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint FILE_CREATE = 2;
        private const uint FILE_DIRECTORY_FILE = 0x00000001;
        private const uint FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020;
        private const uint FILE_OPEN_REPARSE_POINT = 0x00200000;
        private const uint OBJ_CASE_INSENSITIVE = 0x00000040;

        private enum FILE_INFO_BY_HANDLE_CLASS
        {
            FileAttributeTagInfo = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_ATTRIBUTE_TAG_INFO
        {
            internal uint FileAttributes;
            internal uint ReparseTag;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_STATUS_BLOCK
        {
            internal IntPtr Status;
            internal UIntPtr Information;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UNICODE_STRING
        {
            internal ushort Length;
            internal ushort MaximumLength;
            internal IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OBJECT_ATTRIBUTES
        {
            internal uint Length;
            internal IntPtr RootDirectory;
            internal IntPtr ObjectName;
            internal uint Attributes;
            internal IntPtr SecurityDescriptor;
            internal IntPtr SecurityQualityOfService;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            FILE_INFO_BY_HANDLE_CLASS informationClass,
            out FILE_ATTRIBUTE_TAG_INFO information,
            uint bufferSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle file,
            [Out] StringBuilder path,
            uint pathLength,
            uint flags);

        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationFile(
            SafeFileHandle file,
            out IO_STATUS_BLOCK ioStatusBlock,
            IntPtr information,
            uint bufferSize,
            int informationClass);

        [DllImport("ntdll.dll")]
        private static extern uint RtlNtStatusToDosError(int status);

        [DllImport("ntdll.dll")]
        private static extern int NtCreateFile(
            out IntPtr fileHandle,
            uint desiredAccess,
            ref OBJECT_ATTRIBUTES objectAttributes,
            out IO_STATUS_BLOCK ioStatusBlock,
            IntPtr allocationSize,
            uint fileAttributes,
            uint shareAccess,
            uint createDisposition,
            uint createOptions,
            IntPtr eaBuffer,
            uint eaLength);

        public static void MoveDirectoryNoReplace(
            string source,
            string destination,
            string sourceBoundary,
            string destinationBoundary,
            Action<string, string> beforeRename)
        {
            MoveNoReplace(source, destination, sourceBoundary, destinationBoundary, true, beforeRename);
        }

        public static void MoveFileNoReplace(
            string source,
            string destination,
            string sourceBoundary,
            string destinationBoundary,
            Action<string, string> beforeRename)
        {
            MoveNoReplace(source, destination, sourceBoundary, destinationBoundary, false, beforeRename);
        }

        public static void CreateEmptyDirectoryAndRenameNoReplace(
            string replacement,
            string destination,
            string boundary,
            Action<string, string> beforeCreate,
            Action<string, string> beforeRename)
        {
            string replacementFull = NormalizeInputPath(replacement);
            string destinationFull = NormalizeInputPath(destination);
            string boundaryFull = NormalizeInputPath(boundary);
            string replacementParent = NormalizeInputPath(Path.GetDirectoryName(replacementFull));
            string destinationParent = NormalizeInputPath(Path.GetDirectoryName(destinationFull));
            if (!String.Equals(replacementParent, destinationParent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Reposicao vazia e destino devem ter o mesmo parent.");

            string replacementName = Path.GetFileName(replacementFull);
            string destinationName = Path.GetFileName(destinationFull);
            if (String.IsNullOrEmpty(replacementName) || String.IsNullOrEmpty(destinationName))
                throw new InvalidOperationException("Nome invalido para criacao/promocao de diretorio vazio.");

            string parentRelative = GetRelativeDescendant(boundaryFull, destinationParent, "parent da reposicao vazia");
            string replacementRelative = GetRelativeDescendant(boundaryFull, replacementFull, "reposicao vazia");
            string destinationRelative = GetRelativeDescendant(boundaryFull, destinationFull, "destino da reposicao vazia");

            using (SafeFileHandle boundaryHandle = OpenPath(
                boundaryFull, FILE_READ_ATTRIBUTES | SYNCHRONIZE, "fronteira da reposicao vazia"))
            using (SafeFileHandle parentHandle = OpenPath(
                destinationParent,
                FILE_LIST_DIRECTORY | FILE_ADD_SUBDIRECTORY | FILE_READ_ATTRIBUTES | FILE_TRAVERSE | SYNCHRONIZE,
                "parent da reposicao vazia"))
            {
                ValidateRegularDirectory(boundaryHandle, "fronteira da reposicao vazia");
                ValidateRegularDirectory(parentHandle, "parent da reposicao vazia");
                ValidateExpectedFinalPath(boundaryHandle, parentHandle, parentRelative, "parent da reposicao vazia");

                if (beforeCreate != null)
                    beforeCreate(replacementFull, destinationFull);

                // Creation is still relative to this exact pinned parent. If
                // an ancestor changed during the hook, refuse before creating.
                ValidateRegularDirectory(boundaryHandle, "fronteira antes de criar reposicao vazia");
                ValidateRegularDirectory(parentHandle, "parent antes de criar reposicao vazia");
                ValidateExpectedFinalPath(boundaryHandle, parentHandle, parentRelative, "parent antes de criar reposicao vazia");

                using (SafeFileHandle createdHandle = CreateRelativeDirectory(
                    parentHandle, replacementName, replacementFull))
                {
                    ValidateSourceType(createdHandle, true, "reposicao vazia criada");
                    ValidateExpectedFinalPath(boundaryHandle, createdHandle, replacementRelative, "reposicao vazia criada");

                    if (beforeRename != null)
                        beforeRename(replacementFull, destinationFull);

                    ValidateRegularDirectory(boundaryHandle, "fronteira antes de promover reposicao vazia");
                    ValidateRegularDirectory(parentHandle, "parent antes de promover reposicao vazia");
                    ValidateSourceType(createdHandle, true, "reposicao vazia antes da promocao");
                    ValidateExpectedFinalPath(boundaryHandle, parentHandle, parentRelative, "parent antes de promover reposicao vazia");
                    ValidateExpectedFinalPath(boundaryHandle, createdHandle, replacementRelative, "reposicao vazia antes da promocao");
                    RenameRelativeNoReplace(createdHandle, parentHandle, destinationName, destinationFull);
                    ValidateExpectedFinalPath(boundaryHandle, createdHandle, destinationRelative, "reposicao vazia promovida");
                    GC.KeepAlive(parentHandle);
                }
            }
        }

        private static void MoveNoReplace(
            string source,
            string destination,
            string sourceBoundary,
            string destinationBoundary,
            bool sourceMustBeDirectory,
            Action<string, string> beforeRename)
        {
            string sourceFull = NormalizeInputPath(source);
            string destinationFull = NormalizeInputPath(destination);
            string sourceBoundaryFull = NormalizeInputPath(sourceBoundary);
            string destinationBoundaryFull = NormalizeInputPath(destinationBoundary);
            string destinationParentFull = NormalizeInputPath(Path.GetDirectoryName(destinationFull));
            string destinationName = Path.GetFileName(destinationFull);
            if (String.IsNullOrEmpty(destinationName) || destinationName == "." || destinationName == "..")
                throw new InvalidOperationException("Nome de destino invalido para rename por handle: " + destinationFull);

            string sourceRelative = GetRelativeDescendant(sourceBoundaryFull, sourceFull, "origem");
            string destinationParentRelative = GetRelativeDescendant(destinationBoundaryFull, destinationParentFull, "parent do destino");
            if (String.IsNullOrEmpty(sourceRelative))
                throw new InvalidOperationException("A origem nao pode ser a propria fronteira: " + sourceFull);

            using (SafeFileHandle sourceBoundaryHandle = OpenPath(
                sourceBoundaryFull, FILE_READ_ATTRIBUTES | SYNCHRONIZE, "fronteira da origem"))
            using (SafeFileHandle destinationBoundaryHandle = OpenPath(
                destinationBoundaryFull, FILE_READ_ATTRIBUTES | SYNCHRONIZE, "fronteira do destino"))
            using (SafeFileHandle sourceHandle = OpenPath(
                sourceFull, DELETE | FILE_READ_ATTRIBUTES | SYNCHRONIZE, "origem"))
            using (SafeFileHandle destinationParentHandle = OpenPath(
                destinationParentFull,
                FILE_LIST_DIRECTORY | (sourceMustBeDirectory ? FILE_ADD_SUBDIRECTORY : FILE_ADD_FILE) |
                    FILE_READ_ATTRIBUTES | FILE_TRAVERSE | SYNCHRONIZE,
                "parent do destino"))
            {
                ValidateRegularDirectory(sourceBoundaryHandle, "fronteira da origem");
                ValidateRegularDirectory(destinationBoundaryHandle, "fronteira do destino");
                ValidateSourceType(sourceHandle, sourceMustBeDirectory, "origem");
                ValidateRegularDirectory(destinationParentHandle, "parent do destino");
                ValidateExpectedFinalPath(sourceBoundaryHandle, sourceHandle, sourceRelative, "origem");
                ValidateExpectedFinalPath(destinationBoundaryHandle, destinationParentHandle, destinationParentRelative, "parent do destino");

                if (beforeRename != null)
                    beforeRename(sourceFull, destinationFull);

                // Recheck the same open objects after the deterministic test
                // hook (and after any real concurrent activity). The handles
                // remain open without FILE_SHARE_DELETE through the rename.
                ValidateRegularDirectory(sourceBoundaryHandle, "fronteira da origem antes do rename");
                ValidateRegularDirectory(destinationBoundaryHandle, "fronteira do destino antes do rename");
                ValidateSourceType(sourceHandle, sourceMustBeDirectory, "origem antes do rename");
                ValidateRegularDirectory(destinationParentHandle, "parent do destino antes do rename");
                ValidateExpectedFinalPath(sourceBoundaryHandle, sourceHandle, sourceRelative, "origem antes do rename");
                ValidateExpectedFinalPath(destinationBoundaryHandle, destinationParentHandle, destinationParentRelative, "parent do destino antes do rename");

                RenameRelativeNoReplace(sourceHandle, destinationParentHandle, destinationName, destinationFull);
                GC.KeepAlive(destinationParentHandle);
            }
        }

        private static SafeFileHandle OpenPath(string path, uint desiredAccess, string label)
        {
            // Omitting FILE_SHARE_DELETE is intentional: a successful open
            // proves there was no already-open delete-capable handle whose
            // sharing conflicts, and blocks later rename/delete opens until
                // NtSetInformationFile has returned.
            SafeFileHandle handle = CreateFileW(
                path,
                desiredAccess,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);
            if (handle == null || handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                if (handle != null) handle.Dispose();
                throw Win32IOException("Nao foi possivel abrir " + label + " por handle", path, error);
            }
            return handle;
        }

        private static SafeFileHandle CreateRelativeDirectory(
            SafeFileHandle parentHandle,
            string relativeName,
            string displayPath)
        {
            byte[] nameBytes = Encoding.Unicode.GetBytes(relativeName);
            if (nameBytes.Length > UInt16.MaxValue - 2)
                throw new InvalidOperationException("Nome de reposicao vazia excede UNICODE_STRING: " + displayPath);

            IntPtr nameBuffer = IntPtr.Zero;
            IntPtr unicodeStringBuffer = IntPtr.Zero;
            IntPtr rawHandle = IntPtr.Zero;
            try
            {
                nameBuffer = Marshal.StringToHGlobalUni(relativeName);
                UNICODE_STRING objectName = new UNICODE_STRING();
                objectName.Length = (ushort)nameBytes.Length;
                objectName.MaximumLength = (ushort)(nameBytes.Length + 2);
                objectName.Buffer = nameBuffer;
                unicodeStringBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UNICODE_STRING)));
                Marshal.StructureToPtr(objectName, unicodeStringBuffer, false);

                OBJECT_ATTRIBUTES attributes = new OBJECT_ATTRIBUTES();
                attributes.Length = (uint)Marshal.SizeOf(typeof(OBJECT_ATTRIBUTES));
                attributes.RootDirectory = parentHandle.DangerousGetHandle();
                attributes.ObjectName = unicodeStringBuffer;
                attributes.Attributes = OBJ_CASE_INSENSITIVE;
                attributes.SecurityDescriptor = IntPtr.Zero;
                attributes.SecurityQualityOfService = IntPtr.Zero;

                IO_STATUS_BLOCK ioStatus;
                int status = NtCreateFile(
                    out rawHandle,
                    FILE_LIST_DIRECTORY | DELETE | FILE_READ_ATTRIBUTES | SYNCHRONIZE,
                    ref attributes,
                    out ioStatus,
                    IntPtr.Zero,
                    FILE_ATTRIBUTE_NORMAL,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    FILE_CREATE,
                    FILE_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT | FILE_OPEN_REPARSE_POINT,
                    IntPtr.Zero,
                    0);
                if (status != 0)
                {
                    if (rawHandle != IntPtr.Zero && rawHandle != new IntPtr(-1))
                        new SafeFileHandle(rawHandle, true).Dispose();
                    throw Win32IOException(
                        "NtCreateFile relativo recusou a reposicao vazia",
                        displayPath,
                        unchecked((int)RtlNtStatusToDosError(status)));
                }
                SafeFileHandle created = new SafeFileHandle(rawHandle, true);
                rawHandle = IntPtr.Zero;
                return created;
            }
            finally
            {
                if (unicodeStringBuffer != IntPtr.Zero) Marshal.FreeHGlobal(unicodeStringBuffer);
                if (nameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(nameBuffer);
            }
        }

        private static void ValidateSourceType(SafeFileHandle handle, bool mustBeDirectory, string label)
        {
            FILE_ATTRIBUTE_TAG_INFO info = ReadAttributeTagInfo(handle, label);
            if ((info.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                throw new InvalidOperationException(label + " nao pode ser ponto de reanalise.");
            bool isDirectory = (info.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
            if (isDirectory != mustBeDirectory)
                throw new InvalidOperationException(label + (mustBeDirectory ? " nao e uma pasta regular." : " nao e um arquivo regular."));
        }

        private static void ValidateRegularDirectory(SafeFileHandle handle, string label)
        {
            ValidateSourceType(handle, true, label);
        }

        private static FILE_ATTRIBUTE_TAG_INFO ReadAttributeTagInfo(SafeFileHandle handle, string label)
        {
            FILE_ATTRIBUTE_TAG_INFO info;
            uint size = (uint)Marshal.SizeOf(typeof(FILE_ATTRIBUTE_TAG_INFO));
            if (!GetFileInformationByHandleEx(handle, FILE_INFO_BY_HANDLE_CLASS.FileAttributeTagInfo, out info, size))
                throw Win32IOException("Falha ao validar FileAttributeTagInfo de " + label, null, Marshal.GetLastWin32Error());
            return info;
        }

        private static void ValidateExpectedFinalPath(
            SafeFileHandle boundaryHandle,
            SafeFileHandle targetHandle,
            string requestedRelative,
            string label)
        {
            string boundaryFinal = GetFinalPath(boundaryHandle, "fronteira de " + label);
            string expectedFinal = String.IsNullOrEmpty(requestedRelative)
                ? boundaryFinal
                : NormalizeInputPath(Path.Combine(boundaryFinal, requestedRelative));
            string targetFinal = GetFinalPath(targetHandle, label);
            if (!String.Equals(expectedFinal, targetFinal, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    label + " resolveu para caminho diferente do esperado; possivel troca de ancestral/reparse: " +
                    targetFinal + " (esperado " + expectedFinal + ")");
        }

        private static string GetFinalPath(SafeFileHandle handle, string label)
        {
            uint capacity = 512;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                StringBuilder buffer = new StringBuilder((int)capacity);
                uint length = GetFinalPathNameByHandleW(handle, buffer, capacity, 0);
                if (length == 0)
                    throw Win32IOException("Falha em GetFinalPathNameByHandleW para " + label, null, Marshal.GetLastWin32Error());
                if (length < capacity)
                    return NormalizeFinalPath(buffer.ToString());
                capacity = length + 1;
            }
            throw new IOException("GetFinalPathNameByHandleW excedeu o tamanho esperado para " + label + ".");
        }

        private static void RenameRelativeNoReplace(
            SafeFileHandle sourceHandle,
            SafeFileHandle destinationParentHandle,
            string destinationName,
            string destinationFull)
        {
            byte[] nameBytes = Encoding.Unicode.GetBytes(destinationName);
            int rootOffset = IntPtr.Size == 8 ? 8 : 4;
            int lengthOffset = rootOffset + IntPtr.Size;
            int nameOffset = lengthOffset + 4;
            // FILE_RENAME_INFORMATION contains WCHAR FileName[1] and its native
            // sizeof includes trailing structure alignment (24 bytes on x64,
            // 16 on x86). Keep that full header plus a terminating NUL even
            // though FileNameLength deliberately excludes the terminator.
            int nativeHeaderSize = IntPtr.Size == 8 ? 24 : 16;
            int totalSize = checked(nativeHeaderSize + nameBytes.Length + 2);
            IntPtr buffer = Marshal.AllocHGlobal(totalSize);
            try
            {
                for (int index = 0; index < totalSize; index++) Marshal.WriteByte(buffer, index, 0);
                // ReplaceIfExists/Flags remains zero. A concurrently-created
                // destination therefore makes the atomic rename fail.
                Marshal.WriteIntPtr(buffer, rootOffset, destinationParentHandle.DangerousGetHandle());
                Marshal.WriteInt32(buffer, lengthOffset, nameBytes.Length);
                Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, nameOffset), nameBytes.Length);
                IO_STATUS_BLOCK ioStatus;
                // FileRenameInformation (10) accepts RootDirectory-relative
                // FILE_RENAME_INFORMATION on every supported NT version. The
                // Win32 SetFileInformationByHandle wrapper returns ERROR_INVALID_PARAMETER
                // for this relative form on some Windows builds even though
                // the public structure documents RootDirectory.
                int status = NtSetInformationFile(
                    sourceHandle,
                    out ioStatus,
                    buffer,
                    (uint)totalSize,
                    10);
                if (status != 0)
                    throw Win32IOException(
                        "Rename por handle recusado",
                        destinationFull,
                        unchecked((int)RtlNtStatusToDosError(status)));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string GetRelativeDescendant(string boundary, string path, string label)
        {
            if (String.Equals(boundary, path, StringComparison.OrdinalIgnoreCase)) return String.Empty;
            string prefix = boundary + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(label + " fora da fronteira permitida: " + path + " (fronteira " + boundary + ")");
            return path.Substring(prefix.Length);
        }

        private static string NormalizeInputPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Caminho vazio.", "path");
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            while (full.Length > root.Length &&
                   (full.EndsWith("\\", StringComparison.Ordinal) || full.EndsWith("/", StringComparison.Ordinal)))
                full = full.Substring(0, full.Length - 1);
            return full;
        }

        private static string NormalizeFinalPath(string path)
        {
            if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
                path = "\\\\" + path.Substring(8);
            else if (path.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(4);
            return NormalizeInputPath(path);
        }

        private static IOException Win32IOException(string operation, string path, int error)
        {
            Win32Exception detail = new Win32Exception(error);
            string suffix = String.IsNullOrEmpty(path) ? String.Empty : ": " + path;
            return new IOException(operation + suffix + " (Win32 " + error + "): " + detail.Message, detail);
        }
    }
}
'@
    Add-Type -TypeDefinition $nativeSource -Language CSharp
}

function Invoke-NativePathRename(
    [string]$Source,
    [string]$Destination,
    [string]$SourceBoundary,
    [string]$DestinationBoundary,
    [switch]$Directory,
    [scriptblock]$BeforeNativeRenameTestHook = $null) {
    Initialize-NativePathRename
    $nativeHook = $null
    if ($null -ne $BeforeNativeRenameTestHook) {
        $nativeHook = [Action[string,string]]{
            param($openedSource, $targetDestination)
            & $BeforeNativeRenameTestHook $openedSource $targetDestination
        }
    }
    if ($Directory) {
        [TurboRama.Build.NativePathRename]::MoveDirectoryNoReplace(
            $Source, $Destination, $SourceBoundary, $DestinationBoundary, $nativeHook)
    }
    else {
        [TurboRama.Build.NativePathRename]::MoveFileNoReplace(
            $Source, $Destination, $SourceBoundary, $DestinationBoundary, $nativeHook)
    }
}

function New-EmptyDirectoryByHandleAndPromote(
    [string]$Replacement,
    [string]$Destination,
    [string]$Boundary,
    [scriptblock]$BeforeCreateTestHook = $null,
    [scriptblock]$BeforeRenameTestHook = $null) {
    Initialize-NativePathRename
    $nativeBeforeCreate = $null
    if ($null -ne $BeforeCreateTestHook) {
        $nativeBeforeCreate = [Action[string,string]]{
            param($createdPath, $targetPath)
            & $BeforeCreateTestHook $createdPath $targetPath
        }
    }
    $nativeBeforeRename = $null
    if ($null -ne $BeforeRenameTestHook) {
        $nativeBeforeRename = [Action[string,string]]{
            param($createdPath, $targetPath)
            & $BeforeRenameTestHook $createdPath $targetPath
        }
    }
    [TurboRama.Build.NativePathRename]::CreateEmptyDirectoryAndRenameNoReplace(
        $Replacement, $Destination, $Boundary, $nativeBeforeCreate, $nativeBeforeRename)
}

function Test-RetryableNativeRenameFailure([Exception]$Failure) {
    $current = $Failure
    while ($null -ne $current.InnerException -and $current -is [Management.Automation.MethodInvocationException]) {
        $current = $current.InnerException
    }
    return $current -is [IO.IOException]
}

function Move-DirectoryWithRetry(
    [string]$Source,
    [string]$Destination,
    [string]$Label,
    [string]$SourceBoundary,
    [string]$DestinationBoundary,
    [scriptblock]$BeforeNativeRenameTestHook = $null) {
    # Defender/indexing can briefly retain a just-produced artifact. Each retry
    # opens and pins fresh handles; security/type failures are never retried.
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        Assert-AnchoredRegularDirectoryPath $Source $SourceBoundary "Origem de $Label" | Out-Null
        Assert-AnchoredRegularDirectoryPath $Destination $DestinationBoundary "Destino de $Label" -AllowMissingTail | Out-Null
        Assert-PathEntryAbsent $Destination "Destino de $Label"
        try {
            Invoke-NativePathRename $Source $Destination $SourceBoundary $DestinationBoundary -Directory -BeforeNativeRenameTestHook $BeforeNativeRenameTestHook
            return
        }
        catch {
            if (-not (Test-RetryableNativeRenameFailure $_.Exception)) { throw }
            if ($attempt -eq 8) {
                throw "Nao foi possivel mover $Label por handle apos $attempt tentativas: $($_.Exception.Message)"
            }
            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }
}

function Move-RegularFileWithRetry(
    [string]$Source,
    [string]$Destination,
    [string]$Label,
    [string]$SourceBoundary,
    [string]$DestinationBoundary,
    [scriptblock]$BeforeNativeRenameTestHook = $null) {
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        Assert-RegularFilePath $Source "Origem de $Label" | Out-Null
        Assert-AnchoredRegularDirectoryPath (Split-Path -Parent (Get-CanonicalPath $Source)) $SourceBoundary "Parent da origem de $Label" | Out-Null
        Assert-AnchoredRegularDirectoryPath (Split-Path -Parent (Get-CanonicalPath $Destination)) $DestinationBoundary "Parent do destino de $Label" | Out-Null
        Assert-PathEntryAbsent $Destination "Destino de $Label"
        try {
            Invoke-NativePathRename $Source $Destination $SourceBoundary $DestinationBoundary -BeforeNativeRenameTestHook $BeforeNativeRenameTestHook
            return
        }
        catch {
            if (-not (Test-RetryableNativeRenameFailure $_.Exception)) { throw }
            if ($attempt -eq 8) {
                throw "Nao foi possivel mover $Label por handle apos $attempt tentativas: $($_.Exception.Message)"
            }
            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }
}

function Move-RegularFileToUniqueSibling(
    [string]$Path,
    [string]$Boundary,
    [string]$RetainedNamePrefix,
    [string]$Extension,
    [string]$Label,
    [scriptblock]$BeforeNativeRenameTestHook = $null) {
    if ([string]::IsNullOrWhiteSpace($RetainedNamePrefix) -or $RetainedNamePrefix -match '[\\/:]' -or
        $Extension -match '[\\/:]' -or ($Extension -and -not $Extension.StartsWith('.'))) {
        throw "Nome de retencao invalido para $Label."
    }
    $full = Get-CanonicalPath $Path
    $boundaryFull = Assert-RegularDirectoryPath $Boundary "Fronteira de retencao ($Label)"
    $parent = Get-CanonicalPath (Split-Path -Parent $full)
    Assert-AnchoredRegularDirectoryPath $parent $boundaryFull "Parent de retencao ($Label)" | Out-Null
    Assert-RegularFilePath $full "Arquivo para retencao ($Label)" | Out-Null
    $retained = Join-Path $parent ($RetainedNamePrefix + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + $Extension)
    Assert-AnchoredRegularDirectoryPath $retained $boundaryFull "Destino de retencao ($Label)" -AllowMissingTail | Out-Null
    Assert-PathEntryAbsent $retained "Destino de retencao ($Label)"
    Move-RegularFileWithRetry $full $retained $Label $boundaryFull $boundaryFull $BeforeNativeRenameTestHook
    Assert-RegularFilePath $retained "Arquivo retido ($Label)" | Out-Null
    return $retained
}

function Write-PromotionJournal(
    [string]$Journal,
    [string]$Canonical,
    [string]$Previous,
    [scriptblock]$BeforeNativeRenameTestHook = $null) {
    $journalFull = Get-CanonicalPath $Journal
    $journalParent = Split-Path -Parent $journalFull
    Assert-ExactPath $journalParent $ReleaseHistoryRoot 'Diretorio do diario de promocao' | Out-Null
    Assert-AnchoredRegularDirectoryPath $journalParent $WorkspaceRoot 'Diretorio do diario de promocao' | Out-Null
    Assert-PathEntryAbsent $journalFull 'Diario de promocao'
    $record = [ordered]@{
        schemaVersion = 1
        canonical = $Canonical
        previous = $(if ($Previous) { $Previous } else { '' })
        createdAt = (Get-Date -Format o)
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($record | ConvertTo-Json -Compress))
    $temporary = Join-Path $journalParent ((Split-Path -Leaf $journalFull) + '.tmp-' + $PID + '-' + [Guid]::NewGuid().ToString('N'))
    $stream = $null
    $published = $false
    try {
        # CreateNew prevents a concurrent writer from replacing this temporary
        # file.  Flush(true) makes its contents durable before the same-volume
        # rename exposes an all-or-nothing journal at the final path.
        $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
        $stream.Dispose()
        $stream = $null
        Assert-AnchoredRegularDirectoryPath $journalParent $WorkspaceRoot 'Diretorio do diario de promocao antes da publicacao' | Out-Null
        Assert-PathEntryAbsent $journalFull 'Diario de promocao antes da publicacao'
        Move-RegularFileWithRetry $temporary $journalFull 'diario de promocao atomico' $WorkspaceRoot $WorkspaceRoot $BeforeNativeRenameTestHook
        $published = $true
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
        if (-not $published) {
            # Never delete a path after releasing the source handle: a failed
            # atomic publication deliberately leaves its unique temp for audit.
            Write-Host "Publicacao do diario falhou; temporario preservado, se criado, em: $temporary" -ForegroundColor Yellow
        }
    }
}

function Move-PromotionJournalToRetention(
    [string]$Journal,
    [string]$HistoryRoot,
    [string]$State,
    [scriptblock]$BeforeNativeRenameTestHook = $null) {
    if ($State -notmatch '^[a-z0-9-]+$') { throw "Estado de retencao do diario invalido: $State" }
    $journalFull = Get-CanonicalPath $Journal
    $historyFull = Assert-ExactPath $HistoryRoot $ReleaseHistoryRoot 'Historico para retencao do diario'
    Assert-AnchoredRegularDirectoryPath $historyFull $WorkspaceRoot 'Historico para retencao do diario' | Out-Null
    $journalParent = Get-CanonicalPath (Split-Path -Parent $journalFull)
    if (-not [string]::Equals($journalParent, $historyFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Retencao recusada: diario fora do historico permitido: $journalFull"
    }
    return Move-RegularFileToUniqueSibling $journalFull $WorkspaceRoot ('PROMOCAO-v25.journal-' + $State) '.json' "diario para retencao ($State)" $BeforeNativeRenameTestHook
}

function Move-PromotionJournalToQuarantine(
    [string]$Journal,
    [string]$HistoryRoot,
    [scriptblock]$BeforeNativeRenameTestHook = $null) {
    return Move-PromotionJournalToRetention $Journal $HistoryRoot 'descartado' $BeforeNativeRenameTestHook
}

function Recover-InterruptedPromotion([string]$Canonical, [string]$HistoryRoot, [string[]]$ArtifactNames) {
    $canonicalFull = Assert-ExactPath $Canonical $CanonicalOutputRoot 'Destino canonico da recuperacao'
    $historyFull = Assert-ExactPath $HistoryRoot $ReleaseHistoryRoot 'Historico da recuperacao'
    Assert-AnchoredRegularDirectoryPath $canonicalFull $ProjectRoot 'Destino canonico da recuperacao' -AllowMissingTail | Out-Null
    Assert-AnchoredRegularDirectoryPath $historyFull $WorkspaceRoot 'Historico da recuperacao' -AllowMissingTail | Out-Null
    $journal = Join-Path $historyFull 'PROMOCAO-v25.em-andamento.json'
    $journalState = Get-PathEntryState $journal
    if (-not $journalState.Exists) { return }
    Assert-RegularDirectoryPath $historyFull 'Historico da recuperacao com diario' | Out-Null

    # Establish the canonical state independently of journal parsing.  A valid
    # canonical release is authoritative and lets us quarantine a torn journal
    # without blocking all future builds. Files and reparse points remain
    # fail-closed because moving either automatically would weaken path safety.
    $canonicalExists = (Get-PathEntryState $canonicalFull).Exists
    $canonicalValid = $false
    if ($canonicalExists) {
        Assert-AnchoredRegularDirectoryPath $canonicalFull $ProjectRoot 'Destino canonico existente da recuperacao' | Out-Null
        try {
            Test-ReleaseDirectory $canonicalFull $ArtifactNames | Out-Null
            $canonicalValid = $true
        }
        catch { $canonicalValid = $false }
    }

    Assert-RegularFilePath $journal 'Diario de promocao para recuperacao' | Out-Null

    $record = $null
    $previousFull = $null
    try {
        $record = Get-Content -LiteralPath $journal -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($record.schemaVersion -ne 1 -or
            -not [string]::Equals([string]$record.canonical, $canonicalFull, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'identidade inesperada no diario de promocao'
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$record.previous)) {
            $previousFull = Get-CanonicalPath ([string]$record.previous)
            if (-not $previousFull.StartsWith($historyFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw "release anterior fora do historico permitido: $previousFull"
            }
            Assert-AnchoredRegularDirectoryPath $previousFull $historyFull 'Release anterior registrada no diario' -AllowMissingTail | Out-Null
        }
    }
    catch {
        $journalError = $_.Exception.Message
        if ($canonicalValid) {
            $quarantined = Move-PromotionJournalToQuarantine $journal $historyFull
            Write-Host "Release canonica integra; diario interrompido foi isolado em: $quarantined" -ForegroundColor Yellow
            return
        }
        throw "Recuperacao recusada: diario de promocao invalido e release canonica indisponivel: $journal ($journalError)"
    }

    if ($canonicalValid) {
        $completedJournal = Move-PromotionJournalToRetention $journal $historyFull 'concluido-recuperacao'
        Write-Host "Promocao anterior ja estava concluida; diario retido em: $completedJournal" -ForegroundColor Yellow
        return
    }

    # A first publication has no previous release to restore.  If it was
    # interrupted, isolate any normal-but-incomplete canonical directory and
    # discard the journal so the clean build can safely start again.
    if (-not $previousFull) {
        if ($canonicalExists) {
            $rejected = Join-Path $historyFull ('recovery-first-publish-rejected-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
            Move-DirectoryWithRetry $canonicalFull $rejected 'primeira release canonica interrompida' $ProjectRoot $WorkspaceRoot
        }
        $quarantined = Move-PromotionJournalToQuarantine $journal $historyFull
        Write-Host "Primeira publicacao interrompida descartada com seguranca; novo build autorizado. Diario: $quarantined" -ForegroundColor Yellow
        return
    }

    if (-not (Test-Path -LiteralPath $previousFull)) {
        throw "Recuperacao recusada: release anterior registrada nao existe: $previousFull"
    }
    Assert-AnchoredRegularDirectoryPath $previousFull $historyFull 'Release anterior registrada' | Out-Null
    Test-ReleaseDirectory $previousFull $ArtifactNames | Out-Null
    if ($canonicalExists) {
        $rejected = Join-Path $historyFull ('recovery-rejected-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
        Move-DirectoryWithRetry $canonicalFull $rejected 'release canonica interrompida' $ProjectRoot $WorkspaceRoot
    }
    Move-DirectoryWithRetry $previousFull $canonicalFull 'release anterior apos interrupcao' $WorkspaceRoot $ProjectRoot
    Assert-AnchoredRegularDirectoryPath $canonicalFull $ProjectRoot 'Release canonica restaurada' | Out-Null
    Test-ReleaseDirectory $canonicalFull $ArtifactNames | Out-Null
    $completedJournal = Move-PromotionJournalToRetention $journal $historyFull 'concluido-recuperacao'
    Write-Host "Promocao interrompida recuperada: a release anterior completa voltou ao destino canonico. Diario: $completedJournal" -ForegroundColor Yellow
}

function Promote-ReleaseCandidate([string]$Candidate, [string]$Canonical, [string]$HistoryRoot, [string]$ExpectedInstallerHash, [string[]]$ArtifactNames) {
    $candidateFull = Assert-ExactPath $Candidate $CandidateOutputRoot 'Candidato da promocao'
    $canonicalFull = Assert-ExactPath $Canonical $CanonicalOutputRoot 'Destino canonico da promocao'
    $historyFull = Assert-ExactPath $HistoryRoot $ReleaseHistoryRoot 'Historico da promocao'
    Assert-AnchoredRegularDirectoryPath $candidateFull $BuildTempRoot 'Candidato da promocao' | Out-Null
    Assert-AnchoredRegularDirectoryPath $canonicalFull $ProjectRoot 'Destino canonico da promocao' -AllowMissingTail | Out-Null
    Assert-AnchoredRegularDirectoryPath $historyFull $WorkspaceRoot 'Historico da promocao' -AllowMissingTail | Out-Null
    $candidateDrive = [IO.Path]::GetPathRoot($candidateFull)
    $canonicalDrive = [IO.Path]::GetPathRoot($canonicalFull)
    $historyDrive = [IO.Path]::GetPathRoot($historyFull)
    if ((-not [string]::Equals($candidateDrive, $canonicalDrive, [StringComparison]::OrdinalIgnoreCase)) -or (-not [string]::Equals($historyDrive, $canonicalDrive, [StringComparison]::OrdinalIgnoreCase))) {
        throw "Promocao recusada: candidato, release canonica e historico devem estar no mesmo volume ($candidateDrive / $canonicalDrive / $historyDrive)."
    }
    if ([string]::Equals($candidateFull, $canonicalFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Promocao recusada: o candidato nao pode ser a pasta canonica.'
    }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $canonicalFull)) | Out-Null
    [IO.Directory]::CreateDirectory($historyFull) | Out-Null
    Assert-AnchoredRegularDirectoryPath $candidateFull $BuildTempRoot 'Candidato da promocao apos preparar destinos' | Out-Null
    Assert-AnchoredRegularDirectoryPath $canonicalFull $ProjectRoot 'Destino canonico da promocao apos preparar destinos' -AllowMissingTail | Out-Null
    Assert-AnchoredRegularDirectoryPath $historyFull $WorkspaceRoot 'Historico da promocao apos criacao' | Out-Null
    Test-ReleaseDirectory $candidateFull $ArtifactNames | Out-Null
    $candidateInstaller = Join-Path $candidateFull $InstallerFileName
    Assert-RegularFilePath $candidateInstaller 'Instalador candidato antes da promocao' | Out-Null
    if ((Get-FileHash -LiteralPath $candidateInstaller -Algorithm SHA256).Hash -ne $ExpectedInstallerHash) {
        throw 'Promocao recusada: hash do instalador candidato mudou antes da troca.'
    }

    $journal = Join-Path $historyFull 'PROMOCAO-v25.em-andamento.json'
    Assert-PathEntryAbsent $journal 'Diario pendente antes da promocao'
    $previous = $null
    if ((Get-PathEntryState $canonicalFull).Exists) {
        Assert-AnchoredRegularDirectoryPath $canonicalFull $ProjectRoot 'Release canonica anterior' | Out-Null
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $previous = Join-Path $historyFull ('p-' + $stamp + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
        Assert-AnchoredRegularDirectoryPath $previous $historyFull 'Destino da release canonica anterior' -AllowMissingTail | Out-Null
        Assert-PathEntryAbsent $previous 'Destino da release canonica anterior'
    }
    # Revalidate all publication boundaries immediately before making the
    # durable journal visible. No directory rename can begin from a path that
    # was replaced by a junction after the earlier integrity checks.
    Assert-AnchoredRegularDirectoryPath $candidateFull $BuildTempRoot 'Candidato imediatamente antes do diario' | Out-Null
    Assert-AnchoredRegularDirectoryPath $canonicalFull $ProjectRoot 'Destino canonico imediatamente antes do diario' -AllowMissingTail | Out-Null
    Assert-AnchoredRegularDirectoryPath $historyFull $WorkspaceRoot 'Historico imediatamente antes do diario' | Out-Null
    Test-ReleaseDirectory $candidateFull $ArtifactNames | Out-Null
    Write-PromotionJournal $journal $canonicalFull $previous
    $movedPrevious = $false
    $promoted = $false
    try {
        if ($previous) {
            try {
                Move-DirectoryWithRetry $canonicalFull $previous 'release canonica anterior' $ProjectRoot $WorkspaceRoot
                $movedPrevious = $true
            }
            catch {
                throw 'A pasta canonica nao pode ser trocada transacionalmente. Feche processos que a estejam usando e tente novamente; publicacao arquivo por arquivo e proibida.'
            }
        }
        Move-DirectoryWithRetry $candidateFull $canonicalFull 'candidato validado' $BuildTempRoot $ProjectRoot
        $promoted = $true
        Assert-AnchoredRegularDirectoryPath $canonicalFull $ProjectRoot 'Candidato promovido' | Out-Null
        Test-ReleaseDirectory $canonicalFull $ArtifactNames | Out-Null
        $promotedInstaller = Join-Path $canonicalFull $InstallerFileName
        Assert-RegularFilePath $promotedInstaller 'Instalador promovido' | Out-Null
        $promotedHash = (Get-FileHash -LiteralPath $promotedInstaller -Algorithm SHA256).Hash
        if ($promotedHash -ne $ExpectedInstallerHash) {
            throw 'Hash do instalador mudou durante a promocao da release.'
        }
        $completedJournal = Move-PromotionJournalToRetention $journal $historyFull 'concluido-promocao'
        Write-Host "Diario da promocao concluida retido em: $completedJournal" -ForegroundColor Yellow
    }
    catch {
        $promotionError = $_.Exception.Message
        $rejected = $null
        try {
            if ($promoted -and (Test-Path -LiteralPath $canonicalFull -PathType Container)) {
                $rejected = $candidateFull + '.rejected-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
                Move-DirectoryWithRetry $canonicalFull $rejected 'candidato rejeitado durante rollback' $ProjectRoot $BuildTempRoot
            }
            if ($movedPrevious -and $previous -and (Test-Path -LiteralPath $previous -PathType Container) -and -not (Test-Path -LiteralPath $canonicalFull)) {
                Move-DirectoryWithRetry $previous $canonicalFull 'release canonica restaurada' $WorkspaceRoot $ProjectRoot
            }
            if (Test-Path -LiteralPath $journal) {
                $abandonedJournal = Move-PromotionJournalToRetention $journal $historyFull 'abandonado-rollback'
                Write-Host "Diario do rollback retido em: $abandonedJournal" -ForegroundColor Yellow
            }
        }
        catch {
            throw "Falha critica ao reverter promocao da release: $promotionError | $($_.Exception.Message)"
        }
        throw "Falha ao promover a release; a versao anterior foi preservada: $promotionError"
    }
    return $previous
}

function Reset-DirectoryByQuarantine(
    [string]$Path,
    [string]$Boundary,
    [string]$Label,
    [scriptblock]$BeforeQuarantineRenameTestHook = $null,
    [scriptblock]$BeforeReplacementRenameTestHook = $null,
    [scriptblock]$BeforeReplacementCreateTestHook = $null) {
    $full = Get-CanonicalPath $Path
    $boundaryFull = Assert-RegularDirectoryPath $Boundary "Fronteira de limpeza ($Label)"
    Assert-AnchoredRegularDirectoryPath $full $boundaryFull "Alvo de limpeza ($Label)" -AllowMissingTail | Out-Null
    $parent = Get-CanonicalPath (Split-Path -Parent $full)
    Assert-AnchoredRegularDirectoryPath $parent $boundaryFull "Parent do alvo de limpeza ($Label)" | Out-Null

    $leaf = Split-Path -Leaf $full
    $quarantine = $null
    if ((Get-PathEntryState $full).Exists) {
        # A file, junction, symlink or any reparse component fails closed. The
        # directory is renamed as one object; none of its children are walked.
        Assert-AnchoredRegularDirectoryPath $full $boundaryFull "Alvo existente de limpeza ($Label)" | Out-Null
        $quarantine = Join-Path $parent ($leaf + '.preservado-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
        Assert-AnchoredRegularDirectoryPath $quarantine $boundaryFull "Quarentena de limpeza ($Label)" -AllowMissingTail | Out-Null
        Assert-PathEntryAbsent $quarantine "Quarentena de limpeza ($Label)"
        Move-DirectoryWithRetry $full $quarantine "alvo antigo de $Label para quarentena" $boundaryFull $boundaryFull $BeforeQuarantineRenameTestHook
        Assert-AnchoredRegularDirectoryPath $quarantine $boundaryFull "Quarentena criada ($Label)" | Out-Null
    }

    # NtCreateFile creates the random empty sibling relative to the already
    # pinned parent. The returned handle stays open and that same handle is
    # renamed to the final name; the replacement path is never reopened.
    Assert-AnchoredRegularDirectoryPath $parent $boundaryFull "Parent antes da reposicao ($Label)" | Out-Null
    $replacement = Join-Path $parent ($leaf + '.vazio-' + $PID + '-' + [Guid]::NewGuid().ToString('N'))
    Assert-AnchoredRegularDirectoryPath $replacement $boundaryFull "Diretorio vazio temporario ($Label)" -AllowMissingTail | Out-Null
    Assert-PathEntryAbsent $replacement "Diretorio vazio temporario ($Label)"
    try {
        Assert-AnchoredRegularDirectoryPath $parent $boundaryFull "Parent antes de promover diretorio vazio ($Label)" | Out-Null
        Assert-PathEntryAbsent $full "Alvo antes de promover diretorio vazio ($Label)"
        New-EmptyDirectoryByHandleAndPromote $replacement $full $boundaryFull $BeforeReplacementCreateTestHook $BeforeReplacementRenameTestHook
        Assert-AnchoredRegularDirectoryPath $full $boundaryFull "Alvo limpo recriado ($Label)" | Out-Null
    }
    catch {
        $preserved = $(if ($quarantine) { $quarantine } else { '(alvo anterior inexistente)' })
        throw "Reset seguro de $Label interrompido; nenhum delete recursivo foi executado. Quarentena: $preserved. Reposicao: $replacement. $($_.Exception.Message)"
    }

    if ($quarantine) {
        Write-Host "Conteudo anterior de $Label preservado sem travessia recursiva em: $quarantine" -ForegroundColor Yellow
    }
}

function Reset-GeneratedDirectory(
    [string]$Path,
    [scriptblock]$BeforeQuarantineRenameTestHook = $null,
    [scriptblock]$BeforeReplacementRenameTestHook = $null,
    [scriptblock]$BeforeReplacementCreateTestHook = $null) {
    $boundary = Get-GeneratedPathBoundary $Path
    Reset-DirectoryByQuarantine $Path $boundary 'pasta gerada' $BeforeQuarantineRenameTestHook $BeforeReplacementRenameTestHook $BeforeReplacementCreateTestHook
}

function Run([string]$File, [string[]]$Arguments, [string]$Directory = $ProjectRoot) {
    $loggedArguments = @($Arguments | ForEach-Object {
        if ($_ -like '-p:CommercialLicenseIssuerCertificateBase64=*') {
            '-p:CommercialLicenseIssuerCertificateBase64=[CERTIFICADO_PUBLICO_INCORPORADO]'
        }
        else { $_ }
    })
    Add-Content -LiteralPath $LogFile -Value ("Executando: " + (Split-Path -Leaf $File) + ' ' + ($loggedArguments -join ' ')) -Encoding UTF8
    Push-Location $Directory
    $previousErrorAction = $ErrorActionPreference
    try {
        # Ferramentas nativas como CMake escrevem mensagens informativas no
        # stderr mesmo quando retornam sucesso. No Windows PowerShell 5.1,
        # ErrorActionPreference=Stop transformava essas linhas em excecao e
        # interrompia uma compilacao valida antes de ler o exit code real.
        $ErrorActionPreference = 'Continue'
        & $File @Arguments 2>&1 | Tee-Object -FilePath $LogFile -Append | Out-Host
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
        Pop-Location
    }
    if ($code -ne 0) { throw "Comando retornou codigo ${code}: $File" }
}

function Invoke-FrontendSelfTest(
    [string]$Frontend,
    [string]$TestArgument = '--pix-agent-manager-self-test') {
    # The CMake target links against the locally versioned Windows runtimes;
    # they are intentionally not copied into the source bin directory.  Make
    # that exact set discoverable only for the smoke command, instead of
    # relying on an unrelated DLL installed on the build machine.
    $runtimeEntries = @(
        [pscustomobject]@{ Directory = (Join-Path $ProjectRoot 'win32-libs\FreeImage\x64'); File = 'FreeImage.dll' },
        [pscustomobject]@{ Directory = (Join-Path $ProjectRoot 'win32-libs\SDL2\x64'); File = 'SDL2.dll' },
        [pscustomobject]@{ Directory = (Join-Path $ProjectRoot 'win32-libs\SDL2_mixer\x64'); File = 'SDL2_mixer.dll' },
        [pscustomobject]@{ Directory = (Join-Path $ProjectRoot 'win32-libs\SDL2_mixer\x64\optional'); File = 'libogg-0.dll' },
        [pscustomobject]@{ Directory = (Join-Path $ProjectRoot 'win32-libs\curl\x64\bin'); File = 'libcurl.dll' },
        [pscustomobject]@{ Directory = (Join-Path $ProjectRoot 'win32-libs\libvlc\x64'); File = 'libvlc.dll' }
    )
    foreach ($entry in $runtimeEntries) {
        Require-File (Join-Path $entry.Directory $entry.File) "Runtime local do frontend ($($entry.File))"
    }
    $previousPath = $env:Path
    try {
        $env:Path = (($runtimeEntries.Directory + @($previousPath)) -join ';')
        Run $Frontend @($TestArgument)
    }
    finally {
        $env:Path = $previousPath
    }
}

function Find-VsTool([string]$Pattern) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    Require-File $vswhere 'vswhere.exe'
    $tool = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find $Pattern 2>$null | Select-Object -First 1
    Require-File $tool "Ferramenta Visual Studio ($Pattern)"
    return $tool
}

function Import-VsEnvironment([string]$VsDevCmd) {
    $lines = & $env:ComSpec /s /c "`"$VsDevCmd`" -no_logo -arch=x64 -host_arch=x64 >nul && set"
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel preparar o compilador C++ x64.' }
    $vsPath = $null
    foreach ($line in $lines) {
        $separator = $line.IndexOf('=')
        if ($separator -le 0) { continue }
        $name = $line.Substring(0, $separator)
        $value = $line.Substring($separator + 1)
        if ($name -ieq 'Path') { $vsPath = $value; continue }
        Set-Item -Path "Env:$name" -Value $value
    }
    if ([string]::IsNullOrWhiteSpace($vsPath)) { throw 'PATH do Visual Studio nao foi retornado.' }
    $env:Path = $vsPath
}

function Resolve-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kits -PathType Container) {
        $candidate = Get-ChildItem -LiteralPath $kits -Recurse -Filter signtool.exe -File -ErrorAction SilentlyContinue |
            Where-Object FullName -match '\\x64\\signtool\.exe$' |
            Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]'0.0' } } -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }
    throw 'signtool.exe nao foi localizado no Windows SDK.'
}

function Assert-MicrosoftSignedBuildTool([string]$Path, [string]$Label) {
	Require-File $Path $Label
	$signature = Get-AuthenticodeSignature -LiteralPath $Path
	if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
		-not $signature.SignerCertificate -or
		$signature.SignerCertificate.Subject -notmatch '(?i)(^|,\s*)O=Microsoft Corporation(,|$)') {
		throw "Ferramenta de build recusada: $Label nao possui assinatura valida da Microsoft ($Path)."
	}
}

function Initialize-CodeSigning {
    $script:SignTool = $null
    $script:SignCertificate = $null
    $script:SigningEnabled = $false
    $script:NormalizedSigningThumbprint = ''
    $script:LicenseIssuerCertificateBase64 = ''
	$script:AgentBundleSha256 = ''
    $thumbprint = if ($CertificadoThumbprint) { ($CertificadoThumbprint -replace '\s','').ToUpperInvariant() } else { '' }
    if ([string]::IsNullOrWhiteSpace($thumbprint)) {
        if ($ExigirAssinatura) {
            throw 'Assinatura obrigatoria, mas nenhum CertificadoThumbprint foi informado.'
        }
        Write-Host 'Assinatura Authenticode: desativada (nenhum certificado informado).' -ForegroundColor Yellow
        return
    }
    if ($thumbprint -notmatch '^[0-9A-F]{40}$') {
        throw 'CertificadoThumbprint deve conter os 40 digitos hexadecimais do certificado de assinatura.'
    }
    if ($ServidorCarimboDoTempo -and $ServidorCarimboDoTempo -notmatch '^https?://') {
        throw 'ServidorCarimboDoTempo deve ser uma URL HTTP ou HTTPS aprovada pela emissora do certificado.'
    }
    $certificatePath = "Cert:\$LocalCertificado\My\$thumbprint"
    $certificate = Get-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue
    if (-not $certificate -or -not $certificate.HasPrivateKey) {
        throw "Certificado de assinatura com chave privada nao encontrado em $certificatePath."
    }
    if ($ProtecaoComercial) {
        $now = Get-Date
        if ($certificate.NotBefore -gt $now -or $certificate.NotAfter -le $now) {
            throw 'ProtecaoComercial exige um certificado de assinatura dentro do periodo de validade.'
        }
        $codeSigningEku = @($certificate.EnhancedKeyUsageList | Where-Object {
            $_.ObjectId.Value -eq '1.3.6.1.5.5.7.3.3'
        })
        if ($codeSigningEku.Count -eq 0) {
            throw 'ProtecaoComercial exige certificado com finalidade explicita de assinatura de codigo.'
        }

        if ($ServidorAutoritativo) {
            $script:LicenseIssuerCertificateBase64 = ''
        }
        else {
        $licenseThumbprint = if ($CertificadoEmissorLicencaThumbprint) {
            ($CertificadoEmissorLicencaThumbprint -replace '\s','').ToUpperInvariant()
        } else { '' }
        if ($licenseThumbprint -notmatch '^[0-9A-F]{40}$') {
            throw 'ProtecaoComercial exige CertificadoEmissorLicencaThumbprint com 40 digitos hexadecimais.'
        }
        if ($licenseThumbprint -eq $thumbprint) {
            throw 'Use certificados diferentes para Authenticode e emissao de licencas PIX.'
        }
        $licenseCertificatePath = "Cert:\$LocalCertificadoEmissorLicenca\My\$licenseThumbprint"
        $licenseCertificate = Get-Item -LiteralPath $licenseCertificatePath -ErrorAction SilentlyContinue
        if (-not $licenseCertificate -or -not $licenseCertificate.HasPrivateKey) {
            throw "Certificado emissor de licencas com chave privada nao encontrado em $licenseCertificatePath."
        }
        if ($licenseCertificate.NotBefore -gt $now -or $licenseCertificate.NotAfter -le $now) {
            throw 'O certificado emissor de licencas esta fora do periodo de validade.'
        }
        $licenseRsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($licenseCertificate)
        $licenseEcdsa = [Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($licenseCertificate)
		$licenseCngKey = $null
		if ($licenseRsa -is [Security.Cryptography.RSACng]) { $licenseCngKey = $licenseRsa.Key }
		elseif ($licenseEcdsa -is [Security.Cryptography.ECDsaCng]) { $licenseCngKey = $licenseEcdsa.Key }
		if (-not $licenseCngKey) {
			throw 'O emissor comercial exige chave CNG nao exportavel em TPM, smart card, token ou HSM.'
		}
		$exportFlags = [Security.Cryptography.CngExportPolicies]::AllowExport -bor
			[Security.Cryptography.CngExportPolicies]::AllowPlaintextExport -bor
			[Security.Cryptography.CngExportPolicies]::AllowArchiving -bor
			[Security.Cryptography.CngExportPolicies]::AllowPlaintextArchiving
		if (($licenseCngKey.ExportPolicy -band $exportFlags) -ne 0) {
			throw 'A chave privada do emissor de licencas permite exportacao e foi recusada.'
		}
		if ($licenseCngKey.Provider.Provider -eq 'Microsoft Software Key Storage Provider') {
			throw 'A chave do emissor de licencas deve ficar em hardware (TPM, smart card, token ou HSM), nao no provedor de software.'
		}
        $licenseChallenge = New-Object byte[] 32
        $licenseRng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $licenseRng.GetBytes($licenseChallenge) }
        finally { $licenseRng.Dispose() }
        $licenseSignature = $null
        try {
            if ($licenseRsa) {
                if ($licenseRsa.KeySize -lt 2048) { throw 'O emissor RSA de licencas exige pelo menos 2048 bits.' }
                $licenseSignature = $licenseRsa.SignData($licenseChallenge,
                    [Security.Cryptography.HashAlgorithmName]::SHA256,
                    [Security.Cryptography.RSASignaturePadding]::Pkcs1)
                if (-not $licenseRsa.VerifyData($licenseChallenge, $licenseSignature,
                    [Security.Cryptography.HashAlgorithmName]::SHA256,
                    [Security.Cryptography.RSASignaturePadding]::Pkcs1)) {
                    throw 'A chave privada RSA do emissor de licencas falhou no teste de posse.'
                }
            }
            elseif ($licenseEcdsa) {
                if ($licenseEcdsa.KeySize -notin @(256,384,521)) { throw 'A curva ECDSA do emissor de licencas nao e permitida.' }
                $licenseSignature = $licenseEcdsa.SignData($licenseChallenge,
                    [Security.Cryptography.HashAlgorithmName]::SHA256)
                if (-not $licenseEcdsa.VerifyData($licenseChallenge, $licenseSignature,
                    [Security.Cryptography.HashAlgorithmName]::SHA256)) {
                    throw 'A chave privada ECDSA do emissor de licencas falhou no teste de posse.'
                }
            }
            else { throw 'O emissor de licencas deve usar RSA ou ECDSA.' }
        }
        finally {
            if ($licenseSignature) { [Array]::Clear($licenseSignature, 0, $licenseSignature.Length) }
            if ($licenseChallenge) { [Array]::Clear($licenseChallenge, 0, $licenseChallenge.Length) }
            if ($licenseRsa) { $licenseRsa.Dispose() }
            if ($licenseEcdsa) { $licenseEcdsa.Dispose() }
        }

        $publicCertificate = $licenseCertificate.Export(
            [Security.Cryptography.X509Certificates.X509ContentType]::Cert)
        try {
            if ($publicCertificate.Length -lt 256 -or $publicCertificate.Length -gt 65536) {
                throw 'O certificado publico do emissor de licenca possui tamanho invalido.'
            }
            $script:LicenseIssuerCertificateBase64 = [Convert]::ToBase64String($publicCertificate)
        }
        finally {
            if ($null -ne $publicCertificate) { [Array]::Clear($publicCertificate, 0, $publicCertificate.Length) }
        }
        }
    }
    $script:SignTool = Resolve-SignTool
	Assert-MicrosoftSignedBuildTool $script:SignTool 'SignTool do Windows SDK'
    $script:SignCertificate = $certificate
    $script:NormalizedSigningThumbprint = $thumbprint
    $script:SigningEnabled = $true
    Write-Host "Assinatura Authenticode: habilitada ($LocalCertificado / $thumbprint)." -ForegroundColor Green
}

function Sign-Binary([string]$Path) {
    if (-not $script:SigningEnabled) { return }
    Require-File $Path 'Arquivo para assinatura'
    $arguments = @('sign','/v','/fd','SHA256','/s','My','/sha1',$script:SignCertificate.Thumbprint)
    if ($LocalCertificado -eq 'LocalMachine') { $arguments += '/sm' }
    if ($ServidorCarimboDoTempo) { $arguments += @('/tr',$ServidorCarimboDoTempo,'/td','SHA256') }
    $arguments += $Path
    Run $script:SignTool $arguments
    Run $script:SignTool @('verify','/pa','/all',$Path)
}

function Sign-PowerShellArtifact([string]$Path) {
    if (-not $script:SigningEnabled) { return }
    Require-File $Path 'Script para assinatura'
    $parameters = @{
        FilePath = $Path
        Certificate = $script:SignCertificate
        HashAlgorithm = 'SHA256'
    }
    if ($ServidorCarimboDoTempo) { $parameters.TimestampServer = $ServidorCarimboDoTempo }
    $signature = Set-AuthenticodeSignature @parameters
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "A assinatura do script falhou: $Path ($($signature.StatusMessage))"
    }
}

function Copy-Tree([string]$Source, [string]$Destination) {
    Require-Directory $Source 'Pasta para copia'
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    & robocopy.exe $Source $Destination /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Falha ao copiar $Source" }
}

function Compile-Native([string]$SourceDirectory, [string]$BaseName, [string]$OutputName, [string[]]$Libraries) {
    $resource = Join-Path $NativeOutput ($BaseName + '.res')
    $object = Join-Path $NativeOutput ($BaseName + '.obj')
    $output = Join-Path $NativeOutput $OutputName
    Run $script:Rc @('/nologo', "/fo$resource", ($BaseName + '.rc')) $SourceDirectory
    $compileArguments = @('/nologo','/std:c++17','/utf-8','/EHsc','/O2','/W4')
    $linkArguments = @('/link','/SUBSYSTEM:WINDOWS')
    if ($ProtecaoComercial) {
        if ($script:SigningEnabled -and $script:NormalizedSigningThumbprint -notmatch '^[0-9A-F]{40}$') {
            throw 'Thumbprint normalizado ausente antes da compilacao nativa comercial.'
        }
		if ($script:AgentBundleSha256 -notmatch '^[0-9A-F]{64}$') {
			throw 'Manifesto SHA-256 do bundle PIX ausente antes da compilacao nativa comercial.'
		}
        $compileArguments += @('/GL', '/guard:cf', '/GS', '/sdl', '/Gy', '/Gw', '/Brepro',
            ('/DTURBORAMA_REQUIRE_SIGNED_PIX=' + $(if ($script:SigningEnabled) { '1' } else { '0' })),
			('/DTURBORAMA_PIX_BUNDLE_SHA256=' + $script:AgentBundleSha256))
        if ($script:SigningEnabled) {
            $compileArguments += ('/DTURBORAMA_PIX_SIGNER_THUMBPRINT=' + $script:NormalizedSigningThumbprint)
        }
        $linkArguments += @(
            '/LTCG', '/GUARD:CF', '/DYNAMICBASE', '/NXCOMPAT', '/HIGHENTROPYVA',
            '/CETCOMPAT', '/OPT:REF', '/OPT:ICF', '/INCREMENTAL:NO', '/Brepro'
        )
    }
    $arguments = $compileArguments + @("/Fo:$object","/Fe:$output",($BaseName + '.cpp'),$resource) + $Libraries + $linkArguments
    Run $script:Cl $arguments $SourceDirectory
    Require-File $output $OutputName
    return $output
}

function Copy-PrivateDotnet([string]$Dotnet, [string]$Destination) {
    $root = Split-Path -Parent $Dotnet
    $runtime = Get-ChildItem -LiteralPath (Join-Path $root 'shared\Microsoft.NETCore.App') -Directory |
        Where-Object Name -match '^8\.' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $runtime) { throw '.NET Runtime 8 x64 nao encontrado.' }
    $fxr = Join-Path $root ('host\fxr\' + $runtime.Name)
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    Copy-Item -LiteralPath $Dotnet -Destination (Join-Path $Destination 'dotnet.exe') -Force
    Copy-Tree $fxr (Join-Path $Destination ('host\fxr\' + $runtime.Name))
    Copy-Tree $runtime.FullName (Join-Path $Destination ('shared\Microsoft.NETCore.App\' + $runtime.Name))
}

function Assert-ExactAgentBuildTree([string]$Root) {
    $expected = @(
        'appsettings.json',
        'Microsoft.Win32.SystemEvents.dll',
        'QRCoder.dll',
        'runtimes\unix\lib\net6.0\System.Drawing.Common.dll',
        'runtimes\win\lib\net6.0\Microsoft.Win32.SystemEvents.dll',
        'runtimes\win\lib\net6.0\System.Drawing.Common.dll',
        'System.Drawing.Common.dll',
        'TurboRamaPixAgent.deps.json',
        'TurboRamaPixAgent.dll',
        'TurboRamaPixAgent.runtimeconfig.json'
    )
    $rootFull = Assert-RegularDirectoryPath $Root 'Saida fechada do agente PIX'
    $actual = @(Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force | ForEach-Object {
        $_.FullName.Substring($rootFull.Length).TrimStart('\')
    })
    [Array]::Sort($expected, [StringComparer]::OrdinalIgnoreCase)
    [Array]::Sort($actual, [StringComparer]::OrdinalIgnoreCase)
    $difference = @(Compare-Object -ReferenceObject $expected -DifferenceObject $actual -CaseSensitive)
    if ($difference.Count -ne 0) {
        throw "A saida do agente PIX diverge da allowlist comercial fechada: $($difference | Out-String)"
    }
}

function Assert-TrustedAgentPeFiles([string]$Root) {
    $rootFull = Assert-RegularDirectoryPath $Root 'Bundle do agente para assinatura'
    $vendorNames = @('TurboRamaPixAgent.dll','QRCoder.dll')
    foreach ($file in Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force | Where-Object {
        $_.Extension.ToLowerInvariant() -in @('.dll','.exe')
    }) {
        Assert-RegularSingleLinkFilePath $file.FullName "PE do bundle PIX ($($file.Name))" | Out-Null
        $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
        if (($vendorNames -contains $file.Name) -and -not $script:SigningEnabled) {
            continue
        }
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "Componente PE sem assinatura valida no bundle PIX: $($file.FullName)"
        }
        if ($vendorNames -contains $file.Name) {
            if (-not $signature.SignerCertificate -or
                $signature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $script:NormalizedSigningThumbprint) {
                throw "Componente TurboRama assinado por editor diferente do autorizado: $($file.FullName)"
            }
        }
        elseif (-not $signature.SignerCertificate -or
            $signature.SignerCertificate.Subject -notmatch '(?i)(^|,\s*)O=Microsoft Corporation(,|$)') {
            throw "Runtime privado nao foi assinado pela Microsoft: $($file.FullName)"
        }
    }
}

function Get-CommercialBundleDigest([string]$Root) {
    $rootFull = Assert-RegularDirectoryPath $Root 'Bundle para manifesto SHA-256'
    foreach ($directory in Get-ChildItem -LiteralPath $rootFull -Recurse -Directory -Force) {
        if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Bundle PIX contem pasta redirecionada: $($directory.FullName)"
        }
    }
    $records = @(Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force | ForEach-Object {
        Assert-RegularSingleLinkFilePath $_.FullName "Arquivo do manifesto PIX ($($_.Name))" | Out-Null
        [pscustomobject]@{
            Relative = $_.FullName.Substring($rootFull.Length).TrimStart('\').Replace('\','/').ToLowerInvariant()
            FullName = $_.FullName
        }
    })
    if ($records.Count -eq 0) { throw 'O bundle PIX esta vazio.' }
    $records = @($records | Sort-Object -Property Relative -CaseSensitive)
    for ($index = 1; $index -lt $records.Count; ++$index) {
        if ([string]::Equals($records[$index - 1].Relative, $records[$index].Relative, [StringComparison]::Ordinal)) {
            throw "Bundle PIX possui caminho duplicado: $($records[$index].Relative)"
        }
    }
    $tree = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($record in $records) {
            $pathBytes = [Text.Encoding]::UTF8.GetBytes($record.Relative)
            $fileHex = (Get-FileHash -LiteralPath $record.FullName -Algorithm SHA256).Hash
            $fileBytes = New-Object byte[] 32
            for ($offset = 0; $offset -lt 32; ++$offset) {
                $fileBytes[$offset] = [Convert]::ToByte($fileHex.Substring($offset * 2, 2), 16)
            }
            $zero = [byte[]]@(0)
            $newline = [byte[]]@(10)
            [void]$tree.TransformBlock($pathBytes, 0, $pathBytes.Length, $pathBytes, 0)
            [void]$tree.TransformBlock($zero, 0, 1, $zero, 0)
            [void]$tree.TransformBlock($fileBytes, 0, $fileBytes.Length, $fileBytes, 0)
            [void]$tree.TransformBlock($newline, 0, 1, $newline, 0)
            [Array]::Clear($fileBytes, 0, $fileBytes.Length)
        }
        [void]$tree.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        return [BitConverter]::ToString($tree.Hash).Replace('-','')
    }
    finally { $tree.Dispose() }
}

function Prepare-CommercialAgentBundle([string]$Dotnet) {
    $env:DOTNET_CLI_HOME = Join-Path $WorkRoot 'dotnet-home'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $offlineNuget = Join-Path $WorkspaceRoot 'NUGET-COMMERCIAL'
    if (Test-Path -LiteralPath $offlineNuget) { $env:NUGET_PACKAGES = $offlineNuget }
    Require-File (Join-Path (Split-Path -Parent $AgentProject) 'packages.lock.json') 'Lock de dependencias .NET'
    Run $Dotnet @('restore',$AgentProject,'--locked-mode','--ignore-failed-sources','-p:NuGetAudit=false')
    $arguments = @(
        'build',$AgentProject,'-c','Release','--no-restore','-o',$AgentOutput,'-p:NuGetAudit=false',
        '-p:DebugType=None','-p:DebugSymbols=false','-p:CommercialLicenseRequired=false'
    )
    Run $Dotnet $arguments
    Assert-CommercialPayloadTree $AgentOutput 'Saida do agente PIX'

    $agentSettingsOutput = Join-Path $AgentOutput 'appsettings.json'
    Require-File $AgentSettingsTemplate 'Template seguro do appsettings PIX'
    Copy-Item -LiteralPath $AgentSettingsTemplate -Destination $agentSettingsOutput -Force
    if ((Get-FileHash -LiteralPath $AgentSettingsTemplate -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $agentSettingsOutput -Algorithm SHA256).Hash) {
        throw 'appsettings.json distribuido diverge do template seguro versionado.'
    }
    $settingsJson = Get-Content -LiteralPath $agentSettingsOutput -Raw | ConvertFrom-Json
    if (-not $settingsJson.TurboRamaPix -or $settingsJson.TurboRamaPix.Provider -ne 'mock' -or
        $settingsJson.TurboRamaPix.ProductionEnabled -ne $false) {
        throw 'Template appsettings PIX nao esta em estado fail-closed (mock/producao desabilitada).'
    }
    $settingsText = Get-Content -LiteralPath $agentSettingsOutput -Raw
    if ($settingsText -match '(?i)access[_-]?token|client[_-]?secret|authorization\s*[:=]|private[_-]?key|password\s*[:=]') {
        throw 'Campo sensivel encontrado no appsettings distribuido.'
    }

    $appHost = Join-Path $AgentOutput 'TurboRamaPixAgent.exe'
    Assert-RegularSingleLinkFilePath $appHost 'Apphost descartavel do agente PIX' | Out-Null
    Remove-Item -LiteralPath $appHost -Force
    Assert-ExactAgentBuildTree $AgentOutput
    Sign-Binary (Join-Path $AgentOutput 'TurboRamaPixAgent.dll')
    Sign-Binary (Join-Path $AgentOutput 'QRCoder.dll')
    Copy-PrivateDotnet $Dotnet (Join-Path $AgentOutput 'runtime')
    Assert-CommercialPayloadTree $AgentOutput 'Bundle completo do agente PIX'
    Assert-TrustedAgentPeFiles $AgentOutput
    Run (Join-Path $AgentOutput 'runtime\dotnet.exe') @(
        (Join-Path $AgentOutput 'TurboRamaPixAgent.dll'),'--self-test','--bridge',(Join-Path $WorkRoot 'agent-self-test')) $AgentOutput
    $digest = Get-CommercialBundleDigest $AgentOutput
    if ($digest -notmatch '^[0-9A-F]{64}$') { throw 'O manifesto completo do agente PIX nao gerou SHA-256 valido.' }
    return $digest
}

function Resolve-Pinned7za {
    $vendored = Join-Path $InstallerSource 'vendor\7za.exe'
    Require-File $vendored '7za.exe versionado'
    $expected = '223B873C50380FE9A39F1A22B6ABF8D46DB506E1C08D08312902F6F3CD1F7AC3'
    $actual = (Get-FileHash -LiteralPath $vendored -Algorithm SHA256).Hash
    if ($actual -ne $expected) {
        throw "7za.exe versionado diverge do SHA-256 aprovado (esperado $expected; atual $actual)."
    }
    return $vendored
}

try {
    Enter-BuildLock
    Recover-InterruptedPromotion $CanonicalOutputRoot $ReleaseHistoryRoot $ReleaseArtifacts
    Require-Directory $ProjectRoot 'Projeto TurboRama'
    Require-File $AgentProject 'Projeto do agente PIX'
    Require-File $PackScript 'Empacotador comercial'
    Require-File $ThemePacker 'Empacotador deterministico do tema'
    Require-File $SevenZipLicense 'Licenca oficial do 7-Zip 24.09'
    Require-File $SevenZipCopying 'GNU LGPL 2.1 do 7-Zip 24.09'
    Require-File $SevenZipNotice 'Aviso de redistribuicao do 7-Zip 24.09'
    if ($Limpar) {
        Reset-GeneratedDirectory $WorkRoot
    }
    # A release promovida nunca reutiliza objetos CMake/Ninja. Isto torna a
    # afirmacao "build limpo" verificavel mesmo quando -Limpar nao foi passado.
    Reset-GeneratedDirectory $EsBuild
    Reset-GeneratedDirectory $EsOutput
    [IO.Directory]::CreateDirectory($WorkRoot) | Out-Null
    [IO.Directory]::CreateDirectory($EsBuild) | Out-Null
    # A failed candidate must never contaminate a later candidate, while the
    # canonical GERADO-v25 directory remains untouched until final promotion.
    foreach ($directory in @(
        $AgentOutput,
        $NativeOutput,
        $ArchiveRoot,
        $BundleRoot,
        $CandidateContainerRoot,
        $CandidateOutputRoot,
        (Join-Path $WorkRoot 'agent-self-test')
    )) {
        Reset-GeneratedDirectory $directory
    }
    Assert-RetiredRepairAbsent $ArchiveRoot 'Staging limpo'
    Assert-RetiredRepairAbsent $CandidateOutputRoot 'Candidato limpo'
    Set-Content -LiteralPath $LogFile -Value "TurboRama PIX v25 - CANDIDATO - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding UTF8

    Stage '1/9 - FERRAMENTAS'
    $dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
    $sevenZip = Resolve-Pinned7za
    $cmake = Find-VsTool 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    $ninja = Find-VsTool 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
    $vsDevCmd = Find-VsTool 'Common7\Tools\VsDevCmd.bat'
    Import-VsEnvironment $vsDevCmd
    $script:Cl = (Get-Command cl.exe -ErrorAction Stop).Source
    $script:Rc = (Get-Command rc.exe -ErrorAction Stop).Source
	Assert-MicrosoftSignedBuildTool $dotnet 'dotnet.exe'
	Assert-MicrosoftSignedBuildTool $cmake 'CMake do Visual Studio'
	Assert-MicrosoftSignedBuildTool $ninja 'Ninja do Visual Studio'
	Assert-MicrosoftSignedBuildTool $script:Cl 'compilador C++ do Visual Studio'
	Assert-MicrosoftSignedBuildTool $script:Rc 'Resource Compiler do Windows SDK'
    $git = (Get-Command git.exe -ErrorAction Stop).Source
    $sourceCommit = (& $git -C $RepoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) {
		throw 'O build comercial exige um commit Git identificavel.'
	}
    $sourceDirtyEntries = @(& $git -C $RepoRoot status --porcelain=v1 -uall)
    if ($LASTEXITCODE -ne 0) { throw 'git status falhou no build comercial.' }
    $sourceDirtyCount = $sourceDirtyEntries.Count
    if ($sourceDirtyCount -ne 0) {
		throw "O build comercial exige arvore Git limpa. Revise e confirme as alteracoes antes de assinar: $($sourceDirtyEntries -join '; ')"
	}
    $sourceTreeFingerprintAtStart = '(nao identificado)'
    $sourceStatusAtStart = $sourceDirtyEntries -join "`n"
    if ($sourceDirtyCount -ge 0) {
        $sourceTreeFingerprintAtStart = Get-GitWorkingTreeFingerprint $git $RepoRoot
    }
    # Registre os dois arquivos que governam o instalador antes de qualquer
    # compilacao e mantenha a identidade ate o fechamento da release. Assim o
    # relatorio nunca atribui o binario a bytes editados durante um build longo.
    $installerCppSource = Assert-RegularFilePath `
        (Join-Path $InstallerSource 'TurboRamaInstaller.cpp') 'Fonte C++ do instalador no inicio do build'
    $buildScriptSource = Assert-RegularFilePath $PSCommandPath 'Script PowerShell no inicio do build'
    Initialize-SmokeFileIdentity
    try {
        $script:InstallerCppSourcePin = [TurboRama.Build.SmokeFileIdentity]::OpenPinned($installerCppSource)
        $script:BuildScriptSourcePin = [TurboRama.Build.SmokeFileIdentity]::OpenPinned($buildScriptSource)
    }
    catch {
        Exit-BuildSourcePins
        throw
    }
    $installerCppIdentityAtStart = $script:InstallerCppSourcePin.Identity
    $buildScriptIdentityAtStart = $script:BuildScriptSourcePin.Identity
    $installerCppHashAtStart = $script:InstallerCppSourcePin.Sha256
    $buildScriptHashAtStart = $script:BuildScriptSourcePin.Sha256
    Initialize-CodeSigning

    Stage '2/9 - AGENTE PIX SELADO E EMULATIONSTATION'
    $script:AgentBundleSha256 = Prepare-CommercialAgentBundle $dotnet
    $cmakeArguments = @(
        '-S',$ProjectRoot,'-B',$EsBuild,'-G','Ninja','-DCMAKE_BUILD_TYPE=Release',
        ('-DCMAKE_MAKE_PROGRAM=' + ($ninja -replace '\\','/')),
        ('-DCMAKE_C_COMPILER=' + ($script:Cl -replace '\\','/')),
        ('-DCMAKE_CXX_COMPILER=' + ($script:Cl -replace '\\','/')),
        ('-DCMAKE_RC_COMPILER=' + ($script:Rc -replace '\\','/')),
		('-DTURBORAMA_OUTPUT_DIRECTORY=' + ($EsOutput -replace '\\','/')),
        ('-DTURBORAMA_COMMERCIAL_HARDENING=' + $(if ($ProtecaoComercial) { 'ON' } else { 'OFF' })),
        ('-DTURBORAMA_REQUIRE_SIGNED_PIX=' + $(if ($script:SigningEnabled) { 'ON' } else { 'OFF' })),
        ('-DTURBORAMA_PIX_SIGNER_THUMBPRINT=' + $(if ($script:SigningEnabled) { $script:NormalizedSigningThumbprint } else { '' })),
        ('-DTURBORAMA_PIX_BUNDLE_SHA256=' + $(if ($ProtecaoComercial) { $script:AgentBundleSha256 } else { '' }))
    )
    Run $cmake $cmakeArguments
    Run $cmake @('--build',$EsBuild,'--target','emulationstation','--parallel',([Math]::Max(1,[Environment]::ProcessorCount).ToString()))
    $esExe = Join-Path $EsOutput 'emulationstation.exe'
    Require-File $esExe 'emulationstation.exe'
    # Assinatura de editor e opcional. A autorizacao funcional ocorre somente
    # no servidor e nunca depende da confianca de certificados do Windows.
    Sign-Binary $esExe
    Invoke-FrontendSelfTest $esExe

    Stage '3/9 - REVALIDACAO DO BUNDLE SERVIDOR-AUTORITATIVO'
    if ((Get-CommercialBundleDigest $AgentOutput) -ne $script:AgentBundleSha256) {
        throw 'O bundle PIX mudou depois de o seu manifesto ser incorporado ao EmulationStation.'
    }
    Stage '4/9 - PROGRAMAS WINDOWS LZ GAMES'
    $ownerConfigurator = Compile-Native (Join-Path $ProjectRoot 'tools\TurboRamaPixOwnerConfigurator') 'TurboRamaPixOwnerConfigurator' 'CONFIGURAR-USER-TOKEN-PIX.exe' @('user32.lib','gdi32.lib','shell32.lib','comctl32.lib','advapi32.lib')
    $credentialEditor = Compile-Native (Join-Path $ProjectRoot 'tools\TurboRamaPixCredentialEditor') 'TurboRamaPixCredentialEditor' 'CONFIGURAR-ACCESS-TOKEN-PIX.exe' @('user32.lib','gdi32.lib','crypt32.lib','comdlg32.lib','shell32.lib','advapi32.lib','bcrypt.lib','credui.lib','ole32.lib','userenv.lib')
    $installer = Compile-Native $InstallerSource 'TurboRamaInstaller' 'TurboRamaInstaller.exe' @('user32.lib','shlwapi.lib','shell32.lib','advapi32.lib')
    $bootstrapper = Compile-Native $InstallerSource 'TurboRamaBootstrapper' 'TurboRamaBootstrapper.exe' @('user32.lib','bcrypt.lib')
    $guiTest = Start-Process -FilePath $ownerConfigurator -ArgumentList '--self-test' -Wait -PassThru
    if ($guiTest.ExitCode -ne 0) { throw "Autoteste do configurador retornou $($guiTest.ExitCode)." }
    $credentialTest = Start-Process -FilePath $credentialEditor -ArgumentList '--self-test' -Wait -PassThru
    if ($credentialTest.ExitCode -ne 0) { throw "Autoteste do editor de credencial retornou $($credentialTest.ExitCode)." }
    # Este autoteste cobre a matriz logica fail-closed, os pins de leitura e os
    # helpers de inspecao. Processos/servicos Windows IoT reais so podem ser
    # validados posteriormente no quiosque ou em clone controlado.
    $installerBoundaryTest = Start-Process -FilePath $installer -ArgumentList '--self-test' -Wait -PassThru
    if ($installerBoundaryTest.ExitCode -ne 0) { throw "Autoteste das fronteiras Windows IoT do instalador retornou $($installerBoundaryTest.ExitCode)." }
    $bootstrapperSecurityTest = Start-Process -FilePath $bootstrapper -ArgumentList '--self-test' -Wait -PassThru
    if ($bootstrapperSecurityTest.ExitCode -ne 0) { throw "Autoteste do staging do bootstrapper retornou $($bootstrapperSecurityTest.ExitCode)." }
    foreach ($binary in @(
        $ownerConfigurator,
        $credentialEditor,
        $installer
    )) {
        if (Test-Path -LiteralPath $binary -PathType Leaf) { Sign-Binary $binary }
    }

    Stage '5/9 - CONTEUDO SEM DADOS PRIVADOS'
    Reset-GeneratedDirectory $ArchiveRoot
    [IO.Directory]::CreateDirectory((Join-Path $ArchiveRoot 'pix-agent')) | Out-Null
    Copy-Item -LiteralPath $esExe -Destination (Join-Path $ArchiveRoot 'emulationstation.exe') -Force
    $thirdPartyNotices = Join-Path $ArchiveRoot 'THIRD-PARTY-NOTICES'
    [IO.Directory]::CreateDirectory($thirdPartyNotices) | Out-Null
    # Keep the exact, pinned decompressor with its notices inside the payload
    # as well as in the outer self-extracting bundle. This makes redistribution
    # auditable even when the outer package has already been unpacked.
    Copy-Item -LiteralPath $sevenZip -Destination (Join-Path $thirdPartyNotices '7za.exe') -Force
    foreach ($notice in @($SevenZipLicense,$SevenZipCopying,$SevenZipNotice)) {
        Copy-Item -LiteralPath $notice -Destination (Join-Path $thirdPartyNotices (Split-Path -Leaf $notice)) -Force
    }
    Copy-Tree $AgentOutput (Join-Path $ArchiveRoot 'pix-agent')
    if ((Get-FileHash -LiteralPath $AgentSettingsTemplate -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $ArchiveRoot 'pix-agent\appsettings.json') -Algorithm SHA256).Hash) {
        throw 'O payload alterou o appsettings seguro do agente PIX.'
    }
	if ((Get-CommercialBundleDigest (Join-Path $ArchiveRoot 'pix-agent')) -ne $script:AgentBundleSha256) {
		throw 'A copia para o payload alterou o manifesto completo do agente PIX.'
	}
    $forbidden = Get-ChildItem -LiteralPath $ArchiveRoot -Recurse -File | Where-Object { $_.Name -like 'secret.dat*' -or $_.Name -in @('bridge.key','owner-settings.json','.agent.lock') }
    if ($forbidden) { throw 'Arquivo privado encontrado no pacote. Empacotamento cancelado.' }
    foreach ($required in @('emulationstation.exe','pix-agent\TurboRamaPixAgent.dll','pix-agent\runtime\dotnet.exe','THIRD-PARTY-NOTICES\7za.exe','THIRD-PARTY-NOTICES\LICENSE-7ZIP-24.09.txt','THIRD-PARTY-NOTICES\COPYING-LGPL-2.1.txt','THIRD-PARTY-NOTICES\NOTICE-7ZIP-24.09.txt')) {
        Require-File (Join-Path $ArchiveRoot $required) "Conteudo obrigatorio ($required)"
    }
    Assert-CommercialPayloadTree $ArchiveRoot 'Staging do payload comercial'
    Assert-RetiredRepairAbsent $ArchiveRoot 'Staging do payload'

    Stage '6/9 - INSTALADOR UNICO (CANDIDATO)'
    Copy-Item -LiteralPath $sevenZip -Destination (Join-Path $BundleRoot '7za.exe') -Force
    Copy-Item -LiteralPath $installer -Destination (Join-Path $BundleRoot 'TurboRamaInstaller.exe') -Force
    Copy-Item -LiteralPath $bootstrapper -Destination (Join-Path $BundleRoot 'TurboRamaBootstrapper.exe') -Force
    $payload = Join-Path $BundleRoot 'payload-v25.7z'
    if ((Get-PathEntryState $payload).Exists) {
        $retainedPayload = Move-RegularFileToUniqueSibling $payload $BuildTempBoundary 'payload-v25.anterior' '.7z' 'payload anterior'
        Write-Host "Payload anterior inesperado foi retido em: $retainedPayload" -ForegroundColor Yellow
    }
    Assert-PathEntryAbsent $payload 'Payload antes da compactacao'
    Run $sevenZip @('a','-t7z',$payload,'.\*','-mx=9','-mmt=on','-y') $ArchiveRoot
    Run powershell.exe @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$PackScript,'-Bootstrapper',(Join-Path $BundleRoot 'TurboRamaBootstrapper.exe'),'-Installer',(Join-Path $BundleRoot 'TurboRamaInstaller.exe'),'-SevenZip',(Join-Path $BundleRoot '7za.exe'),'-Payload',$payload,'-Output',$FinalInstaller,'-DiretorioTemporarioBuild',$BuildTempBoundary) $OutputRoot
    Sign-Binary $FinalInstaller
    Copy-Item -LiteralPath $ownerConfigurator -Destination (Join-Path $OutputRoot 'CONFIGURAR-USER-TOKEN-PIX.exe') -Force
    Copy-Item -LiteralPath $credentialEditor -Destination (Join-Path $OutputRoot 'CONFIGURAR-ACCESS-TOKEN-PIX.exe') -Force
    Copy-Item -LiteralPath $sevenZip -Destination (Join-Path $OutputRoot '7za.exe') -Force
    foreach ($notice in @($SevenZipLicense,$SevenZipCopying,$SevenZipNotice)) {
        Copy-Item -LiteralPath $notice -Destination (Join-Path $OutputRoot (Split-Path -Leaf $notice)) -Force
    }

    Stage '7/9 - INTEGRIDADE DO CANDIDATO'
    Run (Join-Path $BundleRoot '7za.exe') @('t',$payload) $BundleRoot
    $hash = (Get-FileHash -LiteralPath $FinalInstaller -Algorithm SHA256).Hash
    $vendorHash = (Get-FileHash -LiteralPath $sevenZip -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath (Join-Path $OutputRoot '7za.exe') -Algorithm SHA256).Hash -ne $vendorHash) {
        throw 'A copia distribuida de 7za.exe nao corresponde ao binario versionado e pinado.'
    }
    Assert-ArchiveEntries $sevenZip $payload @(
        'THIRD-PARTY-NOTICES\7za.exe',
        'THIRD-PARTY-NOTICES\LICENSE-7ZIP-24.09.txt',
        'THIRD-PARTY-NOTICES\COPYING-LGPL-2.1.txt',
        'THIRD-PARTY-NOTICES\NOTICE-7ZIP-24.09.txt'
    ) @($RetiredRepairFileName,'CONFIGURAR-USER-TOKEN-PIX.exe','CONFIGURAR-ACCESS-TOKEN-PIX.exe')
    Assert-CommercialArchiveHygiene $sevenZip $payload
    Assert-RetiredRepairAbsent $OutputRoot 'Entrega candidata'
    $instructions = @'
TURBORAMA / LZ GAMES - CONFIGURAÇÃO COMERCIAL PIX v25

STATUS DESTA ENTREGA
- Candidato interno para validação técnica. Não liberar, ofertar ou vender até
  existir uma política de ACL do Windows IoT comprovada sem alterar Launcher,
  Factory Pack, cache, sessões ou credenciais existentes.

INSTALAÇÃO
1. Entre no modo manutenção pelo fluxo oficial do TurboRama.
2. Execute INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe.
3. Ative a licença comercial vinculada ao TPM conforme a seção abaixo.
4. Entre na conta Windows configurada no turborama.json e no AutoLogon
   do gabinete. No gabinete atual essa conta é Admin. Abra o
   CONFIGURAR-USER-TOKEN-PIX.exe no layout selecionado:
   - flat: D:\emulationstation\CONFIGURAR-USER-TOKEN-PIX.exe
   - clássico: D:\Turborama\emulationstation\CONFIGURAR-USER-TOKEN-PIX.exe
5. Escolha Mercado Pago ou Outro banco / Adaptador.
6. Para Mercado Pago, cole somente o Access Token. Não use Public Key,
   Client ID, Client Secret nem ID da aplicação no lugar do Access Token.
7. Informe estabelecimento, caixa, CEP, número, referência e preços.
8. Clique em VALIDAR E ATIVAR PIX.

ATIVAÇÃO COMERCIAL VINCULADA AO TPM
- Feche o EmulationStation e execute o agente na própria conta Windows do
  quiosque com --license-request CAMINHO\pedido.json. Use o dotnet.exe privado
  e TurboRamaPixAgent.dll da pasta pix-agent instalada.
- No computador privado de compilação, emita a licença com o projeto
  tools\TurboRamaPixLicenseIssuer. Ele exige o certificado exclusivo do emissor
  de licenças, diferente do Authenticode, com chave privada no token ou HSM;
  a chave nunca é exportada.
- De volta ao quiosque, execute o agente com
  --install-license CAMINHO\quiosque.license e confirme com --license-status.
- O pedido de ativação contém somente a chave pública/fingerprint do TPM. Não
  contém Access Token, Client Secret, senha ou qualquer credencial do cliente.
- Copiar executáveis, secret.dat e a licença para outro computador não libera
  novas cobranças: a prova da chave TPM original é refeita antes de cobrar.

ESCOPO WINDOWS IOT E MODO MANUTENÇÃO
- Este atualizador interno exige uma pasta .emulationstation\pix preexistente e
  atualiza somente a camada EmulationStation/PIX no layout flat
  (D:\emulationstation) ou clássico (D:\Turborama\emulationstation).
  C:\TurboRama e o Factory Pack ficam integralmente fora do escopo e não são
  reparados, reconfigurados ou substituídos.
- O Launcher, turborama.json, o wrapper TurboRama.exe e todo o cache
  .emulationstation\.runtime são preservados sem alteração.
- Antes de instalar, entre obrigatoriamente no modo manutenção pelo fluxo
  TurboRama. O instalador recusa a operação se maintenance.lock estiver ausente
  ou inseguro e encerra somente os processos dos caminhos exatos envolvidos se
  algum deles ainda estiver ativo; serviços e tarefas não são reconfigurados.
- Não crie maintenance.lock manualmente. Use o menu/serviço Maintenance para
  parar o Launcher e estabelecer o bloqueio operacional correto.

IDENTIDADE WINDOWS / DPAPI
- O token deve ser cadastrado pelo mesmo SID configurado no turborama.json e
  no AutoLogon. No gabinete atual esse SID é da conta Admin.
- Se a instalação antiga tiver secret.dat criado por outra identidade ou o
  agente pedir recadastro, use a conta configurada do gabinete e informe
  novamente o Access Token; nunca copie nem tente descriptografar o segredo anterior.

CEP E ENDEREÇO
- O proprietário informa somente CEP e número/complemento.
- O programa consulta fontes redundantes para obter rua, cidade, estado e a
  localização exigida internamente pela API do Mercado Pago.
- Não existe campo de latitude/longitude para o usuário preencher.
- O endereço confirmado fica em cache; falhas temporárias podem ser retomadas
  sem digitar novamente o cadastro.
- Uma localização recusada pelo provedor não é reutilizada indefinidamente.
- OpenStreetMap/Nominatim é usado somente como último recurso, com cache e
  limite de requisições. Uma instância própria pode ser configurada pela
  variável TURBORAMA_PIX_NOMINATIM_BASE_URL.

CONTA, LOJA E PDV
- O User ID real é identificado pelo próprio Access Token.
- Loja e PDV existentes são reaproveitados sem duplicação.
- Ao trocar de conta ou mudar de teste para produção, o programa prepara Loja
  e PDV vinculados à nova credencial; recursos do sandbox não servem em produção.

SEGURANÇA
- O Access Token é protegido pelo Windows e não entra no instalador ou JSON.
- No perfil -ProtecaoComercial, o cofre usa DPAPI, AES-256-GCM e embrulha a
  chave de dados por RSA-OAEP-SHA256 na chave privada não exportável do TPM.
  Cada nova cobrança também exige licença offline assinada para a máquina.
- Todos os arquivos do agente/runtime, inclusive deps.json, runtimeconfig.json
  e dependências RID, entram em um manifesto SHA-256 incorporado nos programas
  nativos assinados. Arquivo alterado, extra ou ausente bloqueia o agente.
- O runtime privado inicia com ambiente reduzido; startup hooks, dependências
  adicionais, stores externos, profiler e fallback para .NET global são recusados.
- Fontes, projetos, testes, scripts e PDBs são recusados no pacote comercial.
- Os autotestes são locais e simulados; não criam cobrança nem movimentam dinheiro.
- Credenciais que já foram publicadas devem ser revogadas e substituídas.
- Consulte ASSINATURA-AUTHENTICODE.txt. Builds oficiais devem usar
  -ProtecaoComercial com certificado real de assinatura de código.

DOCUMENTAÇÃO OFICIAL CONSULTADA
https://www.mercadopago.com.br/developers/pt/docs/qr-code/create-store-and-pos
https://www.mercadopago.com.br/developers/pt/docs/qr-code/go-to-production
https://docs.awesomeapi.com.br/api-cep/api-busca-de-enderecos
https://operations.osmfoundation.org/policies/nominatim/
'@
	# Instrucoes finais do modelo servidor-autoritativo. O bloco historico acima
	# permanece somente como contexto de migracao e nunca e publicado.
	$instructions = @'
TURBORAMA / LZ GAMES - PIX COM AUTORIZACAO ONLINE v25

CONTEUDO
- O instalador atualiza somente emulationstation.exe e a pasta pix-agent.
- CONFIGURAR-ACCESS-TOKEN-PIX.exe e CONFIGURAR-USER-TOKEN-PIX.exe sao programas
  portateis do administrador. Eles nao sao instalados no gabinete.

ORDEM DE CONFIGURACAO
1. Entre no modo de manutencao oficial e execute o instalador PIX.
2. No painel LZ Games, crie a licenca e gere o codigo de ativacao da maquina.
3. Execute CONFIGURAR-ACCESS-TOKEN-PIX.exe, informe a licenca e o codigo de
   ativacao e confirme o cadastro da maquina no servidor.
4. No painel, gere o codigo bancario de uso unico do cliente. Ele vale por
   15 minutos.
5. Execute CONFIGURAR-USER-TOKEN-PIX.exe, informe Cliente ID, codigo bancario
   e Access Token. Consulte e escolha o PDV real da conta Mercado Pago.
6. Depois da confirmacao, feche e remova os dois programas administrativos do
   computador. Somente EmulationStation e pix-agent permanecem no gabinete.

MODELO DE SEGURANCA
- A autorizacao funcional vem do servidor LZ Games e da prova criptografica da
  maquina. Authenticode e opcional e nunca libera o sistema.
- O mesmo cliente pode usar TPM, token criptografico ou SOFTWARE_BOUND_ONLINE,
  conforme a capacidade da maquina e a politica da licenca.
- Access Token e Client Secret do Mercado Pago ficam somente cifrados no
  servidor. Eles nao sao gravados no kiosk nem retornam ao agente.
- Cada nova cobranca exige autorizacao online; nao existe fallback local para
  criar PIX. Sem internet, somente novas cobrancas PIX ficam indisponiveis.
  EmulationStation, jogos, precos e creditos locais continuam funcionando.
- O servidor guarda uma unica conexao Mercado Pago ativa por cliente. Um novo
  cadastro confirmado substitui a conexao anterior desse cliente.

LIMITES DESTA ENTREGA
- Autotestes locais nao movimentam dinheiro. Uma cobranca real so pode ser
  declarada aprovada depois da publicacao do servidor e de confirmacao humana
  do pagamento na conta Mercado Pago.
- Revogue toda credencial que tenha sido publicada em conversa, log ou
  repositorio e mantenha os repositorios comerciais privados.
'@
    Set-Content -LiteralPath (Join-Path $OutputRoot 'COMO-CONFIGURAR-O-PIX.txt') -Value $instructions -Encoding UTF8

    Stage '8/9 - INSTALACAO ISOLADA DO CANDIDATO'
    if (-not $TestarInstalador) {
        throw 'A release canonica nao sera promovida sem -TestarInstalador; o candidato permanece isolado na area temporaria protegida do usuario.'
    }
	if ($TestarInstalador) {
        Reset-IsolatedSmokeRoot
		$smoke = Join-Path $SmokeRoot 'install'
		[IO.Directory]::CreateDirectory($smoke) | Out-Null

		# O conjunto anterior usa bytes diferentes do candidato. Assim, o teste de
		# rollback detecta qualquer mistura entre a versao antiga e a nova.
		Set-Content -LiteralPath (Join-Path $smoke 'emulationstation.exe') -Value 'legacy-emulationstation-fixture' -Encoding ASCII
		foreach ($legacyFile in @('CONFIGURAR-USER-TOKEN-PIX.exe','CONFIGURAR-ACCESS-TOKEN-PIX.exe')) {
			Set-Content -LiteralPath (Join-Path $smoke $legacyFile) -Value "legacy-$legacyFile" -Encoding ASCII
		}
		[IO.Directory]::CreateDirectory((Join-Path $smoke 'pix-agent\legacy-nested')) | Out-Null
		Set-Content -LiteralPath (Join-Path $smoke 'pix-agent\legacy-agent.marker') -Value 'legacy-agent' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smoke 'pix-agent\legacy-nested\preserve-unknown.dat') -Value 'legacy-agent-nested' -Encoding ASCII

		# Cache do frontend: pertence ao Factory Pack/EmulationStation existente e
		# fica integralmente fora do escopo comercial, inclusive ACLs.
		$smokeRuntimeCache = Join-Path $smoke '.emulationstation\.runtime'
		[IO.Directory]::CreateDirectory((Join-Path $smokeRuntimeCache 'nested')) | Out-Null
		Set-Content -LiteralPath (Join-Path $smokeRuntimeCache 'legacy-theme.marker') -Value 'legacy-theme' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeRuntimeCache 'nested\cache.bin') -Value 'runtime-cache-must-not-change' -Encoding ASCII
		$smokeThemes = Join-Path $smoke '.emulationstation\themes\baseline-theme'
		[IO.Directory]::CreateDirectory($smokeThemes) | Out-Null
		Set-Content -LiteralPath (Join-Path $smokeThemes 'theme.xml') -Value '<theme>must-not-change</theme>' -Encoding ASCII
		$smokeCreditFiles = @(
			(Join-Path $smoke '.emulationstation\arcade_credit.cfg'),
			(Join-Path $smoke '.emulationstation\arcade_credit.dat'),
			(Join-Path $smoke '.emulationstation\arcade_players.dat')
		)
		foreach ($creditFile in $smokeCreditFiles) {
			Set-Content -LiteralPath $creditFile -Value "credit-must-not-change-$([IO.Path]::GetFileName($creditFile))" -Encoding ASCII
		}

		# Estado mutavel pelo instalador, capturado separadamente para exercitar o
		# rollback posterior a reset/identidade sem jamais incluir credenciais no
		# backup transacional.
		$smokePixRoot = Join-Path $smoke '.emulationstation\pix'
		[IO.Directory]::CreateDirectory($smokePixRoot) | Out-Null
		$pixTransactionalStateNames = @(
			'credential-agent-key.dat',
			'agent-public-key.pem',
			'credential-update.json',
			'credential-update-status.json',
			'credential-replay.dat',
			'agent-status.json',
			'owner-setup-status.json',
			'public-options.json',
			'kiosk-identity.sid',
			'owner-reenrollment-required.json',
			'agent-stop.request'
		)
		foreach ($stateName in $pixTransactionalStateNames) {
			Set-Content -LiteralPath (Join-Path $smokePixRoot $stateName) -Value "legacy-pix-state-$stateName" -Encoding ASCII
		}
		$pixCredentialNames = @('secret.dat','bridge.key','owner-settings.json')
		foreach ($credentialName in $pixCredentialNames) {
			Set-Content -LiteralPath (Join-Path $smokePixRoot $credentialName) -Value "private-credential-must-not-change-$credentialName" -Encoding ASCII
		}
		$smokePixLogs = Join-Path $smokePixRoot 'logs'
		$smokePixSessions = Join-Path $smokePixRoot 'sessions'
		[IO.Directory]::CreateDirectory($smokePixLogs) | Out-Null
		[IO.Directory]::CreateDirectory($smokePixSessions) | Out-Null
		Set-Content -LiteralPath (Join-Path $smokePixLogs 'historical.log') -Value 'pix-log-must-not-change' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokePixSessions 'historical.session') -Value 'pix-session-must-not-change' -Encoding ASCII

		$smokeRuntimeLog = Join-Path $smoke 'frontend.log'
		$smokeVersionInfo = Join-Path $smoke 'version.info'
		$smokeRoms = Join-Path $smoke 'roms'
		[IO.Directory]::CreateDirectory($smokeRoms) | Out-Null
		Set-Content -LiteralPath $smokeRuntimeLog -Value 'legacy-runtime-log' -Encoding ASCII
		Set-Content -LiteralPath $smokeVersionInfo -Value 'legacy-version-info' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeRoms 'sentinel.dat') -Value 'roms-must-not-change' -Encoding ASCII

		$legacyFiles = @('emulationstation.exe','CONFIGURAR-USER-TOKEN-PIX.exe','CONFIGURAR-ACCESS-TOKEN-PIX.exe','pix-agent\legacy-agent.marker')
		$legacyHashes = @{}
		foreach ($legacyFile in $legacyFiles) {
			$legacyHashes[$legacyFile] = (Get-FileHash -LiteralPath (Join-Path $smoke $legacyFile) -Algorithm SHA256).Hash
		}

		function Get-DirectoryTreeFingerprint([string]$TreeRoot, [string]$Prefix) {
			if (-not (Test-Path -LiteralPath $TreeRoot -PathType Container)) {
				return @("MISSING|$Prefix")
			}
			$treeEntries = [Collections.Generic.List[string]]::new()
			$treeFull = [IO.Path]::GetFullPath($TreeRoot).TrimEnd('\')
			foreach ($item in @(Get-ChildItem -LiteralPath $TreeRoot -Force -Recurse | Sort-Object FullName)) {
				$relative = $Prefix + $item.FullName.Substring($treeFull.Length)
				if ($item.PSIsContainer) { $treeEntries.Add("DIR|$relative") }
				else { $treeEntries.Add("FILE|$relative|$((Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash)") }
			}
			return @($treeEntries | Sort-Object)
		}

		function Get-SmokeAclMetadata([System.IO.FileSystemInfo]$Item) {
			$acl = Get-Acl -LiteralPath $Item.FullName
			return "attrs=$([int]$Item.Attributes)|owner=$($acl.Owner)|sddl=$($acl.Sddl)"
		}

		function Get-SmokeInstallTreeFingerprint([string]$TreeRoot, [string]$Prefix) {
			if (-not (Test-Path -LiteralPath $TreeRoot -PathType Container)) {
				return @("MISSING|$Prefix")
			}
			$treeEntries = [Collections.Generic.List[string]]::new()
			$treeFull = [IO.Path]::GetFullPath($TreeRoot).TrimEnd('\')
			$rootItem = Get-Item -LiteralPath $TreeRoot -Force
			foreach ($item in (@($rootItem) + @(Get-ChildItem -LiteralPath $TreeRoot -Force -Recurse | Sort-Object FullName))) {
				$relative = if ([string]::Equals($item.FullName.TrimEnd('\'), $treeFull, [StringComparison]::OrdinalIgnoreCase)) {
					$Prefix
				}
				else { $Prefix + $item.FullName.Substring($treeFull.Length) }
				$metadata = Get-SmokeAclMetadata $item
				if ($item.PSIsContainer) { $treeEntries.Add("DIR|$relative|$metadata") }
				else {
					$hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
					$treeEntries.Add("FILE|$relative|$hash|$metadata")
				}
			}
			return @($treeEntries | Sort-Object)
		}

		function Get-SmokeInstallSetFingerprint([string]$Root) {
			$entries = [Collections.Generic.List[string]]::new()
			foreach ($relative in @('emulationstation.exe','CONFIGURAR-USER-TOKEN-PIX.exe','CONFIGURAR-ACCESS-TOKEN-PIX.exe')) {
				$path = Join-Path $Root $relative
				if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $entries.Add("MISSING|$relative"); continue }
				$item = Get-Item -LiteralPath $path -Force
				$entries.Add("FILE|$relative|$((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)|$(Get-SmokeAclMetadata $item)")
			}
			foreach ($entry in @(Get-SmokeInstallTreeFingerprint (Join-Path $Root 'pix-agent') 'pix-agent')) { $entries.Add($entry) }
			return @($entries | Sort-Object)
		}

		function Assert-SmokeInstallSetUnchanged([string[]]$Expected, [string]$Label) {
			$current = @(Get-SmokeInstallSetFingerprint $smoke)
			$delta = @(Compare-Object -ReferenceObject $Expected -DifferenceObject $current)
			if ($delta.Count -ne 0) {
				throw "$Label nao restaurou integralmente conteudo, atributos, owner e SDDL de frontend, agente e utilitarios: $($delta -join '; ')"
			}
		}

		$legacyInstallSetFingerprint = @(Get-SmokeInstallSetFingerprint $smoke)
		$expectedAgentTreeFingerprint = @(Get-DirectoryTreeFingerprint (Join-Path $ArchiveRoot 'pix-agent') 'pix-agent')

		$kioskUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
		if ([string]::IsNullOrWhiteSpace($kioskUser)) {
			throw 'Nao foi possivel obter a identidade Windows para o smoke test.'
		}
		try {
			$account = New-Object System.Security.Principal.NTAccount($kioskUser)
			$currentKioskSid = [string]($account.Translate([System.Security.Principal.SecurityIdentifier]).Value)
		}
		catch {
			throw "A identidade Windows do smoke test nao pode ser resolvida: $kioskUser"
		}
		$previousKioskSid = if ($currentKioskSid -eq 'S-1-5-18') { 'S-1-5-19' } else { 'S-1-5-18' }
		Set-Content -LiteralPath (Join-Path $smokePixRoot 'kiosk-identity.sid') -Value $previousKioskSid -Encoding ASCII

		# Factory Pack/Launcher fica fora do target do instalador. O arquivo usado
		# pelo guard de processo e um fixture regular, nunca um processo real.
		$smokeLauncherBase = Join-Path $SmokeRoot 'Launcher'
		$smokeLauncherRoot = Join-Path $smokeLauncherBase 'Config'
		$smokeLauncherData = Join-Path $smokeLauncherBase 'Data'
		foreach ($directory in @(
			(Join-Path $smokeLauncherBase 'App\Launcher'),
			(Join-Path $smokeLauncherBase 'App\Maintenance'),
			(Join-Path $smokeLauncherBase 'App\Watchdog'),
			$smokeLauncherRoot,
			(Join-Path $smokeLauncherBase 'Logs\Launcher'),
			(Join-Path $smokeLauncherBase 'Logs\Services'),
			(Join-Path $smokeLauncherBase 'State'),
			$smokeLauncherData)) {
			[IO.Directory]::CreateDirectory($directory) | Out-Null
		}
		$smokeLauncherProcess = Join-Path $smokeLauncherBase 'App\Launcher\TurboRama.Launcher.exe'
		Set-Content -LiteralPath $smokeLauncherProcess -Value 'launcher-process-fixture' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'App\Maintenance\TurboRama.Maintenance.exe') -Value 'maintenance-fixture' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'App\Maintenance\TurboRama.Maintenance.dll') -Value 'maintenance-library-fixture' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'App\Watchdog\TurboRama.Watchdog.exe') -Value 'watchdog-fixture' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'App\Watchdog\TurboRama.Watchdog.dll') -Value 'watchdog-library-fixture' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherRoot 'kiosk-user.secret') -Value 'protected-sibling-must-not-change' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'Logs\Launcher\launcher.log') -Value 'launcher-log-fixture' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'Logs\Services\service.log') -Value 'service-log-fixture' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'Logs\security-agent-alive.txt') -Value 'alive-fixture' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'Logs\security-agent-health.txt') -Value 'health-fixture' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'Logs\force-keyboard-filter-boot.bat') -Value '@echo off' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'Logs\post-reboot-wekf.bat') -Value '@echo off' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'Logs\post-reboot-wekf.ps1') -Value 'Write-Host smoke' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherBase 'State\installation-state.json') -Value '{}' -Encoding ASCII
		Set-Content -LiteralPath (Join-Path $smokeLauncherData 'untouched.dat') -Value 'launcher-data-must-not-change' -Encoding ASCII

		# O wrapper flat fica fora do target ES e continua sendo o frontend do JSON.
		$smokeFlatWrapper = Join-Path $SmokeRoot 'TurboRama.exe'
		Set-Content -LiteralPath $smokeFlatWrapper -Value 'flat-wrapper-must-not-change' -Encoding ASCII
		$smokeLauncher = Join-Path $smokeLauncherRoot 'turborama.json'
		[ordered]@{
			kioskUser = $kioskUser
			frontendExecutable = $smokeFlatWrapper
			preservedTestValue = 'preserve-me'
		} | ConvertTo-Json | Set-Content -LiteralPath $smokeLauncher -Encoding UTF8
		$targetFull = [IO.Path]::GetFullPath($smoke).TrimEnd('\')
		$wrapperFull = [IO.Path]::GetFullPath($smokeFlatWrapper)
		if ($wrapperFull.StartsWith($targetFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
			throw 'O wrapper flat do smoke deve ficar fora do target EmulationStation.'
		}

		# O lock operacional e separado do fixture Launcher para permitir primeiro
		# provar o guard de ausencia e depois provar que o lock valido nao e tocado.
		$smokeMaintenanceRoot = Join-Path $SmokeRoot 'Maintenance'
		[IO.Directory]::CreateDirectory($smokeMaintenanceRoot) | Out-Null
		$smokeMaintenanceLock = Join-Path $smokeMaintenanceRoot 'maintenance.lock'
		Initialize-SmokeFileIdentity

		function Get-SmokeProtectedTreeFingerprint([string]$TreeRoot, [string]$Prefix) {
			$rootItem = Get-Item -LiteralPath $TreeRoot -Force
			$treeFull = [IO.Path]::GetFullPath($TreeRoot).TrimEnd('\')
			$entries = [Collections.Generic.List[string]]::new()
			foreach ($item in @($rootItem) + @(Get-ChildItem -LiteralPath $TreeRoot -Force -Recurse | Sort-Object FullName)) {
				if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
					throw "Fixture protegido contem reparse point: $($item.FullName)"
				}
				$relative = if ([string]::Equals($item.FullName.TrimEnd('\'), $treeFull, [StringComparison]::OrdinalIgnoreCase)) {
					$Prefix
				}
				else { $Prefix + $item.FullName.Substring($treeFull.Length) }
				$metadata = Get-SmokeAclMetadata $item
				if ($item.PSIsContainer) { $entries.Add("DIR|$relative|$metadata") }
				else {
					$hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
					$identity = [TurboRama.Build.SmokeFileIdentity]::Read($item.FullName)
					$entries.Add("FILE|$relative|$identity|$hash|$metadata")
				}
			}
			return @($entries | Sort-Object)
		}

		function Get-SmokeProtectedFileFingerprint([string]$Path, [string]$Label) {
			$full = Assert-RegularFilePath $Path "Fixture protegido ($Label)"
			$item = Get-Item -LiteralPath $full -Force
			$identity = [TurboRama.Build.SmokeFileIdentity]::Read($full)
			$hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
			return "FILE|$Label|$identity|$hash|$(Get-SmokeAclMetadata $item)"
		}

		if (@($pixCredentialNames | Where-Object { $pixTransactionalStateNames -contains $_ }).Count -ne 0) {
			throw 'O contrato do smoke misturou credenciais privadas ao estado PIX transacional.'
		}
		function Get-SmokePixTransactionalFingerprint {
			$entries = [Collections.Generic.List[string]]::new()
			foreach ($stateName in $pixTransactionalStateNames) {
				$path = Join-Path $smokePixRoot $stateName
				if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
					$entries.Add("MISSING|$stateName")
					continue
				}
				$full = Assert-RegularFilePath $path "Estado PIX transacional ($stateName)"
				$item = Get-Item -LiteralPath $full -Force
				$hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
				$entries.Add("FILE|$stateName|$hash|$(Get-SmokeAclMetadata $item)")
			}
			return @($entries | Sort-Object)
		}

		$smokePixTransactionalFingerprint = @(Get-SmokePixTransactionalFingerprint)
		function Assert-SmokePixTransactionalStateUnchanged([string]$Label) {
			$current = @(Get-SmokePixTransactionalFingerprint)
			$delta = @(Compare-Object -ReferenceObject $smokePixTransactionalFingerprint -DifferenceObject $current)
			if ($delta.Count -ne 0) {
				throw "$Label nao restaurou exatamente conteudo, atributos, owner e SDDL dos 11 estados PIX transacionais: $($delta -join '; ')"
			}
		}

		function Get-SmokeOutOfScopeFingerprint {
			$entries = [Collections.Generic.List[string]]::new()
			foreach ($entry in @(Get-SmokeProtectedTreeFingerprint $smokeLauncherBase 'Launcher')) { $entries.Add($entry) }
			foreach ($entry in @(Get-SmokeProtectedTreeFingerprint $smokeRuntimeCache '.emulationstation\.runtime')) { $entries.Add($entry) }
			foreach ($entry in @(Get-SmokeProtectedTreeFingerprint $smokeThemes '.emulationstation\themes\baseline-theme')) { $entries.Add($entry) }
			foreach ($entry in @(Get-SmokeProtectedTreeFingerprint $smokePixLogs '.emulationstation\pix\logs')) { $entries.Add($entry) }
			foreach ($entry in @(Get-SmokeProtectedTreeFingerprint $smokePixSessions '.emulationstation\pix\sessions')) { $entries.Add($entry) }
			$entries.Add((Get-SmokeProtectedFileFingerprint $smokeFlatWrapper 'TurboRama.exe'))
			foreach ($credentialName in $pixCredentialNames) {
				$entries.Add((Get-SmokeProtectedFileFingerprint (Join-Path $smokePixRoot $credentialName) ('.emulationstation\pix\' + $credentialName)))
			}
			foreach ($protectedFile in @($smokeRuntimeLog,$smokeVersionInfo,(Join-Path $smokeRoms 'sentinel.dat'))) {
				$entries.Add((Get-SmokeProtectedFileFingerprint $protectedFile ([IO.Path]::GetFileName($protectedFile))))
			}
			foreach ($creditFile in $smokeCreditFiles) {
				$entries.Add((Get-SmokeProtectedFileFingerprint $creditFile ('.emulationstation\' + [IO.Path]::GetFileName($creditFile))))
			}
			foreach ($protectedDirectory in @($smoke,(Join-Path $smoke '.emulationstation'),$smokePixRoot,$smokeRoms,$smokeMaintenanceRoot)) {
				$full = Assert-RegularDirectoryPath $protectedDirectory 'Diretorio protegido do smoke'
				$item = Get-Item -LiteralPath $full -Force
				$entries.Add("DIR|$full|$(Get-SmokeAclMetadata $item)")
			}
			return @($entries | Sort-Object)
		}

		$smokeOutOfScopeFingerprint = @(Get-SmokeOutOfScopeFingerprint)
		function Assert-SmokeOutOfScopeUnchanged([string]$Label) {
			$current = @(Get-SmokeOutOfScopeFingerprint)
			$delta = @(Compare-Object -ReferenceObject $smokeOutOfScopeFingerprint -DifferenceObject $current)
			if ($delta.Count -ne 0) {
				throw "$Label alterou FileId, conteudo, atributos, owner ou SDDL do Launcher, turborama.json, wrapper, cache, credenciais ou outro objeto fora do escopo: $($delta -join '; ')"
			}
		}

		# Contrato compartilhado com TurboRamaInstaller.cpp. Centralizado para que
		# uma futura mudanca intencional de ABI do smoke altere uma unica constante.
		$maintenanceLockGuardExitCode = 24
		Remove-Item Env:TURBORAMA_INSTALLER_TEST_REFUSE_PROCESS_STOP -ErrorAction SilentlyContinue
		$env:TURBORAMA_INSTALL_TARGET = $smoke
		$env:TURBORAMA_INSTALLER_SILENT_TEST = '1'
		$env:TURBORAMA_LAUNCHER_CONFIG = $smokeLauncher
		$env:TURBORAMA_MAINTENANCE_LOCK = $smokeMaintenanceLock
		$env:TURBORAMA_LAUNCHER_PROCESS = $smokeLauncherProcess
		$env:TURBORAMA_FRONTEND_WRAPPER = $smokeFlatWrapper
		try {
			# Sem maintenance.lock o instalador deve recusar antes de qualquer mutacao.
			$maintenanceGuardProcess = Start-Process -FilePath $FinalInstaller -WorkingDirectory $OutputRoot -Wait -PassThru
			if ($maintenanceGuardProcess.ExitCode -ne $maintenanceLockGuardExitCode) {
				throw "Guard de modo manutencao retornou $($maintenanceGuardProcess.ExitCode), esperado $maintenanceLockGuardExitCode."
			}
			if (Test-Path -LiteralPath $smokeMaintenanceLock) {
				throw 'Guard de modo manutencao criou indevidamente maintenance.lock.'
			}
			Assert-SmokeInstallSetUnchanged $legacyInstallSetFingerprint 'Guard de modo manutencao'
			Assert-SmokePixTransactionalStateUnchanged 'Guard de modo manutencao'
			Assert-SmokeOutOfScopeUnchanged 'Guard de modo manutencao'

			# O fluxo real de manutencao cria o lock antes do instalador. O smoke usa
			# arquivo regular isolado e prova que o instalador apenas o le/pina.
			Set-Content -LiteralPath $smokeMaintenanceLock -Value 'maintenance-mode-active' -Encoding ASCII
			$maintenanceAcl = Get-Acl -LiteralPath $smokeMaintenanceLock
			$inheritedMaintenanceRules = @($maintenanceAcl.Access | Where-Object { $_.IsInherited })
			if ($maintenanceAcl.AreAccessRulesProtected -or $inheritedMaintenanceRules.Count -eq 0) {
				throw 'Fixture maintenance.lock deve manter ACL herdada para provar que o lock herdado valido e aceito.'
			}
			$maintenanceLockFingerprint = Get-SmokeProtectedFileFingerprint $smokeMaintenanceLock 'maintenance.lock'
			function Assert-SmokeMaintenanceLockUnchanged([string]$Label) {
				$current = Get-SmokeProtectedFileFingerprint $smokeMaintenanceLock 'maintenance.lock'
				if ($current -ne $maintenanceLockFingerprint) {
					throw "$Label alterou FileId, link count, conteudo, atributos, owner ou SDDL de maintenance.lock."
				}
			}

			# Prova determinística do ramo que recusa prosseguir sem conseguir
			# coordenar os processos exatos. O hook existe somente no smoke isolado.
			$env:TURBORAMA_INSTALLER_TEST_REFUSE_PROCESS_STOP = '1'
			$processRefusal = Start-Process -FilePath $FinalInstaller -WorkingDirectory $OutputRoot -Wait -PassThru
			if ($processRefusal.ExitCode -ne 18) {
				throw "Guard de processos retornou $($processRefusal.ExitCode), esperado 18."
			}
			Assert-SmokeInstallSetUnchanged $legacyInstallSetFingerprint 'Guard de processos'
			Assert-SmokePixTransactionalStateUnchanged 'Guard de processos'
			Assert-SmokeOutOfScopeUnchanged 'Guard de processos'
			Assert-SmokeMaintenanceLockUnchanged 'Guard de processos'
			Remove-Item Env:TURBORAMA_INSTALLER_TEST_REFUSE_PROCESS_STOP -ErrorAction SilentlyContinue

			$env:TURBORAMA_INSTALLER_TEST_FAIL_AFTER_EXTRACT = '1'
			$rollbackProcess = Start-Process -FilePath $FinalInstaller -WorkingDirectory $OutputRoot -Wait -PassThru
			if ($rollbackProcess.ExitCode -ne 13) { throw "Teste de rollback retornou $($rollbackProcess.ExitCode), esperado 13." }
			foreach ($legacyFile in $legacyFiles) {
				$current = Join-Path $smoke $legacyFile
				Require-File $current "Rollback ($legacyFile)"
				if ((Get-FileHash -LiteralPath $current -Algorithm SHA256).Hash -ne $legacyHashes[$legacyFile]) {
					throw "Rollback nao restaurou exatamente $legacyFile."
				}
			}
			if (Test-Path -LiteralPath (Join-Path $smoke 'pix-agent\TurboRamaPixAgent.dll')) {
				throw 'Rollback deixou agente novo misturado ao conjunto anterior.'
			}
			Assert-SmokeInstallSetUnchanged $legacyInstallSetFingerprint 'Rollback de extracao'
			Assert-SmokePixTransactionalStateUnchanged 'Rollback de extracao'
			Assert-SmokeOutOfScopeUnchanged 'Rollback de extracao'
			Assert-SmokeMaintenanceLockUnchanged 'Rollback de extracao'
			Remove-Item Env:TURBORAMA_INSTALLER_TEST_FAIL_AFTER_EXTRACT -ErrorAction SilentlyContinue

			# Falha depois de resetar o editor e registrar a identidade deve restaurar
			# os 11 estados PIX com bytes e metadados exatos; secret.dat, bridge.key e
			# owner-settings.json nunca entram no snapshot e seguem intocados.
			$env:TURBORAMA_INSTALLER_TEST_FAIL_AFTER_PIX_STATE = '1'
			$pixRollbackProcess = Start-Process -FilePath $FinalInstaller -WorkingDirectory $OutputRoot -Wait -PassThru
			if ($pixRollbackProcess.ExitCode -ne 15) {
				throw "Teste de rollback do estado PIX retornou $($pixRollbackProcess.ExitCode), esperado 15."
			}
			Assert-SmokeInstallSetUnchanged $legacyInstallSetFingerprint 'Rollback do estado PIX'
			Assert-SmokePixTransactionalStateUnchanged 'Rollback do estado PIX'
			Assert-SmokeOutOfScopeUnchanged 'Rollback do estado PIX'
			Assert-SmokeMaintenanceLockUnchanged 'Rollback do estado PIX'
			Remove-Item Env:TURBORAMA_INSTALLER_TEST_FAIL_AFTER_PIX_STATE -ErrorAction SilentlyContinue

			$process = Start-Process -FilePath $FinalInstaller -WorkingDirectory $OutputRoot -Wait -PassThru
			if ($process.ExitCode -ne 0) { throw "Instalador retornou $($process.ExitCode)." }
			Assert-SmokeOutOfScopeUnchanged 'Instalacao isolada valida'
			Assert-SmokeMaintenanceLockUnchanged 'Instalacao isolada valida'
		}
		finally {
			Remove-Item Env:TURBORAMA_INSTALL_TARGET -ErrorAction SilentlyContinue
			Remove-Item Env:TURBORAMA_INSTALLER_SILENT_TEST -ErrorAction SilentlyContinue
			Remove-Item Env:TURBORAMA_LAUNCHER_CONFIG -ErrorAction SilentlyContinue
			Remove-Item Env:TURBORAMA_MAINTENANCE_LOCK -ErrorAction SilentlyContinue
			Remove-Item Env:TURBORAMA_LAUNCHER_PROCESS -ErrorAction SilentlyContinue
			Remove-Item Env:TURBORAMA_FRONTEND_WRAPPER -ErrorAction SilentlyContinue
			Remove-Item Env:TURBORAMA_INSTALLER_TEST_FAIL_AFTER_EXTRACT -ErrorAction SilentlyContinue
			Remove-Item Env:TURBORAMA_INSTALLER_TEST_FAIL_AFTER_PIX_STATE -ErrorAction SilentlyContinue
			Remove-Item Env:TURBORAMA_INSTALLER_TEST_REFUSE_PROCESS_STOP -ErrorAction SilentlyContinue
		}

		foreach ($required in @('emulationstation.exe','pix-agent\TurboRamaPixAgent.dll','pix-agent\runtime\dotnet.exe','pix-agent\appsettings.json','.emulationstation\pix\installation-v25.log')) {
			Require-File (Join-Path $smoke $required) "Arquivo instalado ($required)"
		}
		foreach ($publishedFile in @('emulationstation.exe')) {
			$expectedPublishedHash = (Get-FileHash -LiteralPath (Join-Path $ArchiveRoot $publishedFile) -Algorithm SHA256).Hash
			$installedPublishedHash = (Get-FileHash -LiteralPath (Join-Path $smoke $publishedFile) -Algorithm SHA256).Hash
			if ($installedPublishedHash -ne $expectedPublishedHash) {
				throw "Instalacao valida nao publicou exatamente o candidato: $publishedFile"
			}
		}
		$installedSettings = Join-Path $smoke 'pix-agent\appsettings.json'
		if ((Get-FileHash -LiteralPath $installedSettings -Algorithm SHA256).Hash -ne
			(Get-FileHash -LiteralPath $AgentSettingsTemplate -Algorithm SHA256).Hash) {
			throw 'A instalacao isolada alterou ou substituiu o appsettings seguro do agente PIX.'
		}
		$installedAgentTreeFingerprint = @(Get-DirectoryTreeFingerprint (Join-Path $smoke 'pix-agent') 'pix-agent')
		$agentDelta = @(Compare-Object -ReferenceObject $expectedAgentTreeFingerprint -DifferenceObject $installedAgentTreeFingerprint)
		if ($agentDelta.Count -ne 0) {
			throw "A instalacao isolada deixou arquivos ausentes, alterados ou antigos na arvore do agente PIX: $($agentDelta -join '; ')"
		}
		$launcherAfter = Get-Content -LiteralPath $smokeLauncher -Raw | ConvertFrom-Json
		$expectedFrontend = [IO.Path]::GetFullPath($smokeFlatWrapper)
		$directEs = [IO.Path]::GetFullPath((Join-Path $smoke 'emulationstation.exe'))
		$resolvedFrontend = [IO.Path]::GetFullPath([string]$launcherAfter.frontendExecutable)
		$frontendMatches = [string]::Equals($resolvedFrontend, $expectedFrontend, [StringComparison]::OrdinalIgnoreCase)
		$frontendWasNotReboundToEs = -not [string]::Equals($resolvedFrontend, $directEs, [StringComparison]::OrdinalIgnoreCase)
		$preservedValueMatches = [string]$launcherAfter.preservedTestValue -eq 'preserve-me'
		if (-not $frontendMatches -or -not $frontendWasNotReboundToEs -or -not $preservedValueMatches) {
			throw 'turborama.json nao preservou integralmente o wrapper flat configurado.'
		}
		foreach ($rotatedState in $pixTransactionalStateNames[0..7]) {
			if (Test-Path -LiteralPath (Join-Path $smokePixRoot $rotatedState)) {
				throw "Instalacao valida nao rotacionou o estado PIX publico: $rotatedState"
			}
		}
		if (Test-Path -LiteralPath (Join-Path $smokePixRoot 'agent-stop.request')) {
			throw 'Instalacao valida deixou agent-stop.request ativo.'
		}
		$installedIdentity = (Get-Content -LiteralPath (Join-Path $smokePixRoot 'kiosk-identity.sid') -Raw).Trim()
		if ($installedIdentity -ne $currentKioskSid) {
			throw "Instalacao valida registrou SID inesperado: $installedIdentity"
		}
		$reEnrollmentNotice = Get-Content -LiteralPath (Join-Path $smokePixRoot 'owner-reenrollment-required.json') -Raw | ConvertFrom-Json
		if ([string]$reEnrollmentNotice.reason -ne 'kiosk_sid_changed' -or
			[string]$reEnrollmentNotice.state -ne 'recadastro_required') {
			throw 'Instalacao valida nao registrou corretamente o recadastro apos troca de SID.'
		}
		foreach ($legacyTool in @('CONFIGURAR-USER-TOKEN-PIX.exe','CONFIGURAR-ACCESS-TOKEN-PIX.exe')) {
			$legacyToolHash = (Get-FileHash -LiteralPath (Join-Path $smoke $legacyTool) -Algorithm SHA256).Hash
			if ($legacyToolHash -ne $legacyHashes[$legacyTool]) {
				throw "O instalador alterou um programa administrativo fora do payload: $legacyTool"
			}
		}
		$installedBridge = Join-Path $smoke '.emulationstation\pix\self-test-isolado'
		Run (Join-Path $smoke 'pix-agent\runtime\dotnet.exe') @((Join-Path $smoke 'pix-agent\TurboRamaPixAgent.dll'),'--self-test','--bridge',$installedBridge) $smoke
		# O overlay substitui somente o EXE. As DLLs do frontend pertencem ao kiosk
		# existente e ficam fora do payload; use o mesmo conjunto versionado que ja
		# validou o binario compilado, sem copiar ou alterar essas DLLs no fixture.
		Invoke-FrontendSelfTest (Join-Path $smoke 'emulationstation.exe') '--pix-agent-trust-self-test'
		Assert-SmokeOutOfScopeUnchanged 'Autotestes dos componentes instalados'
		Assert-SmokeMaintenanceLockUnchanged 'Autotestes dos componentes instalados'
    }
    else { Write-Host 'Use -TestarInstalador para validar uma instalacao completa.' -ForegroundColor Yellow }

    Stage '9/9 - FECHAMENTO E PROMOCAO TRANSACIONAL'
    if ((Get-FileHash -LiteralPath $FinalInstaller -Algorithm SHA256).Hash -ne $hash) {
        throw 'O instalador candidato mudou durante os testes isolados.'
    }
    $signatureReportFile = Join-Path $OutputRoot 'ASSINATURA-AUTHENTICODE.txt'
    $signatureReport = @(
        'TURBORAMA PIX COMERCIAL v25 - ASSINATURA AUTHENTICODE',
        ('Perfil de protecao comercial: ' + $(if ($ProtecaoComercial) { 'ATIVO' } else { 'DESATIVADO' })),
        ('Status: ' + $(if ($script:SigningEnabled) { 'ASSINADO E VERIFICADO' } else { 'NAO ASSINADO - CERTIFICADO NAO FORNECIDO' })),
        ('Certificado: ' + $(if ($script:SigningEnabled) { $script:SignCertificate.Thumbprint } else { '(nenhum)' })),
        ('Armazenamento: ' + $(if ($script:SigningEnabled) { $LocalCertificado + '\My' } else { '(nao aplicavel)' })),
        ('Carimbo do tempo: ' + $(if ($ServidorCarimboDoTempo) { $ServidorCarimboDoTempo } else { '(nao configurado)' })),
        'Para o perfil comercial completo, use -ProtecaoComercial e -CertificadoThumbprint.',
        'Nenhum certificado ou senha privada e armazenado no repositorio ou no pacote.'
    )
    Set-Content -LiteralPath $signatureReportFile -Value $signatureReport -Encoding UTF8
    $canonicalInstaller = Join-Path $CanonicalOutputRoot $InstallerFileName
    if ($sourceDirtyCount -ge 0) {
        $sourceCommitAtEnd = (& $git -C $RepoRoot rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommitAtEnd)) {
            throw 'HEAD Git ficou indisponivel no fechamento do build.'
        }
        $sourceDirtyEntriesAtEnd = @(& $git -C $RepoRoot status --porcelain=v1 -uall)
        if ($LASTEXITCODE -ne 0) { throw 'git status falhou no fechamento do build.' }
        $sourceStatusAtEnd = $sourceDirtyEntriesAtEnd -join "`n"
        $sourceTreeFingerprintAtEnd = Get-GitWorkingTreeFingerprint $git $RepoRoot
        if ((-not [string]::Equals($sourceCommit, $sourceCommitAtEnd, [StringComparison]::Ordinal)) -or
            (-not [string]::Equals($sourceStatusAtStart, $sourceStatusAtEnd, [StringComparison]::Ordinal)) -or
            (-not [string]::Equals($sourceTreeFingerprintAtStart, $sourceTreeFingerprintAtEnd, [StringComparison]::Ordinal))) {
            throw 'A revisao Git ou a arvore de trabalho mudou durante o build; a release candidata foi recusada.'
        }
    }
    $installerCppSourceAtEnd = Assert-RegularSingleLinkFilePath $installerCppSource 'Fonte C++ do instalador no fechamento'
    $buildScriptSourceAtEnd = Assert-RegularSingleLinkFilePath $buildScriptSource 'Script PowerShell no fechamento'
    $installerCppIdentityAtEnd = [TurboRama.Build.SmokeFileIdentity]::Read($installerCppSourceAtEnd)
    $buildScriptIdentityAtEnd = [TurboRama.Build.SmokeFileIdentity]::Read($buildScriptSourceAtEnd)
    $installerCppHashAtEnd = (Get-FileHash -LiteralPath $installerCppSourceAtEnd -Algorithm SHA256).Hash.ToUpperInvariant()
    $buildScriptHashAtEnd = (Get-FileHash -LiteralPath $buildScriptSourceAtEnd -Algorithm SHA256).Hash.ToUpperInvariant()
    if ((-not [string]::Equals($installerCppIdentityAtStart, $installerCppIdentityAtEnd, [StringComparison]::Ordinal)) -or
        ($installerCppHashAtStart -ne $installerCppHashAtEnd)) {
        throw 'TurboRamaInstaller.cpp mudou durante o build; a release candidata foi recusada.'
    }
    if ((-not [string]::Equals($buildScriptIdentityAtStart, $buildScriptIdentityAtEnd, [StringComparison]::Ordinal)) -or
        ($buildScriptHashAtStart -ne $buildScriptHashAtEnd)) {
        throw 'O script PowerShell mudou durante o build; a release candidata foi recusada.'
    }
    $report = @(
        'TURBORAMA PIX COMERCIAL v25 - CANDIDATO INTERNO VALIDADO TECNICAMENTE',
        'Status comercial: NAO LIBERADO PARA VENDA',
        "Data: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "Instalador publicado: $canonicalInstaller",
        "SHA256: $hash",
        "Destino canonico: $CanonicalOutputRoot",
        "Fonte Git: $sourceCommit",
        ('Arvore de trabalho: ' + $(if ($sourceDirtyCount -eq 0) { 'LIMPA' } elseif ($sourceDirtyCount -gt 0) { "SUJA ($sourceDirtyCount entradas; NAO REPRODUZIVEL APENAS PELO COMMIT; lista completa no log)" } else { 'NAO IDENTIFICADA; REPRODUTIBILIDADE POR COMMIT NAO COMPROVADA' })),
        "Impressao SHA256 da arvore fonte no build: $sourceTreeFingerprintAtStart",
        "Fonte C++ do instalador SHA256: $installerCppHashAtStart",
        "Script PowerShell do build SHA256: $buildScriptHashAtStart",
        'Promocao: serializada e somente apos assinatura/verificacao aplicavel, integridade e smoke test isolado',
        'Recuperacao de publicacao: diario duravel restaura a release anterior apos interrupcao entre renomeacoes',
        'Modelo de concorrencia do build: perfil privado/cooperativo do compilador; escritor externo hostil durante snapshot ou promocao nao e suportado, e a release promovida e revalidada',
        'Smoke test: guards de manutencao/processos, rollback pos-extracao, rollback pos-estado PIX e instalacao valida aprovados no laboratorio isolado',
        'Escopo Windows IoT: somente o EmulationStation/PIX flat (D:\emulationstation) ou classico (D:\Turborama\emulationstation); C:\TurboRama e Factory Pack fora do escopo',
        'Modo manutencao: maintenance.lock valido e capacidade de parar/confirmar os processos dos caminhos exatos sao pre-condicoes obrigatorias',
        'Fronteiras do instalador: matriz logica fail-closed e pins de leitura validados no autoteste nativo; smoke usa wrapper/Launcher/lock separados em fixture flat',
        'Limite do laboratorio: nenhum processo real, servico real ou hardware Windows IoT foi exercitado pelo smoke; validacao no quiosque continua pendente',
        'Objetos fora do escopo: Launcher, turborama.json, wrapper, cache .runtime, temas, creditos, PIX logs/sessions/credenciais e maintenance.lock preservados por bytes, FileId e metadados quando aplicavel',
        'Permissoes Windows: nenhuma politica nova de ACL e aplicada aos objetos preexistentes nem ao destino D:\; staging/backup administrativo e efemero, e rollbacks restauram owner/SDDL/atributos do conjunto anterior',
		'Configurador bancario portatil: compilado e testado; nao entra no payload do kiosk',
		'Ativador de maquina portatil: compilado e testado; nao entra no payload do kiosk',
		'Credencial Mercado Pago: enviada uma unica vez ao servidor e nunca salva no gabinete',
		'Loja e PDV: inventario real, selecao explicita e limpeza limitada a IDs gerenciados pelo TurboRama',
		'Mercado Pago: novas cobrancas criadas exclusivamente pelo servidor autorizado',
        'Testes de cobranca: somente simuladores locais; nenhuma cobranca real foi criada',
        'Credenciais privadas incluidas: NAO',
        'Tema embutido: gerado de forma deterministica no build limpo (sem depender de Python)',
		'Rollback do conjunto instalado: frontend e agente comparados por caminho, SHA-256, atributos, owner e SDDL apos falha injetada; utilitarios administrativos permanecem fora do payload',
        'Rollback PIX: 8 estados do editor, kiosk-identity.sid, owner-reenrollment-required.json e agent-stop.request restaurados por bytes, atributos, owner e SDDL',
        'Credenciais PIX: secret.dat, bridge.key e owner-settings.json nunca entram no snapshot transacional e permanecem intocados',
        '7-Zip 24.09 pinado: SHA256 223B873C50380FE9A39F1A22B6ABF8D46DB506E1C08D08312902F6F3CD1F7AC3',
        '7-Zip 24.09, NOTICE, licenca oficial e GNU LGPL 2.1 incluidos no payload, na entrega e no manifesto SHA-256',
        'Dependencias .NET: packages.lock.json validado por restore --locked-mode',
		('Protecao comercial de compilacao: ' + $(if ($ProtecaoComercial) { 'ATIVA; mitigacoes nativas, manifesto fechado e payload sem simbolos/fontes/testes; Authenticode opcional' } else { 'DESATIVADA' })),
		"Manifesto fechado do bundle PIX: SHA256 $($script:AgentBundleSha256); cobre todos os caminhos, DLLs, JSONs, runtimeTargets e runtime privado; extras e ausencias sao recusados",
		'Inicializacao do .NET: ambiente allowlist; startup hooks, additional deps, shared stores, profiler e fallback para runtime global recusados',
		'Proveniencia da release: Git obrigatoriamente disponivel e limpo antes da assinatura comercial',
		'Cofre da credencial Mercado Pago: AES-256-GCM no servidor Linux; segredo ausente do kiosk',
		('Licenca comercial por maquina: ' + $(if ($ServidorAutoritativo) { 'AUTORIZADA PELO SERVIDOR; prova de posse e sessao exclusiva exigidas antes de nova cobranca' } else { 'MODO SERVIDOR NAO SELECIONADO' })),
		'Perfis de vinculo: TPM_BOUND, USB_TOKEN_BOUND ou SOFTWARE_BOUND_ONLINE conforme politica do servidor',
		'Chave privada do servidor ou credencial Mercado Pago incluida no cliente: NAO',
        ('Assinatura Authenticode: ' + $(if ($script:SigningEnabled) { 'ASSINADA E VERIFICADA' } else { 'NAO APLICADA; certificado nao fornecido' }))
    )
    Set-Content -LiteralPath (Join-Path $OutputRoot 'RELATORIO-COMPILACAO-v25.txt') -Value $report -Encoding UTF8
    Write-ReleaseChecksums $OutputRoot $ReleaseArtifacts
    Test-ReleaseDirectory $OutputRoot $ReleaseArtifacts | Out-Null
    Assert-RetiredRepairAbsent $OutputRoot 'Entrega candidata fechada'
    $ReleaseArtifactsSealed = $true

    # A troca usa somente renomeacoes de diretorio no mesmo volume, nunca copia
    # arquivo por arquivo. O lock impede concorrencia e o diario duravel restaura
    # a release anterior se a maquina parar entre as duas renomeacoes.
    $previousRelease = Promote-ReleaseCandidate $OutputRoot $CanonicalOutputRoot $ReleaseHistoryRoot $hash $ReleaseArtifacts
    Assert-RetiredRepairAbsent $CanonicalOutputRoot 'Entrega canonica promovida'
    $FinalInstaller = $canonicalInstaller
    Write-Host ''
    Write-Host "CANDIDATO INTERNO PRONTO PARA VALIDACAO (NAO LIBERADO PARA VENDA): $FinalInstaller" -ForegroundColor Yellow
    Write-Host "SHA-256: $hash" -ForegroundColor White
    if ($previousRelease) {
        Write-Host "Release anterior preservada em: $previousRelease" -ForegroundColor Yellow
    }
    Exit-BuildSourcePins
    Exit-BuildLock
    exit 0
}
catch {
    $failure = $_
    Exit-BuildSourcePins
    Exit-BuildLock
    Write-Host "`nERRO: $($failure.Exception.Message)" -ForegroundColor Red
    if (-not $ReleaseArtifactsSealed) {
        Add-Content -LiteralPath $LogFile -Value "ERRO: $($failure.Exception.Message)`r`n$($failure.ScriptStackTrace)" -Encoding UTF8 -ErrorAction SilentlyContinue
        Write-Host "Log: $LogFile" -ForegroundColor Yellow
    }
    else {
        Write-Host 'A release ja estava selada; nenhum artefato manifestado foi alterado pelo tratador de erro.' -ForegroundColor Yellow
    }
    if (-not $SemPausa) { Read-Host 'Pressione ENTER para fechar' | Out-Null }
    exit 1
}
