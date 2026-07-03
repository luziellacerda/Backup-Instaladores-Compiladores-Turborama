@echo off
title Turborama - Fix RetroBuild SharpZipLib
color 0A
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Fix_RetroBuild_SharpZipLib.ps1"
pause
