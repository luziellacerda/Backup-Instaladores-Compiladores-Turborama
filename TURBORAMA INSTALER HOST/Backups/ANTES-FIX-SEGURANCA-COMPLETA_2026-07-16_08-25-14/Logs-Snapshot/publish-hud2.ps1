$ErrorActionPreference = "Continue"
$log = "C:\TurboRama\Logs\publish-hud2.log"
function L($m){ Add-Content $log $m }
Remove-Item $log -EA SilentlyContinue
taskkill /F /IM TurboRama.Launcher.exe 2>&1 | ForEach-Object { L $_ }
Start-Sleep 2
$src = "C:\Users\Admin\.grok\sessions\C%3A%5CUsers%5CAdmin\019f61ae-35e2-7652-bc7e-e4881d53e1f4\assets\image-0c1152b5-3538-472f-9dba-1607d7a728c1.png"
$proj = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama\src\TurboRama.Launcher\Assets\security-hud-bg.png"
if (Test-Path $src) { Copy-Item $src $proj -Force }
$root = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama"
$out = & dotnet publish "$root\src\TurboRama.Launcher\TurboRama.Launcher.csproj" -c Release -o "C:\TurboRama\App\Launcher" --nologo 2>&1 | Out-String
L $out
L ("EXIT=" + $LASTEXITCODE)
Copy-Item $proj "C:\TurboRama\App\Launcher\Assets\security-hud-bg.png" -Force -EA SilentlyContinue
L ("BG=" + (Test-Path "C:\TurboRama\App\Launcher\Assets\security-hud-bg.png"))
L ("DLL=" + (Get-Item "C:\TurboRama\App\Launcher\TurboRama.Launcher.dll").LastWriteTime)
Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--security-agent"
Start-Sleep 1
Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--test-security"
Set-Content C:\TurboRama\Logs\publish-hud2-done.flag (Get-Date -Format o)
