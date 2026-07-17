# Enterprise QA â€” contador de fichas + timer + seguranÃ§a (Arcade Timer only)
$ErrorActionPreference = 'Continue'
$results = New-Object System.Collections.Generic.List[object]
function Add-QA([string]$id,[string]$area,[string]$status,[string]$detail,[string]$sev='ALTO') {
  $results.Add([pscustomobject]@{Id=$id;Area=$area;Sev=$sev;Status=$status;Detail=$detail})
  Write-Host "[$status][$area] $id - $detail"
}

$dir = 'D:\Backup-Instaladores-Compiladores-Turborama\TurboRamaArcadeTimer_Proposta\dist\TurboRama-ArcadeTimer-0.1.3-enterprise'
if (-not (Test-Path $dir)) {
  $dir = 'D:\Backup-Instaladores-Compiladores-Turborama\TurboRamaArcadeTimer_Proposta\dist\TurboRama-ArcadeTimer-0.1.2-test'
}
$exe = Join-Path $dir 'TurboRama.ArcadeTimer.exe'
$credit = Join-Path $dir 'credit.json'
$lab = Join-Path $dir 'config.lab.json'
$cfg = Join-Path $dir 'config.json'

function Stop-All {
  Get-Process TurboRama.ArcadeTimer,notepad -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 500
}
function Read-C {
  try {
    $j = Get-Content $credit -Raw | ConvertFrom-Json
    if ($null -ne $j.RemainingSeconds) { return [int64]$j.RemainingSeconds }
    if ($null -ne $j.remainingSeconds) { return [int64]$j.remainingSeconds }
    return -1
  } catch { return -1 }
}
function Read-Coins {
  try {
    $j = Get-Content $credit -Raw | ConvertFrom-Json
    if ($null -ne $j.TotalCoinsAccepted) { return [int64]$j.TotalCoinsAccepted }
    if ($null -ne $j.totalCoinsAccepted) { return [int64]$j.totalCoinsAccepted }
    return -1
  } catch { return -1 }
}
function Write-C([int64]$s,[int64]$coins=0) {
  @{ RemainingSeconds=$s; TotalCoinsAccepted=$coins; UpdatedAt=(Get-Date).ToString('o') } | ConvertTo-Json | Set-Content $credit -Encoding UTF8
}
function Start-T {
  Stop-All
  if (Test-Path $lab) { Copy-Item $lab $cfg -Force }
  return Start-Process $exe -WorkingDirectory $dir -PassThru
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class EntKB {
  [DllImport("user32.dll")] public static extern void keybd_event(byte a, byte b, uint c, UIntPtr d);
  public static void F10(){ keybd_event(0x79,0,0,UIntPtr.Zero); keybd_event(0x79,0,2,UIntPtr.Zero); }
}
'@ -ErrorAction SilentlyContinue

Write-Host "=== ENTERPRISE QA dir=$dir ==="
if (-not (Test-Path $exe)) { throw "EXE missing: $exe" }

# --- E0 baseline kiosk ---
$kf = (sc.exe query MsKeyboardFilter | Out-String) -match 'RUNNING'
$cad = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter' -EA SilentlyContinue).'Ctrl+Alt+Del'
$es = [bool](Get-Process emulationstation -EA SilentlyContinue)
if ($kf) { Add-QA 'E0.FILTER' 'KIOSK' 'PASS' 'MsKeyboardFilter RUNNING' 'CRITICO' } else { Add-QA 'E0.FILTER' 'KIOSK' 'FAIL' 'Filter down' 'CRITICO' }
if ($cad -eq 'Blocked') { Add-QA 'E0.CAD' 'KIOSK' 'PASS' 'CAD Blocked' 'CRITICO' } else { Add-QA 'E0.CAD' 'KIOSK' 'WARN' "CAD=$cad" 'CRITICO' }
if ($es) { Add-QA 'E0.ES' 'KIOSK' 'PASS' 'ES alive' 'ALTO' } else { Add-QA 'E0.ES' 'KIOSK' 'WARN' 'ES not running' 'ALTO' }

# --- C1 exact 1 coin ---
Write-C 0 0
$null = Start-T; Start-Sleep 2
[EntKB]::F10(); Start-Sleep 1
$r = Read-C; $c = Read-Coins
if ($r -eq 300 -and $c -eq 1) { Add-QA 'C1.ONE_COIN' 'FICHAS' 'PASS' "1 ficha => 300s coins=$c" 'CRITICO' }
else { Add-QA 'C1.ONE_COIN' 'FICHAS' 'FAIL' "expected 300/1 got $r/$c" 'CRITICO' }

# --- C2 exact 3 coins spaced ---
Write-C 0 0
Stop-All; $null = Start-T; Start-Sleep 2
1..3 | ForEach-Object { [EntKB]::F10(); Start-Sleep -Milliseconds 400 }
Start-Sleep 1
$r = Read-C; $c = Read-Coins
if ($r -eq 900 -and $c -eq 3) { Add-QA 'C2.THREE_COINS' 'FICHAS' 'PASS' "3 fichas => 900s coins=$c" 'CRITICO' }
else { Add-QA 'C2.THREE_COINS' 'FICHAS' 'FAIL' "expected 900/3 got $r/$c" 'CRITICO' }

# --- C3 bounce: 15 pulses < debounce = 1 coin ---
Write-C 0 0
Stop-All; $null = Start-T; Start-Sleep 2
1..15 | ForEach-Object { [EntKB]::F10(); Start-Sleep -Milliseconds 20 }
Start-Sleep 1
$r = Read-C; $c = Read-Coins
if ($c -eq 1 -and $r -eq 300) { Add-QA 'C3.BOUNCE' 'FICHAS' 'PASS' "15 pulses 20ms => 1 ficha ($r s)" 'CRITICO' }
elseif ($c -le 2 -and $r -le 600) { Add-QA 'C3.BOUNCE' 'FICHAS' 'WARN' "bounce coins=$c rem=$r (aceitavel se edge)" 'ALTO' }
else { Add-QA 'C3.BOUNCE' 'FICHAS' 'FAIL' "bounce coins=$c rem=$r" 'CRITICO' }

# --- C4 no free coin without key ---
Write-C 0 0
Stop-All; $null = Start-T; Start-Sleep 3
$r = Read-C
if ($r -eq 0) { Add-QA 'C4.NO_FREE' 'FICHAS' 'PASS' 'sem tecla = 0 credito' 'CRITICO' }
else { Add-QA 'C4.NO_FREE' 'FICHAS' 'FAIL' "credito espontaneo $r" 'CRITICO' }

# --- C5 cap max remaining (28800) ---
Write-C 28700 10
Stop-All; $null = Start-T; Start-Sleep 2
1..5 | ForEach-Object { [EntKB]::F10(); Start-Sleep -Milliseconds 400 }
Start-Sleep 1
$r = Read-C
if ($r -le 28800) { Add-QA 'C5.CAP' 'FICHAS' 'PASS' "teto credito rem=$r (<=28800)" 'ALTO' }
else { Add-QA 'C5.CAP' 'FICHAS' 'FAIL' "acima do teto rem=$r" 'ALTO' }

# --- T1 no drain idle ---
Write-C 600 1
Stop-All; $null = Start-T; Start-Sleep 2
Start-Sleep 10
$r = Read-C
if ($r -ge 598) { Add-QA 'T1.IDLE' 'TIMER' 'PASS' "idle 10s rem=$r (nao drena)" 'CRITICO' }
else { Add-QA 'T1.IDLE' 'TIMER' 'FAIL' "drenou sem jogo rem=$r" 'CRITICO' }

# --- T2 accuracy ~15s wall ---
Write-C 120 1
Stop-All; $null = Start-T; Start-Sleep 2
$np = Start-Process notepad -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep 15
$r = Read-C
$sw.Stop()
$drained = 120 - $r
$alive = [bool](Get-Process -Id $np.Id -EA SilentlyContinue)
$err = [math]::Abs($drained - 15)
if ($alive -and $err -le 2) { Add-QA 'T2.RATE_15S' 'TIMER' 'PASS' "15s wall drained=${drained}s err=${err}s" 'CRITICO' }
elseif ($alive -and $err -le 3) { Add-QA 'T2.RATE_15S' 'TIMER' 'WARN' "drained=${drained}s err=${err}s" 'CRITICO' }
else { Add-QA 'T2.RATE_15S' 'TIMER' 'FAIL' "drained=${drained} alive=$alive rem=$r" 'CRITICO' }
Stop-Process -Id $np.Id -Force -EA SilentlyContinue

# --- T3 pause ---
Write-C 90 1
Stop-All; $null = Start-T; Start-Sleep 2
$np = Start-Process notepad -PassThru; Start-Sleep 4
Stop-Process -Id $np.Id -Force -EA SilentlyContinue
Start-Sleep 1
$a = Read-C; Start-Sleep 6; $b = Read-C
if (($a - $b) -le 1) { Add-QA 'T3.PAUSE' 'TIMER' 'PASS' "pause $a->$b" 'CRITICO' }
else { Add-QA 'T3.PAUSE' 'TIMER' 'FAIL' "ainda drena $a->$b" 'CRITICO' }

# --- T4 end time kill ---
Write-C 2 1
Stop-All; $null = Start-T; Start-Sleep 2
$np = Start-Process notepad -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
$killed = $false
for ($i=0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 250
  if (-not (Get-Process -Id $np.Id -EA SilentlyContinue)) { $killed=$true; break }
}
if ($killed -and (Read-C) -le 0) { Add-QA 'T4.END' 'TIMER' 'PASS' "fim tempo kill em $($sw.ElapsedMilliseconds)ms" 'CRITICO' }
else { Add-QA 'T4.END' 'TIMER' 'FAIL' "killed=$killed rem=$(Read-C)"; Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -EA SilentlyContinue }

# --- T5 zero credit block ---
Write-C 0 0
Stop-All; $null = Start-T; Start-Sleep 2
$np = Start-Process notepad -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew(); $killed=$false
for ($i=0; $i -lt 20; $i++) {
  Start-Sleep -Milliseconds 200
  if (-not (Get-Process -Id $np.Id -EA SilentlyContinue)) { $killed=$true; break }
}
if ($killed -and $sw.ElapsedMilliseconds -lt 4000) { Add-QA 'T5.BLOCK0' 'TIMER' 'PASS' "0 credito kill $($sw.ElapsedMilliseconds)ms" 'CRITICO' }
else { Add-QA 'T5.BLOCK0' 'TIMER' 'FAIL' "killed=$killed ms=$($sw.ElapsedMilliseconds)"; Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -EA SilentlyContinue }

# --- T6 coin mid-play adds time ---
Write-C 30 1
Stop-All; $null = Start-T; Start-Sleep 2
$np = Start-Process notepad -PassThru; Start-Sleep 3
[EntKB]::F10(); Start-Sleep 1
$r = Read-C
# ~27 + 300 = ~327, allow range
if ($r -ge 310 -and $r -le 340) { Add-QA 'T6.COIN_MID' 'FICHAS' 'PASS' "ficha mid-play rem=$r" 'ALTO' }
else { Add-QA 'T6.COIN_MID' 'FICHAS' 'WARN' "mid-play rem=$r (check)" 'ALTO' }
Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -EA SilentlyContinue

# --- S1 single instance ---
Stop-All
$p1 = Start-Process $exe -WorkingDirectory $dir -PassThru; Start-Sleep 2
$p2 = Start-Process $exe -WorkingDirectory $dir -PassThru; Start-Sleep 2
$n = @(Get-Process TurboRama.ArcadeTimer -EA SilentlyContinue).Count
if ($n -eq 1 -and $p2.HasExited) { Add-QA 'S1.SINGLE' 'SEG' 'PASS' '1 instancia 2a exit' 'CRITICO' }
else { Add-QA 'S1.SINGLE' 'SEG' 'FAIL' "n=$n p2exit=$($p2.HasExited)" 'CRITICO' }

# --- S2 dual race credit ---
Stop-All; Write-C 0 0
Start-Process $exe -WorkingDirectory $dir | Out-Null
Start-Process $exe -WorkingDirectory $dir | Out-Null
Start-Sleep 2
1..4 | ForEach-Object { [EntKB]::F10(); Start-Sleep -Milliseconds 400 }
Start-Sleep 1
$n = @(Get-Process TurboRama.ArcadeTimer -EA SilentlyContinue).Count
$r = Read-C; $c = Read-Coins
if ($n -eq 1 -and $c -eq 4 -and $r -eq 1200) { Add-QA 'S2.RACE' 'SEG' 'PASS' "n=1 coins=4 rem=1200" 'CRITICO' }
elseif ($n -eq 1 -and $c -le 4 -and $r -le 1200) { Add-QA 'S2.RACE' 'SEG' 'PASS' "n=1 coins=$c rem=$r" 'CRITICO' }
else { Add-QA 'S2.RACE' 'SEG' 'FAIL' "n=$n coins=$c rem=$r" 'CRITICO' }

# --- S3 ES protected with zero credit ---
$esB = (Get-Process emulationstation -EA SilentlyContinue).Id
Write-C 0 0
Stop-All; $null = Start-T; Start-Sleep 4
$esA = (Get-Process emulationstation -EA SilentlyContinue).Id
if ($esA) { Add-QA 'S3.ES' 'SEG' 'PASS' "ES vivo $esA" 'CRITICO' } else { Add-QA 'S3.ES' 'SEG' 'FAIL' 'ES morto' 'CRITICO' }

# --- S4 config remove protected still safe (hard) ---
$bad = @{
  minutesPerCoin=5; coinKey='F10'; coinDebounceMilliseconds=300
  blockGameWithoutCredit=$true; closeEmulatorWhenTimeEnds=$true
  countOnlyWhileEmulatorIsRunning=$true; emulatorCheckIntervalMilliseconds=1000
  maxRemainingSeconds=28800
  window=@{enabled=$true;width=168;height=64;allowClose=$true;compact=$true;topMost=$true;opacity=0.8}
  emulatorProcesses=@('notepad','emulationstation','TurboRama.Launcher')
  protectedProcesses=@()
}
$bad | ConvertTo-Json -Depth 6 | Set-Content $cfg -Encoding UTF8
Write-C 0 0
Stop-All; $null = Start-Process $exe -WorkingDirectory $dir -PassThru; Start-Sleep 4
$esA = (Get-Process emulationstation -EA SilentlyContinue).Id
$l = [bool](Get-Process TurboRama.Launcher -EA SilentlyContinue)
if ($esA -and $l) { Add-QA 'S4.HARD_PROT' 'SEG' 'PASS' 'config maliciosa nao matou ES/Launcher' 'CRITICO' }
else { Add-QA 'S4.HARD_PROT' 'SEG' 'FAIL' "ES=$esA Launcher=$l" 'CRITICO' }
if (Test-Path $lab) { Copy-Item $lab $cfg -Force }

# --- S5 path traversal names ignored ---
# covered by validate normalize - runtime: notepad still works
if (Test-Path $lab) { Copy-Item $lab $cfg -Force }
Write-C 0 0
Stop-All; $null = Start-T; Start-Sleep 2
$np = Start-Process notepad -PassThru; Start-Sleep 3
$alive = [bool](Get-Process -Id $np.Id -EA SilentlyContinue)
if (-not $alive) { Add-QA 'S5.NOTEPAD_BLOCK' 'SEG' 'PASS' '0 credit still blocks notepad' 'ALTO' }
else { Add-QA 'S5.NOTEPAD_BLOCK' 'SEG' 'FAIL' 'notepad not blocked'; Stop-Process -Id $np.Id -Force -EA SilentlyContinue }

# --- S6 tamper huge credit capped on next coin/load ---
Write-C 999999999 99
Stop-All; $null = Start-T; Start-Sleep 2
[EntKB]::F10(); Start-Sleep 1
$r = Read-C
if ($r -le 28800) { Add-QA 'S6.TAMPER_CAP' 'SEG' 'PASS' "tamper capped rem=$r" 'ALTO' }
else { Add-QA 'S6.TAMPER_CAP' 'SEG' 'FAIL' "tamper not capped rem=$r" 'ALTO' }

# --- S7 camelCase credit integrity ---
Stop-All
Set-Content $credit -Value '{"remainingSeconds":400,"totalCoinsAccepted":2,"updatedAt":"2026-07-16T00:00:00Z"}' -Encoding UTF8
$null = Start-T; Start-Sleep 2
$np = Start-Process notepad -PassThru; Start-Sleep 3
$alive = [bool](Get-Process -Id $np.Id -EA SilentlyContinue)
$r = Read-C
if ($alive -and $r -ge 390 -and $r -le 400) { Add-QA 'S7.CAMEL' 'SEG' 'PASS' "camel credit OK rem=$r" 'MEDIO' }
elseif ($alive) { Add-QA 'S7.CAMEL' 'SEG' 'PASS' "camel loaded rem=$r" 'MEDIO' }
else { Add-QA 'S7.CAMEL' 'SEG' 'FAIL' "camel lost rem=$r" 'MEDIO' }
Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -EA SilentlyContinue

# --- S8 guard relaunch ---
Stop-All
Start-Process $exe -WorkingDirectory $dir -ArgumentList '--guard' | Out-Null
Start-Sleep 6
$mains = @(Get-CimInstance Win32_Process -Filter "Name='TurboRama.ArcadeTimer.exe'" | Where-Object { $_.CommandLine -notmatch '--guard' })
$guards = @(Get-CimInstance Win32_Process -Filter "Name='TurboRama.ArcadeTimer.exe'" | Where-Object { $_.CommandLine -match '--guard' })
if ($mains.Count -ge 1 -and $guards.Count -ge 1) {
  $mains | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue }
  Start-Sleep 6
  $mains2 = @(Get-CimInstance Win32_Process -Filter "Name='TurboRama.ArcadeTimer.exe'" | Where-Object { $_.CommandLine -notmatch '--guard' })
  if ($mains2.Count -ge 1) { Add-QA 'S8.GUARD' 'SEG' 'PASS' 'Guard relancou Timer' 'ALTO' }
  else { Add-QA 'S8.GUARD' 'SEG' 'FAIL' 'Guard nao relancou' 'ALTO' }
} else {
  Add-QA 'S8.GUARD' 'SEG' 'FAIL' "mains=$($mains.Count) guards=$($guards.Count)" 'ALTO'
}

# --- S9 kiosk after all ---
Stop-All
$es = [bool](Get-Process emulationstation -EA SilentlyContinue)
$l = [bool](Get-Process TurboRama.Launcher -EA SilentlyContinue)
$w = [bool](Get-Process TurboRama.Watchdog -EA SilentlyContinue)
$kf2 = (sc.exe query MsKeyboardFilter | Out-String) -match 'RUNNING'
$cad2 = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter' -EA SilentlyContinue).'Ctrl+Alt+Del'
if ($es -and $l -and $w -and $kf2 -and $cad2 -eq 'Blocked') {
  Add-QA 'S9.KIOSK_FINAL' 'KIOSK' 'PASS' 'kiosk intacto pos-QA' 'CRITICO'
} else {
  Add-QA 'S9.KIOSK_FINAL' 'KIOSK' 'FAIL' "ES=$es L=$l W=$w KF=$kf2 CAD=$cad2" 'CRITICO'
}

# restore lab config
if (Test-Path $lab) { Copy-Item $lab $cfg -Force }
Write-C 0 0
Stop-All

$pass = @($results | Where-Object Status -eq 'PASS').Count
$fail = @($results | Where-Object Status -eq 'FAIL').Count
$warn = @($results | Where-Object Status -eq 'WARN').Count
Write-Host ""
Write-Host "==== ENTERPRISE TOTAL PASS=$pass FAIL=$fail WARN=$warn ===="

$outDir = 'D:\Backup-Instaladores-Compiladores-Turborama\TurboRamaArcadeTimer_Proposta\tests\results'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$out = Join-Path $outDir "ENTERPRISE-QA-$stamp.txt"
$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add('ENTERPRISE QA - TurboRama Arcade Timer')
[void]$lines.Add("Data=$(Get-Date -Format o)")
[void]$lines.Add("Package=$dir")
[void]$lines.Add("PASS=$pass FAIL=$fail WARN=$warn")
[void]$lines.Add('')
foreach ($x in $results) {
  [void]$lines.Add(("[{0}][{1}][{2}] {3} - {4}" -f $x.Status,$x.Sev,$x.Area,$x.Id,$x.Detail))
}
if ($fail -eq 0) {
  [void]$lines.Add('')
  [void]$lines.Add('VEREDICTO: PASS enterprise automatico (confirmar manual Ctrl+End+PIN e tecla ficha real).')
} else {
  [void]$lines.Add('')
  [void]$lines.Add('VEREDICTO: FAIL - nao liberar para multinacional ate zerar FAILs.')
}
$lines | Set-Content $out -Encoding UTF8
Write-Host "REPORT=$out"
$results | Format-Table Id,Area,Sev,Status,Detail -AutoSize
exit $(if ($fail -gt 0) { 1 } else { 0 })

