#pragma once

#include <string>
#include "components/ImageComponent.h"
#include "components/TextComponent.h"
#include "GameSplash.h"

class Window;
class TextureResource;
class VideoVlcComponent;

#if WIN32
#define DEFAULT_SPLASH_IMAGE ":/splash.svg"
#define OLD_SPLASH_LAYOUT true
#else
#define DEFAULT_SPLASH_IMAGE ":/logo.png"
#define OLD_SPLASH_LAYOUT false
#endif

class Splash
{
public:
	Splash(Window* window, const std::string image, bool fullScreenBackGround = true, IBindable* bindable = nullptr);
	Splash(Window* window, const GameSplash::Media& media);

	~Splash();

	void update(std::string text, float percent = -1);
	void tick(int deltaTime);
	void startPlayback();
	bool isPlaybackFinished() const;
	void render(float opacity, bool swapBuffers = true);

private:
	void initCustomMedia(Window* window, const GameSplash::Media& media);

	ImageComponent  mBackground;
	TextComponent   mText;
	float			mPercent;

	ImageComponent  mInactiveProgressbar;
	ImageComponent  mActiveProgressbar;

	unsigned int	mBackgroundColor;
	float			mRoundCorners;

	std::shared_ptr<TextureResource> mTexture;

	std::vector<GuiComponent*> mExtras;

	bool mCustomMediaMode;
	bool mPlaybackStarted;
	bool mVideoFinished;
	int mImageElapsed;
	VideoVlcComponent* mVideo;
} ;