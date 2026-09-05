#Requires -Version 5.1
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Assert-SourceRegularPath([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw ('Reparse point recusado: ' + $Path)
        }
    }
}

function Test-SourceIntegrity([string]$Path, [object]$Entry) {
    Assert-SourceRegularPath $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    return (Get-Item -LiteralPath $Path).Length -eq [long]$Entry.length -and
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Entry.sha256
}

$sourceCatalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'third-party-sources.lock.json') -Raw | ConvertFrom-Json
if ($sourceCatalog.schemaVersion -ne 1 -or @($sourceCatalog.sources).Count -eq 0) { throw 'Manifesto de fontes invalido.' }
$sourceResources = Join-Path $PSScriptRoot 'resources'
$sourceDestination = [IO.Path]::GetFullPath((Join-Path $sourceResources 'third-party-sources'))
Assert-SourceRegularPath $PSScriptRoot
Assert-SourceRegularPath $sourceResources
Assert-SourceRegularPath $sourceDestination
if ($VerifyOnly -and -not (Test-Path -LiteralPath $sourceDestination -PathType Container)) { throw 'Fontes correspondentes ausentes.' }
if (-not $VerifyOnly) { New-Item -ItemType Directory -Path $sourceDestination -Force | Out-Null }
$sourceNames = @{}

foreach ($sourceEntry in $sourceCatalog.sources) {
    $sourceName = [string]$sourceEntry.name
    if ($sourceName -notmatch '^[A-Za-z0-9][A-Za-z0-9._+-]*\.tar\.gz$' -or $sourceName.Contains('..') -or $sourceNames.ContainsKey($sourceName)) {
        throw 'Nome de fonte invalido ou duplicado.'
    }
    $sourceNames[$sourceName] = $true
    if ([long]$sourceEntry.length -le 0 -or [long]$sourceEntry.length -gt 1GB -or $sourceEntry.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
        throw ('Integridade de fonte invalida: ' + $sourceName)
    }
    $sourceUri = [Uri]$sourceEntry.sourceUrl
    if (-not $sourceUri.IsAbsoluteUri -or $sourceUri.Scheme -ne 'https' -or $sourceUri.Host -ne 'github.com' -or
        $sourceUri.AbsolutePath -notmatch '^/adoptium/temurin(8|17|21|25)-binaries/releases/download/') {
        throw ('Fonte oficial invalida: ' + $sourceName)
    }
    $sourceTarget = Join-Path $sourceDestination $sourceName
    if (Test-Path -LiteralPath $sourceTarget) {
        if (-not (Test-SourceIntegrity $sourceTarget $sourceEntry)) { throw ('Fonte local difere do manifesto: ' + $sourceName) }
        Write-Output ('CACHE de fontes verificado: ' + $sourceName)
        continue
    }
    if ($VerifyOnly) { throw ('Fonte correspondente ausente: ' + $sourceName) }

    $sourceTemporary = Join-Path $sourceDestination ('source-' + [Guid]::NewGuid().ToString('N') + '.download')
    $sourceOwnedTemporary = $false
    try {
        $sourceReservation = [IO.File]::Open($sourceTemporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $sourceReservation.Dispose()
        $sourceOwnedTemporary = $true
        Invoke-WebRequest -UseBasicParsing -Uri $sourceUri -OutFile $sourceTemporary -TimeoutSec 180
        if (-not (Test-SourceIntegrity $sourceTemporary $sourceEntry)) { throw ('Download recusado por tamanho ou SHA-256: ' + $sourceName) }
        if (Test-Path -LiteralPath $sourceTarget) { throw ('Destino criado durante o download: ' + $sourceName) }
        Move-Item -LiteralPath $sourceTemporary -Destination $sourceTarget
        Write-Output ('DOWNLOAD de fontes verificado: ' + $sourceName)
    }
    finally {
        # Remove somente o arquivo temporario reservado por esta tentativa; nunca diretorios ou caches.
        if ($sourceOwnedTemporary -and (Test-Path -LiteralPath $sourceTemporary)) {
            $sourceCheckedTemporary = [IO.Path]::GetFullPath($sourceTemporary)
            if ([IO.Path]::GetDirectoryName($sourceCheckedTemporary) -ne $sourceDestination -or
                [IO.Path]::GetFileName($sourceCheckedTemporary) -notmatch '^source-[a-f0-9]{32}\.download$') {
                throw 'Caminho temporario de fontes invalido.'
            }
            Assert-SourceRegularPath $sourceCheckedTemporary
            Remove-Item -LiteralPath $sourceCheckedTemporary -Force
        }
    }
}
Write-Output ('Fontes correspondentes verificadas: ' + @($sourceCatalog.sources).Count)
