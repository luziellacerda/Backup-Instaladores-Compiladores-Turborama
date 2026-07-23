#include "GameSplash.h"

#include "Paths.h"
#include "Log.h"
#include "utils/FileSystemUtil.h"
#include <algorithm>

namespace
{
	const char* ENTRY_BASE_NAMES[] = { "entrada", "launch", "entry", nullptr };
	const char* EXIT_BASE_NAMES[] = { "saida", "exit", nullptr };
	const char* MEDIA_EXTENSIONS[] = { ".png", ".jpg", ".jpeg", ".svg", ".mp4", nullptr };

	std::string findMediaInFolder(const std::string& folder, const char** baseNames, const std::string& systemAlias = "")
	{
		if (folder.empty() || !Utils::FileSystem::isDirectory(folder))
			return "";

		for (int i = 0; baseNames[i] != nullptr; i++)
		{
			for (int j = 0; MEDIA_EXTENSIONS[j] != nullptr; j++)
			{
				std::string path = Utils::FileSystem::combine(folder, std::string(baseNames[i]) + MEDIA_EXTENSIONS[j]);
				if (Utils::FileSystem::exists(path))
					return path;
			}
		}

		if (!systemAlias.empty())
		{
			for (int j = 0; MEDIA_EXTENSIONS[j] != nullptr; j++)
			{
				std::string path = Utils::FileSystem::combine(folder, systemAlias + MEDIA_EXTENSIONS[j]);
				if (Utils::FileSystem::exists(path))
					return path;
			}
		}

		return "";
	}

	GameSplash::MediaType getMediaType(const std::string& path)
	{
		if (Utils::FileSystem::isVideo(path))
			return GameSplash::MediaType::VIDEO;

		return GameSplash::MediaType::IMAGE;
	}

	std::vector<std::string> getSearchRoots()
	{
		std::vector<std::string> roots;

		auto addRoot = [&roots](const std::string& path)
		{
			if (path.empty())
				return;

			std::string root = Utils::FileSystem::combine(path, "game-splashes");
			if (Utils::FileSystem::isDirectory(root) &&
				std::find(roots.cbegin(), roots.cend(), root) == roots.cend())
				roots.push_back(root);
		};

		addRoot(Paths::getUserEmulationStationPath());
		addRoot(Paths::getEmulationStationPath());
		addRoot(Paths::getHomePath());

		return roots;
	}

	std::string resolveInRoots(const std::string& systemName, const char** baseNames, bool useDefaultFolder)
	{
		for (const auto& root : getSearchRoots())
		{
			std::string systemFolder = Utils::FileSystem::combine(root, systemName);
			std::string path = findMediaInFolder(systemFolder, baseNames, systemName);
			if (!path.empty())
				return path;
		}

		if (!useDefaultFolder)
			return "";

		for (const auto& root : getSearchRoots())
		{
			std::string defaultFolder = Utils::FileSystem::combine(root, "default");
			std::string path = findMediaInFolder(defaultFolder, baseNames, systemName);
			if (!path.empty())
				return path;
		}

		return "";
	}
}

GameSplash::Media GameSplash::resolve(const std::string& systemName, Kind kind)
{
	const char** baseNames = (kind == Kind::ENTRY) ? ENTRY_BASE_NAMES : EXIT_BASE_NAMES;
	std::string path = resolveInRoots(systemName, baseNames, true);

	if (path.empty())
	{
		LOG(LogDebug) << "[GameSplash] nenhum arquivo para sistema '" << systemName << "' ("
			<< (kind == Kind::ENTRY ? "entrada" : "saida") << ")";
		return {};
	}

	LOG(LogInfo) << "[GameSplash] " << (kind == Kind::ENTRY ? "entrada" : "saida")
		<< " -> " << path << " (sistema: " << systemName << ")";

	return { path, getMediaType(path) };
}