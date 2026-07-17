$ErrorActionPreference = 'Continue'
taskkill /F /IM TurboRama.Launcher.exe 2>$null
Start-Sleep 1
$root = 'D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama'
$out = & dotnet publish "$root\src\TurboRama.Launcher\TurboRama.Launcher.csproj" -c Release -o 'C:\TurboRama\App\Launcher' --nologo 2>&1 | Out-String
Set-Content 'C:\TurboRama\Logs\backup-apply-publish.log' $out
Set-Content 'C:\TurboRama\Logs\backup-apply-done.flag' ($LASTEXITCODE.ToString() + '|' + (Get-Date -Format o))
