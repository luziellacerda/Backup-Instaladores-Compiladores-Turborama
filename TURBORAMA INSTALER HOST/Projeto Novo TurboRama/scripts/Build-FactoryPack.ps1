#Requires -Version 5.1
# Fase 5 - gera TurboRama-Factory-Pack
param(
    [string]$OutputRoot = "D:\tr-factory-pack",
    [string]$Dotnet = "",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Sln = Join-Path $ProjectRoot "TurboRama.sln"
$PackageName = "TurboRama-Factory-Pack"
$PackageDir = Join-Path $OutputRoot $PackageName
$Stamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

function Find-Dotnet {
    param([string]$Preferred)
    if ($Preferred -and (Test-Path $Preferred)) { return $Preferred }
    foreach ($c in @(
        "D:\tr-dotnet\dotnet.exe",
        "$env:ProgramFiles\dotnet\dotnet.exe"
    )) {
        if (Test-Path $c) { return $c }
    }
    throw "dotnet SDK not found"
}

function Publish-Project {
    param([string]$DotnetExe, [string]$Csproj, [string]$OutDir)
    if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    Write-Host "  publish $(Split-Path $Csproj -Leaf) -> $OutDir"
    & $DotnetExe publish $Csproj -c Release -r win-x64 --self-contained false -o $OutDir /p:UseAppHost=true
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $Csproj" }
}

function Copy-Tree {
    param([string]$Source, [string]$Dest)
    if (-not (Test-Path $Source)) { throw "missing: $Source" }
    New-Item -ItemType Directory -Path $Dest -Force | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Dest -Recurse -Force
}

function Write-TextFile {
    param([string]$Path, [string]$Content)
    $dir = Split-Path $Path -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

Write-Host "============================================"
Write-Host "  TurboRama - Build Factory Pack (Phase 5)"
Write-Host "============================================"
Write-Host "Project: $ProjectRoot"
Write-Host "Output : $PackageDir"
Write-Host ""

if (-not (Test-Path $Sln)) { throw "solution missing: $Sln" }

$dotnetExe = Find-Dotnet -Preferred $Dotnet
Write-Host "dotnet : $dotnetExe"
& $dotnetExe --version
Write-Host ""

if (Test-Path $PackageDir) { Remove-Item $PackageDir -Recurse -Force }

$buildRoot = Join-Path $OutputRoot "_build-temp"
if (Test-Path $buildRoot) { Remove-Item $buildRoot -Recurse -Force }
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null

$uiOut = Join-Path $buildRoot "ui"
$launcherOut = Join-Path $buildRoot "launcher"
$watchdogOut = Join-Path $buildRoot "watchdog"
$maintenanceOut = Join-Path $buildRoot "maintenance"

Write-Host "[1/5] Publishing..."
Publish-Project $dotnetExe (Join-Path $ProjectRoot "src\TurboRama.UI\TurboRama.UI.csproj") $uiOut
Publish-Project $dotnetExe (Join-Path $ProjectRoot "src\TurboRama.Launcher\TurboRama.Launcher.csproj") $launcherOut
Publish-Project $dotnetExe (Join-Path $ProjectRoot "src\TurboRama.Watchdog\TurboRama.Watchdog.csproj") $watchdogOut
Publish-Project $dotnetExe (Join-Path $ProjectRoot "src\TurboRama.Maintenance\TurboRama.Maintenance.csproj") $maintenanceOut

Write-Host "[2/5] Package layout..."
@(
    $PackageDir,
    (Join-Path $PackageDir "Installer"),
    (Join-Path $PackageDir "App\Launcher"),
    (Join-Path $PackageDir "App\Watchdog"),
    (Join-Path $PackageDir "App\Maintenance"),
    (Join-Path $PackageDir "App\Tools"),
    (Join-Path $PackageDir "Config"),
    (Join-Path $PackageDir "Frontend"),
    (Join-Path $PackageDir "docs"),
    (Join-Path $PackageDir "scripts-internal")
) | ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }

Copy-Tree $uiOut (Join-Path $PackageDir "Installer")
Copy-Tree $launcherOut (Join-Path $PackageDir "App\Launcher")
Copy-Tree $watchdogOut (Join-Path $PackageDir "App\Watchdog")
Copy-Tree $maintenanceOut (Join-Path $PackageDir "App\Maintenance")

# Scripts de seguranca do PC de referencia (reaplicar lockdown se preciso)
$secSrcCandidates = @(
    "C:\TurboRama\App\Launcher",
    (Join-Path $ProjectRoot "pack-extra\Launcher")
)
foreach ($secSrc in $secSrcCandidates) {
    if (-not (Test-Path $secSrc)) { continue }
    foreach ($bat in @(
        "INSTALAR-SEGURANCA.bat",
        "DEPLOY-LAUNCHER-SEGURO.bat",
        "BACKUP-SEGURANCA-PANE.bat",
        "TESTAR-SEGURANCA.bat",
        "TESTAR-SEGURANCA-COMPLETO.bat"
    )) {
        $p = Join-Path $secSrc $bat
        if (Test-Path $p) {
            Copy-Item $p (Join-Path $PackageDir "App\Launcher\$bat") -Force
            Write-Host "  security bat: $bat"
        }
    }
    break
}

# Instalador prático na RAIZ do pack (1 clique no outro PC)
$setupDir = Join-Path $PackageDir "_setup-bin"
if (Test-Path $setupDir) { Remove-Item $setupDir -Recurse -Force }
Copy-Tree $uiOut $setupDir
# Copia deps do UI na raiz junto com Setup.exe (mesmo folder do Setup)
Get-ChildItem $setupDir -File | ForEach-Object {
  Copy-Item $_.FullName (Join-Path $PackageDir $_.Name) -Force
}
$setupExe = Join-Path $PackageDir "TurboRama.UI.exe"
$setupNamed = Join-Path $PackageDir "TurboRama.Setup.exe"
if (Test-Path $setupExe) {
  Copy-Item $setupExe $setupNamed -Force
  Write-Host "  TurboRama.Setup.exe na raiz do pack (instalacao completa)"
}
Remove-Item $setupDir -Recurse -Force -ErrorAction SilentlyContinue

# Runtime check helper for target PCs
$checkRuntime = @"
@echo off
chcp 65001 >nul
echo Checking .NET 8 Desktop Runtime...
where dotnet >nul 2>&1
if errorlevel 1 (
  echo WARNING: dotnet not in PATH.
  echo Install .NET 8 Desktop Runtime x64:
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  echo.
  pause
  exit /b 1
)
dotnet --list-runtimes 2>nul | findstr /i "Microsoft.WindowsDesktop.App 8." >nul
if errorlevel 1 (
  echo WARNING: Microsoft.WindowsDesktop.App 8.x not found.
  echo Install .NET 8 Desktop Runtime x64 before INSTALAR.bat
  pause
  exit /b 1
)
echo OK: .NET 8 Desktop runtime present.
exit /b 0
"@
Write-TextFile (Join-Path $PackageDir "CHECAR-DOTNET.bat") $checkRuntime

Write-Host "[3/5] Tools (Autologon)..."
$autoOk = $false
foreach ($a in @(
    "C:\TurboRama\App\Tools\Autologon64.exe",
    (Join-Path $ProjectRoot "..\TurboRamaFactoryShell\resources\Tools\Autologon64.exe"),
    "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\TurboRamaFactoryShell\resources\Tools\Autologon64.exe"
)) {
    if ($a -and (Test-Path $a)) {
        Copy-Item $a (Join-Path $PackageDir "App\Tools\Autologon64.exe") -Force
        Write-Host "  Autologon64 from $a"
        $autoOk = $true
        break
    }
}
if (-not $autoOk) {
    Write-Warning "Autologon64.exe missing - put it in App\Tools before kiosk install"
    Write-TextFile (Join-Path $PackageDir "App\Tools\PUT-Autologon64-HERE.txt") "Download Sysinternals Autologon64.exe into this folder."
}

$configJson = @'
{
  "schemaVersion": 1,
  "installationId": "00000000-0000-0000-0000-000000000000",
  "kioskUser": "Arcade",
  "installDirectory": "C:\\TurboRama",
  "frontendExecutable": "D:\\Turborama\\TurboRama.exe",
  "profile": "KioskBasic",
  "enableAutoLogon": true,
  "enableKeyboardFilter": true,
  "enableUwf": false,
  "enableBootBranding": false,
  "enableSecurityMenu": true,
  "showLoadingScreen": true,
  "watchdog": {
    "enabled": true,
    "restartDelaySeconds": 5,
    "maximumRestarts": 5
  },
  "productVersion": "2.0.0-alpha"
}
'@
Write-TextFile (Join-Path $PackageDir "Config\turborama.json") $configJson.Trim()

Write-TextFile (Join-Path $PackageDir "Frontend\LEIA-COPIAR-TURBORAMA.txt") @"
NAO e obrigatorio colocar o jogo aqui.
O instalador de fabrica so prepara o WINDOWS (kiosk + seguranca).

Depois que o Windows reiniciar no Arcade:
  1) Copie a pasta inteira D:\Turborama do PC modelo
     (ou do kit) para D:\Turborama neste PC.
  2) Confirme que existe D:\Turborama\TurboRama.exe
  3) Reinicie (ou deixe o Launcher/Watchdog reabrir o frontend)

Config aponta para: D:\Turborama\TurboRama.exe
"@

Write-Host "[4/5] Install scripts + docs..."

$seedBat = @"
@echo off
set "ROOT=C:\TurboRama"
set "SRC=%~dp0.."
mkdir "%ROOT%\App\Launcher" 2>nul
mkdir "%ROOT%\App\Watchdog" 2>nul
mkdir "%ROOT%\App\Maintenance" 2>nul
mkdir "%ROOT%\App\Tools" 2>nul
mkdir "%ROOT%\Frontend" 2>nul
mkdir "%ROOT%\Config" 2>nul
mkdir "%ROOT%\Logs\Installer" 2>nul
mkdir "%ROOT%\State" 2>nul
mkdir "%ROOT%\Backup" 2>nul
xcopy "%SRC%\App\Launcher\*" "%ROOT%\App\Launcher\" /E /Y /Q >nul
xcopy "%SRC%\App\Watchdog\*" "%ROOT%\App\Watchdog\" /E /Y /Q >nul
xcopy "%SRC%\App\Maintenance\*" "%ROOT%\App\Maintenance\" /E /Y /Q >nul
if exist "%SRC%\App\Tools\Autologon64.exe" copy /Y "%SRC%\App\Tools\Autologon64.exe" "%ROOT%\App\Tools\" >nul
if exist "%SRC%\Config\turborama.json" if not exist "%ROOT%\Config\turborama.json" copy /Y "%SRC%\Config\turborama.json" "%ROOT%\Config\" >nul
if exist "%SRC%\Frontend\*.exe" xcopy "%SRC%\Frontend\*.exe" "%ROOT%\Frontend\" /Y /Q >nul
exit /b 0
"@
Write-TextFile (Join-Path $PackageDir "scripts-internal\SEED-APP.bat") $seedBat

$installBat = @"
@echo off
chcp 65001 >nul
title TurboRama Secure - Installer
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Requesting Administrator...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
echo ============================================
echo   TurboRama Secure - Installer UI
echo ============================================
echo Flow: Preflight - Phase2 Kiosk - Phase3 Services - Reboot
echo Phase4 optional only if you accept the risk.
echo.
if not exist "%~dp0Installer\TurboRama.UI.exe" (
  echo ERROR: Installer\TurboRama.UI.exe missing
  pause
  exit /b 1
)
call "%~dp0scripts-internal\SEED-APP.bat"
start "" "%~dp0Installer\TurboRama.UI.exe"
exit /b 0
"@
Write-TextFile (Join-Path $PackageDir "INSTALAR.bat") $installBat

# Instalador completo 1 clique (Admin) = Windows igual PC referencia
$setupBat = @"
@echo off
chcp 65001 >nul
title TurboRama Secure - Windows Kiosk + Seguranca (fabrica)
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Solicitando Administrador...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
echo ============================================
echo   TurboRama - WINDOWS KIOSK DE PRODUCAO
echo   Arcade + autologon + servicos +
echo   Keyboard Filter + SecurityAgent
echo   (igual PC referencia)
echo ============================================
echo   Jogos/TurboRama: COPIAR D:\Turborama
echo   DEPOIS do reboot, quando o Windows estiver OK.
echo ============================================
echo.
if exist "%~dp0CHECAR-DOTNET.bat" call "%~dp0CHECAR-DOTNET.bat"
if errorlevel 1 (
  echo Instale .NET 8 Desktop Runtime e rode de novo.
  pause
  exit /b 1
)
set "SETUP=%~dp0TurboRama.Setup.exe"
if not exist "%SETUP%" set "SETUP=%~dp0Installer\TurboRama.UI.exe"
if not exist "%SETUP%" (
  echo ERRO: TurboRama.Setup.exe / Installer\TurboRama.UI.exe ausente
  pause
  exit /b 1
)
set "RESULT=%TEMP%\turborama-factory-full.txt"
echo Instalando Windows kiosk + seguranca... aguarde.
"%SETUP%" --install-full --result "%RESULT%"
set ERR=%ERRORLEVEL%
echo.
type "%RESULT%" 2>nul
echo.
if %ERR% equ 0 (
  echo OK - Windows pronto.
  echo 1 REINICIE o PC
  echo 2 Depois copie D:\Turborama (jogos) para este PC
  echo 3 Confirme D:\Turborama\TurboRama.exe
) else (
  echo FALHA - veja C:\TurboRama\Logs\Installer\
)
pause
exit /b %ERR%
"@
Write-TextFile (Join-Path $PackageDir "INSTALAR-COMPLETO.bat") $setupBat
Write-TextFile (Join-Path $PackageDir "SETUP.bat") $setupBat

$autoBat = @"
@echo off
chcp 65001 >nul
title TurboRama Secure - Automatic install
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
set "UI=%~dp0Installer\TurboRama.UI.exe"
set "RESULT=%TEMP%\turborama-auto-install.txt"
if not exist "%UI%" (
  echo ERROR: UI missing
  pause
  exit /b 1
)
echo [1/4] Seed App...
call "%~dp0scripts-internal\SEED-APP.bat"
echo [2/4] Phase 2 Kiosk...
"%UI%" --phase2 --quiet --result "%RESULT%"
if errorlevel 1 (
  echo FAIL Phase 2
  type "%RESULT%" 2>nul
  pause
  exit /b 1
)
echo [3/4] Phase 3 Services...
"%UI%" --phase3 --quiet --result "%RESULT%"
if errorlevel 1 (
  echo FAIL Phase 3
  type "%RESULT%" 2>nul
  pause
  exit /b 1
)
echo [4/4] OK - reboot for Arcade autologon
type "%RESULT%" 2>nul
pause
exit /b 0
"@
Write-TextFile (Join-Path $PackageDir "INSTALAR-AUTOMATICO.bat") $autoBat

$preflightBat = @"
@echo off
chcp 65001 >nul
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
"%~dp0Installer\TurboRama.UI.exe" --preflight
exit /b %ERRORLEVEL%
"@
Write-TextFile (Join-Path $PackageDir "PREFLIGHT.bat") $preflightBat

$statusBat = @"
@echo off
chcp 65001 >nul
echo === Services ===
sc query TurboRamaWatchdog
sc query TurboRamaMaintenance
echo.
echo === Autologon ===
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultUserName
echo.
echo === Arcade account ===
net user Arcade 2>nul || echo Arcade missing
echo.
echo === Lock ===
if exist C:\TurboRama\State\maintenance.lock (type C:\TurboRama\State\maintenance.lock) else (echo no lock)
pause
"@
Write-TextFile (Join-Path $PackageDir "STATUS.bat") $statusBat

$reinstallBat = @"
@echo off
chcp 65001 >nul
title Reinstall TurboRama services
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
call "%~dp0scripts-internal\SEED-APP.bat"
"%~dp0Installer\TurboRama.UI.exe" --phase3 --quiet --result "%TEMP%\turborama-phase3.txt"
type "%TEMP%\turborama-phase3.txt" 2>nul
sc query TurboRamaWatchdog
sc query TurboRamaMaintenance
pause
"@
Write-TextFile (Join-Path $PackageDir "REINSTALAR-SERVICOS.bat") $reinstallBat

$validateBat = @"
@echo off
chcp 65001 >nul
title TurboRama - Phase 6 Factory Accept
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
set "UI=%~dp0Installer\TurboRama.UI.exe"
set "RESULT=%TEMP%\turborama-phase6.txt"
if not exist "%UI%" (
  echo ERROR: UI missing
  pause
  exit /b 1
)
echo Phase 6 validate + clear-locks...
"%UI%" --validate --clear-locks --quiet --result "%RESULT%"
set ERR=%ERRORLEVEL%
type "%RESULT%" 2>nul
echo.
if %ERR% equ 0 (echo ACCEPT OK) else (echo ACCEPT FAILED)
pause
exit /b %ERR%
"@
Write-TextFile (Join-Path $PackageDir "VALIDAR-ACEITE.bat") $validateBat

$readme = @"
TurboRama Secure - Factory Pack (Windows kiosk de producao)
Built: $Stamp
Version: 2.0.0-alpha

======== O QUE ESTE PACK FAZ ========
Prepara o WINDOWS igual ao PC de referencia:
  - Conta Arcade + autologon
  - Launcher shell + Watchdog + Maintenance
  - Keyboard Filter (IoT) + politicas CAD
  - SecurityAgent (Ctrl+End) + keep-alive
NAO instala jogos. TurboRama = copiar D:\Turborama depois.

======== START HERE (PC formatado) ========
1. Windows 10/11 x64 IoT/Enterprise preferivel (Keyboard Filter).
2. Conta Admin de recuperacao. NAO instalar logado como Arcade.
3. .NET 8 Desktop Runtime.
4. Copie esta pasta inteira para o PC.
5. Clique direito INSTALAR-COMPLETO.bat -> Executar como administrador
   (ou TurboRama.Setup.exe como Admin)
6. REINICIE -> autologon Arcade + filtro de teclado.
7. COPIE a pasta D:\Turborama (do kit/PC modelo) para D:\Turborama.
8. Confirme D:\Turborama\TurboRama.exe — o Launcher abre sozinho.
9. Senha kiosk se login manual: Lz2026@$
10. Admin = manutencao (Explorer).

Alternativas:
- INSTALAR.bat = so abre a UI
- INSTALAR-AUTOMATICO.bat = Fase2+3 quiet (sem seguranca completa)
- VALIDAR-ACEITE.bat = so testa
- App\Launcher\INSTALAR-SEGURANCA.bat = reaplicar so lockdown

Keep an Administrator recovery account (e.g. Admin).

======== STRUCTURE ========
TurboRama.Setup.exe  << INSTALADOR PRINCIPAL (1 clique)
INSTALAR-COMPLETO.bat / SETUP.bat
Installer\TurboRama.UI.exe
App\Launcher\ Watchdog\ Maintenance\ Tools\
Config\ Frontend\
PACK-HASHES.sha256
CHECAR-DOTNET.bat
VALIDAR-ACEITE.bat

======== PROJECT PHASES ========
0 Foundation
1 Baseline (via installer)
2 Kiosk (Arcade, shell, autologon, policies)
3 Services (Watchdog + Maintenance)
4 KeyboardFilter + SecurityAgent (ON no install-full de producao)
5 This factory pack
6 Post-install accept / security validation

======== REQUIREMENTS ========
- Windows 10/11 x64
- .NET 8 Desktop Runtime
  https://dotnet.microsoft.com/download/dotnet/8.0
- Administrator account
- Do NOT install while logged in as Arcade

======== PHASE 4 ========
In UI, only check modules you accept the risk for.
UWF needs IoT/Enterprise. Keyboard Filter needs Embedded lockdown.
Safe default: nothing checked.
"@
Write-TextFile (Join-Path $PackageDir "00-COMECE-AQUI.txt") $readme
Write-TextFile (Join-Path $PackageDir "LEIA-ME-FABRICA.txt") $readme

foreach ($doc in @(
    "REGRAS-NAO-FAZER.md",
    "COMPARATIVO-E-CAMINHO.md",
    "MAPA-LEGADO.md",
    "MANUAL-RECUPERACAO-OFFLINE.md",
    "ROTEIRO-TESTES-VM.md",
    "DESCRITIVO-COMPLETO-PROJETO.md",
    "COMPARATIVO-PROPOSTA-VS-IMPLEMENTADO.md"
)) {
    $src = Join-Path $ProjectRoot "docs\$doc"
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $PackageDir "docs\$doc") -Force
    }
}
# one-shot copy without full republish: also leave docs on D pack if regenerating partially


$exeList = Get-ChildItem $PackageDir -Recurse -Filter "*.exe" | ForEach-Object {
    "  $($_.FullName.Substring($PackageDir.Length + 1)) ($([math]::Round($_.Length/1KB,1)) KB)"
}
Write-TextFile (Join-Path $PackageDir "PACK-MANIFEST.txt") @"
TurboRama-Factory-Pack
Built: $Stamp
Project: $ProjectRoot

EXEs:
$($exeList -join "`r`n")
"@

Write-Host "[5/6] PACK-HASHES.sha256 (integridade)..."
$hashLines = New-Object System.Collections.Generic.List[string]
$hashLines.Add("# TurboRama factory pack hashes (SHA256)")
$hashLines.Add("# Generated: $Stamp")
$exeFiles = Get-ChildItem $PackageDir -Recurse -Filter "*.exe" | Sort-Object FullName
foreach ($f in $exeFiles) {
    $h = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash
    $rel = $f.FullName.Substring($PackageDir.Length + 1).Replace('\','/')
    $hashLines.Add("$h  $rel")
}
$hashFile = Join-Path $PackageDir "PACK-HASHES.sha256"
[System.IO.File]::WriteAllLines($hashFile, $hashLines)
Write-Host "  Hashes: $($exeFiles.Count) exes -> PACK-HASHES.sha256"

Write-Host "[6/6] ZIP..."
$zipPath = Join-Path $OutputRoot "$PackageName.zip"
if (-not $SkipZip) {
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $PackageDir -DestinationPath $zipPath -Force
    Write-Host "  ZIP: $zipPath"
}

try { Remove-Item $buildRoot -Recurse -Force -ErrorAction SilentlyContinue } catch {}

Write-Host ""
Write-Host "============================================"
Write-Host "  PACK OK"
Write-Host "============================================"
Write-Host "Folder: $PackageDir"
if (-not $SkipZip) { Write-Host "ZIP   : $zipPath" }
exit 0
