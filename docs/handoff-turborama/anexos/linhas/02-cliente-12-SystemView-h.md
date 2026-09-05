# 02-cliente: TurboramaEmulationStation/es-app/src/views/SystemView.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Campos e métodos da tela de sistemas. As condições de compilação precisam acompanhar os respectivos usos no .cpp.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 13, depois 13

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.h#L13) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.h#L13)

```text
ANTES | DEPOIS |   CÓDIGO
   13 |     13 |   #include "components/ImageGridComponent.h"
   14 |     14 |   #include "components/ImageComponent.h"
   15 |     15 |   #include "resources/TextureDataManager.h"
      |     16 | + #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
   16 |     17 |   #include "PixBridge.h"
      |     18 | + #endif
   17 |     19 |   
   18 |     20 |   #include <memory>
   19 |     21 |   #include <functional>
```

## Trecho 2: antes 117, depois 119

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.h#L117) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.h#L119)

```text
ANTES | DEPOIS |   CÓDIGO
  117 |    119 |   	void	 renderCarousel(const Transform4x4f& parentTrans);
  118 |    120 |   	void	 renderExtras(const Transform4x4f& parentTrans, float lower, float upper);
  119 |    121 |   	void	 renderInfoBar(const Transform4x4f& trans);
      |    122 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  120 |    123 |   	void	 updateHomePix(int deltaTime);
  121 |    124 |   	void	 startHomePixRequest();
  122 |    125 |   	void	 pollHomePixRequest();
```

## Trecho 3: antes 126, depois 129

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.h#L126) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.h#L129)

```text
ANTES | DEPOIS |   CÓDIGO
  126 |    129 |   	void	 renderHomePix(const Transform4x4f& trans);
  127 |    130 |   	void	 renderHomePixQrMatrix(const Transform4x4f& trans);
  128 |    131 |   	std::string formatHomePixOffer(const PixPackage& package) const;
      |    132 | + 	#endif
  129 |    133 |   	
  130 |    134 |   	ControlWrapper						mCarousel;
  131 |    135 |   
  132 |    136 |   	TextComponent						mSystemInfo;
      |    137 | + 	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
  133 |    138 |   	ImageComponent						mHomePixQrImage;
  134 |    139 |   	TextComponent						mHomePixOffer;
  135 |    140 |   	TextComponent						mHomePixInstruction;
```

## Trecho 4: antes 150, depois 155

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/views/SystemView.h#L150) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.h#L155)

```text
ANTES | DEPOIS |   CÓDIGO
  150 |    155 |   	int								mHomePixEffectElapsedMs;
  151 |    156 |   	bool							mHomePixRequestActive;
  152 |    157 |   	bool							mHomePixQrReady;
      |    158 | + 	#endif
  153 |    159 |   
  154 |    160 |   	std::vector<GuiComponent*>			mStaticBackgrounds;
  155 |    161 |   	std::vector<SystemViewData>			mEntries;
```

Conferência: 4 trechos, 6 linhas adicionadas e 0 removidas.

