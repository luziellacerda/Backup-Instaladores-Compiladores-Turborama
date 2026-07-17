# Matriz risco → teste

| Risco (cruzado kiosk) | Checklist | Smoke |
|----------------------|-----------|-------|
| Timer morto → jogo grátis | B15–B16 | — |
| Whitelist incompleta | B10, LISTAR-EMULADORES | — |
| F10 bloqueado no Filter | B18, SMOKE-KIOSK [5] | SIM |
| Hook vs Ctrl+End | B1–B4 | — |
| Overlay cobre menu | B7–B8 | — |
| Kill emulador com 0 crédito | A15, B13 | — |
| ES morto por engano | A19, B12 | — |
| Crédito após reboot | C1–C2 | — |
| Desligar ES + Timer | C3 | — |
| Kiosk regredido | B6, SMOKE-KIOSK | SIM |

## Entrada de ficha de lab

| Método | Como |
|--------|------|
| Teclado | **F10** (`config.lab.json`) |
| Hardware | Mapear encoder para a mesma tecla do `coinKey` |

## Não fazer em smoke diário

- Desligar PC real (só checklist C)
- Alterar registo do Keyboard Filter sem backup
- Meter `emulationstation` em `emulatorProcesses`
