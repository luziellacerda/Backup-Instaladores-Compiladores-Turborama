#pragma once
#ifndef ES_APP_MAIN_MENU_AUTH_H
#define ES_APP_MAIN_MENU_AUTH_H

#include <string>

// Authentication for the normal START menu.  This deliberately lives outside
// CreditManager so customer builds can keep kiosk protection without compiling
// any credit, PIX, accounting or rental-time state.
class MainMenuAuth
{
public:
	static bool verify(const std::string& password);
	static bool setPassword(const std::string& password);
	static bool isUsingDefaultPassword();
	static bool hasCustomPassword();
	static bool runSelfTest();
};

#endif // ES_APP_MAIN_MENU_AUTH_H
