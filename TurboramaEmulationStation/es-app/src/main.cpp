//EmulationStation, a graphical front-end for ROM browsing. Created by Alec "Aloshi" Lofquist.
//http://www.aloshi.com

#include "services/HttpServerThread.h"
#include "guis/GuiDetectDevice.h"
#include "guis/GuiMsgBox.h"
#include "utils/FileSystemUtil.h"
#include "views/ViewController.h"
#include "CollectionSystemManager.h"
#include "EmulationStation.h"
#include "InputManager.h"
#include "Log.h"
#include "MameNames.h"
#include "Genres.h"
#include "utils/Platform.h"
#include "PowerSaver.h"
#include "Settings.h"
#include "SystemData.h"
#include "SystemScreenSaver.h"
#include <SDL_events.h>
#include <SDL_main.h>
#include <SDL_timer.h>
#include <iostream>
#include <time.h>
#include "LocaleES.h"
#include <SystemConf.h>
#include "ApiSystem.h"
#include "AudioManager.h"
#include "NetworkThread.h"
#include "scrapers/ThreadedScraper.h"
#include "ThreadedHasher.h"
#include <FreeImage.h>
#include "ImageIO.h"
#include "components/VideoVlcComponent.h"
#include <csignal>
#include "InputConfig.h"
#include "RetroAchievements.h"
#include "TextToSpeech.h"
#include "Paths.h"
#include "EmbeddedTheme.h"
#include "resources/TextureData.h"
#include "Scripting.h"
#include "watchers/WatchersManager.h"
#include "HttpReq.h"
#include <thread>
#include <chrono>
#include <vector>
#include "ZaparooSupport.h"
#include "utils/ThreadPool.h"
#include "resources/ProtectedDecorations.h"
#include "resources/ResourceManager.h"
#ifdef TURBORAMA_REQUIRE_SUITE_LICENSE
#include "SuiteAccessGate.h"
#endif
#ifdef TURBORAMA_NO_COMMERCIAL_SERVICES
#include "MainMenuAuth.h"
#endif
#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
#include "CreditManager.h"
#include "CreditWarningOverlay.h"
#include "PixBridge.h"
#include "PixAgentManager.h"
#include "PixBinaryTrust.h"
#endif
#include "guis/GuiMenu.h"

#ifdef WIN32
#include <Windows.h>
#include <direct.h>
#define PATH_MAX MAX_PATH
#endif

static std::string gPlayVideo;
static int gPlayVideoDuration = 0;
static bool enable_startup_game = true;

bool parseArgs(int argc, char* argv[])
{
	Paths::setExePath(argv[0]);

	// We need to process --home before any call to Settings::getInstance(), because settings are loaded from homepath
	for (int i = 1; i < argc; i++)
	{
		if (strcmp(argv[i], "--home") == 0)
		{
			if (i == argc - 1)
				continue;

			std::string arg = argv[i + 1];
			if (arg.find("-") == 0)
				continue;

			Paths::setHomePath(argv[i + 1]);
			break;
		}
	}

	for(int i = 1; i < argc; i++)
	{
		if (strcmp(argv[i], "--videoduration") == 0)
		{
			gPlayVideoDuration = atoi(argv[i + 1]);
			i++; // skip the argument value
		}
		else if (strcmp(argv[i], "--video") == 0)
		{
			gPlayVideo = argv[i + 1];
			i++; // skip the argument value
		}
		else if (strcmp(argv[i], "--monitor") == 0)
		{
			if (i >= argc - 1)
			{
				std::cerr << "Invalid monitor supplied.";
				return false;
			}

			int monitorId = atoi(argv[i + 1]);
			i++; // skip the argument value
			Settings::getInstance()->setInt("MonitorID", monitorId);
		}
		else if(strcmp(argv[i], "--resolution") == 0)
		{
			if(i >= argc - 2)
			{
				std::cerr << "Invalid resolution supplied.";
				return false;
			}

			int width = atoi(argv[i + 1]);
			int height = atoi(argv[i + 2]);
			i += 2; // skip the argument value
			Settings::getInstance()->setInt("WindowWidth", width);
			Settings::getInstance()->setInt("WindowHeight", height);
			Settings::getInstance()->setBool("FullscreenBorderless", false);
		}else if(strcmp(argv[i], "--screensize") == 0)
		{
			if(i >= argc - 2)
			{
				std::cerr << "Invalid screensize supplied.";
				return false;
			}

			int width = atoi(argv[i + 1]);
			int height = atoi(argv[i + 2]);
			i += 2; // skip the argument value
			Settings::getInstance()->setInt("ScreenWidth", width);
			Settings::getInstance()->setInt("ScreenHeight", height);
		}else if(strcmp(argv[i], "--screenoffset") == 0)
		{
			if(i >= argc - 2)
			{
				std::cerr << "Invalid screenoffset supplied.";
				return false;
			}

			int x = atoi(argv[i + 1]);
			int y = atoi(argv[i + 2]);
			i += 2; // skip the argument value
			Settings::getInstance()->setInt("ScreenOffsetX", x);
			Settings::getInstance()->setInt("ScreenOffsetY", y);
		}else if (strcmp(argv[i], "--screenrotate") == 0)
		{
			if (i >= argc - 1)
			{
				std::cerr << "Invalid screenrotate supplied.";
				return false;
			}

			int rotate = atoi(argv[i + 1]);
			++i; // skip the argument value
			Settings::getInstance()->setInt("ScreenRotate", rotate);
		}else if(strcmp(argv[i], "--gamelist-only") == 0)
		{
			Settings::getInstance()->setBool("ParseGamelistOnly", true);
		}else if(strcmp(argv[i], "--ignore-gamelist") == 0)
		{
			Settings::getInstance()->setBool("IgnoreGamelist", true);
		}else if(strcmp(argv[i], "--show-hidden-files") == 0)
		{
			Settings::setShowHiddenFiles(true);
		}else if(strcmp(argv[i], "--draw-framerate") == 0)
		{
			Settings::getInstance()->setBool("DrawFramerate", true);
		}else if(strcmp(argv[i], "--no-exit") == 0)
		{
			Settings::getInstance()->setBool("ShowExit", false);
		}else if(strcmp(argv[i], "--exit-on-reboot-required") == 0)
		{
			Settings::getInstance()->setBool("ExitOnRebootRequired", true);
		}else if(strcmp(argv[i], "--no-startup-game") == 0)
		{
		        enable_startup_game = false;
		}else if(strcmp(argv[i], "--no-splash") == 0)
		{
			Settings::getInstance()->setBool("SplashScreen", false);
		}else if(strcmp(argv[i], "--splash-image") == 0)
		{
		        if (i >= argc - 1)
			{
				std::cerr << "Invalid splash image supplied.";
				return false;
			}
			Settings::getInstance()->setString("AlternateSplashScreen", argv[i+1]);
			++i; // skip the argument value
		}else if(strcmp(argv[i], "--debug") == 0)
		{
			Settings::getInstance()->setBool("Debug", true);
			Settings::getInstance()->setBool("HideConsole", false);
		}
		else if (strcmp(argv[i], "--fullscreen-borderless") == 0)
		{
			Settings::getInstance()->setBool("FullscreenBorderless", true);
		}
		else if (strcmp(argv[i], "--fullscreen") == 0)
		{
		Settings::getInstance()->setBool("FullscreenBorderless", false);
		}
		else if(strcmp(argv[i], "--windowed") == 0)
		{
			Settings::getInstance()->setBool("Windowed", true);
		}else if(strcmp(argv[i], "--vsync") == 0)
		{
			bool vsync = (strcmp(argv[i + 1], "on") == 0 || strcmp(argv[i + 1], "1") == 0) ? true : false;
			Settings::getInstance()->setBool("VSync", vsync);
			i++; // skip vsync value
		}else if(strcmp(argv[i], "--max-vram") == 0)
		{
			if (i >= argc - 1)
			{
				std::cerr << "Invalid max-vram supplied.";
				return false;
			}

			int maxVRAM = atoi(argv[i + 1]);
			if (maxVRAM < 0)
				maxVRAM = 0;

			Settings::getInstance()->setInt("MaxVRAM", maxVRAM);
			++i; // skip max-vram value
		}
		else if(strcmp(argv[i], "--max-ram") == 0)
		{
			if (i >= argc - 1)
			{
				std::cerr << "Invalid max-ram supplied.";
				return false;
			}

			int maxRAM = atoi(argv[i + 1]);
			if (maxRAM < 0)
				maxRAM = 0;

			Settings::getInstance()->setInt("MaxRAM", maxRAM);
			++i; // skip max-ram value
		}
		else if (strcmp(argv[i], "--force-kiosk") == 0)
		{
			Settings::getInstance()->setBool("ForceKiosk", true);
		}
		else if (strcmp(argv[i], "--force-kid") == 0)
		{
			Settings::getInstance()->setBool("ForceKid", true);
		}
		else if (strcmp(argv[i], "--force-disable-filters") == 0)
		{
			Settings::getInstance()->setBool("ForceDisableFilters", true);
		}
		else if (strcmp(argv[i], "--help") == 0 || strcmp(argv[i], "-h") == 0)
		{
#ifdef WIN32
			// This is a bit of a hack, but otherwise output will go to nowhere
			// when the application is compiled with the "WINDOWS" subsystem (which we usually are).
			// If you're an experienced Windows programmer and know how to do this
			// the right way, please submit a pull request!
			AttachConsole(ATTACH_PARENT_PROCESS);
			freopen("CONOUT$", "wb", stdout);
#endif
			std::cout <<
				"EmulationStation, a graphical front-end for ROM browsing.\n"
				"Written by Alec \"Aloshi\" Lofquist.\n"
				"Version " << PROGRAM_VERSION_STRING << ", built " << PROGRAM_BUILT_STRING << "\n\n"
				"Command line arguments:\n"
				"--resolution [width] [height]	try and force a particular resolution\n"
				"--gamelist-only			skip automatic game search, only read from gamelist.xml\n"
				"--ignore-gamelist		ignore the gamelist (useful for troubleshooting)\n"
				"--draw-framerate		display the framerate\n"
				"--no-exit			don't show the exit option in the menu\n"
				"--no-splash			don't show the splash screen\n"
				"--debug				more logging, show console on Windows\n"				
				"--windowed			not fullscreen, should be used with --resolution\n"
				"--vsync [1/on or 0/off]		turn vsync on or off (default is on)\n"
				"--max-vram [size]		Max VRAM to use in Mb before swapping. 0 for unlimited\n"
				"--force-kid		Force the UI mode to be Kid\n"
				"--force-kiosk		Force the UI mode to be Kiosk\n"
				"--force-disable-filters		Force the UI to ignore applied filters in gamelist\n"
				"--home [path]		Directory to use as home path\n"
				"--help, -h			summon a sentient, angry tuba\n\n"
				"--monitor [index]			monitor index\n\n"				
				"More information available in README.md.\n";
			return false; //exit after printing help
		}
	}

	return true;
}

bool verifyHomeFolderExists()
{
	//make sure the config directory exists	
	std::string configDir = Paths::getUserEmulationStationPath();
	if(!Utils::FileSystem::exists(configDir))
	{
		std::cout << "Creating config directory \"" << configDir << "\"\n";
		Utils::FileSystem::createDirectory(configDir);
		if(!Utils::FileSystem::exists(configDir))
		{
			std::cerr << "Config directory could not be created!\n";
			return false;
		}
	}

	return true;
}

// Returns true if everything is OK,
bool loadSystemConfigFile(Window* window, const char** errorString)
{
	*errorString = NULL;

	StopWatch stopWatch("loadSystemConfigFile :", LogDebug);

	if(!SystemData::loadConfig(window))
	{
		LOG(LogError) << "Error while parsing systems configuration file!";
		*errorString = "IT LOOKS LIKE YOUR SYSTEMS CONFIGURATION FILE HAS NOT BEEN SET UP OR IS INVALID. YOU'LL NEED TO DO THIS BY HAND, UNFORTUNATELY.\n\n"
			"VISIT EMULATIONSTATION.ORG FOR MORE INFORMATION.";
		return false;
	}

	if(SystemData::sSystemVector.size() == 0)
	{
		LOG(LogError) << "No systems found! Does at least one system have a game present? (check that extensions match!)\n(Also, make sure you've updated your es_systems.cfg for XML!)";
		*errorString = "WE CAN'T FIND ANY SYSTEMS!\n"
			"CHECK THAT YOUR PATHS ARE CORRECT IN THE SYSTEMS CONFIGURATION FILE, "
			"AND YOUR GAME DIRECTORY HAS AT LEAST ONE GAME WITH THE CORRECT EXTENSION.\n\n"
			"VISIT EMULATIONSTATION.ORG FOR MORE INFORMATION.";
		return false;
	}

	return true;
}

//called on exit, assuming we get far enough to have the log initialized
void onExit()
{
	Log::close();
}

#ifdef WIN32
#define PATH_MAX MAX_PATH
#include <direct.h>
#endif

int setLocale(char * argv1)
{
#if WIN32
	std::locale::global(std::locale("en-US"));
#else
	if (Utils::FileSystem::exists("./locale/lang")) // for local builds
		EsLocale::init("", "./locale/lang");	
	else
		EsLocale::init("", "/usr/share/locale");	
#endif

	setlocale(LC_TIME, "");

	return 0;
}


void signalHandler(int signum) 
{
	if (signum == SIGSEGV)
		LOG(LogError) << "Interrupt signal SIGSEGV received.\n";
	else if (signum == SIGFPE)
		LOG(LogError) << "Interrupt signal SIGFPE received.\n";
	else if (signum == SIGFPE)
		LOG(LogError) << "Interrupt signal SIGFPE received.\n";
	else
		LOG(LogError) << "Interrupt signal (" << signum << ") received.\n";

	Log::flush();

	// cleanup and close up stuff here  
	exit(signum);
}

void playVideo()
{
	ApiSystem::getInstance()->setReadyFlag(false);
	Settings::getInstance()->setBool("AlwaysOnTop", true);

	Window window;
	if (!window.init(true))
	{
		LOG(LogError) << "Window failed to initialize!";
		return;
	}

	Settings::getInstance()->setBool("VideoAudio", true);

	bool exitLoop = false;

	VideoVlcComponent vid(&window);
	vid.setVideo(gPlayVideo);
	vid.setOrigin(0.5f, 0.5f);
	vid.setPosition(Renderer::getScreenWidth() / 2.0f, Renderer::getScreenHeight() / 2.0f);
	vid.setMaxSize(Renderer::getScreenWidth(), Renderer::getScreenHeight());

	vid.setOnVideoEnded([&exitLoop]()
	{
		exitLoop = true;
		return false;
	});

	window.pushGui(&vid);

	vid.onShow();
	vid.topWindow(true);

	int lastTime = SDL_GetTicks();
	int totalTime = 0;

	while (!exitLoop)
	{
#ifdef TURBORAMA_REQUIRE_SUITE_LICENSE
		if (!SuiteAccessGate::instance().authorized()) break;
#endif
		SDL_Event event;

		if (SDL_PollEvent(&event))
		{
			do
			{
				if (event.type == SDL_QUIT)
					return;
			} 
			while (SDL_PollEvent(&event));
		}

		int curTime = SDL_GetTicks();
		int deltaTime = curTime - lastTime;

		if (vid.isPlaying())
		{
			totalTime += deltaTime;

			if (gPlayVideoDuration > 0 && totalTime > gPlayVideoDuration * 100)
				break;
		}

		Transform4x4f transform = Transform4x4f::Identity();
		vid.update(deltaTime);
		vid.render(transform);

		Renderer::swapBuffers();

		if (ApiSystem::getInstance()->isReadyFlagSet())
			break;
	}

	window.deinit(true);
}

void launchStartupGame()
{
	auto gamePath = SystemConf::getInstance()->get("global.bootgame.path");
	if (gamePath.empty() || !Utils::FileSystem::exists(gamePath))
		return;
	
	auto command = SystemConf::getInstance()->get("global.bootgame.cmd");
	if (!command.empty())
	{
		InputManager::getInstance()->init();
		command = Utils::String::replace(command, "%CONTROLLERSCONFIG%", InputManager::getInstance()->configureEmulators());
		Utils::Platform::ProcessStartInfo(command).run();		
	}	
}

// #include "utils/MathExpr.h"

int main(int argc, char* argv[])
{
	// Os auto-testes do gerenciador PIX resolvem o agente em relacao ao EXE.
	// Inicialize esse caminho antes de qualquer retorno antecipado; antes ele
	// dependia por engano do diretorio atual usado para iniciar o teste.
	Paths::setExePath(argv[0]);
#ifdef TURBORAMA_REQUIRE_SUITE_LICENSE
	if (argc == 2 && strcmp(argv[1], "--suite-access-self-test") == 0)
	{
		const bool passed = SuiteAccessGate::runSelfTest();
		fprintf(passed ? stdout : stderr, "SUITE_ACCESS_TEST=%s\n", passed ? "OK" : "FAILED");
		return passed ? 0 : 44;
	}
	if (argc == 2 && strcmp(argv[1], "--suite-access-probe-identity") == 0)
	{
		// Read-only diagnostic. No session, activation, frontend or game launch.
		return SuiteAccessGate::probeIdentity();
	}
	if (argc == 2 && strcmp(argv[1], "--suite-access-integrity-self-test") == 0)
	{
		const bool passed = SuiteAccessGate::verifyHelperIntegrity();
		fprintf(passed ? stdout : stderr, "SUITE_ACCESS_INTEGRITY=%s\n", passed ? "OK" : "FAILED");
		return passed ? 0 : 44;
	}
#endif
#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
#ifdef WIN32
	if (PixBinaryTrust::required())
	{
		std::vector<wchar_t> executable(32768, L'\0');
		const DWORD length = GetModuleFileNameW(nullptr, executable.data(),
			static_cast<DWORD>(executable.size()));
		std::string trustError;
		if (length == 0 || length >= executable.size()
			|| !PixBinaryTrust::verifyVendorBinary(std::wstring(executable.data(), length), trustError))
		{
			MessageBoxA(nullptr, trustError.empty() ? "EmulationStation comercial sem assinatura valida."
				: trustError.c_str(), "TurboRama - protecao comercial", MB_OK | MB_ICONERROR | MB_TOPMOST);
			return 31;
		}
	}
#endif
#endif
	if (argc == 2 && strcmp(argv[1], "--protected-decorations-self-test") == 0)
	{
		const char* systems[] = { "pc", "ps3", "ps4", "ps5", "switch", "windows", "xboxone" };
		const unsigned char pngSignature[] = { 0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A };
		for (const char* system : systems)
		{
			const std::string path = ProtectedDecorations::resourcePathForSystem(system);
			const ResourceData data = ResourceManager::getInstance()->getFileData(path);
			if (!data.ptr || data.length < sizeof(pngSignature) ||
				std::memcmp(data.ptr.get(), pngSignature, sizeof(pngSignature)) != 0)
				return 27;
		}
		return 0;
	}
	if (argc == 2 && strcmp(argv[1], "--no-commercial-services-self-test") == 0)
	{
#ifdef TURBORAMA_NO_COMMERCIAL_SERVICES
		fprintf(stdout, "TURBORAMA_BUILD_PROFILE=CLIENTE_SEM_SERVICOS\n");
		return 0;
#else
		fprintf(stderr, "TURBORAMA_BUILD_PROFILE=SERVICOS_COMERCIAIS_ATIVOS\n");
		return 33;
#endif
	}
#ifdef TURBORAMA_NO_COMMERCIAL_SERVICES
	if (argc == 2 && strcmp(argv[1], "--main-menu-auth-self-test") == 0)
	{
		const bool passed = MainMenuAuth::runSelfTest();
		fprintf(passed ? stdout : stderr, "MAIN_MENU_AUTH_TEST=%s\n", passed ? "OK" : "FAILED");
		return passed ? 0 : 35;
	}
#endif
#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
	if (argc == 2 && strcmp(argv[1], "--credit-warning-overlay-self-test") == 0)
	{
		CreditWarningOverlay::show(
			"TESTE DO AVISO: RESTAM 5 MINUTOS. ESTA CAMADA DEVE FICAR NA FRENTE DE TODAS AS TELAS.");
		const bool visible = CreditWarningOverlay::isVisible();
		for (int elapsedMs = 0; elapsedMs < 4000; elapsedMs += 50)
		{
			CreditWarningOverlay::update();
			std::this_thread::sleep_for(std::chrono::milliseconds(50));
		}
		return visible ? 0 : 26;
	}
	if (argc == 2 && strcmp(argv[1], "--pix-agent-manager-self-test") == 0)
	{
		std::string error;
		const bool passed = PixAgentManager::runSelfTest(error);
		if (!passed && !error.empty()) fprintf(stderr, "PIX_AGENT_MANAGER_TEST=FAILED: %s\n", error.c_str());
		else fprintf(stdout, "PIX_AGENT_MANAGER_TEST=OK\n");
		return passed ? 0 : 25;
	}
	if (argc == 2 && strcmp(argv[1], "--pix-agent-trust-self-test") == 0)
	{
		std::string error;
		const bool passed = PixAgentManager::runTrustSelfTest(error);
		if (!passed && !error.empty()) fprintf(stderr, "PIX_AGENT_TRUST_TEST=FAILED: %s\n", error.c_str());
		else fprintf(stdout, "PIX_AGENT_TRUST_TEST=OK\n");
		return passed ? 0 : 32;
	}
	if (argc == 3 && strcmp(argv[1], "--pix-agent-start-once") == 0)
	{
		Paths::setExePath(argv[0]);
		Paths::setHomePath(argv[2]);
		Utils::FileSystem::createDirectory(Utils::FileSystem::combine(argv[2], ".emulationstation"));
		Log::init();
		std::string error;
		const bool started = PixAgentManager::startIfConfigured(&error);
		if (!started && !error.empty()) LOG(LogError) << "[PIX] " << error;
		Log::close();
		return started ? 0 : 23;
	}
	if (argc == 4 && strcmp(argv[1], "--pix-verify-event") == 0)
		return PixBridge::verifyApprovedEventFileForTest(argv[2], argv[3]) ? 0 : 20;
	if (argc == 4 && strcmp(argv[1], "--pix-test-qr-cache") == 0)
	{
		// Teste de integracao isolado para a publicacao tardia do QR.
		Paths::setExePath(argv[0]);
		Paths::setHomePath(argv[2]);
		const bool passed = PixBridge::runQrCacheRegressionTest();
		Utils::FileSystem::writeAllText(argv[3], passed ? "QR_CACHE_TEST=OK\n" : "QR_CACHE_TEST=FAILED\n");
		return passed ? 0 : 24;
	}
	if (argc == 3 && strcmp(argv[1], "--pix-process-once") == 0)
	{
		Paths::setExePath(argv[0]);
		Paths::setHomePath(argv[2]);
		Utils::FileSystem::createDirectory(Utils::FileSystem::combine(argv[2], ".emulationstation"));
		Log::init();
		PixBridge::processApprovedCredits();
		CreditManager::getInstance().flushNow();
		Log::close();
		return 0;
	}
	if (argc == 5 && strcmp(argv[1], "--pix-create-request") == 0)
	{
		Paths::setExePath(argv[0]);
		Paths::setHomePath(argv[2]);
		Utils::FileSystem::createDirectory(Utils::FileSystem::combine(argv[2], ".emulationstation"));
		PixPackage package;
		try { package.minutes = std::stoi(argv[3]); package.amountCents = std::stoll(argv[4]); }
		catch (...) { return 22; }
		std::string requestId, error;
		const bool created = PixBridge::createPurchaseRequest(package, requestId, error);
		if (!created)
			Utils::FileSystem::writeAllText(Utils::FileSystem::combine(argv[2], "pix-create-error.txt"), error);
		return created ? 0 : 21;
	}
#else
	// Do not let a stale PIX/credit shortcut silently fall through to the GUI in
	// the customer build. These commands belong exclusively to the commercial
	// profile and are rejected before the normal argument parser starts.
	if (argc >= 2)
	{
		const char* disabledCommercialCommands[] = {
			"--credit-warning-overlay-self-test",
			"--pix-agent-manager-self-test",
			"--pix-agent-trust-self-test",
			"--pix-agent-start-once",
			"--pix-verify-event",
			"--pix-test-qr-cache",
			"--pix-process-once",
			"--pix-create-request"
		};
		for (const char* command : disabledCommercialCommands)
		{
			if (strcmp(argv[1], command) == 0)
			{
				fprintf(stderr, "TURBORAMA_COMMERCIAL_COMMAND_DISABLED=%s\n", command);
				return 34;
			}
		}
	}
#endif

	// Utils::MathExpr::performUnitTests();

	// signal(SIGABRT, signalHandler);
	signal(SIGFPE, signalHandler);
	signal(SIGILL, signalHandler);
	signal(SIGINT, signalHandler);
	signal(SIGSEGV, signalHandler);
	// signal(SIGTERM, signalHandler);

	srand((unsigned int)time(NULL));

	std::locale::global(std::locale("C"));

	if(!parseArgs(argc, argv))
		return 0;

	// only show the console on Windows if HideConsole is false
#ifdef WIN32
	// MSVC has a "SubSystem" option, with two primary options: "WINDOWS" and "CONSOLE".
	// In "WINDOWS" mode, no console is automatically created for us.  This is good,
	// because we can choose to only create the console window if the user explicitly
	// asks for it, preventing it from flashing open and then closing.
	// In "CONSOLE" mode, a console is always automatically created for us before we
	// enter main. In this case, we can only hide the console after the fact, which
	// will leave a brief flash.
	// TL;DR: You should compile ES under the "WINDOWS" subsystem.
	// I have no idea how this works with non-MSVC compilers.
	if(!Settings::getInstance()->getBool("HideConsole"))
	{
		// we want to show the console
		// if we're compiled in "CONSOLE" mode, this is already done.
		// if we're compiled in "WINDOWS" mode, no console is created for us automatically;
		// the user asked for one, so make one and then hook stdin/stdout/sterr up to it
		if(AllocConsole()) // should only pass in "WINDOWS" mode
		{
			freopen("CONIN$", "r", stdin);
			freopen("CONOUT$", "wb", stdout);
			freopen("CONOUT$", "wb", stderr);
		}
	}else{
		// we want to hide the console
		// if we're compiled with the "WINDOWS" subsystem, this is already done.
		// if we're compiled with the "CONSOLE" subsystem, a console is already created;
		// it'll flash open, but we hide it nearly immediately
		if(GetConsoleWindow()) // should only pass in "CONSOLE" mode
			ShowWindow(GetConsoleWindow(), SW_HIDE);
	}
#endif

#ifdef TURBORAMA_REQUIRE_SUITE_LICENSE
	// Validate before any media playback, emulator startup or frontend loading.
	// The companion owns the same Suite identity/session and runs independently
	// while the existing game launch loop is blocked waiting for an emulator.
	std::string suiteAccessError;
	if (!SuiteAccessGate::instance().start(suiteAccessError))
	{
		MessageBoxA(nullptr, suiteAccessError.c_str(), "TurboRama Suite - acesso",
			MB_OK | MB_ICONERROR | MB_TOPMOST);
		return 44;
	}
#endif

	// call this ONLY when linking with FreeImage as a static library
#ifdef FREEIMAGE_LIB
	FreeImage_Initialise();
#endif

	//if ~/.emulationstation doesn't exist and cannot be created, bail
	if(!verifyHomeFolderExists())
		return 1;

	if (!gPlayVideo.empty())
	{
		playVideo();
		return 0;
	}

	//start the logger
	Log::init();	

	LOG(LogInfo) << "EmulationStation - v" << PROGRAM_VERSION_STRING << ", built " << PROGRAM_BUILT_STRING;

#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
	// O servico PIX acompanha o EmulationStation. Dados e credenciais ficam na
	// pasta persistente .emulationstation/pix e sobrevivem a reinicializacoes.
	// Se o proprietario ainda nao configurou o PIX, nada externo e iniciado.
	{
		std::string pixStartError;
		if (!PixAgentManager::startIfConfigured(&pixStartError) && !pixStartError.empty())
			LOG(LogInfo) << "[PIX] " << pixStartError;
	}
#else
	LOG(LogInfo) << "TurboRama profile: cliente sem servicos comerciais";
#endif

	//always close the log on exit
	atexit(&onExit);

	// Set locale
	setLocale(argv[0]);	

	// Materialize the singleton on the main thread before background startup
	// work can request resources.
	ResourceManager::getInstance();

#if !WIN32
	if(enable_startup_game) {
	  // Run boot game, before Window Create for linux
	  launchStartupGame();
	}
#endif

	// Threaded initializations
	auto threadPool = new Utils::ThreadPool("main()", -3);
	auto vlcInit = threadPool->queueWorkItem([] { VideoVlcComponent::init(); });
	threadPool->queueWorkItem([] { ApiSystem::getInstance()->getIpAddress(); });
	threadPool->queueWorkItem([] { MetaDataList::initMetadata(); });
	threadPool->queueWorkItem([] { MameNames::init(); });
	threadPool->queueWorkItem([] { Genres::init(); });
	threadPool->queueWorkItem([] { HttpReq::resetCookies(); });

	Window window;
	ViewController::init(&window);

	window.setReloadGamelistsCallback([&window] { ViewController::reloadAllGames(&window, true, true); });	
	window.pushGui(ViewController::get());
	if (!window.init(true, false))
	{
		LOG(LogError) << "Window failed to initialize!";
		return 1;
	}

	Renderer::setWindowResizable(false);

	bool splashScreen = Settings::getInstance()->getBool("SplashScreen");
	bool splashScreenProgress = Settings::getInstance()->getBool("SplashScreenProgress");

	// The embedded theme can take a while to decrypt on its first run. Start it
	// only after the window exists and keep rendering progress so Windows does
	// not present the application as frozen.
	window.renderSplashScreen(_("Loading theme"), 0.0f);
	const bool embeddedThemeReady = EmbeddedTheme::initialize([&window](float progress) {
		window.renderSplashScreen(_("Loading theme"), progress);
	});
	if (!embeddedThemeReady)
		LOG(LogWarning) << "Embedded theme could not be initialized.";
	else
	{
		ResourceManager::invalidatePathCache();
		ResourceManager::getInstance()->unloadAll();
		ResourceManager::getInstance()->reloadAll();
	}

	// Workers consult Settings and ResourceManager; start them only after the
	// theme selection and resource cache are stable.
	threadPool->start();

	if (splashScreen)
		window.renderSplashScreen(splashScreenProgress ? _("Loading system config...") : _("Loading..."));
	else
		window.closeSplashScreen();

	Scripting::fireEvent("start");

	SystemScreenSaver screensaver(&window);
	CollectionSystemManager::init(&window);
	
	Zaparoo::checkZaparooEnabledAsync();
	PowerSaver::init();
	InputConfig::AssignActionButtons();

	if (ApiSystem::getInstance()->isScriptingSupported(ApiSystem::PDFEXTRACTION))
		TextureData::PdfHandler = ApiSystem::getInstance();
	
	threadPool->waitAllExcept(vlcInit); // Wait for what's necessary for loadSystemConfigFile

	const char* errorMsg = NULL;
	if (!loadSystemConfigFile(splashScreen && splashScreenProgress ? &window : nullptr, &errorMsg))
	{
		// something went terribly wrong
		if (errorMsg == NULL)
		{
			LOG(LogError) << "Unknown error occured while parsing system config file.";
			Renderer::deinit();
			return 1;
		}

		// we can't handle es_systems.cfg file problems inside ES itself, so display the error message then quit
		window.pushGui(new GuiMsgBox(&window, errorMsg, _("QUIT"), [] { Utils::Platform::quitES(); }));
	}

	SystemConf* systemConf = SystemConf::getInstance();

#ifdef _ENABLE_KODI_
	if (systemConf->getBool("kodi.enabled", true) && systemConf->getBool("kodi.atstartup"))
	{
		if (splashScreen)
			window.closeSplashScreen();

		ApiSystem::getInstance()->launchKodi(&window);

		if (splashScreen)
		{
			window.renderSplashScreen("");
			splashScreen = false;
		}
	}
#endif

	// preload what we can right away instead of waiting for the user to select it
	// this makes for no delays when accessing content, but a longer startup time
	ViewController::get()->preload();

	// Initialize input
	InputManager::getInstance()->init();
	SDL_StopTextInput();

	NetworkThread* nthread = new NetworkThread(&window);
	HttpServerThread httpServer(&window);

	// tts
	TextToSpeech::getInstance()->enable(Settings::getInstance()->getBool("TTS"), false);
	
	if (errorMsg == NULL)
	{
		ViewController::get()->goToStart(true);
	}

	threadPool->wait();
	delete threadPool;

	window.closeSplashScreen();

	// Create a flag in  temporary directory to signal READY state
	ApiSystem::getInstance()->setReadyFlag();

	// Play music
	AudioManager::getInstance()->init();

	if (ViewController::get()->getState().viewing == ViewController::GAME_LIST || ViewController::get()->getState().viewing == ViewController::SYSTEM_SELECT)
		AudioManager::getInstance()->changePlaylist(ViewController::get()->getState().getSystem()->getTheme());
	else
		AudioManager::getInstance()->playRandomMusic();


#ifdef WIN32	
	DWORD displayFrequency = 60;

	DEVMODE lpDevMode;
	memset(&lpDevMode, 0, sizeof(DEVMODE));
	lpDevMode.dmSize = sizeof(DEVMODE);
	lpDevMode.dmFields = DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFLAGS | DM_DISPLAYFREQUENCY;
	lpDevMode.dmDriverExtra = 0;

	if (EnumDisplaySettings(NULL, ENUM_CURRENT_SETTINGS, &lpDevMode) != 0) {
		displayFrequency = lpDevMode.dmDisplayFrequency; // default value if cannot retrieve from user settings.
	}

	int timeLimit = (1000 / displayFrequency) - 10;	 // Margin for vsync
	if (timeLimit < 0)
		timeLimit = 0;
#endif

	Renderer::setWindowResizable(true);

	int lastTime = SDL_GetTicks();
	int ps_time = SDL_GetTicks();

	bool running = true;

#ifdef BATOCERA
	bool hotkeyPressed = false;
#endif

	while(running)
	{
#ifdef TURBORAMA_REQUIRE_SUITE_LICENSE
		if (!SuiteAccessGate::instance().authorized())
		{
			LOG(LogWarning) << "[Suite] A autorizacao terminou; encerrando o frontend com salvamento normal.";
			MessageBoxA(nullptr,
				"O acesso do TurboRama Suite terminou. Abra o Suite e verifique a ativacao e a conexao.",
				"TurboRama Suite - acesso", MB_OK | MB_ICONWARNING | MB_TOPMOST);
			break;
		}
#endif
		SDL_Event event;

		bool ps_standby = PowerSaver::getState() && (int) SDL_GetTicks() - ps_time > PowerSaver::getMode();
		if(ps_standby ? SDL_WaitEventTimeout(&event, PowerSaver::getTimeout()) : SDL_PollEvent(&event))
		{
			// PowerSaver can push events to exit SDL_WaitEventTimeout immediatly
			// Reset this event's state
			TRYCATCH("resetRefreshEvent", PowerSaver::resetRefreshEvent());

			do
			{
#ifdef BATOCERA
			  // global hotkeys
			  bool eventTaken = false;
			  if(event.type == SDL_JOYBUTTONDOWN || event.type == SDL_JOYBUTTONUP)
			    {
			      InputConfig* config = InputManager::getInstance()->getInputConfigByDevice(event.jbutton.which);
			      if(config)
				{
				  // Find first player controller info
				  auto playerDevices = InputManager::getInstance()->lastKnownPlayersDeviceIndexes();
				  auto playerDevice = playerDevices.find(0);
				  if (playerDevice != playerDevices.cend())
				    {
				      if (config->getDeviceIndex() == playerDevice->second.index)
					{
					  Input input = Input(event.jbutton.which, TYPE_BUTTON, event.jbutton.button, event.jbutton.state == SDL_PRESSED, false);
					  if (config->isMappedTo("hotkey", input))
					    hotkeyPressed = input.value != 0;

					  if(hotkeyPressed && input.value != 0)
					    {
					      std::string hotkey_controlcenter = Settings::getInstance()->getString("HOTKEY_CONTROLCENTER");
					      if (config->isMappedTo(hotkey_controlcenter, input))
						{
						  hotkeyPressed = false;
						  ApiSystem::getInstance()->launchControlcenter();
						  eventTaken = true;
						}
					    }
					}
				    }
				}
			    }
			  //
			  if(eventTaken)
			    continue;
#endif

				TRYCATCH("InputManager::parseEvent", InputManager::getInstance()->parseEvent(event, &window));

				// TurboRama: F10/F12 atuam exclusivamente sobre o saldo avulso.
				// Contas cadastradas e o fluxo PIX nunca sao alterados por estes atalhos.
				if (event.type == SDL_KEYDOWN && !event.key.repeat)
				{
					const SDL_Keycode k = event.key.keysym.sym;
					// Alt+End / Ctrl+End: nao abre menu (desativado no kiosk)
					if (k == SDLK_END && (event.key.keysym.mod & (KMOD_ALT | KMOD_CTRL)))
						continue;
#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
					if (k == SDLK_F11)
					{
						GuiMenu::requestCreditSettingsAccess_static(&window);
						continue;
					}
					if (k == SDLK_F10)
					{
						auto& credits = CreditManager::getInstance();
						if (!credits.isGuestMode())
						{
							window.displayNotificationMessage(
								_("F10 disponivel somente para credito AVULSO."), 4);
						}
						else if (credits.addCoin())
						{
							window.displayNotificationMessage(
								std::string(_("AVULSO +")) + std::to_string(credits.getMinutesPerCoin())
								+ _(" MIN | SALDO: ") + credits.formatRemaining(), 5);
						}
						else
						{
							window.displayNotificationMessage(
								_("Credito avulso nao adicionado. Aguarde e tente novamente."), 3);
						}
						continue;
					}
					if (k == SDLK_F12)
					{
						auto& credits = CreditManager::getInstance();
						if (!credits.isGuestMode())
						{
							window.displayNotificationMessage(
								_("F12 nao altera credito de cliente cadastrado."), 4);
						}
						else if (!credits.hasGuestCredit())
						{
							window.displayNotificationMessage(_("Credito AVULSO ja esta zerado."), 3);
						}
						else
						{
							credits.clearGuestCredit();
							window.displayNotificationMessage(
								credits.hasGuestCredit()
									? _("Nao foi possivel zerar o credito AVULSO.")
									: _("Credito AVULSO ZERADO (F12)."), 4);
						}
						continue;
					}
#else
					if (k == SDLK_F11)
					{
						GuiMenu::requestTurboSystemMenuAccess_static(&window);
						continue;
					}
#endif
				}

				if (event.type == SDL_WINDOWEVENT && event.window.event == SDL_WINDOWEVENT_RESIZED && Settings::getInstance()->getBool("Windowed"))
				{
					if (Renderer::onScreenSizeChanged(event.window.data1, event.window.data2))
					{
						Renderer::setWindowResizable(false);

						window.closeSplashScreen();

						while (window.peekGui() && window.peekGui() != ViewController::get())
							delete window.peekGui();

						ViewController::get()->reloadAll(&window);
						window.closeSplashScreen();

						Renderer::setWindowResizable(true);
					}
				}				

				if (event.type == SDL_QUIT)
					running = false;
			} 
			while(SDL_PollEvent(&event));

			// check guns
			InputManager::getInstance()->updateGuns(&window);

			// triggered if exiting from SDL_WaitEvent due to event
			if (ps_standby)
				// show as if continuing from last event
				lastTime = SDL_GetTicks();

			// reset counter
			ps_time = SDL_GetTicks();
		}
		else if (ps_standby == false)
		{
		  // check guns
		  InputManager::getInstance()->updateGuns(&window);

		  // If exitting SDL_WaitEventTimeout due to timeout. Trail considering
		  // timeout as an event
		  //	ps_time = SDL_GetTicks();
		}

		if (window.isSleeping())
		{
			lastTime = SDL_GetTicks();
			SDL_Delay(1); // this doesn't need to be accurate, we're just giving up our CPU time until something wakes us up
			continue;
		}

		int curTime = SDL_GetTicks();
		int deltaTime = curTime - lastTime;
		lastTime = curTime;

		// cap deltaTime if it ever goes negative
		if(deltaTime < 0)
			deltaTime = 1000;

		TRYCATCH("Window.update" ,window.update(deltaTime))	
		TRYCATCH("Window.render", window.render())

		int fpsLimit = Settings::FpsLimit();
		if (fpsLimit > 0)
		{
			int frameTime = (1000 + fpsLimit / 2) / fpsLimit;
			int processDuration = SDL_GetTicks() - curTime;
			if (processDuration < frameTime)
			{
				int timeToWait = frameTime - processDuration;
				if (timeToWait > 0 && timeToWait < 100)
					SDL_Delay(timeToWait);
			}
		}

		Renderer::swapBuffers();		
	}

#ifdef TURBORAMA_REQUIRE_SUITE_LICENSE
	SuiteAccessGate::instance().stop();
#endif
	if (Utils::Platform::isFastShutdown())
		Settings::getInstance()->setBool("IgnoreGamelist", true);

	#ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
	// TurboRama: flush credit/players before exit (avoid lost last seconds)
	CreditManager::getInstance().flushNow();
	#endif

	WatchersManager::stop();
	ThreadedHasher::stop();
	ThreadedScraper::stop();

	ApiSystem::getInstance()->deinit();

	while (window.peekGui() != ViewController::get())
		delete window.peekGui();

	if (SystemData::hasDirtySystems())
		window.renderSplashScreen(_("SAVING METADATA. PLEASE WAIT..."));

	MameNames::deinit();
	ViewController::saveState();
	CollectionSystemManager::deinit();
	SystemData::deleteSystems();
	Scripting::exitScriptingEngine();

	// call this ONLY when linking with FreeImage as a static library
#ifdef FREEIMAGE_LIB
	FreeImage_DeInitialise();
#endif
	
	// Delete ViewController
	while (window.peekGui() != nullptr)
		delete window.peekGui();

	window.deinit();

	Utils::Platform::processQuitMode();

	LOG(LogInfo) << "EmulationStation cleanly shutting down.";

	Log::flush();

	return 0;
}

