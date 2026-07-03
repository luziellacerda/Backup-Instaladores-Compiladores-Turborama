@echo off
title Turborama - Fix Limpo Sem Tema Global V15
color 0A
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Fix_LIMPO_SemTemaGlobal_V15.ps1"
pause
