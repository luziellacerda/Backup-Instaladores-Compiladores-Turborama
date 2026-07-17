# Compila o Projeto Novo TurboRama (.NET 8)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Preferir SDK portatil em D:\tr-dotnet se existir
$dotnet = $null
if (Test-Path "D:\tr-dotnet\dotnet.exe") {
    $env:DOTNET_ROOT = "D:\tr-dotnet"
    $env:PATH = "D:\tr-dotnet;" + $env:PATH
    $dotnet = "D:\tr-dotnet\dotnet.exe"
}
elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnet = (Get-Command dotnet).Source
}
else {
    Write-Host "ERRO: .NET SDK nao encontrado." -ForegroundColor Red
    Write-Host "Instale o .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
    Write-Host "Ou use o portatil: D:\tr-dotnet\dotnet.exe"
    exit 1
}

Write-Host "dotnet: $dotnet"
& $dotnet --version
& $dotnet restore "$root\TurboRama.sln"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build "$root\TurboRama.sln" -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet test "$root\TurboRama.sln" -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "OK: build e testes concluidos." -ForegroundColor Green
Write-Host "UI: $root\src\TurboRama.UI\bin\Release\net8.0-windows\TurboRama.UI.exe"
