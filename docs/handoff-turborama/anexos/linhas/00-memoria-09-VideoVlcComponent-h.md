# 00-memoria: TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Contrato e estado do player, incluindo estruturas usadas por callbacks e o método público de espera na PIX.

- Antes: `6f6b8b8372610fc2abe1e137d99a48c3ec52412e`.
- Depois: `0e02780b761cb488c591416d2986130efcc166dd`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 5, depois 5

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L5) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L5)

```text
ANTES | DEPOIS |   CÓDIGO
    5 |      5 |   #include "VideoComponent.h"
    6 |      6 |   #include "ThemeData.h"
    7 |      7 |   #include "renderers/Renderer.h"
      |      8 | + #include <atomic>
      |      9 | + #include <cstddef>
    8 |     10 |   #include <mutex>
    9 |     11 |   #include <set>
      |     12 | + #include <string>
   10 |     13 |   #include <vector>
   11 |     14 |   
   12 |     15 |   struct libvlc_instance_t;
```

## Trecho 2: antes 26, depois 29

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L26) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L29)

```text
ANTES | DEPOIS |   CÓDIGO
   26 |     29 |   		poolIndex = -1;
   27 |     30 |   		bufferWidth = 0;
   28 |     31 |   		bufferHeight = 0;
      |     32 | + 		carouselVideo = false;
      |     33 | + 		countAgainstConcurrentLimit = false;
   29 |     34 |   	}
   30 |     35 |   
   31 |     36 |   	~VideoContext()
```

## Trecho 3: antes 43, depois 48

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L43) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L48)

```text
ANTES | DEPOIS |   CÓDIGO
   43 |     48 |   		surfaces[1] = nullptr;
   44 |     49 |   	}
   45 |     50 |   
   46 |        | - 	int					surfaceId;
      |     51 | + 	std::atomic<int>			surfaceId;
   47 |     52 |   	unsigned char*		surfaces[2];	
   48 |     53 |   	std::mutex			mutexes[2];
   49 |        | - 	bool				hasFrame[2];
      |     54 | + 	std::atomic<bool>		hasFrame[2];
   50 |     55 |   
   51 |     56 |   	VideoComponent*		component;
      |     57 | + 	std::mutex			componentMutex;
   52 |     58 |   	int					poolIndex;
   53 |     59 |   	int					bufferWidth;
   54 |     60 |   	int					bufferHeight;
      |     61 | + 	bool				carouselVideo;
      |     62 | + 	bool				countAgainstConcurrentLimit;
   55 |     63 |   };
   56 |     64 |   
   57 |     65 |   
```

## Trecho 4: antes 72, depois 80

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L72) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L80)

```text
ANTES | DEPOIS |   CÓDIGO
   72 |     80 |   	int height;
   73 |     81 |   	unsigned char* surfaces[2];
   74 |     82 |   	bool inUse;
      |     83 | + 	bool retiring;
      |     84 | + 	bool carouselVideo;
      |     85 | + 	bool countAgainstConcurrentLimit;
      |     86 | + 	unsigned long long lastUsed;
   75 |     87 |   };
   76 |     88 |   
   77 |     89 |   class VideoVlcComponent : public VideoComponent
```

## Trecho 5: antes 88, depois 100

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L88) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L100)

```text
ANTES | DEPOIS |   CÓDIGO
   88 |    100 |   public:
   89 |    101 |   	static void init();
   90 |    102 |   	static void releaseContext(VideoContext* ctx);
      |    103 | + 	static void clearBufferPool();
   91 |    104 |   
   92 |    105 |   	VideoVlcComponent(Window* window);
   93 |    106 |   	virtual ~VideoVlcComponent();
```

## Trecho 6: antes 117, depois 130

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L117) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L130)

```text
ANTES | DEPOIS |   CÓDIGO
  117 |    130 |   	void setProperty(const std::string name, const ThemeData::ThemeElement::Property& value) override;
  118 |    131 |   
  119 |    132 |   	void setEffect(VideoVlcFlags::VideoVlcEffect effect) { mEffect = effect; }
      |    133 | + 	// A positive value caps active + retiring players in this component's
      |    134 | + 	// carousel bucket. Zero keeps the global setting/no additional cap.
      |    135 | + 	void setConcurrentPlaybackLimit(int value) { mConcurrentPlaybackLimit = value > 0 ? value : 0; }
      |    136 | + 	// Render the same decoded frame through this component's own transform,
      |    137 | + 	// opacity, z-index and storyboard without starting a second VLC player.
      |    138 | + 	void setSharedVideoSource(VideoVlcComponent* source);
  120 |    139 |   
  121 |    140 |   	bool getLinearSmooth() { return mLinearSmooth; }
  122 |    141 |   	void setLinearSmooth(bool value = true) { mLinearSmooth = value; }
```

## Trecho 7: antes 151, depois 170

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L151) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L170)

```text
ANTES | DEPOIS |   CÓDIGO
  151 |    170 |   
  152 |    171 |   	void onMediaParsed();
  153 |    172 |   	size_t getVideoBufferBytes() const;
  154 |        | - 	static size_t getActiveVideoBufferBytes();
  155 |        | - 	static size_t estimatePendingVideoBufferBytes();
      |    173 | + 	size_t estimatePendingVideoBufferBytes() const;
      |    174 | + 	bool updatePlaybackReservation(size_t bytes);
      |    175 | + 	bool createMedia(bool forceSoftwareDecoder);
      |    176 | + 	bool trySoftwareDecoderFallback();
      |    177 | + 	void failPlayback(unsigned retryDelay, bool countFailure = true);
      |    178 | + 	void resetPlaybackFailures();
      |    179 | + 	void clearPlaybackDeferred();
      |    180 | + 	void deferPlayback(unsigned retryDelay);
      |    181 | + 	void releaseMediaForDecoderRetry();
      |    182 | + 	bool isCarouselVideo() const;
      |    183 | + 	bool isThemeManagedVideo();
      |    184 | + 	bool shouldPlayAudio();
      |    185 | + 	static void queueMediaPlayerRelease(VideoContext* ctx, libvlc_media_player_t* player);
      |    186 | + 	static void trimBufferPoolLocked(size_t maxFreeBytes, size_t maxTotalBytes);
      |    187 | + 	static size_t getBufferPoolCacheLimitBytes(size_t maxVideoBytes);
  156 |    188 |   	static int getMaxVideoRamMb();
  157 |    189 |   	bool mIsParsing;
  158 |    190 |   
```

## Trecho 8: antes 162, depois 194

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L162) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L194)

```text
ANTES | DEPOIS |   CÓDIGO
  162 |    194 |   	int computePlaybackPriority();
  163 |    195 |   	static void notifyPlaybackSlotAvailable();
  164 |    196 |   	static int getEffectiveMaxConcurrentVideos();
      |    197 | + 	static int getEffectiveMaxConcurrentCarouselVideos();
  165 |    198 |   
  166 |    199 |   	struct ActiveVideoPlayer
  167 |    200 |   	{
```

## Trecho 9: antes 174, depois 207

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L174) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L207)

```text
ANTES | DEPOIS |   CÓDIGO
  174 |    207 |   	static std::set<VideoVlcComponent*>		sDeferredPlayers;
  175 |    208 |   	static std::mutex						sBufferPoolMutex;
  176 |    209 |   	static std::vector<VideoBufferPoolEntry> sVideoBufferPool;
      |    210 | + 	static unsigned long long				sBufferPoolUseCounter;
      |    211 | + 	static size_t						sVideoBufferBudgetBytes;
  177 |    212 |   	bool									mIsRegisteredActive;
      |    213 | + 	size_t								mReservedVideoBytes;
      |    214 | + 	int									mConcurrentPlaybackLimit;
  178 |    215 |   
  179 |    216 |   private:
  180 |    217 |   	void crop(float left, float top, float right, float bot);
```

## Trecho 10: antes 199, depois 236

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L199) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L236)

```text
ANTES | DEPOIS |   CÓDIGO
  199 |    236 |   	long long						mLastPlaybackTime;
  200 |    237 |   	unsigned int					mLastPlaybackProgressTick;
  201 |    238 |   	unsigned int					mLastPlaybackRestartTick;
      |    239 | + 	unsigned int					mPlaybackStartedTick;
      |    240 | + 	int								mPlaybackRestartAttempts;
      |    241 | + 	bool							mUsingHardwareDecoder;
      |    242 | + 	bool							mHardwareFallbackAttempted;
      |    243 | + 	bool							mHasAudioTrack;
      |    244 | + 	bool							mAudioPlaybackRegistered;
      |    245 | + 	bool							mPowerSaverPaused;
      |    246 | + 	std::string						mSoftwareDecoderPath;
      |    247 | + 	std::string						mPlaybackFailurePath;
      |    248 | + 	int								mPlaybackFailureCount;
      |    249 | + 	unsigned int					mPlaybackFailureBlockedUntil;
      |    250 | + 	VideoVlcComponent*				mSharedVideoSource;
  202 |    251 |   
  203 |    252 |   	bool							mLinearSmooth;
  204 |    253 |   	float							mSaturation;
```

Conferência: 10 trechos, 53 linhas adicionadas e 4 removidas.

