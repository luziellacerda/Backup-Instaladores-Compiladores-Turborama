# Checklist — ambiente de build Windows (sem erros)

## 1. Software obrigatório

| # | Ferramenta | Notas |
|---|------------|--------|
| 1 | **Visual Studio 2022** | Workload: *Desenvolvimento para desktop com C++* |
| 2 | **CMake** ≥ 3.10 | https://cmake.org/download/ — marcar “Add to PATH” |
| 3 | **Git** | https://git-scm.com/download/win — com PATH |
| 4 | Rede | Para baixar `win32-libs` / submodules na 1ª vez |

Verificar no PowerShell:

```powershell
cmake --version
git --version
# Abrir "x64 Native Tools Command Prompt for VS 2022" e:
cl
```

## 2. Código-fonte

```bat
cd /d D:\TurboramaWork\TurboramaEmulationStation
git submodule update --init --recursive
```

Confirmar que existe: `external\pugixml\src\pugixml.cpp`

## 3. Dependências nativas (uma opção)

### Opção A — win32-libs (recomendada, alinhada ao CMake)

```bat
cd /d D:\TurboramaWork\TurboramaEmulationStation
git clone --depth 1 https://github.com/batocera-linux/batocera-emulationstation-win32-dependencies.git win32-libs
```

Ou pasta irmã:

```bat
cd /d D:\TurboramaWork
git clone --depth 1 https://github.com/batocera-linux/batocera-emulationstation-win32-dependencies.git batocera-emulationstation-win32-dependencies
```

Deve incluir SDL2, FreeImage, FreeType, curl, RapidJSON, **libvlc**.

### Opção B — vcpkg (como CI)

Ver `.github/workflows/build.yaml`.  
**Atenção:** instalar também **VLC/libvlc** (o CI base não lista VLC e o CMake exige).

## 4. Compilar

Duplo clique ou:

```bat
COMPILAR-WINDOWS.bat
```

Ou manual:

```bat
cmake -S . -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release --parallel
```

## 5. Resultado esperado

- `bin\x64\emulationstation.exe`
- Arranque de teste:

```bat
bin\x64\emulationstation.exe --windowed --debug --resolution 1280 720
```

## 6. Módulo crédito (já no source)

| Ficheiro | Função |
|----------|--------|
| `es-app/src/CreditManager.h/.cpp` | Saldo, ficha, debounce, persistência |
| `FileData::launchGame` | Bloqueia sem crédito |
| `main.cpp` | F10 = ficha |
| `es-app/CMakeLists.txt` | Registo no build |

Config automática (1ª execução):

`%USERPROFILE%\.emulationstation\arcade_credit.cfg`

```ini
enabled=1
blockWithoutCredit=1
minutesPerCoin=5
debounceMs=350
maxRemainingSeconds=28800
```

Crédito:

`%USERPROFILE%\.emulationstation\arcade_credit.dat`

Desligar crédito (free play):

```ini
enabled=0
```

## 7. Erros comuns e correção

| Erro | Correção |
|------|----------|
| `Could not find SDL2` | win32-libs ou vcpkg |
| `Could not find VLC` | win32-libs com libvlc |
| `pugixml` missing | `git submodule update --init` |
| Win32 vs x64 | sempre `-A x64` com deps x64 |
| Ficheiro .cpp novo não compila | está no CMakeLists? |

## 8. Ordem segura de trabalho

1. Build **limpo** sem alterações (ou com CreditManager já incluído)  
2. Testar menu + 1 jogo  
3. Testar F10 + bloqueio sem crédito  
4. Só depois: overlay HUD, fechar emulador no zero, etc.
