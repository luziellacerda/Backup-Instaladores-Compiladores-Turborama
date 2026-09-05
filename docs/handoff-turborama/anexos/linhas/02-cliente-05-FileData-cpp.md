# 02-cliente: TurboramaEmulationStation/es-app/src/FileData.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Dados e metadados do jogo; cache de mídia e sequência de preparação, execução e retorno do emulador. Leia os capítulos de memória e da variante correspondente.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 34, depois 34

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L34) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L34)

```text
ANTES | DEPOIS |   CÓDIGO
   34 |     34 |   #include "Paths.h"
   35 |     35 |   #include "resources/TextureData.h"
   36 |     36 |   #include "views/gamelist/GameNameFormatter.h"
      |     37 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
   37 |     38 |   #include "CreditManager.h"
   38 |     39 |   #include "CreditWarningOverlay.h"
      |     40 | + #endif
   39 |     41 |   #include <chrono>
   40 |     42 |   #include <atomic>
   41 |     43 |   
```

## Trecho 2: antes 59, depois 61

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L59) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L61)

```text
ANTES | DEPOIS |   CÓDIGO
   59 |     61 |   			std::chrono::steady_clock::now().time_since_epoch()).count();
   60 |     62 |   	}
   61 |     63 |   
   62 |        | - #ifdef WIN32
      |     64 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
      |     65 | + 	#ifdef WIN32
   63 |     66 |   	// Native, non-activating warning used while an external emulator owns the
   64 |     67 |   	// screen. The regular EmulationStation notification cannot be rendered while
   65 |     68 |   	// ProcessStartInfo::run() is supervising a running game.
```

## Trecho 3: antes 327, depois 330

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L327) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L330)

```text
ANTES | DEPOIS |   CÓDIGO
  327 |    330 |   		void tick() {}
  328 |    331 |   		bool isVisible() const { return false; }
  329 |    332 |   	};
  330 |        | - #endif
      |    333 | + 	#endif
      |    334 | + 	#endif
  331 |    335 |   }
  332 |    336 |   
      |    337 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  333 |    338 |   namespace CreditWarningOverlay
  334 |    339 |   {
  335 |    340 |   	static GameCreditWarningOverlay& instance()
```

## Trecho 4: antes 353, depois 358

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L353) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L358)

```text
ANTES | DEPOIS |   CÓDIGO
  353 |    358 |   		return instance().isVisible();
  354 |    359 |   	}
  355 |    360 |   }
      |    361 | + #endif
  356 |    362 |   
  357 |    363 |   static std::map<std::string, std::function<BindableProperty(FileData*)>> properties =
  358 |    364 |   {
```

## Trecho 5: antes 1210, depois 1216

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L1210) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1216)

```text
ANTES | DEPOIS |   CÓDIGO
 1210 |   1216 |   	if (system == nullptr)
 1211 |   1217 |   		return false;
 1212 |   1218 |   
      |   1219 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
 1213 |   1220 |   	// TurboRama arcade credit: block launch without credit (before audio/window teardown)
 1214 |   1221 |   	CreditManager& credits = CreditManager::getInstance();
 1215 |   1222 |   	const bool creditEnabled = credits.isEnabled();
```

## Trecho 6: antes 1221, depois 1228

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L1221) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1228)

```text
ANTES | DEPOIS |   CÓDIGO
 1221 |   1228 |   			_("OK"), nullptr));
 1222 |   1229 |   		return false;
 1223 |   1230 |   	}
      |   1231 | + 	#endif
 1224 |   1232 |   
 1225 |   1233 |   	std::string command = getlaunchCommand(options);
 1226 |   1234 |   	if (command.empty())
```

## Trecho 7: antes 1238, depois 1246

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L1238) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1246)

```text
ANTES | DEPOIS |   CÓDIGO
 1238 |   1246 |   	Scripting::fireEvent("game-start", rom, basename, getName());
 1239 |   1247 |   	const auto launchT0 = std::chrono::steady_clock::now();
 1240 |   1248 |   
      |   1249 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
 1241 |   1250 |   	// RAII: end only a session that actually started. The first supervised poll
 1242 |   1251 |   	// happens after CreateProcess/Job assignment/ResumeThread succeed, so a launch
 1243 |   1252 |   	// failure never opens or charges a credit session.
```

## Trecho 8: antes 1261, depois 1270

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L1261) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1270)

```text
ANTES | DEPOIS |   CÓDIGO
 1261 |   1270 |   				supervisedElapsedSeconds == nullptr ? 0 : std::max(0L, *supervisedElapsedSeconds));
 1262 |   1271 |   		}
 1263 |   1272 |   	};
      |   1273 | + 	#endif
 1264 |   1274 |   
 1265 |   1275 |   	LOG(LogInfo) << "	" << command;
 1266 |   1276 |   
```

## Trecho 9: antes 1270, depois 1280

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L1270) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1280)

```text
ANTES | DEPOIS |   CÓDIGO
 1270 |   1280 |   
 1271 |   1281 |   	ProcessStartInfo process(command);
 1272 |   1282 |   	process.window = hideWindow ? NULL : window;
      |   1283 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
 1273 |   1284 |   	bool creditExpired = false;
 1274 |   1285 |   	bool creditSessionStarted = false;
 1275 |   1286 |   	bool warned60 = false;
```

## Trecho 10: antes 1310, depois 1321

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L1310) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1321)

```text
ANTES | DEPOIS |   CÓDIGO
 1310 |   1321 |   			return mayContinue;
 1311 |   1322 |   		};
 1312 |   1323 |   	}
      |   1324 | + 	#endif
 1313 |   1325 |   
 1314 |   1326 |   	int exitCode = process.run();
 1315 |   1327 |   	if (exitCode != 0)
 1316 |   1328 |   		LOG(LogWarning) << "...launch terminated with nonzero exit code " << exitCode << "!";
 1317 |   1329 |   
 1318 |   1330 |   	mRunningGame = nullptr;
      |   1331 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
 1319 |   1332 |   	creditGuard.complete();
      |   1333 | + 	#endif
 1320 |   1334 |   
 1321 |   1335 |   	Utils::FileSystem::FileSystemCache::reset();
 1322 |   1336 |   
```

## Trecho 11: antes 1372, depois 1386

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L1372) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1386)

```text
ANTES | DEPOIS |   CÓDIGO
 1372 |   1386 |   	}
 1373 |   1387 |   
 1374 |   1388 |   	window->reactivateGui();
      |   1389 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
 1375 |   1390 |   	if (creditExpired)
 1376 |   1391 |   	{
 1377 |   1392 |   		window->pushGui(new GuiMsgBox(window,
 1378 |   1393 |   			_("TEMPO ESGOTADO. O JOGO FOI ENCERRADO PARA PROTEGER O SALDO DA LOCADORA."),
 1379 |   1394 |   			_("OK"), nullptr));
 1380 |   1395 |   	}
      |   1396 | + 	#endif
 1381 |   1397 |   
 1382 |   1398 |   	if (system != nullptr && system->getTheme() != nullptr)
 1383 |   1399 |   		AudioManager::getInstance()->changePlaylist(system->getTheme(), true);
```

Conferência: 11 trechos, 18 linhas adicionadas e 2 removidas.

