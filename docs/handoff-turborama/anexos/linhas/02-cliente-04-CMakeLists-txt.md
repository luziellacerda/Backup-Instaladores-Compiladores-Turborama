# 02-cliente: TurboramaEmulationStation/es-app/CMakeLists.txt

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Seleção de fontes, dependências, recursos e opções de compilação. Atenção ao CMake da raiz do projeto versus es-app.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/CMakeLists.txt).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 58, depois 58

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/CMakeLists.txt#L58) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/CMakeLists.txt#L58)

```text
ANTES | DEPOIS |   CÓDIGO
   58 |     58 |       ${CMAKE_CURRENT_SOURCE_DIR}/src/SaveStateConfigFile.h    
   59 |     59 |   	${CMAKE_CURRENT_SOURCE_DIR}/src/CustomFeatures.h
   60 |     60 |   	${CMAKE_CURRENT_SOURCE_DIR}/src/DeveloperMenuAuth.h
      |     61 | + 	${CMAKE_CURRENT_SOURCE_DIR}/src/MainMenuAuth.h
   61 |     62 |   	${CMAKE_CURRENT_SOURCE_DIR}/src/ThemeChangeAuth.h
   62 |     63 |   
   63 |     64 |       # GuiComponents    
```

## Trecho 2: antes 138, depois 139

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/CMakeLists.txt#L138) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/CMakeLists.txt#L139)

```text
ANTES | DEPOIS |   CÓDIGO
  138 |    139 |       ${CMAKE_CURRENT_SOURCE_DIR}/src/ZaparooSupport.h
  139 |    140 |       ${CMAKE_CURRENT_SOURCE_DIR}/src/LibretroRatio.h # batocera
  140 |    141 |       ${CMAKE_CURRENT_SOURCE_DIR}/src/Win32ApiSystem.h # batocera
  141 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/CreditManager.h # turborama arcade credit
  142 |        | - 	${CMAKE_CURRENT_SOURCE_DIR}/src/CreditWarningOverlay.h # aviso de credito sempre no topo
  143 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/PixBridge.h # turborama pix bridge
  144 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/PixAgentManager.h # configuracao e ciclo de vida do agente pix
  145 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/PixBinaryTrust.h # assinatura e publisher pin dos binarios pix
  146 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiCreditPlayerSelect.h # locadora player picker
  147 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiCreditOperatorPanel.h # locadora painel profissional
  148 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiPixPurchase.h # compra publica de tempo PIX
  149 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiPixOwnerSettings.h # cadastro protegido do proprietario
  150 |    142 |   )
  151 |    143 |   
  152 |    144 |   set(ES_SOURCES
```

## Trecho 3: antes 178, depois 170

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/CMakeLists.txt#L178) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/CMakeLists.txt#L170)

```text
ANTES | DEPOIS |   CÓDIGO
  178 |    170 |       ${CMAKE_CURRENT_SOURCE_DIR}/src/SaveStateConfigFile.cpp
  179 |    171 |   	${CMAKE_CURRENT_SOURCE_DIR}/src/CustomFeatures.cpp
  180 |    172 |   	${CMAKE_CURRENT_SOURCE_DIR}/src/DeveloperMenuAuth.cpp
      |    173 | + 	${CMAKE_CURRENT_SOURCE_DIR}/src/MainMenuAuth.cpp
  181 |    174 |   	${CMAKE_CURRENT_SOURCE_DIR}/src/ThemeChangeAuth.cpp
  182 |    175 |   
  183 |    176 |       # GuiComponents    
```

## Trecho 4: antes 250, depois 243

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/CMakeLists.txt#L250) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/CMakeLists.txt#L243)

```text
ANTES | DEPOIS |   CÓDIGO
  250 |    243 |       ${CMAKE_CURRENT_SOURCE_DIR}/src/ZaparooSupport.cpp
  251 |    244 |       ${CMAKE_CURRENT_SOURCE_DIR}/src/LibretroRatio.cpp # batocera
  252 |    245 |   	${CMAKE_CURRENT_SOURCE_DIR}/src/Win32ApiSystem.cpp # batocera
  253 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/CreditManager.cpp # turborama arcade credit
  254 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/PixBridge.cpp # turborama pix bridge
  255 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/PixAgentManager.cpp # configuracao e ciclo de vida do agente pix
  256 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiCreditPlayerSelect.cpp # locadora player picker
  257 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiCreditOperatorPanel.cpp # locadora painel profissional
  258 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiPixPurchase.cpp # compra publica de tempo PIX
  259 |        | -     ${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiPixOwnerSettings.cpp # cadastro protegido do proprietario
  260 |    246 |   )
  261 |    247 |   
      |    248 | + # The no-services customer profile must not merely hide menu entries: omit the
      |    249 | + # complete commercial implementation from the link so it cannot start, persist
      |    250 | + # state or be reached through a shortcut. The source remains available to the
      |    251 | + # separate commercial branches and builds that opt in explicitly.
      |    252 | + if(TURBORAMA_ENABLE_COMMERCIAL_SERVICES)
      |    253 | + 	list(APPEND ES_HEADERS
      |    254 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/CreditManager.h
      |    255 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/CreditWarningOverlay.h
      |    256 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/PixBridge.h
      |    257 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/PixAgentManager.h
      |    258 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/PixBinaryTrust.h
      |    259 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiCreditPlayerSelect.h
      |    260 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiCreditOperatorPanel.h
      |    261 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiPixPurchase.h
      |    262 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiPixOwnerSettings.h)
      |    263 | + 	list(APPEND ES_SOURCES
      |    264 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/CreditManager.cpp
      |    265 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/PixBridge.cpp
      |    266 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/PixAgentManager.cpp
      |    267 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiCreditPlayerSelect.cpp
      |    268 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiCreditOperatorPanel.cpp
      |    269 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiPixPurchase.cpp
      |    270 | + 		${CMAKE_CURRENT_SOURCE_DIR}/src/guis/GuiPixOwnerSettings.cpp)
      |    271 | + endif()
      |    272 | + 
  262 |    273 |   #-------------------------------------------------------------------------------
  263 |    274 |   # define OS specific sources and headers
  264 |    275 |   if(MSVC)
```

## Trecho 5: antes 335, depois 346

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/CMakeLists.txt#L335) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/CMakeLists.txt#L346)

```text
ANTES | DEPOIS |   CÓDIGO
  335 |    346 |   # define target
  336 |    347 |   include_directories(${COMMON_INCLUDE_DIRS} ${CMAKE_CURRENT_SOURCE_DIR}/src)
  337 |    348 |   add_executable(emulationstation ${ES_SOURCES} ${ES_HEADERS})
      |    349 | + if(NOT TURBORAMA_ENABLE_COMMERCIAL_SERVICES)
      |    350 | + 	target_compile_definitions(emulationstation PRIVATE TURBORAMA_NO_COMMERCIAL_SERVICES=1)
      |    351 | + endif()
  338 |    352 |   target_link_libraries(emulationstation ${COMMON_LIBRARIES} es-core)
  339 |    353 |   if(MSVC)
  340 |    354 |   	add_dependencies(emulationstation turborama_embedded_theme)
  341 |        | - 	if(TURBORAMA_COMMERCIAL_HARDENING)
      |    355 | + 	if(TURBORAMA_ENABLE_COMMERCIAL_SERVICES AND TURBORAMA_COMMERCIAL_HARDENING)
  342 |    356 |   		# Keep SDL's stricter diagnostics on the new PIX trust boundary without
  343 |    357 |   		# turning unrelated legacy renderer deprecations into release errors.
  344 |    358 |   		set_source_files_properties(
```

Conferência: 5 trechos, 31 linhas adicionadas e 17 removidas.

