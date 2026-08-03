#define UNICODE
#define _UNICODE
#include <windows.h>
#include <bcrypt.h>

#include <array>
#include <cstdint>
#include <string>
#include <vector>

#pragma comment(lib, "bcrypt.lib")

#ifndef TURBORAMA_RELEASE_NUMBER
#define TURBORAMA_RELEASE_NUMBER 16
#endif
#define TR_STRINGIFY_DETAIL(value) #value
#define TR_STRINGIFY(value) TR_STRINGIFY_DETAIL(value)
#define TR_WIDEN_DETAIL(value) L##value
#define TR_WIDEN(value) TR_WIDEN_DETAIL(value)
#define TR_WSTRINGIFY(value) TR_WIDEN(TR_STRINGIFY(value))

#pragma pack(push, 1)
struct TurboRamaPackageFooter
{
	char magic[16];
	std::uint32_t version;
	std::uint64_t installerSize;
	std::uint64_t sevenZipSize;
	std::uint64_t payloadSize;
	unsigned char installerSha256[32];
	unsigned char sevenZipSha256[32];
	unsigned char payloadSha256[32];
};
#pragma pack(pop)

namespace
{
	const wchar_t* kTitle = L"TurboRama - Sistema PIX Comercial v" TR_WSTRINGIFY(TURBORAMA_RELEASE_NUMBER);
	const char kMagic[16] = { 'T','R','P','I','X','V','1','4','P','A','C','K','A','G','E','\0' };

	std::wstring join(const std::wstring& left, const std::wstring& right)
	{
		return left + (left.empty() || left.back() == L'\\' ? L"" : L"\\") + right;
	}

	std::wstring tempDirectory()
	{
		wchar_t temp[MAX_PATH + 1]{};
		if (GetTempPathW(MAX_PATH, temp) == 0) return {};
		return join(temp, L"TurboRamaPixV" TR_WSTRINGIFY(TURBORAMA_RELEASE_NUMBER) L"-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()));
	}

	bool sameHash(const unsigned char* left, const unsigned char* right)
	{
		unsigned char difference = 0;
		for (size_t i = 0; i < 32; ++i) difference |= left[i] ^ right[i];
		return difference == 0;
	}

	bool extractPart(HANDLE source, const std::wstring& destination, std::uint64_t size, const unsigned char expected[32])
	{
		HANDLE output = CreateFileW(destination.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH, nullptr);
		if (output == INVALID_HANDLE_VALUE) return false;

		BCRYPT_ALG_HANDLE algorithm = nullptr;
		BCRYPT_HASH_HANDLE hash = nullptr;
		DWORD objectSize = 0, received = 0;
		std::vector<unsigned char> object;
		bool success = BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0
			&& BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, (PUCHAR)&objectSize, sizeof(objectSize), &received, 0) >= 0;
		if (success)
		{
			object.resize(objectSize);
			success = BCryptCreateHash(algorithm, &hash, object.data(), objectSize, nullptr, 0, 0) >= 0;
		}

		std::vector<unsigned char> buffer(1024 * 1024);
		std::uint64_t remaining = size;
		while (success && remaining > 0)
		{
			const DWORD wanted = (DWORD)(remaining < buffer.size() ? remaining : buffer.size());
			DWORD read = 0, written = 0;
			success = ReadFile(source, buffer.data(), wanted, &read, nullptr) != FALSE && read == wanted
				&& WriteFile(output, buffer.data(), read, &written, nullptr) != FALSE && written == read
				&& BCryptHashData(hash, buffer.data(), read, 0) >= 0;
			remaining -= read;
		}
		unsigned char digest[32]{};
		if (success) success = BCryptFinishHash(hash, digest, sizeof(digest), 0) >= 0 && sameHash(digest, expected);
		if (success) success = FlushFileBuffers(output) != FALSE;
		if (hash) BCryptDestroyHash(hash);
		if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
		CloseHandle(output);
		if (!success) DeleteFileW(destination.c_str());
		return success;
	}

	int launchInstaller(const std::wstring& executable, const std::wstring& directory)
	{
		std::wstring command = L"\"" + executable + L"\"";
		std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
		STARTUPINFOW startup{}; startup.cb = sizeof(startup);
		PROCESS_INFORMATION process{};
		if (!CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE, 0, nullptr,
			directory.c_str(), &startup, &process)) return 30;
		CloseHandle(process.hThread);
		WaitForSingleObject(process.hProcess, INFINITE);
		DWORD exitCode = 31;
		GetExitCodeProcess(process.hProcess, &exitCode);
		CloseHandle(process.hProcess);
		return (int)exitCode;
	}

	void cleanup(const std::wstring& directory)
	{
		DeleteFileW(join(directory, L"TurboRamaInstaller.exe").c_str());
		DeleteFileW(join(directory, L"7za.exe").c_str());
		DeleteFileW(join(directory, L"payload.7z").c_str());
		RemoveDirectoryW(directory.c_str());
	}
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
	wchar_t module[32768]{};
	if (GetModuleFileNameW(nullptr, module, 32768) == 0) return 20;
	HANDLE source = CreateFileW(module, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
	if (source == INVALID_HANDLE_VALUE) return 21;

	LARGE_INTEGER fileSize{};
	TurboRamaPackageFooter footer{};
	LARGE_INTEGER footerPosition{};
	bool valid = GetFileSizeEx(source, &fileSize) != FALSE && fileSize.QuadPart > (LONGLONG)sizeof(footer);
	if (valid)
	{
		footerPosition.QuadPart = fileSize.QuadPart - (LONGLONG)sizeof(footer);
		DWORD read = 0;
		valid = SetFilePointerEx(source, footerPosition, nullptr, FILE_BEGIN) != FALSE
			&& ReadFile(source, &footer, sizeof(footer), &read, nullptr) != FALSE && read == sizeof(footer)
			&& memcmp(footer.magic, kMagic, sizeof(kMagic)) == 0 && footer.version == 14;
	}
	const std::uint64_t packageSize = footer.installerSize + footer.sevenZipSize + footer.payloadSize;
	valid = valid && packageSize > 0 && packageSize <= (std::uint64_t)fileSize.QuadPart - sizeof(footer);
	if (!valid)
	{
		CloseHandle(source);
		MessageBoxW(nullptr, L"O instalador esta incompleto ou corrompido. Baixe novamente o arquivo oficial.", kTitle, MB_OK | MB_ICONERROR);
		return 22;
	}

	LARGE_INTEGER packagePosition{};
	packagePosition.QuadPart = fileSize.QuadPart - (LONGLONG)sizeof(footer) - (LONGLONG)packageSize;
	if (!SetFilePointerEx(source, packagePosition, nullptr, FILE_BEGIN)) { CloseHandle(source); return 23; }
	const std::wstring directory = tempDirectory();
	if (directory.empty() || !CreateDirectoryW(directory.c_str(), nullptr)) { CloseHandle(source); return 24; }
	const std::wstring installer = join(directory, L"TurboRamaInstaller.exe");
	const bool extracted = extractPart(source, installer, footer.installerSize, footer.installerSha256)
		&& extractPart(source, join(directory, L"7za.exe"), footer.sevenZipSize, footer.sevenZipSha256)
		&& extractPart(source, join(directory, L"payload.7z"), footer.payloadSize, footer.payloadSha256);
	CloseHandle(source);
	if (!extracted)
	{
		cleanup(directory);
		MessageBoxW(nullptr, L"Falha de integridade ao abrir o pacote. Nenhum arquivo do TurboRama foi alterado.", kTitle, MB_OK | MB_ICONERROR);
		return 25;
	}

	const int result = launchInstaller(installer, directory);
	cleanup(directory);
	return result;
}
