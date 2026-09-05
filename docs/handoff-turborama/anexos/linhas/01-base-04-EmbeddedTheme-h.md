# 01-base: TurboramaEmulationStation/es-core/src/EmbeddedTheme.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Interface de inicialização do tema e callback de progresso.

- Antes: `0e02780b761cb488c591416d2986130efcc166dd`.
- Depois: `76b214874973fe24017823401216896f3d7a6f40`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 2, depois 2

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.h#L2) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.h#L2)

```text
ANTES | DEPOIS |   CÓDIGO
    2 |      2 |   #ifndef ES_CORE_EMBEDDED_THEME_H
    3 |      3 |   #define ES_CORE_EMBEDDED_THEME_H
    4 |      4 |   
      |      5 | + #include <functional>
    5 |      6 |   #include <string>
    6 |      7 |   
    7 |      8 |   class EmbeddedTheme
    8 |      9 |   {
    9 |     10 |   public:
      |     11 | + 	using ProgressCallback = std::function<void(float)>;
      |     12 | + 
   10 |     13 |   	static const char* THEME_SET_ID;
   11 |     14 |   
   12 |        | - 	static bool initialize();
      |     15 | + 	static bool initialize(const ProgressCallback& progressCallback = ProgressCallback());
   13 |     16 |   	static bool isAvailable();
   14 |     17 |   	static bool isActiveThemeSet(const std::string& themeSet);
   15 |     18 |   	static std::string getRootPath();
```

## Trecho 2: antes 17, depois 20

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.h#L17) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.h#L20)

```text
ANTES | DEPOIS |   CÓDIGO
   17 |     20 |   	static std::string getResourcesPath();
   18 |     21 |   };
   19 |     22 |   
   20 |        | - #endif // ES_CORE_EMBEDDED_THEME_H
      |     23 | + #endif // ES_CORE_EMBEDDED_THEME_H
```

Conferência: 2 trechos, 5 linhas adicionadas e 2 removidas.

