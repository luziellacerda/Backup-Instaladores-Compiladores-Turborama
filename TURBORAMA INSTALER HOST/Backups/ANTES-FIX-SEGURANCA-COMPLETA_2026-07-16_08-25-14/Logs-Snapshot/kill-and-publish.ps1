$ErrorActionPreference = "Continue"
taskkill /F /IM TurboRama.Launcher.exe 2>&1 | Out-String | Set-Content C:\TurboRama\Logs\kill-launcher.txt
Start-Sleep 2
Get-Process TurboRama.Launcher -EA SilentlyContinue | ForEach-Object { try { $_.Kill() } catch {} }
Start-Sleep 1
$root = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama"
$out = & dotnet publish "$root\src\TurboRama.Launcher\TurboRama.Launcher.csproj" -c Release -o "C:\TurboRama\App\Launcher" --nologo 2>&1 | Out-String
Add-Content C:\TurboRama\Logs\kill-launcher.txt $out
Add-Content C:\TurboRama\Logs\kill-launcher.txt ("EXIT=" + $LASTEXITCODE)
$dll = Get-Item "C:\TurboRama\App\Launcher\TurboRama.Launcher.dll"
Add-Content C:\TurboRama\Logs\kill-launcher.txt ("DLL=" + $dll.LastWriteTime + " len=" + $dll.Length)
# start agent + preview
Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--security-agent"
Start-Sleep 1
Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--test-security"
Set-Content C:\TurboRama\Logs\kill-launcher-done.flag (Get-Date -Format o)
