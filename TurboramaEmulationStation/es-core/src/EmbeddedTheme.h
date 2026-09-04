#pragma once
#ifndef ES_CORE_EMBEDDED_THEME_H
#define ES_CORE_EMBEDDED_THEME_H

#include <functional>
#include <string>

class EmbeddedTheme
{
public:
	using ProgressCallback = std::function<void(float)>;

	static const char* THEME_SET_ID;

	static bool initialize(const ProgressCallback& progressCallback = ProgressCallback());
	static bool isAvailable();
	static bool isActiveThemeSet(const std::string& themeSet);
	static std::string getRootPath();
	static std::string getThemePath(const std::string& system);
	static std::string getResourcesPath();
};

#endif // ES_CORE_EMBEDDED_THEME_H
