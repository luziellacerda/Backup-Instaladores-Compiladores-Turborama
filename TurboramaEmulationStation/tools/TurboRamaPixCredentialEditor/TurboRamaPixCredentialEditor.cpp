#define UNICODE
#define _UNICODE
#define NOMINMAX
#include <windows.h>
#include <wincrypt.h>
#include <bcrypt.h>
#include <commdlg.h>
#include <shellapi.h>
#include <tlhelp32.h>

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <ctime>
#include <cstring>
#include <string>
#include <vector>

#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "comdlg32.lib")
#pragma comment(lib, "shell32.lib")

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
	constexpr int ID_SECURITY_TEXT = 1010;
	constexpr int ID_DESTINATION = 1011;
	const wchar_t* kClassName = L"TurboRamaPixCredentialEditor";
	const wchar_t* kTitle = L"LZ Games | Central segura de pagamento PIX";
	const wchar_t* kPublicKeyFile = L"agent-public-key.pem";
	const wchar_t* kCredentialUpdateFile = L"credential-update.json";
	const wchar_t* kCredentialUpdateStatusFile = L"credential-update-status.json";
	HWND gToken = nullptr;
	HWND gStatus = nullptr;
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
		const DWORD length = GetEnvironmentVariableW(L"TURBORAMA_PIX_BRIDGE", overridePath, 32768);
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
		const std::string json = "{\"schemaVersion\":3,\"requestId\":\"" + requestId
			+ "\",\"keyFingerprint\":\"" + fingerprint + "\",\"encryptedPayload\":\"" + encryptedPayload
			+ "\",\"createdAtUnixSeconds\":" + std::to_string((long long)time(nullptr)) + "}";
		DeleteFileW(temporary.c_str());
		if (!writeAll(temporary, json)) { error = L"N\u00E3o foi poss\u00EDvel gravar a atualiza\u00E7\u00E3o segura do token."; return false; }
		if (!MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
		{
			DeleteFileW(temporary.c_str()); error = L"N\u00E3o foi poss\u00EDvel entregar o token cifrado ao servi\u00E7o PIX."; return false;
		}
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

	void stopExact(const std::wstring& expectedPath)
	{
		const std::wstring expected = normalized(expectedPath);
		HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
		if (snapshot == INVALID_HANDLE_VALUE) return;
		PROCESSENTRY32W entry{}; entry.dwSize = sizeof(entry);
		if (Process32FirstW(snapshot, &entry))
		{
			do
			{
				HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE | SYNCHRONIZE, FALSE, entry.th32ProcessID);
				if (!process) continue;
				wchar_t image[32768]{}; DWORD length = 32768;
				if (QueryFullProcessImageNameW(process, 0, image, &length) && normalized(image) == expected)
				{
					TerminateProcess(process, 0);
					WaitForSingleObject(process, 5000);
				}
				CloseHandle(process);
			} while (Process32NextW(snapshot, &entry));
		}
		CloseHandle(snapshot);
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
			executable = dotnet;
			command = L"\"" + dotnet + L"\" \"" + assembly + L"\" " + mode + L" --bridge \"" + bridge + L"\"";
		}
		else if (fileExists(appHost))
		{
			executable = appHost;
			command = L"\"" + appHost + L"\" " + mode + L" --bridge \"" + bridge + L"\"";
		}
		else { error = L"O agente PIX n\u00E3o foi instalado. Execute primeiro o instalador comercial v16."; return false; }
		return true;
	}

	bool ensureAgentPublicKey(const std::wstring& bridge, std::wstring& error)
	{
		if (fileExists(join(bridge, kPublicKeyFile))) return true;
		std::wstring root, executable, command;
		if (!resolveAgentCommand(bridge, L"--prepare-credential-editor", root, executable, command, error)) return false;
		std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
		STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_HIDE;
		PROCESS_INFORMATION process{};
		if (CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &process))
		{
			CloseHandle(process.hThread);
			const DWORD wait = WaitForSingleObject(process.hProcess, 10000);
			DWORD exitCode = 999;
			GetExitCodeProcess(process.hProcess, &exitCode);
			CloseHandle(process.hProcess);
			if (wait != WAIT_OBJECT_0 || (exitCode != 0 && exitCode != 12))
			{
				error = L"O servi\u00E7o PIX n\u00E3o conseguiu preparar a chave segura. Execute novamente o instalador comercial v16.";
				return false;
			}
		}
		else { error = L"N\u00E3o foi poss\u00EDvel iniciar o agente PIX para preparar a chave segura."; return false; }
		for (int attempt = 0; attempt < 160; ++attempt)
		{
			if (fileExists(join(bridge, kPublicKeyFile))) return true;
			Sleep(200);
		}
		error = L"O agente PIX em execu\u00E7\u00E3o ainda n\u00E3o possui a atualiza\u00E7\u00E3o de credencial segura. Instale a atualiza\u00E7\u00E3o PIX e abra o EmulationStation uma vez.";
		return false;
	}

	bool triggerCredentialAcceptance(const std::wstring& bridge, std::wstring& error)
	{
		std::wstring root, executable, command;
		if (!resolveAgentCommand(bridge, L"--accept-credential-once", root, executable, command, error)) return false;
		std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
		STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_HIDE;
		PROCESS_INFORMATION process{};
		if (!CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
			nullptr, root.c_str(), &startup, &process))
		{
			error = L"N\u00E3o foi poss\u00EDvel iniciar o agente PIX para receber o Access Token.";
			return false;
		}
		CloseHandle(process.hThread);
		CloseHandle(process.hProcess);
		return true;
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
		text(device, gTitleFont, kText, L"Central segura de pagamento PIX", titleArea, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
		RECT subtitle{ 134, 88, client.right - 40, 118 };
		text(device, gFont, kMuted, L"Conecte sua conta Mercado Pago ao sistema de cr\u00E9ditos da m\u00E1quina.", subtitle,
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
		text(device, gSmallFont, RGB(147, 236, 230), L"CREDENCIAL DE PRODU\u00C7\u00C3O", modeText,
			DT_LEFT | DT_SINGLELINE | DT_VCENTER);

		RECT tokenCard{ 32, 160, client.right - 32, 356 };
		roundedBox(device, tokenCard, kCard, kBorder, 20);
		RECT tokenAccent{ 32, 180, 37, 336 };
		verticalGradient(device, tokenAccent, kGreen, kCyan);
		RECT stepBadge{ client.right - 176, 176, client.right - 52, 204 };
		roundedBox(device, stepBadge, RGB(27, 34, 37), RGB(91, 80, 43), 14);
		text(device, gSmallFont, kGold, L"ETAPA \u00DANICA", stepBadge, DT_CENTER | DT_SINGLELINE | DT_VCENTER);

		RECT tokenHint{ 52, 204, client.right - 200, 226 };
		text(device, gSmallFont, kMuted, L"Cole o Access Token completo. Ele ser\u00E1 protegido antes de sair desta tela.", tokenHint,
			DT_LEFT | DT_SINGLELINE | DT_VCENTER);
		RECT inputFrame{ 52, 228, client.right - 52, 282 };
		roundedBox(device, inputFrame, kEdit, RGB(62, 84, 99), 12, 2);

		RECT securityCard{ 32, 372, client.right - 32, 530 };
		roundedBox(device, securityCard, RGB(12, 21, 29), kBorder, 20);
		RECT shieldCircle{ 52, 390, 90, 428 };
		roundedBox(device, shieldCircle, RGB(20, 53, 40), RGB(77, 137, 87), 38, 2);
		RECT shieldText{ 52, 390, 90, 428 };
		text(device, gButtonFont, kGreen, L"\u2713", shieldText, DT_CENTER | DT_SINGLELINE | DT_VCENTER);

		RECT statusLabel{ 56, 462, client.right - 56, 480 };
		text(device, gSmallFont, RGB(111, 137, 153), L"STATUS DA CONEX\u00C3O", statusLabel,
			DT_LEFT | DT_SINGLELINE | DT_VCENTER);
		RECT statusStrip{ 52, 484, client.right - 52, 518 };
		roundedBox(device, statusStrip, RGB(10, 17, 23), RGB(35, 49, 61), 12);
		HBRUSH statusBrush = CreateSolidBrush(gStatusColor);
		HGDIOBJ oldBrush = SelectObject(device, statusBrush);
		HGDIOBJ oldPen = SelectObject(device, GetStockObject(NULL_PEN));
		Ellipse(device, 66, 496, 76, 506);
		SelectObject(device, oldPen);
		SelectObject(device, oldBrush);
		DeleteObject(statusBrush);

		RECT footerLine{ 32, 618, client.right - 32, 619 };
		fill(device, footerLine, RGB(26, 39, 49));
		RECT footer{ 32, 624, client.right - 32, 648 };
		text(device, gSmallFont, RGB(103, 121, 134),
			L"LZ GAMES  \u2022  CREDENCIAL PROTEGIDA PELO WINDOWS  \u2022  AMBIENTE COMERCIAL", footer,
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

	void pasteClipboard(HWND owner)
	{
		if (!OpenClipboard(owner)) { setStatus(L"N\u00E3o foi poss\u00EDvel abrir a \u00E1rea de transfer\u00EAncia."); return; }
		HANDLE data = GetClipboardData(CF_UNICODETEXT);
		if (data)
		{
			const wchar_t* text = static_cast<const wchar_t*>(GlobalLock(data));
			if (text) { SetWindowTextW(gToken, trim(text).c_str()); GlobalUnlock(data); setStatus(L"Token colado. Confira e clique em CONECTAR CONTA AO PIX."); }
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
			HWND tokenLabel = control(L"STATIC", L"ACCESS TOKEN DO MERCADO PAGO", SS_LEFT | SS_NOPREFIX,
				52, 178, 520, 22, ID_TOKEN_LABEL);
			SendMessageW(tokenLabel, WM_SETFONT, (WPARAM)gLabelFont, TRUE);
			gToken = control(L"EDIT", L"", WS_TABSTOP | ES_AUTOHSCROLL | ES_PASSWORD,
				64, 236, client.right - 128, 38, ID_TOKEN);
			SendMessageW(gToken, EM_SETPASSWORDCHAR, (WPARAM)L'*', 0);
			SendMessageW(gToken, EM_SETMARGINS, EC_LEFTMARGIN | EC_RIGHTMARGIN, MAKELPARAM(14, 14));
			const int buttonWidth = (client.right - 128) / 3;
			HWND paste = control(L"BUTTON", L"COLAR TOKEN", WS_TABSTOP | BS_OWNERDRAW,
				52, 294, buttonWidth, 42, ID_PASTE);
			HWND import = control(L"BUTTON", L"IMPORTAR ARQUIVO", WS_TABSTOP | BS_OWNERDRAW,
				64 + buttonWidth, 294, buttonWidth, 42, ID_IMPORT);
			HWND showToken = control(L"BUTTON", L"EXIBIR TOKEN", WS_TABSTOP | BS_OWNERDRAW,
				76 + buttonWidth * 2, 294, client.right - (128 + buttonWidth * 2), 42, ID_SHOW);
			SendMessageW(paste, WM_SETFONT, (WPARAM)gButtonFont, TRUE);
			SendMessageW(import, WM_SETFONT, (WPARAM)gButtonFont, TRUE);
			SendMessageW(showToken, WM_SETFONT, (WPARAM)gButtonFont, TRUE);

			HWND securityTitle = control(L"STATIC", L"PROTE\u00C7\u00C3O DE CREDENCIAIS ATIVA", SS_LEFT | SS_NOPREFIX,
				104, 388, client.right - 156, 26, ID_SECURITY_TITLE);
			SendMessageW(securityTitle, WM_SETFONT, (WPARAM)gSecurityTitleFont, TRUE);
			control(L"STATIC",
				L"Sem senha adicional: o token \u00E9 cifrado para esta m\u00E1quina e nunca \u00E9 salvo como texto comum.",
				SS_LEFT | SS_NOPREFIX, 104, 418, client.right - 156, 24, ID_SECURITY_TEXT);
			control(L"STATIC",
				L"Destino seguro  \u2022  Servi\u00E7o PIX em D:\\emulationstation  \u2022  Confirma\u00E7\u00E3o autom\u00E1tica",
				SS_LEFT | SS_NOPREFIX, 104, 440, client.right - 156, 22, ID_DESTINATION);
			gStatus = control(L"STATIC", fileExists(join(bridgeDirectory(), kPublicKeyFile)) ?
				L"Servi\u00E7o PIX pronto. Cole ou importe o Access Token de produ\u00E7\u00E3o." :
				L"Aguardando o servi\u00E7o PIX preparar a conex\u00E3o segura.",
				SS_LEFT | SS_NOPREFIX | SS_CENTERIMAGE, 82, 488, client.right - 164, 26, ID_STATUS);
			HWND save = control(L"BUTTON", L"CONECTAR CONTA AO SERVI\u00C7O PIX", WS_TABSTOP | BS_OWNERDRAW,
				32, 548, 600, 56, ID_SAVE);
			HWND close = control(L"BUTTON", L"FECHAR", WS_TABSTOP | BS_OWNERDRAW,
				648, 548, client.right - 680, 56, ID_CLOSE);
			SendMessageW(save, WM_SETFONT, (WPARAM)gButtonFont, TRUE);
			SendMessageW(close, WM_SETFONT, (WPARAM)gButtonFont, TRUE);
			SetFocus(gToken);
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
			case ID_PASTE: pasteClipboard(window); return 0;
			case ID_IMPORT: importFile(window); return 0;
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
				std::wstring value = editText(gToken); std::string token = utf8(value); std::wstring error;
				const bool accepted = submitTokenToAgent(bridgeDirectory(), token, error);
				SecureZeroMemory(token.data(), token.size());
				SecureZeroMemory(value.data(), value.size() * sizeof(wchar_t));
				if (!accepted) { setStatus(error); MessageBoxW(window, error.c_str(), kTitle, MB_OK | MB_ICONERROR); return 0; }
				SetWindowTextW(gToken, L"");
				setStatus(L"Access Token confirmado pelo agente PIX e protegido pelo Windows.");
				MessageBoxW(window,
					L"Access Token confirmado pelo servi\u00E7o PIX.\n\nAgora o EmulationStation tentar\u00E1 validar a conta, a loja e o caixa automaticamente.",
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
			case ID_TOKEN_LABEL: SetTextColor(device, kGreen); break;
			case ID_SECURITY_TITLE: SetTextColor(device, kText); break;
			case ID_SECURITY_TEXT: SetTextColor(device, RGB(192, 204, 213)); break;
			case ID_DESTINATION: SetTextColor(device, kMuted); break;
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
		std::string token = "APP_USR-" + std::string(80, 'T');
		std::string payload = token;
		HCRYPTPROV provider = 0; HCRYPTKEY privateKey = 0; HCRYPTKEY publicKey = 0;
		auto cleanup = [&]() {
			if (publicKey) CryptDestroyKey(publicKey);
			if (privateKey) CryptDestroyKey(privateKey);
			if (provider) CryptReleaseContext(provider, 0);
			SecureZeroMemory(token.data(), token.size());
			SecureZeroMemory(payload.data(), payload.size());
		};
		std::wstring error;
		if (!validToken(token, error)) { cleanup(); return false; }
		if (!CryptAcquireContextW(&provider, nullptr, MS_ENH_RSA_AES_PROV_W, PROV_RSA_AES, CRYPT_VERIFYCONTEXT)) { cleanup(); return false; }
		if (!CryptGenKey(provider, AT_KEYEXCHANGE, (4096u << 16) | CRYPT_EXPORTABLE, &privateKey)) { cleanup(); return false; }
		DWORD infoSize = 0;
		if (!CryptExportPublicKeyInfo(provider, AT_KEYEXCHANGE, X509_ASN_ENCODING, nullptr, &infoSize) || infoSize == 0) { cleanup(); return false; }
		std::vector<BYTE> der(infoSize);
		auto* information = reinterpret_cast<CERT_PUBLIC_KEY_INFO*>(der.data());
		if (!CryptExportPublicKeyInfo(provider, AT_KEYEXCHANGE, X509_ASN_ENCODING, information, &infoSize)
			|| sha256Fingerprint(der).size() != 64 || !CryptImportPublicKeyInfo(provider, X509_ASN_ENCODING, information, &publicKey))
		{ cleanup(); return false; }
		DWORD keyBits = 0, keyBitsSize = sizeof(keyBits);
		if (!CryptGetKeyParam(publicKey, KP_KEYLEN, reinterpret_cast<BYTE*>(&keyBits), &keyBitsSize, 0)
			|| keyBits != 4096 || payload.size() > keyBits / 8 - 42)
		{ cleanup(); return false; }
		std::vector<BYTE> encrypted(keyBits / 8);
		std::memcpy(encrypted.data(), payload.data(), payload.size());
		DWORD encryptedSize = (DWORD)payload.size();
		const bool encryptedOk = CryptEncrypt(publicKey, 0, TRUE, CRYPT_OAEP, encrypted.data(), &encryptedSize, (DWORD)encrypted.size()) != FALSE;
		if (encryptedOk) std::reverse(encrypted.begin(), encrypted.begin() + encryptedSize); // transporte para .NET: big-endian
		const std::string transport = encryptedOk ? base64(encrypted.data(), encryptedSize) : std::string{};
		std::vector<BYTE> returned;
		const bool decodedOk = !transport.empty() && decodeBase64(transport, returned) && returned.size() == encryptedSize;
		if (decodedOk) std::reverse(returned.begin(), returned.end()); // retorno CAPI: little-endian
		DWORD decryptedSize = encryptedSize;
		const bool decryptedOk = decodedOk && CryptDecrypt(privateKey, 0, TRUE, CRYPT_OAEP, returned.data(), &decryptedSize) != FALSE;
		const bool payloadOk = decryptedOk && decryptedSize == payload.size()
			&& std::memcmp(returned.data(), payload.data(), payload.size()) == 0;
		SecureZeroMemory(encrypted.data(), encrypted.size());
		if (!returned.empty()) SecureZeroMemory(returned.data(), returned.size());
		const std::string schema = "{\"schemaVersion\":3,\"encryptedPayload\":\"teste\"}";
		const bool schemaOk = schema.find("\"schemaVersion\":3") != std::string::npos && schema.find("encryptedPayload") != std::string::npos;
		cleanup();
		return payloadOk && schemaOk;
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
	if (arguments) LocalFree(arguments);
	gApplicationIcon = reinterpret_cast<HICON>(LoadImageW(instance, MAKEINTRESOURCEW(1), IMAGE_ICON,
		64, 64, LR_DEFAULTCOLOR));
	WNDCLASSEXW type{}; type.cbSize = sizeof(type); type.hInstance = instance; type.lpfnWndProc = windowProcedure;
	type.lpszClassName = kClassName; type.hCursor = LoadCursorW(nullptr, IDC_ARROW); type.hIcon = gApplicationIcon;
	type.hIconSm = gApplicationIcon; type.hbrBackground = nullptr;
	if (!RegisterClassExW(&type)) return 1;
	HWND window = CreateWindowExW(0, kClassName, kTitle, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
		CW_USEDEFAULT, CW_USEDEFAULT, 980, 710, nullptr, nullptr, instance, nullptr);
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
