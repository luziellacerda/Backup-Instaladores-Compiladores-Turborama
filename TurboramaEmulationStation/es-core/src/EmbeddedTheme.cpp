#include "EmbeddedTheme.h"

#include "Log.h"
#include "Paths.h"
#include "Settings.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"
#include "utils/ZipFile.h"
#include "utils/md5.h"

#include <algorithm>
#include <cctype>
#include <fstream>
#include <mutex>
#include <vector>

#if WIN32
#include <Windows.h>
#ifndef IDR_EMBEDDED_THEME
#define IDR_EMBEDDED_THEME 101
#endif
#endif

const char* EmbeddedTheme::THEME_SET_ID = "__turborama__";

static bool sAvailable = false;
static std::string sRootPath;
static std::mutex sInitMutex;

namespace
{
	static const unsigned char sKey[] = {
		0xB3, 0x57, 0x9E, 0x24, 0xC8, 0x6A, 0x11, 0xFD,
		0x45, 0x8B, 0xD2, 0x37, 0xE9, 0x02, 0xAC, 0x71
	};

	static const size_t sKeyLen = sizeof(sKey) / sizeof(sKey[0]);
	static const size_t sDecryptChunkSize = 4 * 1024 * 1024;
	static const char sPayloadHeader[] = "TRTHEME1:";
	static const size_t sPayloadIdentityLength = 32;

	struct EmbeddedPayload
	{
		const unsigned char* data = nullptr;
		size_t size = 0;
		size_t archiveOffset = 0;
		std::string identity;
	};

	void setHiddenDirectory(const std::string& path)
	{
#if WIN32
		SetFileAttributesA(path.c_str(), FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
#endif
	}

	std::string getCacheDirectory()
	{
		std::string base = Utils::FileSystem::getCanonicalPath(Paths::getUserEmulationStationPath() + "/.runtime");
		Utils::FileSystem::createDirectory(base);
		return base;
	}

	bool loadEmbeddedPayload(EmbeddedPayload& payload)
	{
#if WIN32
		HMODULE module = GetModuleHandle(NULL);
		HRSRC resource = FindResource(module, MAKEINTRESOURCE(IDR_EMBEDDED_THEME), RT_RCDATA);
		if (resource == NULL)
		{
			LOG(LogError) << "EmbeddedTheme: embedded theme resource not found in executable.";
			return false;
		}

		HGLOBAL loaded = LoadResource(module, resource);
		if (loaded == NULL)
		{
			LOG(LogError) << "EmbeddedTheme: unable to load embedded theme resource.";
			return false;
		}

		payload.size = SizeofResource(module, resource);
		payload.data = (const unsigned char*)LockResource(loaded);
		const size_t prefixLength = sizeof(sPayloadHeader) - 1;
		const size_t headerLength = prefixLength + sPayloadIdentityLength + 1;
		if (payload.data == NULL || payload.size <= headerLength)
		{
			LOG(LogError) << "EmbeddedTheme: embedded theme resource is empty or incomplete.";
			return false;
		}
		if (!std::equal(sPayloadHeader, sPayloadHeader + prefixLength, payload.data)
			|| payload.data[headerLength - 1] != '\n')
		{
			LOG(LogError) << "EmbeddedTheme: payload identity header is missing; rebuild the executable.";
			return false;
		}

		payload.identity.assign(reinterpret_cast<const char*>(payload.data + prefixLength), sPayloadIdentityLength);
		if (!std::all_of(payload.identity.begin(), payload.identity.end(), [](unsigned char ch) {
			return std::isxdigit(ch) != 0;
		}))
		{
			LOG(LogError) << "EmbeddedTheme: payload identity header is invalid.";
			return false;
		}
		std::transform(payload.identity.begin(), payload.identity.end(), payload.identity.begin(), [](unsigned char ch) {
			return (char)std::tolower(ch);
		});
		payload.archiveOffset = headerLength;
		return true;
#else
		(void)payload;
		return false;
#endif
	}

	bool decryptResourceToFile(const EmbeddedPayload& payload, size_t& payloadSize)
	{
		payloadSize = 0;

#if WIN32
		if (payload.data == nullptr || payload.archiveOffset >= payload.size)
			return false;
		const size_t archiveSize = payload.size - payload.archiveOffset;

		const std::string cacheRoot = getCacheDirectory();
		const std::string tempZip = Utils::FileSystem::getCanonicalPath(cacheRoot + "/.theme.pack.zip");

		std::ofstream output(tempZip, std::ios::binary);
		if (!output.is_open())
		{
			LOG(LogError) << "EmbeddedTheme: failed to open temporary theme archive.";
			return false;
		}

		MD5 md5;
		std::vector<unsigned char> chunk;
		chunk.reserve(sDecryptChunkSize);

		for (size_t offset = 0; offset < archiveSize; )
		{
			const size_t length = std::min<size_t>(sDecryptChunkSize, archiveSize - offset);
			chunk.resize(length);

			for (size_t i = 0; i < length; i++)
				chunk[i] = payload.data[payload.archiveOffset + offset + i] ^ sKey[(offset + i) % sKeyLen];

			output.write(reinterpret_cast<const char*>(chunk.data()), length);
			md5.update(reinterpret_cast<const char*>(chunk.data()), (MD5::size_type)length);

			offset += length;
		}

		output.close();
		if (!output.good())
		{
			LOG(LogError) << "EmbeddedTheme: failed to write temporary theme archive.";
			Utils::FileSystem::removeFile(tempZip);
			return false;
		}

		md5.finalize();
		const std::string actualIdentity = md5.hexdigest();
		if (actualIdentity != payload.identity)
		{
			LOG(LogError) << "EmbeddedTheme: payload identity does not match its archive; rebuild the executable.";
			Utils::FileSystem::removeFile(tempZip);
			return false;
		}
		payloadSize = archiveSize;

		LOG(LogInfo) << "EmbeddedTheme: decrypted " << archiveSize << " bytes from executable.";
		// Este arquivo acabou de ser criado. Nao aceite um resultado negativo
		// antigo do FileSystemCache durante a primeira inicializacao.
		return Utils::FileSystem::exists(tempZip, false);
#else
		(void)payload;
		return false;
#endif
	}

	bool extractThemeArchive(const std::string& tempZip, const std::string& targetPath)
	{
		Utils::Zip::ZipFile archive;
		if (!archive.load(tempZip))
		{
			LOG(LogError) << "EmbeddedTheme: unable to open embedded theme archive.";
			return false;
		}

		Utils::FileSystem::createDirectory(targetPath);

		const auto members = archive.namelist();
		size_t extracted = 0;

		for (const auto& member : members)
		{
			if (member.empty() || Utils::String::endsWith(member, "/"))
				continue;

			const std::string fullPath = Utils::FileSystem::getCanonicalPath(targetPath + "/" + member);
			const std::string parentPath = Utils::FileSystem::getParent(fullPath);
			if (!parentPath.empty())
				Utils::FileSystem::createDirectory(parentPath);

			if (!archive.extract(member, targetPath, false))
			{
				LOG(LogError) << "EmbeddedTheme: failed extracting '" << member << "'.";
				return false;
			}

			extracted++;
			if (extracted % 100 == 0)
				LOG(LogInfo) << "EmbeddedTheme: extracted " << extracted << " / " << members.size() << " files...";
		}

		LOG(LogInfo) << "EmbeddedTheme: extracted " << extracted << " files.";
		// theme.xml pode ter sido consultado antes da extracao e estar cacheado
		// como inexistente. A confirmacao pos-escrita precisa tocar o disco.
		return Utils::FileSystem::exists(targetPath + "/theme.xml", false);
	}

	bool isThemeSetAlias(const std::string& themeSet)
	{
		if (themeSet == EmbeddedTheme::THEME_SET_ID)
			return true;

		return Utils::String::toLower(themeSet) == "turborama";
	}

	void applyThemeSetSelection()
	{
		const std::string& currentTheme = Settings::getInstance()->getString("ThemeSet");
		if (currentTheme.empty() || currentTheme == "default" || isThemeSetAlias(currentTheme))
			Settings::getInstance()->setString("ThemeSet", EmbeddedTheme::THEME_SET_ID);
	}

	bool findCachedTheme(const std::string& payloadIdentity, std::string& cachedPath)
	{
		const std::string cacheRoot = getCacheDirectory();
		if (!Utils::FileSystem::isDirectory(cacheRoot) || payloadIdentity.size() < 12)
			return false;

		cachedPath = Utils::FileSystem::getCanonicalPath(cacheRoot + "/" + payloadIdentity.substr(0, 12));
		const std::string markerPath = cachedPath + "/.payload";
		return Utils::FileSystem::exists(cachedPath + "/theme.xml", false)
			&& Utils::FileSystem::exists(markerPath, false)
			&& Utils::FileSystem::readAllText(markerPath) == payloadIdentity;
	}

	void ensureDefaultSubsetSettings()
	{
		Settings* settings = Settings::getInstance();

		auto setDefault = [&](const char* key, const char* value)
		{
			if (settings->getString(key).empty())
				settings->setString(key, value);
		};

		setDefault("subset.region", "LZ");
		setDefault("subset.frontend", "TruboRama");
		setDefault("subset.colorset", "turborama");
		setDefault("subset.aspect-ratio", "16-9");
		setDefault("subset.system-avatar", "Turborama");
		setDefault("subset.system-username", "Turborama-X");
		setDefault("subset.top-info", "default");
	}
}

bool EmbeddedTheme::initialize()
{
	if (sAvailable)
		return true;

	std::lock_guard<std::mutex> lock(sInitMutex);
	if (sAvailable)
		return true;

	const std::string cacheRoot = getCacheDirectory();
	std::string extractPath;
	EmbeddedPayload payload;
	if (!loadEmbeddedPayload(payload))
		return false;

	if (findCachedTheme(payload.identity, extractPath))
	{
		sRootPath = extractPath;
		sAvailable = true;

		applyThemeSetSelection();
		ensureDefaultSubsetSettings();
		LOG(LogInfo) << "EmbeddedTheme: ready at " << sRootPath << " (cached)";
		return true;
	}

	size_t payloadSize = 0;
	if (!decryptResourceToFile(payload, payloadSize))
		return false;

	const std::string tempZip = Utils::FileSystem::getCanonicalPath(cacheRoot + "/.theme.pack.zip");
	extractPath = Utils::FileSystem::getCanonicalPath(cacheRoot + "/" + payload.identity.substr(0, 12));
	const std::string markerPath = extractPath + "/.payload";

	const bool markerMatches = Utils::FileSystem::exists(markerPath, false) && Utils::FileSystem::readAllText(markerPath) == payload.identity;
	const bool themeReady = Utils::FileSystem::exists(extractPath + "/theme.xml", false);

	if (!markerMatches || !themeReady)
	{
		LOG(LogInfo) << "EmbeddedTheme: extracting protected theme to cache (first run may take several minutes)...";
		Utils::FileSystem::deleteDirectoryFiles(extractPath + "/");
		Utils::FileSystem::createDirectory(extractPath);
		setHiddenDirectory(cacheRoot);

		if (!extractThemeArchive(tempZip, extractPath))
		{
			LOG(LogError) << "EmbeddedTheme: failed to extract protected theme.";
			Utils::FileSystem::deleteDirectoryFiles(extractPath + "/");
			Utils::FileSystem::removeFile(tempZip);
			return false;
		}

		Utils::FileSystem::writeAllText(markerPath, payload.identity);
		setHiddenDirectory(markerPath);
	}

	Utils::FileSystem::removeFile(tempZip);

	sRootPath = extractPath;
	sAvailable = true;

	applyThemeSetSelection();
	ensureDefaultSubsetSettings();

	LOG(LogInfo) << "EmbeddedTheme: ready at " << sRootPath;
	return true;
}

bool EmbeddedTheme::isAvailable()
{
	if (!sAvailable)
		initialize();

	return sAvailable;
}

bool EmbeddedTheme::isActiveThemeSet(const std::string& themeSet)
{
	return isAvailable() && isThemeSetAlias(themeSet);
}

std::string EmbeddedTheme::getRootPath()
{
	if (!isAvailable())
		return "";

	return sRootPath;
}

std::string EmbeddedTheme::getThemePath(const std::string& system)
{
	if (!isAvailable())
		return "";

	const std::string systemTheme = sRootPath + "/" + system + "/theme.xml";
	if (Utils::FileSystem::exists(systemTheme, false))
		return systemTheme;

	return sRootPath + "/theme.xml";
}

std::string EmbeddedTheme::getResourcesPath()
{
	if (!isAvailable())
		return "";

	const std::string resourcesPath = sRootPath + "/resources";
	return Utils::FileSystem::isDirectory(resourcesPath) ? resourcesPath : "";
}
