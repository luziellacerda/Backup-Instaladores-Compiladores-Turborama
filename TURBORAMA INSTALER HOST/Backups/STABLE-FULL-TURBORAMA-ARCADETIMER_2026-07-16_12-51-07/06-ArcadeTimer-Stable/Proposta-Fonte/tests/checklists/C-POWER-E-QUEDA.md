# Checklist C — Power e queda (só com operador)

⚠️ Pode desligar/reiniciar a máquina. **Não** correr em produção sem aviso.

**Data:** _______________ **Operador:** _______________

## C1 — restoreCreditAfterRestart = true

| # | Passo | Esperado | Pass? |
|---|--------|----------|-------|
| 1 | Crédito ~5–10 min, Timer a correr | Guardado | ☐ |
| 2 | Reiniciar Windows (não Desligar ES) | — | ☐ |
| 3 | Após logon, Timer a correr | Crédito **restaurado** (±1–2 s) | ☐ |

## C2 — restoreCreditAfterRestart = false (lab)

| # | Passo | Esperado | Pass? |
|---|--------|----------|-------|
| 4 | Config false, crédito &gt;0, reboot | Crédito **zero** | ☐ |
| 5 | Repor config true depois do teste | — | ☐ |

## C3 — Desligar via menu ES (fluxo TurboRama)

| # | Passo | Esperado | Pass? |
|---|--------|----------|-------|
| 6 | Com Timer + jogo a correr | — | ☐ |
| 7 | Desligar no ES | Splash TurboRama; PC desliga; **sem** crash loop | ☐ |
| 8 | ES/Timer não deixam ecrã preto infinito | Sim | ☐ |

## C4 — Queda de energia simulada

| # | Passo | Esperado | Pass? |
|---|--------|----------|-------|
| 9 | Cortar energia com crédito (se seguro) | Após boot, comportamento = C1 ou C2 | ☐ |

---

**Resultado C:** PASS ___ / FAIL ___  
**Notas:** ________________________________
