# 02-cliente: TurboramaEmulationStation/CMakeLists.txt

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Seleção de fontes, dependências, recursos e opções de compilação. Atenção ao CMake da raiz do projeto versus es-app.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/CMakeLists.txt).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 16, depois 16

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/CMakeLists.txt#L16) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/CMakeLists.txt#L16)

```text
ANTES | DEPOIS |   CÓDIGO
   16 |     16 |   option(ENABLE_TTS "Set to ON to enable text to speech" OFF)
   17 |     17 |   option(USE_SYSTEM_PUGIXML "Set to ON to use system-wide pugixml library" OFF)
   18 |     18 |   
      |     19 | + # This branch targets ordinary customers, without the rental/payment stack.
      |     20 | + # Keep the switch explicit so the resulting binary can be audited: when OFF,
      |     21 | + # none of the PIX, credit, accounting or rental-time sources are linked.
      |     22 | + option(TURBORAMA_ENABLE_COMMERCIAL_SERVICES
      |     23 | + 	"Enable TurboRama PIX, credit, accounting and rental-time services" OFF)
      |     24 | + 
      |     25 | + if(NOT TURBORAMA_ENABLE_COMMERCIAL_SERVICES)
      |     26 | + 	message(STATUS "TurboRama profile: CLIENTE SEM SERVICOS COMERCIAIS")
      |     27 | + else()
      |     28 | + 	message(STATUS "TurboRama profile: SERVICOS COMERCIAIS ATIVOS")
      |     29 | + endif()
      |     30 | + 
      |     31 | + # General Release optimization and platform hardening. This option is
      |     32 | + # independent from PIX/signing so the customer profile keeps the same compiler
      |     33 | + # and linker protections without enabling any commercial service.
      |     34 | + option(TURBORAMA_RELEASE_HARDENING "Enable optimized and hardened TurboRama Release flags" OFF)
      |     35 | + 
   19 |     36 |   # Opt-in profile used only by the commercial release orchestrator. Keeping the
   20 |     37 |   # defaults OFF preserves developer and legacy builds that do not own a signing
   21 |     38 |   # certificate. The thumbprint is public metadata; no private key enters CMake.
```

## Trecho 2: antes 24, depois 41

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/CMakeLists.txt#L24) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/CMakeLists.txt#L41)

```text
ANTES | DEPOIS |   CÓDIGO
   24 |     41 |   set(TURBORAMA_PIX_SIGNER_THUMBPRINT "" CACHE STRING "Pinned SHA-1 thumbprint for signed TurboRama PIX components")
   25 |     42 |   set(TURBORAMA_PIX_BUNDLE_SHA256 "" CACHE STRING "Pinned SHA-256 digest of the complete commercial PIX agent bundle")
   26 |     43 |   
      |     44 | + if(NOT TURBORAMA_ENABLE_COMMERCIAL_SERVICES AND
      |     45 | + 	(TURBORAMA_COMMERCIAL_HARDENING OR TURBORAMA_REQUIRE_SIGNED_PIX OR
      |     46 | + 	 NOT "${TURBORAMA_PIX_SIGNER_THUMBPRINT}" STREQUAL "" OR
      |     47 | + 	 NOT "${TURBORAMA_PIX_BUNDLE_SHA256}" STREQUAL ""))
      |     48 | + 	message(FATAL_ERROR
      |     49 | + 		"PIX/commercial signing options cannot be used when commercial services are disabled")
      |     50 | + endif()
      |     51 | + 
   27 |     52 |   # Win32 default platform & directory detection
   28 |     53 |   if(WIN32)
   29 |     54 |   
```

## Trecho 3: antes 274, depois 299

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/CMakeLists.txt#L274) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/CMakeLists.txt#L299)

```text
ANTES | DEPOIS |   CÓDIGO
  274 |    299 |       set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} /MP") #multi-processor compilation
  275 |    300 |       set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} /MP") #multi-processor compilation
  276 |    301 |   
  277 |        | -     if(TURBORAMA_COMMERCIAL_HARDENING)
      |    302 | +     if(TURBORAMA_RELEASE_HARDENING OR TURBORAMA_COMMERCIAL_HARDENING)
  278 |    303 |           string(APPEND CMAKE_CXX_FLAGS_RELEASE " /O2 /GL /guard:cf /GS /Gy /Gw /Brepro")
  279 |    304 |           string(APPEND CMAKE_C_FLAGS_RELEASE " /O2 /GL /guard:cf /GS /Gy /Gw /Brepro")
  280 |    305 |           string(APPEND CMAKE_EXE_LINKER_FLAGS_RELEASE
```

Conferência: 3 trechos, 26 linhas adicionadas e 1 removidas.

