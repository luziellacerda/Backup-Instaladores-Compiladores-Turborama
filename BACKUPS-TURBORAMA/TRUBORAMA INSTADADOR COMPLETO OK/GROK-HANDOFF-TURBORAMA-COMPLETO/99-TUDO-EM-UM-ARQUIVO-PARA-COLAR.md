> **ATUALIZAÇÃO 2026-07-21:** Este arquivo 99 é o dump antigo consolidado.  
> Para o estado **atual** do pack e bugs B09–B11, leia **primeiro**:  
> `11-FIX-SENHA-E-FRONTEND-2026-07-21.md` + `00-LEIA-PRIMEIRO-PARA-GROK.md` + `07-BUGS-CORRIGIDOS-E-FMEA.md`.  
> Pack pen: `F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK` (bins recompilados).  
> Frontend: flat `D:\TurboRama.exe` **ou** pasta `D:\Turborama\TurboRama.exe`.  
> Senha fábrica `Lz2026@$` = 8 chars (min 8 no código).

---
# TURBORAMA — DOCUMENTO UNICO PARA GROK (cola isto no chat se nao puder anexar pasta)

Gerado: 2026-07-21


---

# INSTRUÇÃO PARA O GROK (outra máquina / outro chat)

**Leia esta pasta INTEIRA antes de alterar código ou inventar arquitetura.**

Data do handoff: **2026-07-20 / 2026-07-21**  
Produto: **TurboRama** (kiosk Windows + frontend EmulationStation/retrogaming)  
Idioma do produto/ops: **português (Brasil)**; UI do ES tem multi-idioma  
Versão shell kiosk: **2.0.0-alpha**

---

## O que esta pasta é

Tutorial **extremamente detalhado** do que foi feito, como foi feito, paths, código, bugs, kit de HD, git e decisões do usuário.

**Não invente pastas.** Use os paths reais listados.

---

## Ordem de leitura obrigatória

| # | Arquivo | Conteúdo |
|---|---------|----------|
| 1 | `00-LEIA-PRIMEIRO-PARA-GROK.md` | Este arquivo |
| 2 | `01-MAPA-COMPLETO-DO-PROJETO.md` | Mapa de discos, pastas, repos |
| 3 | `02-ARQUITETURA-DUAS-CAMADAS.md` | Windows kiosk vs jogos |
| 4 | `03-TUTORIAL-INSTALACAO-VIRGULA-A-VIRGULA.md` | Instalação passo a passo |
| 5 | `04-CODIGO-FACTORY-DETALHADO.md` | Código do instalador kiosk |
| 6 | `05-CODIGO-EMULATIONSTATION-KIOSK.md` | Código ES (bezels, teclas) |
| 7 | `06-HISTORICO-SESSAO-O-QUE-FOI-FEITO.md` | Cronologia da sessão |
| 8 | `07-BUGS-CORRIGIDOS-E-FMEA.md` | Bugs e anti-falha |
| 9 | `08-PATHS-GIT-LOGS-SEGREDOS.md` | Git, logs, senhas |
| 10 | `09-PROMPTS-PARA-CONTINUAR.md` | Como continuar o trabalho |
| 11 | `10-GLOSSARIO.md` | Termos |
| 12 | `codigo-snapshot/` | Cópias dos .cs/.cpp alterados |
| 13 | `configs-exemplo/` | JSON/INI reais |
| 14 | `docs-projeto-existentes/` | Docs do pack (FMEA, recovery…) |

---

## Regras que o usuário impôs (não violar)

1. **“Instalar o sistema” = Factory Pack** que transforma Windows em kiosk — **não** só o setup de jogos.
2. **Windows kiosk** deve ficar **igual ao PC de referência** (Arcade, autologon, serviços, Keyboard Filter, SecurityAgent).
3. **TurboRama/jogos** = copiar/instalar **depois**, quando Windows estiver apto (`D:\Turborama`).
4. Produto para **venda** → anti-falha máximo; não “passar quieto” com pack incompleto / sem .NET.
5. Kit de instalador na **unidade E:** para levar no HD.
6. Subir mudanças relevantes no **Git**.

---

## Frase de arranque para o Grok

> Você está no projeto TurboRama. Leia a pasta `04-TUTORIAL-GROK-DETALHADO` (ou `D:\TurboramaWork\GROK-HANDOFF-TURBORAMA-COMPLETO`). Há duas camadas: (A) Factory Pack → `C:\TurboRama` kiosk Windows; (B) setup jogos → `D:\Turborama`. Não misture. Código fonte do kiosk em `Projeto Novo TurboRama`. ES em `TurboramaEmulationStation` branch FICHEIRO-OK. Commit factory: 8118eab no repo Backup-Instaladores.

---

## Prompt mínimo se só puder colar um arquivo

Cole o conteúdo de:
- `06-HISTORICO-SESSAO-O-QUE-FOI-FEITO.md` +  
- `02-ARQUITETURA-DUAS-CAMADAS.md` +  
- `08-PATHS-GIT-LOGS-SEGREDOS.md`


---

# 01 — Mapa completo do projeto (paths reais)

## Máquina de desenvolvimento / referência

| Item | Valor |
|------|--------|
| OS | Windows 10 **IoT Enterprise LTSC 2021** (21H2) |
| Contas | `Admin` (manutenção), `Arcade` (kiosk, autologon) |
| Shell kiosk instalado em | `C:\TurboRama\` |
| Jogos / ES instalados em | `D:\Turborama\` |
| Unidade kit / pendrive | **E:** |

## Árvore mental (não confundir)

```
WINDOWS KIOSK (camada A)
  C:\TurboRama\
    App\Launcher\TurboRama.Launcher.exe     ← shell do Arcade
    App\Watchdog\TurboRama.Watchdog.exe    ← serviço Windows
    App\Maintenance\TurboRama.Maintenance.exe
    App\Tools\Autologon64.exe              ← Sysinternals
    Config\turborama.json
    State\installation-state.json
    Logs\...
    Backup\...

JOGOS / FRONTEND (camada B)
  D:\Turborama\
    TurboRama.exe                          ← o que o Launcher abre
    emulationstation\
    emulators\
    roms\                                  ← opcional / separado
    turborama.ini
    decorations\, screensaver_videos\, ...

CÓDIGO FONTE
  D:\Backup-Instaladores-Compiladores-Turborama\
    TURBORAMA INSTALER HOST\
      Projeto Novo TurboRama\              ← C# .NET 8 kiosk installer
      InstallerHost\                       ← C# setup de jogos (.pkg)
  D:\TurboramaWork\TurboramaEmulationStation\  ← C++ ES kiosk (git)

PACK GERADO
  D:\tr-factory-pack\TurboRama-Factory-Pack\

KIT HD (pronto para copiar)
  E:\Turborama-INSTALADOR-HD\
  E:\Turborama-PARA-OUTRO-PC\              ← kit anterior (ainda existe)
```

## Repositórios Git

### 1) Backup-Instaladores-Compiladores-Turborama
- URL: `https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama.git`
- Branch: `main`
- Commit handoff: **`8118eab`** — *Factory kiosk: install-full hardened for clean PC and HD kit.*
- Contém: Projeto Novo, InstallerHost, tools, scripts

### 2) TurboramaEmulationStation
- URL: `https://github.com/luziellacerda/TurboramaEmulationStation.git`
- Branch de trabalho: **`FICHEIRO-OK`**
- Contém: frontend kiosk C++ (Start menu, F7, bezels screensaver, F11 locadora, etc.)

## Kit E:\Turborama-INSTALADOR-HD (detalhe)

```
E:\Turborama-INSTALADOR-HD\
  00-WINDOWS-KIOSK\          ← cópia do Factory Pack (~11 MB)
  01-TURBORAMA-JOGOS\        ← setup + pkg.001/002/003 (~5.2 GB)
  02-INSTRUCOES\ORDEM.txt
  03-DOTNET-RUNTIME\windowsdesktop-runtime-8.0-win-x64.exe (~56 MB)
  04-TUTORIAL-GROK-DETALHADO\  ← ESTA PASTA DE DOCS
  0-INSTALAR-DOTNET.bat
  1-INSTALAR-WINDOWS-KIOSK.bat
  2-INSTALAR-JOGOS.bat
  LEIA-ME-PRIMEIRO.txt
  VERIFICAR-KIT.bat
  RESUMO-SESSAO-ABRIR-EM-QUALQUER-LUGAR.txt
```

## Ferramentas no PC de build

| Tool | Path típico |
|------|-------------|
| .NET SDK (publish pack) | `D:\tr-dotnet\dotnet.exe` ou `C:\Program Files\dotnet\dotnet.exe` |
| Git portable | `D:\tmp\PortableGit\cmd\git.exe` |
| Build pack | `Projeto Novo TurboRama\scripts\Build-FactoryPack.ps1` |
| Output pack | `D:\tr-factory-pack\TurboRama-Factory-Pack` |

## Comando para regenerar o Factory Pack

```powershell
cd "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama"
powershell -ExecutionPolicy Bypass -File .\scripts\Build-FactoryPack.ps1 -SkipZip
```

Publica: UI (Setup), Launcher, Watchdog, Maintenance → pasta pack + `TurboRama.Setup.exe` na raiz.


---

# 02 — Arquitetura: duas camadas (nunca misturar)

## Diagrama

```
┌─────────────────────────────────────────────────────────────┐
│  BOOT WINDOWS                                               │
│  AutoAdminLogon = Arcade                                    │
└───────────────────────────┬─────────────────────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  CAMADA A — C:\TurboRama  (Factory Pack / “Projeto Novo”)   │
│  • TurboRama.Launcher.exe  = shell do usuário Arcade        │
│  • SecurityAgent (--security-agent) Ctrl+End                │
│  • Watchdog (serviço) reinicia Launcher/frontend            │
│  • Maintenance (serviço + named pipe)                       │
│  • Políticas kiosk + Keyboard Filter (IoT)                  │
└───────────────────────────┬─────────────────────────────────┘
                            │ abre frontendExecutable
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  CAMADA B — D:\Turborama  (setup jogos / ES)                │
│  • TurboRama.exe → EmulationStation kiosk                   │
│  • emuladores, themes, roms, screensaver_videos             │
│  • Atalhos: Start menu senha, F11 locadora, F10 moeda…      │
└─────────────────────────────────────────────────────────────┘
```

## Config que une as duas camadas

Arquivo: `C:\TurboRama\Config\turborama.json`  
Campo crítico:

```json
"frontendExecutable": "D:\\Turborama\\TurboRama.exe"
```

Se este path não existir:
- Kiosk Windows **sobe** (camada A OK)
- Jogo **não abre** (Launcher avisa; Watchdog não deve derrubar o SO)

## Instaladores diferentes

| Instalador | O que faz | O que NÃO faz |
|------------|-----------|----------------|
| `TurboRama.Setup.exe` / `INSTALAR-COMPLETO.bat` (Factory) | Windows → kiosk | Não instala ROMs/ES |
| `TurboRama-stable-*-setup.exe` + `.pkg.*` | Extrai stack jogos em D:\Turborama | Não faz autologon Arcade sozinho |
| Cópia de `D:\Turborama` | Espelha PC modelo | Precisa Windows já kiosk ou config |

## Fluxo de fábrica desejado pelo usuário

1. Formata PC → Windows + Admin  
2. .NET 8 Desktop  
3. **Só Factory Pack** → Windows fica **como o PC referência**  
4. Reinicia → Arcade + Launcher  
5. **Depois** instala/copia TurboRama em `D:\Turborama`  
6. ROMs se necessário  

## Perfil de instalação

- `profile`: **KioskBasic**  
- `enableKeyboardFilter`: **true** (produção = igual PC IoT)  
- `enableUwf`: **false** (UWF default OFF — risco)  
- `enableSecurityMenu`: **true**  
- `productVersion`: **2.0.0-alpha**

## Componentes .NET (Projeto Novo)

| Projeto | Função |
|---------|--------|
| TurboRama.Core | Results, steps, paths, logs |
| TurboRama.Configuration | turborama.json, FactoryDefaults |
| TurboRama.Windows | Contas, shell, autologon, serviços, KF, baseline |
| TurboRama.Security | DPAPI, senhas |
| TurboRama.Installation | Engine + steps + FactoryFullInstall seed |
| TurboRama.Rollback | Rollback ordem inversa |
| TurboRama.Diagnostics | Preflight + Phase 6 + pack hashes |
| TurboRama.UI | WinForms + CLI (`--install-full`) |
| TurboRama.Launcher | Shell kiosk + loading + security agent |
| TurboRama.Watchdog | Windows Service |
| TurboRama.Maintenance | Windows Service + pipe |

## Princípio de instalação segura (não abandonar)

Cada step: **Capture → Apply → Validate**; em falha: **Rollback** das etapas aplicadas na sessão.  
Estado em: `C:\TurboRama\State\installation-state.json`  
Baseline em: `C:\TurboRama\Backup\{installationId}\`


---

# 03 — Instalação vírgula a vírgula (PC formatado)

## Pré-requisitos absolutos

1. Windows **10/11 x64** (ideal: **IoT Enterprise / Enterprise** para Keyboard Filter real).  
2. Conta **Administrador** de recuperação (não instalar logado como `Arcade`).  
3. Disco com espaço: C: ≥ 2 GB livre recomendado; jogos em D: vários GB.  
4. Pasta do kit **inteira** (não só um .exe solto).

---

## Passo 0 — .NET 8 Desktop Runtime

**Arquivo:** `E:\Turborama-INSTALADOR-HD\0-INSTALAR-DOTNET.bat`  
**Ou:** `03-DOTNET-RUNTIME\windowsdesktop-runtime-8.0-win-x64.exe`

- Executar como **Admin**.  
- Sem isto: Launcher/serviços **não sobem** (install-full agora **falha** com `FULL_NO_DOTNET8` / `PF_DOTNET`).

---

## Passo 1 — Windows Kiosk (Factory Pack)

**Arquivo:** `1-INSTALAR-WINDOWS-KIOSK.bat`  
**Ou:** `00-WINDOWS-KIOSK\INSTALAR-COMPLETO.bat` (Admin)  
**Ou:** `00-WINDOWS-KIOSK\TurboRama.Setup.exe` (Admin)

### O que o Setup executa por dentro (`--install-full`)

Ordem real no código `Program.RunFactoryFullInstallAsync`:

| # | Etapa | Código / detalhe |
|---|--------|------------------|
| 1 | Mutex global | `Global\TurboRamaFactoryFullInstall` — evita 2 installs |
| 2 | Achar pack | `FactoryFullInstall.FindPackRoot()` — pasta com `App\Launcher` |
| 3 | Checar .NET 8 Desktop | `HasDotNetDesktopRuntime8()` |
| 4 | **Seed** | Copia App/* → `C:\TurboRama`; para serviços; exige Autologon64 |
| 5 | InstallationId | GUID estável em config/state |
| 6 | **Preflight** | Admin, não-kiosk, disco, recovery Admin, hashes, Autologon tool |
| 7 | Restore point | Best-effort |
| 8 | **Fase 2** | DeployLauncher, CreateKioskAccount, UserShell, Autologon, Policies |
| 9 | **Fase 3** | DeployServicesBinaries, InstallWindowsServices |
| 10 | **Fase 4 + Security** | KeyboardFilter ON + `ProductionKioskSecurityService.Apply()` |
| 11 | **Fase 6** | `PostInstallValidationService` aceite fábrica |

### Resultado esperado após sucesso + **REBOOT**

- Login automático: **Arcade**  
- Shell: Launcher (não Explorer no Arcade)  
- Serviços: `TurboRamaWatchdog`, `TurboRamaMaintenance` = Running Automatic  
- `C:\TurboRama\Config\turborama.json` aponta frontend para `D:\Turborama\TurboRama.exe`  
- Ctrl+End: menu segurança (após filtro/reboot em IoT)  
- Senha kiosk fábrica se login manual: ver FactoryDefaults (**Lz2026@$** — documentada; trocar em produção)

### Se falhar

- Logs: `C:\TurboRama\Logs\Installer\`  
- Mensagens de erro com códigos: `FULL_*`, `SEED_*`, `PF_*`, `LAUNCHER_SRC`, etc.  
- Corrigir causa → rodar de novo (force re-aplica steps de kiosk/serviços)

---

## Passo 2 — TurboRama jogos

**Arquivo:** `2-INSTALAR-JOGOS.bat`  
**Pasta:** `01-TURBORAMA-JOGOS\`

Arquivos que **devem ficar juntos**:

```
TurboRama-stable-20260720-win64-setup.exe
TurboRama-stable-20260720-win64-setup.exe.pkg.001
TurboRama-stable-20260720-win64-setup.exe.pkg.002
TurboRama-stable-20260720-win64-setup.exe.pkg.003
TurboRama-stable-20260720-win64-setup.exe.sha256.txt
```

- Rodar setup como Admin.  
- Destino: **`D:\Turborama`**.  
- Alternativa: copiar pasta `D:\Turborama` completa do PC modelo (robocopy).

---

## Passo 3 — ROMs (opcional / separado)

- Destino: `D:\Turborama\roms\`  
- Kit de jogos **não obriga** ROMs no mesmo media (legal/comercial).  
- Sem ROMs: UI sobe, listas vazias ou parciais.

---

## Passo 4 — Validação

1. `00-WINDOWS-KIOSK\VALIDAR-ACEITE.bat` (Admin) → ACCEPT OK  
2. Reiniciar → Arcade sozinho  
3. Jogo abre via Launcher  
4. Admin login → Explorer  
5. Atalhos kiosk (Start, F11, F10, F7, F12, Ctrl+End)

---

## Alternativas (não usar em fábrica se não souber)

| Script | Uso |
|--------|-----|
| `INSTALAR.bat` | Só abre UI do instalador |
| `INSTALAR-AUTOMATICO.bat` | Fase 2+3 quiet **sem** segurança completa de produção |
| `App\Launcher\INSTALAR-SEGURANCA.bat` | Reaplica só lockdown (Keyboard Filter + agent) |
| `PREFLIGHT.bat` | Só checagens |
| `REINSTALAR-SERVICOS.bat` | Só Watchdog/Maintenance |

---

## Paths fixos (limitação conhecida)

- Kiosk: **sempre** `C:\TurboRama` no desenho atual  
- Jogos esperados: **`D:\Turborama\TurboRama.exe`**  
- PC só com C: → precisa reconfigurar `frontendExecutable` no JSON ou criar volume D:

---

## Checklist de embalagem (venda)

- [ ] `VERIFICAR-KIT.bat` no kit de origem = tudo OK  
- [ ] Setup + 3 pkg presentes  
- [ ] Autologon64 no pack  
- [ ] PACK-HASHES.sha256 presente  
- [ ] .NET offline no kit  
- [ ] LEIA-ME legível  
- [ ] Após install em PC teste: Fase 6 OK + reboot OK


---

# 04 — Código Factory Pack (detalhe arquivo a arquivo)

Raiz fonte:
`D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama\`

Snapshots copiados em: `codigo-snapshot\` (nomes com `__` no lugar de `\`).

---

## 4.1 Entry point CLI — `src\TurboRama.UI\Program.cs`

### Renomeação Setup
Se o EXE se chama `TurboRama.Setup.exe` e não há args → modo **`--install-full`** automático.

### Flags CLI relevantes
| Flag | Efeito |
|------|--------|
| `--install-full` / `--factory-install` / `--setup` | Instalação completa de fábrica |
| `--preflight` | Só preflight |
| `--phase2` / `--phase3` | Fases isoladas |
| `--validate` | Fase 6 aceite |
| `--quiet` / `-q` | Sem MessageBox |
| `--result <arquivo>` | Grava OK/FAIL + mensagem |
| `--rollback-phase2/3` | Rollback |

### `RunFactoryFullInstallAsync` (núcleo)
1. Mutex `Global\TurboRamaFactoryFullInstall`  
2. `FindPackRoot`  
3. `HasDotNetDesktopRuntime8` — **fail duro**  
4. `FactoryFullInstall.SeedPackToMachine`  
5. Preflight  
6. `RunPhase2Async(force: true)`  
7. `RunPhase3Async(force: true)`  
8. Força `EnableKeyboardFilter=true`, frontend `D:\Turborama\TurboRama.exe` se genérico  
9. `RunPhase4Async` (KF)  
10. `ProductionKioskSecurityService.Apply()`  
11. Phase 6 validate + clear locks  

### Build de steps
```
BuildPhase2Steps = layout + baseline + DeployLauncher + CreateKioskAccount
                 + ConfigureUserShell + ConfigureAutologon + ApplyKioskPolicies
BuildPhase3Steps = layout + baseline + DeployServicesBinaries + InstallWindowsServices
BuildPhase4Steps = layout + baseline + OptionalAdvancedModules (KF/UWF/branding via flags)
```

### Force reinstall
Em `RunPipelineAsync`, se `force`, remove de `CompletedStages`:
DeployLauncher, CreateKioskAccount, ConfigureUserShell, ConfigureAutologon,
ApplyKioskPolicies, DeployServicesBinaries, InstallWindowsServices, OptionalAdvancedModules

---

## 4.2 Seed — `src\TurboRama.Installation\FactoryFullInstall.cs`

### `FindPackRoot`
Procura a partir de `AppContext.BaseDirectory` e pais uma pasta com:
- `App\Launcher\TurboRama.Launcher.exe` **ou**
- `App\Watchdog\...` **ou**
- `Installer\` + `App\`

### `SeedPackToMachine` (hardening 2026-07-20)
1. **Valida pack completo** antes de copiar:
   - Launcher.exe, Watchdog.exe, Maintenance.exe, Autologon64.exe  
   - Se faltar → `SEED_PACK_INCOMPLETE`  
2. **Para serviços** Watchdog/Maintenance + kill processos  
3. `CopyTree` com **retry** (IOException/UnauthorizedAccess)  
4. Config template só se destino não existir  
5. Frontend EXEs opcionais de `Frontend\`  
6. `TryBindFrontendPath` — candidatos incluem `D:\Turborama\TurboRama.exe`  
7. Scripts ES power (shutdown/reboot/quit → `power-request.txt`)  
8. `CadBlockService.ApplySystemWide` best-effort  
9. **Validate** pós-cópia — fail se EXE sumiu  

### Bug histórico corrigido
Antes: seed copiava com serviço RUNNING → sharing violation em reinstall.

---

## 4.3 DeployLauncher — `Steps\DeployLauncherStep.cs`

### Bug histórico (CRÍTICO em PC limpo)
Só procurava paths de **dev** (`D:\tr-factory-pack\...`).  
No PC cliente o pack tem `App\Launcher` e o seed já copiou — mas a fase 2 **não usava** e falhava `LAUNCHER_SRC`.

### Ordem de fontes agora
1. `FindPackRoot()\App\Launcher\TurboRama.Launcher.exe`  
2. `BaseDirectory\App\Launcher\...`  
3. `C:\TurboRama\App\Launcher\...` (seed)  
4. Builds dev  
5. Se dest existe e fonte não → usa dest  

Deploy: `AtomicAppDeployer.DeployDirectory` (staging/previous), fallback copy.

---

## 4.4 DeployServices — `Steps\DeployServicesBinariesStep.cs`

1. Stop + kill serviços  
2. Tenta `dotnet publish` se achar solution + SDK (dev)  
3. Senão `FindExe` em **pack/seed** (corrigido 2026-07-20)  
4. Atomic deploy + validate runtimeconfig  
5. Retry copy  

---

## 4.5 InstallationEngine — `InstallationEngine.cs`

Para cada step não concluído:
1. Marca `FailedStage = name:IN_PROGRESS` + save state  
2. Capture → se fail, marca Failed e retorna  
3. Apply → se fail, rollback stack + fail  
4. Validate → se fail, rollback stack + fail  
5. Add CompletedStages + save  

Rollback: ordem inversa, exceptions no rollback viram **Warning** (não engolem o fail original).

---

## 4.6 Preflight — `Diagnostics\PreflightService.cs`

Erros que **bloqueiam** (lista expandida):
- Não Admin  
- Sessão kiosk  
- Nome kiosk inválido  
- Disco C: &lt; 500 MB  
- Sem outra conta Admin  
- Sem escrita em C:\TurboRama  
- Pack hash mismatch (se PACK-HASHES existe)  
- **Sem .NET 8 Desktop Runtime** (agora ERROR)  
- **Sem Autologon64** no pack/Tools (agora ERROR)  

Avisos: BitLocker, frontend ausente, serviços já RUNNING, state prévio, etc.

---

## 4.7 Segurança produção — `Windows\Optional\ProductionKioskSecurityService.cs`

Espelha `INSTALAR-SEGURANCA.bat` sem prompt de reboot:

1. DISM DeviceLockdown + Client-KeyboardFilter  
2. `KeyboardFilterModuleService.Enable()` (AUTO, **sem** sc start forçado pré-reboot em IoT)  
3. Registry `HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter`  
   - Ctrl+Alt+Del = Blocked  
   - Ctrl+End = Allowed  
   - Win, Alt+Tab, Alt+F4, Ctrl+Esc, Shift+Ctrl+Esc = Blocked  
4. Policies System: DisableTaskMgr, DisableChangePassword, DisableLockWorkstation, HideFastUserSwitching  
5. Explorer: NoLogoff  
6. WEKF WMI best-effort  
7. Run key + schtasks via **BAT** `C:\TurboRama\Logs\run-security-agent.bat` (evita escape quebrado)  
8. Task boot `TurboRamaForceKeyboardFilter`  
9. Escreve `Logs\SEGURANCA-STATUS.txt`  

Best-effort se edição sem IoT: políticas + agent ainda aplicam.

---

## 4.8 Config — `ProductConfiguration` / `ConfigurationStore`

Default produção:
```
FrontendExecutable = D:\Turborama\TurboRama.exe
EnableKeyboardFilter = true
EnableSecurityMenu = true
KioskUser = Arcade (FactoryDefaults)
```

Senha kiosk: `FactoryDefaults.KioskPassword` no código (**não** regrava em JSON no Save).

---

## 4.9 Build pack — `scripts\Build-FactoryPack.ps1`

1. `dotnet publish` UI, Launcher, Watchdog, Maintenance (win-x64, framework-dependent)  
2. Monta pasta pack  
3. Copia bats de segurança de `C:\TurboRama\App\Launcher` se existirem  
4. `TurboRama.Setup.exe` = cópia do UI na raiz  
5. Autologon64 de C:\TurboRama ou paths legados  
6. Config JSON com frontend D:\Turborama  
7. INSTALAR-COMPLETO.bat / SETUP.bat / PREFLIGHT / VALIDAR  
8. PACK-HASHES.sha256  

Output: `D:\tr-factory-pack\TurboRama-Factory-Pack`

---

## 4.10 InstallerHost (jogos) — mudanças no commit

Arquivos:
- `InstallerHost\InstallControl.cs`  
- `InstallerHost\Program.cs`  

Escopo: estabilidade do setup de **jogos** (.pkg Zip64, progresso, validação ES ≥ 50MB, mutex).  
**Não** é o kiosk Windows.

---

## 4.11 Como debugar no código

1. Rodar UI com `--install-full --result %TEMP%\tr.txt`  
2. Ler `C:\TurboRama\Logs\Installer\installer.log`  
3. Ler `State\installation-state.json` (`FailedStage`, `CompletedStages`)  
4. Comparar com `Backup\{id}\baseline`


---

# 05 — EmulationStation / kiosk UI (C++)

Repo: `D:\TurboramaWork\TurboramaEmulationStation`  
Branch: **`FICHEIRO-OK`**  
Remote: `https://github.com/luziellacerda/TurboramaEmulationStation.git`  
Deploy runtime: `D:\Turborama\emulationstation\` (+ `TurboRama.exe` bootstrap)

Snapshots: `codigo-snapshot\ES__*`

---

## 5.1 Objetivo desta camada

Experiência **locadora/kiosk** dentro do frontend de jogos:
- Menu Start com **senha**
- Controles de crédito (F10/F11/F7/F12…)
- Screensaver com **molduras (bezels) por pasta de vídeo**
- Painéis locadora / segurança de UI (não confundir com SecurityAgent do Launcher C#)

---

## 5.2 Commits relevantes (históricos recentes)

| Commit (short) | Tema |
|----------------|------|
| `3cd0fed68` | Start = menu senha de volta; F7 = parar contador (não menu) |
| `0df1cb58b` | (intermediário) menu no F7 — **revertido** pelo de cima |
| `7bbf074c4` | Bezels distintos switch/xboxone/pc/ps4/ps5 |
| `b060b3321` / `dc91f3c3e` | Bezel = **nome da pasta** do vídeo → `systems/{pasta}.png` |
| `110f57556` | Remove aliases errados e pack amador de art |
| `0fb128372` | F11 Turbo Sistema; (histórico Ctrl+End UI) |
| `d63bc5f5a` | Locadora Turborama completa |

---

## 5.3 Screensaver / bezels — `SystemScreenSaver.cpp`

### Regra de produto (usuário exigiu)
- Bezel **não** é arte do jogo filho.  
- Bezel = **pasta pai** onde o vídeo está.  
  Ex.: vídeo em `...\screensaver_videos\ps5\foo.mp4` → moldura `systems/ps5.png`  
- Pastas distintas devem ter PNG distintos (ps4 ≠ ps5 ≠ switch ≠ xboxone ≠ pc).

### Dual root de vídeos
- Root interno + externo (config) — vídeos **não** vão “dentro do EXE”; ficam em pasta `screensaver_videos` (cópia CMake para lado do EXE em builds).

### O que **não** fazer de novo
- Não gerar molduras amadoras genéricas iguais para ps3/ps4/ps5.  
- Não usar `setGame` para “adivinhar” moldura por rom name se a regra é pasta.

---

## 5.4 Teclas / ViewController / main

| Tecla | Comportamento atual desejado |
|-------|------------------------------|
| **Start** | Menu com **senha** (requestMainMenuAccess) |
| **F7** | **Parar** contador (não abrir menu) |
| **F11** | Locadora / Turbo Sistema |
| **F10** | Moeda |
| **F8** | Pausa |
| **F12** | Zerar |
| Alt+End | Desativado (histórico) |

Arquivos típicos:
- `es-app\src\views\ViewController.cpp` — Start → menu senha  
- `es-app\src\main.cpp` — F7 stop, F11 locadora  

---

## 5.5 Relação com o Launcher C#

| Atalho | Quem trata |
|--------|------------|
| Ctrl+End (menu PIN segurança Windows) | **Launcher** `--security-agent` (camada A) |
| Start / F11 / F10 no ES | **EmulationStation** (camada B) |

Não unificar sem o usuário pedir — são camadas diferentes.

---

## 5.6 Build ES (contexto)

- CMake no repo TurboramaEmulationStation  
- Work path histórico: `D:\TurboramaWork\TurboramaEmulationStation`  
- Output usado no PC: `D:\Turborama\emulationstation\`  
- `turborama.ini` em `D:\Turborama\` tem `LanguageDetection=1` (idioma do Windows)

---

## 5.7 Ao mexer no ES

1. Trabalhar na branch **FICHEIRO-OK**  
2. Não reintroduzir menu no F7  
3. Bezel = pasta do vídeo  
4. Testar com pastas reais em `screensaver_videos\{sistema}\`  
5. Push no remote ES se o usuário pedir


---

# 06 — Histórico da sessão (o que foi feito e por quê)

Cronologia condensada das decisões do usuário e ações do agente (2026-07-16 → 2026-07-21).

---

## Bloco A — EmulationStation kiosk (antes / durante sessão longa)

1. Screensaver: vídeos em pasta (não embutidos no EXE).  
2. Dual root de `screensaver_videos`.  
3. Bezels: bug de alias → regra **pasta = systems/{folder}.png**.  
4. Rejeição de art amadora gerada para ps4/ps5 iguais.  
5. Teclas kiosk: Start com senha; tentativa F7=menu **revertida** → Start de novo, F7=parar.  
6. Desativar Alt+End; F11 labels Turbo Sistema.  
7. Commits na branch `FICHEIRO-OK` do repo ES (já no GitHub).

---

## Bloco B — Installer de jogos (InstallerHost / RetroBuild)

1. Auditoria de instalação limpa.  
2. Package estável `TurboRama-stable-20260720-win64-setup.exe` + `.pkg.001/002/003`.  
3. Kit inicial em `E:\Turborama-PARA-OUTRO-PC` com setup+pkg+ROMs (parcial).  
4. **Interpretação errada** do agente: tratou “instalar sistema” só como setup de jogos.

---

## Bloco C — Correção de entendimento do usuário

Usuário:  
> “TurboRama-Factory-Pack isso não vai usar? quando falei de instalar sistema estava me referindo ao que criamos”  
> “falei no instalador que transforma windows em kiosk”

**Correto:**
- **Sistema** = Factory Pack (Windows → kiosk)  
- **Jogos** = setup/pkg ou cópia `D:\Turborama`

---

## Bloco D — Factory Pack = Windows igual a este PC

1. Auditoria: pack existia em `D:\tr-factory-pack`, **não** estava no kit E: completo.  
2. PC referência já tinha: Arcade, autologon, Watchdog, Maintenance, SecurityAgent, MsKeyboardFilter.  
3. install-full antigo: só fases 2+3+6 — **sem** lockdown completo.  
4. Implementado:
   - `ProductionKioskSecurityService`  
   - install-full chama Fase 4 KF + security  
   - frontend default `D:\Turborama\TurboRama.exe`  
   - LEIA-ME e Build-FactoryPack atualizados  
5. Rebuild pack.

---

## Bloco E — Anti-falha “produto não pode ter erros”

1. Varredura FMEA de instalação.  
2. Bugs críticos corrigidos (ver `07-BUGS-CORRIGIDOS-E-FMEA.md`):  
   B01–B08 (DeployLauncher paths, seed, .NET, Autologon, mutex, schtasks…).  
3. Pack regenerado + doc FMEA.

---

## Bloco F — Kit HD + Git

1. Pasta **`E:\Turborama-INSTALADOR-HD`**:
   - 00 kiosk, 01 jogos, 03 dotnet offline, bats 0/1/2, LEIA-ME  
2. Git push repo Backup:
   - commit **`8118eab`** em `main`  
3. ES: branch FICHEIRO-OK já sincronizada (sem commits locais pendentes).

---

## Bloco G — Handoff multi-programa

1. `RESUMO-SESSAO-*.txt` (curto).  
2. Esta pasta **`04-TUTORIAL-GROK-DETALHADO`** (tutorial longo + snapshots de código).

---

## O que o usuário quer no estado final

| Pedido | Estado |
|--------|--------|
| Installer deixa Windows como este | Implementado no install-full (IoT ideal) |
| Jogos = copiar depois | Config + docs alinhados |
| Kit no E: com tudo para HD | `E:\Turborama-INSTALADOR-HD` |
| Subir no git o que mudou | `8118eab` pushed |
| Outro Grok entender tudo | Esta pasta |

---

## O que NÃO foi feito / ainda aberto

- Paths C:/D: configuráveis no wizard  
- Senha por máquina (ainda universal Lz2026@$)  
- Authenticode  
- Teste automatizado em VM limpa arquivado nesta sessão  
- SKU internacional limpo (sem ROMs) — só auditado  
- ArcadeTimer reativado no kiosk live (estava DISABLED)


---

# 07 — Bugs corrigidos + FMEA

## Bugs críticos (2026-07-20) — todos corrigidos no commit 8118eab

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

## Matriz de falhas de instalação (resumo)

### Bloqueiam de propósito
Admin, sessão Arcade, sem Admin recovery, disco &lt;500MB, sem .NET 8, pack incompleto, hash errado, install duplicado, pack não encontrado.

### Runtime
Frontend ausente → kiosk sobe sem jogo.  
Crash loop frontend → Watchdog recovery.flag.  
KF CAD total → após reboot em IoT.

## Riscos ainda abertos (não 100%)
Paths fixos C:/D:, senha fábrica universal, framework-dependent (.NET), unsigned, KF depende IoT, AV agressivo.

## Doc original no pack
`docs-projeto-existentes\FMEA-INSTALACAO-ANTI-FALHAS.md`  
(ou no pack: `docs\FMEA-INSTALACAO-ANTI-FALHAS.md`)

## ULTRA-HARD histórico neste PC (Jul 2026)
Houve relatório `C:\TurboRama\Logs\Installer\ULTRA-HARD-REPORT.txt` com PASS de testes de fábrica **neste** host. Não substitui teste em VM limpa do pack **novo** pós-8118eab.


---

# 08 — Paths, Git, logs, segredos

## Paths sagrados

| Uso | Path |
|-----|------|
| Kiosk install | `C:\TurboRama` |
| Config live | `C:\TurboRama\Config\turborama.json` |
| State | `C:\TurboRama\State\installation-state.json` |
| Logs install | `C:\TurboRama\Logs\Installer\` |
| Logs launcher | `C:\TurboRama\Logs\Launcher\launcher.log` |
| Logs watchdog | `C:\TurboRama\Logs\Watchdog\watchdog.log` |
| Segurança status | `C:\TurboRama\Logs\SEGURANCA-STATUS.txt` |
| Jogos | `D:\Turborama` |
| Frontend EXE | `D:\Turborama\TurboRama.exe` |
| Fonte kiosk C# | `...\Projeto Novo TurboRama\` |
| Fonte ES C++ | `D:\TurboramaWork\TurboramaEmulationStation` |
| Pack build | `D:\tr-factory-pack\TurboRama-Factory-Pack` |
| Kit HD | `E:\Turborama-INSTALADOR-HD` |
| Este tutorial | `E:\Turborama-INSTALADOR-HD\04-TUTORIAL-GROK-DETALHADO` |
| Cópia D: | `D:\TurboramaWork\GROK-HANDOFF-TURBORAMA-COMPLETO` |

## Git

### Backup-Instaladores
```
https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama.git
branch: main
commit: 8118eab2b359f66881d82e8ca25f3644c350d0aa
```

Arquivos no commit:
- InstallerHost InstallControl.cs, Program.cs  
- Projeto Novo: Program.cs, FactoryFullInstall.cs, DeployLauncher, DeployServices, Preflight, PostInstall, Configuration*, ProductionKioskSecurityService.cs, Build-FactoryPack.ps1, FMEA.md  
- tools/Build-InstallPackage-Stable.ps1  

### EmulationStation
```
https://github.com/luziellacerda/TurboramaEmulationStation.git
branch: FICHEIRO-OK
```

## Segredos / credenciais de fábrica

| Item | Valor / nota |
|------|----------------|
| Senha kiosk fábrica | `Lz2026@$` (FactoryDefaults + docs) — **trocar em produção** |
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

## Git local no PC de build

```
D:\tmp\PortableGit\cmd\git.exe
```


---

# 09 — Prompts prontos para colar em outro Grok / Cursor

## Prompt 1 — Continuidade geral

```
Leia a pasta E:\Turborama-INSTALADOR-HD\04-TUTORIAL-GROK-DETALHADO (ou
D:\TurboramaWork\GROK-HANDOFF-TURBORAMA-COMPLETO) inteira, começando por
00-LEIA-PRIMEIRO-PARA-GROK.md. Projeto TurboRama: duas camadas —
(A) Factory Pack C:\TurboRama kiosk Windows; (B) D:\Turborama jogos.
Código C# em Projeto Novo TurboRama; ES em TurboramaEmulationStation branch FICHEIRO-OK.
Commit factory 8118eab no repo Backup-Instaladores. Não misture instaladores.
Aguarde minha tarefa.
```

## Prompt 2 — Só instalação / kit

```
Use E:\Turborama-INSTALADOR-HD e o tutorial 03-TUTORIAL-INSTALACAO-VIRGULA-A-VIRGULA.md.
Ordem: .NET → Factory kiosk → reboot → setup jogos D:\Turborama → ROMs opcional.
```

## Prompt 3 — Só código kiosk C#

```
Trabalhe em:
D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama
Leia 04-CODIGO-FACTORY-DETALHADO.md e codigo-snapshot\.
Não altere ES a menos que eu peça. Rebuild com scripts\Build-FactoryPack.ps1.
```

## Prompt 4 — Só EmulationStation

```
Repo D:\TurboramaWork\TurboramaEmulationStation branch FICHEIRO-OK.
Leia 05-CODIGO-EMULATIONSTATION-KIOSK.md.
Regras: Start=menu senha; F7=parar; bezel=pasta do vídeo systems/{pasta}.png.
```

## Prompt 5 — Teste VM limpa

```
Monte roteiro de teste VM Windows limpo do pack em
E:\Turborama-INSTALADOR-HD\00-WINDOWS-KIOSK usando 07-BUGS e ROTEIRO-TESTES-VM.md.
Critério: PREFLIGHT 0 erros, install-full 0, reboot Arcade, VALIDAR-ACEITE OK.
```

## Prompt 6 — Venda internacional

```
Leia auditoria multi-país da sessão (scores BR 58 / LatAm 32 / EUA-UE 18).
Não embutir ROMs comerciais. SKU: TR-SHELL + TR-UI empty. P0: senha por máquina, paths, signing.
```


---

# 10 — Glossário TurboRama

| Termo | Significado |
|-------|-------------|
| Factory Pack | Pasta TurboRama-Factory-Pack / 00-WINDOWS-KIOSK — instala kiosk Windows |
| Projeto Novo | Solution C# .NET 8 do kiosk seguro |
| InstallerHost | Setup C# dos **jogos** (.exe + .pkg) |
| install-full | Modo Setup que faz seed+F2+F3+segurança+F6 |
| Seed | Cópia App do pack → C:\TurboRama |
| Fase 2 | Conta Arcade, shell, autologon, políticas |
| Fase 3 | Serviços Watchdog + Maintenance |
| Fase 4 | Opcionais (Keyboard Filter, UWF, branding) |
| Fase 6 | Aceite / validação pós-install |
| Arcade | Conta kiosk não-admin |
| Launcher | Shell do Arcade; sobe frontend |
| Watchdog | Serviço que reinicia Launcher/frontend |
| Maintenance | Serviço + pipe de manutenção |
| SecurityAgent | Launcher --security-agent; Ctrl+End |
| Keyboard Filter | MsKeyboardFilter IoT; bloqueia CAD etc. |
| frontendExecutable | Path do jogo no turborama.json |
| FICHEIRO-OK | Branch ES com locadora/kiosk UI estável |
| Bezel | Moldura do screensaver por pasta de vídeo |
| Locadora | Modo crédito/tempo no ES (F11) |
| ULTRA-HARD | Suite de testes de fábrica no PC referência |
| KioskBasic | Profile de instalação padrão |
| DPAPI | Proteção do segredo da senha kiosk em disco |
| .pkg | Partes Zip64 do instalador de jogos |


