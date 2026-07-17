@echo off
chcp 65001 >nul
title TurboRama - Testes SEGUROS de seguranca
echo ============================================
echo  TESTES SEGUROS (nao desliga / nao reinicia)
echo ============================================
echo.

set "FAIL=0"

echo [1] MsKeyboardFilter...
sc query MsKeyboardFilter | findstr /I "RUNNING" >nul
if errorlevel 1 (
  echo   FALHA: servico nao RUNNING
  set FAIL=1
) else (
  echo   OK RUNNING
)

echo [2] Registo Ctrl+Alt+Del...
reg query "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+Alt+Del" 2>nul | findstr /I "Blocked" >nul
if errorlevel 1 (
  echo   FALHA: CAD nao Blocked
  set FAIL=1
) else (
  echo   OK Blocked
)

echo [3] Registo Ctrl+End...
reg query "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+End" 2>nul | findstr /I "Allowed" >nul
if errorlevel 1 (
  echo   AVISO: Ctrl+End nao Allowed no registo
) else (
  echo   OK Allowed
)

echo [4] Agent heartbeat...
if exist "C:\TurboRama\Logs\security-agent-alive.txt" (
  echo   Alive:
  type "C:\TurboRama\Logs\security-agent-alive.txt"
) else (
  echo   FALHA: sem security-agent-alive.txt
  set FAIL=1
  echo   A tentar arrancar agent...
  start "" "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" --security-agent
  timeout /t 3 /nobreak >nul
)

echo [5] WEKF CAD ^(se disponivel^)...
powershell -NoProfile -Command "try { $k=Get-CimInstance -Namespace root\standardcimv2\embedded -ClassName WEKF_PredefinedKey | ? { $_.Id -eq 'Ctrl+Alt+Del' }; if($k -and $k.Enabled){ '   OK WEKF Enabled=true (bloqueado)' } elseif($k){ '   FALHA WEKF Enabled=false'; exit 1 } else { '   AVISO WEKF sem Id' } } catch { '   AVISO WEKF: '+$_.Exception.Message }"
if errorlevel 1 set FAIL=1

echo [6] Disco C: livre...
powershell -NoProfile -Command "$f=(Get-PSDrive C).Free/1GB; if($f -lt 10){ '   AVISO: so {0:N1} GB livres' -f $f; exit 1 } else { '   OK {0:N1} GB livres' -f $f }"
if errorlevel 1 echo   ^(espaco baixo - limpar logs/updates^)

echo [7] Preview menu ^(seguro - NAO executa desligar^)...
echo   Abrindo --test-security ...
start "" "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" --test-security

echo.
if "%FAIL%"=="1" (
  echo RESULTADO: Houve FALHAS - ver acima
) else (
  echo RESULTADO: Checks basicos OK
  echo  PIN menu seguranca padrao: Lz2026@Sec
  echo  ^(ou SecurityMenuPin no turborama.json^)
)
echo.
echo Manual: Ctrl+End no kiosk; NAO testar Desligar neste script.
pause
