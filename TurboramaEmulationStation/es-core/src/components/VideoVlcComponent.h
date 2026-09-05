#pragma once
#ifndef ES_CORE_COMPONENTS_VIDEO_VLC_COMPONENT_H
#define ES_CORE_COMPONENTS_VIDEO_VLC_COMPONENT_H

#include "VideoComponent.h"
#include "ThemeData.h"
#include "renderers/Renderer.h"
#include <atomic>
#include <cstddef>
#include <mutex>
#include <set>
#include <string>
#include <vector>

struct libvlc_instance_t;
struct libvlc_media_t;
struct libvlc_media_player_t;

struct VideoContext 
{
	VideoContext()
	{
		surfaces[0] = nullptr;
		surfaces[1] = nullptr;
		component = nullptr;		
		hasFrame[0] = false;
		hasFrame[1] = false;
		surfaceId = 0;
		poolIndex = -1;
		bufferWidth = 0;
		bufferHeight = 0;
		carouselVideo = false;
		countAgainstConcurrentLimit = false;
	}

	~VideoContext()
	{
		if (poolIndex < 0)
		{
			if (surfaces[0])
				delete[] surfaces[0];

			if (surfaces[1])
				delete[] surfaces[1];
		}

		surfaces[0] = nullptr;
		surfaces[1] = nullptr;
	}

	std::atomic<int>			surfaceId;
	unsigned char*		surfaces[2];	
	std::mutex			mutexes[2];
	std::atomic<bool>		hasFrame[2];

	VideoComponent*		component;
	std::mutex			componentMutex;
	int					poolIndex;
	int					bufferWidth;
	int					bufferHeight;
	bool				carouselVideo;
	bool				countAgainstConcurrentLimit;
};


namespace VideoVlcFlags
{
	enum VideoVlcEffect
	{
		NONE,
		BUMP,
		SIZE,
		SLIDERIGHT
	};
}

struct VideoBufferPoolEntry
{
	int width;
	int height;
	unsigned char* surfaces[2];
	bool inUse;
	bool retiring;
	bool carouselVideo;
	bool countAgainstConcurrentLimit;
	unsigned long long lastUsed;
};

class VideoVlcComponent : public VideoComponent
{
	// Structure that groups together the configuration of the video component
	struct Configuration
	{
		unsigned						startDelay;
		bool							showSnapshotNoVideo;
		bool							showSnapshotDelay;
		std::string						defaultVideoPath;
	};

public:
	static void init();
	static bool waitForAudioRelease(unsigned timeoutMs);
	static void releaseContext(VideoContext* ctx);
	static void clearBufferPool();

	VideoVlcComponent(Window* window);
	virtual ~VideoVlcComponent();

	void render(const Transform4x4f& parentTrans) override;

	// Resize the video to fit this size. If one axis is zero, scale that axis to maintain aspect ratio.
	// If both are non-zero, potentially break the aspect ratio.  If both are zero, no resizing.
	// Can be set before or after a video is loaded.
	// setMaxSize() and setResize() are mutually exclusive.
	void setResize(float width, float height);

	// Resize the video to be as large as possible but fit within a box of this size.
	// Can be set before or after a video is loaded.
	// Never breaks the aspect ratio. setMaxSize() and setResize() are mutually exclusive.
	void setMaxSize(float width, float height);
	void setMinSize(float width, float height);

	virtual void applyTheme(const std::shared_ptr<ThemeData>& theme, const std::string& view, const std::string& element, unsigned int properties);
	virtual void update(int deltaTime);	

	void	setColorShift(unsigned int color);

	virtual void onShow() override;

	ThemeData::ThemeElement::Property getProperty(const std::string name) override;
	void setProperty(const std::string name, const ThemeData::ThemeElement::Property& value) override;

	void setEffect(VideoVlcFlags::VideoVlcEffect effect) { mEffect = effect; }
	// A positive value caps active + retiring players in this component's
	// carousel bucket. Zero keeps the global setting/no additional cap.
	void setConcurrentPlaybackLimit(int value) { mConcurrentPlaybackLimit = value > 0 ? value : 0; }
	// Render the same decoded frame through this component's own transform,
	// opacity, z-index and storyboard without starting a second VLC player.
	void setSharedVideoSource(VideoVlcComponent* source);

	bool getLinearSmooth() { return mLinearSmooth; }
	void setLinearSmooth(bool value = true) { mLinearSmooth = value; }

	void setSaturation(float saturation);

	void setRoundCorners(float value) override;
	void onSizeChanged() override;
	void onPaddingChanged() override;
	
	Vector2f getSize() const override;

private:
	// Calculates the correct mSize from our resizing information (set by setResize/setMaxSize).
	// Used internally whenever the resizing parameters or texture change.
	void resize();
	// Start the video Immediately
	virtual void startVideo();
	// Stop the video
	virtual void stopVideo();

	virtual void pauseVideo();
	virtual void resumeVideo();
	virtual bool isPaused();

	// Handle looping the video. Must be called periodically
	virtual void handleLooping();

	virtual void onVideoStarted();

	VideoContext* rentContext();

	void onMediaParsed();
	size_t getVideoBufferBytes() const;
	size_t estimatePendingVideoBufferBytes() const;
	bool updatePlaybackReservation(size_t bytes);
	bool createMedia(bool forceSoftwareDecoder);
	bool trySoftwareDecoderFallback();
	void failPlayback(unsigned retryDelay, bool countFailure = true);
	void resetPlaybackFailures();
	void clearPlaybackDeferred();
	void deferPlayback(unsigned retryDelay);
	void releaseMediaForDecoderRetry();
	bool isCarouselVideo() const;
	bool isThemeManagedVideo();
	bool shouldPlayAudio();
	static void queueMediaPlayerRelease(VideoContext* ctx, libvlc_media_player_t* player);
	static void trimBufferPoolLocked(size_t maxFreeBytes, size_t maxTotalBytes);
	static size_t getBufferPoolCacheLimitBytes(size_t maxVideoBytes);
	static int getMaxVideoRamMb();
	bool mIsParsing;

	void registerActivePlayer();
	void unregisterActivePlayer();
	bool acquirePlaybackSlot();
	int computePlaybackPriority();
	static void notifyPlaybackSlotAvailable();
	static int getEffectiveMaxConcurrentVideos();
	static int getEffectiveMaxConcurrentCarouselVideos();

	struct ActiveVideoPlayer
	{
		VideoVlcComponent*	component;
		int					priority;
	};

	static std::mutex						sActivePlayersMutex;
	static std::vector<ActiveVideoPlayer>	sActivePlayers;
	static std::set<VideoVlcComponent*>		sDeferredPlayers;
	static std::mutex						sBufferPoolMutex;
	static std::vector<VideoBufferPoolEntry> sVideoBufferPool;
	static unsigned long long				sBufferPoolUseCounter;
	static size_t						sVideoBufferBudgetBytes;
	bool									mIsRegisteredActive;
	size_t								mReservedVideoBytes;
	int									mConcurrentPlaybackLimit;

private:
	void crop(float left, float top, float right, float bot);

	static libvlc_instance_t*		mVLC;
	libvlc_media_t*					mMedia;
	libvlc_media_player_t*			mMediaPlayer;
	VideoContext*					mContext;
	std::shared_ptr<TextureResource> mTexture;

	std::string					    mSubtitlePath;
	std::string					    mSubtitleTmpFile;
	Renderer::ShaderInfo			mCustomShader;

	VideoVlcFlags::VideoVlcEffect	mEffect;

	unsigned int					mColorShift;
	int								mElapsed;

	int								mCurrentLoop;
	int								mLoops;
	long long						mLastPlaybackTime;
	unsigned int					mLastPlaybackProgressTick;
	unsigned int					mLastPlaybackRestartTick;
	unsigned int					mPlaybackStartedTick;
	int								mPlaybackRestartAttempts;
	bool							mUsingHardwareDecoder;
	bool							mHardwareFallbackAttempted;
	bool							mHasAudioTrack;
	bool							mAudioPlaybackRegistered;
	bool							mPowerSaverPaused;
	std::string						mSoftwareDecoderPath;
	std::string						mPlaybackFailurePath;
	int								mPlaybackFailureCount;
	unsigned int					mPlaybackFailureBlockedUntil;
	VideoVlcComponent*				mSharedVideoSource;

	bool							mLinearSmooth;
	float							mSaturation;

	void updateVertices();
	void updateColors();
	void updateRoundCorners();
	
	Renderer::Vertex				mVertices[4];
	std::vector<Renderer::Vertex>	mRoundCornerStencil;

	Vector2f mTopLeftCrop;
	Vector2f mBottomRightCrop;
};

#endif // ES_CORE_COMPONENTS_VIDEO_VLC_COMPONENT_H
