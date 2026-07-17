$ErrorActionPreference = "Continue"
Stop-Service TurboRamaWatchdog -Force -ErrorAction SilentlyContinue
sc.exe stop TurboRamaWatchdog
Start-Sleep 2
taskkill /F /IM TurboRama.Watchdog.exe 2>$null
taskkill /F /IM TurboRama.Launcher.exe 2>$null
Start-Sleep 2
$pub = "D:\tr-publish-fix-boot"
$destW = "C:\TurboRama\App\Watchdog"
# atomic: deploy to staging then swap
$stage = "C:\TurboRama\App\.staging-watchdog"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item "$pub\Watchdog\*" $stage -Recurse -Force
$prev = "C:\TurboRama\App\previous\Watchdog"
New-Item -ItemType Directory -Path (Split-Path $prev) -Force | Out-Null
if (Test-Path $destW) {
  if (Test-Path $prev) { Remove-Item $prev -Recurse -Force -ErrorAction SilentlyContinue }
  try { Rename-Item $destW $prev -Force } catch {
    # copy over files one by one
    Get-ChildItem $stage -File | ForEach-Object {
      $t = Join-Path $destW $_.Name
      try { Copy-Item $_.FullName $t -Force } catch {
        $old = $t + ".old"
        try { Move-Item $t $old -Force -ErrorAction SilentlyContinue } catch {}
        Copy-Item $_.FullName $t -Force
      }
    }
  }
}
if (-not (Test-Path $destW)) { Rename-Item $stage $destW } else {
  Copy-Item "$stage\*" $destW -Recurse -Force -ErrorAction SilentlyContinue
}
Copy-Item "$pub\Launcher\*" "C:\TurboRama\App\Launcher" -Recurse -Force
# config
$cfg = "C:\TurboRama\Config\turborama.json"
if ((Test-Path $cfg) -and (Test-Path "D:\Turborama\TurboRama.exe")) {
  $raw = [IO.File]::ReadAllText($cfg)
  $raw = $raw -replace '"frontendExecutable"\s*:\s*"[^"]*"', '"frontendExecutable": "D:\\Turborama\\TurboRama.exe"'
  [IO.File]::WriteAllText($cfg, $raw)
}
Remove-Item "C:\TurboRama\State\recovery.flag","C:\TurboRama\State\maintenance.lock" -Force -ErrorAction SilentlyContinue
Start-Service TurboRamaWatchdog -ErrorAction SilentlyContinue
sc.exe start TurboRamaWatchdog
Start-Sleep 2
Get-Service TurboRamaWatchdog | Format-List Name,Status
Get-Item "C:\TurboRama\App\Watchdog\TurboRama.Watchdog.exe","C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" | Format-List Name,Length,LastWriteTime
# pack
$pack = "D:\tr-factory-pack\TurboRama-Factory-Pack"
if (Test-Path $pack) {
  Copy-Item "$pub\Launcher\*" "$pack\App\Launcher" -Recurse -Force
  Copy-Item "$pub\Watchdog\*" "$pack\App\Watchdog" -Recurse -Force
  Set-Location $pack
  Get-ChildItem -Recurse -File | Where-Object { $_.Name -ne "PACK-HASHES.sha256" } | ForEach-Object {
    $rel = $_.FullName.Substring((Get-Location).Path.Length + 1).Replace('\','/')
    $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$h  $rel"
  } | Set-Content "PACK-HASHES.sha256" -Encoding ASCII
}
"OK" | Out-File "C:\TurboRama\Logs\Installer\deploy-boot-fix.txt" -Encoding utf8