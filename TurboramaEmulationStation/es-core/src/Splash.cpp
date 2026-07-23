#include "Splash.h"
#include "ThemeData.h"
#include "Window.h"
#include "resources/TextureResource.h"
#include "components/NinePatchComponent.h"
#include "components/VideoVlcComponent.h"
#include <algorithm>
#include <SDL_events.h>
#include "BindingManager.h"

static const int GAME_SPLASH_IMAGE_MS = 2000;
static const int GAME_SPLASH_VIDEO_MIN_MS = 800;
static const int GAME_SPLASH_VIDEO_MAX_MS = 30000;

Splash::Splash(Window* window, const GameSplash::Media& media) :
	mBackground(window),
	mText(window),
	mInactiveProgressbar(window),
	mActiveProgressbar(window),
	mCustomMediaMode(true),
	mPlaybackStarted(false),
	mVideoFinished(false),
	mImageElapsed(0),
	mVideo(nullptr)
{
	mBackgroundColor = 0x000000FF;
	mRoundCorners = 0.01;
	mPercent = -1;

	initCustomMedia(window, media);
	SDL_GL_SetSwapInterval(0);
}

void Splash::initCustomMedia(Window* window, const GameSplash::Media& media)
{
	if (media.type == GameSplash::MediaType::VIDEO)
	{
		mVideo = new VideoVlcComponent(window);
		mVideo->setVideo(media.path);
		mVideo->setOrigin(0.5f, 0.5f);
		mVideo->setPosition(Renderer::getScreenWidth() * 0.5f, Renderer::getScreenHeight() * 0.5f);
		mVideo->setMaxSize((float)Renderer::getScreenWidth(), (float)Renderer::getScreenHeight());
		mVideo->setStartDelay(0);
		mVideo->setPlayAudio(true);
		mVideo->setSize((float)Renderer::getScreenWidth(), (float)Renderer::getScreenHeight());
		mVideo->setOnVideoEnded([this]()
		{
			mVideoFinished = true;
			return false;
		});
		mVideo->topWindow(true);
		mVideo->setVisible(true);
		mVideo->onShow();
	}
	else
	{
		mBackground.setOrigin(0.5f, 0.5f);
		mBackground.setPosition(Renderer::getScreenWidth() * 0.5f, Renderer::getScreenHeight() * 0.5f);
		mBackground.setMaxSize((float)Renderer::getScreenWidth(), (float)Renderer::getScreenHeight());

		auto maxSize = mBackground.getMaxSizeInfo();
		mTexture = TextureResource::get(media.path, false, true, true, false, false, &maxSize);
		mBackground.setImage(mTexture);
	}
}

void Splash::startPlayback()
{
	if (!mCustomMediaMode)
		return;

	mPlaybackStarted = true;
	mImageElapsed = 0;
	mVideoFinished = false;

	if (mVideo != nullptr)
	{
		mVideo->topWindow(true);
		mVideo->setVisible(true);
		mVideo->onShow();
	}
}

void Splash::tick(int deltaTime)
{
	if (!mCustomMediaMode || !mPlaybackStarted)
		return;

	mImageElapsed += deltaTime;

	if (mVideo != nullptr)
		mVideo->update(deltaTime);
}

bool Splash::isPlaybackFinished() const
{
	if (!mCustomMediaMode)
		return true;

	if (!mPlaybackStarted)
		return false;

	if (mVideo != nullptr)
	{
		if (mVideoFinished)
			return true;

		if (mImageElapsed < GAME_SPLASH_VIDEO_MIN_MS)
			return false;

		return mImageElapsed >= GAME_SPLASH_VIDEO_MAX_MS;
	}

	return mImageElapsed >= GAME_SPLASH_IMAGE_MS;
}

Splash::Splash(Window* window, const std::string image, bool fullScreenBackGround, IBindable* bindable) :
	mBackground(window),
	mText(window),
	mInactiveProgressbar(window),
	mActiveProgressbar(window),
	mCustomMediaMode(false),
	mPlaybackStarted(false),
	mVideoFinished(false),
	mImageElapsed(0),
	mVideo(nullptr)
{
	mBackgroundColor = 0x000000FF;
	mRoundCorners = 0.01;
	mPercent = -1;

	bool useOldSplashLayout = OLD_SPLASH_LAYOUT;

	// Theme
	auto theme = std::make_shared<ThemeData>();

	std::string themeFilePath = fullScreenBackGround ? ":/splash.xml" : ":/gamesplash.xml";
	themeFilePath = ResourceManager::getInstance()->getResourcePath(themeFilePath);

	std::map<std::string, ThemeSet> themeSets = ThemeData::getThemeSets();
	auto themeset = themeSets.find(Settings::getInstance()->getString("ThemeSet"));
	// Startup loading uses resources/splash.xml only (no theme splash with logos/avatars).
	if (themeset != themeSets.cend() && !fullScreenBackGround)
	{
		std::string path = themeset->second.path + "/gamesplash.xml";
		if (Utils::FileSystem::exists(path))
			themeFilePath = path;
	}

	if (Utils::FileSystem::exists(themeFilePath))
	{
		std::map<std::string, std::string> sysData;

		try { theme->loadFile("splash", sysData, themeFilePath); }
		catch(...) { }
		
		useOldSplashLayout = false;
	}

	// Background image (optional — skip default ES logo on startup splash)
	std::string imagePath;

	auto backGroundImageTheme = theme->getElement("splash", "background", "image");
	if (backGroundImageTheme && backGroundImageTheme->has("path"))
	{
		std::string tmp = backGroundImageTheme->get<std::string>("path");
		if (ResourceManager::getInstance()->fileExists(backGroundImageTheme->get<std::string>("path")) || tmp.find("{game:") != std::string::npos || tmp.find("{system:") != std::string::npos || tmp.find("{binding:") != std::string::npos)
			imagePath = tmp;
	}
	else if (!fullScreenBackGround)
	{
		imagePath = image;
	}

	bool linearSmooth = false;
	if (backGroundImageTheme && backGroundImageTheme->has("linearSmooth"))
		linearSmooth = backGroundImageTheme->get<bool>("linearSmooth");
	
	if (fullScreenBackGround && !useOldSplashLayout)
	{
		mBackground.setOrigin(0.5, 0.5);
		mBackground.setPosition(Renderer::getScreenWidth() / 2, Renderer::getScreenHeight() / 2);

		if (backGroundImageTheme)
			mBackground.setMaxSize(Renderer::getScreenWidth(), Renderer::getScreenHeight());
		else
		{
			mBackground.setMinSize(Renderer::getScreenWidth(), Renderer::getScreenHeight());
			linearSmooth = true;
		}
	}
	else
	{
		mBackground.setOrigin(0.5, 0.5);
		mBackground.setPosition(Renderer::getScreenWidth() * 0.5f, Renderer::getScreenHeight()  * 0.42f);

		if (useOldSplashLayout)
		{
			mBackground.setMaxSize(Renderer::getScreenWidth() * 0.80f, Renderer::getScreenHeight() * 0.60f);
			mBackground.setRoundCorners(0.02);
		}
		else
		{
			mBackground.setMaxSize(Renderer::getScreenWidth() * 0.71f, Renderer::getScreenHeight() * 0.55f);
			mBackground.setRoundCorners(0.02);
		}
	}

	// Label
	auto font = Font::get(FONT_SIZE_MEDIUM);

	if (useOldSplashLayout)
		mText.setColor(0xFFFFFFD0);
	else
		mText.setColor(0xFFFFFFFF);

	mText.setHorizontalAlignment(ALIGN_CENTER);
	mText.setFont(font);

	if (fullScreenBackGround)
		mText.setPosition(0, Renderer::getScreenHeight() * 0.78f);
	else
		mText.setPosition(0, Renderer::getScreenHeight() * 0.835f);

	mText.setSize(Renderer::getScreenWidth(), font->getLetterHeight());

	Vector2f mProgressPosition;
	Vector2f mProgressSize;

	if (backGroundImageTheme)
		mBackground.applyTheme(theme, "splash", "background", ThemeFlags::ALL ^ (ThemeFlags::PATH));

	if (!imagePath.empty() && ResourceManager::getInstance()->fileExists(imagePath))
	{
		auto maxSize = mBackground.getMaxSizeInfo();
		mTexture = TextureResource::get(imagePath, false, linearSmooth, true, false, false, &maxSize);
		mBackground.setImage(mTexture);

		if (!fullScreenBackGround)
			ResourceManager::getInstance()->removeReloadable(mTexture);
	}

	if (theme->getElement("splash", "label", "text"))
		mText.applyTheme(theme, "splash", "label", ThemeFlags::ALL ^ (ThemeFlags::TEXT));
	else if (fullScreenBackGround)
	{
		mText.setGlowColor(0x00000020);
		mText.setGlowSize(2);
		mText.setGlowOffset(1, 1);
	}

	// Splash background
	auto elem = theme->getElement("splash", "splash", "splash");
	if (elem && elem->has("backgroundColor"))
		mBackgroundColor = elem->get<unsigned int>("backgroundColor");

	// Progressbar
	float baseHeight = 0.036f;

	float w = Renderer::getScreenWidth() / 2.0f;
	float h = Renderer::getScreenHeight() * baseHeight;
	float x = Renderer::getScreenWidth() / 2.0f - w / 2.0f;
	float y = Renderer::getScreenHeight() - (Renderer::getScreenHeight() * 3 * baseHeight);

	auto blankTexture = TextureResource::get(":/white.png", false, true, true, false, false);
	
	mInactiveProgressbar.setImage(blankTexture);
	mActiveProgressbar.setImage(blankTexture);

	mInactiveProgressbar.setPosition(x, y);
	mInactiveProgressbar.setSize(w, h);
	mInactiveProgressbar.setResize(w, h);
	mActiveProgressbar.setPosition(x, y);
	mActiveProgressbar.setSize(w, h);
	mActiveProgressbar.setResize(w, h);

	mInactiveProgressbar.setColorShift(0x6060607F);
	mInactiveProgressbar.setColorShiftEnd(0x9090907F);
	mInactiveProgressbar.setColorGradientHorizontal(true);

	elem = theme->getElement("splash", "progressbar", "image");
	if (elem)
	{
		mInactiveProgressbar.applyTheme(theme, "splash", "progressbar", ThemeFlags::ALL);
		mActiveProgressbar.applyTheme(theme, "splash", "progressbar", ThemeFlags::ALL);

		mRoundCorners = mInactiveProgressbar.getRoundCorners();
	}

	if (useOldSplashLayout)
	{
		mActiveProgressbar.setColorShift(0x006C9EFF);
		mActiveProgressbar.setColorShiftEnd(0x003E5CFF);
	}
	else
	{
		mActiveProgressbar.setColorShift(0xDF1010FF);
		mActiveProgressbar.setColorShiftEnd(0x4F0000FF);
	}

	mActiveProgressbar.setColorGradientHorizontal(true);

	elem = theme->getElement("splash", "progressbar:active", "image");
	if (elem)
		mActiveProgressbar.applyTheme(theme, "splash", "progressbar:active", ThemeFlags::ALL);

	mInactiveProgressbar.setRoundCorners(0);
	mActiveProgressbar.setRoundCorners(0);

	mExtras = ThemeData::makeExtras(theme, "splash", window, true);

	if (bindable != nullptr && !fullScreenBackGround)
	{
		mBackground.setExtraType(ExtraType::EXTRA); // Enable binding
		BindingManager::updateBindings(&mBackground, bindable);

		mText.setExtraType(ExtraType::EXTRA); // Enable binding
		BindingManager::updateBindings(&mText, bindable);
		
		for (auto extra : mExtras)
			BindingManager::updateBindings(extra, bindable);
	}

	if (!fullScreenBackGround)
	{
		auto removeReloadable = [](GuiComponent* p)
		{
			if (p->isKindOf<TextComponent>())
				ResourceManager::getInstance()->removeReloadable(((TextComponent*)p)->getFont());

			if (p->isKindOf<ImageComponent>())
				ResourceManager::getInstance()->removeReloadable(((ImageComponent*)p)->getTexture());

			if (p->isKindOf<NinePatchComponent>())
				ResourceManager::getInstance()->removeReloadable(((NinePatchComponent*)p)->getTexture());
		};

		removeReloadable(&mText);
		removeReloadable(&mBackground);

		for (auto extra : mExtras)
		{			
			removeReloadable(extra);
			for (auto im : extra->enumerateExtraChildrens())
				removeReloadable(im);
		}
	}

	std::stable_sort(mExtras.begin(), mExtras.end(), [](GuiComponent* a, GuiComponent* b) { return b->getZIndex() > a->getZIndex(); });

	// Don't waste time in waiting for vsync
	SDL_GL_SetSwapInterval(0);
}

Splash::~Splash()
{
	Renderer::setSwapInterval();

	if (mVideo != nullptr)
	{
		mVideo->onHide();
		delete mVideo;
		mVideo = nullptr;
	}

	for (auto extra : mExtras)
		delete extra;

	mExtras.clear();
}

void Splash::update(std::string text, float percent)
{
	mText.setText(text);
	mPercent = percent;
}

void Splash::render(float opacity, bool swapBuffers)
{
	if (opacity == 0)
		return;

	unsigned char alpha = (unsigned char)(opacity * 255);

	if (mCustomMediaMode)
	{
		Transform4x4f trans = Transform4x4f::Identity();
		Renderer::setMatrix(trans);
		Renderer::drawRect(0, 0, Renderer::getScreenWidth(), Renderer::getScreenHeight(), (mBackgroundColor & 0xFFFFFF00) | alpha);

		if (mVideo != nullptr)
		{
			mVideo->setOpacity(alpha);
			mVideo->render(trans);
		}
		else if (mTexture)
		{
			mBackground.setOpacity(alpha);
			mBackground.render(trans);
		}

		if (swapBuffers)
		{
			Renderer::swapBuffers();

#if defined(_WIN32)
			SDL_Event event;
			while (SDL_PollEvent(&event));
#endif
		}

		return;
	}

	mText.setOpacity(alpha);

	Transform4x4f trans = Transform4x4f::Identity();
	Renderer::setMatrix(trans);
	Renderer::drawRect(0, 0, Renderer::getScreenWidth(), Renderer::getScreenHeight(), (mBackgroundColor & 0xFFFFFF00) | alpha);

	for (auto extra : mExtras)
		extra->setOpacity(alpha);

	for (auto extra : mExtras)
		if (extra->getZIndex() < 5)
			extra->render(trans);

	if (mTexture)
	{
		mBackground.setOpacity(alpha);
		mBackground.render(trans);
	}

	for (auto extra : mExtras)
		if (extra->getZIndex() >= 5 && extra->getZIndex() < 10)
			extra->render(trans);

	if (mPercent >= 0)
	{
		Renderer::setMatrix(trans);

		auto pos = mInactiveProgressbar.getPosition();
		auto sz = mInactiveProgressbar.getSize();

		mInactiveProgressbar.setOpacity(alpha);
		mActiveProgressbar.setOpacity(alpha);
		mActiveProgressbar.setSize(sz.x()  * mPercent, sz.y());

		float radius = Math::max(sz.x(), sz.y()) * mRoundCorners;

		if (radius > 1)
			Renderer::enableRoundCornerStencil(pos.x(), pos.y(), sz.x(), sz.y(), radius);

		mInactiveProgressbar.render(trans);
		mActiveProgressbar.render(trans);

		if (radius > 1)
			Renderer::disableStencil();
	}

	if (!mText.getText().empty())
	{
		// Ensure font is loaded
		auto font = mText.getFont();
		if (font != nullptr)
			font->reload();
		else
			Font::get(FONT_SIZE_MEDIUM)->reload();

		mText.render(trans);
	}

	for (auto extra : mExtras)
		if (extra->getZIndex() >= 10)
			extra->render(trans);

	if (swapBuffers)
	{
		Renderer::swapBuffers();

#if defined(_WIN32)
		// Avoid Window Freezing on Windows
		SDL_Event event;
		while (SDL_PollEvent(&event));
#endif
	}
}
