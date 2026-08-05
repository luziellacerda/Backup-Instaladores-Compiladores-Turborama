#pragma once
#ifndef ES_CORE_PLATFORM_H
#define ES_CORE_PLATFORM_H

#include <functional>
#include <string>

#ifdef WIN32
#include <Windows.h>
#include <intrin.h>

#define sleep Sleep
#endif

class Window;

namespace Utils
{
	namespace Platform
	{
		enum QuitMode
		{
			QUIT = 0,
			RESTART = 1,
			SHUTDOWN = 2,
			REBOOT = 3,
			FAST_SHUTDOWN = 4,
			FAST_REBOOT = 5,
			EXIT_ONLY = 6
		};

		class ProcessStartInfo
		{
		public:
			ProcessStartInfo();
			ProcessStartInfo(const std::string& cmd);

			int run() const;

			std::string command;			
			bool waitForExit;
			bool showWindow;
			Window* window;
			// Chamado durante a espera com segundos decorridos. Se retornar false,
			// o Job Object do processo e encerrado quando a opcao abaixo estiver ativa.
			std::function<bool(long)> pollCallback;
			bool killProcessTreeOnCallbackFalse;
#ifndef WIN32
			std::string stderrFilename;
			std::string stdoutFilename;
#endif
		};

		int quitES(QuitMode mode = QuitMode::QUIT);
		bool isFastShutdown();
		void processQuitMode();

		struct BatteryInformation
		{
			BatteryInformation()
			{
				hasBattery = false;
				level = 0;
				isCharging = false;
			}

			bool hasBattery;
			int  level;
			bool isCharging;
		};

		BatteryInformation queryBatteryInformation();

		std::string queryIPAddress();
		std::string getArchString();
		unsigned long long getTotalSystemMemory();

#if WIN32
		bool isWindows10();
		bool isWindows11();		
#else
		bool isBuildroot();
#endif
	}
}

#endif // ES_CORE_PLATFORM_H
