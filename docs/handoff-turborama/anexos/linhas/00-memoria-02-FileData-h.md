# 00-memoria: TurboramaEmulationStation/es-app/src/FileData.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Contrato público e estado privado de FileData: declarações do cache e das funções consumidas pela interface.

- Antes: `6f6b8b8372610fc2abe1e137d99a48c3ec52412e`.
- Depois: `0e02780b761cb488c591416d2986130efcc166dd`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 9, depois 9

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.h#L9) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.h#L9)

```text
ANTES | DEPOIS |   CÓDIGO
    9 |      9 |   #include <memory>
   10 |     10 |   #include <vector>
   11 |     11 |   #include <stack>
      |     12 | + #include <cstdint>
   12 |     13 |   #include "KeyboardMapping.h"
   13 |     14 |   #include "SystemData.h"
   14 |     15 |   #include "SaveState.h"
```

## Trecho 2: antes 86, depois 87

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.h#L86) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.h#L87)

```text
ANTES | DEPOIS |   CÓDIGO
   86 |     87 |   	inline FileType getType() const { return mType; }
   87 |     88 |   	
   88 |     89 |   	inline FolderData* getParent() const { return mParent; }
   89 |        | - 	inline void setParent(FolderData* parent) { mParent = parent; }
      |     90 | + 	void setParent(FolderData* parent);
   90 |     91 |   
   91 |     92 |   	inline SystemData* getSystem() const { return mSystem; }
   92 |     93 |   
```

## Trecho 3: antes 150, depois 151

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.h#L150) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.h#L151)

```text
ANTES | DEPOIS |   CÓDIGO
  150 |    151 |   	virtual const MetaDataList& getMetadata() const { return mMetadata; }
  151 |    152 |   	virtual MetaDataList& getMetadata() { return mMetadata; }
  152 |    153 |   
  153 |        | - 	void setMetadata(MetaDataList value) { getMetadata() = value; } 
      |    154 | + 	void setMetadata(MetaDataList value);
  154 |    155 |   	
  155 |    156 |   	std::string getMetadata(MetaDataId key) const { return getMetadata().get(key); }
  156 |        | - 	void setMetadata(MetaDataId key, const std::string& value) { return getMetadata().set(key, value); }
      |    157 | + 	void setMetadata(MetaDataId key, const std::string& value);
  157 |    158 |   
  158 |    159 |   	void detectLanguageAndRegion(bool overWrite);
  159 |    160 |   
```

## Trecho 4: antes 192, depois 193

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.h#L192) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.h#L193)

```text
ANTES | DEPOIS |   CÓDIGO
  192 |    193 |   private:
  193 |    194 |   	std::string getKeyboardMappingFilePath();
  194 |    195 |   	std::string getMessageFromExitCode(int exitCode);
      |    196 | + 	const std::string resolveCarouselVideoPath(bool forceRefresh);
  195 |    197 |   	MetaDataList mMetadata;
  196 |    198 |   
  197 |    199 |   protected:	
  198 |    200 |   	std::string  findLocalArt(const std::string& type = "", std::vector<std::string> exts = { ".png", ".jpg" });
      |    201 | + 	void invalidateCarouselVideoPathCache();
  199 |    202 |   
  200 |    203 |   	static FileData* mRunningGame;
  201 |    204 |   
```

## Trecho 5: antes 204, depois 207

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.h#L204) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.h#L207)

```text
ANTES | DEPOIS |   CÓDIGO
  204 |    207 |   	FileType mType;
  205 |    208 |   	SystemData* mSystem;
  206 |    209 |   	std::string* mDisplayName;
      |    210 | + 
      |    211 | + 	// Resolving a folder video can walk every descendant and probe several media
      |    212 | + 	// layouts. Successful lookups remain cached until metadata/tree generation
      |    213 | + 	// changes; only negative results expire, allowing newly copied media to appear
      |    214 | + 	// without putting filesystem probes back in the render loop.
      |    215 | + 	std::string mCarouselVideoPathCache;
      |    216 | + 	std::string mCarouselVideoMetadataPathCache;
      |    217 | + 	std::uint64_t mCarouselVideoCacheGeneration = 0;
      |    218 | + 	long long mCarouselVideoCacheCheckedAt = 0;
      |    219 | + 	bool mCarouselVideoPathCacheValid = false;
  207 |    220 |   };
  208 |    221 |   
  209 |    222 |   class CollectionFileData : public FileData
```

Conferência: 5 trechos, 16 linhas adicionadas e 3 removidas.

