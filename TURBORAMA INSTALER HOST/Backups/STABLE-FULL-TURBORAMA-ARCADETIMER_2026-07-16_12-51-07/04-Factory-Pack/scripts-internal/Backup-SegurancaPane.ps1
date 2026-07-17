#Requires -Version 5.1
<#
.SYNOPSIS
  Backup completo anti-pane do TurboRama (fase final).
  Copia: codigo fonte, deploy C:\TurboRama, factory pack, registo seguranca, config.
  Gera MANIFEST + SHA256 + RESTAURAR-INSTRUCOES.txt
  Nao interrompe kiosk de forma destrutiva (copia; mata processos so se -ForceKill).
#>
param(
    [string]$BackupRoot = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Backups",
    [string]$ProjectRoot = "",
    [string]$Label = "FASE-FINAL-ANTI-PANE",
    [switch]$ForceKill,
    [switch]$SkipFactoryPack,
    [switch]$SkipLiveTurboRama,
    [switch]$SkipArcadeTimer
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $candidate = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama"
    if (Test-Path (Join-Path $candidate "src\TurboRama.Launcher")) {
        $ProjectRoot = $candidate
    }
    else {
        $ProjectRoot = Split-Path -Parent $PSScriptRoot
    }
}
$Stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$Dest = Join-Path $BackupRoot "${Label}_$Stamp"
$LogFile = Join-Path $Dest "BACKUP-LOG.txt"

function L([string]$m) {
    $line = (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + "  " + $m
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
    Write-Host $line
}

function Invoke-Robo {
    param([string]$Src, [string]$Dst, [string[]]$Xd = @())
    if (-not (Test-Path $Src)) {
        L "SKIP (ausente): $Src"
        return $false
    }
    New-Item -ItemType Directory -Force -Path $Dst | Out-Null
    $args = @($Src, $Dst, "/E", "/R:2", "/W:2", "/NFL", "/NDL", "/NJH", "/NJS", "/nc", "/ns", "/np")
    foreach ($x in $Xd) {
        $args += "/XD"
        $args += $x
    }
    & robocopy @args | Out-Null
    $code = $LASTEXITCODE
    # robocopy: 0-7 success-ish
    if ($code -ge 8) {
        L "WARN robocopy exit=$code  $Src -> $Dst"
        return $false
    }
    L "OK robocopy exit=$code  $Src -> $Dst"
    return $true
}

New-Item -ItemType Directory -Force -Path $Dest | Out-Null
# create log first
"" | Set-Content $LogFile -Encoding UTF8
L "=== BACKUP-SEGURANCA-PANE START ==="
L "Dest=$Dest"
L "Project=$ProjectRoot"

$ok = 0
$fail = 0

# 1) Codigo fonte (sem bin/obj)
L "[1/7] Codigo fonte do projecto..."
if (Invoke-Robo -Src $ProjectRoot -Dst (Join-Path $Dest "01-Projeto-Fonte") -Xd @("bin", "obj", ".git", "node_modules", "publish")) {
    $ok++
}
else {
    $fail++
}

# 2) publish se existir
L "[2/7] publish\ (UI/Installer build)..."
$pub = Join-Path $ProjectRoot "publish"
if (Test-Path $pub) {
    if (Invoke-Robo -Src $pub -Dst (Join-Path $Dest "02-Publish")) { $ok++ } else { $fail++ }
}
else {
    L "SKIP publish"
}

# 3) C:\TurboRama live
L "[3/7] C:\TurboRama (deploy live)..."
if (-not $SkipLiveTurboRama) {
    if ($ForceKill) {
        taskkill /F /IM TurboRama.Launcher.exe 2>$null | Out-Null
        Start-Sleep -Seconds 1
    }
    if (Test-Path "C:\TurboRama") {
        # Excluir Logs enormes antigos opcional - copiar tudo critico
        if (Invoke-Robo -Src "C:\TurboRama" -Dst (Join-Path $Dest "03-TurboRama-Live") -Xd @()) {
            $ok++
        }
        else {
            $fail++
        }
    }
    else {
        L "SKIP C:\TurboRama ausente"
    }
}

# 4) Factory pack
L "[4/7] Factory Pack..."
if (-not $SkipFactoryPack) {
    $pack = "D:\tr-factory-pack\TurboRama-Factory-Pack"
    $zip = "D:\tr-factory-pack\TurboRama-Factory-Pack.zip"
    if (Test-Path $pack) {
        if (Invoke-Robo -Src $pack -Dst (Join-Path $Dest "04-Factory-Pack")) { $ok++ } else { $fail++ }
    }
    else {
        L "SKIP factory pack folder"
    }
    if (Test-Path $zip) {
        Copy-Item $zip (Join-Path $Dest "04-TurboRama-Factory-Pack.zip") -Force
        L "OK zip factory pack copiado"
        $ok++
    }
}

# 4b) Arcade Timer estavel + pacote QA internacional
L "[4b] Arcade Timer (estavel + INTL QA pack)..."
if (-not $SkipArcadeTimer) {
    $timerRoot = "D:\Backup-Instaladores-Compiladores-Turborama\TurboRamaArcadeTimer_Proposta"
    $timerDest = Join-Path $Dest "06-ArcadeTimer-Stable"
    New-Item -ItemType Directory -Force -Path $timerDest | Out-Null
    if (Test-Path $timerRoot) {
        if (Invoke-Robo -Src $timerRoot -Dst (Join-Path $timerDest "Proposta-Fonte") -Xd @("bin", "obj", ".git")) {
            $ok++
        }
        else { $fail++ }
    }
    else {
        L "SKIP ArcadeTimer proposta ausente"
    }
    $intl = Join-Path $timerRoot "dist\TurboRama-ArcadeTimer-INTL-QA-Pack"
    if (Test-Path $intl) {
        if (Invoke-Robo -Src $intl -Dst (Join-Path $timerDest "INTL-QA-Pack")) { $ok++ } else { $fail++ }
    }
    $ent = Join-Path $timerRoot "dist\TurboRama-ArcadeTimer-0.1.3-enterprise"
    if (Test-Path $ent) {
        if (Invoke-Robo -Src $ent -Dst (Join-Path $timerDest "0.1.3-enterprise")) { $ok++ } else { $fail++ }
    }
    foreach ($z in @(
            (Join-Path $timerRoot "dist\TurboRama-ArcadeTimer-INTL-QA-Pack.zip"),
            (Join-Path $timerRoot "dist\TurboRama-ArcadeTimer-0.1.3-enterprise.zip")
        )) {
        if (Test-Path $z) {
            Copy-Item $z $timerDest -Force
            L "OK zip timer: $(Split-Path $z -Leaf)"
        }
    }
    @"
Arcade Timer stable snapshot
Version: 0.1.3-enterprise
QA: PASS=26 FAIL=0 (INTL pack)
Deploy product: 0.1.3-enterprise or INTL-QA-Pack\product
Lab tests: INTL-QA-Pack\tests\RUN-ALL-TESTS.bat
"@ | Set-Content (Join-Path $timerDest "VERSION-STABLE.txt") -Encoding UTF8
}

# 5) Estado de seguranca (reg + servicos)
L "[5/8] Snapshot registo/servicos..."
$sec = Join-Path $Dest "05-Security-State"
New-Item -ItemType Directory -Force -Path $sec | Out-Null
try {
    sc.exe query MsKeyboardFilter > (Join-Path $sec "MsKeyboardFilter-query.txt") 2>&1
    sc.exe qc MsKeyboardFilter > (Join-Path $sec "MsKeyboardFilter-qc.txt") 2>&1
    sc.exe query TurboRamaWatchdog > (Join-Path $sec "Watchdog-query.txt") 2>&1
    sc.exe query TurboRamaMaintenance > (Join-Path $sec "Maintenance-query.txt") 2>&1
    reg export "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" (Join-Path $sec "KeyboardFilter.reg") /y 2>$null
    reg export "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" (Join-Path $sec "Run-HKLM.reg") /y 2>$null
    reg export "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" (Join-Path $sec "Winlogon.reg") /y 2>$null
    if (Test-Path "C:\TurboRama\Config\turborama.json") {
        Copy-Item "C:\TurboRama\Config\turborama.json" (Join-Path $sec "turborama.json") -Force
    }
    if (Test-Path "C:\TurboRama\Logs\security-agent-alive.txt") {
        Copy-Item "C:\TurboRama\Logs\security-agent-alive.txt" (Join-Path $sec "security-agent-alive.txt") -Force
    }
    # WEKF sample
    try {
        $wekf = Get-CimInstance -Namespace root\standardcimv2\embedded -ClassName WEKF_PredefinedKey -ErrorAction Stop |
            Where-Object { $_.Id -match "Ctrl\+Alt\+Del|Ctrl\+End|Windows|Alt\+Tab" } |
            Select-Object Id, Enabled
        $wekf | Format-Table | Out-String | Set-Content (Join-Path $sec "WEKF-sample.txt")
    }
    catch {
        "WEKF: $($_.Exception.Message)" | Set-Content (Join-Path $sec "WEKF-sample.txt")
    }
    L "OK security snapshot"
    $ok++
}
catch {
    L "FAIL security snapshot: $($_.Exception.Message)"
    $fail++
}

# 6) Hashes dos EXEs criticos no backup
L "[6/7] SHA256 dos executaveis criticos..."
$hashFile = Join-Path $Dest "SHA256-CRITICAL.txt"
$critical = @(
    "01-Projeto-Fonte\src\TurboRama.Launcher\*.cs",
    "03-TurboRama-Live\App\Launcher\TurboRama.Launcher.exe",
    "03-TurboRama-Live\App\Launcher\TurboRama.Launcher.dll",
    "03-TurboRama-Live\App\Watchdog\TurboRama.Watchdog.exe",
    "04-Factory-Pack\TurboRama.Setup.exe",
    "04-Factory-Pack\App\Launcher\TurboRama.Launcher.exe",
    "04-Factory-Pack\Installer\TurboRama.UI.exe"
)
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("SHA256 backup $Stamp")
Get-ChildItem $Dest -Recurse -Include *.exe,*.dll -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.FullName -match "Launcher|Watchdog|Maintenance|TurboRama\.(UI|Setup|Installation|Windows|Configuration|Core)" -and
        $_.Name -notmatch "System\.|Microsoft\.|runtime"
    } |
    Select-Object -First 80 |
    ForEach-Object {
        try {
            $h = Get-FileHash $_.FullName -Algorithm SHA256
            $rel = $_.FullName.Substring($Dest.Length + 1)
            $lines.Add("$($h.Hash)  $rel")
        }
        catch { }
    }
$lines | Set-Content $hashFile -Encoding ASCII
L "OK hashes -> SHA256-CRITICAL.txt ($($lines.Count) linhas)"

# 7) Instrucoes de restauro + verificacao rapida
L "[7/7] Instrucoes + auto-teste de integridade..."
$instr = @"
============================================================
  RESTAURAR TURBORAMA APOS PANE
  Backup: FASE-FINAL-ANTI-PANE_$Stamp
============================================================

CONTEUDO DESTE BACKUP
  01-Projeto-Fonte     Codigo fonte completo (sem bin/obj)
  02-Publish           Builds publish se existiam
  03-TurboRama-Live    Copia de C:\TurboRama no momento do backup
  04-Factory-Pack      Pack de fabrica (pasta)
  04-TurboRama-Factory-Pack.zip
  05-Security-State    Registos, servicos, config, WEKF
  SHA256-CRITICAL.txt  Hashes
  BACKUP-LOG.txt       Log desta corrida

RESTAURAR CODIGO (PC de desenvolvimento)
  1. Feche Visual Studio / Grok / processos.
  2. Renomeie o projecto actual para ...\Projeto Novo TurboRama-BROKEN
  3. Copie 01-Projeto-Fonte para:
     D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama
  4. Abra TurboRama.sln e compile (ou GERAR-PACK-FABRICA.bat)

RESTAURAR KIOSK (PC arcade)
  1. Conta Admin, Explorer.
  2. Pare servicos: sc stop TurboRamaWatchdog & sc stop TurboRamaMaintenance
  3. taskkill /F /IM TurboRama.Launcher.exe
  4. Copie 03-TurboRama-Live\* para C:\TurboRama\  (ou use o Factory Pack)
  5. Melhor: copie 04-Factory-Pack e execute INSTALAR-COMPLETO.bat como Admin
  6. Reinicie o PC
  7. PIN/senha kiosk: Lz2026@`$
  8. Ctrl+End = menu seguranca

RESTAURAR SO FILTRO TECLADO
  1. Importar 05-Security-State\KeyboardFilter.reg (Admin)
  2. sc config MsKeyboardFilter start= auto
  3. Reiniciar PC
  4. Ou: C:\TurboRama\App\Launcher\INSTALAR-SEGURANCA.bat (Admin)

VERIFICAR INTEGRIDADE
  powershell -NoProfile -Command "Get-FileHash '...\\App\\Launcher\\TurboRama.Launcher.exe' -Algorithm SHA256"
  Compare com SHA256-CRITICAL.txt

CONTACTO / NOTAS
  Pack ZIP e a forma mais rapida de repor instalador limpo.
  Mantenha este backup em D: E num USB externo.
"@
[System.IO.File]::WriteAllText((Join-Path $Dest "RESTAURAR-INSTRUCOES.txt"), $instr, [System.Text.UTF8Encoding]::new($false))

# Auto-test: pastas obrigatorias e contagem de ficheiros
$required = @(
    "01-Projeto-Fonte\src\TurboRama.Launcher\Program.cs",
    "01-Projeto-Fonte\src\TurboRama.Launcher\SystemSecurityForm.cs",
    "01-Projeto-Fonte\src\TurboRama.Launcher\SecurityAgentHost.cs",
    "01-Projeto-Fonte\src\TurboRama.Configuration\FactoryDefaults.cs",
    "01-Projeto-Fonte\scripts\Build-FactoryPack.ps1",
    "RESTAURAR-INSTRUCOES.txt",
    "SHA256-CRITICAL.txt",
    "BACKUP-LOG.txt"
)
$testFail = 0
$testOk = 0
foreach ($r in $required) {
    $p = Join-Path $Dest $r
    if (Test-Path $p) {
        $testOk++
        L "TEST OK  $r"
    }
    else {
        $testFail++
        L "TEST FAIL missing $r"
    }
}

# Factory pack critical
$fpLauncher = Join-Path $Dest "04-Factory-Pack\App\Launcher\TurboRama.Launcher.exe"
if (Test-Path $fpLauncher) {
    $testOk++
    L "TEST OK  Factory Launcher.exe"
}
else {
    # zip may exist instead
    if (Test-Path (Join-Path $Dest "04-TurboRama-Factory-Pack.zip")) {
        $testOk++
        L "TEST OK  Factory ZIP (sem pasta extraida)"
    }
    else {
        $testFail++
        L "TEST FAIL  sem Factory Pack nem ZIP"
    }
}

# Live launcher if copied
$liveL = Join-Path $Dest "03-TurboRama-Live\App\Launcher\TurboRama.Launcher.exe"
if (Test-Path $liveL) {
    $testOk++
    L "TEST OK  Live Launcher.exe"
}
else {
    L "TEST WARN  Live Launcher nao copiado"
}

# Size summary
$sizeMB = [math]::Round(((Get-ChildItem $Dest -Recurse -File -EA SilentlyContinue | Measure-Object Length -Sum).Sum / 1MB), 1)
L "SIZE_MB=$sizeMB"
L "COPY_OK=$ok COPY_WARN=$fail TEST_OK=$testOk TEST_FAIL=$testFail"

$readme = @"
FASE-FINAL-ANTI-PANE_$Stamp
Size: $sizeMB MB
Copy ok/warn: $ok / $fail
Integrity tests: $testOk ok, $testFail fail
PIN kiosk/menu: Lz2026@`$
Ver RESTAURAR-INSTRUCOES.txt
"@
Set-Content (Join-Path $Dest "README.txt") $readme -Encoding UTF8

# Marker for automation
Set-Content (Join-Path $Dest "BACKUP-OK.flag") "ok=$($testFail -eq 0) sizeMB=$sizeMB" -Encoding ASCII

# 8) Marcadores LATEST / ESTAVEL (sistema + pasta de backups)
L "[8/8] Atualizar LATEST-STABLE / sistema..."
try {
    $latestTxt = Join-Path $BackupRoot "LATEST-STABLE-FULL.txt"
    $latestLink = Join-Path $BackupRoot "LATEST-STABLE-FULL"
    @"
STAMP=$Stamp
DEST=$Dest
LABEL=$Label
SIZE_MB=$sizeMB
TEST_OK=$testOk
TEST_FAIL=$testFail
PIN=Lz2026@`$
ARCADE_TIMER=0.1.3-enterprise
DATE=$(Get-Date -Format o)
"@ | Set-Content $latestTxt -Encoding UTF8

    # Junction/copy marker (nao apagar backup antigo)
    $markerDir = Join-Path $BackupRoot "_LATEST-STABLE-POINTER"
    New-Item -ItemType Directory -Force -Path $markerDir | Out-Null
    Set-Content (Join-Path $markerDir "PATH.txt") $Dest -Encoding UTF8
    Copy-Item (Join-Path $Dest "README.txt") (Join-Path $markerDir "README.txt") -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $Dest "BACKUP-OK.flag") (Join-Path $markerDir "BACKUP-OK.flag") -Force -ErrorAction SilentlyContinue

    # Sistema C:\TurboRama\Backup
    $sysBackup = "C:\TurboRama\Backup"
    if (Test-Path "C:\TurboRama") {
        New-Item -ItemType Directory -Force -Path $sysBackup | Out-Null
        $sysSnap = Join-Path $sysBackup "STABLE-SNAPSHOT_$Stamp"
        New-Item -ItemType Directory -Force -Path $sysSnap | Out-Null
        # Snapshot leve do que e critico no live (nao duplicar multi-GB se Logs grandes)
        foreach ($sub in @("App", "Config", "Launcher")) {
            $s = Join-Path "C:\TurboRama" $sub
            if (Test-Path $s) {
                Invoke-Robo -Src $s -Dst (Join-Path $sysSnap $sub) -Xd @("logs", "Logs") | Out-Null
            }
        }
        Copy-Item $latestTxt (Join-Path $sysBackup "LATEST-STABLE-FULL.txt") -Force
        Set-Content (Join-Path $sysBackup "LATEST-STABLE-PATH.txt") $Dest -Encoding UTF8
        @"
Stable system snapshot $Stamp
Full backup: $Dest
Arcade Timer: 0.1.3-enterprise (in full backup 06-ArcadeTimer-Stable)
PIN kiosk: Lz2026@`$
"@ | Set-Content (Join-Path $sysSnap "README.txt") -Encoding UTF8
        Set-Content (Join-Path $sysBackup "LATEST-STABLE-SNAPSHOT.txt") $sysSnap -Encoding UTF8
        L "OK sistema C:\TurboRama\Backup atualizado"
    }
    L "OK LATEST-STABLE-FULL.txt"
}
catch {
    L "WARN latest markers: $($_.Exception.Message)"
}

L "=== BACKUP END Dest=$Dest ==="

# Open explorer
try {
    Start-Process explorer.exe $Dest
}
catch { }

if ($testFail -gt 0) {
    exit 2
}
exit 0
