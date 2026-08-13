[CmdletBinding()]
param(
    [ValidateSet('portable','win-x64','linux-x64')]
    [string]$RuntimeIdentifier = 'portable',
    [string]$Saida,
    [switch]$PermitirGitSujo
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo = Split-Path -Parent $PSCommandPath
$Project = Join-Path $Repo 'TurboramaEmulationStation\tools\TurboRamaPixOnlineServer\TurboRamaPixOnlineServer.csproj'
if (-not $Saida) { $Saida = Join-Path $Repo "outputs\servidor-pix-online-$RuntimeIdentifier" }
$Saida = [IO.Path]::GetFullPath($Saida)

if (-not $PermitirGitSujo) {
    $status = & git.exe -C $Repo status --porcelain --untracked-files=all
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel verificar o Git.' }
    if ($status) { throw 'A compilacao do servidor exige Git limpo e revisado. Registre as alteracoes antes de publicar.' }
}

$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('turborama-online-build-' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $tempRoot 'publish'
$previousDotnetCliHome = $env:DOTNET_CLI_HOME
$previousDotnetFirstUse = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousDotnetNoLogo = $env:DOTNET_NOLOGO
$previousDotnetTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$previousDotnetToolsPath = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
try {
    New-Item -ItemType Directory -Path $publish -Force | Out-Null
    $env:DOTNET_CLI_HOME = Join-Path $tempRoot 'dotnet-home'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME -Force | Out-Null
    & $dotnet restore $Project --ignore-failed-sources --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Restore do servidor falhou.' }
    & $dotnet build $Project -c Release --no-restore -warnaserror --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Compilacao do servidor falhou.' }
    $builtDll = Join-Path (Split-Path -Parent $Project) 'bin\Release\net8.0\TurboRamaPixOnlineServer.dll'
    & $dotnet $builtDll --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Autoteste do servidor falhou.' }
    if ($RuntimeIdentifier -eq 'portable') {
        & $dotnet publish $Project -c Release --no-restore --self-contained false -o $publish `
            -p:DebugType=None -p:DebugSymbols=false -p:Deterministic=true
    }
    else {
        & $dotnet restore $Project -r $RuntimeIdentifier --ignore-failed-sources --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Restore do runtime do servidor falhou.' }
        & $dotnet publish $Project -c Release --no-restore --self-contained false -r $RuntimeIdentifier -o $publish `
            -p:DebugType=None -p:DebugSymbols=false -p:Deterministic=true
    }
    if ($LASTEXITCODE -ne 0) { throw 'Publicacao do servidor falhou.' }

    $forbidden = Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object {
        $_.Extension -in @('.pdb','.cs','.ps1','.pfx','.key') -or $_.Name -match '(?i)(secret|token|credential)'
    }
    if ($forbidden) { throw 'A publicacao contem arquivo proibido: ' + (($forbidden.Name | Sort-Object) -join ', ') }

    if (Test-Path -LiteralPath $Saida) {
        $resolvedOutput = (Resolve-Path -LiteralPath $Saida).Path
        $allowedRoot = [IO.Path]::GetFullPath((Join-Path $Repo 'outputs'))
        if (-not $resolvedOutput.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'A saida existente so pode ser substituida dentro da pasta outputs do repositorio.'
        }
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Saida -Force | Out-Null
    Copy-Item -Path (Join-Path $publish '*') -Destination $Saida -Recurse -Force
    $outputPrefix = $Saida.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $checksums = Get-ChildItem -LiteralPath $Saida -Recurse -File | Sort-Object FullName | ForEach-Object {
        if (-not $_.FullName.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Arquivo de saida escapou da pasta autorizada.'
        }
        $relative = $_.FullName.Substring($outputPrefix.Length).Replace('\','/')
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
    [IO.File]::WriteAllLines((Join-Path $Saida 'CHECKSUMS-SHA256.txt'), $checksums,
        [Text.UTF8Encoding]::new($false))
    Write-Host "Servidor compilado e testado: $Saida" -ForegroundColor Green
}
finally {
    $env:DOTNET_CLI_HOME = $previousDotnetCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousDotnetFirstUse
    $env:DOTNET_NOLOGO = $previousDotnetNoLogo
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousDotnetTelemetry
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $previousDotnetToolsPath
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
