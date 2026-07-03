@echo off
title Turborama - Fix SplashVideo ShowBlackSplash Invalido
color 0A
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Fix_SplashVideo_ShowBlackSplash_Invalido.ps1"
pause
