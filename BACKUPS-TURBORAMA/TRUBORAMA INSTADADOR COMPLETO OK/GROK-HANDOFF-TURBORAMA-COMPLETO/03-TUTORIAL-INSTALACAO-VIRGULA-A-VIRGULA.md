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

**Pen atual:** `F:\TURBORAMA-KIOSK\01-INSTALADOR\`  
**Histórico:** `2-INSTALAR-JOGOS.bat` / `01-TURBORAMA-JOGOS\`

Arquivos que **devem ficar juntos**:

```
TurboRama-stable-20260720-win64-setup.exe
TurboRama-stable-20260720-win64-setup.exe.pkg.001
TurboRama-stable-20260720-win64-setup.exe.pkg.002
TurboRama-stable-20260720-win64-setup.exe.pkg.003
TurboRama-stable-20260720-win64-setup.exe.sha256.txt
```

- Rodar setup como Admin.  
- Destino clássico: **`D:\Turborama`**.  
- Alternativa: copiar pasta completa do PC modelo.  
- **Layout flat validado:** `D:\TurboRama.exe` + `emulationstation` na **raiz de D:\**  
  (o pack kiosk de 2026-07-21 detecta os dois).

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

## Paths (limitação + fix 2026-07-21)

- Kiosk: **sempre** `C:\TurboRama` no desenho atual  
- Jogos: **`D:\Turborama\TurboRama.exe`** (pasta) **ou** **`D:\TurboRama.exe`** (flat)  
- PC só com C: → reconfigurar `frontendExecutable` ou criar volume D:  
- Pack pen corrigido: `F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK`  
- **Produção = INSTALAR-COMPLETO.bat**, não a UI com botões sozinha

---

## Checklist de embalagem (venda)

- [ ] `VERIFICAR-KIT.bat` no kit de origem = tudo OK  
- [ ] Setup + 3 pkg presentes  
- [ ] Autologon64 no pack  
- [ ] PACK-HASHES.sha256 presente  
- [ ] .NET offline no kit  
- [ ] LEIA-ME legível  
- [ ] Após install em PC teste: Fase 6 OK + reboot OK
