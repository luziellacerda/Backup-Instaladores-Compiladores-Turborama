# 07 — Bugs corrigidos + FMEA

## Bugs críticos (2026-07-20) — commit 8118eab

| ID | Sintoma | Causa | Correção |
|----|---------|-------|----------|
| B01 | Fase 2 falha em PC limpo (`LAUNCHER_SRC`) | DeployLauncher só paths de dev | Pack + seed + FindPackRoot |
| B02 | Serviços sem fonte no PC alvo | FindExe sem pack | Pack/seed first |
| B03 | Reinstall corrompe DLL | Seed com serviço RUNNING | Stop+kill+retry |
| B04 | Install “OK” com pack incompleto | Sem validação | Fail SEED_PACK_INCOMPLETE |
| B05 | Autologon silencioso falha | Só warning | Error preflight + seed |
| B06 | Crash sem .NET 8 | Só warning | Error FULL_NO_DOTNET8 / PF_DOTNET |
| B07 | SecurityAgent task inválida | Escape schtasks | BAT intermediário |
| B08 | Dois setups paralelos | Sem mutex | Global\TurboRamaFactoryFullInstall |

## Bugs críticos (2026-07-21) — pack F:\ recompilado

| ID | Sintoma | Causa | Correção |
|----|---------|-------|----------|
| **B09** | `ACCT_PWD` mínimo 12 caracteres | Fábrica `Lz2026@$` = 8 chars | Min = **8** em LocalAccountService |
| **B10** | Frontend “ausente” com jogos em `D:\TurboRama.exe` | Só path pasta `D:\Turborama\...` | Candidates flat + pasta (FactoryDefaults) |
| **B11** | UI instalador fora da tela | Form 940×760 fixo | MainForm adapta WorkingArea |

Detalhes: **`11-FIX-SENHA-E-FRONTEND-2026-07-21.md`**

## Matriz de falhas de instalação (resumo)

### Bloqueiam de propósito
Admin, sessão Arcade, sem Admin recovery, disco &lt;500MB, sem .NET 8, pack incompleto, hash errado, install duplicado, pack não encontrado.

### Runtime
Frontend ausente → kiosk sobe sem jogo (aviso).  
Crash loop frontend → Watchdog recovery.flag.  
KF CAD total → após reboot em IoT.

## Riscos ainda abertos (não 100%)
- Paths C:/D: ainda “convencionais” (não wizard livre)
- Senha fábrica universal (`Lz2026@$`) — trocar em produção multi-cliente
- Framework-dependent (.NET 8 Desktop)
- Unsigned / Authenticode
- KF completo depende edição IoT/Enterprise
- Fix 2026-07-21 pode ainda não estar no GitHub remoto

## Doc original no pack
`docs-projeto-existentes\FMEA-INSTALACAO-ANTI-FALHAS.md`  
(ou no pack: `docs\FMEA-INSTALACAO-ANTI-FALHAS.md`)

## Validação real (2026-07-21)
Neste host: install-full → Arcade + serviços → reboot → frontend **flat** abriu.  
Pack pen: `F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK` com bins novos + PACK-HASHES regenerado.
