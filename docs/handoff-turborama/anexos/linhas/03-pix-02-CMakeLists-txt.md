# 03-pix: TurboramaEmulationStation/CMakeLists.txt

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Seleção de fontes, dependências, recursos e opções de compilação. Atenção ao CMake da raiz do projeto versus es-app.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `476e06179f89ac209ff808dffb27555d740f93d2`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/CMakeLists.txt).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 19, depois 19

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/CMakeLists.txt#L19) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/CMakeLists.txt#L19)

```text
ANTES | DEPOIS |   CÓDIGO
   19 |     19 |   # Opt-in profile used only by the commercial release orchestrator. Keeping the
   20 |     20 |   # defaults OFF preserves developer and legacy builds that do not own a signing
   21 |     21 |   # certificate. The thumbprint is public metadata; no private key enters CMake.
      |     22 | + option(TURBORAMA_RELEASE_HARDENING "Enable optimized and hardened TurboRama Release flags" OFF)
   22 |     23 |   option(TURBORAMA_COMMERCIAL_HARDENING "Enable hardened TurboRama commercial Release flags" OFF)
   23 |     24 |   option(TURBORAMA_REQUIRE_SIGNED_PIX "Require signed TurboRama PIX components at runtime" OFF)
   24 |     25 |   set(TURBORAMA_PIX_SIGNER_THUMBPRINT "" CACHE STRING "Pinned SHA-1 thumbprint for signed TurboRama PIX components")
```

## Trecho 2: antes 274, depois 275

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/CMakeLists.txt#L274) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/CMakeLists.txt#L275)

```text
ANTES | DEPOIS |   CÓDIGO
  274 |    275 |       set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} /MP") #multi-processor compilation
  275 |    276 |       set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} /MP") #multi-processor compilation
  276 |    277 |   
  277 |        | -     if(TURBORAMA_COMMERCIAL_HARDENING)
      |    278 | +     if(TURBORAMA_RELEASE_HARDENING OR TURBORAMA_COMMERCIAL_HARDENING)
  278 |    279 |           string(APPEND CMAKE_CXX_FLAGS_RELEASE " /O2 /GL /guard:cf /GS /Gy /Gw /Brepro")
  279 |    280 |           string(APPEND CMAKE_C_FLAGS_RELEASE " /O2 /GL /guard:cf /GS /Gy /Gw /Brepro")
  280 |    281 |           string(APPEND CMAKE_EXE_LINKER_FLAGS_RELEASE
```

Conferência: 2 trechos, 2 linhas adicionadas e 1 removidas.

