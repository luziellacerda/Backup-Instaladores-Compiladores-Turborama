# 01-base: TurboramaEmulationStation/tools/tests/Test-EmbeddedThemeBuild.ps1

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Teste automatizado: preparação, execução e asserções com dados sintéticos.

- Antes: `0e02780b761cb488c591416d2986130efcc166dd`.
- Depois: `76b214874973fe24017823401216896f3d7a6f40`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/tools/tests/Test-EmbeddedThemeBuild.ps1).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 17, depois 17

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/tools/tests/Test-EmbeddedThemeBuild.ps1#L17) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/tools/tests/Test-EmbeddedThemeBuild.ps1#L17)

```text
ANTES | DEPOIS |   CÓDIGO
   17 |     17 |   $decoded = Join-Path $testRoot 'decoded.zip'
   18 |     18 |   $extracted = Join-Path $testRoot 'extracted'
   19 |     19 |   $runtimeSource = Join-Path $projectRoot 'es-core\src\EmbeddedTheme.cpp'
      |     20 | + $mainSource = Join-Path $projectRoot 'es-app\src\main.cpp'
      |     21 | + $resourceManagerSource = Join-Path $projectRoot 'es-core\src\resources\ResourceManager.cpp'
   20 |     22 |   
   21 |     23 |   try {
   22 |     24 |       [IO.Directory]::CreateDirectory((Join-Path $theme 'nested')) | Out-Null
```

## Trecho 2: antes 61, depois 63

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/tools/tests/Test-EmbeddedThemeBuild.ps1#L61) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/tools/tests/Test-EmbeddedThemeBuild.ps1#L63)

```text
ANTES | DEPOIS |   CÓDIGO
   61 |     63 |   	# resultado negativo memorizado pelo FileSystemCache.
   62 |     64 |   	$runtimeText = [IO.File]::ReadAllText($runtimeSource)
   63 |     65 |   	foreach ($requiredCheck in @(
   64 |        | - 		'exists(tempZip, false)',
   65 |        | - 		'exists(targetPath + "/theme.xml", false)',
   66 |        | - 		'exists(cachedPath + "/theme.xml", false)',
   67 |        | - 		'exists(markerPath, false)',
   68 |        | - 		'exists(extractPath + "/theme.xml", false)'
      |     66 | + 		'sInitializationAttempted',
      |     67 | + 		'ScopedThemeCacheLock',
      |     68 | + 		'pruneObsoleteThemeCaches(cacheRoot, payload.identity, progressCallback)',
      |     69 | + 		'hasEnoughFreeSpace(cacheRoot, static_cast<std::uint64_t>(archiveSize), "theme archive creation")',
      |     70 | + 		'hasEnoughFreeSpace(cacheRoot, uncompressedBytes, "theme extraction")',
      |     71 | + 		'treeContainsReparsePoint',
      |     72 | + 		'isValidCacheDirectory',
      |     73 | + 		'FileSystemCache::reset()',
      |     74 | + 		'exists(themePath, false)',
      |     75 | + 		'getFileSize(markerPath) != sPayloadIdentityLength',
      |     76 | + 		'Utils::FileSystem::readAllText(markerPath) != payload.identity',
      |     77 | + 		'return sAvailable.load(std::memory_order_acquire)'
   69 |     78 |   	)) {
   70 |     79 |   		Assert ($runtimeText.Contains($requiredCheck)) "EmbeddedTheme voltou a usar cache obsoleto em: $requiredCheck"
   71 |     80 |   	}
      |     81 | + 	Assert (-not $runtimeText.Contains('deleteDirectoryFiles(')) 'A limpeza do tema voltou a seguir diretorios recursivamente sem validar reparse points.'
      |     82 | + 	$isAvailableBody = [regex]::Match($runtimeText, 'bool EmbeddedTheme::isAvailable\(\)\s*\{(?<body>.*?)\}', [Text.RegularExpressions.RegexOptions]::Singleline)
      |     83 | + 	Assert ($isAvailableBody.Success -and -not $isAvailableBody.Groups['body'].Value.Contains('initialize(')) 'isAvailable voltou a iniciar a extracao de forma implicita.'
   72 |     84 |   
   73 |        | - 	Write-Host "OK: payload deterministico, cabecalho/hash, extracao e guardas de cache validados ($firstHash)."
      |     85 | + 	$mainText = [IO.File]::ReadAllText($mainSource)
      |     86 | + 	$windowIndex = $mainText.IndexOf('window.init(true, false)')
      |     87 | + 	$themeIndex = $mainText.IndexOf('EmbeddedTheme::initialize(')
      |     88 | + 	$poolIndex = $mainText.IndexOf('threadPool->start()')
      |     89 | + 	$configIndex = $mainText.IndexOf('loadSystemConfigFile(', $themeIndex)
      |     90 | + 	$preloadIndex = $mainText.IndexOf('ViewController::get()->preload()', $themeIndex)
      |     91 | + 	Assert ($windowIndex -ge 0 -and $windowIndex -lt $themeIndex) 'O tema precisa iniciar somente depois da janela.'
      |     92 | + 	Assert ($themeIndex -lt $poolIndex -and $themeIndex -lt $configIndex -and $themeIndex -lt $preloadIndex) 'O tema precisa estar pronto antes dos workers, sistemas e preload.'
      |     93 | + 
      |     94 | + 	$resourceManagerText = [IO.File]::ReadAllText($resourceManagerSource)
      |     95 | + 	Assert ($resourceManagerText.Contains('void ResourceManager::invalidatePathCache()')) 'A invalidacao do cache de recursos foi removida.'
      |     96 | + 
      |     97 | + 	Write-Host "OK: payload deterministico, cabecalho/hash, extracao, ordem de inicializacao e guardas de cache validados ($firstHash)."
   74 |     98 |   }
   75 |     99 |   finally {
   76 |    100 |       if (Test-Path -LiteralPath $testRoot -PathType Container) {
```

Conferência: 2 trechos, 30 linhas adicionadas e 6 removidas.

