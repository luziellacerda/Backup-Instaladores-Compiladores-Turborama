#define UNICODE
#define _UNICODE
#include <windows.h>
#include <tlhelp32.h>
#include <shlwapi.h>
#include <shellapi.h>
#include <bcrypt.h>
#include <sddl.h>
#include <aclapi.h>
#include <lm.h>
#include <shlobj.h>
#include <winioctl.h>

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <cwctype>
#include <set>
#include <string>
#include <vector>

#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "netapi32.lib")
#pragma comment(lib, "ole32.lib")

#ifndef TURBORAMA_RELEASE_NUMBER
#define TURBORAMA_RELEASE_NUMBER 25
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
	constexpr int kAuxiliaryTreeUnconfirmedExitCode = 42;

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
		std::vector<wchar_t> full(32768);
		const DWORD length = GetFullPathNameW(value.c_str(), static_cast<DWORD>(full.size()),
			full.data(), nullptr);
		std::wstring result = length > 0 && length < full.size() ? full.data() : value;
		std::replace(result.begin(), result.end(), L'/', L'\\');
		std::transform(result.begin(), result.end(), result.begin(), ::towlower);
		return result;
	}

	std::wstring environmentValue(const wchar_t* name)
	{
		const DWORD required = GetEnvironmentVariableW(name, nullptr, 0);
		if (required == 0) return {};
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

	std::wstring isolatedSmokeTarget()
	{
		PWSTR localAppData = nullptr;
		if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &localAppData))
			|| localAppData == nullptr) return {};
		const std::wstring target = normalized(join(localAppData, L"Temp\\TurboRama-v25-smoke\\install"));
		CoTaskMemFree(localAppData);
		return target;
	}

	bool hasSuffix(const std::wstring& value, const std::wstring& suffix)
	{
		return value.size() >= suffix.size()
			&& value.compare(value.size() - suffix.size(), suffix.size(), suffix) == 0;
	}

	struct BootstrapArguments
	{
		std::array<unsigned char, 32> installerHash{};
		std::array<unsigned char, 32> sevenZipHash{};
		std::array<unsigned char, 32> payloadHash{};
		bool isolatedSmoke = false;
	};

	bool parseHexHash(const wchar_t* value, std::array<unsigned char, 32>& hash)
	{
		if (value == nullptr || wcslen(value) != 64) return false;
		for (size_t index = 0; index < hash.size(); ++index)
		{
			auto digit = [](wchar_t character) -> int
			{
				if (character >= L'0' && character <= L'9') return character - L'0';
				character = (wchar_t)towlower(character);
				return character >= L'a' && character <= L'f' ? character - L'a' + 10 : -1;
			};
			const int high = digit(value[index * 2]);
			const int low = digit(value[index * 2 + 1]);
			if (high < 0 || low < 0) return false;
			hash[index] = (unsigned char)((high << 4) | low);
		}
		return true;
	}

	bool parseBootstrapArguments(BootstrapArguments& arguments)
	{
		int count = 0;
		wchar_t** values = CommandLineToArgvW(GetCommandLineW(), &count);
		if (values == nullptr) return false;
		const bool shape = (count == 5 || count == 6) && wcscmp(values[1], L"--trusted-bootstrap") == 0
			&& (count == 5 || wcscmp(values[5], L"--isolated-smoke") == 0);
		const bool valid = shape && parseHexHash(values[2], arguments.installerHash)
			&& parseHexHash(values[3], arguments.sevenZipHash)
			&& parseHexHash(values[4], arguments.payloadHash);
		arguments.isolatedSmoke = valid && count == 6;
		LocalFree(values);
		return valid;
	}

	bool sameHash(const unsigned char* left, const unsigned char* right)
	{
		unsigned char difference = 0;
		for (size_t index = 0; index < 32; ++index) difference |= left[index] ^ right[index];
		return difference == 0;
	}

	bool hashHandle(HANDLE file, unsigned char digest[32])
	{
		LARGE_INTEGER original{};
		if (!SetFilePointerEx(file, {}, &original, FILE_CURRENT)) return false;
		if (!SetFilePointerEx(file, {}, nullptr, FILE_BEGIN)) return false;
		BCRYPT_ALG_HANDLE algorithm = nullptr;
		BCRYPT_HASH_HANDLE hash = nullptr;
		DWORD objectSize = 0, received = 0;
		std::vector<unsigned char> object;
		bool ok = BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0
			&& BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, (PUCHAR)&objectSize,
				sizeof(objectSize), &received, 0) >= 0;
		if (ok)
		{
			object.resize(objectSize);
			ok = BCryptCreateHash(algorithm, &hash, object.data(), objectSize, nullptr, 0, 0) >= 0;
		}
		std::vector<unsigned char> buffer(1024 * 1024);
		while (ok)
		{
			DWORD read = 0;
			if (!ReadFile(file, buffer.data(), (DWORD)buffer.size(), &read, nullptr)) { ok = false; break; }
			if (read == 0) break;
			ok = BCryptHashData(hash, buffer.data(), read, 0) >= 0;
		}
		if (ok) ok = BCryptFinishHash(hash, digest, 32, 0) >= 0;
		if (hash) BCryptDestroyHash(hash);
		if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
		SetFilePointerEx(file, original, nullptr, FILE_BEGIN);
		return ok;
	}

	bool validateAdminOnlyObject(HANDLE object, bool directory)
	{
		PSID owner = nullptr;
		PACL dacl = nullptr;
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		if (GetSecurityInfo(object, SE_FILE_OBJECT,
			OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION,
			&owner, nullptr, &dacl, nullptr, &descriptor) != ERROR_SUCCESS) return false;
		BYTE admins[SECURITY_MAX_SID_SIZE]{}, system[SECURITY_MAX_SID_SIZE]{};
		DWORD adminsSize = sizeof(admins), systemSize = sizeof(system);
		bool ok = CreateWellKnownSid(WinBuiltinAdministratorsSid, nullptr, admins, &adminsSize) != FALSE
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
				if (header->AceType != ACCESS_ALLOWED_ACE_TYPE
					|| header->AceFlags != (directory ? OBJECT_INHERIT_ACE | CONTAINER_INHERIT_ACE : 0))
				{
					ok = false;
					break;
				}
				auto* ace = static_cast<ACCESS_ALLOWED_ACE*>(raw);
				if (ace->Mask != FILE_ALL_ACCESS) { ok = false; break; }
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

	HANDLE openPinnedFile(const std::wstring& path, const std::array<unsigned char, 32>& expected,
		bool production)
	{
		const DWORD access = production ? GENERIC_READ | READ_CONTROL : GENERIC_READ;
		HANDLE file = CreateFileW(path.c_str(), access, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
		if (file == INVALID_HANDLE_VALUE) return INVALID_HANDLE_VALUE;
		FILE_ATTRIBUTE_TAG_INFO attributes{};
		FILE_STANDARD_INFO standard{};
		unsigned char digest[32]{};
		if (!GetFileInformationByHandleEx(file, FileAttributeTagInfo, &attributes, sizeof(attributes))
			|| !GetFileInformationByHandleEx(file, FileStandardInfo, &standard, sizeof(standard))
			|| (attributes.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0
			|| standard.NumberOfLinks != 1
			|| (production && !validateAdminOnlyObject(file, false))
			|| !hashHandle(file, digest) || !sameHash(digest, expected.data()))
		{
			CloseHandle(file);
			return INVALID_HANDLE_VALUE;
		}
		return file;
	}

	HANDLE validateAndLockStaging(const std::wstring& source, bool production)
	{
		if (production)
		{
			wchar_t programData[MAX_PATH + 1]{};
			if (SHGetFolderPathW(nullptr, CSIDL_COMMON_APPDATA, nullptr, SHGFP_TYPE_CURRENT, programData) != S_OK)
				return INVALID_HANDLE_VALUE;
			const std::wstring expectedParent = normalized(programData);
			if (normalized(parentOf(source)) != expectedParent) return INVALID_HANDLE_VALUE;
			std::wstring leaf = normalized(source).substr(expectedParent.size() + 1);
			const std::wstring prefix = L"turboramainstaller-stage-";
			if (leaf.size() != prefix.size() + 32 || leaf.compare(0, prefix.size(), prefix) != 0
				|| !std::all_of(leaf.begin() + (std::ptrdiff_t)prefix.size(), leaf.end(), [](wchar_t character)
					{ return (character >= L'0' && character <= L'9') || (character >= L'a' && character <= L'f'); }))
				return INVALID_HANDLE_VALUE;
		}
		HANDLE directory = CreateFileW(source.c_str(),
			production ? GENERIC_READ | READ_CONTROL : GENERIC_READ,
			FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (directory == INVALID_HANDLE_VALUE) return INVALID_HANDLE_VALUE;
		FILE_ATTRIBUTE_TAG_INFO attributes{};
		if (!GetFileInformationByHandleEx(directory, FileAttributeTagInfo, &attributes, sizeof(attributes))
			|| (attributes.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0
			|| (attributes.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0
			|| (production && !validateAdminOnlyObject(directory, true)))
		{
			CloseHandle(directory);
			return INVALID_HANDLE_VALUE;
		}
		return directory;
	}

	bool readUtf8FileStrict(const std::wstring& path, std::wstring& text)
	{
		text.clear();
		HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
		if (file == INVALID_HANDLE_VALUE) return false;
		FILE_ATTRIBUTE_TAG_INFO attributes{};
		LARGE_INTEGER size{};
		bool ok = GetFileInformationByHandleEx(file, FileAttributeTagInfo, &attributes, sizeof(attributes)) != FALSE
			&& (attributes.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) == 0
			&& GetFileSizeEx(file, &size) != FALSE && size.QuadPart > 0 && size.QuadPart <= 1024 * 1024;
		std::vector<char> bytes(ok ? (size_t)size.QuadPart : 0);
		DWORD read = 0;
		if (ok) ok = ReadFile(file, bytes.data(), (DWORD)bytes.size(), &read, nullptr) != FALSE
			&& read == bytes.size();
		CloseHandle(file);
		if (!ok) return false;
		size_t offset = bytes.size() >= 3 && (unsigned char)bytes[0] == 0xEF
			&& (unsigned char)bytes[1] == 0xBB && (unsigned char)bytes[2] == 0xBF ? 3 : 0;
		const int required = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data() + offset,
			(int)(bytes.size() - offset), nullptr, 0);
		if (required <= 0) return false;
		text.resize((size_t)required);
		return MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data() + offset,
			(int)(bytes.size() - offset), text.data(), required) == required;
	}

	class JsonReader
	{
	public:
		explicit JsonReader(const std::wstring& text) : mText(text) {}
		bool readKioskUser(std::wstring& kioskUser)
		{
			std::wstring frontendExecutable;
			bool frontendPresent = false;
			return readLauncherValues(kioskUser, frontendExecutable, frontendPresent);
		}
		bool readLauncherValues(std::wstring& kioskUser, std::wstring& frontendExecutable,
			bool& frontendPresent)
		{
			kioskUser.clear();
			frontendExecutable.clear();
			frontendPresent = false;
			skipWhitespace();
			if (!take(L'{')) return false;
			bool kioskFound = false;
			skipWhitespace();
			if (!take(L'}'))
			{
				for (;;)
				{
					std::wstring key;
					if (!readString(key)) return false;
					skipWhitespace(); if (!take(L':')) return false; skipWhitespace();
					if (key == L"kioskUser")
					{
						if (kioskFound || !readString(kioskUser)) return false;
						kioskFound = true;
					}
					else if (key == L"frontendExecutable")
					{
						if (frontendPresent || !readString(frontendExecutable)) return false;
						frontendPresent = true;
					}
					else if (!skipValue(1)) return false;
					skipWhitespace();
					if (take(L'}')) break;
					if (!take(L',')) return false;
					skipWhitespace();
				}
			}
			skipWhitespace();
			return kioskFound && !kioskUser.empty() && kioskUser.size() <= 256
				&& (!frontendPresent || frontendExecutable.size() < 32768)
				&& mPosition == mText.size();
		}

	private:
		const std::wstring& mText;
		size_t mPosition = 0;
		void skipWhitespace()
		{
			while (mPosition < mText.size()
				&& (mText[mPosition] == L' ' || mText[mPosition] == L'\t'
					|| mText[mPosition] == L'\r' || mText[mPosition] == L'\n')) ++mPosition;
		}
		bool take(wchar_t character)
		{
			if (mPosition >= mText.size() || mText[mPosition] != character) return false;
			++mPosition;
			return true;
		}
		bool readHex(unsigned& value)
		{
			value = 0;
			if (mPosition + 4 > mText.size()) return false;
			for (unsigned count = 0; count < 4; ++count)
			{
				const wchar_t character = mText[mPosition++];
				const int digit = character >= L'0' && character <= L'9' ? character - L'0'
					: character >= L'a' && character <= L'f' ? character - L'a' + 10
					: character >= L'A' && character <= L'F' ? character - L'A' + 10 : -1;
				if (digit < 0) return false;
				value = value * 16 + (unsigned)digit;
			}
			return true;
		}
		bool readString(std::wstring& value)
		{
			value.clear();
			if (!take(L'\"')) return false;
			while (mPosition < mText.size())
			{
				wchar_t character = mText[mPosition++];
				if (character == L'\"') return true;
				if (character < 0x20) return false;
				if (character != L'\\') { value.push_back(character); continue; }
				if (mPosition >= mText.size()) return false;
				character = mText[mPosition++];
				switch (character)
				{
				case L'\"': value.push_back(L'\"'); break;
				case L'\\': value.push_back(L'\\'); break;
				case L'/': value.push_back(L'/'); break;
				case L'b': value.push_back(L'\b'); break;
				case L'f': value.push_back(L'\f'); break;
				case L'n': value.push_back(L'\n'); break;
				case L'r': value.push_back(L'\r'); break;
				case L't': value.push_back(L'\t'); break;
				case L'u':
				{
					unsigned code = 0;
					if (!readHex(code) || (code >= 0xDC00 && code <= 0xDFFF)) return false;
					value.push_back((wchar_t)code);
					if (code >= 0xD800 && code <= 0xDBFF)
					{
						if (mPosition + 2 > mText.size() || mText[mPosition++] != L'\\'
							|| mText[mPosition++] != L'u') return false;
						unsigned low = 0;
						if (!readHex(low) || low < 0xDC00 || low > 0xDFFF) return false;
						value.push_back((wchar_t)low);
					}
					break;
				}
				default: return false;
				}
			}
			return false;
		}
		bool skipLiteral(const wchar_t* value)
		{
			const size_t length = wcslen(value);
			if (mText.compare(mPosition, length, value) != 0) return false;
			mPosition += length;
			return true;
		}
		bool skipNumber()
		{
			const size_t start = mPosition;
			if (take(L'-') && mPosition >= mText.size()) return false;
			if (take(L'0'))
			{
				if (mPosition < mText.size() && iswdigit(mText[mPosition])) return false;
			}
			else
			{
				if (mPosition >= mText.size() || mText[mPosition] < L'1' || mText[mPosition] > L'9') return false;
				while (mPosition < mText.size() && iswdigit(mText[mPosition])) ++mPosition;
			}
			if (take(L'.'))
			{
				const size_t digits = mPosition;
				while (mPosition < mText.size() && iswdigit(mText[mPosition])) ++mPosition;
				if (mPosition == digits) return false;
			}
			if (mPosition < mText.size() && (mText[mPosition] == L'e' || mText[mPosition] == L'E'))
			{
				++mPosition;
				if (mPosition < mText.size() && (mText[mPosition] == L'+' || mText[mPosition] == L'-')) ++mPosition;
				const size_t digits = mPosition;
				while (mPosition < mText.size() && iswdigit(mText[mPosition])) ++mPosition;
				if (mPosition == digits) return false;
			}
			return mPosition > start;
		}
		bool skipValue(unsigned depth)
		{
			if (depth > 32 || mPosition >= mText.size()) return false;
			if (mText[mPosition] == L'\"') { std::wstring ignored; return readString(ignored); }
			if (mText[mPosition] == L'{')
			{
				++mPosition; skipWhitespace();
				if (take(L'}')) return true;
				for (;;)
				{
					std::wstring key;
					if (!readString(key)) return false;
					skipWhitespace(); if (!take(L':')) return false; skipWhitespace();
					if (!skipValue(depth + 1)) return false;
					skipWhitespace(); if (take(L'}')) return true;
					if (!take(L',')) return false; skipWhitespace();
				}
			}
			if (mText[mPosition] == L'[')
			{
				++mPosition; skipWhitespace();
				if (take(L']')) return true;
				for (;;)
				{
					if (!skipValue(depth + 1)) return false;
					skipWhitespace(); if (take(L']')) return true;
					if (!take(L',')) return false; skipWhitespace();
				}
			}
			if (mText[mPosition] == L't') return skipLiteral(L"true");
			if (mText[mPosition] == L'f') return skipLiteral(L"false");
			if (mText[mPosition] == L'n') return skipLiteral(L"null");
			return skipNumber();
		}
	};

	struct ResolvedIdentity
	{
		std::wstring account;
		std::wstring sidText;
		std::vector<unsigned char> sid;
	};

	bool identityFromSid(PSID source, const std::wstring& account, ResolvedIdentity& identity)
	{
		identity = {};
		if (source == nullptr || !IsValidSid(source)) return false;
		const DWORD length = GetLengthSid(source);
		if (length == 0) return false;
		std::vector<unsigned char> sid(length);
		if (!CopySid(length, sid.data(), source)) return false;
		LPWSTR text = nullptr;
		if (!ConvertSidToStringSidW(sid.data(), &text) || text == nullptr) return false;
		identity.account = account;
		identity.sidText = text;
		identity.sid = std::move(sid);
		LocalFree(text);
		return true;
	}

	bool wellKnownIdentity(WELL_KNOWN_SID_TYPE type, const std::wstring& account,
		ResolvedIdentity& identity)
	{
		BYTE sid[SECURITY_MAX_SID_SIZE]{};
		DWORD size = sizeof(sid);
		return CreateWellKnownSid(type, nullptr, sid, &size) != FALSE
			&& identityFromSid(sid, account, identity);
	}

	bool currentProcessIdentity(ResolvedIdentity& identity)
	{
		HANDLE token = nullptr;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
		DWORD size = 0;
		GetTokenInformation(token, TokenUser, nullptr, 0, &size);
		if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || size < sizeof(TOKEN_USER))
		{
			CloseHandle(token);
			return false;
		}
		std::vector<unsigned char> buffer(size);
		const bool read = GetTokenInformation(token, TokenUser, buffer.data(), size, &size) != FALSE;
		CloseHandle(token);
		return read && identityFromSid(reinterpret_cast<TOKEN_USER*>(buffer.data())->User.Sid,
			L"processo do smoke", identity);
	}

	bool isEffectiveTokenMember(const ResolvedIdentity& identity, bool& member)
	{
		member = false;
		if (identity.sid.empty() || IsValidSid((PSID)identity.sid.data()) == FALSE) return false;
		BOOL value = FALSE;
		if (!CheckTokenMembership(nullptr, (PSID)identity.sid.data(), &value)) return false;
		member = value != FALSE;
		return true;
	}

	bool currentRestrictedAccessIdentity(ResolvedIdentity& identity, bool& present)
	{
		identity = {};
		present = false;
		HANDLE token = nullptr;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
		DWORD size = 0;
		GetTokenInformation(token, TokenRestrictedSids, nullptr, 0, &size);
		const DWORD queryError = GetLastError();
		if (size == 0)
		{
			CloseHandle(token);
			return queryError == ERROR_SUCCESS;
		}
		if (queryError != ERROR_INSUFFICIENT_BUFFER || size < sizeof(TOKEN_GROUPS))
		{
			CloseHandle(token);
			return false;
		}
		std::vector<unsigned char> buffer(size);
		DWORD returned = 0;
		const bool read = GetTokenInformation(token, TokenRestrictedSids, buffer.data(), size,
			&returned) != FALSE;
		CloseHandle(token);
		if (!read || returned > size) return false;
		auto* groups = reinterpret_cast<TOKEN_GROUPS*>(buffer.data());
		for (DWORD index = 0; index < groups->GroupCount; ++index)
		{
			PSID sid = groups->Groups[index].Sid;
			if (sid != nullptr && IsValidSid(sid) != FALSE)
			{
				present = true;
				return identityFromSid(sid, L"SID restritivo do smoke", identity);
			}
		}
		return true;
	}

	bool lookupUserSid(const std::wstring& account, ResolvedIdentity& identity, std::wstring* domainResult = nullptr)
	{
		DWORD sidSize = 0, domainSize = 0;
		SID_NAME_USE use{};
		LookupAccountNameW(nullptr, account.c_str(), nullptr, &sidSize, nullptr, &domainSize, &use);
		if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || sidSize == 0 || domainSize == 0) return false;
		std::vector<unsigned char> sid(sidSize);
		std::vector<wchar_t> domain(domainSize);
		if (!LookupAccountNameW(nullptr, account.c_str(), sid.data(), &sidSize,
			domain.data(), &domainSize, &use) || use != SidTypeUser || !IsValidSid(sid.data())) return false;
		LPWSTR sidText = nullptr;
		if (!ConvertSidToStringSidW(sid.data(), &sidText)) return false;
		identity.account = account;
		identity.sidText = sidText;
		identity.sid = std::move(sid);
		LocalFree(sidText);
		if (domainResult) *domainResult = domain.data();
		return true;
	}

	bool readRegistryString(HKEY key, const wchar_t* name, std::wstring& value)
	{
		DWORD type = 0, size = 0;
		if (RegQueryValueExW(key, name, nullptr, &type, nullptr, &size) != ERROR_SUCCESS
			|| (type != REG_SZ && type != REG_EXPAND_SZ) || size < sizeof(wchar_t) || size > 4096) return false;
		std::vector<wchar_t> buffer(size / sizeof(wchar_t) + 1);
		if (RegQueryValueExW(key, name, nullptr, &type, (BYTE*)buffer.data(), &size) != ERROR_SUCCESS) return false;
		buffer.back() = L'\0';
		value = buffer.data();
		return !value.empty();
	}

	bool isLocalEnabledAccount(const std::wstring& resolvedDomain, const std::wstring& localUser)
	{
		wchar_t computer[MAX_COMPUTERNAME_LENGTH + 1]{};
		DWORD computerLength = MAX_COMPUTERNAME_LENGTH + 1;
		if (!GetComputerNameW(computer, &computerLength)
			|| _wcsicmp(resolvedDomain.c_str(), computer) != 0) return false;
		USER_INFO_4* accountInfo = nullptr;
		const NET_API_STATUS accountStatus = NetUserGetInfo(nullptr, localUser.c_str(), 4,
			(LPBYTE*)&accountInfo);
		const bool enabled = accountStatus == NERR_Success && accountInfo != nullptr
			&& (accountInfo->usri4_flags & (UF_ACCOUNTDISABLE | UF_LOCKOUT)) == 0;
		if (accountInfo) NetApiBufferFree(accountInfo);
		return enabled;
	}

	bool resolveKioskIdentity(const std::wstring& launcherConfig, bool smoke, ResolvedIdentity& identity)
	{
		std::wstring json, kioskUser;
		if (!readUtf8FileStrict(launcherConfig, json) || !JsonReader(json).readKioskUser(kioskUser)) return false;
		std::wstring jsonDomain;
		if (!lookupUserSid(kioskUser, identity, &jsonDomain)) return false;
		std::wstring jsonLocalUser = kioskUser;
		const size_t jsonSeparator = jsonLocalUser.find_last_of(L"\\/");
		if (jsonSeparator != std::wstring::npos) jsonLocalUser = jsonLocalUser.substr(jsonSeparator + 1);
		if (jsonLocalUser.empty() || !isLocalEnabledAccount(jsonDomain, jsonLocalUser)) return false;
		// O smoke isolado prova a mesma regra de conta local habilitada usada em
		// producao, mas nunca consulta nem relaxa o Winlogon real da maquina.
		if (smoke) return true;

		HKEY key = nullptr;
		if (RegOpenKeyExW(HKEY_LOCAL_MACHINE,
			L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon", 0,
			KEY_READ | KEY_WOW64_64KEY, &key) != ERROR_SUCCESS) return false;
		std::wstring autoLogon, defaultUser, defaultDomain;
		const bool read = readRegistryString(key, L"AutoAdminLogon", autoLogon)
			&& readRegistryString(key, L"DefaultUserName", defaultUser);
		readRegistryString(key, L"DefaultDomainName", defaultDomain);
		RegCloseKey(key);
		if (!read || autoLogon != L"1") return false;
		const std::wstring trustedAccount = defaultDomain.empty() || defaultDomain == L"."
			? defaultUser : defaultDomain + L"\\" + defaultUser;
		std::wstring localUser = defaultUser;
		const size_t separator = localUser.find_last_of(L"\\/");
		if (separator != std::wstring::npos) localUser = localUser.substr(separator + 1);
		if (localUser.empty()) return false;
		ResolvedIdentity trusted;
		std::wstring trustedDomain;
		if (!lookupUserSid(trustedAccount, trusted, &trustedDomain)
			|| !EqualSid(identity.sid.data(), trusted.sid.data())
			|| !isLocalEnabledAccount(trustedDomain, localUser)) return false;
		identity.account = trusted.account;
		identity.sidText = trusted.sidText;
		identity.sid = std::move(trusted.sid);
		return true;
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

	bool enablePrivilege(const wchar_t* name)
	{
		HANDLE token = nullptr;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &token)) return false;
		LUID luid{};
		TOKEN_PRIVILEGES privileges{};
		bool ok = LookupPrivilegeValueW(nullptr, name, &luid) != FALSE;
		if (ok)
		{
			privileges.PrivilegeCount = 1;
			privileges.Privileges[0].Luid = luid;
			privileges.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
			SetLastError(ERROR_SUCCESS);
			ok = AdjustTokenPrivileges(token, FALSE, &privileges, sizeof(privileges), nullptr, nullptr) != FALSE
				&& GetLastError() == ERROR_SUCCESS;
		}
		CloseHandle(token);
		return ok;
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

	bool directoryExists(const std::wstring& path)
	{
		const DWORD attributes = GetFileAttributesW(path.c_str());
		return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
	}

	bool sameFileIdentity(const FILE_ID_INFO& left, const FILE_ID_INFO& right)
	{
		return left.VolumeSerialNumber == right.VolumeSerialNumber
			&& memcmp(left.FileId.Identifier, right.FileId.Identifier,
				sizeof(left.FileId.Identifier)) == 0;
	}

	bool validateOpenedFilesystemObject(HANDLE object, const std::wstring& expectedPath,
		bool directory, FILE_ID_INFO* identity = nullptr, DWORD* failureCode = nullptr)
	{
		auto fail = [&](DWORD code)
		{
			const DWORD effective = code == ERROR_SUCCESS ? ERROR_INVALID_DATA : code;
			if (failureCode != nullptr) *failureCode = effective;
			SetLastError(effective);
			return false;
		};
		if (object == nullptr || object == INVALID_HANDLE_VALUE || expectedPath.empty()
			|| PathIsRelativeW(expectedPath.c_str())) return fail(ERROR_BAD_PATHNAME);

		FILE_ATTRIBUTE_TAG_INFO attributes{};
		FILE_STANDARD_INFO standard{};
		FILE_ID_INFO currentIdentity{};
		if (!GetFileInformationByHandleEx(object, FileAttributeTagInfo, &attributes,
			sizeof(attributes))
			|| !GetFileInformationByHandleEx(object, FileStandardInfo, &standard,
				sizeof(standard))
			|| !GetFileInformationByHandleEx(object, FileIdInfo, &currentIdentity,
				sizeof(currentIdentity))) return fail(GetLastError());
		if ((attributes.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0
			|| (((attributes.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) != directory))
			return fail(ERROR_REPARSE_TAG_INVALID);
		if (!directory && standard.NumberOfLinks != 1) return fail(ERROR_TOO_MANY_LINKS);

		const DWORD flags = FILE_NAME_NORMALIZED | VOLUME_NAME_DOS;
		const DWORD required = GetFinalPathNameByHandleW(object, nullptr, 0, flags);
		if (required == 0) return fail(GetLastError());
		std::vector<wchar_t> buffer(static_cast<size_t>(required) + 1, L'\0');
		const DWORD written = GetFinalPathNameByHandleW(object, buffer.data(),
			static_cast<DWORD>(buffer.size()), flags);
		if (written == 0 || written >= buffer.size()) return fail(GetLastError());
		std::wstring finalPath(buffer.data(), written);
		if (finalPath.size() >= 8 && _wcsnicmp(finalPath.c_str(), L"\\\\?\\UNC\\", 8) == 0)
			finalPath = L"\\\\" + finalPath.substr(8);
		else if (finalPath.size() >= 4
			&& _wcsnicmp(finalPath.c_str(), L"\\\\?\\", 4) == 0)
			finalPath.erase(0, 4);
		if (normalized(finalPath) != normalized(expectedPath)) return fail(ERROR_BAD_PATHNAME);
		if (identity != nullptr) *identity = currentIdentity;
		if (failureCode != nullptr) *failureCode = ERROR_SUCCESS;
		return true;
	}

	bool validateRegularFileNoReparseOrHardlink(const std::wstring& path)
	{
		// DesiredAccess zero basta para consultar estes metadados e nao conflita
		// com .agent.lock aberto pelo agente com FileShare.None. Solicitar
		// GENERIC_READ ou FILE_READ_ATTRIBUTES gerava uma falsa sharing violation
		// antes de o instalador ter oportunidade de encerrar o agente.
		HANDLE file = CreateFileW(path.c_str(), 0, FILE_SHARE_READ | FILE_SHARE_WRITE
			| FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (file == INVALID_HANDLE_VALUE) return false;
		const bool ok = validateOpenedFilesystemObject(file, path, false);
		CloseHandle(file);
		return ok;
	}

	struct PinnedDirectory
	{
		std::wstring path;
		HANDLE handle = INVALID_HANDLE_VALUE;
		FILE_ID_INFO identity{};
	};

	void closePinnedDirectories(std::vector<PinnedDirectory>& directories)
	{
		for (auto iterator = directories.rbegin(); iterator != directories.rend(); ++iterator)
			if (iterator->handle != INVALID_HANDLE_VALUE) CloseHandle(iterator->handle);
		directories.clear();
	}

	bool pinDirectoryChain(const std::wstring& target,
		std::vector<PinnedDirectory>& directories,
		const std::wstring* testOnlyTrustedRoot = nullptr)
	{
		closePinnedDirectories(directories);
		std::vector<wchar_t> full(32768), volume(32768);
		const DWORD fullLength = GetFullPathNameW(target.c_str(), static_cast<DWORD>(full.size()),
			full.data(), nullptr);
		if (fullLength == 0 || fullLength >= full.size()) return false;
		const std::wstring fullTarget = full.data();
		std::vector<std::wstring> paths;
		if (testOnlyTrustedRoot != nullptr)
		{
			std::vector<wchar_t> trustedFull(32768);
			const DWORD trustedLength = GetFullPathNameW(testOnlyTrustedRoot->c_str(),
				static_cast<DWORD>(trustedFull.size()), trustedFull.data(), nullptr);
			if (trustedLength == 0 || trustedLength >= trustedFull.size()
				|| normalized(trustedFull.data()) != normalized(parentOf(fullTarget))) return false;
			paths.push_back(trustedFull.data());
			paths.push_back(fullTarget);
		}
		else
		{
			// Producao preserva a defesa original: fixa todos os ancestrais desde a
			// raiz do volume. Somente o smoke nao elevado usa o ramo test-only acima.
			if (!GetVolumePathNameW(full.data(), volume.data(), static_cast<DWORD>(volume.size())))
				return false;
			std::wstring current = volume.data();
			while (current.size() > 3 && current.back() == L'\\') current.pop_back();
			paths.push_back(current);
			std::wstring remainder = fullTarget.substr(wcslen(volume.data()));
			size_t start = 0;
			while (start < remainder.size())
			{
				const size_t separator = remainder.find(L'\\', start);
				const std::wstring component = remainder.substr(start,
					separator == std::wstring::npos ? std::wstring::npos : separator - start);
				if (!component.empty())
				{
					current = join(current, component);
					paths.push_back(current);
				}
				if (separator == std::wstring::npos) break;
				start = separator + 1;
			}
		}
		paths.push_back(join(fullTarget, L".emulationstation"));
		paths.push_back(join(fullTarget, L".emulationstation\\pix"));
		std::set<std::wstring> seen;
		for (const auto& path : paths)
		{
			const std::wstring key = normalized(path);
			if (!seen.insert(key).second) continue;
			PinnedDirectory pinned;
			pinned.path = path;
			DWORD access = FILE_READ_ATTRIBUTES | FILE_TRAVERSE | FILE_LIST_DIRECTORY
				| READ_CONTROL;
			const DWORD share = (normalized(path) == normalized(target)
				|| normalized(path) == normalized(join(target, L".emulationstation\\pix")))
				? FILE_SHARE_READ | FILE_SHARE_WRITE : FILE_SHARE_READ;
			pinned.handle = CreateFileW(path.c_str(), access, share, nullptr, OPEN_EXISTING,
				FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			DWORD bindingError = ERROR_SUCCESS;
			if (pinned.handle == INVALID_HANDLE_VALUE
				|| !validateOpenedFilesystemObject(pinned.handle, path, true,
					&pinned.identity, &bindingError))
			{
				if (pinned.handle != INVALID_HANDLE_VALUE) CloseHandle(pinned.handle);
				closePinnedDirectories(directories);
				return false;
			}
			directories.push_back(std::move(pinned));
		}
		return true;
	}

	bool revalidatePinnedDirectories(const std::vector<PinnedDirectory>& directories)
	{
		if (directories.empty()) return false;
		for (const auto& pinned : directories)
		{
			FILE_ID_INFO current{};
			DWORD bindingError = ERROR_SUCCESS;
			if (pinned.handle == INVALID_HANDLE_VALUE
				|| !validateOpenedFilesystemObject(pinned.handle, pinned.path, true,
					&current, &bindingError)
				|| !sameFileIdentity(current, pinned.identity)) return false;
		}
		return true;
	}

	HANDLE pinnedDirectoryHandle(const std::vector<PinnedDirectory>& directories,
		const std::wstring& path)
	{
		const std::wstring expected = normalized(path);
		for (const auto& pinned : directories)
			if (normalized(pinned.path) == expected) return pinned.handle;
		return INVALID_HANDLE_VALUE;
	}

	bool validateDirectoryNoReparse(const std::wstring& path);

	enum class LayoutCandidateState
	{
		Missing,
		Available,
		Unsafe
	};

	enum class LayoutSelectionResult
	{
		Selected,
		Missing,
		Ambiguous,
		Unsafe
	};

	struct InstalledLayout
	{
		std::wstring wrapperExecutable;
		std::wstring target;
		std::wstring emulationStationExecutable;
	};

	LayoutSelectionResult selectLayoutState(LayoutCandidateState flat,
		LayoutCandidateState classic, bool& flatSelected)
	{
		flatSelected = false;
		if (flat == LayoutCandidateState::Unsafe || classic == LayoutCandidateState::Unsafe)
			return LayoutSelectionResult::Unsafe;
		const unsigned available = (flat == LayoutCandidateState::Available ? 1u : 0u)
			+ (classic == LayoutCandidateState::Available ? 1u : 0u);
		if (available == 0) return LayoutSelectionResult::Missing;
		if (available != 1) return LayoutSelectionResult::Ambiguous;
		flatSelected = flat == LayoutCandidateState::Available;
		return LayoutSelectionResult::Selected;
	}

	LayoutCandidateState inspectLayoutCandidate(const InstalledLayout& candidate)
	{
		bool queryUnsafe = false;
		auto attributes = [&](const std::wstring& path)
		{
			const DWORD value = GetFileAttributesW(path.c_str());
			if (value == INVALID_FILE_ATTRIBUTES)
			{
				const DWORD error = GetLastError();
				if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND)
					queryUnsafe = true;
			}
			return value;
		};
		const DWORD wrapperAttributes = attributes(candidate.wrapperExecutable);
		const DWORD targetAttributes = attributes(candidate.target);
		const DWORD frontendAttributes = attributes(candidate.emulationStationExecutable);
		if (queryUnsafe) return LayoutCandidateState::Unsafe;
		const unsigned present = (wrapperAttributes != INVALID_FILE_ATTRIBUTES ? 1u : 0u)
			+ (targetAttributes != INVALID_FILE_ATTRIBUTES ? 1u : 0u)
			+ (frontendAttributes != INVALID_FILE_ATTRIBUTES ? 1u : 0u);
		// Ausencia real significa nenhum vestigio do layout. Qualquer conjunto parcial
		// e inseguro: em especial, um wrapper flat existente jamais autoriza fallback
		// silencioso para o layout classico.
		if (present == 0) return LayoutCandidateState::Missing;
		if (present != 3) return LayoutCandidateState::Unsafe;
		return validateRegularFileNoReparseOrHardlink(candidate.wrapperExecutable)
			&& validateDirectoryNoReparse(candidate.target)
			&& validateRegularFileNoReparseOrHardlink(candidate.emulationStationExecutable)
			? LayoutCandidateState::Available : LayoutCandidateState::Unsafe;
	}

	LayoutSelectionResult selectProductionLayout(InstalledLayout& selected)
	{
		const InstalledLayout flat{
			L"D:\\TurboRama.exe", L"D:\\emulationstation",
			L"D:\\emulationstation\\emulationstation.exe"
		};
		const InstalledLayout classic{
			L"D:\\Turborama\\TurboRama.exe", L"D:\\Turborama\\emulationstation",
			L"D:\\Turborama\\emulationstation\\emulationstation.exe"
		};
		bool flatSelected = false;
		const LayoutSelectionResult result = selectLayoutState(inspectLayoutCandidate(flat),
			inspectLayoutCandidate(classic), flatSelected);
		if (result == LayoutSelectionResult::Selected) selected = flatSelected ? flat : classic;
		return result;
	}

	bool launcherFrontendMatchesLayout(bool present, const std::wstring& configured,
		const std::wstring& selectedWrapper)
	{
		if (!present || configured.empty()) return true;
		if (PathIsRelativeW(configured.c_str())) return false;
		const DWORD attributes = GetFileAttributesW(configured.c_str());
		if (attributes == INVALID_FILE_ATTRIBUTES)
		{
			const DWORD error = GetLastError();
			return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND;
		}
		return validateRegularFileNoReparseOrHardlink(configured)
			&& normalized(configured) == normalized(selectedWrapper);
	}

	HANDLE pinReadOnlyMaintenanceLock(const std::wstring& path)
	{
		if (path.empty() || PathIsRelativeW(path.c_str())) return INVALID_HANDLE_VALUE;
		HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
			OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (file == INVALID_HANDLE_VALUE) return INVALID_HANDLE_VALUE;
		if (!validateOpenedFilesystemObject(file, path, false))
		{
			CloseHandle(file);
			return INVALID_HANDLE_VALUE;
		}
		return file;
	}

	bool validateLayoutSelectionContract()
	{
		bool flat = false;
		return selectLayoutState(LayoutCandidateState::Available,
			LayoutCandidateState::Missing, flat) == LayoutSelectionResult::Selected && flat
			&& selectLayoutState(LayoutCandidateState::Missing,
				LayoutCandidateState::Available, flat) == LayoutSelectionResult::Selected && !flat
			&& selectLayoutState(LayoutCandidateState::Missing,
				LayoutCandidateState::Missing, flat) == LayoutSelectionResult::Missing
			&& selectLayoutState(LayoutCandidateState::Available,
				LayoutCandidateState::Available, flat) == LayoutSelectionResult::Ambiguous
			&& selectLayoutState(LayoutCandidateState::Unsafe,
				LayoutCandidateState::Missing, flat) == LayoutSelectionResult::Unsafe
			&& selectLayoutState(LayoutCandidateState::Unsafe,
				LayoutCandidateState::Available, flat) == LayoutSelectionResult::Unsafe
			&& selectLayoutState(LayoutCandidateState::Available,
				LayoutCandidateState::Unsafe, flat) == LayoutSelectionResult::Unsafe;
	}

	bool validateMetadataWithExclusiveLock()
	{
		wchar_t temporaryDirectory[MAX_PATH + 1]{};
		wchar_t temporaryFile[MAX_PATH + 1]{};
		if (GetTempPathW(MAX_PATH, temporaryDirectory) == 0
			|| GetTempFileNameW(temporaryDirectory, L"trm", 0, temporaryFile) == 0) return false;
		HANDLE lock = CreateFileW(temporaryFile, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
			OPEN_EXISTING, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr);
		if (lock == INVALID_HANDLE_VALUE)
		{
			DeleteFileW(temporaryFile);
			return false;
		}
		const bool ok = validateRegularFileNoReparseOrHardlink(temporaryFile);
		CloseHandle(lock);
		DeleteFileW(temporaryFile);
		return ok;
	}

	bool validateDirectoryNoReparse(const std::wstring& path)
	{
		HANDLE directory = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE
			| FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (directory == INVALID_HANDLE_VALUE) return false;
		const bool ok = validateOpenedFilesystemObject(directory, path, true);
		CloseHandle(directory);
		return ok;
	}

	bool validateTreeNoReparse(const std::wstring& directory)
	{
		if (!validateDirectoryNoReparse(directory)) return false;
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(directory, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE) return false;
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) { ok = false; break; }
			const std::wstring child = join(directory, name);
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
			{
				if (!validateTreeNoReparse(child)) { ok = false; break; }
			}
			else if (!validateRegularFileNoReparseOrHardlink(child)) { ok = false; break; }
		} while (FindNextFileW(search, &entry));
		const DWORD enumerationError = ok ? GetLastError() : ERROR_SUCCESS;
		FindClose(search);
		return ok && enumerationError == ERROR_NO_MORE_FILES;
	}

	const wchar_t* adminOnlyDescriptorText(bool directory)
	{
		return directory
			? L"O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)"
			: L"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)";
	}

	bool validateAdminOnlyDescriptorShape(bool directory)
	{
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(adminOnlyDescriptorText(directory),
			SDDL_REVISION_1, &descriptor, nullptr)) return false;
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
		const BYTE expectedFlags = directory ? OBJECT_INHERIT_ACE | CONTAINER_INHERIT_ACE : 0;
		if (ok)
		{
			for (DWORD index = 0; index < dacl->AceCount; ++index)
			{
				void* raw = nullptr;
				if (!GetAce(dacl, index, &raw)) { ok = false; break; }
				auto* header = static_cast<ACE_HEADER*>(raw);
				auto* ace = static_cast<ACCESS_ALLOWED_ACE*>(raw);
				if (header->AceType != ACCESS_ALLOWED_ACE_TYPE || header->AceFlags != expectedFlags
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

	bool applyAdminOnlySecurity(const std::wstring& path, bool directory)
	{
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(adminOnlyDescriptorText(directory), SDDL_REVISION_1,
			&descriptor, nullptr)) return false;
		PSID owner = nullptr;
		PACL dacl = nullptr;
		BOOL defaulted = FALSE, present = FALSE;
		bool ok = GetSecurityDescriptorOwner(descriptor, &owner, &defaulted) != FALSE
			&& GetSecurityDescriptorDacl(descriptor, &present, &dacl, &defaulted) != FALSE && present;

		auto openObject = [&path, directory](DWORD access) -> HANDLE {
			return CreateFileW(path.c_str(), access,
				FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
				nullptr, OPEN_EXISTING,
				(directory ? FILE_FLAG_BACKUP_SEMANTICS : 0) | FILE_FLAG_OPEN_REPARSE_POINT,
				nullptr);
		};

		HANDLE object = ok ? openObject(READ_CONTROL | WRITE_DAC) : INVALID_HANDLE_VALUE;
		if (object == INVALID_HANDLE_VALUE) ok = false;
		if (ok) ok = validateOpenedFilesystemObject(object, path, directory);

		PSECURITY_DESCRIPTOR currentDescriptor = nullptr;
		PSID currentOwner = nullptr;
		bool ownerAlreadyCorrect = false;
		if (ok)
		{
			const DWORD ownerResult = GetSecurityInfo(object, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION,
				&currentOwner, nullptr, nullptr, nullptr, &currentDescriptor);
			ok = ownerResult == ERROR_SUCCESS && currentOwner != nullptr;
			if (ok) ownerAlreadyCorrect = EqualSid(currentOwner, owner) != FALSE;
		}
		if (ok && !ownerAlreadyCorrect)
		{
			if (currentDescriptor != nullptr)
			{
				LocalFree(currentDescriptor);
				currentDescriptor = nullptr;
			}
			CloseHandle(object);
			object = openObject(READ_CONTROL | WRITE_DAC | WRITE_OWNER);
			if (object == INVALID_HANDLE_VALUE) ok = false;
			if (ok) ok = validateOpenedFilesystemObject(object, path, directory);
		}
		if (ok)
		{
			const SECURITY_INFORMATION information = DACL_SECURITY_INFORMATION
				| PROTECTED_DACL_SECURITY_INFORMATION
				| (ownerAlreadyCorrect ? 0 : OWNER_SECURITY_INFORMATION);
			ok = SetSecurityInfo(object, SE_FILE_OBJECT, information,
				ownerAlreadyCorrect ? nullptr : owner, nullptr, dacl, nullptr) == ERROR_SUCCESS
				&& validateAdminOnlyObject(object, directory);
		}
		if (currentDescriptor != nullptr) LocalFree(currentDescriptor);
		if (object != INVALID_HANDLE_VALUE) CloseHandle(object);
		LocalFree(descriptor);
		return ok;
	}

	struct SecurityFailure
	{
		std::wstring stage;
		std::wstring path;
		DWORD code = ERROR_SUCCESS;

		bool empty() const { return stage.empty(); }
	};

	void recordSecurityFailure(SecurityFailure* failure, const wchar_t* stage,
		const std::wstring& path, DWORD code)
	{
		if (failure == nullptr || !failure->empty()) return;
		failure->stage = stage != nullptr ? stage : L"etapa de seguranca desconhecida";
		failure->path = path;
		failure->code = code == ERROR_SUCCESS ? ERROR_GEN_FAILURE : code;
	}

	std::wstring securityFailureText(const SecurityFailure& failure)
	{
		if (failure.empty()) return L"etapa de seguranca nao identificada";
		std::wstring text = failure.stage;
		if (!failure.path.empty()) text += L"\nObjeto: " + failure.path;
		text += L"\nCodigo Windows/seguranca: " + std::to_wstring(failure.code);
		return text;
	}

	enum class KioskPermission
	{
		ReadExecute,
		ReadWrite,
		Modify
	};

	DWORD kioskAccessMask(KioskPermission permission)
	{
		if (permission == KioskPermission::ReadExecute)
			return FILE_GENERIC_READ | FILE_GENERIC_EXECUTE;
		if (permission == KioskPermission::ReadWrite)
			return FILE_GENERIC_READ | FILE_GENERIC_WRITE;
		return FILE_GENERIC_READ | FILE_GENERIC_WRITE | FILE_GENERIC_EXECUTE | DELETE;
	}

	std::wstring kioskDescriptorText(bool directory, const ResolvedIdentity& identity,
		KioskPermission permission, bool inheritable,
		const ResolvedIdentity* fullControlIdentity = nullptr,
		const ResolvedIdentity* ownerIdentity = nullptr,
		const ResolvedIdentity* auxiliaryAccessIdentity = nullptr)
	{
		if (identity.sidText.empty()
			|| (fullControlIdentity != nullptr && fullControlIdentity->sidText.empty())
			|| (ownerIdentity != nullptr && ownerIdentity->sidText.empty())
			|| (auxiliaryAccessIdentity != nullptr
				&& auxiliaryAccessIdentity->sidText.empty())) return L"";
		const std::wstring flags = directory && inheritable ? L"OICI" : L"";
		const std::wstring fullControlAuthority = fullControlIdentity != nullptr
			? fullControlIdentity->sidText : L"BA";
		const std::wstring ownerAuthority = ownerIdentity != nullptr
			? ownerIdentity->sidText : L"SY";
		wchar_t rights[16]{};
		// RX e M sao abreviacoes do icacls, nao tokens de direitos SDDL. A mascara
		// explicita mantem a representacao aceita por SDDL e validada abaixo.
		swprintf_s(rights, L"0x%08X", (unsigned)kioskAccessMask(permission));
		// O grupo primario nao participa da autorizacao deste objeto e nao e alterado
		// por SetSecurityInfo. Nao o invente no SDDL; em producao, o owner e SYSTEM.
		std::wstring descriptor = L"O:" + ownerAuthority + L"D:P(A;" + flags
			+ L";FA;;;SY)(A;" + flags + L";FA;;;" + fullControlAuthority + L")(A;" + flags
			+ L";" + rights + L";;;" + identity.sidText + L")";
		if (auxiliaryAccessIdentity != nullptr)
			descriptor += L"(A;" + flags + L";" + rights + L";;;"
				+ auxiliaryAccessIdentity->sidText + L")";
		return descriptor;
	}

	bool validateKioskDescriptorShape(KioskPermission permission, bool directory, bool inheritable)
	{
		ResolvedIdentity identity;
		identity.sidText = L"S-1-5-32-545"; // BUILTIN\\Users, somente para a forma do descritor.
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		const std::wstring text = kioskDescriptorText(directory, identity, permission, inheritable);
		if (text.empty() || !ConvertStringSecurityDescriptorToSecurityDescriptorW(text.c_str(),
			SDDL_REVISION_1, &descriptor, nullptr)) return false;
		PSID owner = nullptr;
		PACL dacl = nullptr;
		BOOL defaulted = FALSE, present = FALSE;
		BYTE admins[SECURITY_MAX_SID_SIZE]{}, system[SECURITY_MAX_SID_SIZE]{}, kiosk[SECURITY_MAX_SID_SIZE]{};
		DWORD adminsSize = sizeof(admins), systemSize = sizeof(system), kioskSize = sizeof(kiosk);
		bool ok = GetSecurityDescriptorOwner(descriptor, &owner, &defaulted) != FALSE
			&& GetSecurityDescriptorDacl(descriptor, &present, &dacl, &defaulted) != FALSE && present
			&& CreateWellKnownSid(WinBuiltinAdministratorsSid, nullptr, admins, &adminsSize) != FALSE
			&& CreateWellKnownSid(WinLocalSystemSid, nullptr, system, &systemSize) != FALSE
			&& CreateWellKnownSid(WinBuiltinUsersSid, nullptr, kiosk, &kioskSize) != FALSE
			&& owner != nullptr && EqualSid(owner, system) != FALSE && dacl != nullptr && dacl->AceCount == 3;
		bool adminFull = false, systemFull = false, kioskExpected = false;
		const BYTE expectedFlags = directory && inheritable ? OBJECT_INHERIT_ACE | CONTAINER_INHERIT_ACE : 0;
		if (ok)
		{
			for (DWORD index = 0; index < dacl->AceCount; ++index)
			{
				void* raw = nullptr;
				if (!GetAce(dacl, index, &raw)) { ok = false; break; }
				auto* header = static_cast<ACE_HEADER*>(raw);
				auto* ace = static_cast<ACCESS_ALLOWED_ACE*>(raw);
				if (header->AceType != ACCESS_ALLOWED_ACE_TYPE || header->AceFlags != expectedFlags)
				{ ok = false; break; }
				PSID sid = &ace->SidStart;
				if (EqualSid(sid, admins)) adminFull = ace->Mask == FILE_ALL_ACCESS;
				else if (EqualSid(sid, system)) systemFull = ace->Mask == FILE_ALL_ACCESS;
				else if (EqualSid(sid, kiosk)) kioskExpected = ace->Mask == kioskAccessMask(permission);
				else { ok = false; break; }
			}
		}
		SECURITY_DESCRIPTOR_CONTROL control = 0;
		DWORD revision = 0;
		ok = ok && adminFull && systemFull && kioskExpected
			&& GetSecurityDescriptorControl(descriptor, &control, &revision) != FALSE
			&& (control & SE_DACL_PROTECTED) != 0;
		LocalFree(descriptor);
		return ok;
	}

	bool validateKioskSecurity(HANDLE object, bool directory, const ResolvedIdentity& identity,
		KioskPermission permission, bool inheritable, const std::wstring& path,
		SecurityFailure* failure = nullptr, const ResolvedIdentity* fullControlIdentity = nullptr,
		const ResolvedIdentity* ownerIdentity = nullptr,
		const ResolvedIdentity* auxiliaryAccessIdentity = nullptr)
	{
		PSID owner = nullptr;
		PACL dacl = nullptr;
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		const DWORD securityResult = GetSecurityInfo(object, SE_FILE_OBJECT,
			OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION,
			&owner, nullptr, &dacl, nullptr, &descriptor);
		bool ok = securityResult == ERROR_SUCCESS;
		if (!ok) recordSecurityFailure(failure, L"confirmacao da DACL aplicada", path, securityResult);
		BYTE admins[SECURITY_MAX_SID_SIZE]{}, system[SECURITY_MAX_SID_SIZE]{};
		DWORD adminsSize = sizeof(admins), systemSize = sizeof(system);
		PSID fullControlSid = fullControlIdentity != nullptr
			? (PSID)fullControlIdentity->sid.data() : (PSID)admins;
		PSID expectedOwnerSid = nullptr;
		const bool fullControlSidReady = fullControlIdentity != nullptr
			? !fullControlIdentity->sid.empty() && IsValidSid(fullControlSid) != FALSE
			: CreateWellKnownSid(WinBuiltinAdministratorsSid, nullptr, admins, &adminsSize) != FALSE;
		const bool ownerSidReady = ownerIdentity == nullptr
			|| (!ownerIdentity->sid.empty()
				&& IsValidSid((PSID)ownerIdentity->sid.data()) != FALSE);
		if (ok && (!fullControlSidReady || !ownerSidReady
			|| !CreateWellKnownSid(WinLocalSystemSid, nullptr, system, &systemSize)))
		{
			recordSecurityFailure(failure, L"preparacao dos SIDs de validacao", path, GetLastError());
			ok = false;
		}
		if (ok) expectedOwnerSid = ownerIdentity != nullptr
			? (PSID)ownerIdentity->sid.data() : (PSID)system;
		const DWORD expectedAceCount = auxiliaryAccessIdentity != nullptr ? 4 : 3;
		if (ok && (owner == nullptr || dacl == nullptr || dacl->AceCount != expectedAceCount
			|| EqualSid(owner, expectedOwnerSid) == FALSE))
		{
			recordSecurityFailure(failure, L"forma inesperada da DACL aplicada", path,
				ERROR_INVALID_SECURITY_DESCR);
			ok = false;
		}
		bool adminFull = false, systemFull = false, kioskExpected = false;
		bool auxiliaryExpected = auxiliaryAccessIdentity == nullptr;
		const BYTE expectedFlags = directory && inheritable ? OBJECT_INHERIT_ACE | CONTAINER_INHERIT_ACE : 0;
		if (ok)
		{
			for (DWORD index = 0; index < dacl->AceCount; ++index)
			{
				void* raw = nullptr;
				if (!GetAce(dacl, index, &raw))
				{
					recordSecurityFailure(failure, L"leitura da DACL aplicada", path, GetLastError());
					ok = false;
					break;
				}
				auto* header = static_cast<ACE_HEADER*>(raw);
				auto* ace = static_cast<ACCESS_ALLOWED_ACE*>(raw);
				if (header->AceType != ACCESS_ALLOWED_ACE_TYPE || header->AceFlags != expectedFlags)
				{
					recordSecurityFailure(failure, L"flags inesperadas na DACL aplicada", path,
						ERROR_INVALID_ACL);
					ok = false;
					break;
				}
				PSID sid = &ace->SidStart;
				if (EqualSid(sid, fullControlSid)) adminFull = ace->Mask == FILE_ALL_ACCESS;
				else if (EqualSid(sid, system)) systemFull = ace->Mask == FILE_ALL_ACCESS;
				else if (EqualSid(sid, (PSID)identity.sid.data())) kioskExpected = ace->Mask == kioskAccessMask(permission);
				else if (auxiliaryAccessIdentity != nullptr
					&& EqualSid(sid, (PSID)auxiliaryAccessIdentity->sid.data()))
					auxiliaryExpected = ace->Mask == kioskAccessMask(permission);
				else
				{
					recordSecurityFailure(failure, L"identidade inesperada na DACL aplicada", path,
						ERROR_INVALID_ACL);
					ok = false;
					break;
				}
			}
		}
		SECURITY_DESCRIPTOR_CONTROL control = 0;
		DWORD revision = 0;
		if (ok && (!GetSecurityDescriptorControl(descriptor, &control, &revision)
			|| (control & SE_DACL_PROTECTED) == 0 || !adminFull || !systemFull
			|| !kioskExpected || !auxiliaryExpected))
		{
			recordSecurityFailure(failure, L"confirmacao final da DACL aplicada", path,
				GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_ACL : GetLastError());
			ok = false;
		}
		if (descriptor) LocalFree(descriptor);
		return ok;
	}

	bool openSecurityObject(const std::wstring& path, bool directory, DWORD access,
		const FILE_ID_INFO* expectedFileIdentity, HANDLE& object, SecurityFailure* failure,
		const wchar_t* stage)
	{
		object = CreateFileW(path.c_str(), access,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (object == INVALID_HANDLE_VALUE)
		{
			recordSecurityFailure(failure, stage, path, GetLastError());
			return false;
		}
		FILE_ID_INFO openedIdentity{};
		DWORD bindingError = ERROR_SUCCESS;
		if (!validateOpenedFilesystemObject(object, path, directory, &openedIdentity,
			&bindingError))
		{
			recordSecurityFailure(failure, stage, path, bindingError);
			CloseHandle(object);
			object = INVALID_HANDLE_VALUE;
			return false;
		}
		if (expectedFileIdentity != nullptr
			&& !sameFileIdentity(openedIdentity, *expectedFileIdentity))
		{
			recordSecurityFailure(failure, stage, path, ERROR_FILE_INVALID);
			CloseHandle(object);
			object = INVALID_HANDLE_VALUE;
			return false;
		}
		return true;
	}

	bool applyKioskSecurity(const std::wstring& path, bool directory, const ResolvedIdentity& identity,
		KioskPermission permission, bool inheritable, SecurityFailure* failure = nullptr,
		const ResolvedIdentity* fullControlIdentity = nullptr,
		const ResolvedIdentity* ownerIdentity = nullptr,
		const ResolvedIdentity* auxiliaryAccessIdentity = nullptr,
		const FILE_ID_INFO* expectedFileIdentity = nullptr)
	{
		if (identity.sid.empty() || identity.sidText.empty()
			|| (fullControlIdentity != nullptr && (fullControlIdentity->sid.empty()
				|| fullControlIdentity->sidText.empty()
				|| EqualSid((PSID)identity.sid.data(),
					(PSID)fullControlIdentity->sid.data()) != FALSE))
			|| (ownerIdentity != nullptr && (ownerIdentity->sid.empty()
				|| ownerIdentity->sidText.empty()
				|| IsValidSid((PSID)ownerIdentity->sid.data()) == FALSE))
			|| (auxiliaryAccessIdentity != nullptr
				&& (auxiliaryAccessIdentity->sid.empty()
					|| auxiliaryAccessIdentity->sidText.empty()
					|| IsValidSid((PSID)auxiliaryAccessIdentity->sid.data()) == FALSE
					|| EqualSid((PSID)identity.sid.data(),
						(PSID)auxiliaryAccessIdentity->sid.data()) != FALSE)))
		{
			recordSecurityFailure(failure, L"validacao do SID do quiosque", path, ERROR_INVALID_SID);
			return false;
		}
		const std::wstring sddl = kioskDescriptorText(directory, identity, permission, inheritable,
			fullControlIdentity, ownerIdentity, auxiliaryAccessIdentity);
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1,
			&descriptor, nullptr))
		{
			recordSecurityFailure(failure, L"conversao do descritor do quiosque", path, GetLastError());
			return false;
		}
		PSID owner = nullptr;
		PACL dacl = nullptr;
		BOOL defaulted = FALSE, present = FALSE;
		bool ok = GetSecurityDescriptorOwner(descriptor, &owner, &defaulted) != FALSE
			&& GetSecurityDescriptorDacl(descriptor, &present, &dacl, &defaulted) != FALSE && present;
		if (!ok) recordSecurityFailure(failure, L"leitura do descritor do quiosque", path, GetLastError());
		// Comece apenas com os direitos realmente necessarios para ler owner e trocar
		// DACL. WRITE_OWNER so e pedido num segundo open se o owner precisar mudar.
		HANDLE object = INVALID_HANDLE_VALUE;
		if (ok) ok = openSecurityObject(path, directory, READ_CONTROL | WRITE_DAC,
			expectedFileIdentity, object, failure, L"abertura minima para aplicar DACL");
		PSECURITY_DESCRIPTOR currentDescriptor = nullptr;
		PSID currentOwner = nullptr;
		bool ownerAlreadyCorrect = false;
		auto readCurrentOwner = [&]()
		{
			const DWORD ownerResult = GetSecurityInfo(object, SE_FILE_OBJECT,
				OWNER_SECURITY_INFORMATION, &currentOwner, nullptr, nullptr, nullptr,
				&currentDescriptor);
			if (ownerResult != ERROR_SUCCESS || currentOwner == nullptr)
			{
				recordSecurityFailure(failure, L"leitura do owner antes da DACL", path,
					ownerResult == ERROR_SUCCESS ? ERROR_INVALID_OWNER : ownerResult);
				return false;
			}
			ownerAlreadyCorrect = EqualSid(currentOwner, owner) != FALSE;
			return true;
		};
		if (ok) ok = readCurrentOwner();
		if (ok && !ownerAlreadyCorrect)
		{
			if (currentDescriptor) LocalFree(currentDescriptor);
			currentDescriptor = nullptr;
			currentOwner = nullptr;
			CloseHandle(object);
			object = INVALID_HANDLE_VALUE;
			ok = openSecurityObject(path, directory,
				READ_CONTROL | WRITE_DAC | WRITE_OWNER, expectedFileIdentity, object,
				failure, L"reabertura para trocar owner e aplicar DACL");
			if (ok) ok = readCurrentOwner();
		}
		if (ok)
		{
			SECURITY_INFORMATION information = DACL_SECURITY_INFORMATION
				| PROTECTED_DACL_SECURITY_INFORMATION;
			if (!ownerAlreadyCorrect) information |= OWNER_SECURITY_INFORMATION;
			const DWORD securityResult = SetSecurityInfo(object, SE_FILE_OBJECT, information,
				ownerAlreadyCorrect ? nullptr : owner, nullptr, dacl, nullptr);
			if (securityResult != ERROR_SUCCESS)
			{
				recordSecurityFailure(failure, L"aplicacao da DACL do quiosque", path, securityResult);
				ok = false;
			}
		}
		if (ok) ok = validateKioskSecurity(object, directory, identity, permission, inheritable,
			path, failure, fullControlIdentity, ownerIdentity, auxiliaryAccessIdentity);
		if (currentDescriptor) LocalFree(currentDescriptor);
		if (object != INVALID_HANDLE_VALUE) CloseHandle(object);
		LocalFree(descriptor);
		return ok;
	}

	struct SecurityBackup
	{
		std::wstring path;
		bool directory = false;
		bool daclProtected = false;
		FILE_ID_INFO fileIdentity{};
		std::vector<unsigned char> descriptor;
	};

	const SecurityBackup* findSecurityBackup(const std::vector<SecurityBackup>& backups,
		const std::wstring& path, bool directory)
	{
		for (const auto& backup : backups)
			if (backup.directory == directory
				&& _wcsicmp(backup.path.c_str(), path.c_str()) == 0) return &backup;
		return nullptr;
	}

	bool validateSecurityBackupIdentity(const SecurityBackup& backup,
		SecurityFailure* failure, const wchar_t* stage)
	{
		HANDLE object = CreateFileW(backup.path.c_str(), 0,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (object == INVALID_HANDLE_VALUE)
		{
			recordSecurityFailure(failure, stage, backup.path, GetLastError());
			return false;
		}
		FILE_ID_INFO identity{};
		DWORD bindingError = ERROR_SUCCESS;
		const bool valid = validateOpenedFilesystemObject(object, backup.path, backup.directory,
			&identity, &bindingError) && sameFileIdentity(identity, backup.fileIdentity);
		CloseHandle(object);
		if (!valid)
			recordSecurityFailure(failure, stage, backup.path,
				bindingError == ERROR_SUCCESS ? ERROR_FILE_INVALID : bindingError);
		return valid;
	}

	bool rebindSecurityBackupIdentity(std::vector<SecurityBackup>& backups,
		const std::wstring& path, bool directory, SecurityFailure* failure)
	{
		SecurityBackup* selected = nullptr;
		for (auto& backup : backups)
			if (backup.directory == directory
				&& _wcsicmp(backup.path.c_str(), path.c_str()) == 0)
			{
				selected = &backup;
				break;
			}
		if (selected == nullptr)
		{
			recordSecurityFailure(failure, L"rebind da ACL: objeto fora do snapshot", path,
				ERROR_FILE_INVALID);
			return false;
		}
		HANDLE object = CreateFileW(path.c_str(), 0,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (object == INVALID_HANDLE_VALUE)
		{
			recordSecurityFailure(failure, L"rebind da ACL: abertura", path, GetLastError());
			return false;
		}
		FILE_ID_INFO identity{};
		DWORD bindingError = ERROR_SUCCESS;
		const bool valid = validateOpenedFilesystemObject(object, path, directory, &identity,
			&bindingError);
		CloseHandle(object);
		if (!valid)
		{
			recordSecurityFailure(failure, L"rebind da ACL: vinculo caminho/objeto", path,
				bindingError);
			return false;
		}
		selected->fileIdentity = identity;
		return true;
	}

	bool captureSecurityBackupFromHandle(HANDLE object, const std::wstring& path,
		bool directory, SecurityBackup& backup, SecurityFailure* failure = nullptr)
	{
		FILE_ID_INFO openedIdentity{};
		PSID owner = nullptr, group = nullptr;
		PACL dacl = nullptr;
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		SECURITY_DESCRIPTOR_CONTROL control = 0;
		DWORD revision = 0;
		DWORD bindingError = ERROR_SUCCESS;
		bool ok = validateOpenedFilesystemObject(object, path, directory, &openedIdentity,
			&bindingError);
		if (!ok)
		{
			recordSecurityFailure(failure, L"captura da ACL: vinculo caminho/objeto", path,
				bindingError);
		}
		DWORD securityResult = ERROR_SUCCESS;
		if (ok)
		{
			securityResult = GetSecurityInfo(object, SE_FILE_OBJECT,
				OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION
					| DACL_SECURITY_INFORMATION,
				&owner, &group, &dacl, nullptr, &descriptor);
			if (securityResult != ERROR_SUCCESS)
			{
				recordSecurityFailure(failure, L"captura da ACL: leitura do descritor", path,
					securityResult);
				ok = false;
			}
		}
		if (ok && (owner == nullptr || group == nullptr || dacl == nullptr))
		{
			recordSecurityFailure(failure, L"captura da ACL: descritor incompleto", path,
				ERROR_INVALID_SECURITY_DESCR);
			ok = false;
		}
		if (ok && !GetSecurityDescriptorControl(descriptor, &control, &revision))
		{
			recordSecurityFailure(failure, L"captura da ACL: controle do descritor", path,
				GetLastError());
			ok = false;
		}
		if (ok)
		{
			backup = {};
			backup.path = path;
			backup.directory = directory;
			backup.daclProtected = (control & SE_DACL_PROTECTED) != 0;
			backup.fileIdentity = openedIdentity;
			const DWORD length = GetSecurityDescriptorLength(descriptor);
			if (length == 0)
			{
				recordSecurityFailure(failure, L"captura da ACL: tamanho do descritor", path,
					ERROR_INVALID_SECURITY_DESCR);
				ok = false;
			}
			else
			{
				backup.descriptor.resize(length);
				CopyMemory(backup.descriptor.data(), descriptor, length);
			}
		}
		if (descriptor) LocalFree(descriptor);
		return ok;
	}

	bool captureSecurityBackup(const std::wstring& path, bool directory,
		std::vector<SecurityBackup>& backups, SecurityFailure* failure = nullptr)
	{
		HANDLE object = CreateFileW(path.c_str(), READ_CONTROL,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (object == INVALID_HANDLE_VALUE)
		{
			recordSecurityFailure(failure, L"captura da ACL: abertura", path, GetLastError());
			return false;
		}
		SecurityBackup backup;
		const bool ok = captureSecurityBackupFromHandle(object, path, directory, backup, failure);
		CloseHandle(object);
		if (ok) backups.push_back(std::move(backup));
		return ok;
	}

	bool captureSecurityTree(const std::wstring& path, bool directory,
		std::vector<SecurityBackup>& backups, SecurityFailure* failure = nullptr)
	{
		if (directory)
		{
			if (!validateTreeNoReparse(path))
			{
				recordSecurityFailure(failure, L"captura da arvore: validacao", path,
					GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_DATA : GetLastError());
				return false;
			}
			if (!captureSecurityBackup(path, true, backups, failure)) return false;
			WIN32_FIND_DATAW entry{};
			HANDLE search = FindFirstFileW(join(path, L"*").c_str(), &entry);
			if (search == INVALID_HANDLE_VALUE)
			{
				recordSecurityFailure(failure, L"captura da arvore: enumeracao", path, GetLastError());
				return false;
			}
			bool ok = true;
			do
			{
				const std::wstring name = entry.cFileName;
				if (name == L"." || name == L"..") continue;
				const bool childDirectory = (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
				if (!captureSecurityTree(join(path, name), childDirectory, backups, failure))
				{
					ok = false;
					break;
				}
			} while (FindNextFileW(search, &entry));
			const DWORD enumerationError = ok ? GetLastError() : ERROR_SUCCESS;
			FindClose(search);
			if (ok && enumerationError != ERROR_NO_MORE_FILES)
			{
				recordSecurityFailure(failure, L"captura da arvore: enumeracao interrompida",
					path, enumerationError);
				ok = false;
			}
			return ok;
		}
		if (!validateRegularFileNoReparseOrHardlink(path))
		{
			recordSecurityFailure(failure, L"captura da arvore: validacao do arquivo", path,
				GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_DATA : GetLastError());
			return false;
		}
		return captureSecurityBackup(path, false, backups, failure);
	}

	bool securityBackupMatchesObject(HANDLE object, const SecurityBackup& backup,
		SecurityFailure* failure)
	{
		PSECURITY_DESCRIPTOR expectedDescriptor =
			(PSECURITY_DESCRIPTOR)backup.descriptor.data();
		PSID expectedOwner = nullptr, expectedGroup = nullptr;
		PSID actualOwner = nullptr, actualGroup = nullptr;
		PACL expectedDacl = nullptr, actualDacl = nullptr;
		BOOL defaulted = FALSE, expectedPresent = FALSE, actualPresent = FALSE;
		if (backup.descriptor.empty()
			|| !GetSecurityDescriptorOwner(expectedDescriptor, &expectedOwner, &defaulted)
			|| !GetSecurityDescriptorGroup(expectedDescriptor, &expectedGroup, &defaulted)
			|| !GetSecurityDescriptorDacl(expectedDescriptor, &expectedPresent, &expectedDacl,
				&defaulted) || !expectedPresent || expectedOwner == nullptr
			|| expectedGroup == nullptr || expectedDacl == nullptr)
		{
			recordSecurityFailure(failure, L"rollback da ACL: comparacao do backup salvo",
				backup.path, ERROR_INVALID_SECURITY_DESCR);
			return false;
		}
		PSECURITY_DESCRIPTOR actualDescriptor = nullptr;
		const DWORD result = GetSecurityInfo(object, SE_FILE_OBJECT,
			OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION,
			&actualOwner, &actualGroup,
			&actualDacl, nullptr, &actualDescriptor);
		if (result != ERROR_SUCCESS)
		{
			recordSecurityFailure(failure, L"rollback da ACL: leitura de confirmacao",
				backup.path, result);
			return false;
		}
		SECURITY_DESCRIPTOR_CONTROL control = 0;
		DWORD revision = 0;
		const bool matches = actualOwner != nullptr && actualGroup != nullptr && actualDacl != nullptr
			&& EqualSid(actualOwner, expectedOwner) != FALSE
			&& EqualSid(actualGroup, expectedGroup) != FALSE
			&& actualDacl->AclSize == expectedDacl->AclSize
			&& memcmp(actualDacl, expectedDacl, expectedDacl->AclSize) == 0
			&& GetSecurityDescriptorDacl(actualDescriptor, &actualPresent, &actualDacl,
				&defaulted) != FALSE && actualPresent
			&& GetSecurityDescriptorControl(actualDescriptor, &control, &revision) != FALSE
			&& (((control & SE_DACL_PROTECTED) != 0) == backup.daclProtected);
		if (!matches)
			recordSecurityFailure(failure, L"rollback da ACL: descritor restaurado divergiu",
				backup.path, ERROR_INVALID_ACL);
		if (actualDescriptor) LocalFree(actualDescriptor);
		return matches;
	}

	bool explicitAcesOnly(PACL source, std::vector<unsigned char>& storage, PACL& result)
	{
		result = nullptr;
		if (source == nullptr || !IsValidAcl(source))
		{
			SetLastError(ERROR_INVALID_ACL);
			return false;
		}
		DWORD size = sizeof(ACL);
		for (DWORD index = 0; index < source->AceCount; ++index)
		{
			void* raw = nullptr;
			if (!GetAce(source, index, &raw) || raw == nullptr)
			{
				if (GetLastError() == ERROR_SUCCESS) SetLastError(ERROR_INVALID_ACL);
				return false;
			}
			auto* header = static_cast<ACE_HEADER*>(raw);
			if ((header->AceFlags & INHERITED_ACE) == 0)
			{
				if (header->AceSize < sizeof(ACE_HEADER)
					|| size > static_cast<DWORD>(MAXWORD) - header->AceSize)
				{
					SetLastError(ERROR_INVALID_ACL);
					return false;
				}
				size += header->AceSize;
			}
		}
		storage.assign(size, 0);
		result = reinterpret_cast<PACL>(storage.data());
		if (!InitializeAcl(result, size, source->AclRevision)) return false;
		for (DWORD index = 0; index < source->AceCount; ++index)
		{
			void* raw = nullptr;
			if (!GetAce(source, index, &raw) || raw == nullptr)
			{
				if (GetLastError() == ERROR_SUCCESS) SetLastError(ERROR_INVALID_ACL);
				return false;
			}
			auto* header = static_cast<ACE_HEADER*>(raw);
			if ((header->AceFlags & INHERITED_ACE) == 0
				&& !AddAce(result, source->AclRevision, MAXDWORD, raw, header->AceSize)) return false;
		}
		if (IsValidAcl(result) == FALSE)
		{
			SetLastError(ERROR_INVALID_ACL);
			return false;
		}
		return true;
	}

	bool restoreSecurityBackup(const SecurityBackup& backup, SecurityFailure* failure = nullptr)
	{
		if (backup.descriptor.empty())
		{
			recordSecurityFailure(failure, L"rollback da ACL: backup vazio", backup.path,
				ERROR_INVALID_SECURITY_DESCR);
			return false;
		}
		PSID owner = nullptr, group = nullptr;
		PACL dacl = nullptr;
		BOOL defaulted = FALSE, present = FALSE;
		PSECURITY_DESCRIPTOR descriptor = (PSECURITY_DESCRIPTOR)backup.descriptor.data();
		bool ok = GetSecurityDescriptorOwner(descriptor, &owner, &defaulted) != FALSE
			&& GetSecurityDescriptorGroup(descriptor, &group, &defaulted) != FALSE
			&& GetSecurityDescriptorDacl(descriptor, &present, &dacl, &defaulted) != FALSE
			&& present;
		if (!ok || owner == nullptr || group == nullptr || dacl == nullptr)
		{
			recordSecurityFailure(failure, L"rollback da ACL: descritor salvo invalido", backup.path,
				GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_SECURITY_DESCR : GetLastError());
			return false;
		}
		std::vector<unsigned char> explicitAclStorage;
		PACL restoreDacl = dacl;
		if (!backup.daclProtected
			&& !explicitAcesOnly(dacl, explicitAclStorage, restoreDacl))
		{
			recordSecurityFailure(failure, L"rollback da ACL: filtro de ACEs explicitas",
				backup.path, GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_ACL : GetLastError());
			return false;
		}

		// Comece apenas com os direitos necessarios para ler o descritor e restaurar a
		// DACL. WRITE_OWNER so e pedido se owner ou grupo realmente divergirem.
		HANDLE object = INVALID_HANDLE_VALUE;
		ok = openSecurityObject(backup.path, backup.directory, READ_CONTROL | WRITE_DAC,
			&backup.fileIdentity, object, failure, L"rollback da ACL: abertura minima");
		PSECURITY_DESCRIPTOR currentDescriptor = nullptr;
		PSID currentOwner = nullptr, currentGroup = nullptr;
		bool ownerAlreadyCorrect = false, groupAlreadyCorrect = false;
		auto readCurrentOwnerAndGroup = [&]()
		{
			const DWORD ownerResult = GetSecurityInfo(object, SE_FILE_OBJECT,
				OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION,
				&currentOwner, &currentGroup, nullptr, nullptr,
				&currentDescriptor);
			if (ownerResult != ERROR_SUCCESS || currentOwner == nullptr || currentGroup == nullptr)
			{
				recordSecurityFailure(failure, L"rollback da ACL: leitura do owner atual",
					backup.path, ownerResult == ERROR_SUCCESS ? ERROR_INVALID_OWNER : ownerResult);
				return false;
			}
			ownerAlreadyCorrect = EqualSid(currentOwner, owner) != FALSE;
			groupAlreadyCorrect = EqualSid(currentGroup, group) != FALSE;
			return true;
		};
		if (ok) ok = readCurrentOwnerAndGroup();
		if (ok && (!ownerAlreadyCorrect || !groupAlreadyCorrect))
		{
			if (currentDescriptor) LocalFree(currentDescriptor);
			currentDescriptor = nullptr;
			currentOwner = nullptr;
			currentGroup = nullptr;
			CloseHandle(object);
			object = INVALID_HANDLE_VALUE;
			ok = openSecurityObject(backup.path, backup.directory,
				READ_CONTROL | WRITE_DAC | WRITE_OWNER, &backup.fileIdentity, object,
				failure, L"rollback da ACL: reabertura para owner/grupo");
			if (ok) ok = readCurrentOwnerAndGroup();
		}
		SECURITY_INFORMATION information = DACL_SECURITY_INFORMATION
			| (backup.daclProtected ? PROTECTED_DACL_SECURITY_INFORMATION
				: UNPROTECTED_DACL_SECURITY_INFORMATION);
		if (!ownerAlreadyCorrect) information |= OWNER_SECURITY_INFORMATION;
		if (!groupAlreadyCorrect) information |= GROUP_SECURITY_INFORMATION;
		if (ok)
		{
			const DWORD securityResult = SetSecurityInfo(object, SE_FILE_OBJECT, information,
				ownerAlreadyCorrect ? nullptr : owner,
				groupAlreadyCorrect ? nullptr : group, restoreDacl, nullptr);
			if (securityResult != ERROR_SUCCESS)
			{
				recordSecurityFailure(failure, L"rollback da ACL: restauracao", backup.path,
					securityResult);
				ok = false;
			}
		}
		if (ok) ok = securityBackupMatchesObject(object, backup, failure);
		if (currentDescriptor) LocalFree(currentDescriptor);
		if (object != INVALID_HANDLE_VALUE) CloseHandle(object);
		return ok;
	}

	bool restoreSecurityBackups(const std::vector<SecurityBackup>& backups,
		SecurityFailure* failure = nullptr)
	{
		bool ok = true;
		// A captura e feita pai->filho. Restaurar nessa mesma ordem e essencial para
		// DACLs nao protegidas: o pai original precisa existir antes que o filho volte
		// a herdar, ou o Windows materializa ACEs do pai ainda endurecido.
		for (const auto& item : backups)
			if (!restoreSecurityBackup(item, failure)) ok = false;
		return ok;
	}

	bool applyKioskSecurityTree(const std::wstring& path, bool directory, const ResolvedIdentity& identity,
		KioskPermission permission, bool inheritable, SecurityFailure* failure = nullptr,
		const ResolvedIdentity* fullControlIdentity = nullptr,
		const ResolvedIdentity* ownerIdentity = nullptr,
		const ResolvedIdentity* auxiliaryAccessIdentity = nullptr,
		const std::vector<SecurityBackup>* expectedBackups = nullptr,
		const std::wstring* extraExpectedPath = nullptr,
		const FILE_ID_INFO* extraExpectedIdentity = nullptr,
		const std::wstring* selfTestFailurePath = nullptr)
	{
		const FILE_ID_INFO* expectedIdentity = nullptr;
		if (expectedBackups != nullptr)
		{
			const SecurityBackup* backup = findSecurityBackup(*expectedBackups, path, directory);
			if (backup != nullptr) expectedIdentity = &backup->fileIdentity;
			else if (extraExpectedPath != nullptr && extraExpectedIdentity != nullptr
				&& directory && _wcsicmp(extraExpectedPath->c_str(), path.c_str()) == 0)
				expectedIdentity = extraExpectedIdentity;
			else
			{
				recordSecurityFailure(failure, L"aplicacao da DACL: objeto fora do snapshot",
					path, ERROR_FILE_INVALID);
				return false;
			}
		}
		if (!directory)
		{
			if (selfTestFailurePath != nullptr
				&& _wcsicmp(selfTestFailurePath->c_str(), path.c_str()) == 0)
			{
				recordSecurityFailure(failure, L"aplicacao da DACL: falha injetada no no",
					path, ERROR_CANCELLED);
				return false;
			}
			return applyKioskSecurity(path, false, identity, permission, inheritable, failure,
				fullControlIdentity, ownerIdentity, auxiliaryAccessIdentity, expectedIdentity);
		}

		// Confirme a identidade da raiz antes de enumerar. A aplicacao efetiva ocorre
		// somente depois dos filhos, evitando que uma DACL herdavel do pai altere o
		// descritor de um descendente antes que ele seja validado contra o snapshot.
		HANDLE validationHandle = INVALID_HANDLE_VALUE;
		if (!openSecurityObject(path, true, 0, expectedIdentity, validationHandle, failure,
			L"aplicacao da DACL: validacao pre-enumeracao")) return false;
		CloseHandle(validationHandle);
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(path, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE)
		{
			recordSecurityFailure(failure, L"aplicacao da DACL: enumeracao", path, GetLastError());
			return false;
		}
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			const std::wstring child = join(path, name);
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
			{
				recordSecurityFailure(failure, L"aplicacao da DACL: reparse point", child,
					ERROR_REPARSE_TAG_INVALID);
				ok = false;
				break;
			}
			const bool childDirectory = (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
			if (!childDirectory && !validateRegularFileNoReparseOrHardlink(child))
			{
				recordSecurityFailure(failure, L"aplicacao da DACL: validacao do arquivo", child,
					GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_DATA : GetLastError());
				ok = false;
				break;
			}
			if (!applyKioskSecurityTree(child, childDirectory, identity, permission, inheritable,
				failure, fullControlIdentity, ownerIdentity, auxiliaryAccessIdentity,
				expectedBackups, extraExpectedPath, extraExpectedIdentity, selfTestFailurePath))
			{
				ok = false;
				break;
			}
		} while (FindNextFileW(search, &entry));
		const DWORD enumerationError = ok ? GetLastError() : ERROR_SUCCESS;
		FindClose(search);
		if (ok && enumerationError != ERROR_NO_MORE_FILES)
		{
			recordSecurityFailure(failure, L"aplicacao da DACL: enumeracao interrompida", path,
				enumerationError);
			ok = false;
		}
		if (!ok) return false;
		if (selfTestFailurePath != nullptr
			&& _wcsicmp(selfTestFailurePath->c_str(), path.c_str()) == 0)
		{
			recordSecurityFailure(failure, L"aplicacao da DACL: falha injetada no no", path,
				ERROR_CANCELLED);
			return false;
		}
		return applyKioskSecurity(path, true, identity, permission, inheritable, failure,
			fullControlIdentity, ownerIdentity, auxiliaryAccessIdentity, expectedIdentity);
	}

	bool secureStagedTree(const std::wstring& directory)
	{
		if (!applyAdminOnlySecurity(directory, true)) return false;
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(directory, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE) return false;
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			const std::wstring child = join(directory, name);
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) { ok = false; break; }
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
				ok = secureStagedTree(child);
			else ok = applyAdminOnlySecurity(child, false);
			if (!ok) break;
		} while (FindNextFileW(search, &entry));
		FindClose(search);
		return ok;
	}

	bool removeTree(const std::wstring& directory)
	{
		if (!directoryExists(directory)) return true;
		WIN32_FIND_DATAW entry{};
		const std::wstring pattern = join(directory, L"*");
		HANDLE search = FindFirstFileW(pattern.c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE) return GetLastError() == ERROR_FILE_NOT_FOUND;
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			const std::wstring child = join(directory, name);
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
			{
				if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
				{
					SetFileAttributesW(child.c_str(), FILE_ATTRIBUTE_NORMAL);
					if (!RemoveDirectoryW(child.c_str())) ok = false;
				}
				else if (!removeTree(child)) ok = false;
			}
			else
			{
				SetFileAttributesW(child.c_str(), FILE_ATTRIBUTE_NORMAL);
				if (!DeleteFileW(child.c_str()) && GetLastError() != ERROR_FILE_NOT_FOUND) ok = false;
			}
		} while (FindNextFileW(search, &entry));
		FindClose(search);
		SetFileAttributesW(directory.c_str(), FILE_ATTRIBUTE_NORMAL);
		if (!RemoveDirectoryW(directory.c_str()) && GetLastError() != ERROR_PATH_NOT_FOUND) ok = false;
		return ok;
	}

	bool writeUtf8FilePreservingObject(const std::wstring& destination,
		const std::wstring& text);

	bool stopExactProcessAndConfirm(const std::wstring& expectedPath, bool strictProcessInspection)
	{
		// Producao sempre usa inspecao estrita. O unico chamador nao estrito e o
		// smoke isolado e nao elevado, que pode coexistir com processos reais de
		// mesmo nome em outra sessao sem permissao para consultar o caminho deles.
		const std::wstring expected = normalized(expectedPath);
		const std::wstring expectedName = expected.substr(expected.find_last_of(L'\\') + 1);
		for (unsigned pass = 0; pass < 3; ++pass)
		{
			HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
			if (snapshot == INVALID_HANDLE_VALUE) return false;
			bool ok = true, found = false;
			PROCESSENTRY32W entry{};
			entry.dwSize = sizeof(entry);
			BOOL hasEntry = Process32FirstW(snapshot, &entry);
			if (!hasEntry && GetLastError() != ERROR_NO_MORE_FILES) ok = false;
			while (ok && hasEntry)
			{
				if (entry.th32ProcessID != GetCurrentProcessId()
					&& _wcsicmp(entry.szExeFile, expectedName.c_str()) == 0)
				{
					// Primeiro consulta com o direito minimo. Pedir PROCESS_TERMINATE para
					// todo processo de mesmo nome fazia o smoke falhar quando havia outro
					// configurador/dotnet elevado fora da instalacao alvo.
					HANDLE query = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE,
						entry.th32ProcessID);
					if (query == nullptr)
					{
						const DWORD queryError = GetLastError();
						if (queryError != ERROR_INVALID_PARAMETER && strictProcessInspection) ok = false;
					}
					else
					{
						std::vector<wchar_t> image(32768);
						DWORD length = static_cast<DWORD>(image.size());
						const bool queried = QueryFullProcessImageNameW(query, 0, image.data(), &length) != FALSE;
						DWORD observedExit = STILL_ACTIVE;
						const bool alreadyExited = !queried && GetExitCodeProcess(query, &observedExit) != FALSE
							&& observedExit != STILL_ACTIVE;
						CloseHandle(query);
						if (!queried)
						{
							if (!alreadyExited && strictProcessInspection) ok = false;
						}
						else if (normalized(image.data()) == expected)
						{
							// O alvo ja foi identificado. A partir daqui qualquer falha permanece
							// fechada ate no smoke; somente a corrida de saida natural e aceita.
							HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE
								| SYNCHRONIZE, FALSE, entry.th32ProcessID);
							if (process == nullptr)
							{
								if (GetLastError() != ERROR_INVALID_PARAMETER) ok = false;
							}
							else
							{
								std::vector<wchar_t> confirmedImage(32768);
								DWORD confirmedLength = static_cast<DWORD>(confirmedImage.size());
								const bool confirmedQuery = QueryFullProcessImageNameW(process, 0, confirmedImage.data(),
									&confirmedLength) != FALSE;
								DWORD confirmedExit = STILL_ACTIVE;
								const bool exitedDuringConfirm = !confirmedQuery
									&& GetExitCodeProcess(process, &confirmedExit) != FALSE
									&& confirmedExit != STILL_ACTIVE;
								if (!confirmedQuery)
								{
									if (!exitedDuringConfirm) ok = false;
								}
								else if (normalized(confirmedImage.data()) == expected)
								{
									found = true;
									const bool terminateRequested = TerminateProcess(process, 0) != FALSE;
									DWORD naturalExit = STILL_ACTIVE;
									const bool exitedAfterTerminate = WaitForSingleObject(process, 0) == WAIT_OBJECT_0
										|| (GetExitCodeProcess(process, &naturalExit) != FALSE && naturalExit != STILL_ACTIVE);
									if (!terminateRequested && !exitedAfterTerminate) ok = false;
									else if (!exitedAfterTerminate
										&& WaitForSingleObject(process, 5000) != WAIT_OBJECT_0) ok = false;
								}
								CloseHandle(process);
							}
						}
					}
					if (!ok) break;
				}
				if (!ok) break;
				hasEntry = Process32NextW(snapshot, &entry);
				if (!hasEntry && GetLastError() != ERROR_NO_MORE_FILES) ok = false;
			}
			CloseHandle(snapshot);
			if (!ok) return false;
			if (!found) return true;
			Sleep(100);
		}
		return false;
	}

	bool validateIsolatedProcessInspection()
	{
		wchar_t temporaryDirectory[MAX_PATH + 1]{};
		if (GetTempPathW(MAX_PATH, temporaryDirectory) == 0) return false;
		const std::wstring impossibleRoot = join(temporaryDirectory,
			L"TurboRama-process-self-test-" + std::to_wstring(GetCurrentProcessId()));
		// Exercita os mesmos nomes que normalmente estao vivos em outra sessao
		// durante o build, sempre com caminhos aleatorios que nao sao os alvos.
		return stopExactProcessAndConfirm(join(impossibleRoot, L"dotnet.exe"), false)
			&& stopExactProcessAndConfirm(join(impossibleRoot, L"emulationstation.exe"), false)
			&& stopExactProcessAndConfirm(join(impossibleRoot, L"TurboRama.Launcher.exe"), false)
			&& stopExactProcessAndConfirm(join(impossibleRoot, L"TurboRama.exe"), false);
	}

	bool isExactProcessRunning(const std::wstring& expectedPath)
	{
		const std::wstring expected = normalized(expectedPath);
		HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
		if (snapshot == INVALID_HANDLE_VALUE) return false;
		bool running = false;
		PROCESSENTRY32W entry{}; entry.dwSize = sizeof(entry);
		if (Process32FirstW(snapshot, &entry))
		{
			do
			{
				HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, entry.th32ProcessID);
				if (process == nullptr) continue;
				std::vector<wchar_t> image(32768);
				DWORD length = static_cast<DWORD>(image.size());
				running = QueryFullProcessImageNameW(process, 0, image.data(), &length) != FALSE
					&& normalized(image.data()) == expected;
				CloseHandle(process);
				if (running) break;
			} while (Process32NextW(snapshot, &entry));
		}
		CloseHandle(snapshot);
		return running;
	}

	bool requestGracefulAgentStop(const std::wstring& target)
	{
		const std::wstring bridge = join(target, L".emulationstation\\pix");
		// Nunca criamos o bridge aqui: em producao ele ja foi criado e recebeu
		// owner/DACL protegidos antes de este sentinel ser escrito.
		if (!validateDirectoryNoReparse(bridge)) return false;
		const std::wstring marker = join(bridge, L"agent-stop.request");
		return writeUtf8FilePreservingObject(marker, L"installer-update\n");
	}

	void waitForAgentProcessesExit(const std::wstring& privateDotnet,
		const std::wstring& standaloneAgent, DWORD timeoutMs)
	{
		const ULONGLONG deadline = GetTickCount64() + timeoutMs;
		while (GetTickCount64() < deadline)
		{
			if (!isExactProcessRunning(privateDotnet) && !isExactProcessRunning(standaloneAgent)) return;
			Sleep(100);
		}
	}

	struct FilesystemMetadata
	{
		std::wstring path;
		bool directory = false;
		FILE_BASIC_INFO basic{};
		LARGE_INTEGER size{};
		std::array<unsigned char, 32> hash{};
		bool contentCaptured = false;
		SecurityBackup security;
	};

	struct AtomicFileReplaceResult
	{
		bool rollbackComplete = true;
		bool residueFree = true;
	};

	bool replaceFileBytesAtomically(const std::wstring& destination,
		const std::vector<unsigned char>& bytes,
		const FilesystemMetadata* desiredMetadata,
		AtomicFileReplaceResult* result = nullptr, HANDLE* retainedPin = nullptr,
		HANDLE pinnedOriginal = INVALID_HANDLE_VALUE);
	bool removeRegularFileIfPresent(const std::wstring& path);

	bool randomSiblingLeaf(const std::wstring& prefix, std::wstring& leaf)
	{
		std::array<unsigned char, 16> random{};
		if (BCryptGenRandom(nullptr, random.data(), static_cast<ULONG>(random.size()),
			BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0) return false;
		static const wchar_t digits[] = L"0123456789abcdef";
		leaf = prefix;
		for (unsigned char value : random)
		{
			leaf.push_back(digits[value >> 4]);
			leaf.push_back(digits[value & 15]);
		}
		return leaf.find_first_of(L"\\/") == std::wstring::npos;
	}

	bool renameOpenedObject(HANDLE object, HANDLE parentDirectory,
		const std::wstring& leaf)
	{
		if (object == INVALID_HANDLE_VALUE || parentDirectory == INVALID_HANDLE_VALUE
			|| leaf.empty() || leaf == L"." || leaf == L".."
			|| leaf.find_first_of(L"\\/") != std::wstring::npos) return false;
		auto renameWith = [&](HANDLE root, const std::wstring& name)
		{
			const DWORD nameBytes = static_cast<DWORD>(name.size() * sizeof(wchar_t));
			std::vector<unsigned char> storage(sizeof(FILE_RENAME_INFO) + nameBytes, 0);
			auto* information = reinterpret_cast<FILE_RENAME_INFO*>(storage.data());
			information->ReplaceIfExists = FALSE;
			information->RootDirectory = root;
			information->FileNameLength = nameBytes;
			CopyMemory(information->FileName, name.data(), nameBytes);
			return SetFileInformationByHandle(object, FileRenameInfo, information,
				static_cast<DWORD>(storage.size())) != FALSE;
		};
		// A operacao relativa usa o proprio parent pin e nao exige uma segunda
		// abertura gravavel do diretorio. Alguns filtros antigos recusam esse formato;
		// apenas nesses ambientes tentamos o caminho absoluto derivado do mesmo pin.
		if (renameWith(parentDirectory, leaf)) return true;
		const DWORD flags = FILE_NAME_NORMALIZED | VOLUME_NAME_DOS;
		const DWORD required = GetFinalPathNameByHandleW(parentDirectory, nullptr, 0, flags);
		if (required == 0) return false;
		std::vector<wchar_t> parentBuffer(static_cast<size_t>(required) + 1, L'\0');
		const DWORD written = GetFinalPathNameByHandleW(parentDirectory,
			parentBuffer.data(), static_cast<DWORD>(parentBuffer.size()), flags);
		if (written == 0 || written >= parentBuffer.size()) return false;
		std::wstring parentPath(parentBuffer.data(), written);
		if (parentPath.size() >= 8 && _wcsnicmp(parentPath.c_str(), L"\\\\?\\UNC\\", 8) == 0)
			parentPath = L"\\\\" + parentPath.substr(8);
		else if (parentPath.size() >= 4
			&& _wcsnicmp(parentPath.c_str(), L"\\\\?\\", 4) == 0)
			parentPath.erase(0, 4);
		const std::wstring destination = join(parentPath, leaf);
		return renameWith(nullptr, destination);
	}

	bool markOpenedObjectForDeletion(HANDLE object)
	{
		FILE_DISPOSITION_INFO disposition{};
		disposition.DeleteFile = TRUE;
		return object != INVALID_HANDLE_VALUE
			&& SetFileInformationByHandle(object, FileDispositionInfo, &disposition,
				sizeof(disposition)) != FALSE;
	}

	bool restoreCapturedSecurityToHandle(HANDLE object, const std::wstring& currentPath,
		bool directory, const SecurityBackup& captured)
	{
		if (object == INVALID_HANDLE_VALUE || captured.descriptor.empty()) return false;
		PSECURITY_DESCRIPTOR descriptor =
			reinterpret_cast<PSECURITY_DESCRIPTOR>(const_cast<unsigned char*>(captured.descriptor.data()));
		PSID owner = nullptr, group = nullptr;
		PACL dacl = nullptr;
		BOOL defaulted = FALSE, present = FALSE;
		if (!GetSecurityDescriptorOwner(descriptor, &owner, &defaulted)
			|| !GetSecurityDescriptorGroup(descriptor, &group, &defaulted)
			|| !GetSecurityDescriptorDacl(descriptor, &present, &dacl, &defaulted)
			|| !present || owner == nullptr || group == nullptr || dacl == nullptr) return false;
		FILE_ID_INFO identity{};
		DWORD bindingError = ERROR_SUCCESS;
		if (!validateOpenedFilesystemObject(object, currentPath, directory, &identity,
			&bindingError)) return false;
		std::vector<unsigned char> explicitAclStorage;
		PACL restoreDacl = dacl;
		if (!captured.daclProtected
			&& !explicitAcesOnly(dacl, explicitAclStorage, restoreDacl)) return false;
		SecurityBackup expected = captured;
		expected.path = currentPath;
		expected.directory = directory;
		expected.fileIdentity = identity;
		if (securityBackupMatchesObject(object, expected, nullptr)) return true;
		PSID currentOwner = nullptr, currentGroup = nullptr;
		PSECURITY_DESCRIPTOR currentDescriptor = nullptr;
		const DWORD currentResult = GetSecurityInfo(object, SE_FILE_OBJECT,
			OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION,
			&currentOwner, &currentGroup, nullptr, nullptr, &currentDescriptor);
		if (currentResult != ERROR_SUCCESS || currentOwner == nullptr
			|| currentGroup == nullptr)
		{
			if (currentDescriptor != nullptr) LocalFree(currentDescriptor);
			return false;
		}
		const bool ownerCorrect = EqualSid(currentOwner, owner) != FALSE;
		const bool groupCorrect = EqualSid(currentGroup, group) != FALSE;
		SECURITY_INFORMATION information = DACL_SECURITY_INFORMATION
			| (captured.daclProtected ? PROTECTED_DACL_SECURITY_INFORMATION
				: UNPROTECTED_DACL_SECURITY_INFORMATION);
		if (!ownerCorrect) information |= OWNER_SECURITY_INFORMATION;
		if (!groupCorrect) information |= GROUP_SECURITY_INFORMATION;
		const DWORD restoreResult = SetSecurityInfo(object, SE_FILE_OBJECT, information,
			ownerCorrect ? nullptr : owner, groupCorrect ? nullptr : group,
			restoreDacl, nullptr);
		LocalFree(currentDescriptor);
		if (restoreResult != ERROR_SUCCESS) return false;
		return securityBackupMatchesObject(object, expected, nullptr);
	}

	bool restoreFilesystemMetadataToHandle(HANDLE object, const std::wstring& currentPath,
		const FilesystemMetadata& captured)
	{
		bool ok = restoreCapturedSecurityToHandle(object, currentPath, captured.directory,
			captured.security);
		FILE_BASIC_INFO basic = captured.basic;
		FILE_BASIC_INFO confirmed{};
		if (!SetFileInformationByHandle(object, FileBasicInfo, &basic, sizeof(basic))
			|| !GetFileInformationByHandleEx(object, FileBasicInfo, &confirmed, sizeof(confirmed))
			|| confirmed.CreationTime.QuadPart != basic.CreationTime.QuadPart
			|| confirmed.LastAccessTime.QuadPart != basic.LastAccessTime.QuadPart
			|| confirmed.LastWriteTime.QuadPart != basic.LastWriteTime.QuadPart
			|| confirmed.ChangeTime.QuadPart != basic.ChangeTime.QuadPart
			|| confirmed.FileAttributes != basic.FileAttributes) ok = false;
		return ok;
	}

	bool captureFilesystemMetadataFromHandle(HANDLE object, const std::wstring& path,
		bool directory, FilesystemMetadata& entry)
	{
		entry = {};
		entry.path = path;
		entry.directory = directory;
		FILE_STANDARD_INFO standard{};
		DWORD bindingError = ERROR_SUCCESS;
		if (!validateOpenedFilesystemObject(object, path, directory, nullptr, &bindingError)
			|| GetFileInformationByHandleEx(object, FileBasicInfo, &entry.basic,
				sizeof(entry.basic)) == FALSE
			|| GetFileInformationByHandleEx(object, FileStandardInfo, &standard,
				sizeof(standard)) == FALSE) return false;
		if (!directory)
		{
			entry.size = standard.EndOfFile;
			if (!hashHandle(object, entry.hash.data())) return false;
			entry.contentCaptured = true;
		}
		return captureSecurityBackupFromHandle(object, path, directory, entry.security);
	}

	bool captureFilesystemMetadata(const std::wstring& path, bool directory,
		std::vector<FilesystemMetadata>& metadata)
	{
		HANDLE object = CreateFileW(path.c_str(), READ_CONTROL | FILE_READ_ATTRIBUTES
			| (directory ? FILE_LIST_DIRECTORY : GENERIC_READ), FILE_SHARE_READ,
			nullptr, OPEN_EXISTING,
			(directory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
				| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		FilesystemMetadata entry;
		const bool ok = object != INVALID_HANDLE_VALUE
			&& captureFilesystemMetadataFromHandle(object, path, directory, entry);
		if (object != INVALID_HANDLE_VALUE) CloseHandle(object);
		if (!ok) return false;
		metadata.push_back(std::move(entry));
		return true;
	}

	bool captureFilesystemMetadataTreeFromHandle(HANDLE directoryHandle,
		const std::wstring& directory, std::vector<FilesystemMetadata>& metadata)
	{
		FilesystemMetadata root;
		if (directoryHandle == INVALID_HANDLE_VALUE
			|| !captureFilesystemMetadataFromHandle(directoryHandle, directory, true, root))
			return false;
		const FILE_ID_INFO rootIdentity = root.security.fileIdentity;
		metadata.push_back(std::move(root));
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(directory, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE)
		{
			FILE_ID_INFO finalIdentity{};
			DWORD bindingError = ERROR_SUCCESS;
			return GetLastError() == ERROR_FILE_NOT_FOUND
				&& validateOpenedFilesystemObject(directoryHandle, directory, true,
					&finalIdentity, &bindingError)
				&& sameFileIdentity(finalIdentity, rootIdentity);
		}
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
			{
				ok = false;
				break;
			}
			const std::wstring path = join(directory, name);
			const bool childDirectory = (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
			HANDLE child = CreateFileW(path.c_str(), READ_CONTROL | FILE_READ_ATTRIBUTES
				| (childDirectory ? FILE_LIST_DIRECTORY : GENERIC_READ), FILE_SHARE_READ,
				nullptr, OPEN_EXISTING,
				(childDirectory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
					| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			if (child == INVALID_HANDLE_VALUE)
			{
				ok = false;
				break;
			}
			if (childDirectory)
				ok = captureFilesystemMetadataTreeFromHandle(child, path, metadata);
			else
			{
				FilesystemMetadata childMetadata;
				ok = captureFilesystemMetadataFromHandle(child, path, false, childMetadata);
				if (ok) metadata.push_back(std::move(childMetadata));
			}
			CloseHandle(child);
			if (!ok) break;
		} while (FindNextFileW(search, &entry));
		const DWORD enumerationError = ok ? GetLastError() : ERROR_SUCCESS;
		FindClose(search);
		FILE_ID_INFO finalIdentity{};
		DWORD bindingError = ERROR_SUCCESS;
		const bool rootStable = validateOpenedFilesystemObject(directoryHandle, directory, true,
			&finalIdentity, &bindingError)
			&& sameFileIdentity(finalIdentity, rootIdentity);
		return ok && enumerationError == ERROR_NO_MORE_FILES && rootStable;
	}

	bool captureFilesystemMetadataTree(const std::wstring& directory,
		std::vector<FilesystemMetadata>& metadata)
	{
		HANDLE directoryHandle = CreateFileW(directory.c_str(), READ_CONTROL
			| FILE_READ_ATTRIBUTES | FILE_LIST_DIRECTORY, FILE_SHARE_READ, nullptr,
			OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
			nullptr);
		const bool ok = directoryHandle != INVALID_HANDLE_VALUE
			&& captureFilesystemMetadataTreeFromHandle(directoryHandle, directory, metadata);
		if (directoryHandle != INVALID_HANDLE_VALUE) CloseHandle(directoryHandle);
		return ok;
	}

	bool sameBasicMetadata(const FILE_BASIC_INFO& left, const FILE_BASIC_INFO& right)
	{
		return left.CreationTime.QuadPart == right.CreationTime.QuadPart
			&& left.LastAccessTime.QuadPart == right.LastAccessTime.QuadPart
			&& left.LastWriteTime.QuadPart == right.LastWriteTime.QuadPart
			&& left.ChangeTime.QuadPart == right.ChangeTime.QuadPart
			&& left.FileAttributes == right.FileAttributes;
	}

	bool sameStableBasicMetadata(const FILE_BASIC_INFO& left, const FILE_BASIC_INFO& right)
	{
		// A propria leitura do hash pode atualizar LastAccessTime conforme a politica
		// do volume; os demais campos devem permanecer invariaveis durante o pin.
		return left.CreationTime.QuadPart == right.CreationTime.QuadPart
			&& left.LastWriteTime.QuadPart == right.LastWriteTime.QuadPart
			&& left.ChangeTime.QuadPart == right.ChangeTime.QuadPart
			&& left.FileAttributes == right.FileAttributes;
	}

	std::wstring manifestRelativePath(const std::wstring& root, const std::wstring& path)
	{
		const std::wstring normalizedRoot = normalized(root);
		const std::wstring normalizedPath = normalized(path);
		if (normalizedPath == normalizedRoot) return {};
		const std::wstring prefix = normalizedRoot + L"\\";
		if (normalizedPath.size() <= prefix.size()
			|| normalizedPath.compare(0, prefix.size(), prefix) != 0) return L"\x0001";
		return normalizedPath.substr(prefix.size());
	}

	bool sameFilesystemManifest(const std::wstring& leftRoot,
		const std::vector<FilesystemMetadata>& left,
		const std::wstring& rightRoot,
		const std::vector<FilesystemMetadata>& right,
		bool requireSameIdentity, bool requireSameMetadata)
	{
		if (left.size() != right.size() || left.empty()) return false;
		std::vector<const FilesystemMetadata*> sortedLeft, sortedRight;
		for (const auto& entry : left) sortedLeft.push_back(&entry);
		for (const auto& entry : right) sortedRight.push_back(&entry);
		auto compareLeft = [&](const FilesystemMetadata* first,
			const FilesystemMetadata* second)
		{
			return manifestRelativePath(leftRoot, first->path)
				< manifestRelativePath(leftRoot, second->path);
		};
		auto compareRight = [&](const FilesystemMetadata* first,
			const FilesystemMetadata* second)
		{
			return manifestRelativePath(rightRoot, first->path)
				< manifestRelativePath(rightRoot, second->path);
		};
		std::sort(sortedLeft.begin(), sortedLeft.end(), compareLeft);
		std::sort(sortedRight.begin(), sortedRight.end(), compareRight);
		for (size_t index = 0; index < sortedLeft.size(); ++index)
		{
			const FilesystemMetadata& first = *sortedLeft[index];
			const FilesystemMetadata& second = *sortedRight[index];
			const std::wstring firstRelative = manifestRelativePath(leftRoot, first.path);
			const std::wstring secondRelative = manifestRelativePath(rightRoot, second.path);
			if (firstRelative.empty() != secondRelative.empty()
				|| firstRelative == L"\x0001" || secondRelative == L"\x0001"
				|| firstRelative != secondRelative || first.directory != second.directory)
				return false;
			if (!first.directory
				&& (!first.contentCaptured || !second.contentCaptured
					|| first.size.QuadPart != second.size.QuadPart
					|| !sameHash(first.hash.data(), second.hash.data()))) return false;
			if (requireSameIdentity
				&& !sameFileIdentity(first.security.fileIdentity,
					second.security.fileIdentity)) return false;
			if (requireSameMetadata
				&& (!sameStableBasicMetadata(first.basic, second.basic)
					|| first.security.daclProtected != second.security.daclProtected
					|| first.security.descriptor.size() != second.security.descriptor.size()
					|| memcmp(first.security.descriptor.data(), second.security.descriptor.data(),
						first.security.descriptor.size()) != 0)) return false;
		}
		return true;
	}

	bool pathIsMissing(const std::wstring& path)
	{
		if (GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES) return false;
		const DWORD error = GetLastError();
		return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND;
	}

	bool clearReadonlyAttribute(HANDLE object)
	{
		FILE_BASIC_INFO basic{};
		if (!GetFileInformationByHandleEx(object, FileBasicInfo, &basic, sizeof(basic))) return false;
		if ((basic.FileAttributes & FILE_ATTRIBUTE_READONLY) == 0) return true;
		basic.FileAttributes &= ~FILE_ATTRIBUTE_READONLY;
		if (basic.FileAttributes == 0) basic.FileAttributes = FILE_ATTRIBUTE_NORMAL;
		return SetFileInformationByHandle(object, FileBasicInfo, &basic, sizeof(basic)) != FALSE;
	}

	bool markOpenedTreeContentsForDeletion(HANDLE directoryHandle,
		const std::wstring& directory)
	{
		if (!validateOpenedFilesystemObject(directoryHandle, directory, true)) return false;
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(directory, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE)
			return GetLastError() == ERROR_FILE_NOT_FOUND;
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
			{
				ok = false;
				continue;
			}
			const bool childDirectory = (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
			const std::wstring childPath = join(directory, name);
			const DWORD access = DELETE | FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES
				| READ_CONTROL | (childDirectory ? FILE_LIST_DIRECTORY : GENERIC_READ);
			HANDLE child = CreateFileW(childPath.c_str(), access,
				FILE_SHARE_READ, nullptr, OPEN_EXISTING,
				(childDirectory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
					| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			if (child == INVALID_HANDLE_VALUE
				|| !validateOpenedFilesystemObject(child, childPath, childDirectory))
			{
				if (child != INVALID_HANDLE_VALUE) CloseHandle(child);
				ok = false;
				continue;
			}
			bool childRemoved = true;
			if (childDirectory)
				childRemoved = markOpenedTreeContentsForDeletion(child, childPath);
			if (!clearReadonlyAttribute(child) || !markOpenedObjectForDeletion(child))
				childRemoved = false;
			CloseHandle(child);
			if (!childRemoved || !pathIsMissing(childPath)) ok = false;
		} while (FindNextFileW(search, &entry));
		const DWORD enumerationError = GetLastError();
		FindClose(search);
		return ok && enumerationError == ERROR_NO_MORE_FILES
			&& validateOpenedFilesystemObject(directoryHandle, directory, true);
	}

	bool securelyDeleteOpenedObject(HANDLE& object, const std::wstring& currentPath,
		bool directory, const std::vector<FilesystemMetadata>* expectedManifest,
		const std::wstring& expectedRoot)
	{
		if (object == INVALID_HANDLE_VALUE) return pathIsMissing(currentPath);
		std::vector<FilesystemMetadata> current;
		bool ok = directory
			? captureFilesystemMetadataTreeFromHandle(object, currentPath, current)
			: [&]()
			{
				FilesystemMetadata file;
				const bool captured = captureFilesystemMetadataFromHandle(object, currentPath,
					false, file);
				if (captured) current.push_back(std::move(file));
				return captured;
			}();
		if (!ok) SetLastError(44100 + GetLastError());
		if (ok && expectedManifest != nullptr)
		{
			ok = sameFilesystemManifest(expectedRoot, *expectedManifest, currentPath,
				current, true, false);
			if (!ok) SetLastError(44200);
		}
		if (ok && directory)
		{
			ok = markOpenedTreeContentsForDeletion(object, currentPath);
			if (!ok) SetLastError(44300 + GetLastError());
		}
		if (ok)
		{
			ok = clearReadonlyAttribute(object) && markOpenedObjectForDeletion(object);
			if (!ok) SetLastError(44400 + GetLastError());
		}
		const DWORD operationError = GetLastError();
		CloseHandle(object);
		object = INVALID_HANDLE_VALUE;
		const bool missing = pathIsMissing(currentPath);
		if (!ok) SetLastError(operationError);
		else if (!missing) SetLastError(44500);
		return ok && missing;
	}

	bool createPrivateDirectoryHandle(const std::wstring& path, HANDLE& directory)
	{
		directory = INVALID_HANDLE_VALUE;
		// O nome aleatorio e criado dentro do target ja fixado; a ACL herdada e
		// temporaria e sera substituida pelo descritor capturado antes da publicacao.
		const bool created = CreateDirectoryW(path.c_str(), nullptr) != FALSE;
		if (!created) return false;
		const DWORD access = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER
			| FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES | FILE_LIST_DIRECTORY
			| FILE_TRAVERSE;
		directory = CreateFileW(path.c_str(), access, FILE_SHARE_READ,
			nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (directory == INVALID_HANDLE_VALUE && GetLastError() == ERROR_ACCESS_DENIED)
			directory = CreateFileW(path.c_str(), access & ~WRITE_OWNER, FILE_SHARE_READ,
				nullptr, OPEN_EXISTING,
				FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (directory == INVALID_HANDLE_VALUE && GetLastError() == ERROR_ACCESS_DENIED)
			directory = CreateFileW(path.c_str(), access & ~(WRITE_OWNER | WRITE_DAC),
				FILE_SHARE_READ, nullptr, OPEN_EXISTING,
				FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		return directory != INVALID_HANDLE_VALUE
			&& validateOpenedFilesystemObject(directory, path, true);
	}

	bool copyOpenedFileToNewPath(const std::wstring& sourcePath,
		const std::wstring& destinationPath, HANDLE& destination,
		FilesystemMetadata& sourceMetadata, FilesystemMetadata& destinationMetadata)
	{
		destination = INVALID_HANDLE_VALUE;
		HANDLE source = CreateFileW(sourcePath.c_str(), GENERIC_READ | READ_CONTROL
			| FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN,
			nullptr);
		bool ok = source != INVALID_HANDLE_VALUE
			&& captureFilesystemMetadataFromHandle(source, sourcePath, false, sourceMetadata);
		const DWORD access = GENERIC_READ | GENERIC_WRITE | DELETE | READ_CONTROL
			| WRITE_DAC | WRITE_OWNER | FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES;
		if (ok)
			destination = CreateFileW(destinationPath.c_str(), access,
				FILE_SHARE_READ, nullptr, CREATE_NEW,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH
					| FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
		if (destination == INVALID_HANDLE_VALUE) ok = false;
		std::vector<unsigned char> buffer(1024 * 1024);
		while (ok)
		{
			DWORD read = 0, written = 0;
			if (!ReadFile(source, buffer.data(), static_cast<DWORD>(buffer.size()),
				&read, nullptr))
			{
				ok = false;
				break;
			}
			if (read == 0) break;
			if (!WriteFile(destination, buffer.data(), read, &written, nullptr)
				|| written != read) ok = false;
		}
		if (ok) ok = FlushFileBuffers(destination) != FALSE
			&& captureFilesystemMetadataFromHandle(destination, destinationPath, false,
				destinationMetadata);
		if (ok)
		{
			std::vector<FilesystemMetadata> sourceManifest{ sourceMetadata };
			std::vector<FilesystemMetadata> destinationManifest{ destinationMetadata };
			ok = sameFilesystemManifest(sourcePath, sourceManifest, destinationPath,
				destinationManifest, false, false)
				&& validateOpenedFilesystemObject(source, sourcePath, false);
		}
		if (source != INVALID_HANDLE_VALUE) CloseHandle(source);
		return ok;
	}

	struct PinnedArtifactObject
	{
		std::wstring preparedPath;
		bool directory = false;
		HANDLE handle = INVALID_HANDLE_VALUE;
		FILE_ID_INFO identity{};
	};

	void closePinnedArtifactObjects(std::vector<PinnedArtifactObject>& pins)
	{
		for (auto iterator = pins.rbegin(); iterator != pins.rend(); ++iterator)
			if (iterator->handle != INVALID_HANDLE_VALUE) CloseHandle(iterator->handle);
		pins.clear();
	}

	bool appendPinnedArtifactObject(const std::wstring& path, bool directory,
		HANDLE object, std::vector<PinnedArtifactObject>& pins)
	{
		PinnedArtifactObject pinned;
		pinned.preparedPath = path;
		pinned.directory = directory;
		pinned.handle = object;
		return object != INVALID_HANDLE_VALUE
			&& validateOpenedFilesystemObject(object, path, directory, &pinned.identity)
			&& (pins.push_back(std::move(pinned)), true);
	}

	bool pinArtifactTreeChildren(const std::wstring& directory,
		std::vector<PinnedArtifactObject>& pins, bool metadataWritable)
	{
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(directory, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE) return false;
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
			{
				ok = false;
				break;
			}
			const bool childDirectory = (entry.dwFileAttributes
				& FILE_ATTRIBUTE_DIRECTORY) != 0;
			const std::wstring childPath = join(directory, name);
			DWORD access = READ_CONTROL | FILE_READ_ATTRIBUTES
				| (childDirectory ? FILE_LIST_DIRECTORY | FILE_TRAVERSE : GENERIC_READ);
			if (metadataWritable)
				access |= WRITE_DAC | WRITE_OWNER | FILE_WRITE_ATTRIBUTES;
			HANDLE child = CreateFileW(childPath.c_str(), access, FILE_SHARE_READ,
				nullptr, OPEN_EXISTING,
				(childDirectory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
					| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			if (child == INVALID_HANDLE_VALUE && metadataWritable
				&& GetLastError() == ERROR_ACCESS_DENIED)
			{
				access &= ~WRITE_OWNER;
				child = CreateFileW(childPath.c_str(), access, FILE_SHARE_READ,
					nullptr, OPEN_EXISTING,
					(childDirectory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
						| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			}
			if (child == INVALID_HANDLE_VALUE && metadataWritable
				&& GetLastError() == ERROR_ACCESS_DENIED)
			{
				access &= ~WRITE_DAC;
				child = CreateFileW(childPath.c_str(), access, FILE_SHARE_READ,
					nullptr, OPEN_EXISTING,
					(childDirectory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
						| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			}
			if (!appendPinnedArtifactObject(childPath, childDirectory, child, pins))
			{
				if (child != INVALID_HANDLE_VALUE) CloseHandle(child);
				ok = false;
				break;
			}
			if (childDirectory && !pinArtifactTreeChildren(childPath, pins,
				metadataWritable))
			{
				ok = false;
				break;
			}
		} while (FindNextFileW(search, &entry));
		const DWORD enumerationError = ok ? GetLastError() : ERROR_SUCCESS;
		FindClose(search);
		return ok && enumerationError == ERROR_NO_MORE_FILES;
	}

	bool copyPinnedTreeRecursive(HANDLE sourceDirectory, const std::wstring& sourcePath,
		HANDLE destinationDirectory, const std::wstring& destinationPath,
		std::vector<FilesystemMetadata>& sourceManifest,
		std::vector<PinnedArtifactObject>& destinationPins)
	{
		FilesystemMetadata sourceRoot;
		if (!captureFilesystemMetadataFromHandle(sourceDirectory, sourcePath, true,
			sourceRoot)) return false;
		sourceManifest.push_back(std::move(sourceRoot));
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(sourcePath, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE)
			return GetLastError() == ERROR_FILE_NOT_FOUND
				&& validateOpenedFilesystemObject(sourceDirectory, sourcePath, true)
				&& validateOpenedFilesystemObject(destinationDirectory, destinationPath, true);
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
			{
				ok = false;
				break;
			}
			const bool childDirectory = (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
			const std::wstring sourceChild = join(sourcePath, name);
			const std::wstring destinationChild = join(destinationPath, name);
			if (childDirectory)
			{
				HANDLE input = CreateFileW(sourceChild.c_str(), READ_CONTROL
					| FILE_READ_ATTRIBUTES | FILE_LIST_DIRECTORY | FILE_TRAVERSE,
					FILE_SHARE_READ, nullptr, OPEN_EXISTING,
					FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
				HANDLE output = INVALID_HANDLE_VALUE;
				ok = input != INVALID_HANDLE_VALUE
					&& validateOpenedFilesystemObject(input, sourceChild, true)
					&& createPrivateDirectoryHandle(destinationChild, output);
				if (ok)
				{
					ok = appendPinnedArtifactObject(destinationChild, true, output,
						destinationPins);
					if (ok) ok = copyPinnedTreeRecursive(input, sourceChild, output,
						destinationChild, sourceManifest, destinationPins);
				}
				if (!ok && output != INVALID_HANDLE_VALUE)
				{
					bool owned = false;
					for (const auto& pin : destinationPins)
						if (pin.handle == output) { owned = true; break; }
					if (!owned) CloseHandle(output);
				}
				if (input != INVALID_HANDLE_VALUE) CloseHandle(input);
			}
			else
			{
				HANDLE output = INVALID_HANDLE_VALUE;
				FilesystemMetadata sourceFile, destinationFile;
				ok = copyOpenedFileToNewPath(sourceChild, destinationChild, output,
					sourceFile, destinationFile);
				if (ok)
				{
					sourceManifest.push_back(std::move(sourceFile));
					ok = appendPinnedArtifactObject(destinationChild, false, output,
						destinationPins);
				}
				if (!ok && output != INVALID_HANDLE_VALUE)
				{
					bool owned = false;
					for (const auto& pin : destinationPins)
						if (pin.handle == output) { owned = true; break; }
					if (!owned) CloseHandle(output);
				}
			}
			if (!ok) break;
		} while (FindNextFileW(search, &entry));
		const DWORD enumerationError = ok ? GetLastError() : ERROR_SUCCESS;
		FindClose(search);
		return ok && enumerationError == ERROR_NO_MORE_FILES
			&& validateOpenedFilesystemObject(sourceDirectory, sourcePath, true)
			&& validateOpenedFilesystemObject(destinationDirectory, destinationPath, true);
	}

	std::wstring mappedMetadataPath(const std::wstring& capturedRoot,
		const std::wstring& currentRoot, const std::wstring& capturedPath)
	{
		const std::wstring relative = manifestRelativePath(capturedRoot, capturedPath);
		if (relative == L"\x0001") return {};
		return relative.empty() ? currentRoot : join(currentRoot, relative);
	}

	HANDLE openMetadataObject(const std::wstring& path, bool directory, DWORD extraAccess)
	{
		HANDLE object = CreateFileW(path.c_str(), READ_CONTROL | FILE_READ_ATTRIBUTES | extraAccess,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			(directory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
				| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (object == INVALID_HANDLE_VALUE && GetLastError() == ERROR_ACCESS_DENIED
			&& (extraAccess & (WRITE_DAC | WRITE_OWNER)) != 0)
		{
			extraAccess &= ~(WRITE_DAC | WRITE_OWNER);
			object = CreateFileW(path.c_str(), READ_CONTROL | FILE_READ_ATTRIBUTES | extraAccess,
				FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
				(directory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
					| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		}
		return object;
	}

	bool filesystemMetadataMatchesMapped(const std::wstring& capturedRoot,
		const std::wstring& currentRoot, const std::vector<FilesystemMetadata>& metadata)
	{
		bool ok = !metadata.empty();
		for (const auto& captured : metadata)
		{
			const std::wstring currentPath = mappedMetadataPath(capturedRoot, currentRoot,
				captured.path);
			HANDLE object = currentPath.empty() ? INVALID_HANDLE_VALUE
				: openMetadataObject(currentPath, captured.directory,
					captured.directory ? FILE_LIST_DIRECTORY : GENERIC_READ);
			FILE_ID_INFO identity{};
			FILE_BASIC_INFO basic{};
			DWORD bindingError = ERROR_SUCCESS;
			bool matched = object != INVALID_HANDLE_VALUE
				&& validateOpenedFilesystemObject(object, currentPath, captured.directory,
					&identity, &bindingError)
				&& GetFileInformationByHandleEx(object, FileBasicInfo, &basic, sizeof(basic))
				&& sameBasicMetadata(basic, captured.basic);
			if (matched)
			{
				SecurityBackup expected = captured.security;
				expected.path = currentPath;
				expected.fileIdentity = identity;
				matched = securityBackupMatchesObject(object, expected, nullptr);
			}
			if (object != INVALID_HANDLE_VALUE) CloseHandle(object);
			if (!matched) ok = false;
		}
		return ok;
	}

	bool restoreMappedFilesystemMetadata(const std::wstring& capturedRoot,
		const std::wstring& currentRoot, const std::vector<FilesystemMetadata>& metadata)
	{
		bool ok = !metadata.empty();
		// Owner/group/DACL dos pais primeiro. Nenhuma falha impede as tentativas seguintes.
		for (const auto& captured : metadata)
		{
			const std::wstring currentPath = mappedMetadataPath(capturedRoot, currentRoot,
				captured.path);
			HANDLE object = currentPath.empty() ? INVALID_HANDLE_VALUE
				: openMetadataObject(currentPath, captured.directory,
					WRITE_DAC | (captured.directory
						? FILE_LIST_DIRECTORY : GENERIC_READ));
			const bool restored = object != INVALID_HANDLE_VALUE
				&& restoreCapturedSecurityToHandle(object, currentPath, captured.directory,
					captured.security);
			if (object != INVALID_HANDLE_VALUE) CloseHandle(object);
			if (!restored) ok = false;
		}
		// Arquivos alteram tempos dos pais; FileBasic e reposto de baixo para cima.
		for (auto iterator = metadata.rbegin(); iterator != metadata.rend(); ++iterator)
		{
			const std::wstring currentPath = mappedMetadataPath(capturedRoot, currentRoot,
				iterator->path);
			HANDLE object = currentPath.empty() ? INVALID_HANDLE_VALUE
				: openMetadataObject(currentPath, iterator->directory, FILE_WRITE_ATTRIBUTES
					| (iterator->directory ? FILE_LIST_DIRECTORY : 0));
			FILE_BASIC_INFO basic = iterator->basic;
			FILE_BASIC_INFO confirmed{};
			const bool restored = object != INVALID_HANDLE_VALUE
				&& validateOpenedFilesystemObject(object, currentPath, iterator->directory)
				&& SetFileInformationByHandle(object, FileBasicInfo, &basic, sizeof(basic))
				&& GetFileInformationByHandleEx(object, FileBasicInfo, &confirmed,
					sizeof(confirmed))
				&& sameBasicMetadata(confirmed, basic);
			if (object != INVALID_HANDLE_VALUE) CloseHandle(object);
			if (!restored) ok = false;
		}
		if (!filesystemMetadataMatchesMapped(capturedRoot, currentRoot, metadata)) ok = false;
		return ok;
	}

	struct AtomicInstallEntry
	{
		std::wstring leaf;
		bool directory = false;
		std::wstring canonicalPath;
		std::wstring originalCurrentPath;
		std::wstring replacementCurrentPath;
		std::wstring preparedRoot;
		std::wstring tombstonePath;
		HANDLE original = INVALID_HANDLE_VALUE;
		HANDLE replacement = INVALID_HANDLE_VALUE;
		bool originalExisted = false;
		bool originalAtTombstone = false;
		bool replacementAtCanonical = false;
		std::vector<PinnedArtifactObject> originalPins;
		std::vector<PinnedArtifactObject> replacementPins;
		std::vector<FilesystemMetadata> originalMetadata;
		std::vector<FilesystemMetadata> sourceManifest;
		std::vector<FilesystemMetadata> preparedManifest;
	};

	struct AtomicInstallTransaction
	{
		std::wstring target;
		HANDLE targetDirectory = INVALID_HANDLE_VALUE; // emprestado do pin persistente
		HANDLE targetMutationGuard = INVALID_HANDLE_VALUE;
		FILE_ID_INFO targetIdentity{};
		std::vector<AtomicInstallEntry> entries;
		bool publicationStarted = false;
		bool commitStarted = false;
	};

	bool directoryHasTransactionResidue(const std::wstring& directory);
	bool relevantTransactionResiduesAbsent(const std::wstring& target);

	bool openTargetMutationGuard(AtomicInstallTransaction& transaction)
	{
		if (transaction.targetMutationGuard != INVALID_HANDLE_VALUE)
			CloseHandle(transaction.targetMutationGuard);
		transaction.targetMutationGuard = CreateFileW(transaction.target.c_str(),
			READ_CONTROL | FILE_READ_ATTRIBUTES | FILE_LIST_DIRECTORY | FILE_TRAVERSE
				| FILE_ADD_FILE | FILE_ADD_SUBDIRECTORY,
			FILE_SHARE_READ, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		FILE_ID_INFO identity{};
		if (transaction.targetMutationGuard == INVALID_HANDLE_VALUE
			|| !validateOpenedFilesystemObject(transaction.targetMutationGuard,
				transaction.target, true, &identity)
			|| !sameFileIdentity(identity, transaction.targetIdentity))
		{
			if (transaction.targetMutationGuard != INVALID_HANDLE_VALUE)
				CloseHandle(transaction.targetMutationGuard);
			transaction.targetMutationGuard = INVALID_HANDLE_VALUE;
			return false;
		}
		return true;
	}

	void closeTargetMutationGuard(AtomicInstallTransaction& transaction)
	{
		if (transaction.targetMutationGuard != INVALID_HANDLE_VALUE)
			CloseHandle(transaction.targetMutationGuard);
		transaction.targetMutationGuard = INVALID_HANDLE_VALUE;
	}

	bool capturePinnedReplacement(AtomicInstallEntry& entry,
		const std::wstring& currentRoot, std::vector<FilesystemMetadata>& current);

	const FilesystemMetadata* findMappedMetadataEntry(const std::wstring& capturedRoot,
		const std::vector<FilesystemMetadata>& metadata, const std::wstring& relative,
		bool directory)
	{
		for (const auto& entry : metadata)
			if (entry.directory == directory
				&& manifestRelativePath(capturedRoot, entry.path) == relative) return &entry;
		return nullptr;
	}

	PinnedArtifactObject* findReplacementPin(AtomicInstallEntry& entry,
		const std::wstring& relative, bool directory)
	{
		for (auto& pin : entry.replacementPins)
			if (pin.directory == directory
				&& manifestRelativePath(entry.preparedRoot, pin.preparedPath) == relative)
				return &pin;
		return nullptr;
	}

	bool descriptorHasInheritanceFlag(const SecurityBackup& security, BYTE requiredFlag,
		bool inheritedAce)
	{
		if (security.descriptor.empty()) return false;
		PSECURITY_DESCRIPTOR descriptor = reinterpret_cast<PSECURITY_DESCRIPTOR>(
			const_cast<unsigned char*>(security.descriptor.data()));
		PACL dacl = nullptr;
		BOOL present = FALSE, defaulted = FALSE;
		if (!GetSecurityDescriptorDacl(descriptor, &present, &dacl, &defaulted)
			|| !present || dacl == nullptr) return false;
		for (DWORD index = 0; index < dacl->AceCount; ++index)
		{
			void* rawAce = nullptr;
			if (!GetAce(dacl, index, &rawAce) || rawAce == nullptr) return false;
			const auto* header = reinterpret_cast<const ACE_HEADER*>(rawAce);
			if ((header->AceFlags & requiredFlag) != 0
				&& (((header->AceFlags & INHERITED_ACE) != 0) == inheritedAce)) return true;
		}
		return false;
	}

	bool validateEffectiveInheritedSecurity(HANDLE parent, const std::wstring& parentPath,
		HANDLE child, const std::wstring& childPath, bool childDirectory)
	{
		SecurityBackup parentSecurity, childSecurity;
		if (!captureSecurityBackupFromHandle(parent, parentPath, true, parentSecurity)
			|| !captureSecurityBackupFromHandle(child, childPath, childDirectory,
				childSecurity)
			|| childSecurity.daclProtected) return false;
		const BYTE propagationFlag = childDirectory ? CONTAINER_INHERIT_ACE
			: OBJECT_INHERIT_ACE;
		const bool parentPropagates = descriptorHasInheritanceFlag(parentSecurity,
			propagationFlag, false) || descriptorHasInheritanceFlag(parentSecurity,
			propagationFlag, true);
		return !parentPropagates || descriptorHasInheritanceFlag(childSecurity,
			INHERITED_ACE, true);
	}

	bool setAndConfirmBasicMetadata(HANDLE object, const std::wstring& path,
		bool directory, const FILE_BASIC_INFO& expected)
	{
		FILE_BASIC_INFO basic = expected;
		FILE_BASIC_INFO confirmed{};
		return validateOpenedFilesystemObject(object, path, directory)
			&& SetFileInformationByHandle(object, FileBasicInfo, &basic, sizeof(basic))
			&& GetFileInformationByHandleEx(object, FileBasicInfo, &confirmed,
				sizeof(confirmed))
			&& sameBasicMetadata(confirmed, expected);
	}

	bool pinnedMetadataMatches(HANDLE object, const std::wstring& path, bool directory,
		const FilesystemMetadata& expected)
	{
		FILE_ID_INFO identity{};
		FILE_BASIC_INFO basic{};
		if (!validateOpenedFilesystemObject(object, path, directory, &identity)
			|| !GetFileInformationByHandleEx(object, FileBasicInfo, &basic, sizeof(basic))
			|| !sameBasicMetadata(basic, expected.basic)) return false;
		SecurityBackup security = expected.security;
		security.path = path;
		security.directory = directory;
		security.fileIdentity = identity;
		return securityBackupMatchesObject(object, security, nullptr);
	}

	bool restorePinnedTreeMetadataIntersection(AtomicInstallEntry& entry,
		HANDLE targetDirectory, const std::wstring& currentRoot)
	{
		if (!entry.directory || entry.replacement == INVALID_HANDLE_VALUE
			|| targetDirectory == INVALID_HANDLE_VALUE) return false;
		struct Node
		{
			HANDLE handle;
			std::wstring path;
			std::wstring relative;
			bool directory;
			const FilesystemMetadata* captured;
		};
		std::vector<Node> nodes;
		nodes.push_back({ entry.replacement, currentRoot, {}, true,
			findMappedMetadataEntry(entry.canonicalPath, entry.originalMetadata, {}, true) });
		for (auto& pin : entry.replacementPins)
		{
			const std::wstring relative = manifestRelativePath(entry.preparedRoot,
				pin.preparedPath);
			if (relative.empty() || relative == L"\x0001") return false;
			nodes.push_back({ pin.handle, join(currentRoot, relative), relative,
				pin.directory, findMappedMetadataEntry(entry.canonicalPath,
					entry.originalMetadata, relative, pin.directory) });
		}

		bool ok = true;
		for (auto& node : nodes)
		{
			if (node.captured != nullptr)
			{
				if (!restoreCapturedSecurityToHandle(node.handle, node.path,
					node.directory, node.captured->security)) ok = false;
				continue;
			}
			HANDLE parentHandle = targetDirectory;
			std::wstring parentPath = parentOf(currentRoot);
			if (!node.relative.empty())
			{
				const size_t separator = node.relative.find_last_of(L'\\');
				const std::wstring parentRelative = separator == std::wstring::npos
					? std::wstring() : node.relative.substr(0, separator);
				parentHandle = parentRelative.empty() ? entry.replacement
					: (findReplacementPin(entry, parentRelative, true) == nullptr
						? INVALID_HANDLE_VALUE
						: findReplacementPin(entry, parentRelative, true)->handle);
				parentPath = parentRelative.empty() ? currentRoot
					: join(currentRoot, parentRelative);
			}
			if (!validateEffectiveInheritedSecurity(parentHandle, parentPath,
				node.handle, node.path, node.directory)) ok = false;
		}
		for (auto iterator = nodes.rbegin(); iterator != nodes.rend(); ++iterator)
			if (iterator->captured != nullptr && !setAndConfirmBasicMetadata(
				iterator->handle, iterator->path, iterator->directory,
				iterator->captured->basic)) ok = false;
		for (auto& node : nodes)
		{
			const bool matched = node.captured != nullptr
				? pinnedMetadataMatches(node.handle, node.path, node.directory,
					*node.captured)
				: [&]()
				{
					HANDLE parentHandle = targetDirectory;
					std::wstring parentPath = parentOf(currentRoot);
					if (!node.relative.empty())
					{
						const size_t separator = node.relative.find_last_of(L'\\');
						const std::wstring parentRelative = separator == std::wstring::npos
							? std::wstring() : node.relative.substr(0, separator);
						PinnedArtifactObject* parentPin = parentRelative.empty() ? nullptr
							: findReplacementPin(entry, parentRelative, true);
						parentHandle = parentRelative.empty() ? entry.replacement
							: (parentPin == nullptr ? INVALID_HANDLE_VALUE : parentPin->handle);
						parentPath = parentRelative.empty() ? currentRoot
							: join(currentRoot, parentRelative);
					}
					return validateEffectiveInheritedSecurity(parentHandle, parentPath,
						node.handle, node.path, node.directory);
				}();
			if (!matched) ok = false;
		}
		return ok;
	}

	bool captureOpenedArtifact(HANDLE object, const std::wstring& path, bool directory,
		std::vector<FilesystemMetadata>& manifest)
	{
		manifest.clear();
		if (directory) return captureFilesystemMetadataTreeFromHandle(object, path, manifest);
		FilesystemMetadata file;
		if (!captureFilesystemMetadataFromHandle(object, path, false, file)) return false;
		manifest.push_back(std::move(file));
		return true;
	}

	bool openAndCaptureOriginal(AtomicInstallEntry& entry)
	{
		const DWORD access = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER
			| FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES
			| (entry.directory ? FILE_LIST_DIRECTORY | FILE_TRAVERSE : GENERIC_READ);
		entry.original = CreateFileW(entry.canonicalPath.c_str(), access,
			FILE_SHARE_READ, nullptr, OPEN_EXISTING,
			(entry.directory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
				| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (entry.original == INVALID_HANDLE_VALUE && GetLastError() == ERROR_ACCESS_DENIED)
		{
			entry.original = CreateFileW(entry.canonicalPath.c_str(),
				access & ~WRITE_OWNER, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
				(entry.directory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
					| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		}
		if (entry.original == INVALID_HANDLE_VALUE && GetLastError() == ERROR_ACCESS_DENIED)
		{
			entry.original = CreateFileW(entry.canonicalPath.c_str(),
				access & ~(WRITE_OWNER | WRITE_DAC), FILE_SHARE_READ, nullptr,
				OPEN_EXISTING,
				(entry.directory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
					| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		}
		entry.originalCurrentPath = entry.canonicalPath;
		if (entry.original == INVALID_HANDLE_VALUE)
		{
			const DWORD error = GetLastError();
			if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
			{
				entry.originalExisted = false;
				return pathIsMissing(entry.canonicalPath);
			}
			SetLastError(43100 + error);
			return false;
		}
		entry.originalExisted = true;
		if (entry.directory && !pinArtifactTreeChildren(entry.canonicalPath,
			entry.originalPins, true))
		{
			SetLastError(43150 + GetLastError());
			return false;
		}
		if (!captureOpenedArtifact(entry.original, entry.canonicalPath, entry.directory,
			entry.originalMetadata))
		{
			SetLastError(43200 + GetLastError());
			return false;
		}
		std::vector<FilesystemMetadata> confirmation;
		if (!captureOpenedArtifact(entry.original, entry.canonicalPath, entry.directory,
			confirmation))
		{
			SetLastError(43300 + GetLastError());
			return false;
		}
		const bool same = sameFilesystemManifest(entry.canonicalPath,
			entry.originalMetadata, entry.canonicalPath, confirmation, true, true);
		if (!same) SetLastError(43400);
		return same;
	}

	bool prepareRegularReplacement(const std::wstring& sourcePath,
		AtomicInstallEntry& entry, HANDLE targetDirectory)
	{
		std::wstring temporaryLeaf;
		if (!randomSiblingLeaf(L".turborama-new-", temporaryLeaf)) return false;
		entry.replacementCurrentPath = join(parentOf(entry.canonicalPath), temporaryLeaf);
		entry.preparedRoot = entry.replacementCurrentPath;
		FilesystemMetadata source, destination;
		if (!copyOpenedFileToNewPath(sourcePath, entry.replacementCurrentPath,
			entry.replacement, source, destination)) return false;
		entry.sourceManifest.push_back(std::move(source));
		entry.preparedManifest.push_back(std::move(destination));
		if (entry.originalExisted && !restoreFilesystemMetadataToHandle(entry.replacement,
			entry.replacementCurrentPath, entry.originalMetadata.front())) return false;
		std::vector<FilesystemMetadata> restored;
		if (!captureOpenedArtifact(entry.replacement, entry.replacementCurrentPath, false,
			restored)
			|| !sameFilesystemManifest(sourcePath, entry.sourceManifest,
				entry.replacementCurrentPath, restored, false, false)) return false;
		entry.preparedManifest = std::move(restored);
		return entry.originalExisted
			? filesystemMetadataMatchesMapped(entry.canonicalPath,
				entry.replacementCurrentPath, entry.originalMetadata)
			: validateEffectiveInheritedSecurity(targetDirectory,
				parentOf(entry.canonicalPath), entry.replacement,
				entry.replacementCurrentPath, false);
	}

	bool prepareTreeReplacement(const std::wstring& sourcePath,
		AtomicInstallEntry& entry, HANDLE targetDirectory)
	{
		HANDLE source = CreateFileW(sourcePath.c_str(), READ_CONTROL | FILE_READ_ATTRIBUTES
			| FILE_LIST_DIRECTORY | FILE_TRAVERSE, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (source == INVALID_HANDLE_VALUE
			|| !validateOpenedFilesystemObject(source, sourcePath, true))
		{
			if (source != INVALID_HANDLE_VALUE) CloseHandle(source);
			return false;
		}
		std::wstring temporaryLeaf;
		bool ok = randomSiblingLeaf(L".turborama-new-pix-agent-", temporaryLeaf);
		if (ok)
		{
			entry.replacementCurrentPath = join(parentOf(entry.canonicalPath), temporaryLeaf);
			entry.preparedRoot = entry.replacementCurrentPath;
			ok = createPrivateDirectoryHandle(entry.replacementCurrentPath,
				entry.replacement);
		}
		if (!ok) SetLastError(45100);
		if (ok && entry.originalExisted)
		{
			// A raiz recebe a politica anterior antes da primeira criacao de filho;
			// assim nos novos paths a ACL efetiva nasce por heranca dessa raiz.
			ok = restoreFilesystemMetadataToHandle(entry.replacement,
				entry.replacementCurrentPath, entry.originalMetadata.front());
			if (!ok) SetLastError(45150);
		}
		if (ok)
		{
			ok = copyPinnedTreeRecursive(source, sourcePath, entry.replacement,
				entry.replacementCurrentPath, entry.sourceManifest,
				entry.replacementPins);
			if (!ok) SetLastError(45200);
		}
		std::vector<FilesystemMetadata> stableSource;
		if (ok)
		{
			ok = captureFilesystemMetadataTreeFromHandle(source, sourcePath, stableSource)
				&& sameFilesystemManifest(sourcePath, entry.sourceManifest, sourcePath,
					stableSource, true, true);
			if (!ok) SetLastError(45300);
		}
		if (ok)
		{
			ok = capturePinnedReplacement(entry, entry.replacementCurrentPath,
				entry.preparedManifest)
				&& sameFilesystemManifest(sourcePath, entry.sourceManifest,
					entry.replacementCurrentPath, entry.preparedManifest, false, false);
			if (!ok) SetLastError(45400);
		}
		// Metadados da intersecao e heranca de novos nos sao tratados abaixo,
		// sempre pelos pins persistentes da arvore candidata.
		std::vector<FilesystemMetadata> restored;
		if (ok)
		{
			ok = capturePinnedReplacement(entry, entry.replacementCurrentPath, restored);
			if (!ok) SetLastError(45610);
		}
		if (ok)
		{
			ok = sameFilesystemManifest(sourcePath, entry.sourceManifest,
				entry.replacementCurrentPath, restored, false, false);
			if (!ok) SetLastError(45620);
		}
		if (ok)
		{
			// A enumeracao/hash acima pode tocar LastAccess. Reaplica apenas paths/tipos
			// preservados e valida a heranca efetiva dos novos nos, pelos mesmos pins.
			ok = restorePinnedTreeMetadataIntersection(entry, targetDirectory,
				entry.replacementCurrentPath);
			if (!ok) SetLastError(45625);
		}
		if (ok) entry.preparedManifest = std::move(restored);
		CloseHandle(source);
		return ok;
	}

	bool abandonPreparedInstallTransaction(AtomicInstallTransaction& transaction)
	{
		closeTargetMutationGuard(transaction);
		bool ok = true;
		DWORD firstError = ERROR_SUCCESS;
		for (auto& entry : transaction.entries)
		{
			closePinnedArtifactObjects(entry.replacementPins);
			if (entry.replacement != INVALID_HANDLE_VALUE)
			{
				const std::vector<FilesystemMetadata>* expected = entry.preparedManifest.empty()
					? nullptr : &entry.preparedManifest;
				if (!securelyDeleteOpenedObject(entry.replacement,
					entry.replacementCurrentPath, entry.directory, expected,
					entry.preparedRoot))
				{
					if (firstError == ERROR_SUCCESS) firstError = GetLastError();
					ok = false;
				}
			}
			else if (!entry.replacementCurrentPath.empty()
				&& !pathIsMissing(entry.replacementCurrentPath)) ok = false;
			closePinnedArtifactObjects(entry.originalPins);
			const uintptr_t originalValue = reinterpret_cast<uintptr_t>(entry.original);
			entry.original = INVALID_HANDLE_VALUE;
			if (originalValue != reinterpret_cast<uintptr_t>(INVALID_HANDLE_VALUE)
				&& originalValue != 0)
			{
				CloseHandle(reinterpret_cast<HANDLE>(originalValue));
			}
		}
		if (!ok) SetLastError(firstError);
		return ok;
	}

	void closeAtomicInstallTransactionHandles(AtomicInstallTransaction& transaction)
	{
		closeTargetMutationGuard(transaction);
		for (auto& entry : transaction.entries)
		{
			closePinnedArtifactObjects(entry.replacementPins);
			closePinnedArtifactObjects(entry.originalPins);
			if (entry.replacement != INVALID_HANDLE_VALUE)
			{
				CloseHandle(entry.replacement);
				entry.replacement = INVALID_HANDLE_VALUE;
			}
			if (entry.original != INVALID_HANDLE_VALUE)
			{
				CloseHandle(entry.original);
				entry.original = INVALID_HANDLE_VALUE;
			}
		}
	}

	bool prepareInstallTransaction(const std::wstring& staged, const std::wstring& target,
		HANDLE targetDirectory, AtomicInstallTransaction& transaction)
	{
		transaction = {};
		transaction.target = target;
		transaction.targetDirectory = targetDirectory;
		if (targetDirectory == INVALID_HANDLE_VALUE
			|| !validateOpenedFilesystemObject(targetDirectory, target, true,
				&transaction.targetIdentity)
			|| !openTargetMutationGuard(transaction)) return false;
		transaction.entries.resize(4);
		transaction.entries[0].leaf = L"emulationstation.exe";
		transaction.entries[1].leaf = L"CONFIGURAR-USER-TOKEN-PIX.exe";
		transaction.entries[2].leaf = L"CONFIGURAR-ACCESS-TOKEN-PIX.exe";
		transaction.entries[3].leaf = L"pix-agent";
		transaction.entries[3].directory = true;
		for (size_t index = 0; index < transaction.entries.size(); ++index)
		{
			auto& entry = transaction.entries[index];
			entry.canonicalPath = join(target, entry.leaf);
			if (!openAndCaptureOriginal(entry))
			{
				return false;
			}
		}
		for (size_t index = 0; index < transaction.entries.size(); ++index)
		{
			auto& entry = transaction.entries[index];
			const std::wstring source = join(staged, entry.leaf);
			// O filtro Win32 local nao permite criar um novo filho enquanto o parent
			// guard nega share-write. A janela fica limitada a esta criacao autorizada;
			// cada candidato nasce/passa a ficar pinado sem share-write/delete, e o
			// target guard e reaberto/revalidado antes de seguir ao proximo item.
			closeTargetMutationGuard(transaction);
			const bool prepared = entry.directory
				? prepareTreeReplacement(source, entry, targetDirectory)
				: prepareRegularReplacement(source, entry, targetDirectory);
			const bool targetRevalidated = openTargetMutationGuard(transaction);
			if (!prepared || !targetRevalidated)
			{
				if (GetLastError() < 45000)
					SetLastError(static_cast<DWORD>(42000 + index));
				return false;
			}
		}
		return true;
	}

	bool capturePinnedTreeManifest(HANDLE rootHandle,
		const std::vector<PinnedArtifactObject>& pins, const std::wstring& preparedRoot,
		const std::wstring& currentRoot, std::vector<FilesystemMetadata>& manifest);
	bool pinnedTreeShapeMatches(const std::vector<PinnedArtifactObject>& pins,
		const std::wstring& preparedRoot, const std::wstring& currentRoot,
		const std::wstring& currentDirectory, size_t& seen);

	bool originalStillMatchesSnapshot(AtomicInstallEntry& entry)
	{
		if (!entry.originalExisted) return entry.original == INVALID_HANDLE_VALUE
			&& !entry.originalAtTombstone;
		std::vector<FilesystemMetadata> current;
		const bool captured = entry.directory
			? capturePinnedTreeManifest(entry.original, entry.originalPins,
				entry.canonicalPath, entry.originalCurrentPath, current)
			: captureOpenedArtifact(entry.original, entry.originalCurrentPath, false, current);
		return captured
			&& sameFilesystemManifest(entry.canonicalPath, entry.originalMetadata,
				entry.originalCurrentPath, current, true, true);
	}

	bool capturePinnedTreeManifest(HANDLE rootHandle,
		const std::vector<PinnedArtifactObject>& pins, const std::wstring& preparedRoot,
		const std::wstring& currentRoot, std::vector<FilesystemMetadata>& manifest)
	{
		manifest.clear();
		FilesystemMetadata root;
		if (!captureFilesystemMetadataFromHandle(rootHandle, currentRoot, true, root))
			return false;
		manifest.push_back(std::move(root));
		for (const auto& pin : pins)
		{
			const std::wstring relative = manifestRelativePath(preparedRoot,
				pin.preparedPath);
			if (relative.empty() || relative == L"\x0001") return false;
			FilesystemMetadata current;
			if (!captureFilesystemMetadataFromHandle(pin.handle, join(currentRoot, relative),
				pin.directory, current)) return false;
			manifest.push_back(std::move(current));
		}
		size_t seen = 0;
		return pinnedTreeShapeMatches(pins, preparedRoot, currentRoot, currentRoot, seen)
			&& seen == pins.size();
	}

	bool pinnedTreeShapeMatches(const std::vector<PinnedArtifactObject>& pins,
		const std::wstring& preparedRoot, const std::wstring& currentRoot,
		const std::wstring& currentDirectory, size_t& seen)
	{
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(currentDirectory, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE) return false;
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
			{
				ok = false;
				break;
			}
			const bool directory = (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
			const std::wstring currentPath = join(currentDirectory, name);
			const std::wstring relative = manifestRelativePath(currentRoot, currentPath);
			const PinnedArtifactObject* matched = nullptr;
			for (const auto& pin : pins)
				if (pin.directory == directory
					&& manifestRelativePath(preparedRoot, pin.preparedPath) == relative)
				{
					matched = &pin;
					break;
				}
			FILE_ID_INFO identity{};
			if (matched == nullptr || !validateOpenedFilesystemObject(matched->handle,
				currentPath, directory, &identity)
				|| !sameFileIdentity(identity, matched->identity))
			{
				ok = false;
				break;
			}
			++seen;
			if (directory && !pinnedTreeShapeMatches(pins, preparedRoot, currentRoot,
				currentPath, seen))
			{
				ok = false;
				break;
			}
		} while (FindNextFileW(search, &entry));
		const DWORD enumerationError = ok ? GetLastError() : ERROR_SUCCESS;
		FindClose(search);
		return ok && enumerationError == ERROR_NO_MORE_FILES;
	}

	bool restorePinnedOriginalTreeMetadata(AtomicInstallEntry& entry,
		const std::wstring& currentRoot)
	{
		if (!entry.originalExisted || !entry.directory
			|| entry.original == INVALID_HANDLE_VALUE || entry.originalMetadata.empty())
			return false;
		struct Node
		{
			HANDLE handle;
			std::wstring path;
			const FilesystemMetadata* captured;
		};
		std::vector<Node> nodes;
		const FilesystemMetadata* rootMetadata = findMappedMetadataEntry(
			entry.canonicalPath, entry.originalMetadata, {}, true);
		if (rootMetadata == nullptr) return false;
		nodes.push_back({ entry.original, currentRoot, rootMetadata });
		for (auto& pin : entry.originalPins)
		{
			const std::wstring relative = manifestRelativePath(entry.canonicalPath,
				pin.preparedPath);
			const FilesystemMetadata* captured = findMappedMetadataEntry(
				entry.canonicalPath, entry.originalMetadata, relative, pin.directory);
			if (relative.empty() || relative == L"\x0001" || captured == nullptr)
				return false;
			nodes.push_back({ pin.handle, join(currentRoot, relative), captured });
		}
		bool ok = true;
		for (auto& node : nodes)
			if (!restoreCapturedSecurityToHandle(node.handle, node.path,
				node.captured->directory, node.captured->security)) ok = false;
		for (auto iterator = nodes.rbegin(); iterator != nodes.rend(); ++iterator)
			if (!setAndConfirmBasicMetadata(iterator->handle, iterator->path,
				iterator->captured->directory, iterator->captured->basic)) ok = false;
		for (auto& node : nodes)
			if (!pinnedMetadataMatches(node.handle, node.path,
				node.captured->directory, *node.captured)) ok = false;
		return ok;
	}

	bool capturePinnedReplacement(AtomicInstallEntry& entry,
		const std::wstring& currentRoot, std::vector<FilesystemMetadata>& current)
	{
		if (entry.replacement == INVALID_HANDLE_VALUE) return false;
		if (entry.directory) return capturePinnedTreeManifest(entry.replacement,
			entry.replacementPins, entry.preparedRoot, currentRoot, current);
		current.clear();
		FilesystemMetadata file;
		if (!captureFilesystemMetadataFromHandle(entry.replacement, currentRoot, false,
			file)) return false;
		current.push_back(std::move(file));
		return true;
	}

	bool restorePublishedEntryMetadata(AtomicInstallEntry& entry,
		HANDLE targetDirectory, const std::wstring& currentPath)
	{
		if (entry.directory) return restorePinnedTreeMetadataIntersection(entry,
			targetDirectory, currentPath);
		return entry.originalExisted
			? restoreFilesystemMetadataToHandle(entry.replacement, currentPath,
				entry.originalMetadata.front())
			: validateEffectiveInheritedSecurity(targetDirectory, parentOf(entry.canonicalPath),
				entry.replacement, currentPath, false);
	}

	bool validatePinnedReplacementEntry(AtomicInstallEntry& entry,
		HANDLE targetDirectory, const std::wstring& currentPath)
	{
		std::vector<FilesystemMetadata> current;
		bool ok = capturePinnedReplacement(entry, currentPath, current)
			&& !entry.sourceManifest.empty()
			&& sameFilesystemManifest(entry.preparedRoot, entry.preparedManifest,
				currentPath, current, true, false)
			&& sameFilesystemManifest(entry.sourceManifest.front().path,
				entry.sourceManifest, currentPath, current, false, false);
		if (ok) ok = restorePublishedEntryMetadata(entry, targetDirectory, currentPath);
		return ok;
	}

	bool repinTreeAtCurrentRoot(std::vector<PinnedArtifactObject>& pins,
		const std::wstring& logicalRoot, const std::wstring& currentRoot)
	{
		closePinnedArtifactObjects(pins);
		if (!pinArtifactTreeChildren(currentRoot, pins, true)) return false;
		for (auto& pin : pins)
		{
			const std::wstring relative = manifestRelativePath(currentRoot,
				pin.preparedPath);
			if (relative.empty() || relative == L"\x0001") return false;
			pin.preparedPath = join(logicalRoot, relative);
		}
		return true;
	}

	bool repinAndValidateOriginalTree(AtomicInstallEntry& entry,
		const std::wstring& currentRoot)
	{
		if (!entry.directory || !repinTreeAtCurrentRoot(entry.originalPins,
			entry.canonicalPath, currentRoot)) return false;
		std::vector<FilesystemMetadata> current;
		return capturePinnedTreeManifest(entry.original, entry.originalPins,
			entry.canonicalPath, currentRoot, current)
			&& sameFilesystemManifest(entry.canonicalPath, entry.originalMetadata,
				currentRoot, current, true, false)
			&& restorePinnedOriginalTreeMetadata(entry, currentRoot);
	}

	bool repinAndValidateReplacementTree(AtomicInstallEntry& entry,
		HANDLE targetDirectory, const std::wstring& currentRoot)
	{
		return entry.directory && repinTreeAtCurrentRoot(entry.replacementPins,
			entry.preparedRoot, currentRoot)
			&& validatePinnedReplacementEntry(entry, targetDirectory, currentRoot);
	}

	bool publishInstallTransaction(AtomicInstallTransaction& transaction)
	{
		if (transaction.targetDirectory == INVALID_HANDLE_VALUE
			|| !validateOpenedFilesystemObject(transaction.targetDirectory,
				transaction.target, true))
		{
			SetLastError(46000);
			return false;
		}
		for (size_t index = 0; index < transaction.entries.size(); ++index)
			if (!(transaction.entries[index].originalExisted
				? originalStillMatchesSnapshot(transaction.entries[index])
				: pathIsMissing(transaction.entries[index].canonicalPath)))
			{
				SetLastError(static_cast<DWORD>(46100 + index));
				return false;
			}
		for (size_t index = 0; index < transaction.entries.size(); ++index)
			if (!validatePinnedReplacementEntry(transaction.entries[index],
				transaction.targetDirectory,
				transaction.entries[index].replacementCurrentPath))
			{
				SetLastError(static_cast<DWORD>(46150 + index));
				return false;
			}
		closeTargetMutationGuard(transaction);
		for (size_t index = 0; index < transaction.entries.size(); ++index)
		{
			auto& entry = transaction.entries[index];
			if (!entry.originalExisted) continue;
			std::wstring tombstoneLeaf;
			if (!randomSiblingLeaf(L".turborama-old-", tombstoneLeaf))
			{
				SetLastError(static_cast<DWORD>(46200 + index));
				return false;
			}
			entry.tombstonePath = join(transaction.target, tombstoneLeaf);
			if (entry.directory) closePinnedArtifactObjects(entry.originalPins);
			const bool renamed = renameOpenedObject(entry.original,
				transaction.targetDirectory, tombstoneLeaf);
			if (renamed)
			{
				entry.originalAtTombstone = true;
				entry.originalCurrentPath = entry.tombstonePath;
				transaction.publicationStarted = true;
			}
			if (!renamed)
			{
				const DWORD renameError = GetLastError();
				if (entry.directory) (void)repinAndValidateOriginalTree(entry,
					entry.canonicalPath);
				SetLastError(static_cast<DWORD>(47000 + renameError));
				return false;
			}
			if (!validateOpenedFilesystemObject(entry.original,
				entry.originalCurrentPath, entry.directory))
			{
				SetLastError(static_cast<DWORD>(46400 + index));
				return false;
			}
			if (entry.directory && !repinAndValidateOriginalTree(entry,
				entry.originalCurrentPath))
			{
				SetLastError(static_cast<DWORD>(46450 + index));
				return false;
			}
			if (!entry.directory && !restoreFilesystemMetadataToHandle(entry.original,
				entry.originalCurrentPath, entry.originalMetadata.front()))
			{
				SetLastError(static_cast<DWORD>(46470 + index));
				return false;
			}
		}
		for (size_t index = 0; index < transaction.entries.size(); ++index)
		{
			auto& entry = transaction.entries[index];
			if (entry.directory) closePinnedArtifactObjects(entry.replacementPins);
			const bool renamed = renameOpenedObject(entry.replacement,
				transaction.targetDirectory, entry.leaf);
			if (renamed)
			{
				entry.replacementAtCanonical = true;
				entry.replacementCurrentPath = entry.canonicalPath;
				transaction.publicationStarted = true;
			}
			if (!renamed)
			{
				const DWORD renameError = GetLastError();
				if (entry.directory) (void)repinAndValidateReplacementTree(entry,
					transaction.targetDirectory, entry.replacementCurrentPath);
				SetLastError(static_cast<DWORD>(48000 + renameError));
				return false;
			}
			if (entry.directory && !repinAndValidateReplacementTree(entry,
				transaction.targetDirectory, entry.canonicalPath))
			{
				SetLastError(static_cast<DWORD>(46650 + index));
				return false;
			}
			if (!validateOpenedFilesystemObject(entry.replacement,
				entry.canonicalPath, entry.directory))
			{
				SetLastError(static_cast<DWORD>(46600 + index));
				return false;
			}
		}
		bool metadataRestored = true;
		for (auto& entry : transaction.entries)
			if (!restorePublishedEntryMetadata(entry, transaction.targetDirectory,
				entry.canonicalPath)) metadataRestored = false;
		return metadataRestored && openTargetMutationGuard(transaction);
	}

	bool validatePublishedInstallTransaction(AtomicInstallTransaction& transaction)
	{
		bool ok = transaction.publicationStarted
			&& transaction.targetDirectory != INVALID_HANDLE_VALUE
			&& transaction.targetMutationGuard != INVALID_HANDLE_VALUE
			&& validateOpenedFilesystemObject(transaction.targetDirectory,
				transaction.target, true)
			&& validateOpenedFilesystemObject(transaction.targetMutationGuard,
				transaction.target, true);
		for (auto& entry : transaction.entries)
		{
			const bool entryOk = entry.replacementAtCanonical
				&& validatePinnedReplacementEntry(entry, transaction.targetDirectory,
					entry.canonicalPath);
			if (!entryOk) ok = false;
		}
		return ok;
	}

	bool rollbackInstallTransaction(AtomicInstallTransaction& transaction)
	{
		closeTargetMutationGuard(transaction);
		bool ok = true;
		// Primeiro libera todos os nomes canonicos, por rename do candidato; se isso
		// falhar, a exclusao ainda e feita pelo handle ja validado.
		for (auto iterator = transaction.entries.rbegin();
			iterator != transaction.entries.rend(); ++iterator)
		{
			auto& entry = *iterator;
			if (!entry.replacementAtCanonical) continue;
			if (entry.directory) closePinnedArtifactObjects(entry.replacementPins);
			std::wstring discardLeaf;
			const bool moved = randomSiblingLeaf(L".turborama-discard-", discardLeaf)
				&& renameOpenedObject(entry.replacement, transaction.targetDirectory,
					discardLeaf);
			if (moved)
			{
				entry.replacementAtCanonical = false;
				entry.replacementCurrentPath = join(transaction.target, discardLeaf);
				if (!validateOpenedFilesystemObject(entry.replacement,
					entry.replacementCurrentPath, entry.directory)) ok = false;
				if (entry.directory && !repinAndValidateReplacementTree(entry,
					transaction.targetDirectory, entry.replacementCurrentPath)) ok = false;
			}
			else
			{
				closePinnedArtifactObjects(entry.replacementPins);
				const bool removed = securelyDeleteOpenedObject(entry.replacement,
					entry.canonicalPath, entry.directory, &entry.preparedManifest,
					entry.preparedRoot);
				if (!removed) ok = false;
				else entry.replacementAtCanonical = false;
			}
		}

		// Recoloca cada objeto original pelo mesmo handle e pelo mesmo FileId.
		for (auto iterator = transaction.entries.rbegin();
			iterator != transaction.entries.rend(); ++iterator)
		{
			auto& entry = *iterator;
			if (!entry.originalAtTombstone) continue;
			if (entry.directory) closePinnedArtifactObjects(entry.originalPins);
			const bool restored = pathIsMissing(entry.canonicalPath)
				&& renameOpenedObject(entry.original, transaction.targetDirectory, entry.leaf);
			if (restored)
			{
				entry.originalAtTombstone = false;
				entry.originalCurrentPath = entry.canonicalPath;
			}
			if (!restored || !validateOpenedFilesystemObject(entry.original,
				entry.originalCurrentPath, entry.directory)) ok = false;
			if (entry.directory && !repinAndValidateOriginalTree(entry,
				entry.originalCurrentPath)) ok = false;
		}

		// Mesmo se algum rename falhou, tenta todos os metadados capturados.
		for (auto& entry : transaction.entries)
		{
			if (!entry.originalExisted) continue;
			if (entry.original == INVALID_HANDLE_VALUE)
			{
				ok = false;
				continue;
			}
			const bool restored = entry.directory
				? restorePinnedOriginalTreeMetadata(entry, entry.originalCurrentPath)
				: restoreFilesystemMetadataToHandle(entry.original,
					entry.originalCurrentPath, entry.originalMetadata.front());
			if (!restored) ok = false;
		}

		// Remove todos os candidatos/temporarios por handle, sem seguir nomes.
		for (auto& entry : transaction.entries)
		{
			closePinnedArtifactObjects(entry.replacementPins);
			if (entry.replacement != INVALID_HANDLE_VALUE
				&& !securelyDeleteOpenedObject(entry.replacement,
					entry.replacementCurrentPath, entry.directory,
					&entry.preparedManifest, entry.preparedRoot)) ok = false;
			else if (entry.replacement == INVALID_HANDLE_VALUE
				&& !entry.replacementCurrentPath.empty()
				&& !pathIsMissing(entry.replacementCurrentPath)) ok = false;
		}

		for (auto& entry : transaction.entries)
		{
			std::vector<FilesystemMetadata> current;
			const bool captured = entry.originalExisted && entry.directory
				? capturePinnedTreeManifest(entry.original, entry.originalPins,
					entry.canonicalPath, entry.canonicalPath, current)
				: (entry.originalExisted && captureOpenedArtifact(entry.original,
					entry.canonicalPath, false, current));
			const bool exactOriginal = entry.originalExisted
				? entry.original != INVALID_HANDLE_VALUE && !entry.originalAtTombstone
					&& captured && sameFilesystemManifest(entry.canonicalPath,
						entry.originalMetadata, entry.canonicalPath, current, true, true)
					&& (entry.tombstonePath.empty() || pathIsMissing(entry.tombstonePath))
					&& pathIsMissing(entry.preparedRoot)
				: entry.original == INVALID_HANDLE_VALUE && !entry.originalAtTombstone
					&& pathIsMissing(entry.canonicalPath) && pathIsMissing(entry.preparedRoot);
			if (!exactOriginal) ok = false;
			closePinnedArtifactObjects(entry.originalPins);
			const uintptr_t originalValue = reinterpret_cast<uintptr_t>(entry.original);
			entry.original = INVALID_HANDLE_VALUE;
			if (originalValue != reinterpret_cast<uintptr_t>(INVALID_HANDLE_VALUE)
				&& originalValue != 0)
			{
				CloseHandle(reinterpret_cast<HANDLE>(originalValue));
			}
		}
		return ok;
	}

	bool commitInstallTransaction(AtomicInstallTransaction& transaction)
	{
		// Preflight global e somente leitura: nenhum tombstone e apagado enquanto um
		// unico hash, manifesto, pin ou metadado ainda estiver divergente.
		if (!validatePublishedInstallTransaction(transaction)) return false;
		for (auto& entry : transaction.entries)
		{
			if (entry.originalExisted)
			{
				if (!entry.originalAtTombstone || entry.original == INVALID_HANDLE_VALUE
					|| !originalStillMatchesSnapshot(entry)) return false;
			}
			else if (entry.original != INVALID_HANDLE_VALUE || entry.originalAtTombstone
				|| !entry.tombstonePath.empty()) return false;
		}
		transaction.commitStarted = true;
		closeTargetMutationGuard(transaction);
		bool ok = true;
		// Depois deste ponto, a publicacao permanece ativa mesmo se a limpeza falhar.
		// A resposta deve ser erro de limpeza, nunca um falso rollback parcial.
		for (auto& entry : transaction.entries)
		{
			if (!entry.originalExisted) continue;
			if (!entry.originalAtTombstone || entry.original == INVALID_HANDLE_VALUE)
			{
				ok = false;
				if (entry.original != INVALID_HANDLE_VALUE)
				{
					CloseHandle(entry.original);
					entry.original = INVALID_HANDLE_VALUE;
				}
				continue;
			}
			closePinnedArtifactObjects(entry.originalPins);
			if (!securelyDeleteOpenedObject(entry.original, entry.originalCurrentPath,
				entry.directory, &entry.originalMetadata, entry.canonicalPath)) ok = false;
			entry.originalAtTombstone = false;
		}
		for (auto& entry : transaction.entries)
		{
			const bool installedBound = entry.replacement != INVALID_HANDLE_VALUE
				&& entry.replacementAtCanonical
				&& validateOpenedFilesystemObject(entry.replacement, entry.canonicalPath,
					entry.directory);
			if (!installedBound) ok = false;
			if (!entry.tombstonePath.empty() && !pathIsMissing(entry.tombstonePath)) ok = false;
			if (!entry.preparedRoot.empty() && !pathIsMissing(entry.preparedRoot)) ok = false;
		}
		// Reabre o guard de namespace somente depois que todos os tombstones
		// autorizados foram removidos. Ele, os handles raiz e os pins dos filhos
		// publicados permanecem vivos ate a barreira final/termino do processo.
		if (!openTargetMutationGuard(transaction)
			|| !relevantTransactionResiduesAbsent(transaction.target)) ok = false;
		return ok;
	}

	bool cleanupDirectoryTreeByHandle(const std::wstring& directory,
		const FILE_ID_INFO* expectedIdentity = nullptr)
	{
		if (pathIsMissing(directory)) return true;
		auto pinChildrenForDeletion = [&](auto&& self, const std::wstring& current,
			std::vector<PinnedArtifactObject>& pins) -> bool
		{
			WIN32_FIND_DATAW entry{};
			HANDLE search = FindFirstFileW(join(current, L"*").c_str(), &entry);
			if (search == INVALID_HANDLE_VALUE) return GetLastError() == ERROR_FILE_NOT_FOUND;
			bool ok = true;
			do
			{
				const std::wstring name = entry.cFileName;
				if (name == L"." || name == L"..") continue;
				if ((entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
				{
					ok = false;
					break;
				}
				const bool childDirectory = (entry.dwFileAttributes
					& FILE_ATTRIBUTE_DIRECTORY) != 0;
				const std::wstring childPath = join(current, name);
				const DWORD access = DELETE | READ_CONTROL | FILE_READ_ATTRIBUTES
					| FILE_WRITE_ATTRIBUTES | (childDirectory
						? FILE_LIST_DIRECTORY | FILE_TRAVERSE : GENERIC_READ);
				HANDLE child = CreateFileW(childPath.c_str(), access, FILE_SHARE_READ,
					nullptr, OPEN_EXISTING,
					(childDirectory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL)
						| FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
				if (!appendPinnedArtifactObject(childPath, childDirectory, child, pins))
				{
					if (child != INVALID_HANDLE_VALUE) CloseHandle(child);
					ok = false;
					break;
				}
				if (childDirectory && !self(self, childPath, pins))
				{
					ok = false;
					break;
				}
			} while (FindNextFileW(search, &entry));
			const DWORD enumerationError = ok ? GetLastError() : ERROR_SUCCESS;
			FindClose(search);
			return ok && enumerationError == ERROR_NO_MORE_FILES;
		};
		const DWORD access = DELETE | READ_CONTROL | FILE_READ_ATTRIBUTES
			| FILE_WRITE_ATTRIBUTES | FILE_LIST_DIRECTORY | FILE_TRAVERSE;
		HANDLE object = CreateFileW(directory.c_str(), access,
			FILE_SHARE_READ, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (object == INVALID_HANDLE_VALUE) return false;
		FILE_ID_INFO currentIdentity{};
		if (!validateOpenedFilesystemObject(object, directory, true, &currentIdentity)
			|| (expectedIdentity != nullptr
				&& !sameFileIdentity(currentIdentity, *expectedIdentity)))
		{
			CloseHandle(object);
			return false;
		}
		std::vector<PinnedArtifactObject> pins;
		bool preflight = pinChildrenForDeletion(pinChildrenForDeletion, directory, pins);
		size_t seen = 0;
		if (preflight)
			preflight = pinnedTreeShapeMatches(pins, directory, directory, directory, seen)
				&& seen == pins.size()
				&& validateOpenedFilesystemObject(object, directory, true, &currentIdentity)
				&& (expectedIdentity == nullptr
					|| sameFileIdentity(currentIdentity, *expectedIdentity));
		if (!preflight)
		{
			closePinnedArtifactObjects(pins);
			CloseHandle(object);
			return false;
		}

		bool removed = true;
		for (auto iterator = pins.rbegin(); iterator != pins.rend(); ++iterator)
		{
			const bool deleted = iterator->handle != INVALID_HANDLE_VALUE
				&& validateOpenedFilesystemObject(iterator->handle, iterator->preparedPath,
					iterator->directory)
				&& clearReadonlyAttribute(iterator->handle)
				&& markOpenedObjectForDeletion(iterator->handle);
			if (iterator->handle != INVALID_HANDLE_VALUE)
			{
				CloseHandle(iterator->handle);
				iterator->handle = INVALID_HANDLE_VALUE;
			}
			if (!deleted || !pathIsMissing(iterator->preparedPath)) removed = false;
		}
		pins.clear();
		const bool rootDeleted = removed
			&& validateOpenedFilesystemObject(object, directory, true, &currentIdentity)
			&& clearReadonlyAttribute(object) && markOpenedObjectForDeletion(object);
		CloseHandle(object);
		return rootDeleted && pathIsMissing(directory);
	}

	bool directoryHasTransactionResidue(const std::wstring& directory)
	{
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(directory, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE) return true;
		bool residue = false;
		do
		{
			std::wstring name = entry.cFileName;
			std::transform(name.begin(), name.end(), name.begin(), ::towlower);
			if (name.rfind(L".turborama-new-", 0) == 0
				|| name.rfind(L".turborama-old-", 0) == 0
				|| name.rfind(L".turborama-discard-", 0) == 0
				|| name.rfind(L".turborama-write-", 0) == 0)
			{
				residue = true;
				break;
			}
		} while (FindNextFileW(search, &entry));
		const DWORD enumerationError = GetLastError();
		FindClose(search);
		return residue || enumerationError != ERROR_NO_MORE_FILES;
	}

	bool relevantTransactionResiduesAbsent(const std::wstring& target)
	{
		const std::wstring pix = join(target, L".emulationstation\\pix");
		return !directoryHasTransactionResidue(target)
			&& (pathIsMissing(pix) || !directoryHasTransactionResidue(pix));
	}

	bool staleTransactionStatePresent(const std::wstring& source,
		const std::wstring& target)
	{
		const std::wstring rollback = join(source, L"rollback");
		if (!pathIsMissing(rollback)) return true;
		const std::wstring pix = join(target, L".emulationstation\\pix");
		return directoryHasTransactionResidue(target)
			|| directoryHasTransactionResidue(pix);
	}

	std::wstring timestamp()
	{
		SYSTEMTIME time{}; GetLocalTime(&time);
		wchar_t value[64]{};
		swprintf_s(value, L"%04u%02u%02u-%02u%02u%02u-%03u", time.wYear, time.wMonth, time.wDay,
			time.wHour, time.wMinute, time.wSecond, time.wMilliseconds);
		return value;
	}

	enum class ChildRunResult
	{
		Completed,
		Failed,
		TimedOutTreeTerminated,
		TreeStateUnconfirmed
	};

	enum class JobEmptyWaitResult
	{
		Empty,
		TimedOut,
		QueryFailed
	};

	constexpr bool childTreeStateUnconfirmed(ChildRunResult result)
	{
		return result == ChildRunResult::TreeStateUnconfirmed;
	}

	constexpr bool validateChildRunResultContract()
	{
		static_assert(kAuxiliaryTreeUnconfirmedExitCode == 42,
			"O codigo reservado da arvore auxiliar mudou.");
		return !childTreeStateUnconfirmed(ChildRunResult::Completed)
			&& !childTreeStateUnconfirmed(ChildRunResult::Failed)
			&& !childTreeStateUnconfirmed(ChildRunResult::TimedOutTreeTerminated)
			&& childTreeStateUnconfirmed(ChildRunResult::TreeStateUnconfirmed);
	}

	static_assert(validateChildRunResultContract(),
		"O estado critico da arvore auxiliar deve permanecer exclusivo e fail-closed.");

	JobEmptyWaitResult waitForJobEmpty(HANDLE job, DWORD timeoutMs)
	{
		if (job == nullptr || timeoutMs == 0 || timeoutMs == INFINITE)
			return JobEmptyWaitResult::QueryFailed;
		const ULONGLONG deadline = GetTickCount64() + timeoutMs;
		for (;;)
		{
			JOBOBJECT_BASIC_ACCOUNTING_INFORMATION accounting{};
			if (!QueryInformationJobObject(job, JobObjectBasicAccountingInformation,
				&accounting, sizeof(accounting), nullptr)) return JobEmptyWaitResult::QueryFailed;
			if (accounting.ActiveProcesses == 0) return JobEmptyWaitResult::Empty;
			const ULONGLONG now = GetTickCount64();
			if (now >= deadline) return JobEmptyWaitResult::TimedOut;
			DWORD delay = 50;
			const ULONGLONG remaining = deadline - now;
			if (delay > remaining) delay = static_cast<DWORD>(remaining);
			Sleep(delay);
		}
	}

	ChildRunResult terminateJobAndConfirmEmpty(HANDLE job, DWORD safeExitCode,
		ChildRunResult confirmedResult, DWORD& exitCode)
	{
		exitCode = safeExitCode;
		// Mesmo que TerminateJobObject falhe por uma corrida de saida natural, a
		// consulta abaixo e a autoridade: cleanup so e liberado com ActiveProcesses=0.
		TerminateJobObject(job, safeExitCode);
		if (waitForJobEmpty(job, 5000) == JobEmptyWaitResult::Empty)
			return confirmedResult;
		exitCode = ERROR_TIMEOUT;
		SetLastError(ERROR_TIMEOUT);
		return ChildRunResult::TreeStateUnconfirmed;
	}

	ChildRunResult runAndWait(const std::wstring& executable, const std::wstring& arguments,
		DWORD timeoutMs, DWORD& exitCode)
	{
		exitCode = ERROR_SUCCESS;
		if (timeoutMs == 0 || timeoutMs == INFINITE)
		{
			exitCode = ERROR_INVALID_PARAMETER;
			return ChildRunResult::Failed;
		}

		HANDLE job = CreateJobObjectW(nullptr, nullptr);
		if (job == nullptr)
		{
			exitCode = GetLastError();
			return ChildRunResult::Failed;
		}
		JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
		limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
		if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, sizeof(limits)))
		{
			exitCode = GetLastError();
			CloseHandle(job);
			return ChildRunResult::Failed;
		}

		std::wstring command = L"\"" + executable + L"\" " + arguments;
		std::vector<wchar_t> mutableCommand(command.begin(), command.end());
		mutableCommand.push_back(L'\0');
		STARTUPINFOW startup{};
		startup.cb = sizeof(startup);
		startup.dwFlags = STARTF_USESHOWWINDOW;
		startup.wShowWindow = SW_HIDE;
		PROCESS_INFORMATION process{};
		if (!CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
			CREATE_NO_WINDOW | CREATE_SUSPENDED, nullptr, parentOf(executable).c_str(), &startup, &process))
		{
			exitCode = GetLastError();
			CloseHandle(job);
			return ChildRunResult::Failed;
		}

		if (!AssignProcessToJobObject(job, process.hProcess))
		{
			exitCode = GetLastError();
			// O processo ainda esta suspenso e, portanto, nao criou descendentes.
			// Mesmo assim, so classificamos como falha comum apos confirmar sua saida.
			TerminateProcess(process.hProcess, exitCode);
			const DWORD rootWait = WaitForSingleObject(process.hProcess, 5000);
			CloseHandle(process.hThread);
			CloseHandle(process.hProcess);
			CloseHandle(job);
			return rootWait == WAIT_OBJECT_0
				? ChildRunResult::Failed : ChildRunResult::TreeStateUnconfirmed;
		}

		if (ResumeThread(process.hThread) == static_cast<DWORD>(-1))
		{
			const DWORD resumeError = GetLastError();
			CloseHandle(process.hThread);
			const ChildRunResult result = terminateJobAndConfirmEmpty(job, resumeError,
				ChildRunResult::Failed, exitCode);
			CloseHandle(process.hProcess);
			CloseHandle(job);
			return result;
		}
		CloseHandle(process.hThread);

		const JobEmptyWaitResult waitResult = waitForJobEmpty(job, timeoutMs);
		ChildRunResult result = ChildRunResult::Failed;
		if (waitResult == JobEmptyWaitResult::Empty)
		{
			if (GetExitCodeProcess(process.hProcess, &exitCode) && exitCode != STILL_ACTIVE)
				result = ChildRunResult::Completed;
			else
			{
				exitCode = GetLastError();
				if (exitCode == ERROR_SUCCESS) exitCode = ERROR_GEN_FAILURE;
			}
		}
		else
		{
			const DWORD failure = waitResult == JobEmptyWaitResult::TimedOut
				? ERROR_TIMEOUT : ERROR_GEN_FAILURE;
			result = terminateJobAndConfirmEmpty(job, failure,
				waitResult == JobEmptyWaitResult::TimedOut
					? ChildRunResult::TimedOutTreeTerminated : ChildRunResult::Failed,
				exitCode);
		}

		CloseHandle(process.hProcess);
		// KILL_ON_JOB_CLOSE oferece uma ultima barreira no caso critico, mas nao e
		// usado como prova de vazio: o chamador ainda deve preservar tudo.
		CloseHandle(job);
		return result;
	}

	bool validateSensitiveTargetPaths(const std::wstring& target, const std::wstring& launcherConfig)
	{
		if (!validateDirectoryNoReparse(target)
			|| !validateRegularFileNoReparseOrHardlink(join(target, L"emulationstation.exe"))
			|| !validateDirectoryNoReparse(join(target, L".emulationstation"))
			|| !validateRegularFileNoReparseOrHardlink(launcherConfig)) return false;
		const std::wstring agent = join(target, L"pix-agent");
		if (GetFileAttributesW(agent.c_str()) != INVALID_FILE_ATTRIBUTES
			&& !validateTreeNoReparse(agent)) return false;
		const std::wstring pix = join(target, L".emulationstation\\pix");
		// Esta entrega e somente uma atualizacao interna. Criar a ponte PIX em uma
		// primeira instalacao exigiria definir uma ACL/allowlist ainda nao comprovada.
		// Portanto ela precisa preexistir e permanecer integralmente fora de qualquer
		// mutacao de seguranca.
		if (GetFileAttributesW(pix.c_str()) == INVALID_FILE_ATTRIBUTES
			|| !validateTreeNoReparse(pix)) return false;
		return true;
	}

	struct InstallationSecurityContext
	{
		struct PlanEntry
		{
			std::wstring path;
			bool directory = false;
			bool tree = false;
			KioskPermission permission = KioskPermission::ReadExecute;
			bool inheritable = false;
		};

		std::vector<SecurityBackup> backups;
		std::vector<PlanEntry> plan;
		bool createdPixDirectory = false;
		bool createdPixIdentityPresent = false;
		std::wstring createdPixPath;
		FILE_ID_INFO createdPixIdentity{};
		bool mutationAttempted = false;
	};

	bool hasCaseInsensitiveSuffix(const std::wstring& value, const wchar_t* suffix)
	{
		const size_t suffixLength = wcslen(suffix);
		return value.size() >= suffixLength
			&& _wcsicmp(value.c_str() + value.size() - suffixLength, suffix) == 0;
	}

	bool isCodeFileName(const std::wstring& name)
	{
		for (const wchar_t* extension : {
			L".exe", L".dll", L".com", L".scr", L".msi", L".bat", L".cmd",
			L".ps1", L".psm1", L".js", L".jse", L".vbs", L".vbe", L".wsf" })
			if (hasCaseInsensitiveSuffix(name, extension)) return true;
		return false;
	}

	bool validateDataOnlyTree(const std::wstring& directory, SecurityFailure* failure)
	{
		if (!validateTreeNoReparse(directory))
		{
			recordSecurityFailure(failure, L"plano de ACL: arvore de dados insegura", directory,
				GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_DATA : GetLastError());
			return false;
		}
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(directory, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE)
		{
			recordSecurityFailure(failure, L"plano de ACL: enumeracao da arvore de dados", directory,
				GetLastError());
			return false;
		}
		bool ok = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name == L"." || name == L"..") continue;
			const std::wstring path = join(directory, name);
			if ((entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
			{
				if (!validateDataOnlyTree(path, failure)) { ok = false; break; }
			}
			else if (isCodeFileName(name))
			{
				recordSecurityFailure(failure, L"plano de ACL: codigo em arvore gravavel", path,
					ERROR_INVALID_DATA);
				ok = false;
				break;
			}
		} while (FindNextFileW(search, &entry));
		const DWORD enumerationError = ok ? GetLastError() : ERROR_SUCCESS;
		FindClose(search);
		if (ok && enumerationError != ERROR_NO_MORE_FILES)
		{
			recordSecurityFailure(failure,
				L"plano de ACL: enumeracao interrompida na arvore de dados", directory,
				enumerationError);
			ok = false;
		}
		return ok;
	}

	bool appendSecurityPlanEntry(std::vector<InstallationSecurityContext::PlanEntry>& plan,
		const std::wstring& path, bool directory, bool tree, KioskPermission permission,
		bool inheritable, bool required, SecurityFailure* failure)
	{
		const DWORD attributes = GetFileAttributesW(path.c_str());
		if (attributes == INVALID_FILE_ATTRIBUTES)
		{
			const DWORD code = GetLastError();
			if (!required && (code == ERROR_FILE_NOT_FOUND || code == ERROR_PATH_NOT_FOUND)) return true;
			recordSecurityFailure(failure, L"plano de ACL: objeto ausente ou inacessivel", path, code);
			return false;
		}
		if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0
			|| (((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0) != directory))
		{
			recordSecurityFailure(failure, L"plano de ACL: tipo ou redirecionamento", path,
				ERROR_REPARSE_TAG_INVALID);
			return false;
		}
		if (tree && !validateTreeNoReparse(path))
		{
			recordSecurityFailure(failure, L"plano de ACL: arvore insegura", path,
				GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_DATA : GetLastError());
			return false;
		}
		if (!directory && !validateRegularFileNoReparseOrHardlink(path))
		{
			recordSecurityFailure(failure, L"plano de ACL: arquivo inseguro", path,
				GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_DATA : GetLastError());
			return false;
		}
		plan.push_back({ path, directory, tree, permission, inheritable });
		return true;
	}

	bool buildInstallationSecurityPlan(const std::wstring& target,
		std::vector<InstallationSecurityContext::PlanEntry>& plan, SecurityFailure* failure)
	{
		plan.clear();
		// A camada Windows IoT/Factory Pack e externa a este instalador. O plano
		// contem exclusivamente objetos do EmulationStation selecionado em D:.
		if (!appendSecurityPlanEntry(plan, join(target, L"emulationstation.exe"), false, false,
			KioskPermission::ReadExecute, false, true, failure)) return false;
		for (const auto& relative : {
			L"CONFIGURAR-USER-TOKEN-PIX.exe", L"CONFIGURAR-ACCESS-TOKEN-PIX.exe" })
		{
			if (!appendSecurityPlanEntry(plan, join(target, relative), false, false,
				KioskPermission::ReadExecute, false, false, failure)) return false;
		}
		if (!appendSecurityPlanEntry(plan, join(target, L"pix-agent"), true, true,
			KioskPermission::ReadExecute, true, false, failure)) return false;
		const std::wstring emulationstationData = join(target, L".emulationstation");
		const std::wstring pix = join(emulationstationData, L"pix");
		const DWORD pixAttributes = GetFileAttributesW(pix.c_str());
		if (pixAttributes != INVALID_FILE_ATTRIBUTES && (!validateDataOnlyTree(pix, failure)
			|| !appendSecurityPlanEntry(plan, pix, true, true, KioskPermission::Modify,
				true, true, failure))) return false;
		if (pixAttributes == INVALID_FILE_ATTRIBUTES)
		{
			const DWORD code = GetLastError();
			if (code != ERROR_FILE_NOT_FOUND && code != ERROR_PATH_NOT_FOUND)
			{
				recordSecurityFailure(failure, L"plano de ACL: consulta da pasta PIX", pix, code);
				return false;
			}
		}
		return true;
	}

	bool validateWritableDataScopes(const std::wstring& target, SecurityFailure* failure)
	{
		for (const auto& path : { join(target, L".emulationstation\\pix") })
		{
			const DWORD attributes = GetFileAttributesW(path.c_str());
			if (attributes == INVALID_FILE_ATTRIBUTES)
			{
				const DWORD code = GetLastError();
				if (code == ERROR_FILE_NOT_FOUND || code == ERROR_PATH_NOT_FOUND) continue;
				recordSecurityFailure(failure, L"validacao final da arvore de dados gravavel",
					path, code);
				return false;
			}
			if ((attributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT))
				!= FILE_ATTRIBUTE_DIRECTORY || !validateDataOnlyTree(path, failure))
			{
				recordSecurityFailure(failure, L"validacao final da arvore de dados gravavel",
					path, ERROR_INVALID_DATA);
				return false;
			}
		}
		return true;
	}

	bool captureInstallationSecurity(const std::wstring& target,
		InstallationSecurityContext& context, SecurityFailure* failure)
	{
		context = {};
		if (!buildInstallationSecurityPlan(target, context.plan, failure)) return false;
		for (const auto& entry : context.plan)
		{
			if (entry.tree)
			{
				if (!captureSecurityTree(entry.path, entry.directory, context.backups, failure)) return false;
			}
			else if (!captureSecurityBackup(entry.path, entry.directory, context.backups, failure)) return false;
		}
		return true;
	}

	bool restoreInstallationSecurity(const std::wstring& target, InstallationSecurityContext& context,
		SecurityFailure* failure = nullptr)
	{
		bool ok = true;
		if (context.createdPixDirectory)
		{
			const std::wstring pix = join(target, L".emulationstation\\pix");
			SecurityBackup created;
			created.path = pix;
			created.directory = true;
			created.fileIdentity = context.createdPixIdentity;
			if (!context.createdPixIdentityPresent
				|| _wcsicmp(context.createdPixPath.c_str(), pix.c_str()) != 0
				|| !validateSecurityBackupIdentity(created, failure,
					L"rollback da ACL: pasta PIX criada substituida")
				|| !cleanupDirectoryTreeByHandle(pix, &context.createdPixIdentity))
			{
				if (failure == nullptr || failure->empty())
					recordSecurityFailure(failure, L"rollback da ACL: remocao da pasta PIX criada", pix,
						GetLastError() == ERROR_SUCCESS ? ERROR_GEN_FAILURE : GetLastError());
				ok = false;
			}
		}
		if (!restoreSecurityBackups(context.backups, failure)) ok = false;
		return ok;
	}

	bool applyInstallationSecurityPlan(
		const std::vector<InstallationSecurityContext::PlanEntry>& plan,
		const ResolvedIdentity& kioskIdentity, SecurityFailure* failure,
		const ResolvedIdentity* fullControlIdentity = nullptr,
		const ResolvedIdentity* ownerIdentity = nullptr,
		const ResolvedIdentity* auxiliaryAccessIdentity = nullptr,
		const InstallationSecurityContext* expectedContext = nullptr)
	{
		auto validateSnapshot = [&]()
		{
			if (expectedContext == nullptr) return true;
			for (const auto& backup : expectedContext->backups)
				if (!validateSecurityBackupIdentity(backup, failure,
					L"aplicacao da DACL: snapshot substituido")) return false;
			if (expectedContext->createdPixDirectory)
			{
				if (!expectedContext->createdPixIdentityPresent
					|| expectedContext->createdPixPath.empty())
				{
					recordSecurityFailure(failure,
						L"aplicacao da DACL: identidade da pasta PIX criada ausente",
						expectedContext->createdPixPath, ERROR_FILE_INVALID);
					return false;
				}
				SecurityBackup created;
				created.path = expectedContext->createdPixPath;
				created.directory = true;
				created.fileIdentity = expectedContext->createdPixIdentity;
				if (!validateSecurityBackupIdentity(created, failure,
					L"aplicacao da DACL: pasta PIX criada substituida")) return false;
			}
			return true;
		};
		if (!validateSnapshot()) return false;
		for (const auto& entry : plan)
		{
			const FILE_ID_INFO* expectedIdentity = nullptr;
			if (expectedContext != nullptr && !entry.tree)
			{
				const SecurityBackup* backup = findSecurityBackup(expectedContext->backups,
					entry.path, entry.directory);
				if (backup == nullptr)
				{
					recordSecurityFailure(failure, L"aplicacao da DACL: objeto fora do snapshot",
						entry.path, ERROR_FILE_INVALID);
					return false;
				}
				expectedIdentity = &backup->fileIdentity;
			}
			const bool applied = entry.tree
				? applyKioskSecurityTree(entry.path, entry.directory, kioskIdentity, entry.permission,
					entry.inheritable, failure, fullControlIdentity, ownerIdentity,
					auxiliaryAccessIdentity,
					expectedContext != nullptr ? &expectedContext->backups : nullptr,
					expectedContext != nullptr ? &expectedContext->createdPixPath : nullptr,
					expectedContext != nullptr && expectedContext->createdPixIdentityPresent
						? &expectedContext->createdPixIdentity : nullptr)
				: applyKioskSecurity(entry.path, entry.directory, kioskIdentity, entry.permission,
					entry.inheritable, failure, fullControlIdentity, ownerIdentity,
					auxiliaryAccessIdentity, expectedIdentity);
			if (!applied) return false;
		}
		return validateSnapshot();
	}

	bool hardenInstallationSecurity(const std::wstring& target,
		const ResolvedIdentity& kioskIdentity, InstallationSecurityContext& context,
		SecurityFailure* failure, const ResolvedIdentity* fullControlIdentity = nullptr,
		const ResolvedIdentity* ownerIdentity = nullptr,
		const ResolvedIdentity* auxiliaryAccessIdentity = nullptr)
	{
		if (!captureInstallationSecurity(target, context, failure)) return false;
		const std::wstring pix = join(target, L".emulationstation\\pix");
		if (GetFileAttributesW(pix.c_str()) == INVALID_FILE_ATTRIBUTES)
		{
			context.mutationAttempted = true;
			if (!CreateDirectoryW(pix.c_str(), nullptr))
			{
				recordSecurityFailure(failure, L"criacao da pasta PIX", pix, GetLastError());
				return false;
			}
			context.createdPixDirectory = true;
			HANDLE pixHandle = CreateFileW(pix.c_str(), 0,
				FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
				FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			DWORD bindingError = ERROR_SUCCESS;
			if (pixHandle == INVALID_HANDLE_VALUE
				|| !validateOpenedFilesystemObject(pixHandle, pix, true,
					&context.createdPixIdentity, &bindingError))
			{
				recordSecurityFailure(failure, L"validacao da pasta PIX criada", pix,
					pixHandle == INVALID_HANDLE_VALUE ? GetLastError() : bindingError);
				if (pixHandle != INVALID_HANDLE_VALUE) CloseHandle(pixHandle);
				return false;
			}
			CloseHandle(pixHandle);
			context.createdPixIdentityPresent = true;
			context.createdPixPath = pix;
			context.plan.push_back({ pix, true, true, KioskPermission::Modify, true });
		}
		context.mutationAttempted = true;
		return applyInstallationSecurityPlan(context.plan, kioskIdentity, failure,
			fullControlIdentity, ownerIdentity, auxiliaryAccessIdentity, &context);
	}

	bool hardenInstalledApplication(const std::wstring& target, const ResolvedIdentity& kioskIdentity,
		SecurityFailure* failure)
	{
		std::vector<SecurityBackup> snapshot;
		for (const auto& relative : {
			L"emulationstation.exe",
			L"CONFIGURAR-USER-TOKEN-PIX.exe",
			L"CONFIGURAR-ACCESS-TOKEN-PIX.exe" })
		{
			const std::wstring path = join(target, relative);
			if (!captureSecurityBackup(path, false, snapshot, failure))
			{
				recordSecurityFailure(failure, L"validacao do arquivo de payload instalado", path,
					GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_DATA : GetLastError());
				return false;
			}
			const SecurityBackup* backup = findSecurityBackup(snapshot, path, false);
			if (backup == nullptr) return false;
			if (!applyKioskSecurity(path, false, kioskIdentity, KioskPermission::ReadExecute,
				false, failure, nullptr, nullptr, nullptr, &backup->fileIdentity)) return false;
		}
		const std::wstring agent = join(target, L"pix-agent");
		if (!captureSecurityTree(agent, true, snapshot, failure))
		{
			recordSecurityFailure(failure, L"validacao do agente PIX instalado", agent,
				GetLastError() == ERROR_SUCCESS ? ERROR_INVALID_DATA : GetLastError());
			return false;
		}
		return applyKioskSecurityTree(agent, true, kioskIdentity,
			KioskPermission::ReadExecute, true, failure, nullptr, nullptr, nullptr, &snapshot);
	}

	bool beginsWith(const std::wstring& value, const std::wstring& prefix)
	{
		return value.size() >= prefix.size() && value.compare(0, prefix.size(), prefix) == 0;
	}

	bool rebindAuthorizedRollbackIdentities(InstallationSecurityContext& context,
		const std::set<std::wstring>& exactPaths, const std::wstring& treeRoot,
		SecurityFailure* failure)
	{
		std::set<std::wstring> normalizedExactPaths;
		for (const auto& path : exactPaths) normalizedExactPaths.insert(normalized(path));
		const std::wstring normalizedTreeRoot = treeRoot.empty() ? std::wstring()
			: normalized(treeRoot);
		auto isAuthorized = [&](const std::wstring& path)
		{
			const std::wstring value = normalized(path);
			return normalizedExactPaths.find(value) != normalizedExactPaths.end()
				|| (!normalizedTreeRoot.empty()
					&& (value == normalizedTreeRoot
						|| beginsWith(value, normalizedTreeRoot + L"\\")));
		};

		bool ok = true;
		for (auto& backup : context.backups)
		{
			HANDLE object = CreateFileW(backup.path.c_str(), 0,
				FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
				FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			if (object == INVALID_HANDLE_VALUE)
			{
				recordSecurityFailure(failure, L"rebind do rollback: abertura", backup.path,
					GetLastError());
				ok = false;
				continue;
			}
			FILE_ID_INFO current{};
			DWORD bindingError = ERROR_SUCCESS;
			const bool valid = validateOpenedFilesystemObject(object, backup.path,
				backup.directory, &current, &bindingError);
			CloseHandle(object);
			if (!valid)
			{
				recordSecurityFailure(failure, L"rebind do rollback: vinculo caminho/objeto",
					backup.path, bindingError);
				ok = false;
				continue;
			}
			if (sameFileIdentity(current, backup.fileIdentity)) continue;
			if (!isAuthorized(backup.path))
			{
				recordSecurityFailure(failure, L"rebind do rollback: troca fora da allowlist",
					backup.path, ERROR_FILE_INVALID);
				ok = false;
				continue;
			}
			// Estes paths sao recriados apenas pelos restores transacionais confirmados
			// imediatamente antes desta chamada. Todo objeto fora da allowlist precisa
			// conservar o FileId capturado ou o rollback e declarado incompleto.
			backup.fileIdentity = current;
		}
		if (context.createdPixDirectory)
		{
			SecurityBackup created;
			created.path = context.createdPixPath;
			created.directory = true;
			created.fileIdentity = context.createdPixIdentity;
			if (!context.createdPixIdentityPresent
				|| !validateSecurityBackupIdentity(created, failure,
					L"rebind do rollback: pasta PIX criada substituida")) ok = false;
		}
		return ok;
	}

	const std::wstring& installLogFileName()
	{
		static const std::wstring name = L"installation-"
			+ std::wstring(kReleaseTag) + L".log";
		return name;
	}

	const std::array<std::wstring, 12>& pixTransactionalStateNames()
	{
		static const std::array<std::wstring, 12> names = {
			L"credential-agent-key.dat",
			L"agent-public-key.pem",
			L"credential-update.json",
			L"credential-update-status.json",
			L"credential-replay.dat",
			L"agent-status.json",
			L"owner-setup-status.json",
			L"public-options.json",
			L"kiosk-identity.sid",
			L"owner-reenrollment-required.json",
			installLogFileName(),
			L"agent-stop.request"
		};
		return names;
	}

	struct PixStateBackup
	{
		struct File
		{
			std::wstring name;
			bool existed = false;
			std::vector<unsigned char> content;
			FilesystemMetadata metadata;
			bool currentExisted = false;
			HANDLE currentPin = INVALID_HANDLE_VALUE;
			std::vector<unsigned char> currentContent;
			FilesystemMetadata currentMetadata;
		};
		bool directoryStateCaptured = false;
		bool pixDirectoryExisted = false;
		FILE_ID_INFO pixDirectoryIdentity{};
		HANDLE currentDirectoryGuard = INVALID_HANDLE_VALUE;
		bool writerFailure = false;
		bool writerRollbackComplete = true;
		bool writerResidueFree = true;
		std::vector<File> files;
	};

	void closePixStatePins(PixStateBackup& backup)
	{
		if (backup.currentDirectoryGuard != INVALID_HANDLE_VALUE)
		{
			CloseHandle(backup.currentDirectoryGuard);
			backup.currentDirectoryGuard = INVALID_HANDLE_VALUE;
		}
		for (size_t index = 0; index < backup.files.size(); ++index)
		{
			auto& file = backup.files[index];
			if (file.currentPin != INVALID_HANDLE_VALUE)
				CloseHandle(file.currentPin);
			file.currentPin = INVALID_HANDLE_VALUE;
		}
	}

	PixStateBackup::File* findPixStateFile(PixStateBackup& backup,
		const std::wstring& name)
	{
		for (auto& file : backup.files)
			if (_wcsicmp(file.name.c_str(), name.c_str()) == 0) return &file;
		return nullptr;
	}

	bool pixTemporaryResidueAbsent(const std::wstring& pix)
	{
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(pix, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE) return false;
		bool clean = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name.rfind(L".turborama-", 0) == 0)
			{
				clean = false;
				break;
			}
		} while (FindNextFileW(search, &entry));
		FindClose(search);
		return clean;
	}

	bool readOpenedFileContent(HANDLE object, const FilesystemMetadata& captured,
		std::vector<unsigned char>& content)
	{
		constexpr LONGLONG kMaximumPixStateFile = 16LL * 1024 * 1024;
		if (object == INVALID_HANDLE_VALUE || captured.directory
			|| captured.size.QuadPart < 0 || captured.size.QuadPart > kMaximumPixStateFile)
			return false;
		if (!SetFilePointerEx(object, {}, nullptr, FILE_BEGIN)) return false;
		content.assign(static_cast<size_t>(captured.size.QuadPart), 0);
		size_t offset = 0;
		while (offset < content.size())
		{
			const DWORD requested = static_cast<DWORD>(std::min<size_t>(
				content.size() - offset, 1024 * 1024));
			DWORD received = 0;
			if (!ReadFile(object, content.data() + offset, requested, &received, nullptr)
				|| received == 0) return false;
			offset += received;
		}
		unsigned char extra = 0;
		DWORD extraReceived = 0;
		return ReadFile(object, &extra, 1, &extraReceived, nullptr) != FALSE
			&& extraReceived == 0;
	}

	bool samePixFileSnapshot(const PixStateBackup::File& expected,
		const FilesystemMetadata& current, const std::vector<unsigned char>& content,
		bool requireSameIdentity)
	{
		return expected.existed && content == expected.content
			&& current.size.QuadPart == expected.metadata.size.QuadPart
			&& sameHash(current.hash.data(), expected.metadata.hash.data())
			&& sameStableBasicMetadata(current.basic, expected.metadata.basic)
			&& current.security.daclProtected == expected.metadata.security.daclProtected
			&& current.security.descriptor == expected.metadata.security.descriptor
			&& (!requireSameIdentity || sameFileIdentity(current.security.fileIdentity,
				expected.metadata.security.fileIdentity));
	}

	bool openPixDirectoryGuard(const std::wstring& target, PixStateBackup& backup)
	{
		const std::wstring pix = join(target, L".emulationstation\\pix");
		if (backup.currentDirectoryGuard != INVALID_HANDLE_VALUE)
			CloseHandle(backup.currentDirectoryGuard);
		backup.currentDirectoryGuard = CreateFileW(pix.c_str(), READ_CONTROL
			| FILE_READ_ATTRIBUTES | FILE_LIST_DIRECTORY | FILE_TRAVERSE
			| FILE_ADD_FILE | FILE_ADD_SUBDIRECTORY,
			FILE_SHARE_READ, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		FILE_ID_INFO identity{};
		if (backup.currentDirectoryGuard == INVALID_HANDLE_VALUE
			|| !validateOpenedFilesystemObject(backup.currentDirectoryGuard, pix, true,
				&identity)
			|| !backup.pixDirectoryExisted
			|| !sameFileIdentity(identity, backup.pixDirectoryIdentity))
		{
			if (backup.currentDirectoryGuard != INVALID_HANDLE_VALUE)
				CloseHandle(backup.currentDirectoryGuard);
			backup.currentDirectoryGuard = INVALID_HANDLE_VALUE;
			return false;
		}
		return true;
	}

	bool validatePinnedPixCurrentState(const std::wstring& target,
		PixStateBackup& backup)
	{
		const std::wstring pix = join(target, L".emulationstation\\pix");
		bool ok = backup.currentDirectoryGuard != INVALID_HANDLE_VALUE
			&& validateOpenedFilesystemObject(backup.currentDirectoryGuard, pix, true);
		for (size_t index = 0; index < backup.files.size(); ++index)
		{
			auto& file = backup.files[index];
			const std::wstring path = join(pix, file.name);
			if (!file.currentExisted)
			{
				if (file.currentPin != INVALID_HANDLE_VALUE || !pathIsMissing(path)) ok = false;
				continue;
			}
			FilesystemMetadata current;
			std::vector<unsigned char> content;
			bool matched = file.currentPin != INVALID_HANDLE_VALUE
				&& captureFilesystemMetadataFromHandle(file.currentPin, path, false, current)
				&& readOpenedFileContent(file.currentPin, current, content)
				&& content == file.currentContent
				&& sameFileIdentity(current.security.fileIdentity,
					file.currentMetadata.security.fileIdentity)
				&& sameStableBasicMetadata(current.basic, file.currentMetadata.basic)
				&& current.security.daclProtected
					== file.currentMetadata.security.daclProtected
				&& current.security.descriptor == file.currentMetadata.security.descriptor;
			if (matched)
			{
				FILE_BASIC_INFO desired = file.currentMetadata.basic;
				FILE_BASIC_INFO confirmed{};
				matched = SetFileInformationByHandle(file.currentPin, FileBasicInfo,
					&desired, sizeof(desired)) != FALSE
					&& GetFileInformationByHandleEx(file.currentPin, FileBasicInfo,
						&confirmed, sizeof(confirmed)) != FALSE
					&& sameBasicMetadata(confirmed, desired);
			}
			if (!matched)
			{
				SetLastError(static_cast<DWORD>(56000 + index));
				ok = false;
			}
		}
		return ok && pixTemporaryResidueAbsent(pix);
	}

	bool acceptPinnedPixSecurityTransition(const std::wstring& target,
		PixStateBackup& backup)
	{
		const std::wstring pix = join(target, L".emulationstation\\pix");
		if (backup.currentDirectoryGuard == INVALID_HANDLE_VALUE
			|| !validateOpenedFilesystemObject(backup.currentDirectoryGuard, pix, true))
			return false;

		for (auto& file : backup.files)
		{
			const std::wstring path = join(pix, file.name);
			if (!file.currentExisted)
			{
				if (file.currentPin != INVALID_HANDLE_VALUE || !pathIsMissing(path)) return false;
				continue;
			}

			FilesystemMetadata current;
			std::vector<unsigned char> content;
			if (file.currentPin == INVALID_HANDLE_VALUE
				|| !captureFilesystemMetadataFromHandle(file.currentPin, path, false, current)
				|| !readOpenedFileContent(file.currentPin, current, content)
				|| content != file.currentContent
				|| !sameFileIdentity(current.security.fileIdentity,
					file.currentMetadata.security.fileIdentity)
				|| current.basic.CreationTime.QuadPart
					!= file.currentMetadata.basic.CreationTime.QuadPart
				|| current.basic.LastWriteTime.QuadPart
					!= file.currentMetadata.basic.LastWriteTime.QuadPart
				|| current.basic.FileAttributes != file.currentMetadata.basic.FileAttributes)
				return false;

			// A aplicacao autorizada da DACL altera o descritor e pode alterar ChangeTime.
			// Conteudo, FileId e os metadados de dados continuam fixados pelo handle exato.
			file.currentMetadata.basic = current.basic;
			file.currentMetadata.security = std::move(current.security);
		}
		return validatePinnedPixCurrentState(target, backup);
	}

	bool finalizePixStatePins(const std::wstring& target, PixStateBackup& backup)
	{
		if (!validatePinnedPixCurrentState(target, backup)) return false;
		closePixStatePins(backup);
		return backup.writerRollbackComplete && backup.writerResidueFree
			&& pixTemporaryResidueAbsent(
			join(target, L".emulationstation\\pix"));
	}

	bool freezePixStateAfterAgentStop(const std::wstring& target,
		PixStateBackup& backup)
	{
		closePixStatePins(backup);
		if (backup.files.size() != pixTransactionalStateNames().size()
			|| !openPixDirectoryGuard(target, backup))
		{
			SetLastError(57100);
			return false;
		}
		const std::wstring pix = join(target, L".emulationstation\\pix");
		const std::vector<unsigned char> markerExpected = {
			'i','n','s','t','a','l','l','e','r','-','u','p','d','a','t','e','\n'
		};
		bool ok = true;
		for (size_t index = 0; index < pixTransactionalStateNames().size(); ++index)
		{
			auto* file = findPixStateFile(backup, pixTransactionalStateNames()[index]);
			if (file == nullptr) { SetLastError(static_cast<DWORD>(57200 + index)); ok = false; break; }
			const std::wstring path = join(pix, file->name);
			file->currentPin = CreateFileW(path.c_str(), GENERIC_READ | DELETE | READ_CONTROL
				| FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			if (file->currentPin == INVALID_HANDLE_VALUE)
			{
				const DWORD error = GetLastError();
				file->currentExisted = false;
				if (index == pixTransactionalStateNames().size() - 1 || file->existed
					|| (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND))
				{
					SetLastError(static_cast<DWORD>(57220 + index));
					ok = false;
					break;
				}
				continue;
			}
			file->currentExisted = true;
			if (!captureFilesystemMetadataFromHandle(file->currentPin, path, false,
				file->currentMetadata)
				|| !readOpenedFileContent(file->currentPin, file->currentMetadata,
					file->currentContent))
			{
				SetLastError(static_cast<DWORD>(57300 + index));
				ok = false;
				break;
			}
			if (index + 1 == pixTransactionalStateNames().size())
				ok = file->currentContent == markerExpected;
			else ok = samePixFileSnapshot(*file, file->currentMetadata,
				file->currentContent, true);
			if (ok && file->existed)
			{
				ok = restoreFilesystemMetadataToHandle(file->currentPin, path,
					file->metadata);
				if (ok)
				{
					file->currentMetadata.basic = file->metadata.basic;
					file->currentMetadata.security.daclProtected
						= file->metadata.security.daclProtected;
					file->currentMetadata.security.descriptor
						= file->metadata.security.descriptor;
				}
			}
			else if (ok && index + 1 == pixTransactionalStateNames().size())
				ok = validateEffectiveInheritedSecurity(backup.currentDirectoryGuard, pix,
					file->currentPin, path, false);
			if (!ok)
			{
				if (GetLastError() < 57000)
					SetLastError(static_cast<DWORD>(57400 + index));
				break;
			}
		}
		if (ok) ok = validatePinnedPixCurrentState(target, backup);
		if (!ok) closePixStatePins(backup);
		return ok;
	}

	bool backupPixStateRange(const std::wstring& target, const std::wstring& transactionBackup,
		PixStateBackup& backup, size_t first, size_t last, bool reset)
	{
		(void)transactionBackup;
		if (reset)
		{
			closePixStatePins(backup);
			backup = {};
		}
		if (first > last || last > pixTransactionalStateNames().size()) return false;
		const std::wstring pix = join(target, L".emulationstation\\pix");
		HANDLE pixDirectory = CreateFileW(pix.c_str(), READ_CONTROL | FILE_READ_ATTRIBUTES
			| FILE_LIST_DIRECTORY | FILE_TRAVERSE, FILE_SHARE_READ,
			nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		bool currentPixExists = pixDirectory != INVALID_HANDLE_VALUE;
		FILE_ID_INFO currentPixIdentity{};
		if (!currentPixExists)
		{
			const DWORD error = GetLastError();
			if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND)
				return false;
		}
		else if (!validateOpenedFilesystemObject(pixDirectory, pix, true,
			&currentPixIdentity))
		{
			CloseHandle(pixDirectory);
			return false;
		}
		if (!backup.directoryStateCaptured)
		{
			backup.directoryStateCaptured = true;
			backup.pixDirectoryExisted = currentPixExists;
			if (currentPixExists) backup.pixDirectoryIdentity = currentPixIdentity;
		}
		else if (backup.pixDirectoryExisted != currentPixExists
			|| (currentPixExists && !sameFileIdentity(backup.pixDirectoryIdentity,
				currentPixIdentity)))
		{
			if (pixDirectory != INVALID_HANDLE_VALUE) CloseHandle(pixDirectory);
			return false;
		}
		if (!currentPixExists) return first == last;

		size_t totalBytes = 0;
		for (const auto& captured : backup.files)
		{
			if (captured.content.size() > 64 * 1024 * 1024 - totalBytes)
			{
				CloseHandle(pixDirectory);
				return false;
			}
			totalBytes += captured.content.size();
		}
		for (size_t index = first; index < last; ++index)
		{
			const std::wstring& name = pixTransactionalStateNames()[index];
			PixStateBackup::File file;
			file.name = name;
			const std::wstring source = join(pix, name);
			HANDLE object = CreateFileW(source.c_str(), GENERIC_READ | READ_CONTROL
				| FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT
					| FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
			if (object == INVALID_HANDLE_VALUE)
			{
				const DWORD error = GetLastError();
				if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND)
				{
					CloseHandle(pixDirectory);
					return false;
				}
				backup.files.push_back(std::move(file));
				continue;
			}
			file.existed = true;
			FilesystemMetadata confirmation;
			bool captured = captureFilesystemMetadataFromHandle(object, source, false,
				file.metadata)
				&& readOpenedFileContent(object, file.metadata, file.content)
				&& captureFilesystemMetadataFromHandle(object, source, false, confirmation);
			if (captured)
			{
				const std::vector<FilesystemMetadata> firstSnapshot{ file.metadata };
				const std::vector<FilesystemMetadata> secondSnapshot{ confirmation };
				captured = sameFilesystemManifest(source, firstSnapshot, source,
					secondSnapshot, true, true)
					&& file.content.size() == static_cast<size_t>(file.metadata.size.QuadPart)
					&& file.content.size() <= 64 * 1024 * 1024 - totalBytes;
			}
			CloseHandle(object);
			if (!captured)
			{
				CloseHandle(pixDirectory);
				return false;
			}
			totalBytes += file.content.size();
			backup.files.push_back(std::move(file));
		}
		FILE_ID_INFO confirmedPixIdentity{};
		const bool intact = validateOpenedFilesystemObject(pixDirectory, pix, true,
			&confirmedPixIdentity)
			&& sameFileIdentity(currentPixIdentity, confirmedPixIdentity);
		CloseHandle(pixDirectory);
		return intact;
	}

	bool backupAgentStopRequest(const std::wstring& target,
		const std::wstring& transactionBackup, PixStateBackup& backup)
	{
		return backupPixStateRange(target, transactionBackup, backup,
			pixTransactionalStateNames().size() - 1, pixTransactionalStateNames().size(), true);
	}

	bool completePixStateBackup(const std::wstring& target,
		const std::wstring& transactionBackup, PixStateBackup& backup)
	{
		return backupPixStateRange(target, transactionBackup, backup, 0,
			pixTransactionalStateNames().size() - 1, false);
	}

	void recordPixWriterResult(PixStateBackup& backup, bool succeeded,
		const AtomicFileReplaceResult& result)
	{
		if (!succeeded) backup.writerFailure = true;
		backup.writerRollbackComplete = backup.writerRollbackComplete
			&& result.rollbackComplete;
		backup.writerResidueFree = backup.writerResidueFree && result.residueFree;
	}

	bool mutatePinnedPixFileWrite(const std::wstring& target, PixStateBackup& backup,
		const std::wstring& name, const std::vector<unsigned char>& bytes)
	{
		auto* file = findPixStateFile(backup, name);
		if (file == nullptr || !validatePinnedPixCurrentState(target, backup)) return false;
		const std::wstring pix = join(target, L".emulationstation\\pix");
		const std::wstring path = join(pix, name);
		const bool previouslyExisted = file->currentExisted;
		const FilesystemMetadata previousMetadata = file->currentMetadata;
		HANDLE exactOriginal = INVALID_HANDLE_VALUE;
		if (previouslyExisted)
		{
			if (file->currentPin == INVALID_HANDLE_VALUE) return false;
			exactOriginal = file->currentPin;
			file->currentPin = INVALID_HANDLE_VALUE; // ownership transferred to writer
		}
		else if (file->currentPin != INVALID_HANDLE_VALUE) return false;
		if (backup.currentDirectoryGuard != INVALID_HANDLE_VALUE)
		{
			CloseHandle(backup.currentDirectoryGuard);
			backup.currentDirectoryGuard = INVALID_HANDLE_VALUE;
		}
		AtomicFileReplaceResult writerResult;
		HANDLE retained = INVALID_HANDLE_VALUE;
		const bool written = replaceFileBytesAtomically(path, bytes, nullptr,
			&writerResult, &retained, exactOriginal);
		recordPixWriterResult(backup, written, writerResult);
		if (!written || retained == INVALID_HANDLE_VALUE)
		{
			if (retained != INVALID_HANDLE_VALUE) CloseHandle(retained);
			return false;
		}
		file->currentPin = retained;
		file->currentExisted = true;
		file->currentContent.clear();
		file->currentMetadata = {};
		bool ok = captureFilesystemMetadataFromHandle(retained, path, false,
			file->currentMetadata)
			&& readOpenedFileContent(retained, file->currentMetadata,
				file->currentContent)
			&& file->currentContent == bytes
			&& openPixDirectoryGuard(target, backup);
		if (ok && previouslyExisted)
		{
			ok = sameStableBasicMetadata(file->currentMetadata.basic,
				previousMetadata.basic)
				&& file->currentMetadata.security.daclProtected
					== previousMetadata.security.daclProtected
				&& file->currentMetadata.security.descriptor
					== previousMetadata.security.descriptor;
			if (ok)
			{
				ok = restoreFilesystemMetadataToHandle(file->currentPin, path,
					previousMetadata);
				if (ok)
				{
					const FILE_ID_INFO identity = file->currentMetadata.security.fileIdentity;
					file->currentMetadata = previousMetadata;
					file->currentMetadata.path = path;
					file->currentMetadata.security.path = path;
					file->currentMetadata.security.fileIdentity = identity;
				}
			}
		}
		else if (ok)
		{
			ok = validateEffectiveInheritedSecurity(backup.currentDirectoryGuard, pix,
				file->currentPin, path, false);
			if (ok)
			{
				FILE_BASIC_INFO desired = file->currentMetadata.basic;
				ok = SetFileInformationByHandle(file->currentPin, FileBasicInfo,
					&desired, sizeof(desired)) != FALSE;
			}
		}
		return ok && validatePinnedPixCurrentState(target, backup);
	}

	bool mutatePinnedPixFileDelete(const std::wstring& target, PixStateBackup& backup,
		const std::wstring& name)
	{
		auto* file = findPixStateFile(backup, name);
		if (file == nullptr || !validatePinnedPixCurrentState(target, backup)) return false;
		if (!file->currentExisted) return true;
		if (file->currentPin == INVALID_HANDLE_VALUE) return false;
		const std::wstring path = join(join(target, L".emulationstation\\pix"), name);
		HANDLE exact = file->currentPin;
		file->currentPin = INVALID_HANDLE_VALUE; // ownership transferred below
		if (backup.currentDirectoryGuard != INVALID_HANDLE_VALUE)
		{
			CloseHandle(backup.currentDirectoryGuard);
			backup.currentDirectoryGuard = INVALID_HANDLE_VALUE;
		}
		const bool deleted = validateOpenedFilesystemObject(exact, path, false)
			&& clearReadonlyAttribute(exact) && markOpenedObjectForDeletion(exact);
		CloseHandle(exact);
		if (!deleted || !pathIsMissing(path)) return false;
		file->currentExisted = false;
		file->currentContent.clear();
		file->currentMetadata = {};
		return openPixDirectoryGuard(target, backup)
			&& validatePinnedPixCurrentState(target, backup);
	}

	bool mutatePinnedPixFileWriteUtf8(const std::wstring& target,
		PixStateBackup& backup, const std::wstring& name, const std::wstring& text)
	{
		if (text.empty()) return false;
		const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(),
			static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
		if (size <= 0) return false;
		std::vector<unsigned char> bytes(static_cast<size_t>(size));
		return WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(),
			static_cast<int>(text.size()), reinterpret_cast<char*>(bytes.data()), size,
			nullptr, nullptr) == size
			&& mutatePinnedPixFileWrite(target, backup, name, bytes);
	}

	bool freezeRestoredPixState(const std::wstring& target, PixStateBackup& backup)
	{
		closePixStatePins(backup);
		if (!openPixDirectoryGuard(target, backup))
		{
			SetLastError(54990);
			return false;
		}
		const std::wstring pix = join(target, L".emulationstation\\pix");
		bool ok = true;
		for (size_t index = 0; index < backup.files.size(); ++index)
		{
			auto& file = backup.files[index];
			const std::wstring path = join(pix, file.name);
			if (!file.existed)
			{
				file.currentExisted = false;
				if (!pathIsMissing(path))
				{
					SetLastError(static_cast<DWORD>(55000 + index * 10));
					ok = false;
					break;
				}
				continue;
			}
			file.currentPin = CreateFileW(path.c_str(), GENERIC_READ | DELETE | READ_CONTROL
				| FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			file.currentExisted = file.currentPin != INVALID_HANDLE_VALUE;
			if (!file.currentExisted
				|| !captureFilesystemMetadataFromHandle(file.currentPin, path, false,
					file.currentMetadata)
				|| !readOpenedFileContent(file.currentPin, file.currentMetadata,
					file.currentContent)
				|| !samePixFileSnapshot(file, file.currentMetadata,
					file.currentContent, false))
			{
				SetLastError(static_cast<DWORD>(55001 + index * 10));
				ok = false;
				break;
			}
			if (!restoreFilesystemMetadataToHandle(file.currentPin, path, file.metadata))
			{
				SetLastError(static_cast<DWORD>(55002 + index * 10));
				ok = false;
				break;
			}
			const FILE_ID_INFO identity = file.currentMetadata.security.fileIdentity;
			file.currentMetadata = file.metadata;
			file.currentMetadata.path = path;
			file.currentMetadata.security.path = path;
			file.currentMetadata.security.fileIdentity = identity;
		}
		if (ok && !validatePinnedPixCurrentState(target, backup))
		{
			SetLastError(55990);
			ok = false;
		}
		if (!ok) closePixStatePins(backup);
		return ok;
	}

	bool restorePixState(const std::wstring& target, PixStateBackup& backup)
	{
		if (!backup.directoryStateCaptured) return false;
		closePixStatePins(backup);
		const std::wstring pix = join(target, L".emulationstation\\pix");
		HANDLE pixDirectory = CreateFileW(pix.c_str(), READ_CONTROL | FILE_READ_ATTRIBUTES
			| FILE_LIST_DIRECTORY | FILE_TRAVERSE,
			FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (pixDirectory == INVALID_HANDLE_VALUE)
		{
			const DWORD error = GetLastError();
			return !backup.pixDirectoryExisted
				&& (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND);
		}
		FILE_ID_INFO currentPixIdentity{};
		bool ok = backup.pixDirectoryExisted
			&& validateOpenedFilesystemObject(pixDirectory, pix, true, &currentPixIdentity)
			&& sameFileIdentity(currentPixIdentity, backup.pixDirectoryIdentity);
		CloseHandle(pixDirectory);
		pixDirectory = INVALID_HANDLE_VALUE;
		ok = ok && backup.writerRollbackComplete && backup.writerResidueFree;
		DWORD firstError = ok ? ERROR_SUCCESS : GetLastError();
		for (size_t index = 0; index < backup.files.size(); ++index)
		{
			const auto& file = backup.files[index];
			const std::wstring destination = join(pix, file.name);
			AtomicFileReplaceResult writerResult;
			const bool restored = file.existed
				? replaceFileBytesAtomically(destination, file.content, &file.metadata,
					&writerResult, nullptr)
				: removeRegularFileIfPresent(destination);
			if (file.existed) recordPixWriterResult(backup, restored, writerResult);
			if (!restored)
			{
				if (firstError == ERROR_SUCCESS)
					firstError = GetLastError() != ERROR_SUCCESS ? GetLastError()
						: static_cast<DWORD>(51000 + index);
				ok = false;
			}
		}
		if (!pixTemporaryResidueAbsent(pix) || !freezeRestoredPixState(target, backup))
			ok = false;
		if (!ok && firstError != ERROR_SUCCESS) SetLastError(firstError);
		return ok;
	}

	bool removeRegularFileIfPresent(const std::wstring& path)
	{
		HANDLE object = CreateFileW(path.c_str(), DELETE | FILE_READ_ATTRIBUTES
			| FILE_WRITE_ATTRIBUTES | GENERIC_READ, FILE_SHARE_READ,
			nullptr, OPEN_EXISTING,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (object == INVALID_HANDLE_VALUE)
		{
			const DWORD error = GetLastError();
			return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND;
		}
		const bool removed = validateOpenedFilesystemObject(object, path, false)
			&& clearReadonlyAttribute(object) && markOpenedObjectForDeletion(object);
		CloseHandle(object);
		return removed && pathIsMissing(path);
	}

	bool resetCredentialEditorState(const std::wstring& target,
		PixStateBackup* backup = nullptr)
	{
		const std::wstring pix = join(target, L".emulationstation\\pix");
		// Instalacoes anteriores deixavam essa ponte editavel por usuarios
		// autenticados. Rotacionamos chaves/recibos temporarios e apagamos
		// estados publicos antigos; secret.dat, cadastro do dono, creditos,
		// ROMs e temas sao preservados.
		for (size_t index = 0; index < 8; ++index)
			if (!(backup != nullptr
				? mutatePinnedPixFileDelete(target, *backup,
					pixTransactionalStateNames()[index])
				: removeRegularFileIfPresent(join(pix,
					pixTransactionalStateNames()[index]))))
				return false;
		return true;
	}

	struct KioskIdentityTransition
	{
		bool previousIdentityWasPresent = false;
		bool reEnrollmentRequired = false;
	};

	bool readRecordedKioskIdentity(const std::wstring& target, const ResolvedIdentity& current,
		KioskIdentityTransition& transition)
	{
		transition = {};
		const std::wstring file = join(target, L".emulationstation\\pix\\kiosk-identity.sid");
		if (GetFileAttributesW(file.c_str()) == INVALID_FILE_ATTRIBUTES) return true;
		std::wstring recorded;
		if (!validateRegularFileNoReparseOrHardlink(file) || !readUtf8FileStrict(file, recorded))
		{
			transition.previousIdentityWasPresent = true;
			transition.reEnrollmentRequired = true;
			return true;
		}
		while (!recorded.empty() && (recorded.back() == L'\r' || recorded.back() == L'\n'
			|| recorded.back() == L' ' || recorded.back() == L'\t')) recorded.pop_back();
		LPWSTR canonical = nullptr;
		PSID sid = nullptr;
		const bool valid = ConvertStringSidToSidW(recorded.c_str(), &sid) != FALSE
			&& ConvertSidToStringSidW(sid, &canonical) != FALSE && canonical != nullptr;
		if (sid) LocalFree(sid);
		if (!valid)
		{
			if (canonical) LocalFree(canonical);
			transition.previousIdentityWasPresent = true;
			transition.reEnrollmentRequired = true;
			return true;
		}
		transition.previousIdentityWasPresent = true;
		transition.reEnrollmentRequired = _wcsicmp(canonical, current.sidText.c_str()) != 0;
		LocalFree(canonical);
		return true;
	}

	bool writeUtf8FileForSelfTest(const std::wstring& destination, const std::wstring& text)
	{
		if (text.empty()) return false;
		const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), (int)text.size(),
			nullptr, 0, nullptr, nullptr);
		if (size <= 0) return false;
		std::vector<char> bytes((size_t)size);
		if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), (int)text.size(), bytes.data(),
			size, nullptr, nullptr) != size) return false;
		const std::wstring temporary = destination + L"." + timestamp() + L".tmp";
		HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (file == INVALID_HANDLE_VALUE) return false;
		DWORD written = 0;
		bool ok = WriteFile(file, bytes.data(), (DWORD)bytes.size(), &written, nullptr) != FALSE
			&& written == bytes.size() && FlushFileBuffers(file) != FALSE;
		CloseHandle(file);
		if (ok) ok = MoveFileExW(temporary.c_str(), destination.c_str(),
			MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE;
		if (!ok) DeleteFileW(temporary.c_str());
		return ok;
	}

	bool openedFileMetadataMatches(HANDLE object, const std::wstring& path,
		const FilesystemMetadata& expected)
	{
		if (object == INVALID_HANDLE_VALUE || expected.directory) return false;
		FILE_ID_INFO identity{};
		FILE_BASIC_INFO basic{};
		DWORD bindingError = ERROR_SUCCESS;
		if (!validateOpenedFilesystemObject(object, path, false, &identity, &bindingError)
			|| !GetFileInformationByHandleEx(object, FileBasicInfo, &basic, sizeof(basic))
			|| !sameBasicMetadata(basic, expected.basic)) return false;
		SecurityBackup security = expected.security;
		security.path = path;
		security.directory = false;
		security.fileIdentity = identity;
		return securityBackupMatchesObject(object, security, nullptr);
	}

	bool restoreFilesystemMetadataToPinnedFile(HANDLE pinned, const std::wstring& path,
		const FilesystemMetadata& expected)
	{
		if (pinned == INVALID_HANDLE_VALUE || expected.directory) return false;
		FILE_ID_INFO pinnedIdentity{};
		if (!validateOpenedFilesystemObject(pinned, path, false, &pinnedIdentity)) return false;

		bool securityRestored = restoreCapturedSecurityToHandle(pinned, path, false,
			expected.security);
		const std::array<DWORD, 3> optionalAccess = {
			WRITE_DAC | WRITE_OWNER, WRITE_DAC, 0
		};
		for (DWORD additional : optionalAccess)
		{
			if (securityRestored) break;
			HANDLE metadataHandle = CreateFileW(path.c_str(), READ_CONTROL
				| FILE_READ_ATTRIBUTES | additional, FILE_SHARE_READ | FILE_SHARE_WRITE,
				nullptr, OPEN_EXISTING,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			FILE_ID_INFO metadataIdentity{};
			securityRestored = metadataHandle != INVALID_HANDLE_VALUE
				&& validateOpenedFilesystemObject(metadataHandle, path, false,
					&metadataIdentity)
				&& sameFileIdentity(pinnedIdentity, metadataIdentity)
				&& restoreCapturedSecurityToHandle(metadataHandle, path, false,
					expected.security);
			if (metadataHandle != INVALID_HANDLE_VALUE) CloseHandle(metadataHandle);
		}
		FILE_BASIC_INFO basic = expected.basic;
		const bool basicRestored = SetFileInformationByHandle(pinned, FileBasicInfo,
			&basic, sizeof(basic)) != FALSE;
		FILE_ID_INFO confirmedIdentity{};
		return securityRestored && basicRestored
			&& validateOpenedFilesystemObject(pinned, path, false, &confirmedIdentity)
			&& sameFileIdentity(pinnedIdentity, confirmedIdentity)
			&& openedFileMetadataMatches(pinned, path, expected);
	}

	bool replaceFileBytesAtomically(const std::wstring& destination,
		const std::vector<unsigned char>& bytes,
		const FilesystemMetadata* desiredMetadata, AtomicFileReplaceResult* result,
		HANDLE* retainedPin, HANDLE pinnedOriginal)
	{
		if (result != nullptr) *result = {};
		if (retainedPin != nullptr) *retainedPin = INVALID_HANDLE_VALUE;
		HANDLE original = pinnedOriginal;
		bool existed = original != INVALID_HANDLE_VALUE;
		bool originalStateKnown = existed;
		auto failBeforeCandidate = [&]()
		{
			if (original != INVALID_HANDLE_VALUE) CloseHandle(original);
			original = INVALID_HANDLE_VALUE;
			return false;
		};
		int failureStage = 1;
		if (bytes.size() > MAXDWORD || (desiredMetadata != nullptr
			&& (desiredMetadata->directory || !desiredMetadata->contentCaptured
				|| desiredMetadata->size.QuadPart < 0
				|| static_cast<ULONGLONG>(desiredMetadata->size.QuadPart) != bytes.size())))
			return failBeforeCandidate();
		const std::wstring parentPath = parentOf(destination);
		const std::wstring destinationLeaf = PathFindFileNameW(destination.c_str());
		if (parentPath.empty() || destinationLeaf.empty() || destinationLeaf == L"."
			|| destinationLeaf == L".."
			|| destinationLeaf.find_first_of(L"\\/") != std::wstring::npos)
			return failBeforeCandidate();
		HANDLE parent = CreateFileW(parentPath.c_str(), FILE_READ_ATTRIBUTES | FILE_TRAVERSE,
			FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (parent == INVALID_HANDLE_VALUE
			|| !validateOpenedFilesystemObject(parent, parentPath, true))
		{
			if (parent != INVALID_HANDLE_VALUE) CloseHandle(parent);
			return failBeforeCandidate();
		}

		std::wstring temporaryLeaf;
		if (!randomSiblingLeaf(L".turborama-write-", temporaryLeaf))
		{
			CloseHandle(parent);
			return failBeforeCandidate();
		}
		const std::wstring temporaryPath = join(parentPath, temporaryLeaf);
		const DWORD replacementAccess = GENERIC_READ | GENERIC_WRITE | DELETE
			| READ_CONTROL | WRITE_DAC | WRITE_OWNER | FILE_READ_ATTRIBUTES
			| FILE_WRITE_ATTRIBUTES;
		const DWORD originalAccess = GENERIC_READ | DELETE | READ_CONTROL
			| FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES;
		HANDLE replacement = CreateFileW(temporaryPath.c_str(), replacementAccess,
			FILE_SHARE_READ, nullptr, CREATE_NEW,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH | FILE_FLAG_OPEN_REPARSE_POINT,
			nullptr);
		if (replacement == INVALID_HANDLE_VALUE)
		{
			CloseHandle(parent);
			return failBeforeCandidate();
		}
		DWORD written = 0;
		failureStage = 2;
		bool replacementAtDestination = false;
		std::wstring replacementPath = temporaryPath;
		bool ok = validateOpenedFilesystemObject(replacement, replacementPath, false);
		size_t writeOffset = 0;
		while (ok && writeOffset < bytes.size())
		{
			const DWORD requested = static_cast<DWORD>(std::min<size_t>(
				bytes.size() - writeOffset, 1024 * 1024));
			written = 0;
			ok = WriteFile(replacement, bytes.data() + writeOffset, requested,
				&written, nullptr) != FALSE && written != 0;
			writeOffset += written;
		}
		if (ok) ok = FlushFileBuffers(replacement) != FALSE;
		auto replacementMatches = [&]()
		{
			if (!validateOpenedFilesystemObject(replacement, replacementPath, false)) return false;
			FILE_STANDARD_INFO standard{};
			if (!GetFileInformationByHandleEx(replacement, FileStandardInfo, &standard,
				sizeof(standard))
				|| standard.EndOfFile.QuadPart != static_cast<LONGLONG>(bytes.size())
				|| !SetFilePointerEx(replacement, {}, nullptr, FILE_BEGIN)) return false;
			std::vector<unsigned char> confirmed(bytes.size());
			size_t readOffset = 0;
			while (readOffset < confirmed.size())
			{
				const DWORD requested = static_cast<DWORD>(std::min<size_t>(
					confirmed.size() - readOffset, 1024 * 1024));
				DWORD received = 0;
				if (!ReadFile(replacement, confirmed.data() + readOffset, requested,
					&received, nullptr) || received == 0) return false;
				readOffset += received;
			}
			unsigned char extra = 0;
			DWORD extraRead = 0;
			return ReadFile(replacement, &extra, 1, &extraRead, nullptr) != FALSE
				&& extraRead == 0 && confirmed == bytes;
		};
		if (ok) ok = replacementMatches();
		if (ok && desiredMetadata != nullptr)
		{
			std::array<unsigned char, 32> candidateHash{};
			ok = hashHandle(replacement, candidateHash.data())
				&& sameHash(candidateHash.data(), desiredMetadata->hash.data());
		}

		FilesystemMetadata originalMetadata;
		bool originalAtTombstone = false;
		std::wstring tombstoneLeaf, tombstonePath;
		if (ok)
		{
			failureStage = 3;
			if (original != INVALID_HANDLE_VALUE)
				ok = validateOpenedFilesystemObject(original, destination, false);
			else
			{
				original = CreateFileW(destination.c_str(), originalAccess,
					FILE_SHARE_READ, nullptr, OPEN_EXISTING,
					FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
				if (original == INVALID_HANDLE_VALUE)
				{
					const DWORD error = GetLastError();
					if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND)
						ok = false;
					else originalStateKnown = true;
				}
				else
				{
					existed = true;
					originalStateKnown = true;
				}
			}
		}
		if (ok && existed)
		{
			failureStage = 4;
			ok = captureFilesystemMetadataFromHandle(original, destination, false,
					originalMetadata)
				&& randomSiblingLeaf(L".turborama-old-", tombstoneLeaf);
			if (ok)
			{
				failureStage = 5;
				tombstonePath = join(parentPath, tombstoneLeaf);
				const bool renamed = renameOpenedObject(original, parent, tombstoneLeaf);
				if (renamed) originalAtTombstone = true;
				ok = renamed && validateOpenedFilesystemObject(original, tombstonePath, false);
			}
		}
		if (ok)
		{
			failureStage = 6;
			const bool renamed = renameOpenedObject(replacement, parent, destinationLeaf);
			if (renamed)
			{
				replacementAtDestination = true;
				replacementPath = destination;
			}
			ok = renamed && validateOpenedFilesystemObject(replacement, destination, false);
		}
		if (ok) ok = replacementMatches();
		const FilesystemMetadata* metadataToApply = desiredMetadata != nullptr
			? desiredMetadata : (existed ? &originalMetadata : nullptr);
		if (ok && metadataToApply != nullptr)
		{
			failureStage = 7;
			ok = restoreFilesystemMetadataToHandle(replacement, destination,
				*metadataToApply);
		}

		auto rollback = [&]()
		{
			bool restored = true;
			std::wstring discardPath;
			if (replacementAtDestination)
			{
				std::wstring discardLeaf;
				const bool moved = randomSiblingLeaf(L".turborama-discard-", discardLeaf)
					&& renameOpenedObject(replacement, parent, discardLeaf);
				if (moved)
				{
					replacementAtDestination = false;
					discardPath = join(parentPath, discardLeaf);
					replacementPath = discardPath;
				}
				else
				{
					const bool attributesCleared = clearReadonlyAttribute(replacement);
					const bool deletionMarked = markOpenedObjectForDeletion(replacement);
					if (!attributesCleared || !deletionMarked) restored = false;
					CloseHandle(replacement);
					replacement = INVALID_HANDLE_VALUE;
					if (!deletionMarked || !pathIsMissing(destination)) restored = false;
					else replacementAtDestination = false;
				}
			}
			if (originalAtTombstone)
			{
				const bool movedBack = pathIsMissing(destination)
					&& renameOpenedObject(original, parent, destinationLeaf);
				const std::wstring originalPath = movedBack ? destination : tombstonePath;
				if (movedBack) originalAtTombstone = false;
				FILE_ID_INFO identity{};
				FILE_STANDARD_INFO standard{};
				std::array<unsigned char, 32> digest{};
				const bool contentRestored = validateOpenedFilesystemObject(original,
					originalPath, false, &identity)
					&& sameFileIdentity(identity, originalMetadata.security.fileIdentity)
					&& GetFileInformationByHandleEx(original, FileStandardInfo, &standard,
						sizeof(standard)) != FALSE
					&& standard.EndOfFile.QuadPart == originalMetadata.size.QuadPart
					&& hashHandle(original, digest.data())
					&& sameHash(digest.data(), originalMetadata.hash.data());
				if (!movedBack || !contentRestored) restored = false;
				if (!restoreFilesystemMetadataToHandle(original, originalPath,
					originalMetadata)) restored = false;
			}
			if (replacement != INVALID_HANDLE_VALUE)
			{
				const bool attributesCleared = clearReadonlyAttribute(replacement);
				const bool deletionMarked = markOpenedObjectForDeletion(replacement);
				if (!attributesCleared || !deletionMarked) restored = false;
				CloseHandle(replacement);
				replacement = INVALID_HANDLE_VALUE;
				if (!deletionMarked || !pathIsMissing(replacementPath)) restored = false;
			}
			if (originalStateKnown && !existed && !pathIsMissing(destination)) restored = false;
			if (!discardPath.empty() && !pathIsMissing(discardPath)) restored = false;
			if (!temporaryPath.empty() && !pathIsMissing(temporaryPath)) restored = false;
			if (originalAtTombstone || (!tombstonePath.empty()
				&& !pathIsMissing(tombstonePath))) restored = false;
			return restored;
		};

		if (!ok)
		{
			const DWORD operationError = GetLastError();
			const bool rolledBack = rollback();
			if (result != nullptr)
			{
				result->rollbackComplete = rolledBack;
				result->residueFree = pathIsMissing(temporaryPath)
					&& (tombstonePath.empty() || pathIsMissing(tombstonePath))
					&& !replacementAtDestination;
			}
			if (original != INVALID_HANDLE_VALUE) CloseHandle(original);
			CloseHandle(parent);
			SetLastError(static_cast<DWORD>(52000 + failureStage * 100
				+ (rolledBack ? (operationError % 100) : 99)));
			return false;
		}
		if (original != INVALID_HANDLE_VALUE)
		{
			if (!clearReadonlyAttribute(original) || !markOpenedObjectForDeletion(original))
			{
				const bool rolledBack = rollback();
				if (result != nullptr)
				{
					result->rollbackComplete = rolledBack;
					result->residueFree = pathIsMissing(temporaryPath)
						&& (tombstonePath.empty() || pathIsMissing(tombstonePath))
						&& !replacementAtDestination;
				}
				if (replacement != INVALID_HANDLE_VALUE) CloseHandle(replacement);
				CloseHandle(original);
				CloseHandle(parent);
				return false;
			}
			CloseHandle(original);
			original = INVALID_HANDLE_VALUE;
		}
		const bool clean = (tombstonePath.empty() || pathIsMissing(tombstonePath))
			&& pathIsMissing(temporaryPath)
			&& validateRegularFileNoReparseOrHardlink(destination);
		if (result != nullptr) result->residueFree = clean;
		if (clean && retainedPin != nullptr)
		{
			*retainedPin = replacement;
			replacement = INVALID_HANDLE_VALUE;
		}
		if (replacement != INVALID_HANDLE_VALUE) CloseHandle(replacement);
		CloseHandle(parent);
		return clean;
	}

	bool writeUtf8FilePreservingObject(const std::wstring& destination,
		const std::wstring& text)
	{
		if (text.empty()) return false;
		const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(),
			static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
		if (size <= 0) return false;
		std::vector<unsigned char> bytes(static_cast<size_t>(size));
		if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(),
			static_cast<int>(text.size()), reinterpret_cast<char*>(bytes.data()), size,
			nullptr, nullptr) != size) return false;
		return replaceFileBytesAtomically(destination, bytes, nullptr);
	}

	std::wstring writeSecurityIncidentLog(const SecurityFailure& primaryFailure,
		bool rollbackAttempted, bool rollbackComplete, const SecurityFailure& rollbackFailure)
	{
		PWSTR programData = nullptr;
		if (FAILED(SHGetKnownFolderPath(FOLDERID_ProgramData, KF_FLAG_DEFAULT, nullptr, &programData))
			|| programData == nullptr) return {};
		const std::wstring directory = join(programData, L"TurboRamaInstallerLogs");
		CoTaskMemFree(programData);
		if (!ensureDirectory(directory) || !validateDirectoryNoReparse(directory)
			|| !applyAdminOnlySecurity(directory, true)) return {};
		const std::wstring file = join(directory, L"security-" + std::wstring(kReleaseTag) + L"-"
			+ timestamp() + L"-pid" + std::to_wstring(GetCurrentProcessId()) + L".log");
		std::wstring text = L"TurboRama PIX Comercial " + std::wstring(kReleaseTag)
			+ L" - falha de seguranca da instalacao\r\nFalha primaria:\r\n"
			+ securityFailureText(primaryFailure) + L"\r\nRollback de ACL: ";
		if (!rollbackAttempted) text += L"nao necessario (nenhuma mutacao iniciada)";
		else text += rollbackComplete ? L"confirmado" : L"INCOMPLETO";
		if (!rollbackComplete && !rollbackFailure.empty())
			text += L"\r\nFalha do rollback:\r\n" + securityFailureText(rollbackFailure);
		text += L"\r\n";
		if (!writeUtf8FilePreservingObject(file, text)
			|| !validateRegularFileNoReparseOrHardlink(file)
			|| !applyAdminOnlySecurity(file, false))
		{
			DeleteFileW(file.c_str());
			return {};
		}
		return file;
	}

	bool recordKioskIdentity(const std::wstring& target, const ResolvedIdentity& identity,
		const KioskIdentityTransition& transition, PixStateBackup* backup = nullptr)
	{
		// SID canonico ja registrado: nao reescreva o mesmo objeto, pois ate um
		// replace de conteudo identico mudaria owner/DACL herdada e atributos.
		if (transition.previousIdentityWasPresent && !transition.reEnrollmentRequired)
			return true;
		const std::wstring pix = join(target, L".emulationstation\\pix");
		const std::wstring identityFile = join(pix, L"kiosk-identity.sid");
		if (!(backup != nullptr
			? mutatePinnedPixFileWriteUtf8(target, *backup, L"kiosk-identity.sid",
				identity.sidText + L"\n")
			: writeUtf8FilePreservingObject(identityFile, identity.sidText + L"\n")))
			return false;
		if (!transition.reEnrollmentRequired) return true;
		const std::wstring notice = join(pix, L"owner-reenrollment-required.json");
		const std::wstring json = L"{\"schemaVersion\":1,\"state\":\"recadastro_required\","
			L"\"reason\":\"kiosk_sid_changed\",\"message\":\"O usuario do quiosque mudou; recadastre a credencial PIX do proprietario. secret.dat foi preservado e nao sera reutilizado por outro SID.\"}\n";
		return backup != nullptr
			? mutatePinnedPixFileWriteUtf8(target, *backup,
				L"owner-reenrollment-required.json", json)
			: writeUtf8FilePreservingObject(notice, json);
	}

	ChildRunResult prepareCredentialEditor(const std::wstring& target, bool& prepared)
	{
		prepared = false;
		const std::wstring runtime = join(target, L"pix-agent\\runtime\\dotnet.exe");
		const std::wstring agent = join(target, L"pix-agent\\TurboRamaPixAgent.dll");
		const std::wstring bridge = join(target, L".emulationstation\\pix");
		if (!exists(runtime) || !exists(agent)) return ChildRunResult::Failed;
		DWORD exitCode = 999;
		const std::wstring arguments = L"\"" + agent + L"\" --prepare-credential-editor --bridge \"" + bridge + L"\"";
		const ChildRunResult result = runAndWait(runtime, arguments, 2 * 60 * 1000, exitCode);
		prepared = result == ChildRunResult::Completed && exitCode == 0
			&& exists(join(bridge, L"agent-public-key.pem"));
		return result;
	}

	bool writeInstallLog(const std::wstring& target, bool editorPrepared,
		bool reEnrollmentRequired, PixStateBackup* backup = nullptr)
	{
		const std::wstring directory = join(target, L".emulationstation\\pix");
		if (backup == nullptr && !ensureDirectory(directory)) return false;
		const std::wstring file = join(directory, installLogFileName());
		const std::wstring text = L"TurboRama PIX Comercial " + std::wstring(kReleaseTag) + L" instalado com sucesso.\r\n"
			+ L"Backup transacional temporario: usado somente para rollback durante a instalacao e removido apos o sucesso; nenhum backup persistente foi criado."
			+ L"\r\nPonte segura do editor: " + (editorPrepared ? L"preparada" : L"sera preparada ao abrir o EmulationStation")
			+ L"\r\nFactory Pack, wrapper, Launcher e cache: preservados; fora do escopo desta atualizacao."
			+ L"\r\nRecadastro PIX: " + (reEnrollmentRequired
				? L"obrigatorio: o SID do quiosque foi alterado; secret.dat foi preservado e bloqueado para o novo SID."
				: L"nao necessario.") + L"\r\n";
		return backup != nullptr
			? mutatePinnedPixFileWriteUtf8(target, *backup, installLogFileName(), text)
			: writeUtf8FilePreservingObject(file, text);
	}

	bool hashRegularFile(const std::wstring& path, std::array<unsigned char, 32>& digest)
	{
		HANDLE file = CreateFileW(path.c_str(), GENERIC_READ,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT
				| FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
		if (file == INVALID_HANDLE_VALUE) return false;
		const bool ok = validateOpenedFilesystemObject(file, path, false)
			&& hashHandle(file, digest.data());
		CloseHandle(file);
		return ok;
	}

	bool securityDescriptorSddl(HANDLE object, std::wstring& text)
	{
		text.clear();
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		const SECURITY_INFORMATION information = OWNER_SECURITY_INFORMATION
			| GROUP_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION;
		const DWORD result = GetSecurityInfo(object, SE_FILE_OBJECT, information,
			nullptr, nullptr, nullptr, nullptr, &descriptor);
		if (result != ERROR_SUCCESS || descriptor == nullptr) return false;
		LPWSTR converted = nullptr;
		const bool ok = ConvertSecurityDescriptorToStringSecurityDescriptorW(descriptor,
			SDDL_REVISION_1, information, &converted, nullptr) != FALSE && converted != nullptr;
		if (ok) text = converted;
		if (converted != nullptr) LocalFree(converted);
		LocalFree(descriptor);
		return ok;
	}

	struct PinnedFileEvidence
	{
		HANDLE handle = INVALID_HANDLE_VALUE;
		std::wstring path;
		FILE_ID_INFO identity{};
		std::array<unsigned char, 32> hash{};
		std::wstring sddl;
	};

	void closePinnedEvidence(PinnedFileEvidence& evidence)
	{
		if (evidence.handle != INVALID_HANDLE_VALUE) CloseHandle(evidence.handle);
		evidence.handle = INVALID_HANDLE_VALUE;
	}

	bool pinReadOnlyFileEvidence(const std::wstring& path, PinnedFileEvidence& evidence)
	{
		closePinnedEvidence(evidence);
		evidence = {};
		evidence.handle = INVALID_HANDLE_VALUE;
		if (path.empty() || PathIsRelativeW(path.c_str())) return false;
		HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
			OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT
				| FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
		if (file == INVALID_HANDLE_VALUE) return false;
		DWORD bindingError = ERROR_SUCCESS;
		const bool ok = validateOpenedFilesystemObject(file, path, false, &evidence.identity,
			&bindingError) && hashHandle(file, evidence.hash.data())
			&& securityDescriptorSddl(file, evidence.sddl);
		if (!ok)
		{
			CloseHandle(file);
			return false;
		}
		evidence.handle = file;
		evidence.path = path;
		return true;
	}

	bool revalidatePinnedFileEvidence(const PinnedFileEvidence& evidence)
	{
		if (evidence.handle == INVALID_HANDLE_VALUE || evidence.path.empty()) return false;
		FILE_ID_INFO currentIdentity{};
		std::array<unsigned char, 32> currentHash{};
		std::wstring currentSddl;
		DWORD bindingError = ERROR_SUCCESS;
		return validateOpenedFilesystemObject(evidence.handle, evidence.path, false,
			&currentIdentity, &bindingError)
			&& sameFileIdentity(currentIdentity, evidence.identity)
			&& hashHandle(evidence.handle, currentHash.data()) && currentHash == evidence.hash
			&& securityDescriptorSddl(evidence.handle, currentSddl) && currentSddl == evidence.sddl;
	}

	bool validateReadOnlyPins()
	{
		wchar_t temporaryDirectory[MAX_PATH + 1]{};
		wchar_t temporaryFile[MAX_PATH + 1]{};
		if (GetTempPathW(MAX_PATH, temporaryDirectory) == 0
			|| GetTempFileNameW(temporaryDirectory, L"trp", 0, temporaryFile) == 0) return false;

		HANDLE maintenance = pinReadOnlyMaintenanceLock(temporaryFile);
		FILE_ID_INFO maintenanceIdentity{};
		DWORD bindingError = ERROR_SUCCESS;
		bool ok = maintenance != INVALID_HANDLE_VALUE
			&& validateOpenedFilesystemObject(maintenance, temporaryFile, false,
				&maintenanceIdentity, &bindingError);
		if (ok)
		{
			HANDLE writer = CreateFileW(temporaryFile, GENERIC_WRITE, FILE_SHARE_READ, nullptr,
				OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
			ok = writer == INVALID_HANDLE_VALUE;
			if (writer != INVALID_HANDLE_VALUE) CloseHandle(writer);
			ok = ok && DeleteFileW(temporaryFile) == FALSE;
		}
		if (maintenance != INVALID_HANDLE_VALUE) CloseHandle(maintenance);

		PinnedFileEvidence wrapper;
		ok = ok && pinReadOnlyFileEvidence(temporaryFile, wrapper)
			&& revalidatePinnedFileEvidence(wrapper);
		if (ok)
		{
			HANDLE writer = CreateFileW(temporaryFile, GENERIC_WRITE, FILE_SHARE_READ, nullptr,
				OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
			ok = writer == INVALID_HANDLE_VALUE;
			if (writer != INVALID_HANDLE_VALUE) CloseHandle(writer);
			ok = ok && revalidatePinnedFileEvidence(wrapper);
		}
		closePinnedEvidence(wrapper);
		const std::wstring thirdFrontend = std::wstring(temporaryFile) + L".third";
		const HANDLE third = CreateFileW(thirdFrontend.c_str(), GENERIC_WRITE, 0, nullptr,
			CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
		const bool thirdCreated = third != INVALID_HANDLE_VALUE;
		if (thirdCreated) CloseHandle(third);
		ok = ok && thirdCreated
			&& launcherFrontendMatchesLayout(false, L"", temporaryFile)
			&& launcherFrontendMatchesLayout(true,
				std::wstring(temporaryFile) + L".stale-missing", temporaryFile)
			&& launcherFrontendMatchesLayout(true, temporaryFile, temporaryFile)
			&& !launcherFrontendMatchesLayout(true, thirdFrontend, temporaryFile)
			&& !launcherFrontendMatchesLayout(true, L"relative-wrapper.exe", temporaryFile);
		DeleteFileW(thirdFrontend.c_str());

		const std::wstring hardlink = std::wstring(temporaryFile) + L".hardlink";
		if (ok) ok = CreateHardLinkW(hardlink.c_str(), temporaryFile, nullptr) != FALSE;
		HANDLE rejectedHardlink = ok ? pinReadOnlyMaintenanceLock(temporaryFile)
			: INVALID_HANDLE_VALUE;
		if (rejectedHardlink != INVALID_HANDLE_VALUE)
		{
			CloseHandle(rejectedHardlink);
			ok = false;
		}
		DeleteFileW(hardlink.c_str());

		const std::wstring symlink = std::wstring(temporaryFile) + L".symlink";
		if (CreateSymbolicLinkW(symlink.c_str(), temporaryFile, 0x2) != FALSE)
		{
			HANDLE rejectedReparse = pinReadOnlyMaintenanceLock(symlink);
			if (rejectedReparse != INVALID_HANDLE_VALUE)
			{
				CloseHandle(rejectedReparse);
				ok = false;
			}
			DeleteFileW(symlink.c_str());
		}
		DeleteFileW(temporaryFile);
		const std::wstring missing = std::wstring(temporaryFile) + L".missing";
		HANDLE absent = pinReadOnlyMaintenanceLock(missing);
		if (absent != INVALID_HANDLE_VALUE) CloseHandle(absent);
		return ok && absent == INVALID_HANDLE_VALUE;
	}

	bool validateTestOnlyDirectoryPinScope()
	{
		wchar_t temporaryDirectory[MAX_PATH + 1]{};
		if (GetTempPathW(MAX_PATH, temporaryDirectory) == 0) return false;
		const std::wstring root = join(temporaryDirectory,
			L"TurboRama-pin-scope-self-test-" + std::to_wstring(GetCurrentProcessId())
				+ L"-" + std::to_wstring(GetTickCount64()));
		const std::wstring target = join(root, L"install");
		const std::wstring emulationStation = join(target, L".emulationstation");
		const std::wstring pix = join(emulationStation, L"pix");
		removeTree(root);
		bool ok = ensureDirectory(pix);
		std::vector<PinnedDirectory> pins;
		if (ok) ok = pinDirectoryChain(target, pins, &root);
		const std::array<std::wstring, 4> expected = {
			root, target, emulationStation, pix
		};
		if (ok)
		{
			ok = pins.size() == expected.size();
			for (size_t index = 0; ok && index < expected.size(); ++index)
				ok = normalized(pins[index].path) == normalized(expected[index]);
			if (ok) ok = revalidatePinnedDirectories(pins);
		}
		std::vector<PinnedDirectory> rejectedPins;
		const std::wstring overlyBroadRoot = parentOf(root);
		if (ok) ok = !pinDirectoryChain(target, rejectedPins, &overlyBroadRoot)
			&& rejectedPins.empty();
		closePinnedDirectories(rejectedPins);
		closePinnedDirectories(pins);
		const bool cleaned = cleanupDirectoryTreeByHandle(root);
		return ok && cleaned && pathIsMissing(root);
	}

	bool securityBackupsMatch(const std::vector<SecurityBackup>& expected)
	{
		for (const auto& backup : expected)
		{
			std::vector<SecurityBackup> current;
			if (!captureSecurityBackup(backup.path, backup.directory, current)
				|| current.size() != 1
				|| !sameFileIdentity(current[0].fileIdentity, backup.fileIdentity)
				|| current[0].daclProtected != backup.daclProtected
				|| current[0].descriptor != backup.descriptor) return false;
		}
		return true;
	}

	bool securityBackupVectorsEqual(const std::vector<SecurityBackup>& left,
		const std::vector<SecurityBackup>& right)
	{
		if (left.size() != right.size()) return false;
		for (size_t index = 0; index < left.size(); ++index)
		{
			const auto& first = left[index];
			const auto& second = right[index];
			if (_wcsicmp(first.path.c_str(), second.path.c_str()) != 0
				|| first.directory != second.directory
				|| first.daclProtected != second.daclProtected
				|| !sameFileIdentity(first.fileIdentity, second.fileIdentity)
				|| first.descriptor != second.descriptor) return false;
		}
		return true;
	}

	bool grantSecurityShareSelfTestAccess(const std::wstring& path,
		const ResolvedIdentity& identity, bool directory = false, bool inheritable = false)
	{
		if (identity.sidText.empty()) return false;
		const std::wstring sddl = L"D:P(A;" + std::wstring(inheritable ? L"OICI" : L"")
			+ L";FA;;;" + identity.sidText + L")";
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1,
			&descriptor, nullptr)) return false;
		PACL dacl = nullptr;
		BOOL present = FALSE, defaulted = FALSE;
		bool ok = GetSecurityDescriptorDacl(descriptor, &present, &dacl, &defaulted) != FALSE
			&& present && dacl != nullptr;
		HANDLE object = ok ? CreateFileW(path.c_str(), READ_CONTROL | WRITE_DAC,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr)
			: INVALID_HANDLE_VALUE;
		if (object == INVALID_HANDLE_VALUE) ok = false;
		if (ok) ok = validateOpenedFilesystemObject(object, path, directory)
			&& SetSecurityInfo(object, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION
				| PROTECTED_DACL_SECURITY_INFORMATION, nullptr, nullptr, dacl, nullptr)
				== ERROR_SUCCESS;
		if (object != INVALID_HANDLE_VALUE) CloseHandle(object);
		LocalFree(descriptor);
		if (!ok) return false;

		// A ACE explicita garante que o proprio teste exercite tambem WRITE_OWNER,
		// em vez de depender apenas dos direitos implicitos concedidos ao owner.
		object = CreateFileW(path.c_str(), READ_CONTROL | WRITE_DAC | WRITE_OWNER,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		ok = object != INVALID_HANDLE_VALUE
			&& validateOpenedFilesystemObject(object, path, directory);
		if (object != INVALID_HANDLE_VALUE) CloseHandle(object);
		return ok;
	}

	bool maximumAllowedBlockedBySharing(const std::wstring& path)
	{
		HANDLE object = CreateFileW(path.c_str(), MAXIMUM_ALLOWED,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (object != INVALID_HANDLE_VALUE)
		{
			CloseHandle(object);
			return false;
		}
		return GetLastError() == ERROR_SHARING_VIOLATION;
	}

	bool applyAndRestoreSecurityShareSelfTest(const std::wstring& path,
		const ResolvedIdentity& kioskIdentity, const ResolvedIdentity& ownerIdentity,
		const std::vector<SecurityBackup>& backup)
	{
		if (backup.size() != 1) return false;
		SecurityFailure applyFailure, restoreFailure;
		return applyKioskSecurity(path, false, kioskIdentity, KioskPermission::ReadExecute,
			false, &applyFailure, &ownerIdentity, &ownerIdentity, nullptr,
			&backup.front().fileIdentity)
			&& restoreSecurityBackup(backup.front(), &restoreFailure)
			&& securityBackupsMatch(backup);
	}

	bool applyAndRestoreSecurityTreeSelfTest(const std::wstring& tree,
		const ResolvedIdentity& kioskIdentity, const ResolvedIdentity& ownerIdentity,
		const std::vector<SecurityBackup>& treeBackup,
		const std::vector<SecurityBackup>& sentinelBackup)
	{
		if (treeBackup.size() < 3 || sentinelBackup.size() != 1) return false;
		SecurityFailure applyFailure, restoreFailure;
		const bool applied = applyKioskSecurityTree(tree, true, kioskIdentity,
			KioskPermission::ReadExecute, true, &applyFailure, &ownerIdentity,
			&ownerIdentity, nullptr, &treeBackup);
		const bool treeChanged = applied && !securityBackupsMatch(treeBackup);
		const bool sentinelUntouched = securityBackupsMatch(sentinelBackup);
		// Mesmo que a aplicacao falhe parcialmente, o teste sempre exercita o rollback.
		const bool restored = restoreSecurityBackups(treeBackup, &restoreFailure);
		return applied && treeChanged && sentinelUntouched && restored
			&& securityBackupsMatch(treeBackup) && securityBackupsMatch(sentinelBackup);
	}

	bool rejectSubstitutedSecurityTreeSelfTest(const std::wstring& tree,
		const std::wstring& leaf, const ResolvedIdentity& kioskIdentity,
		const ResolvedIdentity& ownerIdentity,
		const std::vector<SecurityBackup>& treeBackup,
		const std::vector<SecurityBackup>& sentinelBackup)
	{
		const SecurityBackup* leafBackup = findSecurityBackup(treeBackup, leaf, false);
		if (leafBackup == nullptr || sentinelBackup.size() != 1) return false;
		const std::vector<SecurityBackup> preservedBackups = treeBackup;
		// Mova o original para fora da arvore: deixa o mesmo caminho ocupado por um
		// novo FileId sem introduzir um objeto extra que o snapshot deva rejeitar.
		const std::wstring displaced = tree + L".snapshot-original-leaf";
		if (!MoveFileExW(leaf.c_str(), displaced.c_str(), MOVEFILE_WRITE_THROUGH)) return false;

		const bool replacementCreated = writeUtf8FileForSelfTest(leaf,
			L"replacement created after security snapshot\n");
		SecurityFailure applyFailure;
		const bool rejected = replacementCreated
			&& !applyKioskSecurityTree(tree, true, kioskIdentity,
				KioskPermission::ReadExecute, true, &applyFailure, &ownerIdentity,
				&ownerIdentity, nullptr, &treeBackup)
			&& applyFailure.code == ERROR_FILE_INVALID
			&& _wcsicmp(applyFailure.path.c_str(), leaf.c_str()) == 0;
		const bool backupsPreserved = securityBackupVectorsEqual(treeBackup, preservedBackups);
		const bool sentinelUntouchedBeforeRestore = securityBackupsMatch(sentinelBackup);

		const bool replacementRemoved = !replacementCreated || removeRegularFileIfPresent(leaf);
		const bool originalRestored = replacementRemoved
			&& MoveFileExW(displaced.c_str(), leaf.c_str(), MOVEFILE_WRITE_THROUGH) != FALSE;
		SecurityFailure restoreFailure;
		const bool descriptorRestored = originalRestored
			&& restoreSecurityBackups(treeBackup, &restoreFailure);
		return replacementCreated && rejected && backupsPreserved
			&& sentinelUntouchedBeforeRestore && descriptorRestored
			&& securityBackupsMatch(treeBackup) && securityBackupsMatch(sentinelBackup);
	}

	bool applyInjectedFailureAndRollbackSecurityTreeSelfTest(const std::wstring& tree,
		const std::wstring& failurePath, bool mutationExpected,
		const ResolvedIdentity& kioskIdentity, const ResolvedIdentity& ownerIdentity,
		const std::vector<SecurityBackup>& treeBackup,
		const std::vector<SecurityBackup>& sentinelBackup)
	{
		const std::vector<SecurityBackup> preservedBackups = treeBackup;
		SecurityFailure applyFailure, restoreFailure;
		const bool rejected = !applyKioskSecurityTree(tree, true, kioskIdentity,
			KioskPermission::ReadExecute, true, &applyFailure, &ownerIdentity,
			&ownerIdentity, nullptr, &treeBackup, nullptr, nullptr, &failurePath)
			&& applyFailure.code == ERROR_CANCELLED
			&& _wcsicmp(applyFailure.path.c_str(), failurePath.c_str()) == 0;
		const bool partialStateAsExpected = !mutationExpected || !securityBackupsMatch(treeBackup);
		const bool backupsPreserved = securityBackupVectorsEqual(treeBackup, preservedBackups);
		const bool sentinelUntouchedBeforeRestore = securityBackupsMatch(sentinelBackup);
		// O rollback e sempre tentado, inclusive quando a falha foi injetada no primeiro no.
		const bool restored = restoreSecurityBackups(treeBackup, &restoreFailure);
		return rejected && partialStateAsExpected && backupsPreserved
			&& sentinelUntouchedBeforeRestore && restored
			&& securityBackupsMatch(treeBackup) && securityBackupsMatch(sentinelBackup);
	}

	bool validateSecurityOnlyHandleShareSelfTest()
	{
		wchar_t temporaryDirectory[MAX_PATH + 1]{};
		std::vector<wchar_t> module(32768);
		if (GetTempPathW(MAX_PATH, temporaryDirectory) == 0
			|| GetModuleFileNameW(nullptr, module.data(), static_cast<DWORD>(module.size())) == 0)
			return false;
		const std::wstring root = join(temporaryDirectory,
			L"TurboRama-security-share-self-test-" + std::to_wstring(GetCurrentProcessId())
				+ L"-" + std::to_wstring(GetTickCount64()));
		const std::wstring mappedExecutable = join(root, L"mapped-fixture.exe");
		const std::wstring pinnedExecutable = join(root, L"pinned-fixture.exe");
		const std::wstring tree = join(root, L"tree-fixture");
		const std::wstring treeChild = join(tree, L"child");
		const std::wstring treeGrandchild = join(treeChild, L"grandchild");
		const std::wstring treeLeaf = join(treeGrandchild, L"leaf.txt");
		const std::wstring siblingSentinel = join(root, L"sibling-sentinel.txt");
		removeTree(root);

		ResolvedIdentity processIdentity, kioskIdentity;
		bool ok = ensureDirectory(root)
			&& CopyFileW(module.data(), mappedExecutable.c_str(), TRUE) != FALSE
			&& CopyFileW(module.data(), pinnedExecutable.c_str(), TRUE) != FALSE
			&& ensureDirectory(tree)
			&& currentProcessIdentity(processIdentity)
			&& wellKnownIdentity(WinBuiltinUsersSid, L"BUILTIN\\Users", kioskIdentity)
			&& grantSecurityShareSelfTestAccess(mappedExecutable, processIdentity)
			&& grantSecurityShareSelfTestAccess(pinnedExecutable, processIdentity)
			&& grantSecurityShareSelfTestAccess(tree, processIdentity, true, true)
			&& ensureDirectory(treeChild)
			&& ensureDirectory(treeGrandchild)
			&& grantSecurityShareSelfTestAccess(treeGrandchild, processIdentity, true, true)
			&& writeUtf8FileForSelfTest(treeLeaf, L"tree leaf\n")
			&& writeUtf8FileForSelfTest(siblingSentinel, L"outside tree\n")
			&& grantSecurityShareSelfTestAccess(siblingSentinel, processIdentity);

		std::vector<SecurityBackup> mappedBackup, pinnedBackup, treeBackup, sentinelBackup;
		if (ok) ok = captureSecurityBackup(mappedExecutable, false, mappedBackup)
			&& captureSecurityBackup(pinnedExecutable, false, pinnedBackup)
			&& captureSecurityTree(tree, true, treeBackup)
			&& captureSecurityBackup(siblingSentinel, false, sentinelBackup);
		if (ok)
		{
			const SecurityBackup* rootBackup = findSecurityBackup(treeBackup, tree, true);
			const SecurityBackup* childBackup = findSecurityBackup(treeBackup, treeChild, true);
			const SecurityBackup* grandchildBackup = findSecurityBackup(treeBackup,
				treeGrandchild, true);
			const SecurityBackup* leafBackup = findSecurityBackup(treeBackup, treeLeaf, false);
			ok = treeBackup.size() == 4 && rootBackup != nullptr && rootBackup->daclProtected
				&& childBackup != nullptr && !childBackup->daclProtected
				&& grandchildBackup != nullptr && grandchildBackup->daclProtected
				&& leafBackup != nullptr && !leafBackup->daclProtected;
		}
		if (ok) ok = rejectSubstitutedSecurityTreeSelfTest(tree, treeLeaf, kioskIdentity,
			processIdentity, treeBackup, sentinelBackup);
		if (ok)
		{
			for (const auto& backup : treeBackup)
			{
				if (!applyInjectedFailureAndRollbackSecurityTreeSelfTest(tree, backup.path,
					backup.directory, kioskIdentity, processIdentity, treeBackup, sentinelBackup))
				{
					ok = false;
					break;
				}
			}
		}
		if (ok) ok = applyAndRestoreSecurityTreeSelfTest(tree, kioskIdentity,
			processIdentity, treeBackup, sentinelBackup);

		HANDLE imageFile = INVALID_HANDLE_VALUE;
		HANDLE imageMapping = nullptr;
		void* imageView = nullptr;
		if (ok)
		{
			imageFile = CreateFileW(mappedExecutable.c_str(), GENERIC_READ, FILE_SHARE_READ,
				nullptr, OPEN_EXISTING,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			ok = imageFile != INVALID_HANDLE_VALUE;
		}
		if (ok)
		{
			imageMapping = CreateFileMappingW(imageFile, nullptr, PAGE_READONLY | SEC_IMAGE,
				0, 0, nullptr);
			ok = imageMapping != nullptr;
		}
		if (ok)
		{
			imageView = MapViewOfFile(imageMapping, FILE_MAP_READ, 0, 0, 0);
			ok = imageView != nullptr;
		}
		if (imageFile != INVALID_HANDLE_VALUE)
		{
			CloseHandle(imageFile);
			imageFile = INVALID_HANDLE_VALUE;
		}
		if (ok) ok = maximumAllowedBlockedBySharing(mappedExecutable)
			&& applyAndRestoreSecurityShareSelfTest(mappedExecutable, kioskIdentity,
				processIdentity, mappedBackup);
		if (imageView != nullptr) UnmapViewOfFile(imageView);
		if (imageMapping != nullptr) CloseHandle(imageMapping);

		HANDLE transactionPin = INVALID_HANDLE_VALUE;
		if (ok)
		{
			const DWORD access = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER
				| FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES | GENERIC_READ;
			transactionPin = CreateFileW(pinnedExecutable.c_str(), access, FILE_SHARE_READ,
				nullptr, OPEN_EXISTING,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			ok = transactionPin != INVALID_HANDLE_VALUE
				&& validateOpenedFilesystemObject(transactionPin, pinnedExecutable, false);
		}
		if (ok) ok = maximumAllowedBlockedBySharing(pinnedExecutable)
			&& applyAndRestoreSecurityShareSelfTest(pinnedExecutable, kioskIdentity,
				processIdentity, pinnedBackup);
		if (transactionPin != INVALID_HANDLE_VALUE) CloseHandle(transactionPin);

		const bool cleaned = cleanupDirectoryTreeByHandle(root);
		return ok && cleaned && pathIsMissing(root);
	}

	bool planHasEntry(const std::vector<InstallationSecurityContext::PlanEntry>& plan,
		const std::wstring& path, bool directory, bool tree, KioskPermission permission,
		bool inheritable)
	{
		for (const auto& entry : plan)
			if (_wcsicmp(entry.path.c_str(), path.c_str()) == 0 && entry.directory == directory
				&& entry.tree == tree && entry.permission == permission
				&& entry.inheritable == inheritable) return true;
		return false;
	}

	bool validateSecurityPlanBoundarySelfTest()
	{
		wchar_t temporaryDirectory[MAX_PATH + 1]{};
		if (GetTempPathW(MAX_PATH, temporaryDirectory) == 0) return false;
		const std::wstring root = join(temporaryDirectory,
			L"TurboRama-plan-self-test-" + std::to_wstring(GetCurrentProcessId())
				+ L"-" + std::to_wstring(GetTickCount64()));
		const std::wstring target = join(root, L"D-layout\\emulationstation");
		const std::wstring pix = join(target, L".emulationstation\\pix");
		const std::wstring agent = join(target, L"pix-agent");
		removeTree(root);
		bool ok = ensureDirectory(pix) && ensureDirectory(agent)
			&& writeUtf8FileForSelfTest(join(target, L"emulationstation.exe"), L"es")
			&& writeUtf8FileForSelfTest(join(target, L"CONFIGURAR-USER-TOKEN-PIX.exe"), L"user")
			&& writeUtf8FileForSelfTest(join(target, L"CONFIGURAR-ACCESS-TOKEN-PIX.exe"), L"access")
			&& writeUtf8FileForSelfTest(join(agent, L"TurboRamaPixAgent.dll"), L"agent")
			&& writeUtf8FileForSelfTest(join(pix, L"state.json"), L"{}\n");
		std::vector<SecurityBackup> before;
		if (ok) ok = captureSecurityTree(target, true, before);
		std::vector<InstallationSecurityContext::PlanEntry> plan;
		SecurityFailure failure;
		if (ok) ok = buildInstallationSecurityPlan(target, plan, &failure) && plan.size() == 5;
		const std::wstring targetPrefix = normalized(target) + L"\\";
		const std::wstring forbidden = normalized(L"C:\\TurboRama");
		if (ok)
		{
			for (const auto& entry : plan)
			{
				const std::wstring path = normalized(entry.path);
				if (!beginsWith(path, targetPrefix) || beginsWith(path, forbidden)
					|| path.find(L"reparar-instalacao-turborama.ps1") != std::wstring::npos)
				{
					ok = false;
					break;
				}
			}
		}
		if (ok) ok = securityBackupsMatch(before);
		const bool cleaned = removeTree(root);
		return ok && cleaned;
	}

	bool createAtomicTransactionFixture(const std::wstring& root,
		std::wstring& staged, std::wstring& target)
	{
		staged = join(root, L"staged");
		target = join(root, L"target");
		if (!ensureDirectory(join(staged, L"pix-agent\\runtime"))
			|| !ensureDirectory(join(target, L"pix-agent\\runtime"))) return false;
		for (const auto& relative : {
			L"emulationstation.exe", L"CONFIGURAR-USER-TOKEN-PIX.exe",
			L"CONFIGURAR-ACCESS-TOKEN-PIX.exe" })
		{
			if (!writeUtf8FileForSelfTest(join(staged, relative),
				L"new:" + std::wstring(relative) + L"\n")
				|| !writeUtf8FileForSelfTest(join(target, relative),
					L"old:" + std::wstring(relative) + L"\n")) return false;
		}
		if (!writeUtf8FileForSelfTest(join(staged,
			L"pix-agent\\TurboRamaPixAgent.dll"), L"new-agent\n")
			|| !writeUtf8FileForSelfTest(join(staged,
				L"pix-agent\\runtime\\dotnet.exe"), L"new-runtime\n")
			|| !writeUtf8FileForSelfTest(join(staged,
				L"pix-agent\\data.bin"), L"new-data\n")
			|| !writeUtf8FileForSelfTest(join(target,
				L"pix-agent\\TurboRamaPixAgent.dll"), L"old-agent\n")
			|| !writeUtf8FileForSelfTest(join(target,
				L"pix-agent\\runtime\\dotnet.exe"), L"old-runtime\n")
			|| !writeUtf8FileForSelfTest(join(target,
				L"pix-agent\\data.bin"), L"old-data\n")) return false;
		return SetFileAttributesW(join(target, L"emulationstation.exe").c_str(),
			FILE_ATTRIBUTE_HIDDEN) != FALSE;
	}

	HANDLE openAtomicTestTarget(const std::wstring& target)
	{
		return CreateFileW(target.c_str(), FILE_READ_ATTRIBUTES | FILE_TRAVERSE
			| FILE_LIST_DIRECTORY | READ_CONTROL,
			FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
	}

	bool atomicTransactionResidueAbsent(const std::wstring& target)
	{
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(target, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE) return false;
		bool clean = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name.rfind(L".turborama-", 0) == 0)
			{
				clean = false;
				break;
			}
		} while (FindNextFileW(search, &entry));
		FindClose(search);
		return clean;
	}

	bool transactionOriginalsRestored(const AtomicInstallTransaction& transaction)
	{
		bool ok = true;
		for (const auto& entry : transaction.entries)
		{
			std::vector<FilesystemMetadata> current;
			if (entry.directory)
			{
				if (!captureFilesystemMetadataTree(entry.canonicalPath, current)) ok = false;
			}
			else if (!captureFilesystemMetadata(entry.canonicalPath, false, current)) ok = false;
			if (current.empty()
				|| !sameFilesystemManifest(entry.canonicalPath, entry.originalMetadata,
					entry.canonicalPath, current, true, true)) ok = false;
		}
		return ok && atomicTransactionResidueAbsent(transaction.target);
	}

	bool transactionPublicationHasExpectedMetadata(
		const AtomicInstallTransaction& transaction)
	{
		bool ok = true;
		for (const auto& entry : transaction.entries)
		{
			std::vector<FilesystemMetadata> current;
			if (entry.directory)
			{
				if (!captureFilesystemMetadataTree(entry.canonicalPath, current)) ok = false;
			}
			else if (!captureFilesystemMetadata(entry.canonicalPath, false, current)) ok = false;
			bool entryOk = !current.empty()
				&& !entry.sourceManifest.empty()
				&& sameFilesystemManifest(entry.sourceManifest.front().path,
					entry.sourceManifest, entry.canonicalPath, current, false, false)
				&& !sameFileIdentity(entry.originalMetadata.front().security.fileIdentity,
					current.front().security.fileIdentity);
			if (entryOk && !restoreMappedFilesystemMetadata(entry.canonicalPath,
				entry.canonicalPath, entry.originalMetadata)) entryOk = false;
			if (entryOk && !filesystemMetadataMatchesMapped(entry.canonicalPath,
				entry.canonicalPath, entry.originalMetadata)) entryOk = false;
			if (!entryOk) ok = false;
		}
		return ok && atomicTransactionResidueAbsent(transaction.target);
	}

	bool createSelfTestJunction(const std::wstring& junction,
		const std::wstring& destination)
	{
		if (!CreateDirectoryW(junction.c_str(), nullptr))
		{
			const DWORD error = GetLastError();
			SetLastError(10000 + error);
			return false;
		}
		HANDLE object = CreateFileW(junction.c_str(), GENERIC_WRITE, 0, nullptr,
			OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
			nullptr);
		if (object == INVALID_HANDLE_VALUE)
		{
			const DWORD error = GetLastError();
			SetLastError(20000 + error);
			return false;
		}
		struct MountPointData
		{
			DWORD tag;
			WORD dataLength;
			WORD reserved;
			WORD substituteOffset;
			WORD substituteLength;
			WORD printOffset;
			WORD printLength;
			wchar_t names[2048];
		};
		MountPointData data{};
		const std::wstring substitute = L"\\??\\" + normalized(destination);
		const std::wstring print = normalized(destination);
		const size_t substituteBytes = substitute.size() * sizeof(wchar_t);
		const size_t printBytes = print.size() * sizeof(wchar_t);
		if (substituteBytes + printBytes + 2 * sizeof(wchar_t) > sizeof(data.names))
		{
			CloseHandle(object);
			return false;
		}
		data.tag = IO_REPARSE_TAG_MOUNT_POINT;
		data.substituteOffset = 0;
		data.substituteLength = static_cast<WORD>(substituteBytes);
		data.printOffset = static_cast<WORD>(substituteBytes + sizeof(wchar_t));
		data.printLength = static_cast<WORD>(printBytes);
		CopyMemory(data.names, substitute.c_str(), substituteBytes + sizeof(wchar_t));
		CopyMemory(reinterpret_cast<unsigned char*>(data.names) + data.printOffset,
			print.c_str(), printBytes + sizeof(wchar_t));
		const DWORD total = static_cast<DWORD>(offsetof(MountPointData, names)
			+ data.printOffset + printBytes + sizeof(wchar_t));
		data.dataLength = static_cast<WORD>(total - 8);
		DWORD returned = 0;
		const bool created = DeviceIoControl(object, FSCTL_SET_REPARSE_POINT,
			&data, total, nullptr, 0, &returned, nullptr) != FALSE;
		CloseHandle(object);
		if (created)
		{
			SetLastError(ERROR_SUCCESS);
			return true;
		}
		// Alguns filtros de filesystem recusam o buffer direto. O fallback mklink /J
		// continua deterministico e nao depende do privilegio de symlink.
		if (!RemoveDirectoryW(junction.c_str()))
		{
			const DWORD fallbackError = GetLastError();
			SetLastError(31000 + fallbackError);
			return false;
		}
		wchar_t systemDirectory[MAX_PATH + 1]{};
		if (GetSystemDirectoryW(systemDirectory, MAX_PATH) == 0)
		{
			SetLastError(32000 + GetLastError());
			return false;
		}
		const std::wstring commandProcessor = join(systemDirectory, L"cmd.exe");
		std::wstring command = L"\"" + commandProcessor
			+ L"\" /d /q /c mklink /J \"" + junction + L"\" \""
			+ destination + L"\" >nul";
		std::vector<wchar_t> writable(command.begin(), command.end());
		writable.push_back(L'\0');
		STARTUPINFOW startup{};
		startup.cb = sizeof(startup);
		startup.dwFlags = STARTF_USESHOWWINDOW;
		startup.wShowWindow = SW_HIDE;
		PROCESS_INFORMATION process{};
		if (!CreateProcessW(commandProcessor.c_str(), writable.data(), nullptr, nullptr,
			FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process))
		{
			SetLastError(33000 + GetLastError());
			return false;
		}
		WaitForSingleObject(process.hProcess, 30000);
		DWORD exitCode = 1;
		GetExitCodeProcess(process.hProcess, &exitCode);
		CloseHandle(process.hThread);
		CloseHandle(process.hProcess);
		const DWORD junctionAttributes = GetFileAttributesW(junction.c_str());
		const bool fallbackCreated = exitCode == 0
			&& junctionAttributes != INVALID_FILE_ATTRIBUTES
			&& (junctionAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
		SetLastError(fallbackCreated ? ERROR_SUCCESS : 30000 + exitCode);
		return fallbackCreated;
	}

	bool validateAtomicPublicationAndMetadataSelfTest()
	{
		wchar_t temporaryDirectory[MAX_PATH + 1]{};
		if (GetTempPathW(MAX_PATH, temporaryDirectory) == 0) return false;
		const std::wstring root = join(temporaryDirectory,
			L"TurboRama-atomic-self-test-" + std::to_wstring(GetCurrentProcessId())
				+ L"-" + std::to_wstring(GetTickCount64()));
		std::wstring staged, target;
		removeTree(root);
		int failureStage = 1;
		bool ok = createAtomicTransactionFixture(root, staged, target);
		const std::wstring knownResidue = join(target,
			L".TuRbOrAmA-WrItE-self-test");
		if (ok)
		{
			failureStage = 11;
			ok = writeUtf8FileForSelfTest(knownResidue, L"residue\n")
				&& !relevantTransactionResiduesAbsent(target)
				&& removeRegularFileIfPresent(knownResidue)
				&& relevantTransactionResiduesAbsent(target);
		}
		HANDLE targetHandle = INVALID_HANDLE_VALUE;
		if (ok)
		{
			failureStage = 12;
			targetHandle = openAtomicTestTarget(target);
			ok = targetHandle != INVALID_HANDLE_VALUE;
		}

		// Hardlink: a preparacao falha antes do primeiro rename canonico.
		if (ok) failureStage = 2;
		wchar_t hardlinkSource[MAX_PATH + 1]{};
		if (ok)
		{
			failureStage = 21;
			ok = GetTempFileNameW(temporaryDirectory, L"trh", 0, hardlinkSource) != 0;
		}
		const std::wstring hardlink = std::wstring(hardlinkSource) + L".hardlink";
		DWORD hardlinkError = ERROR_SUCCESS;
		if (ok)
		{
			failureStage = 22;
			ok = CreateHardLinkW(hardlink.c_str(), hardlinkSource, nullptr) != FALSE;
		}
		if (ok)
		{
			failureStage = 23;
			ok = DeleteFileW(join(staged, L"emulationstation.exe").c_str()) != FALSE;
		}
		if (ok)
		{
			failureStage = 24;
			ok = MoveFileExW(hardlinkSource,
				join(staged, L"emulationstation.exe").c_str(), MOVEFILE_WRITE_THROUGH) != FALSE;
		}
		if (!ok) hardlinkError = GetLastError();
		AtomicInstallTransaction hardlinkTransaction;
		if (ok)
		{
			failureStage = 3;
			ok = !prepareInstallTransaction(staged, target, targetHandle,
				hardlinkTransaction)
				&& abandonPreparedInstallTransaction(hardlinkTransaction)
				&& atomicTransactionResidueAbsent(target);
		}
		DeleteFileW(hardlink.c_str());
		DeleteFileW(hardlinkSource);
		if (ok) ok = writeUtf8FileForSelfTest(join(staged, L"emulationstation.exe"),
			L"new:emulationstation.exe\n");

		// Junction real via FSCTL: a copia recursiva deve rejeita-la deterministicamente.
		if (ok) failureStage = 4;
		const std::wstring junctionTarget = join(root, L"junction-target");
		const std::wstring junction = join(staged, L"pix-agent\\reparse-child");
		DWORD junctionError = ERROR_SUCCESS;
		if (ok) ok = ensureDirectory(junctionTarget)
			&& createSelfTestJunction(junction, junctionTarget);
		if (!ok) junctionError = GetLastError();
		AtomicInstallTransaction reparseTransaction;
		DWORD reparseCleanupError = ERROR_SUCCESS;
		if (ok)
		{
			failureStage = 5;
			const bool rejected = !prepareInstallTransaction(staged, target, targetHandle,
				reparseTransaction);
			const bool abandoned = abandonPreparedInstallTransaction(reparseTransaction);
			if (!abandoned) reparseCleanupError = GetLastError();
			const bool residueAbsent = atomicTransactionResidueAbsent(target);
			if (!rejected) failureStage = 51;
			else if (!abandoned) failureStage = 52;
			else if (!residueAbsent) failureStage = 53;
			ok = rejected && abandoned && residueAbsent;
		}
		RemoveDirectoryW(junction.c_str());

		// Publicacao seguida de rollback: FileIds e todos os metadados voltam iguais.
		if (ok) failureStage = 6;
		AtomicInstallTransaction rollbackTransaction;
		DWORD transactionError = ERROR_SUCCESS;
		if (ok)
		{
			failureStage = 61;
			ok = prepareInstallTransaction(staged, target, targetHandle,
				rollbackTransaction);
			if (!ok) transactionError = GetLastError();
		}
		if (ok)
		{
			failureStage = 62;
			ok = publishInstallTransaction(rollbackTransaction);
			if (!ok) transactionError = GetLastError();
		}
		if (ok)
		{
			failureStage = 63;
			ok = validatePublishedInstallTransaction(rollbackTransaction);
		}
		if (ok)
		{
			failureStage = 64;
			ok = rollbackInstallTransaction(rollbackTransaction);
		}
		if (ok)
		{
			failureStage = 65;
			ok = transactionOriginalsRestored(rollbackTransaction);
		}

		// Publicacao/commit: conteudo exato novo, FileIds novos, metadados antigos e
		// nenhum temp/tombstone remanescente.
		if (ok) failureStage = 7;
		AtomicInstallTransaction commitTransaction;
		if (ok)
		{
			failureStage = 71;
			ok = prepareInstallTransaction(staged, target, targetHandle, commitTransaction);
		}
		if (ok)
		{
			failureStage = 72;
			ok = publishInstallTransaction(commitTransaction);
		}
		if (ok)
		{
			failureStage = 73;
			ok = validatePublishedInstallTransaction(commitTransaction);
		}
		if (ok)
		{
			failureStage = 74;
			ok = commitInstallTransaction(commitTransaction);
		}
		if (ok)
		{
			failureStage = 76;
			ok = validatePublishedInstallTransaction(commitTransaction)
				&& relevantTransactionResiduesAbsent(target);
		}
		// A producao conserva estes pins ate sair; o teste precisa libera-los antes
		// da verificacao externa por pathname e da remocao do fixture isolado.
		closeAtomicInstallTransactionHandles(commitTransaction);
		if (ok)
		{
			failureStage = 75;
			ok = transactionPublicationHasExpectedMetadata(commitTransaction);
		}

		if (!ok) writeUtf8FileForSelfTest(join(root,
			L"failure-stage-" + std::to_wstring(failureStage) + L".txt"),
			L"failed; hardlink-error=" + std::to_wstring(hardlinkError)
				+ L"; junction-error=" + std::to_wstring(junctionError)
				+ L"; reparse-cleanup-error=" + std::to_wstring(reparseCleanupError)
				+ L"; transaction-error=" + std::to_wstring(transactionError) + L"\n");
		if (targetHandle != INVALID_HANDLE_VALUE) CloseHandle(targetHandle);
		const bool cleaned = cleanupDirectoryTreeByHandle(root);
		if (!ok) SetLastError(static_cast<DWORD>(failureStage * 100000
			+ ((transactionError != ERROR_SUCCESS ? transactionError
				: reparseCleanupError) % 100000)));
		return ok && cleaned && pathIsMissing(root);
	}

	bool pixStateResidueAbsent(const std::wstring& pix)
	{
		WIN32_FIND_DATAW entry{};
		HANDLE search = FindFirstFileW(join(pix, L"*").c_str(), &entry);
		if (search == INVALID_HANDLE_VALUE) return false;
		bool clean = true;
		do
		{
			const std::wstring name = entry.cFileName;
			if (name.rfind(L".turborama-", 0) == 0)
			{
				clean = false;
				break;
			}
		} while (FindNextFileW(search, &entry));
		FindClose(search);
		return clean;
	}

	bool pixStateMatchesSnapshot(const std::wstring& target,
		const PixStateBackup& backup)
	{
		const std::wstring pix = join(target, L".emulationstation\\pix");
		bool ok = true;
		for (const auto& expected : backup.files)
		{
			const std::wstring path = join(pix, expected.name);
			if (!expected.existed)
			{
				if (!pathIsMissing(path)) ok = false;
				continue;
			}
			HANDLE object = CreateFileW(path.c_str(), GENERIC_READ | READ_CONTROL
				| FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			FilesystemMetadata current;
			std::vector<unsigned char> content;
			const bool matched = object != INVALID_HANDLE_VALUE
				&& captureFilesystemMetadataFromHandle(object, path, false, current)
				&& readOpenedFileContent(object, current, content)
				&& content == expected.content
				&& current.size.QuadPart == expected.metadata.size.QuadPart
				&& sameHash(current.hash.data(), expected.metadata.hash.data())
				&& sameBasicMetadata(current.basic, expected.metadata.basic)
				&& current.security.daclProtected == expected.metadata.security.daclProtected
				&& current.security.descriptor == expected.metadata.security.descriptor;
			if (object != INVALID_HANDLE_VALUE) CloseHandle(object);
			if (!matched) ok = false;
		}
		return ok && pixStateResidueAbsent(pix);
	}

	bool validatePixStateAtomicRestoreSelfTest()
	{
		int failureStage = 1;
		wchar_t temporaryDirectory[MAX_PATH + 1]{};
		if (GetTempPathW(MAX_PATH, temporaryDirectory) == 0) return false;
		const std::wstring root = join(temporaryDirectory,
			L"TurboRama-pix-state-self-test-" + std::to_wstring(GetCurrentProcessId())
				+ L"-" + std::to_wstring(GetTickCount64()));
		const std::wstring target = join(root, L"target");
		const std::wstring pix = join(target, L".emulationstation\\pix");
		const std::wstring rollback = join(root, L"rollback");
		removeTree(root);
		const std::wstring key = join(pix, L"credential-agent-key.dat");
		const std::wstring installLog = join(pix, installLogFileName());
		const std::wstring marker = join(pix, L"agent-stop.request");
		bool ok = ensureDirectory(pix);
		if (ok) { failureStage = 2; ok = ensureDirectory(rollback); }
		if (ok) { failureStage = 3; ok = writeUtf8FileForSelfTest(key, L"old-key\n"); }
		if (ok) { failureStage = 4; ok = writeUtf8FileForSelfTest(marker, L"old-marker\n"); }
		if (ok) { failureStage = 5; ok = writeUtf8FileForSelfTest(installLog, L"old-log\n"); }
		if (ok) ok = SetFileAttributesW(key.c_str(),
			FILE_ATTRIBUTE_HIDDEN) != FALSE;
		PixStateBackup backup;
		if (ok) { failureStage = 6; ok = backupPixStateRange(target, rollback, backup, 0,
			pixTransactionalStateNames().size(), true); }
		if (ok)
		{
			failureStage = 7;
			HANDLE stickyWriter = CreateFileW(key.c_str(), GENERIC_READ | GENERIC_WRITE,
				FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			AtomicFileReplaceResult stickyResult;
			const std::vector<unsigned char> rejectedBytes = { 'r','e','j','e','c','t','\n' };
			ok = stickyWriter != INVALID_HANDLE_VALUE
				&& !replaceFileBytesAtomically(key, rejectedBytes, nullptr,
					&stickyResult, nullptr)
				&& stickyResult.rollbackComplete && stickyResult.residueFree
				&& pixTemporaryResidueAbsent(pix);
			if (stickyWriter != INVALID_HANDLE_VALUE) CloseHandle(stickyWriter);
		}
		if (ok)
		{
			failureStage = 71;
			ok = writeUtf8FileForSelfTest(marker, L"installer-update\n")
				&& freezePixStateAfterAgentStop(target, backup);
		}
		if (ok)
		{
			failureStage = 72;
			HANDLE blockedWriter = CreateFileW(key.c_str(), GENERIC_WRITE,
				FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			const bool existingBlocked = blockedWriter == INVALID_HANDLE_VALUE;
			if (blockedWriter != INVALID_HANDLE_VALUE) CloseHandle(blockedWriter);
			const std::wstring absent = join(pix, L"agent-public-key.pem");
			HANDLE injected = CreateFileW(absent.c_str(), GENERIC_WRITE,
				FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, CREATE_NEW,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
			bool absenceGuarded = injected == INVALID_HANDLE_VALUE;
			if (injected != INVALID_HANDLE_VALUE)
			{
				CloseHandle(injected);
				absenceGuarded = !validatePinnedPixCurrentState(target, backup)
					&& removeRegularFileIfPresent(absent)
					&& validatePinnedPixCurrentState(target, backup);
			}
			ok = existingBlocked && absenceGuarded;
		}
		if (ok)
		{
			failureStage = 73;
			ok = mutatePinnedPixFileWriteUtf8(target, backup,
				L"credential-agent-key.dat", L"mutated-key\n")
				&& mutatePinnedPixFileWriteUtf8(target, backup,
					L"agent-public-key.pem", L"created-after-snapshot\n")
				&& mutatePinnedPixFileWriteUtf8(target, backup,
					installLogFileName(), L"new-log\n")
				&& mutatePinnedPixFileDelete(target, backup, L"agent-stop.request")
				&& validatePinnedPixCurrentState(target, backup);
		}
		if (ok) { failureStage = 8; ok = restorePixState(target, backup); }
		if (ok) ok = finalizePixStatePins(target, backup);
		if (ok) { failureStage = 9; ok = pixStateMatchesSnapshot(target, backup); }
		const DWORD operationError = ok ? ERROR_SUCCESS : GetLastError();
		if (!ok) closePixStatePins(backup);
		if (ok)
		{
			failureStage = 10;
			const std::wstring blockedRoot = join(root, L"blocked-cleanup");
			const std::wstring blockedFile = join(blockedRoot, L"writer.bin");
			ok = ensureDirectory(blockedRoot)
				&& writeUtf8FileForSelfTest(blockedFile, L"busy\n");
			HANDLE writer = ok ? CreateFileW(blockedFile.c_str(), GENERIC_WRITE,
				FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
				FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr)
				: INVALID_HANDLE_VALUE;
			if (ok) ok = writer != INVALID_HANDLE_VALUE
				&& !cleanupDirectoryTreeByHandle(blockedRoot)
				&& !pathIsMissing(blockedRoot) && !pathIsMissing(blockedFile);
			if (writer != INVALID_HANDLE_VALUE) CloseHandle(writer);
			if (ok) ok = cleanupDirectoryTreeByHandle(blockedRoot)
				&& pathIsMissing(blockedRoot);
		}
		const bool cleaned = cleanupDirectoryTreeByHandle(root);
		if (ok && !cleaned) failureStage = 11;
		if (!ok || !cleaned || !pathIsMissing(root))
			SetLastError(static_cast<DWORD>(failureStage * 100000
				+ (operationError == ERROR_SUCCESS ? GetLastError() : operationError)));
		return ok && cleaned && pathIsMissing(root);
	}


}

_Use_decl_annotations_
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
	std::vector<wchar_t> moduleBuffer(32768);
	if (GetModuleFileNameW(nullptr, moduleBuffer.data(),
		static_cast<DWORD>(moduleBuffer.size())) == 0) return 10;
	const std::wstring module = moduleBuffer.data();
	if (_wcsicmp(PathFindFileNameW(module.c_str()), L"TurboRamaInstaller.exe") != 0) return 10;
	if (hasSingleArgument(L"--self-test"))
	{
		if (!validateLayoutSelectionContract()) return 57;
		if (!validateChildRunResultContract()) return 58;
		if (!validateMetadataWithExclusiveLock()) return 59;
		if (!validateIsolatedProcessInspection()) return 60;
		if (!validateReadOnlyPins()) return 61;
		if (!validateSecurityPlanBoundarySelfTest()) return 62;
		if (!validateAtomicPublicationAndMetadataSelfTest()) return 63;
		if (!validatePixStateAtomicRestoreSelfTest()) return 64;
		if (!validateTestOnlyDirectoryPinScope()) return 65;
		if (!validateSecurityOnlyHandleShareSelfTest()) return 67;
		return 0;
	}
	if (hasSingleArgument(L"--validate-installed-kiosk-identity"))
	{
		ResolvedIdentity identity;
		return resolveKioskIdentity(L"C:\\TurboRama\\Config\\turborama.json", false, identity)
			? 0 : 66;
	}

	BootstrapArguments bootstrap;
	if (!parseBootstrapArguments(bootstrap)) return 10;
	const std::wstring source = parentOf(module);
	const std::wstring archive = join(source, L"payload.7z");
	const std::wstring sevenZip = join(source, L"7za.exe");
	const bool elevated = isProcessElevated();
	const std::wstring requestedTarget = bootstrap.isolatedSmoke && !elevated
		? environmentValue(L"TURBORAMA_INSTALL_TARGET") : std::wstring();
	const std::wstring expectedSmokeTarget = isolatedSmokeTarget();
	const bool silentTest = bootstrap.isolatedSmoke && !elevated
		&& environmentValue(L"TURBORAMA_INSTALLER_SILENT_TEST") == L"1"
		&& !expectedSmokeTarget.empty() && normalized(requestedTarget) == expectedSmokeTarget
		&& normalized(parentOf(source)) == normalized(parentOf(requestedTarget));
	if (bootstrap.isolatedSmoke != silentTest || (!silentTest && !elevated))
	{
		MessageBoxW(nullptr,
			L"O instalador interno so pode ser iniciado pelo pacote externo elevado e autenticado.",
			kTitle, MB_OK | MB_ICONERROR);
		return 17;
	}

	InstalledLayout layout;
	std::wstring launcherConfig;
	std::wstring launcherProcess;
	std::wstring maintenanceLockPath;
	if (silentTest)
	{
		layout.target = requestedTarget;
		layout.emulationStationExecutable = join(layout.target, L"emulationstation.exe");
		layout.wrapperExecutable = environmentValue(L"TURBORAMA_FRONTEND_WRAPPER");
		launcherConfig = environmentValue(L"TURBORAMA_LAUNCHER_CONFIG");
		launcherProcess = environmentValue(L"TURBORAMA_LAUNCHER_PROCESS");
		maintenanceLockPath = environmentValue(L"TURBORAMA_MAINTENANCE_LOCK");
	}
	else
	{
		SetEnvironmentVariableW(L"TURBORAMA_INSTALL_TARGET", nullptr);
		SetEnvironmentVariableW(L"TURBORAMA_INSTALLER_SILENT_TEST", nullptr);
		SetEnvironmentVariableW(L"TURBORAMA_FRONTEND_WRAPPER", nullptr);
		SetEnvironmentVariableW(L"TURBORAMA_LAUNCHER_CONFIG", nullptr);
		SetEnvironmentVariableW(L"TURBORAMA_LAUNCHER_PROCESS", nullptr);
		SetEnvironmentVariableW(L"TURBORAMA_MAINTENANCE_LOCK", nullptr);
		SetEnvironmentVariableW(L"TURBORAMA_INSTALLER_TEST_FAIL_AFTER_EXTRACT", nullptr);
		SetEnvironmentVariableW(L"TURBORAMA_INSTALLER_TEST_FAIL_AFTER_PIX_STATE", nullptr);
		SetEnvironmentVariableW(L"TURBORAMA_INSTALLER_TEST_REFUSE_PROCESS_STOP", nullptr);
		enablePrivilege(SE_BACKUP_NAME);
		enablePrivilege(SE_TAKE_OWNERSHIP_NAME);
		enablePrivilege(SE_RESTORE_NAME);

		const LayoutSelectionResult layoutResult = selectProductionLayout(layout);
		if (layoutResult != LayoutSelectionResult::Selected)
		{
			std::wstring detail;
			if (layoutResult == LayoutSelectionResult::Missing)
				detail = L"Nenhum layout completo foi encontrado.";
			else if (layoutResult == LayoutSelectionResult::Ambiguous)
				detail = L"Os layouts flat e classico coexistem; a selecao seria ambigua.";
			else
				detail = L"Foi encontrado um layout parcial, redirecionado, com hardlink ou inconsistente.";
			const std::wstring message = L"Nao foi possivel selecionar com seguranca a instalacao em D:. "
				L"O wrapper e o EmulationStation nao foram alterados.\n\n" + detail;
			MessageBoxW(nullptr, message.c_str(), kTitle, MB_OK | MB_ICONERROR);
			return 25;
		}
		launcherConfig = L"C:\\TurboRama\\Config\\turborama.json";
		launcherProcess = L"C:\\TurboRama\\App\\Launcher\\TurboRama.Launcher.exe";
		maintenanceLockPath = L"C:\\TurboRama\\State\\maintenance.lock";
	}

	const std::wstring target = layout.target;
	const std::wstring targetExecutable = layout.emulationStationExecutable;
	auto absoluteRegular = [](const std::wstring& path)
	{
		return !path.empty() && PathIsRelativeW(path.c_str()) == FALSE
			&& validateRegularFileNoReparseOrHardlink(path);
	};
	const bool separatedSmokeWrapper = !silentTest
		|| (normalized(layout.wrapperExecutable) != normalized(targetExecutable)
			&& !beginsWith(normalized(layout.wrapperExecutable), normalized(target) + L"\\"));
	if (!separatedSmokeWrapper || !absoluteRegular(layout.wrapperExecutable))
	{
		if (!silentTest) MessageBoxW(nullptr,
			L"O wrapper selecionado esta ausente, inseguro ou sobreposto ao target. Nenhum arquivo foi alterado.",
			kTitle, MB_OK | MB_ICONERROR);
		return 25;
	}
	if (!absoluteRegular(launcherProcess)
		|| launcherConfig.empty() || PathIsRelativeW(launcherConfig.c_str())
		|| !validateSensitiveTargetPaths(target, launcherConfig))
	{
		if (!silentTest) MessageBoxW(nullptr,
			L"A instalacao, a ponte .emulationstation\\pix, o wrapper, o Launcher ou o turborama.json estao ausentes ou inseguros. "
			L"Esta atualizacao interna nao cria uma ponte PIX nova; nenhum arquivo foi alterado.",
			kTitle, MB_OK | MB_ICONERROR);
		return 11;
	}
	if (staleTransactionStatePresent(source, target))
	{
		if (!silentTest) MessageBoxW(nullptr,
			L"Foi detectado um estado transacional anterior ou um arquivo temporario de troca. Por seguranca, esta execucao nao alterou arquivos nem processos. A recuperacao pos-crash precisa de revisao manual antes de uma nova tentativa.",
			kTitle, MB_OK | MB_ICONERROR);
		return 26;
	}

	HANDLE stagingLock = validateAndLockStaging(source, !silentTest);
	if (stagingLock == INVALID_HANDLE_VALUE)
	{
		if (!silentTest) MessageBoxW(nullptr,
			L"O staging administrativo nao passou na validacao de seguranca.",
			kTitle, MB_OK | MB_ICONERROR);
		return 10;
	}
	HANDLE installerPin = openPinnedFile(module, bootstrap.installerHash, !silentTest);
	HANDLE sevenZipPin = openPinnedFile(sevenZip, bootstrap.sevenZipHash, !silentTest);
	HANDLE payloadPin = openPinnedFile(archive, bootstrap.payloadHash, !silentTest);
	HANDLE maintenanceLockPin = INVALID_HANDLE_VALUE;
	FILE_ID_INFO maintenanceLockIdentity{};
	PinnedFileEvidence wrapperPin;
	PinnedFileEvidence launcherConfigPin;
	std::vector<PinnedDirectory> targetDirectoryPins;
	PixStateBackup pixStateBackup;
	auto closePinned = [&]()
	{
		closePixStatePins(pixStateBackup);
		closePinnedDirectories(targetDirectoryPins);
		closePinnedEvidence(launcherConfigPin);
		closePinnedEvidence(wrapperPin);
		if (maintenanceLockPin != INVALID_HANDLE_VALUE)
		{
			CloseHandle(maintenanceLockPin);
			maintenanceLockPin = INVALID_HANDLE_VALUE;
		}
		if (payloadPin != INVALID_HANDLE_VALUE)
		{
			CloseHandle(payloadPin);
			payloadPin = INVALID_HANDLE_VALUE;
		}
		if (sevenZipPin != INVALID_HANDLE_VALUE)
		{
			CloseHandle(sevenZipPin);
			sevenZipPin = INVALID_HANDLE_VALUE;
		}
		if (installerPin != INVALID_HANDLE_VALUE)
		{
			CloseHandle(installerPin);
			installerPin = INVALID_HANDLE_VALUE;
		}
		if (stagingLock != INVALID_HANDLE_VALUE)
		{
			CloseHandle(stagingLock);
			stagingLock = INVALID_HANDLE_VALUE;
		}
	};
	if (installerPin == INVALID_HANDLE_VALUE || sevenZipPin == INVALID_HANDLE_VALUE
		|| payloadPin == INVALID_HANDLE_VALUE)
	{
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"Os arquivos internos mudaram depois da validacao do pacote.",
			kTitle, MB_OK | MB_ICONERROR);
		return 10;
	}

	maintenanceLockPin = pinReadOnlyMaintenanceLock(maintenanceLockPath);
	DWORD lockBindingError = ERROR_SUCCESS;
	if (maintenanceLockPin == INVALID_HANDLE_VALUE
		|| !validateOpenedFilesystemObject(maintenanceLockPin, maintenanceLockPath, false,
			&maintenanceLockIdentity, &lockBindingError))
	{
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"O maintenance.lock obrigatorio esta ausente, trocado, redirecionado ou possui hardlink. "
			L"Nenhum processo foi interrompido e nenhum arquivo foi alterado.",
			kTitle, MB_OK | MB_ICONERROR);
		return 24;
	}
	if (!pinReadOnlyFileEvidence(layout.wrapperExecutable, wrapperPin))
	{
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"O wrapper selecionado nao pode ser fixado para leitura ou mudou durante a validacao. Nenhum processo foi interrompido.",
			kTitle, MB_OK | MB_ICONERROR);
		return 25;
	}
	if (!pinReadOnlyFileEvidence(launcherConfig, launcherConfigPin))
	{
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"O turborama.json nao pode ser fixado para leitura ou mudou durante a validacao. Nenhum processo foi interrompido.",
			kTitle, MB_OK | MB_ICONERROR);
		return 19;
	}
	const std::wstring testOnlyTrustedPinRoot = silentTest
		? parentOf(target) : std::wstring();
	if (!pinDirectoryChain(target, targetDirectoryPins,
		silentTest ? &testOnlyTrustedPinRoot : nullptr))
	{
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"A cadeia de diretorios do layout selecionado em D: nao pode ser fixada sem compartilhamento de exclusao. Nenhum processo foi interrompido.",
			kTitle, MB_OK | MB_ICONERROR);
		return 25;
	}

	std::wstring launcherJson, configuredKioskUser, configuredFrontend;
	bool configuredFrontendPresent = false;
	if (!readUtf8FileStrict(launcherConfig, launcherJson)
		|| !JsonReader(launcherJson).readLauncherValues(configuredKioskUser,
			configuredFrontend, configuredFrontendPresent))
	{
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"O turborama.json nao e um JSON valido com kioskUser unico. Nenhum processo foi interrompido.",
			kTitle, MB_OK | MB_ICONERROR);
		return 19;
	}
	if (!launcherFrontendMatchesLayout(configuredFrontendPresent, configuredFrontend,
		layout.wrapperExecutable))
	{
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"frontendExecutable aponta para um terceiro frontend existente, um caminho relativo ou um objeto inseguro. "
			L"A configuracao nao sera corrigida automaticamente; nenhum arquivo foi alterado.",
			kTitle, MB_OK | MB_ICONERROR);
		return 25;
	}

	auto coordinationEvidenceIntact = [&]()
	{
		FILE_ID_INFO currentLockIdentity{};
		DWORD bindingError = ERROR_SUCCESS;
		return validateOpenedFilesystemObject(maintenanceLockPin, maintenanceLockPath, false,
			&currentLockIdentity, &bindingError)
			&& sameFileIdentity(currentLockIdentity, maintenanceLockIdentity)
			&& revalidatePinnedFileEvidence(wrapperPin)
			&& revalidatePinnedFileEvidence(launcherConfigPin)
			&& revalidatePinnedDirectories(targetDirectoryPins);
	};

	ResolvedIdentity kioskIdentity;
	if (!resolveKioskIdentity(launcherConfig, silentTest, kioskIdentity))
	{
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"Nao foi possivel confirmar a conta Windows do TurboRama a partir do turborama.json/Winlogon. Neste gabinete a conta correta e Admin. Nenhum arquivo foi alterado.",
			kTitle, MB_OK | MB_ICONERROR);
		return 19;
	}
	if (!silentTest && MessageBoxW(nullptr,
		L"Instalar o candidato interno do Sistema PIX Comercial?\n\n"
		L"- Somente o EmulationStation selecionado em D: e os tres artefatos PIX serao trocados.\n"
		L"- Factory Pack, wrapper, Launcher, servicos, cache, .runtime, ROMs e temas permanecem fora do escopo.\n"
		L"- Esta compilacao e para validacao interna; nao esta liberada para venda.",
		kTitle, MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON1) != IDYES)
	{
		closePinned();
		return 0;
	}

	const std::wstring stagedPayload = join(source, L"payload-expanded");
	if (!CreateDirectoryW(stagedPayload.c_str(), nullptr)
		|| (!silentTest && !applyAdminOnlySecurity(stagedPayload, true)))
	{
		closePinned();
		return 10;
	}
	DWORD childExitCode = 999;
	const std::wstring extractArguments = L"x -y \"" + archive + L"\" -o\"" + stagedPayload + L"\"";
	const ChildRunResult extractionResult = runAndWait(sevenZip, extractArguments,
		10 * 60 * 1000, childExitCode);
	if (childTreeStateUnconfirmed(extractionResult))
	{
		closePinned();
		return kAuxiliaryTreeUnconfirmedExitCode;
	}
	if (extractionResult != ChildRunResult::Completed || childExitCode != 0
		|| !validateTreeNoReparse(stagedPayload)
		|| (!silentTest && !secureStagedTree(stagedPayload))
		|| !exists(join(stagedPayload, L"emulationstation.exe"))
		|| !exists(join(stagedPayload, L"pix-agent\\TurboRamaPixAgent.dll"))
		|| !exists(join(stagedPayload, L"pix-agent\\runtime\\dotnet.exe"))
		|| !exists(join(stagedPayload, L"CONFIGURAR-ACCESS-TOKEN-PIX.exe"))
		|| !exists(join(stagedPayload, L"CONFIGURAR-USER-TOKEN-PIX.exe")))
	{
		closePinned();
		return 10;
	}

	const std::wstring transactionBackup = join(source, L"rollback");
	const bool transactionBackupCreated = CreateDirectoryW(transactionBackup.c_str(),
		nullptr) != FALSE;
	if (!transactionBackupCreated
		|| !validateDirectoryNoReparse(transactionBackup)
		|| (!silentTest && !applyAdminOnlySecurity(transactionBackup, true)))
	{
		const bool backupRemoved = !transactionBackupCreated
			? pathIsMissing(transactionBackup)
			: cleanupDirectoryTreeByHandle(transactionBackup);
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"Nao foi possivel criar o backup transacional. Nada em D: foi substituido.",
			kTitle, MB_OK | MB_ICONERROR);
		return backupRemoved ? 12 : 14;
	}
	if (!backupAgentStopRequest(target, transactionBackup, pixStateBackup))
	{
		const bool backupRemoved = cleanupDirectoryTreeByHandle(transactionBackup);
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"Nao foi possivel registrar o estado PIX para rollback exato. Nada em D: foi substituido.",
			kTitle, MB_OK | MB_ICONERROR);
		return backupRemoved ? 12 : 14;
	}

	const std::wstring privateDotnet = join(target, L"pix-agent\\runtime\\dotnet.exe");
	const std::wstring standaloneAgent = join(target, L"pix-agent\\TurboRamaPixAgent.exe");
	const std::wstring userConfigurator = join(target, L"CONFIGURAR-USER-TOKEN-PIX.exe");
	const std::wstring accessConfigurator = join(target, L"CONFIGURAR-ACCESS-TOKEN-PIX.exe");
	std::vector<std::wstring> processPaths;
	auto addProcessPath = [&](const std::wstring& path)
	{
		for (const auto& existing : processPaths)
			if (normalized(existing) == normalized(path)) return;
		processPaths.push_back(path);
	};
	addProcessPath(launcherProcess);
	addProcessPath(layout.wrapperExecutable);
	addProcessPath(targetExecutable);
	addProcessPath(userConfigurator);
	addProcessPath(accessConfigurator);
	addProcessPath(privateDotnet);
	addProcessPath(standaloneAgent);
	auto quiesceExactProcesses = [&]()
	{
		if (!coordinationEvidenceIntact()) return false;
		for (const auto& path : processPaths)
			if (!stopExactProcessAndConfirm(path, !silentTest)) return false;
		return coordinationEvidenceIntact();
	};

	bool stopPrepared = !(silentTest
		&& environmentValue(L"TURBORAMA_INSTALLER_TEST_REFUSE_PROCESS_STOP") == L"1");
	if (stopPrepared)
	{
		for (const auto& path : { launcherProcess, layout.wrapperExecutable,
			targetExecutable, userConfigurator, accessConfigurator })
			if (!stopExactProcessAndConfirm(path, !silentTest)) { stopPrepared = false; break; }
	}
	if (stopPrepared && directoryExists(join(target, L".emulationstation\\pix")))
		stopPrepared = requestGracefulAgentStop(target);
	if (stopPrepared)
	{
		waitForAgentProcessesExit(privateDotnet, standaloneAgent, 35000);
		stopPrepared = quiesceExactProcesses();
	}
	if (stopPrepared)
	{
		Sleep(250);
		stopPrepared = quiesceExactProcesses();
	}
	if (!stopPrepared)
	{
		const bool processesQuiet = quiesceExactProcesses();
		const bool pixRestored = processesQuiet
			? restorePixState(target, pixStateBackup) : false;
		const bool backupRemoved = cleanupDirectoryTreeByHandle(transactionBackup);
		const bool stateRestored = processesQuiet && pixRestored && backupRemoved;
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			stateRestored
				? L"Nao foi possivel confirmar a parada dos processos exatos. O estado PIX anterior foi restaurado; nenhum binario foi substituido."
				: L"Nao foi possivel confirmar a parada e o rollback do estado PIX ficou incompleto. Nao inicie o TurboRama.",
			kTitle, MB_OK | MB_ICONERROR);
		return stateRestored ? 18 : 16;
	}
	// O marker precisa ser salvo antes do pedido gracioso; os outros onze arquivos
	// so sao copiados agora, depois de confirmar a ausencia do agente PIX.
	if (!completePixStateBackup(target, transactionBackup, pixStateBackup)
		|| !freezePixStateAfterAgentStop(target, pixStateBackup))
	{
		const bool processesQuiet = quiesceExactProcesses();
		const bool pixRestored = processesQuiet
			? restorePixState(target, pixStateBackup) : false;
		const bool backupRemoved = cleanupDirectoryTreeByHandle(transactionBackup);
		const bool restored = processesQuiet && pixRestored && backupRemoved;
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			restored
				? L"O estado PIX mudou durante a janela de parada. O marker anterior foi restaurado e nenhum binario foi substituido."
				: L"O estado PIX mudou durante a janela de parada e a restauracao do marker ficou incompleta.",
			kTitle, MB_OK | MB_ICONERROR);
		return restored ? 12 : 16;
	}

	KioskIdentityTransition identityTransition;
	if (!readRecordedKioskIdentity(target, kioskIdentity, identityTransition))
	{
		const bool processesQuiet = quiesceExactProcesses();
		const bool pixRestored = processesQuiet
			? restorePixState(target, pixStateBackup) : false;
		const bool backupRemoved = cleanupDirectoryTreeByHandle(transactionBackup);
		const bool restored = processesQuiet && pixRestored && backupRemoved;
		closePinned();
		return restored ? 19 : 16;
	}

	AtomicInstallTransaction installTransaction;
	const HANDLE targetDirectory = pinnedDirectoryHandle(targetDirectoryPins, target);
	const bool transactionPrepared = quiesceExactProcesses()
		&& prepareInstallTransaction(stagedPayload, target, targetDirectory,
			installTransaction);
	if (!transactionPrepared)
	{
		const bool candidatesRemoved = abandonPreparedInstallTransaction(installTransaction);
		const bool processesQuiet = quiesceExactProcesses();
		const bool pixRestored = processesQuiet
			? restorePixState(target, pixStateBackup) : false;
		const bool backupRemoved = cleanupDirectoryTreeByHandle(transactionBackup);
		const bool restored = candidatesRemoved && processesQuiet && pixRestored
			&& backupRemoved && coordinationEvidenceIntact();
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			restored
				? L"A preparacao atomica dos quatro artefatos falhou antes da publicacao. Os temporarios foram removidos e o estado PIX anterior foi restaurado."
				: L"A preparacao atomica falhou e a recuperacao ou limpeza ficou incompleta. Nao inicie o TurboRama.",
			kTitle, MB_OK | MB_ICONERROR);
		return restored ? 12 : 14;
	}
	InstallationSecurityContext installationSecurity;
	SecurityFailure installationSecurityFailure;
	SecurityFailure installationSecurityRollbackFailure;
	bool transactionFinalized = false;
	auto rollbackAll = [&]()
	{
		if (transactionFinalized) return false;
		transactionFinalized = true;
		const bool processesQuiet = quiesceExactProcesses();
		if (!processesQuiet)
		{
			// Sem a ausencia confirmada dos processos, nao iniciamos novas mutacoes.
			// Ainda assim liberamos todos os handles antes de fechar os pins e relatamos
			// rollback incompleto, sem recibo de sucesso.
			closeAtomicInstallTransactionHandles(installTransaction);
			(void)cleanupDirectoryTreeByHandle(transactionBackup);
			return false;
		}
		const bool securityRestored = !installationSecurity.mutationAttempted
			|| restoreInstallationSecurity(target, installationSecurity,
				&installationSecurityRollbackFailure);
		const bool installRestored = rollbackInstallTransaction(installTransaction);
		const bool pixRestored = restorePixState(target, pixStateBackup);
		const bool evidenceRestored = coordinationEvidenceIntact();
		const bool backupRemoved = cleanupDirectoryTreeByHandle(transactionBackup);
		return processesQuiet && securityRestored && installRestored && pixRestored
			&& evidenceRestored && backupRemoved;
	};

	bool installed = quiesceExactProcesses()
		&& publishInstallTransaction(installTransaction)
		&& validatePublishedInstallTransaction(installTransaction);
	const bool forceRollbackTest = silentTest
		&& environmentValue(L"TURBORAMA_INSTALLER_TEST_FAIL_AFTER_EXTRACT") == L"1";
	if (!installed || forceRollbackTest)
	{
		const bool restored = rollbackAll();
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			restored
				? L"A troca dos quatro artefatos falhou. O conjunto anterior e o estado PIX foram restaurados."
				: L"A troca falhou e o rollback ficou incompleto. Nao inicie o TurboRama.",
			kTitle, MB_OK | MB_ICONERROR);
		return restored ? 13 : 14;
	}

	bool pixUpdated = quiesceExactProcesses()
		&& recordKioskIdentity(target, kioskIdentity, identityTransition, &pixStateBackup)
		&& quiesceExactProcesses()
		&& resetCredentialEditorState(target, &pixStateBackup);
	const bool forcePixRollbackTest = silentTest
		&& environmentValue(L"TURBORAMA_INSTALLER_TEST_FAIL_AFTER_PIX_STATE") == L"1";
	if (!pixUpdated || forcePixRollbackTest)
	{
		const bool restored = rollbackAll();
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			restored
				? L"A atualizacao do estado PIX falhou. Binarios e estado PIX anteriores foram restaurados exatamente."
				: L"A atualizacao do estado PIX falhou e o rollback ficou incompleto. Nao inicie o TurboRama.",
			kTitle, MB_OK | MB_ICONERROR);
		return restored ? 15 : 16;
	}

	if (!quiesceExactProcesses()
		|| !mutatePinnedPixFileDelete(target, pixStateBackup, L"agent-stop.request")
		|| !quiesceExactProcesses())
	{
		const bool restored = rollbackAll();
		closePinned();
		return restored ? 15 : 16;
	}

	// O smoke test autenticado roda propositalmente sem elevacao e em uma arvore
	// temporaria. Ele valida integralmente o plano, os snapshots, os limites de
	// dados e a transacao, mas nao tenta trocar owner/DACL: essa operacao exige o
	// token elevado que o bootstrap real fornece. A instalacao real continua
	// fail-closed e so avanca depois de aplicar e validar todas as ACLs.
	const bool installationSecurityProtected = silentTest
		? (quiesceExactProcesses()
			&& captureInstallationSecurity(target, installationSecurity,
				&installationSecurityFailure)
			&& validateWritableDataScopes(target, &installationSecurityFailure)
			&& validatePinnedPixCurrentState(target, pixStateBackup)
			&& quiesceExactProcesses())
		: (quiesceExactProcesses()
			&& hardenInstallationSecurity(target, kioskIdentity, installationSecurity,
				&installationSecurityFailure)
			&& validateWritableDataScopes(target, &installationSecurityFailure)
			&& acceptPinnedPixSecurityTransition(target, pixStateBackup)
			&& quiesceExactProcesses());
	if (!installationSecurityProtected)
	{
		if (installationSecurityFailure.empty())
			recordSecurityFailure(&installationSecurityFailure,
				L"barreira transacional da ACL", target,
				GetLastError() == ERROR_SUCCESS ? ERROR_GEN_FAILURE : GetLastError());
		const bool restored = rollbackAll();
		const std::wstring incident = silentTest ? std::wstring()
			: writeSecurityIncidentLog(installationSecurityFailure,
				installationSecurity.mutationAttempted, restored,
				installationSecurityRollbackFailure);
		closePinned();
		if (!silentTest)
		{
			std::wstring message = restored
				? L"A protecao de permissoes da instalacao falhou. Binarios, estado PIX e permissoes anteriores foram restaurados."
				: L"A protecao de permissoes falhou e o rollback ficou incompleto. Nao inicie o TurboRama.";
			message += L"\n\n" + securityFailureText(installationSecurityFailure);
			if (!incident.empty()) message += L"\n\nRegistro protegido: " + incident;
			MessageBoxW(nullptr, message.c_str(), kTitle, MB_OK | MB_ICONERROR);
		}
		return restored ? 20 : 16;
	}

	const bool finalEvidenceValid = coordinationEvidenceIntact()
		&& validatePublishedInstallTransaction(installTransaction)
		&& validatePinnedPixCurrentState(target, pixStateBackup);
	if (!finalEvidenceValid)
	{
		const bool restored = rollbackAll();
		closePinned();
		return restored ? 15 : 16;
	}
	// Commit: os tombstones e o backup PIX sao limpos e a ausencia de ambos e
	// confirmada. Uma falha daqui em diante nunca e anunciada como sucesso.
	const bool artifactsCommitted = commitInstallTransaction(installTransaction);
	if (!artifactsCommitted && !installTransaction.commitStarted)
	{
		const bool restored = rollbackAll();
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			restored
				? L"O preflight final do commit falhou; os artefatos e o estado PIX anteriores foram restaurados."
				: L"O preflight final do commit falhou e o rollback ficou incompleto. Nao inicie o TurboRama.",
			kTitle, MB_OK | MB_ICONERROR);
		return restored ? 15 : 14;
	}
	transactionFinalized = true;
	const bool backupRemoved = cleanupDirectoryTreeByHandle(transactionBackup);
	if (!artifactsCommitted || !backupRemoved || !coordinationEvidenceIntact())
	{
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"Os novos artefatos foram publicados, mas a limpeza transacional nao foi confirmada por completo. Nao foi gravado recibo de sucesso; revise a instalacao antes de iniciar o quiosque.",
			kTitle, MB_OK | MB_ICONERROR);
		return 14;
	}
	if (!quiesceExactProcesses())
	{
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"Os novos artefatos foram publicados, mas a quiescencia final nao pode ser confirmada. Nao foi gravado recibo de sucesso.",
			kTitle, MB_OK | MB_ICONERROR);
		return 14;
	}
	if (!validatePinnedPixCurrentState(target, pixStateBackup))
	{
		closeAtomicInstallTransactionHandles(installTransaction);
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"A protecao final do estado PIX nao foi confirmada. Nao foi gravado recibo de sucesso.",
			kTitle, MB_OK | MB_ICONERROR);
		return 14;
	}
	const bool editorPrepared = false;
	// O recibo pertence ao conjunto PIX rastreado. Se ele ja existia, o writer
	// recebe o mesmo handle exato; se era ausente, vale a limitacao de nome ausente.
	const bool logWritten = writeInstallLog(target, editorPrepared,
		identityTransition.reEnrollmentRequired, &pixStateBackup);
	const bool pixStateProtected = !pixStateBackup.writerFailure
		&& pixStateBackup.writerRollbackComplete
		&& pixStateBackup.writerResidueFree
		&& validatePinnedPixCurrentState(target, pixStateBackup);
	const bool artifactsProtected = validatePublishedInstallTransaction(
		installTransaction);
	const bool transactionResidueFree = relevantTransactionResiduesAbsent(target);
	const bool finalEvidenceProtected = coordinationEvidenceIntact();
	if (!logWritten || !pixStateProtected || !artifactsProtected
		|| !transactionResidueFree || !finalEvidenceProtected)
	{
		closeAtomicInstallTransactionHandles(installTransaction);
		closePinned();
		if (!silentTest) MessageBoxW(nullptr,
			L"A barreira final protegida falhou ao validar o recibo, os artefatos, o estado PIX ou a limpeza transacional. Nao foi gravado recibo de sucesso.",
			kTitle, MB_OK | MB_ICONERROR);
		return 14;
	}

	const std::wstring completionMessage = std::wstring(
		L"CANDIDATO INTERNO INSTALADO. NAO LIBERAR PARA VENDA.\n\n")
		+ L"Foram trocados somente o EmulationStation, os dois configuradores e o pix-agent em:\n"
		+ target + L"\n\n"
		+ L"Factory Pack, wrapper, Launcher, servicos, cache, .runtime, ROMs e temas foram preservados e permaneceram fora do escopo. "
			L"Nenhum servico foi parado ou reiniciado.\n\n"
		+ L"O arquivo legado REPARAR-INSTALACAO-TURBORAMA.ps1, se ja existia em D:, foi preservado sem ser executado ou instalado.\n\n"
		+ (identityTransition.reEnrollmentRequired
			? L"ATENCAO: o SID do quiosque mudou. Recadastre a credencial PIX do proprietario; secret.dat foi preservado.\n\n"
			: L"")
		+ L"O maintenance.lock e o wrapper permaneceram fixados e foram revalidados por FileId; o wrapper tambem por hash e SDDL.";
	if (!silentTest) MessageBoxW(nullptr, completionMessage.c_str(),
		kTitle, MB_OK | MB_ICONINFORMATION);
	// Nao abra uma janela depois da barreira final: guards de namespace, handles
	// raiz e pins exatos permanecem ativos ate o encerramento deste processo.
	return 0;
}
