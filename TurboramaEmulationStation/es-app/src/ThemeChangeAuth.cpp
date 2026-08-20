#include "ThemeChangeAuth.h"

#include "Settings.h"
#include "utils/md5.h"

namespace
{
	// MD5 de "admin". A senha em texto puro nao e persistida no arquivo de configuracao.
	const char* DEFAULT_THEME_PASSWORD_HASH = "21232f297a57a5a743894a0e4a801fc3";

	std::string hashPassword(const std::string& password)
	{
		return MD5(password).hexdigest();
	}

	std::string getActivePasswordHash()
	{
		const std::string& stored = Settings::getInstance()->getString("ThemeChangePasswordHash");
		return stored.empty() ? DEFAULT_THEME_PASSWORD_HASH : stored;
	}
}

bool ThemeChangeAuth::verify(const std::string& password)
{
	return !password.empty() && hashPassword(password) == getActivePasswordHash();
}

bool ThemeChangeAuth::setPassword(const std::string& password)
{
	if (password.empty())
		return false;

	Settings::getInstance()->setString("ThemeChangePasswordHash", hashPassword(password));
	return true;
}

bool ThemeChangeAuth::hasCustomPassword()
{
	return !Settings::getInstance()->getString("ThemeChangePasswordHash").empty();
}
