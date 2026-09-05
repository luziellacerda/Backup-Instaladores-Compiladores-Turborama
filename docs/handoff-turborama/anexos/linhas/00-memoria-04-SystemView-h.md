# 00-memoria: TurboramaEmulationStation/es-app/src/views/SystemView.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Campos e métodos da tela de sistemas. As condições de compilação precisam acompanhar os respectivos usos no .cpp.

- Antes: `6f6b8b8372610fc2abe1e137d99a48c3ec52412e`.
- Depois: `0e02780b761cb488c591416d2986130efcc166dd`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 32, depois 32

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.h#L32) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.h#L32)

```text
ANTES | DEPOIS |   CÓDIGO
   32 |     32 |   	std::vector<GuiComponent*> backgroundExtras;
   33 |     33 |   	std::shared_ptr<VideoVlcComponent> frontCarouselVideo;
   34 |     34 |   	std::string frontCarouselVideoPath;
      |     35 | + 	std::string frontCarouselVideoConfiguredPath;
      |     36 | + 	long long frontCarouselVideoCheckedAt = 0;
   35 |     37 |   };
   36 |     38 |   
   37 |     39 |   
```

## Trecho 2: antes 151, depois 153

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.h#L151) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.h#L153)

```text
ANTES | DEPOIS |   CÓDIGO
  151 |    153 |   
  152 |    154 |   	std::vector<GuiComponent*>			mStaticBackgrounds;
  153 |    155 |   	std::vector<SystemViewData>			mEntries;
      |    156 | + 	std::vector<int>					mFrontCarouselActiveVideoIndices;
  154 |    157 |   	int									mFrontCarouselMaxVisible;
      |    158 | + 	int									mFrontCarouselSyncedCursor;
      |    159 | + 	int									mFrontCarouselSyncedCount;
      |    160 | + 	int									mFrontCarouselSyncedEntryCount;
      |    161 | + 	std::string							mFrontCarouselSyncedMode;
      |    162 | + 	bool								mFrontCarouselSyncValid;
  155 |    163 |   	bool								mFrontCarouselVideoModeDirty;
  156 |    164 |   	bool								mFrontCarouselVideoModePreview;
  157 |    165 |   
```

Conferência: 2 trechos, 8 linhas adicionadas e 0 removidas.

