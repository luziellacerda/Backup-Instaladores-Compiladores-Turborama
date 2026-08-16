#define UNICODE
#define _UNICODE
#include <windows.h>
#include <wintrust.h>
#include <bcrypt.h>
#include <shellapi.h>
#include <sddl.h>
#include <aclapi.h>
#include <shlobj.h>

#include <array>
#include <cstdint>
#include <cwctype>
#include <string>
#include <vector>

#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "ole32.lib")

#ifndef TURBORAMA_RELEASE_NUMBER
#define TURBORAMA_RELEASE_NUMBER 25
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
	constexpr int kAuxiliaryTreeUnconfirmedExitCode = 42;

	constexpr bool expectedIsolatedSmokeFailure(int result)
	{
		// O compilador provoca estes quatro retornos de forma intencional e
		// verifica o rollback antes de seguir. O processo interno ja confirmou a
		// arvore do 7-Zip vazia; conservar quatro copias de 1,6 GB faria o proprio
		// laboratorio ficar sem espaco antes do teste valido.
		return result == 24 || result == 18 || result == 13 || result == 15;
	}

	constexpr bool preserveStagingForInstallerResult(int result, bool smoke)
	{
		return result != 0 && !(smoke && expectedIsolatedSmokeFailure(result));
	}

	constexpr int classifyInstallerProcessResult(DWORD waitResult, bool exitCodeRead,
		DWORD exitCode)
	{
		return waitResult == WAIT_OBJECT_0 && exitCodeRead && exitCode != STILL_ACTIVE
			? static_cast<int>(exitCode) : kAuxiliaryTreeUnconfirmedExitCode;
	}

	constexpr bool validateInstallerProcessResultContract()
	{
		return classifyInstallerProcessResult(WAIT_OBJECT_0, true, 0) == 0
			&& classifyInstallerProcessResult(WAIT_OBJECT_0, true, 41) == 41
			&& classifyInstallerProcessResult(WAIT_OBJECT_0, true,
				kAuxiliaryTreeUnconfirmedExitCode) == kAuxiliaryTreeUnconfirmedExitCode
			&& classifyInstallerProcessResult(WAIT_OBJECT_0, true,
				STILL_ACTIVE) == kAuxiliaryTreeUnconfirmedExitCode
			&& classifyInstallerProcessResult(WAIT_OBJECT_0, false, 0)
				== kAuxiliaryTreeUnconfirmedExitCode
			&& classifyInstallerProcessResult(WAIT_TIMEOUT, true, 0)
				== kAuxiliaryTreeUnconfirmedExitCode
			&& classifyInstallerProcessResult(WAIT_FAILED, true, 0)
				== kAuxiliaryTreeUnconfirmedExitCode;
	}

	static_assert(preserveStagingForInstallerResult(kAuxiliaryTreeUnconfirmedExitCode, true)
		&& !preserveStagingForInstallerResult(0, false)
		&& preserveStagingForInstallerResult(13, false)
		&& !preserveStagingForInstallerResult(13, true)
		&& !preserveStagingForInstallerResult(15, true)
		&& !preserveStagingForInstallerResult(18, true)
		&& !preserveStagingForInstallerResult(24, true)
		&& preserveStagingForInstallerResult(25, true)
		&& preserveStagingForInstallerResult(41, true)
		&& preserveStagingForInstallerResult(kAuxiliaryTreeUnconfirmedExitCode + 1, true),
		"Producao preserva falhas; o smoke limpa somente retornos injetados conhecidos.");
	static_assert(validateInstallerProcessResultContract(),
		"O bootstrapper so pode liberar cleanup depois de confirmar o termino do instalador interno.");

	std::wstring join(const std::wstring& left, const std::wstring& right)
	{
		return left + (left.empty() || left.back() == L'\\' ? L"" : L"\\") + right;
	}

	std::wstring parentOf(const std::wstring& path)
	{
		const size_t position = path.find_last_of(L"\\/");
		return position == std::wstring::npos ? L"." : path.substr(0, position);
	}

	std::wstring leafOf(const std::wstring& path)
	{
		const size_t position = path.find_last_of(L"\\/");
		return position == std::wstring::npos ? path : path.substr(position + 1);
	}

	std::wstring normalized(const std::wstring& value)
	{
		std::vector<wchar_t> full(32768, L'\0');
		const DWORD length = GetFullPathNameW(value.c_str(), static_cast<DWORD>(full.size()), full.data(), nullptr);
		std::wstring result = length > 0 && length < full.size() ? std::wstring(full.data(), length) : value;
		for (auto& character : result)
		{
			if (character == L'/') character = L'\\';
			character = (wchar_t)towlower(character);
		}
		while (result.size() > 3 && result.back() == L'\\') result.pop_back();
		return result;
	}

	std::wstring environmentValue(const wchar_t* name)
	{
		const DWORD required = GetEnvironmentVariableW(name, nullptr, 0);
		if (required == 0 || required > 32768) return {};
		std::vector<wchar_t> value(required);
		const DWORD written = GetEnvironmentVariableW(name, value.data(), required);
		return written > 0 && written < required ? std::wstring(value.data(), written) : std::wstring();
	}

	bool hasSingleArgument(const wchar_t* expected)
	{
		int count = 0;
		wchar_t** values = CommandLineToArgvW(GetCommandLineW(), &count);
		if (values == nullptr) return false;
		const bool matches = count == 2 && wcscmp(values[1], expected) == 0;
		LocalFree(values);
		return matches;
	}

	std::wstring isolatedSmokeTarget(const std::wstring& module)
	{
		// O smoke so pode atingir a arvore irma do candidato temporario exato.
		// Isso mantem todos os bytes descartaveis na unidade escolhida pelo build e
		// impede que um pacote ja promovido entre em modo de teste.
		const std::wstring generated = parentOf(module);
		const std::wstring pixCommercial = parentOf(generated);
		const std::wstring pack = parentOf(pixCommercial);
		const std::wstring build = parentOf(pack);
		const std::wstring boundary = parentOf(build);
		if (_wcsicmp(leafOf(generated).c_str(), L"GERADO-v25") != 0
			|| _wcsicmp(leafOf(pixCommercial).c_str(), L"PIX-COMERCIAL") != 0
			|| _wcsicmp(leafOf(pack).c_str(), L"pack") != 0
			|| _wcsicmp(leafOf(build).c_str(), L"TurboRama-v25-build") != 0)
			return {};
		return normalized(join(boundary, L"TurboRama-v25-smoke\\install"));
	}

	bool validateIsolatedSmokeTargetContract()
	{
		const std::wstring candidate = L"H:\\fixture\\TurboRama-v25-build\\pack\\PIX-COMERCIAL\\GERADO-v25\\INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe";
		const std::wstring canonical = L"H:\\fixture\\TurboramaEmulationStation\\PIX-COMERCIAL\\GERADO-v25\\INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe";
		return isolatedSmokeTarget(candidate)
			== normalized(L"H:\\fixture\\TurboRama-v25-smoke\\install")
			&& isolatedSmokeTarget(canonical).empty();
	}

	bool isProcessElevated()
	{
		HANDLE token = nullptr;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
		TOKEN_ELEVATION elevation{};
		DWORD returned = 0;
		const bool elevated = GetTokenInformation(token, TokenElevation, &elevation,
			sizeof(elevation), &returned) != FALSE && elevation.TokenIsElevated != 0;
		CloseHandle(token);
		return elevated;
	}

	bool strictSmokeRequest(const std::wstring& module)
	{
		if (environmentValue(L"TURBORAMA_INSTALLER_SILENT_TEST") != L"1") return false;
		const std::wstring target = normalized(environmentValue(L"TURBORAMA_INSTALL_TARGET"));
		const std::wstring expectedTarget = isolatedSmokeTarget(module);
		if (expectedTarget.empty() || target != expectedTarget) return false;
		const std::wstring image = normalized(module);
		const std::wstring imageSuffix = L"\\pix-comercial\\gerado-v25\\instalar-turborama-pix-comercial-v25-ultra-final.exe";
		return image.size() >= imageSuffix.size()
			&& image.compare(image.size() - imageSuffix.size(), imageSuffix.size(), imageSuffix) == 0;
	}

	int relaunchElevated(const std::wstring& executable)
	{
		const std::wstring directory = parentOf(executable);
		SHELLEXECUTEINFOW execute{};
		execute.cbSize = sizeof(execute);
		execute.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC;
		execute.lpVerb = L"runas";
		execute.lpFile = executable.c_str();
		execute.lpParameters = L"--turborama-elevated-bootstrap";
		execute.lpDirectory = directory.c_str();
		execute.nShow = SW_SHOWNORMAL;
		if (!ShellExecuteExW(&execute) || execute.hProcess == nullptr) return 26;
		WaitForSingleObject(execute.hProcess, INFINITE);
		DWORD exitCode = 26;
		GetExitCodeProcess(execute.hProcess, &exitCode);
		CloseHandle(execute.hProcess);
		return (int)exitCode;
	}

	void enablePrivilege(const wchar_t* name)
	{
		HANDLE token = nullptr;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &token)) return;
		LUID luid{};
		if (LookupPrivilegeValueW(nullptr, name, &luid))
		{
			TOKEN_PRIVILEGES privileges{};
			privileges.PrivilegeCount = 1;
			privileges.Privileges[0].Luid = luid;
			privileges.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
			AdjustTokenPrivileges(token, FALSE, &privileges, sizeof(privileges), nullptr, nullptr);
		}
		CloseHandle(token);
	}

	bool isReparsePoint(HANDLE directory)
	{
		FILE_ATTRIBUTE_TAG_INFO information{};
		return !GetFileInformationByHandleEx(directory, FileAttributeTagInfo, &information, sizeof(information))
			|| (information.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
	}

	bool applyObjectSecurity(HANDLE object, bool directory)
	{
		const wchar_t* descriptorText = directory
			? L"O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)"
			: L"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)";
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(descriptorText,
			SDDL_REVISION_1, &descriptor, nullptr)) return false;
		PSID owner = nullptr;
		PACL dacl = nullptr;
		BOOL defaulted = FALSE;
		BOOL present = FALSE;
		bool ok = GetSecurityDescriptorOwner(descriptor, &owner, &defaulted) != FALSE
			&& GetSecurityDescriptorDacl(descriptor, &present, &dacl, &defaulted) != FALSE && present;
		if (ok)
		{
			SECURITY_INFORMATION information = OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION
				| PROTECTED_DACL_SECURITY_INFORMATION;
			ok = SetSecurityInfo(object, SE_FILE_OBJECT, information, owner, nullptr, dacl, nullptr) == ERROR_SUCCESS;
		}
		LocalFree(descriptor);
		return ok;
	}

	bool validateObjectSecurity(HANDLE object, bool directory)
	{
		PSID owner = nullptr;
		PACL dacl = nullptr;
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		SECURITY_INFORMATION information = OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION;
		if (GetSecurityInfo(object, SE_FILE_OBJECT, information, &owner, nullptr, &dacl,
			nullptr, &descriptor) != ERROR_SUCCESS) return false;
		BYTE adminBuffer[SECURITY_MAX_SID_SIZE]{};
		BYTE systemBuffer[SECURITY_MAX_SID_SIZE]{};
		DWORD adminSize = sizeof(adminBuffer), systemSize = sizeof(systemBuffer);
		const bool sids = CreateWellKnownSid(WinBuiltinAdministratorsSid, nullptr, adminBuffer, &adminSize) != FALSE
			&& CreateWellKnownSid(WinLocalSystemSid, nullptr, systemBuffer, &systemSize) != FALSE;
		bool ownerOk = sids && owner != nullptr && EqualSid(owner, adminBuffer) != FALSE;
		bool adminFull = false, systemFull = false, unexpected = false;
		if (dacl == nullptr || dacl->AceCount != 2) unexpected = true;
		else
		{
			for (DWORD index = 0; index < dacl->AceCount; ++index)
			{
				void* raw = nullptr;
				if (!GetAce(dacl, index, &raw)) { unexpected = true; break; }
				auto* header = static_cast<ACE_HEADER*>(raw);
				if (header->AceType != ACCESS_ALLOWED_ACE_TYPE) { unexpected = true; continue; }
				auto* ace = static_cast<ACCESS_ALLOWED_ACE*>(raw);
				PSID sid = &ace->SidStart;
				const BYTE expectedFlags = directory ? (OBJECT_INHERIT_ACE | CONTAINER_INHERIT_ACE) : 0;
				if (header->AceFlags != expectedFlags) { unexpected = true; continue; }
				if (EqualSid(sid, adminBuffer)) adminFull = ace->Mask == FILE_ALL_ACCESS;
				else if (EqualSid(sid, systemBuffer)) systemFull = ace->Mask == FILE_ALL_ACCESS;
				else unexpected = true;
			}
		}
		SECURITY_DESCRIPTOR_CONTROL control = 0;
		DWORD revision = 0;
		const bool protectedDacl = GetSecurityDescriptorControl(descriptor, &control, &revision) != FALSE
			&& (control & SE_DACL_PROTECTED) != 0;
		LocalFree(descriptor);
		return ownerOk && adminFull && systemFull && !unexpected && protectedDacl;
	}

	bool validateProgramDataParent(const std::wstring& path)
	{
		HANDLE handle = CreateFileW(path.c_str(), READ_CONTROL,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (handle == INVALID_HANDLE_VALUE || isReparsePoint(handle))
		{
			if (handle != INVALID_HANDLE_VALUE) CloseHandle(handle);
			return false;
		}
		PACL dacl = nullptr;
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		bool ok = GetSecurityInfo(handle, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
			nullptr, nullptr, &dacl, nullptr, &descriptor) == ERROR_SUCCESS && dacl != nullptr;
		BYTE users[SECURITY_MAX_SID_SIZE]{}, authenticated[SECURITY_MAX_SID_SIZE]{}, everyone[SECURITY_MAX_SID_SIZE]{};
		DWORD usersSize = sizeof(users), authenticatedSize = sizeof(authenticated), everyoneSize = sizeof(everyone);
		ok = ok && CreateWellKnownSid(WinBuiltinUsersSid, nullptr, users, &usersSize) != FALSE
			&& CreateWellKnownSid(WinAuthenticatedUserSid, nullptr, authenticated, &authenticatedSize) != FALSE
			&& CreateWellKnownSid(WinWorldSid, nullptr, everyone, &everyoneSize) != FALSE;
		GENERIC_MAPPING directoryMapping{
			FILE_GENERIC_READ, FILE_GENERIC_WRITE, FILE_GENERIC_EXECUTE, FILE_ALL_ACCESS
		};
		const DWORD destructiveDirectoryRights = FILE_DELETE_CHILD | DELETE | WRITE_DAC | WRITE_OWNER;
		if (ok)
		{
			for (DWORD index = 0; index < dacl->AceCount; ++index)
			{
				void* raw = nullptr;
				if (!GetAce(dacl, index, &raw)) { ok = false; break; }
				auto* header = static_cast<ACE_HEADER*>(raw);
				if (header->AceType != ACCESS_ALLOWED_ACE_TYPE) continue;
				auto* ace = static_cast<ACCESS_ALLOWED_ACE*>(raw);
				PSID sid = &ace->SidStart;
				DWORD mappedMask = ace->Mask;
				MapGenericMask(&mappedMask, &directoryMapping);
				if ((EqualSid(sid, users) || EqualSid(sid, authenticated) || EqualSid(sid, everyone))
					&& (mappedMask & destructiveDirectoryRights) != 0)
				{
					ok = false;
					break;
				}
			}
		}
		// A pasta filha recebe nome aleatorio criptografico, DACL protegida e um
		// handle sem FILE_SHARE_DELETE imediatamente apos a criacao. Avaliar o
		// token UAC ligado aqui gerava falso negativo em contas administrativas
		// validas (inclusive maquinas sem token dividido) sem acrescentar protecao
		// ao objeto filho ja pinado. Mantemos a rejeicao objetiva das ACEs
		// destrutivas de Users/Authenticated Users/Everyone no pai.
		if (descriptor) LocalFree(descriptor);
		CloseHandle(handle);
		return ok;
	}

	PSECURITY_DESCRIPTOR createStagingDescriptor()
	{
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		return ConvertStringSecurityDescriptorToSecurityDescriptorW(
			L"O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)",
			SDDL_REVISION_1, &descriptor, nullptr) ? descriptor : nullptr;
	}

	bool validateStagingDescriptorShape()
	{
		PSECURITY_DESCRIPTOR descriptor = createStagingDescriptor();
		if (descriptor == nullptr) return false;
		PSID owner = nullptr;
		PACL dacl = nullptr;
		BOOL defaulted = FALSE, present = FALSE;
		BYTE admins[SECURITY_MAX_SID_SIZE]{}, system[SECURITY_MAX_SID_SIZE]{};
		DWORD adminsSize = sizeof(admins), systemSize = sizeof(system);
		bool ok = GetSecurityDescriptorOwner(descriptor, &owner, &defaulted) != FALSE
			&& GetSecurityDescriptorDacl(descriptor, &present, &dacl, &defaulted) != FALSE && present
			&& CreateWellKnownSid(WinBuiltinAdministratorsSid, nullptr, admins, &adminsSize) != FALSE
			&& CreateWellKnownSid(WinLocalSystemSid, nullptr, system, &systemSize) != FALSE
			&& owner != nullptr && EqualSid(owner, admins) != FALSE && dacl != nullptr && dacl->AceCount == 2;
		bool adminFull = false, systemFull = false;
		if (ok)
		{
			for (DWORD index = 0; index < dacl->AceCount; ++index)
			{
				void* raw = nullptr;
				if (!GetAce(dacl, index, &raw)) { ok = false; break; }
				auto* header = static_cast<ACE_HEADER*>(raw);
				auto* ace = static_cast<ACCESS_ALLOWED_ACE*>(raw);
				if (header->AceType != ACCESS_ALLOWED_ACE_TYPE
					|| header->AceFlags != (OBJECT_INHERIT_ACE | CONTAINER_INHERIT_ACE)
					|| ace->Mask != FILE_ALL_ACCESS) { ok = false; break; }
				PSID sid = &ace->SidStart;
				if (EqualSid(sid, admins)) adminFull = true;
				else if (EqualSid(sid, system)) systemFull = true;
				else { ok = false; break; }
			}
		}
		SECURITY_DESCRIPTOR_CONTROL control = 0;
		DWORD revision = 0;
		ok = ok && adminFull && systemFull
			&& GetSecurityDescriptorControl(descriptor, &control, &revision) != FALSE
			&& (control & SE_DACL_PROTECTED) != 0;
		LocalFree(descriptor);
		return ok;
	}

	std::wstring randomName()
	{
		std::array<unsigned char, 16> random{};
		if (BCryptGenRandom(nullptr, random.data(), (ULONG)random.size(),
			BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0) return {};
		const wchar_t digits[] = L"0123456789abcdef";
		std::wstring result = L"stage-";
		for (unsigned char value : random)
		{
			result.push_back(digits[value >> 4]);
			result.push_back(digits[value & 0x0F]);
		}
		return result;
	}

	struct StagingDirectory
	{
		std::wstring path;
		HANDLE lock = INVALID_HANDLE_VALUE;
		bool smoke = false;
	};

	bool createStagingDirectory(bool smoke, const std::wstring& smokeTarget, StagingDirectory& staging,
		std::wstring& failure)
	{
		failure.clear();
		auto windowsFailure = [&](const wchar_t* step, DWORD code)
		{
			failure = std::wstring(step) + L" (codigo Windows " + std::to_wstring(code) + L")";
			return false;
		};
		staging.smoke = smoke;
		const std::wstring name = randomName();
		if (name.empty()) { failure = L"geracao do nome aleatorio seguro"; return false; }
		if (smoke)
		{
			const std::wstring base = parentOf(smokeTarget);
			const DWORD attributes = GetFileAttributesW(base.c_str());
			if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0
				|| (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
				return windowsFailure(L"validacao da pasta temporaria de teste", GetLastError());
			staging.path = join(base, name);
			if (!CreateDirectoryW(staging.path.c_str(), nullptr))
				return windowsFailure(L"criacao do staging de teste", GetLastError());
			staging.lock = CreateFileW(staging.path.c_str(), GENERIC_READ,
				FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
				FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			if (staging.lock == INVALID_HANDLE_VALUE)
				return windowsFailure(L"abertura do staging de teste", GetLastError());
			if (isReparsePoint(staging.lock))
			{
				failure = L"staging de teste redirecionado";
				CloseHandle(staging.lock);
				staging.lock = INVALID_HANDLE_VALUE;
				RemoveDirectoryW(staging.path.c_str());
				return false;
			}
			return true;
		}

		wchar_t programData[MAX_PATH + 1]{};
		if (SHGetFolderPathW(nullptr, CSIDL_COMMON_APPDATA, nullptr, SHGFP_TYPE_CURRENT, programData) != S_OK)
			return windowsFailure(L"localizacao de C:\\ProgramData", GetLastError());
		if (!validateProgramDataParent(programData))
		{
			failure = L"validacao das permissoes de C:\\ProgramData";
			return false;
		}
		PSECURITY_DESCRIPTOR descriptor = createStagingDescriptor();
		if (descriptor == nullptr)
			return windowsFailure(L"preparo da DACL administrativa", GetLastError());
		SECURITY_ATTRIBUTES security{};
		security.nLength = sizeof(security);
		security.lpSecurityDescriptor = descriptor;
		security.bInheritHandle = FALSE;
		bool created = false;
		DWORD createError = ERROR_SUCCESS;
		for (unsigned attempt = 0; attempt < 4 && !created; ++attempt)
		{
			const std::wstring candidateName = attempt == 0 ? name : randomName();
			if (candidateName.empty()) break;
			staging.path = join(programData, L"TurboRamaInstaller-" + candidateName);
			created = CreateDirectoryW(staging.path.c_str(), &security) != FALSE;
			if (!created)
			{
				createError = GetLastError();
				if (createError != ERROR_ALREADY_EXISTS) break;
			}
		}
		LocalFree(descriptor);
		if (!created) return windowsFailure(L"criacao da pasta administrativa", createError);
		staging.lock = CreateFileW(staging.path.c_str(), READ_CONTROL | WRITE_DAC | WRITE_OWNER,
			FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		const bool opened = staging.lock != INVALID_HANDLE_VALUE;
		const bool redirected = opened && isReparsePoint(staging.lock);
		const bool secured = opened && !redirected && applyObjectSecurity(staging.lock, true);
		const bool verified = secured && validateObjectSecurity(staging.lock, true);
		if (!opened || redirected || !secured || !verified)
		{
			const DWORD failureCode = GetLastError();
			failure = !opened ? L"abertura da pasta administrativa"
				: redirected ? L"staging administrativo redirecionado"
				: !secured ? L"aplicacao da DACL administrativa"
				: L"confirmacao da DACL administrativa";
			if (failureCode != ERROR_SUCCESS)
				failure += L" (codigo Windows " + std::to_wstring(failureCode) + L")";
			if (staging.lock != INVALID_HANDLE_VALUE) CloseHandle(staging.lock);
			RemoveDirectoryW(staging.path.c_str());
			staging.lock = INVALID_HANDLE_VALUE;
			return false;
		}
		return true;
	}

	bool sameHash(const unsigned char* left, const unsigned char* right)
	{
		unsigned char difference = 0;
		for (size_t i = 0; i < 32; ++i) difference |= left[i] ^ right[i];
		return difference == 0;
	}

	std::wstring hashHex(const unsigned char hash[32])
	{
		const wchar_t digits[] = L"0123456789abcdef";
		std::wstring result;
		result.reserve(64);
		for (size_t index = 0; index < 32; ++index)
		{
			result.push_back(digits[hash[index] >> 4]);
			result.push_back(digits[hash[index] & 0x0F]);
		}
		return result;
	}

	bool hashHandle(HANDLE source, unsigned char digest[32])
	{
		LARGE_INTEGER original{};
		if (!SetFilePointerEx(source, {}, &original, FILE_CURRENT)) return false;
		LARGE_INTEGER start{};
		if (!SetFilePointerEx(source, start, nullptr, FILE_BEGIN)) return false;
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
		while (success)
		{
			DWORD read = 0;
			if (!ReadFile(source, buffer.data(), (DWORD)buffer.size(), &read, nullptr)) { success = false; break; }
			if (read == 0) break;
			success = BCryptHashData(hash, buffer.data(), read, 0) >= 0;
		}
		if (success) success = BCryptFinishHash(hash, digest, 32, 0) >= 0;
		if (hash) BCryptDestroyHash(hash);
		if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
		SetFilePointerEx(source, original, nullptr, FILE_BEGIN);
		return success;
	}

	bool extractPart(HANDLE source, const std::wstring& destination, std::uint64_t size,
		const unsigned char expected[32], bool secure)
	{
		const DWORD desiredAccess = secure
			? GENERIC_WRITE | READ_CONTROL | WRITE_DAC | WRITE_OWNER
			: GENERIC_WRITE;
		HANDLE output = CreateFileW(destination.c_str(), desiredAccess, 0, nullptr, CREATE_NEW,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH, nullptr);
		if (output == INVALID_HANDLE_VALUE) return false;
		BCRYPT_ALG_HANDLE algorithm = nullptr;
		BCRYPT_HASH_HANDLE hash = nullptr;
		DWORD objectSize = 0, received = 0;
		std::vector<unsigned char> object;
		bool success = (!secure || (applyObjectSecurity(output, false) && validateObjectSecurity(output, false)))
			&& BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0
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

	HANDLE openPinnedFile(const std::wstring& path, const unsigned char expected[32], bool secure)
	{
		const DWORD desiredAccess = secure ? GENERIC_READ | READ_CONTROL : GENERIC_READ;
		HANDLE file = CreateFileW(path.c_str(), desiredAccess,
			FILE_SHARE_READ, nullptr, OPEN_EXISTING,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
		if (file == INVALID_HANDLE_VALUE) return INVALID_HANDLE_VALUE;
		FILE_ATTRIBUTE_TAG_INFO attributes{};
		unsigned char digest[32]{};
		if (!GetFileInformationByHandleEx(file, FileAttributeTagInfo, &attributes, sizeof(attributes))
			|| (attributes.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0
			|| (secure && !validateObjectSecurity(file, false))
			|| !hashHandle(file, digest) || !sameHash(digest, expected))
		{
			CloseHandle(file);
			return INVALID_HANDLE_VALUE;
		}
		return file;
	}

	int launchInstaller(const std::wstring& executable, const std::wstring& directory,
		const TurboRamaPackageFooter& footer, bool smoke)
	{
		std::wstring command = L"\"" + executable + L"\" --trusted-bootstrap "
			+ hashHex(footer.installerSha256) + L" " + hashHex(footer.sevenZipSha256)
			+ L" " + hashHex(footer.payloadSha256);
		if (smoke) command += L" --isolated-smoke";
		std::vector<wchar_t> mutableCommand(command.begin(), command.end());
		mutableCommand.push_back(L'\0');
		STARTUPINFOW startup{};
		startup.cb = sizeof(startup);
		PROCESS_INFORMATION process{};
		if (!CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE, 0, nullptr,
			directory.c_str(), &startup, &process)) return 30;
		CloseHandle(process.hThread);
		const DWORD waitResult = WaitForSingleObject(process.hProcess, INFINITE);
		DWORD exitCode = STILL_ACTIVE;
		const bool exitCodeRead = waitResult == WAIT_OBJECT_0
			&& GetExitCodeProcess(process.hProcess, &exitCode) != FALSE;
		const int result = classifyInstallerProcessResult(waitResult, exitCodeRead, exitCode);
		CloseHandle(process.hProcess);
		return result;
	}

	bool removeTree(const std::wstring& directory)
	{
		const DWORD rootAttributes = GetFileAttributesW(directory.c_str());
		if (rootAttributes == INVALID_FILE_ATTRIBUTES) return true;
		if ((rootAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0
			|| (rootAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) return false;
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(directory, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE) return false;
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			const std::wstring child = join(directory, name);
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) { ok = false; continue; }
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
			{
				if (!removeTree(child)) ok = false;
			}
			else if (!DeleteFileW(child.c_str()) && GetLastError() != ERROR_FILE_NOT_FOUND) ok = false;
		} while (FindNextFileW(search, &entry));
		FindClose(search);
		return ok && (RemoveDirectoryW(directory.c_str()) != FALSE || GetLastError() == ERROR_PATH_NOT_FOUND);
	}

	bool removeTreeWithRetry(const std::wstring& directory)
	{
		constexpr unsigned attempts = 120;
		for (unsigned attempt = 0; attempt < attempts; ++attempt)
		{
			if (removeTree(directory)) return true;
			Sleep(250);
		}
		if (GetFileAttributesW(directory.c_str()) != INVALID_FILE_ATTRIBUTES) return false;
		const DWORD error = GetLastError();
		return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND;
	}

	bool readAt(HANDLE source, std::uint64_t offset, void* destination, DWORD size)
	{
		LARGE_INTEGER position{};
		position.QuadPart = (LONGLONG)offset;
		DWORD read = 0;
		return SetFilePointerEx(source, position, nullptr, FILE_BEGIN) != FALSE
			&& ReadFile(source, destination, size, &read, nullptr) != FALSE && read == size;
	}

	std::uint64_t signedContentEnd(HANDLE source, std::uint64_t fileSize)
	{
		IMAGE_DOS_HEADER dos{};
		if (!readAt(source, 0, &dos, sizeof(dos)) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew <= 0)
			return fileSize;
		DWORD signature = 0;
		IMAGE_FILE_HEADER fileHeader{};
		const std::uint64_t ntOffset = (std::uint64_t)dos.e_lfanew;
		if (!readAt(source, ntOffset, &signature, sizeof(signature)) || signature != IMAGE_NT_SIGNATURE
			|| !readAt(source, ntOffset + sizeof(signature), &fileHeader, sizeof(fileHeader))) return fileSize;
		const std::uint64_t optionalOffset = ntOffset + sizeof(signature) + sizeof(fileHeader);
		WORD magic = 0;
		if (!readAt(source, optionalOffset, &magic, sizeof(magic))) return fileSize;
		IMAGE_DATA_DIRECTORY security{};
		if (magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC)
		{
			IMAGE_OPTIONAL_HEADER64 optional{};
			if (fileHeader.SizeOfOptionalHeader < sizeof(optional)
				|| !readAt(source, optionalOffset, &optional, sizeof(optional))) return fileSize;
			security = optional.DataDirectory[IMAGE_DIRECTORY_ENTRY_SECURITY];
		}
		else if (magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC)
		{
			IMAGE_OPTIONAL_HEADER32 optional{};
			if (fileHeader.SizeOfOptionalHeader < sizeof(optional)
				|| !readAt(source, optionalOffset, &optional, sizeof(optional))) return fileSize;
			security = optional.DataDirectory[IMAGE_DIRECTORY_ENTRY_SECURITY];
		}
		if (security.VirtualAddress == 0 || security.Size < sizeof(WIN_CERTIFICATE)
			|| (std::uint64_t)security.VirtualAddress + security.Size > fileSize) return fileSize;
		return security.VirtualAddress;
	}
}

int WINAPI wWinMain(_In_ HINSTANCE, _In_opt_ HINSTANCE, _In_ PWSTR, _In_ int)
{
	std::vector<wchar_t> moduleBuffer(32768, L'\0');
	const DWORD moduleLength = GetModuleFileNameW(nullptr, moduleBuffer.data(),
		static_cast<DWORD>(moduleBuffer.size()));
	if (moduleLength == 0 || moduleLength >= moduleBuffer.size()) return 20;
	const std::wstring module(moduleBuffer.data(), moduleLength);
	if (hasSingleArgument(L"--self-test"))
	{
		const std::wstring first = randomName();
		const std::wstring second = randomName();
		return validateStagingDescriptorShape() && first.size() == 38 && second.size() == 38
			&& first != second && first.rfind(L"stage-", 0) == 0 && second.rfind(L"stage-", 0) == 0
			&& validateIsolatedSmokeTargetContract()
			&& validateInstallerProcessResultContract()
			&& preserveStagingForInstallerResult(kAuxiliaryTreeUnconfirmedExitCode, true)
			&& !preserveStagingForInstallerResult(0, false)
			&& preserveStagingForInstallerResult(13, false)
			&& !preserveStagingForInstallerResult(13, true)
			&& !preserveStagingForInstallerResult(15, true)
			&& !preserveStagingForInstallerResult(18, true)
			&& !preserveStagingForInstallerResult(24, true)
			&& preserveStagingForInstallerResult(25, true)
			&& preserveStagingForInstallerResult(41, true)
			&& preserveStagingForInstallerResult(kAuxiliaryTreeUnconfirmedExitCode + 1, true) ? 0 : 41;
	}
	const bool elevated = isProcessElevated();
	if (elevated && environmentValue(L"TURBORAMA_INSTALLER_SILENT_TEST") == L"1")
	{
		MessageBoxW(nullptr,
			L"O smoke test isolado deve ser executado em uma sessao nao elevada. Teste cancelado para proteger D:\\emulationstation.",
			kTitle, MB_OK | MB_ICONERROR);
		return 28;
	}
	const bool smoke = !elevated && strictSmokeRequest(module);
	if (!elevated && !smoke) return relaunchElevated(module);

	enablePrivilege(SE_TAKE_OWNERSHIP_NAME);
	enablePrivilege(SE_RESTORE_NAME);

	HANDLE source = CreateFileW(module.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
		FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
	if (source == INVALID_HANDLE_VALUE) return 21;
	FILE_ATTRIBUTE_TAG_INFO moduleAttributes{};
	if (!GetFileInformationByHandleEx(source, FileAttributeTagInfo, &moduleAttributes, sizeof(moduleAttributes))
		|| (moduleAttributes.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
	{
		CloseHandle(source);
		return 21;
	}

	LARGE_INTEGER fileSize{};
	TurboRamaPackageFooter footer{};
	LARGE_INTEGER footerPosition{};
	bool valid = GetFileSizeEx(source, &fileSize) != FALSE && fileSize.QuadPart > (LONGLONG)sizeof(footer);
	if (valid)
	{
		const std::uint64_t contentEnd = signedContentEnd(source, (std::uint64_t)fileSize.QuadPart);
		valid = false;
		for (std::uint64_t padding = 0; padding < 8 && contentEnd >= sizeof(footer) + padding; ++padding)
		{
			footerPosition.QuadPart = (LONGLONG)(contentEnd - sizeof(footer) - padding);
			if (readAt(source, (std::uint64_t)footerPosition.QuadPart, &footer, sizeof(footer))
				&& memcmp(footer.magic, kMagic, sizeof(kMagic)) == 0 && footer.version == 14)
			{
				valid = true;
				break;
			}
		}
	}
	std::uint64_t packageSize = 0;
	if (valid)
	{
		const std::uint64_t limit = (std::uint64_t)footerPosition.QuadPart;
		valid = footer.installerSize > 0 && footer.sevenZipSize > 0 && footer.payloadSize > 0
			&& footer.installerSize <= limit;
		if (valid) packageSize = footer.installerSize;
		valid = valid && footer.sevenZipSize <= limit - packageSize;
		if (valid) packageSize += footer.sevenZipSize;
		valid = valid && footer.payloadSize <= limit - packageSize;
		if (valid) packageSize += footer.payloadSize;
	}
	if (!valid)
	{
		CloseHandle(source);
		MessageBoxW(nullptr, L"O instalador esta incompleto ou corrompido. Baixe novamente o arquivo oficial.",
			kTitle, MB_OK | MB_ICONERROR);
		return 22;
	}

	LARGE_INTEGER packagePosition{};
	packagePosition.QuadPart = footerPosition.QuadPart - (LONGLONG)packageSize;
	if (!SetFilePointerEx(source, packagePosition, nullptr, FILE_BEGIN)) { CloseHandle(source); return 23; }
	StagingDirectory staging;
	std::wstring stagingFailure;
	if (!createStagingDirectory(smoke, environmentValue(L"TURBORAMA_INSTALL_TARGET"), staging, stagingFailure))
	{
		CloseHandle(source);
		std::wstring message = L"Nao foi possivel criar o staging administrativo protegido. Nenhum arquivo foi alterado.";
		if (!stagingFailure.empty()) message += L"\n\nEtapa: " + stagingFailure + L".";
		MessageBoxW(nullptr, message.c_str(), kTitle, MB_OK | MB_ICONERROR);
		return 24;
	}
	const std::wstring installer = join(staging.path, L"TurboRamaInstaller.exe");
	const std::wstring sevenZip = join(staging.path, L"7za.exe");
	const std::wstring payload = join(staging.path, L"payload.7z");
	const bool extracted = extractPart(source, installer, footer.installerSize, footer.installerSha256, !smoke)
		&& extractPart(source, sevenZip, footer.sevenZipSize, footer.sevenZipSha256, !smoke)
		&& extractPart(source, payload, footer.payloadSize, footer.payloadSha256, !smoke);
	CloseHandle(source);
	if (!extracted)
	{
		CloseHandle(staging.lock);
		removeTree(staging.path);
		MessageBoxW(nullptr, L"Falha de integridade ao abrir o pacote. Nenhum arquivo do TurboRama foi alterado.",
			kTitle, MB_OK | MB_ICONERROR);
		return 25;
	}

	HANDLE installerLock = openPinnedFile(installer, footer.installerSha256, !smoke);
	HANDLE sevenZipLock = openPinnedFile(sevenZip, footer.sevenZipSha256, !smoke);
	HANDLE payloadLock = openPinnedFile(payload, footer.payloadSha256, !smoke);
	if (installerLock == INVALID_HANDLE_VALUE || sevenZipLock == INVALID_HANDLE_VALUE
		|| payloadLock == INVALID_HANDLE_VALUE)
	{
		if (installerLock != INVALID_HANDLE_VALUE) CloseHandle(installerLock);
		if (sevenZipLock != INVALID_HANDLE_VALUE) CloseHandle(sevenZipLock);
		if (payloadLock != INVALID_HANDLE_VALUE) CloseHandle(payloadLock);
		CloseHandle(staging.lock);
		removeTree(staging.path);
		MessageBoxW(nullptr, L"O staging mudou apos a validacao. Instalacao cancelada sem alterar o TurboRama.",
			kTitle, MB_OK | MB_ICONERROR);
		return 27;
	}

	const int result = launchInstaller(installer, staging.path, footer, smoke);
	CloseHandle(payloadLock);
	CloseHandle(sevenZipLock);
	CloseHandle(installerLock);
	CloseHandle(staging.lock);
	if (preserveStagingForInstallerResult(result, smoke))
	{
		const std::wstring reason = result == kAuxiliaryTreeUnconfirmedExitCode
			? L"Uma ferramenta auxiliar nao confirmou o encerramento de toda a arvore de processos."
			: L"O instalador interno recusou concluir a operacao (codigo "
				+ std::to_wstring(result) + L").";
		const std::wstring message = reason
			+ L" Para impedir rollback ou limpeza insegura, o staging e o rollback foram preservados em:\n\n"
			+ staging.path
			+ L"\n\nReinicie o Windows. Depois, verifique manualmente se nao existe 7za.exe, powershell.exe ou dotnet.exe dessa instalacao em execucao antes de analisar ou remover essa pasta. Nao execute outro instalador ate concluir essa verificacao.";
		MessageBoxW(nullptr, message.c_str(), kTitle, MB_OK | MB_ICONERROR);
		return result;
	}
	if (!removeTreeWithRetry(staging.path))
	{
		MessageBoxW(nullptr,
			L"O processo interno terminou, mas o staging nao pode ser removido integralmente. "
			L"A validacao foi recusada para evitar acumulo ou mistura de artefatos.",
			kTitle, MB_OK | MB_ICONERROR);
		return 29;
	}
	return result;
}
