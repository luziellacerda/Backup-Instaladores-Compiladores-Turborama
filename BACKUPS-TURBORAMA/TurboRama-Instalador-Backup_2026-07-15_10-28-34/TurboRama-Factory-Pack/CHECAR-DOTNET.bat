@echo off
chcp 65001 >nul
echo Checking .NET 8 Desktop Runtime...
where dotnet >nul 2>&1
if errorlevel 1 (
  echo WARNING: dotnet not in PATH.
  echo Install .NET 8 Desktop Runtime x64:
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  echo.
  pause
  exit /b 1
)
dotnet --list-runtimes 2>nul | findstr /i "Microsoft.WindowsDesktop.App 8." >nul
if errorlevel 1 (
  echo WARNING: Microsoft.WindowsDesktop.App 8.x not found.
  echo Install .NET 8 Desktop Runtime x64 before INSTALAR.bat
  pause
  exit /b 1
)
echo OK: .NET 8 Desktop runtime present.
exit /b 0