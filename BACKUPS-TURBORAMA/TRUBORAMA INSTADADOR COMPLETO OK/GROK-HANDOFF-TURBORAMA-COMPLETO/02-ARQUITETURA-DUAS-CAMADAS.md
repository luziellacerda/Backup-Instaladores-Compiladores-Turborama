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
│  CAMADA B — JOGOS (setup / ES)                              │
│  Layout PASTA: D:\Turborama\TurboRama.exe                   │
│  Layout FLAT:  D:\TurboRama.exe  (+ ES na raiz D:\)         │
│  • emuladores, themes, roms, screensaver_videos             │
│  • Atalhos: Start menu senha, F11 locadora, F10 moeda…      │
└─────────────────────────────────────────────────────────────┘
```

## Config que une as duas camadas

Arquivo: `C:\TurboRama\Config\turborama.json`  
Campo crítico (exemplo **flat** validado):

```json
"frontendExecutable": "D:\\TurboRama.exe"
```

Preferido de fábrica no JSON template ainda pode ser pasta:
`D:\\Turborama\\TurboRama.exe`  
O **seed e o Launcher** resolvem sozinhos se o EXE real for o flat.

Se **nenhum** candidato existir:
- Kiosk Windows **sobe** (camada A OK)
- Jogo **não abre** (aviso; Watchdog não deve derrubar o SO)

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
