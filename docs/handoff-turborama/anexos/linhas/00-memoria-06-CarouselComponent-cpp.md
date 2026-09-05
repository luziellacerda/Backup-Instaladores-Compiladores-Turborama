# 00-memoria: TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Visibilidade das células, preparação/reuso de vídeo, descarte de recursos e renderização do carrossel.

- Antes: `6f6b8b8372610fc2abe1e137d99a48c3ec52412e`.
- Depois: `0e02780b761cb488c591416d2986130efcc166dd`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 11, depois 11

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L11) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L11)

```text
ANTES | DEPOIS |   CÓDIGO
   11 |     11 |   #include "components/VideoComponent.h"
   12 |     12 |   #include "components/VideoVlcComponent.h"
   13 |     13 |   #include "utils/FileSystemUtil.h"
      |     14 | + #include <algorithm>
      |     15 | + #include <cmath>
   14 |     16 |   
   15 |     17 |   // buffer values for scrolling velocity (left, stopped, right)
   16 |     18 |   const int logoBuffersLeft[] = { -5, -2, -1 };
```

## Trecho 2: antes 64, depois 66

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L64) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L66)

```text
ANTES | DEPOIS |   CÓDIGO
   64 |     66 |   
   65 |     67 |   	mCellVideoEnabled = false;
   66 |     68 |   	mCellVideoFoldersOnly = true;
   67 |        | - 	mCellVideoAudio = false;
   68 |     69 |   	mCellVideoDelay = 0.0f;
   69 |     70 |   	mCellVideoRoundCorners = 0.0f;
   70 |     71 |   	mCellVideoSize = Vector2f(0.98f, 0.98f);
      |     72 | + 	mActiveCellVideoCount = 0;
   71 |     73 |   }
   72 |     74 |   
   73 |     75 |   CarouselComponent::~CarouselComponent()
```

## Trecho 3: antes 83, depois 85

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L83) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L85)

```text
ANTES | DEPOIS |   CÓDIGO
   83 |     85 |   	mEntries.clear();
   84 |     86 |   }
   85 |     87 |   
      |     88 | + void CarouselComponent::clear()
      |     89 | + {
      |     90 | + 	// Detach every active player before IList destroys its owning entry. Released
      |     91 | + 	// wrappers remain in the bounded pool and can be reused when the list is
      |     92 | + 	// populated again (the normal system/theme refresh sequence).
      |     93 | + 	for (auto& entry : mEntries)
      |     94 | + 		releaseCellVideo(entry.data);
      |     95 | + 	mActiveCellVideoIndices.clear();
      |     96 | + 	mActiveCellVideoCount = 0;
      |     97 | + 	mWasRendered = false;
      |     98 | + 
      |     99 | + 	// Preserve IList::clear semantics: reset the cursor, stop list movement and
      |    100 | + 	// deliver the usual CURSOR_STOPPED notification.
      |    101 | + 	IList<CarouselComponentData, IBindable*>::clear();
      |    102 | + }
      |    103 | + 
      |    104 | + bool CarouselComponent::remove(IBindable* obj)
      |    105 | + {
      |    106 | + 	auto entry = findEntry(obj);
      |    107 | + 	if (entry == end())
      |    108 | + 		return false;
      |    109 | + 
      |    110 | + 	const int removedIndex = (int)(entry - mEntries.begin());
      |    111 | + 	releaseCellVideo(entry->data);
      |    112 | + 
      |    113 | + 	// IList::remove erases the entry before notifying the cursor callback. Keep
      |    114 | + 	// the active-index bookkeeping in the post-erase coordinate space so that a
      |    115 | + 	// callback cannot release or reactivate the neighboring cell by mistake.
      |    116 | + 	std::vector<int> remappedIndices;
      |    117 | + 	remappedIndices.reserve(mActiveCellVideoIndices.size());
      |    118 | + 	for (int index : mActiveCellVideoIndices)
      |    119 | + 	{
      |    120 | + 		if (index == removedIndex)
      |    121 | + 			continue;
      |    122 | + 		remappedIndices.push_back(index > removedIndex ? index - 1 : index);
      |    123 | + 	}
      |    124 | + 	mActiveCellVideoIndices.swap(remappedIndices);
      |    125 | + 	mActiveCellVideoCount = (int)mActiveCellVideoIndices.size();
      |    126 | + 
      |    127 | + 	const bool removed = IList<CarouselComponentData, IBindable*>::remove(obj);
      |    128 | + 	trimCellVideoPool();
      |    129 | + 	return removed;
      |    130 | + }
      |    131 | + 
   86 |    132 |   int CarouselComponent::moveCursorFast(bool forward)
   87 |    133 |   {
   88 |    134 |   	int value = mCursor;
```

## Trecho 4: antes 499, depois 545

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L499) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L545)

```text
ANTES | DEPOIS |   CÓDIGO
  499 |    545 |   		bufferRight = 0;
  500 |    546 |   	}
  501 |    547 |   
  502 |        | - 	std::vector<bool> renderedEntries(mEntries.size(), false);
      |    548 | + 	std::vector<int> renderPositions;
      |    549 | + 	for (int i = center - logoCount / 2 + bufferLeft;
      |    550 | + 		i <= center + logoCount / 2 + bufferRight; i++)
      |    551 | + 		renderPositions.push_back(i);
      |    552 | + 
      |    553 | + 	// Image/animation buffers remain intact, but only the XML-requested number
      |    554 | + 	// of central cells may own decoders. Pick the closest distinct entries to the
      |    555 | + 	// moving camera so buffered, off-screen entries always fall back to covers.
      |    556 | + 	std::vector<int> videoPositions = renderPositions;
      |    557 | + 	std::stable_sort(videoPositions.begin(), videoPositions.end(),
      |    558 | + 		[this](int left, int right)
      |    559 | + 		{
      |    560 | + 			const float leftDistance = std::abs((float)left - mCamOffset);
      |    561 | + 			const float rightDistance = std::abs((float)right - mCamOffset);
      |    562 | + 			if (leftDistance != rightDistance)
      |    563 | + 				return leftDistance < rightDistance;
      |    564 | + 
      |    565 | + 			// Match SystemView's exact window: with an even maxLogoCount, the
      |    566 | + 			// unpaired cell belongs to the forward (positive-index) side.
      |    567 | + 			return left > right;
      |    568 | + 		});
      |    569 | + 
      |    570 | + 	std::vector<int> videoEntries;
      |    571 | + 	videoEntries.reserve(logoCount);
      |    572 | + 	for (int position : videoPositions)
      |    573 | + 	{
      |    574 | + 		if (logoCount <= 0)
      |    575 | + 			break;
      |    576 | + 
      |    577 | + 		int index = position % (int)mEntries.size();
      |    578 | + 		if (index < 0)
      |    579 | + 			index += (int)mEntries.size();
      |    580 | + 
      |    581 | + 		if (std::find(videoEntries.begin(), videoEntries.end(), index) == videoEntries.end())
      |    582 | + 		{
      |    583 | + 			videoEntries.push_back(index);
      |    584 | + 			if ((int)videoEntries.size() >= logoCount)
      |    585 | + 				break;
      |    586 | + 		}
      |    587 | + 	}
      |    588 | + 
      |    589 | + 	// Recycle players before assigning the newly central entries. This permits a
      |    590 | + 	// cell hand-off in the same frame without constructing another VLC wrapper.
      |    591 | + 	// Both vectors are bounded by maxLogoCount, so this stays independent of the
      |    592 | + 	// total library size.
      |    593 | + 	for (int index : mActiveCellVideoIndices)
      |    594 | + 		if (std::find(videoEntries.begin(), videoEntries.end(), index) == videoEntries.end() &&
      |    595 | + 			index >= 0 && index < (int)mEntries.size())
      |    596 | + 			releaseCellVideo(mEntries[index].data);
  503 |    597 |   
  504 |        | - 	auto renderLogo = [this, carouselTrans, logoSpacing, xOff, yOff, &renderedEntries](int i)
      |    598 | + 	mActiveCellVideoIndices.clear();
      |    599 | + 	for (int index : videoEntries)
      |    600 | + 	{
      |    601 | + 		auto& entry = mEntries[index];
      |    602 | + 		ensureLogo(entry);
      |    603 | + 		prepareCellVideo(entry);
      |    604 | + 		if (entry.data.cellVideo != nullptr)
      |    605 | + 			mActiveCellVideoIndices.push_back(index);
      |    606 | + 	}
      |    607 | + 
      |    608 | + 	auto renderLogo = [this, carouselTrans, logoSpacing, xOff, yOff](int i)
  505 |    609 |   	{
  506 |    610 |   		int index = i % (int)mEntries.size();
  507 |    611 |   		if (index < 0)
  508 |    612 |   			index += (int)mEntries.size();
  509 |        | - 		renderedEntries[index] = true;
  510 |    613 |   
  511 |    614 |   		Transform4x4f logoTrans = carouselTrans;
  512 |    615 |   
```

## Trecho 5: antes 537, depois 640

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L537) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L640)

```text
ANTES | DEPOIS |   CÓDIGO
  537 |    640 |   
  538 |    641 |   		auto& entry = mEntries.at(index);
  539 |    642 |   		ensureLogo(entry);
  540 |        | - 		prepareCellVideo(entry);
  541 |    643 |   
  542 |    644 |   		const std::shared_ptr<GuiComponent> &comp = entry.data.logo;
  543 |    645 |   		if (mType == CarouselType::VERTICAL_WHEEL || mType == CarouselType::HORIZONTAL_WHEEL)
```

## Trecho 6: antes 560, depois 662

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L560) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L662)

```text
ANTES | DEPOIS |   CÓDIGO
  560 |    662 |   
  561 |    663 |   
  562 |    664 |   	std::vector<int> activePositions;
  563 |        | - 	for (int i = center - logoCount / 2 + bufferLeft; i <= center + logoCount / 2 + bufferRight; i++)
      |    665 | + 	for (int i : renderPositions)
  564 |    666 |   	{
  565 |    667 |   		int index = i % (int)mEntries.size();
  566 |    668 |   		if (index < 0)
```

## Trecho 7: antes 571, depois 673

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L571) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L673)

```text
ANTES | DEPOIS |   CÓDIGO
  571 |    673 |   		else
  572 |    674 |   			renderLogo(i);
  573 |    675 |   	}
  574 |        | - 	
      |    676 | + 
  575 |    677 |   	for (auto activePos : activePositions)
  576 |    678 |   		renderLogo(activePos);
  577 |        | - 
  578 |        | - 	// Release decoders as soon as their cells leave the rendered carousel range.
  579 |        | - 	// The entry retains its static cover and recreates only its own player later.
  580 |        | - 	for (int index = 0; index < (int)mEntries.size(); index++)
  581 |        | - 		if (!renderedEntries[index] && mEntries[index].data.cellVideo != nullptr)
  582 |        | - 			releaseCellVideo(mEntries[index].data);
  583 |    679 |   }
  584 |    680 |   
  585 |    681 |   void CarouselComponent::getCarouselFromTheme(const ThemeData::ThemeElement* elem)
```

## Trecho 8: antes 606, depois 702

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L606) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L702)

```text
ANTES | DEPOIS |   CÓDIGO
  606 |    702 |   	if (elem->has("logoPos"))
  607 |    703 |   		mLogoPos = elem->get<Vector2f>("logoPos") * size;
  608 |    704 |   	if (elem->has("maxLogoCount"))
  609 |        | - 		mMaxLogoCount = (int)Math::round(elem->get<float>("maxLogoCount"));
      |    705 | + 		// Rendering divides by this value and the video pool uses it as a size.
      |    706 | + 		// Treat invalid theme values as one visible cell instead of allowing a
      |    707 | + 		// divide-by-zero or a negative value to become a huge size_t allocation.
      |    708 | + 		mMaxLogoCount = Math::max(1, (int)Math::round(elem->get<float>("maxLogoCount")));
  610 |    709 |   	if (elem->has("logoRotation"))
  611 |    710 |   		mLogoRotation = elem->get<float>("logoRotation");
  612 |    711 |   	if (elem->has("logoRotationOrigin"))
```

## Trecho 9: antes 676, depois 775

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L676) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L775)

```text
ANTES | DEPOIS |   CÓDIGO
  676 |    775 |   		mCellVideoEnabled = elem->get<bool>("cellVideoEnabled");
  677 |    776 |   	if (elem->has("cellVideoFoldersOnly"))
  678 |    777 |   		mCellVideoFoldersOnly = elem->get<bool>("cellVideoFoldersOnly");
  679 |        | - 	if (elem->has("cellVideoAudio"))
  680 |        | - 		mCellVideoAudio = elem->get<bool>("cellVideoAudio");
  681 |    778 |   	if (elem->has("cellVideoDelay"))
  682 |    779 |   		mCellVideoDelay = Math::max(0.0f, elem->get<float>("cellVideoDelay"));
  683 |    780 |   	if (elem->has("cellVideoSize"))
```

## Trecho 10: antes 693, depois 790

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L693) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L790)

```text
ANTES | DEPOIS |   CÓDIGO
  693 |    790 |   	if (!mCellVideoEnabled ||
  694 |    791 |   		!Settings::getInstance()->getBool("CarouselCellVideoKeepPlaying"))
  695 |    792 |   		stopCellVideo();
      |    793 | + 
      |    794 | + 	trimCellVideoPool();
  696 |    795 |   }
  697 |    796 |   
  698 |    797 |   void CarouselComponent::prepareCellVideo(IList<CarouselComponentData, IBindable*>::Entry& entry)
```

## Trecho 11: antes 713, depois 812

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L713) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L812)

```text
ANTES | DEPOIS |   CÓDIGO
  713 |    812 |   	}
  714 |    813 |   
  715 |    814 |   	const std::string videoPath = entry.object->getProperty("carouselVideo").toString();
  716 |        | - 	const bool available = !videoPath.empty() && Utils::FileSystem::exists(videoPath);
  717 |        | - 
  718 |        | - 	if (!available)
      |    815 | + 	// FileData resolves and validates this property through its media cache. Do
      |    816 | + 	// not repeat an exists() probe for every rendered cell on every frame.
      |    817 | + 	if (videoPath.empty())
  719 |    818 |   	{
  720 |    819 |   		releaseCellVideo(entry.data);
  721 |    820 |   		return;
```

## Trecho 12: antes 723, depois 822

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L723) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L822)

```text
ANTES | DEPOIS |   CÓDIGO
  723 |    822 |   
  724 |    823 |   	if (entry.data.cellVideo == nullptr)
  725 |    824 |   	{
  726 |        | - 		auto video = std::make_shared<VideoVlcComponent>(mWindow);
  727 |        | - 		video->setEffect(VideoVlcFlags::SIZE);
  728 |        | - 		video->setTag("carouselCellVideo");
      |    825 | + 		auto video = acquireCellVideo();
      |    826 | + 		if (video == nullptr)
      |    827 | + 			return;
      |    828 | + 
  729 |    829 |   		video->setOrigin(0.5f, 0.5f);
  730 |    830 |   
  731 |    831 |   		// Item templates keep their focus storyboard on the first themed container.
```

## Trecho 13: antes 743, depois 843

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L743) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L843)

```text
ANTES | DEPOIS |   CÓDIGO
  743 |    843 |   		video->setMaxSize(mLogoSize.x() * mCellVideoSize.x(),
  744 |    844 |   			mLogoSize.y() * mCellVideoSize.y());
  745 |    845 |   		video->setStartDelay((int)(mCellVideoDelay * 1000.0f));
  746 |        | - 		video->setPlayAudio(mCellVideoAudio);
      |    846 | + 		// Embedded cell players are deliberately silent. The selected game's main
      |    847 | + 		// preview is a separate component and retains its existing audio behavior.
      |    848 | + 		video->setPlayAudio(false);
  747 |    849 |   		video->setRoundCorners(mCellVideoRoundCorners);
  748 |    850 |   		video->setVisible(true);
  749 |    851 |   		videoParent->addChild(video.get());
  750 |    852 |   		video->setZIndex(12060);
  751 |    853 |   		video->onShow();
  752 |    854 |   		entry.data.cellVideo = video;
      |    855 | + 		mActiveCellVideoCount++;
  753 |    856 |   	}
  754 |    857 |   
      |    858 | + 	if (auto* vlcVideo = dynamic_cast<VideoVlcComponent*>(entry.data.cellVideo.get()))
      |    859 | + 		vlcVideo->setConcurrentPlaybackLimit((int)getCellVideoPoolLimit());
      |    860 | + 
  755 |    861 |   	if (entry.data.cellVideoPath != videoPath)
  756 |    862 |   	{
  757 |    863 |   		entry.data.cellVideo->stopPlayback();
  758 |        | - 		entry.data.cellVideo->setVideo(videoPath);
      |    864 | + 		entry.data.cellVideo->setVideo(videoPath, false);
  759 |    865 |   		entry.data.cellVideoPath = videoPath;
  760 |    866 |   		LOG(LogInfo) << "[CarouselCellVideo] playing cell " << entry.name << ": " << videoPath;
  761 |    867 |   	}
```

## Trecho 14: antes 771, depois 877

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L771) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L877)

```text
ANTES | DEPOIS |   CÓDIGO
  771 |    877 |   	if (data.cellVideo == nullptr)
  772 |    878 |   		return;
  773 |    879 |   
  774 |        | - 	data.cellVideo->stopPlayback();
  775 |        | - 	data.cellVideo->setVideo("");
  776 |        | - 	data.cellVideo->setVisible(false);
  777 |        | - 	data.cellVideo->onHide();
  778 |        | - 	GuiComponent* videoParent = data.cellVideo->getParent();
      |    880 | + 	auto video = data.cellVideo;
      |    881 | + 	video->stopPlayback();
      |    882 | + 	video->setVideo("", false);
      |    883 | + 	video->setVisible(false);
      |    884 | + 	video->onHide();
      |    885 | + 	GuiComponent* videoParent = video->getParent();
  779 |    886 |   	if (videoParent != nullptr)
  780 |        | - 		videoParent->removeChild(data.cellVideo.get());
      |    887 | + 		videoParent->removeChild(video.get());
  781 |    888 |   	data.cellVideo.reset();
  782 |    889 |   	data.cellVideoPath.clear();
      |    890 | + 	if (mActiveCellVideoCount > 0)
      |    891 | + 		mActiveCellVideoCount--;
      |    892 | + 
      |    893 | + 	if ((size_t)mActiveCellVideoCount + mCellVideoPool.size() < getCellVideoPoolLimit())
      |    894 | + 		mCellVideoPool.push_back(video);
      |    895 | + }
      |    896 | + 
      |    897 | + std::shared_ptr<VideoComponent> CarouselComponent::acquireCellVideo()
      |    898 | + {
      |    899 | + 	const size_t limit = getCellVideoPoolLimit();
      |    900 | + 	if (limit == 0 || (size_t)mActiveCellVideoCount >= limit)
      |    901 | + 		return nullptr;
      |    902 | + 
      |    903 | + 	// Active and idle wrappers together never exceed the XML cell count. Idle
      |    904 | + 	// wrappers have no media/context, but bounding both keeps the pool predictable
      |    905 | + 	// when a theme lowers maxLogoCount at runtime.
      |    906 | + 	const size_t idleLimit = limit - (size_t)mActiveCellVideoCount;
      |    907 | + 	while (mCellVideoPool.size() > idleLimit)
      |    908 | + 		mCellVideoPool.pop_back();
      |    909 | + 
      |    910 | + 	while (!mCellVideoPool.empty())
      |    911 | + 	{
      |    912 | + 		auto video = mCellVideoPool.back();
      |    913 | + 		mCellVideoPool.pop_back();
      |    914 | + 		if (video != nullptr)
      |    915 | + 		{
      |    916 | + 			if (auto* vlcVideo = dynamic_cast<VideoVlcComponent*>(video.get()))
      |    917 | + 				vlcVideo->setConcurrentPlaybackLimit((int)getCellVideoPoolLimit());
      |    918 | + 			return video;
      |    919 | + 		}
      |    920 | + 	}
      |    921 | + 
      |    922 | + 	auto video = std::make_shared<VideoVlcComponent>(mWindow);
      |    923 | + 	video->setEffect(VideoVlcFlags::SIZE);
      |    924 | + 	video->setTag("carouselCellVideo");
      |    925 | + 	video->setPlayAudio(false);
      |    926 | + 	video->setConcurrentPlaybackLimit((int)getCellVideoPoolLimit());
      |    927 | + 	return video;
      |    928 | + }
      |    929 | + 
      |    930 | + size_t CarouselComponent::getCellVideoPoolLimit() const
      |    931 | + {
      |    932 | + 	if (!mCellVideoEnabled || mMaxLogoCount <= 0 || mEntries.empty())
      |    933 | + 		return 0;
      |    934 | + 
      |    935 | + 	return (size_t)Math::min(mMaxLogoCount, (int)mEntries.size());
      |    936 | + }
      |    937 | + 
      |    938 | + void CarouselComponent::trimCellVideoPool()
      |    939 | + {
      |    940 | + 	const size_t limit = getCellVideoPoolLimit();
      |    941 | + 
      |    942 | + 	// Theme reloads can lower maxLogoCount while players are still active. Keep
      |    943 | + 	// the nearest entries (the vector is stored nearest-first) and retire the
      |    944 | + 	// excess immediately instead of waiting for another render pass.
      |    945 | + 	while ((size_t)mActiveCellVideoCount > limit && !mActiveCellVideoIndices.empty())
      |    946 | + 	{
      |    947 | + 		const int index = mActiveCellVideoIndices.back();
      |    948 | + 		mActiveCellVideoIndices.pop_back();
      |    949 | + 		if (index >= 0 && index < (int)mEntries.size())
      |    950 | + 			releaseCellVideo(mEntries[index].data);
      |    951 | + 	}
      |    952 | + 
      |    953 | + 	const size_t idleLimit = (size_t)mActiveCellVideoCount < limit ?
      |    954 | + 		limit - (size_t)mActiveCellVideoCount : 0;
      |    955 | + 	while (mCellVideoPool.size() > idleLimit)
      |    956 | + 		mCellVideoPool.pop_back();
      |    957 | + 
      |    958 | + 	const int concurrentLimit = (int)getCellVideoPoolLimit();
      |    959 | + 	for (int index : mActiveCellVideoIndices)
      |    960 | + 		if (index >= 0 && index < (int)mEntries.size())
      |    961 | + 			if (auto* video = dynamic_cast<VideoVlcComponent*>(mEntries[index].data.cellVideo.get()))
      |    962 | + 				video->setConcurrentPlaybackLimit(concurrentLimit);
      |    963 | + 	for (auto& pooledVideo : mCellVideoPool)
      |    964 | + 		if (auto* video = dynamic_cast<VideoVlcComponent*>(pooledVideo.get()))
      |    965 | + 			video->setConcurrentPlaybackLimit(concurrentLimit);
  783 |    966 |   }
  784 |    967 |   
  785 |    968 |   void CarouselComponent::refreshCellVideo()
```

## Trecho 15: antes 794, depois 977

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L794) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L977)

```text
ANTES | DEPOIS |   CÓDIGO
  794 |    977 |   
  795 |    978 |   	// Do not transfer a player here. Visible entries are refreshed independently
  796 |    979 |   	// by renderCarousel, preserving the ownership of every folder cell.
  797 |        | - 	for (auto& entry : mEntries)
  798 |        | - 		if (entry.data.cellVideo != nullptr && !entry.data.cellVideo->isPlaying())
  799 |        | - 			entry.data.cellVideo->resumePlayback();
      |    980 | + 	for (int index : mActiveCellVideoIndices)
      |    981 | + 		if (index >= 0 && index < (int)mEntries.size() &&
      |    982 | + 			mEntries[index].data.cellVideo != nullptr &&
      |    983 | + 			!mEntries[index].data.cellVideo->isPlaying())
      |    984 | + 			mEntries[index].data.cellVideo->resumePlayback();
  800 |    985 |   }
  801 |    986 |   
  802 |    987 |   void CarouselComponent::stopCellVideo()
  803 |    988 |   {
  804 |        | - 	for (auto& entry : mEntries)
  805 |        | - 		releaseCellVideo(entry.data);
      |    989 | + 	for (int index : mActiveCellVideoIndices)
      |    990 | + 		if (index >= 0 && index < (int)mEntries.size())
      |    991 | + 			releaseCellVideo(mEntries[index].data);
      |    992 | + 	mActiveCellVideoIndices.clear();
      |    993 | + 	mActiveCellVideoCount = 0;
  806 |    994 |   }
  807 |    995 |   
  808 |    996 |   void CarouselComponent::onShow()
```

Conferência: 15 trechos, 223 linhas adicionadas e 35 removidas.

