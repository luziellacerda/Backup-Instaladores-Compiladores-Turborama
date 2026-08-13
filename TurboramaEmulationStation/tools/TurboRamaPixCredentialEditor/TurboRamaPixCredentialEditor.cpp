#define UNICODE
#define _UNICODE
#define NOMINMAX
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0602
#endif
#include <windows.h>
#include <wincrypt.h>
#include <wincred.h>
#include <sddl.h>
#include <bcrypt.h>
#include <commdlg.h>
#include <objbase.h>
#include <shellapi.h>
#include <tlhelp32.h>
#include <userenv.h>

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <climits>
#include <ctime>
#include <cstring>
#include <cwctype>
#include <utility>
#include <string>
#include <vector>

#include "../../es-app/src/PixBinaryTrust.h"

#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "comdlg32.lib")
#pragma comment(lib, "credui.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "userenv.lib")

namespace
{
	constexpr int ID_TOKEN = 1001;
	constexpr int ID_PASTE = 1002;
	constexpr int ID_IMPORT = 1003;
	constexpr int ID_SHOW = 1004;
	constexpr int ID_SAVE = 1005;
	constexpr int ID_STATUS = 1006;
	constexpr int ID_CLOSE = 1007;
	constexpr int ID_TOKEN_LABEL = 1008;
	constexpr int ID_SECURITY_TITLE = 1009;
	constexpr DWORD kAgentAdministrativeTimeoutMs = 90000;
	constexpr DWORD kIdentityPreflightTimeoutMs = 15000;
	constexpr size_t kAgentOutputLimit = 16384;
	constexpr UINT WM_APP_IDENTITY_PREFLIGHT = WM_APP + 41;
	constexpr int ID_SECURITY_TEXT = 1010;
	constexpr int ID_DESTINATION = 1011;
	constexpr int ID_LICENSE = 1012;
	constexpr int ID_PROFILE = 1013;
	constexpr int ID_LICENSE_LABEL = 1014;
	constexpr int ID_PROFILE_LABEL = 1015;
	const wchar_t* kClassName = L"TurboRamaPixCredentialEditor";
	const wchar_t* kTitle = L"LZ Games | Reconhecimento TurboRama PIX";
	const wchar_t* kPublicKeyFile = L"agent-public-key.pem";
	const wchar_t* kCredentialUpdateFile = L"credential-update.json";
	const wchar_t* kCredentialUpdateStatusFile = L"credential-update-status.json";
	const wchar_t* kOwnerSettingsFile = L"owner-settings.json";
	const wchar_t* kOnlineServer = L"https://pix.lzgames.com.br/";
	HWND gToken = nullptr;
	HWND gLicense = nullptr;
	HWND gProfile = nullptr;
	HWND gStatus = nullptr;
	HWND gPaste = nullptr;
	HWND gImport = nullptr;
	HWND gShow = nullptr;
	HWND gSave = nullptr;
	HFONT gFont = nullptr;
	HFONT gSmallFont = nullptr;
	HFONT gLabelFont = nullptr;
	HFONT gButtonFont = nullptr;
	HFONT gSecurityTitleFont = nullptr;
	HFONT gTitleFont = nullptr;
	HFONT gBrandFont = nullptr;
	HBRUSH gEditBrush = nullptr;
	HICON gApplicationIcon = nullptr;
	COLORREF gStatusColor = RGB(161, 224, 82);
	bool gTokenVisible = false;
	bool gIdentityApproved = false;

	struct AgentCommandResult
	{
		bool launched = false;
		bool timedOut = false;
		bool exitConfirmed = false;
		DWORD exitCode = 999;
		std::string output;
	};

	struct KioskAccount
	{
		std::wstring user;
		std::wstring domain;
		std::vector<unsigned char> sid;
	};

	constexpr COLORREF kBackground = RGB(5, 9, 14);
	constexpr COLORREF kHeaderTop = RGB(13, 24, 32);
	constexpr COLORREF kHeaderBottom = RGB(7, 14, 21);
	constexpr COLORREF kCard = RGB(13, 21, 29);
	constexpr COLORREF kCardRaised = RGB(19, 31, 41);
	constexpr COLORREF kEdit = RGB(6, 12, 18);
	constexpr COLORREF kBorder = RGB(42, 62, 76);
	constexpr COLORREF kText = RGB(241, 247, 250);
	constexpr COLORREF kMuted = RGB(155, 170, 182);
	constexpr COLORREF kGreen = RGB(143, 232, 46);
	constexpr COLORREF kGreenPressed = RGB(110, 190, 31);
	constexpr COLORREF kCyan = RGB(31, 211, 218);
	constexpr COLORREF kGold = RGB(244, 194, 66);
	constexpr COLORREF kDanger = RGB(255, 105, 105);

	std::wstring join(const std::wstring& left, const std::wstring& right)
	{
		return left + (left.empty() || left.back() == L'\\' ? L"" : L"\\") + right;
	}

	std::wstring parentOf(const std::wstring& path)
	{
		const size_t position = path.find_last_of(L"\\/");
		return position == std::wstring::npos ? L"." : path.substr(0, position);
	}

	bool ensureDirectory(const std::wstring& directory)
	{
		if (directory.empty()) return false;
		const DWORD attributes = GetFileAttributesW(directory.c_str());
		if (attributes != INVALID_FILE_ATTRIBUTES) return (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
		const std::wstring parent = parentOf(directory);
		if (parent != directory && !ensureDirectory(parent)) return false;
		return CreateDirectoryW(directory.c_str(), nullptr) != FALSE || GetLastError() == ERROR_ALREADY_EXISTS;
	}

	bool fileExists(const std::wstring& path)
	{
		const DWORD attributes = GetFileAttributesW(path.c_str());
		return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
	}

	std::wstring bridgeDirectory()
	{
		wchar_t overridePath[32768]{};
		const DWORD length = GetEnvironmentVariableW(L"TURBORAMA_PIX_BRIDGE_DIRECTORY", overridePath, 32768);
		if (length > 0 && length < 32768) return overridePath;
		return L"D:\\emulationstation\\.emulationstation\\pix";
	}

	std::string utf8(const std::wstring& value)
	{
		if (value.empty()) return {};
		const int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), (int)value.size(), nullptr, 0, nullptr, nullptr);
		if (length <= 0) return {};
		std::string result(length, '\0');
		WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), (int)value.size(), result.data(), length, nullptr, nullptr);
		return result;
	}

	std::wstring wideUtf8(const std::string& value)
	{
		if (value.empty() || value.size() > static_cast<size_t>(INT_MAX)) return {};
		const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(),
			static_cast<int>(value.size()), nullptr, 0);
		if (length <= 0) return {};
		std::wstring result(static_cast<size_t>(length), L'\0');
		if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(),
			static_cast<int>(value.size()), result.data(), length) != length) return {};
		return result;
	}

	std::wstring sanitizeAgentOutput(const std::string& raw)
	{
		std::wstring decoded = wideUtf8(raw);
		if (!decoded.empty() && decoded.front() == 0xFEFF) decoded.erase(decoded.begin());
		std::wstring compact;
		compact.reserve(std::min<size_t>(decoded.size(), 1024));
		bool pendingSpace = false;
		for (const wchar_t character : decoded)
		{
			if (iswspace(character) != 0)
			{
				pendingSpace = !compact.empty();
				continue;
			}
			if (iswcntrl(character) != 0) continue;
			if (pendingSpace && !compact.empty()) compact.push_back(L' ');
			pendingSpace = false;
			compact.push_back(character);
			if (compact.size() >= 2048) break;
		}

		// Estes modos administrativos nunca recebem o Access Token. Ainda assim,
		// uma saida inesperada do agente nao pode fazer uma credencial aparecer na UI.
		const std::wstring marker = L"APP_USR-";
		size_t position = 0;
		while ((position = compact.find(marker, position)) != std::wstring::npos)
		{
			size_t end = position + marker.size();
			while (end < compact.size() && iswspace(compact[end]) == 0
				&& compact[end] != L'\"' && compact[end] != L'\'' && compact[end] != L','
				&& compact[end] != L';' && end - position <= 384) ++end;
			compact.replace(position, end - position, L"[CREDENCIAL OCULTA]");
			position += 20;
		}
		if (compact.size() > 900) compact = compact.substr(0, 897) + L"...";
		SecureZeroMemory(decoded.data(), decoded.size() * sizeof(wchar_t));
		return compact;
	}

	std::wstring windowsErrorMessage(DWORD code)
	{
		wchar_t* message = nullptr;
		const DWORD length = FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM
			| FORMAT_MESSAGE_IGNORE_INSERTS, nullptr, code, 0, reinterpret_cast<wchar_t*>(&message), 0, nullptr);
		std::wstring result = length > 0 && message ? std::wstring(message, length) : std::wstring{};
		if (message) LocalFree(message);
		while (!result.empty() && iswspace(result.back()) != 0) result.pop_back();
		return result;
	}

	std::wstring trim(const std::wstring& value)
	{
		const size_t first = value.find_first_not_of(L" \t\r\n");
		if (first == std::wstring::npos) return {};
		const size_t last = value.find_last_not_of(L" \t\r\n");
		return value.substr(first, last - first + 1);
	}

	bool validToken(const std::string& token, std::wstring& error)
	{
		if (token.size() < 40 || token.size() > 384 || token.rfind("APP_USR-", 0) != 0)
		{
			error = L"Access Token inv\u00E1lido. Cole o valor completo iniciado por APP_USR-.";
			return false;
		}
		for (const unsigned char character : token)
		{
			if (std::isspace(character) != 0 || character < 33 || character > 126)
			{
				error = L"O Access Token cont\u00E9m espa\u00E7os ou caracteres inv\u00E1lidos.";
				return false;
			}
		}
		return true;
	}

	std::string base64(const BYTE* data, DWORD size)
	{
		DWORD required = 0;
		if (!CryptBinaryToStringA(data, size, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, nullptr, &required)) return {};
		std::string encoded(required, '\0');
		if (!CryptBinaryToStringA(data, size, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, encoded.data(), &required)) return {};
		while (!encoded.empty() && encoded.back() == '\0') encoded.pop_back();
		return encoded;
	}

	bool decodeBase64(const std::string& encoded, std::vector<BYTE>& output)
	{
		DWORD required = 0;
		if (!CryptStringToBinaryA(encoded.c_str(), (DWORD)encoded.size(), CRYPT_STRING_BASE64, nullptr, &required, nullptr, nullptr)) return false;
		output.resize(required);
		return CryptStringToBinaryA(encoded.c_str(), (DWORD)encoded.size(), CRYPT_STRING_BASE64, output.data(), &required, nullptr, nullptr) != FALSE;
	}

	bool writeAll(const std::wstring& path, const std::string& text)
	{
		HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH, nullptr);
		if (file == INVALID_HANDLE_VALUE) return false;
		DWORD written = 0;
		const bool success = WriteFile(file, text.data(), (DWORD)text.size(), &written, nullptr) != FALSE
			&& written == text.size() && FlushFileBuffers(file) != FALSE;
		CloseHandle(file);
		if (!success) DeleteFileW(path.c_str());
		return success;
	}

	bool readAll(const std::wstring& path, std::string& text)
	{
		HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
		if (file == INVALID_HANDLE_VALUE) return false;
		LARGE_INTEGER size{};
		bool success = GetFileSizeEx(file, &size) != FALSE && size.QuadPart > 0 && size.QuadPart <= 4096;
		if (success)
		{
			text.resize((size_t)size.QuadPart);
			DWORD read = 0;
			success = ReadFile(file, text.data(), (DWORD)text.size(), &read, nullptr) != FALSE && read == text.size();
		}
		CloseHandle(file);
		return success;
	}

	std::wstring timestamp()
	{
		SYSTEMTIME time{}; GetLocalTime(&time);
		wchar_t result[64]{};
		swprintf_s(result, L"%04u%02u%02u-%02u%02u%02u", time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond);
		return result;
	}

	std::string pemBody(const std::string& pem)
	{
		const std::string begin = "-----BEGIN PUBLIC KEY-----";
		const std::string end = "-----END PUBLIC KEY-----";
		const size_t first = pem.find(begin);
		if (first == std::string::npos) return {};
		const size_t content = first + begin.size();
		const size_t last = pem.find(end, content);
		if (last == std::string::npos) return {};
		std::string result;
		for (size_t index = content; index < last; ++index)
			if (std::isspace((unsigned char)pem[index]) == 0) result.push_back(pem[index]);
		return result;
	}

	std::string sha256Fingerprint(const std::vector<BYTE>& bytes)
	{
		// CryptHashCertificate aceita somente um subconjunto de algoritmos em
		// algumas versoes do Windows; CALG_SHA_256 retorna ERROR_INVALID_PARAMETER
		// nesses sistemas. BCrypt usa SHA-256 de forma consistente no Windows
		// moderno e produz o mesmo digest que SHA256.HashData do agente .NET.
		if (bytes.empty() || bytes.size() > MAXDWORD) return {};
		BCRYPT_ALG_HANDLE algorithm = nullptr;
		BCRYPT_HASH_HANDLE hash = nullptr;
		DWORD objectSize = 0, digestSize = 0, received = 0;
		std::vector<BYTE> hashObject;
		std::vector<BYTE> digest;
		auto cleanup = [&]() {
			if (hash) BCryptDestroyHash(hash);
			if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
			if (!hashObject.empty()) SecureZeroMemory(hashObject.data(), hashObject.size());
		};
		if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) { cleanup(); return {}; }
		if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectSize), sizeof(objectSize), &received, 0) < 0
			|| received != sizeof(objectSize) || objectSize == 0) { cleanup(); return {}; }
		if (BCryptGetProperty(algorithm, BCRYPT_HASH_LENGTH, reinterpret_cast<PUCHAR>(&digestSize), sizeof(digestSize), &received, 0) < 0
			|| received != sizeof(digestSize) || digestSize != 32) { cleanup(); return {}; }
		hashObject.resize(objectSize);
		digest.resize(digestSize);
		if (BCryptCreateHash(algorithm, &hash, hashObject.data(), objectSize, nullptr, 0, 0) < 0
			|| BCryptHashData(hash, const_cast<PUCHAR>(bytes.data()), static_cast<ULONG>(bytes.size()), 0) < 0
			|| BCryptFinishHash(hash, digest.data(), digestSize, 0) < 0)
		{
			if (!digest.empty()) SecureZeroMemory(digest.data(), digest.size());
			cleanup();
			return {};
		}
		static const char digits[] = "0123456789abcdef";
		std::string result; result.reserve(digest.size() * 2);
		for (const BYTE value : digest) { result.push_back(digits[value >> 4]); result.push_back(digits[value & 15]); }
		SecureZeroMemory(digest.data(), digest.size());
		cleanup();
		return result;
	}

	bool encryptForAgent(const std::wstring& publicKeyFile, const std::string& payload, std::string& fingerprint, std::string& encryptedPayload, std::wstring& error)
	{
		std::string pem, body; std::vector<BYTE> der;
		if (!readAll(publicKeyFile, pem) || (body = pemBody(pem)).empty() || !decodeBase64(body, der))
		{
			error = L"A chave p\u00FAblica do servi\u00E7o PIX ainda n\u00E3o est\u00E1 dispon\u00EDvel. Abra o EmulationStation uma vez e tente novamente.";
			return false;
		}
		fingerprint = sha256Fingerprint(der);
		if (fingerprint.empty()) { error = L"N\u00E3o foi poss\u00EDvel validar a chave p\u00FAblica do servi\u00E7o PIX."; return false; }

		CERT_PUBLIC_KEY_INFO* information = nullptr; DWORD informationSize = 0;
		if (!CryptDecodeObjectEx(X509_ASN_ENCODING, X509_PUBLIC_KEY_INFO, der.data(), (DWORD)der.size(), CRYPT_DECODE_ALLOC_FLAG,
			nullptr, &information, &informationSize))
		{
			error = L"A chave p\u00FAblica do servi\u00E7o PIX est\u00E1 em formato inv\u00E1lido.";
			return false;
		}
		HCRYPTPROV provider = 0; HCRYPTKEY key = 0;
		auto cleanup = [&]() { if (key) CryptDestroyKey(key); if (provider) CryptReleaseContext(provider, 0); LocalFree(information); };
		if (!CryptAcquireContextW(&provider, nullptr, MS_ENH_RSA_AES_PROV_W, PROV_RSA_AES, CRYPT_VERIFYCONTEXT))
		{ cleanup(); error = L"O Windows n\u00E3o conseguiu preparar a criptografia do token."; return false; }
		if (!CryptImportPublicKeyInfo(provider, X509_ASN_ENCODING, information, &key))
		{ cleanup(); error = L"O Windows n\u00E3o conseguiu abrir a chave p\u00FAblica PIX."; return false; }
		DWORD keyBits = 0, keyBitsSize = sizeof(keyBits);
		if (!CryptGetKeyParam(key, KP_KEYLEN, (BYTE*)&keyBits, &keyBitsSize, 0) || keyBits < 1024)
		{ cleanup(); error = L"A chave p\u00FAblica PIX n\u00E3o \u00E9 segura ou est\u00E1 inv\u00E1lida."; return false; }
		const DWORD capacity = keyBits / 8;
		if (payload.size() > capacity - 42)
		{ cleanup(); error = L"O Access Token excede o limite seguro. Confira se o valor completo foi colado corretamente."; return false; }
		std::vector<BYTE> cipher(capacity);
		std::memcpy(cipher.data(), payload.data(), payload.size());
		DWORD cipherSize = (DWORD)payload.size();
		if (!CryptEncrypt(key, 0, TRUE, CRYPT_OAEP, cipher.data(), &cipherSize, capacity))
		{ SecureZeroMemory(cipher.data(), cipher.size()); cleanup(); error = L"O Windows n\u00E3o conseguiu cifrar o Access Token para o servi\u00E7o PIX."; return false; }
		// CryptoAPI entrega cifra RSA em little-endian; System.Security.Cryptography
		// usa a representacao big-endian. A inversao e obrigatoria para o agente
		// .NET conseguir abrir o mesmo envelope OAEP/SHA-1.
		std::reverse(cipher.begin(), cipher.begin() + cipherSize);
		encryptedPayload = base64(cipher.data(), cipherSize);
		SecureZeroMemory(cipher.data(), cipher.size());
		cleanup();
		if (encryptedPayload.empty()) { error = L"N\u00E3o foi poss\u00EDvel preparar a atualiza\u00E7\u00E3o segura do Access Token."; return false; }
		return true;
	}

	bool writeCredentialUpdate(const std::wstring& bridge, const std::string& requestId, const std::string& fingerprint,
		const std::string& encryptedPayload, std::wstring& error)
	{
		const std::wstring destination = join(bridge, kCredentialUpdateFile);
		const std::wstring temporary = destination + L"." + std::to_wstring(GetCurrentProcessId()) + L".tmp";
		std::string json = "{\"schemaVersion\":3,\"requestId\":\"" + requestId
			+ "\",\"keyFingerprint\":\"" + fingerprint + "\",\"encryptedPayload\":\"" + encryptedPayload
			+ "\",\"createdAtUnixSeconds\":" + std::to_string((long long)time(nullptr)) + "}";
		DeleteFileW(temporary.c_str());
		if (!writeAll(temporary, json))
		{
			SecureZeroMemory(json.data(), json.size());
			error = L"N\u00E3o foi poss\u00EDvel gravar a atualiza\u00E7\u00E3o segura do token.";
			return false;
		}
		if (!MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
		{
			SecureZeroMemory(json.data(), json.size());
			DeleteFileW(temporary.c_str()); error = L"N\u00E3o foi poss\u00EDvel entregar o token cifrado ao servi\u00E7o PIX."; return false;
		}
		SecureZeroMemory(json.data(), json.size());
		return true;
	}

	std::wstring normalized(const std::wstring& path)
	{
		wchar_t full[32768]{};
		const DWORD length = GetFullPathNameW(path.c_str(), 32768, full, nullptr);
		std::wstring result = length > 0 && length < 32768 ? full : path;
		std::replace(result.begin(), result.end(), L'/', L'\\');
		std::transform(result.begin(), result.end(), result.begin(), ::towlower);
		return result;
	}

	bool readRegistryString(HKEY key, const wchar_t* name, std::wstring& value)
	{
		value.clear();
		DWORD size = 0;
		const LSTATUS measured = RegGetValueW(key, nullptr, name, RRF_RT_REG_SZ, nullptr, nullptr, &size);
		if (measured != ERROR_SUCCESS || size < sizeof(wchar_t) || size > 4096
			|| size % sizeof(wchar_t) != 0) return false;
		std::vector<wchar_t> buffer(size / sizeof(wchar_t), L'\0');
		DWORD type = 0;
		if (RegGetValueW(key, nullptr, name, RRF_RT_REG_SZ, &type, buffer.data(), &size) != ERROR_SUCCESS
			|| type != REG_SZ || buffer.back() != L'\0') return false;
		value = trim(buffer.data());
		return value.find_first_of(L"\r\n\0", 0, 3) == std::wstring::npos;
	}

	bool currentProcessElevated();

	bool currentProcessIsLocalAdminAccount(std::wstring& error)
	{
		HANDLE token = nullptr;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
		{
			error = L"O Windows nao conseguiu confirmar a conta Admin atual.";
			return false;
		}
		DWORD bytes = 0;
		GetTokenInformation(token, TokenUser, nullptr, 0, &bytes);
		std::vector<unsigned char> tokenUser(bytes);
		const bool read = bytes > 0 && bytes <= 64 * 1024
			&& GetTokenInformation(token, TokenUser, tokenUser.data(), bytes, &bytes) != FALSE;
		CloseHandle(token);
		if (!read)
		{
			error = L"O SID da conta Admin atual nao pode ser confirmado.";
			return false;
		}
		wchar_t name[257]{}, domain[257]{};
		DWORD nameSize = 257, domainSize = 257;
		SID_NAME_USE use = SidTypeUnknown;
		if (!LookupAccountSidW(nullptr, reinterpret_cast<TOKEN_USER*>(tokenUser.data())->User.Sid,
			name, &nameSize, domain, &domainSize, &use) || use != SidTypeUser)
		{
			error = L"A identidade local da conta Admin nao pode ser resolvida.";
			return false;
		}
		wchar_t machine[MAX_COMPUTERNAME_LENGTH + 1]{};
		DWORD machineSize = MAX_COMPUTERNAME_LENGTH + 1;
		if (!GetComputerNameW(machine, &machineSize) || _wcsicmp(name, L"Admin") != 0
			|| _wcsicmp(domain, machine) != 0)
		{
			error = L"Execute este configurador na conta local Admin deste gabinete.";
			return false;
		}
		return true;
	}

	bool resolveAutomaticKioskAccount(KioskAccount& account, std::wstring& error)
	{
		account = {};
		HKEY winlogon = nullptr;
		const LSTATUS opened = RegOpenKeyExW(HKEY_LOCAL_MACHINE,
			L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon", 0, KEY_QUERY_VALUE, &winlogon);
		if (opened != ERROR_SUCCESS)
		{
			error = L"A configura\u00E7\u00E3o da conta Windows do TurboRama n\u00E3o est\u00E1 acess\u00EDvel.";
			return false;
		}
		std::wstring autoLogon, configuredUser, configuredDomain;
		const bool registryOk = readRegistryString(winlogon, L"AutoAdminLogon", autoLogon)
			&& readRegistryString(winlogon, L"DefaultUserName", configuredUser);
		readRegistryString(winlogon, L"DefaultDomainName", configuredDomain);
		RegCloseKey(winlogon);
		if (!registryOk || autoLogon != L"1" || configuredUser.empty() || configuredUser.size() > 256)
		{
			error = L"O AutoLogon do TurboRama n\u00E3o est\u00E1 configurado corretamente.";
			return false;
		}

		wchar_t machineBuffer[MAX_COMPUTERNAME_LENGTH + 1]{};
		DWORD machineLength = MAX_COMPUTERNAME_LENGTH + 1;
		if (!GetComputerNameW(machineBuffer, &machineLength) || machineLength == 0)
		{
			error = L"O nome local deste computador n\u00E3o p\u00F4de ser confirmado.";
			return false;
		}
		const std::wstring machine(machineBuffer, machineLength);
		std::wstring user = configuredUser;
		std::wstring domain = configuredDomain;
		const size_t separator = user.find(L'\\');
		if (separator != std::wstring::npos)
		{
			domain = user.substr(0, separator);
			user = user.substr(separator + 1);
		}
		if (domain.empty() || domain == L".") domain = machine;
		if (user.empty() || user.find_first_of(L"\\/@\r\n") != std::wstring::npos
			|| _wcsicmp(domain.c_str(), machine.c_str()) != 0)
		{
			error = L"A conta Windows do TurboRama precisa ser uma conta local v\u00E1lida deste computador.";
			return false;
		}

		const std::wstring fullAccount = machine + L"\\" + user;
		DWORD sidSize = 0, resolvedDomainSize = 0;
		SID_NAME_USE use = SidTypeUnknown;
		LookupAccountNameW(nullptr, fullAccount.c_str(), nullptr, &sidSize, nullptr, &resolvedDomainSize, &use);
		if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || sidSize == 0
			|| sidSize > SECURITY_MAX_SID_SIZE || resolvedDomainSize == 0 || resolvedDomainSize > 1024)
		{
			error = L"A conta Windows configurada no TurboRama n\u00E3o p\u00F4de ser resolvida pelo Windows.";
			return false;
		}
		std::vector<unsigned char> sid(sidSize);
		std::vector<wchar_t> resolvedDomain(resolvedDomainSize, L'\0');
		if (!LookupAccountNameW(nullptr, fullAccount.c_str(), sid.data(), &sidSize,
			resolvedDomain.data(), &resolvedDomainSize, &use) || use != SidTypeUser || !IsValidSid(sid.data())
			|| _wcsicmp(resolvedDomain.data(), machine.c_str()) != 0)
		{
			error = L"A conta Windows configurada no TurboRama n\u00E3o \u00E9 um usu\u00E1rio local v\u00E1lido.";
			return false;
		}
		account.user = user;
		account.domain = machine;
		account.sid = std::move(sid);
		return true;
	}

	bool tokenMatchesKiosk(HANDLE token, const KioskAccount& account, std::wstring& error)
	{
		DWORD bytes = 0;
		GetTokenInformation(token, TokenUser, nullptr, 0, &bytes);
		if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || bytes == 0 || bytes > 64 * 1024)
		{
			error = L"O Windows n\u00E3o conseguiu confirmar o SID da conta Windows do TurboRama.";
			return false;
		}
		std::vector<unsigned char> tokenUser(bytes);
		if (!GetTokenInformation(token, TokenUser, tokenUser.data(), bytes, &bytes)
			|| !EqualSid(reinterpret_cast<TOKEN_USER*>(tokenUser.data())->User.Sid,
				const_cast<unsigned char*>(account.sid.data())))
		{
			error = L"A credencial informada n\u00E3o pertence \u00E0 conta Windows configurada no TurboRama.";
			return false;
		}
		return true;
	}

	void clearKioskSessionOverrides()
	{
		// A conta Windows do TurboRama pode ter herdado variaveis usadas somente em
		// laboratorios. A ativacao comercial sempre usa os caminhos e o runtime
		// fechados do produto, nunca valores controlados pelo ambiente.
		const wchar_t* names[] = {
			L"TURBORAMA_PIX_BRIDGE_DIRECTORY",
			L"TURBORAMA_PIX_PROVIDER",
			L"TURBORAMA_PIX_ADAPTER_BASE_URL",
			L"TURBORAMA_PIX_ADAPTER_PROVIDER_ID",
			L"TURBORAMA_PIX_MANAGER_TOKEN",
			L"TURBORAMA_PIX_NOMINATIM_BASE_URL",
			L"DOTNET_STARTUP_HOOKS",
			L"DOTNET_ADDITIONAL_DEPS",
			L"DOTNET_SHARED_STORE",
			L"DOTNET_HOST_PATH",
			L"CORECLR_ENABLE_PROFILING",
			L"CORECLR_PROFILER",
			L"CORECLR_PROFILER_PATH",
			L"CORECLR_PROFILER_PATH_32",
			L"CORECLR_PROFILER_PATH_64",
			L"COMPLUS_ProfAPI_ProfilerCompatibilitySetting"
		};
		for (const wchar_t* name : names) SetEnvironmentVariableW(name, nullptr);
	}

	bool currentProcessMatchesKiosk(const KioskAccount& account, std::wstring& error)
	{
		HANDLE token = nullptr;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
		{
			error = L"O Windows n\u00E3o conseguiu confirmar o usu\u00E1rio deste processo.";
			return false;
		}
		const bool matches = tokenMatchesKiosk(token, account, error);
		CloseHandle(token);
		return matches;
	}

	bool currentProcessElevated()
	{
		HANDLE token = nullptr;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
		TOKEN_ELEVATION elevation{};
		DWORD bytes = 0;
		const bool elevated = GetTokenInformation(token, TokenElevation, &elevation,
			sizeof(elevation), &bytes) != FALSE && elevation.TokenIsElevated != 0;
		CloseHandle(token);
		return elevated;
	}

	bool launchElevatedEditor(std::wstring& error)
	{
		wchar_t module[32768]{};
		const DWORD moduleLength = GetModuleFileNameW(nullptr, module, 32768);
		if (moduleLength == 0 || moduleLength >= 32768 || !fileExists(module))
		{
			error = L"O executavel deste configurador nao pode ser localizado para autorizacao administrativa.";
			return false;
		}
		const std::wstring workingDirectory = parentOf(module);
		SHELLEXECUTEINFOW execution{};
		execution.cbSize = sizeof(execution);
		execution.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC;
		execution.lpVerb = L"runas";
		execution.lpFile = module;
		execution.lpDirectory = workingDirectory.c_str();
		execution.nShow = SW_SHOWNORMAL;
		if (!ShellExecuteExW(&execution))
		{
			const DWORD code = GetLastError();
			error = code == ERROR_CANCELLED
				? L"A autorizacao administrativa foi cancelada. Nenhum codigo foi enviado ao servidor."
				: L"O Windows nao conseguiu autorizar o configurador no usuario Admin.";
			const std::wstring detail = windowsErrorMessage(code);
			if (code != ERROR_CANCELLED && !detail.empty()) error += L"\n\n" + detail;
			return false;
		}
		if (execution.hProcess) CloseHandle(execution.hProcess);
		return true;
	}

	bool resolveAgentCommand(const std::wstring& bridge, const std::wstring& mode, std::wstring& root,
		std::wstring& executable, std::wstring& command, std::wstring& error)
	{
		wchar_t module[32768]{};
		if (GetModuleFileNameW(nullptr, module, 32768) == 0)
		{
			error = L"N\u00E3o foi poss\u00EDvel localizar a instala\u00E7\u00E3o do editor PIX.";
			return false;
		}
		root = parentOf(module);
		std::wstring dotnet = join(root, L"pix-agent\\runtime\\dotnet.exe");
		std::wstring assembly = join(root, L"pix-agent\\TurboRamaPixAgent.dll");
		std::wstring appHost = join(root, L"pix-agent\\TurboRamaPixAgent.exe");
		if ((!fileExists(dotnet) || !fileExists(assembly)) && !fileExists(appHost))
		{
			root = L"D:\\emulationstation";
			dotnet = join(root, L"pix-agent\\runtime\\dotnet.exe");
			assembly = join(root, L"pix-agent\\TurboRamaPixAgent.dll");
			appHost = join(root, L"pix-agent\\TurboRamaPixAgent.exe");
		}
		if (fileExists(dotnet) && fileExists(assembly))
		{
			std::string trustError;
			if (!PixBinaryTrust::verifyCommercialAgentBundle(join(root, L"pix-agent"), trustError)
				|| !PixBinaryTrust::verifyTrustedRuntime(dotnet, trustError)
				|| !PixBinaryTrust::verifyVendorBinary(assembly, trustError))
			{
				error = wideUtf8(trustError);
				return false;
			}
			executable = dotnet;
			command = L"\"" + dotnet + L"\" \"" + assembly + L"\" " + mode + L" --bridge \"" + bridge + L"\"";
		}
		else if (PixBinaryTrust::required())
		{
			error = L"O runtime privado assinado do agente PIX est\u00E1 ausente; o fallback global foi recusado.";
			return false;
		}
		else if (fileExists(appHost))
		{
			std::string trustError;
			if (!PixBinaryTrust::verifyVendorBinary(appHost, trustError))
			{
				error = wideUtf8(trustError);
				return false;
			}
			executable = appHost;
			command = L"\"" + appHost + L"\" " + mode + L" --bridge \"" + bridge + L"\"";
		}
		else { error = L"O agente PIX n\u00E3o foi instalado. Execute primeiro o instalador comercial v25."; return false; }
		return true;
	}

	void drainAgentOutput(HANDLE pipe, std::string& output)
	{
		char buffer[2048];
		for (;;)
		{
			DWORD available = 0;
			if (!PeekNamedPipe(pipe, nullptr, 0, nullptr, &available, nullptr) || available == 0) return;
			DWORD received = 0;
			const DWORD requested = std::min<DWORD>(available, static_cast<DWORD>(sizeof(buffer)));
			if (!ReadFile(pipe, buffer, requested, &received, nullptr) || received == 0) return;
			if (output.size() < kAgentOutputLimit)
			{
				const size_t remaining = kAgentOutputLimit - output.size();
				output.append(buffer, std::min<size_t>(received, remaining));
			}
		}
	}

	bool runAgentCommand(const std::wstring& bridge, const std::wstring& mode, DWORD timeoutMs,
		AgentCommandResult& result, std::wstring& error)
	{
		result = {};
		std::wstring root, executable, command;
		if (!resolveAgentCommand(bridge, mode, root, executable, command, error)) return false;

		SECURITY_ATTRIBUTES security{};
		security.nLength = sizeof(security);
		security.bInheritHandle = TRUE;
		HANDLE readPipe = nullptr, writePipe = nullptr;
		if (!CreatePipe(&readPipe, &writePipe, &security, 0)
			|| !SetHandleInformation(readPipe, HANDLE_FLAG_INHERIT, 0))
		{
			const DWORD code = GetLastError();
			if (readPipe) CloseHandle(readPipe);
			if (writePipe) CloseHandle(writePipe);
			error = L"O Windows n\u00E3o conseguiu abrir o canal de diagn\u00F3stico do agente PIX";
			const std::wstring detail = windowsErrorMessage(code);
			if (!detail.empty()) error += L": " + detail;
			return false;
		}

		HANDLE nullInput = CreateFileW(L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
			&security, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
		if (nullInput == INVALID_HANDLE_VALUE)
		{
			const DWORD code = GetLastError();
			CloseHandle(readPipe); CloseHandle(writePipe);
			error = L"O Windows n\u00E3o conseguiu preparar a entrada segura do agente PIX";
			const std::wstring detail = windowsErrorMessage(code);
			if (!detail.empty()) error += L": " + detail;
			return false;
		}

		std::vector<wchar_t> mutableCommand(command.begin(), command.end());
		mutableCommand.push_back(L'\0');
		SIZE_T attributeBytes = 0;
		InitializeProcThreadAttributeList(nullptr, 1, 0, &attributeBytes);
		std::vector<unsigned char> attributeStorage(attributeBytes);
		auto attributes = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(attributeStorage.data());
		HANDLE inherited[] = { nullInput, writePipe };
		const bool attributesReady = attributeBytes != 0
			&& InitializeProcThreadAttributeList(attributes, 1, 0, &attributeBytes) != FALSE;
		if (!attributesReady || !UpdateProcThreadAttribute(attributes, 0,
			PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited, sizeof(inherited), nullptr, nullptr))
		{
			if (attributesReady) DeleteProcThreadAttributeList(attributes);
			CloseHandle(writePipe);
			CloseHandle(readPipe);
			CloseHandle(nullInput);
			error = L"O Windows n\u00E3o conseguiu isolar os canais do agente PIX.";
			return false;
		}
		STARTUPINFOEXW startup{};
		startup.StartupInfo.cb = sizeof(startup);
		startup.StartupInfo.dwFlags = STARTF_USESHOWWINDOW | STARTF_USESTDHANDLES;
		startup.StartupInfo.wShowWindow = SW_HIDE;
		startup.StartupInfo.hStdInput = nullInput;
		startup.StartupInfo.hStdOutput = writePipe;
		startup.StartupInfo.hStdError = writePipe;
		startup.lpAttributeList = attributes;
		PROCESS_INFORMATION process{};
		std::vector<wchar_t> environment;
		std::string environmentError;
		if (!PixBinaryTrust::buildSanitizedDotnetEnvironment(
			join(root, L"pix-agent\\runtime"), {}, environment, environmentError))
		{
			DeleteProcThreadAttributeList(attributes);
			CloseHandle(writePipe);
			CloseHandle(readPipe);
			CloseHandle(nullInput);
			error = L"O Windows n\u00E3o conseguiu preparar o ambiente protegido do agente PIX.";
			return false;
		}
		const BOOL created = CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, TRUE,
			CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT | EXTENDED_STARTUPINFO_PRESENT,
			environment.data(), root.c_str(), &startup.StartupInfo, &process);
		const DWORD creationError = created ? ERROR_SUCCESS : GetLastError();
		SecureZeroMemory(environment.data(), environment.size() * sizeof(wchar_t));
		DeleteProcThreadAttributeList(attributes);
		CloseHandle(writePipe);
		CloseHandle(nullInput);
		if (!created)
		{
			CloseHandle(readPipe);
			error = L"N\u00E3o foi poss\u00EDvel iniciar o agente PIX";
			const std::wstring detail = windowsErrorMessage(creationError);
			if (!detail.empty()) error += L": " + detail;
			return false;
		}

		result.launched = true;
		CloseHandle(process.hThread);
		const ULONGLONG started = GetTickCount64();
		for (;;)
		{
			drainAgentOutput(readPipe, result.output);
			const DWORD wait = WaitForSingleObject(process.hProcess, 50);
			if (wait == WAIT_OBJECT_0)
			{
				result.exitConfirmed = true;
				break;
			}
			if (wait == WAIT_FAILED) break;
			if (GetTickCount64() - started < timeoutMs) continue;
			result.timedOut = true;
			const bool terminated = TerminateProcess(process.hProcess, 21) != FALSE;
			result.exitConfirmed = terminated && WaitForSingleObject(process.hProcess, 5000) == WAIT_OBJECT_0;
			break;
		}
		drainAgentOutput(readPipe, result.output);
		if (result.exitConfirmed) GetExitCodeProcess(process.hProcess, &result.exitCode);
		CloseHandle(process.hProcess);
		CloseHandle(readPipe);
		if (!result.exitConfirmed && !result.timedOut)
		{
			error = L"O Windows n\u00E3o confirmou a sa\u00EDda do agente PIX.";
			return false;
		}
		return true;
	}

	void appendAgentFailureDetail(std::wstring& error, const AgentCommandResult& result)
	{
		const std::wstring detail = sanitizeAgentOutput(result.output);
		if (!detail.empty()) error += L"\n\nMotivo informado pelo servi\u00E7o PIX: " + detail;
		if (result.exitConfirmed) error += L"\n\nC\u00F3digo do agente: " + std::to_wstring(result.exitCode) + L".";
	}

	bool mapKioskIdentityResult(const AgentCommandResult& result, std::wstring& error)
	{
		if (result.launched && result.exitConfirmed && !result.timedOut && result.exitCode == 0) return true;
		error = L"Este programa s\u00F3 pode proteger a credencial na conta Windows configurada no TurboRama.";
		if (result.timedOut)
			error += L" O teste de identidade ultrapassou o tempo de seguran\u00E7a e foi encerrado.";
		appendAgentFailureDetail(error, result);
		return false;
	}

	bool validateKioskIdentity(const std::wstring& bridge, std::wstring& error)
	{
		AgentCommandResult result;
		if (!runAgentCommand(bridge, L"--check-kiosk-identity", kIdentityPreflightTimeoutMs, result, error)) return false;
		return mapKioskIdentityResult(result, error);
	}

	bool ensureAgentPublicKey(const std::wstring& bridge, std::wstring& error)
	{
		if (fileExists(join(bridge, kPublicKeyFile))) return true;
		AgentCommandResult result;
		if (!runAgentCommand(bridge, L"--prepare-credential-editor", kAgentAdministrativeTimeoutMs, result, error)) return false;
		if (result.timedOut)
		{
			error = L"O agente PIX ultrapassou 90 segundos ao preparar a chave segura e foi encerrado.";
			appendAgentFailureDetail(error, result);
			return false;
		}
		if (result.exitCode != 0 && result.exitCode != 12)
		{
			error = L"O servi\u00E7o PIX recusou a prepara\u00E7\u00E3o da chave segura.";
			appendAgentFailureDetail(error, result);
			return false;
		}
		for (int attempt = 0; attempt < 160; ++attempt)
		{
			if (fileExists(join(bridge, kPublicKeyFile))) return true;
			Sleep(200);
		}
		error = L"O agente PIX foi iniciado, mas n\u00E3o publicou a chave segura para a conta Windows configurada no TurboRama.";
		appendAgentFailureDetail(error, result);
		return false;
	}

	bool triggerCredentialAcceptance(const std::wstring& bridge, std::wstring& error)
	{
		AgentCommandResult result;
		if (!runAgentCommand(bridge, L"--accept-credential-once", kAgentAdministrativeTimeoutMs, result, error)) return false;
		if (!result.timedOut && (result.exitCode == 0 || result.exitCode == 12)) return true;
		error = result.timedOut
			? L"O agente PIX ultrapassou 90 segundos ao receber a credencial cifrada e foi encerrado."
			: L"O agente PIX recusou o recebimento da credencial cifrada.";
		appendAgentFailureDetail(error, result);
		return false;
	}

	bool isStatus(const std::string& text, const char* value)
	{
		const std::string compact = std::string("\"state\":\"") + value + "\"";
		const std::string formatted = std::string("\"state\": \"") + value + "\"";
		return text.find(compact) != std::string::npos || text.find(formatted) != std::string::npos;
	}

	bool waitForCredentialAcceptance(const std::wstring& bridge, const std::string& requestId, std::wstring& error)
	{
		const std::wstring status = join(bridge, kCredentialUpdateStatusFile);
		for (int attempt = 0; attempt < 160; ++attempt)
		{
			std::string text;
			if (readAll(status, text) && text.find(requestId) != std::string::npos)
			{
				if (isStatus(text, "accepted")) return true;
				if (isStatus(text, "rejected")) { error = L"O agente PIX recusou a atualiza\u00E7\u00E3o. Confira se o Access Token APP_USR foi copiado completo e tente novamente."; return false; }
			}
			Sleep(250);
		}
		error = L"O token cifrado foi entregue, mas o agente PIX n\u00E3o confirmou em 40 segundos. Abra o EmulationStation e veja o status do PIX antes de tentar cobrar.";
		return false;
	}

	bool submitTokenToAgent(const std::wstring& bridge, const std::string& token, std::wstring& error)
	{
		if (!validateKioskIdentity(bridge, error)) return false;
		if (!validToken(token, error)) return false;
		if (!ensureDirectory(bridge)) { error = L"N\u00E3o foi poss\u00EDvel criar ou acessar a pasta PIX."; return false; }
		if (!ensureAgentPublicKey(bridge, error)) return false;
		std::string fingerprint, encrypted, payload = token;
		if (!encryptForAgent(join(bridge, kPublicKeyFile), payload, fingerprint, encrypted, error))
		{ SecureZeroMemory(payload.data(), payload.size()); return false; }
		SecureZeroMemory(payload.data(), payload.size());
		const std::string requestId = "CRED-" + std::to_string((unsigned long long)GetTickCount64()) + "-" + std::to_string(GetCurrentProcessId());
		const bool written = writeCredentialUpdate(bridge, requestId, fingerprint, encrypted, error);
		SecureZeroMemory(encrypted.data(), encrypted.size());
		if (!written) return false;
		if (!triggerCredentialAcceptance(bridge, error))
		{
			DeleteFileW(join(bridge, kCredentialUpdateFile).c_str());
			return false;
		}
		return waitForCredentialAcceptance(bridge, requestId, error);
	}

	bool validOnlineIdentifier(const std::wstring& value)
	{
		if (value.size() < 6 || value.size() > 64) return false;
		return std::all_of(value.begin(), value.end(), [](wchar_t character) {
			return (character >= L'a' && character <= L'z') || (character >= L'A' && character <= L'Z')
				|| (character >= L'0' && character <= L'9') || character == L'-' || character == L'_';
		});
	}

	bool validActivationCode(const std::wstring& value)
	{
		if (value.size() < 16 || value.size() > 128) return false;
		return std::all_of(value.begin(), value.end(), [](wchar_t character) {
			return character >= 0x21 && character <= 0x7e;
		});
	}

	std::string jsonString(const std::string& value)
	{
		std::string result = "\"";
		for (const unsigned char character : value)
		{
			if (character == '"' || character == '\\') result.push_back('\\');
			if (character >= 0x20) result.push_back(static_cast<char>(character));
		}
		result.push_back('"');
		return result;
	}

	std::string extractJsonString(const std::string& json, const std::string& name)
	{
		const std::string marker = "\"" + name + "\"";
		size_t position = json.find(marker);
		if (position == std::string::npos) return {};
		position = json.find(':', position + marker.size());
		if (position == std::string::npos) return {};
		position = json.find('"', position + 1);
		if (position == std::string::npos) return {};
		std::string result;
		for (++position; position < json.size(); ++position)
		{
			if (json[position] == '"') return result;
			if (json[position] == '\\' || static_cast<unsigned char>(json[position]) < 0x20) return {};
			result.push_back(json[position]);
		}
		return {};
	}

	std::string onlineConfigurationJson(const std::wstring& license, const std::wstring& profile)
	{
		return "{\"schemaVersion\":1,\"baseUrl\":\"https://pix.lzgames.com.br/\",\"licenseId\":"
			+ jsonString(utf8(license)) + ",\"protectionProfile\":" + jsonString(utf8(profile))
			+ "}";
	}

	bool replaceAtomically(const std::wstring& destination, const std::string& text, std::wstring& error)
	{
		const std::wstring temporary = destination + L"." + std::to_wstring(GetCurrentProcessId()) + L".restore";
		DeleteFileW(temporary.c_str());
		if (!writeAll(temporary, text))
		{
			error = L"O Windows nao conseguiu gravar a restauracao temporaria.";
			return false;
		}
		if (!MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
		{
			DeleteFileW(temporary.c_str());
			error = L"O Windows nao conseguiu restaurar o cadastro anterior.";
			return false;
		}
		return true;
	}

	bool runAgentSecretCommand(const std::wstring& bridge, const std::wstring& mode,
		const std::string& secret, DWORD timeoutMs, AgentCommandResult& result, std::wstring& error)
	{
		result = {};
		std::wstring root, executable, command;
		if (!resolveAgentCommand(bridge, mode, root, executable, command, error)) return false;
		SECURITY_ATTRIBUTES security{ sizeof(SECURITY_ATTRIBUTES), nullptr, TRUE };
		HANDLE stdinRead = nullptr, stdinWrite = nullptr, stdoutRead = nullptr, stdoutWrite = nullptr;
		auto closeOne = [](HANDLE& handle) { if (handle) CloseHandle(handle); handle = nullptr; };
		auto closeAll = [&]() { closeOne(stdinRead); closeOne(stdinWrite); closeOne(stdoutRead); closeOne(stdoutWrite); };
		if (!CreatePipe(&stdinRead, &stdinWrite, &security, 0)
			|| !CreatePipe(&stdoutRead, &stdoutWrite, &security, 0)
			|| !SetHandleInformation(stdinWrite, HANDLE_FLAG_INHERIT, 0)
			|| !SetHandleInformation(stdoutRead, HANDLE_FLAG_INHERIT, 0))
		{
			closeAll();
			error = L"O Windows nao conseguiu criar o canal protegido do codigo unico.";
			return false;
		}
		SIZE_T bytes = 0;
		InitializeProcThreadAttributeList(nullptr, 1, 0, &bytes);
		std::vector<unsigned char> storage(bytes);
		auto attributes = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(storage.data());
		HANDLE inherited[] = { stdinRead, stdoutWrite };
		const bool initialized = bytes != 0
			&& InitializeProcThreadAttributeList(attributes, 1, 0, &bytes) != FALSE;
		if (!initialized || !UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
			inherited, sizeof(inherited), nullptr, nullptr))
		{
			if (initialized) DeleteProcThreadAttributeList(attributes);
			closeAll();
			error = L"O Windows nao conseguiu isolar o canal protegido do codigo unico.";
			return false;
		}
		std::vector<wchar_t> mutableCommand(command.begin(), command.end());
		mutableCommand.push_back(L'\0');
		STARTUPINFOEXW startup{};
		startup.StartupInfo.cb = sizeof(startup);
		startup.StartupInfo.dwFlags = STARTF_USESHOWWINDOW | STARTF_USESTDHANDLES;
		startup.StartupInfo.wShowWindow = SW_HIDE;
		startup.StartupInfo.hStdInput = stdinRead;
		startup.StartupInfo.hStdOutput = stdoutWrite;
		startup.StartupInfo.hStdError = stdoutWrite;
		startup.lpAttributeList = attributes;
		PROCESS_INFORMATION process{};
		std::vector<wchar_t> environment;
		std::string environmentError;
		if (!PixBinaryTrust::buildSanitizedDotnetEnvironment(join(root, L"pix-agent\\runtime"),
			{}, environment, environmentError))
		{
			DeleteProcThreadAttributeList(attributes);
			closeAll();
			error = L"O Windows nao conseguiu preparar o ambiente protegido do agente PIX.";
			return false;
		}
		const BOOL created = CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, TRUE,
			CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT | EXTENDED_STARTUPINFO_PRESENT,
			environment.data(), root.c_str(), &startup.StartupInfo, &process);
		const DWORD creationError = created ? ERROR_SUCCESS : GetLastError();
		SecureZeroMemory(environment.data(), environment.size() * sizeof(wchar_t));
		DeleteProcThreadAttributeList(attributes);
		closeOne(stdinRead);
		closeOne(stdoutWrite);
		if (!created)
		{
			closeOne(stdinWrite);
			closeOne(stdoutRead);
			error = L"Nao foi possivel iniciar o agente PIX";
			const std::wstring detail = windowsErrorMessage(creationError);
			if (!detail.empty()) error += L": " + detail;
			return false;
		}
		result.launched = true;
		CloseHandle(process.hThread);
		std::string secretLine = secret + "\r\n";
		DWORD written = 0;
		const bool delivered = WriteFile(stdinWrite, secretLine.data(), static_cast<DWORD>(secretLine.size()),
			&written, nullptr) != FALSE && written == secretLine.size();
		SecureZeroMemory(secretLine.data(), secretLine.size());
		closeOne(stdinWrite);
		if (!delivered) TerminateProcess(process.hProcess, 24);
		const ULONGLONG started = GetTickCount64();
		for (;;)
		{
			drainAgentOutput(stdoutRead, result.output);
			const DWORD wait = WaitForSingleObject(process.hProcess, 50);
			if (wait == WAIT_OBJECT_0) { result.exitConfirmed = true; break; }
			if (wait == WAIT_FAILED) break;
			if (GetTickCount64() - started < timeoutMs) continue;
			result.timedOut = true;
			const bool terminated = TerminateProcess(process.hProcess, 25) != FALSE;
			result.exitConfirmed = terminated && WaitForSingleObject(process.hProcess, 5000) == WAIT_OBJECT_0;
			break;
		}
		drainAgentOutput(stdoutRead, result.output);
		if (result.exitConfirmed) GetExitCodeProcess(process.hProcess, &result.exitCode);
		CloseHandle(process.hProcess);
		closeOne(stdoutRead);
		if (!delivered)
		{
			error = L"O codigo unico nao foi entregue ao agente PIX.";
			return false;
		}
		if (!result.exitConfirmed && !result.timedOut)
		{
			error = L"O Windows nao confirmou a saida do agente PIX.";
			return false;
		}
		return true;
	}

	bool activateOnlineMachineLocal(const std::wstring& license, const std::wstring& profile,
		std::string& activationCode, std::wstring& error, bool& indeterminate)
	{
		indeterminate = false;
		const std::wstring bridge = bridgeDirectory();
		AgentCommandResult identity;
		if (!runAgentCommand(bridge, L"--check-kiosk-identity", kIdentityPreflightTimeoutMs, identity, error)
			|| identity.timedOut || !identity.exitConfirmed || identity.exitCode != 0)
		{
			if (error.empty())
				error = L"Abra este programa diretamente na conta Windows configurada no TurboRama. Neste gabinete a conta correta e Admin.";
			appendAgentFailureDetail(error, identity);
			return false;
		}
		if (!ensureDirectory(bridge))
		{
			error = L"Nao foi possivel acessar a pasta protegida do PIX.";
			return false;
		}
		const std::wstring settingsPath = join(bridge, kOwnerSettingsFile);
		const bool previousExisted = fileExists(settingsPath);
		std::string previous;
		if (previousExisted && !readAll(settingsPath, previous))
		{
			error = L"O cadastro PIX anterior nao pode ser lido com seguranca.";
			return false;
		}
		const std::string configuration = onlineConfigurationJson(license, profile);
		const std::wstring request = join(bridge,
			L"online-activation-" + std::to_wstring(GetCurrentProcessId()) + L".json");
		DeleteFileW(request.c_str());
		if (!writeAll(request, configuration))
		{
			error = L"Nao foi possivel preparar o cadastro on-line temporario.";
			return false;
		}
		AgentCommandResult configured;
		const bool configureRan = runAgentCommand(bridge, L"--online-configure \"" + request + L"\"",
			kAgentAdministrativeTimeoutMs, configured, error);
		DeleteFileW(request.c_str());
		if (!configureRan || configured.timedOut || !configured.exitConfirmed || configured.exitCode != 0)
		{
			if (error.empty())
				error = configured.exitCode == 12
					? L"Feche o EmulationStation e aguarde o servico PIX encerrar antes de ativar."
					: L"O agente PIX recusou o cadastro on-line.";
			appendAgentFailureDetail(error, configured);
			return false;
		}
		AgentCommandResult activated;
		const bool activationRan = runAgentSecretCommand(bridge, L"--online-activate", activationCode,
			180000, activated, error);
		SecureZeroMemory(activationCode.data(), activationCode.size());
		activationCode.clear();
		if (activationRan && !activated.timedOut && activated.exitConfirmed && activated.exitCode == 0)
			return true;
		const bool mayHaveCompleted = !activationRan || activated.timedOut || !activated.exitConfirmed
			|| activated.exitCode == 25;
		if (mayHaveCompleted)
		{
			indeterminate = true;
			error = L"A resposta final nao foi confirmada. O cadastro foi preservado para conferencia no painel. Nao gere outro codigo ainda.";
			appendAgentFailureDetail(error, activated);
			return false;
		}
		std::wstring restoreError;
		const bool restored = previousExisted
			? replaceAtomically(settingsPath, previous, restoreError)
			: (DeleteFileW(settingsPath.c_str()) != FALSE || GetLastError() == ERROR_FILE_NOT_FOUND);
		error = L"O servidor recusou a ativacao desta maquina.";
		appendAgentFailureDetail(error, activated);
		if (!restored) error += L"\n\nATENCAO: " + restoreError;
		return false;
	}

	bool activateOnlineMachine(const std::wstring& license, const std::wstring& profile,
		std::string& activationCode, std::wstring& error, bool& indeterminate)
	{
		KioskAccount account;
		if (!resolveAutomaticKioskAccount(account, error)) return false;
		std::wstring identityError;
		if (currentProcessMatchesKiosk(account, identityError))
			return activateOnlineMachineLocal(license, profile, activationCode, error, indeterminate);
		error = L"Este configurador precisa ser executado diretamente na conta Windows configurada no TurboRama: "
			+ account.domain + L"\\" + account.user + L".\n\n"
			L"Regra atual deste gabinete: use somente Admin nesta ativacao.";
		if (!identityError.empty()) error += L"\n\nDetalhe: " + identityError;
		return false;
	}

	HFONT makeFont(int height, int weight)
	{
		return CreateFontW(height, 0, 0, 0, weight, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
			OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
			DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
	}

	COLORREF blend(COLORREF first, COLORREF second, int position, int total)
	{
		if (total <= 0) return first;
		auto channel = [position, total](BYTE a, BYTE b) {
			return static_cast<BYTE>(a + (static_cast<int>(b) - a) * position / total);
		};
		return RGB(channel(GetRValue(first), GetRValue(second)),
			channel(GetGValue(first), GetGValue(second)),
			channel(GetBValue(first), GetBValue(second)));
	}

	void fill(HDC device, const RECT& area, COLORREF color)
	{
		HBRUSH brush = CreateSolidBrush(color);
		FillRect(device, &area, brush);
		DeleteObject(brush);
	}

	void verticalGradient(HDC device, const RECT& area, COLORREF top, COLORREF bottom)
	{
		const int height = std::max(1L, area.bottom - area.top);
		for (int y = 0; y < height; y += 3)
		{
			RECT line{ area.left, area.top + y, area.right, std::min(area.bottom, area.top + y + 3) };
			fill(device, line, blend(top, bottom, y, height));
		}
	}

	void roundedBox(HDC device, const RECT& area, COLORREF background, COLORREF border, int radius, int borderWidth = 1)
	{
		HBRUSH brush = CreateSolidBrush(background);
		HPEN pen = CreatePen(PS_SOLID, borderWidth, border);
		HGDIOBJ oldBrush = SelectObject(device, brush);
		HGDIOBJ oldPen = SelectObject(device, pen);
		RoundRect(device, area.left, area.top, area.right, area.bottom, radius, radius);
		SelectObject(device, oldPen);
		SelectObject(device, oldBrush);
		DeleteObject(pen);
		DeleteObject(brush);
	}

	void text(HDC device, HFONT font, COLORREF color, const wchar_t* value, RECT area, UINT format)
	{
		HGDIOBJ previous = SelectObject(device, font);
		SetBkMode(device, TRANSPARENT);
		SetTextColor(device, color);
		DrawTextW(device, value, -1, &area, format | DT_NOPREFIX);
		SelectObject(device, previous);
	}

	void paintInterface(HWND window, HDC device)
	{
		RECT client{};
		GetClientRect(window, &client);
		fill(device, client, kBackground);

		// A malha quase imperceptivel acrescenta profundidade sem competir com
		// os controles. Toda a interface continua sendo desenhada nativamente.
		for (int x = 32; x < client.right; x += 96)
		{
			RECT gridLine{ x, 148, x + 1, client.bottom };
			fill(device, gridLine, RGB(8, 15, 22));
		}

		RECT header{ 0, 0, client.right, 148 };
		verticalGradient(device, header, kHeaderTop, kHeaderBottom);
		for (int x = 0; x < client.right; x += 3)
		{
			RECT segment{ x, 0, std::min<LONG>(client.right, static_cast<LONG>(x + 3)), 5 };
			fill(device, segment, blend(kGreen, kCyan, x, std::max(1L, client.right)));
		}
		RECT headerDivider{ 32, 142, client.right - 32, 144 };
		fill(device, headerDivider, RGB(28, 44, 56));
		for (int x = 32; x < client.right - 32; x += 3)
		{
			RECT glow{ x, 142, std::min<LONG>(client.right - 32, static_cast<LONG>(x + 3)), 144 };
			fill(device, glow, blend(kGreen, kCyan, x - 32, std::max(1L, client.right - 64)));
		}

		if (gApplicationIcon)
		{
			RECT logoGlow{ 32, 22, 116, 106 };
			roundedBox(device, logoGlow, RGB(10, 18, 24), RGB(72, 111, 81), 20, 2);
			RECT logoBox{ 38, 28, 110, 100 };
			roundedBox(device, logoBox, RGB(8, 14, 19), RGB(35, 61, 69), 16);
			DrawIconEx(device, 46, 36, gApplicationIcon, 56, 56, 0, nullptr, DI_NORMAL);
		}

		RECT brand{ 134, 22, client.right - 270, 44 };
		text(device, gBrandFont, kGreen, L"LZ GAMES  /  TURBORAMA", brand, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
		RECT titleArea{ 132, 45, client.right - 250, 86 };
		text(device, gTitleFont, kText, L"Reconhecer esta m\u00E1quina no PIX", titleArea, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
		RECT subtitle{ 134, 88, client.right - 40, 118 };
		text(device, gFont, kMuted, L"Use a licen\u00E7a permanente e o c\u00F3digo \u00FAnico criado no painel TurboRama.", subtitle,
			DT_LEFT | DT_SINGLELINE | DT_VCENTER);

		RECT modeBadge{ client.right - 286, 26, client.right - 34, 58 };
		roundedBox(device, modeBadge, RGB(14, 34, 36), RGB(39, 103, 100), 16);
		RECT modeDot{ modeBadge.left + 14, modeBadge.top + 11, modeBadge.left + 24, modeBadge.top + 21 };
		HBRUSH modeBrush = CreateSolidBrush(kCyan);
		HGDIOBJ previousBrush = SelectObject(device, modeBrush);
		HGDIOBJ previousPen = SelectObject(device, GetStockObject(NULL_PEN));
		Ellipse(device, modeDot.left, modeDot.top, modeDot.right, modeDot.bottom);
		SelectObject(device, previousPen);
		SelectObject(device, previousBrush);
		DeleteObject(modeBrush);
		RECT modeText{ modeBadge.left + 32, modeBadge.top, modeBadge.right - 10, modeBadge.bottom };
		text(device, gSmallFont, RGB(147, 236, 230), L"ATIVA\u00C7\u00C3O ONLINE", modeText,
			DT_LEFT | DT_SINGLELINE | DT_VCENTER);

		RECT tokenCard{ 32, 160, client.right - 32, 458 };
		roundedBox(device, tokenCard, kCard, kBorder, 20);
		RECT tokenAccent{ 32, 180, 37, 438 };
		verticalGradient(device, tokenAccent, kGreen, kCyan);

		RECT tokenHint{ 52, 422, client.right - 52, 448 };
		text(device, gSmallFont, kMuted, L"Servidor fixo: https://pix.lzgames.com.br/  \u2022  Os pre\u00E7os atuais ser\u00E3o preservados.", tokenHint,
			DT_LEFT | DT_SINGLELINE | DT_VCENTER);
		RECT licenseFrame{ 52, 210, client.right - 274, 254 };
		roundedBox(device, licenseFrame, kEdit, RGB(62, 84, 99), 12, 2);
		RECT profileFrame{ 52, 294, client.right - 52, 338 };
		roundedBox(device, profileFrame, kEdit, RGB(62, 84, 99), 12, 2);
		RECT codeFrame{ 52, 378, client.right - 410, 422 };
		roundedBox(device, codeFrame, kEdit, RGB(62, 84, 99), 12, 2);

		RECT securityCard{ 32, 476, client.right - 32, 638 };
		roundedBox(device, securityCard, RGB(12, 21, 29), kBorder, 20);
		RECT shieldCircle{ 52, 494, 90, 532 };
		roundedBox(device, shieldCircle, RGB(20, 53, 40), RGB(77, 137, 87), 38, 2);
		RECT shieldText{ 52, 494, 90, 532 };
		text(device, gButtonFont, kGreen, L"\u2713", shieldText, DT_CENTER | DT_SINGLELINE | DT_VCENTER);

		RECT statusLabel{ 56, 570, client.right - 56, 588 };
		text(device, gSmallFont, RGB(111, 137, 153), L"STATUS DO RECONHECIMENTO", statusLabel,
			DT_LEFT | DT_SINGLELINE | DT_VCENTER);
		RECT statusStrip{ 52, 592, client.right - 52, 628 };
		roundedBox(device, statusStrip, RGB(10, 17, 23), RGB(35, 49, 61), 12);
		HBRUSH statusBrush = CreateSolidBrush(gStatusColor);
		HGDIOBJ oldBrush = SelectObject(device, statusBrush);
		HGDIOBJ oldPen = SelectObject(device, GetStockObject(NULL_PEN));
		Ellipse(device, 66, 605, 76, 615);
		SelectObject(device, oldPen);
		SelectObject(device, oldBrush);
		DeleteObject(statusBrush);

		RECT footerLine{ 32, 724, client.right - 32, 725 };
		fill(device, footerLine, RGB(26, 39, 49));
		RECT footer{ 32, 730, client.right - 32, 754 };
		text(device, gSmallFont, RGB(103, 121, 134),
			L"C\u00D3DIGO \u00DANICO N\u00C3O GRAVADO  \u2022  EMULATIONSTATION N\u00C3O ALTERADO", footer,
			DT_CENTER | DT_SINGLELINE | DT_VCENTER);
	}

	void drawButton(const DRAWITEMSTRUCT* item)
	{
		const int identifier = static_cast<int>(item->CtlID);
		const bool pressed = (item->itemState & ODS_SELECTED) != 0;
		const bool disabled = (item->itemState & ODS_DISABLED) != 0;
		const bool checked = identifier == ID_SHOW ? gTokenVisible :
			SendMessageW(item->hwndItem, BM_GETCHECK, 0, 0) == BST_CHECKED;
		RECT area = item->rcItem;
		InflateRect(&area, -1, -1);

		COLORREF background = kCardRaised;
		COLORREF border = kBorder;
		COLORREF foreground = kText;
		if (identifier == ID_SAVE)
		{
			background = pressed ? kGreenPressed : kGreen;
			border = pressed ? kGreenPressed : RGB(177, 240, 93);
			foreground = RGB(7, 16, 12);
		}
		else if (identifier == ID_CLOSE)
		{
			background = pressed ? RGB(35, 45, 54) : RGB(18, 27, 36);
			border = RGB(62, 77, 89);
		}
		else if (identifier == ID_SHOW && checked)
		{
			background = RGB(17, 48, 52);
			border = kCyan;
			foreground = RGB(115, 238, 232);
		}
		else if (pressed)
		{
			background = RGB(29, 43, 54);
			border = RGB(91, 111, 124);
		}
		if (disabled)
		{
			background = RGB(26, 31, 36);
			foreground = RGB(91, 101, 108);
		}

		roundedBox(item->hDC, area, background, border, 12, 1);
		wchar_t caption[256]{};
		GetWindowTextW(item->hwndItem, caption, 256);
		RECT captionArea = area;
		if (pressed) OffsetRect(&captionArea, 0, 1);
		text(item->hDC, gButtonFont, foreground, caption, captionArea, DT_CENTER | DT_SINGLELINE | DT_VCENTER);

		if ((item->itemState & ODS_FOCUS) != 0)
		{
			RECT focus = area;
			InflateRect(&focus, -5, -5);
			DrawFocusRect(item->hDC, &focus);
		}
	}

	void setStatus(const std::wstring& value, COLORREF color = 0)
	{
		if (color == 0)
		{
			std::wstring lower = value;
			std::transform(lower.begin(), lower.end(), lower.begin(), ::towlower);
			if (lower.find(L"n\u00E3o ") != std::wstring::npos || lower.find(L"nao ") != std::wstring::npos
				|| lower.find(L"inv\u00E1l") != std::wstring::npos
				|| lower.find(L"inval") != std::wstring::npos || lower.find(L"recus") != std::wstring::npos
				|| lower.find(L"erro") != std::wstring::npos)
				gStatusColor = kDanger;
			else if (lower.find(L"confirm") != std::wstring::npos || lower.find(L"pronto") != std::wstring::npos)
				gStatusColor = kGreen;
			else
				gStatusColor = kCyan;
		}
		else gStatusColor = color;
		SetWindowTextW(gStatus, value.c_str());
		if (gStatus) InvalidateRect(GetParent(gStatus), nullptr, FALSE);
	}

	std::wstring editText(HWND control)
	{
		const int length = GetWindowTextLengthW(control);
		std::wstring value(length + 1, L'\0');
		GetWindowTextW(control, value.data(), length + 1);
		value.resize(length);
		return trim(value);
	}

	void pasteClipboard(HWND owner, HWND destination, const wchar_t* confirmation)
	{
		if (!OpenClipboard(owner)) { setStatus(L"N\u00E3o foi poss\u00EDvel abrir a \u00E1rea de transfer\u00EAncia."); return; }
		HANDLE data = GetClipboardData(CF_UNICODETEXT);
		if (data)
		{
			const wchar_t* text = static_cast<const wchar_t*>(GlobalLock(data));
			if (text) { SetWindowTextW(destination, trim(text).c_str()); GlobalUnlock(data); setStatus(confirmation); }
		}
		else setStatus(L"A \u00E1rea de transfer\u00EAncia n\u00E3o cont\u00E9m texto.");
		CloseClipboard();
	}

	std::wstring decodeTextFile(const std::vector<BYTE>& bytes)
	{
		if (bytes.size() >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			return std::wstring((wchar_t*)(bytes.data() + 2), (bytes.size() - 2) / sizeof(wchar_t));
		size_t offset = bytes.size() >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
		const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, (const char*)bytes.data() + offset, (int)(bytes.size() - offset), nullptr, 0);
		if (length <= 0) return {};
		std::wstring result(length, L'\0');
		MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, (const char*)bytes.data() + offset, (int)(bytes.size() - offset), result.data(), length);
		return result;
	}

	void importFile(HWND owner)
	{
		wchar_t fileName[32768]{};
		OPENFILENAMEW dialog{}; dialog.lStructSize = sizeof(dialog); dialog.hwndOwner = owner;
		dialog.lpstrFilter = L"Arquivo de texto (*.txt)\0*.txt\0Todos os arquivos\0*.*\0";
		dialog.lpstrFile = fileName; dialog.nMaxFile = 32768; dialog.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST;
		if (!GetOpenFileNameW(&dialog)) return;
		HANDLE file = CreateFileW(fileName, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
		if (file == INVALID_HANDLE_VALUE) { setStatus(L"N\u00E3o foi poss\u00EDvel abrir o arquivo selecionado."); return; }
		LARGE_INTEGER size{}; bool valid = GetFileSizeEx(file, &size) && size.QuadPart > 0 && size.QuadPart <= 4096;
		std::vector<BYTE> bytes(valid ? (size_t)size.QuadPart : 0); DWORD read = 0;
		valid = valid && ReadFile(file, bytes.data(), (DWORD)bytes.size(), &read, nullptr) && read == bytes.size(); CloseHandle(file);
		if (!valid) { setStatus(L"O arquivo deve conter somente o Access Token e ter no m\u00E1ximo 4 KB."); return; }
		const std::wstring token = trim(decodeTextFile(bytes));
		if (token.empty()) { setStatus(L"O arquivo n\u00E3o cont\u00E9m um texto UTF-8 ou UTF-16 v\u00E1lido."); return; }
		SetWindowTextW(gToken, token.c_str()); setStatus(L"Token importado. Confira e clique em CONECTAR CONTA AO PIX.");
	}

	std::wstring selectedProfile()
	{
		return SendMessageW(gProfile, CB_GETCURSEL, 0, 0) == 1
			? L"TPM_BOUND" : L"SOFTWARE_BOUND_ONLINE";
	}

	void loadExistingOnlineRegistration()
	{
		std::string settings;
		if (!readAll(join(bridgeDirectory(), kOwnerSettingsFile), settings)) return;
		const std::string license = extractJsonString(settings, "onlineLicenseId");
		const std::string profile = extractJsonString(settings, "onlineProtectionProfile");
		if (!license.empty()) SetWindowTextW(gLicense, wideUtf8(license).c_str());
		SendMessageW(gProfile, CB_SETCURSEL, profile == "TPM_BOUND" ? 1 : 0, 0);
		if (!license.empty() && license != "CONFIGURE-A-LICENCA")
			setStatus(L"Cadastro on-line encontrado. O EmulationStation j\u00E1 pode ler esta identifica\u00E7\u00E3o.", kGreen);
	}

	LRESULT CALLBACK windowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
	{
		switch (message)
		{
		case WM_CREATE:
		{
			RECT client{};
			GetClientRect(window, &client);
			gFont = makeFont(-18, FW_NORMAL);
			gSmallFont = makeFont(-14, FW_NORMAL);
			gLabelFont = makeFont(-14, FW_BOLD);
			gButtonFont = makeFont(-16, FW_BOLD);
			gSecurityTitleFont = makeFont(-17, FW_BOLD);
			gTitleFont = makeFont(-31, FW_BOLD);
			gBrandFont = makeFont(-14, FW_BOLD);
			gEditBrush = CreateSolidBrush(kEdit);
			auto control = [window](const wchar_t* kind, const wchar_t* value, DWORD style, int x, int y, int width, int height, int id) {
				HWND handle = CreateWindowExW(0, kind, value, WS_CHILD | WS_VISIBLE | style,
					x, y, width, height, window, (HMENU)(INT_PTR)id, GetModuleHandleW(nullptr), nullptr);
				SendMessageW(handle, WM_SETFONT, (WPARAM)gFont, TRUE); return handle;
			};
			HWND licenseLabel = control(L"STATIC", L"LICEN\u00C7A PERMANENTE (EX.: TR-...)", SS_LEFT | SS_NOPREFIX,
				52, 178, client.right - 104, 22, ID_LICENSE_LABEL);
			SendMessageW(licenseLabel, WM_SETFONT, (WPARAM)gLabelFont, TRUE);
			gLicense = control(L"EDIT", L"", WS_TABSTOP | ES_AUTOHSCROLL,
				64, 216, client.right - 350, 32, ID_LICENSE);
			SendMessageW(gLicense, EM_SETMARGINS, EC_LEFTMARGIN | EC_RIGHTMARGIN, MAKELPARAM(10, 10));
			HWND profileLabel = control(L"STATIC", L"PROTE\u00C7\u00C3O DA M\u00C1QUINA", SS_LEFT | SS_NOPREFIX,
				52, 262, client.right - 104, 22, ID_PROFILE_LABEL);
			SendMessageW(profileLabel, WM_SETFONT, (WPARAM)gLabelFont, TRUE);
			gProfile = control(L"COMBOBOX", L"", WS_TABSTOP | CBS_DROPDOWNLIST | WS_VSCROLL,
				64, 300, client.right - 128, 150, ID_PROFILE);
			SendMessageW(gProfile, CB_ADDSTRING, 0, (LPARAM)L"SEM TPM - PROTE\u00C7\u00C3O ONLINE");
			SendMessageW(gProfile, CB_ADDSTRING, 0, (LPARAM)L"TPM DESTA PLACA-M\u00C3E");
			SendMessageW(gProfile, CB_SETCURSEL, 0, 0);

			HWND codeLabel = control(L"STATIC", L"C\u00D3DIGO \u00DANICO DE ATIVA\u00C7\u00C3O", SS_LEFT | SS_NOPREFIX,
				52, 346, client.right - 104, 22, ID_TOKEN_LABEL);
			SendMessageW(codeLabel, WM_SETFONT, (WPARAM)gLabelFont, TRUE);
			gToken = control(L"EDIT", L"", WS_TABSTOP | ES_AUTOHSCROLL | ES_PASSWORD,
				64, 384, client.right - 486, 32, ID_TOKEN);
			SendMessageW(gToken, EM_SETPASSWORDCHAR, (WPARAM)L'*', 0);
			SendMessageW(gToken, EM_SETMARGINS, EC_LEFTMARGIN | EC_RIGHTMARGIN, MAKELPARAM(10, 10));
			HWND pasteCode = control(L"BUTTON", L"COLAR C\u00D3DIGO", WS_TABSTOP | BS_OWNERDRAW,
				client.right - 396, 378, 174, 44, ID_PASTE);
			HWND showCode = control(L"BUTTON", L"EXIBIR", WS_TABSTOP | BS_OWNERDRAW,
				client.right - 208, 378, 156, 44, ID_SHOW);
			HWND pasteLicenseVisible = control(L"BUTTON", L"COLAR LICEN\u00C7A", WS_TABSTOP | BS_OWNERDRAW,
				client.right - 258, 210, 206, 44, ID_IMPORT);
			for (HWND button : { pasteCode, showCode, pasteLicenseVisible })
				SendMessageW(button, WM_SETFONT, (WPARAM)gButtonFont, TRUE);

			HWND securityTitle = control(L"STATIC", L"RECONHECIMENTO SEGURO DA INSTALA\u00C7\u00C3O", SS_LEFT | SS_NOPREFIX,
				104, 490, client.right - 156, 28, ID_SECURITY_TITLE);
			SendMessageW(securityTitle, WM_SETFONT, (WPARAM)gSecurityTitleFont, TRUE);
			control(L"STATIC",
				L"A chave privada permanece nesta m\u00E1quina. O c\u00F3digo \u00FAnico \u00E9 usado uma vez e apagado da mem\u00F3ria.",
				SS_LEFT | SS_NOPREFIX, 104, 522, client.right - 156, 42, ID_SECURITY_TEXT);
			gStatus = control(L"STATIC", L"Informe a licen\u00E7a permanente e o c\u00F3digo \u00FAnico criado no painel.",
				SS_LEFT | SS_NOPREFIX | SS_CENTERIMAGE | SS_ENDELLIPSIS, 82, 596, client.right - 164, 28, ID_STATUS);
			gSave = control(L"BUTTON", L"RECONHECER E ATIVAR ESTA M\u00C1QUINA", WS_TABSTOP | BS_OWNERDRAW,
				32, 654, client.right - 350, 56, ID_SAVE);
			HWND close = control(L"BUTTON", L"FECHAR", WS_TABSTOP | BS_OWNERDRAW,
				client.right - 302, 654, 270, 56, ID_CLOSE);
			SendMessageW(gSave, WM_SETFONT, (WPARAM)gButtonFont, TRUE);
			SendMessageW(close, WM_SETFONT, (WPARAM)gButtonFont, TRUE);
			loadExistingOnlineRegistration();
			SetFocus(gLicense);
			return 0;
		}
		case WM_PAINT:
		{
			PAINTSTRUCT paint{};
			HDC device = BeginPaint(window, &paint);
			paintInterface(window, device);
			EndPaint(window, &paint);
			return 0;
		}
		case WM_ERASEBKGND:
			return 1;
		case WM_DRAWITEM:
			drawButton(reinterpret_cast<const DRAWITEMSTRUCT*>(lParam));
			return TRUE;
		case WM_COMMAND:
			switch (LOWORD(wParam))
			{
			case ID_PASTE:
				pasteClipboard(window, gToken, L"C\u00F3digo \u00FAnico colado. Ele ser\u00E1 apagado depois da tentativa.");
				return 0;
			case ID_IMPORT:
				pasteClipboard(window, gLicense, L"Licen\u00E7a permanente colada. Confira antes de ativar.");
				return 0;
			case ID_SHOW:
			{
				gTokenVisible = !gTokenVisible;
				SendMessageW(gToken, EM_SETPASSWORDCHAR, gTokenVisible ? 0 : (WPARAM)L'*', 0);
				InvalidateRect(reinterpret_cast<HWND>(lParam), nullptr, TRUE);
				InvalidateRect(gToken, nullptr, TRUE);
				return 0;
			}
			case ID_SAVE:
			{
				std::wstring license = editText(gLicense);
				std::wstring codeWide = editText(gToken);
				if (!validOnlineIdentifier(license))
				{
					const std::wstring error = L"Informe a licen\u00E7a permanente exibida no painel (ex.: TR-...).";
					setStatus(error); MessageBoxW(window, error.c_str(), kTitle, MB_OK | MB_ICONWARNING);
					SetFocus(gLicense); return 0;
				}
				if (!validActivationCode(codeWide))
				{
					const std::wstring error = L"Informe o c\u00F3digo \u00FAnico de ativa\u00E7\u00E3o criado para esta licen\u00E7a.";
					setStatus(error); MessageBoxW(window, error.c_str(), kTitle, MB_OK | MB_ICONWARNING);
					SetFocus(gToken); return 0;
				}

				const std::wstring profile = selectedProfile();
				std::string activationCode = utf8(codeWide);
				if (!codeWide.empty()) SecureZeroMemory(codeWide.data(), codeWide.size() * sizeof(wchar_t));
				codeWide.clear();
				SetWindowTextW(gToken, L"");
				EnableWindow(gSave, FALSE);
				SetCursor(LoadCursorW(nullptr, IDC_WAIT));
				setStatus(L"Conferindo a licen\u00E7a e registrando a identidade desta m\u00E1quina...", kGold);
				std::wstring error;
				bool indeterminate = false;
				const bool accepted = activateOnlineMachine(license, profile, activationCode, error, indeterminate);
				if (!activationCode.empty())
				{
					SecureZeroMemory(activationCode.data(), activationCode.size());
					activationCode.clear();
				}
				EnableWindow(gSave, TRUE);
				SetCursor(LoadCursorW(nullptr, IDC_ARROW));
				if (!accepted)
				{
					setStatus(error, indeterminate ? kGold : kDanger);
					MessageBoxW(window, error.c_str(), kTitle,
						MB_OK | (indeterminate ? MB_ICONWARNING : MB_ICONERROR));
					return 0;
				}
				setStatus(L"M\u00E1quina reconhecida. O EmulationStation j\u00E1 pode usar este cadastro.", kGreen);
				MessageBoxW(window,
					L"Ativa\u00E7\u00E3o confirmada.\n\nAgora abra o EmulationStation normalmente. Ele ler\u00E1 o cadastro existente sem pedir novamente a licen\u00E7a ou o c\u00F3digo \u00FAnico.",
					kTitle, MB_OK | MB_ICONINFORMATION);
				return 0;
			}
			case ID_CLOSE: DestroyWindow(window); return 0;
			}
			break;
		case WM_CTLCOLORSTATIC:
		{
			HDC device = reinterpret_cast<HDC>(wParam);
			HWND control = reinterpret_cast<HWND>(lParam);
			SetBkMode(device, TRANSPARENT);
			switch (GetDlgCtrlID(control))
			{
			case ID_TOKEN_LABEL:
			case ID_LICENSE_LABEL:
			case ID_PROFILE_LABEL: SetTextColor(device, kGreen); break;
			case ID_SECURITY_TITLE: SetTextColor(device, kText); break;
			case ID_SECURITY_TEXT: SetTextColor(device, RGB(192, 204, 213)); break;
			case ID_STATUS: SetTextColor(device, gStatusColor); break;
			default: SetTextColor(device, kText); break;
			}
			return reinterpret_cast<LRESULT>(GetStockObject(NULL_BRUSH));
		}
		case WM_CTLCOLOREDIT:
		{
			HDC device = reinterpret_cast<HDC>(wParam);
			SetTextColor(device, kText);
			SetBkColor(device, kEdit);
			return reinterpret_cast<LRESULT>(gEditBrush);
		}
		case WM_CTLCOLORLISTBOX:
		{
			HDC device = reinterpret_cast<HDC>(wParam);
			SetTextColor(device, kText);
			SetBkColor(device, kEdit);
			return reinterpret_cast<LRESULT>(gEditBrush);
		}
		case WM_CTLCOLORBTN:
			SetBkMode(reinterpret_cast<HDC>(wParam), TRANSPARENT);
			return reinterpret_cast<LRESULT>(GetStockObject(NULL_BRUSH));
		case WM_DESTROY:
			if (gFont) DeleteObject(gFont);
			if (gSmallFont) DeleteObject(gSmallFont);
			if (gLabelFont) DeleteObject(gLabelFont);
			if (gButtonFont) DeleteObject(gButtonFont);
			if (gSecurityTitleFont) DeleteObject(gSecurityTitleFont);
			if (gTitleFont) DeleteObject(gTitleFont);
			if (gBrandFont) DeleteObject(gBrandFont);
			if (gEditBrush) DeleteObject(gEditBrush);
			if (gApplicationIcon) { DestroyIcon(gApplicationIcon); gApplicationIcon = nullptr; }
			PostQuitMessage(0);
			return 0;
		}
		return DefWindowProcW(window, message, wParam, lParam);
	}

	bool credentialProtocolSelfTest()
	{
		const std::wstring license = L"TR-SELFTEST-001";
		std::wstring code = L"ACTIVATION-CODE-SELFTEST-001";
		const std::string configuration = onlineConfigurationJson(
			license, L"SOFTWARE_BOUND_ONLINE");
		const bool valid = validOnlineIdentifier(license) && validActivationCode(code)
			&& !validOnlineIdentifier(L"TR COM ESPACO")
			&& !validActivationCode(L"curto")
			&& configuration.find("\"schemaVersion\":1") != std::string::npos
			&& configuration.find("\"baseUrl\":\"https://pix.lzgames.com.br/\"") != std::string::npos
			&& configuration.find("\"licenseId\":\"TR-SELFTEST-001\"") != std::string::npos
			&& configuration.find("\"protectionProfile\":\"SOFTWARE_BOUND_ONLINE\"") != std::string::npos
			&& configuration.find("packagePricesCents") == std::string::npos
			&& configuration.find(utf8(code)) == std::string::npos;
		SecureZeroMemory(code.data(), code.size() * sizeof(wchar_t));
		return valid;
	}
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int show)
{
	SetProcessDPIAware();
	int argumentCount = 0;
	wchar_t** arguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
	if (arguments && argumentCount >= 2 && std::wstring(arguments[1]) == L"--self-test")
	{
		const bool success = credentialProtocolSelfTest(); LocalFree(arguments);
		return success ? 0 : 20;
	}
	if (arguments && argumentCount > 1)
	{
		LocalFree(arguments);
		return 21;
	}
	if (arguments) LocalFree(arguments);

	KioskAccount kioskAccount;
	std::wstring identityError;
	if (!resolveAutomaticKioskAccount(kioskAccount, identityError))
	{
		MessageBoxW(nullptr, identityError.c_str(), kTitle, MB_OK | MB_ICONERROR);
		return 19;
	}
	const bool correctIdentity = currentProcessMatchesKiosk(kioskAccount, identityError);
	if (!correctIdentity)
	{
		std::wstring message = L"Este configurador precisa ser aberto diretamente na conta Windows configurada no TurboRama: "
			+ kioskAccount.domain + L"\\" + kioskAccount.user + L".\n\n"
			L"Regra atual deste gabinete: use somente Admin nesta ativacao.";
		if (!identityError.empty()) message += L"\n\nDetalhe: " + identityError;
		MessageBoxW(nullptr, message.c_str(), kTitle, MB_OK | MB_ICONERROR);
		return 19;
	}
	clearKioskSessionOverrides();
	gApplicationIcon = reinterpret_cast<HICON>(LoadImageW(instance, MAKEINTRESOURCEW(1), IMAGE_ICON,
		64, 64, LR_DEFAULTCOLOR));
	WNDCLASSEXW type{}; type.cbSize = sizeof(type); type.hInstance = instance; type.lpfnWndProc = windowProcedure;
	type.lpszClassName = kClassName; type.hCursor = LoadCursorW(nullptr, IDC_ARROW); type.hIcon = gApplicationIcon;
	type.hIconSm = gApplicationIcon; type.hbrBackground = nullptr;
	if (!RegisterClassExW(&type)) return 1;
	HWND window = CreateWindowExW(0, kClassName, kTitle, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
		CW_USEDEFAULT, CW_USEDEFAULT, 1000, 810, nullptr, nullptr, instance, nullptr);
	if (!window) return 2;
	RECT area{};
	if (GetWindowRect(window, &area))
	{
		const int width = area.right - area.left;
		const int height = area.bottom - area.top;
		SetWindowPos(window, nullptr, (GetSystemMetrics(SM_CXSCREEN) - width) / 2,
			(GetSystemMetrics(SM_CYSCREEN) - height) / 2, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
	}
	ShowWindow(window, show); UpdateWindow(window);
	MSG message{};
	while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
	return (int)message.wParam;
}
