# 02-cliente: TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Montagem e ações dos menus; filtros de compilação retiram entradas comerciais no cliente sem apagar opções de aparência/desempenho.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 48, depois 48

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L48) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L48)

```text
ANTES | DEPOIS |   CÓDIGO
   48 |     48 |   #include "guis/GuiTextEditPopupKeyboard.h"
   49 |     49 |   #include "guis/GuiBackupStart.h"
   50 |     50 |   #include "guis/GuiTextEditPopup.h"
      |     51 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
   51 |     52 |   #include "CreditManager.h"
   52 |     53 |   #include "guis/GuiCreditPlayerSelect.h"
   53 |     54 |   #include "guis/GuiCreditOperatorPanel.h"
   54 |     55 |   #include "guis/GuiPixPurchase.h"
   55 |     56 |   #include "guis/GuiPixOwnerSettings.h"
      |     57 | + #endif
   56 |     58 |   // forward usavel via include acima para dynamic_cast no callback da senha
   57 |     59 |   #include "guis/GuiWifi.h"
   58 |     60 |   #include "guis/GuiBluetoothPair.h"
   59 |     61 |   #include "guis/GuiBluetoothDevices.h"
   60 |     62 |   #include "DeveloperMenuAuth.h"
      |     63 | + #include "MainMenuAuth.h"
   61 |     64 |   #include "ThemeChangeAuth.h"
   62 |     65 |   #include "EmbeddedTheme.h"
   63 |     66 |   #include "scrapers/ThreadedScraper.h"
```

## Trecho 2: antes 230, depois 233

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L230) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L233)

```text
ANTES | DEPOIS |   CÓDIGO
  230 |    233 |   		addEntry(_("UNLOCK USER INTERFACE MODE").c_str(), true, [this] { exitKidMode(); }, "iconAdvanced");
  231 |    234 |   	}
  232 |    235 |   
      |    236 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  233 |    237 |   	// Locadora: menu de credito (F11/painel) escondido — so contabilidade no Start se necessario
  234 |    238 |   	// addEntry(_("LOCADORA / CREDITO"), true, [this] { requestCreditSettingsAccess(); }, "iconGames");
  235 |    239 |   	// Este menu inteiro ja foi liberado pela senha do START. A compra do cliente
  236 |    240 |   	// fica fora daqui e e aberta diretamente pelo SELECT, sem senha.
  237 |    241 |   	addEntry(_("CONFIGURACAO PIX DO PROPRIETARIO"), true, [this] { mWindow->pushGui(new GuiPixOwnerSettings(mWindow)); }, "iconSystem");
  238 |    242 |   	addEntry(_("CONTABILIDADE LOCADORA"), true, [this] { requestCreditAccountingAccess(); }, "iconSystem");
      |    243 | + 	#endif
  239 |    244 |   
  240 |    245 |   #ifdef WIN32
  241 |    246 |   	addEntry(_("DESLIGAR TURBORAMA"), !Settings::getInstance()->getBool("ShowOnlyExit") || !Settings::getInstance()->getBool("ShowExit"), [this] { openQuitMenu(); }, "iconQuit");
```

## Trecho 3: antes 276, depois 281

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L276) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L281)

```text
ANTES | DEPOIS |   CÓDIGO
  276 |    281 |   	);
  277 |    282 |   }
  278 |    283 |   
      |    284 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  279 |    285 |   void GuiMenu::requestCreditSettingsAccess()
  280 |    286 |   {
  281 |    287 |   	requestCreditSettingsAccess_static(mWindow);
```

## Trecho 4: antes 285, depois 291

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L285) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L291)

```text
ANTES | DEPOIS |   CÓDIGO
  285 |    291 |   {
  286 |    292 |   	requestCreditAccountingAccess_static(mWindow);
  287 |    293 |   }
      |    294 | + #endif
  288 |    295 |   
  289 |    296 |   namespace
  290 |    297 |   {
```

## Trecho 5: antes 328, depois 335

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L328) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L335)

```text
ANTES | DEPOIS |   CÓDIGO
  328 |    335 |   		});
  329 |    336 |   	}
  330 |    337 |   
      |    338 | + 	bool verifyStartMenuPassword(const std::string& password)
      |    339 | + 	{
      |    340 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
      |    341 | + 		return CreditManager::getInstance().verifyAdminPassword(password);
      |    342 | + #else
      |    343 | + 		return MainMenuAuth::verify(password);
      |    344 | + #endif
      |    345 | + 	}
      |    346 | + 
      |    347 | + 	bool setStartMenuPassword(const std::string& password)
      |    348 | + 	{
      |    349 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
      |    350 | + 		return CreditManager::getInstance().setAdminPassword(password);
      |    351 | + #else
      |    352 | + 		return MainMenuAuth::setPassword(password);
      |    353 | + #endif
      |    354 | + 	}
      |    355 | + 
      |    356 | + 	bool isUsingDefaultStartMenuPassword()
      |    357 | + 	{
      |    358 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
      |    359 | + 		return CreditManager::getInstance().isUsingDefaultAdminPassword();
      |    360 | + #else
      |    361 | + 		return MainMenuAuth::isUsingDefaultPassword();
      |    362 | + #endif
      |    363 | + 	}
      |    364 | + 
      |    365 | + 	void requestStartMenuPasswordChange(Window* window, const std::function<void()>& onSaved)
      |    366 | + 	{
      |    367 | + 		pushSecretTextEdit(window, _("NOVA SENHA ADMIN"), [window, onSaved](const std::string& password)
      |    368 | + 		{
      |    369 | + 			const std::string normalizedPassword = Utils::String::trim(password);
      |    370 | + 			if (normalizedPassword.size() < 8)
      |    371 | + 			{
      |    372 | + 				window->pushGui(new GuiMsgBox(window,
      |    373 | + 					_("SENHA INVALIDA. USE NO MINIMO 8 CARACTERES."), _("OK"), nullptr));
      |    374 | + 				return;
      |    375 | + 			}
      |    376 | + 
      |    377 | + 			window->postToUiThread([window, onSaved, normalizedPassword]
      |    378 | + 			{
      |    379 | + 				pushSecretTextEdit(window, _("CONFIRME A NOVA SENHA ADMIN"),
      |    380 | + 					[window, onSaved, normalizedPassword](const std::string& confirmation)
      |    381 | + 					{
      |    382 | + 						if (Utils::String::trim(confirmation) != normalizedPassword)
      |    383 | + 						{
      |    384 | + 							window->pushGui(new GuiMsgBox(window,
      |    385 | + 								_("AS SENHAS NAO COINCIDEM. A SENHA NAO FOI ALTERADA."),
      |    386 | + 								_("OK"), nullptr));
      |    387 | + 							return;
      |    388 | + 						}
      |    389 | + 
      |    390 | + 						if (!setStartMenuPassword(normalizedPassword))
      |    391 | + 						{
      |    392 | + 							window->pushGui(new GuiMsgBox(window,
      |    393 | + 								_("NAO FOI POSSIVEL GRAVAR A NOVA SENHA."), _("OK"), nullptr));
      |    394 | + 							return;
      |    395 | + 						}
      |    396 | + 
      |    397 | + 						window->displayNotificationMessage(_("Senha admin protegida com sucesso"));
      |    398 | + 						if (onSaved)
      |    399 | + 							window->postToUiThread(onSaved);
      |    400 | + 					});
      |    401 | + 			});
      |    402 | + 		});
      |    403 | + 	}
      |    404 | + 
  331 |    405 |   	void requireNonDefaultAdminPassword(Window* window, const std::function<void()>& onReady)
  332 |    406 |   	{
  333 |        | - 		if (!CreditManager::getInstance().isUsingDefaultAdminPassword())
      |    407 | + 		if (!isUsingDefaultStartMenuPassword())
  334 |    408 |   		{
  335 |    409 |   			onReady();
  336 |    410 |   			return;
  337 |    411 |   		}
  338 |    412 |   
  339 |        | - 		auto requestNewPassword = [window, onReady] {
  340 |        | - 			auto onNewPassword = [window, onReady](const std::string& password) {
  341 |        | - 				if (password.size() < 8)
  342 |        | - 				{
  343 |        | - 					window->pushGui(new GuiMsgBox(window,
  344 |        | - 						_("SENHA INVALIDA. USE NO MINIMO 8 CARACTERES."), _("OK"), nullptr));
  345 |        | - 					return;
  346 |        | - 				}
  347 |        | - 				window->postToUiThread([window, onReady, password] {
  348 |        | - 					pushSecretTextEdit(window, _("CONFIRME A NOVA SENHA ADMIN"),
  349 |        | - 						[window, onReady, password](const std::string& confirmation) {
  350 |        | - 							if (confirmation != password)
  351 |        | - 							{
  352 |        | - 								window->pushGui(new GuiMsgBox(window,
  353 |        | - 									_("AS SENHAS NAO COINCIDEM. A SENHA NAO FOI ALTERADA."),
  354 |        | - 									_("OK"), nullptr));
  355 |        | - 								return;
  356 |        | - 							}
  357 |        | - 							if (!CreditManager::getInstance().setAdminPassword(password))
  358 |        | - 							{
  359 |        | - 								window->pushGui(new GuiMsgBox(window,
  360 |        | - 									_("NAO FOI POSSIVEL GRAVAR A NOVA SENHA."), _("OK"), nullptr));
  361 |        | - 								return;
  362 |        | - 							}
  363 |        | - 							window->displayNotificationMessage(_("Senha admin protegida com sucesso"));
  364 |        | - 							window->postToUiThread(onReady);
  365 |        | - 						});
  366 |        | - 				});
  367 |        | - 			};
  368 |        | - 			pushSecretTextEdit(window, _("NOVA SENHA ADMIN"), onNewPassword);
  369 |        | - 		};
  370 |        | - 
  371 |    413 |   		window->pushGui(new GuiMsgBox(window,
  372 |    414 |   			_("A SENHA PADRAO 'admin' E INSEGURA. DEFINA AGORA UMA NOVA SENHA COM NO MINIMO 8 CARACTERES PARA CONTINUAR."),
  373 |        | - 			_("TROCAR AGORA"), [window, requestNewPassword] {
  374 |        | - 				window->postToUiThread(requestNewPassword);
      |    415 | + 			_("TROCAR AGORA"), [window, onReady] {
      |    416 | + 				window->postToUiThread([window, onReady] {
      |    417 | + 					requestStartMenuPasswordChange(window, onReady);
      |    418 | + 				});
  375 |    419 |   			},
  376 |    420 |   			_("CANCELAR"), nullptr));
  377 |    421 |   	}
```

## Trecho 6: antes 388, depois 432

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L388) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L432)

```text
ANTES | DEPOIS |   CÓDIGO
  388 |    432 |   
  389 |    433 |   	auto onPasswordEntered = [window](const std::string& password)
  390 |    434 |   	{
  391 |        | - 		if (!CreditManager::getInstance().verifyAdminPassword(password))
      |    435 | + 		if (!verifyStartMenuPassword(password))
  392 |    436 |   		{
  393 |    437 |   			window->pushGui(new GuiMsgBox(window, _("SENHA INCORRETA"), _("OK"), nullptr));
  394 |    438 |   			return;
```

## Trecho 7: antes 410, depois 454

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L410) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L454)

```text
ANTES | DEPOIS |   CÓDIGO
  410 |    454 |   		window->pushGui(new GuiTextEditPopup(window, _("SENHA MENU START"), "", onPasswordEntered, false, "OK", true));
  411 |    455 |   }
  412 |    456 |   
      |    457 | + #ifdef TURBORAMA_NO_COMMERCIAL_SERVICES
      |    458 | + void GuiMenu::requestTurboSystemMenuAccess_static(Window* window)
      |    459 | + {
      |    460 | + 	if (window == nullptr)
      |    461 | + 		return;
      |    462 | + 
      |    463 | + 	auto onPasswordEntered = [window](const std::string& password)
      |    464 | + 	{
      |    465 | + 		if (!verifyStartMenuPassword(password))
      |    466 | + 		{
      |    467 | + 			window->pushGui(new GuiMsgBox(window, _("SENHA INCORRETA"), _("OK"), nullptr));
      |    468 | + 			return;
      |    469 | + 		}
      |    470 | + 
      |    471 | + 		// Abrir no proximo frame, depois de o teclado fechar, e conservar a
      |    472 | + 		// mesma troca obrigatoria da senha padrao usada pelo menu START.
      |    473 | + 		window->postToUiThread([window]
      |    474 | + 		{
      |    475 | + 			requireNonDefaultAdminPassword(window, [window]
      |    476 | + 			{
      |    477 | + 				window->postToUiThread([window]
      |    478 | + 				{
      |    479 | + 					openTurboSystemMenu_static(window);
      |    480 | + 				});
      |    481 | + 			});
      |    482 | + 		});
      |    483 | + 	};
      |    484 | + 
      |    485 | + 	if (Settings::getInstance()->getBool("UseOSK"))
      |    486 | + 		window->pushGui(new GuiTextEditPopupKeyboard(window, _("SENHA PAINEL F11"), "", onPasswordEntered, false, "OK", true));
      |    487 | + 	else
      |    488 | + 		window->pushGui(new GuiTextEditPopup(window, _("SENHA PAINEL F11"), "", onPasswordEntered, false, "OK", true));
      |    489 | + }
      |    490 | + 
      |    491 | + void GuiMenu::openTurboSystemMenu_static(Window* window)
      |    492 | + {
      |    493 | + 	if (window == nullptr)
      |    494 | + 		return;
      |    495 | + 
      |    496 | + 	auto menu = new GuiSettings(window, _("TURBO SISTEMA").c_str());
      |    497 | + #ifdef WIN32
      |    498 | + 	menu->addEntry(_("ABRIR TURBO SISTEMA..."), false, [window]
      |    499 | + 	{
      |    500 | + 		window->pushGui(new GuiMsgBox(window,
      |    501 | + 			_("Abrir o Turbo Sistema (ambiente do sistema)?\n\nPode voltar ao EmulationStation depois."),
      |    502 | + 			_("SIM, ABRIR"), [window]
      |    503 | + 			{
      |    504 | + 				window->displayNotificationMessage(_("A abrir Turbo Sistema..."), 2);
      |    505 | + 				Utils::Platform::ProcessStartInfo explorer("explorer.exe");
      |    506 | + 				explorer.waitForExit = false;
      |    507 | + 				explorer.showWindow = true;
      |    508 | + 				if (explorer.run() != 0)
      |    509 | + 				{
      |    510 | + 					Utils::Platform::ProcessStartInfo shell("C:\\Windows\\explorer.exe");
      |    511 | + 					shell.waitForExit = false;
      |    512 | + 					shell.showWindow = true;
      |    513 | + 					shell.run();
      |    514 | + 				}
      |    515 | + 			},
      |    516 | + 			_("NAO"), nullptr));
      |    517 | + 	}, "iconSystem");
      |    518 | + 
      |    519 | + 	menu->addEntry(_("TROCAR DE USUARIO..."), false, [window]
      |    520 | + 	{
      |    521 | + 		window->pushGui(new GuiMsgBox(window,
      |    522 | + 			_("Trocar de usuario?\n\nA sessao atual sera desligada e aparecera a tela de contas."),
      |    523 | + 			_("SIM, TROCAR"), [window]
      |    524 | + 			{
      |    525 | + 				window->displayNotificationMessage(_("A trocar de usuario..."), 2);
      |    526 | + 				Utils::Platform::ProcessStartInfo switchUser("C:\\Windows\\System32\\tsdiscon.exe");
      |    527 | + 				switchUser.waitForExit = false;
      |    528 | + 				switchUser.showWindow = false;
      |    529 | + 				if (switchUser.run() != 0)
      |    530 | + 				{
      |    531 | + 					Utils::Platform::ProcessStartInfo logoff("shutdown /l");
      |    532 | + 					logoff.waitForExit = false;
      |    533 | + 					logoff.showWindow = false;
      |    534 | + 					logoff.run();
      |    535 | + 				}
      |    536 | + 			},
      |    537 | + 			_("NAO"), nullptr));
      |    538 | + 	}, "iconSystem");
      |    539 | + 
      |    540 | + 	menu->addEntry(_("ENCERRAR PROCESSO..."), false, [window]
      |    541 | + 	{
      |    542 | + 		window->pushGui(new GuiMsgBox(window,
      |    543 | + 			_("Encerrar o processo do EmulationStation?\n\nO computador permanecera ligado."),
      |    544 | + 			_("SIM, ENCERRAR"), [window]
      |    545 | + 			{
      |    546 | + 				if (Utils::Platform::quitES(Utils::Platform::QuitMode::EXIT_ONLY) != 0)
      |    547 | + 				{
      |    548 | + 					window->pushGui(new GuiMsgBox(window,
      |    549 | + 						_("Nao foi possivel preparar a saida segura.\n\n"
      |    550 | + 							"O EmulationStation continuara aberto e o computador nao sera desligado."),
      |    551 | + 						_("OK"), nullptr));
      |    552 | + 				}
      |    553 | + 			},
      |    554 | + 			_("NAO"), nullptr));
      |    555 | + 	}, "iconQuit");
      |    556 | + #else
      |    557 | + 	menu->addEntry(_("ACOES DISPONIVEIS SOMENTE NO WINDOWS"), false, nullptr, "iconSystem");
      |    558 | + #endif
      |    559 | + 
      |    560 | + 	window->pushGui(menu);
      |    561 | + }
      |    562 | + #endif
      |    563 | + 
      |    564 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  413 |    565 |   void GuiMenu::requestCreditSettingsAccess_static(Window* window)
  414 |    566 |   {
  415 |    567 |   	if (window == nullptr)
```

## Trecho 8: antes 732, depois 884

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L732) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L884)

```text
ANTES | DEPOIS |   CÓDIGO
  732 |    884 |   
  733 |    885 |   	window->pushGui(s);
  734 |    886 |   }
      |    887 | + #endif
  735 |    888 |   
  736 |    889 |   void GuiMenu::addVersionInfo()
  737 |    890 |   {
```

## Trecho 9: antes 1210, depois 1363

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L1210) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L1363)

```text
ANTES | DEPOIS |   CÓDIGO
 1210 |   1363 |   		DeveloperMenuAuth::setPassword(newVal);
 1211 |   1364 |   		Settings::getInstance()->saveFile();
 1212 |   1365 |   	});
      |   1366 | + 
      |   1367 | + #ifdef TURBORAMA_NO_COMMERCIAL_SERVICES
      |   1368 | + 	s->addEntry(_("ALTERAR SENHA DO MENU START"), true, [window]
      |   1369 | + 	{
      |   1370 | + 		requestStartMenuPasswordChange(window, std::function<void()>());
      |   1371 | + 	});
      |   1372 | + #endif
 1213 |   1373 |   	
 1214 |   1374 |   	s->addGroup(_("VIDEO OPTIONS"));
 1215 |   1375 |   
```

## Trecho 10: antes 5153, depois 5313

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L5153) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L5313)

```text
ANTES | DEPOIS |   CÓDIGO
 5153 |   5313 |   	{
 5154 |   5314 |       		s->addGroup(_("QUICK ACCESS"));
 5155 |   5315 |   
      |   5316 | + 		#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
 5156 |   5317 |   		// Menu LOCADORA/CREDITO (painel tipo F11) escondido
 5157 |   5318 |   		// s->addEntry(_("LOCADORA / CREDITO"), ...);
 5158 |   5319 |   		s->addEntry(_("CONTABILIDADE LOCADORA"), true, [s, window] {
 5159 |   5320 |   			delete s;
 5160 |   5321 |   			GuiMenu::requestCreditAccountingAccess_static(window);
 5161 |   5322 |   		}, "iconSystem");
      |   5323 | + 		#endif
 5162 |   5324 |   
 5163 |   5325 |               if (AudioManager::getInstance()->isSongPlaying())
 5164 |   5326 |               {
```

Conferência: 10 trechos, 198 linhas adicionadas e 36 removidas.

