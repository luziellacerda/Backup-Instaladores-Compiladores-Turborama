#include "PixAgentManager.h"

#include "Log.h"
#include "Paths.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"

#include <algorithm>
#include <cctype>
#include <cwctype>
#include <ctime>
#include <vector>
#include <rapidjson/document.h>
#include <rapidjson/stringbuffer.h>
#include <rapidjson/writer.h>

#ifdef _WIN32
#include <windows.h>
#include <wincrypt.h>
#pragma comment(lib, "crypt32.lib")
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

#ifdef _WIN32
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

	bool readAgentPid(DWORD& pid)
	{
		pid = 0;
		const std::string text = Utils::FileSystem::readAllText(statusFile());
		if (text.empty() || text.size() > 16384) return false;
		rapidjson::Document document;
		if (document.Parse(text.c_str()).HasParseError() || !document.IsObject()
			|| !document.HasMember("processId") || !document["processId"].IsUint()) return false;
		pid = document["processId"].GetUint();
		return pid != 0;
	}

	bool isExpectedProcessRunning()
	{
		DWORD pid = 0;
		if (!readAgentPid(pid)) return false;
		HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
		if (process == nullptr) return false;
		wchar_t path[MAX_PATH * 4]{};
		DWORD size = (DWORD)(sizeof(path) / sizeof(path[0]));
		const bool queried = QueryFullProcessImageNameW(process, 0, path, &size) != FALSE;
		CloseHandle(process);
		return queried && normalizedWindowsPath(Utils::String::convertFromWideString(path)) == normalizedWindowsPath(PixAgentManager::agentExecutable());
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
		else if (settings.adapterBaseUrl.rfind("https://", 0) != 0
			&& settings.adapterBaseUrl.rfind("http://127.0.0.1", 0) != 0
			&& settings.adapterBaseUrl.rfind("http://localhost", 0) != 0)
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
	if (isExpectedProcessRunning()) return true;
	const std::wstring exe = Utils::String::convertToWideString(executable);
	const std::wstring bridge = Utils::String::convertToWideString(bridgeDirectory());
	std::wstring command = L"\"" + exe + L"\"";
	if (Utils::FileSystem::exists(privateDotnet()))
		command += L" \"" + Utils::String::convertToWideString(agentAssembly()) + L"\"";
	command += L" --bridge \"" + bridge + L"\"";
	std::vector<wchar_t> mutableCommand(command.begin(), command.end());
	mutableCommand.push_back(L'\0');
	STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_HIDE;
	PROCESS_INFORMATION process{};
	const std::wstring working = Utils::String::convertToWideString(agentDirectory());
	const BOOL started = CreateProcessW(exe.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
		CREATE_NO_WINDOW, nullptr, working.c_str(), &startup, &process);
	if (!started) { if (error) *error = "Nao foi possivel iniciar o servico PIX (Windows " + std::to_string(GetLastError()) + ")."; return false; }
	CloseHandle(process.hThread); CloseHandle(process.hProcess);
	LOG(LogInfo) << "[PIX] Agente iniciado automaticamente.";
	return true;
#else
	if (error) *error = "Agente PIX disponivel somente no Windows.";
	return false;
#endif
}

bool PixAgentManager::stopExpectedAgent()
{
#ifdef _WIN32
	DWORD pid = 0;
	if (!readAgentPid(pid)) return true;
	HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE | SYNCHRONIZE, FALSE, pid);
	if (process == nullptr) return true;
	wchar_t path[MAX_PATH * 4]{};
	DWORD size = (DWORD)(sizeof(path) / sizeof(path[0]));
	const bool expected = QueryFullProcessImageNameW(process, 0, path, &size) != FALSE
		&& normalizedWindowsPath(Utils::String::convertFromWideString(path)) == normalizedWindowsPath(agentExecutable());
	if (!expected) { CloseHandle(process); return false; }
	const bool stopped = TerminateProcess(process, 0) != FALSE;
	if (stopped) WaitForSingleObject(process, 3000);
	CloseHandle(process);
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
	const std::string text = Utils::FileSystem::readAllText(statusFile());
	if (text.empty() || text.size() > 16384) return "AGENTE SEM RESPOSTA";
	rapidjson::Document document;
	if (document.Parse(text.c_str()).HasParseError() || !document.IsObject()) return "STATUS INVALIDO";
	const long long updated = jsonLong(document, "updatedAtUnixSeconds");
	if (updated < (long long)std::time(nullptr) - 30) return "AGENTE SEM RESPOSTA";
	const std::string state = jsonString(document, "state");
	if (state == "online") return "ATIVO E PRONTO";
	if (state == "starting") return "INICIANDO...";
	if (state == "provider_unavailable") return "MERCADO PAGO INDISPONIVEL";
	return state.empty() ? "AGENTE EM EXECUCAO" : state;
}
