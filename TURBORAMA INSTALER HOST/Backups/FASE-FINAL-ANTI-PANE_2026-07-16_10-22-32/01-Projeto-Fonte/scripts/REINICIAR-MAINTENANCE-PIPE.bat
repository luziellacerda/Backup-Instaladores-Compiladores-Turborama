@echo off
:: Reinicia o serviço Maintenance com o fix do named pipe (Status).
:: Clique direito > Executar como administrador.

setlocal
set "SRC=D:\tr-phase3-fix\maintenance-pipe"
set "DEST=C:\TurboRama\App\Maintenance"

net session >nul 2>&1
if errorlevel 1 (
  echo ERRO: execute como Administrador.
  pause
  exit /b 1
)

if not exist "%SRC%\TurboRama.Maintenance.exe" (
  echo ERRO: binarios nao encontrados em %SRC%
  echo Publique de novo o projeto Maintenance.
  pause
  exit /b 1
)

echo Parando TurboRamaMaintenance...
sc stop TurboRamaMaintenance
timeout /t 3 /nobreak >nul
taskkill /F /IM TurboRama.Maintenance.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo Copiando de %SRC% ...
xcopy "%SRC%\*" "%DEST%\" /E /Y /Q
if errorlevel 1 (
  echo FALHA ao copiar. Feche processos e tente de novo.
  pause
  exit /b 1
)

echo Iniciando servico...
sc start TurboRamaMaintenance
timeout /t 2 /nobreak >nul
sc query TurboRamaMaintenance

echo.
echo Pronto. Abra a UI (ui-status) como Admin e clique Status de novo.
echo Opcional: se lock=true e quiser modo normal, clique "Sair manutencao".
pause
