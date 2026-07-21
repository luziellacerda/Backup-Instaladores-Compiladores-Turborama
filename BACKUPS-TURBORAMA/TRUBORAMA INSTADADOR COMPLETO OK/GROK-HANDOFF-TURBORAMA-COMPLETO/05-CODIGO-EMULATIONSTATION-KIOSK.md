# 05 — EmulationStation / kiosk UI (C++)

Repo: `D:\TurboramaWork\TurboramaEmulationStation`  
Branch: **`FICHEIRO-OK`**  
Remote: `https://github.com/luziellacerda/TurboramaEmulationStation.git`  
Deploy runtime: `D:\Turborama\emulationstation\` (+ `TurboRama.exe` bootstrap)

Snapshots: `codigo-snapshot\ES__*`

---

## 5.1 Objetivo desta camada

Experiência **locadora/kiosk** dentro do frontend de jogos:
- Menu Start com **senha**
- Controles de crédito (F10/F11/F7/F12…)
- Screensaver com **molduras (bezels) por pasta de vídeo**
- Painéis locadora / segurança de UI (não confundir com SecurityAgent do Launcher C#)

---

## 5.2 Commits relevantes (históricos recentes)

| Commit (short) | Tema |
|----------------|------|
| `3cd0fed68` | Start = menu senha de volta; F7 = parar contador (não menu) |
| `0df1cb58b` | (intermediário) menu no F7 — **revertido** pelo de cima |
| `7bbf074c4` | Bezels distintos switch/xboxone/pc/ps4/ps5 |
| `b060b3321` / `dc91f3c3e` | Bezel = **nome da pasta** do vídeo → `systems/{pasta}.png` |
| `110f57556` | Remove aliases errados e pack amador de art |
| `0fb128372` | F11 Turbo Sistema; (histórico Ctrl+End UI) |
| `d63bc5f5a` | Locadora Turborama completa |

---

## 5.3 Screensaver / bezels — `SystemScreenSaver.cpp`

### Regra de produto (usuário exigiu)
- Bezel **não** é arte do jogo filho.  
- Bezel = **pasta pai** onde o vídeo está.  
  Ex.: vídeo em `...\screensaver_videos\ps5\foo.mp4` → moldura `systems/ps5.png`  
- Pastas distintas devem ter PNG distintos (ps4 ≠ ps5 ≠ switch ≠ xboxone ≠ pc).

### Dual root de vídeos
- Root interno + externo (config) — vídeos **não** vão “dentro do EXE”; ficam em pasta `screensaver_videos` (cópia CMake para lado do EXE em builds).

### O que **não** fazer de novo
- Não gerar molduras amadoras genéricas iguais para ps3/ps4/ps5.  
- Não usar `setGame` para “adivinhar” moldura por rom name se a regra é pasta.

---

## 5.4 Teclas / ViewController / main

| Tecla | Comportamento atual desejado |
|-------|------------------------------|
| **Start** | Menu com **senha** (requestMainMenuAccess) |
| **F7** | **Parar** contador (não abrir menu) |
| **F11** | Locadora / Turbo Sistema |
| **F10** | Moeda |
| **F8** | Pausa |
| **F12** | Zerar |
| Alt+End | Desativado (histórico) |

Arquivos típicos:
- `es-app\src\views\ViewController.cpp` — Start → menu senha  
- `es-app\src\main.cpp` — F7 stop, F11 locadora  

---

## 5.5 Relação com o Launcher C#

| Atalho | Quem trata |
|--------|------------|
| Ctrl+End (menu PIN segurança Windows) | **Launcher** `--security-agent` (camada A) |
| Start / F11 / F10 no ES | **EmulationStation** (camada B) |

Não unificar sem o usuário pedir — são camadas diferentes.

---

## 5.6 Build ES (contexto)

- CMake no repo TurboramaEmulationStation  
- Work path histórico: `D:\TurboramaWork\TurboramaEmulationStation`  
- Output usado no PC: `D:\Turborama\emulationstation\`  
- `turborama.ini` em `D:\Turborama\` tem `LanguageDetection=1` (idioma do Windows)

---

## 5.7 Ao mexer no ES

1. Trabalhar na branch **FICHEIRO-OK**  
2. Não reintroduzir menu no F7  
3. Bezel = pasta do vídeo  
4. Testar com pastas reais em `screensaver_videos\{sistema}\`  
5. Push no remote ES se o usuário pedir
