# Checklist A — Timer isolado (sem stress no kiosk)

**Máquina:** _______________  
**Data:** _______________  
**Operador:** _______________  
**Build Timer:** _______________  
**config:** `configs/config.lab.json`  

**Pré-requisitos**
- [ ] Timer compilado (`COMPILAR.bat` ou publish)
- [ ] `config.lab.json` copiado para a pasta do EXE como `config.json`
- [ ] Pode usar **F10** como ficha de teste
- [ ] Não precisa de aceitador físico

---

## A1 — Arranque

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 1 | Abrir `TurboRama.ArcadeTimer.exe` | Janela overlay, sem crash | ☐ | |
| 2 | Abrir segunda instância | Mensagem “já em execução” | ☐ | |
| 3 | Ver `logs/` criado | Ficheiro de log do dia | ☐ | |

## A2 — Ficha (F10)

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 4 | Crédito 0, UI “INSIRA UMA FICHA” | Sim | ☐ | |
| 5 | Premir F10 1× | +5 min (ou minutesPerCoin) | ☐ | |
| 6 | Premir F10 3× rápido | Debounce: não soma 3 se &lt;300ms; depois soma | ☐ | |
| 7 | Premir F10 com intervalo &gt;300ms 3× | 15 min se 5/ficha | ☐ | |
| 8 | Ver `credit.json` | remainingSeconds &gt; 0 | ☐ | |

## A3 — Tempo parado sem emulador

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 9 | Com crédito, esperar 30s sem jogo | Tempo **não** desce | ☐ | |
| 10 | UI “CRÉDITO DISPONÍVEL” | Sim | ☐ | |

## A4 — Emulador de lab (Notepad)

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 11 | Abrir **Notepad** (está na lista lab) | Estado JOGANDO; tempo a descer | ☐ | |
| 12 | Fechar Notepad | Tempo **pausa** | ☐ | |
| 13 | Reabrir Notepad | Tempo continua a descer | ☐ | |

## A5 — Fim de tempo / bloqueio

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 14 | Deixar zerar com Notepad aberto | Notepad fecha; Timer “TEMPO ENCERRADO” / sem crédito | ☐ | |
| 15 | Com 0 crédito, abrir Notepad | Notepad fecha rápido; ES/desktop intacto | ☐ | |

## A6 — Persistência

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 16 | Com crédito, fechar Timer (Alt+F4 se possível / Task Manager) | `credit.json` mantém valor | ☐ | |
| 17 | Reabrir Timer | Crédito restaurado (se restore=true) | ☐ | |
| 18 | Renomear `credit.json` para `.bad`, reabrir | Usa backup ou zero sem crash | ☐ | |

## A7 — Protegidos

| # | Passo | Esperado | Pass? | Notas |
|---|--------|----------|-------|-------|
| 19 | Não matar Explorer / ES se abertos | Continuam vivos | ☐ | |

---

**Resultado A:** PASS ___ / FAIL ___ / N/A ___  
**Assinatura:** _______________  
**Log anexo:** `results/____________________.md`
