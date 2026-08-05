#include "PixAgentManager.h"

#include "Log.h"
#include "Paths.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"

#include <algorithm>
#include <cctype>
#include <cwctype>
#include <ctime>
#include <iomanip>
#include <limits>
#include <sstream>
#include <vector>
#include <rapidjson/document.h>
#include <rapidjson/stringbuffer.h>
#include <rapidjson/writer.h>

#ifdef _WIN32
#include <windows.h>
#include <bcrypt.h>
#include <wincrypt.h>
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "bcrypt.lib")
#endif

namespace
{
	std::string settingsFile()
	{
		return Utils::FileSystem::combine(PixAgentManager::bridgeDirectory(), "owner-settings.json");
	}

	std::string secretFile()
	{
		return Utils::FileSystem::combine(PixAgentManager::bridgeDirectory(), "secret.dat");
	}

	std::string statusFile()
	{
		return Utils::FileSystem::combine(PixAgentManager::bridgeDirectory(), "agent-status.json");
	}

	std::string setupStatusFile()
	{
		return Utils::FileSystem::combine(PixAgentManager::bridgeDirectory(), "owner-setup-status.json");
	}

	std::string stopRequestFile()
	{
		return Utils::FileSystem::combine(PixAgentManager::bridgeDirectory(), "agent-stop.request");
	}

	std::string agentDirectory()
	{
		return Utils::FileSystem::combine(Paths::getExePath(), "pix-agent");
	}

	std::string agentAssembly()
	{
		return Utils::FileSystem::combine(agentDirectory(), "TurboRamaPixAgent.dll");
	}

	std::string agentAppHost()
	{
		return Utils::FileSystem::combine(agentDirectory(), "TurboRamaPixAgent.exe");
	}

	std::string privateDotnet()
	{
		return Utils::FileSystem::combine(agentDirectory(), "runtime/dotnet.exe");
	}

	bool agentIsInstalled()
	{
		if (Utils::FileSystem::exists(privateDotnet()))
			return Utils::FileSystem::exists(agentAssembly());
		return Utils::FileSystem::exists(agentAppHost());
	}

	std::string jsonString(const rapidjson::Value& object, const char* name, const std::string& fallback = {})
	{
		if (!object.IsObject() || !object.HasMember(name) || !object[name].IsString()) return fallback;
		return object[name].GetString();
	}

	bool jsonBool(const rapidjson::Value& object, const char* name, bool fallback = false)
	{
		if (!object.IsObject() || !object.HasMember(name) || !object[name].IsBool()) return fallback;
		return object[name].GetBool();
	}

	long long jsonLong(const rapidjson::Value& object, const char* name, long long fallback = 0)
	{
		if (!object.IsObject() || !object.HasMember(name) || !object[name].IsInt64()) return fallback;
		return object[name].GetInt64();
	}

	std::string base64Encode(const unsigned char* data, size_t size)
	{
		static const char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
		std::string output;
		output.reserve(((size + 2) / 3) * 4);
		for (size_t i = 0; i < size; i += 3)
		{
			const unsigned int first = data[i];
			const unsigned int second = i + 1 < size ? data[i + 1] : 0;
			const unsigned int third = i + 2 < size ? data[i + 2] : 0;
			const unsigned int value = (first << 16) | (second << 8) | third;
			output.push_back(alphabet[(value >> 18) & 63]);
			output.push_back(alphabet[(value >> 12) & 63]);
			output.push_back(i + 1 < size ? alphabet[(value >> 6) & 63] : '=');
			output.push_back(i + 2 < size ? alphabet[value & 63] : '=');
		}
		return output;
	}

	bool writeAtomically(const std::string& destination, const std::string& contents, std::string& error)
	{
		Utils::FileSystem::createDirectory(Utils::FileSystem::getParent(destination));
		const std::string temporary = destination + ".new";
		Utils::FileSystem::writeAllText(temporary, contents);
		if (!Utils::FileSystem::exists(temporary)
			|| Utils::FileSystem::readAllText(temporary) != contents)
		{
			error = "Nao foi possivel gravar a configuracao PIX.";
			return false;
		}
#ifdef _WIN32
		const std::wstring from = Utils::String::convertToWideString(temporary);
		const std::wstring to = Utils::String::convertToWideString(destination);
		if (!MoveFileExW(from.c_str(), to.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
		{
			Utils::FileSystem::removeFile(temporary);
			error = "O Windows nao conseguiu finalizar a configuracao PIX.";
			return false;
		}
#else
		if (!Utils::FileSystem::renameFile(temporary, destination, true))
		{
			error = "Nao foi possivel finalizar a configuracao PIX.";
			return false;
		}
#endif
		return true;
	}

	bool onlyLettersAndNumbers(const std::string& value, size_t maximum)
	{
		return !value.empty() && value.size() <= maximum && std::all_of(value.begin(), value.end(), [](unsigned char ch) {
			return std::isalnum(ch) != 0;
		});
	}

	bool validPort(const std::string& value)
	{
		if (value.empty() || value.size() > 5
			|| !std::all_of(value.begin(), value.end(), [](unsigned char ch) { return std::isdigit(ch) != 0; })) return false;
		try
		{
			const int port = std::stoi(value);
			return port >= 1 && port <= 65535;
		}
		catch (...) { return false; }
	}

	bool isIpv4Loopback(const std::string& host)
	{
		std::istringstream input(host);
		std::string part;
		int index = 0;
		while (std::getline(input, part, '.'))
		{
			if (index >= 4 || part.empty() || part.size() > 3
				|| !std::all_of(part.begin(), part.end(), [](unsigned char ch) { return std::isdigit(ch) != 0; })) return false;
			if (part.size() > 1 && part[0] == '0') return false;
			const int octet = std::stoi(part);
			if (octet > 255 || (index == 0 && octet != 127)) return false;
			index++;
		}
		return index == 4;
	}

	bool validAdapterBaseUrl(const std::string& value)
	{
		if (value.size() < 10 || value.size() > 2048
			|| value.find_first_of("?#\r\n\t") != std::string::npos
			|| std::any_of(value.begin(), value.end(), [](unsigned char ch) { return ch < 32 || ch == 127 || ch == '\\'; })) return false;
		std::string lower = value;
		std::transform(lower.begin(), lower.end(), lower.begin(), [](unsigned char ch) { return (char)std::tolower(ch); });
		const bool https = lower.rfind("https://", 0) == 0;
		const bool http = lower.rfind("http://", 0) == 0;
		if (!https && !http) return false;
		const size_t authorityStart = https ? 8 : 7;
		const size_t pathStart = value.find('/', authorityStart);
		const std::string authority = value.substr(authorityStart,
			pathStart == std::string::npos ? std::string::npos : pathStart - authorityStart);
		if (authority.empty() || authority.find('@') != std::string::npos) return false;

		std::string host;
		std::string port;
		bool portSpecified = false;
		if (authority[0] == '[')
		{
			const size_t close = authority.find(']');
			if (close == std::string::npos || close == 1) return false;
			host = authority.substr(1, close - 1);
			if (close + 1 < authority.size())
			{
				if (authority[close + 1] != ':') return false;
				portSpecified = true;
				port = authority.substr(close + 2);
			}
		}
		else
		{
			const size_t colon = authority.rfind(':');
			if (colon != std::string::npos)
			{
				if (authority.find(':') != colon) return false;
				portSpecified = true;
				host = authority.substr(0, colon);
				port = authority.substr(colon + 1);
			}
			else host = authority;
		}
		if (host.empty() || (portSpecified && !validPort(port))) return false;
		std::transform(host.begin(), host.end(), host.begin(), [](unsigned char ch) { return (char)std::tolower(ch); });
		if (!std::all_of(host.begin(), host.end(), [](unsigned char ch) {
			return std::isalnum(ch) != 0 || ch == '.' || ch == '-' || ch == ':';
		})) return false;
		if (!https && host != "localhost" && host != "::1" && !isIpv4Loopback(host)) return false;
		return pathStart == std::string::npos || value[pathStart] == '/';
	}

#ifdef _WIN32
	const long long agentHeartbeatTimeoutSeconds = 90;
	const long long agentStartupGraceSeconds = 90;
	// Primeira inicializacao pode criar chaves/ACLs sob HDD e antivirus lentos.
	// O PID continua retido e autenticado durante toda a espera.
	const DWORD agentIdentityStartupTimeoutMs = 90000;
	const wchar_t* managerTokenEnvironment = L"TURBORAMA_PIX_MANAGER_TOKEN";
	const wchar_t* daemonSingletonMutex = L"Local\\TurboRamaPixAgent-Daemon-v1";
	DWORD expectedDaemonPid = 0;
	ULONGLONG expectedDaemonCreationFileTime = 0;
	std::string expectedDaemonTokenHash;

	struct AgentStatus
	{
		DWORD pid = 0;
		long long updatedAt = 0;
		ULONGLONG creationFileTime = 0;
		std::string mode;
		std::string managerTokenHash;
		std::string state;
		bool ready = false;
	};

	enum class AgentStatusReadResult
	{
		Missing,
		Invalid,
		Unknown,
		Valid
	};

	enum class DaemonLookupResult
	{
		Absent,
		Unknown,
		Found
	};

	bool isHexDigest(const std::string& value)
	{
		return value.size() == 64 && std::all_of(value.begin(), value.end(), [](unsigned char ch) {
			return std::isxdigit(ch) != 0;
		});
	}

	std::string lowerAscii(std::string value)
	{
		std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
			return (char)std::tolower(ch);
		});
		return value;
	}

	std::string sha256Hex(const std::string& value)
	{
		BCRYPT_ALG_HANDLE algorithm = nullptr;
		BCRYPT_HASH_HANDLE hash = nullptr;
		DWORD objectSize = 0;
		DWORD received = 0;
		std::vector<unsigned char> object;
		std::vector<unsigned char> digest(32);
		if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) return {};
		if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, (PUCHAR)&objectSize,
			sizeof(objectSize), &received, 0) < 0)
		{
			BCryptCloseAlgorithmProvider(algorithm, 0);
			return {};
		}
		object.resize(objectSize);
		if (BCryptCreateHash(algorithm, &hash, object.data(), objectSize, nullptr, 0, 0) < 0)
		{
			BCryptCloseAlgorithmProvider(algorithm, 0);
			return {};
		}
		NTSTATUS status = BCryptHashData(hash, (PUCHAR)value.data(), (ULONG)value.size(), 0);
		if (status >= 0) status = BCryptFinishHash(hash, digest.data(), (ULONG)digest.size(), 0);
		BCryptDestroyHash(hash);
		BCryptCloseAlgorithmProvider(algorithm, 0);
		if (status < 0) return {};
		std::ostringstream output;
		output << std::hex << std::setfill('0');
		for (const unsigned char byte : digest) output << std::setw(2) << (int)byte;
		return output.str();
	}

	bool generateManagerToken(std::string& token)
	{
		unsigned char bytes[32]{};
		const NTSTATUS generated = BCryptGenRandom(nullptr, bytes, sizeof(bytes), BCRYPT_USE_SYSTEM_PREFERRED_RNG);
		if (generated < 0)
		{
			SecureZeroMemory(bytes, sizeof(bytes));
			return false;
		}
		std::ostringstream output;
		output << std::hex << std::setfill('0');
		for (const unsigned char byte : bytes) output << std::setw(2) << (int)byte;
		SecureZeroMemory(bytes, sizeof(bytes));
		token = output.str();
		return token.size() == 64;
	}

	std::wstring daemonMutexName(DWORD pid)
	{
		return L"Local\\TurboRamaPixAgent-Daemon-v1-" + std::to_wstring(pid);
	}

	DaemonLookupResult daemonMutexState(DWORD pid)
	{
		if (pid == 0) return DaemonLookupResult::Unknown;
		HANDLE mutex = OpenMutexW(SYNCHRONIZE, FALSE, daemonMutexName(pid).c_str());
		if (mutex == nullptr)
		{
			const DWORD error = GetLastError();
			return error == ERROR_FILE_NOT_FOUND
				? DaemonLookupResult::Absent : DaemonLookupResult::Unknown;
		}
		CloseHandle(mutex);
		return DaemonLookupResult::Found;
	}

	DaemonLookupResult daemonSingletonMutexState()
	{
		HANDLE mutex = OpenMutexW(SYNCHRONIZE, FALSE, daemonSingletonMutex);
		if (mutex == nullptr)
		{
			const DWORD error = GetLastError();
			return error == ERROR_FILE_NOT_FOUND
				? DaemonLookupResult::Absent : DaemonLookupResult::Unknown;
		}
		CloseHandle(mutex);
		return DaemonLookupResult::Found;
	}

	bool buildDaemonEnvironment(const std::string& token, std::vector<wchar_t>& environment)
	{
		environment.clear();
		LPWCH inherited = GetEnvironmentStringsW();
		if (inherited == nullptr) return false;
		const std::wstring prefix = std::wstring(managerTokenEnvironment) + L"=";
		std::vector<std::wstring> entries;
		for (const wchar_t* current = inherited; *current != L'\0'; current += wcslen(current) + 1)
		{
			const size_t length = wcslen(current);
			if (length >= prefix.size() && _wcsnicmp(current, prefix.c_str(), prefix.size()) == 0)
				continue;
			entries.emplace_back(current, length);
		}
		FreeEnvironmentStringsW(inherited);
		entries.push_back(prefix + Utils::String::convertToWideString(token));
		std::sort(entries.begin(), entries.end(), [](const std::wstring& left, const std::wstring& right) {
			return _wcsicmp(left.c_str(), right.c_str()) < 0;
		});
		for (const auto& entry : entries)
		{
			environment.insert(environment.end(), entry.begin(), entry.end());
			environment.push_back(L'\0');
		}
		environment.push_back(L'\0');
		return true;
	}

	std::wstring normalizedWindowsPath(const std::string& value)
	{
		wchar_t full[MAX_PATH * 4]{};
		const std::wstring wide = Utils::String::convertToWideString(value);
		const DWORD length = GetFullPathNameW(wide.c_str(), (DWORD)(sizeof(full) / sizeof(full[0])), full, nullptr);
		std::wstring normalized = length > 0 && length < (sizeof(full) / sizeof(full[0])) ? full : wide;
		std::replace(normalized.begin(), normalized.end(), L'/', L'\\');
		std::transform(normalized.begin(), normalized.end(), normalized.begin(), ::towlower);
		return normalized;
	}

	AgentStatusReadResult readAgentStatus(AgentStatus& status)
	{
		status = AgentStatus{};
		const std::wstring path = Utils::String::convertToWideString(statusFile());
		HANDLE file = CreateFileW(path.c_str(), GENERIC_READ,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (file == INVALID_HANDLE_VALUE)
		{
			const DWORD error = GetLastError();
			return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND
				? AgentStatusReadResult::Missing : AgentStatusReadResult::Unknown;
		}
		BY_HANDLE_FILE_INFORMATION information{};
		if (!GetFileInformationByHandle(file, &information))
		{
			CloseHandle(file);
			return AgentStatusReadResult::Unknown;
		}
		if ((information.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0
			|| (information.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
		{
			CloseHandle(file);
			return AgentStatusReadResult::Invalid;
		}
		LARGE_INTEGER size{};
		if (!GetFileSizeEx(file, &size))
		{
			CloseHandle(file);
			return AgentStatusReadResult::Unknown;
		}
		if (size.QuadPart <= 0 || size.QuadPart > 16384)
		{
			CloseHandle(file);
			return AgentStatusReadResult::Invalid;
		}
		std::string text((size_t)size.QuadPart, '\0');
		DWORD received = 0;
		const bool read = ReadFile(file, text.data(), (DWORD)text.size(), &received, nullptr) != FALSE;
		CloseHandle(file);
		if (!read || received != text.size()) return AgentStatusReadResult::Unknown;
		rapidjson::Document document;
		if (document.Parse(text.c_str()).HasParseError() || !document.IsObject()
			|| !document.HasMember("schemaVersion") || !document["schemaVersion"].IsInt()
			|| document["schemaVersion"].GetInt() != 2
			|| !document.HasMember("processId") || !document["processId"].IsUint()
			|| !document.HasMember("processStartFileTimeUtc") || !document["processStartFileTimeUtc"].IsUint64()
			|| !document.HasMember("updatedAtUnixSeconds") || !document["updatedAtUnixSeconds"].IsInt64()
			|| !document.HasMember("mode") || !document["mode"].IsString()
			|| !document.HasMember("managerTokenHash") || !document["managerTokenHash"].IsString()
			|| !document.HasMember("state") || !document["state"].IsString()
			|| !document.HasMember("ready") || !document["ready"].IsBool())
			return AgentStatusReadResult::Invalid;
		status.pid = document["processId"].GetUint();
		status.creationFileTime = document["processStartFileTimeUtc"].GetUint64();
		status.updatedAt = document["updatedAtUnixSeconds"].GetInt64();
		status.mode = document["mode"].GetString();
		status.managerTokenHash = lowerAscii(document["managerTokenHash"].GetString());
		status.state = document["state"].GetString();
		status.ready = document["ready"].GetBool();
		if (status.pid == 0 || status.creationFileTime == 0 || status.updatedAt <= 0 || status.mode != "daemon"
			|| !isHexDigest(status.managerTokenHash) || status.state.empty() || status.state.size() > 64)
			return AgentStatusReadResult::Invalid;
		return AgentStatusReadResult::Valid;
	}

	ULONGLONG fileTimeValue(const FILETIME& value)
	{
		ULARGE_INTEGER converted{};
		converted.LowPart = value.dwLowDateTime;
		converted.HighPart = value.dwHighDateTime;
		return converted.QuadPart;
	}

	DaemonLookupResult validateProcessHandle(HANDLE process, const AgentStatus& status)
	{
		if (process == nullptr) return DaemonLookupResult::Unknown;
		DWORD exitCode = 0;
		if (!GetExitCodeProcess(process, &exitCode)) return DaemonLookupResult::Unknown;
		if (exitCode != STILL_ACTIVE) return DaemonLookupResult::Absent;
		wchar_t path[MAX_PATH * 4]{};
		DWORD size = (DWORD)(sizeof(path) / sizeof(path[0]));
		if (!QueryFullProcessImageNameW(process, 0, path, &size)) return DaemonLookupResult::Unknown;
		if (normalizedWindowsPath(Utils::String::convertFromWideString(path))
			!= normalizedWindowsPath(PixAgentManager::agentExecutable())) return DaemonLookupResult::Absent;
		FILETIME creation{}, exit{}, kernel{}, user{};
		if (!GetProcessTimes(process, &creation, &exit, &kernel, &user)) return DaemonLookupResult::Unknown;
		if (fileTimeValue(creation) != status.creationFileTime) return DaemonLookupResult::Absent;
		return DaemonLookupResult::Found;
	}

	DaemonLookupResult openAndValidateDaemon(const AgentStatus& status, HANDLE* openedProcess = nullptr)
	{
		if (openedProcess) *openedProcess = nullptr;
		HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE
			| (openedProcess ? PROCESS_TERMINATE : 0), FALSE, status.pid);
		if (process == nullptr)
		{
			const DWORD error = GetLastError();
			return error == ERROR_INVALID_PARAMETER
				? DaemonLookupResult::Absent : DaemonLookupResult::Unknown;
		}
		const DaemonLookupResult processState = validateProcessHandle(process, status);
		if (processState != DaemonLookupResult::Found)
		{
			CloseHandle(process);
			return processState;
		}
		const DaemonLookupResult mutexState = daemonMutexState(status.pid);
		if (mutexState != DaemonLookupResult::Found)
		{
			CloseHandle(process);
			// A imagem correta sem o mutex de identidade nao e uma ausencia segura:
			// pode ser um one-shot ou um daemon ainda inicializando.
			return DaemonLookupResult::Unknown;
		}
		if (openedProcess) *openedProcess = process;
		else CloseHandle(process);
		return DaemonLookupResult::Found;
	}

	DaemonLookupResult lookupDaemon(AgentStatus& status, DWORD requiredPid = 0,
		ULONGLONG requiredCreationFileTime = 0, const std::string& requiredTokenHash = {},
		HANDLE* openedProcess = nullptr)
	{
		if (openedProcess) *openedProcess = nullptr;
		const AgentStatusReadResult read = readAgentStatus(status);
		if (read == AgentStatusReadResult::Unknown) return DaemonLookupResult::Unknown;
		if (read != AgentStatusReadResult::Valid)
		{
			const DaemonLookupResult singletonState = daemonSingletonMutexState();
			if (singletonState != DaemonLookupResult::Absent) return DaemonLookupResult::Unknown;
			if (expectedDaemonPid == 0) return DaemonLookupResult::Absent;
			AgentStatus expected;
			expected.pid = expectedDaemonPid;
			expected.creationFileTime = expectedDaemonCreationFileTime;
			const DaemonLookupResult expectedState = openAndValidateDaemon(expected);
			if (expectedState != DaemonLookupResult::Absent) return DaemonLookupResult::Unknown;
			expectedDaemonPid = 0;
			expectedDaemonCreationFileTime = 0;
			expectedDaemonTokenHash.clear();
			return DaemonLookupResult::Absent;
		}
		if (requiredPid != 0 && status.pid != requiredPid) return DaemonLookupResult::Unknown;
		if (requiredCreationFileTime != 0 && status.creationFileTime != requiredCreationFileTime)
			return DaemonLookupResult::Unknown;
		if (!requiredTokenHash.empty()
			&& status.managerTokenHash != lowerAscii(requiredTokenHash)) return DaemonLookupResult::Unknown;
		if (expectedDaemonPid != 0)
		{
			AgentStatus expected = status;
			expected.pid = expectedDaemonPid;
			expected.creationFileTime = expectedDaemonCreationFileTime;
			const DaemonLookupResult expectedState = openAndValidateDaemon(expected);
			if (expectedState == DaemonLookupResult::Found
				&& (status.pid != expectedDaemonPid
					|| status.creationFileTime != expectedDaemonCreationFileTime
					|| status.managerTokenHash != expectedDaemonTokenHash))
				return DaemonLookupResult::Unknown;
			if (expectedState == DaemonLookupResult::Unknown)
				return DaemonLookupResult::Unknown;
			if (expectedState == DaemonLookupResult::Absent)
			{
				expectedDaemonPid = 0;
				expectedDaemonCreationFileTime = 0;
				expectedDaemonTokenHash.clear();
			}
		}
		const DaemonLookupResult processState = openAndValidateDaemon(status, openedProcess);
		const DaemonLookupResult singletonState = daemonSingletonMutexState();
		if (processState == DaemonLookupResult::Absent)
			return singletonState == DaemonLookupResult::Absent
				? DaemonLookupResult::Absent : DaemonLookupResult::Unknown;
		if (processState != DaemonLookupResult::Found) return DaemonLookupResult::Unknown;
		if (singletonState == DaemonLookupResult::Found)
			return DaemonLookupResult::Found;
		if (openedProcess && *openedProcess)
		{
			CloseHandle(*openedProcess);
			*openedProcess = nullptr;
		}
		return DaemonLookupResult::Unknown;
	}

	long long processAgeSeconds(ULONGLONG creationFileTime)
	{
		FILETIME nowFile{};
		GetSystemTimeAsFileTime(&nowFile);
		const ULONGLONG now = fileTimeValue(nowFile);
		return creationFileTime != 0 && now >= creationFileTime
			? (long long)((now - creationFileTime) / 10000000ULL) : -1;
	}

#endif
}

std::string PixAgentManager::bridgeDirectory()
{
	return Utils::FileSystem::combine(Paths::getUserEmulationStationPath(), "pix");
}

std::string PixAgentManager::agentExecutable()
{
#ifdef _WIN32
	return Utils::FileSystem::exists(privateDotnet()) ? privateDotnet() : agentAppHost();
#else
	return {};
#endif
}

PixOwnerSettings PixAgentManager::loadOwnerSettings()
{
	PixOwnerSettings settings;
	const std::string text = Utils::FileSystem::readAllText(settingsFile());
	if (text.empty() || text.size() > 65536) return settings;
	try
	{
		rapidjson::Document document;
		if (document.Parse(text.c_str()).HasParseError() || !document.IsObject()) return settings;
		settings.enabled = jsonBool(document, "enabled");
		settings.provider = jsonString(document, "provider", settings.provider);
		settings.accountId = jsonString(document, "accountId");
		settings.storeExternalId = jsonString(document, "storeExternalId", settings.storeExternalId);
		settings.storeName = jsonString(document, "storeName", settings.storeName);
		settings.posExternalId = jsonString(document, "posExternalId", settings.posExternalId);
		settings.posName = jsonString(document, "posName", settings.posName);
		settings.postalCode = jsonString(document, "postalCode");
		settings.streetNumber = jsonString(document, "streetNumber");
		settings.reference = jsonString(document, "reference", settings.reference);
		settings.adapterBaseUrl = jsonString(document, "adapterBaseUrl", settings.adapterBaseUrl);
		settings.adapterProviderId = jsonString(document, "adapterProviderId", settings.adapterProviderId);
		if (document.HasMember("packagePricesCents") && document["packagePricesCents"].IsObject())
		{
			for (const int minutes : { 15, 30, 45, 60, 120 })
			{
				const std::string key = std::to_string(minutes);
				if (document["packagePricesCents"].HasMember(key.c_str()) && document["packagePricesCents"][key.c_str()].IsInt64())
					settings.pricesCents[minutes] = document["packagePricesCents"][key.c_str()].GetInt64();
			}
		}
	}
	catch (...) { return PixOwnerSettings{}; }
	return settings;
}

bool PixAgentManager::validateOwnerSettings(const PixOwnerSettings& settings, std::string& error)
{
	for (const int minutes : { 15, 30, 45, 60, 120 })
	{
		auto found = settings.pricesCents.find(minutes);
		if (found == settings.pricesCents.end() || found->second < 50 || found->second > 100000000)
		{
			error = "Todos os pacotes precisam de um preco valido.";
			return false;
		}
	}
	std::string provider = settings.provider;
	std::transform(provider.begin(), provider.end(), provider.begin(), [](unsigned char ch) { return (char)std::tolower(ch); });
	if (provider != "mercadopago" && provider != "adapter")
		error = "Selecione Mercado Pago ou Adaptador bancario.";
	else if (provider == "adapter")
	{
		if (settings.adapterProviderId.size() < 2 || settings.adapterProviderId.size() > 64
			|| !std::all_of(settings.adapterProviderId.begin(), settings.adapterProviderId.end(), [](unsigned char ch) {
				return std::isalnum(ch) != 0 || ch == '-' || ch == '_';
			}))
			error = "Informe um identificador valido para o adaptador bancario.";
		else if (!validAdapterBaseUrl(settings.adapterBaseUrl))
			error = "O adaptador deve usar HTTPS ou HTTP local neste computador.";
		return error.empty();
	}
	else if (settings.accountId.size() < 5 || settings.accountId.size() > 24
		|| !std::all_of(settings.accountId.begin(), settings.accountId.end(), [](unsigned char ch) { return std::isdigit(ch) != 0; }))
		error = "Informe o User ID numerico da conta Mercado Pago.";
	else if (!onlyLettersAndNumbers(settings.storeExternalId, 60))
		error = "O identificador da loja deve ter somente letras e numeros.";
	else if (settings.storeName.size() < 2 || settings.storeName.size() >= 60)
		error = "Informe um nome valido para a loja.";
	else if (!onlyLettersAndNumbers(settings.posExternalId, 40))
		error = "O identificador do caixa deve ter somente letras e numeros.";
	else if (settings.posName.size() < 2 || settings.posName.size() >= 45)
		error = "Informe um nome valido para o caixa.";
	else
	{
		std::string cep;
		for (unsigned char ch : settings.postalCode) if (std::isdigit(ch)) cep.push_back((char)ch);
		if (cep.size() != 8) error = "Informe um CEP com 8 numeros.";
		else if (settings.streetNumber.empty() || settings.streetNumber.size() > 20) error = "Informe o numero do estabelecimento.";
		else if (settings.reference.empty() || settings.reference.size() > 120) error = "Informe uma referencia do estabelecimento.";
	}
	return error.empty();
}

bool PixAgentManager::runSelfTest(std::string& error)
{
	PixOwnerSettings base;
	base.provider = "adapter";
	base.adapterProviderId = "banco-teste";
	auto accepted = [&](const std::string& url) {
		PixOwnerSettings candidate = base;
		candidate.adapterBaseUrl = url;
		std::string detail;
		return validateOwnerSettings(candidate, detail);
	};
	for (const std::string& url : {
		"http://localhost:8765/", "http://127.0.0.2:8765/api/",
		"http://[::1]:8765/", "https://banco.example:443/pix/" })
	{
		if (!accepted(url))
		{
			error = "URL valida do adaptador foi recusada: " + url;
			return false;
		}
	}
	for (const std::string& url : {
		"http://localhost.evil.com:8765/", "http://127.0.0.1.evil.com/",
		"http://127.0.0.1@evil.com/", "http://127.0.0.1:8765/?segredo=1",
		"http://127.0.0.1:/", "ftp://127.0.0.1/" })
	{
		if (accepted(url))
		{
			error = "URL maliciosa do adaptador foi aceita: " + url;
			return false;
		}
	}
#ifdef _WIN32
	std::string token;
	struct TokenClearGuard
	{
		explicit TokenClearGuard(std::string& value) : token(value) {}
		~TokenClearGuard()
		{
			if (!token.empty()) SecureZeroMemory(token.data(), token.size());
		}
		TokenClearGuard(const TokenClearGuard&) = delete;
		TokenClearGuard& operator=(const TokenClearGuard&) = delete;
		std::string& token;
	} tokenClear(token);
	if (!generateManagerToken(token) || !isHexDigest(token)
		|| sha256Hex(token).size() != 64)
	{
		error = "Nao foi possivel gerar a identidade criptografica do daemon.";
		return false;
	}
	HANDLE current = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE,
		FALSE, GetCurrentProcessId());
	FILETIME creation{}, exit{}, kernel{}, user{};
	if (current == nullptr || !GetProcessTimes(current, &creation, &exit, &kernel, &user))
	{
		if (current) CloseHandle(current);
		error = "Nao foi possivel validar o FILETIME do processo de teste.";
		return false;
	}
	CloseHandle(current);
	if (fileTimeValue(creation) == 0)
	{
		error = "FILETIME de criacao invalido no teste de identidade.";
		return false;
	}
	return true;
#else
	error = "Teste do supervisor PIX disponivel somente no Windows.";
	return false;
#endif
}

bool PixAgentManager::hasProtectedToken()
{
	const std::string token = Utils::FileSystem::readAllText(secretFile());
	return token.size() >= 40 && token.size() <= 4096;
}

bool PixAgentManager::protectAndSaveToken(const std::string& token, std::string& error)
{
#ifdef _WIN32
	if (token.size() < 40 || token.size() > 512 || token.rfind("APP_USR-", 0) != 0
		|| std::any_of(token.begin(), token.end(), [](unsigned char ch) { return std::isspace(ch) != 0; }))
	{
		error = "Access Token invalido. Use o token completo iniciado por APP_USR-.";
		return false;
	}
	const std::string entropyText = "TurboRamaPixAgent-v1";
	DATA_BLOB input{ (DWORD)token.size(), (BYTE*)token.data() };
	DATA_BLOB entropy{ (DWORD)entropyText.size(), (BYTE*)entropyText.data() };
	DATA_BLOB output{};
	if (!CryptProtectData(&input, L"TurboRama PIX", &entropy, nullptr, nullptr, CRYPTPROTECT_UI_FORBIDDEN, &output))
	{
		error = "O Windows nao conseguiu proteger o Access Token.";
		return false;
	}
	const std::string encoded = base64Encode(output.pbData, output.cbData);
	LocalFree(output.pbData);
	if (!writeAtomically(secretFile(), encoded, error)) return false;
	SetFileAttributesW(Utils::String::convertToWideString(secretFile()).c_str(), FILE_ATTRIBUTE_HIDDEN);
	return true;
#else
	(void)token;
	error = "Configuracao PIX disponivel somente no Windows.";
	return false;
#endif
}

bool PixAgentManager::saveOwnerSettings(const PixOwnerSettings& requested, const std::string& newAccessToken, std::string& error)
{
	PixOwnerSettings settings = requested;
	settings.enabled = true;
	settings.postalCode.erase(std::remove_if(settings.postalCode.begin(), settings.postalCode.end(), [](unsigned char ch) {
		return std::isdigit(ch) == 0;
	}), settings.postalCode.end());
	if (!validateOwnerSettings(settings, error)) return false;
	if (!newAccessToken.empty())
	{
		error = "Por seguranca, cole o Access Token somente em CONFIGURAR-ACCESS-TOKEN-PIX.exe.";
		return false;
	}

	rapidjson::StringBuffer buffer;
	rapidjson::Writer<rapidjson::StringBuffer> writer(buffer);
	writer.StartObject();
	writer.Key("schemaVersion"); writer.Int(1);
	writer.Key("enabled"); writer.Bool(true);
	auto write = [&writer](const char* name, const std::string& value) { writer.Key(name); writer.String(value.c_str()); };
	write("provider", settings.provider);
	write("accountId", settings.accountId);
	write("storeExternalId", settings.storeExternalId);
	write("storeName", settings.storeName);
	write("posExternalId", settings.posExternalId);
	write("posName", settings.posName);
	write("postalCode", settings.postalCode);
	write("streetNumber", settings.streetNumber);
	write("reference", settings.reference);
	write("adapterBaseUrl", settings.adapterBaseUrl);
	write("adapterProviderId", settings.adapterProviderId);
	writer.Key("packagePricesCents"); writer.StartObject();
	for (const auto& price : settings.pricesCents) { writer.Key(std::to_string(price.first).c_str()); writer.Int64(price.second); }
	writer.EndObject();
	writer.EndObject();
	return writeAtomically(settingsFile(), buffer.GetString(), error);
}

bool PixAgentManager::startIfConfigured(std::string* error)
{
	const PixOwnerSettings settings = loadOwnerSettings();
	if (!settings.enabled) { if (error) *error = "PIX ainda nao foi configurado pelo proprietario."; return false; }
	const std::string executable = agentExecutable();
	if (!agentIsInstalled()) { if (error) *error = "Agente PIX nao foi instalado."; return false; }
#ifdef _WIN32
	AgentStatus existingStatus;
	const DaemonLookupResult existing = lookupDaemon(existingStatus);
	if (existing == DaemonLookupResult::Found) return true;
	if (existing == DaemonLookupResult::Unknown)
	{
		if (error) *error = "A identidade do servico PIX nao pode ser confirmada; nenhum processo novo foi iniciado.";
		return false;
	}
	std::string token;
	if (!generateManagerToken(token))
	{
		if (error) *error = "O Windows nao conseguiu gerar a identidade do servico PIX.";
		return false;
	}
	const std::string tokenHash = sha256Hex(token);
	std::vector<wchar_t> environment;
	if (tokenHash.size() != 64 || !buildDaemonEnvironment(token, environment))
	{
		if (!token.empty()) SecureZeroMemory(token.data(), token.size());
		if (!environment.empty()) SecureZeroMemory(environment.data(), environment.size() * sizeof(wchar_t));
		if (error) *error = "O Windows nao conseguiu preparar o ambiente seguro do servico PIX.";
		return false;
	}
	const std::wstring exe = Utils::String::convertToWideString(executable);
	const std::wstring bridge = Utils::String::convertToWideString(bridgeDirectory());
	std::wstring command = L"\"" + exe + L"\"";
	if (Utils::FileSystem::exists(privateDotnet()))
		command += L" \"" + Utils::String::convertToWideString(agentAssembly()) + L"\"";
	command += L" --daemon --bridge \"" + bridge + L"\"";
	std::vector<wchar_t> mutableCommand(command.begin(), command.end());
	mutableCommand.push_back(L'\0');
	STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_HIDE;
	PROCESS_INFORMATION process{};
	const std::wstring working = Utils::String::convertToWideString(agentDirectory());
	const BOOL started = CreateProcessW(exe.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
		CREATE_NO_WINDOW | CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT,
		environment.data(), working.c_str(), &startup, &process);
	const DWORD startError = started ? ERROR_SUCCESS : GetLastError();
	SecureZeroMemory(token.data(), token.size());
	SecureZeroMemory(environment.data(), environment.size() * sizeof(wchar_t));
	if (!started) { if (error) *error = "Nao foi possivel iniciar o servico PIX (Windows " + std::to_string(startError) + ")."; return false; }
	FILETIME creation{}, exit{}, kernel{}, user{};
	const bool creationRead = GetProcessTimes(process.hProcess, &creation, &exit, &kernel, &user) != FALSE;
	const ULONGLONG creationFileTime = creationRead ? fileTimeValue(creation) : 0;
	const bool resumed = creationRead && creationFileTime != 0 && ResumeThread(process.hThread) != (DWORD)-1;
	CloseHandle(process.hThread);
	if (!resumed)
	{
		TerminateProcess(process.hProcess, 22);
		const bool exited = WaitForSingleObject(process.hProcess, 3000) == WAIT_OBJECT_0;
		CloseHandle(process.hProcess);
		if (error) *error = exited
			? "O Windows nao conseguiu ativar a identidade do servico PIX."
			: "O servico PIX nao iniciou e seu encerramento nao pode ser confirmado.";
		return false;
	}

	const ULONGLONG deadline = GetTickCount64() + agentIdentityStartupTimeoutMs;
	while (GetTickCount64() < deadline)
	{
		if (WaitForSingleObject(process.hProcess, 0) == WAIT_OBJECT_0) break;
		AgentStatus launchedStatus;
		if (lookupDaemon(launchedStatus, process.dwProcessId, creationFileTime, tokenHash)
			== DaemonLookupResult::Found)
		{
			expectedDaemonPid = process.dwProcessId;
			expectedDaemonCreationFileTime = creationFileTime;
			expectedDaemonTokenHash = tokenHash;
			CloseHandle(process.hProcess);
			LOG(LogInfo) << "[PIX] Agente iniciado e identidade confirmada.";
			return true;
		}
		Sleep(50);
	}

	DWORD exitCode = STILL_ACTIVE;
	GetExitCodeProcess(process.hProcess, &exitCode);
	bool exited = WaitForSingleObject(process.hProcess, 0) == WAIT_OBJECT_0;
	if (!exited && TerminateProcess(process.hProcess, 22))
		exited = WaitForSingleObject(process.hProcess, 3000) == WAIT_OBJECT_0;
	CloseHandle(process.hProcess);
	if (error)
	{
		if (exitCode != STILL_ACTIVE)
			*error = "O servico PIX encerrou antes de publicar sua identidade (codigo " + std::to_string(exitCode) + ").";
		else if (!exited)
			*error = "O servico PIX nao confirmou identidade e seu encerramento falhou.";
		else *error = "O servico PIX nao confirmou sua identidade dentro do prazo seguro.";
	}
	return false;
#else
	if (error) *error = "Agente PIX disponivel somente no Windows.";
	return false;
#endif
}

bool PixAgentManager::superviseIfConfigured(std::string* error)
{
	const PixOwnerSettings settings = loadOwnerSettings();
	if (!settings.enabled)
	{
		if (error) *error = "PIX ainda nao foi configurado pelo proprietario.";
		return false;
	}
	if (!agentIsInstalled())
	{
		if (error) *error = "Agente PIX nao foi instalado.";
		return false;
	}
#ifdef _WIN32
	AgentStatus status;
	const DaemonLookupResult found = lookupDaemon(status);
	if (found == DaemonLookupResult::Absent) return startIfConfigured(error);
	if (found == DaemonLookupResult::Unknown)
	{
		if (error) *error = "A identidade do servico PIX esta indisponivel; supervisao interrompida sem encerrar processos.";
		return false;
	}
	const long long now = (long long)std::time(nullptr);
	const bool heartbeatFresh = status.updatedAt >= now - agentHeartbeatTimeoutSeconds && status.updatedAt <= now + 120;
	if (heartbeatFresh)
		return true;

	const long long age = processAgeSeconds(status.creationFileTime);
	if (age >= 0 && age < agentStartupGraceSeconds)
		return true;

	LOG(LogWarning) << "[PIX] Agente sem heartbeat valido ha mais de " << agentHeartbeatTimeoutSeconds
		<< " segundos; reiniciando daemon autenticado " << status.pid << ".";
	std::string restartError;
	const bool restarted = restartIfConfigured(restartError);
	if (!restarted && error) *error = restartError;
	return restarted;
#else
	if (error) *error = "Agente PIX disponivel somente no Windows.";
	return false;
#endif
}

bool PixAgentManager::stopExpectedAgent()
{
#ifdef _WIN32
	AgentStatus status;
	HANDLE process = nullptr;
	const DaemonLookupResult found = lookupDaemon(status, 0, 0, {}, &process);
	if (found == DaemonLookupResult::Absent) return true;
	if (found != DaemonLookupResult::Found || process == nullptr) return false;

	// The headless agent consumes this sentinel between cycles, removes it and
	// exits after its current atomic writes are complete.
	rapidjson::StringBuffer buffer;
	rapidjson::Writer<rapidjson::StringBuffer> writer(buffer);
	writer.StartObject();
	writer.Key("schemaVersion"); writer.Int(1);
	writer.Key("mode"); writer.String("daemon");
	writer.Key("processId"); writer.Uint(status.pid);
	writer.Key("processStartFileTimeUtc"); writer.Uint64(status.creationFileTime);
	writer.Key("managerTokenHash"); writer.String(status.managerTokenHash.c_str());
	writer.EndObject();
	std::string stopError;
	const bool stopRequested = writeAtomically(stopRequestFile(), buffer.GetString(), stopError);
	if (!stopRequested)
		LOG(LogWarning) << "[PIX] Nao foi possivel solicitar parada graciosa: " << stopError;
	bool stopped = stopRequested && WaitForSingleObject(process, 5000) == WAIT_OBJECT_0;
	if (!stopped)
	{
		if (validateProcessHandle(process, status) != DaemonLookupResult::Found)
		{
			Utils::FileSystem::removeFile(stopRequestFile());
			CloseHandle(process);
			return false;
		}
		LOG(LogWarning) << "[PIX] Daemon autenticado nao respondeu ao sentinel; encerrando-o.";
		const bool terminated = TerminateProcess(process, 0) != FALSE;
		stopped = terminated && WaitForSingleObject(process, 3000) == WAIT_OBJECT_0;
	}
	Utils::FileSystem::removeFile(stopRequestFile());
	CloseHandle(process);
	if (stopped)
	{
		expectedDaemonPid = 0;
		expectedDaemonCreationFileTime = 0;
		expectedDaemonTokenHash.clear();
	}
	return stopped;
#else
	return true;
#endif
}

bool PixAgentManager::restartIfConfigured(std::string& error)
{
	if (!stopExpectedAgent())
	{
		error = "Um processo diferente esta usando o identificador do agente PIX; nada foi encerrado.";
		return false;
	}
	return startIfConfigured(&error);
}

std::string PixAgentManager::statusText()
{
	const PixOwnerSettings settings = loadOwnerSettings();
	if (!settings.enabled) return "NAO CONFIGURADO";
	if (!agentIsInstalled()) return "AGENTE NAO INSTALADO";
	const std::string setupText = Utils::FileSystem::readAllText(setupStatusFile());
	if (!setupText.empty() && setupText.size() < 32768)
	{
		rapidjson::Document setup;
		if (!setup.Parse(setupText.c_str()).HasParseError() && setup.IsObject())
		{
			// Um status antigo nao pode mascarar a situacao atual para sempre.
			// O agente atualiza este arquivo a cada tentativa; depois de dois
			// minutos a interface ignora a copia antiga e mostra a ausencia de
			// token ou de resposta do agente, em vez de "CONFIGURANDO" infinito.
			const long long updated = jsonLong(setup, "updatedAtUnixSeconds");
			const long long now = (long long)std::time(nullptr);
			if (jsonLong(setup, "schemaVersion") == 1 && updated >= now - 120 && updated <= now + 120)
			{
				const std::string state = jsonString(setup, "state");
				if (state == "error") return "ERRO: " + jsonString(setup, "message", "CONFIGURACAO RECUSADA");
				if (state == "waiting_network") return "SEM CONEXAO: " + jsonString(setup, "message", "AGUARDANDO INTERNET");
				if (state == "configuring") return "CONFIGURANDO: " + jsonString(setup, "message", "MERCADO PAGO...");
			}
		}
	}
	if (!hasProtectedToken()) return "FALTA ACCESS TOKEN - USE O EDITOR DO WINDOWS";
#ifdef _WIN32
	AgentStatus status;
	const DaemonLookupResult identity = lookupDaemon(status);
	if (identity == DaemonLookupResult::Absent) return "AGENTE SEM RESPOSTA";
	if (identity == DaemonLookupResult::Unknown) return "IDENTIDADE DO AGENTE INVALIDA";
	const long long now = (long long)std::time(nullptr);
	if (status.updatedAt < now - 30 || status.updatedAt > now + 120) return "AGENTE SEM RESPOSTA";
	if (status.state == "online") return status.ready ? "ATIVO E PRONTO" : "AGENTE AINDA NAO PRONTO";
	if (status.state == "starting") return "INICIANDO...";
	if (status.state == "provider_unavailable") return "MERCADO PAGO INDISPONIVEL";
	return status.state;
#else
	return "AGENTE SEM RESPOSTA";
#endif
}
