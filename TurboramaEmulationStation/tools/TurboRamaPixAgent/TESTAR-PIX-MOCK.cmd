@echo off
setlocal EnableExtensions
title TurboRama - Teste PIX Seguro

set "BASE=%~dp0"
set "EXE=%BASE%bin\Release\net8.0-windows\TurboRamaPixAgent.exe"
set "AUTO_TEST="
if /I "%~1"=="/auto" set "AUTO_TEST=1"

if not exist "%EXE%" (
  echo.
  echo O agente ainda nao foi compilado.
  echo Execute a compilacao antes deste teste.
  pause
  exit /b 1
)

for /f %%I in ('powershell -NoProfile -Command "[guid]::NewGuid().ToString('N')"') do set "PIX_ID=pix-teste-%%I"
set "TURBORAMA_PIX_PROVIDER=mock"
set "TURBORAMA_PIX_BRIDGE_DIRECTORY=%BASE%runtime"

powershell -NoProfile -Command "$request = Join-Path $env:TURBORAMA_PIX_BRIDGE_DIRECTORY 'requests'; New-Item -ItemType Directory -Force -Path $request | Out-Null; [ordered]@{ id=$env:PIX_ID; minutes=15; amountCents=750; requestedAt=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $request ($env:PIX_ID + '.request.json')) -Encoding utf8"

echo.
echo 1/3 - Gerando QR PIX de demonstracao...
"%EXE%" --once

set "QR=%TURBORAMA_PIX_BRIDGE_DIRECTORY%\qr\%PIX_ID%.png"
if exist "%QR%" if not defined AUTO_TEST start "QR PIX de teste" "%QR%"

echo.
echo QR criado. Este e somente um teste, nao gera cobranca real.
if not defined AUTO_TEST (
  echo Quando quiser simular o pagamento aprovado, pressione qualquer tecla.
  pause >nul
)

echo.
echo 2/3 - Simulando confirmacao do PIX...
"%EXE%" --approve %PIX_ID%

set "CREDIT=%TURBORAMA_PIX_BRIDGE_DIRECTORY%\approved\%PIX_ID%.credit.json"
if exist "%CREDIT%" (
  echo.
  echo 3/3 - SUCESSO: evento de credito criado para 15 minutos.
  echo Arquivo: %CREDIT%
) else (
  echo.
  echo O teste falhou: o evento de credito nao foi criado.
)

echo.
if not defined AUTO_TEST pause
