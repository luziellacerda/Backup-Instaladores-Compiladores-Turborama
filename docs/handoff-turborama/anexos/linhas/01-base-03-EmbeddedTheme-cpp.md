# 01-base: TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Leitura e extração do tema embutido, cache identificado pelo conteúdo, validação de caminhos, trava e progresso de inicialização.

- Antes: `0e02780b761cb488c591416d2986130efcc166dd`.
- Depois: `76b214874973fe24017823401216896f3d7a6f40`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 9, depois 9

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L9) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L9)

```text
ANTES | DEPOIS |   CÓDIGO
    9 |      9 |   #include "utils/md5.h"
   10 |     10 |   
   11 |     11 |   #include <algorithm>
      |     12 | + #include <atomic>
   12 |     13 |   #include <cctype>
      |     14 | + #include <cstdint>
      |     15 | + #include <ctime>
   13 |     16 |   #include <fstream>
      |     17 | + #include <limits>
   14 |     18 |   #include <mutex>
   15 |     19 |   #include <vector>
   16 |     20 |   
```

## Trecho 2: antes 23, depois 27

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L23) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L27)

```text
ANTES | DEPOIS |   CÓDIGO
   23 |     27 |   
   24 |     28 |   const char* EmbeddedTheme::THEME_SET_ID = "__turborama__";
   25 |     29 |   
   26 |        | - static bool sAvailable = false;
      |     30 | + static std::atomic<bool> sAvailable(false);
      |     31 | + static bool sInitializationAttempted = false;
   27 |     32 |   static std::string sRootPath;
   28 |     33 |   static std::mutex sInitMutex;
   29 |     34 |   
```

## Trecho 3: antes 38, depois 43

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L38) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L43)

```text
ANTES | DEPOIS |   CÓDIGO
   38 |     43 |   	static const size_t sDecryptChunkSize = 4 * 1024 * 1024;
   39 |     44 |   	static const char sPayloadHeader[] = "TRTHEME1:";
   40 |     45 |   	static const size_t sPayloadIdentityLength = 32;
      |     46 | + 	static const std::uint64_t sDiskReserveBytes = 64ULL * 1024ULL * 1024ULL;
      |     47 | + 	static const double sMinimumCacheAgeSeconds = 24.0 * 60.0 * 60.0;
   41 |     48 |   
   42 |     49 |   	struct EmbeddedPayload
   43 |     50 |   	{
```

## Trecho 4: antes 47, depois 54

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L47) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L54)

```text
ANTES | DEPOIS |   CÓDIGO
   47 |     54 |   		std::string identity;
   48 |     55 |   	};
   49 |     56 |   
   50 |        | - 	void setHiddenDirectory(const std::string& path)
      |     57 | + 	struct CacheCandidate
      |     58 | + 	{
      |     59 | + 		std::string path;
      |     60 | + 		time_t lastModified = 0;
      |     61 | + 	};
      |     62 | + 
      |     63 | + 	void reportProgress(const EmbeddedTheme::ProgressCallback& progressCallback, float progress)
      |     64 | + 	{
      |     65 | + 		if (progressCallback)
      |     66 | + 			progressCallback(std::max(0.0f, std::min(1.0f, progress)));
      |     67 | + 	}
      |     68 | + 
      |     69 | + 	bool isLowerHexString(const std::string& value, size_t expectedLength)
      |     70 | + 	{
      |     71 | + 		return value.size() == expectedLength && std::all_of(value.begin(), value.end(), [](unsigned char ch) {
      |     72 | + 			return (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f');
      |     73 | + 		});
      |     74 | + 	}
      |     75 | + 
      |     76 | + 	void setHiddenPath(const std::string& path)
   51 |     77 |   	{
   52 |     78 |   #if WIN32
   53 |        | - 		SetFileAttributesA(path.c_str(), FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
      |     79 | + 		const std::wstring widePath = Utils::String::convertToWideString(path);
      |     80 | + 		const DWORD attributes = GetFileAttributesW(widePath.c_str());
      |     81 | + 		if (attributes != INVALID_FILE_ATTRIBUTES)
      |     82 | + 			SetFileAttributesW(widePath.c_str(), attributes | FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
      |     83 | + #else
      |     84 | + 		(void)path;
   54 |     85 |   #endif
   55 |     86 |   	}
   56 |     87 |   
   57 |     88 |   	std::string getCacheDirectory()
   58 |     89 |   	{
   59 |        | - 		std::string base = Utils::FileSystem::getCanonicalPath(Paths::getUserEmulationStationPath() + "/.runtime");
   60 |        | - 		Utils::FileSystem::createDirectory(base);
      |     90 | + 		const std::string base = Utils::FileSystem::getCanonicalPath(Paths::getUserEmulationStationPath() + "/.runtime");
      |     91 | + 		if (!Utils::FileSystem::createDirectory(base) || !Utils::FileSystem::isDirectory(base))
      |     92 | + 		{
      |     93 | + 			LOG(LogError) << "EmbeddedTheme: unable to create the theme cache directory.";
      |     94 | + 			return "";
      |     95 | + 		}
   61 |     96 |   		return base;
   62 |     97 |   	}
   63 |     98 |   
      |     99 | + #if WIN32
      |    100 | + 	class ScopedThemeCacheLock
      |    101 | + 	{
      |    102 | + 	public:
      |    103 | + 		ScopedThemeCacheLock(const std::string& cacheRoot, const EmbeddedTheme::ProgressCallback& progressCallback)
      |    104 | + 		{
      |    105 | + 			const std::string normalizedRoot = Utils::String::toLower(Utils::FileSystem::getCanonicalPath(cacheRoot));
      |    106 | + 			MD5 hash;
      |    107 | + 			hash.update(normalizedRoot.c_str(), static_cast<MD5::size_type>(normalizedRoot.size()));
      |    108 | + 			hash.finalize();
      |    109 | + 
      |    110 | + 			const std::wstring mutexName = Utils::String::convertToWideString(
      |    111 | + 				"Local\\TurboRamaEmbeddedTheme-" + hash.hexdigest());
      |    112 | + 			mHandle = CreateMutexW(NULL, FALSE, mutexName.c_str());
      |    113 | + 			if (mHandle == NULL)
      |    114 | + 			{
      |    115 | + 				LOG(LogError) << "EmbeddedTheme: unable to create the cache lock (Windows error "
      |    116 | + 					<< GetLastError() << ").";
      |    117 | + 				return;
      |    118 | + 			}
      |    119 | + 
      |    120 | + 			const DWORD waitIntervalMs = 1000;
      |    121 | + 			const DWORD waitTimeoutMs = 120000;
      |    122 | + 			DWORD elapsedMs = 0;
      |    123 | + 			while (elapsedMs < waitTimeoutMs)
      |    124 | + 			{
      |    125 | + 				const DWORD waitResult = WaitForSingleObject(mHandle, waitIntervalMs);
      |    126 | + 				if (waitResult == WAIT_OBJECT_0 || waitResult == WAIT_ABANDONED)
      |    127 | + 				{
      |    128 | + 					mOwned = true;
      |    129 | + 					return;
      |    130 | + 				}
      |    131 | + 				if (waitResult != WAIT_TIMEOUT)
      |    132 | + 				{
      |    133 | + 					LOG(LogError) << "EmbeddedTheme: unable to acquire the cache lock (Windows error "
      |    134 | + 						<< GetLastError() << ").";
      |    135 | + 					return;
      |    136 | + 				}
      |    137 | + 
      |    138 | + 				elapsedMs += waitIntervalMs;
      |    139 | + 				reportProgress(progressCallback, 0.01f);
      |    140 | + 			}
      |    141 | + 
      |    142 | + 			LOG(LogError) << "EmbeddedTheme: timed out waiting for another theme initialization to finish.";
      |    143 | + 		}
      |    144 | + 
      |    145 | + 		~ScopedThemeCacheLock()
      |    146 | + 		{
      |    147 | + 			if (mOwned)
      |    148 | + 				ReleaseMutex(mHandle);
      |    149 | + 			if (mHandle != NULL)
      |    150 | + 				CloseHandle(mHandle);
      |    151 | + 		}
      |    152 | + 
      |    153 | + 		bool acquired() const { return mOwned; }
      |    154 | + 
      |    155 | + 	private:
      |    156 | + 		HANDLE mHandle = NULL;
      |    157 | + 		bool mOwned = false;
      |    158 | + 	};
      |    159 | + #else
      |    160 | + 	class ScopedThemeCacheLock
      |    161 | + 	{
      |    162 | + 	public:
      |    163 | + 		ScopedThemeCacheLock(const std::string&, const EmbeddedTheme::ProgressCallback&) { }
      |    164 | + 		bool acquired() const { return true; }
      |    165 | + 	};
      |    166 | + #endif
      |    167 | + 
   64 |    168 |   	bool loadEmbeddedPayload(EmbeddedPayload& payload)
   65 |    169 |   	{
   66 |    170 |   #if WIN32
```

## Trecho 5: antes 80, depois 184

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L80) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L184)

```text
ANTES | DEPOIS |   CÓDIGO
   80 |    184 |   		}
   81 |    185 |   
   82 |    186 |   		payload.size = SizeofResource(module, resource);
   83 |        | - 		payload.data = (const unsigned char*)LockResource(loaded);
      |    187 | + 		payload.data = static_cast<const unsigned char*>(LockResource(loaded));
   84 |    188 |   		const size_t prefixLength = sizeof(sPayloadHeader) - 1;
   85 |    189 |   		const size_t headerLength = prefixLength + sPayloadIdentityLength + 1;
   86 |    190 |   		if (payload.data == NULL || payload.size <= headerLength)
```

## Trecho 6: antes 104, depois 208

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L104) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L208)

```text
ANTES | DEPOIS |   CÓDIGO
  104 |    208 |   			return false;
  105 |    209 |   		}
  106 |    210 |   		std::transform(payload.identity.begin(), payload.identity.end(), payload.identity.begin(), [](unsigned char ch) {
  107 |        | - 			return (char)std::tolower(ch);
      |    211 | + 			return static_cast<char>(std::tolower(ch));
  108 |    212 |   		});
  109 |    213 |   		payload.archiveOffset = headerLength;
  110 |    214 |   		return true;
```

## Trecho 7: antes 114, depois 218

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L114) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L218)

```text
ANTES | DEPOIS |   CÓDIGO
  114 |    218 |   #endif
  115 |    219 |   	}
  116 |    220 |   
  117 |        | - 	bool decryptResourceToFile(const EmbeddedPayload& payload, size_t& payloadSize)
      |    221 | + 	bool treeContainsReparsePoint(const std::string& path)
      |    222 | + 	{
      |    223 | + 		if (!Utils::FileSystem::exists(path, false))
      |    224 | + 			return false;
      |    225 | + 		if (Utils::FileSystem::isSymlink(path))
      |    226 | + 			return true;
      |    227 | + 		if (!Utils::FileSystem::isDirectory(path))
      |    228 | + 			return false;
      |    229 | + 
      |    230 | + 		for (const auto& child : Utils::FileSystem::getDirContent(path, false, true))
      |    231 | + 		{
      |    232 | + 			if (Utils::FileSystem::isSymlink(child))
      |    233 | + 				return true;
      |    234 | + 			if (Utils::FileSystem::isDirectory(child) && treeContainsReparsePoint(child))
      |    235 | + 				return true;
      |    236 | + 		}
      |    237 | + 
      |    238 | + 		return false;
      |    239 | + 	}
      |    240 | + 
      |    241 | + 	bool removeTreeWithoutFollowingReparsePoints(const std::string& path,
      |    242 | + 		const EmbeddedTheme::ProgressCallback& progressCallback, float progress, size_t& removedEntries)
      |    243 | + 	{
      |    244 | + 		if (!Utils::FileSystem::exists(path, false))
      |    245 | + 			return true;
      |    246 | + 		if (Utils::FileSystem::isSymlink(path))
      |    247 | + 			return false;
      |    248 | + 
      |    249 | + 		if (!Utils::FileSystem::isDirectory(path))
      |    250 | + 		{
      |    251 | + 			const bool removed = Utils::FileSystem::removeFile(path);
      |    252 | + 			if (removed && ++removedEntries % 32 == 0)
      |    253 | + 				reportProgress(progressCallback, progress);
      |    254 | + 			return removed && !Utils::FileSystem::exists(path, false);
      |    255 | + 		}
      |    256 | + 
      |    257 | + 		for (const auto& child : Utils::FileSystem::getDirContent(path, false, true))
      |    258 | + 		{
      |    259 | + 			// Re-check immediately before descending to reduce the chance of a
      |    260 | + 			// reparse-point swap between validation and deletion.
      |    261 | + 			if (Utils::FileSystem::isSymlink(child))
      |    262 | + 				return false;
      |    263 | + 			if (!removeTreeWithoutFollowingReparsePoints(child, progressCallback, progress, removedEntries))
      |    264 | + 				return false;
      |    265 | + 		}
      |    266 | + 
      |    267 | + 		const bool removed = Utils::FileSystem::removeDirectory(path);
      |    268 | + 		if (removed && ++removedEntries % 32 == 0)
      |    269 | + 			reportProgress(progressCallback, progress);
      |    270 | + 		return removed && !Utils::FileSystem::exists(path, false);
      |    271 | + 	}
      |    272 | + 
      |    273 | + 	bool safelyRemoveCachePath(const std::string& path,
      |    274 | + 		const EmbeddedTheme::ProgressCallback& progressCallback, float progress)
      |    275 | + 	{
      |    276 | + 		if (!Utils::FileSystem::exists(path, false))
      |    277 | + 			return true;
      |    278 | + 		if (treeContainsReparsePoint(path))
      |    279 | + 		{
      |    280 | + 			LOG(LogWarning) << "EmbeddedTheme: refusing to delete a cache containing a symlink or reparse point: " << path;
      |    281 | + 			return false;
      |    282 | + 		}
      |    283 | + 
      |    284 | + 		size_t removedEntries = 0;
      |    285 | + 		return removeTreeWithoutFollowingReparsePoints(path, progressCallback, progress, removedEntries);
      |    286 | + 	}
      |    287 | + 
      |    288 | + 	bool removeTemporaryArchive(const std::string& tempZip)
      |    289 | + 	{
      |    290 | + 		if (!Utils::FileSystem::exists(tempZip, false))
      |    291 | + 			return true;
      |    292 | + 		if (Utils::FileSystem::isSymlink(tempZip) || Utils::FileSystem::isDirectory(tempZip)
      |    293 | + 			|| !Utils::FileSystem::isRegularFile(tempZip))
      |    294 | + 		{
      |    295 | + 			LOG(LogError) << "EmbeddedTheme: refusing to replace an unsafe temporary archive path.";
      |    296 | + 			return false;
      |    297 | + 		}
      |    298 | + 
      |    299 | + 		if (!Utils::FileSystem::removeFile(tempZip) || Utils::FileSystem::exists(tempZip, false))
      |    300 | + 		{
      |    301 | + 			LOG(LogError) << "EmbeddedTheme: unable to remove the previous temporary theme archive.";
      |    302 | + 			return false;
      |    303 | + 		}
      |    304 | + 		return true;
      |    305 | + 	}
      |    306 | + 
      |    307 | + 	bool isValidCacheDirectory(const std::string& path, const std::string& directoryName, std::string* markerValue = nullptr)
      |    308 | + 	{
      |    309 | + 		if (!isLowerHexString(directoryName, 12) || !Utils::FileSystem::exists(path, false)
      |    310 | + 			|| Utils::FileSystem::isSymlink(path)
      |    311 | + 			|| !Utils::FileSystem::isDirectory(path))
      |    312 | + 			return false;
      |    313 | + 
      |    314 | + 		const std::string markerPath = path + "/.payload";
      |    315 | + 		const std::string themePath = path + "/theme.xml";
      |    316 | + 		if (!Utils::FileSystem::exists(markerPath, false) || !Utils::FileSystem::exists(themePath, false)
      |    317 | + 			|| Utils::FileSystem::isSymlink(markerPath) || Utils::FileSystem::isSymlink(themePath)
      |    318 | + 			|| !Utils::FileSystem::isRegularFile(markerPath) || !Utils::FileSystem::isRegularFile(themePath)
      |    319 | + 			|| Utils::FileSystem::getFileSize(markerPath) != sPayloadIdentityLength)
      |    320 | + 			return false;
      |    321 | + 
      |    322 | + 		const std::string marker = Utils::FileSystem::readAllText(markerPath);
      |    323 | + 		if (!isLowerHexString(marker, sPayloadIdentityLength) || marker.substr(0, 12) != directoryName)
      |    324 | + 			return false;
      |    325 | + 
      |    326 | + 		if (markerValue != nullptr)
      |    327 | + 			*markerValue = marker;
      |    328 | + 		return true;
      |    329 | + 	}
      |    330 | + 
      |    331 | + 	void pruneObsoleteThemeCaches(const std::string& cacheRoot, const std::string& currentIdentity,
      |    332 | + 		const EmbeddedTheme::ProgressCallback& progressCallback)
      |    333 | + 	{
      |    334 | + 		if (!isLowerHexString(currentIdentity, sPayloadIdentityLength))
      |    335 | + 			return;
      |    336 | + 
      |    337 | + 		const std::string currentDirectory = currentIdentity.substr(0, 12);
      |    338 | + 		std::vector<CacheCandidate> candidates;
      |    339 | + 		for (const auto& entry : Utils::FileSystem::getDirContent(cacheRoot, false, true))
      |    340 | + 		{
      |    341 | + 			const std::string name = Utils::FileSystem::getFileName(entry);
      |    342 | + 			if (name == currentDirectory || !isValidCacheDirectory(entry, name))
      |    343 | + 				continue;
      |    344 | + 
      |    345 | + 			CacheCandidate candidate;
      |    346 | + 			candidate.path = entry;
      |    347 | + 			candidate.lastModified = Utils::FileSystem::getFileModificationDate(entry).getTime();
      |    348 | + 			candidates.push_back(candidate);
      |    349 | + 		}
      |    350 | + 
      |    351 | + 		std::sort(candidates.begin(), candidates.end(), [](const CacheCandidate& lhs, const CacheCandidate& rhs) {
      |    352 | + 			return lhs.lastModified > rhs.lastModified;
      |    353 | + 		});
      |    354 | + 
      |    355 | + 		const time_t now = std::time(nullptr);
      |    356 | + 		size_t removed = 0;
      |    357 | + 		// Keep the newest previous cache as a rollback target and for a recently
      |    358 | + 		// started process that may still be using the prior executable.
      |    359 | + 		for (size_t index = 1; index < candidates.size(); index++)
      |    360 | + 		{
      |    361 | + 			const CacheCandidate& candidate = candidates[index];
      |    362 | + 			if (candidate.lastModified <= 0
      |    363 | + 				|| std::difftime(now, candidate.lastModified) < sMinimumCacheAgeSeconds)
      |    364 | + 				continue;
      |    365 | + 
      |    366 | + 			if (safelyRemoveCachePath(candidate.path, progressCallback, 0.02f))
      |    367 | + 				removed++;
      |    368 | + 			else
      |    369 | + 				LOG(LogWarning) << "EmbeddedTheme: unable to safely remove obsolete cache '" << candidate.path << "'.";
      |    370 | + 		}
      |    371 | + 
      |    372 | + 		if (removed > 0)
      |    373 | + 			LOG(LogInfo) << "EmbeddedTheme: removed " << removed << " obsolete theme cache(s).";
      |    374 | + 	}
      |    375 | + 
      |    376 | + 	bool findCachedTheme(const std::string& cacheRoot, const std::string& payloadIdentity, std::string& cachedPath)
      |    377 | + 	{
      |    378 | + 		if (!Utils::FileSystem::isDirectory(cacheRoot) || payloadIdentity.size() < 12)
      |    379 | + 			return false;
      |    380 | + 
      |    381 | + 		const std::string directoryName = payloadIdentity.substr(0, 12);
      |    382 | + 		cachedPath = Utils::FileSystem::getCanonicalPath(cacheRoot + "/" + directoryName);
      |    383 | + 		std::string marker;
      |    384 | + 		return isValidCacheDirectory(cachedPath, directoryName, &marker) && marker == payloadIdentity;
      |    385 | + 	}
      |    386 | + 
      |    387 | + 	bool hasEnoughFreeSpace(const std::string& cacheRoot, std::uint64_t contentBytes, const char* phase)
  118 |    388 |   	{
  119 |        | - 		payloadSize = 0;
      |    389 | + 		if (contentBytes > std::numeric_limits<std::uint64_t>::max() - sDiskReserveBytes)
      |    390 | + 		{
      |    391 | + 			LOG(LogError) << "EmbeddedTheme: theme size overflow while checking disk space for " << phase << ".";
      |    392 | + 			return false;
      |    393 | + 		}
      |    394 | + 
      |    395 | + #if WIN32
      |    396 | + 		ULARGE_INTEGER freeBytesAvailableToCaller;
      |    397 | + 		if (!GetDiskFreeSpaceExW(Utils::String::convertToWideString(cacheRoot).c_str(),
      |    398 | + 			&freeBytesAvailableToCaller, NULL, NULL))
      |    399 | + 		{
      |    400 | + 			LOG(LogWarning) << "EmbeddedTheme: unable to determine free disk space for " << phase << ".";
      |    401 | + 			return true;
      |    402 | + 		}
  120 |    403 |   
      |    404 | + 		const std::uint64_t requiredBytes = contentBytes + sDiskReserveBytes;
      |    405 | + 		if (freeBytesAvailableToCaller.QuadPart < requiredBytes)
      |    406 | + 		{
      |    407 | + 			LOG(LogError) << "EmbeddedTheme: insufficient disk space for " << phase << " (free "
      |    408 | + 				<< freeBytesAvailableToCaller.QuadPart << " bytes, required at least " << requiredBytes << " bytes).";
      |    409 | + 			return false;
      |    410 | + 		}
      |    411 | + #else
      |    412 | + 		(void)cacheRoot;
      |    413 | + 		(void)phase;
      |    414 | + #endif
      |    415 | + 		return true;
      |    416 | + 	}
      |    417 | + 
      |    418 | + 	bool decryptResourceToFile(const EmbeddedPayload& payload, const std::string& tempZip,
      |    419 | + 		const EmbeddedTheme::ProgressCallback& progressCallback)
      |    420 | + 	{
  121 |    421 |   #if WIN32
  122 |    422 |   		if (payload.data == nullptr || payload.archiveOffset >= payload.size)
  123 |    423 |   			return false;
  124 |    424 |   		const size_t archiveSize = payload.size - payload.archiveOffset;
  125 |    425 |   
  126 |        | - 		const std::string cacheRoot = getCacheDirectory();
  127 |        | - 		const std::string tempZip = Utils::FileSystem::getCanonicalPath(cacheRoot + "/.theme.pack.zip");
  128 |        | - 
  129 |        | - 		std::ofstream output(tempZip, std::ios::binary);
      |    426 | + 		std::ofstream output(tempZip, std::ios::binary | std::ios::trunc);
  130 |    427 |   		if (!output.is_open())
  131 |    428 |   		{
  132 |    429 |   			LOG(LogError) << "EmbeddedTheme: failed to open temporary theme archive.";
```

## Trecho 8: antes 145, depois 442

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L145) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L442)

```text
ANTES | DEPOIS |   CÓDIGO
  145 |    442 |   			for (size_t i = 0; i < length; i++)
  146 |    443 |   				chunk[i] = payload.data[payload.archiveOffset + offset + i] ^ sKey[(offset + i) % sKeyLen];
  147 |    444 |   
  148 |        | - 			output.write(reinterpret_cast<const char*>(chunk.data()), length);
  149 |        | - 			md5.update(reinterpret_cast<const char*>(chunk.data()), (MD5::size_type)length);
      |    445 | + 			output.write(reinterpret_cast<const char*>(chunk.data()), static_cast<std::streamsize>(length));
      |    446 | + 			if (!output.good())
      |    447 | + 			{
      |    448 | + 				output.close();
      |    449 | + 				LOG(LogError) << "EmbeddedTheme: failed to write temporary theme archive.";
      |    450 | + 				Utils::FileSystem::removeFile(tempZip);
      |    451 | + 				return false;
      |    452 | + 			}
      |    453 | + 			md5.update(reinterpret_cast<const char*>(chunk.data()), static_cast<MD5::size_type>(length));
  150 |    454 |   
  151 |    455 |   			offset += length;
      |    456 | + 			reportProgress(progressCallback, 0.05f + 0.40f * static_cast<float>(offset) / static_cast<float>(archiveSize));
  152 |    457 |   		}
  153 |    458 |   
  154 |    459 |   		output.close();
  155 |    460 |   		if (!output.good())
  156 |    461 |   		{
  157 |        | - 			LOG(LogError) << "EmbeddedTheme: failed to write temporary theme archive.";
      |    462 | + 			LOG(LogError) << "EmbeddedTheme: failed to finalize the temporary theme archive.";
  158 |    463 |   			Utils::FileSystem::removeFile(tempZip);
  159 |    464 |   			return false;
  160 |    465 |   		}
  161 |    466 |   
  162 |    467 |   		md5.finalize();
  163 |        | - 		const std::string actualIdentity = md5.hexdigest();
  164 |        | - 		if (actualIdentity != payload.identity)
      |    468 | + 		if (md5.hexdigest() != payload.identity)
  165 |    469 |   		{
  166 |    470 |   			LOG(LogError) << "EmbeddedTheme: payload identity does not match its archive; rebuild the executable.";
  167 |    471 |   			Utils::FileSystem::removeFile(tempZip);
  168 |    472 |   			return false;
  169 |    473 |   		}
  170 |        | - 		payloadSize = archiveSize;
      |    474 | + 
      |    475 | + 		if (!Utils::FileSystem::exists(tempZip, false)
      |    476 | + 			|| Utils::FileSystem::getFileSize(tempZip) != static_cast<unsigned long long>(archiveSize))
      |    477 | + 		{
      |    478 | + 			LOG(LogError) << "EmbeddedTheme: temporary theme archive is incomplete.";
      |    479 | + 			Utils::FileSystem::removeFile(tempZip);
      |    480 | + 			return false;
      |    481 | + 		}
  171 |    482 |   
  172 |    483 |   		LOG(LogInfo) << "EmbeddedTheme: decrypted " << archiveSize << " bytes from executable.";
  173 |        | - 		// Este arquivo acabou de ser criado. Nao aceite um resultado negativo
  174 |        | - 		// antigo do FileSystemCache durante a primeira inicializacao.
  175 |        | - 		return Utils::FileSystem::exists(tempZip, false);
      |    484 | + 		return true;
  176 |    485 |   #else
  177 |    486 |   		(void)payload;
      |    487 | + 		(void)tempZip;
      |    488 | + 		(void)progressCallback;
  178 |    489 |   		return false;
  179 |    490 |   #endif
  180 |    491 |   	}
  181 |    492 |   
  182 |        | - 	bool extractThemeArchive(const std::string& tempZip, const std::string& targetPath)
      |    493 | + 	bool isPathInside(const std::string& parentPath, const std::string& childPath)
      |    494 | + 	{
      |    495 | + 		std::string parent = Utils::FileSystem::getCanonicalPath(parentPath);
      |    496 | + 		std::string child = Utils::FileSystem::getCanonicalPath(childPath);
      |    497 | + #if WIN32
      |    498 | + 		parent = Utils::String::toLower(parent);
      |    499 | + 		child = Utils::String::toLower(child);
      |    500 | + #endif
      |    501 | + 		if (!Utils::String::endsWith(parent, "/"))
      |    502 | + 			parent += "/";
      |    503 | + 		return Utils::String::startsWith(child, parent);
      |    504 | + 	}
      |    505 | + 
      |    506 | + 	bool extractThemeArchive(const std::string& tempZip, const std::string& targetPath,
      |    507 | + 		const std::string& cacheRoot, const EmbeddedTheme::ProgressCallback& progressCallback)
  183 |    508 |   	{
  184 |    509 |   		Utils::Zip::ZipFile archive;
  185 |    510 |   		if (!archive.load(tempZip))
```

## Trecho 9: antes 188, depois 513

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L188) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L513)

```text
ANTES | DEPOIS |   CÓDIGO
  188 |    513 |   			return false;
  189 |    514 |   		}
  190 |    515 |   
  191 |        | - 		Utils::FileSystem::createDirectory(targetPath);
  192 |        | - 
  193 |        | - 		const auto members = archive.namelist();
  194 |        | - 		size_t extracted = 0;
  195 |        | - 
      |    516 | + 		const auto members = archive.infolist();
      |    517 | + 		std::uint64_t uncompressedBytes = 0;
  196 |    518 |   		for (const auto& member : members)
  197 |    519 |   		{
  198 |        | - 			if (member.empty() || Utils::String::endsWith(member, "/"))
  199 |        | - 				continue;
      |    520 | + 			const std::uint64_t memberSize = static_cast<std::uint64_t>(member.file_size);
      |    521 | + 			if (memberSize > std::numeric_limits<std::uint64_t>::max() - uncompressedBytes)
      |    522 | + 			{
      |    523 | + 				LOG(LogError) << "EmbeddedTheme: theme archive size overflow.";
      |    524 | + 				return false;
      |    525 | + 			}
      |    526 | + 			uncompressedBytes += memberSize;
      |    527 | + 		}
      |    528 | + 
      |    529 | + 		// The encrypted ZIP is already on disk here, so only the expanded files
      |    530 | + 		// plus a small operating reserve are still required.
      |    531 | + 		if (!hasEnoughFreeSpace(cacheRoot, uncompressedBytes, "theme extraction"))
      |    532 | + 			return false;
  200 |    533 |   
  201 |        | - 			const std::string fullPath = Utils::FileSystem::getCanonicalPath(targetPath + "/" + member);
  202 |        | - 			const std::string parentPath = Utils::FileSystem::getParent(fullPath);
  203 |        | - 			if (!parentPath.empty())
  204 |        | - 				Utils::FileSystem::createDirectory(parentPath);
      |    534 | + 		if (!Utils::FileSystem::createDirectory(targetPath) || Utils::FileSystem::isSymlink(targetPath))
      |    535 | + 		{
      |    536 | + 			LOG(LogError) << "EmbeddedTheme: unable to create a safe extraction directory.";
      |    537 | + 			return false;
      |    538 | + 		}
  205 |    539 |   
  206 |        | - 			if (!archive.extract(member, targetPath, false))
      |    540 | + 		size_t extracted = 0;
      |    541 | + 		for (size_t index = 0; index < members.size(); index++)
      |    542 | + 		{
      |    543 | + 			const std::string& member = members[index].filename;
      |    544 | + 			if (!member.empty() && !Utils::String::endsWith(member, "/"))
  207 |    545 |   			{
  208 |        | - 				LOG(LogError) << "EmbeddedTheme: failed extracting '" << member << "'.";
  209 |        | - 				return false;
      |    546 | + 				const std::string fullPath = Utils::FileSystem::getCanonicalPath(targetPath + "/" + member);
      |    547 | + 				if (!isPathInside(targetPath, fullPath))
      |    548 | + 				{
      |    549 | + 					LOG(LogError) << "EmbeddedTheme: refusing an archive member outside the theme cache.";
      |    550 | + 					return false;
      |    551 | + 				}
      |    552 | + 
      |    553 | + 				const std::string parentPath = Utils::FileSystem::getParent(fullPath);
      |    554 | + 				if (!parentPath.empty()
      |    555 | + 					&& (!Utils::FileSystem::createDirectory(parentPath) || Utils::FileSystem::isSymlink(parentPath)))
      |    556 | + 				{
      |    557 | + 					LOG(LogError) << "EmbeddedTheme: unable to create a safe archive member directory.";
      |    558 | + 					return false;
      |    559 | + 				}
      |    560 | + 
      |    561 | + 				if (!archive.extract(member, targetPath, false))
      |    562 | + 				{
      |    563 | + 					LOG(LogError) << "EmbeddedTheme: failed extracting '" << member << "'.";
      |    564 | + 					return false;
      |    565 | + 				}
      |    566 | + 				extracted++;
  210 |    567 |   			}
  211 |    568 |   
  212 |        | - 			extracted++;
  213 |        | - 			if (extracted % 100 == 0)
  214 |        | - 				LOG(LogInfo) << "EmbeddedTheme: extracted " << extracted << " / " << members.size() << " files...";
      |    569 | + 			if (index % 10 == 0 || index + 1 == members.size())
      |    570 | + 			{
      |    571 | + 				const float ratio = members.empty() ? 1.0f
      |    572 | + 					: static_cast<float>(index + 1) / static_cast<float>(members.size());
      |    573 | + 				reportProgress(progressCallback, 0.50f + 0.48f * ratio);
      |    574 | + 			}
  215 |    575 |   		}
  216 |    576 |   
  217 |    577 |   		LOG(LogInfo) << "EmbeddedTheme: extracted " << extracted << " files.";
  218 |        | - 		// theme.xml pode ter sido consultado antes da extracao e estar cacheado
  219 |        | - 		// como inexistente. A confirmacao pos-escrita precisa tocar o disco.
  220 |        | - 		return Utils::FileSystem::exists(targetPath + "/theme.xml", false);
      |    578 | + 		// ZipFile writes outside FileSystemUtil, so discard any negative lookups
      |    579 | + 		// collected while validating an incomplete cache before extraction.
      |    580 | + 		Utils::FileSystem::FileSystemCache::reset();
      |    581 | + 		const std::string themePath = targetPath + "/theme.xml";
      |    582 | + 		// Refresh a possible negative lookup performed before extraction.
      |    583 | + 		return Utils::FileSystem::exists(themePath, false)
      |    584 | + 			&& !Utils::FileSystem::isSymlink(themePath)
      |    585 | + 			&& Utils::FileSystem::isRegularFile(themePath);
  221 |    586 |   	}
  222 |    587 |   
  223 |    588 |   	bool isThemeSetAlias(const std::string& themeSet)
```

## Trecho 10: antes 235, depois 600

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L235) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L600)

```text
ANTES | DEPOIS |   CÓDIGO
  235 |    600 |   			Settings::getInstance()->setString("ThemeSet", EmbeddedTheme::THEME_SET_ID);
  236 |    601 |   	}
  237 |    602 |   
  238 |        | - 	bool findCachedTheme(const std::string& payloadIdentity, std::string& cachedPath)
  239 |        | - 	{
  240 |        | - 		const std::string cacheRoot = getCacheDirectory();
  241 |        | - 		if (!Utils::FileSystem::isDirectory(cacheRoot) || payloadIdentity.size() < 12)
  242 |        | - 			return false;
  243 |        | - 
  244 |        | - 		cachedPath = Utils::FileSystem::getCanonicalPath(cacheRoot + "/" + payloadIdentity.substr(0, 12));
  245 |        | - 		const std::string markerPath = cachedPath + "/.payload";
  246 |        | - 		return Utils::FileSystem::exists(cachedPath + "/theme.xml", false)
  247 |        | - 			&& Utils::FileSystem::exists(markerPath, false)
  248 |        | - 			&& Utils::FileSystem::readAllText(markerPath) == payloadIdentity;
  249 |        | - 	}
  250 |        | - 
  251 |    603 |   	void ensureDefaultSubsetSettings()
  252 |    604 |   	{
  253 |    605 |   		Settings* settings = Settings::getInstance();
```

## Trecho 11: antes 266, depois 618

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/0e02780b761cb488c591416d2986130efcc166dd/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L266) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp#L618)

```text
ANTES | DEPOIS |   CÓDIGO
  266 |    618 |   		setDefault("subset.system-username", "Turborama-X");
  267 |    619 |   		setDefault("subset.top-info", "default");
  268 |    620 |   	}
      |    621 | + 
      |    622 | + 	void publishTheme(const std::string& rootPath, bool cached)
      |    623 | + 	{
      |    624 | + 		sRootPath = rootPath;
      |    625 | + 		applyThemeSetSelection();
      |    626 | + 		ensureDefaultSubsetSettings();
      |    627 | + 		sAvailable.store(true, std::memory_order_release);
      |    628 | + 
      |    629 | + 		LOG(LogInfo) << "EmbeddedTheme: ready at " << sRootPath << (cached ? " (cached)" : "");
      |    630 | + 	}
  269 |    631 |   }
  270 |    632 |   
  271 |        | - bool EmbeddedTheme::initialize()
      |    633 | + bool EmbeddedTheme::initialize(const ProgressCallback& progressCallback)
  272 |    634 |   {
  273 |        | - 	if (sAvailable)
      |    635 | + 	if (sAvailable.load(std::memory_order_acquire))
  274 |    636 |   		return true;
  275 |    637 |   
  276 |    638 |   	std::lock_guard<std::mutex> lock(sInitMutex);
  277 |        | - 	if (sAvailable)
      |    639 | + 	if (sAvailable.load(std::memory_order_relaxed))
  278 |    640 |   		return true;
      |    641 | + 	if (sInitializationAttempted)
      |    642 | + 		return false;
      |    643 | + 	sInitializationAttempted = true;
      |    644 | + 	reportProgress(progressCallback, 0.0f);
  279 |    645 |   
  280 |        | - 	const std::string cacheRoot = getCacheDirectory();
  281 |        | - 	std::string extractPath;
  282 |    646 |   	EmbeddedPayload payload;
  283 |    647 |   	if (!loadEmbeddedPayload(payload))
  284 |    648 |   		return false;
  285 |    649 |   
  286 |        | - 	if (findCachedTheme(payload.identity, extractPath))
      |    650 | + 	const std::string cacheRoot = getCacheDirectory();
      |    651 | + 	if (cacheRoot.empty() || Utils::FileSystem::isSymlink(cacheRoot))
  287 |    652 |   	{
  288 |        | - 		sRootPath = extractPath;
  289 |        | - 		sAvailable = true;
  290 |        | - 
  291 |        | - 		applyThemeSetSelection();
  292 |        | - 		ensureDefaultSubsetSettings();
  293 |        | - 		LOG(LogInfo) << "EmbeddedTheme: ready at " << sRootPath << " (cached)";
  294 |        | - 		return true;
      |    653 | + 		LOG(LogError) << "EmbeddedTheme: refusing an unsafe theme cache directory.";
      |    654 | + 		return false;
  295 |    655 |   	}
  296 |    656 |   
  297 |        | - 	size_t payloadSize = 0;
  298 |        | - 	if (!decryptResourceToFile(payload, payloadSize))
      |    657 | + 	ScopedThemeCacheLock processLock(cacheRoot, progressCallback);
      |    658 | + 	if (!processLock.acquired())
  299 |    659 |   		return false;
  300 |    660 |   
  301 |    661 |   	const std::string tempZip = Utils::FileSystem::getCanonicalPath(cacheRoot + "/.theme.pack.zip");
  302 |        | - 	extractPath = Utils::FileSystem::getCanonicalPath(cacheRoot + "/" + payload.identity.substr(0, 12));
  303 |        | - 	const std::string markerPath = extractPath + "/.payload";
      |    662 | + 	if (!removeTemporaryArchive(tempZip))
      |    663 | + 		return false;
  304 |    664 |   
  305 |        | - 	const bool markerMatches = Utils::FileSystem::exists(markerPath, false) && Utils::FileSystem::readAllText(markerPath) == payload.identity;
  306 |        | - 	const bool themeReady = Utils::FileSystem::exists(extractPath + "/theme.xml", false);
      |    665 | + 	pruneObsoleteThemeCaches(cacheRoot, payload.identity, progressCallback);
  307 |    666 |   
  308 |        | - 	if (!markerMatches || !themeReady)
      |    667 | + 	std::string extractPath;
      |    668 | + 	if (findCachedTheme(cacheRoot, payload.identity, extractPath))
  309 |    669 |   	{
  310 |        | - 		LOG(LogInfo) << "EmbeddedTheme: extracting protected theme to cache (first run may take several minutes)...";
  311 |        | - 		Utils::FileSystem::deleteDirectoryFiles(extractPath + "/");
  312 |        | - 		Utils::FileSystem::createDirectory(extractPath);
  313 |        | - 		setHiddenDirectory(cacheRoot);
  314 |        | - 
  315 |        | - 		if (!extractThemeArchive(tempZip, extractPath))
  316 |        | - 		{
  317 |        | - 			LOG(LogError) << "EmbeddedTheme: failed to extract protected theme.";
  318 |        | - 			Utils::FileSystem::deleteDirectoryFiles(extractPath + "/");
  319 |        | - 			Utils::FileSystem::removeFile(tempZip);
  320 |        | - 			return false;
  321 |        | - 		}
      |    670 | + 		publishTheme(extractPath, true);
      |    671 | + 		reportProgress(progressCallback, 1.0f);
      |    672 | + 		return true;
      |    673 | + 	}
  322 |    674 |   
  323 |        | - 		Utils::FileSystem::writeAllText(markerPath, payload.identity);
  324 |        | - 		setHiddenDirectory(markerPath);
      |    675 | + 	extractPath = Utils::FileSystem::getCanonicalPath(cacheRoot + "/" + payload.identity.substr(0, 12));
      |    676 | + 	if (Utils::FileSystem::exists(extractPath, false)
      |    677 | + 		&& !safelyRemoveCachePath(extractPath, progressCallback, 0.03f))
      |    678 | + 	{
      |    679 | + 		LOG(LogError) << "EmbeddedTheme: unable to safely clear the incomplete current theme cache.";
      |    680 | + 		return false;
  325 |    681 |   	}
  326 |    682 |   
  327 |        | - 	Utils::FileSystem::removeFile(tempZip);
      |    683 | + 	const size_t archiveSize = payload.size - payload.archiveOffset;
      |    684 | + 	if (!hasEnoughFreeSpace(cacheRoot, static_cast<std::uint64_t>(archiveSize), "theme archive creation"))
      |    685 | + 		return false;
  328 |    686 |   
  329 |        | - 	sRootPath = extractPath;
  330 |        | - 	sAvailable = true;
      |    687 | + 	if (!decryptResourceToFile(payload, tempZip, progressCallback))
      |    688 | + 		return false;
  331 |    689 |   
  332 |        | - 	applyThemeSetSelection();
  333 |        | - 	ensureDefaultSubsetSettings();
      |    690 | + 	setHiddenPath(cacheRoot);
      |    691 | + 	LOG(LogInfo) << "EmbeddedTheme: extracting protected theme to cache (first run may take several minutes)...";
      |    692 | + 	if (!extractThemeArchive(tempZip, extractPath, cacheRoot, progressCallback))
      |    693 | + 	{
      |    694 | + 		LOG(LogError) << "EmbeddedTheme: failed to extract protected theme.";
      |    695 | + 		if (!safelyRemoveCachePath(extractPath, progressCallback, 0.98f))
      |    696 | + 			LOG(LogWarning) << "EmbeddedTheme: unable to safely clean the incomplete theme cache.";
      |    697 | + 		removeTemporaryArchive(tempZip);
      |    698 | + 		return false;
      |    699 | + 	}
  334 |    700 |   
  335 |        | - 	LOG(LogInfo) << "EmbeddedTheme: ready at " << sRootPath;
      |    701 | + 	const std::string markerPath = extractPath + "/.payload";
      |    702 | + 	Utils::FileSystem::writeAllText(markerPath, payload.identity);
      |    703 | + 	setHiddenPath(markerPath);
      |    704 | + 	if (Utils::FileSystem::isSymlink(markerPath)
      |    705 | + 		|| !Utils::FileSystem::isRegularFile(markerPath)
      |    706 | + 		|| Utils::FileSystem::getFileSize(markerPath) != sPayloadIdentityLength
      |    707 | + 		|| Utils::FileSystem::readAllText(markerPath) != payload.identity)
      |    708 | + 	{
      |    709 | + 		LOG(LogError) << "EmbeddedTheme: failed to verify the extracted theme marker.";
      |    710 | + 		if (!safelyRemoveCachePath(extractPath, progressCallback, 0.98f))
      |    711 | + 			LOG(LogWarning) << "EmbeddedTheme: unable to safely clean the unverified theme cache.";
      |    712 | + 		removeTemporaryArchive(tempZip);
      |    713 | + 		return false;
      |    714 | + 	}
      |    715 | + 
      |    716 | + 	if (!removeTemporaryArchive(tempZip))
      |    717 | + 		LOG(LogWarning) << "EmbeddedTheme: the temporary theme archive will be removed on the next start.";
      |    718 | + 
      |    719 | + 	publishTheme(extractPath, false);
      |    720 | + 	reportProgress(progressCallback, 1.0f);
  336 |    721 |   	return true;
  337 |    722 |   }
  338 |    723 |   
  339 |    724 |   bool EmbeddedTheme::isAvailable()
  340 |    725 |   {
  341 |        | - 	if (!sAvailable)
  342 |        | - 		initialize();
  343 |        | - 
  344 |        | - 	return sAvailable;
      |    726 | + 	return sAvailable.load(std::memory_order_acquire);
  345 |    727 |   }
  346 |    728 |   
  347 |    729 |   bool EmbeddedTheme::isActiveThemeSet(const std::string& themeSet)
```

Conferência: 11 trechos, 482 linhas adicionadas e 100 removidas.

