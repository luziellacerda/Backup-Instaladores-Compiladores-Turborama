# Fix pack 2026-07-21 — senha, frontend flat, UI (OBRIGATÓRIO)

Atualização do Factory Pack após instalar em PC formatado e validar kiosk + jogo.

---

## Bugs corrigidos (B09–B11)

| ID | Sintoma | Causa | Correção |
|----|---------|-------|----------|
| **B09** | `FAIL CreateStandardUser (ACCT_PWD): Senha kiosk deve ter no mínimo 12 caracteres` | `LocalAccountService` exigia 12; fábrica `Lz2026@$` tem **8** | Mínimo = **8** (`FactoryDefaults.MinKioskPasswordLength`) |
| **B10** | Fase 6 / seed: frontend ausente `D:\Turborama\TurboRama.exe` | Só um path; PC modelo/setup usa **flat** `D:\TurboRama.exe` | `FactoryDefaults.GetFrontendCandidates` / `FindExistingFrontend` / `ResolveFrontendExecutable` |
| **B11** | Janela instalador maior que a tela; não dá para clicar | `MainForm` 940×760 fixo | Tamanho = WorkingArea; min 640×480; AutoScroll |

---

## Layouts de jogos aceitos

| Layout | Path do EXE | Onde está o ES |
|--------|-------------|----------------|
| **A — Pasta (clássico)** | `D:\Turborama\TurboRama.exe` | `D:\Turborama\emulationstation\` |
| **B — Flat (setup estável / cópia modelo)** | `D:\TurboRama.exe` | `D:\emulationstation\` |

Ordem de descoberta (resumo):
1. path configurado no JSON (se existir no disco)
2. `D:\TurboRama.exe`
3. `D:\Turborama\TurboRama.exe`
4. `C:\TurboRama\Frontend\TurboRama.exe` / `Frontend.exe`
5. ES em `D:\Turborama\...` ou `D:\emulationstation\...`
6. legados TURBOPCINSTALL

Seed grava no `turborama.json` o **primeiro EXE real** encontrado.

---

## Arquivos de código alterados

Fonte neste PC: `C:\Users\Admin\Turborama-src\ProjetoNovo\`

| Arquivo | Mudança |
|---------|---------|
| `FactoryDefaults.cs` | `MinKioskPasswordLength=8`; candidates frontend |
| `LocalAccountService.cs` | `password.Length < 8` |
| `FactoryFullInstall.cs` | `TryBindFrontendPath` + scripts ES flat |
| `PreflightService.cs` | candidatos frontend |
| `PostInstallValidationService.cs` | candidatos; aviso se JSON morto |
| `Launcher/Program.cs` | `ResolveFrontend` via FactoryDefaults |
| `UI/Program.cs` | resolve frontend no install-full |
| `UI/MainForm.cs` | tamanho adaptativo |
| `ProductConfiguration.cs` / `ConfigurationStore.cs` | preferido = pasta clássica |

Snapshots no handoff: `codigo-snapshot\src__TurboRama.*` (atualizados).

---

## Pack no pen (bins)

**Pasta:** `F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK\`

Recompilado e copiado (2026-07-21):
- `TurboRama.Setup.exe` / `TurboRama.UI.exe` + DLLs
- `App\Launcher|Watchdog|Maintenance\`
- `PACK-HASHES.sha256` **regenerado**
- `00-COMECE-AQUI.txt` / `LEIA-ME-FABRICA.txt` / `Frontend\LEIA-COPIAR-TURBORAMA.txt`

Backup bins antigos:
`F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK-BACKUP-*`

---

## Como instalar em outro PC (após este fix)

```text
1. Windows 10/11 x64 + Admin recovery (.NET 8 Desktop)
2. F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK\INSTALAR-COMPLETO.bat (Admin)
3. REBOOT → Arcade + Launcher
4. Jogos: layout A ou B (acima)
5. ROMs se necessário
6. Opcional: VALIDAR-ACEITE.bat (Admin)
```

Senha kiosk manual: **`Lz2026@$`**  
Não usar `INSTALAR.bat` (UI manual) como fluxo de fábrica.

---

## Git

- Commit histórico anti-falha: **`8118eab`** (repo Backup-Instaladores)
- **Fix 2026-07-21:** recompilado **localmente** neste PC; **push Git ainda pode estar pendente** no repositório remoto — se for commitar, incluir os arquivos da tabela acima + regenerar pack no build machine.

---

## O que NÃO reintroduzir

- Não voltar mínimo de senha para 12 sem alongar `FactoryDefaults.KioskPassword`
- Não apagar candidato `D:\TurboRama.exe`
- Não declarar “só pasta D:\Turborama” nos docs de fábrica
- Não deixar MainForm com tamanho fixo maior que monitores 1366×768 / 720p
