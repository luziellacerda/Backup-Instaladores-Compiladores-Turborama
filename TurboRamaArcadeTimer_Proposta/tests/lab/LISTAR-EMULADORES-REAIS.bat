@echo off
chcp 65001 >nul
title Listar processos - preencher whitelist
echo Processos com janela / candidatos a emulador:
echo (use estes nomes SEM .exe no config.json)
echo.
powershell -NoProfile -Command "Get-Process | Where-Object { $_.MainWindowTitle } | Sort-Object ProcessName | Select-Object ProcessName,Id,MainWindowTitle | Format-Table -AutoSize"
echo.
echo Guarde os nomes reais em tests\configs\config.producao-modelo.json
pause
