@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul
title TurboRama EmulationStation - Build Windows
cd /d "%~dp0"

echo ============================================================
echo  TurboRama EmulationStation - COMPILAR WINDOWS (x64 Release)
echo ============================================================
echo.

set "ERR=0"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "VCVARS="
set "CMAKE_EXE="
set "GIT_EXE="

REM ---- Locate tools ----
where cmake >nul 2>&1 && set "CMAKE_EXE=cmake"
if not defined CMAKE_EXE if exist "%ProgramFiles%\CMake\bin\cmake.exe" set "CMAKE_EXE=%ProgramFiles%\CMake\bin\cmake.exe"
if not defined CMAKE_EXE if exist "%ProgramFiles(x86)%\CMake\bin\cmake.exe" set "CMAKE_EXE=%ProgramFiles(x86)%\CMake\bin\cmake.exe"

where git >nul 2>&1 && set "GIT_EXE=git"
if not defined GIT_EXE if exist "%ProgramFiles%\Git\cmd\git.exe" set "GIT_EXE=%ProgramFiles%\Git\cmd\git.exe"

if exist "%VSWHERE%" (
  for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do (
    if exist "%%i\VC\Auxiliary\Build\vcvars64.bat" set "VCVARS=%%i\VC\Auxiliary\Build\vcvars64.bat"
  )
)

echo [CHECK] CMake: %CMAKE_EXE%
echo [CHECK] Git:   %GIT_EXE%
echo [CHECK] VS:    %VCVARS%
echo.

if not defined CMAKE_EXE (
  echo [ERRO] CMake nao encontrado. Instale: https://cmake.org/download/
  set ERR=1
)
if not defined GIT_EXE (
  echo [AVISO] Git nao encontrado. Submodules podem falhar.
  echo         Instale: https://git-scm.com/download/win
)
if not defined VCVARS (
  echo [ERRO] Visual Studio 2022 C++ nao encontrado.
  echo        Instale workload "Desenvolvimento para desktop com C++"
  set ERR=1
)

if %ERR% neq 0 (
  echo.
  echo Corrija as ferramentas e leia: docs\CHECKLIST-AMBIENTE-BUILD.md
  pause
  exit /b 1
)

REM ---- Submodules ----
if defined GIT_EXE (
  echo [1/4] git submodule update --init --recursive
  "%GIT_EXE%" submodule update --init --recursive
  if errorlevel 1 (
    echo [AVISO] submodule falhou - se external\pugixml\src existir, continue.
  )
) else (
  echo [1/4] Git ausente - a saltar submodules
)

if not exist "external\pugixml\src\pugixml.cpp" (
  echo [ERRO] external\pugixml incompleto. Precisa: git submodule update --init --recursive
  pause
  exit /b 2
)

REM ---- Dependencies ----
echo [2/4] Dependencias Windows
if exist "win32-libs" (
  echo       OK win32-libs\
) else if exist "..\batocera-emulationstation-win32-dependencies" (
  echo       OK ..\batocera-emulationstation-win32-dependencies\
) else (
  echo       win32-libs ausente.
  echo       O CMake tenta baixar de:
  echo       https://github.com/batocera-linux/batocera-emulationstation-win32-dependencies
  echo       Se falhar: clone esse repo para win32-libs ou pasta irma.
  if defined GIT_EXE (
    echo       Tentando clonar win32-libs...
    "%GIT_EXE%" clone --depth 1 https://github.com/batocera-linux/batocera-emulationstation-win32-dependencies.git win32-libs
  )
)

REM ---- Configure ----
echo [3/4] cmake configure x64
if defined VCVARS call "%VCVARS%"

if exist build\CMakeCache.txt (
  echo       Reutilizando pasta build\ ^(apague build\ se mudar de gerador^)
) else (
  mkdir build 2>nul
)

"%CMAKE_EXE%" -S . -B build -G "Visual Studio 17 2022" -A x64
if errorlevel 1 (
  echo.
  echo [ERRO] cmake configure falhou.
  echo        Causas comuns: VLC/SDL2/FreeImage em falta, plataforma errada.
  echo        Ver docs\CHECKLIST-AMBIENTE-BUILD.md
  pause
  exit /b 3
)

REM ---- Build ----
echo [4/4] cmake build Release
"%CMAKE_EXE%" --build build --config Release --parallel
if errorlevel 1 (
  echo [ERRO] Build falhou. Veja erros C++ acima.
  pause
  exit /b 4
)

echo.
echo ============================================================
echo  BUILD OK
echo ============================================================
if exist "bin\x64\emulationstation.exe" (
  echo  EXE: %CD%\bin\x64\emulationstation.exe
) else if exist "bin\Win32\emulationstation.exe" (
  echo  EXE: %CD%\bin\Win32\emulationstation.exe
) else (
  echo  Procurando emulationstation.exe ...
  dir /s /b emulationstation.exe 2>nul
)
echo.
echo  Teste: emulationstation.exe --windowed --debug --resolution 1280 720
echo  Credito: F10 = ficha  ^| sem credito = nao lanca jogo
echo  Config:  %%USERPROFILE%%\.emulationstation\arcade_credit.cfg
echo ============================================================
pause
exit /b 0
