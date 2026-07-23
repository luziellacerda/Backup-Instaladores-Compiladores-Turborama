# Análise: compilação do TurboramaEmulationStation e módulo de crédito

**Fonte analisada:** `D:\TurboramaWork\TurboramaEmulationStation`  
**Remote:** `https://github.com/luziellacerda/TurboramaEmulationStation.git`  
**Base:** EmulationStation (fork estilo Batocera) — C++17, CMake, Windows/Linux  

---

## 1. Arquitetura do código (para não errar ao modificar)

```
TurboramaEmulationStation/
├── CMakeLists.txt          ← projeto raiz (deps + flags + output)
├── CMake/Packages/         ← Find*.cmake (SDL2, FreeImage, VLC, …)
├── external/               ← pugixml, nanosvg, id3v2, libcheevos
├── es-core/                ← motor: Window, Input, Renderer, Settings, GUI base
├── es-app/                 ← aplicação: main, FileData, SystemData, views, launch
├── resources/              ← fontes, SVG, shaders (runtime)
├── locale/                 ← i18n (Linux; no WIN32 i18n desligado no CMake)
└── .github/workflows/      ← CI Windows (vcpkg + MSVC)
```

### Fluxo runtime relevante para ficha/tempo

1. `es-app/src/main.cpp` — arranque, args (`--force-kiosk`, `--windowed`, …)
2. `ViewController::launch` / `doLaunchGame`
3. **`FileData::launchGame()`** (`es-app/src/FileData.cpp`)  
   - monta comando (`getlaunchCommand`)  
   - `Scripting::fireEvent("game-start", …)`  
   - `ProcessStartInfo::run()` (bloqueia até o emulador sair)  
   - `Scripting::fireEvent("game-end")`  
   - reabre janela do ES  

**Conclusão:** o ponto de integração **correto e seguro** do crédito é em volta de `launchGame` + input global, **não** um segundo EXE.

### Onde encaixar um módulo de crédito (sem quebrar o resto)

| Ficheiro / área | Função do módulo |
|-----------------|------------------|
| Novo: `es-app/src/CreditManager.h/.cpp` | saldo, ficha, debounce, persistência, teto |
| `FileData::launchGame` | se `!hasCredit()` → `GuiMsgBox` e `return false` |
| Durante jogo | ES fica em `process.run()` bloqueado; desconto de tempo = (a) no processo filho com script ou (b) thread/timer **antes** de bloquear / via evento externo |
| `InputManager` / loop do `Window` | tecla ficha (ex. F10) → `CreditManager::addCoin()` |
| Overlay | componente em `Window` ou view (TextComponent) |

**Nota importante sobre tempo:** `process.run()` **bloqueia** a thread do UI até o jogo acabar. Contar tempo **segundo a segundo com UI no ES** enquanto o jogo corre exige:

- contagem no **retorno** com `time()` (só total da sessão — simples, zero race), **ou**
- processo/monitor externo só para fechar o emulador no zero, **ou**
- refactor não-bloqueante do launch (mais invasivo).

MVP **sem erros** e simples:  
**bloquear launch sem crédito + descontar no `game-end` pelo tempo real da sessão** (`tstart` já existe em `launchGame`).  
Fechar o emulador ao zerar a meio da sessão = fase 2 (thread/job com PID).

---

## 2. Como a compilação funciona (Windows)

### 2.1 CMake (raiz)

- `cmake_minimum_required(VERSION 3.10)`
- `project(emulationstation-all)`
- **WIN32:**
  - `CMAKE_CXX_STANDARD 17`
  - plataforma default no CMake: **Win32** se não passar `-A`
  - libs em `win32-libs/` **ou** pasta irmã `batocera-emulationstation-win32-dependencies`  
    **ou** `FetchContent` do repo batocera win32-dependencies
  - output: `bin/${CMAKE_GENERATOR_PLATFORM}/` (ex. `bin/x64/emulationstation.exe`)
- Subprojetos: `external` → `es-core` → `es-app`
- MSVC: `/MP`, `NOMINMAX`, `_CRT_SECURE_NO_DEPRECATE`

### 2.2 Dependências obrigatórias (find_package)

| Pacote | Uso |
|--------|-----|
| OpenGL (Desktop) | render |
| Freetype | fontes |
| FreeImage | imagens |
| SDL2 | janela/input |
| SDL2_mixer | áudio |
| CURL | rede/scrapers |
| VLC | vídeo |
| RapidJSON | JSON |

Externos embutidos: pugixml, nanosvg, id3v2, libcheevos.

### 2.3 CI oficial do repo (`.github/workflows/build.yaml`)

Runner: `windows-2022`

```text
1. checkout + submodules recursive
2. MSVC x64 (ilammy/msvc-dev-cmd)
3. CMake (get-cmake)
4. vcpkg install: sdl2, sdl2-mixer, freeimage, freetype, curl, rapidjson, boost-*
5. cmake -G "Visual Studio 17 2022" -A x64 -DCMAKE_TOOLCHAIN_FILE=vcpkg.cmake
6. cmake --build build --config Release
7. artefacto: emulationstation.exe
```

**Risco de erro no CI atual:** o workflow **não instala VLC no vcpkg**, mas o `CMakeLists.txt` faz `find_package(VLC REQUIRED)`.  
Numa máquina limpa, o configure pode falhar sem VLC (ou sem `win32-libs` com libvlc).  
Para build **sem erros**, é obrigatório:

- ter **libvlc** no `win32-libs` / path CMake, **ou**
- estender o vcpkg/workflow com VLC, **ou**
- documentar path manual para `VLC_INCLUDE_DIR` / `VLC_LIBRARIES`.

### 2.4 Receita local recomendada (Windows, sem erros)

**Pré-requisitos nesta máquina (estado da análise):**

| Ferramenta | Estado no PC de testes |
|------------|-------------------------|
| Git | MISSING no PATH |
| CMake | MISSING no PATH |
| MSVC / cl / msbuild | MISSING no PATH |
| win32-libs / vcpkg | ausentes na pasta do projeto |

**Instalar antes de compilar:**

1. Visual Studio 2022 (workload **Desktop C++**)  
2. CMake ≥ 3.10  
3. Git  
4. Uma destas fontes de libs:
   - **A)** clonar `batocera-emulationstation-win32-dependencies` para  
     `D:\TurboramaWork\batocera-emulationstation-win32-dependencies`  
     (ou `TurboramaEmulationStation\win32-libs`)  
   - **B)** vcpkg como no CI **+** VLC SDK  

**Configure + build (x64 Release, alinhado ao CI):**

```bat
cd /d D:\TurboramaWork\TurboramaEmulationStation
git submodule update --init --recursive

cmake -S . -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release --parallel
```

EXE esperado:  
`D:\TurboramaWork\TurboramaEmulationStation\bin\x64\emulationstation.exe`  
(ou sob `build\` conforme gerador; o CMake força `EXECUTABLE_OUTPUT_PATH` para `bin/<platform>` no WIN32).

**Evitar erros comuns:**

| Erro | Causa | Correção |
|------|--------|----------|
| `git` / submodule falha | Git ausente; pugixml vazio | Instalar Git; `submodule update --init` |
| `Could not find SDL2` | Sem win32-libs/vcpkg | Colocar libs no path esperado |
| `Could not find VLC` | VLC não no CI/vcpkg | win32-libs libvlc ou paths manuais |
| Plataforma Win32 vs x64 | CMake default Win32; deps x64 | Sempre `-A x64` se deps forem x64 |
| Link MSVC C++17 | compilador antigo | VS 2019+ / 2022 |
| Esquecer `/MP` / NOMINMAX | já no CMake | não remover flags MSVC |
| Ficheiro novo `.cpp` não no CMakeLists | es-app lista ficheiros à mão | **registar** CreditManager em `es-app/CMakeLists.txt` |

---

## 3. Regras para o código do módulo de crédito **não conter erros**

1. **Não** alterar o fluxo de `process.run()` sem testes — risco de ES ficar preto/travado.  
2. **MVP seguro:**
   - `hasCredit()` antes de launch  
   - `addCoin()` no input  
   - no `game-end`: `consume(time(NULL) - tstart)`  
3. Persistência: ficheiro sob pasta home do ES (`Paths` / `.emulationstation`), com write atómico (temp + rename).  
4. Thread-safety: se houver thread de monitor, lock no saldo.  
5. Kiosk: UI mode kiosk já existe; não desativar.  
6. Compilar **Release** e testar:
   - sem crédito → não lança  
   - 1 ficha → lança  
   - volta do jogo → saldo desce  
7. Cada ficheiro novo: header + cpp **e** entrada no `es-app/CMakeLists.txt` (`ES_HEADERS` / `ES_SOURCES`).  
8. Não depender de `System.*` Windows desnecessário; preferir utilitários já do ES (`Utils::FileSystem`, `Log`, `Settings`).

---

## 4. Comparação final (compilação no ES vs app à parte)

| | App Timer (.NET) | Módulo no ES (este projeto) |
|--|------------------|-----------------------------|
| Arranque kiosk Arcade | Problemático | Nativo |
| Build | Fácil (dotnet) | CMake+MSVC+deps pesadas |
| Integração launch | Whitelist EXEs | `launchGame` nativo |
| Risco de bug no menu | Isolado | Alto se mal feito |
| Produto fliperama | Workaround | **Caminho correto** |

**Recomendação de engenharia:** implementar crédito **neste** repositório, com build Windows documentado e pipeline sem `find_package` em falta (VLC).  
App .NET à parte: apenas protótipo de contador; **não** como processo de arranque do kiosk.

---

## 5. Checklist “build limpo” antes de mexer no crédito

- [ ] Git + submodules OK  
- [ ] VS 2022 C++ + CMake no PATH  
- [ ] win32-libs **ou** vcpkg completo (incl. **VLC**)  
- [ ] `cmake -A x64` configure **sem** erro  
- [ ] `cmake --build Release` gera `emulationstation.exe`  
- [ ] EXE arranca com `--windowed --debug`  
- [ ] Só depois: branch `feature/credit-timer` + módulo  

---

## 6. Estado desta máquina (análise)

Não está pronta para compilar agora: faltam **Git, CMake, MSVC** no PATH e pasta de **dependências Windows**.  
O código-fonte está completo e analisável em `D:\TurboramaWork\TurboramaEmulationStation`.
