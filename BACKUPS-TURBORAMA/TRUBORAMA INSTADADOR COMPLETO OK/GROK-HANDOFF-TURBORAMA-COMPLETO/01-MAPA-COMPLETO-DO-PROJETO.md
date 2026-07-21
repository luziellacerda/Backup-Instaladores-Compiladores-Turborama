# 01 — Mapa completo do projeto (paths reais)

## Máquina de referência / este PC (2026-07-21)

| Item | Valor |
|------|--------|
| OS | Windows 10 **IoT Enterprise LTSC 2021** (21H2) |
| Contas | `Admin` (manutenção), `Arcade` (kiosk, autologon) |
| Shell kiosk instalado em | `C:\TurboRama\` |
| Jogos / ES **neste PC** | **Layout flat:** `D:\TurboRama.exe` + `D:\emulationstation\` + `D:\emulators\` + `D:\bios\` |
| Layout alternativo (docs clássicos) | `D:\Turborama\TurboRama.exe` (pasta) |
| Unidade kit / pendrive **atual** | **`F:\TURBORAMA-KIOSK`** |
| Unidade kit (doc histórico) | **E:** (`E:\Turborama-INSTALADOR-HD`) |

## Árvore mental (não confundir)

```
WINDOWS KIOSK (camada A)
  C:\TurboRama\
    App\Launcher\TurboRama.Launcher.exe     ← shell do Arcade
    App\Watchdog\TurboRama.Watchdog.exe    ← serviço Windows
    App\Maintenance\TurboRama.Maintenance.exe
    App\Tools\Autologon64.exe              ← Sysinternals
    Config\turborama.json                  ← frontendExecutable
    State\installation-state.json
    Logs\...
    Backup\...

JOGOS / FRONTEND (camada B) — dois layouts válidos
  LAYOUT FLAT (validado 2026-07-21):
    D:\TurboRama.exe
    D:\emulationstation\
    D:\emulators\
    D:\bios\
    D:\turborama.ini

  LAYOUT PASTA (clássico):
    D:\Turborama\
      TurboRama.exe
      emulationstation\
      emulators\
      roms\                                  ← opcional
      turborama.ini

CÓDIGO FONTE
  D:\Backup-Instaladores-Compiladores-Turborama\   ← PC de build (se existir)
    TURBORAMA INSTALER HOST\
      Projeto Novo TurboRama\
  C:\Users\Admin\Turborama-src\ProjetoNovo\        ← fonte editada + rebuild 2026-07-21
  D:\TurboramaWork\TurboramaEmulationStation\      ← C++ ES kiosk (git)

PACK / KIT
  F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK\     ← pack kiosk CORRIGIDO (pen)
  F:\TURBORAMA-KIOSK\01-INSTALADOR\                ← setup jogos + pkg
  F:\TURBORAMA-KIOSK\GROK-HANDOFF-TURBORAMA-COMPLETO\  ← este tutorial
  D:\tr-factory-pack\TurboRama-Factory-Pack\       ← output build (se existir)
  E:\Turborama-INSTALADOR-HD\                      ← kit histórico
```

## Repositórios Git

### 1) Backup-Instaladores-Compiladores-Turborama
- URL: `https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama.git`
- Branch: `main`
- Commit anti-falha: **`8118eab`**
- Fix 2026-07-21 (senha/frontend/UI): **local neste PC** — push remoto pode estar pendente

### 2) TurboramaEmulationStation
- URL: `https://github.com/luziellacerda/TurboramaEmulationStation.git`
- Branch: **`FICHEIRO-OK`**

## Kit F:\TURBORAMA-KIOSK (pen atual)

```
F:\TURBORAMA-KIOSK\
  00-SISTEMA-WINDOWS-KIOSK\     ← Factory Pack (bins 2026-07-21)
  01-INSTALADOR\                ← TurboRama-stable + pkg.001/002/003
  02-INSTRUCOES\
  GROK-HANDOFF-TURBORAMA-COMPLETO\
  LEIA-ME-PRIMEIRO.txt
  VERIFICAR-KIT.bat
  1-ABRIR-INSTALADOR.bat
```

## Comando para regenerar o Factory Pack (PC de build com SDK)

```powershell
cd "C:\Users\Admin\Turborama-src\ProjetoNovo"
# ou: ...\Projeto Novo TurboRama
dotnet publish src\TurboRama.UI\TurboRama.UI.csproj -c Release -r win-x64 --self-contained false
# + Launcher, Watchdog, Maintenance; copiar para pack; regenerar PACK-HASHES.sha256
# Ideal: scripts\Build-FactoryPack.ps1 no repo completo
```
