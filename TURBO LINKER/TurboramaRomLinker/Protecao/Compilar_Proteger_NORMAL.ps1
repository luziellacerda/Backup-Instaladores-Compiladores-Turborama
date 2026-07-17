$ErrorActionPreference = "Stop"
$proteção = Split-Path -Parent $MyInvocation.MyCommand.Path
$raiz = Resolve-Path (Join-Path $proteção "..")
$sln = Join-Path $raiz "TurboramaRomLinker.sln"
$projDir = Join-Path $raiz "TurboramaRomLinker"
$releaseDir = Join-Path $projDir "bin\Release"
$exe = Join-Path $releaseDir "TurboramaRomLinker.exe"
$outDir = Join-Path $releaseDir "Protegido_NORMAL"

Write-Host "=== TURBORAMA ROM LINKER - BUILD + PROTEÇÃO NORMAL ===" -ForegroundColor Cyan
Write-Host "Raiz: $raiz"

function Get-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $p = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
        if ($p -and (Test-Path $p)) { return $p }
    }
    $candidatos = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($c in $candidatos) { if (Test-Path $c) { return $c } }
    throw "MSBuild não encontrado. Instale Visual Studio com workload .NET desktop development."
}

function Get-Confuser {
    $local = Join-Path $proteção "Ferramentas\Confuser.CLI.exe"
    if (Test-Path $local) { return $local }
    $local2 = Join-Path $proteção "Confuser.CLI.exe"
    if (Test-Path $local2) { return $local2 }
    $cmd = Get-Command "Confuser.CLI.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "Confuser.CLI.exe não encontrado. Coloque em Protecao\Ferramentas\Confuser.CLI.exe ou adicione ao PATH."
}

$msbuild = Get-MSBuild
Write-Host "MSBuild: $msbuild" -ForegroundColor Green

if (Test-Path (Join-Path $projDir "bin")) { Remove-Item (Join-Path $projDir "bin") -Recurse -Force }
if (Test-Path (Join-Path $projDir "obj")) { Remove-Item (Join-Path $projDir "obj") -Recurse -Force }

& $msbuild $sln /t:Clean /p:Configuration=Release /p:Platform="Any CPU" /m
& $msbuild $sln /t:Build /p:Configuration=Release /p:Platform="Any CPU" /p:DebugType=none /p:DebugSymbols=false /m

if (!(Test-Path $exe)) { throw "EXE Release não foi gerado: $exe" }
Get-ChildItem $releaseDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

$confuser = Get-Confuser
Write-Host "Confuser: $confuser" -ForegroundColor Green

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$crproj = Join-Path $proteção "ConfuserEx_Turborama_NORMAL.crproj"
& $confuser $crproj

$protectedExe = Join-Path $outDir "TurboramaRomLinker.exe"
if (!(Test-Path $protectedExe)) { throw "EXE protegido não foi gerado: $protectedExe" }

Write-Host "" 
Write-Host "PROTEÇÃO NORMAL FINALIZADA" -ForegroundColor Green
Write-Host "EXE protegido:" -ForegroundColor Yellow
Write-Host $protectedExe -ForegroundColor Yellow
Write-Host "" 
Write-Host "Teste esse EXE em outro PC antes de distribuir."
