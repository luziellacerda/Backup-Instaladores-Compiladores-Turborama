# Manual de recuperação offline — TurboRama Secure

Use este guia se o kiosk falhar e você precisar recuperar o PC **sem** reformatar.

## 1. Entrar como Administrador

1. Na tela de logon (ou com Ctrl+Alt+Del):
2. **Outro usuário** → conta **Admin** (ou outra conta Administrators).
3. Nunca apague a conta Admin de recuperação.

Se o autologon Arcade “prende” a tela:

- Mantenha **Shift** pressionado durante a inicialização (em alguns hosts impede autologon), **ou**
- Entre offline / modo seguro e desative autologon (abaixo).

## 2. Desativar autologon (emergência)

Em **Admin**, PowerShell elevado:

```powershell
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 0 /f
```

Reinicie. O Windows pedirá login normal.

## 3. Restaurar shell do usuário Arcade (Explorer)

Se o Arcade ficar sem desktop e sem launcher:

- Use a UI TurboRama (Admin) → se houver rollback da Fase 2, **ou**
- Reinstale o Launcher e reconfigure shell com **Fase 2** de novo.

Não altere o shell **global** HKLM `Shell` se for conta Admin (deve permanecer `explorer.exe`).

## 4. Parar reinícios do Watchdog

Se o Watchdog ficar em loop:

```powershell
# Cria lock (equivalente a "Entrar manutenção")
New-Item -ItemType File -Force -Path C:\TurboRama\State\maintenance.lock | Out-Null
Set-Content C:\TurboRama\State\maintenance.lock "reason=manual-recovery`nuser=$env:USERNAME`nat=$(Get-Date -Format o)"
sc stop TurboRamaWatchdog
```

Ou UI: **Entrar manutenção**.

Para voltar ao normal: UI **Sair manutenção** ou Fase 6 com `--clear-locks`.

## 5. Serviços

```text
sc query TurboRamaWatchdog
sc query TurboRamaMaintenance
sc start TurboRamaWatchdog
sc start TurboRamaMaintenance
```

Pack: `REINSTALAR-SERVICOS.bat` (Admin).

## 6. Aceite / diagnóstico

```text
VALIDAR-ACEITE.bat
```

ou

```text
TurboRama.UI.exe --validate --clear-locks --quiet
```

Relatórios: `C:\TurboRama\Logs\Installer\`

## 7. Rollback de fases

UI / CLI (Admin):

- `--rollback-phase2` — kiosk (conta/shell/autologon/políticas) conforme snapshots  
- `--rollback-phase3` — serviços  

Baseline completo: `C:\TurboRama\Backup\<InstallationId>\baseline\`

## 8. Logs úteis

| Log | Caminho |
|-----|---------|
| Installer | `C:\TurboRama\Logs\Installer\installer.log` |
| Watchdog | `C:\TurboRama\Logs\Watchdog\watchdog.log` |
| Maintenance | `C:\TurboRama\Logs\Maintenance\maintenance.log` |
| Launcher | `C:\TurboRama\Logs\Launcher\launcher.log` |

## 9. Pack de fábrica

Em outro USB: `TurboRama-Factory-Pack` → `INSTALAR.bat` ou `INSTALAR-AUTOMATICO.bat` → reboot → `VALIDAR-ACEITE.bat`.

## 10. O que NÃO fazer

- Não apagar perfil Arcade com dados sem backup  
- Não formatar C: por “launcher não abriu” antes de tentar Admin + logs  
- Não habilitar UWF/Keyboard Filter em pânico  
- Não colocar senha kiosk vazia  

---

*TurboRama Secure 2.0.0-alpha — recuperação alinhada à proposta §31.*
