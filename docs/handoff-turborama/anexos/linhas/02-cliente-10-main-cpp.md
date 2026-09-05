# 02-cliente: TurboramaEmulationStation/es-app/src/main.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Ponto de entrada, criação da janela, inicialização do tema, loop e encerramento. Serviços comerciais ficam condicionados na versão cliente.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 47, depois 47

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L47) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L47)

```text
ANTES | DEPOIS |   CÓDIGO
   47 |     47 |   #include <vector>
   48 |     48 |   #include "ZaparooSupport.h"
   49 |     49 |   #include "utils/ThreadPool.h"
   50 |        | - #include "CreditManager.h"
   51 |        | - #include "CreditWarningOverlay.h"
   52 |     50 |   #include "resources/ProtectedDecorations.h"
   53 |     51 |   #include "resources/ResourceManager.h"
      |     52 | + #ifdef TURBORAMA_NO_COMMERCIAL_SERVICES
      |     53 | + #include "MainMenuAuth.h"
      |     54 | + #endif
      |     55 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
      |     56 | + #include "CreditManager.h"
      |     57 | + #include "CreditWarningOverlay.h"
   54 |     58 |   #include "PixBridge.h"
   55 |     59 |   #include "PixAgentManager.h"
   56 |     60 |   #include "PixBinaryTrust.h"
      |     61 | + #endif
   57 |     62 |   #include "guis/GuiMenu.h"
   58 |     63 |   
   59 |     64 |   #ifdef WIN32
```

## Trecho 2: antes 485, depois 490

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L485) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L490)

```text
ANTES | DEPOIS |   CÓDIGO
  485 |    490 |   	// Inicialize esse caminho antes de qualquer retorno antecipado; antes ele
  486 |    491 |   	// dependia por engano do diretorio atual usado para iniciar o teste.
  487 |    492 |   	Paths::setExePath(argv[0]);
      |    493 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  488 |    494 |   #ifdef WIN32
  489 |    495 |   	if (PixBinaryTrust::required())
  490 |    496 |   	{
```

## Trecho 3: antes 500, depois 506

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L500) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L506)

```text
ANTES | DEPOIS |   CÓDIGO
  500 |    506 |   			return 31;
  501 |    507 |   		}
  502 |    508 |   	}
      |    509 | + #endif
  503 |    510 |   #endif
  504 |    511 |   	if (argc == 2 && strcmp(argv[1], "--protected-decorations-self-test") == 0)
  505 |    512 |   	{
```

## Trecho 4: antes 515, depois 522

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L515) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L522)

```text
ANTES | DEPOIS |   CÓDIGO
  515 |    522 |   		}
  516 |    523 |   		return 0;
  517 |    524 |   	}
      |    525 | + 	if (argc == 2 && strcmp(argv[1], "--no-commercial-services-self-test") == 0)
      |    526 | + 	{
      |    527 | + #ifdef TURBORAMA_NO_COMMERCIAL_SERVICES
      |    528 | + 		fprintf(stdout, "TURBORAMA_BUILD_PROFILE=CLIENTE_SEM_SERVICOS\n");
      |    529 | + 		return 0;
      |    530 | + #else
      |    531 | + 		fprintf(stderr, "TURBORAMA_BUILD_PROFILE=SERVICOS_COMERCIAIS_ATIVOS\n");
      |    532 | + 		return 33;
      |    533 | + #endif
      |    534 | + 	}
      |    535 | + #ifdef TURBORAMA_NO_COMMERCIAL_SERVICES
      |    536 | + 	if (argc == 2 && strcmp(argv[1], "--main-menu-auth-self-test") == 0)
      |    537 | + 	{
      |    538 | + 		const bool passed = MainMenuAuth::runSelfTest();
      |    539 | + 		fprintf(passed ? stdout : stderr, "MAIN_MENU_AUTH_TEST=%s\n", passed ? "OK" : "FAILED");
      |    540 | + 		return passed ? 0 : 35;
      |    541 | + 	}
      |    542 | + #endif
      |    543 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  518 |    544 |   	if (argc == 2 && strcmp(argv[1], "--credit-warning-overlay-self-test") == 0)
  519 |    545 |   	{
  520 |    546 |   		CreditWarningOverlay::show(
```

## Trecho 5: antes 591, depois 617

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L591) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L617)

```text
ANTES | DEPOIS |   CÓDIGO
  591 |    617 |   			Utils::FileSystem::writeAllText(Utils::FileSystem::combine(argv[2], "pix-create-error.txt"), error);
  592 |    618 |   		return created ? 0 : 21;
  593 |    619 |   	}
      |    620 | + #else
      |    621 | + 	// Do not let a stale PIX/credit shortcut silently fall through to the GUI in
      |    622 | + 	// the customer build. These commands belong exclusively to the commercial
      |    623 | + 	// profile and are rejected before the normal argument parser starts.
      |    624 | + 	if (argc >= 2)
      |    625 | + 	{
      |    626 | + 		const char* disabledCommercialCommands[] = {
      |    627 | + 			"--credit-warning-overlay-self-test",
      |    628 | + 			"--pix-agent-manager-self-test",
      |    629 | + 			"--pix-agent-trust-self-test",
      |    630 | + 			"--pix-agent-start-once",
      |    631 | + 			"--pix-verify-event",
      |    632 | + 			"--pix-test-qr-cache",
      |    633 | + 			"--pix-process-once",
      |    634 | + 			"--pix-create-request"
      |    635 | + 		};
      |    636 | + 		for (const char* command : disabledCommercialCommands)
      |    637 | + 		{
      |    638 | + 			if (strcmp(argv[1], command) == 0)
      |    639 | + 			{
      |    640 | + 				fprintf(stderr, "TURBORAMA_COMMERCIAL_COMMAND_DISABLED=%s\n", command);
      |    641 | + 				return 34;
      |    642 | + 			}
      |    643 | + 		}
      |    644 | + 	}
      |    645 | + #endif
  594 |    646 |   
  595 |    647 |   	// Utils::MathExpr::performUnitTests();
  596 |    648 |   
```

## Trecho 6: antes 661, depois 713

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L661) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L713)

```text
ANTES | DEPOIS |   CÓDIGO
  661 |    713 |   
  662 |    714 |   	LOG(LogInfo) << "EmulationStation - v" << PROGRAM_VERSION_STRING << ", built " << PROGRAM_BUILT_STRING;
  663 |    715 |   
      |    716 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  664 |    717 |   	// O servico PIX acompanha o EmulationStation. Dados e credenciais ficam na
  665 |    718 |   	// pasta persistente .emulationstation/pix e sobrevivem a reinicializacoes.
  666 |    719 |   	// Se o proprietario ainda nao configurou o PIX, nada externo e iniciado.
```

## Trecho 7: antes 669, depois 722

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L669) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L722)

```text
ANTES | DEPOIS |   CÓDIGO
  669 |    722 |   		if (!PixAgentManager::startIfConfigured(&pixStartError) && !pixStartError.empty())
  670 |    723 |   			LOG(LogInfo) << "[PIX] " << pixStartError;
  671 |    724 |   	}
      |    725 | + #else
      |    726 | + 	LOG(LogInfo) << "TurboRama profile: cliente sem servicos comerciais";
      |    727 | + #endif
  672 |    728 |   
  673 |    729 |   	//always close the log on exit
  674 |    730 |   	atexit(&onExit);
```

## Trecho 8: antes 910, depois 966

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L910) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L966)

```text
ANTES | DEPOIS |   CÓDIGO
  910 |    966 |   					// Alt+End / Ctrl+End: nao abre menu (desativado no kiosk)
  911 |    967 |   					if (k == SDLK_END && (event.key.keysym.mod & (KMOD_ALT | KMOD_CTRL)))
  912 |    968 |   						continue;
      |    969 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  913 |    970 |   					if (k == SDLK_F11)
  914 |    971 |   					{
  915 |    972 |   						GuiMenu::requestCreditSettingsAccess_static(&window);
```

## Trecho 9: antes 958, depois 1015

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L958) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L1015)

```text
ANTES | DEPOIS |   CÓDIGO
  958 |   1015 |   						}
  959 |   1016 |   						continue;
  960 |   1017 |   					}
      |   1018 | + #else
      |   1019 | + 					if (k == SDLK_F11)
      |   1020 | + 					{
      |   1021 | + 						GuiMenu::requestTurboSystemMenuAccess_static(&window);
      |   1022 | + 						continue;
      |   1023 | + 					}
      |   1024 | + #endif
  961 |   1025 |   				}
  962 |   1026 |   
  963 |   1027 |   				if (event.type == SDL_WINDOWEVENT && event.window.event == SDL_WINDOWEVENT_RESIZED && Settings::getInstance()->getBool("Windowed"))
```

## Trecho 10: antes 1041, depois 1105

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L1041) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L1105)

```text
ANTES | DEPOIS |   CÓDIGO
 1041 |   1105 |   	if (Utils::Platform::isFastShutdown())
 1042 |   1106 |   		Settings::getInstance()->setBool("IgnoreGamelist", true);
 1043 |   1107 |   
      |   1108 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
 1044 |   1109 |   	// TurboRama: flush credit/players before exit (avoid lost last seconds)
 1045 |   1110 |   	CreditManager::getInstance().flushNow();
      |   1111 | + 	#endif
 1046 |   1112 |   
 1047 |   1113 |   	WatchersManager::stop();
 1048 |   1114 |   	ThreadedHasher::stop();
```

Conferência: 10 trechos, 68 linhas adicionadas e 2 removidas.

