# 00-memoria: TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Player VLC, callbacks concorrentes, buffers e pools. Na PIX, inclui a espera limitada pela fila de liberação antes do emulador.

- Antes: `6f6b8b8372610fc2abe1e137d99a48c3ec52412e`.
- Depois: `0e02780b761cb488c591416d2986130efcc166dd`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 16, depois 16

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L16) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L16)

```text
ANTES | DEPOIS |   CÓDIGO
   16 |     16 |   #include "ThemeData.h"
   17 |     17 |   #include <SDL_timer.h>
   18 |     18 |   #include "AudioManager.h"
      |     19 | + #include "Log.h"
      |     20 | + #include <condition_variable>
      |     21 | + #include <deque>
      |     22 | + #include <new>
      |     23 | + #include <thread>
   19 |     24 |   
   20 |     25 |   #ifdef WIN32
   21 |     26 |   #include <codecvt>
```

## Trecho 2: antes 31, depois 36

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L31) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L36)

```text
ANTES | DEPOIS |   CÓDIGO
   31 |     36 |   std::set<VideoVlcComponent*> VideoVlcComponent::sDeferredPlayers;
   32 |     37 |   std::mutex VideoVlcComponent::sBufferPoolMutex;
   33 |     38 |   std::vector<VideoBufferPoolEntry> VideoVlcComponent::sVideoBufferPool;
      |     39 | + unsigned long long VideoVlcComponent::sBufferPoolUseCounter = 0;
      |     40 | + size_t VideoVlcComponent::sVideoBufferBudgetBytes = (size_t)768 * 1024 * 1024;
   34 |     41 |   
   35 |        | - static const int MAX_VIDEO_BUFFER_POOL_SIZE = 6;
      |     42 | + namespace
      |     43 | + {
      |     44 | + 	struct MediaPlayerReleaseJob
      |     45 | + 	{
      |     46 | + 		libvlc_media_player_t* player;
      |     47 | + 		VideoContext* context;
      |     48 | + 	};
      |     49 | + 
      |     50 | + 	// libvlc_media_player_release may wait for VLC decoder threads.  A single
      |     51 | + 	// process-lifetime worker keeps that wait off the render thread without
      |     52 | + 	// creating one detached thread for every carousel movement.
      |     53 | + 	class MediaPlayerReleaseQueue
      |     54 | + 	{
      |     55 | + 	public:
      |     56 | + 		static MediaPlayerReleaseQueue& instance()
      |     57 | + 		{
      |     58 | + 			static MediaPlayerReleaseQueue queue;
      |     59 | + 			return queue;
      |     60 | + 		}
      |     61 | + 
      |     62 | + 		void enqueue(libvlc_media_player_t* player, VideoContext* context)
      |     63 | + 		{
      |     64 | + 			bool releaseSynchronously = false;
      |     65 | + 			{
      |     66 | + 				std::lock_guard<std::mutex> lock(mMutex);
      |     67 | + 				// Keep retiring VLC internals bounded too. Pixel buffers are already
      |     68 | + 				// budgeted, but libVLC owns additional decoder state that we cannot
      |     69 | + 				// measure. Under pathological rapid scrolling, apply backpressure by
      |     70 | + 				// completing this one release on the caller instead of growing forever.
      |     71 | + 				if (mJobs.size() + mInFlight >= MAX_RELEASE_JOBS)
      |     72 | + 					releaseSynchronously = true;
      |     73 | + 				else
      |     74 | + 					mJobs.push_back({ player, context });
      |     75 | + 			}
      |     76 | + 			if (releaseSynchronously)
      |     77 | + 			{
      |     78 | + 				if (player != nullptr)
      |     79 | + 					libvlc_media_player_release(player);
      |     80 | + 				VideoVlcComponent::releaseContext(context);
      |     81 | + 				return;
      |     82 | + 			}
      |     83 | + 			mCondition.notify_one();
      |     84 | + 		}
      |     85 | + 
      |     86 | + 	private:
      |     87 | + 		MediaPlayerReleaseQueue() : mStopping(false), mInFlight(0), mWorker([this]() { run(); })
      |     88 | + 		{
      |     89 | + 		}
      |     90 | + 
      |     91 | + 		~MediaPlayerReleaseQueue()
      |     92 | + 		{
      |     93 | + 			{
      |     94 | + 				std::lock_guard<std::mutex> lock(mMutex);
      |     95 | + 				mStopping = true;
      |     96 | + 			}
      |     97 | + 			mCondition.notify_one();
      |     98 | + 			if (mWorker.joinable())
      |     99 | + 				mWorker.join();
      |    100 | + 			VideoVlcComponent::clearBufferPool();
      |    101 | + 		}
      |    102 | + 
      |    103 | + 		void run()
      |    104 | + 		{
      |    105 | + 			for (;;)
      |    106 | + 			{
      |    107 | + 				MediaPlayerReleaseJob job;
      |    108 | + 				{
      |    109 | + 					std::unique_lock<std::mutex> lock(mMutex);
      |    110 | + 					mCondition.wait(lock, [this]() { return mStopping || !mJobs.empty(); });
      |    111 | + 					if (mStopping && mJobs.empty())
      |    112 | + 						return;
      |    113 | + 					job = mJobs.front();
      |    114 | + 					mJobs.pop_front();
      |    115 | + 					mInFlight++;
      |    116 | + 				}
      |    117 | + 
      |    118 | + 				if (job.player != nullptr)
      |    119 | + 					libvlc_media_player_release(job.player);
      |    120 | + 				VideoVlcComponent::releaseContext(job.context);
      |    121 | + 				{
      |    122 | + 					std::lock_guard<std::mutex> lock(mMutex);
      |    123 | + 					mInFlight--;
      |    124 | + 				}
      |    125 | + 			}
      |    126 | + 		}
      |    127 | + 
      |    128 | + 		static const size_t MAX_RELEASE_JOBS = 16;
      |    129 | + 		std::mutex mMutex;
      |    130 | + 		std::condition_variable mCondition;
      |    131 | + 		std::deque<MediaPlayerReleaseJob> mJobs;
      |    132 | + 		bool mStopping;
      |    133 | + 		size_t mInFlight;
      |    134 | + 		std::thread mWorker;
      |    135 | + 	};
      |    136 | + }
   36 |    137 |   
   37 |    138 |   // VLC prepares to render a video frame.
   38 |    139 |   static void *lock(void *data, void **p_pixels) 
   39 |    140 |   {
   40 |    141 |   	struct VideoContext *c = (struct VideoContext *)data;
   41 |    142 |   
   42 |        | - 	int frame = (c->surfaceId ^ 1);
      |    143 | + 	int frame = (c->surfaceId.load(std::memory_order_acquire) ^ 1);
   43 |    144 |   	
   44 |    145 |   	c->mutexes[frame].lock();
   45 |        | - 	c->hasFrame[frame] = false;
      |    146 | + 	c->hasFrame[frame].store(false, std::memory_order_release);
   46 |    147 |   	*p_pixels = c->surfaces[frame];
   47 |    148 |   	return NULL; // Picture identifier, not needed here.
   48 |    149 |   }
```

## Trecho 3: antes 52, depois 153

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L52) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L153)

```text
ANTES | DEPOIS |   CÓDIGO
   52 |    153 |   {
   53 |    154 |   	struct VideoContext *c = (struct VideoContext *)data;
   54 |    155 |   
   55 |        | - 	int frame = (c->surfaceId ^ 1);	
      |    156 | + 	int frame = (c->surfaceId.load(std::memory_order_acquire) ^ 1);
   56 |    157 |   
   57 |        | - 	c->surfaceId = frame;
   58 |        | - 	c->hasFrame[frame] = true;
      |    158 | + 	c->surfaceId.store(frame, std::memory_order_release);
      |    159 | + 	c->hasFrame[frame].store(true, std::memory_order_release);
   59 |    160 |   	c->mutexes[frame].unlock();
   60 |    161 |   }
   61 |    162 |   
   62 |    163 |   // VLC wants to display a video frame.
   63 |        | - static void display(void* data, void* id)
      |    164 | + static void display(void* /*data*/, void* /*id*/)
   64 |    165 |   {
   65 |        | - 	if (data == NULL)
   66 |        | - 		return;
   67 |        | - 
   68 |        | - 	struct VideoContext *c = (struct VideoContext *)data;
   69 |        | - 	if (c->component != nullptr && !c->component->isPlaying() && c->component->isWaitingForVideoToStart())
   70 |        | - 		c->component->onVideoStarted();
      |    166 | + 	// VLC invokes this from a decoder thread. Playback state and component
      |    167 | + 	// callbacks are deliberately handled by update() on the UI thread.
   71 |    168 |   }
   72 |    169 |   
   73 |    170 |   VideoVlcComponent::VideoVlcComponent(Window* window) : VideoComponent(window), 
```

## Trecho 4: antes 75, depois 172

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L75) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L172)

```text
ANTES | DEPOIS |   CÓDIGO
   75 |    172 |   	mTopLeftCrop(0.0f, 0.0f), mBottomRightCrop(1.0f, 1.0f), mContext(nullptr)
   76 |    173 |   {
   77 |    174 |   	mIsRegisteredActive = false;
      |    175 | + 	mReservedVideoBytes = 0;
      |    176 | + 	mConcurrentPlaybackLimit = 0;
   78 |    177 |   	mIsParsing = false;
      |    178 | + 	mUsingHardwareDecoder = false;
      |    179 | + 	mHardwareFallbackAttempted = false;
      |    180 | + 	mHasAudioTrack = false;
      |    181 | + 	mAudioPlaybackRegistered = false;
      |    182 | + 	mPowerSaverPaused = false;
      |    183 | + 	mPlaybackFailureCount = 0;
      |    184 | + 	mPlaybackFailureBlockedUntil = 0;
      |    185 | + 	mSharedVideoSource = nullptr;
   79 |    186 |   	mSaturation = 1.0f;
   80 |    187 |   	mElapsed = 0;
   81 |    188 |   	mColorShift = 0xFFFFFFFF;
```

## Trecho 5: antes 86, depois 193

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L86) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L193)

```text
ANTES | DEPOIS |   CÓDIGO
   86 |    193 |   	mLastPlaybackTime = -1;
   87 |    194 |   	mLastPlaybackProgressTick = SDL_GetTicks();
   88 |    195 |   	mLastPlaybackRestartTick = 0;
      |    196 | + 	mPlaybackStartedTick = 0;
      |    197 | + 	mPlaybackRestartAttempts = 0;
   89 |    198 |   
   90 |    199 |   	// Get an empty texture for rendering the video
   91 |    200 |   	mTexture = nullptr;// TextureResource::get("");
```

## Trecho 6: antes 95, depois 204

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L95) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L204)

```text
ANTES | DEPOIS |   CÓDIGO
   95 |    204 |   	init();
   96 |    205 |   }
   97 |    206 |   
   98 |        | - static void mediaplayer_release_async(VideoContext* ctx, libvlc_media_player_t* p_mi)
      |    207 | + void VideoVlcComponent::queueMediaPlayerRelease(VideoContext* ctx, libvlc_media_player_t* player)
   99 |    208 |   {
  100 |        | - 	if (p_mi == nullptr)
      |    209 | + 	if (player == nullptr)
  101 |    210 |   	{
  102 |        | - 		VideoVlcComponent::releaseContext(ctx);
      |    211 | + 		releaseContext(ctx);
      |    212 | + 		return;
      |    213 | + 	}
      |    214 | + 	if (ctx == nullptr)
      |    215 | + 	{
      |    216 | + 		// No video callbacks or decoder were installed yet, so this release is
      |    217 | + 		// cheap and avoids an unaccounted context-less job in the worker queue.
      |    218 | + 		libvlc_media_player_release(player);
  103 |    219 |   		return;
  104 |    220 |   	}
  105 |    221 |   
  106 |        | - 	if (ctx != nullptr)
      |    222 | + 	{
      |    223 | + 		// Synchronize with any callback that already observed this context. The
      |    224 | + 		// callback no longer calls the component, but this also keeps the context
      |    225 | + 		// contract safe for older VLC callback ordering during teardown.
      |    226 | + 		std::lock_guard<std::mutex> lock(ctx->componentMutex);
  107 |    227 |   		ctx->component = nullptr;
      |    228 | + 	}
  108 |    229 |   
  109 |        | - 	std::thread([p_mi, ctx]()
      |    230 | + 	{
      |    231 | + 		std::lock_guard<std::mutex> lock(sBufferPoolMutex);
      |    232 | + 		if (ctx->poolIndex >= 0 && ctx->poolIndex < (int)sVideoBufferPool.size())
  110 |    233 |   		{
  111 |        | - 			libvlc_media_player_release(p_mi);
  112 |        | - 			VideoVlcComponent::releaseContext(ctx);
  113 |        | - 		}).detach();
      |    234 | + 			VideoBufferPoolEntry& entry = sVideoBufferPool[ctx->poolIndex];
      |    235 | + 			if (entry.surfaces[0] == ctx->surfaces[0] && entry.inUse)
      |    236 | + 				entry.retiring = true;
      |    237 | + 		}
      |    238 | + 	}
      |    239 | + 
      |    240 | + 	MediaPlayerReleaseQueue::instance().enqueue(player, ctx);
  114 |    241 |   }
  115 |    242 |   
  116 |    243 |   VideoVlcComponent::~VideoVlcComponent()
```

## Trecho 7: antes 127, depois 254

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L127) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L254)

```text
ANTES | DEPOIS |   CÓDIGO
  127 |    254 |   	return maxVideos;
  128 |    255 |   }
  129 |    256 |   
      |    257 | + int VideoVlcComponent::getEffectiveMaxConcurrentCarouselVideos()
      |    258 | + {
      |    259 | + 	// Zero deliberately means "the XML controls the number of cells".  The RAM
      |    260 | + 	// budget is still authoritative and every cell reserves memory before parse.
      |    261 | + 	return Math::max(0, Settings::getInstance()->getInt("MaxConcurrentCarouselVideos"));
      |    262 | + }
      |    263 | + 
  130 |    264 |   int VideoVlcComponent::getMaxVideoRamMb()
  131 |    265 |   {
  132 |    266 |   	int maxVideoRam = Settings::getInstance()->getInt("MaxVideoRAM");
  133 |    267 |   	if (maxVideoRam <= 0)
  134 |        | - 		maxVideoRam = 768;
      |    268 | + 	{
      |    269 | + 		const int maxRam = Settings::getInstance()->getInt("MaxRAM");
      |    270 | + 		maxVideoRam = Math::max(64, Math::min(768, maxRam > 0 ? maxRam / 4 : 128));
      |    271 | + 	}
  135 |    272 |   
  136 |    273 |   	return maxVideoRam;
  137 |    274 |   }
```

## Trecho 8: antes 144, depois 281

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L144) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L281)

```text
ANTES | DEPOIS |   CÓDIGO
  144 |    281 |   	return (size_t)mVideoWidth * (size_t)mVideoHeight * 4 * 2;
  145 |    282 |   }
  146 |    283 |   
  147 |        | - size_t VideoVlcComponent::getActiveVideoBufferBytes()
      |    284 | + size_t VideoVlcComponent::estimatePendingVideoBufferBytes() const
  148 |    285 |   {
  149 |        | - 	std::unique_lock<std::mutex> lock(sActivePlayersMutex);
  150 |        | - 	size_t total = 0;
      |    286 | + 	int width = Renderer::getScreenWidth();
      |    287 | + 	int height = Renderer::getScreenHeight();
      |    288 | + 	if (width <= 0 || height <= 0)
      |    289 | + 		width = 1280, height = 720;
  151 |    290 |   
  152 |        | - 	for (const auto& player : sActivePlayers)
      |    291 | + 	// OptimizeVideo asks VLC to decode at the component's target size.  Reserving
      |    292 | + 	// that size prevents a row of small cells from each claiming a full-screen
      |    293 | + 	// buffer while still falling back to a conservative screen-sized estimate.
      |    294 | + 	if (Settings::getInstance()->getBool("OptimizeVideo"))
  153 |    295 |   	{
  154 |        | - 		if (player.component != nullptr)
  155 |        | - 			total += player.component->getVideoBufferBytes();
      |    296 | + 		if (mTargetSize.x() > 0)
      |    297 | + 			width = Math::min(width, Math::max(1, (int)std::ceil(mTargetSize.x())));
      |    298 | + 		if (mTargetSize.y() > 0)
      |    299 | + 			height = Math::min(height, Math::max(1, (int)std::ceil(mTargetSize.y())));
  156 |    300 |   	}
  157 |    301 |   
  158 |        | - 	return total;
      |    302 | + 	return (size_t)width * (size_t)height * 4 * 2;
  159 |    303 |   }
  160 |    304 |   
  161 |        | - size_t VideoVlcComponent::estimatePendingVideoBufferBytes()
      |    305 | + size_t VideoVlcComponent::getBufferPoolCacheLimitBytes(size_t maxVideoBytes)
  162 |    306 |   {
  163 |        | - 	int width = Renderer::getScreenWidth();
  164 |        | - 	int height = Renderer::getScreenHeight();
  165 |        | - 	if (width <= 0 || height <= 0)
  166 |        | - 		width = 1280, height = 720;
      |    307 | + 	const size_t maxCacheBytes = (size_t)128 * 1024 * 1024;
      |    308 | + 	return std::min(maxCacheBytes, maxVideoBytes / 4);
      |    309 | + }
  167 |    310 |   
  168 |        | - 	return (size_t)width * (size_t)height * 4 * 2;
      |    311 | + void VideoVlcComponent::trimBufferPoolLocked(size_t maxFreeBytes, size_t maxTotalBytes)
      |    312 | + {
      |    313 | + 	size_t totalBytes = 0;
      |    314 | + 	size_t freeBytes = 0;
      |    315 | + 	for (const auto& entry : sVideoBufferPool)
      |    316 | + 	{
      |    317 | + 		if (entry.surfaces[0] == nullptr)
      |    318 | + 			continue;
      |    319 | + 
      |    320 | + 		const size_t bytes = (size_t)entry.width * (size_t)entry.height * 4 * 2;
      |    321 | + 		totalBytes += bytes;
      |    322 | + 		if (!entry.inUse)
      |    323 | + 			freeBytes += bytes;
      |    324 | + 	}
      |    325 | + 
      |    326 | + 	while (freeBytes > maxFreeBytes || totalBytes > maxTotalBytes)
      |    327 | + 	{
      |    328 | + 		int oldestIndex = -1;
      |    329 | + 		unsigned long long oldestUse = 0;
      |    330 | + 		for (int i = 0; i < (int)sVideoBufferPool.size(); ++i)
      |    331 | + 		{
      |    332 | + 			const VideoBufferPoolEntry& entry = sVideoBufferPool[i];
      |    333 | + 			if (entry.inUse || entry.surfaces[0] == nullptr)
      |    334 | + 				continue;
      |    335 | + 
      |    336 | + 			if (oldestIndex < 0 || entry.lastUsed < oldestUse)
      |    337 | + 			{
      |    338 | + 				oldestIndex = i;
      |    339 | + 				oldestUse = entry.lastUsed;
      |    340 | + 			}
      |    341 | + 		}
      |    342 | + 
      |    343 | + 		if (oldestIndex < 0)
      |    344 | + 			break;
      |    345 | + 
      |    346 | + 		VideoBufferPoolEntry& entry = sVideoBufferPool[oldestIndex];
      |    347 | + 		const size_t bytes = (size_t)entry.width * (size_t)entry.height * 4 * 2;
      |    348 | + 		delete[] entry.surfaces[0];
      |    349 | + 		delete[] entry.surfaces[1];
      |    350 | + 		entry.surfaces[0] = nullptr;
      |    351 | + 		entry.surfaces[1] = nullptr;
      |    352 | + 		entry.width = 0;
      |    353 | + 		entry.height = 0;
      |    354 | + 		entry.inUse = false;
      |    355 | + 		entry.retiring = false;
      |    356 | + 		entry.carouselVideo = false;
      |    357 | + 		entry.countAgainstConcurrentLimit = false;
      |    358 | + 		entry.lastUsed = ++sBufferPoolUseCounter;
      |    359 | + 		totalBytes -= bytes;
      |    360 | + 		freeBytes -= bytes;
      |    361 | + 	}
      |    362 | + }
      |    363 | + 
      |    364 | + bool VideoVlcComponent::isCarouselVideo() const
      |    365 | + {
      |    366 | + 	const std::string& tag = getTag();
      |    367 | + 	return tag == "carouselCellVideo" || tag == "frontSystemCarouselVideo";
      |    368 | + }
      |    369 | + 
      |    370 | + bool VideoVlcComponent::isThemeManagedVideo()
      |    371 | + {
      |    372 | + 	return !isCarouselVideo() && getExtraType() != ExtraType::BUILTIN;
      |    373 | + }
      |    374 | + 
      |    375 | + bool VideoVlcComponent::shouldPlayAudio()
      |    376 | + {
      |    377 | + 	// Carousel and other setPlayAudio(false) decorations receive :no-audio,
      |    378 | + 	// while the selected game's normal preview keeps the existing audio policy.
      |    379 | + 	return !isCarouselVideo() && getPlayAudio() &&
      |    380 | + 		(mScreensaverMode || Settings::getInstance()->getBool("VideoAudio")) &&
      |    381 | + 		!(mScreensaverMode && Settings::getInstance()->getBool("ScreenSaverVideoMute"));
  169 |    382 |   }
  170 |    383 |   
  171 |    384 |   void VideoVlcComponent::releaseContext(VideoContext* ctx)
```

## Trecho 9: antes 173, depois 386

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L173) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L386)

```text
ANTES | DEPOIS |   CÓDIGO
  173 |    386 |   	if (ctx == nullptr)
  174 |    387 |   		return;
  175 |    388 |   
  176 |        | - 	std::unique_lock<std::mutex> lock(sBufferPoolMutex);
  177 |        | - 
  178 |        | - 	if (ctx->poolIndex >= 0 && ctx->poolIndex < (int)sVideoBufferPool.size())
  179 |    389 |   	{
  180 |        | - 		sVideoBufferPool[ctx->poolIndex].inUse = false;
      |    390 | + 		std::unique_lock<std::mutex> lock(sBufferPoolMutex);
      |    391 | + 		bool returnedToPool = false;
      |    392 | + 		if (ctx->poolIndex >= 0 && ctx->poolIndex < (int)sVideoBufferPool.size())
      |    393 | + 		{
      |    394 | + 			VideoBufferPoolEntry& entry = sVideoBufferPool[ctx->poolIndex];
      |    395 | + 			if (entry.surfaces[0] == ctx->surfaces[0] && entry.surfaces[1] == ctx->surfaces[1])
      |    396 | + 			{
      |    397 | + 				entry.inUse = false;
      |    398 | + 				entry.retiring = false;
      |    399 | + 				entry.lastUsed = ++sBufferPoolUseCounter;
      |    400 | + 				returnedToPool = true;
      |    401 | + 			}
      |    402 | + 		}
      |    403 | + 
      |    404 | + 		if (!returnedToPool)
      |    405 | + 			ctx->poolIndex = -1;
      |    406 | + 
  181 |    407 |   		delete ctx;
  182 |        | - 		return;
      |    408 | + 		trimBufferPoolLocked(getBufferPoolCacheLimitBytes(sVideoBufferBudgetBytes),
      |    409 | + 			sVideoBufferBudgetBytes);
  183 |    410 |   	}
  184 |    411 |   
  185 |        | - 	if (ctx->surfaces[0] != nullptr)
      |    412 | + 	// A retiring VLC player remains in both the byte and slot budgets until this
      |    413 | + 	// point. Deferred components poll on their short retry timer; the worker does
      |    414 | + 	// not touch component state from its background thread.
      |    415 | + }
      |    416 | + 
      |    417 | + void VideoVlcComponent::clearBufferPool()
      |    418 | + {
      |    419 | + 	std::lock_guard<std::mutex> lock(sBufferPoolMutex);
      |    420 | + 	for (auto& entry : sVideoBufferPool)
      |    421 | + 	{
      |    422 | + 		// At normal shutdown the release queue has drained every in-use context.
      |    423 | + 		// Retain a defensive check so a future static video cannot free live pixels.
      |    424 | + 		if (entry.inUse)
      |    425 | + 			continue;
      |    426 | + 
      |    427 | + 		delete[] entry.surfaces[0];
      |    428 | + 		delete[] entry.surfaces[1];
      |    429 | + 		entry.surfaces[0] = nullptr;
      |    430 | + 		entry.surfaces[1] = nullptr;
      |    431 | + 		entry.width = 0;
      |    432 | + 		entry.height = 0;
      |    433 | + 		entry.retiring = false;
      |    434 | + 	}
      |    435 | + 	sVideoBufferPool.erase(std::remove_if(sVideoBufferPool.begin(), sVideoBufferPool.end(),
      |    436 | + 		[](const VideoBufferPoolEntry& entry) { return !entry.inUse; }), sVideoBufferPool.end());
      |    437 | + }
      |    438 | + 
      |    439 | + bool VideoVlcComponent::updatePlaybackReservation(size_t bytes)
      |    440 | + {
      |    441 | + 	const size_t maxVideoBytes = (size_t)getMaxVideoRamMb() * 1024 * 1024;
      |    442 | + 	std::unique_lock<std::mutex> playersLock(sActivePlayersMutex);
      |    443 | + 	if (!mIsRegisteredActive)
      |    444 | + 		return false;
      |    445 | + 
      |    446 | + 	size_t pendingBytes = 0;
      |    447 | + 	for (const auto& player : sActivePlayers)
  186 |    448 |   	{
  187 |        | - 		delete[] ctx->surfaces[0];
  188 |        | - 		ctx->surfaces[0] = nullptr;
      |    449 | + 		if (player.component != nullptr && player.component != this &&
      |    450 | + 			player.component->mContext == nullptr)
      |    451 | + 			pendingBytes += player.component->mReservedVideoBytes;
  189 |    452 |   	}
  190 |    453 |   
  191 |        | - 	if (ctx->surfaces[1] != nullptr)
      |    454 | + 	size_t inUseBytes = 0;
  192 |    455 |   	{
  193 |        | - 		delete[] ctx->surfaces[1];
  194 |        | - 		ctx->surfaces[1] = nullptr;
      |    456 | + 		std::lock_guard<std::mutex> poolLock(sBufferPoolMutex);
      |    457 | + 		sVideoBufferBudgetBytes = maxVideoBytes;
      |    458 | + 		for (const auto& entry : sVideoBufferPool)
      |    459 | + 			if (entry.inUse && entry.surfaces[0] != nullptr)
      |    460 | + 				inUseBytes += (size_t)entry.width * (size_t)entry.height * 4 * 2;
  195 |    461 |   	}
  196 |    462 |   
  197 |        | - 	delete ctx;
      |    463 | + 	if (inUseBytes > maxVideoBytes || pendingBytes > maxVideoBytes - inUseBytes ||
      |    464 | + 		bytes > maxVideoBytes - inUseBytes - pendingBytes)
      |    465 | + 		return false;
      |    466 | + 
      |    467 | + 	mReservedVideoBytes = bytes;
      |    468 | + 	return true;
      |    469 | + }
      |    470 | + 
      |    471 | + void VideoVlcComponent::clearPlaybackDeferred()
      |    472 | + {
      |    473 | + 	std::lock_guard<std::mutex> lock(sActivePlayersMutex);
      |    474 | + 	mPlaybackDeferred = false;
      |    475 | + 	sDeferredPlayers.erase(this);
      |    476 | + }
      |    477 | + 
      |    478 | + void VideoVlcComponent::deferPlayback(unsigned retryDelay)
      |    479 | + {
      |    480 | + 	// startVideoWithDelay marks the component as waiting before calling us. A
      |    481 | + 	// deferred attempt has not actually started, so release that gate or the
      |    482 | + 	// timer can expire without ever re-entering startVideo().
      |    483 | + 	mIsWaitingForVideoToStart = false;
      |    484 | + 	mStartDelayed = false;
      |    485 | + 	std::lock_guard<std::mutex> lock(sActivePlayersMutex);
      |    486 | + 	mPlaybackDeferred = true;
      |    487 | + 	mDeferredRetryTime = SDL_GetTicks() + retryDelay;
      |    488 | + 	sDeferredPlayers.insert(this);
  198 |    489 |   }
  199 |    490 |   
  200 |    491 |   int VideoVlcComponent::computePlaybackPriority()
```

## Trecho 10: antes 225, depois 516

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L225) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L516)

```text
ANTES | DEPOIS |   CÓDIGO
  225 |    516 |   
  226 |    517 |   bool VideoVlcComponent::acquirePlaybackSlot()
  227 |    518 |   {
  228 |        | - 	int priority = computePlaybackPriority();
      |    519 | + 	const int priority = computePlaybackPriority();
  229 |    520 |   	if (priority <= 0)
  230 |    521 |   		return false;
  231 |    522 |   
  232 |        | - 	// Carousel cell videos are decoded at their small on-screen target size and
  233 |        | - 	// already remain bounded to the rendered carousel range. They must not
  234 |        | - 	// compete with the general three-player limit, otherwise adjacent cells keep
  235 |        | - 	// entering the deferred queue and visibly reload while the carousel moves.
  236 |        | - 	// The global video RAM check below remains authoritative for every player.
  237 |        | - 	const auto isDedicatedCarouselVideo = [](VideoVlcComponent* component)
  238 |        | - 	{
  239 |        | - 		if (component == nullptr)
  240 |        | - 			return false;
  241 |        | - 		const std::string& componentTag = component->getTag();
  242 |        | - 		return componentTag == "carouselCellVideo" || componentTag == "frontSystemCarouselVideo";
  243 |        | - 	};
  244 |        | - 	const bool isCarouselCellVideo = isDedicatedCarouselVideo(this);
  245 |        | - 
  246 |        | - 	size_t maxVideoBytes = (size_t)getMaxVideoRamMb() * 1024 * 1024;
  247 |        | - 	size_t activeVideoBytes = getActiveVideoBufferBytes();
  248 |        | - 	size_t pendingVideoBytes = estimatePendingVideoBufferBytes();
  249 |        | - 
  250 |        | - 	if (activeVideoBytes + pendingVideoBytes > maxVideoBytes)
  251 |        | - 		return false;
  252 |        | - 
  253 |        | - 	if (!Settings::getInstance()->getBool("EnforceVideoLimit"))
  254 |        | - 		return true;
  255 |        | - 
  256 |        | - 	if (isCarouselCellVideo)
  257 |        | - 		return true;
  258 |        | - 
  259 |        | - 	int maxVideos = getEffectiveMaxConcurrentVideos();
  260 |        | - 
  261 |        | - 	std::unique_lock<std::mutex> lock(sActivePlayersMutex);
      |    523 | + 	const bool carousel = isCarouselVideo();
      |    524 | + 	const bool themeManaged = isThemeManagedVideo();
      |    525 | + 	const size_t reservationBytes = estimatePendingVideoBufferBytes();
      |    526 | + 	const size_t maxVideoBytes = (size_t)getMaxVideoRamMb() * 1024 * 1024;
  262 |    527 |   
  263 |        | - 	if (mIsRegisteredActive)
  264 |        | - 		return true;
  265 |        | - 
  266 |        | - 	int limitedPlayerCount = 0;
  267 |        | - 	for (const auto& player : sActivePlayers)
  268 |        | - 		if (player.component != nullptr && !isDedicatedCarouselVideo(player.component))
  269 |        | - 			limitedPlayerCount++;
  270 |        | - 
  271 |        | - 	while (limitedPlayerCount >= maxVideos)
      |    528 | + 	for (;;)
  272 |    529 |   	{
  273 |        | - 		int weakestIdx = -1;
  274 |        | - 		int weakestPriority = priority;
      |    530 | + 		VideoVlcComponent* victim = nullptr;
      |    531 | + 		std::unique_lock<std::mutex> playersLock(sActivePlayersMutex);
      |    532 | + 		if (mIsRegisteredActive)
      |    533 | + 			return true;
  275 |    534 |   
  276 |        | - 		for (int i = 0; i < (int)sActivePlayers.size(); i++)
      |    535 | + 		int bucketCount = 0;
      |    536 | + 		for (const auto& player : sActivePlayers)
  277 |    537 |   		{
  278 |        | - 			if (sActivePlayers[i].component == nullptr ||
  279 |        | - 				isDedicatedCarouselVideo(sActivePlayers[i].component))
      |    538 | + 			if (player.component == nullptr)
  280 |    539 |   				continue;
      |    540 | + 			if (carousel && player.component->isCarouselVideo())
      |    541 | + 				bucketCount++;
      |    542 | + 			else if (!carousel && !themeManaged && !player.component->isCarouselVideo() &&
      |    543 | + 				!player.component->isThemeManagedVideo())
      |    544 | + 				bucketCount++;
      |    545 | + 		}
  281 |    546 |   
  282 |        | - 			if (sActivePlayers[i].priority < weakestPriority)
      |    547 | + 		size_t inUseBytes = 0;
      |    548 | + 		int retiringBucketCount = 0;
      |    549 | + 		{
      |    550 | + 			std::lock_guard<std::mutex> poolLock(sBufferPoolMutex);
      |    551 | + 			sVideoBufferBudgetBytes = maxVideoBytes;
      |    552 | + 			trimBufferPoolLocked(getBufferPoolCacheLimitBytes(maxVideoBytes), maxVideoBytes);
      |    553 | + 			for (const auto& entry : sVideoBufferPool)
  283 |    554 |   			{
  284 |        | - 				weakestPriority = sActivePlayers[i].priority;
  285 |        | - 				weakestIdx = i;
      |    555 | + 				if (!entry.inUse || entry.surfaces[0] == nullptr)
      |    556 | + 					continue;
      |    557 | + 				inUseBytes += (size_t)entry.width * (size_t)entry.height * 4 * 2;
      |    558 | + 				if (entry.retiring && ((carousel && entry.carouselVideo) ||
      |    559 | + 					(!carousel && !themeManaged && entry.countAgainstConcurrentLimit)))
      |    560 | + 					retiringBucketCount++;
  286 |    561 |   			}
  287 |    562 |   		}
  288 |    563 |   
  289 |        | - 		if (weakestIdx < 0)
      |    564 | + 		const bool enforceCount = Settings::getInstance()->getBool("EnforceVideoLimit");
      |    565 | + 		const int globalCarouselLimit = getEffectiveMaxConcurrentCarouselVideos();
      |    566 | + 		const int configuredCarouselLimit = mConcurrentPlaybackLimit > 0 && globalCarouselLimit > 0 ?
      |    567 | + 			Math::min(mConcurrentPlaybackLimit, globalCarouselLimit) :
      |    568 | + 			Math::max(mConcurrentPlaybackLimit, globalCarouselLimit);
      |    569 | + 		const int bucketLimit = carousel ? configuredCarouselLimit :
      |    570 | + 			(themeManaged ? 0 : getEffectiveMaxConcurrentVideos());
      |    571 | + 		if (enforceCount && bucketLimit > 0 && bucketCount + retiringBucketCount >= bucketLimit)
      |    572 | + 		{
      |    573 | + 			int weakestIndex = -1;
      |    574 | + 			int weakestPriority = priority;
      |    575 | + 			for (int i = 0; i < (int)sActivePlayers.size(); ++i)
      |    576 | + 			{
      |    577 | + 				VideoVlcComponent* candidate = sActivePlayers[i].component;
      |    578 | + 				if (candidate == nullptr)
      |    579 | + 					continue;
      |    580 | + 
      |    581 | + 				const bool sameBucket = carousel ? candidate->isCarouselVideo() :
      |    582 | + 					(!themeManaged && !candidate->isCarouselVideo() &&
      |    583 | + 						!candidate->isThemeManagedVideo());
      |    584 | + 				if (sameBucket && sActivePlayers[i].priority < weakestPriority)
      |    585 | + 				{
      |    586 | + 					weakestIndex = i;
      |    587 | + 					weakestPriority = sActivePlayers[i].priority;
      |    588 | + 				}
      |    589 | + 			}
      |    590 | + 
      |    591 | + 			if (weakestIndex < 0)
      |    592 | + 				return false;
      |    593 | + 
      |    594 | + 			victim = sActivePlayers[weakestIndex].component;
      |    595 | + 			sActivePlayers.erase(sActivePlayers.begin() + weakestIndex);
      |    596 | + 			victim->mIsRegisteredActive = false;
      |    597 | + 			victim->mReservedVideoBytes = 0;
      |    598 | + 			playersLock.unlock();
      |    599 | + 			victim->stopVideo();
      |    600 | + 			// Its VLC player/context is now retiring and still consumes both the
      |    601 | + 			// decoder token and byte budget. Wait for the release worker instead of
      |    602 | + 			// cascading through every lower-priority player in this bucket.
  290 |    603 |   			return false;
      |    604 | + 		}
  291 |    605 |   
  292 |        | - 		VideoVlcComponent* victim = sActivePlayers[weakestIdx].component;
  293 |        | - 		sActivePlayers.erase(sActivePlayers.begin() + weakestIdx);
  294 |        | - 		victim->mIsRegisteredActive = false;
  295 |        | - 		limitedPlayerCount--;
      |    606 | + 		size_t pendingBytes = 0;
      |    607 | + 		for (const auto& player : sActivePlayers)
      |    608 | + 			if (player.component != nullptr && player.component->mContext == nullptr)
      |    609 | + 				pendingBytes += player.component->mReservedVideoBytes;
  296 |    610 |   
  297 |        | - 		lock.unlock();
  298 |        | - 		victim->stopVideo();
  299 |        | - 		lock.lock();
  300 |        | - 	}
      |    611 | + 		if (inUseBytes > maxVideoBytes || pendingBytes > maxVideoBytes - inUseBytes ||
      |    612 | + 			reservationBytes > maxVideoBytes - inUseBytes - pendingBytes)
      |    613 | + 			return false;
  301 |    614 |   
  302 |        | - 	return true;
      |    615 | + 		sActivePlayers.push_back({ this, priority });
      |    616 | + 		mIsRegisteredActive = true;
      |    617 | + 		mReservedVideoBytes = reservationBytes;
      |    618 | + 		mPlaybackDeferred = false;
      |    619 | + 		sDeferredPlayers.erase(this);
      |    620 | + 		return true;
      |    621 | + 	}
  303 |    622 |   }
  304 |    623 |   
  305 |    624 |   void VideoVlcComponent::registerActivePlayer()
  306 |    625 |   {
  307 |        | - 	if (mIsRegisteredActive)
  308 |        | - 		return;
  309 |        | - 
  310 |    626 |   	std::unique_lock<std::mutex> lock(sActivePlayersMutex);
  311 |        | - 	sActivePlayers.push_back({ this, computePlaybackPriority() });
  312 |        | - 	mIsRegisteredActive = true;
      |    627 | + 	if (!mIsRegisteredActive)
      |    628 | + 	{
      |    629 | + 		// Defensive compatibility for callers that bypassed startVideo().
      |    630 | + 		sActivePlayers.push_back({ this, computePlaybackPriority() });
      |    631 | + 		mIsRegisteredActive = true;
      |    632 | + 		mReservedVideoBytes = getVideoBufferBytes();
      |    633 | + 	}
      |    634 | + 	else
      |    635 | + 	{
      |    636 | + 		for (auto& player : sActivePlayers)
      |    637 | + 			if (player.component == this)
      |    638 | + 				player.priority = computePlaybackPriority();
      |    639 | + 	}
  313 |    640 |   	mPlaybackDeferred = false;
  314 |    641 |   	sDeferredPlayers.erase(this);
  315 |    642 |   }
  316 |    643 |   
  317 |    644 |   void VideoVlcComponent::unregisterActivePlayer()
  318 |    645 |   {
  319 |        | - 	if (!mIsRegisteredActive)
  320 |        | - 		return;
  321 |        | - 
  322 |    646 |   	std::unique_lock<std::mutex> lock(sActivePlayersMutex);
  323 |    647 |   	sActivePlayers.erase(std::remove_if(sActivePlayers.begin(), sActivePlayers.end(),
  324 |    648 |   		[this](const ActiveVideoPlayer& p) { return p.component == this; }), sActivePlayers.end());
      |    649 | + 	const bool wasRegistered = mIsRegisteredActive;
  325 |    650 |   	mIsRegisteredActive = false;
      |    651 | + 	mReservedVideoBytes = 0;
  326 |    652 |   	lock.unlock();
  327 |    653 |   
  328 |        | - 	notifyPlaybackSlotAvailable();
      |    654 | + 	if (wasRegistered)
      |    655 | + 		notifyPlaybackSlotAvailable();
  329 |    656 |   }
  330 |    657 |   
  331 |    658 |   void VideoVlcComponent::notifyPlaybackSlotAvailable()
  332 |    659 |   {
  333 |        | - 	std::vector<VideoVlcComponent*> retry;
  334 |        | - 
  335 |        | - 	{
  336 |        | - 		std::unique_lock<std::mutex> lock(sActivePlayersMutex);
  337 |        | - 		for (auto component : sDeferredPlayers)
  338 |        | - 			if (component != nullptr && component->mPlaybackDeferred)
  339 |        | - 				retry.push_back(component);
  340 |        | - 	}
  341 |        | - 
  342 |        | - 	for (auto component : retry)
  343 |        | - 		component->mDeferredRetryTime = SDL_GetTicks();
      |    660 | + 	// Keep pointer validation and the write under the same lock. In particular,
      |    661 | + 	// the release worker must not retain a raw component pointer past destruction.
      |    662 | + 	std::unique_lock<std::mutex> lock(sActivePlayersMutex);
      |    663 | + 	for (auto component : sDeferredPlayers)
      |    664 | + 		if (component != nullptr && component->mPlaybackDeferred)
      |    665 | + 			component->mDeferredRetryTime = SDL_GetTicks();
  344 |    666 |   }
  345 |    667 |   
  346 |    668 |   Vector2f VideoVlcComponent::getSize() const
```

## Trecho 11: antes 358, depois 680

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L358) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L680)

```text
ANTES | DEPOIS |   CÓDIGO
  358 |    680 |   	return GuiComponent::getSize() * (mBottomRightCrop - mTopLeftCrop);
  359 |    681 |   }
  360 |    682 |   
      |    683 | + void VideoVlcComponent::setSharedVideoSource(VideoVlcComponent* source)
      |    684 | + {
      |    685 | + 	if (mSharedVideoSource == source)
      |    686 | + 		return;
      |    687 | + 
      |    688 | + 	stopVideo();
      |    689 | + 	mSharedVideoSource = source;
      |    690 | + 	mVideoPath.clear();
      |    691 | + 	mPlayingVideoPath.clear();
      |    692 | + 	mTexture = nullptr;
      |    693 | + 	mVideoWidth = 0;
      |    694 | + 	mVideoHeight = 0;
      |    695 | + 	// The source owns playback fade. This component retains its independent
      |    696 | + 	// opacity/storyboard while drawing the already-decoded frame.
      |    697 | + 	mFadeIn = source == nullptr ? 0.0f : 1.0f;
      |    698 | + }
      |    699 | + 
  361 |    700 |   void VideoVlcComponent::setResize(float width, float height)
  362 |    701 |   {
  363 |    702 |   	if (mSize.x() != 0 && mSize.y() != 0 && !mTargetIsMax && !mTargetIsMin && mTargetSize.x() == width && mTargetSize.y() == height)
```

## Trecho 12: antes 399, depois 738

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L399) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L738)

```text
ANTES | DEPOIS |   CÓDIGO
  399 |    738 |   
  400 |    739 |   void VideoVlcComponent::onVideoStarted()
  401 |    740 |   {
      |    741 | + 	resetPlaybackFailures();
  402 |    742 |   	VideoComponent::onVideoStarted();
  403 |    743 |   	resize();
  404 |    744 |   }
```

## Trecho 13: antes 678, depois 1018

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L678) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1018)

```text
ANTES | DEPOIS |   CÓDIGO
  678 |   1018 |   
  679 |   1019 |   	VideoComponent::render(parentTrans);
  680 |   1020 |   
  681 |        | - 	bool initFromPixels = true;
      |   1021 | + 	bool initFromPixels = mSharedVideoSource == nullptr;
      |   1022 | + 	if (mSharedVideoSource != nullptr)
      |   1023 | + 	{
      |   1024 | + 		if (mSharedVideoSource == this || mSharedVideoSource->mTexture == nullptr ||
      |   1025 | + 			!mSharedVideoSource->mTexture->isLoaded())
      |   1026 | + 			return;
  682 |   1027 |   
  683 |        | - 	if (!mIsPlaying || !mContext || mIsParsing)
      |   1028 | + 		const bool dimensionsChanged = mVideoWidth != mSharedVideoSource->mVideoWidth ||
      |   1029 | + 			mVideoHeight != mSharedVideoSource->mVideoHeight;
      |   1030 | + 		mTexture = mSharedVideoSource->mTexture;
      |   1031 | + 		mVideoWidth = mSharedVideoSource->mVideoWidth;
      |   1032 | + 		mVideoHeight = mSharedVideoSource->mVideoHeight;
      |   1033 | + 		if (dimensionsChanged)
      |   1034 | + 			resize();
      |   1035 | + 	}
      |   1036 | + 
      |   1037 | + 	if (mSharedVideoSource == nullptr && (!mIsPlaying || !mContext || mIsParsing))
  684 |   1038 |   	{
  685 |   1039 |   		// If video is still attached to the path & texture is initialized, we suppose it had just been stopped (onhide, ondisable, screensaver...)
  686 |   1040 |   		// still render the last frame
```

## Trecho 14: antes 714, depois 1068

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L714) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1068)

```text
ANTES | DEPOIS |   CÓDIGO
  714 |   1068 |   	// Build a texture for the video frame
  715 |   1069 |   	if (initFromPixels)
  716 |   1070 |   	{		
  717 |        | - 		int frame = mContext->surfaceId;
  718 |        | - 		if (mContext->hasFrame[frame])
      |   1071 | + 		int frame = mContext->surfaceId.load(std::memory_order_acquire);
      |   1072 | + 		std::lock_guard<std::mutex> frameLock(mContext->mutexes[frame]);
      |   1073 | + 		if (mContext->hasFrame[frame].load(std::memory_order_acquire))
  719 |   1074 |   		{
  720 |   1075 |   			if (mTexture == nullptr)
  721 |   1076 |   			{
```

## Trecho 15: antes 730, depois 1085

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L730) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1085)

```text
ANTES | DEPOIS |   CÓDIGO
  730 |   1085 |   			if (!Settings::getInstance()->getBool("OptimizeVideo") || mElapsed >= 33)
  731 |   1086 |   #endif
  732 |   1087 |   			{
  733 |        | - 				mContext->mutexes[frame].lock();
  734 |   1088 |   				mTexture->updateFromExternalPixels(mContext->surfaces[frame], mVideoWidth, mVideoHeight);
  735 |        | - 				mContext->hasFrame[frame] = false;
  736 |        | - 				mContext->mutexes[frame].unlock();
      |   1089 | + 				mContext->hasFrame[frame].store(false, std::memory_order_release);
  737 |   1090 |   
  738 |   1091 |   				mElapsed = 0;
  739 |   1092 |   			}
```

## Trecho 16: antes 823, depois 1176

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L823) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1176)

```text
ANTES | DEPOIS |   CÓDIGO
  823 |   1176 |   	ctx->poolIndex = -1;
  824 |   1177 |   	ctx->bufferWidth = mVideoWidth;
  825 |   1178 |   	ctx->bufferHeight = mVideoHeight;
      |   1179 | + 	ctx->carouselVideo = isCarouselVideo();
      |   1180 | + 	ctx->countAgainstConcurrentLimit = !ctx->carouselVideo && !isThemeManagedVideo();
  826 |   1181 |   	ctx->hasFrame[0] = false;
  827 |   1182 |   	ctx->hasFrame[1] = false;
  828 |   1183 |   	ctx->surfaceId = 0;
  829 |   1184 |   
  830 |   1185 |   	const size_t frameBytes = (size_t)mVideoWidth * (size_t)mVideoHeight * 4;
      |   1186 | + 	const size_t bufferBytes = frameBytes * 2;
      |   1187 | + 	const size_t maxVideoBytes = (size_t)getMaxVideoRamMb() * 1024 * 1024;
      |   1188 | + 
      |   1189 | + 	// Keep the lock order consistent with reservation checks: players, then pool.
      |   1190 | + 	std::unique_lock<std::mutex> playersLock(sActivePlayersMutex);
      |   1191 | + 	size_t otherPendingBytes = 0;
      |   1192 | + 	for (const auto& player : sActivePlayers)
      |   1193 | + 		if (player.component != nullptr && player.component != this &&
      |   1194 | + 			player.component->mContext == nullptr)
      |   1195 | + 			otherPendingBytes += player.component->mReservedVideoBytes;
  831 |   1196 |   
  832 |        | - 	std::unique_lock<std::mutex> lock(sBufferPoolMutex);
      |   1197 | + 	std::unique_lock<std::mutex> poolLock(sBufferPoolMutex);
      |   1198 | + 	sVideoBufferBudgetBytes = maxVideoBytes;
      |   1199 | + 	trimBufferPoolLocked(getBufferPoolCacheLimitBytes(maxVideoBytes), maxVideoBytes);
  833 |   1200 |   
  834 |   1201 |   	for (int i = 0; i < (int)sVideoBufferPool.size(); i++)
  835 |   1202 |   	{
  836 |   1203 |   		VideoBufferPoolEntry& entry = sVideoBufferPool[i];
  837 |        | - 		if (!entry.inUse && entry.width == mVideoWidth && entry.height == mVideoHeight)
      |   1204 | + 		if (!entry.inUse && entry.surfaces[0] != nullptr &&
      |   1205 | + 			entry.width == (int)mVideoWidth && entry.height == (int)mVideoHeight)
  838 |   1206 |   		{
  839 |   1207 |   			ctx->surfaces[0] = entry.surfaces[0];
  840 |   1208 |   			ctx->surfaces[1] = entry.surfaces[1];
  841 |   1209 |   			ctx->poolIndex = i;
  842 |   1210 |   			entry.inUse = true;
  843 |        | - 			lock.unlock();
      |   1211 | + 			entry.retiring = false;
      |   1212 | + 			entry.carouselVideo = ctx->carouselVideo;
      |   1213 | + 			entry.countAgainstConcurrentLimit = ctx->countAgainstConcurrentLimit;
      |   1214 | + 			entry.lastUsed = ++sBufferPoolUseCounter;
      |   1215 | + 			mContext = ctx;
      |   1216 | + 			poolLock.unlock();
      |   1217 | + 			playersLock.unlock();
  844 |   1218 |   			resize();
  845 |   1219 |   			return ctx;
  846 |   1220 |   		}
  847 |   1221 |   	}
  848 |   1222 |   
  849 |        | - 	ctx->surfaces[0] = new unsigned char[frameBytes];
  850 |        | - 	ctx->surfaces[1] = new unsigned char[frameBytes];
      |   1223 | + 	if (otherPendingBytes > maxVideoBytes || bufferBytes > maxVideoBytes - otherPendingBytes)
      |   1224 | + 	{
      |   1225 | + 		poolLock.unlock();
      |   1226 | + 		playersLock.unlock();
      |   1227 | + 		delete ctx;
      |   1228 | + 		return nullptr;
      |   1229 | + 	}
      |   1230 | + 
      |   1231 | + 	// Free LRU idle buffers until this allocation plus every parser reservation
      |   1232 | + 	// fits. Retiring entries are in-use and therefore cannot be evicted early.
      |   1233 | + 	const size_t maxExistingBytes = maxVideoBytes - otherPendingBytes - bufferBytes;
      |   1234 | + 	trimBufferPoolLocked(getBufferPoolCacheLimitBytes(maxVideoBytes), maxExistingBytes);
      |   1235 | + 
      |   1236 | + 	size_t allocatedBytes = 0;
      |   1237 | + 	for (const auto& entry : sVideoBufferPool)
      |   1238 | + 		if (entry.surfaces[0] != nullptr)
      |   1239 | + 			allocatedBytes += (size_t)entry.width * (size_t)entry.height * 4 * 2;
      |   1240 | + 
      |   1241 | + 	if (allocatedBytes > maxExistingBytes)
      |   1242 | + 	{
      |   1243 | + 		poolLock.unlock();
      |   1244 | + 		playersLock.unlock();
      |   1245 | + 		delete ctx;
      |   1246 | + 		return nullptr;
      |   1247 | + 	}
      |   1248 | + 
      |   1249 | + 	ctx->surfaces[0] = new (std::nothrow) unsigned char[frameBytes];
      |   1250 | + 	ctx->surfaces[1] = new (std::nothrow) unsigned char[frameBytes];
      |   1251 | + 	if (ctx->surfaces[0] == nullptr || ctx->surfaces[1] == nullptr)
      |   1252 | + 	{
      |   1253 | + 		poolLock.unlock();
      |   1254 | + 		playersLock.unlock();
      |   1255 | + 		delete ctx;
      |   1256 | + 		return nullptr;
      |   1257 | + 	}
  851 |   1258 |   
  852 |        | - 	if ((int)sVideoBufferPool.size() < MAX_VIDEO_BUFFER_POOL_SIZE)
      |   1259 | + 	int poolIndex = -1;
      |   1260 | + 	for (int i = 0; i < (int)sVideoBufferPool.size(); ++i)
      |   1261 | + 	{
      |   1262 | + 		if (!sVideoBufferPool[i].inUse && sVideoBufferPool[i].surfaces[0] == nullptr)
      |   1263 | + 		{
      |   1264 | + 			poolIndex = i;
      |   1265 | + 			break;
      |   1266 | + 		}
      |   1267 | + 	}
      |   1268 | + 
      |   1269 | + 	VideoBufferPoolEntry entry;
      |   1270 | + 	entry.width = mVideoWidth;
      |   1271 | + 	entry.height = mVideoHeight;
      |   1272 | + 	entry.surfaces[0] = ctx->surfaces[0];
      |   1273 | + 	entry.surfaces[1] = ctx->surfaces[1];
      |   1274 | + 	entry.inUse = true;
      |   1275 | + 	entry.retiring = false;
      |   1276 | + 	entry.carouselVideo = ctx->carouselVideo;
      |   1277 | + 	entry.countAgainstConcurrentLimit = ctx->countAgainstConcurrentLimit;
      |   1278 | + 	entry.lastUsed = ++sBufferPoolUseCounter;
      |   1279 | + 
      |   1280 | + 	if (poolIndex >= 0)
      |   1281 | + 		sVideoBufferPool[poolIndex] = entry;
      |   1282 | + 	else
  853 |   1283 |   	{
  854 |        | - 		VideoBufferPoolEntry entry;
  855 |        | - 		entry.width = mVideoWidth;
  856 |        | - 		entry.height = mVideoHeight;
  857 |        | - 		entry.surfaces[0] = ctx->surfaces[0];
  858 |        | - 		entry.surfaces[1] = ctx->surfaces[1];
  859 |        | - 		entry.inUse = true;
  860 |   1284 |   		sVideoBufferPool.push_back(entry);
  861 |        | - 		ctx->poolIndex = (int)sVideoBufferPool.size() - 1;
      |   1285 | + 		poolIndex = (int)sVideoBufferPool.size() - 1;
  862 |   1286 |   	}
  863 |   1287 |   
  864 |        | - 	lock.unlock();
      |   1288 | + 	ctx->poolIndex = poolIndex;
      |   1289 | + 	mContext = ctx;
      |   1290 | + 	poolLock.unlock();
      |   1291 | + 	playersLock.unlock();
  865 |   1292 |   	resize();
  866 |   1293 |   	return ctx;
  867 |   1294 |   }
```

## Trecho 17: antes 951, depois 1378

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L951) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1378)

```text
ANTES | DEPOIS |   CÓDIGO
  951 |   1378 |   	delete[] theArgs;
  952 |   1379 |   }
  953 |   1380 |   
  954 |        | - void VideoVlcComponent::handleLooping()
      |   1381 | + bool VideoVlcComponent::createMedia(bool forceSoftwareDecoder)
  955 |   1382 |   {
  956 |        | - 	if (mIsPlaying && mMediaPlayer && mMedia && !mIsParsing)
  957 |        | - 	{
  958 |        | - 		libvlc_state_t state = libvlc_media_player_get_state(mMediaPlayer);
  959 |        | - 		const bool isCarouselCellVideo =
  960 |        | - 			getTag() == "carouselCellVideo" || getTag() == "frontSystemCarouselVideo";
      |   1383 | + #ifdef WIN32
      |   1384 | + 	const std::string path = Utils::String::replace(mVideoPath, "/", "\\");
      |   1385 | + #else
      |   1386 | + 	const std::string path = mVideoPath;
      |   1387 | + #endif
  961 |   1388 |   
  962 |        | - 		if (isCarouselCellVideo)
  963 |        | - 		{
  964 |        | - 			const unsigned int now = SDL_GetTicks();
  965 |        | - 			const long long playbackTime = (long long)libvlc_media_player_get_time(mMediaPlayer);
      |   1389 | + 	mMedia = libvlc_media_new_path(mVLC, path.c_str());
      |   1390 | + 	if (mMedia == nullptr)
      |   1391 | + 		return false;
  966 |   1392 |   
  967 |        | - 			if (playbackTime >= 0 &&
  968 |        | - 				(mLastPlaybackTime < 0 || playbackTime > mLastPlaybackTime ||
  969 |        | - 					playbackTime + 500 < mLastPlaybackTime))
      |   1393 | + 	bool explicitHardwareOption = false;
      |   1394 | + 	bool explicitSoftwareDecoder = false;
      |   1395 | + 	const std::string options = SystemConf::getInstance()->get("vlc.options");
      |   1396 | + 	if (!options.empty())
      |   1397 | + 	{
      |   1398 | + 		for (const auto& token : Utils::String::split(options, ' '))
      |   1399 | + 		{
      |   1400 | + 			if (token.empty())
      |   1401 | + 				continue;
      |   1402 | + 			libvlc_media_add_option(mMedia, token.c_str());
      |   1403 | + 			if (token.find("avcodec-hw=") != std::string::npos)
  970 |   1404 |   			{
  971 |        | - 				mLastPlaybackTime = playbackTime;
  972 |        | - 				mLastPlaybackProgressTick = now;
      |   1405 | + 				explicitHardwareOption = true;
      |   1406 | + 				explicitSoftwareDecoder = token.find("avcodec-hw=none") != std::string::npos;
  973 |   1407 |   			}
      |   1408 | + 		}
      |   1409 | + 	}
      |   1410 | + 
      |   1411 | + #if WIN32
      |   1412 | + 	if (forceSoftwareDecoder)
      |   1413 | + 	{
      |   1414 | + 		// Added after custom options so the one-shot fallback is authoritative.
      |   1415 | + 		libvlc_media_add_option(mMedia, ":avcodec-hw=none");
      |   1416 | + 		mUsingHardwareDecoder = false;
      |   1417 | + 	}
      |   1418 | + 	else
      |   1419 | + 	{
      |   1420 | + 		if (!explicitHardwareOption)
      |   1421 | + 			libvlc_media_add_option(mMedia, ":avcodec-hw=any");
      |   1422 | + 		mUsingHardwareDecoder = !explicitSoftwareDecoder;
      |   1423 | + 	}
      |   1424 | + 	libvlc_media_add_option(mMedia, ":no-spu");
      |   1425 | + #else
      |   1426 | + 	(void)forceSoftwareDecoder;
      |   1427 | + 	mUsingHardwareDecoder = false;
      |   1428 | + #endif
  974 |   1429 |   
  975 |        | - 			const bool terminalState =
  976 |        | - 				state == libvlc_Ended || state == libvlc_Stopped || state == libvlc_Error;
  977 |        | - 			const bool unexpectedlyPaused =
  978 |        | - 				state == libvlc_Paused && now - mLastPlaybackProgressTick >= 1500;
  979 |        | - 			const bool stalledWhilePlaying =
  980 |        | - 				state == libvlc_Playing && playbackTime >= 0 &&
  981 |        | - 				now - mLastPlaybackProgressTick >= 4000;
      |   1430 | + 	if (!shouldPlayAudio())
      |   1431 | + 	{
      |   1432 | + 		// Decorative menu videos explicitly use setPlayAudio(false), carousel tags
      |   1433 | + 		// are always silent, and a disabled global audio option should also avoid
      |   1434 | + 		// creating an audio decoder. The selected game's preview keeps its policy.
      |   1435 | + 		libvlc_media_add_option(mMedia, ":no-audio");
      |   1436 | + 	}
      |   1437 | + #if WIN32
      |   1438 | + 	if (isCarouselVideo())
      |   1439 | + 		libvlc_media_add_option(mMedia, ":input-repeat=65535");
      |   1440 | + #endif
  982 |   1441 |   
  983 |        | - 			if ((terminalState || unexpectedlyPaused || stalledWhilePlaying) &&
  984 |        | - 				now - mLastPlaybackRestartTick >= 1000)
  985 |        | - 			{
  986 |        | - 				// VLC can keep reporting the component as playing while a short cell
  987 |        | - 				// video is already stopped on its final frame. Reattach the same media
  988 |        | - 				// and restart only this dedicated cell player.
  989 |        | - 				libvlc_media_player_set_media(mMediaPlayer, mMedia);
  990 |        | - 				if (!getPlayAudio() || !Settings::getInstance()->getBool("VideoAudio"))
  991 |        | - 					libvlc_audio_set_mute(mMediaPlayer, 1);
  992 |        | - 				libvlc_media_player_play(mMediaPlayer);
  993 |        | - 
  994 |        | - 				mLastPlaybackTime = -1;
  995 |        | - 				mLastPlaybackProgressTick = now;
  996 |        | - 				mLastPlaybackRestartTick = now;
  997 |        | - 			}
      |   1442 | + 	if (mPlaylist != nullptr && mConfig.startDelay == 0 &&
      |   1443 | + 		!mConfig.showSnapshotDelay && !mConfig.showSnapshotNoVideo)
      |   1444 | + 		libvlc_media_add_option(mMedia, ":start-time=0.7");
  998 |   1445 |   
  999 |        | - 			return;
      |   1446 | + 	mIsParsing = false;
      |   1447 | + #if LIBVLC_VERSION_MAJOR >= 3
      |   1448 | + 	#if WIN32
      |   1449 | + 		const char* vlcVersion = libvlc_get_version();
      |   1450 | + 		if (vlcVersion[0] < '3')
      |   1451 | + 			libvlc_media_parse(mMedia);
      |   1452 | + 		else
      |   1453 | + 	#endif
      |   1454 | + 	{
      |   1455 | + 		const int parseResult = libvlc_media_parse_with_options(
      |   1456 | + 			mMedia, libvlc_media_parse_local, 5000);
      |   1457 | + 		if (parseResult != 0)
      |   1458 | + 		{
      |   1459 | + 			LOG(LogWarning) << "[VideoVlcComponent] failed to start media parsing: " << mVideoPath;
      |   1460 | + 			libvlc_media_release(mMedia);
      |   1461 | + 			mMedia = nullptr;
      |   1462 | + 			return false;
      |   1463 | + 		}
      |   1464 | + 		if ((int)libvlc_media_get_parsed_status(mMedia) == 0)
      |   1465 | + 		{
      |   1466 | + 			mIsParsing = true;
      |   1467 | + 			return true;
 1000 |   1468 |   		}
      |   1469 | + 	}
      |   1470 | + #else
      |   1471 | + 	libvlc_media_parse(mMedia);
      |   1472 | + #endif
 1001 |   1473 |   
 1002 |        | - 		if (state == libvlc_Ended)
      |   1474 | + 	onMediaParsed();
      |   1475 | + 	return mIsParsing || mMediaPlayer != nullptr;
      |   1476 | + }
      |   1477 | + 
      |   1478 | + void VideoVlcComponent::releaseMediaForDecoderRetry()
      |   1479 | + {
      |   1480 | + 	if (mAudioPlaybackRegistered)
      |   1481 | + 	{
      |   1482 | + 		AudioManager::setVideoPlaying(false);
      |   1483 | + 		mAudioPlaybackRegistered = false;
      |   1484 | + 	}
      |   1485 | + 
      |   1486 | + 	if (mMediaPlayer != nullptr)
      |   1487 | + 	{
      |   1488 | + 		// The release worker is deliberately serialized and can have a short
      |   1489 | + 		// backlog while a carousel is moving. Silence this player immediately so
      |   1490 | + 		// audio from the previous selection cannot continue until its release job
      |   1491 | + 		// reaches the front of the queue.
      |   1492 | + 		libvlc_audio_set_mute(mMediaPlayer, 1);
      |   1493 | + 		queueMediaPlayerRelease(mContext, mMediaPlayer);
      |   1494 | + 	}
      |   1495 | + 	else if (mContext != nullptr)
      |   1496 | + 		releaseContext(mContext);
      |   1497 | + 
      |   1498 | + 	mMediaPlayer = nullptr;
      |   1499 | + 	mContext = nullptr;
      |   1500 | + 	if (mMedia != nullptr)
      |   1501 | + 		libvlc_media_release(mMedia);
      |   1502 | + 	mMedia = nullptr;
      |   1503 | + 	mIsParsing = false;
      |   1504 | + 	mIsPlaying = false;
      |   1505 | + 	mIsWaitingForVideoToStart = true;
      |   1506 | + 	mTexture = nullptr;
      |   1507 | + 	mVideoWidth = 0;
      |   1508 | + 	mVideoHeight = 0;
      |   1509 | + 	mHasAudioTrack = false;
      |   1510 | + 	mLastPlaybackTime = -1;
      |   1511 | + 	mLastPlaybackProgressTick = SDL_GetTicks();
      |   1512 | + 	mLastPlaybackRestartTick = 0;
      |   1513 | + 	mPlaybackStartedTick = 0;
      |   1514 | + 	mPlaybackRestartAttempts = 0;
      |   1515 | + }
      |   1516 | + 
      |   1517 | + bool VideoVlcComponent::trySoftwareDecoderFallback()
      |   1518 | + {
      |   1519 | + #if WIN32
      |   1520 | + 	if (!mUsingHardwareDecoder || mHardwareFallbackAttempted)
      |   1521 | + 		return false;
      |   1522 | + 
      |   1523 | + 	mHardwareFallbackAttempted = true;
      |   1524 | + 	mSoftwareDecoderPath = mVideoPath;
      |   1525 | + 	LOG(LogWarning) << "[VideoVlcComponent] hardware decoding failed; retrying once in software: "
      |   1526 | + 		<< mVideoPath;
      |   1527 | + 	// The old decoder remains alive until the release worker finishes. Re-enter
      |   1528 | + 	// through the normal slot allocator so that retiring hardware + replacement
      |   1529 | + 	// software never exceed the XML decoder count or RAM budget.
      |   1530 | + 	stopVideo();
      |   1531 | + 	deferPlayback(100);
      |   1532 | + 	return true;
      |   1533 | + #else
      |   1534 | + 	return false;
      |   1535 | + #endif
      |   1536 | + }
      |   1537 | + 
      |   1538 | + void VideoVlcComponent::resetPlaybackFailures()
      |   1539 | + {
      |   1540 | + 	mPlaybackFailurePath.clear();
      |   1541 | + 	mPlaybackFailureCount = 0;
      |   1542 | + 	mPlaybackFailureBlockedUntil = 0;
      |   1543 | + }
      |   1544 | + 
      |   1545 | + void VideoVlcComponent::failPlayback(unsigned retryDelay, bool countFailure)
      |   1546 | + {
      |   1547 | + 	const std::string failedPath = mVideoPath;
      |   1548 | + 	if (countFailure)
      |   1549 | + 	{
      |   1550 | + 		if (mPlaybackFailurePath != failedPath)
 1003 |   1551 |   		{
 1004 |        | - 			if (mLoops >= 0)
 1005 |        | - 			{
 1006 |        | - 				mCurrentLoop++;
 1007 |        | - 				if (mCurrentLoop > mLoops)
 1008 |        | - 				{
 1009 |        | - 					stopVideo();
      |   1552 | + 			mPlaybackFailurePath = failedPath;
      |   1553 | + 			mPlaybackFailureCount = 0;
      |   1554 | + 		}
      |   1555 | + 		mPlaybackFailureCount++;
      |   1556 | + 	}
 1010 |   1557 |   
 1011 |        | - 					mFadeIn = 0.0;
 1012 |        | - 					mPlayingVideoPath = "";
 1013 |        | - 					mVideoPath = "";
 1014 |        | - 					return;
 1015 |        | - 				}
 1016 |        | - 			}
      |   1558 | + 	stopVideo();
      |   1559 | + 	if (failedPath.empty())
      |   1560 | + 		return;
 1017 |   1561 |   
 1018 |        | - 			if (mPlaylist != nullptr)
      |   1562 | + 	if (!countFailure || mPlaybackFailureCount <= 3)
      |   1563 | + 	{
      |   1564 | + 		const unsigned int delay = countFailure ?
      |   1565 | + 			std::min(15000U, retryDelay * (unsigned int)mPlaybackFailureCount) : retryDelay;
      |   1566 | + 		deferPlayback(delay);
      |   1567 | + 		return;
      |   1568 | + 	}
      |   1569 | + 
      |   1570 | + 	// A broken/unsupported file must not be reopened every frame forever. Keep
      |   1571 | + 	// the component cheap, but retry after a long cooldown so replaced media can
      |   1572 | + 	// recover without restarting EmulationStation.
      |   1573 | + 	mPlaybackFailureBlockedUntil = SDL_GetTicks() + 60000;
      |   1574 | + 	LOG(LogWarning) << "[VideoVlcComponent] pausing retries for 60 seconds after repeated failures: "
      |   1575 | + 		<< failedPath;
      |   1576 | + }
      |   1577 | + 
      |   1578 | + void VideoVlcComponent::handleLooping()
      |   1579 | + {
      |   1580 | + 	if (!mIsPlaying || mMediaPlayer == nullptr || mMedia == nullptr || mIsParsing)
      |   1581 | + 		return;
      |   1582 | + 
      |   1583 | + 	const libvlc_state_t state = libvlc_media_player_get_state(mMediaPlayer);
      |   1584 | + 	const unsigned int now = SDL_GetTicks();
      |   1585 | + 	const long long playbackTime = (long long)libvlc_media_player_get_time(mMediaPlayer);
      |   1586 | + 	if (playbackTime >= 0 &&
      |   1587 | + 		(mLastPlaybackTime < 0 || playbackTime > mLastPlaybackTime ||
      |   1588 | + 			playbackTime + 500 < mLastPlaybackTime))
      |   1589 | + 	{
      |   1590 | + 		mLastPlaybackTime = playbackTime;
      |   1591 | + 		mLastPlaybackProgressTick = now;
      |   1592 | + 		mPlaybackRestartAttempts = 0;
      |   1593 | + 	}
      |   1594 | + 
      |   1595 | + 	const bool hardwareStall = mUsingHardwareDecoder && state == libvlc_Playing &&
      |   1596 | + 		playbackTime >= 0 && now - mLastPlaybackProgressTick >= 6000;
      |   1597 | + 	const bool decoderStopped = state == libvlc_Stopped;
      |   1598 | + 	if (state == libvlc_Error || hardwareStall || (decoderStopped && mUsingHardwareDecoder))
      |   1599 | + 	{
      |   1600 | + 		if (trySoftwareDecoderFallback())
      |   1601 | + 			return;
      |   1602 | + 		failPlayback(2000);
      |   1603 | + 		return;
      |   1604 | + 	}
      |   1605 | + 
      |   1606 | + 	if (isCarouselVideo())
      |   1607 | + 	{
      |   1608 | + 		const bool expectedEnd = state == libvlc_Ended;
      |   1609 | + 		const bool terminalState = expectedEnd || decoderStopped;
      |   1610 | + 		const bool unexpectedlyPaused =
      |   1611 | + 			state == libvlc_Paused && now - mLastPlaybackProgressTick >= 1500;
      |   1612 | + 		const bool stalledWhilePlaying = state == libvlc_Playing && playbackTime >= 0 &&
      |   1613 | + 			now - mLastPlaybackProgressTick >= 4000;
      |   1614 | + 
      |   1615 | + 		if ((terminalState || unexpectedlyPaused || stalledWhilePlaying) &&
      |   1616 | + 			now - mLastPlaybackRestartTick >= 1000)
      |   1617 | + 		{
      |   1618 | + 			if (!expectedEnd && ++mPlaybackRestartAttempts > 2)
 1019 |   1619 |   			{
 1020 |        | - 				auto nextVideo = mPlaylist->getNextItem();
 1021 |        | - 				if (!nextVideo.empty())
 1022 |        | - 				{
 1023 |        | - 					stopVideo();
 1024 |        | - 					setVideo(nextVideo);
 1025 |        | - 					return;
 1026 |        | - 				}
 1027 |        | - 				else
 1028 |        | - 					mPlaylist = nullptr;
      |   1620 | + 				failPlayback(2000);
      |   1621 | + 				return;
 1029 |   1622 |   			}
 1030 |        | - 			
 1031 |        | - 			if (mVideoEnded != nullptr)
      |   1623 | + 
      |   1624 | + 			libvlc_media_player_set_media(mMediaPlayer, mMedia);
      |   1625 | + 			if (libvlc_media_player_play(mMediaPlayer) < 0)
 1032 |   1626 |   			{
 1033 |        | - 				bool cont = mVideoEnded();
 1034 |        | - 				if (!cont)
 1035 |        | - 				{
 1036 |        | - 					stopVideo();
 1037 |        | - 					return;
 1038 |        | - 				}
      |   1627 | + 				if (!trySoftwareDecoderFallback())
      |   1628 | + 					failPlayback(2000);
      |   1629 | + 				return;
 1039 |   1630 |   			}
 1040 |   1631 |   
 1041 |        | - 			if (!getPlayAudio() || (!mScreensaverMode && !Settings::getInstance()->getBool("VideoAudio")) || (Settings::getInstance()->getBool("ScreenSaverVideoMute") && mScreensaverMode))
 1042 |        | - 				libvlc_audio_set_mute(mMediaPlayer, 1);
      |   1632 | + 			mLastPlaybackTime = -1;
      |   1633 | + 			mLastPlaybackProgressTick = now;
      |   1634 | + 			mLastPlaybackRestartTick = now;
      |   1635 | + 			mPlaybackStartedTick = now;
      |   1636 | + 		}
      |   1637 | + 		return;
      |   1638 | + 	}
      |   1639 | + 	if (decoderStopped)
      |   1640 | + 	{
      |   1641 | + 		failPlayback(2000);
      |   1642 | + 		return;
      |   1643 | + 	}
      |   1644 | + 
      |   1645 | + 	if (state != libvlc_Ended)
      |   1646 | + 		return;
 1043 |   1647 |   
 1044 |        | - 			//libvlc_media_player_set_position(mMediaPlayer, 0.0f);
 1045 |        | - 			if (mMedia)
 1046 |        | - 				libvlc_media_player_set_media(mMediaPlayer, mMedia);
      |   1648 | + 	if (mLoops >= 0)
      |   1649 | + 	{
      |   1650 | + 		mCurrentLoop++;
      |   1651 | + 		if (mCurrentLoop > mLoops)
      |   1652 | + 		{
      |   1653 | + 			stopVideo();
      |   1654 | + 			mFadeIn = 0.0;
      |   1655 | + 			mPlayingVideoPath = "";
      |   1656 | + 			mVideoPath = "";
      |   1657 | + 			return;
      |   1658 | + 		}
      |   1659 | + 	}
 1047 |   1660 |   
 1048 |        | - 			libvlc_media_player_play(mMediaPlayer);
      |   1661 | + 	if (mPlaylist != nullptr)
      |   1662 | + 	{
      |   1663 | + 		const auto nextVideo = mPlaylist->getNextItem();
      |   1664 | + 		if (!nextVideo.empty())
      |   1665 | + 		{
      |   1666 | + 			stopVideo();
      |   1667 | + 			setVideo(nextVideo);
      |   1668 | + 			return;
 1049 |   1669 |   		}
      |   1670 | + 		mPlaylist = nullptr;
      |   1671 | + 	}
      |   1672 | + 
      |   1673 | + 	if (mVideoEnded != nullptr && !mVideoEnded())
      |   1674 | + 	{
      |   1675 | + 		stopVideo();
      |   1676 | + 		return;
      |   1677 | + 	}
      |   1678 | + 
      |   1679 | + 	if (!shouldPlayAudio())
      |   1680 | + 		libvlc_audio_set_mute(mMediaPlayer, 1);
      |   1681 | + 	libvlc_media_player_set_media(mMediaPlayer, mMedia);
      |   1682 | + 	if (libvlc_media_player_play(mMediaPlayer) < 0)
      |   1683 | + 	{
      |   1684 | + 		if (!trySoftwareDecoderFallback())
      |   1685 | + 			failPlayback(2000);
      |   1686 | + 		return;
 1050 |   1687 |   	}
      |   1688 | + 	mPlaybackStartedTick = now;
 1051 |   1689 |   }
 1052 |   1690 |   
 1053 |   1691 |   void VideoVlcComponent::onMediaParsed()
 1054 |   1692 |   {
 1055 |   1693 |   	StopWatch stopWatch("[VideoVlcComponent] onMediaParsed", LogDebug);
      |   1694 | + 	if (mMedia == nullptr)
      |   1695 | + 		return;
 1056 |   1696 |   
 1057 |   1697 |   	mVideoWidth = 0;
 1058 |   1698 |   	mVideoHeight = 0;
 1059 |        | - 
 1060 |        | - 	bool hasAudioTrack = false;
 1061 |        | - 	unsigned track_count;
 1062 |        | - 
 1063 |        | - 	libvlc_media_track_t** tracks;
 1064 |        | - 	track_count = libvlc_media_tracks_get(mMedia, &tracks);
 1065 |        | - 	for (unsigned track = 0; track < track_count; ++track)
      |   1699 | + 	mHasAudioTrack = false;
      |   1700 | + 	libvlc_media_track_t** tracks = nullptr;
      |   1701 | + 	const unsigned trackCount = libvlc_media_tracks_get(mMedia, &tracks);
      |   1702 | + 	for (unsigned track = 0; track < trackCount; ++track)
 1066 |   1703 |   	{
 1067 |   1704 |   		if (tracks[track]->i_type == libvlc_track_audio)
 1068 |        | - 			hasAudioTrack = true;
      |   1705 | + 			mHasAudioTrack = true;
 1069 |   1706 |   		else if (tracks[track]->i_type == libvlc_track_video)
 1070 |   1707 |   		{
 1071 |   1708 |   			mVideoWidth = tracks[track]->video->i_width;
 1072 |   1709 |   			mVideoHeight = tracks[track]->video->i_height;
 1073 |        | - 
 1074 |        | - 			if (hasAudioTrack)
 1075 |        | - 				break;
 1076 |   1710 |   		}
 1077 |   1711 |   	}
 1078 |        | - 	libvlc_media_tracks_release(tracks, track_count);
      |   1712 | + 	if (tracks != nullptr)
      |   1713 | + 		libvlc_media_tracks_release(tracks, trackCount);
 1079 |   1714 |   
 1080 |        | - 	if (mVideoWidth == 0 && mVideoHeight == 0 && Utils::FileSystem::isAudio(mPlayingVideoPath))
      |   1715 | + 	if (mVideoWidth == 0 && mVideoHeight == 0 &&
      |   1716 | + 		Utils::FileSystem::isAudio(mPlayingVideoPath) && shouldPlayAudio() && !mScreensaverMode)
 1081 |   1717 |   	{
 1082 |        | - 		if (getPlayAudio() && !mScreensaverMode && Settings::getInstance()->getBool("VideoAudio"))
 1083 |        | - 		{
 1084 |        | - 			// Make fake dimension to play audio files
 1085 |        | - 			mVideoWidth = 1;
 1086 |        | - 			mVideoHeight = 1;
 1087 |        | - 		}
      |   1718 | + 		mVideoWidth = 1;
      |   1719 | + 		mVideoHeight = 1;
 1088 |   1720 |   	}
 1089 |   1721 |   
 1090 |        | - 	// Make sure we found a valid video track
 1091 |   1722 |   	if (mVideoWidth <= 0 || mVideoHeight <= 0)
      |   1723 | + 	{
      |   1724 | + 		failPlayback(2000);
 1092 |   1725 |   		return;
      |   1726 | + 	}
 1093 |   1727 |   
 1094 |   1728 |   	if (mVideoWidth > 1 && Settings::getInstance()->getBool("OptimizeVideo"))
 1095 |   1729 |   	{
 1096 |        | - 		// Avoid videos bigger than resolution
 1097 |   1730 |   		Vector2f maxSize(Renderer::getScreenWidth(), Renderer::getScreenHeight());
 1098 |        | - 
 1099 |   1731 |   #ifdef _RPI_
 1100 |        | - 		// Temporary -> RPI -> Try to limit videos to 400x300 for performance benchmark
 1101 |   1732 |   		if (!Renderer::isSmallScreen())
 1102 |   1733 |   			maxSize = Vector2f(400, 300);
 1103 |   1734 |   #endif
 1104 |        | - 
 1105 |        | - 		if (!mTargetSize.empty() && (mTargetSize.x() < maxSize.x() || mTargetSize.y() < maxSize.y()))
      |   1735 | + 		if (!mTargetSize.empty() &&
      |   1736 | + 			(mTargetSize.x() < maxSize.x() || mTargetSize.y() < maxSize.y()))
 1106 |   1737 |   			maxSize = mTargetSize;
 1107 |   1738 |   
 1108 |        | - 		// If video is bigger than display, ask VLC for a smaller image
 1109 |        | - 		auto sz = ImageIO::adjustPictureSize(Vector2i(mVideoWidth, mVideoHeight), Vector2i(maxSize.x(), maxSize.y()), mTargetIsMin);
 1110 |        | - 		if (sz.x() < mVideoWidth || sz.y() < mVideoHeight)
      |   1739 | + 		const auto size = ImageIO::adjustPictureSize(
      |   1740 | + 			Vector2i(mVideoWidth, mVideoHeight), Vector2i(maxSize.x(), maxSize.y()), mTargetIsMin);
      |   1741 | + 		if (size.x() < mVideoWidth || size.y() < mVideoHeight)
 1111 |   1742 |   		{
 1112 |        | - 			mVideoWidth = sz.x();
 1113 |        | - 			mVideoHeight = sz.y();
      |   1743 | + 			mVideoWidth = size.x();
      |   1744 | + 			mVideoHeight = size.y();
 1114 |   1745 |   		}
 1115 |   1746 |   	}
 1116 |   1747 |   
      |   1748 | + 	if (!updatePlaybackReservation(getVideoBufferBytes()))
      |   1749 | + 	{
      |   1750 | + 		failPlayback(300);
      |   1751 | + 		return;
      |   1752 | + 	}
      |   1753 | + 
 1117 |   1754 |   	mMediaPlayer = libvlc_media_player_new_from_media(mMedia);
 1118 |        | - 	if (!mMediaPlayer)
      |   1755 | + 	if (mMediaPlayer == nullptr)
      |   1756 | + 	{
      |   1757 | + 		if (!trySoftwareDecoderFallback())
      |   1758 | + 			failPlayback(2000);
 1119 |   1759 |   		return;
      |   1760 | + 	}
 1120 |   1761 |   
 1121 |   1762 |   	mContext = rentContext();
      |   1763 | + 	if (mContext == nullptr)
      |   1764 | + 	{
      |   1765 | + 		failPlayback(300);
      |   1766 | + 		return;
      |   1767 | + 	}
      |   1768 | + 
      |   1769 | + 	const unsigned int now = SDL_GetTicks();
 1122 |   1770 |   	mLastPlaybackTime = -1;
 1123 |        | - 	mLastPlaybackProgressTick = SDL_GetTicks();
      |   1771 | + 	mLastPlaybackProgressTick = now;
 1124 |   1772 |   	mLastPlaybackRestartTick = 0;
      |   1773 | + 	mPlaybackStartedTick = now;
      |   1774 | + 	mPlaybackRestartAttempts = 0;
 1125 |   1775 |   
 1126 |        | - 	if (hasAudioTrack)
      |   1776 | + 	if (mHasAudioTrack && shouldPlayAudio())
 1127 |   1777 |   	{
 1128 |        | - 		if (!getPlayAudio() || (!mScreensaverMode && !Settings::getInstance()->getBool("VideoAudio")) || (Settings::getInstance()->getBool("ScreenSaverVideoMute") && mScreensaverMode))
 1129 |        | - 			libvlc_audio_set_mute(mMediaPlayer, 1);
 1130 |        | - 		else
 1131 |        | - 			AudioManager::setVideoPlaying(true);
      |   1778 | + 		AudioManager::setVideoPlaying(true);
      |   1779 | + 		mAudioPlaybackRegistered = true;
 1132 |   1780 |   	}
      |   1781 | + 	else if (mHasAudioTrack)
      |   1782 | + 		libvlc_audio_set_mute(mMediaPlayer, 1);
 1133 |   1783 |   
 1134 |   1784 |   	if (mVideoWidth > 1)
 1135 |   1785 |   	{
 1136 |   1786 |   		libvlc_video_set_callbacks(mMediaPlayer, lock, unlock, display, (void*)mContext);
 1137 |        | - 		libvlc_video_set_format(mMediaPlayer, "RGBA", (int)mVideoWidth, (int)mVideoHeight, (int)mVideoWidth * 4);
 1138 |        | - 	}	
 1139 |        | - 	
 1140 |        | - 	libvlc_media_player_play(mMediaPlayer);
      |   1787 | + 		libvlc_video_set_format(mMediaPlayer, "RGBA", (int)mVideoWidth,
      |   1788 | + 			(int)mVideoHeight, (int)mVideoWidth * 4);
      |   1789 | + 	}
      |   1790 | + 
      |   1791 | + 	if (libvlc_media_player_play(mMediaPlayer) < 0)
      |   1792 | + 	{
      |   1793 | + 		if (!trySoftwareDecoderFallback())
      |   1794 | + 			failPlayback(2000);
      |   1795 | + 		return;
      |   1796 | + 	}
 1141 |   1797 |   	registerActivePlayer();
 1142 |   1798 |   }
 1143 |   1799 |   
 1144 |   1800 |   void VideoVlcComponent::startVideo()
 1145 |   1801 |   {
 1146 |        | - 	if (mIsPlaying || !mVLC)
      |   1802 | + 	if (mSharedVideoSource != nullptr)
      |   1803 | + 		return;
      |   1804 | + 
      |   1805 | + 	if (mIsPlaying || mIsParsing || mMediaPlayer != nullptr || mMedia != nullptr || !mVLC)
 1147 |   1806 |   		return;
 1148 |   1807 |   
 1149 |   1808 |   	if (mVideoPath.empty())
```

## Trecho 18: antes 1152, depois 1811

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1152) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1811)

```text
ANTES | DEPOIS |   CÓDIGO
 1152 |   1811 |   		return;
 1153 |   1812 |   	}
 1154 |   1813 |   
 1155 |        | - 	if (!acquirePlaybackSlot())
      |   1814 | + 	if (mPlaybackFailurePath != mVideoPath)
 1156 |   1815 |   	{
 1157 |        | - 		mPlaybackDeferred = true;
 1158 |        | - 		mDeferredRetryTime = SDL_GetTicks() + 300;
 1159 |        | - 		sDeferredPlayers.insert(this);
 1160 |        | - 		return;
      |   1816 | + 		mPlaybackFailurePath = mVideoPath;
      |   1817 | + 		mPlaybackFailureCount = 0;
      |   1818 | + 		mPlaybackFailureBlockedUntil = 0;
 1161 |   1819 |   	}
      |   1820 | + 	else if (mPlaybackFailureCount > 3 && mPlaybackFailureBlockedUntil != 0)
      |   1821 | + 	{
      |   1822 | + 		const unsigned int now = SDL_GetTicks();
      |   1823 | + 		if ((int)(now - mPlaybackFailureBlockedUntil) < 0)
      |   1824 | + 		{
      |   1825 | + 			deferPlayback(mPlaybackFailureBlockedUntil - now);
      |   1826 | + 			return;
      |   1827 | + 		}
 1162 |   1828 |   
 1163 |        | - 	mPlaybackDeferred = false;
 1164 |        | - 	sDeferredPlayers.erase(this);
 1165 |        | - 
 1166 |        | - 	StopWatch stopWatch("[VideoVlcComponent] startVideo", LogDebug);
 1167 |        | - 
 1168 |        | - #ifdef WIN32
 1169 |        | - 	std::string path = Utils::String::replace(mVideoPath, "/", "\\");
 1170 |        | - #else
 1171 |        | - 	std::string path = mVideoPath;
 1172 |        | - #endif
      |   1829 | + 		mPlaybackFailureCount = 0;
      |   1830 | + 		mPlaybackFailureBlockedUntil = 0;
      |   1831 | + 	}
 1173 |   1832 |   
 1174 |        | - 	mMedia = libvlc_media_new_path(mVLC, path.c_str());
 1175 |        | - 	if (!mMedia)
      |   1833 | + 	if (!acquirePlaybackSlot())
 1176 |   1834 |   	{
 1177 |        | - 		stopVideo();
      |   1835 | + 		deferPlayback(300);
 1178 |   1836 |   		return;
 1179 |   1837 |   	}
 1180 |   1838 |   
      |   1839 | + 	StopWatch stopWatch("[VideoVlcComponent] startVideo", LogDebug);
 1181 |   1840 |   	if (hasStoryBoard("", true) && mConfig.startDelay > 0)
 1182 |   1841 |   		startStoryboard();
 1183 |   1842 |   
```

## Trecho 19: antes 1185, depois 1844

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1185) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1844)

```text
ANTES | DEPOIS |   CÓDIGO
 1185 |   1844 |   	mCurrentLoop = 0;
 1186 |   1845 |   	mIsParsing = false;
 1187 |   1846 |   	mPlayingVideoPath = mVideoPath;
 1188 |        | - 
 1189 |        | - 	PowerSaver::pause();
 1190 |        | - 
 1191 |        | - 	// use : vlc �long-help
 1192 |        | - 	// WIN32 ? libvlc_media_add_option(mMedia, ":avcodec-hw=dxva2");
 1193 |        | - 	// RPI/OMX ? libvlc_media_add_option(mMedia, ":codec=mediacodec,iomx,all"); .
 1194 |        | - 
 1195 |        | - 	const bool isCarouselCellVideo =
 1196 |        | - 		getTag() == "carouselCellVideo" || getTag() == "frontSystemCarouselVideo";
 1197 |        | - 	std::string options = SystemConf::getInstance()->get("vlc.options");
 1198 |        | - 	if (!options.empty())
      |   1847 | + 	mPlaybackRestartAttempts = 0;
      |   1848 | + 	mHasAudioTrack = false;
      |   1849 | + 	if (mSoftwareDecoderPath != mVideoPath)
 1199 |   1850 |   	{
 1200 |        | - 		for (auto token : Utils::String::split(options, ' '))
 1201 |        | - 			libvlc_media_add_option(mMedia, token.c_str());
      |   1851 | + 		mSoftwareDecoderPath.clear();
      |   1852 | + 		mHardwareFallbackAttempted = false;
 1202 |   1853 |   	}
 1203 |        | - #if WIN32
 1204 |   1854 |   	else
 1205 |        | - 	{
 1206 |        | - 		libvlc_media_add_option(mMedia,
 1207 |        | - 			isCarouselCellVideo ? ":avcodec-hw=none" : ":avcodec-hw=any");
 1208 |        | - 		libvlc_media_add_option(mMedia, ":no-spu");
 1209 |        | - 	}
 1210 |        | - 	if (isCarouselCellVideo)
 1211 |        | - 		libvlc_media_add_option(mMedia, ":input-repeat=65535");
 1212 |        | - #endif
      |   1855 | + 		mHardwareFallbackAttempted = true;
 1213 |   1856 |   
 1214 |        | - 	// If we have a playlist : most videos have a fader, skip it 1 second
 1215 |        | - 	if (mPlaylist != nullptr && mConfig.startDelay == 0 && !mConfig.showSnapshotDelay && !mConfig.showSnapshotNoVideo)
 1216 |        | - 		libvlc_media_add_option(mMedia, ":start-time=0.7");
 1217 |        | - 
 1218 |        | - #if LIBVLC_VERSION_MAJOR >= 3
 1219 |        | - 	#if WIN32
 1220 |        | - 		const char* vlc_ver = libvlc_get_version();
 1221 |        | - 		if (vlc_ver[0] < '3')
 1222 |        | - 			libvlc_media_parse(mMedia);
 1223 |        | - 		else
 1224 |        | - 	#endif
      |   1857 | + 	if (!mPowerSaverPaused)
 1225 |   1858 |   	{
 1226 |        | - 		libvlc_media_parse_with_options(mMedia, libvlc_media_parse_local, 0);
 1227 |        | - 		if ((int)libvlc_media_get_parsed_status(mMedia) == 0)
 1228 |        | - 		{
 1229 |        | - 			mIsParsing = true;
 1230 |        | - 			return;
 1231 |        | - 		}
      |   1859 | + 		PowerSaver::pause();
      |   1860 | + 		mPowerSaverPaused = true;
 1232 |   1861 |   	}
 1233 |        | - #else
 1234 |        | - 	// It looks like an older version of the library is being used on Windows.
 1235 |        | - 	libvlc_media_parse(mMedia);
 1236 |        | - #endif
 1237 |   1862 |   
 1238 |        | - 	onMediaParsed();
      |   1863 | + 	const bool forceSoftwareDecoder = mSoftwareDecoderPath == mVideoPath;
      |   1864 | + 	if (!createMedia(forceSoftwareDecoder) && mIsRegisteredActive)
      |   1865 | + 		failPlayback(2000);
 1239 |   1866 |   }
 1240 |   1867 |   
 1241 |   1868 |   void VideoVlcComponent::stopVideo()
 1242 |   1869 |   {
 1243 |        | - 	mPlaybackDeferred = false;
 1244 |        | - 	sDeferredPlayers.erase(this);
 1245 |        | - 
      |   1870 | + 	clearPlaybackDeferred();
 1246 |   1871 |   	unregisterActivePlayer();
 1247 |   1872 |   
 1248 |        | - 	if (mMediaPlayer == nullptr && mMedia == nullptr && !mContext)
 1249 |        | - 		return;
 1250 |        | - 
 1251 |        | - 	StopWatch stopWatch("[VideoVlcComponent] stopVideo", LogDebug);
      |   1873 | + 	const bool hadResources = mMediaPlayer != nullptr || mMedia != nullptr || mContext != nullptr;
      |   1874 | + 	if (hadResources)
      |   1875 | + 	{
      |   1876 | + 		StopWatch stopWatch("[VideoVlcComponent] stopVideo", LogDebug);
      |   1877 | + 		releaseMediaForDecoderRetry();
      |   1878 | + 	}
 1252 |   1879 |   
 1253 |   1880 |   	mIsPlaying = false;
 1254 |   1881 |   	mIsWaitingForVideoToStart = false;
 1255 |   1882 |   	mStartDelayed = false;
 1256 |        | - 
 1257 |        | - 	// Release the media player so it stops calling back to us
 1258 |        | - 	if (mMediaPlayer)
 1259 |        | - 	{
 1260 |        | - 		mediaplayer_release_async(mContext, mMediaPlayer);
 1261 |        | - 		mMediaPlayer = nullptr;		
 1262 |        | - 	}
 1263 |        | - 	else if (mContext)
 1264 |        | - 		releaseContext(mContext);
 1265 |        | - 
 1266 |        | - 	mContext = nullptr;
 1267 |        | - 
 1268 |        | - 	// Release the media
 1269 |        | - 	if (mMedia)
 1270 |        | - 	{
 1271 |        | - 		mIsParsing = false;
 1272 |        | - 		libvlc_media_release(mMedia); 
 1273 |        | - 		mMedia = NULL;
 1274 |        | - 
 1275 |        | - 		PowerSaver::resume();
 1276 |        | - 	}		
 1277 |        | - 
      |   1883 | + 	mIsParsing = false;
      |   1884 | + 	mUsingHardwareDecoder = false;
 1278 |   1885 |   	mTexture = nullptr;
 1279 |   1886 |   	mLastPlaybackTime = -1;
 1280 |   1887 |   	mLastPlaybackProgressTick = SDL_GetTicks();
 1281 |   1888 |   	mLastPlaybackRestartTick = 0;
      |   1889 | + 	mPlaybackStartedTick = 0;
      |   1890 | + 	mPlaybackRestartAttempts = 0;
 1282 |   1891 |   
 1283 |        | - 	AudioManager::setVideoPlaying(false);
      |   1892 | + 	if (mAudioPlaybackRegistered)
      |   1893 | + 	{
      |   1894 | + 		AudioManager::setVideoPlaying(false);
      |   1895 | + 		mAudioPlaybackRegistered = false;
      |   1896 | + 	}
      |   1897 | + 	if (mPowerSaverPaused)
      |   1898 | + 	{
      |   1899 | + 		PowerSaver::resume();
      |   1900 | + 		mPowerSaverPaused = false;
      |   1901 | + 	}
 1284 |   1902 |   }
 1285 |   1903 |   
 1286 |   1904 |   void VideoVlcComponent::applyTheme(const std::shared_ptr<ThemeData>& theme, const std::string& view, const std::string& element, unsigned int properties)
```

## Trecho 20: antes 1344, depois 1962

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1344) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1962)

```text
ANTES | DEPOIS |   CÓDIGO
 1344 |   1962 |   	{
 1345 |   1963 |   		mIsParsing = false;
 1346 |   1964 |   		onMediaParsed();
 1347 |        | - 	}		
      |   1965 | + 	}
      |   1966 | + 
      |   1967 | + 	// VLC callbacks run on decoder threads. Publish frame readiness there, but
      |   1968 | + 	// perform component state changes here on the UI thread. Audio-only media has
      |   1969 | + 	// no frame callback, so VLC's Playing state is its start signal.
      |   1970 | + 	if (!mIsParsing && mMediaPlayer != nullptr && !mIsPlaying &&
      |   1971 | + 		mIsWaitingForVideoToStart)
      |   1972 | + 	{
      |   1973 | + 		bool started = mVideoWidth <= 1 &&
      |   1974 | + 			libvlc_media_player_get_state(mMediaPlayer) == libvlc_Playing;
      |   1975 | + 		if (!started && mContext != nullptr)
      |   1976 | + 		{
      |   1977 | + 			for (int frame = 0; frame < 2 && !started; ++frame)
      |   1978 | + 			{
      |   1979 | + 				std::lock_guard<std::mutex> frameLock(mContext->mutexes[frame]);
      |   1980 | + 				started = mContext->hasFrame[frame].load(std::memory_order_acquire);
      |   1981 | + 			}
      |   1982 | + 		}
      |   1983 | + 
      |   1984 | + 		if (started)
      |   1985 | + 			onVideoStarted();
      |   1986 | + 	}
      |   1987 | + 
      |   1988 | + 	// A decoder can fail before VLC produces its first display callback, in
      |   1989 | + 	// which case handleLooping() is not active yet. Catch both an explicit error
      |   1990 | + 	// and an opening stall here, with one hardware-to-software transition only.
      |   1991 | + 	if (mVideoWidth > 1 && !mIsParsing && mMediaPlayer != nullptr && !mIsPlaying &&
      |   1992 | + 		mIsWaitingForVideoToStart && mPlaybackStartedTick != 0)
      |   1993 | + 	{
      |   1994 | + 		const unsigned int now = SDL_GetTicks();
      |   1995 | + 		const libvlc_state_t state = libvlc_media_player_get_state(mMediaPlayer);
      |   1996 | + 		const unsigned int timeout = mUsingHardwareDecoder ? 8000 : 12000;
      |   1997 | + 		if (state == libvlc_Error || now - mPlaybackStartedTick >= timeout)
      |   1998 | + 		{
      |   1999 | + 			if (!trySoftwareDecoderFallback())
      |   2000 | + 				failPlayback(2000);
      |   2001 | + 		}
      |   2002 | + 	}
 1348 |   2003 |   	
 1349 |   2004 |   	VideoComponent::update(deltaTime);
 1350 |   2005 |   }
```

## Trecho 21: antes 1430, depois 2085

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1430) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L2085)

```text
ANTES | DEPOIS |   CÓDIGO
 1430 |   2085 |   	else
 1431 |   2086 |   	{
 1432 |   2087 |   		libvlc_media_player_pause(mMediaPlayer);
 1433 |        | - 		
 1434 |        | - 		PowerSaver::resume();
 1435 |        | - 		AudioManager::setVideoPlaying(false);
      |   2088 | + 		if (mPowerSaverPaused)
      |   2089 | + 		{
      |   2090 | + 			PowerSaver::resume();
      |   2091 | + 			mPowerSaverPaused = false;
      |   2092 | + 		}
      |   2093 | + 		if (mAudioPlaybackRegistered)
      |   2094 | + 		{
      |   2095 | + 			AudioManager::setVideoPlaying(false);
      |   2096 | + 			mAudioPlaybackRegistered = false;
      |   2097 | + 		}
 1436 |   2098 |   	}
 1437 |   2099 |   }
 1438 |   2100 |   
```

## Trecho 22: antes 1447, depois 2109

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/6f6b8b8372610fc2abe1e137d99a48c3ec52412e/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1447) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L2109)

```text
ANTES | DEPOIS |   CÓDIGO
 1447 |   2109 |   		return;
 1448 |   2110 |   	}
 1449 |   2111 |   
      |   2112 | + 	if (libvlc_media_player_play(mMediaPlayer) < 0)
      |   2113 | + 	{
      |   2114 | + 		if (!trySoftwareDecoderFallback())
      |   2115 | + 			failPlayback(2000);
      |   2116 | + 		return;
      |   2117 | + 	}
      |   2118 | + 
 1450 |   2119 |   	mIsPlaying = true;
 1451 |        | - 	libvlc_media_player_play(mMediaPlayer);
 1452 |   2120 |   	mLastPlaybackTime = -1;
 1453 |   2121 |   	mLastPlaybackProgressTick = SDL_GetTicks();
 1454 |        | - 	PowerSaver::pause();
 1455 |        | - 	AudioManager::setVideoPlaying(true);
      |   2122 | + 	mPlaybackStartedTick = mLastPlaybackProgressTick;
      |   2123 | + 	if (!mPowerSaverPaused)
      |   2124 | + 	{
      |   2125 | + 		PowerSaver::pause();
      |   2126 | + 		mPowerSaverPaused = true;
      |   2127 | + 	}
      |   2128 | + 	if (mHasAudioTrack && shouldPlayAudio() && !mAudioPlaybackRegistered)
      |   2129 | + 	{
      |   2130 | + 		AudioManager::setVideoPlaying(true);
      |   2131 | + 		mAudioPlaybackRegistered = true;
      |   2132 | + 	}
 1456 |   2133 |   }
 1457 |   2134 |   
 1458 |   2135 |   bool VideoVlcComponent::isPaused()
```

Conferência: 22 trechos, 1039 linhas adicionadas e 362 removidas.

