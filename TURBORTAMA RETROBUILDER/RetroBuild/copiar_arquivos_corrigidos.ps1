param(
    [Parameter(Mandatory=$true)]
    [string]$RetroBuildProjectPath,

    [Parameter(Mandatory=$true)]
    [string]$InstallerHostProjectPath
)

$ErrorActionPreference = "Stop"
$base = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "Copiando arquivos corrigidos do RetroBuild..."
Copy-Item "$base\RetroBuild\BuilderOptions.cs" $RetroBuildProjectPath -Force
Copy-Item "$base\RetroBuild\IniParser.cs" $RetroBuildProjectPath -Force
Copy-Item "$base\RetroBuild\Installer.cs" $RetroBuildProjectPath -Force
Copy-Item "$base\RetroBuild\Logger.cs" $RetroBuildProjectPath -Force
Copy-Item "$base\RetroBuild\Methods.cs" $RetroBuildProjectPath -Force
Copy-Item "$base\RetroBuild\Program.cs" $RetroBuildProjectPath -Force
Copy-Item "$base\RetroBuild\RetroBuild.csproj" $RetroBuildProjectPath -Force
Copy-Item "$base\RetroBuild\build.ini" $RetroBuildProjectPath -Force
Copy-Item "$base\RetroBuild\app.manifest" $RetroBuildProjectPath -Force
Copy-Item "$base\RetroBuild\RetroBuild.ico" $RetroBuildProjectPath -Force

$propertiesPath = Join-Path $RetroBuildProjectPath "Properties"
if (!(Test-Path $propertiesPath)) {
    New-Item -ItemType Directory -Path $propertiesPath | Out-Null
}
Copy-Item "$base\RetroBuild\AssemblyInfo.cs" (Join-Path $propertiesPath "AssemblyInfo.cs") -Force

Write-Host "Copiando arquivos corrigidos do InstallerHost..."
Copy-Item "$base\InstallerHost\InstallControl.cs" $InstallerHostProjectPath -Force
Copy-Item "$base\InstallerHost\InstallControl.Designer.cs" $InstallerHostProjectPath -Force

Write-Host "Pronto. Agora compile em Release x64 com Prefer 32-bit desmarcado."
