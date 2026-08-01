@echo off
setlocal
cd /d "%~dp0"
set "DOTNET_CLI_HOME=%~dp0.dotnet-local"
set "NUGET_PACKAGES=%~dp0.nuget-local"
echo.
echo  TurboRama PIX Test
echo  Abra http://127.0.0.1:18888 no navegador.
echo  Este ambiente usa somente pagamentos simulados.
echo.
dotnet run --configuration Release --project "%~dp0TurboRamaPixTest.csproj"
endlocal
