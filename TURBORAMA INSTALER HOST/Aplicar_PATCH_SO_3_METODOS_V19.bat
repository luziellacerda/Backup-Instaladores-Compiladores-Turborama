@echo off
title Turborama - Patch so 3 metodos V19
color 0A
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Aplicar_PATCH_SO_3_METODOS_V19.ps1"
pause
