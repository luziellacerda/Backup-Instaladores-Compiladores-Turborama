#define UNICODE
#define _UNICODE
#include <windows.h>
#include <wincrypt.h>
#include <commdlg.h>
#include <shellapi.h>
#include <tlhelp32.h>

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <string>
#include <vector>

#pragma comment(lib, "crypt32.lib")
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
	const wchar_t* kClassName = L"TurboRamaPixCredentialEditor";
	const wchar_t* kTitle = L"TurboRama - Configurar Access Token PIX";
	HWND gToken = nullptr;
	HWND gStatus = nullptr;
	HFONT gFont = nullptr;

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
		if (token.size() < 40 || token.size() > 512 || token.rfind("APP_USR-", 0) != 0)
		{
			error = L"Access Token invalido. Cole o valor completo iniciado por APP_USR-.";
			return false;
		}
		for (const unsigned char character : token)
		{
			if (std::isspace(character) != 0 || character < 33 || character > 126)
			{
				error = L"O Access Token contem espacos ou caracteres invalidos.";
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

	bool decryptMatches(const std::wstring& path, const std::string& expected)
	{
		std::string encoded;
		std::vector<BYTE> encrypted;
		if (!readAll(path, encoded) || !decodeBase64(encoded, encrypted)) return false;
		const std::string entropyText = "TurboRamaPixAgent-v1";
		DATA_BLOB input{ (DWORD)encrypted.size(), encrypted.data() };
		DATA_BLOB entropy{ (DWORD)entropyText.size(), (BYTE*)entropyText.data() };
		DATA_BLOB output{};
		if (!CryptUnprotectData(&input, nullptr, &entropy, nullptr, nullptr, CRYPTPROTECT_UI_FORBIDDEN, &output)) return false;
		const bool matches = output.cbData == expected.size() && memcmp(output.pbData, expected.data(), expected.size()) == 0;
		SecureZeroMemory(output.pbData, output.cbData);
		LocalFree(output.pbData);
		return matches;
	}

	bool saveToken(const std::wstring& bridge, const std::string& token, std::wstring& error)
	{
		if (!validToken(token, error)) return false;
		if (!ensureDirectory(bridge)) { error = L"Nao foi possivel criar ou acessar a pasta PIX."; return false; }
		const std::string entropyText = "TurboRamaPixAgent-v1";
		DATA_BLOB input{ (DWORD)token.size(), (BYTE*)token.data() };
		DATA_BLOB entropy{ (DWORD)entropyText.size(), (BYTE*)entropyText.data() };
		DATA_BLOB output{};
		if (!CryptProtectData(&input, L"TurboRama PIX", &entropy, nullptr, nullptr, CRYPTPROTECT_UI_FORBIDDEN, &output))
		{
			error = L"O Windows nao conseguiu criptografar o Access Token.";
			return false;
		}
		const std::string encoded = base64(output.pbData, output.cbData);
		SecureZeroMemory(output.pbData, output.cbData);
		LocalFree(output.pbData);
		if (encoded.empty()) { error = L"Nao foi possivel preparar a credencial criptografada."; return false; }

		const std::wstring destination = join(bridge, L"secret.dat");
		const std::wstring temporary = destination + L"." + std::to_wstring(GetCurrentProcessId()) + L".tmp";
		std::wstring backup;
		DeleteFileW(temporary.c_str());
		if (!writeAll(temporary, encoded)) { error = L"Nao foi possivel gravar a credencial temporaria."; return false; }
		if (fileExists(destination))
		{
			backup = destination + L".backup-" + timestamp();
			CopyFileW(destination.c_str(), backup.c_str(), TRUE);
			SetFileAttributesW(destination.c_str(), FILE_ATTRIBUTE_NORMAL);
		}
		if (!MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
		{
			DeleteFileW(temporary.c_str());
			if (fileExists(destination)) SetFileAttributesW(destination.c_str(), FILE_ATTRIBUTE_HIDDEN);
			error = L"Nao foi possivel substituir o arquivo secret.dat.";
			return false;
		}
		SetFileAttributesW(destination.c_str(), FILE_ATTRIBUTE_HIDDEN);
		if (!decryptMatches(destination, token))
		{
			if (!backup.empty() && fileExists(backup)) CopyFileW(backup.c_str(), destination.c_str(), FALSE);
			if (fileExists(destination)) SetFileAttributesW(destination.c_str(), FILE_ATTRIBUTE_HIDDEN);
			error = L"A verificacao da credencial criptografada falhou.";
			return false;
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

	void restartAgent(const std::wstring& bridge)
	{
		if (normalized(bridge) != normalized(L"D:\\emulationstation\\.emulationstation\\pix")) return;
		const std::wstring root = L"D:\\emulationstation";
		const std::wstring dotnet = join(root, L"pix-agent\\runtime\\dotnet.exe");
		const std::wstring assembly = join(root, L"pix-agent\\TurboRamaPixAgent.dll");
		const std::wstring appHost = join(root, L"pix-agent\\TurboRamaPixAgent.exe");
		stopExact(dotnet); stopExact(appHost);
		std::wstring executable;
		std::wstring command;
		if (fileExists(dotnet) && fileExists(assembly))
		{
			executable = dotnet;
			command = L"\"" + dotnet + L"\" \"" + assembly + L"\" --bridge \"" + bridge + L"\"";
		}
		else if (fileExists(appHost))
		{
			executable = appHost;
			command = L"\"" + appHost + L"\" --bridge \"" + bridge + L"\"";
		}
		else return;
		std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
		STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_HIDE;
		PROCESS_INFORMATION process{};
		if (CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &process))
		{
			CloseHandle(process.hThread); CloseHandle(process.hProcess);
		}
	}

	void setStatus(const std::wstring& text, COLORREF = RGB(20, 80, 40))
	{
		SetWindowTextW(gStatus, text.c_str());
	}

	std::wstring editText()
	{
		const int length = GetWindowTextLengthW(gToken);
		std::wstring value(length + 1, L'\0');
		GetWindowTextW(gToken, value.data(), length + 1);
		value.resize(length);
		return trim(value);
	}

	void pasteClipboard(HWND owner)
	{
		if (!OpenClipboard(owner)) { setStatus(L"Nao foi possivel abrir a area de transferencia."); return; }
		HANDLE data = GetClipboardData(CF_UNICODETEXT);
		if (data)
		{
			const wchar_t* text = static_cast<const wchar_t*>(GlobalLock(data));
			if (text) { SetWindowTextW(gToken, trim(text).c_str()); GlobalUnlock(data); setStatus(L"Token colado. Confira e clique em SALVAR CRIPTOGRAFADO."); }
		}
		else setStatus(L"A area de transferencia nao contem texto.");
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
		if (file == INVALID_HANDLE_VALUE) { setStatus(L"Nao foi possivel abrir o arquivo selecionado."); return; }
		LARGE_INTEGER size{}; bool valid = GetFileSizeEx(file, &size) && size.QuadPart > 0 && size.QuadPart <= 4096;
		std::vector<BYTE> bytes(valid ? (size_t)size.QuadPart : 0); DWORD read = 0;
		valid = valid && ReadFile(file, bytes.data(), (DWORD)bytes.size(), &read, nullptr) && read == bytes.size(); CloseHandle(file);
		if (!valid) { setStatus(L"O arquivo deve conter somente o Access Token e ter no maximo 4 KB."); return; }
		const std::wstring token = trim(decodeTextFile(bytes));
		if (token.empty()) { setStatus(L"O arquivo nao contem um texto UTF-8 ou UTF-16 valido."); return; }
		SetWindowTextW(gToken, token.c_str()); setStatus(L"Token importado. Confira e clique em SALVAR CRIPTOGRAFADO.");
	}

	LRESULT CALLBACK windowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
	{
		switch (message)
		{
		case WM_CREATE:
		{
			gFont = CreateFontW(-19, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
				CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
			auto control = [window](const wchar_t* kind, const wchar_t* text, DWORD style, int x, int y, int width, int height, int id) {
				HWND handle = CreateWindowExW(kind == std::wstring(L"EDIT") ? WS_EX_CLIENTEDGE : 0, kind, text, WS_CHILD | WS_VISIBLE | style,
					x, y, width, height, window, (HMENU)(INT_PTR)id, GetModuleHandleW(nullptr), nullptr);
				SendMessageW(handle, WM_SETFONT, (WPARAM)gFont, TRUE); return handle;
			};
			control(L"STATIC", L"CONFIGURAR ACCESS TOKEN DO MERCADO PAGO", SS_LEFT, 28, 22, 630, 30, 0);
			control(L"STATIC", L"Cole o token completo iniciado por APP_USR-. Ele sera criptografado pelo Windows e nunca sera salvo como texto comum.", SS_LEFT, 28, 60, 650, 48, 0);
			gToken = control(L"EDIT", L"", ES_AUTOHSCROLL | ES_PASSWORD, 28, 120, 650, 34, ID_TOKEN);
			SendMessageW(gToken, EM_SETPASSWORDCHAR, (WPARAM)L'*', 0);
			control(L"BUTTON", L"COLAR TOKEN", BS_PUSHBUTTON, 28, 172, 145, 38, ID_PASTE);
			control(L"BUTTON", L"IMPORTAR TXT", BS_PUSHBUTTON, 184, 172, 145, 38, ID_IMPORT);
			control(L"BUTTON", L"MOSTRAR", BS_AUTOCHECKBOX, 346, 177, 120, 28, ID_SHOW);
			control(L"STATIC", L"Destino: D:\\emulationstation\\.emulationstation\\pix\\secret.dat", SS_LEFT, 28, 230, 650, 28, 0);
			gStatus = control(L"STATIC", fileExists(join(bridgeDirectory(), L"secret.dat")) ?
				L"Credencial atual encontrada. Salvar criara um backup e substituirá somente o token." :
				L"Nenhuma credencial encontrada. Cole ou importe o Access Token.", SS_LEFT, 28, 268, 650, 52, ID_STATUS);
			control(L"BUTTON", L"SALVAR CRIPTOGRAFADO", BS_DEFPUSHBUTTON, 28, 330, 290, 46, ID_SAVE);
			control(L"BUTTON", L"FECHAR", BS_PUSHBUTTON, 528, 330, 150, 46, ID_CLOSE);
			return 0;
		}
		case WM_COMMAND:
			switch (LOWORD(wParam))
			{
			case ID_PASTE: pasteClipboard(window); return 0;
			case ID_IMPORT: importFile(window); return 0;
			case ID_SHOW:
				SendMessageW(gToken, EM_SETPASSWORDCHAR, IsDlgButtonChecked(window, ID_SHOW) == BST_CHECKED ? 0 : (WPARAM)L'*', 0);
				InvalidateRect(gToken, nullptr, TRUE); return 0;
			case ID_SAVE:
			{
				std::wstring value = editText(); std::string token = utf8(value); std::wstring error;
				if (!saveToken(bridgeDirectory(), token, error)) { SecureZeroMemory(token.data(), token.size()); setStatus(error); MessageBoxW(window, error.c_str(), kTitle, MB_OK | MB_ICONERROR); return 0; }
				SecureZeroMemory(token.data(), token.size()); SecureZeroMemory(value.data(), value.size() * sizeof(wchar_t)); SetWindowTextW(gToken, L"");
				restartAgent(bridgeDirectory());
				setStatus(L"Access Token salvo, criptografado e agente PIX reiniciado.");
				MessageBoxW(window, L"Access Token salvo com seguranca.\n\nO agente PIX foi reiniciado e tentara conectar automaticamente.", kTitle, MB_OK | MB_ICONINFORMATION);
				return 0;
			}
			case ID_CLOSE: DestroyWindow(window); return 0;
			}
			break;
		case WM_CTLCOLORSTATIC:
			SetBkColor((HDC)wParam, RGB(248, 250, 253)); return (LRESULT)GetStockObject(NULL_BRUSH);
		case WM_DESTROY:
			if (gFont) DeleteObject(gFont); PostQuitMessage(0); return 0;
		}
		return DefWindowProcW(window, message, wParam, lParam);
	}
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int show)
{
	int argumentCount = 0;
	wchar_t** arguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
	if (arguments && argumentCount >= 2 && std::wstring(arguments[1]) == L"--self-test")
	{
		std::wstring testBridge = argumentCount >= 3 ? arguments[2] : bridgeDirectory();
		std::string testToken = "APP_USR-" + std::string(64, 'T'); std::wstring error;
		const bool success = saveToken(testBridge, testToken, error) && decryptMatches(join(testBridge, L"secret.dat"), testToken);
		SecureZeroMemory(testToken.data(), testToken.size()); LocalFree(arguments);
		return success ? 0 : 20;
	}
	if (arguments) LocalFree(arguments);
	WNDCLASSEXW type{}; type.cbSize = sizeof(type); type.hInstance = instance; type.lpfnWndProc = windowProcedure;
	type.lpszClassName = kClassName; type.hCursor = LoadCursorW(nullptr, IDC_ARROW); type.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(1));
	type.hIconSm = type.hIcon; type.hbrBackground = CreateSolidBrush(RGB(248, 250, 253));
	if (!RegisterClassExW(&type)) return 1;
	HWND window = CreateWindowExW(0, kClassName, kTitle, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
		CW_USEDEFAULT, CW_USEDEFAULT, 730, 440, nullptr, nullptr, instance, nullptr);
	if (!window) return 2;
	ShowWindow(window, show); UpdateWindow(window);
	MSG message{};
	while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
	return (int)message.wParam;
}
