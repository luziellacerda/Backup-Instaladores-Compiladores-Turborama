@echo off
chcp 65001 >nul
echo ========================================
echo  TurboRama - instalar no PC
echo ========================================
echo.
echo 1. Vai abrir a pasta do instalador.
echo 2. Clique DIREITO no setup.exe
echo 3. Escolha: Executar como administrador
echo 4. Destino recomendado: D:\Turborama
echo.
pause
explorer "%~dp001-INSTALADOR"
