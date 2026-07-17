# Projeto Novo TurboRama

Reconstrução **segura** do sistema arcade kiosk TurboRama.

- **Constituição:** estudo `AUDITORIA GPT COMO CONSTRUIR PROGRAMA.txt`
- **Referência de fábrica:** `TurboRamaFactoryShell` (comportamento/UX, não monólito)
- **Versão:** 2.0.0-alpha — **Fases 0–6 + gap-fill**  
- **Senha kiosk de fábrica (no código):** `FactoryDefaults.KioskPassword` = `Lz2026@$` (conta Arcade)

## Princípio

Nenhuma alteração no Windows sem:

1. Detectar estado  
2. Salvar original (baseline)  
3. Aplicar  
4. Validar  
5. Poder restaurar exatamente o capturado  

## Estrutura

```text
Projeto Novo TurboRama\
  TurboRama.sln
  src\
    TurboRama.Core            # OperationResult, IInstallationStep, logs, paths
    TurboRama.Configuration  # turborama.json versionado
    TurboRama.Windows        # (stub) Registro, contas, ACL, BCD
    TurboRama.Security       # (stub) DPAPI, políticas SID
    TurboRama.Installation   # máquina de estados + engine + 1ª etapa (layout)
    TurboRama.Rollback       # rollback ordem inversa
    TurboRama.Diagnostics    # preflight
    TurboRama.Launcher       # stub — sem mexer no sistema
    TurboRama.Watchdog       # stub → Windows Service
    TurboRama.Maintenance    # stub → serviço + named pipe
    TurboRama.UI             # instalador Fase 0 (preflight + layout)
  tests\TurboRama.Tests
  docs\
  scripts\
```

## Requisitos

- Windows 10/11 x64  
- **.NET 8 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/8.0))  
- Conta Administrador para instalar layout em `C:\TurboRama`

## Build

```powershell
cd "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama"
dotnet restore
dotnet build TurboRama.sln -c Release
dotnet test
```

Ou:

```powershell
.\scripts\Compilar.ps1
```

## Executar UI (Admin recomendado)

```powershell
dotnet run --project src\TurboRama.UI -c Release
```

CLI:

```text
TurboRama.UI.exe --preflight
TurboRama.UI.exe --install-layout
```

## Layout em disco

```text
C:\TurboRama\
  App\ (Launcher, Watchdog, Maintenance, SecurityAgent)
  Frontend\ Config\ Data\ Saves\ Logs\ State\ Backup\ Recovery\ Updates\
```

## Perfis

| Perfil | Conteúdo |
|--------|----------|
| **KioskBasic** (padrão) | Conta, shell por usuário, autologon, políticas, watchdog |
| KioskHardened | + hook, maintenance service |
| ArcadeDedicated | + UWF/Filter/branding (opcional, com aviso) |

## Documentação

- **[DESCRITIVO-COMPLETO-PROJETO.md](docs/DESCRITIVO-COMPLETO-PROJETO.md)** — descritivo total + comparativo com estudo/FactoryShell  
- **[COMPARATIVO-PROPOSTA-VS-IMPLEMENTADO.md](docs/COMPARATIVO-PROPOSTA-VS-IMPLEMENTADO.md)** — checklist da proposta de reconstrução segura × o que foi feito  
- [REGRAS-NAO-FAZER.md](docs/REGRAS-NAO-FAZER.md)  
- [COMPARATIVO-E-CAMINHO.md](docs/COMPARATIVO-E-CAMINHO.md)  
- [MAPA-LEGADO.md](docs/MAPA-LEGADO.md)  

## Fase 1 — o que faz

1. **Captura baseline** em `C:\TurboRama\Backup\<Id>\baseline\baseline.json`
   - Registro (Winlogon, LSA, timeouts, marker) 32+64 bits com `existed`/tipo/valor  
   - BCD export + `bcd-enum-all.txt` + SHA-256  
   - ACL via icacls  
   - Serviços (sc.exe)  
   - Features Embedded/UWF (DISM)  
2. **Manifesto** `change-manifest.json`  
3. **Probe opcional** `HKLM\SOFTWARE\TurboRama\Secure\Phase1Probe` (prova Capture→Apply→Validate→Rollback)  
4. **Não** altera shell, autologon, conta kiosk, BCD ativo  

### UI (Admin)

1. Preflight  
2. Layout  
3. Capturar baseline  
4. Fase 1 completa (baseline + probe)  
5. Rollback probe  

CLI: `--baseline` | `--phase1` | `--rollback-phase1`

## Fase 2 — kiosk básico

Etapas: DeployLauncher → CreateKioskAccount (senha forte + DPAPI) → ConfigureUserShell (só hive do usuário) → ConfigureAutologon (Sysinternals) → ApplyKioskPolicies (por SID).

```text
ABRIR-FASE2.bat
```

Botão principal: **INSTALAR KIOSK (Fase 2)** → reiniciar PC.  
SHIFT no logon = conta Admin.  
Rollback: botão **Rollback kiosk**.

## Fase 3 — serviços

```text
ABRIR-FASE3.bat
```

- `TurboRamaWatchdog` — reinicia launcher com backoff; para em loop (TR-008)  
- `TurboRamaMaintenance` — named pipe `TurboRamaMaintenance` (comandos fixos)  
- `C:\TurboRama\State\maintenance.lock` — suspende reinícios  

UI: **INSTALAR SERVIÇOS** | Entrar/Sair manutenção | Status  

## Fase 4 — opcionais (default OFF)

Na UI: marcar UWF / Keyboard Filter / branding só se aceitar o risco.  
UWF precisa IoT/Enterprise. Keyboard Filter precisa Embedded lockdown.

## Fase 5 — pack de fábrica / instalador prático

```text
GERAR-PACK-FABRICA.bat
```

Gera:

```text
D:\tr-factory-pack\TurboRama-Factory-Pack\
D:\tr-factory-pack\TurboRama-Factory-Pack.zip
```

### Outro PC (recomendado)

1. Copie a **pasta inteira** ou o ZIP  
2. Opcional: jogo em `Frontend\`  
3. Admin → **`INSTALAR-COMPLETO.bat`** ou **`TurboRama.Setup.exe`**  
   - seed App → preflight → Fase 2 kiosk → Fase 3 serviços → Fase 6 aceite  
4. **Reinicie**  
5. Arcade autologon; senha manual se precisar: `Lz2026@$`  

CLI: `TurboRama.Setup.exe --install-full` (ou `TurboRama.UI.exe --install-full`)  

Alternativas: `INSTALAR.bat` (só UI), `INSTALAR-AUTOMATICO.bat` (F2+F3 quiet).

## Status das fases

| Fase | Conteúdo | Status |
|------|----------|--------|
| 0 | Fundação, layout, preflight | Completa |
| 1 | Baseline + manifesto + rollback | Completa |
| 2 | Kiosk (Arcade, shell, autologon, políticas) | Completa |
| 3 | Watchdog + Maintenance + Status | Completa |
| 4 | UWF / Filter / branding (código, default OFF) | Completa |
| 5 | Pack fábrica | Completa |
| 6 | Aceite de fábrica / validação pós-install | Completa |

## Fase 6 — aceite de fábrica

Valida segurança e saúde **depois** de instalar (não altera o kiosk, só reporta; com `--clear-locks` remove lock de manutenção).

```text
VALIDAR-ACEITE.bat
```

Ou CLI:

```text
TurboRama.UI.exe --validate --clear-locks --quiet
TurboRama.UI.exe --phase6
```

UI: botão **Fase 6 Aceite**.

Checklist: layout, baseline, conta Arcade (não Admin), DPAPI, autologon, serviços RUNNING, locks, defaults Fase 4, frontend, pipe.
