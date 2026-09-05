# 02-cliente: TurboramaEmulationStation/es-app/src/views/ViewController.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Navegação central e acesso ao menu; encaminha autenticação à implementação da variante.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 32, depois 32

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L32) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L32)

```text
ANTES | DEPOIS |   CÓDIGO
   32 |     32 |   #include "VolumeControl.h"
   33 |     33 |   #include "guis/GuiNetPlay.h"
   34 |     34 |   #include "Gamelist.h"
      |     35 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
   35 |     36 |   #include "CreditManager.h"
   36 |     37 |   #include "CreditWarningOverlay.h"
   37 |     38 |   #include "PixBridge.h"
   38 |     39 |   #include "PixAgentManager.h"
   39 |     40 |   #include "guis/GuiPixPurchase.h"
      |     41 | + #endif
   40 |     42 |   #include "resources/Font.h"
   41 |     43 |   
   42 |     44 |   ViewController* ViewController::sInstance = nullptr;
```

## Trecho 2: antes 86, depois 88

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L86) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L88)

```text
ANTES | DEPOIS |   CÓDIGO
   86 |     88 |   
   87 |     89 |   ViewController::ViewController(Window* window)
   88 |     90 |   	: GuiComponent(window), mCurrentView(nullptr), mCamera(Transform4x4f::Identity()), mFadeOpacity(0), mLockInput(false)
      |     91 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
   89 |     92 |   	, mCreditHudElapsedMs(0)
      |     93 | + 	#endif
   90 |     94 |   {
   91 |     95 |   	mSystemListView = nullptr;
   92 |     96 |   	mState.viewing = NOTHING;	
   93 |     97 |   	mState.system = nullptr;
      |     98 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
   94 |     99 |   	// Force first HUD paint immediately (do not wait 500ms / F10)
   95 |    100 |   	if (CreditManager::getInstance().isEnabled() && CreditManager::getInstance().isShowHud())
   96 |    101 |   		mCreditHudText = CreditManager::getInstance().formatHudLine();
   97 |    102 |   	else
   98 |    103 |   		mCreditHudText.clear();
      |    104 | + 	#endif
   99 |    105 |   }
  100 |    106 |   
  101 |    107 |   ViewController::~ViewController()
```

## Trecho 3: antes 912, depois 918

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L912) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L918)

```text
ANTES | DEPOIS |   CÓDIGO
  912 |    918 |   		return true;
  913 |    919 |   	}
  914 |    920 |   
      |    921 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  915 |    922 |   	// TurboRama comercial: SELECT pertence ao cliente e abre somente a compra PIX.
  916 |    923 |   	// Nenhuma senha ou configuracao administrativa fica acessivel por este atalho.
  917 |    924 |   	if(config->isMappedTo("select", input) && input.value != 0)
```

## Trecho 4: antes 920, depois 927

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L920) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L927)

```text
ANTES | DEPOIS |   CÓDIGO
  920 |    927 |   			mWindow->pushGui(new GuiPixPurchase(mWindow));
  921 |    928 |   		return true;
  922 |    929 |   	}
      |    930 | + 	#endif
  923 |    931 |   
  924 |    932 |   	// Next song
  925 |    933 |   	if (((mState.viewing != GAME_LIST && config->isMappedTo("l3", input)) || config->isMappedTo("r3", input)) && input.value != 0)
```

## Trecho 5: antes 958, depois 966

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L958) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L966)

```text
ANTES | DEPOIS |   CÓDIGO
  958 |    966 |   
  959 |    967 |   	updateSelf(deltaTime);
  960 |    968 |   
      |    969 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  961 |    970 |   	// Count credit while browsing menus (capped inside tick against lag spikes)
  962 |    971 |   	// Skip absurd frames (paused debugger / post-game) — tick also caps
  963 |    972 |   	if (deltaTime > 0 && deltaTime < 60000)
```

## Trecho 6: antes 1005, depois 1014

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L1005) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L1014)

```text
ANTES | DEPOIS |   CÓDIGO
 1005 |   1014 |   			mCreditHudCache.reset();
 1006 |   1015 |   		}
 1007 |   1016 |   	}
      |   1017 | + 	#endif
 1008 |   1018 |   
 1009 |   1019 |   	if (mDeferPlayViewTransitionTo != nullptr)
 1010 |   1020 |   	{
```

## Trecho 7: antes 1053, depois 1063

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L1053) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L1063)

```text
ANTES | DEPOIS |   CÓDIGO
 1053 |   1063 |   	if(mWindow->peekGui() == this)
 1054 |   1064 |   		mWindow->renderHelpPromptsEarly(parentTrans);
 1055 |   1065 |   
      |   1066 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
 1056 |   1067 |   	// TurboRama credit HUD: SO na tela inicial (sistemas) + fonte menor
 1057 |   1068 |   	// Nao mostra na lista de jogos nem com menus abertos por cima
 1058 |   1069 |   	const bool onHomeScreen =
```

## Trecho 8: antes 1124, depois 1135

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L1124) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L1135)

```text
ANTES | DEPOIS |   CÓDIGO
 1124 |   1135 |   				font->renderTextCache(drawTime.get());
 1125 |   1136 |   		}
 1126 |   1137 |   	}
      |   1138 | + 	#endif
 1127 |   1139 |   
 1128 |   1140 |   	// fade out
 1129 |   1141 |   	if (mFadeOpacity)
```

Conferência: 8 trechos, 12 linhas adicionadas e 0 removidas.

