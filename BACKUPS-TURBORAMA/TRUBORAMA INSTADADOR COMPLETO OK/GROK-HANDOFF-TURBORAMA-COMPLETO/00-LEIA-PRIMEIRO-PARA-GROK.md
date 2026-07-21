# INSTRUÇÃO PARA O GROK (outra máquina / outro chat)

**Leia esta pasta INTEIRA antes de alterar código ou inventar arquitetura.**

Data do handoff: **2026-07-20 / 2026-07-21**  
**Última atualização pack/código: 2026-07-21** (fix senha + frontend flat + UI)  
Produto: **TurboRama** (kiosk Windows + frontend EmulationStation/retrogaming)  
Idioma do produto/ops: **português (Brasil)**; UI do ES tem multi-idioma  
Versão shell kiosk: **2.0.0-alpha** (bins recompilados em 2026-07-21 neste PC)

---

## O que esta pasta é

Tutorial **extremamente detalhado** do que foi feito, como foi feito, paths, código, bugs, kit de HD/pen, git e decisões do usuário.

**Não invente pastas.** Use os paths reais listados.

---

## Ordem de leitura obrigatória

| # | Arquivo | Conteúdo |
|---|---------|----------|
| 1 | `00-LEIA-PRIMEIRO-PARA-GROK.md` | Este arquivo |
| 2 | **`11-FIX-SENHA-E-FRONTEND-2026-07-21.md`** | **Fixes críticos do pack (senha/path/UI)** |
| 3 | `01-MAPA-COMPLETO-DO-PROJETO.md` | Mapa de discos, pastas, repos |
| 4 | `02-ARQUITETURA-DUAS-CAMADAS.md` | Windows kiosk vs jogos |
| 5 | `03-TUTORIAL-INSTALACAO-VIRGULA-A-VIRGULA.md` | Instalação passo a passo |
| 6 | `04-CODIGO-FACTORY-DETALHADO.md` | Código do instalador kiosk |
| 7 | `05-CODIGO-EMULATIONSTATION-KIOSK.md` | Código ES (bezels, teclas) |
| 8 | `06-HISTORICO-SESSAO-O-QUE-FOI-FEITO.md` | Cronologia da sessão |
| 9 | `07-BUGS-CORRIGIDOS-E-FMEA.md` | Bugs e anti-falha (B01–B11) |
| 10 | `08-PATHS-GIT-LOGS-SEGREDOS.md` | Git, logs, senhas |
| 11 | `09-PROMPTS-PARA-CONTINUAR.md` | Como continuar o trabalho |
| 12 | `10-GLOSSARIO.md` | Termos |
| 13 | `codigo-snapshot/` | Cópias dos .cs/.cpp (inclui fix) |
| 14 | `configs-exemplo/` | JSON live + pack + turborama.ini |
| 15 | `docs-projeto-existentes/` | Docs do pack (FMEA, recovery…) |

---

## Regras que o usuário impôs (não violar)

1. **“Instalar o sistema” = Factory Pack** que transforma Windows em kiosk — **não** só o setup de jogos.
2. **Windows kiosk** deve ficar **igual ao PC de referência** (Arcade, autologon, serviços, Keyboard Filter, SecurityAgent).
3. **TurboRama/jogos** = copiar/instalar **depois** (ou já no disco). Aceita:
   - pasta: `D:\Turborama\TurboRama.exe`
   - **flat:** `D:\TurboRama.exe` (+ ES na raiz de D:\)
4. Produto para **venda** → anti-falha máximo; não “passar quieto” com pack incompleto / sem .NET.
5. Kit de instalador no **pen/HD** (neste kit: **`F:\TURBORAMA-KIOSK`**).
6. Subir mudanças relevantes no **Git** quando no PC de build.
7. **Não começar pela UI manual** (`INSTALAR.bat` / botões). Produção = **`INSTALAR-COMPLETO.bat`** / `TurboRama.Setup.exe`.

---

## Estado validado neste PC (2026-07-21)

| Item | Status |
|------|--------|
| Pack `F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK` | **Corrigido e recompilado** |
| install-full + Arcade + serviços + segurança | **OK** |
| Frontend flat `D:\TurboRama.exe` | **OK** (abriu corretamente) |
| Senha fábrica `Lz2026@$` (8 chars) | **Aceita** (min 8 no código) |
| Próximo em PC novo | .NET 8 → INSTALAR-COMPLETO → reboot → jogos |

---

## Frase de arranque para o Grok

> Você está no projeto TurboRama. Leia `F:\TURBORAMA-KIOSK\GROK-HANDOFF-TURBORAMA-COMPLETO` (ou cópia desta pasta), começando por `00-LEIA-PRIMEIRO-PARA-GROK.md` e **`11-FIX-SENHA-E-FRONTEND-2026-07-21.md`**. Duas camadas: (A) Factory Pack → `C:\TurboRama` kiosk Windows; (B) jogos → `D:\TurboRama.exe` **ou** `D:\Turborama\TurboRama.exe`. Pack de fábrica no pen: `F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK` (bins de 2026-07-21). Fonte editada: `C:\Users\Admin\Turborama-src\ProjetoNovo`. Produção = INSTALAR-COMPLETO.bat, não UI manual. Commit histórico factory: 8118eab; fix local 2026-07-21 ainda pode precisar push no Git.

---

## Prompt mínimo se só puder colar um arquivo

Cole o conteúdo de:
- `11-FIX-SENHA-E-FRONTEND-2026-07-21.md` +  
- `02-ARQUITETURA-DUAS-CAMADAS.md` +  
- `08-PATHS-GIT-LOGS-SEGREDOS.md`
