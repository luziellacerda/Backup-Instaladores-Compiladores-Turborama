#define UNICODE
#define _UNICODE
#include <windows.h>
#include <tlhelp32.h>
#include <shlwapi.h>

#include <algorithm>
#include <cwctype>
#include <string>
#include <vector>

#pragma comment(lib, "shlwapi.lib")

#ifndef TURBORAMA_RELEASE_NUMBER
#define TURBORAMA_RELEASE_NUMBER 16
#endif
#define TR_STRINGIFY_DETAIL(value) #value
#define TR_STRINGIFY(value) TR_STRINGIFY_DETAIL(value)
#define TR_WIDEN_DETAIL(value) L##value
#define TR_WIDEN(value) TR_WIDEN_DETAIL(value)
#define TR_WSTRINGIFY(value) TR_WIDEN(TR_STRINGIFY(value))

namespace
{
	const wchar_t* kReleaseTag = L"v" TR_WSTRINGIFY(TURBORAMA_RELEASE_NUMBER);
	const wchar_t* kTitle = L"TurboRama - Sistema PIX Comercial v" TR_WSTRINGIFY(TURBORAMA_RELEASE_NUMBER);

	std::wstring join(const std::wstring& left, const std::wstring& right)
	{
		if (left.empty()) return right;
		return left + (left.back() == L'\\' ? L"" : L"\\") + right;
	}

	std::wstring parentOf(const std::wstring& path)
	{
		auto copy = path;
		const size_t position = copy.find_last_of(L"\\/");
		return position == std::wstring::npos ? L"." : copy.substr(0, position);
	}

	std::wstring normalized(const std::wstring& value)
	{
		wchar_t full[32768]{};
		const DWORD length = GetFullPathNameW(value.c_str(), 32768, full, nullptr);
		std::wstring result = length > 0 && length < 32768 ? full : value;
		std::replace(result.begin(), result.end(), L'/', L'\\');
		std::transform(result.begin(), result.end(), result.begin(), ::towlower);
		return result;
	}

	bool ensureDirectory(const std::wstring& directory)
	{
		if (directory.empty()) return false;
		if (GetFileAttributesW(directory.c_str()) != INVALID_FILE_ATTRIBUTES) return true;
		const auto parent = parentOf(directory);
		if (parent != directory && !ensureDirectory(parent)) return false;
		return CreateDirectoryW(directory.c_str(), nullptr) != FALSE || GetLastError() == ERROR_ALREADY_EXISTS;
	}

	bool exists(const std::wstring& path)
	{
		const DWORD attributes = GetFileAttributesW(path.c_str());
		return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
	}

	void stopExactProcess(const std::wstring& expectedPath)
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
				if (process == nullptr) continue;
				wchar_t image[32768]{}; DWORD length = 32768;
				const bool same = QueryFullProcessImageNameW(process, 0, image, &length) != FALSE && normalized(image) == expected;
				if (same)
				{
					TerminateProcess(process, 0);
					WaitForSingleObject(process, 5000);
				}
				CloseHandle(process);
			} while (Process32NextW(snapshot, &entry));
		}
		CloseHandle(snapshot);
	}

	std::wstring timestamp()
	{
		SYSTEMTIME time{}; GetLocalTime(&time);
		wchar_t value[64]{};
		swprintf_s(value, L"%04u%02u%02u-%02u%02u%02u", time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond);
		return value;
	}

	bool runAndWait(const std::wstring& executable, const std::wstring& arguments, DWORD& exitCode)
	{
		std::wstring command = L"\"" + executable + L"\" " + arguments;
		std::vector<wchar_t> mutableCommand(command.begin(), command.end());
		mutableCommand.push_back(L'\0');
		STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_HIDE;
		PROCESS_INFORMATION process{};
		if (!CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
			nullptr, parentOf(executable).c_str(), &startup, &process)) return false;
		CloseHandle(process.hThread);
		WaitForSingleObject(process.hProcess, INFINITE);
		GetExitCodeProcess(process.hProcess, &exitCode);
		CloseHandle(process.hProcess);
		return true;
	}

	void removeIfPresent(const std::wstring& path)
	{
		if (!DeleteFileW(path.c_str()) && GetLastError() != ERROR_FILE_NOT_FOUND)
		{
			// O proximo agente ainda valida e protege a pasta. Nao falhamos a
			// atualizacao somente por um arquivo transitorio que ja nao existe.
		}
	}

	void resetCredentialEditorState(const std::wstring& target)
	{
		const std::wstring pix = join(target, L".emulationstation\\pix");
		// Instalacoes anteriores deixavam essa ponte editavel por usuarios
		// autenticados. Rotacionamos chaves/recibos temporarios e apagamos
		// estados publicos antigos; secret.dat, cadastro do dono, creditos,
		// ROMs e temas sao preservados.
		removeIfPresent(join(pix, L"credential-agent-key.dat"));
		removeIfPresent(join(pix, L"agent-public-key.pem"));
		removeIfPresent(join(pix, L"credential-update.json"));
		removeIfPresent(join(pix, L"credential-update-status.json"));
		removeIfPresent(join(pix, L"credential-replay.dat"));
		removeIfPresent(join(pix, L"agent-status.json"));
		removeIfPresent(join(pix, L"owner-setup-status.json"));
		removeIfPresent(join(pix, L"public-options.json"));
	}

	bool prepareCredentialEditor(const std::wstring& target)
	{
		const std::wstring runtime = join(target, L"pix-agent\\runtime\\dotnet.exe");
		const std::wstring agent = join(target, L"pix-agent\\TurboRamaPixAgent.dll");
		const std::wstring bridge = join(target, L".emulationstation\\pix");
		if (!exists(runtime) || !exists(agent)) return false;
		DWORD exitCode = 999;
		const std::wstring arguments = L"\"" + agent + L"\" --prepare-credential-editor --bridge \"" + bridge + L"\"";
		return runAndWait(runtime, arguments, exitCode) && exitCode == 0
			&& exists(join(bridge, L"agent-public-key.pem"));
	}

	void copyIfPresent(const std::wstring& source, const std::wstring& destination)
	{
		if (!exists(source)) return;
		ensureDirectory(parentOf(destination));
		CopyFileW(source.c_str(), destination.c_str(), FALSE);
	}

	void writeInstallLog(const std::wstring& target, const std::wstring& backup, bool editorPrepared)
	{
		const std::wstring directory = join(target, L".emulationstation\\pix");
		if (!ensureDirectory(directory)) return;
		const std::wstring file = join(directory, L"installation-" + std::wstring(kReleaseTag) + L".log");
		const std::wstring text = L"TurboRama PIX Comercial " + std::wstring(kReleaseTag) + L" instalado com sucesso.\r\nBackup: " + backup
			+ L"\r\nPonte segura do editor: " + (editorPrepared ? L"preparada" : L"sera preparada ao abrir o EmulationStation") + L"\r\n";
		HANDLE handle = CreateFileW(file.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
		if (handle == INVALID_HANDLE_VALUE) return;
		DWORD written = 0;
		const unsigned char bom[] = { 0xFF, 0xFE };
		WriteFile(handle, bom, sizeof(bom), &written, nullptr);
		WriteFile(handle, text.data(), (DWORD)(text.size() * sizeof(wchar_t)), &written, nullptr);
		FlushFileBuffers(handle); CloseHandle(handle);
	}

	void launchEmulationStation(const std::wstring& executable)
	{
		STARTUPINFOW startup{}; startup.cb = sizeof(startup);
		PROCESS_INFORMATION process{};
		std::wstring command = L"\"" + executable + L"\"";
		std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
		if (CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE, 0, nullptr,
			parentOf(executable).c_str(), &startup, &process))
		{
			CloseHandle(process.hThread); CloseHandle(process.hProcess);
		}
	}
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
	wchar_t module[32768]{};
	GetModuleFileNameW(nullptr, module, 32768);
	const std::wstring source = parentOf(module);
	const std::wstring archive = join(source, L"payload.7z");
	const std::wstring sevenZip = join(source, L"7za.exe");
	wchar_t targetOverride[32768]{};
	const DWORD targetLength = GetEnvironmentVariableW(L"TURBORAMA_INSTALL_TARGET", targetOverride, 32768);
	const std::wstring target = targetLength > 0 && targetLength < 32768 ? targetOverride : L"D:\\emulationstation";
	wchar_t silentValue[8]{};
	const bool silentTest = GetEnvironmentVariableW(L"TURBORAMA_INSTALLER_SILENT_TEST", silentValue, 8) > 0
		&& std::wstring(silentValue) == L"1";
	const std::wstring targetExecutable = join(target, L"emulationstation.exe");

	if (!exists(archive) || !exists(sevenZip))
	{
		if (!silentTest) MessageBoxW(nullptr, L"O pacote interno do instalador esta incompleto. Baixe novamente o arquivo oficial.", kTitle, MB_OK | MB_ICONERROR);
		return 10;
	}
	if (!exists(targetExecutable))
	{
		if (!silentTest) MessageBoxW(nullptr,
			L"A instalacao do TurboRama nao foi encontrada em:\n\nD:\\emulationstation\\emulationstation.exe\n\n"
			L"Este instalador atualiza o sistema existente e nao altera ROMs ou temas.", kTitle, MB_OK | MB_ICONERROR);
		return 11;
	}
	if (!silentTest && MessageBoxW(nullptr,
		L"Instalar o Sistema PIX Comercial no TurboRama?\n\n"
		L"- O EmulationStation sera fechado, mas o computador permanecera ligado.\n"
		L"- ROMs, temas, creditos e configuracoes serao preservados.\n"
		L"- Um backup sera criado automaticamente.",
		kTitle, MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON1) != IDYES) return 0;

	stopExactProcess(targetExecutable);
	stopExactProcess(join(target, L"pix-agent\\runtime\\dotnet.exe"));
	stopExactProcess(join(target, L"pix-agent\\TurboRamaPixAgent.exe"));

	const std::wstring backup = join(target, L"backups\\PIX-COMERCIAL-" + std::wstring(kReleaseTag) + L"-" + timestamp());
	if (!ensureDirectory(backup) || !CopyFileW(targetExecutable.c_str(), join(backup, L"emulationstation.exe").c_str(), FALSE))
	{
		if (!silentTest) MessageBoxW(nullptr, L"Nao foi possivel criar o backup. A instalacao foi cancelada sem alterar o sistema.", kTitle, MB_OK | MB_ICONERROR);
		return 12;
	}
	copyIfPresent(join(target, L".emulationstation\\pix\\owner-settings.json"), join(backup, L"owner-settings.json"));
	// O segredo DPAPI permanece no local protegido. Nao o duplicamos em
	// backups legiveis: a atualizacao nao o substitui e o agente validara sua
	// leitura apos reiniciar.

	DWORD exitCode = 999;
	const std::wstring arguments = L"x -y -aoa \"" + archive + L"\" -o\"" + target + L"\"";
	if (!runAndWait(sevenZip, arguments, exitCode) || exitCode != 0
		|| !exists(targetExecutable)
		|| !exists(join(target, L"pix-agent\\TurboRamaPixAgent.dll"))
		|| !exists(join(target, L"pix-agent\\runtime\\dotnet.exe"))
		|| !exists(join(target, L"CONFIGURAR-ACCESS-TOKEN-PIX.exe"))
		|| !exists(join(target, L"CONFIGURAR-USER-TOKEN-PIX.exe")))
	{
		CopyFileW(join(backup, L"emulationstation.exe").c_str(), targetExecutable.c_str(), FALSE);
		if (!silentTest) MessageBoxW(nullptr, L"A instalacao falhou. O executavel anterior foi restaurado pelo backup.", kTitle, MB_OK | MB_ICONERROR);
		return 13;
	}

	resetCredentialEditorState(target);
	const bool editorPrepared = prepareCredentialEditor(target);
	writeInstallLog(target, backup, editorPrepared);
	const std::wstring completionMessage = std::wstring(
		L"INSTALACAO CONCLUIDA.\n\n"
		L"Proprietario: pressione START, informe a senha e abra CONFIGURACAO PIX DO PROPRIETARIO.\n\n"
		L"Para configurar ou trocar proprietario, provedor, conta, Loja, PDV, Access Token e precos, use:\n"
		L"D:\\emulationstation\\CONFIGURAR-USER-TOKEN-PIX.exe\n\n"
		L"O editor CONFIGURAR-ACCESS-TOKEN-PIX.exe permanece disponivel somente para trocar a credencial da conta atual.\n\n")
		+ (editorPrepared
			? L"A ponte segura do editor PIX ja foi preparada.\n\n"
			: L"Abra o EmulationStation uma vez para finalizar a ponte segura do editor PIX.\n\n")
		+ L"Cliente: pressione SELECT para comprar tempo com PIX, sem senha.\n\n"
		L"Deseja abrir o EmulationStation agora?";
	if (!silentTest && MessageBoxW(nullptr, completionMessage.c_str(),
		kTitle, MB_YESNO | MB_ICONINFORMATION | MB_DEFBUTTON1) == IDYES)
		launchEmulationStation(targetExecutable);
	return 0;
}
