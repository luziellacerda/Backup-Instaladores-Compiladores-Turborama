@echo off
title TurboRama - Baixar videos ScreenScraper
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Baixar-Tudo-Simples.ps1"
echo.
pause
