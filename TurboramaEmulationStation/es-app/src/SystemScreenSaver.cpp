#include "SystemScreenSaver.h"

#ifdef _RPI_
#include "components/VideoPlayerComponent.h"
#endif
#include "components/VideoVlcComponent.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"
#include "views/gamelist/IGameListView.h"
#include "views/ViewController.h"
#include "FileData.h"
#include "FileFilterIndex.h"
#include "Log.h"
#include "PowerSaver.h"
#include "Scripting.h"
#include "Sound.h"
#include "SystemData.h"
#include "components/ImageComponent.h"
#include "components/TextComponent.h"
#include <unordered_map>
#include <time.h>
#include "AudioManager.h"
#include "math/Vector2i.h"
#include "SystemConf.h"
#include "ImageIO.h"
#include "utils/Randomizer.h"
#include "Paths.h"
#include "ApiSystem.h"
#include "resources/ProtectedDecorations.h"

#define FADE_TIME					(500)
#define DATE_TIME_UPDATE_INTERVAL	(100)

SystemScreenSaver::SystemScreenSaver(Window* window) :
	mVideoScreensaver(NULL),
	mImageScreensaver(NULL),
	mWindow(window),
	mGamesWithVideosLoaded(false),
	mGamesWithImagesLoaded(false),
	mState(STATE_INACTIVE),
	mOpacity(0.0f),
	mTimer(0),
	mSystemName(""),
	mGameName(""),
	mCurrentGame(NULL),
	mLoadingNext(false)
{

	mWindow->setScreenSaver(this);
	std::string path = getTitleFolder();
	if(!Utils::FileSystem::exists(path))
		Utils::FileSystem::createDirectory(path);
	
	mVideoChangeTime = 30000;
}

SystemScreenSaver::~SystemScreenSaver()
{
	// Delete subtitle file, if existing
	remove(getTitlePath().c_str());
	mCurrentGame = NULL;
	mOwnedCustomGame.reset();
}

bool SystemScreenSaver::allowSleep()
{
	return (mVideoScreensaver == nullptr && mImageScreensaver == nullptr);
}

bool SystemScreenSaver::isScreenSaverActive()
{
	return (mState != STATE_INACTIVE);
}

void SystemScreenSaver::startScreenSaver()
{
	std::string screensaver_behavior = Settings::getInstance()->getString("ScreenSaverBehavior");

	bool loadingNext = mLoadingNext;

	if (mState == STATE_INACTIVE)
		Scripting::fireEvent("screensaver-start", screensaver_behavior);

	stopScreenSaver();


	if (screensaver_behavior == "suspend")
	{
		if (ApiSystem::getInstance()->isScriptingSupported(ApiSystem::SUSPEND))
		{
			ApiSystem::getInstance()->suspend();

			mLoadingNext = false;
			mWindow->cancelScreenSaver();
			return;
		}
		else
			screensaver_behavior = "black";
	}

	if (!loadingNext && Settings::getInstance()->getBool("StopMusicOnScreenSaver")) //(Settings::getInstance()->getBool("VideoAudio") && !Settings::getInstance()->getBool("ScreenSaverVideoMute")))
		AudioManager::getInstance()->deinit();


	if (screensaver_behavior == "random video")
	{
		mVideoChangeTime = Settings::getInstance()->getInt("ScreenSaverSwapVideoTimeout");

		// Configure to fade out the windows, Skip Fading if Instant mode
		mState =  PowerSaver::getMode() == PowerSaver::INSTANT
					? STATE_SCREENSAVER_ACTIVE
					: STATE_FADE_OUT_WINDOW;
	
		if (mState == STATE_FADE_OUT_WINDOW)
		{
			mState = STATE_FADE_IN_VIDEO;
			mOpacity = 1.0f;
		}
		else
			mOpacity = 0.0f;
			
		std::string path;
		if (Settings::getInstance()->getBool("SlideshowScreenSaverCustomVideoSource"))
		{
			path = pickRandomCustomImage(true);
			// TurboRama: se o video esta em screensaver_videos/{sistema}/ficheiro.mp4
			// associa ao SystemData com o mesmo nome da pasta (bezel por plataforma)
			mCurrentGame = bindCustomMediaToSystem(path, true);
		}
		else
		{
			// Load a random video
			path = pickRandomGameMedia(true);

			int retry = 10;
			while (retry > 0 && !Utils::FileSystem::exists(path))
			{
				retry--;
				path = pickRandomGameMedia(true);
			}
		}

		if (!path.empty() && Utils::FileSystem::exists(path))
		{
			LOG(LogDebug) << "VideoScreenSaver::startScreenSaver " << path.c_str() << " (remaining: " << countGameListNodes(true) << ")";

			mVideoScreensaver = std::make_shared<VideoScreenSaver>(mWindow, this);

			// REGRA UNICA: pasta do video = nome do bezel
			// .../screensaver_videos/ps5/jogo.mp4  ->  systems/ps5.png
			// NAO usar SystemData/setGame para moldura (evita bezel errado).
			const std::string folderSys = extractSystemFolderFromCustomMediaPath(path, true);
			if (!folderSys.empty())
			{
				LOG(LogError) << "[SS-BEZEL] folder='" << folderSys << "' video='" << path << "'";
				mVideoScreensaver->setSystemDecoration(folderSys, folderSys);
			}
			else
			{
				LOG(LogError) << "[SS-BEZEL] FALHA extrair pasta do video='" << path << "'";
			}

			mVideoScreensaver->setVideo(path);

			if (mCurrentGame)
				Scripting::fireEvent("game-selected", mCurrentGame->getSystem()->getName(), mCurrentGame->getPath(), mCurrentGame->getName());

			PowerSaver::runningScreenSaver(true);
			mTimer = 0;
			return;
		}
	}
	else if (screensaver_behavior == "slideshow")
	{
		mVideoChangeTime = Settings::getInstance()->getInt("ScreenSaverSwapImageTimeout");

		// Configure to fade out the windows, Skip Fading if Instant mode
		mState = PowerSaver::getMode() == PowerSaver::INSTANT
			? STATE_SCREENSAVER_ACTIVE
			: STATE_FADE_OUT_WINDOW;

		if (mState == STATE_FADE_OUT_WINDOW)
		{
			mState = STATE_FADE_IN_VIDEO;
			mOpacity = 1.0f;
		}
		else
			mOpacity = 0.0f;

		// Load a random image
		std::string path;
		if (Settings::getInstance()->getBool("SlideshowScreenSaverCustomImageSource"))
		{
			path = pickRandomCustomImage();
			// Mesma regra de pasta por sistema (bezel)
			mCurrentGame = bindCustomMediaToSystem(path, false);
		}
		else
			path = pickRandomGameMedia();

		if (!path.empty() && Utils::FileSystem::exists(path))
		{
			LOG(LogDebug) << "ImageScreenSaver::startScreenSaver " << path.c_str() << " (remaining: " << countGameListNodes(false) << ")";

			mImageScreensaver = std::make_shared<ImageScreenSaver>(mWindow);
			mImageScreensaver->setGame(mCurrentGame);
			mImageScreensaver->setImage(path);

			if (mCurrentGame)
				Scripting::fireEvent("game-selected", mCurrentGame->getSystem()->getName(), mCurrentGame->getPath(), mCurrentGame->getName());

			PowerSaver::runningScreenSaver(true);
			mTimer = 0;
			return;
		}	
	}

	// No videos. Just use a standard screensaver
	mState = STATE_SCREENSAVER_ACTIVE;
	mCurrentGame = NULL;
	mOwnedCustomGame.reset();
}

void SystemScreenSaver::stopScreenSaver()
{
	bool isExitingScreenSaver = !mLoadingNext;
	bool isVideoScreenSaver = (mVideoScreensaver != nullptr);

	if (mLoadingNext)
		mFadingImageScreensaver = mImageScreensaver;
	else
		mFadingImageScreensaver = nullptr;

	// so that we stop the background audio next time, unless we're restarting the screensaver
	mLoadingNext = false;

	mVideoScreensaver = nullptr;
	mImageScreensaver = nullptr;

	if (isExitingScreenSaver)
	{
		mCurrentGame = NULL;
		mOwnedCustomGame.reset();
	}

	if(isExitingScreenSaver && mState != STATE_INACTIVE) {
	  Scripting::fireEvent("screensaver-stop");
	}

	// we need this to loop through different videos
	mState = STATE_INACTIVE;
	PowerSaver::runningScreenSaver(false);

	// Exiting screen saver -> Restore sound
	if (isExitingScreenSaver && Settings::getInstance()->getBool("StopMusicOnScreenSaver")) //isVideoScreenSaver && Settings::getInstance()->getBool("VideoAudio") && !Settings::getInstance()->getBool("ScreenSaverVideoMute"))
	{
		AudioManager::getInstance()->init();

		if (Settings::getInstance()->getBool("audio.bgmusic"))
		{
			if (ViewController::get()->getState().viewing == ViewController::GAME_LIST || ViewController::get()->getState().viewing == ViewController::SYSTEM_SELECT)
				AudioManager::getInstance()->changePlaylist(ViewController::get()->getState().getSystem()->getTheme(), true);
			else
				AudioManager::getInstance()->playRandomMusic();
		}
	}
}

void SystemScreenSaver::renderScreenSaver()
{
	Transform4x4f transform = Transform4x4f::Identity();
	
	if (mVideoScreensaver)
	{
		// Render black background
		Renderer::setMatrix(Transform4x4f::Identity());
		Renderer::drawRect(0.0f, 0.0f, Renderer::getScreenWidth(), Renderer::getScreenHeight(), 0x000000FF, 0x000000FF);

		// Only render the video if the state requires it
		if ((int)mState >= STATE_FADE_IN_VIDEO)
		{
			unsigned int opacity = 255 - (unsigned char)(mOpacity * 255);

			mVideoScreensaver->setOpacity(opacity);
			mVideoScreensaver->render(transform);
		}
	}
	else if (mImageScreensaver)
	{
		// Render black background
		Renderer::setMatrix(transform);
		Renderer::drawRect(0.0f, 0.0f, Renderer::getScreenWidth(), Renderer::getScreenHeight(), 0x000000FF);

		if (mFadingImageScreensaver != nullptr)		
			mFadingImageScreensaver->render(transform);

		// Only render the video if the state requires it
		if ((int)mState >= STATE_FADE_IN_VIDEO)
		{			
			if (mImageScreensaver->hasImage())
			{
				unsigned int opacity = 255 - (unsigned char)(mOpacity * 255);
													
				Renderer::setMatrix(transform);
				Renderer::drawRect(0.0f, 0.0f, Renderer::getScreenWidth(), Renderer::getScreenHeight(), 0x00000000 | opacity);
				mImageScreensaver->setOpacity(opacity);
				mImageScreensaver->render(transform);
			}
		}
	}
	else if (mState != STATE_INACTIVE)
	{
		std::string screensaver_behavior = Settings::getInstance()->getString("ScreenSaverBehavior");

		Renderer::setMatrix(Transform4x4f::Identity());
		unsigned char color = screensaver_behavior == "dim" ? 0x000000A0 : 0x000000FF;
		Renderer::drawRect(0.0f, 0.0f, Renderer::getScreenWidth(), Renderer::getScreenHeight(), color, color);
	}
}

unsigned long SystemScreenSaver::countGameListNodes(bool video)
{
	if (video && mGamesWithVideosLoaded)
		return mGamesWithVideos.size();

	if (!video && mGamesWithImagesLoaded)
		return mGamesWithImages.size();

	unsigned long nodeCount = 0;

	if (video)
	{
		mGamesWithVideosLoaded = true;
		mGamesWithVideos.clear();
	}
	else
	{
		mGamesWithImagesLoaded = true;
		mGamesWithImages.clear();
	}

	std::unordered_set<FileData*> seen;

	for (auto system : SystemData::sSystemVector)
	{
		// We only want nodes from game systems that are not collections
		if (!system->isGameSystem() || system->isCollection() || system->hasPlatformId(PlatformIds::IMAGEVIEWER) || system->hasPlatformId(PlatformIds::PLATFORM_IGNORE))
			continue;

		auto games = system->getRootFolder()->getFilesRecursive(GAME, true);
		for (auto game : games)
		{
			if (!seen.insert(game).second)
				continue;

			if (video && !game->getVideoPath().empty())
			{
				mGamesWithVideos.push_back(game);
				nodeCount++;
			}
			else if (!video && !game->getImagePath().empty())
			{
				mGamesWithImages.push_back(game);
				nodeCount++;
			}
		}
	}

	return nodeCount;
}

std::string  SystemScreenSaver::selectGameMedia(FileData* game, bool video)
{
	std::string path = video ? game->getVideoPath() : game->getImagePath();
	if (!Utils::FileSystem::exists(path))
		return "";

	mSystemName = game->getSourceFileData()->getSystem()->getFullName();
	mGameName = game->getSourceFileData()->getSystem()->getName();
	mCurrentGame = game;

#ifdef _RPI_
	if (Settings::getInstance()->getBool("ScreenSaverOmxPlayer"))
	{
		if (Settings::getInstance()->getString("ScreenSaverGameInfo") != "never" && video)
		{
			std::string path = getTitleFolder();
			if (!Utils::FileSystem::exists(path))
				Utils::FileSystem::createDirectory(path);

			writeSubtitle(mGameName.c_str(), mSystemName.c_str(), (Settings::getInstance()->getString("ScreenSaverGameInfo") == "always"));
		}
	}
#endif

	return path;
}

std::string SystemScreenSaver::pickRandomGameMedia(bool video)
{
	mCurrentGame = nullptr;

	int count = countGameListNodes(video);
	if (count == 0)
		return "";

	std::vector<FileData*>* games = video ? &mGamesWithVideos : &mGamesWithImages;

	int index = Randomizer::random(count) % count;

	while (!games->empty())
	{
		auto path = selectGameMedia(games->at(index), video);
		games->erase(games->begin() + index);

		if (!path.empty())
		{
			if (games->empty())
			{
				(video ? mGamesWithVideosLoaded : mGamesWithImagesLoaded) = false;
				if (video)
					LOG(LogDebug) << "VideoScreenSaver::pickRandomGameMedia - All videos used, resetting list.";
				else
					LOG(LogDebug) << "ImageScreenSaver::pickRandomGameMedia - All images used, resetting list.";
			}
			return path;
		}

		// move index one step backward with wrap-around
		index = (index + games->size() - 1) % games->size();
	}

	return "";
}

std::string SystemScreenSaver::pickRandomCustomImage(bool video)
{
	std::string path;

	std::string imageFilter = Settings::getInstance()->getString(video ? "SlideshowScreenSaverVideoFilter" : "SlideshowScreenSaverImageFilter");
	const std::string filterLower = Utils::String::toLower(imageFilter);
	bool recurse = Settings::getInstance()->getBool(video ? "SlideshowScreenSaverVideoRecurse" : "SlideshowScreenSaverRecurse");
	std::vector<std::string> matchingFiles;
	std::vector<std::string> roots;

	if (!video)
	{
		std::string imageDir = Settings::getInstance()->getString("SlideshowScreenSaverImageDir");
		if (imageDir.empty())
			imageDir = Paths::getScreenShotPath();
		else if (imageDir[0] == '~')
			imageDir = Utils::FileSystem::getCanonicalPath(
				Utils::FileSystem::combine(Paths::getEmulationStationPath(), imageDir.substr(1)));
		if (!imageDir.empty())
			roots.push_back(Utils::FileSystem::getCanonicalPath(imageDir));
	}
	else
	{
		// 1) Interna: {EXE}/screensaver_videos
		std::string internalDir = Settings::getInstance()->getString("SlideshowScreenSaverVideoDir");
		if (internalDir.empty())
			internalDir = Utils::FileSystem::getCanonicalPath(
				Utils::FileSystem::combine(Paths::getEmulationStationPath(), "screensaver_videos"));
		else if (internalDir[0] == '~')
			internalDir = Utils::FileSystem::getCanonicalPath(
				Utils::FileSystem::combine(Paths::getEmulationStationPath(), internalDir.substr(1)));
		else
			internalDir = Utils::FileSystem::getCanonicalPath(internalDir);

		if (!internalDir.empty() && Utils::FileSystem::isDirectory(internalDir))
			roots.push_back(internalDir);

		// 2) Externa: {pai do EXE}/screensaver_videos  (opcional)
		std::string parent = Utils::FileSystem::getParent(Paths::getEmulationStationPath());
		if (!parent.empty())
		{
			std::string externalDir = Utils::FileSystem::getCanonicalPath(
				Utils::FileSystem::combine(parent, "screensaver_videos"));
			if (!externalDir.empty() && Utils::FileSystem::isDirectory(externalDir))
			{
				bool same = false;
				for (const auto& r : roots)
				{
					if (Utils::String::toLower(Utils::String::replace(r, "\\", "/")) ==
						Utils::String::toLower(Utils::String::replace(externalDir, "\\", "/")))
					{
						same = true;
						break;
					}
				}
				if (!same)
					roots.push_back(externalDir);
			}
		}
	}

	for (const auto& imageDir : roots)
	{
		if (!Utils::FileSystem::isDirectory(imageDir))
			continue;

		// Recurse: entra em subpastas de sistema (ps5/, ps4/, switch/, ...)
		Utils::FileSystem::stringList dirContent = Utils::FileSystem::getDirContent(imageDir, recurse);
		for (Utils::FileSystem::stringList::const_iterator it = dirContent.cbegin(); it != dirContent.cend(); ++it)
		{
			if (!Utils::FileSystem::isRegularFile(*it))
				continue;

			if (filterLower.empty())
			{
				matchingFiles.push_back(*it);
			}
			else
			{
				// case-insensitive: .MP4 == .mp4
				const std::string ext = Utils::String::toLower(Utils::FileSystem::getExtension(*it));
				if (!ext.empty() && filterLower.find(ext) != std::string::npos)
					matchingFiles.push_back(*it);
			}
		}
	}

	int fileCount = (int)matchingFiles.size();
	if (fileCount > 0)
	{
		int randomIndex = Randomizer::random(fileCount);
		path = matchingFiles[randomIndex];
		LOG(LogInfo) << "Screensaver custom media pick: " << path
			<< " systemFolder=" << extractSystemFolderFromCustomMediaPath(path, video);
	}
	else
	{
		LOG(LogError) << "Slideshow Screensaver - No image/video files found in custom folders\n";
	}

	return path;
}

std::string SystemScreenSaver::extractSystemFolderFromCustomMediaPath(const std::string& mediaPath, bool video)
{
	(void)video;
	if (mediaPath.empty())
		return "";

	// REGRA SIMPLES E DETERMINISTICA:
	// ficheiro =  .../{sistema}/ficheiro.mp4
	// pasta pai do ficheiro = nome do sistema (ps5, ps4, switch, xboxone, ...)
	const std::string full = Utils::FileSystem::getCanonicalPath(mediaPath);
	const std::string parentPath = Utils::FileSystem::getParent(full);
	const std::string folder = Utils::FileSystem::getFileName(parentPath);

	if (folder.empty())
		return "";

	const std::string folderLow = Utils::String::toLower(folder);
	// ignorar se o video esta solto na raiz de screensaver_videos (sem subpasta)
	if (folderLow == "screensaver_videos")
		return "";

	return folder; // manter casing original (Windows e case-insensitive no exists)
}

SystemData* SystemScreenSaver::resolveSystemFromCustomMediaPath(const std::string& mediaPath, bool video)
{
	std::string systemFolder = extractSystemFolderFromCustomMediaPath(mediaPath, video);
	if (systemFolder.empty())
		return nullptr;

	SystemData* sys = SystemData::getSystem(systemFolder);
	if (sys == nullptr)
	{
		for (auto* s : SystemData::sSystemVector)
		{
			if (s != nullptr && Utils::String::toLower(s->getName()) == Utils::String::toLower(systemFolder))
			{
				sys = s;
				break;
			}
		}
	}

	// aliases de pasta -> SystemData ES (so quando o nome da pasta != nome do sistema ES)
	// NAO mapear ps5->ps3 nem switch->wiiu (bezel usa o nome da pasta exacto via setSystemDecoration)
	if (sys == nullptr)
	{
		std::string low = Utils::String::toLower(systemFolder);
		std::vector<std::string> aliases;
		if (low == "pc" || low == "steam")
			aliases.push_back("windows");
		if (low == "ports")
			aliases.push_back("ports");
		if (low == "ps1" || low == "playstation")
			aliases.push_back("psx");
		if (low == "genesis")
			aliases.push_back("megadrive");

		for (const auto& a : aliases)
		{
			sys = SystemData::getSystem(a);
			if (sys)
				break;
		}
	}

	if (sys != nullptr)
		LOG(LogDebug) << "Screensaver custom media system folder: " << systemFolder << " -> " << sys->getName();
	else
		LOG(LogDebug) << "Screensaver custom media: pasta '" << systemFolder << "' sem SystemData (bezel por nome mesmo assim)";

	return sys;
}

FileData* SystemScreenSaver::bindCustomMediaToSystem(const std::string& mediaPath, bool video)
{
	mOwnedCustomGame.reset();

	SystemData* sys = resolveSystemFromCustomMediaPath(mediaPath, video);
	if (sys == nullptr)
		return nullptr;

	// Placeholder: so serve para bezel/decoracao por sistema (nome do ficheiro irrelevante)
	mOwnedCustomGame = std::unique_ptr<FileData>(new FileData(PLACEHOLDER, mediaPath, sys));
	return mOwnedCustomGame.get();
}

void SystemScreenSaver::update(int deltaTime)
{
	// Use this to update the fade value for the current fade stage
	if (mState == STATE_FADE_OUT_WINDOW)
	{
		mOpacity += (float)deltaTime / FADE_TIME;
		if (mOpacity >= 1.0f)
		{
			mOpacity = 1.0f;

			// Update to the next state
			mState = STATE_FADE_IN_VIDEO;			
		}
	}
	else if (mState == STATE_FADE_IN_VIDEO)
	{
		mOpacity -= (float)deltaTime / FADE_TIME;
		if (mOpacity <= 0.0f)
		{
			mOpacity = 0.0f;
			// Update to the next state
			mState = STATE_SCREENSAVER_ACTIVE;
			mFadingImageScreensaver = nullptr;
		}
	}
	else if (mState == STATE_SCREENSAVER_ACTIVE)
	{
		// Update the timer that swaps the videos
		mTimer += deltaTime;
		if (mTimer > mVideoChangeTime)
			nextVideo();
	}

	// If we have a loaded video then update it
	if (mVideoScreensaver)
		mVideoScreensaver->update(deltaTime);

	if (mImageScreensaver)
		mImageScreensaver->update(deltaTime);
}

void SystemScreenSaver::nextVideo() 
{
	mLoadingNext = true;
	startScreenSaver();
}

FileData* SystemScreenSaver::getCurrentGame()
{
	return mCurrentGame;
}

void SystemScreenSaver::launchGame()
{
	if (mCurrentGame != NULL)
	{
		// Placeholder de pasta custom nao e jogo real
		if (mCurrentGame->getType() == PLACEHOLDER)
		{
			if (mCurrentGame->getSystem() != nullptr)
				ViewController::get()->goToGameList(mCurrentGame->getSystem());
			return;
		}

		// launching Game
		auto view = ViewController::get()->getGameListView(mCurrentGame->getSystem(), false);
		if (view != nullptr)
			view->setCursor(mCurrentGame);

		if (Settings::getInstance()->getBool("ScreenSaverControls"))
			mCurrentGame->launchGame(mWindow);
		else
			ViewController::get()->goToGameList(mCurrentGame->getSystem());
	}
}


// ------------------------------------------------------------------------------------------------------------------------
// GAME SCREEN SAVER BASE CLASS
// ------------------------------------------------------------------------------------------------------------------------

GameScreenSaverBase::GameScreenSaverBase(Window* window) : GuiComponent(window),
	mViewport(0, 0, Renderer::getScreenWidth(), Renderer::getScreenHeight())
{
	mDecoration = nullptr;
	mMarquee = nullptr;
	mLabelGame = nullptr;
	mLabelSystem = nullptr;
	mLabelDate = nullptr;
	mLabelTime = nullptr;
	mDateTimeUpdateAccumulator = 0;
	mDateTimeLastUpdate = 0;

	if (Settings::getInstance()->getBool("ScreenSaverDateTime"))
	{
		auto ph = ThemeData::getMenuTheme()->Text.font->getPath();
		auto sz = mViewport.h / 16.f;
		auto margin = sz / 2.f;
		auto font = Font::get(sz, ph);
		int fh = font->getLetterHeight();

		mLabelDate = new TextComponent(mWindow);
		mLabelDate->setPosition(mViewport.x + margin, mViewport.y + margin);
		mLabelDate->setSize(mViewport.w, sz * 0.66);
		mLabelDate->setHorizontalAlignment(ALIGN_LEFT);
		mLabelDate->setVerticalAlignment(ALIGN_CENTER);
		mLabelDate->setColor(0xD0D0D0FF);
		mLabelDate->setGlowColor(0x00000060);
		mLabelDate->setGlowSize(2);
		mLabelDate->setFont(ph, sz * 0.66);

		mLabelTime = new TextComponent(mWindow);
		mLabelTime->setPosition(mViewport.x + margin, mViewport.y + margin + mLabelDate->getSize().y() * 1.3f);
		mLabelTime->setSize(mViewport.w, fh);
		mLabelTime->setHorizontalAlignment(ALIGN_LEFT);
		mLabelTime->setVerticalAlignment(ALIGN_CENTER);
		mLabelTime->setColor(0xFFFFFFFF);
		mLabelTime->setGlowColor(0x00000040);
		mLabelTime->setGlowSize(3);
		mLabelTime->setFont(font);
	}
}

GameScreenSaverBase::~GameScreenSaverBase()
{
	if (mMarquee != nullptr)
	{
		delete mMarquee;
		mMarquee = nullptr;
	}

	if (mDecoration != nullptr)
	{
		delete mDecoration;
		mDecoration = nullptr;
	}

	if (mLabelGame != nullptr)
	{
		delete mLabelGame;
		mLabelGame = nullptr;
	}

	if (mLabelSystem != nullptr)
	{
		delete mLabelSystem;
		mLabelSystem = nullptr;
	}

	if (mLabelDate != nullptr)
	{
		delete mLabelDate;
		mLabelDate = nullptr;
	}

	if (mLabelTime != nullptr)
	{
		delete mLabelTime;
		mLabelTime = nullptr;
	}
}

#include "guis/GuiMenu.h"
#include <rapidjson/document.h>
#include <rapidjson/error/en.h>
#include <rapidjson/filereadstream.h>

void GameScreenSaverBase::setSystemDecoration(const std::string& systemName, const std::string& displayLabel)
{
	if (mDecoration != nullptr)
	{
		delete mDecoration;
		mDecoration = nullptr;
	}

	mViewport = Renderer::Rect(0, 0, Renderer::getScreenWidth(), Renderer::getScreenHeight());

	std::string decos = Settings::getInstance()->getString("ScreenSaverDecorations");
	if (decos.empty())
		decos = "systems"; // TurboRama default: moldura de sistema

#ifdef _RPI_
	if (!Settings::getInstance()->getBool("ScreenSaverOmxPlayer"))
#endif
	if (decos != "none")
	{
		// systemName = nome da PASTA do video (ps5, ps4, switch, xboxone, windows, pc, ...)
		const std::string sysLow = Utils::String::toLower(systemName);
		std::string directBezel;

		if (!sysLow.empty())
		{
			// Nomes de ficheiro PNG a tentar (SEMPRE o exacto primeiro).
			// Cada pasta puxa o SEU bezel: switch, xboxone, windows/pc, ps4, ps5 — distintos.
			// NUNCA ps5->ps3, switch->wiiu, etc.
			std::vector<std::string> bezelKeys;
			bezelKeys.push_back(sysLow);
			if (sysLow == "pc" || sysLow == "ports" || sysLow == "steam" || sysLow == "microsoftwindows")
				bezelKeys.push_back("windows"); // pasta "pc" usa windows.png se nao houver pc.png
			if (sysLow == "nintendoswitch" || sysLow == "nsw" || sysLow == "switch2" || sysLow == "hac")
				bezelKeys.push_back("switch");
			if (sysLow == "xbox_one" || sysLow == "xbox-one" || sysLow == "xone" || sysLow == "xbox1")
				bezelKeys.push_back("xboxone");
			if (sysLow == "playstation4" || sysLow == "ps4pro")
				bezelKeys.push_back("ps4");
			if (sysLow == "playstation5" || sysLow == "ps5pro")
				bezelKeys.push_back("ps5");

			const std::string esPath = Paths::getEmulationStationPath();
			const std::string esParent = Utils::FileSystem::getParent(esPath);

			// Raizes de decorations (ordem de prioridade)
			std::vector<std::string> decoRoots;
			auto addRoot = [&](const std::string& r)
			{
				if (r.empty() || !Utils::FileSystem::isDirectory(r))
					return;
				std::string c = Utils::FileSystem::getCanonicalPath(r);
				if (c.empty())
					c = r;
				for (const auto& e : decoRoots)
				{
					if (Utils::String::toLower(Utils::String::replace(e, "\\", "/")) ==
						Utils::String::toLower(Utils::String::replace(c, "\\", "/")))
						return;
				}
				decoRoots.push_back(c);
			};
			if (!esParent.empty())
			{
				addRoot(Utils::FileSystem::combine(esParent, "system/decorations"));
				addRoot(Utils::FileSystem::combine(esParent, "decorations"));
			}
			addRoot(Paths::getDecorationsPath());
			addRoot(Paths::getUserDecorationsPath());
			addRoot(Utils::FileSystem::combine(esPath, "../system/decorations"));

			const char* packs[] = { "default_unglazed", "default_nocurve", "default_curve", "default", nullptr };

			// Para cada chave de bezel (exacto primeiro), procurar em todas as raizes/packs
			for (const auto& key : bezelKeys)
			{
				for (const auto& root : decoRoots)
				{
					for (int pi = 0; packs[pi] != nullptr; pi++)
					{
						std::string raw = Utils::FileSystem::combine(
							Utils::FileSystem::combine(
								Utils::FileSystem::combine(root, packs[pi]),
								"systems"),
							key + ".png");
						if (Utils::FileSystem::exists(raw))
						{
							directBezel = raw;
							break;
						}
						std::string canon = Utils::FileSystem::getCanonicalPath(raw);
						if (!canon.empty() && Utils::FileSystem::exists(canon))
						{
							directBezel = canon;
							break;
						}
					}
					if (!directBezel.empty())
						break;

					// varrer qualquer pack sob a raiz
					auto packDirs = Utils::FileSystem::getDirContent(root, false);
					for (const auto& pack : packDirs)
					{
						if (!Utils::FileSystem::isDirectory(pack))
							continue;
						std::string tryPath = Utils::FileSystem::combine(
							Utils::FileSystem::combine(pack, "systems"), key + ".png");
						if (Utils::FileSystem::exists(tryPath))
						{
							directBezel = tryPath;
							break;
						}
					}
					if (!directBezel.empty())
						break;
				}
				if (!directBezel.empty())
					break;
			}

			// Keep every existing disk override. If no file exists, use the AES-GCM
			// protected copy stored inside emulationstation.exe without extracting it.
			if (directBezel.empty())
			{
				for (const auto& key : bezelKeys)
				{
					if (ProtectedDecorations::hasSystem(key))
					{
						directBezel = ProtectedDecorations::resourcePathForSystem(key);
						break;
					}
				}
			}

			LOG(LogError) << "[SS-BEZEL] folder='" << sysLow << "' bezel='"
				<< (directBezel.empty() ? std::string("(NOT FOUND)") : directBezel) << "'";
		}

		if (!directBezel.empty())
		{
			// Viewport a partir do .info (se for pointer "default_4_3.info", resolver)
			std::string infoFile = Utils::String::replace(directBezel, ".png", ".info");
			// tambem tentar .PNG -> .info
			if (!Utils::FileSystem::exists(infoFile))
			{
				std::string alt = directBezel;
				auto dot = alt.find_last_of('.');
				if (dot != std::string::npos)
					infoFile = alt.substr(0, dot) + ".info";
			}
			if (Utils::FileSystem::exists(infoFile))
			{
				FILE* fprobe = fopen(infoFile.c_str(), "rb");
				if (fprobe)
				{
					char buf[512] = { 0 };
					size_t n = fread(buf, 1, sizeof(buf) - 1, fprobe);
					fclose(fprobe);
					std::string content(buf, n);
					while (!content.empty() && (unsigned char)content.back() <= 32)
						content.pop_back();
					while (!content.empty() && (unsigned char)content.front() <= 32)
						content.erase(content.begin());
					if (!content.empty() && content[0] != '{')
					{
						// pointer textual para outro .info
						std::string pointed = Utils::FileSystem::combine(
							Utils::FileSystem::getParent(infoFile), content);
						if (Utils::FileSystem::exists(pointed))
							infoFile = pointed;
					}
				}

				FILE* fp = fopen(infoFile.c_str(), "rb");
				if (fp)
				{
					char readBuffer[65536];
					rapidjson::FileReadStream is(fp, readBuffer, sizeof(readBuffer));
					rapidjson::Document doc;
					doc.ParseStream(is);
					if (!doc.HasParseError() &&
						doc.HasMember("top") && doc.HasMember("left") && doc.HasMember("bottom") &&
						doc.HasMember("right") && doc.HasMember("width") && doc.HasMember("height") &&
						doc["width"].IsNumber() && doc["height"].IsNumber())
					{
						int width = doc["width"].GetInt();
						int height = doc["height"].GetInt();
						int top = doc["top"].GetInt();
						int left = doc["left"].GetInt();
						int bottom = doc["bottom"].GetInt();
						int right = doc["right"].GetInt();
						if (width > 0 && height > 0 && (left + right) < width && (top + bottom) < height)
						{
							Vector2i sz = ImageIO::adjustPictureSize(
								Vector2i(width, height),
								Vector2i(Renderer::getScreenWidth(), Renderer::getScreenHeight()));
							float px = (float)sz.x() / (float)width;
							float py = (float)sz.y() / (float)height;
							float dx = (Renderer::getScreenWidth() - sz.x()) / 2.0f;
							float dy = (Renderer::getScreenHeight() - sz.y()) / 2.0f;
							mViewport = Renderer::Rect(
								(int)(dx + left * px),
								(int)(dy + top * py),
								(int)((width - right - left) * px),
								(int)((height - bottom - top) * py));
						}
					}
					fclose(fp);
				}
			}

			// Sempre mostrar moldura se temos ficheiro (incl. ecras verticais)
			mDecoration = new ImageComponent(mWindow, true);
			mDecoration->setImage(directBezel);
			mDecoration->setOrigin(0.5f, 0.5f);
			mDecoration->setPosition(Renderer::getScreenWidth() / 2.0f, (float)Renderer::getScreenHeight() / 2.0f);
			mDecoration->setMaxSize((float)Renderer::getScreenWidth() * Renderer::getScreenProportion(), (float)Renderer::getScreenHeight());
		}
		else if (!sysLow.empty())
		{
		// Fallback antigo so se o PNG exacto nao existir
		auto sets = GuiMenu::getDecorationsSetsByName(systemName);
		if (sets.empty())
		{
			LOG(LogWarning) << "Screensaver: nenhum set de decorations encontrado para '" << systemName << "'";
		}
		else
		{
			int setId = 0;
			bool found = false;
			// Preferir PNG exacto: .../systems/{system}.png (nao alias nem default.png)
			auto isExactSystemBezel = [&sysLow](const std::string& url) -> bool
			{
				if (sysLow.empty() || url.empty())
					return false;
				std::string u = Utils::String::toLower(Utils::String::replace(url, "\\", "/"));
				return Utils::String::endsWith(u, "/systems/" + sysLow + ".png");
			};

			// Preferir set default_unglazed / global.bezel se tiver bezel exacto
			std::string bezel = SystemConf::getInstance()->get("global.bezel");
			if (decos == "systems" || decos == "random")
			{
				// 1) Exacto em default_unglazed (melhor cobertura: ps5, ps4, switch, xbox360, windows...)
				for (int i = 0; i < (int)sets.size(); i++)
				{
					if (sets[i].name == "default_unglazed" && isExactSystemBezel(sets[i].imageUrl))
					{
						found = true;
						setId = i;
						break;
					}
				}

				// 2) Exacto em qualquer pack default_*
				if (!found)
				{
					for (int i = 0; i < (int)sets.size(); i++)
					{
						if (Utils::String::startsWith(sets[i].name, "default") && isExactSystemBezel(sets[i].imageUrl))
						{
							found = true;
							setId = i;
							break;
						}
					}
				}

				// 3) Exacto em qualquer set
				if (!found)
				{
					for (int i = 0; i < (int)sets.size(); i++)
					{
						if (isExactSystemBezel(sets[i].imageUrl))
						{
							found = true;
							setId = i;
							break;
						}
					}
				}

				// 4) global.bezel se tiver imagem (pode ser alias seguro, ex. pc->windows)
				if (!found && !bezel.empty() && bezel != "default")
				{
					for (int i = 0; i < (int)sets.size(); i++)
					{
						if (sets[i].name == bezel && !sets[i].imageUrl.empty())
						{
							found = true;
							setId = i;
							break;
						}
					}
				}

				// 5) default_unglazed com qualquer imagem de sistema
				if (!found)
				{
					for (int i = 0; i < (int)sets.size(); i++)
					{
						if (sets[i].name == "default_unglazed" && !sets[i].imageUrl.empty())
						{
							found = true;
							setId = i;
							break;
						}
					}
				}

				if (!found && decos == "random" && !sets.empty())
					setId = Randomizer::random((int)sets.size());
				else if (!found)
				{
					for (int i = 0; i < (int)sets.size(); i++)
					{
						if (!sets[i].imageUrl.empty())
						{
							setId = i;
							found = true;
							break;
						}
					}
				}
			}

			if (setId >= 0 && setId < (int)sets.size() && Utils::FileSystem::exists(sets[setId].imageUrl))
			{
				// Viewport a partir do .info (area interior da moldura)
				std::string infoFile = Utils::String::replace(sets[setId].imageUrl, ".png", ".info");
				if (Utils::FileSystem::exists(infoFile))
				{
					FILE* fp = fopen(infoFile.c_str(), "r");
					if (fp)
					{
						char readBuffer[65536];
						rapidjson::FileReadStream is(fp, readBuffer, sizeof(readBuffer));
						rapidjson::Document doc;
						doc.ParseStream(is);

						if (!doc.HasParseError())
						{
							if (doc.HasMember("top") && doc.HasMember("left") && doc.HasMember("bottom") && doc.HasMember("right") && doc.HasMember("width") && doc.HasMember("height"))
							{
								auto width = doc["width"].GetInt();
								auto height = doc["height"].GetInt();
								if (width > 0 && height > 0)
								{
									Vector2i sz = ImageIO::adjustPictureSize(Vector2i(width, height), Vector2i(Renderer::getScreenWidth(), Renderer::getScreenHeight()));

									float px = (float)sz.x() / (float)width;
									float py = (float)sz.y() / (float)height;

									float dx = (Renderer::getScreenWidth() - sz.x()) / 2.0f;
									float dy = (Renderer::getScreenHeight() - sz.y()) / 2.0f;

									auto top = doc["top"].GetInt();
									auto left = doc["left"].GetInt();
									auto bottom = doc["bottom"].GetInt();
									auto right = doc["right"].GetInt();

									mViewport = Renderer::Rect(
										(int)(dx + left * px),
										(int)(dy + top * py),
										(int)((width - right - left) * px),
										(int)((height - bottom - top) * py));
								}
							}
						}

						fclose(fp);
					}
				}

				if (!Renderer::isVerticalScreen())
				{
					mDecoration = new ImageComponent(mWindow, true);
					mDecoration->setImage(sets[setId].imageUrl);
					mDecoration->setOrigin(0.5f, 0.5f);
					mDecoration->setPosition(Renderer::getScreenWidth() / 2.0f, (float)Renderer::getScreenHeight() / 2.0f);
					mDecoration->setMaxSize((float)Renderer::getScreenWidth() * Renderer::getScreenProportion(), (float)Renderer::getScreenHeight());
					LOG(LogInfo) << "Screensaver bezel: " << sets[setId].imageUrl << " system=" << systemName;
				}
			}
		}
		} // else if (!sysLow.empty()) fallback
	} // if (decos != none)

	// Label do sistema (opcional)
	if (!displayLabel.empty() && Settings::getInstance()->getBool("SlideshowScreenSaverGameName"))
	{
		if (mLabelSystem != nullptr)
		{
			delete mLabelSystem;
			mLabelSystem = nullptr;
		}

		auto ph = ThemeData::getMenuTheme()->Text.font->getPath();
		auto sz = mViewport.h / 16.f;
		auto font = Font::get(sz, ph);
		int h = mViewport.h / 4.0f;
		int fh = font->getLetterHeight();

		mLabelSystem = new TextComponent(mWindow);
		mLabelSystem->setPosition((float)mViewport.x, (float)(mViewport.y + mViewport.h - h + fh / 2));
		mLabelSystem->setSize((float)mViewport.w, (float)(h + fh / 2));
		mLabelSystem->setHorizontalAlignment(ALIGN_CENTER);
		mLabelSystem->setVerticalAlignment(ALIGN_CENTER);
		mLabelSystem->setColor(0xD0D0D0FF);
		mLabelSystem->setGlowColor(0x00000060);
		mLabelSystem->setGlowSize(2);
		mLabelSystem->setFont(ph, sz * 0.66f);
		mLabelSystem->setText(displayLabel);
	}
}

void GameScreenSaverBase::setGame(FileData* game)
{	
	if (mLabelGame != nullptr)
	{
		delete mLabelGame;
		mLabelGame = nullptr;
	}

	if (mLabelSystem != nullptr)
	{
		delete mLabelSystem;
		mLabelSystem = nullptr;
	}

	if (mMarquee != nullptr)
	{
		delete mMarquee;
		mMarquee = nullptr;
	}

	if (mDecoration != nullptr)
	{
		delete mDecoration;
		mDecoration = nullptr;
	}

	if (game == nullptr)
		return;

	// Moldura / bezel do sistema
	std::string sysName = game->getSystem() ? game->getSystem()->getName() : "";
	std::string sysFull = game->getSystem() ? game->getSystem()->getFullName() : "";
	setSystemDecoration(sysName, sysFull);

	if (!Settings::getInstance()->getBool("SlideshowScreenSaverGameName"))
		return;

	if (Settings::getInstance()->getBool("ScreenSaverMarquee") && Utils::FileSystem::exists(game->getMarqueePath()))
	{
		mMarquee = new ImageComponent(mWindow, true);
		mMarquee->setImage(game->getMarqueePath());
		mMarquee->setOrigin(0.5f, 0.5f);
		mMarquee->setPosition(mViewport.x + mViewport.w * 0.50f, mViewport.y + mViewport.h * 0.16f);
		mMarquee->setMaxSize((float)mViewport.w * 0.40f, (float)mViewport.h * 0.22f);
	}
	
	auto ph = ThemeData::getMenuTheme()->Text.font->getPath();
	auto sz = mViewport.h / 16.f;
	auto font = Font::get(sz, ph);

	int h = mViewport.h / 4.0f;
	int fh = font->getLetterHeight();

	mLabelGame = new TextComponent(mWindow);
	mLabelGame->setPosition(mViewport.x, mViewport.y + mViewport.h - h - fh / 2);
	mLabelGame->setSize(mViewport.w, h - fh / 2);
	mLabelGame->setHorizontalAlignment(ALIGN_CENTER);
	mLabelGame->setVerticalAlignment(ALIGN_CENTER);
	mLabelGame->setColor(0xFFFFFFFF);
	mLabelGame->setGlowColor(0x00000040);
	mLabelGame->setGlowSize(3);
	mLabelGame->setFont(font);
	mLabelGame->setText(game->getName());

	if (mLabelSystem == nullptr && game->getSystem() != nullptr)
	{
		mLabelSystem = new TextComponent(mWindow);
		mLabelSystem->setPosition(mViewport.x, mViewport.y + mViewport.h - h + fh / 2);
		mLabelSystem->setSize(mViewport.w, h + fh / 2);
		mLabelSystem->setHorizontalAlignment(ALIGN_CENTER);
		mLabelSystem->setVerticalAlignment(ALIGN_CENTER);
		mLabelSystem->setColor(0xD0D0D0FF);
		mLabelSystem->setGlowColor(0x00000060);
		mLabelSystem->setGlowSize(2);
		mLabelSystem->setFont(ph, sz * 0.66);
		mLabelSystem->setText(game->getSystem()->getFullName());
	}
}

void GameScreenSaverBase::render(const Transform4x4f& transform)
{
	if (mMarquee)
	{
		mMarquee->setOpacity(mOpacity);
		mMarquee->render(transform);
	}
	else if (mLabelGame)
	{
		mLabelGame->setOpacity(mOpacity);
		mLabelGame->render(transform);
	}

	if (mDecoration == nullptr || Settings::getInstance()->getString("ScreenSaverDecorations") != "systems")
	if (mLabelSystem)
	{
		mLabelSystem->setOpacity(mOpacity);
		mLabelSystem->render(transform);
	}

	if (mDecoration)
	{
		mDecoration->setOpacity(mOpacity);
		mDecoration->render(transform);
	}

	if (mLabelDate)
	{
		mLabelDate->setOpacity(255);
		mLabelDate->render(transform);
	}

	if (mLabelTime)
	{
		mLabelTime->setOpacity(255);
		mLabelTime->render(transform);
	}
}

void GameScreenSaverBase::update(int deltaTime)
{
	GuiComponent::update(deltaTime);

	if (Settings::getInstance()->getBool("ScreenSaverDateTime"))
	{
		mDateTimeUpdateAccumulator += deltaTime;
		if (mDateTimeUpdateAccumulator >= DATE_TIME_UPDATE_INTERVAL)
		{
			mDateTimeUpdateAccumulator -= DATE_TIME_UPDATE_INTERVAL;

			time_t now = time(NULL);
			if (now != mDateTimeLastUpdate)
			{
				mDateTimeLastUpdate = now;

				struct tm* timeinfo = localtime(&now);

				const std::string& dateFormat = Settings::getInstance()->getString("ScreenSaverDateFormat");
				const std::string& timeFormat = Settings::getInstance()->getString("ScreenSaverTimeFormat");
				const std::string* dateFormatPtr = &dateFormat;

				std::string modifiedDateFormat;
				std::string language = SystemConf::getInstance()->get("system.language");
				if (language == "ko_KR")	// fix Korean string
				{
					if (dateFormat == "%A, %B %d")
						modifiedDateFormat = std::string("%A, %B %dì¼");
					else if (dateFormat == "%b %d, %Y")
						modifiedDateFormat = std::string("%b %dì¼, %Yë…„");
				}
				if (!modifiedDateFormat.empty()) {
					dateFormatPtr = &modifiedDateFormat;
				}

				char dateBuffer[64];
				char timeBuffer[64];
				strftime(dateBuffer, sizeof(dateBuffer), dateFormatPtr->c_str(), timeinfo);
				strftime(timeBuffer, sizeof(timeBuffer), timeFormat.c_str(), timeinfo);

				if (mLabelDate)
					mLabelDate->setText(std::string(dateBuffer));

				if (mLabelTime)
					mLabelTime->setText(std::string(timeBuffer));
			}
		}
	}
}

void GameScreenSaverBase::setOpacity(unsigned char opacity)
{
	mOpacity = opacity;
}


// ------------------------------------------------------------------------------------------------------------------------
// IMAGE SCREEN SAVER CLASS
// ------------------------------------------------------------------------------------------------------------------------

ImageScreenSaver::ImageScreenSaver(Window* window) : GameScreenSaverBase(window)
{
	mImage = nullptr;
}

ImageScreenSaver::~ImageScreenSaver()
{
	if (mImage != nullptr)
		delete mImage;
}

void ImageScreenSaver::setImage(const std::string path)
{
	if (mImage == nullptr)
	{
		mImage = new ImageComponent(mWindow, true);
		mImage->setOrigin(0.5f, 0.5f);
		mImage->setPosition(mViewport.x + mViewport.w / 2.0f, mViewport.y + mViewport.h / 2.0f);

		if (Settings::getInstance()->getBool("SlideshowScreenSaverStretch"))
			mImage->setMinSize((float)mViewport.w, (float)mViewport.h);
		else
			mImage->setMaxSize((float)mViewport.w, (float)mViewport.h);
	}

	mImage->setImage(path);
}

bool ImageScreenSaver::hasImage()
{
	return mImage != nullptr && mImage->hasImage();
}

void ImageScreenSaver::render(const Transform4x4f& transform)
{
	if (mImage)
	{
		mImage->setOpacity(mOpacity);

		Renderer::pushClipRect(Vector2i(mViewport.x, mViewport.y), Vector2i(mViewport.w, mViewport.h));
		mImage->render(transform);
		Renderer::popClipRect();
	}

	GameScreenSaverBase::render(transform);
}

// ------------------------------------------------------------------------------------------------------------------------
// VIDEO SCREEN SAVER CLASS
// ------------------------------------------------------------------------------------------------------------------------

VideoScreenSaver::VideoScreenSaver(Window* window, SystemScreenSaver* systemScreenSaver) : GameScreenSaverBase(window)
{
	mSystemScreenSaver = systemScreenSaver;
	mVideo = nullptr;
	mTime = 0;
	mFade = 1.0;
}

VideoScreenSaver::~VideoScreenSaver()
{
	if (mVideo != nullptr)
		delete mVideo;
}

void VideoScreenSaver::setVideo(const std::string path)
{
	if (mVideo == nullptr)
	{
#ifdef _RPI_
		// Create the correct type of video component
		if (Settings::getInstance()->getBool("ScreenSaverOmxPlayer"))
			mVideo = new VideoPlayerComponent(mWindow, getTitlePath());
		else
#endif
		mVideo = new VideoVlcComponent(mWindow);

		mVideo->setRoundCorners(0);
		mVideo->topWindow(true);
		mVideo->setOrigin(0.5f, 0.5f);
		
		mVideo->setPosition(mViewport.x + mViewport.w / 2.0f, mViewport.y + mViewport.h / 2.0f);

		if (Settings::getInstance()->getBool("StretchVideoOnScreenSaver"))
			mVideo->setMinSize((float)mViewport.w, (float)mViewport.h);
		else
			mVideo->setMaxSize((float)mViewport.w, (float)mViewport.h);

		mVideo->setVideo(path);
		mVideo->setScreensaverMode(true);
		mVideo->onShow();

		if (mSystemScreenSaver != nullptr)
		{
			mVideo->setOnVideoEnded([&]() 
			{ 				
				auto ss = mSystemScreenSaver;
				mWindow->postToUiThread([ss]() { ss->nextVideo(); });
				return false; 
			});
		}
	}

	mFade = 1.0;
	mTime = 0;
	mVideo->setVideo(path);
}

#define SUBTITLE_DURATION 4000
#define SUBTITLE_FADE 150

void VideoScreenSaver::render(const Transform4x4f& transform)
{	
	if (mVideo)
	{		
		mVideo->setOpacity(mOpacity);

		Renderer::pushClipRect(Vector2i(mViewport.x, mViewport.y), Vector2i(mViewport.w, mViewport.h));
		mVideo->render(transform);
		Renderer::popClipRect();
	}

#ifdef _RPI_
	if (Settings::getInstance()->getBool("ScreenSaverOmxPlayer"))
		return;
#endif

	if (Settings::getInstance()->getString("ScreenSaverGameInfo") != "never")
	{
		if (mMarquee && mFade != 0)
		{
			mMarquee->setOpacity(mOpacity * mFade);
			mMarquee->render(transform);
		}
		else if (mLabelGame && mFade != 0)
		{
			mLabelGame->setOpacity(mOpacity * mFade);
			mLabelGame->render(transform);
		}

		if (mDecoration == nullptr || Settings::getInstance()->getString("ScreenSaverDecorations") != "systems")
		if (mLabelSystem && mFade != 0)
		{
			mLabelSystem->setOpacity(mOpacity * mFade);
			mLabelSystem->render(transform);
		}
	}
	
	if (mDecoration)
	{
		mDecoration->setOpacity(mOpacity);
		mDecoration->render(transform);		
	}

	if (mLabelDate)
	{
		mLabelDate->setOpacity(255);
		mLabelDate->render(transform);
	}

	if (mLabelTime)
	{
		mLabelTime->setOpacity(255);
		mLabelTime->render(transform);
	}

	if (Settings::DebugImage())
		Renderer::drawRect(mViewport.x, mViewport.y, mViewport.w, mViewport.h, 0xFFFF0090, 0xFFFF0090);
}

void VideoScreenSaver::update(int deltaTime)
{
	GameScreenSaverBase::update(deltaTime); 

	if (mVideo)
	{
		if (Settings::getInstance()->getString("ScreenSaverGameInfo") == "start & end")
		{
			int duration = SUBTITLE_DURATION;
			int end = Settings::getInstance()->getInt("ScreenSaverSwapVideoTimeout") - duration;

			if (mTime >= duration - SUBTITLE_FADE && mTime < duration)
			{
				mFade -= (float)deltaTime / SUBTITLE_FADE;
				if (mFade < 0)
					mFade = 0;
			}
			else if (mTime >= end - SUBTITLE_FADE && mTime < end)
			{
				mFade += (float)deltaTime / SUBTITLE_FADE;
				if (mFade > 1)
					mFade = 1;
			}
			else if (mTime > duration && mTime < end - SUBTITLE_FADE)
				mFade = 0;
		}
	
		mTime += deltaTime;	
		mVideo->update(deltaTime);
	}
}
