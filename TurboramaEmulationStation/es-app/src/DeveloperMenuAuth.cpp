#include "DeveloperMenuAuth.h"

#include "Settings.h"
#include "utils/md5.h"

namespace
{
	static const unsigned char sKey[] = {
		0xA7, 0x3C, 0x91, 0x5E, 0xD2, 0x48, 0xB6, 0x1F,
		0x83, 0x29, 0xF4, 0x6A, 0xC5, 0x0D, 0x97, 0x52
	};

	static const unsigned char sObfuscatedDigest[] = {
		0x2A, 0xAA, 0x97, 0x4F, 0xC0, 0xE8, 0xCD, 0x06,
		0x00, 0xD5, 0xF4, 0xCB, 0x1F, 0x09, 0xCC, 0x19
	};

	std::string hashPassword(const std::string& password)
	{
		return MD5(password).hexdigest();
	}

	std::string decodeDefaultHash()
	{
		static const char hex[] = "0123456789abcdef";
		std::string result;
		result.reserve(32);

		for (int i = 0; i < 16; i++)
		{
			const unsigned char value = sObfuscatedDigest[i] ^ sKey[i % (sizeof(sKey) / sizeof(sKey[0]))];
			result += hex[(value >> 4) & 0x0F];
			result += hex[value & 0x0F];
		}

		return result;
	}

	std::string getActivePasswordHash()
	{
		const std::string& stored = Settings::getInstance()->getString("DeveloperMenuPasswordHash");
		if (!stored.empty())
			return stored;

		return decodeDefaultHash();
	}
}

bool DeveloperMenuAuth::verify(const std::string& password)
{
	if (password.empty())
		return false;

	return hashPassword(password) == getActivePasswordHash();
}

void DeveloperMenuAuth::setPassword(const std::string& password)
{
	if (password.empty())
		Settings::getInstance()->setString("DeveloperMenuPasswordHash", "");
	else
		Settings::getInstance()->setString("DeveloperMenuPasswordHash", hashPassword(password));
}

bool DeveloperMenuAuth::hasCustomPassword()
{
	return !Settings::getInstance()->getString("DeveloperMenuPasswordHash").empty();
}