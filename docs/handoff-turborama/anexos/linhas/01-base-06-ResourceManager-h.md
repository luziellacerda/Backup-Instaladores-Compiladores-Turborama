# 01-base: TurboramaEmulationStation/es-core/src/resources/ResourceManager.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Declaração pública da invalidação de caminhos usada na inicialização.

- Antes: `0e02780b761cb488c591416d2986130efcc166dd`.
- Depois: `76b214874973fe24017823401216896f3d7a6f40`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/resources/ResourceManager.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 30, depois 30

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/resources/ResourceManager.h#L30) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/resources/ResourceManager.h#L30)

```text
ANTES | DEPOIS |   CÓDIGO
   30 |     30 |   {
   31 |     31 |   public:
   32 |     32 |   	static std::shared_ptr<ResourceManager>& getInstance();
      |     33 | + 	static void invalidatePathCache();
   33 |     34 |   
   34 |     35 |   	void addReloadable(std::weak_ptr<IReloadable> reloadable);
   35 |     36 |   	void removeReloadable(std::weak_ptr<IReloadable> reloadable);
```

## Trecho 2: antes 46, depois 47

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/resources/ResourceManager.h#L46) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/resources/ResourceManager.h#L47)

```text
ANTES | DEPOIS |   CÓDIGO
   46 |     47 |   private:
   47 |     48 |   	ResourceManager();
   48 |     49 |   
   49 |        | - 	static std::shared_ptr<ResourceManager> sInstance;
   50 |        | - 
   51 |     50 |   	ResourceData loadFile(const std::string& path, size_t size) const;
   52 |     51 |   
   53 |     52 |   	class ReloadableInfo
```

Conferência: 2 trechos, 1 linhas adicionadas e 2 removidas.

