@echo off
chcp 65001 >nul
title TurboRama - Testar ferramenta de backup
cd /d "%~dp0"
echo.
echo A testar BACKUP-SEGURANCA-PANE (corrida real + verificacao)...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Backup-SegurancaPane.ps1"
set ERR=%ERRORLEVEL%
echo.
if %ERR% equ 0 (
  echo ============================================
  echo  TESTE BACKUP: PASSOU
  echo ============================================
) else (
  echo ============================================
  echo  TESTE BACKUP: FALHOU codigo=%ERR%
  echo ============================================
)
pause
exit /b %ERR%
