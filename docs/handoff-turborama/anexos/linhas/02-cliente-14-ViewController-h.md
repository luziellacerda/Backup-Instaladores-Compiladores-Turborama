# 02-cliente: TurboramaEmulationStation/es-app/src/views/ViewController.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Declarações do controlador que acompanham os caminhos de autenticação.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 138, depois 138

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/ViewController.h#L138) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.h#L138)

```text
ANTES | DEPOIS |   CÓDIGO
  138 |    138 |   	std::shared_ptr<GuiComponent>	mDeferPlayViewTransitionTo;
  139 |    139 |   	State mState;
  140 |    140 |   
      |    141 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  141 |    142 |   	// TurboRama arcade credit HUD (always visible when credit system is enabled)
  142 |    143 |   	int mCreditHudElapsedMs;
  143 |    144 |   	std::string mCreditHudText;
  144 |    145 |   	std::unique_ptr<TextCache> mCreditHudCache;
      |    146 | + 	#endif
  145 |    147 |   };
  146 |    148 |   
  147 |    149 |   #endif // ES_APP_VIEWS_VIEW_CONTROLLER_H
```

Conferência: 1 trechos, 2 linhas adicionadas e 0 removidas.

