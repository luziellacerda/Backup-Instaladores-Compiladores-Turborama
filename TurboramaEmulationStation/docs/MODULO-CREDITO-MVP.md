# Módulo de crédito MVP (dentro do ES)

## Comportamento

1. **F10** (no menu ES) → +5 minutos (configurável), com debounce 350 ms  
2. **Sem crédito** e `blockWithoutCredit=1` → ao escolher jogo mostra *INSERT COIN / SEM CREDITO* e **não lança**  
3. **Com crédito** → lança normalmente  
4. **Ao voltar do jogo** → desconta segundos reais da sessão (`time()` início/fim)  
5. Persistência em `arcade_credit.dat`  

`enabled=0` → free play (não bloqueia; F10 não é obrigatório).

## Limitações conscientes do MVP (evitar bugs)

- Durante o jogo o ES fica bloqueado em `process.run()` — **não** há contagem ao vivo no overlay nesta fase.  
- Não mata o emulador no “zero a meio da sessão” ainda (fase 2).  
- Overlay visual permanente no canto: fase 2 (TextComponent no Window).

## Testes manuais após build

| # | Passo | Esperado |
|---|--------|----------|
| 1 | `enabled=1`, apagar `arcade_credit.dat`, abrir ES | saldo 0 |
| 2 | Tentar jogo | mensagem INSERT COIN |
| 3 | F10 | log + ficheiro com remaining=300 |
| 4 | Abrir jogo 20 s e sair | remaining ≈ 280 |
| 5 | F10 10× rápido | ~1 ficha (debounce) |
| 6 | `enabled=0` | free play |

## Fase 2 (futuro)

- HUD com `formatRemaining()`  
- Thread/job para fechar emulador no zero  
- Tecla ficha configurável (não só F10)  
- ACL na pasta de crédito no kiosk  
