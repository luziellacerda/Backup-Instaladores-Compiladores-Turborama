@echo off
title Limpar Cache de Icones do Windows
color 0E
echo.
echo Este script vai reiniciar o Explorer para limpar cache de icones.
echo Feche janelas importantes antes.
echo.
pause
taskkill /IM explorer.exe /F
ie4uinit.exe -show
del /A /Q "%localappdata%\IconCache.db" 2>nul
del /A /F /Q "%localappdata%\Microsoft\Windows\Explorer\iconcache*" 2>nul
start explorer.exe
echo.
echo Cache de icones limpo.
pause
