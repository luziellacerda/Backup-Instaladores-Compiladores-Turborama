@echo off
:: Execute no Prompt de Comando da RECUPERACAO DO WINDOWS (WinRE)
:: Caminho: Solucionar problemas - Opcoes avancadas - Prompt de Comando
::
:: Se Windows estiver em C:, use os comandos abaixo.
:: Se estiver em D:, troque C: por D:

set "SOFTWARE_HIVE=C:\Windows\System32\config\SOFTWARE"

echo Descarregando hive anterior (se existir)...
reg unload HKLM\OFFLINE >nul 2>&1

echo Carregando registro offline...
reg load HKLM\OFFLINE "%SOFTWARE_HIVE%"
if errorlevel 1 (
    echo ERRO ao carregar SOFTWARE. Tente D:\Windows\System32\config\SOFTWARE
    pause
    exit /b 1
)

echo Restaurando explorer.exe e autologon...
reg add "HKLM\OFFLINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /t REG_SZ /d explorer.exe /f
reg add "HKLM\OFFLINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 0 /f
reg delete "HKLM\OFFLINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultPassword /f >nul 2>&1
reg delete "HKLM\OFFLINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultUserName /f >nul 2>&1
reg delete "HKLM\OFFLINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoLogonSID /f >nul 2>&1
reg add "HKLM\OFFLINE\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /t REG_SZ /d explorer.exe /f
reg add "HKLM\OFFLINE\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 0 /f

echo Descarregando registro...
reg unload HKLM\OFFLINE

echo.
echo PRONTO! Saia do WinRE e reinicie o PC normalmente.
echo AVISO: Shell do perfil TurboRama pode precisar de RESTAURAR-WINDOWS-AGORA.bat no Windows normal.
pause