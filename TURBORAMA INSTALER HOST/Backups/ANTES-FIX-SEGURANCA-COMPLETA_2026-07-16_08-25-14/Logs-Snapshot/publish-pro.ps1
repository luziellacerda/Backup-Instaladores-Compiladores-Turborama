$ErrorActionPreference = "Continue"
$log = "C:\TurboRama\Logs\publish-pro.log"
function L($m){ Add-Content $log $m }
Remove-Item $log -EA SilentlyContinue
taskkill /F /IM TurboRama.Launcher.exe 2>&1 | Out-Null
Start-Sleep 1
$root = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama"
$out = & dotnet publish "$root\src\TurboRama.Launcher\TurboRama.Launcher.csproj" -c Release -o "C:\TurboRama\App\Launcher" --nologo 2>&1 | Out-String
L $out
L ("EXIT=" + $LASTEXITCODE)
if ($LASTEXITCODE -eq 0) {
  Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--security-agent"
  Start-Sleep 1
  Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--test-security"
  L ("OK " + (Get-Item "C:\TurboRama\App\Launcher\TurboRama.Launcher.dll").LastWriteTime)
}
Set-Content C:\TurboRama\Logs\publish-pro-done.flag (Get-Date -Format o)
