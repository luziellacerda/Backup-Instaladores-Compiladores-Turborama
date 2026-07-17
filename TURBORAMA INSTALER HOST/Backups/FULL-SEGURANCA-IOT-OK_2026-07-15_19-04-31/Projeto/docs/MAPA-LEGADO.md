# Mapa: FactoryShell → Projeto Novo

| Classe / área legada | Módulo novo | Nota |
|---------------------|-------------|------|
| `FactoryDeployer` | `TurboRama.Installation` | Orquestrador de steps |
| `WindowsBaselineHelper` | `TurboRama.Rollback` + baseline Windows | Reescrever (baseline rico) |
| `ShellInstaller` / `WinlogonBridge` | `TurboRama.Windows` + `Launcher` | Shell por usuário primeiro |
| `ShellLauncher` / `LoadingForm` | `TurboRama.Launcher` | Portar UX na Fase 2/3 |
| `AutoLoginHelper` | `TurboRama.Security` | Sem senha vazia |
| `KioskAccountHelper` | `TurboRama.Windows` | Senha forte |
| `KioskPolicyHelper` | `TurboRama.Security` | Por SID + capture |
| `TurboRamaWatchdog` | `TurboRama.Watchdog` | Serviço Windows |
| `MaintenanceForm` + PIN | `TurboRama.UI` + `Maintenance` | Named pipe |
| `UwfHelper` / CAD / BootUi | módulos opcionais Fase 4 | Default OFF |
| `FactoryPackBuilder` | scripts + bootstrapper Fase 5 | |
| `InstallPreflightHelper` | `TurboRama.Diagnostics` | Expandir checks |
| `Program` multi-mode | vários EXEs | Não um binário faz tudo |
