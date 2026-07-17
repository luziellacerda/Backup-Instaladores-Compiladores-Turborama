# Pente fino + retestes Arcade Timer x TurboRama
$ErrorActionPreference = 'Continue'
$results = New-Object System.Collections.Generic.List[object]

function Add-R {
    param([string]$id, [string]$sev, [string]$status, [string]$detail)
    $results.Add([pscustomobject]@{ Id = $id; Sev = $sev; Status = $status; Detail = $detail })
    Write-Host "[$status][$sev] $id - $detail"
}

$timerDir = 'D:\Backup-Instaladores-Compiladores-Turborama\TurboRamaArcadeTimer_Proposta\dist\TurboRama-ArcadeTimer-0.1.2-test'
$srcDir   = 'D:\Backup-Instaladores-Compiladores-Turborama\TurboRamaArcadeTimer_Proposta\src\TurboRama.ArcadeTimer'
$exe      = Join-Path $timerDir 'TurboRama.ArcadeTimer.exe'
$creditPath = Join-Path $timerDir 'credit.json'
$configPath = Join-Path $timerDir 'config.json'
$labConfig  = Join-Path $timerDir 'config.lab.json'

function Stop-Timer {
    Get-Process TurboRama.ArcadeTimer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}
function Stop-Notepad {
    Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force
}
function Start-Timer {
    Stop-Timer
    return Start-Process $exe -WorkingDirectory $timerDir -PassThru
}
function Read-Credit {
    try {
        $j = Get-Content $creditPath -Raw -ErrorAction Stop | ConvertFrom-Json
        if ($null -ne $j.RemainingSeconds) { return [int64]$j.RemainingSeconds }
        if ($null -ne $j.remainingSeconds) { return [int64]$j.remainingSeconds }
        return -1
    } catch { return -1 }
}
function Write-Credit {
    param([int64]$sec)
    $o = @{ RemainingSeconds = $sec; TotalCoinsAccepted = 1; UpdatedAt = (Get-Date).ToString('o') }
    $o | ConvertTo-Json | Set-Content -Path $creditPath -Encoding UTF8
}
function Restore-LabConfig {
    Copy-Item $labConfig $configPath -Force
}

$code = @'
using System;
using System.Runtime.InteropServices;
public class PenteKB {
  [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
  public static void F10() {
    keybd_event(0x79, 0, 0, UIntPtr.Zero);
    keybd_event(0x79, 0, 2, UIntPtr.Zero);
  }
}
public class WinClose {
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
  public const uint WM_CLOSE = 0x0010;
}
'@
Add-Type -TypeDefinition $code -ErrorAction SilentlyContinue

# ========== STATIC ==========
$consumeCode = Get-Content (Join-Path $srcDir 'Services\CreditManager.cs') -Raw
if ($consumeCode -match 'Math\.Max\(1,\s*\(long\)Math\.Floor\(elapsed') {
    Add-R 'CODE.TIME_FAST' 'CRITICO' 'BUG' 'Consume Max(1,Floor): intervalo 250ms drena ~4x tempo real'
} else {
    Add-R 'CODE.TIME_FAST' 'CRITICO' 'PASS' 'Consume rate OK'
}

$tf = Get-Content (Join-Path $srcDir 'TimerForm.cs') -Raw
if ($tf -match 'if \(!_config\.CountOnlyWhileEmulatorIsRunning\)') {
    Add-R 'CODE.COUNT_ALWAYS' 'ALTO' 'PASS' 'Branch count-always existe'
} else {
    Add-R 'CODE.COUNT_ALWAYS' 'ALTO' 'BUG' 'CountOnlyWhileEmulatorIsRunning=false: tempo nunca desce'
}

$cfgCs = Get-Content (Join-Path $srcDir 'Configuration\TimerConfig.cs') -Raw
if ($cfgCs -match 'TurboRama\.Watchdog' -and $cfgCs -match 'TurboRama\.Launcher') {
    Add-R 'CODE.DEFAULT_PROT' 'MEDIO' 'PASS' 'Defaults protegem Launcher/Watchdog'
} else {
    Add-R 'CODE.DEFAULT_PROT' 'MEDIO' 'BUG' 'Defaults ProtectedProcesses sem Launcher/Watchdog'
}

if ($tf -match 'BeginInvoke|Invoke\(') {
    Add-R 'CODE.HOOK_UI' 'MEDIO' 'PASS' 'Hook usa Invoke para UI'
} else {
    Add-R 'CODE.HOOK_UI' 'MEDIO' 'WARN' 'KeyPressed->UI sem Invoke: risco cross-thread'
}

$cs = Get-Content (Join-Path $srcDir 'Services\CreditStore.cs') -Raw
if ($cs -match 'PropertyNameCaseInsensitive\s*=\s*true') {
    Add-R 'CODE.CREDIT_CASE' 'BAIXO' 'PASS' 'Credit JSON case-insensitive'
} else {
    Add-R 'CODE.CREDIT_CASE' 'BAIXO' 'BUG' 'CreditStore case-sensitive: camelCase = credito 0'
}

$prog = Get-Content (Join-Path $srcDir 'Program.cs') -Raw
if ($prog -match 'MessageBox' -and $prog -match 'Mutex') {
    Add-R 'CODE.MUTEX_MSG' 'BAIXO' 'WARN' '2a instancia MessageBox fica viva ate clicar'
}

$ec = Get-Content (Join-Path $srcDir 'Services\EmulatorController.cs') -Raw
if ($ec -match 'entireProcessTree:\s*true') {
    Add-R 'CODE.KILL_TREE' 'MEDIO' 'WARN' 'Kill(entireProcessTree) se whitelist errada'
}

if ($tf -match 'private void LoopTick[\s\S]{0,80}try') {
    Add-R 'CODE.LOOP_TRY' 'BAIXO' 'PASS' 'LoopTick tem try'
} else {
    Add-R 'CODE.LOOP_TRY' 'BAIXO' 'WARN' 'LoopTick sem try: excecao pode parar timer UI'
}

# ========== RUNTIME ==========
Stop-Timer
Stop-Notepad
Restore-LabConfig

$kf = (sc.exe query MsKeyboardFilter | Out-String) -match 'RUNNING'
$cad = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter').'Ctrl+Alt+Del'
$agent = Test-Path 'C:\TurboRama\Logs\security-agent-alive.txt'
$es0 = [bool](Get-Process emulationstation -ErrorAction SilentlyContinue)
if ($kf) { Add-R 'K0.FILTER' 'CRITICO' 'PASS' 'MsKeyboardFilter RUNNING' } else { Add-R 'K0.FILTER' 'CRITICO' 'FAIL' 'Filter down' }
if ($cad -eq 'Blocked') { Add-R 'K0.CAD' 'CRITICO' 'PASS' 'CAD Blocked' } else { Add-R 'K0.CAD' 'CRITICO' 'FAIL' ("CAD=" + $cad) }
if ($agent) { Add-R 'K0.AGENT' 'ALTO' 'PASS' 'Agent heartbeat existe' } else { Add-R 'K0.AGENT' 'ALTO' 'FAIL' 'Sem heartbeat' }
if ($es0) { Add-R 'K0.ES' 'ALTO' 'PASS' 'ES running' } else { Add-R 'K0.ES' 'ALTO' 'FAIL' 'ES down' }

$p1 = Start-Timer
Start-Sleep 2
if (-not $p1.HasExited) { Add-R 'T1.START' 'ALTO' 'PASS' ("pid=" + $p1.Id) } else { Add-R 'T1.START' 'ALTO' 'FAIL' 'exit immediate' }
$null = Start-Process $exe -WorkingDirectory $timerDir -PassThru
Start-Sleep 2
$cnt = @(Get-Process TurboRama.ArcadeTimer -ErrorAction SilentlyContinue).Count
if ($cnt -eq 1) { Add-R 'T1.SINGLE' 'MEDIO' 'PASS' '1 instancia' }
elseif ($cnt -ge 2) { Add-R 'T1.SINGLE' 'MEDIO' 'FAIL' ("$cnt processos (2a com dialog)") }
else { Add-R 'T1.SINGLE' 'MEDIO' 'FAIL' '0 processos' }
Stop-Timer

Write-Credit 0
$null = Start-Timer
Start-Sleep 2
[PenteKB]::F10()
Start-Sleep 1
$c = Read-Credit
if ($c -ge 280 -and $c -le 310) { Add-R 'T2.F10' 'ALTO' 'PASS' ("remaining=$c") } else { Add-R 'T2.F10' 'ALTO' 'FAIL' ("remaining=$c") }

$before = Read-Credit
1..10 | ForEach-Object { [PenteKB]::F10(); Start-Sleep -Milliseconds 40 }
Start-Sleep 1
$after = Read-Credit
$d = $after - $before
if ($d -le 300) { Add-R 'T3.DEBOUNCE' 'MEDIO' 'PASS' ("rapid +${d}s") }
elseif ($d -le 600) { Add-R 'T3.DEBOUNCE' 'MEDIO' 'WARN' ("rapid +${d}s") }
else { Add-R 'T3.DEBOUNCE' 'MEDIO' 'FAIL' ("rapid +${d}s demais") }

Stop-Notepad
Write-Credit 600
$null = Start-Timer
Start-Sleep 2
$a = Read-Credit
Start-Sleep 8
$b = Read-Credit
if (($a - $b) -le 1) { Add-R 'T4.NO_DRAIN' 'ALTO' 'PASS' ("$a->$b sem emulador") } else { Add-R 'T4.NO_DRAIN' 'ALTO' 'FAIL' ("$a->$b") }

Write-Credit 120
$null = Start-Timer
Start-Sleep 2
$np = Start-Process notepad -PassThru
Start-Sleep 10
$c5 = Read-Credit
$aliveNp = [bool](Get-Process -Id $np.Id -ErrorAction SilentlyContinue)
$drained = 120 - $c5
if ($aliveNp -and $drained -ge 6 -and $drained -le 14) {
    Add-R 'T5.DRAIN_RATE' 'ALTO' 'PASS' ("10s wall drained ${drained}s (~1:1)")
} elseif ($aliveNp -and $drained -ge 3) {
    Add-R 'T5.DRAIN_RATE' 'ALTO' 'WARN' ("drained ${drained}s/10s")
} elseif (-not $aliveNp) {
    Add-R 'T5.DRAIN_RATE' 'ALTO' 'FAIL' ("notepad morto remaining=$c5")
} else {
    Add-R 'T5.DRAIN_RATE' 'ALTO' 'FAIL' ("pouco drain ${drained}s remaining=$c5")
}
Stop-Notepad

Write-Credit 90
$null = Start-Timer
Start-Sleep 2
$np = Start-Process notepad -PassThru
Start-Sleep 4
Stop-Process -Id $np.Id -Force -ErrorAction SilentlyContinue
Start-Sleep 1
$p0 = Read-Credit
Start-Sleep 6
$p1c = Read-Credit
if (($p0 - $p1c) -le 1) { Add-R 'T6.PAUSE' 'ALTO' 'PASS' ("pause $p0->$p1c") } else { Add-R 'T6.PAUSE' 'ALTO' 'FAIL' ("drain $p0->$p1c") }

Write-Credit 0
$null = Start-Timer
Start-Sleep 2
$np = Start-Process notepad -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
$killed = $false
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 250
    if (-not (Get-Process -Id $np.Id -ErrorAction SilentlyContinue)) { $killed = $true; break }
}
$sw.Stop()
if ($killed -and $sw.ElapsedMilliseconds -lt 5000) {
    Add-R 'T7.BLOCK0' 'CRITICO' 'PASS' ("kill sem credito em $($sw.ElapsedMilliseconds)ms")
} elseif ($killed) {
    Add-R 'T7.BLOCK0' 'CRITICO' 'WARN' ("kill lento $($sw.ElapsedMilliseconds)ms")
} else {
    Add-R 'T7.BLOCK0' 'CRITICO' 'FAIL' 'notepad sobreviveu sem credito'
    Stop-Notepad
}

$esBefore = (Get-Process emulationstation -ErrorAction SilentlyContinue).Id
Write-Credit 0
$null = Start-Timer
Start-Sleep 3
$esAfter = (Get-Process emulationstation -ErrorAction SilentlyContinue).Id
if ($esAfter) { Add-R 'T8.ES_SAFE' 'CRITICO' 'PASS' ("ES vivo id=$esAfter antes=$esBefore") }
else { Add-R 'T8.ES_SAFE' 'CRITICO' 'FAIL' ("ES perdido before=$esBefore") }

Stop-Timer
Set-Content $configPath -Value '{ this is not json !!!' -Encoding UTF8
$p = Start-Process $exe -WorkingDirectory $timerDir -PassThru
Start-Sleep 3
if (-not $p.HasExited) { Add-R 'T9.BAD_CONFIG' 'ALTO' 'PASS' 'arranca com config invalida' }
else { Add-R 'T9.BAD_CONFIG' 'ALTO' 'FAIL' 'exit com config invalida' }
Stop-Timer
Restore-LabConfig

Stop-Timer
Set-Content $creditPath -Value 'NOT_JSON' -Encoding UTF8
$p = Start-Process $exe -WorkingDirectory $timerDir -PassThru
Start-Sleep 2
if (-not $p.HasExited) { Add-R 'T10.BAD_CREDIT' 'MEDIO' 'PASS' ("sobrevive credit corrompido rem=$(Read-Credit)") }
else { Add-R 'T10.BAD_CREDIT' 'MEDIO' 'FAIL' 'exit com credit corrompido' }
Stop-Timer

$bak = Join-Path $timerDir 'credit.backup.json'
@{ RemainingSeconds = 777; TotalCoinsAccepted = 9; UpdatedAt = '2026-07-16T12:00:00-03:00' } | ConvertTo-Json | Set-Content $bak -Encoding UTF8
Set-Content $creditPath -Value 'BROKEN' -Encoding UTF8
$null = Start-Timer
Start-Sleep 2
[PenteKB]::F10()
Start-Sleep 1
$c = Read-Credit
if ($c -ge 1000 -and $c -le 1100) { Add-R 'T11.BACKUP' 'MEDIO' 'PASS' ("backup 777 + ficha => $c") }
elseif ($c -ge 280 -and $c -le 310) { Add-R 'T11.BACKUP' 'MEDIO' 'FAIL' ("backup ignorado, so ficha $c") }
else { Add-R 'T11.BACKUP' 'MEDIO' 'WARN' ("inesperado remaining=$c") }
Stop-Timer

Restore-LabConfig
Write-Credit 444
$null = Start-Timer
Start-Sleep 2
Stop-Process -Name TurboRama.ArcadeTimer -Force -ErrorAction SilentlyContinue
Start-Sleep 1
$cKill = Read-Credit
if ($cKill -eq 444) { Add-R 'T12.KILL_PERSIST' 'MEDIO' 'PASS' 'Kill: credit manteve 444 em disco' }
else { Add-R 'T12.KILL_PERSIST' 'MEDIO' 'WARN' ("Kill remaining=$cKill") }

Write-Credit 0
$null = Start-Timer
Start-Sleep 2
[PenteKB]::F10()
Start-Sleep 1
$cBefore = Read-Credit
$proc = Get-Process TurboRama.ArcadeTimer -ErrorAction SilentlyContinue | Select-Object -First 1
if ($proc -and $proc.MainWindowHandle -ne [IntPtr]::Zero) {
    [void][WinClose]::PostMessage($proc.MainWindowHandle, [WinClose]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep 2
    $still = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    $cAfter = Read-Credit
    if (-not $still -and $cAfter -ge ($cBefore - 5)) {
        Add-R 'T13.GRACEFUL_SAVE' 'MEDIO' 'PASS' ("WM_CLOSE gravou rem=$cAfter antes=$cBefore")
    } elseif (-not $still) {
        Add-R 'T13.GRACEFUL_SAVE' 'MEDIO' 'WARN' ("fechou rem=$cAfter antes=$cBefore")
    } else {
        Add-R 'T13.GRACEFUL_SAVE' 'MEDIO' 'FAIL' 'WM_CLOSE nao fechou'
        Stop-Timer
    }
} else {
    Add-R 'T13.GRACEFUL_SAVE' 'MEDIO' 'WARN' 'sem MainWindowHandle'
    Stop-Timer
}

Restore-LabConfig
Write-Credit 0
Stop-Timer
$null = Start-Process $exe -WorkingDirectory $timerDir -PassThru
$null = Start-Process $exe -WorkingDirectory $timerDir -PassThru
Start-Sleep 2
1..5 | ForEach-Object { [PenteKB]::F10(); Start-Sleep -Milliseconds 350 }
Start-Sleep 1
$cRace = Read-Credit
$n = @(Get-Process TurboRama.ArcadeTimer -ErrorAction SilentlyContinue).Count
if ($n -ge 2 -and $cRace -gt 900) {
    Add-R 'T14.DUAL_RACE' 'ALTO' 'FAIL' ("2+ inst credito inflado rem=$cRace n=$n")
} elseif ($n -ge 2) {
    Add-R 'T14.DUAL_RACE' 'ALTO' 'WARN' ("2 inst n=$n rem=$cRace")
} else {
    Add-R 'T14.DUAL_RACE' 'ALTO' 'PASS' ("1 inst efetiva rem=$cRace")
}
Stop-Timer

$mis = Get-Content $labConfig -Raw | ConvertFrom-Json
$list = New-Object System.Collections.Generic.List[string]
foreach ($x in @($mis.emulatorProcesses)) { [void]$list.Add([string]$x) }
if (-not $list.Contains('emulationstation')) { [void]$list.Add('emulationstation') }
$mis.emulatorProcesses = $list.ToArray()
$mis | ConvertTo-Json -Depth 8 | Set-Content $configPath -Encoding UTF8
Write-Credit 0
$esB = (Get-Process emulationstation -ErrorAction SilentlyContinue).Id
$null = Start-Timer
Start-Sleep 4
$esA = (Get-Process emulationstation -ErrorAction SilentlyContinue).Id
if ($esA) { Add-R 'T15.ES_IN_EMU_PROT' 'CRITICO' 'PASS' ("ES na whitelist+protected vivo id=$esA") }
else { Add-R 'T15.ES_IN_EMU_PROT' 'CRITICO' 'FAIL' 'ES MORREU com misconfig' }
Restore-LabConfig
Stop-Timer

$emptyObj = @{
    minutesPerCoin = 5
    coinKey = 'F10'
    coinDebounceMilliseconds = 300
    countOnlyWhileEmulatorIsRunning = $true
    blockGameWithoutCredit = $true
    closeEmulatorWhenTimeEnds = $true
    emulatorCheckIntervalMilliseconds = 1000
    saveRemainingTime = $true
    restoreCreditAfterRestart = $true
    window = @{ enabled = $true; width = 168; height = 64; allowClose = $true; compact = $true; topMost = $true; opacity = 0.82 }
    emulatorProcesses = @()
    protectedProcesses = @('emulationstation','TurboRama.Launcher','TurboRama.Watchdog','explorer')
}
$emptyObj | ConvertTo-Json -Depth 6 | Set-Content $configPath -Encoding UTF8
Write-Credit 60
$null = Start-Timer
Start-Sleep 2
$np = Start-Process notepad -PassThru
Start-Sleep 3
$still = [bool](Get-Process -Id $np.Id -ErrorAction SilentlyContinue)
$c = Read-Credit
if ($still -and $c -ge 55) { Add-R 'T16.EMPTY_EMU' 'MEDIO' 'PASS' 'lista vazia inerte' }
else { Add-R 'T16.EMPTY_EMU' 'MEDIO' 'WARN' ("lista vazia stillNp=$still credit=$c") }
Stop-Notepad
Stop-Timer
Restore-LabConfig

Write-Credit 200
$null = Start-Timer
Start-Sleep 2
$np = Start-Process notepad -PassThru
Start-Sleep 2
Stop-Timer
Start-Sleep 1
if (Get-Process -Id $np.Id -ErrorAction SilentlyContinue) {
    Add-R 'T18.TIMER_DIE_GAME' 'ALTO' 'PASS' 'Timer morto: jogo CONTINUA (risco jogo gratis)'
} else {
    Add-R 'T18.TIMER_DIE_GAME' 'ALTO' 'WARN' 'notepad morreu com Timer'
}
Stop-Notepad

$l = [bool](Get-Process TurboRama.Launcher -ErrorAction SilentlyContinue)
$w = [bool](Get-Process TurboRama.Watchdog -ErrorAction SilentlyContinue)
$es = [bool](Get-Process emulationstation -ErrorAction SilentlyContinue)
$kf2 = (sc.exe query MsKeyboardFilter | Out-String) -match 'RUNNING'
$cad2 = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter').'Ctrl+Alt+Del'
if ($l -and $w -and $es) { Add-R 'T19.KIOSK_LIVE' 'CRITICO' 'PASS' 'Launcher+Watchdog+ES vivos' }
else { Add-R 'T19.KIOSK_LIVE' 'CRITICO' 'FAIL' ("L=$l W=$w ES=$es") }
if ($kf2 -and $cad2 -eq 'Blocked') { Add-R 'T19.WEKF' 'CRITICO' 'PASS' 'WEKF/CAD intactos' }
else { Add-R 'T19.WEKF' 'CRITICO' 'FAIL' ("kf=$kf2 cad=$cad2") }

$alivePath = 'C:\TurboRama\Logs\security-agent-alive.txt'
if (Test-Path $alivePath) {
    $age = ((Get-Date) - (Get-Item $alivePath).LastWriteTime).TotalSeconds
    if ($age -lt 120) { Add-R 'T20.AGENT_HB' 'ALTO' 'PASS' ("heartbeat age=${age}s") }
    else { Add-R 'T20.AGENT_HB' 'ALTO' 'WARN' ("heartbeat age=${age}s") }
}

Write-Credit 999999999
$null = Start-Timer
Start-Sleep 2
if (Get-Process TurboRama.ArcadeTimer -ErrorAction SilentlyContinue) {
    Add-R 'T21.HUGE_CREDIT' 'BAIXO' 'PASS' ("huge credit ok rem=$(Read-Credit)")
} else {
    Add-R 'T21.HUGE_CREDIT' 'BAIXO' 'FAIL' 'crash huge credit'
}
Stop-Timer

@{ RemainingSeconds = -50; TotalCoinsAccepted = 0; UpdatedAt = '2026-07-16T12:00:00-03:00' } | ConvertTo-Json | Set-Content $creditPath -Encoding UTF8
$null = Start-Timer
Start-Sleep 2
$np = Start-Process notepad -PassThru
Start-Sleep 3
$alive = [bool](Get-Process -Id $np.Id -ErrorAction SilentlyContinue)
if (-not $alive) { Add-R 'T22.NEG_CREDIT' 'MEDIO' 'PASS' 'credito negativo = 0 (bloqueia)' }
else { Add-R 'T22.NEG_CREDIT' 'MEDIO' 'FAIL' 'negativo nao bloqueou'; Stop-Notepad }
Stop-Timer

# camelCase - write raw file without PascalCase
Set-Content $creditPath -Value '{"remainingSeconds":500,"totalCoinsAccepted":2,"updatedAt":"2026-07-16T12:00:00Z"}' -Encoding UTF8
$null = Start-Timer
Start-Sleep 2
$np = Start-Process notepad -PassThru
Start-Sleep 3
$alive = [bool](Get-Process -Id $np.Id -ErrorAction SilentlyContinue)
if (-not $alive) {
    Add-R 'T23.CAMEL_CREDIT' 'BAIXO' 'BUG' 'camelCase credit lido como 0 (bloqueou com 500s no ficheiro)'
} else {
    Add-R 'T23.CAMEL_CREDIT' 'BAIXO' 'PASS' 'camelCase credit aceite'
    Stop-Notepad
}
Stop-Timer

Restore-LabConfig
Write-Credit 3
$null = Start-Timer
Start-Sleep 2
$np = Start-Process notepad -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
$ended = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 500
    if (-not (Get-Process -Id $np.Id -ErrorAction SilentlyContinue)) { $ended = $true; break }
}
$sw.Stop()
$rem = Read-Credit
if ($ended -and $rem -le 1) {
    Add-R 'T24.TIME_END' 'ALTO' 'PASS' ("fim tempo fechou notepad em $($sw.ElapsedMilliseconds)ms rem=$rem")
} elseif ($ended) {
    Add-R 'T24.TIME_END' 'ALTO' 'WARN' ("notepad fechou rem=$rem ms=$($sw.ElapsedMilliseconds)")
} else {
    Add-R 'T24.TIME_END' 'ALTO' 'FAIL' ("notepad nao fechou rem=$rem")
    Stop-Notepad
}
Stop-Timer

Restore-LabConfig
Write-Credit 0
Stop-Timer
Stop-Notepad

Add-R 'MATRIX.POWER_LOSS' 'ALTO' 'WARN' 'Queda luz: Kill sem OnFormClosing perde ticks em memoria'
Add-R 'MATRIX.F10_WEKF' 'MEDIO' 'WARN' 'Se WEKF bloquear tecla da ficha, fichas param'
Add-R 'MATRIX.ELEVATION' 'MEDIO' 'WARN' 'Elevacao/UIPI pode fazer hook falhar'
Add-R 'MATRIX.CLOCK_JUMP' 'BAIXO' 'WARN' 'Salto de relogio drena varios segundos num tick'
Add-R 'MATRIX.NO_WATCHDOG_TIMER' 'ALTO' 'BUG' 'Ninguem relanca Timer se morrer = jogo gratis'
Add-R 'MATRIX.CTRL_END_MANUAL' 'ALTO' 'WARN' 'Ctrl+End+PIN Lz2026@$ com Timer: confirmar manual'
Add-R 'MATRIX.REAL_EMU' 'ALTO' 'WARN' 'EXE real tem de bater com whitelist ou nao drena/bloqueia'

$pass = @($results | Where-Object { $_.Status -eq 'PASS' }).Count
$fail = @($results | Where-Object { $_.Status -eq 'FAIL' }).Count
$warn = @($results | Where-Object { $_.Status -eq 'WARN' }).Count
$bug  = @($results | Where-Object { $_.Status -eq 'BUG' }).Count

Write-Host ""
Write-Host "==== TOTAL PASS=$pass FAIL=$fail WARN=$warn BUG=$bug ===="

$outDir = 'D:\Backup-Instaladores-Compiladores-Turborama\TurboRamaArcadeTimer_Proposta\tests\results'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$out = Join-Path $outDir "PENTE-FINO-$stamp.txt"

$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add('PENTE FINO - TurboRama x Arcade Timer 0.1.1-test')
[void]$lines.Add(('Data=' + (Get-Date -Format o)))
[void]$lines.Add("PASS=$pass FAIL=$fail WARN=$warn BUG=$bug")
[void]$lines.Add('')
foreach ($r in $results) {
    [void]$lines.Add(('[{0}][{1}] {2} - {3}' -f $r.Status, $r.Sev, $r.Id, $r.Detail))
}
$lines | Set-Content $out -Encoding UTF8
Write-Host "REPORT=$out"
$results | Format-Table Id, Sev, Status, Detail -AutoSize
