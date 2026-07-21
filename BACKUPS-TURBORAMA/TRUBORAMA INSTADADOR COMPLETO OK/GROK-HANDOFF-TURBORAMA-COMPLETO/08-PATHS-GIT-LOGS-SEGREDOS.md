# 08 — Paths, Git, logs, segredos

## Paths sagrados (atualizado 2026-07-21)

| Uso | Path |
|-----|------|
| Kiosk install | `C:\TurboRama` |
| Config live | `C:\TurboRama\Config\turborama.json` |
| State | `C:\TurboRama\State\installation-state.json` |
| Logs install | `C:\TurboRama\Logs\Installer\` |
| Logs launcher | `C:\TurboRama\Logs\Launcher\launcher.log` |
| Logs watchdog | `C:\TurboRama\Logs\Watchdog\watchdog.log` |
| Segurança status | `C:\TurboRama\Logs\SEGURANCA-STATUS.txt` |
| Frontend **flat** (validado) | **`D:\TurboRama.exe`** |
| Frontend **pasta** (clássico) | `D:\Turborama\TurboRama.exe` |
| ES flat | `D:\emulationstation\` |
| ES pasta | `D:\Turborama\emulationstation\` |
| Fonte kiosk editada (este PC) | `C:\Users\Admin\Turborama-src\ProjetoNovo\` |
| Fonte kiosk (PC build histórico) | `...\Projeto Novo TurboRama\` |
| Fonte ES C++ | `D:\TurboramaWork\TurboramaEmulationStation` |
| Pack pen **atual** | **`F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK`** |
| Setup jogos pen | `F:\TURBORAMA-KIOSK\01-INSTALADOR` |
| **Este tutorial** | **`F:\TURBORAMA-KIOSK\GROK-HANDOFF-TURBORAMA-COMPLETO`** |
| Pack build histórico | `D:\tr-factory-pack\TurboRama-Factory-Pack` |
| Kit HD histórico | `E:\Turborama-INSTALADOR-HD` |

## Git

### Backup-Instaladores
```
https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama.git
branch: main
commit anti-falha: 8118eab2b359f66881d82e8ca25f3644c350d0aa
fix 2026-07-21: LOCAL (senha/frontend/UI) — push pode estar pendente
```

### EmulationStation
```
https://github.com/luziellacerda/TurboramaEmulationStation.git
branch: FICHEIRO-OK
```

## Segredos / credenciais de fábrica

| Item | Valor / nota |
|------|----------------|
| Senha kiosk fábrica | `Lz2026@$` (8 chars) — **MinKioskPasswordLength = 8** |
| PIN menu Ctrl+End default | Mesma senha kiosk se não configurado |
| Conta kiosk | `Arcade` (não Admin) |
| Conta manutenção | `Admin` (ou outra Admin) |
| DefaultPassword no Winlogon | Deve estar **ausente** (usa Autologon/LSA) |

**Atenção:** senha está em LEIA-MEs e código — inadequado para revenda multi-cliente sem rotação.

## Serviços Windows

| Nome | EXE |
|------|-----|
| TurboRamaWatchdog | `C:\TurboRama\App\Watchdog\TurboRama.Watchdog.exe` |
| TurboRamaMaintenance | `C:\TurboRama\App\Maintenance\TurboRama.Maintenance.exe` |
| MsKeyboardFilter | Serviço OS (IoT) |

## Tarefas agendadas típicas

- TurboRamaSecurityAgent (ONLOGON)  
- TurboRamaSecurityAgentKeepAlive (MINUTE / 2)  
- TurboRamaForceKeyboardFilter (ONSTART)

## Config live exemplo (este PC pós-install)

Ver `configs-exemplo\turborama-LIVE-PC.json` — `frontendExecutable`: `D:\\TurboRama.exe`
