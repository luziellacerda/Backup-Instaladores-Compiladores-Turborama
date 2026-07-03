@echo off
title Turborama - Fix RawInputForm private protected
color 0A
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Fix_RawInputForm_PrivateProtected.ps1"
pause
