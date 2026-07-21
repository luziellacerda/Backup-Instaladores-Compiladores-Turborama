@echo off
chcp 65001 >nul
echo ========================================
echo  TurboRama - verificar kit instalacao
echo ========================================
set OK=1
cd /d "%~dp001-INSTALADOR"
if not exist "TurboRama-stable-20260720-win64-setup.exe" (echo [FALTA] setup.exe & set OK=0) else echo [OK] setup.exe
if not exist "TurboRama-stable-20260720-win64-setup.exe.pkg.001" (echo [FALTA] pkg.001 & set OK=0) else echo [OK] pkg.001
if not exist "TurboRama-stable-20260720-win64-setup.exe.pkg.002" (echo [FALTA] pkg.002 & set OK=0) else echo [OK] pkg.002
if not exist "TurboRama-stable-20260720-win64-setup.exe.pkg.003" (echo [FALTA] pkg.003 & set OK=0) else echo [OK] pkg.003
if not exist "TurboRama-stable-20260720-win64-setup.exe.sha256.txt" (echo [AVISO] sha256) else echo [OK] sha256
echo.
if exist "%~dp003-ROMS" (
  dir /s /b "%~dp003-ROMS\*.*" 2>nul | find /c /v "" >nul
  echo Pasta 03-ROMS presente.
)
echo.
if "%OK%"=="1" (echo KIT INSTALADOR COMPLETO.) else (echo KIT INCOMPLETO.)
pause
