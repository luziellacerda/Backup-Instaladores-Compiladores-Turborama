#include "PixAgentManager.h"
#include "PixBinaryTrust.h"

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
#include <thread>
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

	std::string startupErrorFile()
	{
		return Utils::FileSystem::combine(PixAgentManager::bridgeDirectory(), "agent-startup-error.json");
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
		if (PixBinaryTrust::required())
			return Utils::FileSystem::exists(privateDotnet())
				&& Utils::FileSystem::exists(agentAssembly());
		if (Utils::FileSystem::exists(privateDotnet())) return Utils::FileSystem::exists(agentAssembly());
		return Utils::FileSystem::exists(agentAppHost());
	}

	bool agentTrustValid(std::string& error)
	{
		if (!agentIsInstalled())
		{
			error = "Agente PIX nao foi instalado.";
			return false;
		}
#ifdef _WIN32
		if (!PixBinaryTrust::verifyCommercialAgentBundle(
			Utils::String::convertToWideString(agentDirectory()), error)) return false;
		if (Utils::FileSystem::exists(privateDotnet()))
		{
			if (!PixBinaryTrust::verifyTrustedRuntime(
				Utils::String::convertToWideString(privateDotnet()), error)) return false;
			if (!PixBinaryTrust::verifyVendorBinary(
				Utils::String::convertToWideString(agentAssembly()), error)) return false;

			const std::string agentRoot = Utils::FileSystem::getParent(agentAssembly());
			for (const std::string& vendorDependency : {
				Utils::FileSystem::combine(agentRoot, "QRCoder.dll") })
			{
				if (!Utils::FileSystem::exists(vendorDependency)
					|| !PixBinaryTrust::verifyVendorBinary(
						Utils::String::convertToWideString(vendorDependency), error)) return false;
			}
			for (const std::string& microsoftDependency : {
				Utils::FileSystem::combine(agentRoot, "Microsoft.Win32.SystemEvents.dll"),
				Utils::FileSystem::combine(agentRoot, "System.Drawing.Common.dll") })
			{
				if (!Utils::FileSystem::exists(microsoftDependency)
					|| !PixBinaryTrust::verifyTrustedRuntime(
						Utils::String::convertToWideString(microsoftDependency), error)) return false;
			}

			auto onlyVersionDirectory = [&](const std::string& parent, std::string& selected) {
				selected.clear();
				if (!Utils::FileSystem::isDirectory(parent)) return false;
				for (const std::string& entry : Utils::FileSystem::getDirContent(parent, false, true))
				{
					if (!Utils::FileSystem::isDirectory(entry)) continue;
					if (!selected.empty()) return false;
					selected = entry;
				}
				return !selected.empty();
			};
			const std::string runtimeRoot = Utils::FileSystem::combine(agentRoot, "runtime");
			std::string fxrVersion;
			std::string sharedVersion;
			if (!onlyVersionDirectory(Utils::FileSystem::combine(runtimeRoot, "host/fxr"), fxrVersion)
				|| !onlyVersionDirectory(Utils::FileSystem::combine(runtimeRoot,
					"shared/Microsoft.NETCore.App"), sharedVersion))
			{
				error = "Runtime PIX recusado: versao privada ausente ou ambigua.";
				return false;
			}
			for (const std::string& runtimeBinary : {
				Utils::FileSystem::combine(fxrVersion, "hostfxr.dll"),
				Utils::FileSystem::combine(sharedVersion, "hostpolicy.dll"),
				Utils::FileSystem::combine(sharedVersion, "coreclr.dll"),
				Utils::FileSystem::combine(sharedVersion, "clrjit.dll"),
				Utils::FileSystem::combine(sharedVersion, "System.Private.CoreLib.dll") })
			{
				if (!Utils::FileSystem::exists(runtimeBinary)
					|| !PixBinaryTrust::verifyTrustedRuntime(
						Utils::String::convertToWideString(runtimeBinary), error)) return false;
			}
			return true;
		}
		if (PixBinaryTrust::required())
		{
			error = "Runtime privado PIX obrigatorio ausente; o fallback global foi recusado.";
			return false;
		}
		return PixBinaryTrust::verifyVendorBinary(Utils::String::convertToWideString(agentAppHost()), error);
#else
		return !PixBinaryTrust::required();
#endif
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

	std::string trimForDisplay(std::string value, size_t maximum)
	{
		while (!value.empty() && static_cast<unsigned char>(value.front()) <= ' ') value.erase(value.begin());
		while (!value.empty() && static_cast<unsigned char>(value.back()) <= ' ') value.pop_back();
		value.erase(std::remove(value.begin(), value.end(), '\r'), value.end());
		value.erase(std::remove(value.begin(), value.end(), '\n'), value.end());
		if (value.size() > maximum) value.resize(maximum);
		return value;
	}

	std::string readStartupErrorMessage()
	{
		const std::string file = startupErrorFile();
		if (!Utils::FileSystem::exists(file)) return {};
		const std::string text = Utils::FileSystem::readAllText(file);
		if (text.empty() || text.size() > 16 * 1024) return {};
		rapidjson::Document document;
		document.Parse(text.c_str());
		if (document.HasParseError() || !document.IsObject()) return {};
		if (!document.HasMember("schemaVersion") || !document["schemaVersion"].IsInt()
			|| document["schemaVersion"].GetInt() != 1) return {};
		return trimForDisplay(jsonString(document, "message"), 1024);
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

	std::string normalizedAscii(std::string value)
	{
		std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
			return (char)std::tolower(ch);
		});
		return value;
	}

	bool isNumericMercadoPagoAccountId(const std::string& value)
	{
		return value.size() >= 5 && value.size() <= 24
			&& std::all_of(value.begin(), value.end(), [](unsigned char ch) {
				return std::isdigit(ch) != 0;
			});
	}

	bool hasReadyMercadoPagoRegistration(const PixOwnerSettings& settings, std::string& error)
	{
		const std::string provider = normalizedAscii(settings.provider);
		const std::string setupState = normalizedAscii(settings.setupState);
		const std::string environment = normalizedAscii(settings.mercadoPagoEnvironment);
		if (provider != "mercadopago")
			error = "O cadastro local salvo nao pertence ao Mercado Pago.";
		else if (setupState != "ready")
			error = "O cadastro Mercado Pago ainda nao foi validado pelo configurador.";
		else if (environment != "production" && environment != "sandbox")
			error = "O ambiente Mercado Pago salvo e invalido.";
		else if (!isNumericMercadoPagoAccountId(settings.accountId))
			error = "O cadastro Mercado Pago salvo nao possui User ID valido.";
		else if (!onlyLettersAndNumbers(settings.storeExternalId, 60))
			error = "O cadastro Mercado Pago salvo nao possui loja valida.";
		else if (!onlyLettersAndNumbers(settings.posExternalId, 40)
			|| normalizedAscii(settings.posExternalId) == "lzpixcomp")
			error = "O cadastro Mercado Pago salvo nao possui PDV valido.";
		else if (settings.storeName.size() < 2 || settings.storeName.size() >= 60
			|| settings.posName.size() < 2 || settings.posName.size() >= 45)
			error = "O cadastro Mercado Pago salvo possui nomes invalidos.";
		else
		{
			std::string cep;
			for (unsigned char ch : settings.postalCode)
				if (std::isdigit(ch)) cep.push_back((char)ch);
			if (cep.size() != 8) error = "O cadastro Mercado Pago salvo nao possui CEP valido.";
			else if (settings.streetNumber.empty() || settings.streetNumber.size() > 20)
				error = "O cadastro Mercado Pago salvo nao possui numero valido.";
			else if (settings.reference.empty() || settings.reference.size() > 120)
				error = "O cadastro Mercado Pago salvo nao possui referencia valida.";
		}
		return error.empty();
	}

	bool preserveMercadoPagoRegistrationForActivation(PixOwnerSettings& settings,
		const PixOwnerSettings& registered, std::string& error)
	{
		if (normalizedAscii(settings.provider) != "mercadopago") return true;
		if (!hasReadyMercadoPagoRegistration(registered, error))
		{
			error += "\n\nAbra CONFIGURAR-USER-TOKEN-PIX.exe, selecione o unico cadastro desta maquina e valide novamente.";
			return false;
		}

		// O menu do EmulationStation edita preco/licenca local. A conta, loja e
		// PDV do Mercado Pago pertencem ao configurador USER e nao podem ser
		// sobrescritos por um rascunho antigo da tela.
		settings.enabled = true;
		settings.provider = "mercadopago";
		settings.setupState = "ready";
		settings.mercadoPagoEnvironment = registered.mercadoPagoEnvironment;
		settings.accountId = registered.accountId;
		settings.storeExternalId = registered.storeExternalId;
		settings.storeName = registered.storeName;
		settings.posExternalId = registered.posExternalId;
		settings.posName = registered.posName;
		if (settings.postalCode.empty()) settings.postalCode = registered.postalCode;
		if (settings.streetNumber.empty()) settings.streetNumber = registered.streetNumber;
		if (settings.reference.empty()) settings.reference = registered.reference;
		if (registered.onlineLicensingEnabled) settings.onlineLicensingEnabled = true;
		if (!registered.onlineBaseUrl.empty()) settings.onlineBaseUrl = registered.onlineBaseUrl;
		if (settings.onlineLicenseId.empty() || settings.onlineLicenseId == "CONFIGURE-A-LICENCA")
			settings.onlineLicenseId = registered.onlineLicenseId;
		if (!registered.onlineProtectionProfile.empty())
			settings.onlineProtectionProfile = registered.onlineProtectionProfile;
		settings.onlineConfigurationVersion = registered.onlineConfigurationVersion;
		settings.onlineConfigurationPending = false;
		return true;
	}

#ifdef _WIN32
	const long long agentHeartbeatTimeoutSeconds = 90;
	const long long agentStartupGraceSeconds = 90;
	// Primeira inicializacao pode criar chaves/ACLs sob HDD e antivirus lentos.
	// O PID continua retido e autenticado durante toda a espera.
	const DWORD agentIdentityStartupTimeoutMs = 90000;
	const DWORD onlineActivationReconciliationTimeoutMs = 30000;
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
		std::string ignoredError;
		return PixBinaryTrust::buildSanitizedDotnetEnvironment(
			Utils::String::convertToWideString(Utils::FileSystem::combine(agentDirectory(), "runtime")),
			{ { managerTokenEnvironment, Utils::String::convertToWideString(token) } },
			environment, ignoredError);
	}

	std::string safeAgentOutput(const std::string& output)
	{
		std::string last;
		std::istringstream lines(output);
		for (std::string line; std::getline(lines, line); )
		{
			line.erase(std::remove(line.begin(), line.end(), '\r'), line.end());
			while (!line.empty() && std::isspace((unsigned char)line.front())) line.erase(line.begin());
			while (!line.empty() && std::isspace((unsigned char)line.back())) line.pop_back();
			if (!line.empty() && line.rfind("Digite o codigo", 0) != 0) last = line;
		}
		if (last.size() > 1024) last.resize(1024);
		return last;
	}

	bool runOnlineActivationProcess(const std::string& activationCode, bool& processStarted,
		DWORD& exitCode, std::string& output, std::string& error)
	{
		processStarted = false;
		exitCode = STILL_ACTIVE;
		output.clear();
		std::string trustError;
		if (!agentTrustValid(trustError)) { error = trustError; return false; }

		SECURITY_ATTRIBUTES attributes{ sizeof(SECURITY_ATTRIBUTES), nullptr, TRUE };
		HANDLE stdinRead = nullptr, stdinWrite = nullptr, stdoutRead = nullptr, stdoutWrite = nullptr;
		auto closeOne = [](HANDLE& handle) { if (handle != nullptr) CloseHandle(handle); handle = nullptr; };
		auto closePipes = [&]() { closeOne(stdinRead); closeOne(stdinWrite); closeOne(stdoutRead); closeOne(stdoutWrite); };
		if (!CreatePipe(&stdinRead, &stdinWrite, &attributes, 0)
			|| !CreatePipe(&stdoutRead, &stdoutWrite, &attributes, 0)
			|| !SetHandleInformation(stdinWrite, HANDLE_FLAG_INHERIT, 0)
			|| !SetHandleInformation(stdoutRead, HANDLE_FLAG_INHERIT, 0))
		{
			closePipes();
			error = "O Windows nao conseguiu criar o canal protegido de ativacao.";
			return false;
		}

		SIZE_T attributeBytes = 0;
		InitializeProcThreadAttributeList(nullptr, 1, 0, &attributeBytes);
		std::vector<unsigned char> attributeStorage(attributeBytes);
		auto attributeList = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(attributeStorage.data());
		HANDLE inheritedHandles[] = { stdinRead, stdoutWrite };
		const bool attributeListInitialized = attributeBytes != 0
			&& InitializeProcThreadAttributeList(attributeList, 1, 0, &attributeBytes) != FALSE;
		if (!attributeListInitialized
			|| !UpdateProcThreadAttribute(attributeList, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
				inheritedHandles, sizeof(inheritedHandles), nullptr, nullptr))
		{
			if (attributeListInitialized) DeleteProcThreadAttributeList(attributeList);
			closePipes();
			error = "O Windows nao conseguiu isolar os handles da ativacao PIX.";
			return false;
		}

		const std::wstring executable = Utils::String::convertToWideString(PixAgentManager::agentExecutable());
		std::wstring command = L"\"" + executable + L"\"";
		if (Utils::FileSystem::exists(privateDotnet()))
			command += L" \"" + Utils::String::convertToWideString(agentAssembly()) + L"\"";
		command += L" --online-activate --bridge \""
			+ Utils::String::convertToWideString(PixAgentManager::bridgeDirectory()) + L"\"";
		std::vector<wchar_t> mutableCommand(command.begin(), command.end());
		mutableCommand.push_back(L'\0');
		std::vector<wchar_t> environment;
		std::string environmentError;
		if (!PixBinaryTrust::buildSanitizedDotnetEnvironment(
			Utils::String::convertToWideString(Utils::FileSystem::combine(agentDirectory(), "runtime")),
			{}, environment, environmentError))
		{
			DeleteProcThreadAttributeList(attributeList);
			closePipes();
			error = "O Windows nao conseguiu preparar o ambiente protegido da ativacao PIX.";
			return false;
		}

		STARTUPINFOEXW startup{};
		startup.StartupInfo.cb = sizeof(startup);
		startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
		startup.StartupInfo.hStdInput = stdinRead;
		startup.StartupInfo.hStdOutput = stdoutWrite;
		startup.StartupInfo.hStdError = stdoutWrite;
		startup.StartupInfo.wShowWindow = SW_HIDE;
		startup.lpAttributeList = attributeList;
		PROCESS_INFORMATION process{};
		const std::wstring working = Utils::String::convertToWideString(agentDirectory());
		const BOOL created = CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, TRUE,
			CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT | EXTENDED_STARTUPINFO_PRESENT,
			environment.data(), working.c_str(), &startup.StartupInfo, &process);
		const DWORD createError = created ? ERROR_SUCCESS : GetLastError();
		SecureZeroMemory(environment.data(), environment.size() * sizeof(wchar_t));
		DeleteProcThreadAttributeList(attributeList);
		closeOne(stdinRead);
		closeOne(stdoutWrite);
		if (!created)
		{
			closeOne(stdinWrite);
			closeOne(stdoutRead);
			error = "Nao foi possivel iniciar a ativacao PIX (Windows " + std::to_string(createError) + ").";
			return false;
		}
		processStarted = true;

		std::thread reader([&]() {
			char buffer[4096];
			DWORD received = 0;
			while (ReadFile(stdoutRead, buffer, sizeof(buffer), &received, nullptr) && received != 0)
			{
				const size_t available = output.size() < 65536 ? 65536 - output.size() : 0;
				output.append(buffer, buffer + std::min<size_t>(available, received));
			}
		});

		std::string secretLine = activationCode + "\r\n";
		DWORD written = 0;
		const bool inputDelivered = WriteFile(stdinWrite, secretLine.data(), (DWORD)secretLine.size(), &written, nullptr) != FALSE
			&& written == secretLine.size();
		SecureZeroMemory(secretLine.data(), secretLine.size());
		closeOne(stdinWrite);
		if (!inputDelivered) TerminateProcess(process.hProcess, 24);

		DWORD wait = WaitForSingleObject(process.hProcess, 180000);
		if (wait != WAIT_OBJECT_0)
		{
			TerminateProcess(process.hProcess, 24);
			wait = WaitForSingleObject(process.hProcess, 5000);
		}
		const bool exitConfirmed = wait == WAIT_OBJECT_0
			&& GetExitCodeProcess(process.hProcess, &exitCode) != FALSE;
		if (!exitConfirmed)
		{
			CancelSynchronousIo(reader.native_handle());
			closeOne(stdoutRead);
		}
		closeOne(process.hThread);
		closeOne(process.hProcess);
		reader.join();
		closeOne(stdoutRead);

		if (!inputDelivered)
		{
			error = "O codigo nao foi entregue ao agente PIX; a ativacao foi cancelada.";
			return false;
		}
		if (!exitConfirmed)
		{
			error = "A ativacao ultrapassou o prazo e o encerramento do agente nao foi confirmado.";
			return false;
		}
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

	bool waitForOnlineAgentReady(DWORD timeoutMs)
	{
		const ULONGLONG deadline = GetTickCount64() + timeoutMs;
		while (GetTickCount64() < deadline)
		{
			AgentStatus status;
			if (lookupDaemon(status) == DaemonLookupResult::Found)
			{
				const long long now = (long long)std::time(nullptr);
				if (status.ready && status.state == "online"
					&& status.updatedAt >= now - 30 && status.updatedAt <= now + 120)
					return true;
			}
			Sleep(100);
		}
		return false;
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
	if (PixBinaryTrust::required()) return privateDotnet();
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
		settings.setupState = jsonString(document, "setupState", settings.setupState);
		settings.provider = jsonString(document, "provider", settings.provider);
		settings.mercadoPagoEnvironment = jsonString(document, "mercadoPagoEnvironment",
			settings.mercadoPagoEnvironment);
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
		settings.onlineLicensingEnabled = jsonBool(document, "onlineLicensingEnabled",
			settings.onlineLicensingEnabled);
		settings.onlineBaseUrl = jsonString(document, "onlineBaseUrl", settings.onlineBaseUrl);
		settings.onlineLicenseId = jsonString(document, "onlineLicenseId", settings.onlineLicenseId);
		settings.onlineProtectionProfile = jsonString(document, "onlineProtectionProfile",
			settings.onlineProtectionProfile);
		settings.pixEnabled = jsonBool(document, "pixEnabled", settings.pixEnabled);
		settings.onlineConfigurationVersion = jsonLong(document, "onlineConfigurationVersion",
			settings.onlineConfigurationVersion);
		settings.onlineConfigurationPending = jsonBool(document, "onlineConfigurationPending",
			settings.onlineConfigurationPending);
		if (document.HasMember("packagePricesCents") && document["packagePricesCents"].IsObject())
		{
			for (const int minutes : { 15, 30, 45, 60, 120 })
			{
				const std::string key = std::to_string(minutes);
				if (document["packagePricesCents"].HasMember(key.c_str()) && document["packagePricesCents"][key.c_str()].IsInt64())
					settings.pricesCents[minutes] = document["packagePricesCents"][key.c_str()].GetInt64();
			}
		}
		std::string normalizedProvider = settings.provider;
		std::transform(normalizedProvider.begin(), normalizedProvider.end(), normalizedProvider.begin(),
			[](unsigned char ch) { return (char)std::tolower(ch); });
		if (normalizedProvider == "online")
		{
			// Migracao da versao que confundia licenca com provedor PIX.
			// Preserva o cadastro local se estiver completo; caso contrario,
			// mantem a licenca e pede apenas a configuracao local do pagamento.
			settings.provider = "mercadopago";
			settings.onlineLicensingEnabled = true;
			settings.onlineConfigurationPending = false;
			std::string cep;
			for (unsigned char ch : settings.postalCode) if (std::isdigit(ch)) cep.push_back((char)ch);
			const bool completePayment = !settings.accountId.empty()
				&& !settings.posExternalId.empty() && cep.size() == 8
				&& !settings.streetNumber.empty();
			if (!completePayment) { settings.enabled = false; settings.setupState = "pending"; }
		}
	}
	catch (...) { return PixOwnerSettings{}; }
	return settings;
}

bool PixAgentManager::validateOwnerSettings(const PixOwnerSettings& settings, std::string& error)
{
	if (settings.onlineLicensingEnabled)
	{
		std::string lowerUrl = settings.onlineBaseUrl;
		std::transform(lowerUrl.begin(), lowerUrl.end(), lowerUrl.begin(), [](unsigned char ch) {
			return (char)std::tolower(ch);
		});
		const bool validLicense = settings.onlineLicenseId.size() >= 6 && settings.onlineLicenseId.size() <= 64
			&& std::all_of(settings.onlineLicenseId.begin(), settings.onlineLicenseId.end(), [](unsigned char ch) {
				return std::isalnum(ch) != 0 || ch == '-' || ch == '_';
			});
		if (lowerUrl.rfind("https://", 0) != 0 || !validAdapterBaseUrl(settings.onlineBaseUrl))
			error = "O servidor de licenca TurboRama deve usar um endereco HTTPS valido.";
		else if (!validLicense || settings.onlineLicenseId == "CONFIGURE-A-LICENCA")
			error = "A licenca TurboRama Online ainda nao foi configurada.";
		else if (settings.onlineProtectionProfile != "TPM_BOUND"
			&& settings.onlineProtectionProfile != "SOFTWARE_BOUND_ONLINE"
			&& settings.onlineProtectionProfile != "USB_TOKEN_BOUND")
			error = "O perfil de protecao TurboRama Online e invalido.";
		if (!error.empty()) return false;
	}
	if (!settings.enabled) return true;
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
	std::string setupState = settings.setupState;
	std::transform(setupState.begin(), setupState.end(), setupState.begin(), [](unsigned char ch) { return (char)std::tolower(ch); });
	std::string mercadoPagoEnvironment = settings.mercadoPagoEnvironment;
	std::transform(mercadoPagoEnvironment.begin(), mercadoPagoEnvironment.end(), mercadoPagoEnvironment.begin(),
		[](unsigned char ch) { return (char)std::tolower(ch); });
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
	else if (setupState != "pending" && setupState != "ready" && setupState != "needs_address_confirmation")
		error = "O estado do cadastro Mercado Pago e invalido.";
	else if (mercadoPagoEnvironment != "production" && mercadoPagoEnvironment != "sandbox")
		error = "O ambiente Mercado Pago deve ser producao ou sandbox.";
	else if ((setupState == "ready" || !settings.accountId.empty())
		&& (settings.accountId.size() < 5 || settings.accountId.size() > 24
			|| !std::all_of(settings.accountId.begin(), settings.accountId.end(),
				[](unsigned char ch) { return std::isdigit(ch) != 0; })))
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

bool PixAgentManager::prepareOwnerSettingsForLocalActivation(PixOwnerSettings& settings, std::string& error)
{
	PixOwnerSettings prepared = settings;
	prepared.enabled = true;
	if (!preserveMercadoPagoRegistrationForActivation(prepared, loadOwnerSettings(), error))
		return false;
	if (!validateOwnerSettings(prepared, error)) return false;
	settings = prepared;
	return true;
}

bool PixAgentManager::runSelfTest(std::string& error)
{
	PixOwnerSettings base;
	// O teste precisa atravessar a validacao completa do provedor. Com o PIX
	// desativado, validateOwnerSettings encerra corretamente antes de avaliar
	// campos que nao serao usados, o que tornava este teste de URL ineficaz.
	base.enabled = true;
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
	PixOwnerSettings pending;
	pending.enabled = true;
	pending.provider = "mercadopago";
	pending.setupState = "pending";
	pending.mercadoPagoEnvironment = "sandbox";
	pending.accountId.clear();
	pending.postalCode = "57084648";
	pending.streetNumber = "52";
	std::string settingsError;
	if (!validateOwnerSettings(pending, settingsError))
	{
		error = "Cadastro Mercado Pago pendente valido foi recusado: " + settingsError;
		return false;
	}
	PixOwnerSettings invalidEnvironment = pending;
	invalidEnvironment.mercadoPagoEnvironment = "unknown";
	settingsError.clear();
	if (validateOwnerSettings(invalidEnvironment, settingsError))
	{
		error = "Ambiente Mercado Pago desconhecido foi aceito.";
		return false;
	}
	PixOwnerSettings readyWithoutAccount = pending;
	readyWithoutAccount.setupState = "ready";
	settingsError.clear();
	if (validateOwnerSettings(readyWithoutAccount, settingsError))
	{
		error = "Cadastro Mercado Pago pronto sem User ID foi aceito.";
		return false;
	}
	PixOwnerSettings staleDraft = pending;
	staleDraft.enabled = true;
	staleDraft.setupState = "pending";
	staleDraft.accountId.clear();
	staleDraft.storeExternalId = "TURBORAMALOJA01";
	staleDraft.posExternalId = "TURBORAMAKIOSK01";
	staleDraft.pricesCents[15] = 123;
	staleDraft.onlineLicensingEnabled = true;
	staleDraft.onlineLicenseId = "TR-TESTE-001";
	staleDraft.onlineBaseUrl = "https://pix.lzgames.com.br/";
	PixOwnerSettings registered = pending;
	registered.enabled = true;
	registered.setupState = "ready";
	registered.mercadoPagoEnvironment = "production";
	registered.accountId = "123456789";
	registered.storeExternalId = "LZLOJAABC123";
	registered.storeName = "TurboRamaX";
	registered.posExternalId = "LZPIXABC123";
	registered.posName = "TurboRama Kiosk";
	registered.onlineLicensingEnabled = true;
	registered.onlineLicenseId = "TR-TESTE-001";
	registered.onlineBaseUrl = "https://pix.lzgames.com.br/";
	std::string preserveError;
	if (!preserveMercadoPagoRegistrationForActivation(staleDraft, registered, preserveError))
	{
		error = "Cadastro Mercado Pago pronto nao foi preservado para ativacao local: " + preserveError;
		return false;
	}
	if (staleDraft.setupState != "ready" || staleDraft.accountId != registered.accountId
		|| staleDraft.storeExternalId != registered.storeExternalId
		|| staleDraft.posExternalId != registered.posExternalId
		|| staleDraft.pricesCents[15] != 123)
	{
		error = "Ativacao local nao preservou conta/PDV Mercado Pago ou alterou preco local.";
		return false;
	}
	PixOwnerSettings missingRegistration = registered;
	missingRegistration.setupState = "pending";
	missingRegistration.accountId.clear();
	preserveError.clear();
	if (preserveMercadoPagoRegistrationForActivation(staleDraft, missingRegistration, preserveError))
	{
		error = "Ativacao local aceitou Mercado Pago sem cadastro validado pelo configurador.";
		return false;
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
	std::vector<wchar_t> safeEnvironment;
	if (!buildDaemonEnvironment(token, safeEnvironment) || safeEnvironment.size() < 2)
	{
		error = "Nao foi possivel criar o ambiente isolado do runtime PIX.";
		return false;
	}
	bool managerTokenFound = false;
	bool diagnosticsDisabled = false;
	for (const wchar_t* current = safeEnvironment.data(); *current != L'\0'; current += wcslen(current) + 1)
	{
		std::wstring entry(current);
		std::wstring upper = entry;
		std::transform(upper.begin(), upper.end(), upper.begin(), ::towupper);
		if (upper.rfind(L"TURBORAMA_PIX_MANAGER_TOKEN=", 0) == 0)
			managerTokenFound = entry.substr(entry.find(L'=') + 1)
				== Utils::String::convertToWideString(token);
		if (upper == L"DOTNET_ENABLEDIAGNOSTICS=0") diagnosticsDisabled = true;
		for (const wchar_t* forbidden : { L"DOTNET_STARTUP_HOOKS=", L"DOTNET_ADDITIONAL_DEPS=",
			L"DOTNET_SHARED_STORE=", L"CORECLR_ENABLE_PROFILING=", L"COR_ENABLE_PROFILING=" })
		{
			if (upper.rfind(forbidden, 0) == 0)
			{
				SecureZeroMemory(safeEnvironment.data(), safeEnvironment.size() * sizeof(wchar_t));
				error = "O ambiente isolado preservou uma variavel de injecao .NET.";
				return false;
			}
		}
	}
	SecureZeroMemory(safeEnvironment.data(), safeEnvironment.size() * sizeof(wchar_t));
	if (!managerTokenFound || !diagnosticsDisabled)
	{
		error = "O ambiente isolado nao fixou a identidade ou o bloqueio de diagnostico do agente.";
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

bool PixAgentManager::runTrustSelfTest(std::string& error)
{
	return agentTrustValid(error);
}

bool PixAgentManager::hasProtectedToken()
{
	const std::string token = Utils::FileSystem::readAllText(secretFile());
	return token.size() >= 40 && token.size() <= 4096;
}

bool PixAgentManager::saveOwnerSettings(const PixOwnerSettings& requested, const std::string& newAccessToken, std::string& error)
{
	PixOwnerSettings settings = requested;
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
	writer.Key("enabled"); writer.Bool(settings.enabled);
	auto write = [&writer](const char* name, const std::string& value) { writer.Key(name); writer.String(value.c_str()); };
	write("setupState", settings.setupState);
	write("provider", settings.provider);
	write("mercadoPagoEnvironment", settings.mercadoPagoEnvironment);
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
	writer.Key("onlineLicensingEnabled"); writer.Bool(settings.onlineLicensingEnabled);
	write("onlineBaseUrl", settings.onlineBaseUrl);
	write("onlineLicenseId", settings.onlineLicenseId);
	write("onlineProtectionProfile", settings.onlineProtectionProfile);
	writer.Key("pixEnabled"); writer.Bool(settings.pixEnabled);
	writer.Key("onlineConfigurationVersion"); writer.Int64(settings.onlineConfigurationVersion);
	writer.Key("onlineConfigurationPending"); writer.Bool(false);
	writer.Key("packagePricesCents"); writer.StartObject();
	for (const auto& price : settings.pricesCents) { writer.Key(std::to_string(price.first).c_str()); writer.Int64(price.second); }
	writer.EndObject();
	writer.EndObject();
	return writeAtomically(settingsFile(), buffer.GetString(), error);
}

bool PixAgentManager::activateOnline(const PixOwnerSettings& requested,
	const std::string& activationCode, std::string& error)
{
	PixOwnerSettings settings = requested;
	settings.onlineLicensingEnabled = true;
	settings.onlineBaseUrl = "https://pix.lzgames.com.br/";
	if (!validateOwnerSettings(settings, error)) return false;
	if (activationCode.size() < 16 || activationCode.size() > 256
		|| !std::all_of(activationCode.begin(), activationCode.end(), [](unsigned char ch) {
			return ch >= 0x21 && ch <= 0x7e;
		}))
	{
		error = "O codigo de ativacao e invalido. Gere um novo codigo no painel TurboRama.";
		return false;
	}
#ifdef _WIN32
	const bool previousExisted = Utils::FileSystem::exists(settingsFile());
	const std::string previousContents = previousExisted
		? Utils::FileSystem::readAllText(settingsFile()) : std::string();
	const PixOwnerSettings previousSettings = loadOwnerSettings();
	if (!stopExpectedAgent())
	{
		error = "A identidade do agente PIX em execucao nao pode ser confirmada; a ativacao foi cancelada.";
		return false;
	}
	if (!saveOwnerSettings(settings, "", error))
	{
		if (previousSettings.enabled) { std::string ignored; startIfConfigured(&ignored); }
		return false;
	}

	bool processStarted = false;
	DWORD exitCode = STILL_ACTIVE;
	std::string output;
	const bool processCompleted = runOnlineActivationProcess(
		activationCode, processStarted, exitCode, output, error);
	if (!processCompleted || exitCode != 0)
	{
		const std::string agentMessage = safeAgentOutput(output);
		const bool activationMayHaveCompleted = processStarted
			&& (!processCompleted || exitCode == 25);
		std::string reconciliationError;
		if (activationMayHaveCompleted)
		{
			const bool candidateStarted = startIfConfigured(&reconciliationError);
			if (candidateStarted
				&& waitForOnlineAgentReady(onlineActivationReconciliationTimeoutMs))
			{
				LOG(LogWarning) << "[PIX] A resposta da ativacao foi perdida, mas a sessao autenticada confirmou o cadastro.";
				return true;
			}
			if (candidateStarted && !stopExpectedAgent())
			{
				error = "A ativacao ficou inconclusiva e o servico PIX iniciado para conferencia nao pode ser encerrado com seguranca. "
					"A configuracao on-line foi preservada; nao gere outro codigo ate verificar esta maquina no painel.";
				return false;
			}
		}
		bool restored = false;
		std::string restoreError;
		if (previousExisted)
			restored = !previousContents.empty()
				&& writeAtomically(settingsFile(), previousContents, restoreError);
		else
			restored = Utils::FileSystem::removeFile(settingsFile()) || !Utils::FileSystem::exists(settingsFile());
		if (restored && previousSettings.enabled)
		{
			std::string restartError;
			if (!startIfConfigured(&restartError))
			{
				restored = false;
				restoreError = restartError;
			}
		}
		if (processCompleted && exitCode != 0)
			error = agentMessage.empty()
				? "O servidor recusou a ativacao desta maquina (codigo " + std::to_string(exitCode) + ")."
				: agentMessage;
		else if (activationMayHaveCompleted)
			error = "Nao foi possivel confirmar a resposta final nem abrir uma sessao autenticada; o cadastro anterior foi restaurado.";
		if (activationMayHaveCompleted && !reconciliationError.empty())
			error += "\n\nConferencia on-line: " + reconciliationError;
		if (!restored)
			error += "\n\nATENCAO: o cadastro anterior nao foi restaurado por completo. " + restoreError;
		return false;
	}

	if (!settings.enabled) return true;
	if (!startIfConfigured(&error))
	{
		error = "A maquina foi ativada no servidor, mas o servico PIX nao iniciou: " + error;
		return false;
	}
	return true;
#else
	error = "A ativacao TurboRama Online esta disponivel somente no Windows.";
	return false;
#endif
}

bool PixAgentManager::startIfConfigured(std::string* error)
{
	const PixOwnerSettings settings = loadOwnerSettings();
	if (!settings.enabled) { if (error) *error = "PIX ainda nao foi configurado pelo proprietario."; return false; }
	std::string trustError;
	if (!agentTrustValid(trustError)) { if (error) *error = trustError; return false; }
	const std::string executable = agentExecutable();
#ifdef _WIN32
	AgentStatus existingStatus;
	const DaemonLookupResult existing = lookupDaemon(existingStatus);
	if (existing == DaemonLookupResult::Found) return true;
	if (existing == DaemonLookupResult::Unknown)
	{
		if (error) *error = "A identidade do servico PIX nao pode ser confirmada; nenhum processo novo foi iniciado.";
		return false;
	}
	Utils::FileSystem::removeFile(startupErrorFile());
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
		{
			const std::string startupError = readStartupErrorMessage();
			*error = startupError.empty()
				? "O servico PIX encerrou antes de publicar sua identidade (codigo " + std::to_string(exitCode) + ")."
				: startupError + " (codigo " + std::to_string(exitCode) + ").";
		}
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
	std::string trustError;
	if (!agentTrustValid(trustError))
	{
		if (error) *error = trustError;
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
	if (status.state == "license_denied") return "LICENCA DA MAQUINA RECUSADA PELO SERVIDOR";
	if (status.state == "starting") return "INICIANDO...";
	if (status.state == "provider_unavailable") return "MERCADO PAGO INDISPONIVEL";
	return status.state;
#else
	return "AGENTE SEM RESPOSTA";
#endif
}
