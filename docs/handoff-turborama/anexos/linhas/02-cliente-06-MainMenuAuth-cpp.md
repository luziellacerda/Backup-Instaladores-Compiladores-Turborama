# 02-cliente: TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Autenticação administrativa do menu separada do gerenciador de crédito. Não é pagamento nem cronômetro.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 0, depois 1

Arquivo novo nesta comparação; não existe na revisão anterior. [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L1)

```text
ANTES | DEPOIS |   CÓDIGO
      |      1 | + #include "MainMenuAuth.h"
      |      2 | + 
      |      3 | + #include "Log.h"
      |      4 | + #include "Paths.h"
      |      5 | + #include "utils/FileSystemUtil.h"
      |      6 | + #include "utils/StringUtil.h"
      |      7 | + #include "utils/md5.h"
      |      8 | + 
      |      9 | + #include <algorithm>
      |     10 | + #include <cerrno>
      |     11 | + #include <cstddef>
      |     12 | + #include <cstdlib>
      |     13 | + #include <fstream>
      |     14 | + #include <iomanip>
      |     15 | + #include <locale>
      |     16 | + #include <random>
      |     17 | + #include <sstream>
      |     18 | + #include <vector>
      |     19 | + 
      |     20 | + #ifdef _WIN32
      |     21 | + #include <windows.h>
      |     22 | + #include <bcrypt.h>
      |     23 | + #pragma comment(lib, "bcrypt.lib")
      |     24 | + #else
      |     25 | + #include <fcntl.h>
      |     26 | + #include <sys/stat.h>
      |     27 | + #include <unistd.h>
      |     28 | + #endif
      |     29 | + 
      |     30 | + namespace
      |     31 | + {
      |     32 | + 	const char* const DEFAULT_PASSWORD = "admin";
      |     33 | + 	const char* const HASH_PREFIX = "pbkdf2-sha256$";
      |     34 | + 	const char* const PORTABLE_HASH_PREFIX = "legacy-md5$";
      |     35 | + 	const unsigned long long PBKDF2_ITERATIONS = 210000;
      |     36 | + 	const std::size_t MAX_CONFIG_BYTES = 64u * 1024u;
      |     37 | + 	const std::size_t MAX_CONFIG_LINES = 512u;
      |     38 | + 	const std::size_t MAX_LINE_BYTES = 4096u;
      |     39 | + 
      |     40 | + 	enum class RegularFileState
      |     41 | + 	{
      |     42 | + 		Missing,
      |     43 | + 		Regular,
      |     44 | + 		UnsafeOrError
      |     45 | + 	};
      |     46 | + 
      |     47 | + 	enum class CredentialSource
      |     48 | + 	{
      |     49 | + 		DefaultPassword,
      |     50 | + 		AuthFile,
      |     51 | + 		LegacyHash,
      |     52 | + 		LegacyPlainText,
      |     53 | + 		Invalid
      |     54 | + 	};
      |     55 | + 
      |     56 | + 	struct ActiveCredential
      |     57 | + 	{
      |     58 | + 		CredentialSource source;
      |     59 | + 		std::string value;
      |     60 | + 	};
      |     61 | + 
      |     62 | + 	bool constantTimeEquals(const std::string& left, const std::string& right)
      |     63 | + 	{
      |     64 | + 		const std::size_t count = left.size() > right.size() ? left.size() : right.size();
      |     65 | + 		std::size_t difference = left.size() ^ right.size();
      |     66 | + 		for (std::size_t index = 0; index < count; ++index)
      |     67 | + 		{
      |     68 | + 			const unsigned char leftByte = index < left.size()
      |     69 | + 				? static_cast<unsigned char>(left[index]) : 0;
      |     70 | + 			const unsigned char rightByte = index < right.size()
      |     71 | + 				? static_cast<unsigned char>(right[index]) : 0;
      |     72 | + 			difference |= static_cast<std::size_t>(leftByte ^ rightByte);
      |     73 | + 		}
      |     74 | + 		return difference == 0;
      |     75 | + 	}
      |     76 | + 
      |     77 | + 	std::string hexEncode(const std::vector<unsigned char>& bytes)
      |     78 | + 	{
      |     79 | + 		std::ostringstream output;
      |     80 | + 		output.imbue(std::locale::classic());
      |     81 | + 		output << std::hex << std::setfill('0');
      |     82 | + 		for (const unsigned char byte : bytes)
      |     83 | + 			output << std::setw(2) << static_cast<unsigned int>(byte);
      |     84 | + 		return output.str();
      |     85 | + 	}
      |     86 | + 
      |     87 | + 	bool hexDecode(const std::string& text, std::vector<unsigned char>& bytes)
      |     88 | + 	{
      |     89 | + 		bytes.clear();
      |     90 | + 		if (text.empty() || (text.size() % 2) != 0)
      |     91 | + 			return false;
      |     92 | + 
      |     93 | + 		bytes.reserve(text.size() / 2);
      |     94 | + 		for (std::size_t index = 0; index < text.size(); index += 2)
      |     95 | + 		{
      |     96 | + 			auto nibble = [](const char value) -> int
      |     97 | + 			{
      |     98 | + 				if (value >= '0' && value <= '9') return value - '0';
      |     99 | + 				if (value >= 'a' && value <= 'f') return value - 'a' + 10;
      |    100 | + 				if (value >= 'A' && value <= 'F') return value - 'A' + 10;
      |    101 | + 				return -1;
      |    102 | + 			};
      |    103 | + 
      |    104 | + 			const int high = nibble(text[index]);
      |    105 | + 			const int low = nibble(text[index + 1]);
      |    106 | + 			if (high < 0 || low < 0)
      |    107 | + 			{
      |    108 | + 				bytes.clear();
      |    109 | + 				return false;
      |    110 | + 			}
      |    111 | + 			bytes.push_back(static_cast<unsigned char>((high << 4) | low));
      |    112 | + 		}
      |    113 | + 		return true;
      |    114 | + 	}
      |    115 | + 
      |    116 | + 	bool isMd5Hash(const std::string& encoded)
      |    117 | + 	{
      |    118 | + 		if (encoded.size() != 32)
      |    119 | + 			return false;
      |    120 | + 		for (const char value : encoded)
      |    121 | + 			if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f')
      |    122 | + 				|| (value >= 'A' && value <= 'F')))
      |    123 | + 				return false;
      |    124 | + 		return true;
      |    125 | + 	}
      |    126 | + 
      |    127 | + 	bool secureRandomBytes(std::vector<unsigned char>& bytes)
      |    128 | + 	{
      |    129 | + 		if (bytes.empty())
      |    130 | + 			return false;
      |    131 | + #ifdef _WIN32
      |    132 | + 		return BCryptGenRandom(nullptr, bytes.data(), static_cast<ULONG>(bytes.size()),
      |    133 | + 			BCRYPT_USE_SYSTEM_PREFERRED_RNG) >= 0;
      |    134 | + #else
      |    135 | + 		try
      |    136 | + 		{
      |    137 | + 			std::random_device random;
      |    138 | + 			for (unsigned char& byte : bytes)
      |    139 | + 				byte = static_cast<unsigned char>(random());
      |    140 | + 			return true;
      |    141 | + 		}
      |    142 | + 		catch (...)
      |    143 | + 		{
      |    144 | + 			return false;
      |    145 | + 		}
      |    146 | + #endif
      |    147 | + 	}
      |    148 | + 
      |    149 | + 	bool derivePbkdf2Sha256(const std::string& password,
      |    150 | + 		const std::vector<unsigned char>& salt, const unsigned long long iterations,
      |    151 | + 		std::vector<unsigned char>& digest)
      |    152 | + 	{
      |    153 | + #ifdef _WIN32
      |    154 | + 		BCRYPT_ALG_HANDLE algorithm = nullptr;
      |    155 | + 		if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM,
      |    156 | + 			nullptr, BCRYPT_ALG_HANDLE_HMAC_FLAG) < 0)
      |    157 | + 			return false;
      |    158 | + 
      |    159 | + 		const NTSTATUS status = BCryptDeriveKeyPBKDF2(algorithm,
      |    160 | + 			reinterpret_cast<PUCHAR>(const_cast<char*>(password.data())),
      |    161 | + 			static_cast<ULONG>(password.size()),
      |    162 | + 			const_cast<PUCHAR>(salt.data()), static_cast<ULONG>(salt.size()),
      |    163 | + 			iterations, digest.data(), static_cast<ULONG>(digest.size()), 0);
      |    164 | + 		BCryptCloseAlgorithmProvider(algorithm, 0);
      |    165 | + 		return status >= 0;
      |    166 | + #else
      |    167 | + 		(void)password;
      |    168 | + 		(void)salt;
      |    169 | + 		(void)iterations;
      |    170 | + 		(void)digest;
      |    171 | + 		return false;
      |    172 | + #endif
      |    173 | + 	}
      |    174 | + 
      |    175 | + 	bool parsePbkdf2Hash(const std::string& encoded, unsigned long long& iterations,
      |    176 | + 		std::vector<unsigned char>& salt, std::vector<unsigned char>& digest)
      |    177 | + 	{
      |    178 | + 		const std::string prefix(HASH_PREFIX);
      |    179 | + 		if (encoded.rfind(prefix, 0) != 0)
      |    180 | + 			return false;
      |    181 | + 
      |    182 | + 		const std::size_t iterationEnd = encoded.find('$', prefix.size());
      |    183 | + 		const std::size_t saltEnd = iterationEnd == std::string::npos
      |    184 | + 			? std::string::npos : encoded.find('$', iterationEnd + 1);
      |    185 | + 		if (iterationEnd == std::string::npos || saltEnd == std::string::npos
      |    186 | + 			|| encoded.find('$', saltEnd + 1) != std::string::npos)
      |    187 | + 			return false;
      |    188 | + 
      |    189 | + 		const std::string iterationText = encoded.substr(prefix.size(), iterationEnd - prefix.size());
      |    190 | + 		if (iterationText.empty() || iterationText.size() > 9)
      |    191 | + 			return false;
      |    192 | + 		for (const char digit : iterationText)
      |    193 | + 			if (digit < '0' || digit > '9')
      |    194 | + 				return false;
      |    195 | + 
      |    196 | + 		iterations = std::strtoull(iterationText.c_str(), nullptr, 10);
      |    197 | + 		return iterations >= 100000 && iterations <= 2000000
      |    198 | + 			&& hexDecode(encoded.substr(iterationEnd + 1, saltEnd - iterationEnd - 1), salt)
      |    199 | + 			&& salt.size() >= 16 && salt.size() <= 64
      |    200 | + 			&& hexDecode(encoded.substr(saltEnd + 1), digest)
      |    201 | + 			&& digest.size() == 32;
      |    202 | + 	}
      |    203 | + 
      |    204 | + 	bool isSupportedPasswordHash(const std::string& encoded)
      |    205 | + 	{
      |    206 | + 		if (isMd5Hash(encoded))
      |    207 | + 			return true;
      |    208 | + 		const std::string portablePrefix(PORTABLE_HASH_PREFIX);
      |    209 | + 		if (encoded.rfind(portablePrefix, 0) == 0)
      |    210 | + 			return isMd5Hash(encoded.substr(portablePrefix.size()));
      |    211 | + 
      |    212 | + 		unsigned long long iterations = 0;
      |    213 | + 		std::vector<unsigned char> salt;
      |    214 | + 		std::vector<unsigned char> digest;
      |    215 | + 		return parsePbkdf2Hash(encoded, iterations, salt, digest);
      |    216 | + 	}
      |    217 | + 
      |    218 | + 	std::string createPasswordHash(const std::string& password)
      |    219 | + 	{
      |    220 | + #ifdef _WIN32
      |    221 | + 		std::vector<unsigned char> salt(16);
      |    222 | + 		std::vector<unsigned char> digest(32);
      |    223 | + 		if (!secureRandomBytes(salt)
      |    224 | + 			|| !derivePbkdf2Sha256(password, salt, PBKDF2_ITERATIONS, digest))
      |    225 | + 			return std::string();
      |    226 | + 
      |    227 | + 		return std::string(HASH_PREFIX) + std::to_string(PBKDF2_ITERATIONS)
      |    228 | + 			+ "$" + hexEncode(salt) + "$" + hexEncode(digest);
      |    229 | + #else
      |    230 | + 		// The customer release is Windows-only. Keep other developer targets
      |    231 | + 		// functional with the same portable hash already used by legacy menus.
      |    232 | + 		return std::string(PORTABLE_HASH_PREFIX) + MD5(password).hexdigest();
      |    233 | + #endif
      |    234 | + 	}
      |    235 | + 
      |    236 | + 	bool verifyPasswordHash(const std::string& password, const std::string& encoded)
      |    237 | + 	{
      |    238 | + 		if (isMd5Hash(encoded))
      |    239 | + 			return constantTimeEquals(MD5(password).hexdigest(), Utils::String::toLower(encoded));
      |    240 | + 
      |    241 | + 		const std::string portablePrefix(PORTABLE_HASH_PREFIX);
      |    242 | + 		if (encoded.rfind(portablePrefix, 0) == 0)
      |    243 | + 			return constantTimeEquals(MD5(password).hexdigest(),
      |    244 | + 				Utils::String::toLower(encoded.substr(portablePrefix.size())));
      |    245 | + 
      |    246 | + 		unsigned long long iterations = 0;
      |    247 | + 		std::vector<unsigned char> salt;
      |    248 | + 		std::vector<unsigned char> expected;
      |    249 | + 		if (!parsePbkdf2Hash(encoded, iterations, salt, expected))
      |    250 | + 			return false;
      |    251 | + 
      |    252 | + 		std::vector<unsigned char> actual(32);
      |    253 | + 		return derivePbkdf2Sha256(password, salt, iterations, actual)
      |    254 | + 			&& constantTimeEquals(hexEncode(actual), hexEncode(expected));
      |    255 | + 	}
      |    256 | + 
      |    257 | + 	RegularFileState inspectRegularFile(const std::string& path)
      |    258 | + 	{
      |    259 | + #ifdef _WIN32
      |    260 | + 		const std::wstring widePath = Utils::String::convertToWideString(path);
      |    261 | + 		const DWORD attributes = GetFileAttributesW(widePath.c_str());
      |    262 | + 		if (attributes == INVALID_FILE_ATTRIBUTES)
      |    263 | + 		{
      |    264 | + 			const DWORD error = GetLastError();
      |    265 | + 			return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND
      |    266 | + 				? RegularFileState::Missing : RegularFileState::UnsafeOrError;
      |    267 | + 		}
      |    268 | + 		if ((attributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0)
      |    269 | + 			return RegularFileState::UnsafeOrError;
      |    270 | + 		return RegularFileState::Regular;
      |    271 | + #else
      |    272 | + 		struct stat info;
      |    273 | + 		if (::lstat(path.c_str(), &info) == 0)
      |    274 | + 			return S_ISREG(info.st_mode) ? RegularFileState::Regular
      |    275 | + 				: RegularFileState::UnsafeOrError;
      |    276 | + 		return errno == ENOENT || errno == ENOTDIR
      |    277 | + 			? RegularFileState::Missing : RegularFileState::UnsafeOrError;
      |    278 | + #endif
      |    279 | + 	}
      |    280 | + 
      |    281 | + 	bool readBoundedTextLines(const std::string& path, std::vector<std::string>& lines)
      |    282 | + 	{
      |    283 | + 		lines.clear();
      |    284 | + #ifdef _WIN32
      |    285 | + 		// Paths are UTF-8 in the frontend. Use the wide MSVC file overload so a
      |    286 | + 		// Windows account such as "Joao" with accents can read its credential.
      |    287 | + 		std::ifstream input(Utils::String::convertToWideString(path).c_str(),
      |    288 | + 			std::ios::in | std::ios::binary);
      |    289 | + #else
      |    290 | + 		std::ifstream input(path, std::ios::in | std::ios::binary);
      |    291 | + #endif
      |    292 | + 		if (!input.is_open())
      |    293 | + 			return false;
      |    294 | + 
      |    295 | + 		input.seekg(0, std::ios::end);
      |    296 | + 		const std::streamoff declaredSize = input.tellg();
      |    297 | + 		if (declaredSize < 0 || static_cast<unsigned long long>(declaredSize) > MAX_CONFIG_BYTES)
      |    298 | + 			return false;
      |    299 | + 		input.seekg(0, std::ios::beg);
      |    300 | + 		if (!input.good())
      |    301 | + 			return false;
      |    302 | + 
      |    303 | + 		std::size_t bytesRead = 0;
      |    304 | + 		std::string line;
      |    305 | + 		line.reserve(256);
      |    306 | + 		char value = 0;
      |    307 | + 		while (input.get(value))
      |    308 | + 		{
      |    309 | + 			if (bytesRead >= MAX_CONFIG_BYTES)
      |    310 | + 				return false;
      |    311 | + 			++bytesRead;
      |    312 | + 			if (value == '\n')
      |    313 | + 			{
      |    314 | + 				if (lines.size() >= MAX_CONFIG_LINES)
      |    315 | + 					return false;
      |    316 | + 				lines.push_back(line);
      |    317 | + 				line.clear();
      |    318 | + 			}
      |    319 | + 			else
      |    320 | + 			{
      |    321 | + 				if (line.size() >= MAX_LINE_BYTES)
      |    322 | + 					return false;
      |    323 | + 				line.push_back(value);
      |    324 | + 			}
      |    325 | + 		}
      |    326 | + 		if (input.bad())
      |    327 | + 			return false;
      |    328 | + 		if (!line.empty())
      |    329 | + 		{
      |    330 | + 			if (lines.size() >= MAX_CONFIG_LINES)
      |    331 | + 				return false;
      |    332 | + 			lines.push_back(line);
      |    333 | + 		}
      |    334 | + 		return true;
      |    335 | + 	}
      |    336 | + 
      |    337 | + 	std::string authenticationFilePath()
      |    338 | + 	{
      |    339 | + 		return Utils::FileSystem::combine(Paths::getUserEmulationStationPath(), "main_menu_auth.cfg");
      |    340 | + 	}
      |    341 | + 
      |    342 | + 	std::string legacyCredentialFilePath()
      |    343 | + 	{
      |    344 | + 		// Read only the former administrator credential once. No financial,
      |    345 | + 		// payment, rental-time or daemon state is loaded from this file.
      |    346 | + 		return Utils::FileSystem::combine(Paths::getUserEmulationStationPath(), "arcade_credit.cfg");
      |    347 | + 	}
      |    348 | + 
      |    349 | + 	bool atomicWriteText(const std::string& path, const std::string& content)
      |    350 | + 	{
      |    351 | + 		if (inspectRegularFile(path) == RegularFileState::UnsafeOrError)
      |    352 | + 			return false;
      |    353 | + 
      |    354 | + 		const std::string directory = Utils::FileSystem::getParent(path);
      |    355 | + 		if (!directory.empty() && !Utils::FileSystem::createDirectory(directory))
      |    356 | + 			return false;
      |    357 | + 
      |    358 | + 		std::string temporaryPath;
      |    359 | + #ifdef _WIN32
      |    360 | + 		HANDLE temporaryHandle = INVALID_HANDLE_VALUE;
      |    361 | + 		std::wstring wideTemporaryPath;
      |    362 | + 		for (int attempt = 0; attempt < 32; ++attempt)
      |    363 | + 		{
      |    364 | + 			std::vector<unsigned char> randomBytes(16);
      |    365 | + 			if (!secureRandomBytes(randomBytes))
      |    366 | + 				break;
      |    367 | + 			temporaryPath = path + ".tmp-" + hexEncode(randomBytes);
      |    368 | + 			wideTemporaryPath = Utils::String::convertToWideString(temporaryPath);
      |    369 | + 			temporaryHandle = CreateFileW(wideTemporaryPath.c_str(), GENERIC_WRITE, 0,
      |    370 | + 				nullptr, CREATE_NEW,
      |    371 | + 				FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_WRITE_THROUGH | FILE_FLAG_OPEN_REPARSE_POINT,
      |    372 | + 				nullptr);
      |    373 | + 			if (temporaryHandle != INVALID_HANDLE_VALUE)
      |    374 | + 				break;
      |    375 | + 			const DWORD error = GetLastError();
      |    376 | + 			if (error != ERROR_FILE_EXISTS && error != ERROR_ALREADY_EXISTS)
      |    377 | + 				break;
      |    378 | + 		}
      |    379 | + 		if (temporaryHandle == INVALID_HANDLE_VALUE)
      |    380 | + 			return false;
      |    381 | + 
      |    382 | + 		bool stored = true;
      |    383 | + 		std::size_t offset = 0;
      |    384 | + 		while (stored && offset < content.size())
      |    385 | + 		{
      |    386 | + 			const DWORD chunk = static_cast<DWORD>(std::min<std::size_t>(
      |    387 | + 				content.size() - offset, 0x7ffff000u));
      |    388 | + 			DWORD written = 0;
      |    389 | + 			stored = WriteFile(temporaryHandle, content.data() + offset, chunk, &written, nullptr) != FALSE
      |    390 | + 				&& written == chunk;
      |    391 | + 			offset += written;
      |    392 | + 		}
      |    393 | + 		stored = stored && FlushFileBuffers(temporaryHandle) != FALSE;
      |    394 | + 		stored = CloseHandle(temporaryHandle) != FALSE && stored;
      |    395 | + 		if (!stored)
      |    396 | + 		{
      |    397 | + 			DeleteFileW(wideTemporaryPath.c_str());
      |    398 | + 			return false;
      |    399 | + 		}
      |    400 | + 
      |    401 | + 		const std::wstring widePath = Utils::String::convertToWideString(path);
      |    402 | + 		if (MoveFileExW(wideTemporaryPath.c_str(), widePath.c_str(),
      |    403 | + 			MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
      |    404 | + 			return true;
      |    405 | + 		DeleteFileW(wideTemporaryPath.c_str());
      |    406 | + #else
      |    407 | + 		int temporaryDescriptor = -1;
      |    408 | + 		for (int attempt = 0; attempt < 32; ++attempt)
      |    409 | + 		{
      |    410 | + 			std::vector<unsigned char> randomBytes(16);
      |    411 | + 			if (!secureRandomBytes(randomBytes))
      |    412 | + 				break;
      |    413 | + 			temporaryPath = path + ".tmp-" + hexEncode(randomBytes);
      |    414 | + 			int flags = O_WRONLY | O_CREAT | O_EXCL;
      |    415 | + #ifdef O_NOFOLLOW
      |    416 | + 			flags |= O_NOFOLLOW;
      |    417 | + #endif
      |    418 | + 			temporaryDescriptor = ::open(temporaryPath.c_str(), flags, 0600);
      |    419 | + 			if (temporaryDescriptor >= 0)
      |    420 | + 				break;
      |    421 | + 			if (errno != EEXIST)
      |    422 | + 				break;
      |    423 | + 		}
      |    424 | + 		if (temporaryDescriptor < 0)
      |    425 | + 			return false;
      |    426 | + 
      |    427 | + 		bool stored = true;
      |    428 | + 		std::size_t offset = 0;
      |    429 | + 		while (stored && offset < content.size())
      |    430 | + 		{
      |    431 | + 			const ssize_t written = ::write(temporaryDescriptor,
      |    432 | + 				content.data() + offset, content.size() - offset);
      |    433 | + 			if (written <= 0)
      |    434 | + 				stored = false;
      |    435 | + 			else
      |    436 | + 				offset += static_cast<std::size_t>(written);
      |    437 | + 		}
      |    438 | + 		stored = stored && ::fsync(temporaryDescriptor) == 0;
      |    439 | + 		stored = (::close(temporaryDescriptor) == 0) && stored;
      |    440 | + 		if (!stored)
      |    441 | + 		{
      |    442 | + 			::unlink(temporaryPath.c_str());
      |    443 | + 			return false;
      |    444 | + 		}
      |    445 | + 		if (::rename(temporaryPath.c_str(), path.c_str()) == 0)
      |    446 | + 			return true;
      |    447 | + 		::unlink(temporaryPath.c_str());
      |    448 | + #endif
      |    449 | + 		return false;
      |    450 | + 	}
      |    451 | + 
      |    452 | + 	bool writeAuthenticationHash(const std::string& encodedHash)
      |    453 | + 	{
      |    454 | + 		if (!isSupportedPasswordHash(encodedHash))
      |    455 | + 			return false;
      |    456 | + 		return atomicWriteText(authenticationFilePath(),
      |    457 | + 			std::string("schemaVersion=1\npasswordHash=") + encodedHash + "\n");
      |    458 | + 	}
      |    459 | + 
      |    460 | + 	RegularFileState loadAuthenticationHash(std::string& encodedHash)
      |    461 | + 	{
      |    462 | + 		const std::string path = authenticationFilePath();
      |    463 | + 		const RegularFileState fileState = inspectRegularFile(path);
      |    464 | + 		if (fileState != RegularFileState::Regular)
      |    465 | + 			return fileState;
      |    466 | + 
      |    467 | + 		std::vector<std::string> lines;
      |    468 | + 		if (!readBoundedTextLines(path, lines))
      |    469 | + 			return RegularFileState::UnsafeOrError;
      |    470 | + 
      |    471 | + 		bool sawSchema = false;
      |    472 | + 		bool sawHash = false;
      |    473 | + 		for (std::size_t index = 0; index < lines.size(); ++index)
      |    474 | + 		{
      |    475 | + 			std::string line = lines[index];
      |    476 | + 			if (index == 0 && line.size() >= 3
      |    477 | + 				&& static_cast<unsigned char>(line[0]) == 0xEF
      |    478 | + 				&& static_cast<unsigned char>(line[1]) == 0xBB
      |    479 | + 				&& static_cast<unsigned char>(line[2]) == 0xBF)
      |    480 | + 				line = line.substr(3);
      |    481 | + 			line = Utils::String::trim(line);
      |    482 | + 			if (line.empty() || line[0] == '#' || line[0] == ';')
      |    483 | + 				continue;
      |    484 | + 
      |    485 | + 			const std::size_t separator = line.find('=');
      |    486 | + 			if (separator == std::string::npos)
      |    487 | + 				return RegularFileState::UnsafeOrError;
      |    488 | + 			const std::string key = Utils::String::toLower(
      |    489 | + 				Utils::String::trim(line.substr(0, separator)));
      |    490 | + 			const std::string value = Utils::String::trim(line.substr(separator + 1));
      |    491 | + 			if (key == "schemaversion")
      |    492 | + 			{
      |    493 | + 				if (sawSchema || value != "1")
      |    494 | + 					return RegularFileState::UnsafeOrError;
      |    495 | + 				sawSchema = true;
      |    496 | + 			}
      |    497 | + 			else if (key == "passwordhash")
      |    498 | + 			{
      |    499 | + 				if (sawHash || !isSupportedPasswordHash(value))
      |    500 | + 					return RegularFileState::UnsafeOrError;
      |    501 | + 				encodedHash = value;
      |    502 | + 				sawHash = true;
      |    503 | + 			}
      |    504 | + 			else
      |    505 | + 				return RegularFileState::UnsafeOrError;
      |    506 | + 		}
      |    507 | + 
      |    508 | + 		return sawSchema && sawHash ? RegularFileState::Regular
      |    509 | + 			: RegularFileState::UnsafeOrError;
      |    510 | + 	}
      |    511 | + 
      |    512 | + 	RegularFileState loadLegacyCredential(ActiveCredential& credential)
      |    513 | + 	{
      |    514 | + 		const std::string path = legacyCredentialFilePath();
      |    515 | + 		const RegularFileState fileState = inspectRegularFile(path);
      |    516 | + 		if (fileState != RegularFileState::Regular)
      |    517 | + 			return fileState;
      |    518 | + 
      |    519 | + 		std::vector<std::string> lines;
      |    520 | + 		if (!readBoundedTextLines(path, lines))
      |    521 | + 			return RegularFileState::UnsafeOrError;
      |    522 | + 
      |    523 | + 		bool sawHash = false;
      |    524 | + 		bool sawPlainText = false;
      |    525 | + 		for (std::size_t index = 0; index < lines.size(); ++index)
      |    526 | + 		{
      |    527 | + 			std::string line = lines[index];
      |    528 | + 			if (index == 0 && line.size() >= 3
      |    529 | + 				&& static_cast<unsigned char>(line[0]) == 0xEF
      |    530 | + 				&& static_cast<unsigned char>(line[1]) == 0xBB
      |    531 | + 				&& static_cast<unsigned char>(line[2]) == 0xBF)
      |    532 | + 				line = line.substr(3);
      |    533 | + 			line = Utils::String::trim(line);
      |    534 | + 			if (line.empty() || line[0] == '#' || line[0] == ';')
      |    535 | + 				continue;
      |    536 | + 
      |    537 | + 			const std::size_t separator = line.find('=');
      |    538 | + 			if (separator == std::string::npos)
      |    539 | + 				continue;
      |    540 | + 			const std::string key = Utils::String::toLower(
      |    541 | + 				Utils::String::trim(line.substr(0, separator)));
      |    542 | + 			const std::string value = Utils::String::trim(line.substr(separator + 1));
      |    543 | + 			if (key == "adminpasswordhash")
      |    544 | + 			{
      |    545 | + 				if (sawHash || sawPlainText || !isSupportedPasswordHash(value))
      |    546 | + 					return RegularFileState::UnsafeOrError;
      |    547 | + 				credential = { CredentialSource::LegacyHash, value };
      |    548 | + 				sawHash = true;
      |    549 | + 			}
      |    550 | + 			else if (key == "adminpassword")
      |    551 | + 			{
      |    552 | + 				if (sawHash || sawPlainText || value.size() < 4 || value.size() > 256)
      |    553 | + 					return RegularFileState::UnsafeOrError;
      |    554 | + 				credential = { CredentialSource::LegacyPlainText, value };
      |    555 | + 				sawPlainText = true;
      |    556 | + 			}
      |    557 | + 		}
      |    558 | + 
      |    559 | + 		return sawHash || sawPlainText ? RegularFileState::Regular
      |    560 | + 			: RegularFileState::UnsafeOrError;
      |    561 | + 	}
      |    562 | + 
      |    563 | + 	ActiveCredential activeCredential()
      |    564 | + 	{
      |    565 | + 		std::string encodedHash;
      |    566 | + 		const RegularFileState authState = loadAuthenticationHash(encodedHash);
      |    567 | + 		if (authState == RegularFileState::Regular)
      |    568 | + 			return { CredentialSource::AuthFile, encodedHash };
      |    569 | + 		if (authState == RegularFileState::UnsafeOrError)
      |    570 | + 			return { CredentialSource::Invalid, std::string() };
      |    571 | + 
      |    572 | + 		ActiveCredential legacy = { CredentialSource::Invalid, std::string() };
      |    573 | + 		const RegularFileState legacyState = loadLegacyCredential(legacy);
      |    574 | + 		if (legacyState == RegularFileState::Regular)
      |    575 | + 			return legacy;
      |    576 | + 		if (legacyState == RegularFileState::UnsafeOrError)
      |    577 | + 			return { CredentialSource::Invalid, std::string() };
      |    578 | + 
      |    579 | + 		return { CredentialSource::DefaultPassword, DEFAULT_PASSWORD };
      |    580 | + 	}
      |    581 | + 
      |    582 | + 	bool verifyCredential(const std::string& password, const ActiveCredential& credential)
      |    583 | + 	{
      |    584 | + 		switch (credential.source)
      |    585 | + 		{
      |    586 | + 		case CredentialSource::DefaultPassword:
      |    587 | + 		case CredentialSource::LegacyPlainText:
      |    588 | + 			return constantTimeEquals(password, credential.value);
      |    589 | + 		case CredentialSource::AuthFile:
      |    590 | + 		case CredentialSource::LegacyHash:
      |    591 | + 			return verifyPasswordHash(password, credential.value);
      |    592 | + 		default:
      |    593 | + 			return false;
      |    594 | + 		}
      |    595 | + 	}
      |    596 | + }
      |    597 | + 
      |    598 | + bool MainMenuAuth::verify(const std::string& password)
      |    599 | + {
      |    600 | + 	const std::string trimmed = Utils::String::trim(password);
      |    601 | + 	if (trimmed.empty())
      |    602 | + 		return false;
      |    603 | + 
      |    604 | + 	const ActiveCredential credential = activeCredential();
      |    605 | + 	const bool verified = verifyCredential(trimmed, credential);
      |    606 | + 	if (verified && (credential.source == CredentialSource::LegacyHash
      |    607 | + 		|| credential.source == CredentialSource::LegacyPlainText))
      |    608 | + 	{
      |    609 | + 		const std::string migratedHash = createPasswordHash(trimmed);
      |    610 | + 		if (migratedHash.empty() || !writeAuthenticationHash(migratedHash))
      |    611 | + 			LOG(LogError) << "[MainMenuAuth] nao foi possivel migrar a credencial administrativa";
      |    612 | + 	}
      |    613 | + 	return verified;
      |    614 | + }
      |    615 | + 
      |    616 | + bool MainMenuAuth::setPassword(const std::string& password)
      |    617 | + {
      |    618 | + 	const std::string trimmed = Utils::String::trim(password);
      |    619 | + 	if (trimmed.size() < 8)
      |    620 | + 		return false;
      |    621 | + 
      |    622 | + 	const std::string encodedHash = createPasswordHash(trimmed);
      |    623 | + 	return !encodedHash.empty() && writeAuthenticationHash(encodedHash);
      |    624 | + }
      |    625 | + 
      |    626 | + bool MainMenuAuth::isUsingDefaultPassword()
      |    627 | + {
      |    628 | + 	return verifyCredential(DEFAULT_PASSWORD, activeCredential());
      |    629 | + }
      |    630 | + 
      |    631 | + bool MainMenuAuth::hasCustomPassword()
      |    632 | + {
      |    633 | + 	const ActiveCredential credential = activeCredential();
      |    634 | + 	return credential.source != CredentialSource::DefaultPassword
      |    635 | + 		&& credential.source != CredentialSource::Invalid;
      |    636 | + }
      |    637 | + 
      |    638 | + bool MainMenuAuth::runSelfTest()
      |    639 | + {
      |    640 | + 	const std::string password = "turborama-auth-self-test";
      |    641 | + 	const std::string encodedHash = createPasswordHash(password);
      |    642 | + 	if (encodedHash.empty() || !isSupportedPasswordHash(encodedHash)
      |    643 | + 		|| !verifyPasswordHash(password, encodedHash)
      |    644 | + 		|| verifyPasswordHash("senha-incorreta", encodedHash)
      |    645 | + 		|| verifyPasswordHash(password, "hash-invalido"))
      |    646 | + 		return false;
      |    647 | + #ifdef _WIN32
      |    648 | + 	return encodedHash.rfind(HASH_PREFIX, 0) == 0;
      |    649 | + #else
      |    650 | + 	return encodedHash.rfind(PORTABLE_HASH_PREFIX, 0) == 0;
      |    651 | + #endif
      |    652 | + }
```

Conferência: 1 trechos, 652 linhas adicionadas e 0 removidas.
