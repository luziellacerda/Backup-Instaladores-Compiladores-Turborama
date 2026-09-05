# 01-base: TurboramaEmulationStation/es-core/src/resources/ResourceManager.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Resolução de caminhos dos recursos e invalidação do cache após disponibilizar o tema.

- Antes: `0e02780b761cb488c591416d2986130efcc166dd`.
- Depois: `76b214874973fe24017823401216896f3d7a6f40`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/resources/ResourceManager.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 14, depois 14

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/resources/ResourceManager.cpp#L14) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/resources/ResourceManager.cpp#L14)

```text
ANTES | DEPOIS |   CÓDIGO
   14 |     14 |   
   15 |     15 |   auto array_deleter = [](unsigned char* p) { delete[] p; };
   16 |     16 |   
   17 |        | - std::shared_ptr<ResourceManager> ResourceManager::sInstance = nullptr;
   18 |        | - 
   19 |     17 |   ResourceManager::ResourceManager()
   20 |     18 |   {
   21 |     19 |   }
   22 |     20 |   
   23 |     21 |   std::shared_ptr<ResourceManager>& ResourceManager::getInstance()
   24 |     22 |   {
   25 |        | - 	if (!sInstance)
   26 |        | - 		sInstance = std::shared_ptr<ResourceManager>(new ResourceManager());
   27 |        | - 
   28 |        | - 	return sInstance;
      |     23 | + 	static std::shared_ptr<ResourceManager> instance(new ResourceManager());
      |     24 | + 	return instance;
   29 |     25 |   }
   30 |     26 |   
   31 |     27 |   static std::mutex                                 _cacheBuildLock;
```

## Trecho 2: antes 33, depois 29

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/resources/ResourceManager.cpp#L33) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/resources/ResourceManager.cpp#L29)

```text
ANTES | DEPOIS |   CÓDIGO
   33 |     29 |   static std::string                                _cachedThemeSet;
   34 |     30 |   static ConcurrentMap<std::string, std::string>    _resourcePathCache;
   35 |     31 |   
      |     32 | + void ResourceManager::invalidatePathCache()
      |     33 | + {
      |     34 | + 	std::unique_lock<std::mutex> lock(_cacheBuildLock);
      |     35 | + 	_cachedPaths.clear();
      |     36 | + 	_cachedThemeSet.clear();
      |     37 | + 	_resourcePathCache.clear();
      |     38 | + }
      |     39 | + 
   36 |     40 |   std::vector<std::string> ResourceManager::getResourcePaths() const
   37 |     41 |   {
   38 |     42 |   	auto themeSet = Settings::getInstance()->getString("ThemeSet");
```

Conferência: 2 trechos, 10 linhas adicionadas e 6 removidas.

