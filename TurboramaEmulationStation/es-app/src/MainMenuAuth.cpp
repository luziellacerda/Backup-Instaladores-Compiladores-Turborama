#include "MainMenuAuth.h"

#include "Log.h"
#include "Paths.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"
#include "utils/md5.h"

#include <algorithm>
#include <cerrno>
#include <cstddef>
#include <cstdlib>
#include <fstream>
#include <iomanip>
#include <locale>
#include <random>
#include <sstream>
#include <vector>

#ifdef _WIN32
#include <windows.h>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")
#else
#include <fcntl.h>
#include <sys/stat.h>
#include <unistd.h>
#endif

namespace
{
	const char* const DEFAULT_PASSWORD = "admin";
	const char* const HASH_PREFIX = "pbkdf2-sha256$";
	const char* const PORTABLE_HASH_PREFIX = "legacy-md5$";
	const unsigned long long PBKDF2_ITERATIONS = 210000;
	const std::size_t MAX_CONFIG_BYTES = 64u * 1024u;
	const std::size_t MAX_CONFIG_LINES = 512u;
	const std::size_t MAX_LINE_BYTES = 4096u;

	enum class RegularFileState
	{
		Missing,
		Regular,
		UnsafeOrError
	};

	enum class CredentialSource
	{
		DefaultPassword,
		AuthFile,
		LegacyHash,
		LegacyPlainText,
		Invalid
	};

	struct ActiveCredential
	{
		CredentialSource source;
		std::string value;
	};

	bool constantTimeEquals(const std::string& left, const std::string& right)
	{
		const std::size_t count = left.size() > right.size() ? left.size() : right.size();
		std::size_t difference = left.size() ^ right.size();
		for (std::size_t index = 0; index < count; ++index)
		{
			const unsigned char leftByte = index < left.size()
				? static_cast<unsigned char>(left[index]) : 0;
			const unsigned char rightByte = index < right.size()
				? static_cast<unsigned char>(right[index]) : 0;
			difference |= static_cast<std::size_t>(leftByte ^ rightByte);
		}
		return difference == 0;
	}

	std::string hexEncode(const std::vector<unsigned char>& bytes)
	{
		std::ostringstream output;
		output.imbue(std::locale::classic());
		output << std::hex << std::setfill('0');
		for (const unsigned char byte : bytes)
			output << std::setw(2) << static_cast<unsigned int>(byte);
		return output.str();
	}

	bool hexDecode(const std::string& text, std::vector<unsigned char>& bytes)
	{
		bytes.clear();
		if (text.empty() || (text.size() % 2) != 0)
			return false;

		bytes.reserve(text.size() / 2);
		for (std::size_t index = 0; index < text.size(); index += 2)
		{
			auto nibble = [](const char value) -> int
			{
				if (value >= '0' && value <= '9') return value - '0';
				if (value >= 'a' && value <= 'f') return value - 'a' + 10;
				if (value >= 'A' && value <= 'F') return value - 'A' + 10;
				return -1;
			};

			const int high = nibble(text[index]);
			const int low = nibble(text[index + 1]);
			if (high < 0 || low < 0)
			{
				bytes.clear();
				return false;
			}
			bytes.push_back(static_cast<unsigned char>((high << 4) | low));
		}
		return true;
	}

	bool isMd5Hash(const std::string& encoded)
	{
		if (encoded.size() != 32)
			return false;
		for (const char value : encoded)
			if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f')
				|| (value >= 'A' && value <= 'F')))
				return false;
		return true;
	}

	bool secureRandomBytes(std::vector<unsigned char>& bytes)
	{
		if (bytes.empty())
			return false;
#ifdef _WIN32
		return BCryptGenRandom(nullptr, bytes.data(), static_cast<ULONG>(bytes.size()),
			BCRYPT_USE_SYSTEM_PREFERRED_RNG) >= 0;
#else
		try
		{
			std::random_device random;
			for (unsigned char& byte : bytes)
				byte = static_cast<unsigned char>(random());
			return true;
		}
		catch (...)
		{
			return false;
		}
#endif
	}

	bool derivePbkdf2Sha256(const std::string& password,
		const std::vector<unsigned char>& salt, const unsigned long long iterations,
		std::vector<unsigned char>& digest)
	{
#ifdef _WIN32
		BCRYPT_ALG_HANDLE algorithm = nullptr;
		if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM,
			nullptr, BCRYPT_ALG_HANDLE_HMAC_FLAG) < 0)
			return false;

		const NTSTATUS status = BCryptDeriveKeyPBKDF2(algorithm,
			reinterpret_cast<PUCHAR>(const_cast<char*>(password.data())),
			static_cast<ULONG>(password.size()),
			const_cast<PUCHAR>(salt.data()), static_cast<ULONG>(salt.size()),
			iterations, digest.data(), static_cast<ULONG>(digest.size()), 0);
		BCryptCloseAlgorithmProvider(algorithm, 0);
		return status >= 0;
#else
		(void)password;
		(void)salt;
		(void)iterations;
		(void)digest;
		return false;
#endif
	}

	bool parsePbkdf2Hash(const std::string& encoded, unsigned long long& iterations,
		std::vector<unsigned char>& salt, std::vector<unsigned char>& digest)
	{
		const std::string prefix(HASH_PREFIX);
		if (encoded.rfind(prefix, 0) != 0)
			return false;

		const std::size_t iterationEnd = encoded.find('$', prefix.size());
		const std::size_t saltEnd = iterationEnd == std::string::npos
			? std::string::npos : encoded.find('$', iterationEnd + 1);
		if (iterationEnd == std::string::npos || saltEnd == std::string::npos
			|| encoded.find('$', saltEnd + 1) != std::string::npos)
			return false;

		const std::string iterationText = encoded.substr(prefix.size(), iterationEnd - prefix.size());
		if (iterationText.empty() || iterationText.size() > 9)
			return false;
		for (const char digit : iterationText)
			if (digit < '0' || digit > '9')
				return false;

		iterations = std::strtoull(iterationText.c_str(), nullptr, 10);
		return iterations >= 100000 && iterations <= 2000000
			&& hexDecode(encoded.substr(iterationEnd + 1, saltEnd - iterationEnd - 1), salt)
			&& salt.size() >= 16 && salt.size() <= 64
			&& hexDecode(encoded.substr(saltEnd + 1), digest)
			&& digest.size() == 32;
	}

	bool isSupportedPasswordHash(const std::string& encoded)
	{
		if (isMd5Hash(encoded))
			return true;
		const std::string portablePrefix(PORTABLE_HASH_PREFIX);
		if (encoded.rfind(portablePrefix, 0) == 0)
			return isMd5Hash(encoded.substr(portablePrefix.size()));

		unsigned long long iterations = 0;
		std::vector<unsigned char> salt;
		std::vector<unsigned char> digest;
		return parsePbkdf2Hash(encoded, iterations, salt, digest);
	}

	std::string createPasswordHash(const std::string& password)
	{
#ifdef _WIN32
		std::vector<unsigned char> salt(16);
		std::vector<unsigned char> digest(32);
		if (!secureRandomBytes(salt)
			|| !derivePbkdf2Sha256(password, salt, PBKDF2_ITERATIONS, digest))
			return std::string();

		return std::string(HASH_PREFIX) + std::to_string(PBKDF2_ITERATIONS)
			+ "$" + hexEncode(salt) + "$" + hexEncode(digest);
#else
		// The customer release is Windows-only. Keep other developer targets
		// functional with the same portable hash already used by legacy menus.
		return std::string(PORTABLE_HASH_PREFIX) + MD5(password).hexdigest();
#endif
	}

	bool verifyPasswordHash(const std::string& password, const std::string& encoded)
	{
		if (isMd5Hash(encoded))
			return constantTimeEquals(MD5(password).hexdigest(), Utils::String::toLower(encoded));

		const std::string portablePrefix(PORTABLE_HASH_PREFIX);
		if (encoded.rfind(portablePrefix, 0) == 0)
			return constantTimeEquals(MD5(password).hexdigest(),
				Utils::String::toLower(encoded.substr(portablePrefix.size())));

		unsigned long long iterations = 0;
		std::vector<unsigned char> salt;
		std::vector<unsigned char> expected;
		if (!parsePbkdf2Hash(encoded, iterations, salt, expected))
			return false;

		std::vector<unsigned char> actual(32);
		return derivePbkdf2Sha256(password, salt, iterations, actual)
			&& constantTimeEquals(hexEncode(actual), hexEncode(expected));
	}

	RegularFileState inspectRegularFile(const std::string& path)
	{
#ifdef _WIN32
		const std::wstring widePath = Utils::String::convertToWideString(path);
		const DWORD attributes = GetFileAttributesW(widePath.c_str());
		if (attributes == INVALID_FILE_ATTRIBUTES)
		{
			const DWORD error = GetLastError();
			return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND
				? RegularFileState::Missing : RegularFileState::UnsafeOrError;
		}
		if ((attributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0)
			return RegularFileState::UnsafeOrError;
		return RegularFileState::Regular;
#else
		struct stat info;
		if (::lstat(path.c_str(), &info) == 0)
			return S_ISREG(info.st_mode) ? RegularFileState::Regular
				: RegularFileState::UnsafeOrError;
		return errno == ENOENT || errno == ENOTDIR
			? RegularFileState::Missing : RegularFileState::UnsafeOrError;
#endif
	}

	bool readBoundedTextLines(const std::string& path, std::vector<std::string>& lines)
	{
		lines.clear();
#ifdef _WIN32
		// Paths are UTF-8 in the frontend. Use the wide MSVC file overload so a
		// Windows account such as "Joao" with accents can read its credential.
		std::ifstream input(Utils::String::convertToWideString(path).c_str(),
			std::ios::in | std::ios::binary);
#else
		std::ifstream input(path, std::ios::in | std::ios::binary);
#endif
		if (!input.is_open())
			return false;

		input.seekg(0, std::ios::end);
		const std::streamoff declaredSize = input.tellg();
		if (declaredSize < 0 || static_cast<unsigned long long>(declaredSize) > MAX_CONFIG_BYTES)
			return false;
		input.seekg(0, std::ios::beg);
		if (!input.good())
			return false;

		std::size_t bytesRead = 0;
		std::string line;
		line.reserve(256);
		char value = 0;
		while (input.get(value))
		{
			if (bytesRead >= MAX_CONFIG_BYTES)
				return false;
			++bytesRead;
			if (value == '\n')
			{
				if (lines.size() >= MAX_CONFIG_LINES)
					return false;
				lines.push_back(line);
				line.clear();
			}
			else
			{
				if (line.size() >= MAX_LINE_BYTES)
					return false;
				line.push_back(value);
			}
		}
		if (input.bad())
			return false;
		if (!line.empty())
		{
			if (lines.size() >= MAX_CONFIG_LINES)
				return false;
			lines.push_back(line);
		}
		return true;
	}

	std::string authenticationFilePath()
	{
		return Utils::FileSystem::combine(Paths::getUserEmulationStationPath(), "main_menu_auth.cfg");
	}

	std::string legacyCredentialFilePath()
	{
		// Read only the former administrator credential once. No financial,
		// payment, rental-time or daemon state is loaded from this file.
		return Utils::FileSystem::combine(Paths::getUserEmulationStationPath(), "arcade_credit.cfg");
	}

	bool atomicWriteText(const std::string& path, const std::string& content)
	{
		if (inspectRegularFile(path) == RegularFileState::UnsafeOrError)
			return false;

		const std::string directory = Utils::FileSystem::getParent(path);
		if (!directory.empty() && !Utils::FileSystem::createDirectory(directory))
			return false;

		std::string temporaryPath;
#ifdef _WIN32
		HANDLE temporaryHandle = INVALID_HANDLE_VALUE;
		std::wstring wideTemporaryPath;
		for (int attempt = 0; attempt < 32; ++attempt)
		{
			std::vector<unsigned char> randomBytes(16);
			if (!secureRandomBytes(randomBytes))
				break;
			temporaryPath = path + ".tmp-" + hexEncode(randomBytes);
			wideTemporaryPath = Utils::String::convertToWideString(temporaryPath);
			temporaryHandle = CreateFileW(wideTemporaryPath.c_str(), GENERIC_WRITE, 0,
				nullptr, CREATE_NEW,
				FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_WRITE_THROUGH | FILE_FLAG_OPEN_REPARSE_POINT,
				nullptr);
			if (temporaryHandle != INVALID_HANDLE_VALUE)
				break;
			const DWORD error = GetLastError();
			if (error != ERROR_FILE_EXISTS && error != ERROR_ALREADY_EXISTS)
				break;
		}
		if (temporaryHandle == INVALID_HANDLE_VALUE)
			return false;

		bool stored = true;
		std::size_t offset = 0;
		while (stored && offset < content.size())
		{
			const DWORD chunk = static_cast<DWORD>(std::min<std::size_t>(
				content.size() - offset, 0x7ffff000u));
			DWORD written = 0;
			stored = WriteFile(temporaryHandle, content.data() + offset, chunk, &written, nullptr) != FALSE
				&& written == chunk;
			offset += written;
		}
		stored = stored && FlushFileBuffers(temporaryHandle) != FALSE;
		stored = CloseHandle(temporaryHandle) != FALSE && stored;
		if (!stored)
		{
			DeleteFileW(wideTemporaryPath.c_str());
			return false;
		}

		const std::wstring widePath = Utils::String::convertToWideString(path);
		if (MoveFileExW(wideTemporaryPath.c_str(), widePath.c_str(),
			MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
			return true;
		DeleteFileW(wideTemporaryPath.c_str());
#else
		int temporaryDescriptor = -1;
		for (int attempt = 0; attempt < 32; ++attempt)
		{
			std::vector<unsigned char> randomBytes(16);
			if (!secureRandomBytes(randomBytes))
				break;
			temporaryPath = path + ".tmp-" + hexEncode(randomBytes);
			int flags = O_WRONLY | O_CREAT | O_EXCL;
#ifdef O_NOFOLLOW
			flags |= O_NOFOLLOW;
#endif
			temporaryDescriptor = ::open(temporaryPath.c_str(), flags, 0600);
			if (temporaryDescriptor >= 0)
				break;
			if (errno != EEXIST)
				break;
		}
		if (temporaryDescriptor < 0)
			return false;

		bool stored = true;
		std::size_t offset = 0;
		while (stored && offset < content.size())
		{
			const ssize_t written = ::write(temporaryDescriptor,
				content.data() + offset, content.size() - offset);
			if (written <= 0)
				stored = false;
			else
				offset += static_cast<std::size_t>(written);
		}
		stored = stored && ::fsync(temporaryDescriptor) == 0;
		stored = (::close(temporaryDescriptor) == 0) && stored;
		if (!stored)
		{
			::unlink(temporaryPath.c_str());
			return false;
		}
		if (::rename(temporaryPath.c_str(), path.c_str()) == 0)
			return true;
		::unlink(temporaryPath.c_str());
#endif
		return false;
	}

	bool writeAuthenticationHash(const std::string& encodedHash)
	{
		if (!isSupportedPasswordHash(encodedHash))
			return false;
		return atomicWriteText(authenticationFilePath(),
			std::string("schemaVersion=1\npasswordHash=") + encodedHash + "\n");
	}

	RegularFileState loadAuthenticationHash(std::string& encodedHash)
	{
		const std::string path = authenticationFilePath();
		const RegularFileState fileState = inspectRegularFile(path);
		if (fileState != RegularFileState::Regular)
			return fileState;

		std::vector<std::string> lines;
		if (!readBoundedTextLines(path, lines))
			return RegularFileState::UnsafeOrError;

		bool sawSchema = false;
		bool sawHash = false;
		for (std::size_t index = 0; index < lines.size(); ++index)
		{
			std::string line = lines[index];
			if (index == 0 && line.size() >= 3
				&& static_cast<unsigned char>(line[0]) == 0xEF
				&& static_cast<unsigned char>(line[1]) == 0xBB
				&& static_cast<unsigned char>(line[2]) == 0xBF)
				line = line.substr(3);
			line = Utils::String::trim(line);
			if (line.empty() || line[0] == '#' || line[0] == ';')
				continue;

			const std::size_t separator = line.find('=');
			if (separator == std::string::npos)
				return RegularFileState::UnsafeOrError;
			const std::string key = Utils::String::toLower(
				Utils::String::trim(line.substr(0, separator)));
			const std::string value = Utils::String::trim(line.substr(separator + 1));
			if (key == "schemaversion")
			{
				if (sawSchema || value != "1")
					return RegularFileState::UnsafeOrError;
				sawSchema = true;
			}
			else if (key == "passwordhash")
			{
				if (sawHash || !isSupportedPasswordHash(value))
					return RegularFileState::UnsafeOrError;
				encodedHash = value;
				sawHash = true;
			}
			else
				return RegularFileState::UnsafeOrError;
		}

		return sawSchema && sawHash ? RegularFileState::Regular
			: RegularFileState::UnsafeOrError;
	}

	RegularFileState loadLegacyCredential(ActiveCredential& credential)
	{
		const std::string path = legacyCredentialFilePath();
		const RegularFileState fileState = inspectRegularFile(path);
		if (fileState != RegularFileState::Regular)
			return fileState;

		std::vector<std::string> lines;
		if (!readBoundedTextLines(path, lines))
			return RegularFileState::UnsafeOrError;

		bool sawHash = false;
		bool sawPlainText = false;
		for (std::size_t index = 0; index < lines.size(); ++index)
		{
			std::string line = lines[index];
			if (index == 0 && line.size() >= 3
				&& static_cast<unsigned char>(line[0]) == 0xEF
				&& static_cast<unsigned char>(line[1]) == 0xBB
				&& static_cast<unsigned char>(line[2]) == 0xBF)
				line = line.substr(3);
			line = Utils::String::trim(line);
			if (line.empty() || line[0] == '#' || line[0] == ';')
				continue;

			const std::size_t separator = line.find('=');
			if (separator == std::string::npos)
				continue;
			const std::string key = Utils::String::toLower(
				Utils::String::trim(line.substr(0, separator)));
			const std::string value = Utils::String::trim(line.substr(separator + 1));
			if (key == "adminpasswordhash")
			{
				if (sawHash || sawPlainText || !isSupportedPasswordHash(value))
					return RegularFileState::UnsafeOrError;
				credential = { CredentialSource::LegacyHash, value };
				sawHash = true;
			}
			else if (key == "adminpassword")
			{
				if (sawHash || sawPlainText || value.size() < 4 || value.size() > 256)
					return RegularFileState::UnsafeOrError;
				credential = { CredentialSource::LegacyPlainText, value };
				sawPlainText = true;
			}
		}

		return sawHash || sawPlainText ? RegularFileState::Regular
			: RegularFileState::UnsafeOrError;
	}

	ActiveCredential activeCredential()
	{
		std::string encodedHash;
		const RegularFileState authState = loadAuthenticationHash(encodedHash);
		if (authState == RegularFileState::Regular)
			return { CredentialSource::AuthFile, encodedHash };
		if (authState == RegularFileState::UnsafeOrError)
			return { CredentialSource::Invalid, std::string() };

		ActiveCredential legacy = { CredentialSource::Invalid, std::string() };
		const RegularFileState legacyState = loadLegacyCredential(legacy);
		if (legacyState == RegularFileState::Regular)
			return legacy;
		if (legacyState == RegularFileState::UnsafeOrError)
			return { CredentialSource::Invalid, std::string() };

		return { CredentialSource::DefaultPassword, DEFAULT_PASSWORD };
	}

	bool verifyCredential(const std::string& password, const ActiveCredential& credential)
	{
		switch (credential.source)
		{
		case CredentialSource::DefaultPassword:
		case CredentialSource::LegacyPlainText:
			return constantTimeEquals(password, credential.value);
		case CredentialSource::AuthFile:
		case CredentialSource::LegacyHash:
			return verifyPasswordHash(password, credential.value);
		default:
			return false;
		}
	}
}

bool MainMenuAuth::verify(const std::string& password)
{
	const std::string trimmed = Utils::String::trim(password);
	if (trimmed.empty())
		return false;

	const ActiveCredential credential = activeCredential();
	const bool verified = verifyCredential(trimmed, credential);
	if (verified && (credential.source == CredentialSource::LegacyHash
		|| credential.source == CredentialSource::LegacyPlainText))
	{
		const std::string migratedHash = createPasswordHash(trimmed);
		if (migratedHash.empty() || !writeAuthenticationHash(migratedHash))
			LOG(LogError) << "[MainMenuAuth] nao foi possivel migrar a credencial administrativa";
	}
	return verified;
}

bool MainMenuAuth::setPassword(const std::string& password)
{
	const std::string trimmed = Utils::String::trim(password);
	if (trimmed.size() < 8)
		return false;

	const std::string encodedHash = createPasswordHash(trimmed);
	return !encodedHash.empty() && writeAuthenticationHash(encodedHash);
}

bool MainMenuAuth::isUsingDefaultPassword()
{
	return verifyCredential(DEFAULT_PASSWORD, activeCredential());
}

bool MainMenuAuth::hasCustomPassword()
{
	const ActiveCredential credential = activeCredential();
	return credential.source != CredentialSource::DefaultPassword
		&& credential.source != CredentialSource::Invalid;
}

bool MainMenuAuth::runSelfTest()
{
	const std::string password = "turborama-auth-self-test";
	const std::string encodedHash = createPasswordHash(password);
	if (encodedHash.empty() || !isSupportedPasswordHash(encodedHash)
		|| !verifyPasswordHash(password, encodedHash)
		|| verifyPasswordHash("senha-incorreta", encodedHash)
		|| verifyPasswordHash(password, "hash-invalido"))
		return false;
#ifdef _WIN32
	return encodedHash.rfind(HASH_PREFIX, 0) == 0;
#else
	return encodedHash.rfind(PORTABLE_HASH_PREFIX, 0) == 0;
#endif
}
