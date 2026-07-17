$ErrorActionPreference = "Continue"
Get-Process TurboRama.Launcher -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep 1
$src = "D:\tr-publish-loading"
$dst = "C:\TurboRama\App\Launcher"
New-Item -ItemType Directory -Path "$dst\Assets" -Force | Out-Null
Copy-Item "$src\*" $dst -Recurse -Force
New-Item -ItemType Directory -Path "C:\TurboRama\Launcher\assets" -Force | Out-Null
Copy-Item "$src\Assets\boot.wav","$src\Assets\logo.png" "C:\TurboRama\Launcher\assets" -Force -EA SilentlyContinue
$pack = "D:\tr-factory-pack\TurboRama-Factory-Pack\App\Launcher"
if (Test-Path (Split-Path $pack)) {
  New-Item -ItemType Directory -Path "$pack\Assets" -Force | Out-Null
  Copy-Item "$src\*" $pack -Recurse -Force
}
$j = Get-Content "C:\TurboRama\Config\turborama.json" -Raw | ConvertFrom-Json
$j | Add-Member -NotePropertyName showLoadingScreen -NotePropertyValue $true -Force
$j | Add-Member -NotePropertyName loadingSoundFile -NotePropertyValue "" -Force
$j | Add-Member -NotePropertyName loadingMinDisplayMs -NotePropertyValue 4500 -Force
$j | ConvertTo-Json -Depth 8 | Set-Content "C:\TurboRama\Config\turborama.json" -Encoding UTF8
@(
  "boot.wav=$(Test-Path "$dst\Assets\boot.wav") size=$((Get-Item "$dst\Assets\boot.wav" -EA SilentlyContinue).Length)"
  "logo=$(Test-Path "$dst\Assets\logo.png")"
  "exe=$(Test-Path "$dst\TurboRama.Launcher.exe")"
) | Set-Content "C:\TurboRama\Logs\Installer\deploy-loading2.txt"