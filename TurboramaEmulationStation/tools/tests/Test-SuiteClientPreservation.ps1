param([string]$BaseCommit = '5a356172013a620a1a0ecf151c00c9238ea21a24')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
Push-Location $repo
try {
    & git cat-file -e "$BaseCommit^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        & git fetch --no-tags --depth=1 --filter=blob:none origin $BaseCommit
        if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel conferir a base imutavel.' }
    }
    # Compare tracked working files as well as committed changes. New files are
    # reviewed separately; none of the old functionality may disappear.
    $allowed = @(
        'TurboramaEmulationStation/CMakeLists.txt',
        'TurboramaEmulationStation/es-app/CMakeLists.txt',
        'TurboramaEmulationStation/es-app/src/EmulationStation.h',
        'TurboramaEmulationStation/es-app/src/main.cpp',
        'TurboramaEmulationStation/es-app/src/FileData.cpp',
        # Test-only adaptation for the embedded Microsoft HTTP/2 runtime's
        # CreditManager metadata name. App/game/memory/audio sources unchanged.
        'TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1'
    )
    $changes = @(& git diff --no-renames --name-status $BaseCommit -- .)
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao comparar a base.' }
    foreach ($change in $changes) {
        $parts = $change -split "`t", 2
        if ($parts[0] -eq 'A') { continue }
        if ($parts[0] -ne 'M' -or $parts[1] -notin $allowed) {
            throw "Mudanca fora da integracao autorizada: $change"
        }
    }
    $nativeRoot = Join-Path $repo 'TurboramaEmulationStation'
    $main = Get-Content -LiteralPath (Join-Path $nativeRoot 'es-app/src/main.cpp') -Raw
    $game = Get-Content -LiteralPath (Join-Path $nativeRoot 'es-app/src/FileData.cpp') -Raw
    if ($main -notmatch 'SuiteAccess' -or $game -notmatch 'SuiteAccess') {
        throw 'Faltam os pontos de controle de inicializacao/lancamento.'
    }
    # Authorization reuses the existing activation, never a running Suite app.
    # Cover both initial login and loss-of-authorization messages in the frontend.
    foreach ($relative in @('es-app/src/main.cpp', 'es-app/src/SuiteAccessGate.cpp',
        'suite-licensing/AccessFailurePresentation.cs')) {
        $messages = Get-Content -LiteralPath (Join-Path $nativeRoot $relative) -Raw
        if ($messages -match '(?i)abra\s+(?:o\s+|a\s+)?(?:turborama\s+)?suite') {
            throw "A interface ainda pede para abrir a Suite: $relative"
        }
    }
    'SUITE_CLIENT_PRESERVATION=OK'
    "Base=$BaseCommit"
    "ChangedExistingFiles=$($changes.Count)"
    'Core, menus, recursos, temas, memoria, videos e workflows antigos preservados.'
}
finally { Pop-Location }
