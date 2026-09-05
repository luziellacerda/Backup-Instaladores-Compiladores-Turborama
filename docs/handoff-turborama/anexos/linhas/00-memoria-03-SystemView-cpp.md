# 00-memoria: TurboramaEmulationStation/es-app/src/views/SystemView.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Tela de sistemas: seleção, carrossel, ciclo de vida dos vídeos, atualização visual e, na PIX, integrações de serviços.

- Antes: `6f6b8b8372610fc2abe1e137d99a48c3ec52412e`.
- Depois: `0e02780b761cb488c591416d2986130efcc166dd`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 32, depois 32

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L32) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L32)

```text
ANTES | DEPOIS |   CÓDIGO
   32 |     32 |   #include "utils/FileSystemUtil.h"
   33 |     33 |   
   34 |     34 |   #include <cmath>
      |     35 | + #include <chrono>
   35 |     36 |   #include <iomanip>
   36 |     37 |   #include <sstream>
   37 |     38 |   
   38 |     39 |   namespace
   39 |     40 |   {
   40 |     41 |   	const char* FRONT_CAROUSEL_VIDEO_TAG = "video_celula_ativa_v2";
      |     42 | + 	const char* FRONT_BASE_BACKGROUND_VIDEO_TAG = "background_movie";
      |     43 | + 	const char* FRONT_ANIMATED_BACKGROUND_VIDEO_TAG = "default_background";
      |     44 | + 	const long long FRONT_VIDEO_CACHE_TTL_MS = 5000;
      |     45 | + 
      |     46 | + 	long long frontVideoCacheNow()
      |     47 | + 	{
      |     48 | + 		return std::chrono::duration_cast<std::chrono::milliseconds>(
      |     49 | + 			std::chrono::steady_clock::now().time_since_epoch()).count();
      |     50 | + 	}
   41 |     51 |   
   42 |     52 |   	std::string resolveFrontCarouselVideoPath(SystemData* system, const std::string& configuredPath)
   43 |     53 |   	{
```

## Trecho 2: antes 45, depois 55

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L45) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L55)

```text
ANTES | DEPOIS |   CÓDIGO
   45 |     55 |   			return configuredPath;
   46 |     56 |   
   47 |     57 |   		if (system == nullptr || system->getTheme() == nullptr)
   48 |        | - 			return configuredPath;
      |     58 | + 			return "";
   49 |     59 |   
   50 |     60 |   		std::string mediaName = system->getThemeFolder();
   51 |     61 |   		if (mediaName == "fbneo")
```

## Trecho 3: antes 55, depois 65

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L55) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L65)

```text
ANTES | DEPOIS |   CÓDIGO
   55 |     65 |   		else if (mediaName == "saturn")
   56 |     66 |   			mediaName = "saturno";
   57 |     67 |   		else
   58 |        | - 			return configuredPath;
      |     68 | + 			return "";
   59 |     69 |   
   60 |     70 |   		const std::string themeRoot = system->getTheme()->getVariable("themePath");
   61 |     71 |   		const std::string mediaRelativePath = system->getTheme()->getVariable("theme.caratulasPath");
   62 |     72 |   		if (themeRoot.empty() || mediaRelativePath.empty())
   63 |        | - 			return configuredPath;
      |     73 | + 			return "";
   64 |     74 |   
   65 |     75 |   		const std::string mediaFolder = Utils::FileSystem::resolveRelativePath(
   66 |     76 |   			mediaRelativePath, themeRoot, true);
```

## Trecho 4: antes 72, depois 82

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L72) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L82)

```text
ANTES | DEPOIS |   CÓDIGO
   72 |     82 |   			return aliasPath;
   73 |     83 |   		}
   74 |     84 |   
   75 |        | - 		return configuredPath;
      |     85 | + 		// Cache a negative lookup too.  Theme reload is the invalidation point;
      |     86 | + 		// render must not hit the filesystem once per cell and frame.
      |     87 | + 		return "";
   76 |     88 |   	}
   77 |     89 |   }
   78 |     90 |   
```

## Trecho 5: antes 95, depois 107

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L95) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L107)

```text
ANTES | DEPOIS |   CÓDIGO
   95 |    107 |   	mDisable = false;
   96 |    108 |   	mLastCursor = 0;
   97 |    109 |   	mFrontCarouselMaxVisible = 3;
      |    110 | + 	mFrontCarouselSyncedCursor = -1;
      |    111 | + 	mFrontCarouselSyncedCount = 0;
      |    112 | + 	mFrontCarouselSyncedEntryCount = 0;
      |    113 | + 	mFrontCarouselSyncValid = false;
   98 |    114 |   	mFrontCarouselVideoModeDirty = false;
   99 |    115 |   	mFrontCarouselVideoModePreview = false;
  100 |    116 |   	mExtrasFadeOldCursor = -1;
```

## Trecho 6: antes 159, depois 175

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L159) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L175)

```text
ANTES | DEPOIS |   CÓDIGO
  159 |    175 |   	}
  160 |    176 |   
  161 |    177 |   	mEntries.clear();
      |    178 | + 	mFrontCarouselActiveVideoIndices.clear();
      |    179 | + 	mFrontCarouselSyncValid = false;
      |    180 | + 	mFrontCarouselSyncedCursor = -1;
      |    181 | + 	mFrontCarouselSyncedCount = 0;
      |    182 | + 	mFrontCarouselSyncedEntryCount = 0;
      |    183 | + 	mFrontCarouselSyncedMode.clear();
  162 |    184 |   	mCarousel.clear();
  163 |    185 |   }
  164 |    186 |   
```

## Trecho 7: antes 222, depois 244

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L222) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L244)

```text
ANTES | DEPOIS |   CÓDIGO
  222 |    244 |   	// from the normal extras so it can be parented to its own animated system cell
  223 |    245 |   	// instead of being rendered as a fixed screen overlay.
  224 |    246 |   	auto themedExtras = ThemeData::makeExtras(system->getTheme(), "system", mWindow);
      |    247 | + 
      |    248 | + 	// The TURBORAMA front layout supplies background_movie as its full-screen
      |    249 | + 	// base.  The optional animated-background include can resolve
      |    250 | + 	// default_background to that exact same movie and placement, which otherwise
      |    251 | + 	// starts a second VLC decoder just to draw the same frames again.  Find the
      |    252 | + 	// base up front so the duplicate can be discarded regardless of XML order.
      |    253 | + 	VideoVlcComponent* baseBackgroundVideo = nullptr;
      |    254 | + 	std::string baseBackgroundVideoPath;
      |    255 | + 	for (GuiComponent* extra : themedExtras)
      |    256 | + 	{
      |    257 | + 		if (extra->getTag() != FRONT_BASE_BACKGROUND_VIDEO_TAG ||
      |    258 | + 			!extra->isKindOf<VideoVlcComponent>() || !extra->isVisible() ||
      |    259 | + 			extra->getOpacity() != 0xFF || extra->hasStoryBoard())
      |    260 | + 			continue;
      |    261 | + 
      |    262 | + 		baseBackgroundVideoPath = extra->getProperty("path").s;
      |    263 | + 		if (!baseBackgroundVideoPath.empty())
      |    264 | + 		{
      |    265 | + 			baseBackgroundVideo = dynamic_cast<VideoVlcComponent*>(extra);
      |    266 | + 			break;
      |    267 | + 		}
      |    268 | + 	}
      |    269 | + 
  225 |    270 |   	std::vector<GuiComponent*> extras;
  226 |    271 |   	std::shared_ptr<VideoVlcComponent> frontCarouselVideo;
  227 |    272 |   	std::string frontCarouselVideoPath;
      |    273 | + 	std::string frontCarouselVideoConfiguredPath;
  228 |    274 |   	for (auto extra : themedExtras)
  229 |    275 |   	{
  230 |    276 |   		if (extra->getTag() == FRONT_CAROUSEL_VIDEO_TAG && extra->isKindOf<VideoVlcComponent>())
  231 |    277 |   		{
      |    278 | + 			frontCarouselVideoConfiguredPath = extra->getProperty("path").s;
  232 |    279 |   			frontCarouselVideoPath = resolveFrontCarouselVideoPath(
  233 |        | - 				system, extra->getProperty("path").s);
      |    280 | + 				system, frontCarouselVideoConfiguredPath);
  234 |    281 |   
  235 |    282 |   			if (frontCarouselVideo == nullptr)
  236 |    283 |   			{
```

## Trecho 8: antes 241, depois 288

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L241) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L288)

```text
ANTES | DEPOIS |   CÓDIGO
  241 |    288 |   				frontCarouselVideo->setVisible(false);
  242 |    289 |   				frontCarouselVideo->setTag("frontSystemCarouselVideo");
  243 |    290 |   				frontCarouselVideo->setPlayAudio(false);
      |    291 | + 				frontCarouselVideo->setConcurrentPlaybackLimit(
      |    292 | + 					Math::max(1, mFrontCarouselMaxVisible));
  244 |    293 |   				frontCarouselVideo->setEffect(VideoVlcFlags::SIZE);
  245 |    294 |   			}
  246 |    295 |   			else
```

## Trecho 9: antes 249, depois 298

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L249) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L298)

```text
ANTES | DEPOIS |   CÓDIGO
  249 |    298 |   			continue;
  250 |    299 |   		}
  251 |    300 |   
      |    301 | + 		if (baseBackgroundVideo != nullptr &&
      |    302 | + 			extra->getTag() == FRONT_ANIMATED_BACKGROUND_VIDEO_TAG &&
      |    303 | + 			extra->isKindOf<VideoVlcComponent>() &&
      |    304 | + 			extra->getProperty("path").s == baseBackgroundVideoPath &&
      |    305 | + 			extra->getPosition() == baseBackgroundVideo->getPosition() &&
      |    306 | + 			extra->getSize() == baseBackgroundVideo->getSize() &&
      |    307 | + 			extra->getOrigin() == baseBackgroundVideo->getOrigin())
      |    308 | + 		{
      |    309 | + 			// Preserve this element's independent z-index, opacity and storyboard,
      |    310 | + 			// but borrow the decoded texture from background_movie.
      |    311 | + 			dynamic_cast<VideoVlcComponent*>(extra)->setSharedVideoSource(baseBackgroundVideo);
      |    312 | + 			LOG(LogDebug) << "[SystemView] sharing duplicate front background decoder for "
      |    313 | + 				<< system->getName() << ": " << baseBackgroundVideoPath;
      |    314 | + 		}
      |    315 | + 
  252 |    316 |   		extras.push_back(extra);
  253 |    317 |   
  254 |    318 |   		if (extra->isKindOf<VideoComponent>())
  255 |    319 |   		{
      |    320 | + 			// Every SystemView video is decorative. Decode no audio here; the normal
      |    321 | + 			// selected-game preview lives in the game view and keeps its audio policy.
      |    322 | + 			dynamic_cast<VideoComponent*>(extra)->setPlayAudio(false);
  256 |    323 |   			auto elem = system->getTheme()->getElement("system", extra->getTag(), "video");
  257 |    324 |   			if (elem != nullptr && elem->has("path") && Utils::String::startsWith(elem->get<std::string>("path"), "{random"))
  258 |    325 |   				((VideoComponent*)extra)->setPlaylist(std::make_shared<SystemRandomPlaylist>(system, SystemRandomPlaylist::VIDEO));
```

## Trecho 10: antes 295, depois 362

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L295) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L362)

```text
ANTES | DEPOIS |   CÓDIGO
  295 |    362 |   		data.backgroundExtras = extras;
  296 |    363 |   		data.frontCarouselVideo = frontCarouselVideo;
  297 |    364 |   		data.frontCarouselVideoPath = frontCarouselVideoPath;
      |    365 | + 		data.frontCarouselVideoConfiguredPath = frontCarouselVideoConfiguredPath;
      |    366 | + 		data.frontCarouselVideoCheckedAt = frontVideoCacheNow();
  298 |    367 |   		mEntries.push_back(data);
  299 |    368 |   	}
  300 |    369 |   	else
```

## Trecho 11: antes 302, depois 371

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L302) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L371)

```text
ANTES | DEPOIS |   CÓDIGO
  302 |    371 |   		it->backgroundExtras = extras;
  303 |    372 |   		it->frontCarouselVideo = frontCarouselVideo;
  304 |    373 |   		it->frontCarouselVideoPath = frontCarouselVideoPath;
      |    374 | + 		it->frontCarouselVideoConfiguredPath = frontCarouselVideoConfiguredPath;
      |    375 | + 		it->frontCarouselVideoCheckedAt = frontVideoCacheNow();
  305 |    376 |   	}
  306 |    377 |   
  307 |    378 |   	SystemRandomPlaylist::resetCache();
```

## Trecho 12: antes 1533, depois 1604

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L1533) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L1604)

```text
ANTES | DEPOIS |   CÓDIGO
 1533 |   1604 |   		{
 1534 |   1605 |   			VideoVlcComponent* sv = new VideoVlcComponent(mWindow);
 1535 |   1606 |   			sv->applyTheme(theme, "system", name, ThemeFlags::ALL);
      |   1607 | + 			sv->setPlayAudio(false);
 1536 |   1608 |   			mStaticBackgrounds.push_back(sv);
 1537 |   1609 |   		}
 1538 |   1610 |   	}
```

## Trecho 13: antes 1916, depois 1988

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L1916) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L1988)

```text
ANTES | DEPOIS |   CÓDIGO
 1916 |   1988 |   
 1917 |   1989 |   void SystemView::getCarouselFromTheme(const ThemeData::ThemeElement* elem)
 1918 |   1990 |   {
 1919 |        | - 	mFrontCarouselMaxVisible = elem->has("maxLogoCount") ?
 1920 |        | - 		(int)Math::round(elem->get<float>("maxLogoCount")) : 3;
      |   1991 | + 	mFrontCarouselMaxVisible = Math::max(1, elem->has("maxLogoCount") ?
      |   1992 | + 		(int)Math::round(elem->get<float>("maxLogoCount")) : 3);
 1921 |   1993 |   
 1922 |   1994 |   	if (elem->has("systemInfoDelay"))
 1923 |   1995 |   		mSystemInfoDelay = elem->get<float>("systemInfoDelay");
```

## Trecho 14: antes 2103, depois 2175

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2103) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2175)

```text
ANTES | DEPOIS |   CÓDIGO
 2103 |   2175 |   	}
 2104 |   2176 |   
 2105 |   2177 |   	const std::string videoMode = Settings::getInstance()->getString("FrontSystemCarouselVideoMode");
      |   2178 | + 	bool forceReactivate = false;
 2106 |   2179 |   	if (mFrontCarouselVideoModeDirty)
 2107 |   2180 |   	{
 2108 |   2181 |   		// The theme menu stops the existing VLC players while it is on top. Force
 2109 |   2182 |   		// their media path to be assigned again after changing mode; otherwise the
 2110 |   2183 |   		// stopped player can leave only the underlying cell image visible.
      |   2184 | + 		hideFrontCarouselVideos();
 2111 |   2185 |   		for (auto& entry : mEntries)
 2112 |   2186 |   		{
 2113 |   2187 |   			if (entry.frontCarouselVideo == nullptr)
```

## Trecho 15: antes 2117, depois 2191

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2117) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2191)

```text
ANTES | DEPOIS |   CÓDIGO
 2117 |   2191 |   			entry.frontCarouselVideo->setVideo("");
 2118 |   2192 |   		}
 2119 |   2193 |   		mFrontCarouselVideoModeDirty = false;
      |   2194 | + 		forceReactivate = true;
 2120 |   2195 |   	}
 2121 |   2196 |   
 2122 |   2197 |   	if (videoMode == "images")
 2123 |   2198 |   	{
 2124 |        | - 		hideFrontCarouselVideos();
      |   2199 | + 		if (!mFrontCarouselActiveVideoIndices.empty())
      |   2200 | + 			hideFrontCarouselVideos();
      |   2201 | + 		mFrontCarouselSyncedCursor = cursor;
      |   2202 | + 		mFrontCarouselSyncedCount = 0;
      |   2203 | + 		mFrontCarouselSyncedEntryCount = (int)mEntries.size();
      |   2204 | + 		mFrontCarouselSyncedMode = videoMode;
      |   2205 | + 		mFrontCarouselSyncValid = true;
 2125 |   2206 |   		return;
 2126 |   2207 |   	}
 2127 |   2208 |   
 2128 |   2209 |   	const bool showAllVisible = videoMode == "all";
 2129 |   2210 |   	const int entryCount = (int)mEntries.size();
 2130 |        | - 	const int scrollBuffer = mCarousel.getScrollingVelocity() == 0 ? 2 : 5;
 2131 |        | - 	const int visibleRadius = showAllVisible ?
 2132 |        | - 		Math::min(entryCount / 2, Math::max(0, mFrontCarouselMaxVisible / 2 + scrollBuffer)) : 0;
      |   2211 | + 	const int requestedCount = showAllVisible ?
      |   2212 | + 		Math::min(entryCount, Math::max(1, mFrontCarouselMaxVisible)) : 1;
      |   2213 | + 	const bool syncChanged = forceReactivate || !mFrontCarouselSyncValid ||
      |   2214 | + 		cursor != mFrontCarouselSyncedCursor ||
      |   2215 | + 		requestedCount != mFrontCarouselSyncedCount ||
      |   2216 | + 		entryCount != mFrontCarouselSyncedEntryCount ||
      |   2217 | + 		videoMode != mFrontCarouselSyncedMode;
      |   2218 | + 
      |   2219 | + 	if (!syncChanged)
      |   2220 | + 	{
      |   2221 | + 		// A negative media lookup is the only stable-state work. Retry only the
      |   2222 | + 		// handful of active cells after its TTL; successful paths never probe disk.
      |   2223 | + 		const long long now = frontVideoCacheNow();
      |   2224 | + 		for (int index : mFrontCarouselActiveVideoIndices)
      |   2225 | + 		{
      |   2226 | + 			if (index < 0 || index >= entryCount)
      |   2227 | + 				continue;
 2133 |   2228 |   
 2134 |        | - 	for (int i = 0; i < entryCount; i++)
      |   2229 | + 			SystemViewData& data = mEntries[index];
      |   2230 | + 			const bool retryNegative = data.frontCarouselVideo != nullptr &&
      |   2231 | + 				data.frontCarouselVideoPath.empty() &&
      |   2232 | + 				!data.frontCarouselVideoConfiguredPath.empty() &&
      |   2233 | + 				now - data.frontCarouselVideoCheckedAt >= FRONT_VIDEO_CACHE_TTL_MS;
      |   2234 | + 			const bool retryMissingCell = data.frontCarouselVideo != nullptr &&
      |   2235 | + 				!data.frontCarouselVideoPath.empty() &&
      |   2236 | + 				!data.frontCarouselVideo->isVisible();
      |   2237 | + 			if (retryNegative || retryMissingCell)
      |   2238 | + 				showFrontCarouselVideo(data, index);
      |   2239 | + 		}
      |   2240 | + 		return;
      |   2241 | + 	}
      |   2242 | + 
      |   2243 | + 	// Update decoder limits only when configuration/lifecycle changes, never on
      |   2244 | + 	// every rendered frame. A newly reloaded component is covered by !SyncValid.
      |   2245 | + 	if (!mFrontCarouselSyncValid || requestedCount != mFrontCarouselSyncedCount ||
      |   2246 | + 		entryCount != mFrontCarouselSyncedEntryCount)
 2135 |   2247 |   	{
 2136 |        | - 		const int linearDistance = abs(i - cursor);
 2137 |        | - 		const int circularDistance = Math::min(linearDistance, entryCount - linearDistance);
 2138 |        | - 		if (i == cursor || (showAllVisible && circularDistance <= visibleRadius))
 2139 |        | - 			showFrontCarouselVideo(mEntries[i], i);
 2140 |        | - 		else
 2141 |        | - 			hideFrontCarouselVideo(mEntries[i]);
      |   2248 | + 		for (auto& entry : mEntries)
      |   2249 | + 			if (entry.frontCarouselVideo != nullptr)
      |   2250 | + 				entry.frontCarouselVideo->setConcurrentPlaybackLimit(requestedCount);
 2142 |   2251 |   	}
      |   2252 | + 
      |   2253 | + 	// maxLogoCount describes cells, not a radius. Build the exact circular
      |   2254 | + 	// window only when its inputs change; an even count gets its spare cell on
      |   2255 | + 	// the forward side. Keep the selected cell first for decoder allocation.
      |   2256 | + 	std::vector<int> desiredIndices;
      |   2257 | + 	desiredIndices.reserve(requestedCount);
      |   2258 | + 	const int firstOffset = -(requestedCount - 1) / 2;
      |   2259 | + 	for (int offset = 0; offset < requestedCount; offset++)
      |   2260 | + 	{
      |   2261 | + 		int index = (cursor + firstOffset + offset) % entryCount;
      |   2262 | + 		if (index < 0)
      |   2263 | + 			index += entryCount;
      |   2264 | + 		desiredIndices.push_back(index);
      |   2265 | + 	}
      |   2266 | + 	std::sort(desiredIndices.begin(), desiredIndices.end());
      |   2267 | + 	auto selected = std::find(desiredIndices.begin(), desiredIndices.end(), cursor);
      |   2268 | + 	if (selected != desiredIndices.end())
      |   2269 | + 	{
      |   2270 | + 		desiredIndices.erase(selected);
      |   2271 | + 		desiredIndices.insert(desiredIndices.begin(), cursor);
      |   2272 | + 	}
      |   2273 | + 
      |   2274 | + 	// Hide departures before showing arrivals. Only the small previous/current
      |   2275 | + 	// windows are compared, so library size no longer affects per-frame work.
      |   2276 | + 	for (int index : mFrontCarouselActiveVideoIndices)
      |   2277 | + 		if (index >= 0 && index < entryCount &&
      |   2278 | + 			std::find(desiredIndices.begin(), desiredIndices.end(), index) == desiredIndices.end())
      |   2279 | + 			hideFrontCarouselVideo(mEntries[index]);
      |   2280 | + 
      |   2281 | + 	for (int index : desiredIndices)
      |   2282 | + 		if (std::find(mFrontCarouselActiveVideoIndices.begin(),
      |   2283 | + 			mFrontCarouselActiveVideoIndices.end(), index) ==
      |   2284 | + 			mFrontCarouselActiveVideoIndices.end())
      |   2285 | + 			showFrontCarouselVideo(mEntries[index], index);
      |   2286 | + 
      |   2287 | + 	mFrontCarouselActiveVideoIndices.swap(desiredIndices);
      |   2288 | + 	mFrontCarouselSyncedCursor = cursor;
      |   2289 | + 	mFrontCarouselSyncedCount = requestedCount;
      |   2290 | + 	mFrontCarouselSyncedEntryCount = entryCount;
      |   2291 | + 	mFrontCarouselSyncedMode = videoMode;
      |   2292 | + 	mFrontCarouselSyncValid = true;
 2143 |   2293 |   }
 2144 |   2294 |   
 2145 |   2295 |   void SystemView::invalidateFrontCarouselVideoMode()
```

## Trecho 16: antes 2156, depois 2306

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2156) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2306)

```text
ANTES | DEPOIS |   CÓDIGO
 2156 |   2306 |   
 2157 |   2307 |   void SystemView::showFrontCarouselVideo(SystemViewData& data, int index)
 2158 |   2308 |   {
 2159 |        | - 	if (data.frontCarouselVideo == nullptr || data.frontCarouselVideoPath.empty() ||
 2160 |        | - 		!Utils::FileSystem::exists(data.frontCarouselVideoPath))
      |   2309 | + 	// loadExtras validates and caches this path (including a negative result).
      |   2310 | + 	if (data.frontCarouselVideo == nullptr)
      |   2311 | + 	{
      |   2312 | + 		hideFrontCarouselVideo(data);
      |   2313 | + 		return;
      |   2314 | + 	}
      |   2315 | + 
      |   2316 | + 	if (data.frontCarouselVideoPath.empty() &&
      |   2317 | + 		!data.frontCarouselVideoConfiguredPath.empty())
      |   2318 | + 	{
      |   2319 | + 		const long long now = frontVideoCacheNow();
      |   2320 | + 		if (now - data.frontCarouselVideoCheckedAt >= FRONT_VIDEO_CACHE_TTL_MS)
      |   2321 | + 		{
      |   2322 | + 			data.frontCarouselVideoPath = resolveFrontCarouselVideoPath(
      |   2323 | + 				data.object, data.frontCarouselVideoConfiguredPath);
      |   2324 | + 			data.frontCarouselVideoCheckedAt = now;
      |   2325 | + 		}
      |   2326 | + 	}
      |   2327 | + 
      |   2328 | + 	if (data.frontCarouselVideoPath.empty())
 2161 |   2329 |   	{
 2162 |   2330 |   		hideFrontCarouselVideo(data);
 2163 |   2331 |   		return;
```

## Trecho 17: antes 2197, depois 2365

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2197) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2365)

```text
ANTES | DEPOIS |   CÓDIGO
 2197 |   2365 |   	data.frontCarouselVideo->setOrigin(0.5f, 0.5f);
 2198 |   2366 |   	data.frontCarouselVideo->setPosition(parentSize.x() * 0.5f, parentSize.y() * 0.5f, 0.0f);
 2199 |   2367 |   	data.frontCarouselVideo->setMaxSize(parentSize.x(), parentSize.y());
 2200 |        | - 	data.frontCarouselVideo->setVideo(data.frontCarouselVideoPath);
      |   2368 | + 	// The resolver already validated this cached path. Avoid VideoComponent's
      |   2369 | + 	// second ResourceManager::fileExists() call on every activation.
      |   2370 | + 	data.frontCarouselVideo->setVideo(data.frontCarouselVideoPath, false);
 2201 |   2371 |   	// A player may have been created after SystemView::topWindow(true), notably
 2202 |   2372 |   	// during a theme reload. Ensure it consumes the active/preview state before
 2203 |   2373 |   	// onShow() evaluates VideoComponent::manageState().
```

## Trecho 18: antes 2228, depois 2398

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2228) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2398)

```text
ANTES | DEPOIS |   CÓDIGO
 2228 |   2398 |   
 2229 |   2399 |   void SystemView::hideFrontCarouselVideos()
 2230 |   2400 |   {
 2231 |        | - 	for (auto& entry : mEntries)
 2232 |        | - 		hideFrontCarouselVideo(entry);
      |   2401 | + 	for (int index : mFrontCarouselActiveVideoIndices)
      |   2402 | + 		if (index >= 0 && index < (int)mEntries.size())
      |   2403 | + 			hideFrontCarouselVideo(mEntries[index]);
      |   2404 | + 
      |   2405 | + 	mFrontCarouselActiveVideoIndices.clear();
      |   2406 | + 	mFrontCarouselSyncValid = false;
 2233 |   2407 |   }
 2234 |   2408 |   
 2235 |   2409 |   void SystemView::releaseFrontCarouselVideo(SystemViewData& data)
 2236 |   2410 |   {
      |   2411 | + 	for (auto it = mFrontCarouselActiveVideoIndices.begin();
      |   2412 | + 		it != mFrontCarouselActiveVideoIndices.end(); ++it)
      |   2413 | + 	{
      |   2414 | + 		const int index = *it;
      |   2415 | + 		if (index >= 0 && index < (int)mEntries.size() && &mEntries[index] == &data)
      |   2416 | + 		{
      |   2417 | + 			mFrontCarouselActiveVideoIndices.erase(it);
      |   2418 | + 			mFrontCarouselSyncValid = false;
      |   2419 | + 			break;
      |   2420 | + 		}
      |   2421 | + 	}
      |   2422 | + 
 2237 |   2423 |   	if (data.frontCarouselVideo == nullptr)
 2238 |   2424 |   		return;
 2239 |   2425 |   
```

## Trecho 19: antes 2244, depois 2430

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2244) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2430)

```text
ANTES | DEPOIS |   CÓDIGO
 2244 |   2430 |   		videoParent->removeChild(data.frontCarouselVideo.get());
 2245 |   2431 |   	data.frontCarouselVideo.reset();
 2246 |   2432 |   	data.frontCarouselVideoPath.clear();
      |   2433 | + 	data.frontCarouselVideoConfiguredPath.clear();
      |   2434 | + 	data.frontCarouselVideoCheckedAt = 0;
 2247 |   2435 |   }
 2248 |   2436 |   
 2249 |   2437 |   SystemData* SystemView::getActiveSystem()
```

Conferência: 19 trechos, 211 linhas adicionadas e 23 removidas.

