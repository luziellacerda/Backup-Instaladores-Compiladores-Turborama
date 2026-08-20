#include "components/VideoVlcComponent.h"

#include "renderers/Renderer.h"
#include "resources/TextureResource.h"
#include "utils/StringUtil.h"
#include "PowerSaver.h"
#include "Settings.h"
#include <vlc/vlc.h>
#include <vlc/libvlc_version.h>
#include <SDL_mutex.h>
#include <cmath>
#include <vector>
#include <algorithm>
#include "SystemConf.h"
#include "utils/StringUtil.h"
#include "ThemeData.h"
#include <SDL_timer.h>
#include "AudioManager.h"
#include "Log.h"
#include <condition_variable>
#include <deque>
#include <new>
#include <thread>

#ifdef WIN32
#include <codecvt>
#endif

#include "ImageIO.h"

#define MATHPI          3.141592653589793238462643383279502884L

libvlc_instance_t* VideoVlcComponent::mVLC = NULL;
std::mutex VideoVlcComponent::sActivePlayersMutex;
std::vector<VideoVlcComponent::ActiveVideoPlayer> VideoVlcComponent::sActivePlayers;
std::set<VideoVlcComponent*> VideoVlcComponent::sDeferredPlayers;
std::mutex VideoVlcComponent::sBufferPoolMutex;
std::vector<VideoBufferPoolEntry> VideoVlcComponent::sVideoBufferPool;
unsigned long long VideoVlcComponent::sBufferPoolUseCounter = 0;
size_t VideoVlcComponent::sVideoBufferBudgetBytes = (size_t)768 * 1024 * 1024;

namespace
{
	struct MediaPlayerReleaseJob
	{
		libvlc_media_player_t* player;
		VideoContext* context;
	};

	// libvlc_media_player_release may wait for VLC decoder threads.  A single
	// process-lifetime worker keeps that wait off the render thread without
	// creating one detached thread for every carousel movement.
	class MediaPlayerReleaseQueue
	{
	public:
		static MediaPlayerReleaseQueue& instance()
		{
			static MediaPlayerReleaseQueue queue;
			return queue;
		}

		void enqueue(libvlc_media_player_t* player, VideoContext* context)
		{
			bool releaseSynchronously = false;
			{
				std::lock_guard<std::mutex> lock(mMutex);
				// Keep retiring VLC internals bounded too. Pixel buffers are already
				// budgeted, but libVLC owns additional decoder state that we cannot
				// measure. Under pathological rapid scrolling, apply backpressure by
				// completing this one release on the caller instead of growing forever.
				if (mJobs.size() + mInFlight >= MAX_RELEASE_JOBS)
					releaseSynchronously = true;
				else
					mJobs.push_back({ player, context });
			}
			if (releaseSynchronously)
			{
				if (player != nullptr)
					libvlc_media_player_release(player);
				VideoVlcComponent::releaseContext(context);
				return;
			}
			mCondition.notify_one();
		}

	private:
		MediaPlayerReleaseQueue() : mStopping(false), mInFlight(0), mWorker([this]() { run(); })
		{
		}

		~MediaPlayerReleaseQueue()
		{
			{
				std::lock_guard<std::mutex> lock(mMutex);
				mStopping = true;
			}
			mCondition.notify_one();
			if (mWorker.joinable())
				mWorker.join();
			VideoVlcComponent::clearBufferPool();
		}

		void run()
		{
			for (;;)
			{
				MediaPlayerReleaseJob job;
				{
					std::unique_lock<std::mutex> lock(mMutex);
					mCondition.wait(lock, [this]() { return mStopping || !mJobs.empty(); });
					if (mStopping && mJobs.empty())
						return;
					job = mJobs.front();
					mJobs.pop_front();
					mInFlight++;
				}

				if (job.player != nullptr)
					libvlc_media_player_release(job.player);
				VideoVlcComponent::releaseContext(job.context);
				{
					std::lock_guard<std::mutex> lock(mMutex);
					mInFlight--;
				}
			}
		}

		static const size_t MAX_RELEASE_JOBS = 16;
		std::mutex mMutex;
		std::condition_variable mCondition;
		std::deque<MediaPlayerReleaseJob> mJobs;
		bool mStopping;
		size_t mInFlight;
		std::thread mWorker;
	};
}

// VLC prepares to render a video frame.
static void *lock(void *data, void **p_pixels) 
{
	struct VideoContext *c = (struct VideoContext *)data;

	int frame = (c->surfaceId.load(std::memory_order_acquire) ^ 1);
	
	c->mutexes[frame].lock();
	c->hasFrame[frame].store(false, std::memory_order_release);
	*p_pixels = c->surfaces[frame];
	return NULL; // Picture identifier, not needed here.
}

// VLC just rendered a video frame.
static void unlock(void *data, void* /*id*/, void *const* /*p_pixels*/) 
{
	struct VideoContext *c = (struct VideoContext *)data;

	int frame = (c->surfaceId.load(std::memory_order_acquire) ^ 1);

	c->surfaceId.store(frame, std::memory_order_release);
	c->hasFrame[frame].store(true, std::memory_order_release);
	c->mutexes[frame].unlock();
}

// VLC wants to display a video frame.
static void display(void* /*data*/, void* /*id*/)
{
	// VLC invokes this from a decoder thread. Playback state and component
	// callbacks are deliberately handled by update() on the UI thread.
}

VideoVlcComponent::VideoVlcComponent(Window* window) : VideoComponent(window), 
	mMediaPlayer(nullptr), mMedia(nullptr),
	mTopLeftCrop(0.0f, 0.0f), mBottomRightCrop(1.0f, 1.0f), mContext(nullptr)
{
	mIsRegisteredActive = false;
	mReservedVideoBytes = 0;
	mConcurrentPlaybackLimit = 0;
	mIsParsing = false;
	mUsingHardwareDecoder = false;
	mHardwareFallbackAttempted = false;
	mHasAudioTrack = false;
	mAudioPlaybackRegistered = false;
	mPowerSaverPaused = false;
	mPlaybackFailureCount = 0;
	mPlaybackFailureBlockedUntil = 0;
	mSharedVideoSource = nullptr;
	mSaturation = 1.0f;
	mElapsed = 0;
	mColorShift = 0xFFFFFFFF;
	mLinearSmooth = false;

	mLoops = -1;
	mCurrentLoop = 0;
	mLastPlaybackTime = -1;
	mLastPlaybackProgressTick = SDL_GetTicks();
	mLastPlaybackRestartTick = 0;
	mPlaybackStartedTick = 0;
	mPlaybackRestartAttempts = 0;

	// Get an empty texture for rendering the video
	mTexture = nullptr;// TextureResource::get("");
	mEffect = VideoVlcFlags::VideoVlcEffect::BUMP;

	// Make sure VLC has been initialised
	init();
}

void VideoVlcComponent::queueMediaPlayerRelease(VideoContext* ctx, libvlc_media_player_t* player)
{
	if (player == nullptr)
	{
		releaseContext(ctx);
		return;
	}
	if (ctx == nullptr)
	{
		// No video callbacks or decoder were installed yet, so this release is
		// cheap and avoids an unaccounted context-less job in the worker queue.
		libvlc_media_player_release(player);
		return;
	}

	{
		// Synchronize with any callback that already observed this context. The
		// callback no longer calls the component, but this also keeps the context
		// contract safe for older VLC callback ordering during teardown.
		std::lock_guard<std::mutex> lock(ctx->componentMutex);
		ctx->component = nullptr;
	}

	{
		std::lock_guard<std::mutex> lock(sBufferPoolMutex);
		if (ctx->poolIndex >= 0 && ctx->poolIndex < (int)sVideoBufferPool.size())
		{
			VideoBufferPoolEntry& entry = sVideoBufferPool[ctx->poolIndex];
			if (entry.surfaces[0] == ctx->surfaces[0] && entry.inUse)
				entry.retiring = true;
		}
	}

	MediaPlayerReleaseQueue::instance().enqueue(player, ctx);
}

VideoVlcComponent::~VideoVlcComponent()
{
	stopVideo();
}

int VideoVlcComponent::getEffectiveMaxConcurrentVideos()
{
	int maxVideos = Settings::getInstance()->getInt("MaxConcurrentVideos");
	if (maxVideos <= 0)
		maxVideos = 3;

	return maxVideos;
}

int VideoVlcComponent::getEffectiveMaxConcurrentCarouselVideos()
{
	// Zero deliberately means "the XML controls the number of cells".  The RAM
	// budget is still authoritative and every cell reserves memory before parse.
	return Math::max(0, Settings::getInstance()->getInt("MaxConcurrentCarouselVideos"));
}

int VideoVlcComponent::getMaxVideoRamMb()
{
	int maxVideoRam = Settings::getInstance()->getInt("MaxVideoRAM");
	if (maxVideoRam <= 0)
	{
		const int maxRam = Settings::getInstance()->getInt("MaxRAM");
		maxVideoRam = Math::max(64, Math::min(768, maxRam > 0 ? maxRam / 4 : 128));
	}

	return maxVideoRam;
}

size_t VideoVlcComponent::getVideoBufferBytes() const
{
	if (mVideoWidth <= 0 || mVideoHeight <= 0)
		return 0;

	return (size_t)mVideoWidth * (size_t)mVideoHeight * 4 * 2;
}

size_t VideoVlcComponent::estimatePendingVideoBufferBytes() const
{
	int width = Renderer::getScreenWidth();
	int height = Renderer::getScreenHeight();
	if (width <= 0 || height <= 0)
		width = 1280, height = 720;

	// OptimizeVideo asks VLC to decode at the component's target size.  Reserving
	// that size prevents a row of small cells from each claiming a full-screen
	// buffer while still falling back to a conservative screen-sized estimate.
	if (Settings::getInstance()->getBool("OptimizeVideo"))
	{
		if (mTargetSize.x() > 0)
			width = Math::min(width, Math::max(1, (int)std::ceil(mTargetSize.x())));
		if (mTargetSize.y() > 0)
			height = Math::min(height, Math::max(1, (int)std::ceil(mTargetSize.y())));
	}

	return (size_t)width * (size_t)height * 4 * 2;
}

size_t VideoVlcComponent::getBufferPoolCacheLimitBytes(size_t maxVideoBytes)
{
	const size_t maxCacheBytes = (size_t)128 * 1024 * 1024;
	return std::min(maxCacheBytes, maxVideoBytes / 4);
}

void VideoVlcComponent::trimBufferPoolLocked(size_t maxFreeBytes, size_t maxTotalBytes)
{
	size_t totalBytes = 0;
	size_t freeBytes = 0;
	for (const auto& entry : sVideoBufferPool)
	{
		if (entry.surfaces[0] == nullptr)
			continue;

		const size_t bytes = (size_t)entry.width * (size_t)entry.height * 4 * 2;
		totalBytes += bytes;
		if (!entry.inUse)
			freeBytes += bytes;
	}

	while (freeBytes > maxFreeBytes || totalBytes > maxTotalBytes)
	{
		int oldestIndex = -1;
		unsigned long long oldestUse = 0;
		for (int i = 0; i < (int)sVideoBufferPool.size(); ++i)
		{
			const VideoBufferPoolEntry& entry = sVideoBufferPool[i];
			if (entry.inUse || entry.surfaces[0] == nullptr)
				continue;

			if (oldestIndex < 0 || entry.lastUsed < oldestUse)
			{
				oldestIndex = i;
				oldestUse = entry.lastUsed;
			}
		}

		if (oldestIndex < 0)
			break;

		VideoBufferPoolEntry& entry = sVideoBufferPool[oldestIndex];
		const size_t bytes = (size_t)entry.width * (size_t)entry.height * 4 * 2;
		delete[] entry.surfaces[0];
		delete[] entry.surfaces[1];
		entry.surfaces[0] = nullptr;
		entry.surfaces[1] = nullptr;
		entry.width = 0;
		entry.height = 0;
		entry.inUse = false;
		entry.retiring = false;
		entry.carouselVideo = false;
		entry.countAgainstConcurrentLimit = false;
		entry.lastUsed = ++sBufferPoolUseCounter;
		totalBytes -= bytes;
		freeBytes -= bytes;
	}
}

bool VideoVlcComponent::isCarouselVideo() const
{
	const std::string& tag = getTag();
	return tag == "carouselCellVideo" || tag == "frontSystemCarouselVideo";
}

bool VideoVlcComponent::isThemeManagedVideo()
{
	return !isCarouselVideo() && getExtraType() != ExtraType::BUILTIN;
}

bool VideoVlcComponent::shouldPlayAudio()
{
	// Carousel and other setPlayAudio(false) decorations receive :no-audio,
	// while the selected game's normal preview keeps the existing audio policy.
	return !isCarouselVideo() && getPlayAudio() &&
		(mScreensaverMode || Settings::getInstance()->getBool("VideoAudio")) &&
		!(mScreensaverMode && Settings::getInstance()->getBool("ScreenSaverVideoMute"));
}

void VideoVlcComponent::releaseContext(VideoContext* ctx)
{
	if (ctx == nullptr)
		return;

	{
		std::unique_lock<std::mutex> lock(sBufferPoolMutex);
		bool returnedToPool = false;
		if (ctx->poolIndex >= 0 && ctx->poolIndex < (int)sVideoBufferPool.size())
		{
			VideoBufferPoolEntry& entry = sVideoBufferPool[ctx->poolIndex];
			if (entry.surfaces[0] == ctx->surfaces[0] && entry.surfaces[1] == ctx->surfaces[1])
			{
				entry.inUse = false;
				entry.retiring = false;
				entry.lastUsed = ++sBufferPoolUseCounter;
				returnedToPool = true;
			}
		}

		if (!returnedToPool)
			ctx->poolIndex = -1;

		delete ctx;
		trimBufferPoolLocked(getBufferPoolCacheLimitBytes(sVideoBufferBudgetBytes),
			sVideoBufferBudgetBytes);
	}

	// A retiring VLC player remains in both the byte and slot budgets until this
	// point. Deferred components poll on their short retry timer; the worker does
	// not touch component state from its background thread.
}

void VideoVlcComponent::clearBufferPool()
{
	std::lock_guard<std::mutex> lock(sBufferPoolMutex);
	for (auto& entry : sVideoBufferPool)
	{
		// At normal shutdown the release queue has drained every in-use context.
		// Retain a defensive check so a future static video cannot free live pixels.
		if (entry.inUse)
			continue;

		delete[] entry.surfaces[0];
		delete[] entry.surfaces[1];
		entry.surfaces[0] = nullptr;
		entry.surfaces[1] = nullptr;
		entry.width = 0;
		entry.height = 0;
		entry.retiring = false;
	}
	sVideoBufferPool.erase(std::remove_if(sVideoBufferPool.begin(), sVideoBufferPool.end(),
		[](const VideoBufferPoolEntry& entry) { return !entry.inUse; }), sVideoBufferPool.end());
}

bool VideoVlcComponent::updatePlaybackReservation(size_t bytes)
{
	const size_t maxVideoBytes = (size_t)getMaxVideoRamMb() * 1024 * 1024;
	std::unique_lock<std::mutex> playersLock(sActivePlayersMutex);
	if (!mIsRegisteredActive)
		return false;

	size_t pendingBytes = 0;
	for (const auto& player : sActivePlayers)
	{
		if (player.component != nullptr && player.component != this &&
			player.component->mContext == nullptr)
			pendingBytes += player.component->mReservedVideoBytes;
	}

	size_t inUseBytes = 0;
	{
		std::lock_guard<std::mutex> poolLock(sBufferPoolMutex);
		sVideoBufferBudgetBytes = maxVideoBytes;
		for (const auto& entry : sVideoBufferPool)
			if (entry.inUse && entry.surfaces[0] != nullptr)
				inUseBytes += (size_t)entry.width * (size_t)entry.height * 4 * 2;
	}

	if (inUseBytes > maxVideoBytes || pendingBytes > maxVideoBytes - inUseBytes ||
		bytes > maxVideoBytes - inUseBytes - pendingBytes)
		return false;

	mReservedVideoBytes = bytes;
	return true;
}

void VideoVlcComponent::clearPlaybackDeferred()
{
	std::lock_guard<std::mutex> lock(sActivePlayersMutex);
	mPlaybackDeferred = false;
	sDeferredPlayers.erase(this);
}

void VideoVlcComponent::deferPlayback(unsigned retryDelay)
{
	// startVideoWithDelay marks the component as waiting before calling us. A
	// deferred attempt has not actually started, so release that gate or the
	// timer can expire without ever re-entering startVideo().
	mIsWaitingForVideoToStart = false;
	mStartDelayed = false;
	std::lock_guard<std::mutex> lock(sActivePlayersMutex);
	mPlaybackDeferred = true;
	mDeferredRetryTime = SDL_GetTicks() + retryDelay;
	sDeferredPlayers.insert(this);
}

int VideoVlcComponent::computePlaybackPriority()
{
	if (!mShowing || !isVisible() || mScreensaverActive)
		return 0;

	if (getOpacity() < 16)
		return 1;

	int priority = 10;

	const std::string& tag = getTag();
	if (Utils::String::startsWith(tag, "staticBackground"))
		priority = 15;
	else if (tag == "background" || Utils::String::startsWith(tag, "bg-") || Utils::String::startsWith(tag, "bg_"))
		priority = 25;
	else if (isStaticExtra())
		priority = 20;

	priority += (int)Math::min(getZIndex(), 75.f);

	if (mScreensaverMode)
		priority += 200;

	return priority;
}

bool VideoVlcComponent::acquirePlaybackSlot()
{
	const int priority = computePlaybackPriority();
	if (priority <= 0)
		return false;

	const bool carousel = isCarouselVideo();
	const bool themeManaged = isThemeManagedVideo();
	const size_t reservationBytes = estimatePendingVideoBufferBytes();
	const size_t maxVideoBytes = (size_t)getMaxVideoRamMb() * 1024 * 1024;

	for (;;)
	{
		VideoVlcComponent* victim = nullptr;
		std::unique_lock<std::mutex> playersLock(sActivePlayersMutex);
		if (mIsRegisteredActive)
			return true;

		int bucketCount = 0;
		for (const auto& player : sActivePlayers)
		{
			if (player.component == nullptr)
				continue;
			if (carousel && player.component->isCarouselVideo())
				bucketCount++;
			else if (!carousel && !themeManaged && !player.component->isCarouselVideo() &&
				!player.component->isThemeManagedVideo())
				bucketCount++;
		}

		size_t inUseBytes = 0;
		int retiringBucketCount = 0;
		{
			std::lock_guard<std::mutex> poolLock(sBufferPoolMutex);
			sVideoBufferBudgetBytes = maxVideoBytes;
			trimBufferPoolLocked(getBufferPoolCacheLimitBytes(maxVideoBytes), maxVideoBytes);
			for (const auto& entry : sVideoBufferPool)
			{
				if (!entry.inUse || entry.surfaces[0] == nullptr)
					continue;
				inUseBytes += (size_t)entry.width * (size_t)entry.height * 4 * 2;
				if (entry.retiring && ((carousel && entry.carouselVideo) ||
					(!carousel && !themeManaged && entry.countAgainstConcurrentLimit)))
					retiringBucketCount++;
			}
		}

		const bool enforceCount = Settings::getInstance()->getBool("EnforceVideoLimit");
		const int globalCarouselLimit = getEffectiveMaxConcurrentCarouselVideos();
		const int configuredCarouselLimit = mConcurrentPlaybackLimit > 0 && globalCarouselLimit > 0 ?
			Math::min(mConcurrentPlaybackLimit, globalCarouselLimit) :
			Math::max(mConcurrentPlaybackLimit, globalCarouselLimit);
		const int bucketLimit = carousel ? configuredCarouselLimit :
			(themeManaged ? 0 : getEffectiveMaxConcurrentVideos());
		if (enforceCount && bucketLimit > 0 && bucketCount + retiringBucketCount >= bucketLimit)
		{
			int weakestIndex = -1;
			int weakestPriority = priority;
			for (int i = 0; i < (int)sActivePlayers.size(); ++i)
			{
				VideoVlcComponent* candidate = sActivePlayers[i].component;
				if (candidate == nullptr)
					continue;

				const bool sameBucket = carousel ? candidate->isCarouselVideo() :
					(!themeManaged && !candidate->isCarouselVideo() &&
						!candidate->isThemeManagedVideo());
				if (sameBucket && sActivePlayers[i].priority < weakestPriority)
				{
					weakestIndex = i;
					weakestPriority = sActivePlayers[i].priority;
				}
			}

			if (weakestIndex < 0)
				return false;

			victim = sActivePlayers[weakestIndex].component;
			sActivePlayers.erase(sActivePlayers.begin() + weakestIndex);
			victim->mIsRegisteredActive = false;
			victim->mReservedVideoBytes = 0;
			playersLock.unlock();
			victim->stopVideo();
			// Its VLC player/context is now retiring and still consumes both the
			// decoder token and byte budget. Wait for the release worker instead of
			// cascading through every lower-priority player in this bucket.
			return false;
		}

		size_t pendingBytes = 0;
		for (const auto& player : sActivePlayers)
			if (player.component != nullptr && player.component->mContext == nullptr)
				pendingBytes += player.component->mReservedVideoBytes;

		if (inUseBytes > maxVideoBytes || pendingBytes > maxVideoBytes - inUseBytes ||
			reservationBytes > maxVideoBytes - inUseBytes - pendingBytes)
			return false;

		sActivePlayers.push_back({ this, priority });
		mIsRegisteredActive = true;
		mReservedVideoBytes = reservationBytes;
		mPlaybackDeferred = false;
		sDeferredPlayers.erase(this);
		return true;
	}
}

void VideoVlcComponent::registerActivePlayer()
{
	std::unique_lock<std::mutex> lock(sActivePlayersMutex);
	if (!mIsRegisteredActive)
	{
		// Defensive compatibility for callers that bypassed startVideo().
		sActivePlayers.push_back({ this, computePlaybackPriority() });
		mIsRegisteredActive = true;
		mReservedVideoBytes = getVideoBufferBytes();
	}
	else
	{
		for (auto& player : sActivePlayers)
			if (player.component == this)
				player.priority = computePlaybackPriority();
	}
	mPlaybackDeferred = false;
	sDeferredPlayers.erase(this);
}

void VideoVlcComponent::unregisterActivePlayer()
{
	std::unique_lock<std::mutex> lock(sActivePlayersMutex);
	sActivePlayers.erase(std::remove_if(sActivePlayers.begin(), sActivePlayers.end(),
		[this](const ActiveVideoPlayer& p) { return p.component == this; }), sActivePlayers.end());
	const bool wasRegistered = mIsRegisteredActive;
	mIsRegisteredActive = false;
	mReservedVideoBytes = 0;
	lock.unlock();

	if (wasRegistered)
		notifyPlaybackSlotAvailable();
}

void VideoVlcComponent::notifyPlaybackSlotAvailable()
{
	// Keep pointer validation and the write under the same lock. In particular,
	// the release worker must not retain a raw component pointer past destruction.
	std::unique_lock<std::mutex> lock(sActivePlayersMutex);
	for (auto component : sDeferredPlayers)
		if (component != nullptr && component->mPlaybackDeferred)
			component->mDeferredRetryTime = SDL_GetTicks();
}

Vector2f VideoVlcComponent::getSize() const
{
	if (mTargetIsMax && mPadding != Vector4f::Zero())
	{
		auto targetSize = mTargetSize - mPadding.xy() - mPadding.zw();

		if (mSize.x() == targetSize.x())
			return Vector2f(mSize.x() + mPadding.x() + mPadding.z(), mSize.y());
		else if (mSize.y() == targetSize.y())
			return Vector2f(mSize.x(), mSize.y() + mPadding.y() + mPadding.w());
	}

	return GuiComponent::getSize() * (mBottomRightCrop - mTopLeftCrop);
}

void VideoVlcComponent::setSharedVideoSource(VideoVlcComponent* source)
{
	if (mSharedVideoSource == source)
		return;

	stopVideo();
	mSharedVideoSource = source;
	mVideoPath.clear();
	mPlayingVideoPath.clear();
	mTexture = nullptr;
	mVideoWidth = 0;
	mVideoHeight = 0;
	// The source owns playback fade. This component retains its independent
	// opacity/storyboard while drawing the already-decoded frame.
	mFadeIn = source == nullptr ? 0.0f : 1.0f;
}

void VideoVlcComponent::setResize(float width, float height)
{
	if (mSize.x() != 0 && mSize.y() != 0 && !mTargetIsMax && !mTargetIsMin && mTargetSize.x() == width && mTargetSize.y() == height)
		return;

	mTargetSize = Vector2f(width, height);
	mSize = mTargetSize;
	mTargetIsMax = false;
	mTargetIsMin = false;
	mStaticImage.setMaxSize(width, height);
	resize();
}

void VideoVlcComponent::setMaxSize(float width, float height)
{
	if (mSize.x() != 0 && mSize.y() != 0 && mTargetIsMax && !mTargetIsMin && mTargetSize.x() == width && mTargetSize.y() == height)
		return;

	mTargetSize = Vector2f(width, height);
	mSize = mTargetSize;
	mTargetIsMax = true;
	mTargetIsMin = false;
	mStaticImage.setMaxSize(width, height);
	resize();
}

void VideoVlcComponent::setMinSize(float width, float height)
{
	if (mSize.x() != 0 && mSize.y() != 0 && mTargetIsMin && !mTargetIsMax && mTargetSize.x() == width && mTargetSize.y() == height)
		return;

	mTargetSize = Vector2f(width, height);
	mSize = mTargetSize;
	mTargetIsMax = false;
	mTargetIsMin = true;
	mStaticImage.setMaxSize(width, height);
	resize();
}

void VideoVlcComponent::onVideoStarted()
{
	resetPlaybackFailures();
	VideoComponent::onVideoStarted();
	resize();
}

void VideoVlcComponent::crop(float left, float top, float right, float bot)
{
	mTopLeftCrop.x() = Math::clamp(left, 0.0f, 1.0f);
	mTopLeftCrop.y() = Math::clamp(top, 0.0f, 1.0f);
	mBottomRightCrop.x() = 1.0f - Math::clamp(right, 0.0f, 1.0f);
	mBottomRightCrop.y() = 1.0f - Math::clamp(bot, 0.0f, 1.0f);
}

void VideoVlcComponent::resize()
{
	if (!mTexture)
		return;

	const Vector2f textureSize((float)mVideoWidth, (float)mVideoHeight);

	if (textureSize == Vector2f::Zero())
		return;

	auto targetSize = mTargetSize - mPadding.xy() - mPadding.zw();

	if (mTargetIsMax)
	{
		crop(0, 0, 0, 0);
		mSize = textureSize;

		Vector2f resizeScale((targetSize.x() / mSize.x()), (targetSize.y() / mSize.y()));

		if (resizeScale.x() < resizeScale.y())
		{
			mSize[0] *= resizeScale.x(); // this will be mTargetSize.x(). We can't exceed it, nor be lower than it.
			// we need to make sure we're not creating an image larger than max size
			//mSize[1] = Math::min(Math::round(mSize[1] *= resizeScale.x()), mTargetSize.y());
			mSize[1] = Math::min(mSize[1] *= resizeScale.x(), targetSize.y());
		}
		else
		{
			//mSize[1] = Math::round(mSize[1] * resizeScale.y()); // this will be mTargetSize.y(). We can't exceed it.
			mSize[1] = mSize[1] * resizeScale.y(); // this will be mTargetSize.y(). We can't exceed it.

			// for SVG rasterization, always calculate width from rounded height (see comment above)
			// we need to make sure we're not creating an image larger than max size
			mSize[0] = Math::min((mSize[1] / textureSize.y()) * textureSize.x(), targetSize.x());
		}
	}
	else if (mTargetIsMin)
	{
		// mSize = ImageIO::getPictureMinSize(textureSize, mTargetSize);			
		mSize = textureSize;

		Vector2f resizeScale((targetSize.x() / mSize.x()), (targetSize.y() / mSize.y()));

		if (resizeScale.x() > resizeScale.y())
		{
			mSize[0] *= resizeScale.x();
			mSize[1] *= resizeScale.x();

			float cropPercent = (mSize.y() - targetSize.y()) / (mSize.y() * 2);
			crop(0, cropPercent, 0, cropPercent);
		}
		else
		{
			mSize[0] *= resizeScale.y();
			mSize[1] *= resizeScale.y();

			float cropPercent = (mSize.x() - targetSize.x()) / (mSize.x() * 2);
			crop(cropPercent, 0, cropPercent, 0);
		}

		// for SVG rasterization, always calculate width from rounded height (see comment above)
		// we need to make sure we're not creating an image smaller than min size
		// mSize[1] = Math::max(Math::round(mSize[1]), mTargetSize.y());
		// mSize[0] = Math::max((mSize[1] / textureSize.y()) * textureSize.x(), mTargetSize.x());
	}
	else
	{
		crop(0, 0, 0, 0);
		// if both components are set, we just stretch
		// if no components are set, we don't resize at all
		mSize = targetSize == Vector2f::Zero() ? textureSize : targetSize;

		// if only one component is set, we resize in a way that maintains aspect ratio
		// for SVG rasterization, we always calculate width from rounded height (see comment above)
		if (!targetSize.x() && targetSize.y())
		{
			//mSize[1] = Math::round(mTargetSize.y());
			mSize[1] = targetSize.y();
			mSize[0] = (mSize.y() / textureSize.y()) * textureSize.x();
		}
		else if (targetSize.x() && !targetSize.y())
		{
			//mSize[1] = Math::round((mTargetSize.x() / textureSize.x()) * textureSize.y());
			mSize[1] = (targetSize.x() / textureSize.x()) * textureSize.y();
			mSize[0] = (mSize.y() / textureSize.y()) * textureSize.x();
		}
	}

	mTexture->rasterizeAt((size_t)Math::round(mSize.x()), (size_t)Math::round(mSize.y()));
	onSizeChanged();
}

void VideoVlcComponent::onSizeChanged()
{
	GuiComponent::onSizeChanged();
	updateVertices();
}

void VideoVlcComponent::onPaddingChanged()
{
	GuiComponent::onPaddingChanged();
	resize();
	updateVertices();
}

void VideoVlcComponent::setColorShift(unsigned int color)
{
	mColorShift = color;
}

void VideoVlcComponent::updateVertices()
{
	if (!mTexture)
		return;

	auto textureSize = mTexture->getSize();

	Vector2f     topLeft = mSize * mTopLeftCrop;
	Vector2f     bottomRight = mSize * mBottomRightCrop;

	Vector2f paddingOffset;

	if (mPadding != Vector4f::Zero())
	{
		paddingOffset = mPadding.xy() - (mPadding.xy() + mPadding.zw()) * mOrigin;
		topLeft += paddingOffset;
		bottomRight += paddingOffset;
	}

	const float        px = mTexture->isTiled() ? mSize.x() / textureSize.x() : 1.0f;
	const float        py = mTexture->isTiled() ? mSize.y() / textureSize.y() : 1.0f;

	const unsigned int color = Renderer::convertColor(mColorShift);

	mVertices[0] = {
		{ topLeft.x(),					topLeft.y()	 },
		{ mTopLeftCrop.x(),				1.0f - mBottomRightCrop.y()    },
		color };

	mVertices[1] = {
		{ topLeft.x(),					bottomRight.y() },
		{ mTopLeftCrop.x(),				py - mTopLeftCrop.y() },
		color };

	mVertices[2] = {
		{ bottomRight.x(),				topLeft.y()	},
		{ mBottomRightCrop.x() * px,	1.0f - mBottomRightCrop.y()     },
		color };

	mVertices[3] = {
		{ bottomRight.x(),				bottomRight.y() },
		{ mBottomRightCrop.x() * px,    py - mTopLeftCrop.y() },
		color };

	// Fix vertices for min Target
	if (mTargetIsMin)
	{		
		auto targetSize = mTargetSize - mPadding.xy() - mPadding.zw();
		Vector2f targetSizePos = (mSize - targetSize) * mOrigin + paddingOffset;

		float x = targetSizePos.x();
		float y = targetSizePos.y();
		float r = x + targetSize.x();
		float b = y + targetSize.y();

		mVertices[0].pos[0] = x;
		mVertices[0].pos[1] = y;

		mVertices[1].pos[0] = x;
		mVertices[1].pos[1] = b;

		mVertices[2].pos[0] = r;
		mVertices[2].pos[1] = y;

		mVertices[3].pos[0] = r;
		mVertices[3].pos[1] = b;
	}

	/*
	// round vertices
	for (int i = 0; i < 4; ++i)
		mVertices[i].pos.round();
	*/
	/*
	if (mFlipX)
	{
		for (int i = 0; i < 4; ++i)
			mVertices[i].tex[0] = px - mVertices[i].tex[0];
	}

	if (mFlipY)
	{
		for (int i = 0; i < 4; ++i)
			mVertices[i].tex[1] = py - mVertices[i].tex[1];
	}
	*/
	updateColors();
	updateRoundCorners();	
}

void VideoVlcComponent::updateColors()
{
	float t = mFadeIn;
	if (mFadeIn < 1.0)
	{
		t = 1.0 - mFadeIn;
		t -= 1; // cubic ease in
		t = Math::lerp(0, 1, t * t * t + 1);
		t = 1.0 - t;
	}

	float opacity = (getOpacity() / 255.0f) * t;

	if (hasStoryBoard() && currentStoryBoardHasProperty("opacity") && isStoryBoardRunning())
		opacity = (getOpacity() / 255.0f);

	unsigned int color = Renderer::convertColor(mColorShift & 0xFFFFFF00 | (unsigned char)((mColorShift & 0xFF) * opacity));

	mVertices[0].col = color;
	mVertices[1].col = color;
	mVertices[2].col = color;
	mVertices[3].col = color;
}

void VideoVlcComponent::setRoundCorners(float value)
{
	if (mRoundCorners == value)
		return;

	VideoComponent::setRoundCorners(value);
	updateRoundCorners();
}

void VideoVlcComponent::updateRoundCorners()
{
	if (mRoundCorners <= 0 || Renderer::shaderSupportsCornerSize(mCustomShader.path))
	{
		mRoundCornerStencil.clear();
		return;
	}

	float x = 0;
	float y = 0;
	float size_x = mSize.x();
	float size_y = mSize.y();

	if (mTargetIsMin)
	{
		Vector2f targetSizePos = (mTargetSize - mSize) * mOrigin * -1;

		x = targetSizePos.x();
		y = targetSizePos.y();
		size_x = mTargetSize.x();
		size_y = mTargetSize.y();
	}

	float radius = mRoundCorners < 1 ? Math::max(size_x, size_y) * mRoundCorners : mRoundCorners;
	mRoundCornerStencil = Renderer::createRoundRect(x, y, size_x, size_y, radius);
}

void VideoVlcComponent::render(const Transform4x4f& parentTrans)
{
	if (!isShowing() || !isVisible())
		return;

	VideoComponent::render(parentTrans);

	bool initFromPixels = mSharedVideoSource == nullptr;
	if (mSharedVideoSource != nullptr)
	{
		if (mSharedVideoSource == this || mSharedVideoSource->mTexture == nullptr ||
			!mSharedVideoSource->mTexture->isLoaded())
			return;

		const bool dimensionsChanged = mVideoWidth != mSharedVideoSource->mVideoWidth ||
			mVideoHeight != mSharedVideoSource->mVideoHeight;
		mTexture = mSharedVideoSource->mTexture;
		mVideoWidth = mSharedVideoSource->mVideoWidth;
		mVideoHeight = mSharedVideoSource->mVideoHeight;
		if (dimensionsChanged)
			resize();
	}

	if (mSharedVideoSource == nullptr && (!mIsPlaying || !mContext || mIsParsing))
	{
		// If video is still attached to the path & texture is initialized, we suppose it had just been stopped (onhide, ondisable, screensaver...)
		// still render the last frame
		if (mTexture != nullptr && !mVideoPath.empty() && mPlayingVideoPath == mVideoPath && mTexture->isLoaded())
			initFromPixels = false;
		else
			return;
	}

	float t = mFadeIn;
	if (mFadeIn < 1.0)
	{
		t = 1.0 - mFadeIn;
		t -= 1; // cubic ease in
		t = Math::lerp(0, 1, t*t*t + 1);
		t = 1.0 - t;
	}

	if (t == 0.0)
		return;
		
	Transform4x4f trans = parentTrans * getTransform();
	
	if (mRotation == 0 && !mTargetIsMin)
	{
		auto rect = Renderer::getScreenRect(trans, mSize);
		if (!Renderer::isVisibleOnScreen(rect))
			return;
	}

	// Build a texture for the video frame
	if (initFromPixels)
	{		
		int frame = mContext->surfaceId.load(std::memory_order_acquire);
		std::lock_guard<std::mutex> frameLock(mContext->mutexes[frame]);
		if (mContext->hasFrame[frame].load(std::memory_order_acquire))
		{
			if (mTexture == nullptr)
			{
				mTexture = TextureResource::get("", false, mLinearSmooth);

				resize();
				trans = parentTrans * getTransform();
			}

#if defined(_RPI_) || defined(WIN32)
			// Limit OpenGL texture uploads to ~30fps when OptimizeVideo is enabled.
			if (!Settings::getInstance()->getBool("OptimizeVideo") || mElapsed >= 33)
#endif
			{
				mTexture->updateFromExternalPixels(mContext->surfaces[frame], mVideoWidth, mVideoHeight);
				mContext->hasFrame[frame].store(false, std::memory_order_release);

				mElapsed = 0;
			}
		}
	}

	if (mTexture == nullptr)
		return;

	updateColors();

	bool isDefaultEffectDisabled = hasStoryBoard() && currentStoryBoardHasProperty("scale") && isStoryBoardRunning();

	/*if (mEffect == VideoVlcFlags::VideoVlcEffect::SLIDERIGHT && mFadeIn > 0.0 && mFadeIn < 1.0 && mConfig.startDelay > 0 && !isDefaultEffectDisabled)
	{
		float t = 1.0 - mFadeIn;
		t -= 1;
		t = Math::lerp(0, 1, t*t*t + 1);

		vertices[0] = { { 0.0f     , 0.0f      }, { t, 0.0f }, color };
		vertices[1] = { { 0.0f     , mSize.y() }, { t, 1.0f }, color };
		vertices[2] = { { mSize.x(), 0.0f      }, { t + 1.0f, 0.0f }, color };
		vertices[3] = { { mSize.x(), mSize.y() }, { t + 1.0f, 1.0f }, color };
	}
	else*/
	if (mEffect == VideoVlcFlags::VideoVlcEffect::SIZE && mFadeIn > 0.0 && mFadeIn < 1.0 && mConfig.startDelay > 0 && !isDefaultEffectDisabled)
	{		
		float bump = Math::easeOutCubic(mFadeIn);

		auto scale = mScale;
		mScale = mScale * bump;
		mTransformDirty = true;
		trans = parentTrans * getTransform();
		mScale = scale;
		mTransformDirty = true;
	}
	else if (mEffect == VideoVlcFlags::VideoVlcEffect::BUMP && mFadeIn > 0.0 && mFadeIn < 1.0 && mConfig.startDelay > 0 && !isDefaultEffectDisabled)
	{
		float bump = sin((MATHPI / 2.0) * mFadeIn) + sin(MATHPI * mFadeIn) / 2.0;

		auto scale = mScale;
		mScale = mScale * bump;
		mTransformDirty = true;
		trans = parentTrans * getTransform();
		mScale = scale;
		mTransformDirty = true;
	}

	// round vertices
	// for (int i = 0; i < 4; ++i)
	//	vertices[i].pos.round();
	
	if (mTexture->bind())
	{
		Renderer::setMatrix(trans);

		beginCustomClipRect();

		Vector2f targetSizePos = (mTargetSize - mSize) * mOrigin * -1;
		
		// Render it
		mVertices->saturation = mSaturation;
		mVertices->customShader = mCustomShader.path.empty() ? nullptr : &mCustomShader;
	
		if (mRoundCorners > 0 && mRoundCornerStencil.size() > 0)
		{
			Renderer::setStencil(mRoundCornerStencil.data(), mRoundCornerStencil.size());
			Renderer::drawTriangleStrips(&mVertices[0], 4);
			Renderer::disableStencil();
		}
		else
		{
			mVertices->cornerRadius = mRoundCorners < 1 ? Math::max(mSize.x(), mSize.y()) * mRoundCorners : mRoundCorners;
			Renderer::drawTriangleStrips(&mVertices[0], 4);
		}

		endCustomClipRect();

		Renderer::bindTexture(0);
	}
}

VideoContext* VideoVlcComponent::rentContext()
{
	VideoContext* ctx = new VideoContext();
	ctx->component = this;
	ctx->poolIndex = -1;
	ctx->bufferWidth = mVideoWidth;
	ctx->bufferHeight = mVideoHeight;
	ctx->carouselVideo = isCarouselVideo();
	ctx->countAgainstConcurrentLimit = !ctx->carouselVideo && !isThemeManagedVideo();
	ctx->hasFrame[0] = false;
	ctx->hasFrame[1] = false;
	ctx->surfaceId = 0;

	const size_t frameBytes = (size_t)mVideoWidth * (size_t)mVideoHeight * 4;
	const size_t bufferBytes = frameBytes * 2;
	const size_t maxVideoBytes = (size_t)getMaxVideoRamMb() * 1024 * 1024;

	// Keep the lock order consistent with reservation checks: players, then pool.
	std::unique_lock<std::mutex> playersLock(sActivePlayersMutex);
	size_t otherPendingBytes = 0;
	for (const auto& player : sActivePlayers)
		if (player.component != nullptr && player.component != this &&
			player.component->mContext == nullptr)
			otherPendingBytes += player.component->mReservedVideoBytes;

	std::unique_lock<std::mutex> poolLock(sBufferPoolMutex);
	sVideoBufferBudgetBytes = maxVideoBytes;
	trimBufferPoolLocked(getBufferPoolCacheLimitBytes(maxVideoBytes), maxVideoBytes);

	for (int i = 0; i < (int)sVideoBufferPool.size(); i++)
	{
		VideoBufferPoolEntry& entry = sVideoBufferPool[i];
		if (!entry.inUse && entry.surfaces[0] != nullptr &&
			entry.width == (int)mVideoWidth && entry.height == (int)mVideoHeight)
		{
			ctx->surfaces[0] = entry.surfaces[0];
			ctx->surfaces[1] = entry.surfaces[1];
			ctx->poolIndex = i;
			entry.inUse = true;
			entry.retiring = false;
			entry.carouselVideo = ctx->carouselVideo;
			entry.countAgainstConcurrentLimit = ctx->countAgainstConcurrentLimit;
			entry.lastUsed = ++sBufferPoolUseCounter;
			mContext = ctx;
			poolLock.unlock();
			playersLock.unlock();
			resize();
			return ctx;
		}
	}

	if (otherPendingBytes > maxVideoBytes || bufferBytes > maxVideoBytes - otherPendingBytes)
	{
		poolLock.unlock();
		playersLock.unlock();
		delete ctx;
		return nullptr;
	}

	// Free LRU idle buffers until this allocation plus every parser reservation
	// fits. Retiring entries are in-use and therefore cannot be evicted early.
	const size_t maxExistingBytes = maxVideoBytes - otherPendingBytes - bufferBytes;
	trimBufferPoolLocked(getBufferPoolCacheLimitBytes(maxVideoBytes), maxExistingBytes);

	size_t allocatedBytes = 0;
	for (const auto& entry : sVideoBufferPool)
		if (entry.surfaces[0] != nullptr)
			allocatedBytes += (size_t)entry.width * (size_t)entry.height * 4 * 2;

	if (allocatedBytes > maxExistingBytes)
	{
		poolLock.unlock();
		playersLock.unlock();
		delete ctx;
		return nullptr;
	}

	ctx->surfaces[0] = new (std::nothrow) unsigned char[frameBytes];
	ctx->surfaces[1] = new (std::nothrow) unsigned char[frameBytes];
	if (ctx->surfaces[0] == nullptr || ctx->surfaces[1] == nullptr)
	{
		poolLock.unlock();
		playersLock.unlock();
		delete ctx;
		return nullptr;
	}

	int poolIndex = -1;
	for (int i = 0; i < (int)sVideoBufferPool.size(); ++i)
	{
		if (!sVideoBufferPool[i].inUse && sVideoBufferPool[i].surfaces[0] == nullptr)
		{
			poolIndex = i;
			break;
		}
	}

	VideoBufferPoolEntry entry;
	entry.width = mVideoWidth;
	entry.height = mVideoHeight;
	entry.surfaces[0] = ctx->surfaces[0];
	entry.surfaces[1] = ctx->surfaces[1];
	entry.inUse = true;
	entry.retiring = false;
	entry.carouselVideo = ctx->carouselVideo;
	entry.countAgainstConcurrentLimit = ctx->countAgainstConcurrentLimit;
	entry.lastUsed = ++sBufferPoolUseCounter;

	if (poolIndex >= 0)
		sVideoBufferPool[poolIndex] = entry;
	else
	{
		sVideoBufferPool.push_back(entry);
		poolIndex = (int)sVideoBufferPool.size() - 1;
	}

	ctx->poolIndex = poolIndex;
	mContext = ctx;
	poolLock.unlock();
	playersLock.unlock();
	resize();
	return ctx;
}

#if WIN32
#include <Windows.h>
#pragma comment(lib, "Version.lib")

// If Vlc2 dlls have been upgraded with vlc3 dlls, libqt4_plugin.dll is not compatible, so check if libvlc is 3.x then delete obsolete libqt4_plugin.dll
void _checkUpgradedVlcVersion()
{
	char str[1024] = { 0 };
	if (GetModuleFileNameA(NULL, str, 1024) == 0)
		return;

	auto dir = Utils::FileSystem::getParent(str);
	auto path = Utils::FileSystem::getPreferredPath(Utils::FileSystem::combine(dir, "libvlc.dll"));
	if (Utils::FileSystem::exists(path))
	{
		// Get the version information for the file requested
		DWORD dwSize = GetFileVersionInfoSize(path.c_str(), NULL);
		if (dwSize == 0)
		{
			printf("Error in GetFileVersionInfoSize: %d\n", GetLastError());
			return;
		}

		BYTE                *pbVersionInfo = NULL;
		VS_FIXEDFILEINFO    *pFileInfo = NULL;
		UINT                puLenFileInfo = 0;

		pbVersionInfo = new BYTE[dwSize];

		if (!GetFileVersionInfo(path.c_str(), 0, dwSize, pbVersionInfo))
		{
			printf("Error in GetFileVersionInfo: %d\n", GetLastError());
			delete[] pbVersionInfo;
			return;
		}

		if (!VerQueryValue(pbVersionInfo, TEXT("\\"), (LPVOID*)&pFileInfo, &puLenFileInfo))
		{
			printf("Error in VerQueryValue: %d\n", GetLastError());
			delete[] pbVersionInfo;
			return;
		}

		// FileVersion for libvlc.dll is >= 3.x.x.x ???
		if (HIWORD(pFileInfo->dwFileVersionMS) >= 3)
		{
			auto badV2PluginPath = Utils::FileSystem::getPreferredPath(Utils::FileSystem::combine(dir, "plugins/gui/libqt4_plugin.dll"));
			if (Utils::FileSystem::exists(badV2PluginPath))
				Utils::FileSystem::removeFile(badV2PluginPath);
		}
	}
}
#endif

void VideoVlcComponent::init()
{
	if (mVLC != nullptr)
		return;

	std::vector<std::string> cmdline;
	cmdline.push_back("--quiet");
	cmdline.push_back("--no-video-title-show");

	std::string commandLine = SystemConf::getInstance()->get("vlc.commandline");
	if (!commandLine.empty())
	{
		std::vector<std::string> tokens = Utils::String::split(commandLine, ' ');
		for (auto token : tokens)
			cmdline.push_back(token);
	}

	const char* *theArgs = new const char*[cmdline.size()];

	for (int i = 0 ; i < cmdline.size() ; i++)
		theArgs[i] = cmdline[i].c_str();

#if WIN32
	_checkUpgradedVlcVersion();
#endif

	mVLC = libvlc_new(cmdline.size(), theArgs);

	delete[] theArgs;
}

bool VideoVlcComponent::createMedia(bool forceSoftwareDecoder)
{
#ifdef WIN32
	const std::string path = Utils::String::replace(mVideoPath, "/", "\\");
#else
	const std::string path = mVideoPath;
#endif

	mMedia = libvlc_media_new_path(mVLC, path.c_str());
	if (mMedia == nullptr)
		return false;

	bool explicitHardwareOption = false;
	bool explicitSoftwareDecoder = false;
	const std::string options = SystemConf::getInstance()->get("vlc.options");
	if (!options.empty())
	{
		for (const auto& token : Utils::String::split(options, ' '))
		{
			if (token.empty())
				continue;
			libvlc_media_add_option(mMedia, token.c_str());
			if (token.find("avcodec-hw=") != std::string::npos)
			{
				explicitHardwareOption = true;
				explicitSoftwareDecoder = token.find("avcodec-hw=none") != std::string::npos;
			}
		}
	}

#if WIN32
	if (forceSoftwareDecoder)
	{
		// Added after custom options so the one-shot fallback is authoritative.
		libvlc_media_add_option(mMedia, ":avcodec-hw=none");
		mUsingHardwareDecoder = false;
	}
	else
	{
		if (!explicitHardwareOption)
			libvlc_media_add_option(mMedia, ":avcodec-hw=any");
		mUsingHardwareDecoder = !explicitSoftwareDecoder;
	}
	libvlc_media_add_option(mMedia, ":no-spu");
#else
	(void)forceSoftwareDecoder;
	mUsingHardwareDecoder = false;
#endif

	if (!shouldPlayAudio())
	{
		// Decorative menu videos explicitly use setPlayAudio(false), carousel tags
		// are always silent, and a disabled global audio option should also avoid
		// creating an audio decoder. The selected game's preview keeps its policy.
		libvlc_media_add_option(mMedia, ":no-audio");
	}
#if WIN32
	if (isCarouselVideo())
		libvlc_media_add_option(mMedia, ":input-repeat=65535");
#endif

	if (mPlaylist != nullptr && mConfig.startDelay == 0 &&
		!mConfig.showSnapshotDelay && !mConfig.showSnapshotNoVideo)
		libvlc_media_add_option(mMedia, ":start-time=0.7");

	mIsParsing = false;
#if LIBVLC_VERSION_MAJOR >= 3
	#if WIN32
		const char* vlcVersion = libvlc_get_version();
		if (vlcVersion[0] < '3')
			libvlc_media_parse(mMedia);
		else
	#endif
	{
		const int parseResult = libvlc_media_parse_with_options(
			mMedia, libvlc_media_parse_local, 5000);
		if (parseResult != 0)
		{
			LOG(LogWarning) << "[VideoVlcComponent] failed to start media parsing: " << mVideoPath;
			libvlc_media_release(mMedia);
			mMedia = nullptr;
			return false;
		}
		if ((int)libvlc_media_get_parsed_status(mMedia) == 0)
		{
			mIsParsing = true;
			return true;
		}
	}
#else
	libvlc_media_parse(mMedia);
#endif

	onMediaParsed();
	return mIsParsing || mMediaPlayer != nullptr;
}

void VideoVlcComponent::releaseMediaForDecoderRetry()
{
	if (mAudioPlaybackRegistered)
	{
		AudioManager::setVideoPlaying(false);
		mAudioPlaybackRegistered = false;
	}

	if (mMediaPlayer != nullptr)
	{
		// The release worker is deliberately serialized and can have a short
		// backlog while a carousel is moving. Silence this player immediately so
		// audio from the previous selection cannot continue until its release job
		// reaches the front of the queue.
		libvlc_audio_set_mute(mMediaPlayer, 1);
		queueMediaPlayerRelease(mContext, mMediaPlayer);
	}
	else if (mContext != nullptr)
		releaseContext(mContext);

	mMediaPlayer = nullptr;
	mContext = nullptr;
	if (mMedia != nullptr)
		libvlc_media_release(mMedia);
	mMedia = nullptr;
	mIsParsing = false;
	mIsPlaying = false;
	mIsWaitingForVideoToStart = true;
	mTexture = nullptr;
	mVideoWidth = 0;
	mVideoHeight = 0;
	mHasAudioTrack = false;
	mLastPlaybackTime = -1;
	mLastPlaybackProgressTick = SDL_GetTicks();
	mLastPlaybackRestartTick = 0;
	mPlaybackStartedTick = 0;
	mPlaybackRestartAttempts = 0;
}

bool VideoVlcComponent::trySoftwareDecoderFallback()
{
#if WIN32
	if (!mUsingHardwareDecoder || mHardwareFallbackAttempted)
		return false;

	mHardwareFallbackAttempted = true;
	mSoftwareDecoderPath = mVideoPath;
	LOG(LogWarning) << "[VideoVlcComponent] hardware decoding failed; retrying once in software: "
		<< mVideoPath;
	// The old decoder remains alive until the release worker finishes. Re-enter
	// through the normal slot allocator so that retiring hardware + replacement
	// software never exceed the XML decoder count or RAM budget.
	stopVideo();
	deferPlayback(100);
	return true;
#else
	return false;
#endif
}

void VideoVlcComponent::resetPlaybackFailures()
{
	mPlaybackFailurePath.clear();
	mPlaybackFailureCount = 0;
	mPlaybackFailureBlockedUntil = 0;
}

void VideoVlcComponent::failPlayback(unsigned retryDelay, bool countFailure)
{
	const std::string failedPath = mVideoPath;
	if (countFailure)
	{
		if (mPlaybackFailurePath != failedPath)
		{
			mPlaybackFailurePath = failedPath;
			mPlaybackFailureCount = 0;
		}
		mPlaybackFailureCount++;
	}

	stopVideo();
	if (failedPath.empty())
		return;

	if (!countFailure || mPlaybackFailureCount <= 3)
	{
		const unsigned int delay = countFailure ?
			std::min(15000U, retryDelay * (unsigned int)mPlaybackFailureCount) : retryDelay;
		deferPlayback(delay);
		return;
	}

	// A broken/unsupported file must not be reopened every frame forever. Keep
	// the component cheap, but retry after a long cooldown so replaced media can
	// recover without restarting EmulationStation.
	mPlaybackFailureBlockedUntil = SDL_GetTicks() + 60000;
	LOG(LogWarning) << "[VideoVlcComponent] pausing retries for 60 seconds after repeated failures: "
		<< failedPath;
}

void VideoVlcComponent::handleLooping()
{
	if (!mIsPlaying || mMediaPlayer == nullptr || mMedia == nullptr || mIsParsing)
		return;

	const libvlc_state_t state = libvlc_media_player_get_state(mMediaPlayer);
	const unsigned int now = SDL_GetTicks();
	const long long playbackTime = (long long)libvlc_media_player_get_time(mMediaPlayer);
	if (playbackTime >= 0 &&
		(mLastPlaybackTime < 0 || playbackTime > mLastPlaybackTime ||
			playbackTime + 500 < mLastPlaybackTime))
	{
		mLastPlaybackTime = playbackTime;
		mLastPlaybackProgressTick = now;
		mPlaybackRestartAttempts = 0;
	}

	const bool hardwareStall = mUsingHardwareDecoder && state == libvlc_Playing &&
		playbackTime >= 0 && now - mLastPlaybackProgressTick >= 6000;
	const bool decoderStopped = state == libvlc_Stopped;
	if (state == libvlc_Error || hardwareStall || (decoderStopped && mUsingHardwareDecoder))
	{
		if (trySoftwareDecoderFallback())
			return;
		failPlayback(2000);
		return;
	}

	if (isCarouselVideo())
	{
		const bool expectedEnd = state == libvlc_Ended;
		const bool terminalState = expectedEnd || decoderStopped;
		const bool unexpectedlyPaused =
			state == libvlc_Paused && now - mLastPlaybackProgressTick >= 1500;
		const bool stalledWhilePlaying = state == libvlc_Playing && playbackTime >= 0 &&
			now - mLastPlaybackProgressTick >= 4000;

		if ((terminalState || unexpectedlyPaused || stalledWhilePlaying) &&
			now - mLastPlaybackRestartTick >= 1000)
		{
			if (!expectedEnd && ++mPlaybackRestartAttempts > 2)
			{
				failPlayback(2000);
				return;
			}

			libvlc_media_player_set_media(mMediaPlayer, mMedia);
			if (libvlc_media_player_play(mMediaPlayer) < 0)
			{
				if (!trySoftwareDecoderFallback())
					failPlayback(2000);
				return;
			}

			mLastPlaybackTime = -1;
			mLastPlaybackProgressTick = now;
			mLastPlaybackRestartTick = now;
			mPlaybackStartedTick = now;
		}
		return;
	}
	if (decoderStopped)
	{
		failPlayback(2000);
		return;
	}

	if (state != libvlc_Ended)
		return;

	if (mLoops >= 0)
	{
		mCurrentLoop++;
		if (mCurrentLoop > mLoops)
		{
			stopVideo();
			mFadeIn = 0.0;
			mPlayingVideoPath = "";
			mVideoPath = "";
			return;
		}
	}

	if (mPlaylist != nullptr)
	{
		const auto nextVideo = mPlaylist->getNextItem();
		if (!nextVideo.empty())
		{
			stopVideo();
			setVideo(nextVideo);
			return;
		}
		mPlaylist = nullptr;
	}

	if (mVideoEnded != nullptr && !mVideoEnded())
	{
		stopVideo();
		return;
	}

	if (!shouldPlayAudio())
		libvlc_audio_set_mute(mMediaPlayer, 1);
	libvlc_media_player_set_media(mMediaPlayer, mMedia);
	if (libvlc_media_player_play(mMediaPlayer) < 0)
	{
		if (!trySoftwareDecoderFallback())
			failPlayback(2000);
		return;
	}
	mPlaybackStartedTick = now;
}

void VideoVlcComponent::onMediaParsed()
{
	StopWatch stopWatch("[VideoVlcComponent] onMediaParsed", LogDebug);
	if (mMedia == nullptr)
		return;

	mVideoWidth = 0;
	mVideoHeight = 0;
	mHasAudioTrack = false;
	libvlc_media_track_t** tracks = nullptr;
	const unsigned trackCount = libvlc_media_tracks_get(mMedia, &tracks);
	for (unsigned track = 0; track < trackCount; ++track)
	{
		if (tracks[track]->i_type == libvlc_track_audio)
			mHasAudioTrack = true;
		else if (tracks[track]->i_type == libvlc_track_video)
		{
			mVideoWidth = tracks[track]->video->i_width;
			mVideoHeight = tracks[track]->video->i_height;
		}
	}
	if (tracks != nullptr)
		libvlc_media_tracks_release(tracks, trackCount);

	if (mVideoWidth == 0 && mVideoHeight == 0 &&
		Utils::FileSystem::isAudio(mPlayingVideoPath) && shouldPlayAudio() && !mScreensaverMode)
	{
		mVideoWidth = 1;
		mVideoHeight = 1;
	}

	if (mVideoWidth <= 0 || mVideoHeight <= 0)
	{
		failPlayback(2000);
		return;
	}

	if (mVideoWidth > 1 && Settings::getInstance()->getBool("OptimizeVideo"))
	{
		Vector2f maxSize(Renderer::getScreenWidth(), Renderer::getScreenHeight());
#ifdef _RPI_
		if (!Renderer::isSmallScreen())
			maxSize = Vector2f(400, 300);
#endif
		if (!mTargetSize.empty() &&
			(mTargetSize.x() < maxSize.x() || mTargetSize.y() < maxSize.y()))
			maxSize = mTargetSize;

		const auto size = ImageIO::adjustPictureSize(
			Vector2i(mVideoWidth, mVideoHeight), Vector2i(maxSize.x(), maxSize.y()), mTargetIsMin);
		if (size.x() < mVideoWidth || size.y() < mVideoHeight)
		{
			mVideoWidth = size.x();
			mVideoHeight = size.y();
		}
	}

	if (!updatePlaybackReservation(getVideoBufferBytes()))
	{
		failPlayback(300);
		return;
	}

	mMediaPlayer = libvlc_media_player_new_from_media(mMedia);
	if (mMediaPlayer == nullptr)
	{
		if (!trySoftwareDecoderFallback())
			failPlayback(2000);
		return;
	}

	mContext = rentContext();
	if (mContext == nullptr)
	{
		failPlayback(300);
		return;
	}

	const unsigned int now = SDL_GetTicks();
	mLastPlaybackTime = -1;
	mLastPlaybackProgressTick = now;
	mLastPlaybackRestartTick = 0;
	mPlaybackStartedTick = now;
	mPlaybackRestartAttempts = 0;

	if (mHasAudioTrack && shouldPlayAudio())
	{
		AudioManager::setVideoPlaying(true);
		mAudioPlaybackRegistered = true;
	}
	else if (mHasAudioTrack)
		libvlc_audio_set_mute(mMediaPlayer, 1);

	if (mVideoWidth > 1)
	{
		libvlc_video_set_callbacks(mMediaPlayer, lock, unlock, display, (void*)mContext);
		libvlc_video_set_format(mMediaPlayer, "RGBA", (int)mVideoWidth,
			(int)mVideoHeight, (int)mVideoWidth * 4);
	}

	if (libvlc_media_player_play(mMediaPlayer) < 0)
	{
		if (!trySoftwareDecoderFallback())
			failPlayback(2000);
		return;
	}
	registerActivePlayer();
}

void VideoVlcComponent::startVideo()
{
	if (mSharedVideoSource != nullptr)
		return;

	if (mIsPlaying || mIsParsing || mMediaPlayer != nullptr || mMedia != nullptr || !mVLC)
		return;

	if (mVideoPath.empty())
	{
		stopVideo();
		return;
	}

	if (mPlaybackFailurePath != mVideoPath)
	{
		mPlaybackFailurePath = mVideoPath;
		mPlaybackFailureCount = 0;
		mPlaybackFailureBlockedUntil = 0;
	}
	else if (mPlaybackFailureCount > 3 && mPlaybackFailureBlockedUntil != 0)
	{
		const unsigned int now = SDL_GetTicks();
		if ((int)(now - mPlaybackFailureBlockedUntil) < 0)
		{
			deferPlayback(mPlaybackFailureBlockedUntil - now);
			return;
		}

		mPlaybackFailureCount = 0;
		mPlaybackFailureBlockedUntil = 0;
	}

	if (!acquirePlaybackSlot())
	{
		deferPlayback(300);
		return;
	}

	StopWatch stopWatch("[VideoVlcComponent] startVideo", LogDebug);
	if (hasStoryBoard("", true) && mConfig.startDelay > 0)
		startStoryboard();

	mTexture = nullptr;
	mCurrentLoop = 0;
	mIsParsing = false;
	mPlayingVideoPath = mVideoPath;
	mPlaybackRestartAttempts = 0;
	mHasAudioTrack = false;
	if (mSoftwareDecoderPath != mVideoPath)
	{
		mSoftwareDecoderPath.clear();
		mHardwareFallbackAttempted = false;
	}
	else
		mHardwareFallbackAttempted = true;

	if (!mPowerSaverPaused)
	{
		PowerSaver::pause();
		mPowerSaverPaused = true;
	}

	const bool forceSoftwareDecoder = mSoftwareDecoderPath == mVideoPath;
	if (!createMedia(forceSoftwareDecoder) && mIsRegisteredActive)
		failPlayback(2000);
}

void VideoVlcComponent::stopVideo()
{
	clearPlaybackDeferred();
	unregisterActivePlayer();

	const bool hadResources = mMediaPlayer != nullptr || mMedia != nullptr || mContext != nullptr;
	if (hadResources)
	{
		StopWatch stopWatch("[VideoVlcComponent] stopVideo", LogDebug);
		releaseMediaForDecoderRetry();
	}

	mIsPlaying = false;
	mIsWaitingForVideoToStart = false;
	mStartDelayed = false;
	mIsParsing = false;
	mUsingHardwareDecoder = false;
	mTexture = nullptr;
	mLastPlaybackTime = -1;
	mLastPlaybackProgressTick = SDL_GetTicks();
	mLastPlaybackRestartTick = 0;
	mPlaybackStartedTick = 0;
	mPlaybackRestartAttempts = 0;

	if (mAudioPlaybackRegistered)
	{
		AudioManager::setVideoPlaying(false);
		mAudioPlaybackRegistered = false;
	}
	if (mPowerSaverPaused)
	{
		PowerSaver::resume();
		mPowerSaverPaused = false;
	}
}

void VideoVlcComponent::applyTheme(const std::shared_ptr<ThemeData>& theme, const std::string& view, const std::string& element, unsigned int properties)
{
	using namespace ThemeFlags;

	const ThemeData::ThemeElement* elem = theme->getElement(view, element, "video");
	if (!elem)
		return;

	if (elem && elem->has("effect"))
	{
		if (!(elem->get<std::string>("effect").compare("slideRight")))
			mEffect = VideoVlcFlags::VideoVlcEffect::SLIDERIGHT;
		else if (!(elem->get<std::string>("effect").compare("size")))
			mEffect = VideoVlcFlags::VideoVlcEffect::SIZE;
		else if (!(elem->get<std::string>("effect").compare("bump")))
			mEffect = VideoVlcFlags::VideoVlcEffect::BUMP;
		else
			mEffect = VideoVlcFlags::VideoVlcEffect::NONE;

		mConfig.scaleSnapshot = (mEffect != VideoVlcFlags::VideoVlcEffect::NONE);
	}

	if (elem && elem->has("roundCorners"))
		setRoundCorners(elem->get<float>("roundCorners"));
	
	if (properties & COLOR)
	{
		if (elem && elem->has("color"))
			setColorShift(elem->get<unsigned int>("color"));

		if (elem->has("linearSmooth"))
			mLinearSmooth = elem->get<bool>("linearSmooth");

		if (elem->has("saturation"))
			setSaturation(Math::clamp(elem->get<float>("saturation"), 0.0f, 1.0f));

		if (ThemeData::parseCustomShader(elem, &mCustomShader))
			updateRoundCorners();

		mStaticImage.setCustomShader(mCustomShader);
	}

	if (elem && elem->has("loops"))
		mLoops = (int)elem->get<float>("loops");
	else
		mLoops = -1;

	VideoComponent::applyTheme(theme, view, element, properties);
}

void VideoVlcComponent::update(int deltaTime)
{
	mElapsed += deltaTime;

	if (mConfig.showSnapshotNoVideo || mConfig.showSnapshotDelay)
		mStaticImage.update(deltaTime);

	if (mIsParsing && mMedia != nullptr && libvlc_media_get_parsed_status(mMedia) != 0)
	{
		mIsParsing = false;
		onMediaParsed();
	}

	// VLC callbacks run on decoder threads. Publish frame readiness there, but
	// perform component state changes here on the UI thread. Audio-only media has
	// no frame callback, so VLC's Playing state is its start signal.
	if (!mIsParsing && mMediaPlayer != nullptr && !mIsPlaying &&
		mIsWaitingForVideoToStart)
	{
		bool started = mVideoWidth <= 1 &&
			libvlc_media_player_get_state(mMediaPlayer) == libvlc_Playing;
		if (!started && mContext != nullptr)
		{
			for (int frame = 0; frame < 2 && !started; ++frame)
			{
				std::lock_guard<std::mutex> frameLock(mContext->mutexes[frame]);
				started = mContext->hasFrame[frame].load(std::memory_order_acquire);
			}
		}

		if (started)
			onVideoStarted();
	}

	// A decoder can fail before VLC produces its first display callback, in
	// which case handleLooping() is not active yet. Catch both an explicit error
	// and an opening stall here, with one hardware-to-software transition only.
	if (mVideoWidth > 1 && !mIsParsing && mMediaPlayer != nullptr && !mIsPlaying &&
		mIsWaitingForVideoToStart && mPlaybackStartedTick != 0)
	{
		const unsigned int now = SDL_GetTicks();
		const libvlc_state_t state = libvlc_media_player_get_state(mMediaPlayer);
		const unsigned int timeout = mUsingHardwareDecoder ? 8000 : 12000;
		if (state == libvlc_Error || now - mPlaybackStartedTick >= timeout)
		{
			if (!trySoftwareDecoderFallback())
				failPlayback(2000);
		}
	}
	
	VideoComponent::update(deltaTime);
}

void VideoVlcComponent::onShow()
{
	VideoComponent::onShow();
	mStaticImage.onShow();

	if (hasStoryBoard("", true) && mConfig.startDelay > 0)
		pauseStoryboard();
}

ThemeData::ThemeElement::Property VideoVlcComponent::getProperty(const std::string name)
{
	Vector2f scale = getParent() ? getParent()->getSize() : Vector2f((float)Renderer::getScreenWidth(), (float)Renderer::getScreenHeight());
	
	if (Utils::String::startsWith(name, "shader."))
	{
		auto prop = name.substr(7);

		auto it = mCustomShader.parameters.find(prop);
		if (it != mCustomShader.parameters.cend())
			return Utils::String::toFloat(it->second);

		return 0.0f;
	}

	if (name == "size" || name == "maxSize" || name == "minSize")
		return mSize / scale;

	if (name == "color")
		return mColorShift;

	if (name == "roundCorners")
		return mRoundCorners;

	if (name == "saturation")
		return mSaturation;

	return VideoComponent::getProperty(name);
}

void VideoVlcComponent::setProperty(const std::string name, const ThemeData::ThemeElement::Property& value)
{
	Vector2f scale = getParent() ? getParent()->getSize() : Vector2f((float)Renderer::getScreenWidth(), (float)Renderer::getScreenHeight());
	
	if (value.type == ThemeData::ThemeElement::Property::PropertyType::Pair && (name == "maxSize" || name == "minSize"))
	{
		mSourceBounds.zw() = value.v;
		mTargetSize = Vector2f(value.v.x() * scale.x(), value.v.y() * scale.y());
		resize();
	}
	else if (value.type == ThemeData::ThemeElement::Property::PropertyType::Int && name == "color")
		setColorShift(value.i);
	else if (value.type == ThemeData::ThemeElement::Property::PropertyType::Float && name == "roundCorners")
		setRoundCorners(value.f);
	else if (value.type == ThemeData::ThemeElement::Property::PropertyType::Float && name == "saturation")
		setSaturation(value.f);
	else if (value.type == ThemeData::ThemeElement::Property::PropertyType::Float && Utils::String::startsWith(name, "shader."))
	{
		auto prop = name.substr(7);

		auto it = mCustomShader.parameters.find(prop);
		if (it != mCustomShader.parameters.cend())
			mCustomShader.parameters[prop] = std::to_string(value.f);
	}
	else 
		VideoComponent::setProperty(name, value);
}

void VideoVlcComponent::pauseVideo()
{
	if (!mIsPlaying && !mIsWaitingForVideoToStart && !mStartDelayed)
		return;

	mIsPlaying = false;
	mIsWaitingForVideoToStart = false;
	mStartDelayed = false;

	if (mMediaPlayer == NULL || mMedia == NULL)
		stopVideo();
	else
	{
		libvlc_media_player_pause(mMediaPlayer);
		if (mPowerSaverPaused)
		{
			PowerSaver::resume();
			mPowerSaverPaused = false;
		}
		if (mAudioPlaybackRegistered)
		{
			AudioManager::setVideoPlaying(false);
			mAudioPlaybackRegistered = false;
		}
	}
}

void VideoVlcComponent::resumeVideo()
{
	if (mIsPlaying)
		return;

	if (mMediaPlayer == NULL || mMedia == NULL)
	{
		startVideoWithDelay();
		return;
	}

	if (libvlc_media_player_play(mMediaPlayer) < 0)
	{
		if (!trySoftwareDecoderFallback())
			failPlayback(2000);
		return;
	}

	mIsPlaying = true;
	mLastPlaybackTime = -1;
	mLastPlaybackProgressTick = SDL_GetTicks();
	mPlaybackStartedTick = mLastPlaybackProgressTick;
	if (!mPowerSaverPaused)
	{
		PowerSaver::pause();
		mPowerSaverPaused = true;
	}
	if (mHasAudioTrack && shouldPlayAudio() && !mAudioPlaybackRegistered)
	{
		AudioManager::setVideoPlaying(true);
		mAudioPlaybackRegistered = true;
	}
}

bool VideoVlcComponent::isPaused()
{
	return !mIsPlaying && !mIsWaitingForVideoToStart && !mStartDelayed && mMedia != NULL;
}

void VideoVlcComponent::setSaturation(float saturation)
{
	mSaturation = saturation;
}
