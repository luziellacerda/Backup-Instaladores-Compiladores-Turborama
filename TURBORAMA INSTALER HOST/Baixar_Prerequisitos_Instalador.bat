@echo off
title Turborama - Baixar Pre-requisitos do Instalador
color 0A
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Baixar_Prerequisitos_Instalador.ps1"
pause