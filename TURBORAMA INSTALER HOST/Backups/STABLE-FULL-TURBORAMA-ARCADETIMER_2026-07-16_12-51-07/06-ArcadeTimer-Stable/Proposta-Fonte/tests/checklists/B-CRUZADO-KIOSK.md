# Checklist B — Cruzado Timer × Kiosk TurboRama

**Objectivo:** provar que o Timer **não regrede** o kiosk e que convivem.

**Máquina:** _______________  
**Data:** _______________  
**Operador:** _______________  

**Pré-requisitos**
- [ ] Checklist A concluída (ou smoke Timer OK)
- [ ] Kiosk TurboRama instalado e a arrancar
- [ ] Security Agent vivo (`security-agent-alive.txt`)
- [ ] Keyboard Filter RUNNING (IoT)
- [ ] Timer a correr **em paralelo** (conta Arcade ou Admin de lab)

---

## B0 — Baseline kiosk (antes do Timer)

| # | Verificação | Esperado | Pass? |
|---|-------------|----------|-------|
| 0.1 | `sc query MsKeyboardFilter` | RUNNING | ☐ |
| 0.2 | Ctrl+Alt+Del | Bloqueado / CAD inútil | ☐ |
| 0.3 | Ctrl+End | Menu segurança TurboRama | ☐ |
| 0.4 | PIN kiosk | `Lz2026@$` (se for o default) | ☐ |
| 0.5 | Frontend / ES abre | Lista de jogos | ☐ |
| 0.6 | Desligar do menu ES (só se autorizado) | **Não fazer** neste checklist se for smoke | ☐ N/A |

---

## B1 — Convivência teclado

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 1 | Timer activo + F10 | Ficha conta no Timer | ☐ | |
| 2 | Ctrl+End | Menu segurança **ainda** abre | ☐ | |
| 3 | PIN no menu | Aceita senha kiosk | ☐ | |
| 4 | Cancelar menu | Volta ao jogo/ES | ☐ | |
| 5 | F10 **não** dispara Ctrl+End | Separados | ☐ | |
| 6 | Teclas de jogo (A/B/Start) | Não contam ficha | ☐ | |

## B2 — Overlay vs UI TurboRama

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 7 | Overlay Timer no canto | Não cobre botões críticos do menu se aberto | ☐ | |
| 8 | Abrir menu segurança | Dá para focar PIN e botões | ☐ | |
| 9 | Loading do Launcher (se reboot lab) | Timer não impede loading **ou** documentar conflito | ☐ | |

## B3 — Emulador real + crédito

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 10 | Com crédito, abrir jogo real (ex. RetroArch via ES) | Tempo a descer | ☐ | Nome processo: _______ |
| 11 | Sair do jogo com atalho **já do kiosk** | Tempo pausa; ES volta | ☐ | |
| 12 | Zerar tempo em jogo | Só emulador fecha; ES fica | ☐ | |
| 13 | Sem crédito, tentar jogo | Emulador fecha; ES fica | ☐ | |

## B4 — Processos / estabilidade

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 14 | Task Manager: Timer + Agent + ES | Todos vivos | ☐ | |
| 15 | Matar **só** o Timer (teste de falha) | Kiosk continua; **documentar** jogo grátis | ☐ | |
| 16 | Relançar Timer | Crédito conforme config | ☐ | |
| 17 | Agent keep-alive (esperar 2–3 min se matou agent) | Agent volta (kiosk) | ☐ | |

## B5 — Keyboard Filter × tecla ficha

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 18 | Confirmar tecla ficha **não** Blocked no Filter | Ficha funciona | ☐ | Tecla: F10 / _____ |
| 19 | Win key | Continua bloqueada (kiosk) | ☐ | |
| 20 | CAD | Continua bloqueado | ☐ | |

## B6 — Regressão kiosk (obrigatório)

| # | Passo | Esperado | Pass? |
|---|--------|----------|-------|
| 21 | Ctrl+End ainda funciona | Sim | ☐ |
| 22 | ES ainda navega | Sim | ☐ |
| 23 | Nenhum script ES alterado pelo Timer | Sim | ☐ |
| 24 | Nenhuma tecla de jogo mudou | Sim | ☐ |

---

**Resultado B:** PASS ___ / FAIL ___  
**Bloqueadores:**  
1. ________________________________  
2. ________________________________  

**Assinatura:** _______________
