#ifndef WIN32
#error The Suite licensing gate requires Windows.
#endif
#ifndef TURBORAMA_SUITE_HELPER_SHA256
#error The Suite licensing helper SHA-256 must be pinned at build time.
#endif

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#include <bcrypt.h>
#include <sddl.h>
#include <ShlObj.h>
#include "SuiteAccessGate.h"

#include <array>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstring>
#include <thread>
#include <vector>

namespace
{
	constexpr DWORD LoginTimeoutMs = 300000;
	constexpr DWORD ReplyTimeoutMs = 3000;
	constexpr DWORD PollIntervalMs = 1000;
	constexpr long long MaximumProofAgeMs = 4000;
	constexpr char HelperDigest[] = TURBORAMA_SUITE_HELPER_SHA256;
	constexpr wchar_t HelperName[] = L"TurboRama.Suite.Access.exe";

	class Handle final
	{
	public:
		Handle() = default;
		explicit Handle(HANDLE value) : mValue(value) {}
		~Handle() { reset(); }
		Handle(const Handle&) = delete;
		Handle& operator=(const Handle&) = delete;
		HANDLE get() const { return mValue; }
		bool valid() const { return mValue != nullptr && mValue != INVALID_HANDLE_VALUE; }
		void reset(HANDLE value = nullptr)
		{
			if (valid()) CloseHandle(mValue);
			mValue = value;
		}
	private:
		HANDLE mValue = nullptr;
	};

	long long nowMs()
	{
		return std::chrono::duration_cast<std::chrono::milliseconds>(
			std::chrono::steady_clock::now().time_since_epoch()).count();
	}

	bool fresh(long long now, long long last)
	{
		return last > 0 && now >= last && now - last <= MaximumProofAgeMs;
	}

	bool digestMatches(const unsigned char* digest, const char* expected)
	{
		if (expected == nullptr || std::strlen(expected) != 64) return false;
		unsigned int difference = 0;
		for (size_t i = 0; i < 32; ++i)
		{
			unsigned int value = 0;
			for (size_t j = 0; j < 2; ++j)
			{
				const char c = expected[i * 2 + j];
				const unsigned int digit = c >= '0' && c <= '9' ? c - '0' :
					c >= 'a' && c <= 'f' ? c - 'a' + 10 :
					c >= 'A' && c <= 'F' ? c - 'A' + 10 : 256;
				if (digit > 15) return false;
				value = value * 16 + digit;
			}
			difference |= digest[i] ^ value;
		}
		return difference == 0;
	}

	class Sha256 final
	{
	public:
		Sha256()
		{
			if (BCryptOpenAlgorithmProvider(&mAlgorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0)
				return;
			DWORD size = 0, result = 0;
			if (BCryptGetProperty(mAlgorithm, BCRYPT_OBJECT_LENGTH,
				reinterpret_cast<PUCHAR>(&size), sizeof(size), &result, 0) < 0)
				return;
			mObject.resize(size);
			if (BCryptCreateHash(mAlgorithm, &mHash, mObject.data(), size, nullptr, 0, 0) < 0)
				mHash = nullptr;
		}
		~Sha256()
		{
			if (mHash) BCryptDestroyHash(mHash);
			if (mAlgorithm) BCryptCloseAlgorithmProvider(mAlgorithm, 0);
		}
		bool append(const unsigned char* bytes, ULONG size)
		{
			return mHash && BCryptHashData(mHash, const_cast<PUCHAR>(bytes), size, 0) >= 0;
		}
		bool finish(unsigned char* digest)
		{
			return mHash && BCryptFinishHash(mHash, digest, 32, 0) >= 0;
		}
	private:
		BCRYPT_ALG_HANDLE mAlgorithm = nullptr;
		BCRYPT_HASH_HANDLE mHash = nullptr;
		std::vector<unsigned char> mObject;
	};

	bool helperPath(std::wstring& path, std::wstring& directory)
	{
		std::vector<wchar_t> buffer(32768);
		const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
		if (length == 0 || length >= buffer.size()) return false;
		path.assign(buffer.data(), length);
		const auto slash = path.find_last_of(L"\\/");
		if (slash == std::wstring::npos) return false;
		directory = path.substr(0, slash);
		path = directory + L"\\" + HelperName;
		return true;
	}

	bool lockAndVerifyHelper(const std::wstring& path, Handle& file)
	{
		// Holding this handle with read-only sharing prevents in-place writes,
		// replacement and deletion until the licensing process has stopped.
		file.reset(CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
			OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
		if (!file.valid()) return false;
		BY_HANDLE_FILE_INFORMATION info{};
		if (!GetFileInformationByHandle(file.get(), &info) ||
			(info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) return false;
		Sha256 hash;
		std::vector<unsigned char> buffer(1024 * 1024);
		for (;;)
		{
			DWORD read = 0;
			if (!ReadFile(file.get(), buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr))
				return false;
			if (read == 0) break;
			if (!hash.append(buffer.data(), read)) return false;
		}
		std::array<unsigned char, 32> digest{};
		return hash.finish(digest.data()) && digestMatches(digest.data(), HelperDigest);
	}

	bool sameFile(HANDLE first, HANDLE second)
	{
		BY_HANDLE_FILE_INFORMATION a{}, b{};
		return GetFileInformationByHandle(first, &a) && GetFileInformationByHandle(second, &b) &&
			a.dwVolumeSerialNumber == b.dwVolumeSerialNumber &&
			a.nFileIndexHigh == b.nFileIndexHigh && a.nFileIndexLow == b.nFileIndexLow;
	}

	bool makePipe(Handle& readEnd, Handle& writeEnd, bool childReads)
	{
		SECURITY_ATTRIBUTES security{ sizeof(SECURITY_ATTRIBUTES), nullptr, TRUE };
		HANDLE read = nullptr, write = nullptr;
		if (!CreatePipe(&read, &write, &security, 4096)) return false;
		readEnd.reset(read);
		writeEnd.reset(write);
		return SetHandleInformation(childReads ? write : read, HANDLE_FLAG_INHERIT, 0) != FALSE;
	}

	bool exactReply(const char* data, size_t size, const char* expected)
	{
		const size_t required = std::strlen(expected);
		return size == required && std::memcmp(data, expected, required) == 0;
	}

	bool knownFolder(REFKNOWNFOLDERID id, std::wstring& value)
	{
		PWSTR path = nullptr;
		if (FAILED(SHGetKnownFolderPath(id, KF_FLAG_DEFAULT, nullptr, &path))) return false;
		value.assign(path);
		CoTaskMemFree(path);
		return !value.empty();
	}

	void removePrivateRuntime(const std::wstring& path)
	{
		// The caller supplies only its freshly generated absolute directory.
		// Open the entry itself, denying write/delete sharing before deciding
		// whether to recurse. Keep every ancestor locked during traversal so a
		// directory cannot be swapped for a reparse point between check and use.
		Handle entryLock(CreateFileW(path.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ,
			nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
		BY_HANDLE_FILE_INFORMATION info{};
		if (!entryLock.valid() || !GetFileInformationByHandle(entryLock.get(), &info)) return;
		const DWORD attributes = info.dwFileAttributes;
		if ((attributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
		{
			entryLock.reset();
			// A single file/link deletion never follows its target.
			DeleteFileW(path.c_str());
			return;
		}
		if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0)
		{
			WIN32_FIND_DATAW entry{};
			const HANDLE search = FindFirstFileW((path + L"\\*").c_str(), &entry);
			if (search != INVALID_HANDLE_VALUE)
			{
				do
				{
					if (std::wcscmp(entry.cFileName, L".") != 0 && std::wcscmp(entry.cFileName, L"..") != 0)
						removePrivateRuntime(path + L"\\" + entry.cFileName);
				} while (FindNextFileW(search, &entry));
				FindClose(search);
			}
		}
		entryLock.reset();
		// This removes only the one directory/link entry, never its target tree.
		RemoveDirectoryW(path.c_str());
	}

	bool createRuntimeDirectory(const std::wstring& localAppData, std::wstring& path, Handle& lock)
	{
		Handle token;
		HANDLE rawToken = nullptr;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &rawToken)) return false;
		token.reset(rawToken);
		DWORD size = 0;
		GetTokenInformation(token.get(), TokenUser, nullptr, 0, &size);
		if (size == 0) return false;
		std::vector<unsigned char> userData(size);
		if (!GetTokenInformation(token.get(), TokenUser, userData.data(), size, &size)) return false;
		LPWSTR sid = nullptr;
		if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(userData.data())->User.Sid, &sid)) return false;
		const std::wstring sddl = L"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;" + std::wstring(sid) + L")";
		LocalFree(sid);
		PSECURITY_DESCRIPTOR descriptor = nullptr;
		if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &descriptor, nullptr))
			return false;
		SECURITY_ATTRIBUTES security{ sizeof(SECURITY_ATTRIBUTES), descriptor, FALSE };
		std::array<unsigned char, 16> random{};
		bool created = false;
		if (BCryptGenRandom(nullptr, random.data(), static_cast<ULONG>(random.size()), BCRYPT_USE_SYSTEM_PREFERRED_RNG) >= 0)
		{
			constexpr wchar_t hex[] = L"0123456789abcdef";
			std::wstring name = L"\\TurboRama.Suite.Access.";
			for (const unsigned char byte : random)
			{
				name += hex[byte >> 4];
				name += hex[byte & 15];
			}
			const std::wstring candidate = localAppData + name;
			// Never adopt an existing directory, even when its name happens to match.
			created = CreateDirectoryW(candidate.c_str(), &security) != FALSE;
			if (created) path = candidate;
		}
		LocalFree(descriptor);
		if (!created) return false;
		lock.reset(CreateFileW(path.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE,
			nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
		if (!lock.valid()) return false;
		BY_HANDLE_FILE_INFORMATION info{};
		return GetFileInformationByHandle(lock.get(), &info) &&
			(info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0;
	}

	bool buildChildEnvironment(std::vector<wchar_t>& block, std::wstring& extractionPath, Handle& extractionLock)
	{
		std::wstring profile, local, roaming, common;
		if (!knownFolder(FOLDERID_Profile, profile) || !knownFolder(FOLDERID_LocalAppData, local) ||
			!knownFolder(FOLDERID_RoamingAppData, roaming) || !knownFolder(FOLDERID_ProgramData, common)) return false;
		std::array<wchar_t, 32768> windows{}, system{}, processor{};
		const UINT windowsLength = GetWindowsDirectoryW(windows.data(), static_cast<UINT>(windows.size()));
		const UINT systemLength = GetSystemDirectoryW(system.data(), static_cast<UINT>(system.size()));
		const DWORD processorLength = GetEnvironmentVariableW(L"PROCESSOR_IDENTIFIER", processor.data(),
			static_cast<DWORD>(processor.size()));
		if (windowsLength == 0 || windowsLength >= windows.size() || systemLength == 0 ||
			systemLength >= system.size() || processorLength >= processor.size() ||
			!createRuntimeDirectory(local, extractionPath, extractionLock)) return false;

		// Build from an allowlist instead of inheriting CLR startup hooks,
		// profilers, additional dependencies or a caller-selected extraction cache.
		std::vector<std::wstring> entries{
			L"APPDATA=" + roaming,
			L"COMSPEC=" + std::wstring(system.data()) + L"\\cmd.exe",
			L"DOTNET_BUNDLE_EXTRACT_BASE_DIR=" + extractionPath,
			L"DOTNET_EnableDiagnostics=0",
			L"LOCALAPPDATA=" + local,
			L"PATH=" + std::wstring(system.data()) + L";" + windows.data(),
			L"PROCESSOR_IDENTIFIER=" + std::wstring(processor.data(), processorLength),
			L"ProgramData=" + common,
			L"SystemRoot=" + std::wstring(windows.data()),
			L"TEMP=" + extractionPath,
			L"TMP=" + extractionPath,
			L"USERPROFILE=" + profile,
			L"windir=" + std::wstring(windows.data())
		};
		std::sort(entries.begin(), entries.end(), [](const std::wstring& a, const std::wstring& b) {
			return _wcsicmp(a.c_str(), b.c_str()) < 0;
		});
		for (const auto& entry : entries)
		{
			block.insert(block.end(), entry.begin(), entry.end());
			block.push_back(L'\0');
		}
		block.push_back(L'\0');
		return true;
	}
}

struct SuiteAccessGate::State
{
	Handle helperFile, helperDirectory, extractionDirectory, process, job, input, output, stopEvent;
	std::wstring extractionPath;
	std::atomic<bool> revoked{ true };
	std::atomic<long long> lastProof{ 0 };
	std::thread monitor;
	bool started = false;

	bool readReply(const char* expected, DWORD timeout, long long& receivedAt)
	{
		std::array<char, 16> reply{};
		size_t size = 0;
		const size_t required = std::strlen(expected);
		const auto deadline = nowMs() + timeout;
		while (nowMs() <= deadline)
		{
			if (WaitForSingleObject(stopEvent.get(), 0) != WAIT_TIMEOUT ||
				WaitForSingleObject(process.get(), 0) != WAIT_TIMEOUT) return false;
			DWORD available = 0;
			if (!PeekNamedPipe(output.get(), nullptr, 0, nullptr, &available, nullptr)) return false;
			if (available > 0)
			{
				// No queued or unsolicited OKs: each check consumes exactly one reply.
				if (size + available > required || required > reply.size()) return false;
				DWORD read = 0;
				if (!ReadFile(output.get(), reply.data() + size, available, &read, nullptr) || read == 0)
					return false;
				size += read;
				if (size == required)
				{
					receivedAt = nowMs();
					return receivedAt <= deadline && exactReply(reply.data(), size, expected);
				}
			}
			if (WaitForSingleObject(stopEvent.get(), 10) != WAIT_TIMEOUT) return false;
		}
		return false;
	}

	void monitorSession()
	{
		while (!revoked.load() && fresh(nowMs(), lastProof.load()))
		{
			DWORD available = 0, written = 0;
			long long receivedAt = 0;
			if (!PeekNamedPipe(output.get(), nullptr, 0, nullptr, &available, nullptr) || available != 0 ||
				!WriteFile(input.get(), "CHECK\n", 6, &written, nullptr) || written != 6 ||
				!readReply("OK\n", ReplyTimeoutMs, receivedAt) || revoked.load() ||
				!fresh(receivedAt, lastProof.load())) break;
			// Retain the receipt instant, not a later scheduled store instant.
			lastProof.store(receivedAt);
			if (WaitForSingleObject(stopEvent.get(), PollIntervalMs) != WAIT_TIMEOUT) break;
		}
		revoked.store(true);
	}

	void close()
	{
		revoked.store(true);
		if (stopEvent.valid()) SetEvent(stopEvent.get());
		if (monitor.joinable()) monitor.join();
		// Only the helper belongs to this job. Emulator process trees are untouched.
		input.reset();
		job.reset();
		if (process.valid()) WaitForSingleObject(process.get(), 3000);
		process.reset();
		output.reset();
		helperFile.reset();
		helperDirectory.reset();
		extractionDirectory.reset();
		if (!extractionPath.empty()) removePrivateRuntime(extractionPath);
		extractionPath.clear();
		stopEvent.reset();
	}
};

SuiteAccessGate::SuiteAccessGate() : mState(new State) {}
SuiteAccessGate::~SuiteAccessGate() { stop(); }
SuiteAccessGate& SuiteAccessGate::instance()
{
	static SuiteAccessGate gate;
	return gate;
}

bool SuiteAccessGate::start(std::string& error)
{
	error = "Nao foi possivel validar a ativacao do TurboRama Suite. Abra o Suite nesta conta do Windows e tente novamente.";
	if (mState->started) return false;
	mState->started = true;
	std::wstring path, directory;
	if (!helperPath(path, directory)) return false;
	mState->helperDirectory.reset(CreateFileW(directory.c_str(), FILE_READ_ATTRIBUTES,
		FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, nullptr));
	if (!mState->helperDirectory.valid() || !lockAndVerifyHelper(path, mState->helperFile))
	{
		error = "O componente de acesso do TurboRama Suite esta ausente ou foi alterado. Reinstale o pacote completo desta versao.";
		mState->close();
		return false;
	}

	Handle childInput, childOutput, childError, childThread;
	auto fail = [&]() { mState->close(); return false; };
	std::vector<wchar_t> childEnvironment;
	if (!buildChildEnvironment(childEnvironment, mState->extractionPath, mState->extractionDirectory))
		return fail();
	if (!makePipe(childInput, mState->input, true) || !makePipe(mState->output, childOutput, false))
		return fail();
	SECURITY_ATTRIBUTES security{ sizeof(SECURITY_ATTRIBUTES), nullptr, TRUE };
	childError.reset(CreateFileW(L"NUL", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
		&security, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
	mState->stopEvent.reset(CreateEventW(nullptr, TRUE, FALSE, nullptr));
	mState->job.reset(CreateJobObjectW(nullptr, nullptr));
	if (!childError.valid() || !mState->stopEvent.valid() || !mState->job.valid()) return fail();
	JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
	limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
	if (!SetInformationJobObject(mState->job.get(), JobObjectExtendedLimitInformation, &limits, sizeof(limits)))
		return fail();

	SIZE_T attributeSize = 0;
	InitializeProcThreadAttributeList(nullptr, 1, 0, &attributeSize);
	if (attributeSize == 0) return fail();
	std::vector<unsigned char> attributes(attributeSize);
	STARTUPINFOEXW startup{};
	startup.StartupInfo.cb = sizeof(startup);
	startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
	startup.StartupInfo.hStdInput = childInput.get();
	startup.StartupInfo.hStdOutput = childOutput.get();
	startup.StartupInfo.hStdError = childError.get();
	startup.lpAttributeList = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(attributes.data());
	if (!InitializeProcThreadAttributeList(startup.lpAttributeList, 1, 0, &attributeSize)) return fail();
	HANDLE inherited[] = { childInput.get(), childOutput.get(), childError.get() };
	const bool attributesReady = UpdateProcThreadAttribute(startup.lpAttributeList, 0,
		PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited, sizeof(inherited), nullptr, nullptr) != FALSE;
	std::wstring command = L"\"" + path + L"\" --bridge";
	PROCESS_INFORMATION child{};
	const bool created = attributesReady && CreateProcessW(path.c_str(), command.data(), nullptr, nullptr,
		TRUE, CREATE_NO_WINDOW | CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | EXTENDED_STARTUPINFO_PRESENT,
		childEnvironment.data(), directory.c_str(), &startup.StartupInfo, &child) != FALSE;
	DeleteProcThreadAttributeList(startup.lpAttributeList);
	if (!created) return fail();
	mState->process.reset(child.hProcess);
	childThread.reset(child.hThread);
	if (!AssignProcessToJobObject(mState->job.get(), child.hProcess))
	{
		// A suspended process not assigned to our job must also be reclaimed.
		TerminateProcess(child.hProcess, 44);
		return fail();
	}
	std::vector<wchar_t> imagePath(32768);
	DWORD imageLength = static_cast<DWORD>(imagePath.size());
	if (!QueryFullProcessImageNameW(child.hProcess, 0, imagePath.data(), &imageLength)) return fail();
	Handle loadedImage(CreateFileW(imagePath.data(), GENERIC_READ, FILE_SHARE_READ, nullptr,
		OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
	if (!loadedImage.valid() || !sameFile(mState->helperFile.get(), loadedImage.get())) return fail();
	if (ResumeThread(childThread.get()) == static_cast<DWORD>(-1)) return fail();
	childInput.reset();
	childOutput.reset();
	childError.reset();
	childThread.reset();
	long long receivedAt = 0;
	if (!mState->readReply("READY\n", LoginTimeoutMs, receivedAt) || !fresh(nowMs(), receivedAt)) return fail();
	mState->lastProof.store(receivedAt);
	mState->revoked.store(false);
	try { mState->monitor = std::thread([this]() { mState->monitorSession(); }); }
	catch (...) { return fail(); }
	error.clear();
	return true;
}

bool SuiteAccessGate::authorized() const
{
	if (mState->revoked.load()) return false;
	if (!fresh(nowMs(), mState->lastProof.load()) ||
		!mState->process.valid() || WaitForSingleObject(mState->process.get(), 0) != WAIT_TIMEOUT)
	{
		// Expiration is sticky even if a delayed monitor thread wakes up later.
		mState->revoked.store(true);
		return false;
	}
	return true;
}

void SuiteAccessGate::stop() { mState->close(); }

bool SuiteAccessGate::verifyHelperIntegrity()
{
	std::wstring path, directory;
	Handle file;
	return helperPath(path, directory) && lockAndVerifyHelper(path, file);
}

bool SuiteAccessGate::runSelfTest()
{
	const unsigned char input[] = { 'a', 'b', 'c' };
	std::array<unsigned char, 32> digest{};
	Sha256 hash;
	if (!hash.append(input, sizeof(input)) || !hash.finish(digest.data()) ||
		!digestMatches(digest.data(), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")) return false;
	if (digestMatches(digest.data(), "aa7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad") ||
		digestMatches(digest.data(), "xyz")) return false;
	return exactReply("OK\n", 3, "OK\n") && !exactReply("OK\nOK\n", 6, "OK\n") &&
		!exactReply("DENIED\n", 7, "OK\n") && !exactReply("OK\r\n", 4, "OK\n") &&
		!exactReply("OK", 2, "OK\n") && fresh(5000, 1000) && !fresh(5001, 1000) &&
		!fresh(999, 1000) && !fresh(1000, 0) && !instance().authorized();
}
