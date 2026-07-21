# 10 — Glossário TurboRama

| Termo | Significado |
|-------|-------------|
| Factory Pack | Pasta TurboRama-Factory-Pack / **00-SISTEMA-WINDOWS-KIOSK** — instala kiosk Windows |
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
| **Layout flat** | `D:\TurboRama.exe` + ES/emulators na raiz de D:\ |
| **Layout pasta** | `D:\Turborama\TurboRama.exe` + árvore sob pasta |
| FactoryDefaults | Senha fábrica, min 8, candidates frontend |
| FICHEIRO-OK | Branch ES com locadora/kiosk UI estável |
| Bezel | Moldura do screensaver por pasta de vídeo |
| Locadora | Modo crédito/tempo no ES (F11) |
| ULTRA-HARD | Suite de testes de fábrica no PC referência |
| KioskBasic | Profile de instalação padrão |
| DPAPI | Proteção do segredo da senha kiosk em disco |
| .pkg | Partes Zip64 do instalador de jogos |
| ACCT_PWD | Erro de senha kiosk curta demais (corrigido B09) |
| INSTALAR-COMPLETO | BAT de produção (Setup --install-full) |
| pen F: | Kit atual `F:\TURBORAMA-KIOSK` |
