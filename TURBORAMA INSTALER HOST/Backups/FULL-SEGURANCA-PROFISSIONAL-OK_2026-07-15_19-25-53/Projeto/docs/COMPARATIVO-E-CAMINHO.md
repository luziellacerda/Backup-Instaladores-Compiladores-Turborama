# Comparativo e caminho

## Fontes

| Fonte | Caminho |
|-------|---------|
| Estudo (constituição) | `..\AUDITORIA GPT COMO CONSTRUIR PROGRAMA.txt` |
| Legado fábrica | `..\TurboRamaFactoryShell\` |
| Pacote estável | `..\..\BACKUP-INSTALADOR-ESTAVEL\` |

## Decisão

- **Greenfield** neste repositório.
- Estudo = regras.
- FactoryShell = referência de UX/fábrica (preflight, recovery, pack), **não** base de copy-paste do monólito.

## Fases

| Fase | Conteúdo | Status |
|------|----------|--------|
| 0 | Core, config, estado, preflight, layout step, UI | **Feita** |
| 1 | Baseline completo + manifesto + rollback real (probe) | **Feita** |
| 2 | Kiosk básico (conta+senha, shell usuário, autologon, políticas, launcher) | **Feita** |
| 3 | Watchdog/Maintenance services + maintenance.lock + Status/pipe | **Feita** |
| 4 | UWF / Filter / branding opcionais (default OFF na UI) | **Feita** (código; aplicar só se necessário) |
| 5 | Pack fábrica (`GERAR-PACK-FABRICA.bat` → `TurboRama-Factory-Pack`) | **Feita** |
| 6 | Aceite de fábrica / validação pós-install (`--validate`, `VALIDAR-ACEITE.bat`) | **Feita** |

## Perfil padrão

**KioskBasic** — sem Keyboard Filter, UWF ou branding de boot.
