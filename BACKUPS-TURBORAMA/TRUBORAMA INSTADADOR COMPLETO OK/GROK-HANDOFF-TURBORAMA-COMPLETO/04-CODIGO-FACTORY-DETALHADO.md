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

## 4.8 Config — `ProductConfiguration` / `ConfigurationStore` / `FactoryDefaults`

Default produção (JSON preferido):
```
FrontendExecutable = D:\Turborama\TurboRama.exe   // preferido pasta
EnableKeyboardFilter = true
EnableSecurityMenu = true
KioskUser = Arcade (FactoryDefaults)
KioskPassword fábrica = Lz2026@$
MinKioskPasswordLength = 8   // NÃO 12 — B09
```

**Frontend (B10 — 2026-07-21):**  
`FactoryDefaults.GetFrontendCandidates` / `FindExistingFrontend` / `ResolveFrontendExecutable`  
incluem **flat** `D:\TurboRama.exe` e pasta. Usado em seed, Launcher, Preflight, Fase 6.

Senha kiosk: `FactoryDefaults.KioskPassword` no código (**não** regrava em JSON no Save).

**UI (B11):** `MainForm` ajusta Width/Height ao WorkingArea (não fixar 940×760 cego).

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
