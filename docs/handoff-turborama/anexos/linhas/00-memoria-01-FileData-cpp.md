# 00-memoria: TurboramaEmulationStation/es-app/src/FileData.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Dados e metadados do jogo; cache de mídia e sequência de preparação, execução e retorno do emulador. Leia os capítulos de memória e da variante correspondente.

- Antes: `6f6b8b8372610fc2abe1e137d99a48c3ec52412e`.
- Depois: `0e02780b761cb488c591416d2986130efcc166dd`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 37, depois 37

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.cpp#L37) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.cpp#L37)

```text
ANTES | DEPOIS |   CÓDIGO
   37 |     37 |   #include "CreditManager.h"
   38 |     38 |   #include "CreditWarningOverlay.h"
   39 |     39 |   #include <chrono>
      |     40 | + #include <atomic>
   40 |     41 |   
   41 |     42 |   using namespace Utils::Platform;
   42 |     43 |   
   43 |     44 |   namespace
   44 |     45 |   {
      |     46 | + 	// A media mutation can affect a collection wrapper or any ancestor folder,
      |     47 | + 	// not just the FileData that received the metadata update. A single cheap
      |     48 | + 	// generation therefore invalidates all path caches without maintaining a
      |     49 | + 	// second dependency tree. Mutations are rare; reads happen every frame.
      |     50 | + 	std::atomic<std::uint64_t> sCarouselVideoCacheGeneration(1);
      |     51 | + 	// Media can also be copied directly to disk, outside the metadata/tree update
      |     52 | + 	// paths below. Keep negative folder results short-lived without putting any
      |     53 | + 	// filesystem probes back in the per-frame render path.
      |     54 | + 	const long long CAROUSEL_VIDEO_CACHE_TTL_MS = 5000;
      |     55 | + 
      |     56 | + 	long long carouselVideoCacheNow()
      |     57 | + 	{
      |     58 | + 		return std::chrono::duration_cast<std::chrono::milliseconds>(
      |     59 | + 			std::chrono::steady_clock::now().time_since_epoch()).count();
      |     60 | + 	}
      |     61 | + 
   45 |     62 |   #ifdef WIN32
   46 |     63 |   	// Native, non-activating warning used while an external emulator owns the
   47 |     64 |   	// screen. The regular EmulationStation notification cannot be rendered while
```

## Trecho 2: antes 377, depois 394

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.cpp#L377) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.cpp#L394)

```text
ANTES | DEPOIS |   CÓDIGO
  377 |    394 |   	mMetadata.resetChangedFlag();
  378 |    395 |   }
  379 |    396 |   
      |    397 | + void FileData::setParent(FolderData* parent)
      |    398 | + {
      |    399 | + 	if (mParent == parent)
      |    400 | + 		return;
      |    401 | + 
      |    402 | + 	mParent = parent;
      |    403 | + 	invalidateCarouselVideoPathCache();
      |    404 | + }
      |    405 | + 
      |    406 | + void FileData::setMetadata(MetaDataList value)
      |    407 | + {
      |    408 | + 	getMetadata() = value;
      |    409 | + 	invalidateCarouselVideoPathCache();
      |    410 | + }
      |    411 | + 
      |    412 | + void FileData::setMetadata(MetaDataId key, const std::string& value)
      |    413 | + {
      |    414 | + 	MetaDataList& metadata = getMetadata();
      |    415 | + 	metadata.set(key, value);
      |    416 | + 
      |    417 | + 	// Treat setting the same path as an update too: scrapers/importers may replace
      |    418 | + 	// the file contents in place and then write the unchanged metadata value.
      |    419 | + 	if (key == MetaDataId::Video)
      |    420 | + 		invalidateCarouselVideoPathCache();
      |    421 | + }
      |    422 | + 
      |    423 | + void FileData::invalidateCarouselVideoPathCache()
      |    424 | + {
      |    425 | + 	mCarouselVideoPathCacheValid = false;
      |    426 | + 	sCarouselVideoCacheGeneration.fetch_add(1, std::memory_order_relaxed);
      |    427 | + }
      |    428 | + 
  380 |    429 |   const std::string FileData::getPath() const
  381 |    430 |   {
  382 |    431 |   	if (mPath.empty())
```

## Trecho 3: antes 661, depois 710

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.cpp#L661) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.cpp#L710)

```text
ANTES | DEPOIS |   CÓDIGO
  661 |    710 |   
  662 |    711 |   const std::string FileData::getCarouselVideoPath()
  663 |    712 |   {
      |    713 | + 	return resolveCarouselVideoPath(false);
      |    714 | + }
      |    715 | + 
      |    716 | + const std::string FileData::resolveCarouselVideoPath(bool forceRefresh)
      |    717 | + {
      |    718 | + 	const long long now = carouselVideoCacheNow();
      |    719 | + 	const std::uint64_t generation =
      |    720 | + 		sCarouselVideoCacheGeneration.load(std::memory_order_relaxed);
      |    721 | + 	const std::string configuredVideo = getMetadata(MetaDataId::Video);
      |    722 | + 
      |    723 | + 	if (!forceRefresh && mCarouselVideoPathCacheValid &&
      |    724 | + 		mCarouselVideoCacheGeneration == generation &&
      |    725 | + 		mCarouselVideoMetadataPathCache == configuredVideo &&
      |    726 | + 		(!mCarouselVideoPathCache.empty() ||
      |    727 | + 			now - mCarouselVideoCacheCheckedAt < CAROUSEL_VIDEO_CACHE_TTL_MS))
      |    728 | + 		return mCarouselVideoPathCache;
      |    729 | + 
      |    730 | + 	std::string resolvedVideo;
  664 |    731 |   	const std::string directVideo = getVideoPath();
  665 |        | - 	if (!directVideo.empty() && Utils::FileSystem::exists(directVideo))
  666 |        | - 		return directVideo;
      |    732 | + 	if (!directVideo.empty() && Utils::FileSystem::exists(directVideo, false))
      |    733 | + 		resolvedVideo = directVideo;
  667 |    734 |   
  668 |    735 |   	auto findVideoByRomPath = [](FileData* game, const std::string& configuredVideo)
  669 |    736 |   	{
```

## Trecho 4: antes 680, depois 747

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.cpp#L680) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.cpp#L747)

```text
ANTES | DEPOIS |   CÓDIGO
  680 |    747 |   			for (const auto& extension : extensions)
  681 |    748 |   			{
  682 |    749 |   				const std::string candidate = configuredParent + "/" + romStem + extension;
  683 |        | - 				if (Utils::FileSystem::exists(candidate))
      |    750 | + 				if (Utils::FileSystem::exists(candidate, false))
  684 |    751 |   					return candidate;
  685 |    752 |   			}
  686 |    753 |   		}
```

## Trecho 5: antes 697, depois 764

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.cpp#L697) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.cpp#L764)

```text
ANTES | DEPOIS |   CÓDIGO
  697 |    764 |   		for (const auto& extension : extensions)
  698 |    765 |   		{
  699 |    766 |   			const std::string candidate = systemRoot + "/media/videos/" + relativeStem + extension;
  700 |        | - 			if (Utils::FileSystem::exists(candidate))
      |    767 | + 			if (Utils::FileSystem::exists(candidate, false))
  701 |    768 |   				return candidate;
  702 |    769 |   		}
  703 |    770 |   
  704 |    771 |   		return std::string();
  705 |    772 |   	};
  706 |    773 |   
  707 |        | - 	if (getType() == GAME)
  708 |        | - 		return findVideoByRomPath(this, directVideo);
  709 |        | - 
  710 |        | - 	if (getType() != FOLDER)
  711 |        | - 		return "";
  712 |        | - 
  713 |        | - 	// Folder carousel cells work identically for every system. If the folder has
  714 |        | - 	// no media of its own, use the first valid video from any descendant game.
  715 |        | - 	for (auto game : ((FolderData*)this)->getFilesRecursive(GAME, false, nullptr, false))
      |    774 | + 	if (resolvedVideo.empty() && getType() == GAME)
      |    775 | + 		resolvedVideo = findVideoByRomPath(this, directVideo);
      |    776 | + 	else if (resolvedVideo.empty() && getType() == FOLDER)
  716 |    777 |   	{
  717 |        | - 		const std::string video = game->getVideoPath();
  718 |        | - 		if (!video.empty() && Utils::FileSystem::exists(video))
  719 |        | - 			return video;
  720 |        | - 
  721 |        | - 		const std::string videoByRom = findVideoByRomPath(game, video);
  722 |        | - 		if (!videoByRom.empty())
  723 |        | - 			return videoByRom;
      |    778 | + 		// A folder refresh also refreshes each consulted child, avoiding staggered
      |    779 | + 		// parent/child TTLs that could otherwise keep a changed disk file stale for
      |    780 | + 		// two cache intervals. Normal frame reads still hit both cache levels.
      |    781 | + 		for (auto game : ((FolderData*)this)->getFilesRecursive(GAME, false, nullptr, false))
      |    782 | + 		{
      |    783 | + 			resolvedVideo = game->resolveCarouselVideoPath(true);
      |    784 | + 			if (!resolvedVideo.empty())
      |    785 | + 				break;
      |    786 | + 		}
  724 |    787 |   	}
  725 |    788 |   
  726 |        | - 	return "";
      |    789 | + 	mCarouselVideoPathCache = resolvedVideo;
      |    790 | + 	// getVideoPath() may discover local art and update Video metadata while this
      |    791 | + 	// lookup is in progress, so capture both values after resolution completes.
      |    792 | + 	mCarouselVideoMetadataPathCache = getMetadata(MetaDataId::Video);
      |    793 | + 	mCarouselVideoCacheGeneration =
      |    794 | + 		sCarouselVideoCacheGeneration.load(std::memory_order_relaxed);
      |    795 | + 	mCarouselVideoCacheCheckedAt = now;
      |    796 | + 	mCarouselVideoPathCacheValid = true;
      |    797 | + 	return mCarouselVideoPathCache;
  727 |    798 |   }
  728 |    799 |   
  729 |    800 |   const std::string FileData::getMarqueePath()
```

## Trecho 6: antes 1852, depois 1923

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.cpp#L1852) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.cpp#L1923)

```text
ANTES | DEPOIS |   CÓDIGO
 1852 |   1923 |   	mChildren.push_back(file);
 1853 |   1924 |   
 1854 |   1925 |   	if (assignParent)
 1855 |        | - 		file->setParent(this);	
      |   1926 | + 		file->setParent(this);
      |   1927 | + 	else
      |   1928 | + 		invalidateCarouselVideoPathCache();
 1856 |   1929 |   }
 1857 |   1930 |   
 1858 |   1931 |   void FolderData::removeChild(FileData* file)
```

## Trecho 7: antes 2304, depois 2377

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.cpp#L2304) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.cpp#L2377)

```text
ANTES | DEPOIS |   CÓDIGO
 2304 |   2377 |   }
 2305 |   2378 |   
 2306 |   2379 |   void FolderData::clear() {
      |   2380 | + 	const bool hadChildren = !mChildren.empty();
 2307 |   2381 |   	if (mOwnsChildrens)
 2308 |   2382 |   		for (auto* child : mChildren)
 2309 |   2383 |   		{
```

## Trecho 8: antes 2311, depois 2385

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.cpp#L2311) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.cpp#L2385)

```text
ANTES | DEPOIS |   CÓDIGO
 2311 |   2385 |   			delete child;
 2312 |   2386 |   		}
 2313 |   2387 |   	mChildren.clear();
      |   2388 | + 	if (hadChildren)
      |   2389 | + 		invalidateCarouselVideoPathCache();
 2314 |   2390 |   }
 2315 |   2391 |   
 2316 |   2392 |   void FolderData::removeFromVirtualFolders(FileData* game)
```

## Trecho 9: antes 2326, depois 2402

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/FileData.cpp#L2326) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/FileData.cpp#L2402)

```text
ANTES | DEPOIS |   CÓDIGO
 2326 |   2402 |   		if ((*it) == game)
 2327 |   2403 |   		{
 2328 |   2404 |   			mChildren.erase(it);
      |   2405 | + 			invalidateCarouselVideoPathCache();
 2329 |   2406 |   			return;
 2330 |   2407 |   		}
 2331 |   2408 |   	}
```

Conferência: 9 trechos, 99 linhas adicionadas e 22 removidas.

