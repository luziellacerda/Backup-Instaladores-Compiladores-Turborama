# 09 — Prompts prontos para colar em outro Grok / Cursor

## Prompt 1 — Continuidade geral

```
Leia a pasta F:\TURBORAMA-KIOSK\GROK-HANDOFF-TURBORAMA-COMPLETO inteira,
começando por 00-LEIA-PRIMEIRO-PARA-GROK.md e 11-FIX-SENHA-E-FRONTEND-2026-07-21.md.
Projeto TurboRama: duas camadas —
(A) Factory Pack C:\TurboRama kiosk Windows;
(B) jogos D:\TurboRama.exe (flat) OU D:\Turborama\TurboRama.exe (pasta).
Pack pen corrigido: F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK
Fonte: C:\Users\Admin\Turborama-src\ProjetoNovo
Produção = INSTALAR-COMPLETO.bat (não UI manual).
Aguarde minha tarefa.
```

## Prompt 2 — Só instalação / kit

```
Use F:\TURBORAMA-KIOSK e 03-TUTORIAL-INSTALACAO + 11-FIX.
Ordem: .NET 8 → INSTALAR-COMPLETO (00-SISTEMA-WINDOWS-KIOSK) → reboot
→ jogos (01-INSTALADOR ou cópia flat/pasta D:) → ROMs opcional.
Senha kiosk: Lz2026@$ (8 chars OK).
```

## Prompt 3 — Só código kiosk C#

```
Trabalhe em C:\Users\Admin\Turborama-src\ProjetoNovo
(ou repo Projeto Novo TurboRama).
Leia 04-CODIGO-FACTORY-DETALHADO.md, 11-FIX e codigo-snapshot\.
Não altere ES a menos que eu peça. Rebuild e atualize pack + PACK-HASHES.
```

## Prompt 4 — Só EmulationStation

```
Repo D:\TurboramaWork\TurboramaEmulationStation branch FICHEIRO-OK.
Leia 05-CODIGO-EMULATIONSTATION-KIOSK.md.
Regras: Start=menu senha; F7=parar; bezel=pasta do vídeo systems/{pasta}.png.
```

## Prompt 5 — Teste VM limpa

```
Monte roteiro de teste VM Windows limpo do pack em
F:\TURBORAMA-KIOSK\00-SISTEMA-WINDOWS-KIOSK usando 07-BUGS e 11-FIX.
Critério: PREFLIGHT 0 erros, install-full 0, reboot Arcade,
frontend flat ou pasta, VALIDAR-ACEITE OK.
```

## Prompt 6 — Venda internacional

```
Leia auditoria multi-país da sessão (scores BR 58 / LatAm 32 / EUA-UE 18).
Não embutir ROMs comerciais. SKU: TR-SHELL + TR-UI empty.
P0: senha por máquina, paths, signing.
```
