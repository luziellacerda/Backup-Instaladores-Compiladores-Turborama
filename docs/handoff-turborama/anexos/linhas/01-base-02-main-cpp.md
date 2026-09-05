# 01-base: TurboramaEmulationStation/es-app/src/main.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Ponto de entrada, criação da janela, inicialização do tema, loop e encerramento. Serviços comerciais ficam condicionados na versão cliente.

- Antes: `0e02780b761cb488c591416d2986130efcc166dd`.
- Depois: `76b214874973fe24017823401216896f3d7a6f40`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 676, depois 676

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/main.cpp#L676) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L676)

```text
ANTES | DEPOIS |   CÓDIGO
  676 |    676 |   	// Set locale
  677 |    677 |   	setLocale(argv[0]);	
  678 |    678 |   
      |    679 | + 	// Materialize the singleton on the main thread before background startup
      |    680 | + 	// work can request resources.
      |    681 | + 	ResourceManager::getInstance();
      |    682 | + 
  679 |    683 |   #if !WIN32
  680 |    684 |   	if(enable_startup_game) {
  681 |    685 |   	  // Run boot game, before Window Create for linux
```

## Trecho 2: antes 691, depois 695

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/main.cpp#L691) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L695)

```text
ANTES | DEPOIS |   CÓDIGO
  691 |    695 |   	threadPool->queueWorkItem([] { MameNames::init(); });
  692 |    696 |   	threadPool->queueWorkItem([] { Genres::init(); });
  693 |    697 |   	threadPool->queueWorkItem([] { HttpReq::resetCookies(); });
  694 |        | - 	threadPool->start();
  695 |    698 |   
  696 |    699 |   	Window window;
  697 |    700 |   	ViewController::init(&window);
```

## Trecho 3: antes 709, depois 712

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/main.cpp#L709) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L712)

```text
ANTES | DEPOIS |   CÓDIGO
  709 |    712 |   	bool splashScreen = Settings::getInstance()->getBool("SplashScreen");
  710 |    713 |   	bool splashScreenProgress = Settings::getInstance()->getBool("SplashScreenProgress");
  711 |    714 |   
      |    715 | + 	// The embedded theme can take a while to decrypt on its first run. Start it
      |    716 | + 	// only after the window exists and keep rendering progress so Windows does
      |    717 | + 	// not present the application as frozen.
      |    718 | + 	window.renderSplashScreen(_("Loading theme"), 0.0f);
      |    719 | + 	const bool embeddedThemeReady = EmbeddedTheme::initialize([&window](float progress) {
      |    720 | + 		window.renderSplashScreen(_("Loading theme"), progress);
      |    721 | + 	});
      |    722 | + 	if (!embeddedThemeReady)
      |    723 | + 		LOG(LogWarning) << "Embedded theme could not be initialized.";
      |    724 | + 	else
      |    725 | + 	{
      |    726 | + 		ResourceManager::invalidatePathCache();
      |    727 | + 		ResourceManager::getInstance()->unloadAll();
      |    728 | + 		ResourceManager::getInstance()->reloadAll();
      |    729 | + 	}
      |    730 | + 
      |    731 | + 	// Workers consult Settings and ResourceManager; start them only after the
      |    732 | + 	// theme selection and resource cache are stable.
      |    733 | + 	threadPool->start();
      |    734 | + 
  712 |    735 |   	if (splashScreen)
  713 |    736 |   		window.renderSplashScreen(splashScreenProgress ? _("Loading system config...") : _("Loading..."));
      |    737 | + 	else
      |    738 | + 		window.closeSplashScreen();
  714 |    739 |   
  715 |    740 |   	Scripting::fireEvent("start");
  716 |    741 |   
```

## Trecho 4: antes 775, depois 800

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/main.cpp#L775) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/main.cpp#L800)

```text
ANTES | DEPOIS |   CÓDIGO
  775 |    800 |   	
  776 |    801 |   	if (errorMsg == NULL)
  777 |    802 |   	{
  778 |        | - 		if (splashScreen)
  779 |        | - 			window.renderSplashScreen(_("Loading theme"));
  780 |        | - 
  781 |        | - 		if (!EmbeddedTheme::initialize())
  782 |        | - 			LOG(LogWarning) << "Embedded theme could not be initialized.";
  783 |        | - 
  784 |    803 |   		ViewController::get()->goToStart(true);
  785 |    804 |   	}
  786 |    805 |   
```

Conferência: 4 trechos, 26 linhas adicionadas e 7 removidas.

