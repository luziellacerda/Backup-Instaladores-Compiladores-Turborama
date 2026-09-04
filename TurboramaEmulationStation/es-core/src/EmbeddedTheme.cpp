#include "EmbeddedTheme.h"

#include "Log.h"
#include "Paths.h"
#include "Settings.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"
#include "utils/ZipFile.h"
#include "utils/md5.h"

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cstdint>
#include <ctime>
#include <fstream>
#include <limits>
#include <mutex>
#include <vector>

#if WIN32
#include <Windows.h>
#ifndef IDR_EMBEDDED_THEME
#define IDR_EMBEDDED_THEME 101
#endif
#endif

const char* EmbeddedTheme::THEME_SET_ID = "__turborama__";

static std::atomic<bool> sAvailable(false);
static bool sInitializationAttempted = false;
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
	static const std::uint64_t sDiskReserveBytes = 64ULL * 1024ULL * 1024ULL;
	static const double sMinimumCacheAgeSeconds = 24.0 * 60.0 * 60.0;

	struct EmbeddedPayload
	{
		const unsigned char* data = nullptr;
		size_t size = 0;
		size_t archiveOffset = 0;
		std::string identity;
	};

	struct CacheCandidate
	{
		std::string path;
		time_t lastModified = 0;
	};

	void reportProgress(const EmbeddedTheme::ProgressCallback& progressCallback, float progress)
	{
		if (progressCallback)
			progressCallback(std::max(0.0f, std::min(1.0f, progress)));
	}

	bool isLowerHexString(const std::string& value, size_t expectedLength)
	{
		return value.size() == expectedLength && std::all_of(value.begin(), value.end(), [](unsigned char ch) {
			return (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f');
		});
	}

	void setHiddenPath(const std::string& path)
	{
#if WIN32
		const std::wstring widePath = Utils::String::convertToWideString(path);
		const DWORD attributes = GetFileAttributesW(widePath.c_str());
		if (attributes != INVALID_FILE_ATTRIBUTES)
			SetFileAttributesW(widePath.c_str(), attributes | FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
#else
		(void)path;
#endif
	}

	std::string getCacheDirectory()
	{
		const std::string base = Utils::FileSystem::getCanonicalPath(Paths::getUserEmulationStationPath() + "/.runtime");
		if (!Utils::FileSystem::createDirectory(base) || !Utils::FileSystem::isDirectory(base))
		{
			LOG(LogError) << "EmbeddedTheme: unable to create the theme cache directory.";
			return "";
		}
		return base;
	}

#if WIN32
	class ScopedThemeCacheLock
	{
	public:
		ScopedThemeCacheLock(const std::string& cacheRoot, const EmbeddedTheme::ProgressCallback& progressCallback)
		{
			const std::string normalizedRoot = Utils::String::toLower(Utils::FileSystem::getCanonicalPath(cacheRoot));
			MD5 hash;
			hash.update(normalizedRoot.c_str(), static_cast<MD5::size_type>(normalizedRoot.size()));
			hash.finalize();

			const std::wstring mutexName = Utils::String::convertToWideString(
				"Local\\TurboRamaEmbeddedTheme-" + hash.hexdigest());
			mHandle = CreateMutexW(NULL, FALSE, mutexName.c_str());
			if (mHandle == NULL)
			{
				LOG(LogError) << "EmbeddedTheme: unable to create the cache lock (Windows error "
					<< GetLastError() << ").";
				return;
			}

			const DWORD waitIntervalMs = 1000;
			const DWORD waitTimeoutMs = 120000;
			DWORD elapsedMs = 0;
			while (elapsedMs < waitTimeoutMs)
			{
				const DWORD waitResult = WaitForSingleObject(mHandle, waitIntervalMs);
				if (waitResult == WAIT_OBJECT_0 || waitResult == WAIT_ABANDONED)
				{
					mOwned = true;
					return;
				}
				if (waitResult != WAIT_TIMEOUT)
				{
					LOG(LogError) << "EmbeddedTheme: unable to acquire the cache lock (Windows error "
						<< GetLastError() << ").";
					return;
				}

				elapsedMs += waitIntervalMs;
				reportProgress(progressCallback, 0.01f);
			}

			LOG(LogError) << "EmbeddedTheme: timed out waiting for another theme initialization to finish.";
		}

		~ScopedThemeCacheLock()
		{
			if (mOwned)
				ReleaseMutex(mHandle);
			if (mHandle != NULL)
				CloseHandle(mHandle);
		}

		bool acquired() const { return mOwned; }

	private:
		HANDLE mHandle = NULL;
		bool mOwned = false;
	};
#else
	class ScopedThemeCacheLock
	{
	public:
		ScopedThemeCacheLock(const std::string&, const EmbeddedTheme::ProgressCallback&) { }
		bool acquired() const { return true; }
	};
#endif

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
		payload.data = static_cast<const unsigned char*>(LockResource(loaded));
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
			return static_cast<char>(std::tolower(ch));
		});
		payload.archiveOffset = headerLength;
		return true;
#else
		(void)payload;
		return false;
#endif
	}

	bool treeContainsReparsePoint(const std::string& path)
	{
		if (!Utils::FileSystem::exists(path, false))
			return false;
		if (Utils::FileSystem::isSymlink(path))
			return true;
		if (!Utils::FileSystem::isDirectory(path))
			return false;

		for (const auto& child : Utils::FileSystem::getDirContent(path, false, true))
		{
			if (Utils::FileSystem::isSymlink(child))
				return true;
			if (Utils::FileSystem::isDirectory(child) && treeContainsReparsePoint(child))
				return true;
		}

		return false;
	}

	bool removeTreeWithoutFollowingReparsePoints(const std::string& path,
		const EmbeddedTheme::ProgressCallback& progressCallback, float progress, size_t& removedEntries)
	{
		if (!Utils::FileSystem::exists(path, false))
			return true;
		if (Utils::FileSystem::isSymlink(path))
			return false;

		if (!Utils::FileSystem::isDirectory(path))
		{
			const bool removed = Utils::FileSystem::removeFile(path);
			if (removed && ++removedEntries % 32 == 0)
				reportProgress(progressCallback, progress);
			return removed && !Utils::FileSystem::exists(path, false);
		}

		for (const auto& child : Utils::FileSystem::getDirContent(path, false, true))
		{
			// Re-check immediately before descending to reduce the chance of a
			// reparse-point swap between validation and deletion.
			if (Utils::FileSystem::isSymlink(child))
				return false;
			if (!removeTreeWithoutFollowingReparsePoints(child, progressCallback, progress, removedEntries))
				return false;
		}

		const bool removed = Utils::FileSystem::removeDirectory(path);
		if (removed && ++removedEntries % 32 == 0)
			reportProgress(progressCallback, progress);
		return removed && !Utils::FileSystem::exists(path, false);
	}

	bool safelyRemoveCachePath(const std::string& path,
		const EmbeddedTheme::ProgressCallback& progressCallback, float progress)
	{
		if (!Utils::FileSystem::exists(path, false))
			return true;
		if (treeContainsReparsePoint(path))
		{
			LOG(LogWarning) << "EmbeddedTheme: refusing to delete a cache containing a symlink or reparse point: " << path;
			return false;
		}

		size_t removedEntries = 0;
		return removeTreeWithoutFollowingReparsePoints(path, progressCallback, progress, removedEntries);
	}

	bool removeTemporaryArchive(const std::string& tempZip)
	{
		if (!Utils::FileSystem::exists(tempZip, false))
			return true;
		if (Utils::FileSystem::isSymlink(tempZip) || Utils::FileSystem::isDirectory(tempZip)
			|| !Utils::FileSystem::isRegularFile(tempZip))
		{
			LOG(LogError) << "EmbeddedTheme: refusing to replace an unsafe temporary archive path.";
			return false;
		}

		if (!Utils::FileSystem::removeFile(tempZip) || Utils::FileSystem::exists(tempZip, false))
		{
			LOG(LogError) << "EmbeddedTheme: unable to remove the previous temporary theme archive.";
			return false;
		}
		return true;
	}

	bool isValidCacheDirectory(const std::string& path, const std::string& directoryName, std::string* markerValue = nullptr)
	{
		if (!isLowerHexString(directoryName, 12) || !Utils::FileSystem::exists(path, false)
			|| Utils::FileSystem::isSymlink(path)
			|| !Utils::FileSystem::isDirectory(path))
			return false;

		const std::string markerPath = path + "/.payload";
		const std::string themePath = path + "/theme.xml";
		if (!Utils::FileSystem::exists(markerPath, false) || !Utils::FileSystem::exists(themePath, false)
			|| Utils::FileSystem::isSymlink(markerPath) || Utils::FileSystem::isSymlink(themePath)
			|| !Utils::FileSystem::isRegularFile(markerPath) || !Utils::FileSystem::isRegularFile(themePath)
			|| Utils::FileSystem::getFileSize(markerPath) != sPayloadIdentityLength)
			return false;

		const std::string marker = Utils::FileSystem::readAllText(markerPath);
		if (!isLowerHexString(marker, sPayloadIdentityLength) || marker.substr(0, 12) != directoryName)
			return false;

		if (markerValue != nullptr)
			*markerValue = marker;
		return true;
	}

	void pruneObsoleteThemeCaches(const std::string& cacheRoot, const std::string& currentIdentity,
		const EmbeddedTheme::ProgressCallback& progressCallback)
	{
		if (!isLowerHexString(currentIdentity, sPayloadIdentityLength))
			return;

		const std::string currentDirectory = currentIdentity.substr(0, 12);
		std::vector<CacheCandidate> candidates;
		for (const auto& entry : Utils::FileSystem::getDirContent(cacheRoot, false, true))
		{
			const std::string name = Utils::FileSystem::getFileName(entry);
			if (name == currentDirectory || !isValidCacheDirectory(entry, name))
				continue;

			CacheCandidate candidate;
			candidate.path = entry;
			candidate.lastModified = Utils::FileSystem::getFileModificationDate(entry).getTime();
			candidates.push_back(candidate);
		}

		std::sort(candidates.begin(), candidates.end(), [](const CacheCandidate& lhs, const CacheCandidate& rhs) {
			return lhs.lastModified > rhs.lastModified;
		});

		const time_t now = std::time(nullptr);
		size_t removed = 0;
		// Keep the newest previous cache as a rollback target and for a recently
		// started process that may still be using the prior executable.
		for (size_t index = 1; index < candidates.size(); index++)
		{
			const CacheCandidate& candidate = candidates[index];
			if (candidate.lastModified <= 0
				|| std::difftime(now, candidate.lastModified) < sMinimumCacheAgeSeconds)
				continue;

			if (safelyRemoveCachePath(candidate.path, progressCallback, 0.02f))
				removed++;
			else
				LOG(LogWarning) << "EmbeddedTheme: unable to safely remove obsolete cache '" << candidate.path << "'.";
		}

		if (removed > 0)
			LOG(LogInfo) << "EmbeddedTheme: removed " << removed << " obsolete theme cache(s).";
	}

	bool findCachedTheme(const std::string& cacheRoot, const std::string& payloadIdentity, std::string& cachedPath)
	{
		if (!Utils::FileSystem::isDirectory(cacheRoot) || payloadIdentity.size() < 12)
			return false;

		const std::string directoryName = payloadIdentity.substr(0, 12);
		cachedPath = Utils::FileSystem::getCanonicalPath(cacheRoot + "/" + directoryName);
		std::string marker;
		return isValidCacheDirectory(cachedPath, directoryName, &marker) && marker == payloadIdentity;
	}

	bool hasEnoughFreeSpace(const std::string& cacheRoot, std::uint64_t contentBytes, const char* phase)
	{
		if (contentBytes > std::numeric_limits<std::uint64_t>::max() - sDiskReserveBytes)
		{
			LOG(LogError) << "EmbeddedTheme: theme size overflow while checking disk space for " << phase << ".";
			return false;
		}

#if WIN32
		ULARGE_INTEGER freeBytesAvailableToCaller;
		if (!GetDiskFreeSpaceExW(Utils::String::convertToWideString(cacheRoot).c_str(),
			&freeBytesAvailableToCaller, NULL, NULL))
		{
			LOG(LogWarning) << "EmbeddedTheme: unable to determine free disk space for " << phase << ".";
			return true;
		}

		const std::uint64_t requiredBytes = contentBytes + sDiskReserveBytes;
		if (freeBytesAvailableToCaller.QuadPart < requiredBytes)
		{
			LOG(LogError) << "EmbeddedTheme: insufficient disk space for " << phase << " (free "
				<< freeBytesAvailableToCaller.QuadPart << " bytes, required at least " << requiredBytes << " bytes).";
			return false;
		}
#else
		(void)cacheRoot;
		(void)phase;
#endif
		return true;
	}

	bool decryptResourceToFile(const EmbeddedPayload& payload, const std::string& tempZip,
		const EmbeddedTheme::ProgressCallback& progressCallback)
	{
#if WIN32
		if (payload.data == nullptr || payload.archiveOffset >= payload.size)
			return false;
		const size_t archiveSize = payload.size - payload.archiveOffset;

		std::ofstream output(tempZip, std::ios::binary | std::ios::trunc);
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

			output.write(reinterpret_cast<const char*>(chunk.data()), static_cast<std::streamsize>(length));
			if (!output.good())
			{
				output.close();
				LOG(LogError) << "EmbeddedTheme: failed to write temporary theme archive.";
				Utils::FileSystem::removeFile(tempZip);
				return false;
			}
			md5.update(reinterpret_cast<const char*>(chunk.data()), static_cast<MD5::size_type>(length));

			offset += length;
			reportProgress(progressCallback, 0.05f + 0.40f * static_cast<float>(offset) / static_cast<float>(archiveSize));
		}

		output.close();
		if (!output.good())
		{
			LOG(LogError) << "EmbeddedTheme: failed to finalize the temporary theme archive.";
			Utils::FileSystem::removeFile(tempZip);
			return false;
		}

		md5.finalize();
		if (md5.hexdigest() != payload.identity)
		{
			LOG(LogError) << "EmbeddedTheme: payload identity does not match its archive; rebuild the executable.";
			Utils::FileSystem::removeFile(tempZip);
			return false;
		}

		if (!Utils::FileSystem::exists(tempZip, false)
			|| Utils::FileSystem::getFileSize(tempZip) != static_cast<unsigned long long>(archiveSize))
		{
			LOG(LogError) << "EmbeddedTheme: temporary theme archive is incomplete.";
			Utils::FileSystem::removeFile(tempZip);
			return false;
		}

		LOG(LogInfo) << "EmbeddedTheme: decrypted " << archiveSize << " bytes from executable.";
		return true;
#else
		(void)payload;
		(void)tempZip;
		(void)progressCallback;
		return false;
#endif
	}

	bool isPathInside(const std::string& parentPath, const std::string& childPath)
	{
		std::string parent = Utils::FileSystem::getCanonicalPath(parentPath);
		std::string child = Utils::FileSystem::getCanonicalPath(childPath);
#if WIN32
		parent = Utils::String::toLower(parent);
		child = Utils::String::toLower(child);
#endif
		if (!Utils::String::endsWith(parent, "/"))
			parent += "/";
		return Utils::String::startsWith(child, parent);
	}

	bool extractThemeArchive(const std::string& tempZip, const std::string& targetPath,
		const std::string& cacheRoot, const EmbeddedTheme::ProgressCallback& progressCallback)
	{
		Utils::Zip::ZipFile archive;
		if (!archive.load(tempZip))
		{
			LOG(LogError) << "EmbeddedTheme: unable to open embedded theme archive.";
			return false;
		}

		const auto members = archive.infolist();
		std::uint64_t uncompressedBytes = 0;
		for (const auto& member : members)
		{
			const std::uint64_t memberSize = static_cast<std::uint64_t>(member.file_size);
			if (memberSize > std::numeric_limits<std::uint64_t>::max() - uncompressedBytes)
			{
				LOG(LogError) << "EmbeddedTheme: theme archive size overflow.";
				return false;
			}
			uncompressedBytes += memberSize;
		}

		// The encrypted ZIP is already on disk here, so only the expanded files
		// plus a small operating reserve are still required.
		if (!hasEnoughFreeSpace(cacheRoot, uncompressedBytes, "theme extraction"))
			return false;

		if (!Utils::FileSystem::createDirectory(targetPath) || Utils::FileSystem::isSymlink(targetPath))
		{
			LOG(LogError) << "EmbeddedTheme: unable to create a safe extraction directory.";
			return false;
		}

		size_t extracted = 0;
		for (size_t index = 0; index < members.size(); index++)
		{
			const std::string& member = members[index].filename;
			if (!member.empty() && !Utils::String::endsWith(member, "/"))
			{
				const std::string fullPath = Utils::FileSystem::getCanonicalPath(targetPath + "/" + member);
				if (!isPathInside(targetPath, fullPath))
				{
					LOG(LogError) << "EmbeddedTheme: refusing an archive member outside the theme cache.";
					return false;
				}

				const std::string parentPath = Utils::FileSystem::getParent(fullPath);
				if (!parentPath.empty()
					&& (!Utils::FileSystem::createDirectory(parentPath) || Utils::FileSystem::isSymlink(parentPath)))
				{
					LOG(LogError) << "EmbeddedTheme: unable to create a safe archive member directory.";
					return false;
				}

				if (!archive.extract(member, targetPath, false))
				{
					LOG(LogError) << "EmbeddedTheme: failed extracting '" << member << "'.";
					return false;
				}
				extracted++;
			}

			if (index % 10 == 0 || index + 1 == members.size())
			{
				const float ratio = members.empty() ? 1.0f
					: static_cast<float>(index + 1) / static_cast<float>(members.size());
				reportProgress(progressCallback, 0.50f + 0.48f * ratio);
			}
		}

		LOG(LogInfo) << "EmbeddedTheme: extracted " << extracted << " files.";
		// ZipFile writes outside FileSystemUtil, so discard any negative lookups
		// collected while validating an incomplete cache before extraction.
		Utils::FileSystem::FileSystemCache::reset();
		const std::string themePath = targetPath + "/theme.xml";
		// Refresh a possible negative lookup performed before extraction.
		return Utils::FileSystem::exists(themePath, false)
			&& !Utils::FileSystem::isSymlink(themePath)
			&& Utils::FileSystem::isRegularFile(themePath);
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

	void publishTheme(const std::string& rootPath, bool cached)
	{
		sRootPath = rootPath;
		applyThemeSetSelection();
		ensureDefaultSubsetSettings();
		sAvailable.store(true, std::memory_order_release);

		LOG(LogInfo) << "EmbeddedTheme: ready at " << sRootPath << (cached ? " (cached)" : "");
	}
}

bool EmbeddedTheme::initialize(const ProgressCallback& progressCallback)
{
	if (sAvailable.load(std::memory_order_acquire))
		return true;

	std::lock_guard<std::mutex> lock(sInitMutex);
	if (sAvailable.load(std::memory_order_relaxed))
		return true;
	if (sInitializationAttempted)
		return false;
	sInitializationAttempted = true;
	reportProgress(progressCallback, 0.0f);

	EmbeddedPayload payload;
	if (!loadEmbeddedPayload(payload))
		return false;

	const std::string cacheRoot = getCacheDirectory();
	if (cacheRoot.empty() || Utils::FileSystem::isSymlink(cacheRoot))
	{
		LOG(LogError) << "EmbeddedTheme: refusing an unsafe theme cache directory.";
		return false;
	}

	ScopedThemeCacheLock processLock(cacheRoot, progressCallback);
	if (!processLock.acquired())
		return false;

	const std::string tempZip = Utils::FileSystem::getCanonicalPath(cacheRoot + "/.theme.pack.zip");
	if (!removeTemporaryArchive(tempZip))
		return false;

	pruneObsoleteThemeCaches(cacheRoot, payload.identity, progressCallback);

	std::string extractPath;
	if (findCachedTheme(cacheRoot, payload.identity, extractPath))
	{
		publishTheme(extractPath, true);
		reportProgress(progressCallback, 1.0f);
		return true;
	}

	extractPath = Utils::FileSystem::getCanonicalPath(cacheRoot + "/" + payload.identity.substr(0, 12));
	if (Utils::FileSystem::exists(extractPath, false)
		&& !safelyRemoveCachePath(extractPath, progressCallback, 0.03f))
	{
		LOG(LogError) << "EmbeddedTheme: unable to safely clear the incomplete current theme cache.";
		return false;
	}

	const size_t archiveSize = payload.size - payload.archiveOffset;
	if (!hasEnoughFreeSpace(cacheRoot, static_cast<std::uint64_t>(archiveSize), "theme archive creation"))
		return false;

	if (!decryptResourceToFile(payload, tempZip, progressCallback))
		return false;

	setHiddenPath(cacheRoot);
	LOG(LogInfo) << "EmbeddedTheme: extracting protected theme to cache (first run may take several minutes)...";
	if (!extractThemeArchive(tempZip, extractPath, cacheRoot, progressCallback))
	{
		LOG(LogError) << "EmbeddedTheme: failed to extract protected theme.";
		if (!safelyRemoveCachePath(extractPath, progressCallback, 0.98f))
			LOG(LogWarning) << "EmbeddedTheme: unable to safely clean the incomplete theme cache.";
		removeTemporaryArchive(tempZip);
		return false;
	}

	const std::string markerPath = extractPath + "/.payload";
	Utils::FileSystem::writeAllText(markerPath, payload.identity);
	setHiddenPath(markerPath);
	if (Utils::FileSystem::isSymlink(markerPath)
		|| !Utils::FileSystem::isRegularFile(markerPath)
		|| Utils::FileSystem::getFileSize(markerPath) != sPayloadIdentityLength
		|| Utils::FileSystem::readAllText(markerPath) != payload.identity)
	{
		LOG(LogError) << "EmbeddedTheme: failed to verify the extracted theme marker.";
		if (!safelyRemoveCachePath(extractPath, progressCallback, 0.98f))
			LOG(LogWarning) << "EmbeddedTheme: unable to safely clean the unverified theme cache.";
		removeTemporaryArchive(tempZip);
		return false;
	}

	if (!removeTemporaryArchive(tempZip))
		LOG(LogWarning) << "EmbeddedTheme: the temporary theme archive will be removed on the next start.";

	publishTheme(extractPath, false);
	reportProgress(progressCallback, 1.0f);
	return true;
}

bool EmbeddedTheme::isAvailable()
{
	return sAvailable.load(std::memory_order_acquire);
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
