# Base de testes — TurboRama Arcade Timer × Kiosk

Base **sólida e repetível** para validar o Timer sem destruir o kiosk TurboRama.

## Pastas

| Pasta | Conteúdo |
|-------|----------|
| `configs/` | `config.json` de laboratório (F10, lista emuladores) |
| `checklists/` | Protocolos manuais (impressão / tablet) |
| `lab/` | Scripts de smoke **seguros** (não desligam o PC) |
| `results/` | Relatórios preenchidos por data |

## Regras de ouro

1. **Não alterar** teclas/scripts/ES do kiosk nos testes.
2. Preferir **F10** em lab (sem aceitador físico).
3. Testes de **Desligar/Reiniciar** só com checklist explícito e operador presente.
4. Guardar resultado em `results/YYYY-MM-dd-NOME.md`.
5. PIN / senha kiosk TurboRama: a do sistema (ex. `Lz2026@$`) — **não** misturar com tecla de ficha.

## Ordem recomendada (primeira vez na máquina)

```text
1. SMOKE-KIOSK.bat          → kiosk vivo? agent? filter?
2. SMOKE-TIMER-BUILD.bat    → compila Timer
3. Checklist A (Timer isolado)
4. Checklist B (cruzado kiosk)
5. Checklist C (só se for dia de power-test)
```

## Critério de “base OK”

- [ ] Smoke kiosk PASS  
- [ ] Timer compila e abre  
- [ ] Checklist A ≥ 80% PASS  
- [ ] Checklist B sem regressão de Ctrl+End / CAD / ES  

---

Ver também: `../PROPOSTA-DO-PROGRAMA.md` e spec completa no `PROMPT-PARA-OUTRA-IA.txt`.
