#pragma once
#ifndef ES_APP_DEVELOPER_MENU_AUTH_H
#define ES_APP_DEVELOPER_MENU_AUTH_H

#include <string>

class DeveloperMenuAuth
{
public:
	static bool verify(const std::string& password);
	static void setPassword(const std::string& password);
	static bool hasCustomPassword();
};

#endif // ES_APP_DEVELOPER_MENU_AUTH_H