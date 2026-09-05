# 00-memoria: TurboramaEmulationStation/es-core/src/components/CarouselComponent.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Estado e métodos do carrossel que dão suporte aos limites, à visibilidade e ao reuso.

- Antes: `6f6b8b8372610fc2abe1e137d99a48c3ec52412e`.
- Depois: `0e02780b761cb488c591416d2986130efcc166dd`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 95, depois 95

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.h#L95) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.h#L95)

```text
ANTES | DEPOIS |   CÓDIGO
   95 |     95 |   
   96 |     96 |   	void		add(const std::string& name, IBindable* obj, bool preloadLogo = false);
   97 |     97 |   	IBindable*	getActiveObject();
      |     98 | + 	bool		remove(IBindable* obj);
      |     99 | + 	void		clear() override;
   98 |    100 |   
   99 |    101 |   	inline void setCursorChangedCallback(const std::function<void(CursorState state)>& func) { mCursorChangedCallback = func; }
  100 |    102 |   	void	applyTheme(const std::shared_ptr<ThemeData>& theme, const std::string& view, const std::string& element, unsigned int properties);
```

## Trecho 2: antes 129, depois 131

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.h#L129) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.h#L131)

```text
ANTES | DEPOIS |   CÓDIGO
  129 |    131 |   	void prepareCellVideo(IList<CarouselComponentData, IBindable*>::Entry& entry);
  130 |    132 |   	void releaseCellVideo(CarouselComponentData& data);
  131 |    133 |   	void stopCellVideo();
      |    134 | + 	std::shared_ptr<VideoComponent> acquireCellVideo();
      |    135 | + 	void trimCellVideoPool();
      |    136 | + 	size_t getCellVideoPoolLimit() const;
  132 |    137 |   
  133 |    138 |   	void renderCarousel(const Transform4x4f& parentTrans);	
  134 |    139 |   	
```

## Trecho 3: antes 178, depois 183

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.h#L178) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.h#L183)

```text
ANTES | DEPOIS |   CÓDIGO
  178 |    183 |   
  179 |    184 |   	bool			mCellVideoEnabled;
  180 |    185 |   	bool			mCellVideoFoldersOnly;
  181 |        | - 	bool			mCellVideoAudio;
  182 |    186 |   	float			mCellVideoDelay;
  183 |    187 |   	float			mCellVideoRoundCorners;
  184 |    188 |   	Vector2f		mCellVideoSize;
      |    189 | + 	std::vector<std::shared_ptr<VideoComponent>> mCellVideoPool;
      |    190 | + 	std::vector<int> mActiveCellVideoIndices;
      |    191 | + 	int mActiveCellVideoCount;
  185 |    192 |   
  186 |    193 |   	// Mouse support
  187 |    194 |   	int				mPressedCursor;
```

Conferência: 3 trechos, 8 linhas adicionadas e 1 removidas.

