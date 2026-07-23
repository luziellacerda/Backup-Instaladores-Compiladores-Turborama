#pragma once
#ifndef ES_CORE_EMBEDDED_THEME_H
#define ES_CORE_EMBEDDED_THEME_H

#include <string>

class EmbeddedTheme
{
public:
	static const char* THEME_SET_ID;

	static bool initialize();
	static bool isAvailable();
	static bool isActiveThemeSet(const std::string& themeSet);
	static std::string getRootPath();
	static std::string getThemePath(const std::string& system);
	static std::string getResourcesPath();
};

#endif // ES_CORE_EMBEDDED_THEME_H