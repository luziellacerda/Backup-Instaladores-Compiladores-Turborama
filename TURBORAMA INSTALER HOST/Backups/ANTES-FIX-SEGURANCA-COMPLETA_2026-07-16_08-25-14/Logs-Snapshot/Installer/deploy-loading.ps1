Get-Process TurboRama.Launcher -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep 1
$src="D:\tr-publish-loading"; $dst="C:\TurboRama\App\Launcher"
New-Item -ItemType Directory -Path "$dst\Assets" -Force | Out-Null
Copy-Item "$src\*" $dst -Recurse -Force
$pack="D:\tr-factory-pack\TurboRama-Factory-Pack\App\Launcher"
if (Test-Path (Split-Path $pack)) {
  New-Item -ItemType Directory -Path "$pack\Assets" -Force | Out-Null
  Copy-Item "$src\*" $pack -Recurse -Force
}
"OK" | Set-Content "C:\TurboRama\Logs\Installer\deploy-loading.txt"