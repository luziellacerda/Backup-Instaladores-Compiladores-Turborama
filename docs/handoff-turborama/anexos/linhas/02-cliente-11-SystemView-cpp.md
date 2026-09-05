# 02-cliente: TurboramaEmulationStation/es-app/src/views/SystemView.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Tela de sistemas: seleção, carrossel, ciclo de vida dos vídeos, atualização visual e, na PIX, integrações de serviços.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 91, depois 91

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L91) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L91)

```text
ANTES | DEPOIS |   CÓDIGO
   91 |     91 |   SystemView::SystemView(Window* window) : GuiComponent(window),
   92 |     92 |   	mViewNeedsReload(true),
   93 |     93 |   	mSystemInfo(window, _("SYSTEM INFO"), Font::get(FONT_SIZE_SMALL), 0x33333300, ALIGN_CENTER),
      |     94 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
   94 |     95 |   	mHomePixQrImage(window, true),
   95 |     96 |   	mHomePixOffer(window, "15 MINUTOS\n5 REAIS", Font::get(FONT_SIZE_LARGE), 0x62FF55FF, ALIGN_CENTER),
   96 |     97 |   	mHomePixInstruction(window, _("GERANDO QR PIX..."), Font::get(FONT_SIZE_SMALL), 0xEAF5FFFF, ALIGN_CENTER),
      |     98 | + #endif
   97 |     99 |   	mYButton("y")
   98 |    100 |   {
   99 |    101 |   	mExtraTransitionSpeed = 500.0f;
```

## Trecho 2: antes 123, depois 125

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L123) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L125)

```text
ANTES | DEPOIS |   CÓDIGO
  123 |    125 |   	mExtrasTransitionActive = false;
  124 |    126 |   	mPressedCursor = -1;
  125 |    127 |   	mPressedPoint = Vector2i(-1, -1);
      |    128 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  126 |    129 |   	mHomePixQrSize = 0.f;
  127 |    130 |   	mHomePixQrModuleCount = 0;
  128 |    131 |   	mHomePixPollElapsedMs = 0;
```

## Trecho 3: antes 144, depois 147

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L144) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L147)

```text
ANTES | DEPOIS |   CÓDIGO
  144 |    147 |   	mHomePixOffer.setGlowSize(2);
  145 |    148 |   	mHomePixInstruction.setGlowColor(0x000000F0);
  146 |    149 |   	mHomePixInstruction.setGlowSize(2);
      |    150 | + #endif
  147 |    151 |   
  148 |    152 |   	setSize((float)Renderer::getScreenWidth(), (float)Renderer::getScreenHeight());
      |    153 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  149 |    154 |   	layoutHomePix();
      |    155 | + #endif
  150 |    156 |   	populate();
  151 |    157 |   }
  152 |    158 |   
```

## Trecho 4: antes 808, depois 814

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L808) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L814)

```text
ANTES | DEPOIS |   CÓDIGO
  808 |    814 |   
  809 |    815 |   	GuiComponent::update(deltaTime);
  810 |    816 |   
      |    817 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  811 |    818 |   	if (!mDisable && !mScreensaverActive)
  812 |    819 |   		updateHomePix(deltaTime);
      |    820 | + 	#endif
  813 |    821 |   
  814 |    822 |   	if (mYButton.isLongPressed(deltaTime))
  815 |    823 |   	{
```

## Trecho 5: antes 821, depois 829

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L821) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L829)

```text
ANTES | DEPOIS |   CÓDIGO
  821 |    829 |   	}
  822 |    830 |   }
  823 |    831 |   
      |    832 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  824 |    833 |   std::string SystemView::formatHomePixOffer(const PixPackage& package) const
  825 |    834 |   {
  826 |    835 |   	std::ostringstream output;
```

## Trecho 6: antes 1122, depois 1131

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L1122) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L1131)

```text
ANTES | DEPOIS |   CÓDIGO
 1122 |   1131 |   	if (!mHomePixQrModules.empty()) renderHomePixQrMatrix(trans);
 1123 |   1132 |   	else mHomePixQrImage.render(trans);
 1124 |   1133 |   }
      |   1134 | + #endif
 1125 |   1135 |   
 1126 |   1136 |   void SystemView::updateExtraTextBinding()
 1127 |   1137 |   {
```

## Trecho 7: antes 1445, depois 1455

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L1445) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L1455)

```text
ANTES | DEPOIS |   CÓDIGO
 1445 |   1455 |   
 1446 |   1456 |   	renderExtras(trans, minMax.second, INT16_MAX);
 1447 |   1457 |   
      |   1458 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
 1448 |   1459 |   	// O QR comercial pertence somente a tela principal de sistemas. Ele e
 1449 |   1460 |   	// desenhado por ultimo para permanecer legivel sem alterar o tema ativo.
 1450 |   1461 |   	renderHomePix(trans);
      |   1462 | + 	#endif
 1451 |   1463 |   }
 1452 |   1464 |   
 1453 |   1465 |   std::vector<HelpPrompt> SystemView::getHelpPrompts()
```

## Trecho 8: antes 2016, depois 2028

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2016) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2028)

```text
ANTES | DEPOIS |   CÓDIGO
 2016 |   2028 |   	if (getSelected() != nullptr)
 2017 |   2029 |   		TextToSpeech::getInstance()->say(getSelected()->getFullName());
 2018 |   2030 |   
      |   2031 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
 2019 |   2032 |   	// A primeira tentativa ocorre logo apos a tela principal aparecer. Se o
 2020 |   2033 |   	// agente ainda estiver iniciando, updateHomePix repete sem bloquear a UI.
 2021 |   2034 |   	if (!mHomePixRequestActive)
```

## Trecho 9: antes 2023, depois 2036

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2023) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2036)

```text
ANTES | DEPOIS |   CÓDIGO
 2023 |   2036 |   		mHomePixRetryElapsedMs = 0;
 2024 |   2037 |   		mHomePixRetryDelayMs = 350;
 2025 |   2038 |   	}
      |   2039 | + 	#endif
 2026 |   2040 |   }
 2027 |   2041 |   
 2028 |   2042 |   void SystemView::onHide()
```

Conferência: 9 trechos, 14 linhas adicionadas e 0 removidas.

