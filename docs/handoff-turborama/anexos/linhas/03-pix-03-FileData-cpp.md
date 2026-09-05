# 03-pix: TurboramaEmulationStation/es-app/src/FileData.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Dados e metadados do jogo; cache de mídia e sequência de preparação, execução e retorno do emulador. Leia os capítulos de memória e da variante correspondente.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `476e06179f89ac209ff808dffb27555d740f93d2`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 4, depois 4

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L4) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L4)

```text
ANTES | DEPOIS |   CÓDIGO
    4 |      4 |   #include "utils/StringUtil.h"
    5 |      5 |   #include "utils/TimeUtil.h"
    6 |      6 |   #include "AudioManager.h"
      |      7 | + #include "components/VideoVlcComponent.h"
    7 |      8 |   #include "CollectionSystemManager.h"
    8 |      9 |   #include "FileFilterIndex.h"
    9 |     10 |   #include "FileSorts.h"
```

## Trecho 2: antes 1231, depois 1232

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L1231) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L1232)

```text
ANTES | DEPOIS |   CÓDIGO
 1231 |   1232 |   
 1232 |   1233 |   	bool hideWindow = Settings::getInstance()->getBool("HideWindow");
 1233 |   1234 |   	window->deinit(hideWindow);
      |   1235 | + 	// Muting VLC does not release its audio device. Finish queued releases
      |   1236 | + 	// before game-start and before the existing supervised credit session.
      |   1237 | + 	if (!VideoVlcComponent::waitForAudioRelease(3000))
      |   1238 | + 		LOG(LogWarning) << "[AudioHandoff] VLC release exceeded 3000 ms; continuing game launch";
      |   1239 | + 	else
      |   1240 | + 		LOG(LogInfo) << "[AudioHandoff] VLC audio released before game launch";
 1234 |   1241 |   	
 1235 |   1242 |   	const std::string rom = Utils::FileSystem::getEscapedPath(getPath());
 1236 |   1243 |   	const std::string basename = Utils::FileSystem::getStem(getPath());
```

Conferência: 2 trechos, 7 linhas adicionadas e 0 removidas.

