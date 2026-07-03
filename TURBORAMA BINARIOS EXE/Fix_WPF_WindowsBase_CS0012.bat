@echo off
title Turborama - Fix WPF WindowsBase CS0012
color 0A
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Fix_WPF_WindowsBase_CS0012.ps1"
pause
