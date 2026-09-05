# 02-cliente: TurboramaEmulationStation/es-app/src/MainMenuAuth.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Contrato mínimo da autenticação não comercial do menu.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 0, depois 1

Arquivo novo nesta comparação; não existe na revisão anterior. [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.h#L1)

```text
ANTES | DEPOIS |   CÓDIGO
      |      1 | + #pragma once
      |      2 | + #ifndef ES_APP_MAIN_MENU_AUTH_H
      |      3 | + #define ES_APP_MAIN_MENU_AUTH_H
      |      4 | + 
      |      5 | + #include <string>
      |      6 | + 
      |      7 | + // Authentication for the normal START menu.  This deliberately lives outside
      |      8 | + // CreditManager so customer builds can keep kiosk protection without compiling
      |      9 | + // any credit, PIX, accounting or rental-time state.
      |     10 | + class MainMenuAuth
      |     11 | + {
      |     12 | + public:
      |     13 | + 	static bool verify(const std::string& password);
      |     14 | + 	static bool setPassword(const std::string& password);
      |     15 | + 	static bool isUsingDefaultPassword();
      |     16 | + 	static bool hasCustomPassword();
      |     17 | + 	static bool runSelfTest();
      |     18 | + };
      |     19 | + 
      |     20 | + #endif // ES_APP_MAIN_MENU_AUTH_H
```

Conferência: 1 trechos, 20 linhas adicionadas e 0 removidas.
