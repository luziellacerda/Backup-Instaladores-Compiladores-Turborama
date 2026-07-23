@echo off
title TurboRama - Organizar videos por sistema
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ORGANIZAR-POR-SISTEMA.ps1"
echo.
pause
