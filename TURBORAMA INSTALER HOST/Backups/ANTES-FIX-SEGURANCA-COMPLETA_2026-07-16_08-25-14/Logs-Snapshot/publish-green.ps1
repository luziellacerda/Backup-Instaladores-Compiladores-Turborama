$ErrorActionPreference = "Continue"
$log = "C:\TurboRama\Logs\publish-green.log"
function L($m){ Add-Content $log $m }
Remove-Item $log -EA SilentlyContinue
taskkill /F /IM TurboRama.Launcher.exe 2>&1 | ForEach-Object { L $_ }
Start-Sleep 2
$projBg = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama\src\TurboRama.Launcher\Assets\security-hud-bg.png"
$root = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama"
$out = & dotnet publish "$root\src\TurboRama.Launcher\TurboRama.Launcher.csproj" -c Release -o "C:\TurboRama\App\Launcher" --nologo 2>&1 | Out-String
L $out
L ("EXIT=" + $LASTEXITCODE)
Copy-Item $projBg "C:\TurboRama\App\Launcher\Assets\security-hud-bg.png" -Force
L ("DLL=" + (Get-Item "C:\TurboRama\App\Launcher\TurboRama.Launcher.dll").LastWriteTime)
Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--security-agent"
Start-Sleep 1
Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--test-security"
Set-Content C:\TurboRama\Logs\publish-green-done.flag (Get-Date -Format o)
