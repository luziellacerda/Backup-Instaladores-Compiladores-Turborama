$ErrorActionPreference = "Continue"
$log = "C:\TurboRama\Logs\publish-hud.log"
function L($m){ Add-Content $log $m }

Remove-Item $log -EA SilentlyContinue
taskkill /F /IM TurboRama.Launcher.exe 2>&1 | ForEach-Object { L $_ }
Start-Sleep 2

# ensure asset in project
$src = "C:\Users\Admin\.grok\sessions\C%3A%5CUsers%5CAdmin\019f61ae-35e2-7652-bc7e-e4881d53e1f4\assets\image-185a68ff-d99e-4914-a445-e87fb7cf78ef.png"
$proj = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama\src\TurboRama.Launcher\Assets\security-hud-bg.png"
if (Test-Path $src) { Copy-Item $src $proj -Force; L "asset copied" }

$root = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama"
$out = & dotnet publish "$root\src\TurboRama.Launcher\TurboRama.Launcher.csproj" -c Release -o "C:\TurboRama\App\Launcher" --nologo 2>&1 | Out-String
L $out
L ("EXIT=" + $LASTEXITCODE)

$bg = "C:\TurboRama\App\Launcher\Assets\security-hud-bg.png"
if (-not (Test-Path $bg) -and (Test-Path $proj)) {
  New-Item -ItemType Directory -Force -Path "C:\TurboRama\App\Launcher\Assets" | Out-Null
  Copy-Item $proj $bg -Force
  L "forced copy bg to deploy"
}
L ("BG exists=" + (Test-Path $bg))
L ("DLL=" + (Get-Item "C:\TurboRama\App\Launcher\TurboRama.Launcher.dll").LastWriteTime)

Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--security-agent"
Start-Sleep 1
Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--test-security"
Set-Content C:\TurboRama\Logs\publish-hud-done.flag (Get-Date -Format o)
