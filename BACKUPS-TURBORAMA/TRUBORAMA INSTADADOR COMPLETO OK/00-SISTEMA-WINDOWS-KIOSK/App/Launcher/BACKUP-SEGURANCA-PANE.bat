@echo off
chcp 65001 >nul
title TurboRama - BACKUP SEGURANCA (anti-pane)
cd /d "%~dp0"

echo ============================================
echo  BACKUP DE SEGURANCA - FASE FINAL
echo  Projecto + C:\TurboRama + Factory Pack
echo ============================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Backup-SegurancaPane.ps1"
set ERR=%ERRORLEVEL%
echo.
if %ERR% neq 0 (
  echo FALHA no backup. Codigo=%ERR%
  pause
  exit /b %ERR%
)
echo.
echo Backup OK. Pasta aberta no explorador se disponivel.
pause
exit /b 0
