#include "CreditManager.h"

#include "Log.h"
#include "Paths.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"
#include "utils/md5.h"

#include <algorithm>
#include <chrono>
#include <climits>
#include <cstddef>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <fstream>
#include <iomanip>
#include <locale>
#include <random>
#include <sstream>
#include <unordered_set>
#include <utility>
#include <vector>

#ifdef _WIN32
#include <io.h>
#include <windows.h>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")
#else
#include <cerrno>
#include <fcntl.h>
#include <sys/file.h>
#include <sys/stat.h>
#include <unistd.h>
#endif

namespace
{
	long long nowMs()
	{
		using namespace std::chrono;
		return duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count();
	}

	// CRITICAL: never use global locale for numbers (pt-BR writes "28,800" / "28.800")
	std::ostringstream makePlainOut()
	{
		std::ostringstream out;
		out.imbue(std::locale::classic());
		return out;
	}

	enum class RegularFileState
	{
		Missing,
		Regular,
		UnsafeOrError
	};

	static const size_t kMaxConfigFileBytes = 64u * 1024u;
	static const size_t kMaxFinancialFileBytes = 8u * 1024u * 1024u;
	static const size_t kMaxConfigLines = 512u;
	static const size_t kMaxFinancialLines = 350000u;
	static const size_t kMaxTextLineBytes = 4096u;

	RegularFileState inspectRegularFile(const std::string& path)
	{
#if defined(_WIN32)
		const std::wstring widePath = Utils::String::convertToWideString(path);
		const DWORD attributes = GetFileAttributesW(widePath.c_str());
		if (attributes == INVALID_FILE_ATTRIBUTES)
		{
			const DWORD error = GetLastError();
			return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND
				? RegularFileState::Missing : RegularFileState::UnsafeOrError;
		}
		if ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0
			|| (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
			return RegularFileState::UnsafeOrError;
		return RegularFileState::Regular;
#else
		struct stat info;
		if (lstat(path.c_str(), &info) == 0)
			return S_ISREG(info.st_mode)
				? RegularFileState::Regular : RegularFileState::UnsafeOrError;
		return errno == ENOENT || errno == ENOTDIR
			? RegularFileState::Missing : RegularFileState::UnsafeOrError;
#endif
	}

	bool readBoundedTextLines(const std::string& path, size_t maxBytes,
		size_t maxLines, size_t maxLineBytes, std::vector<std::string>& lines)
	{
		lines.clear();
		std::ifstream in(path, std::ios::in | std::ios::binary);
		if (!in.is_open())
			return false;

		in.seekg(0, std::ios::end);
		const std::streamoff declaredSize = in.tellg();
		if (declaredSize < 0 || (unsigned long long)declaredSize > maxBytes)
			return false;
		in.seekg(0, std::ios::beg);
		if (!in.good())
			return false;

		size_t bytesRead = 0;
		std::string line;
		line.reserve(std::min<size_t>(maxLineBytes, 256u));
		char ch = 0;
		while (in.get(ch))
		{
			// The size check above avoids reading an already oversized file. These
			// per-byte checks also close a race where it grows after seek/tell.
			if (bytesRead >= maxBytes)
				return false;
			++bytesRead;
			if (ch == '\n')
			{
				if (lines.size() >= maxLines)
					return false;
				lines.push_back(line);
				line.clear();
			}
			else
			{
				if (line.size() >= maxLineBytes)
					return false;
				line.push_back(ch);
			}
		}
		if (in.bad())
			return false;
		if (!line.empty())
		{
			if (lines.size() >= maxLines)
				return false;
			lines.push_back(line);
		}
		return true;
	}

	std::string hexEncode(const std::vector<unsigned char>& bytes)
	{
		std::ostringstream out;
		out.imbue(std::locale::classic());
		out << std::hex << std::setfill('0');
		for (const unsigned char byte : bytes)
			out << std::setw(2) << (unsigned int)byte;
		return out.str();
	}

	bool hexDecode(const std::string& text, std::vector<unsigned char>& bytes)
	{
		bytes.clear();
		if (text.empty() || (text.size() % 2) != 0)
			return false;
		bytes.reserve(text.size() / 2);
		for (size_t i = 0; i < text.size(); i += 2)
		{
			auto nibble = [](char ch) -> int {
				if (ch >= '0' && ch <= '9') return ch - '0';
				if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
				if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
				return -1;
			};
			const int high = nibble(text[i]);
			const int low = nibble(text[i + 1]);
			if (high < 0 || low < 0)
			{
				bytes.clear();
				return false;
			}
			bytes.push_back((unsigned char)((high << 4) | low));
		}
		return true;
	}

	bool secureRandomBytes(std::vector<unsigned char>& bytes)
	{
		if (bytes.empty())
			return false;
#ifdef _WIN32
		return BCryptGenRandom(nullptr, bytes.data(), (ULONG)bytes.size(),
			BCRYPT_USE_SYSTEM_PREFERRED_RNG) >= 0;
#else
		try
		{
			std::random_device random;
			for (auto& byte : bytes)
				byte = (unsigned char)random();
			return true;
		}
		catch (...)
		{
			return false;
		}
#endif
	}

	bool derivePbkdf2Sha256(const std::string& password,
		const std::vector<unsigned char>& salt, unsigned long long iterations,
		std::vector<unsigned char>& digest)
	{
#ifdef _WIN32
		BCRYPT_ALG_HANDLE algorithm = nullptr;
		if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM,
			nullptr, BCRYPT_ALG_HANDLE_HMAC_FLAG) < 0)
			return false;
		const NTSTATUS status = BCryptDeriveKeyPBKDF2(algorithm,
			(PUCHAR)password.data(), (ULONG)password.size(),
			(PUCHAR)salt.data(), (ULONG)salt.size(), iterations,
			digest.data(), (ULONG)digest.size(), 0);
		BCryptCloseAlgorithmProvider(algorithm, 0);
		return status >= 0;
#else
		(void)password;
		(void)salt;
		(void)iterations;
		(void)digest;
		return false;
#endif
	}
}

CreditManager& CreditManager::getInstance()
{
	static CreditManager instance;
	return instance;
}

CreditManager::CreditManager()
	: mProcessLockHandle(nullptr)
	, mProcessLockFd(-1)
	, mEnabled(true)
	, mBlockWithoutCredit(true)
	, mShowHud(true)
	, mMinutesPerCoin(30)
	, mDebounceMs(350)
	, mMaxRemainingSeconds(28800)
	, mRemainingSeconds(0)
	, mTotalCoinsAccepted(0)
	, mTotalMinutesSold(0)
	, mTotalSecondsPlayed(0)
	, mPriceCentsPerMinute(0)
	, mCreditPersistenceBlocked(false)
	, mLastCoinTickMs(-1)
	, mSessionRunning(false)
	, mSessionPaused(false)
	, mInGame(false)
	, mGameWasCounting(false)
	, mGameAccountedSeconds(0)
	, mTickAccumMs(0)
	, mSaveAccumMs(0)
	, mGuestRemainingSeconds(0)
	, mAdminPasswordHash(defaultAdminPasswordHash())
	, mLowTimeWarnStage(0)
{
	if (!acquireProcessLock())
	{
		blockCreditOperationsAndPersistenceUnlocked(
			"outra instancia financeira ativa ou lock inseguro");
		return;
	}
	load();
}

CreditManager::~CreditManager()
{
	releaseProcessLock();
}

std::string CreditManager::creditFilePath() const
{
	return Utils::FileSystem::combine(Paths::getUserEmulationStationPath(), "arcade_credit.dat");
}

std::string CreditManager::configFilePath() const
{
	return Utils::FileSystem::combine(Paths::getUserEmulationStationPath(), "arcade_credit.cfg");
}

std::string CreditManager::playersFilePath() const
{
	return Utils::FileSystem::combine(Paths::getUserEmulationStationPath(), "arcade_players.dat");
}

std::string CreditManager::processLockFilePath() const
{
	return Utils::FileSystem::combine(Paths::getUserEmulationStationPath(), "arcade_credit.lock");
}

bool CreditManager::acquireProcessLock()
{
	const std::string path = processLockFilePath();
	const std::string dir = Utils::FileSystem::getParent(path);
	if (!dir.empty() && !Utils::FileSystem::createDirectory(dir))
		return false;
#if defined(_WIN32)
	const std::wstring widePath = Utils::String::convertToWideString(path);
	HANDLE handle = CreateFileW(widePath.c_str(), GENERIC_READ | GENERIC_WRITE, 0,
		nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
	if (handle == INVALID_HANDLE_VALUE)
		return false;
	BY_HANDLE_FILE_INFORMATION info;
	if (!GetFileInformationByHandle(handle, &info)
		|| (info.dwFileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0)
	{
		CloseHandle(handle);
		return false;
	}
	mProcessLockHandle = handle;
	return true;
#else
	int flags = O_RDWR | O_CREAT;
#ifdef O_NOFOLLOW
	flags |= O_NOFOLLOW;
#endif
#ifdef O_CLOEXEC
	flags |= O_CLOEXEC;
#endif
	const int fd = ::open(path.c_str(), flags, 0600);
	if (fd < 0)
		return false;
	const int descriptorFlags = ::fcntl(fd, F_GETFD);
	if (descriptorFlags < 0 || ::fcntl(fd, F_SETFD, descriptorFlags | FD_CLOEXEC) != 0)
	{
		::close(fd);
		return false;
	}
	struct stat info;
	if (::fstat(fd, &info) != 0 || !S_ISREG(info.st_mode)
		|| ::flock(fd, LOCK_EX | LOCK_NB) != 0)
	{
		::close(fd);
		return false;
	}
	mProcessLockFd = fd;
	return true;
#endif
}

void CreditManager::releaseProcessLock()
{
#if defined(_WIN32)
	if (mProcessLockHandle != nullptr)
	{
		CloseHandle((HANDLE)mProcessLockHandle);
		mProcessLockHandle = nullptr;
	}
#else
	if (mProcessLockFd >= 0)
	{
		::flock(mProcessLockFd, LOCK_UN);
		::close(mProcessLockFd);
		mProcessLockFd = -1;
	}
#endif
}

std::string CreditManager::legacyPasswordHash(const std::string& password)
{
	return MD5(password).hexdigest();
}

std::string CreditManager::defaultAdminPasswordHash()
{
	return createPasswordHash("admin");
}

bool CreditManager::isLegacyPasswordHash(const std::string& encodedHash)
{
	if (encodedHash.size() != 32)
		return false;
	for (const char ch : encodedHash)
		if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F')))
			return false;
	return true;
}

bool CreditManager::isSupportedPasswordHash(const std::string& encodedHash)
{
	if (isLegacyPasswordHash(encodedHash))
		return true;
	const std::string prefix = "pbkdf2-sha256$";
	if (encodedHash.rfind(prefix, 0) != 0)
		return false;
	const size_t iterationEnd = encodedHash.find('$', prefix.size());
	const size_t saltEnd = iterationEnd == std::string::npos
		? std::string::npos : encodedHash.find('$', iterationEnd + 1);
	if (iterationEnd == std::string::npos || saltEnd == std::string::npos
		|| encodedHash.find('$', saltEnd + 1) != std::string::npos)
		return false;
	const std::string iterationText = encodedHash.substr(prefix.size(), iterationEnd - prefix.size());
	if (iterationText.empty() || iterationText.size() > 9)
		return false;
	for (const char ch : iterationText)
		if (ch < '0' || ch > '9')
			return false;
	const long iterations = std::strtol(iterationText.c_str(), nullptr, 10);
	std::vector<unsigned char> salt;
	std::vector<unsigned char> digest;
	return iterations >= 100000 && iterations <= 2000000
		&& hexDecode(encodedHash.substr(iterationEnd + 1, saltEnd - iterationEnd - 1), salt)
		&& salt.size() >= 16 && salt.size() <= 64
		&& hexDecode(encodedHash.substr(saltEnd + 1), digest)
		&& digest.size() == 32;
}

std::string CreditManager::createPasswordHash(const std::string& password)
{
	static const unsigned long long iterations = 210000;
	std::vector<unsigned char> salt(16);
	std::vector<unsigned char> digest(32);
	if (secureRandomBytes(salt) && derivePbkdf2Sha256(password, salt, iterations, digest))
		return std::string("pbkdf2-sha256$") + std::to_string(iterations)
			+ "$" + hexEncode(salt) + "$" + hexEncode(digest);

	// Compatibilidade de emergencia para plataformas sem BCrypt. No Windows
	// comercial esse caminho nao deve ocorrer e e registrado pelo chamador.
	return legacyPasswordHash(password);
}

bool CreditManager::verifyPasswordHash(const std::string& password, const std::string& encodedHash)
{
	if (isLegacyPasswordHash(encodedHash))
		return constantTimeEqual(legacyPasswordHash(password), Utils::String::toLower(encodedHash));
	if (!isSupportedPasswordHash(encodedHash))
		return false;
	const std::string prefix = "pbkdf2-sha256$";
	const size_t iterationEnd = encodedHash.find('$', prefix.size());
	const size_t saltEnd = encodedHash.find('$', iterationEnd + 1);
	const unsigned long long iterations = std::strtoull(
		encodedHash.substr(prefix.size(), iterationEnd - prefix.size()).c_str(), nullptr, 10);
	std::vector<unsigned char> salt;
	std::vector<unsigned char> expected;
	std::vector<unsigned char> actual(32);
	if (!hexDecode(encodedHash.substr(iterationEnd + 1, saltEnd - iterationEnd - 1), salt)
		|| !hexDecode(encodedHash.substr(saltEnd + 1), expected)
		|| !derivePbkdf2Sha256(password, salt, iterations, actual))
		return false;
	return constantTimeEqual(hexEncode(actual), hexEncode(expected));
}

bool CreditManager::constantTimeEqual(const std::string& a, const std::string& b)
{
	const size_t na = a.size();
	const size_t nb = b.size();
	const size_t n = (na > nb) ? na : nb;
	unsigned char diff = (unsigned char)(na ^ nb);
	for (size_t i = 0; i < n; ++i)
	{
		const unsigned char ca = (i < na) ? (unsigned char)a[i] : 0;
		const unsigned char cb = (i < nb) ? (unsigned char)b[i] : 0;
		diff = (unsigned char)(diff | (ca ^ cb));
	}
	return diff == 0;
}

bool CreditManager::parseLegacyNonNegativeLong(const std::string& val, long& parsed)
{
	parsed = 0;
	bool sawDigit = false;
	for (const char ch : val)
	{
		if (ch >= '0' && ch <= '9')
		{
			sawDigit = true;
			const int digit = ch - '0';
			if (parsed > (LONG_MAX - digit) / 10L)
				return false;
			parsed = parsed * 10L + digit;
		}
		else if (ch != '.' && ch != ',' && ch != ' ' && ch != '\t')
			return false;
	}
	return sawDigit;
}

bool CreditManager::parseStrictNonNegativeLong(const std::string& val, long& parsed)
{
	parsed = 0;
	if (val.empty()) return false;
	for (const char ch : val)
	{
		if (ch < '0' || ch > '9') return false;
		const int digit = ch - '0';
		if (parsed > (LONG_MAX - digit) / 10L) return false;
		parsed = parsed * 10L + digit;
	}
	return true;
}

std::string CreditManager::sanitizePlayerName(const std::string& name)
{
	std::string n = Utils::String::trim(name);
	std::string out;
	for (unsigned char c : n)
	{
		if (c < 32 || c == 127)
			continue;
		if (c == ';' || c == '=' || c == '\n' || c == '\r' || c == '#' || c == '%' || c == '"' || c == '\\')
			continue;
		out.push_back((char)c);
		if (out.size() >= 24)
			break;
	}
	return Utils::String::trim(out);
}

bool CreditManager::isValidWalletId(const std::string& walletId)
{
	if (walletId.size() < 16 || walletId.size() > 128)
		return false;
	for (const char ch : walletId)
	{
		const bool allowed = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')
			|| (ch >= '0' && ch <= '9') || ch == '-' || ch == '_';
		if (!allowed)
			return false;
	}
	return true;
}

std::string CreditManager::generateWalletId()
{
	std::vector<unsigned char> bytes(16);
	if (!secureRandomBytes(bytes))
	{
		const std::string fallback = std::to_string((long long)std::chrono::high_resolution_clock::now()
			.time_since_epoch().count()) + std::to_string((unsigned long long)nowMs());
		return std::string("wallet-") + legacyPasswordHash(fallback);
	}
	return std::string("wallet-") + hexEncode(bytes);
}

CreditPlayer* CreditManager::findPlayerByIdUnlocked(const std::string& walletId)
{
	for (auto& player : mPlayers)
		if (player.id == walletId)
			return &player;
	return nullptr;
}

const CreditPlayer* CreditManager::findPlayerByIdUnlocked(const std::string& walletId) const
{
	for (const auto& player : mPlayers)
		if (player.id == walletId)
			return &player;
	return nullptr;
}

size_t CreditManager::activePlayerCountUnlocked() const
{
	size_t count = 0;
	for (const auto& player : mPlayers)
		if (!player.archived) ++count;
	return count;
}

bool CreditManager::guestRotationNeedsActiveSlotUnlocked(bool preserveBalanceAsRecovery) const
{
	if (!preserveBalanceAsRecovery || mGuestRemainingSeconds <= 0
		|| !isValidWalletId(mGuestWalletId))
		return false;
	const CreditPlayer* recovered = findPlayerByIdUnlocked(mGuestWalletId);
	return recovered == nullptr || recovered->archived;
}

bool CreditManager::canRotateGuestWalletUnlocked(bool preserveBalanceAsRecovery) const
{
	const std::string retiredId = mGuestWalletId;
	const long retiredBalance = std::max(0L, mGuestRemainingSeconds);
	const bool paidRecovery = preserveBalanceAsRecovery && retiredBalance > 0
		&& isValidWalletId(retiredId);
	const bool aliasExists = std::find(mRetiredGuestAliases.begin(),
		mRetiredGuestAliases.end(), retiredId) != mRetiredGuestAliases.end();
	bool retiredExists = false;
	for (const auto& retired : mRetiredGuestWallets)
		if (retired.id == retiredId) retiredExists = true;
	if (guestRotationNeedsActiveSlotUnlocked(preserveBalanceAsRecovery)
		&& activePlayerCountUnlocked() >= (size_t)kMaxPlayers)
		return false;
	if (paidRecovery && !aliasExists
		&& mRetiredGuestAliases.size() >= kMaxWalletTombstones)
		return false;
	if (!paidRecovery && isValidWalletId(retiredId) && !retiredExists
		&& mRetiredGuestWallets.size() >= kMaxWalletTombstones)
		return false;
	return true;
}

bool CreditManager::rotateGuestWalletUnlocked(bool preserveBalanceAsRecovery)
{
	if (!canRotateGuestWalletUnlocked(preserveBalanceAsRecovery))
	{
		LOG(LogError) << "[CreditManager] rotacao guest recusada por limite de recovery";
		return false;
	}
	const std::string retiredId = mGuestWalletId;
	const long retiredBalance = std::max(0L, mGuestRemainingSeconds);
	const bool paidRecovery = preserveBalanceAsRecovery && retiredBalance > 0
		&& isValidWalletId(retiredId);
	bool promotedToVisiblePlayer = false;

	// If a paid balance reached the guest wallet while another player was active,
	// never hide it in a tombstone.  Give it a visible, recoverable account while
	// preserving the opaque wallet id used by the payment provider.
	if (paidRecovery)
	{
		CreditPlayer* recovered = findPlayerByIdUnlocked(retiredId);
		if (recovered == nullptr)
		{
			CreditPlayer player;
			player.id = retiredId;
			player.name = recoveredGuestPlayerNameUnlocked(retiredId);
			player.remainingSeconds = retiredBalance;
			mPlayers.push_back(player);
		}
		else
		{
			recovered->remainingSeconds = std::max(recovered->remainingSeconds, retiredBalance);
			recovered->archived = false;
			recovered->tombstonedAtUnixSeconds = 0;
		}
		if (std::find(mRetiredGuestAliases.begin(), mRetiredGuestAliases.end(), retiredId)
			== mRetiredGuestAliases.end())
			mRetiredGuestAliases.push_back(retiredId);
		mRetiredGuestWallets.erase(std::remove_if(mRetiredGuestWallets.begin(),
			mRetiredGuestWallets.end(), [&](const RetiredGuestWallet& retired) {
				return retired.id == retiredId;
			}), mRetiredGuestWallets.end());
		promotedToVisiblePlayer = true;
	}

	if (!promotedToVisiblePlayer && isValidWalletId(retiredId))
	{
		bool found = false;
		for (auto& retired : mRetiredGuestWallets)
		{
			if (retired.id == retiredId)
			{
				retired.remainingSeconds = preserveBalanceAsRecovery
					? std::max(retired.remainingSeconds, retiredBalance) : 0;
				retired.retiredAtUnixSeconds = (long)std::time(nullptr);
				found = true;
				break;
			}
		}
		if (!found)
			mRetiredGuestWallets.push_back({ retiredId,
				preserveBalanceAsRecovery ? retiredBalance : 0, (long)std::time(nullptr) });
	}

	bool collision = false;
	do
	{
		mGuestWalletId = generateWalletId();
		collision = findPlayerByIdUnlocked(mGuestWalletId) != nullptr;
		for (const auto& retired : mRetiredGuestWallets)
			if (retired.id == mGuestWalletId) collision = true;
		for (const auto& alias : mRetiredGuestAliases)
			if (alias == mGuestWalletId) collision = true;
	}
	while (collision);
	mGuestRemainingSeconds = 0;
	if (mCurrentPlayer.empty()) mRemainingSeconds = 0;
	return true;
}

std::string CreditManager::recoveredGuestPlayerNameUnlocked(const std::string& walletId) const
{
	const std::string suffix = walletId.size() > 8 ? walletId.substr(walletId.size() - 8) : walletId;
	const std::string base = sanitizePlayerName(std::string("PIX AVULSO ") + suffix);
	std::string candidate = base;
	for (int index = 2; index < 1000; ++index)
	{
		bool exists = false;
		for (const auto& player : mPlayers)
			if (Utils::String::toLower(player.name) == Utils::String::toLower(candidate)) exists = true;
		if (!exists) return candidate;
		candidate = sanitizePlayerName(base + " " + std::to_string(index));
	}
	return sanitizePlayerName(std::string("PIX ") + suffix);
}

bool CreditManager::promoteRetiredGuestUnlocked(const std::string& walletId)
{
	for (const auto& alias : mRetiredGuestAliases)
		if (alias == walletId) return findPlayerByIdUnlocked(walletId) != nullptr;
	for (auto it = mRetiredGuestWallets.begin(); it != mRetiredGuestWallets.end(); ++it)
	{
		if (it->id != walletId) continue;
		if (activePlayerCountUnlocked() >= (size_t)kMaxPlayers)
			return false;
		if (mRetiredGuestAliases.size() >= kMaxWalletTombstones)
			return false;
		CreditPlayer recovered;
		recovered.id = it->id;
		recovered.name = recoveredGuestPlayerNameUnlocked(it->id);
		recovered.remainingSeconds = it->remainingSeconds;
		mPlayers.push_back(recovered);
		mRetiredGuestAliases.push_back(it->id);
		mRetiredGuestWallets.erase(it);
		return true;
	}
	return false;
}

std::string CreditManager::formatTimeUnlocked(long totalSec)
{
	if (totalSec < 0)
		totalSec = 0;
	const long h = totalSec / 3600;
	long s = totalSec % 3600;
	const long m = s / 60;
	s %= 60;
	char buf[32];
	if (h > 0)
		snprintf(buf, sizeof(buf), "%ld:%02ld:%02ld", h, m, s);
	else
		snprintf(buf, sizeof(buf), "%02ld:%02ld", m, s);
	return std::string(buf);
}

bool CreditManager::atomicWriteText(const std::string& path, const std::string& content)
{
	if (inspectRegularFile(path) == RegularFileState::UnsafeOrError)
	{
		LOG(LogError) << "[CreditManager] destino inseguro ou inacessivel: " << path;
		return false;
	}
	const std::string dir = Utils::FileSystem::getParent(path);
	if (!dir.empty())
		Utils::FileSystem::createDirectory(dir);
#ifdef CREDIT_MANAGER_TEST_HOOKS
	const char* forcedFailurePath = std::getenv("TURBORAMA_CREDIT_TEST_FAIL_ATOMIC_PATH");
	if (forcedFailurePath != nullptr && *forcedFailurePath != '\0'
		&& path.find(forcedFailurePath) != std::string::npos)
		return false;
#endif

	std::string tmp;
#if defined(_WIN32)
	HANDLE tempHandle = INVALID_HANDLE_VALUE;
	std::wstring wideTmp;
	for (int attempt = 0; attempt < 32; ++attempt)
	{
		std::vector<unsigned char> randomBytes(16);
		if (!secureRandomBytes(randomBytes)) break;
		tmp = path + ".tmp-" + hexEncode(randomBytes);
		wideTmp = Utils::String::convertToWideString(tmp);
		tempHandle = CreateFileW(wideTmp.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
			FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_WRITE_THROUGH | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
		if (tempHandle != INVALID_HANDLE_VALUE) break;
		const DWORD error = GetLastError();
		if (error != ERROR_FILE_EXISTS && error != ERROR_ALREADY_EXISTS)
			break;
	}
	if (tempHandle == INVALID_HANDLE_VALUE)
	{
		LOG(LogError) << "[CreditManager] exclusive temp create failed: " << path;
		return false;
	}

	bool stored = true;
	size_t offset = 0;
	while (stored && offset < content.size())
	{
		const DWORD chunk = (DWORD)std::min<size_t>(content.size() - offset, 0x7ffff000u);
		DWORD written = 0;
		stored = WriteFile(tempHandle, content.data() + offset, chunk, &written, nullptr) != FALSE
			&& written == chunk;
		offset += written;
	}
	stored = stored && FlushFileBuffers(tempHandle) != FALSE;
	stored = CloseHandle(tempHandle) != FALSE && stored;
	if (!stored)
	{
		DeleteFileW(wideTmp.c_str());
		LOG(LogError) << "[CreditManager] exclusive temp write/flush failed: " << path;
		return false;
	}

	const std::wstring widePath = Utils::String::convertToWideString(path);
	if (MoveFileExW(wideTmp.c_str(), widePath.c_str(),
		MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
		return true;
	DeleteFileW(wideTmp.c_str());
#else
	int tempFd = -1;
	for (int attempt = 0; attempt < 32; ++attempt)
	{
		std::vector<unsigned char> randomBytes(16);
		if (!secureRandomBytes(randomBytes)) break;
		tmp = path + ".tmp-" + hexEncode(randomBytes);
		int flags = O_WRONLY | O_CREAT | O_EXCL;
#ifdef O_NOFOLLOW
		flags |= O_NOFOLLOW;
#endif
		tempFd = ::open(tmp.c_str(), flags, 0600);
		if (tempFd >= 0) break;
		if (errno != EEXIST) break;
	}
	if (tempFd < 0)
	{
		LOG(LogError) << "[CreditManager] exclusive temp create failed: " << path;
		return false;
	}

	bool stored = true;
	size_t offset = 0;
	while (stored && offset < content.size())
	{
		const ssize_t written = ::write(tempFd, content.data() + offset, content.size() - offset);
		if (written <= 0) stored = false;
		else offset += (size_t)written;
	}
	stored = stored && ::fsync(tempFd) == 0;
	stored = (::close(tempFd) == 0) && stored;
	if (!stored)
	{
		::unlink(tmp.c_str());
		LOG(LogError) << "[CreditManager] exclusive temp write/flush failed: " << path;
		return false;
	}
	if (::rename(tmp.c_str(), path.c_str()) == 0)
		return true;
	::unlink(tmp.c_str());
#endif

	LOG(LogError) << "[CreditManager] atomic replace failed; destination preserved: " << path;
	return false;
}

void CreditManager::syncActivePlayerWalletUnlocked()
{
	if (mCurrentPlayer.empty())
	{
		mGuestRemainingSeconds = mRemainingSeconds;
		return;
	}
	for (auto& p : mPlayers)
	{
		if (Utils::String::toLower(p.name) == Utils::String::toLower(mCurrentPlayer))
		{
			p.remainingSeconds = mRemainingSeconds;
			return;
		}
	}
}

bool CreditManager::loadActivePlayerWalletUnlocked()
{
	if (mCurrentPlayer.empty())
	{
		mRemainingSeconds = mGuestRemainingSeconds;
		clamp();
		return true;
	}
	for (const auto& p : mPlayers)
	{
		if (Utils::String::toLower(p.name) == Utils::String::toLower(mCurrentPlayer))
		{
			mRemainingSeconds = p.remainingSeconds;
			clamp();
			return true;
		}
	}
	return false;
}

bool CreditManager::persistConfigUnlocked() const
{
	if (mCreditPersistenceBlocked)
		return false;

	auto out = makePlainOut();
	out << "# TurboRama Locadora / Credito (numeros SEM separador de milhar)\n"
		<< "schemaVersion=" << kSchemaVersion << "\n"
		<< "enabled=" << (mEnabled ? 1 : 0) << "\n"
		<< "blockWithoutCredit=" << (mBlockWithoutCredit ? 1 : 0) << "\n"
		<< "showHud=" << (mShowHud ? 1 : 0) << "\n"
		<< "minutesPerCoin=" << mMinutesPerCoin << "\n"
		<< "debounceMs=" << mDebounceMs << "\n"
		<< "maxRemainingSeconds=" << mMaxRemainingSeconds << "\n"
		<< "priceCentsPerMinute=" << mPriceCentsPerMinute << "\n"
		<< "adminPasswordHash=" << mAdminPasswordHash << "\n";
	const std::string serialized = out.str();
	return serialized.size() <= kMaxConfigFileBytes
		&& atomicWriteText(configFilePath(), serialized);
}

void CreditManager::loadConfig()
{
	const std::string path = configFilePath();
	const RegularFileState fileState = inspectRegularFile(path);
	if (fileState == RegularFileState::Missing)
	{
		mAdminPasswordHash = defaultAdminPasswordHash();
		if (isLegacyPasswordHash(mAdminPasswordHash) || !persistConfigUnlocked())
		{
			mAdminPasswordHash = "invalid-admin-hash-recovery-required";
			blockCreditOperationsAndPersistenceUnlocked("configuracao inicial nao duravel");
			LOG(LogError) << "[CreditManager] configuracao admin inicial nao foi gravada; autenticacao bloqueada";
		}
		return;
	}
	if (fileState != RegularFileState::Regular)
	{
		mAdminPasswordHash = "invalid-admin-hash-recovery-required";
		blockCreditOperationsAndPersistenceUnlocked("configuracao insegura ou inacessivel");
		return;
	}

	std::vector<std::string> configLines;
	if (!readBoundedTextLines(path, kMaxConfigFileBytes, kMaxConfigLines,
		kMaxTextLineBytes, configLines))
	{
		// An existing but unreadable config must never fall back to the known
		// bootstrap password kept by the constructor.
		mAdminPasswordHash = "invalid-admin-hash-recovery-required";
		blockCreditOperationsAndPersistenceUnlocked("configuracao ilegivel ou acima dos limites");
		return;
	}

	// Validate provenance and the complete grammar before changing any runtime
	// setting. Schema 4 is the only historical producer; schema 5 is current.
	std::vector<std::pair<std::string, std::string>> configFields;
	std::unordered_set<std::string> configKeys;
	bool configValid = true;
	long configSchema = 0;
	bool firstPreflightLine = true;
	for (std::string line : configLines)
	{
		if (firstPreflightLine)
		{
			firstPreflightLine = false;
			if (line.size() >= 3 && (unsigned char)line[0] == 0xEF
				&& (unsigned char)line[1] == 0xBB && (unsigned char)line[2] == 0xBF)
				line.erase(0, 3);
		}
		line = Utils::String::trim(line);
		if (line.empty() || line[0] == '#' || line[0] == ';') continue;
		const size_t pos = line.find('=');
		if (pos == std::string::npos)
		{
			configValid = false;
			break;
		}
		const std::string key = Utils::String::toLower(Utils::String::trim(line.substr(0, pos)));
		const std::string val = Utils::String::trim(line.substr(pos + 1));
		const bool knownKey = key == "schemaversion" || key == "enabled"
			|| key == "blockwithoutcredit" || key == "showhud"
			|| key == "minutespercoin" || key == "debouncems"
			|| key == "maxremainingseconds" || key == "pricecentsperminute"
			|| key == "adminpasswordhash" || key == "adminpassword";
		if (!knownKey || !configKeys.insert(key).second)
		{
			configValid = false;
			break;
		}
		configFields.push_back(std::make_pair(key, val));
	}

	auto findConfigValue = [&](const char* wanted, std::string& value) -> bool {
		for (const auto& field : configFields)
			if (field.first == wanted) { value = field.second; return true; }
		return false;
	};
	std::string schemaText;
	if (!findConfigValue("schemaversion", schemaText)
		|| !parseStrictNonNegativeLong(schemaText, configSchema)
		|| (configSchema != 4 && configSchema != kSchemaVersion))
		configValid = false;
	auto validBoolean = [](const std::string& value) -> bool {
		const std::string lowered = Utils::String::toLower(value);
		return value == "0" || value == "1" || lowered == "true" || lowered == "false";
	};
	auto validConfigNumber = [&](const std::string& value, long minimum, long maximum) -> bool {
		long parsed = 0;
		const bool parsedOk = configSchema == 4
			? parseLegacyNonNegativeLong(value, parsed)
			: parseStrictNonNegativeLong(value, parsed);
		return parsedOk && parsed >= minimum && parsed <= maximum;
	};
	std::string value;
	if (!findConfigValue("enabled", value) || !validBoolean(value)) configValid = false;
	if (!findConfigValue("blockwithoutcredit", value) || !validBoolean(value)) configValid = false;
	if (!findConfigValue("showhud", value) || !validBoolean(value)) configValid = false;
	if (!findConfigValue("minutespercoin", value) || !validConfigNumber(value, 1, 60)) configValid = false;
	if (!findConfigValue("debouncems", value) || !validConfigNumber(value, 100, 5000)) configValid = false;
	if (!findConfigValue("maxremainingseconds", value)
		|| !validConfigNumber(value, 3600, kMaxLegacyWalletSeconds)) configValid = false;
	if (!findConfigValue("pricecentsperminute", value)
		|| !validConfigNumber(value, 0, 100000)) configValid = false;
	std::string hashValue;
	std::string plainValue;
	const bool hasHash = findConfigValue("adminpasswordhash", hashValue);
	const bool hasPlain = findConfigValue("adminpassword", plainValue);
	if (hasHash == hasPlain) configValid = false;
	if (hasHash)
	{
		const bool validHash = configSchema == 4
			? isLegacyPasswordHash(hashValue) : isSupportedPasswordHash(hashValue);
		if (!validHash) configValid = false;
	}
	if (hasPlain && (configSchema != 4 || plainValue.size() < 4 || plainValue.size() > 256))
		configValid = false;
	if (!configValid)
	{
		mAdminPasswordHash = "invalid-admin-hash-recovery-required";
		blockCreditOperationsAndPersistenceUnlocked("schema/configuracao invalida ou ambigua");
		return;
	}

	bool sawHash = false;
	std::string legacyPlain;
	bool first = true;
	for (std::string line : configLines)
	{
		if (first)
		{
			first = false;
			if (line.size() >= 3 && (unsigned char)line[0] == 0xEF &&
				(unsigned char)line[1] == 0xBB && (unsigned char)line[2] == 0xBF)
				line = line.substr(3);
		}
		line = Utils::String::trim(line);
		if (line.empty() || line[0] == '#' || line[0] == ';')
			continue;
		auto pos = line.find('=');
		if (pos == std::string::npos)
			continue;
		std::string key = Utils::String::toLower(Utils::String::trim(line.substr(0, pos)));
		std::string val = Utils::String::trim(line.substr(pos + 1));

		if (key == "enabled")
			mEnabled = (val == "1" || Utils::String::toLower(val) == "true");
		else if (key == "blockwithoutcredit")
			mBlockWithoutCredit = (val == "1" || Utils::String::toLower(val) == "true");
		else if (key == "showhud")
			mShowHud = (val == "1" || Utils::String::toLower(val) == "true");
		else if (key == "minutespercoin")
		{
			// SEMPRE digitos puros — "5" ou lixo "5,0" / "5.0"
			long v = 0;
			const bool valid = parseLegacyNonNegativeLong(val, v);
			// Se leu lixo (0) ou valor absurdo, usa 30 minutos por moeda (padrao locadora)
			if (!valid || v < 1 || v > 60)
				mMinutesPerCoin = 30;
			else
				mMinutesPerCoin = (int)v;
		}
		else if (key == "debouncems")
		{
			long v = 0;
			const bool valid = parseLegacyNonNegativeLong(val, v);
			mDebounceMs = (int)std::max(100L, std::min(5000L, valid && v > 0 ? v : 350L));
		}
		else if (key == "maxremainingseconds")
		{
			// BUG CRITICO: locale pt-BR gravava "28,800" e toInteger lia 28
			// → teto de ~1 minuto. Nunca mais aceitar teto < 1 hora.
			long v = 0;
			const bool valid = parseLegacyNonNegativeLong(val, v);
			if (!valid || v < 3600L)
				mMaxRemainingSeconds = 28800L; // 8 horas padrao
			else
				mMaxRemainingSeconds = std::min(7L * 24 * 3600, v);
		}
		else if (key == "pricecentsperminute")
		{
			// 0 = sem preco em R$; 100 = R$ 1,00 por minuto
			long v = 0;
			mPriceCentsPerMinute = parseLegacyNonNegativeLong(val, v)
				? std::min(100000L, v) : 0;
		}
		else if (key == "adminpasswordhash" && isSupportedPasswordHash(val))
		{
			mAdminPasswordHash = isLegacyPasswordHash(val) ? Utils::String::toLower(val) : val;
			sawHash = true;
		}
		else if (key == "adminpassword")
			legacyPlain = val;
	}
	if (!sawHash)
	{
		if (!legacyPlain.empty())
			mAdminPasswordHash = createPasswordHash(legacyPlain);
		else
		{
			// Config existente sem hash valido nunca volta para a credencial conhecida "admin".
			mAdminPasswordHash = "invalid-admin-hash-recovery-required";
			LOG(LogError) << "[CreditManager] hash admin invalido; autenticacao bloqueada ate recuperacao";
		}
	}
	// Sempre regrava cfg em locale C (corrige maxRemainingSeconds=28,800 corrompido)
	if (!persistConfigUnlocked())
	{
		blockCreditOperationsAndPersistenceUnlocked("falha ao promover configuracao validada");
		return;
	}

	LOG(LogInfo) << "[CreditManager] cfg maxRemainingSeconds=" << mMaxRemainingSeconds
		<< " minutesPerCoin=" << mMinutesPerCoin;
}

bool CreditManager::loadPlayers(long& loadedSchemaVersion, bool& fileExists)
{
#ifdef CREDIT_MANAGER_TEST_HOOKS
	const char* abortOnMirrorRead = std::getenv("TURBORAMA_CREDIT_TEST_ABORT_ON_MIRROR_READ");
	if (abortOnMirrorRead != nullptr && *abortOnMirrorRead != '\0')
		std::abort();
#endif
	loadedSchemaVersion = 0;
	fileExists = false;
	mPlayers.clear();
	mCurrentPlayer.clear();
	const std::string path = playersFilePath();
	const RegularFileState fileState = inspectRegularFile(path);
	if (fileState == RegularFileState::Missing)
		return true;
	fileExists = true;
	if (fileState != RegularFileState::Regular)
		return false;

	std::vector<std::string> storedLines;
	if (!readBoundedTextLines(path, kMaxFinancialFileBytes, kMaxFinancialLines,
		kMaxTextLineBytes, storedLines))
		return false;
	if (!storedLines.empty())
	{
		std::string& firstLine = storedLines.front();
		if (firstLine.size() >= 3 && (unsigned char)firstLine[0] == 0xEF
			&& (unsigned char)firstLine[1] == 0xBB
			&& (unsigned char)firstLine[2] == 0xBF)
			firstLine.erase(0, 3);
	}

	bool sawContent = false;
	bool sawSchema = false;
	long schemaVersion = 0;
	for (const auto& storedLine : storedLines)
	{
		const std::string line = Utils::String::trim(storedLine);
		if (line.empty() || line[0] == '#' || line[0] == ';') continue;
		const size_t pos = line.find('=');
		if (pos == std::string::npos) return false;
		const std::string key = Utils::String::toLower(Utils::String::trim(line.substr(0, pos)));
		const std::string val = Utils::String::trim(line.substr(pos + 1));
		if (key == "schemaversion")
		{
			long parsed = 0;
			if (sawSchema || !parseStrictNonNegativeLong(val, parsed)
				|| (parsed != 4 && parsed != kSchemaVersion))
				return false;
			sawSchema = true;
			schemaVersion = parsed;
		}
		else if (key != "currentplayer" && key != "player")
			return false;
		else
			sawContent = true;
	}
	if (!sawContent || !sawSchema)
		return false;

	const bool strictWalletSchema = sawSchema && schemaVersion == kSchemaVersion;
	auto parseStoredNumber = [&](const std::string& value, long& parsed,
		long maximum = LONG_MAX) -> bool
	{
		const bool valid = strictWalletSchema
			? parseStrictNonNegativeLong(value, parsed)
			: parseLegacyNonNegativeLong(value, parsed);
		return valid && parsed <= maximum;
	};

	bool sawCurrentPlayer = false;
	std::string storedCurrentPlayer;
	std::vector<CreditPlayer> storedPlayers;
	std::unordered_set<std::string> storedPlayerNames;
	std::unordered_set<std::string> storedPlayerIds;
	int activePlayerCount = 0;
	size_t archivedPlayerCount = 0;
	for (const auto& storedLine : storedLines)
	{
		const std::string line = Utils::String::trim(storedLine);
		if (line.empty() || line[0] == '#' || line[0] == ';') continue;
		const size_t pos = line.find('=');
		const std::string key = Utils::String::toLower(Utils::String::trim(line.substr(0, pos)));
		const std::string val = Utils::String::trim(line.substr(pos + 1));
		if (key == "schemaversion") continue;
		if (key == "currentplayer")
		{
			if (sawCurrentPlayer || (!val.empty() && sanitizePlayerName(val) != val)) return false;
			sawCurrentPlayer = true;
			storedCurrentPlayer = val;
			continue;
		}

		CreditPlayer player;
		std::string rest = val;
		const size_t firstSeparator = rest.find(';');
		if (firstSeparator == std::string::npos) return false;
		const std::string rawName = Utils::String::trim(rest.substr(0, firstSeparator));
		if (rawName.empty() || sanitizePlayerName(rawName) != rawName) return false;
		player.name = rawName;
		rest = rest.substr(firstSeparator + 1);
		bool sawId = false;
		bool sawPlayed = false;
		bool sawRemaining = false;
		bool sawPurchased = false;
		bool sawArchived = false;
		bool sawTombstonedAt = false;
		while (!rest.empty())
		{
			const size_t separator = rest.find(';');
			const std::string field = separator == std::string::npos ? rest : rest.substr(0, separator);
			const size_t equals = field.find('=');
			if (equals == std::string::npos) return false;
			const std::string fieldKey = Utils::String::toLower(Utils::String::trim(field.substr(0, equals)));
			const std::string fieldValue = Utils::String::trim(field.substr(equals + 1));
			long parsed = 0;
			if (fieldKey == "id")
			{
				if (!strictWalletSchema || sawId || !isValidWalletId(fieldValue)) return false;
				sawId = true;
				player.id = fieldValue;
			}
			else if (fieldKey == "playedseconds")
			{
				if (sawPlayed || !parseStoredNumber(fieldValue, parsed)) return false;
				sawPlayed = true;
				player.totalPlayedSeconds = parsed;
			}
			else if (fieldKey == "remainingseconds")
			{
				if (sawRemaining || !parseStoredNumber(fieldValue, parsed,
					strictWalletSchema ? kMaxPixWalletSeconds : kMaxLegacyWalletSeconds)) return false;
				sawRemaining = true;
				player.remainingSeconds = parsed;
			}
			else if (fieldKey == "totalminutespurchased"
				|| (!strictWalletSchema && fieldKey == "minutespurchased"))
			{
				if (sawPurchased || !parseStoredNumber(fieldValue, parsed)) return false;
				sawPurchased = true;
				player.totalMinutesPurchased = parsed;
			}
			else if (fieldKey == "archived")
			{
				if (!strictWalletSchema) return false;
				const std::string lowered = Utils::String::toLower(fieldValue);
				if (sawArchived || (fieldValue != "0" && fieldValue != "1"
					&& lowered != "true" && lowered != "false")) return false;
				sawArchived = true;
				player.archived = fieldValue == "1" || lowered == "true";
			}
			else if (fieldKey == "tombstonedat")
			{
				if (!strictWalletSchema || sawTombstonedAt
					|| !parseStrictNonNegativeLong(fieldValue, parsed)) return false;
				sawTombstonedAt = true;
				player.tombstonedAtUnixSeconds = parsed;
			}
			else return false;

			if (separator == std::string::npos) break;
			rest = rest.substr(separator + 1);
		}
		if (!sawPlayed || !sawRemaining || !sawPurchased
			|| (strictWalletSchema && (!sawId || !sawArchived || !sawTombstonedAt)))
			return false;
		const std::string loweredName = Utils::String::toLower(player.name);
		if (!storedPlayerNames.insert(loweredName).second
			|| (!player.id.empty() && !storedPlayerIds.insert(player.id).second))
			return false;
		if (player.archived) ++archivedPlayerCount; else ++activePlayerCount;
		if (activePlayerCount > kMaxPlayers || archivedPlayerCount > kMaxWalletTombstones)
			return false;
		storedPlayers.push_back(player);
	}
	if (sawSchema && !sawCurrentPlayer)
		return false;
	if (!storedCurrentPlayer.empty())
	{
		bool found = false;
		for (const auto& player : storedPlayers)
		{
			if (!player.archived && Utils::String::toLower(player.name)
				== Utils::String::toLower(storedCurrentPlayer))
			{
				storedCurrentPlayer = player.name;
				found = true;
				break;
			}
		}
		if (!found) return false;
	}

	mPlayers = storedPlayers;
	mCurrentPlayer = storedCurrentPlayer;
	loadedSchemaVersion = schemaVersion;
	// Instalacoes antigas nao tinham ID opaco. Gere uma vez e persista no fim do load().
	for (auto& player : mPlayers)
	{
		if (isValidWalletId(player.id)) continue;
		bool duplicate = false;
		do
		{
			player.id = generateWalletId();
			duplicate = false;
			for (const auto& other : mPlayers)
				if (&other != &player && other.id == player.id) duplicate = true;
		}
		while (duplicate);
	}
	return true;
}

void CreditManager::blockCreditOperationsAndPersistenceUnlocked(const char* reason)
{
	if (mCreditPersistenceBlocked)
		return;

	mCreditPersistenceBlocked = true;
	mRemainingSeconds = 0;
	mGuestRemainingSeconds = 0;
	mGuestWalletId.clear();
	mTotalCoinsAccepted = 0;
	mTotalMinutesSold = 0;
	mTotalSecondsPlayed = 0;
	mPlayers.clear();
	mCurrentPlayer.clear();
	mRetiredGuestWallets.clear();
	mRetiredGuestAliases.clear();
	mAppliedPixTransactions.clear();
	mSessionRunning = false;
	mSessionPaused = false;
	mInGame = false;
	mGameWasCounting = false;
	mGameAccountedSeconds = 0;
	mTickAccumMs = 0;
	mSaveAccumMs = 0;
	resetLowTimeWarningsUnlocked();

	LOG(LogError) << "[CreditManager] modo financeiro bloqueado ("
		<< (reason ? reason : "falha desconhecida")
		<< "); operacoes financeiras e toda persistencia foram bloqueadas nesta execucao";
}

void CreditManager::load()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked)
		return;

	loadConfig();
	if (mCreditPersistenceBlocked)
		return;

	mPlayers.clear();
	mCurrentPlayer.clear();
	mRemainingSeconds = 0;
	mGuestRemainingSeconds = 0;
	mGuestWalletId = generateWalletId();
	mRetiredGuestWallets.clear();
	mRetiredGuestAliases.clear();
	mTotalCoinsAccepted = 0;
	mTotalMinutesSold = 0;
	mTotalSecondsPlayed = 0;
	mAppliedPixTransactions.clear();
	mSessionRunning = false;
	mSessionPaused = false;
	mInGame = false;
	mGameWasCounting = false;
	mGameAccountedSeconds = 0;
	mTickAccumMs = 0;
	mSaveAccumMs = 0;

	const std::string path = creditFilePath();
	const RegularFileState creditFileState = inspectRegularFile(path);
	if (creditFileState == RegularFileState::UnsafeOrError)
	{
		blockCreditOperationsAndPersistenceUnlocked("arquivo financeiro inseguro ou inacessivel");
		return;
	}
	const bool creditFileExists = creditFileState == RegularFileState::Regular;
	bool creditHasAuthoritativeWalletSchema = false;
	std::vector<std::string> storedLines;
	if (creditFileExists)
	{
		if (!readBoundedTextLines(path, kMaxFinancialFileBytes, kMaxFinancialLines,
			kMaxTextLineBytes, storedLines))
		{
			blockCreditOperationsAndPersistenceUnlocked("arquivo financeiro ilegivel ou acima dos limites");
			return;
		}
		if (!storedLines.empty())
		{
			std::string& firstLine = storedLines.front();
			if (firstLine.size() >= 3 && (unsigned char)firstLine[0] == 0xEF
				&& (unsigned char)firstLine[1] == 0xBB
				&& (unsigned char)firstLine[2] == 0xBF)
				firstLine.erase(0, 3);
		}
	}
	long machineRemaining = 0;
	long storedGuestRemaining = 0;
	bool sawGuestRemaining = false;
	bool sawWalletSchema = false;
	std::string storedGuestId;
	std::string storedCurrentPlayer;
	std::vector<CreditPlayer> authoritativePlayers;
	std::unordered_set<std::string> parsedAliasIds;
	std::unordered_set<std::string> parsedTransactionIds;
	bool truncatedWalletRecords = false;
	bool invalidWalletRecords = false;
	auto parseAuthoritativeNumber = [&](const std::string& value, const char* field,
		long maximum = LONG_MAX) -> long
	{
		long parsed = 0;
		const bool valid = sawWalletSchema
			? parseStrictNonNegativeLong(value, parsed)
			: parseLegacyNonNegativeLong(value, parsed);
		if (!valid || parsed > maximum)
		{
			invalidWalletRecords = true;
			LOG(LogError) << "[CreditManager] valor invalido no estado de carteiras: "
				<< field;
			return 0;
		}
		return parsed;
	};
	auto parsePlayerRecord = [&](const std::string& value, CreditPlayer& player) -> bool
	{
		std::string rest = value;
		const size_t firstSeparator = rest.find(';');
		player.name = sanitizePlayerName(firstSeparator == std::string::npos
			? rest : rest.substr(0, firstSeparator));
		if (player.name.empty()) return false;
		rest = firstSeparator == std::string::npos ? std::string() : rest.substr(firstSeparator + 1);
		while (!rest.empty())
		{
			const size_t separator = rest.find(';');
			const std::string part = separator == std::string::npos ? rest : rest.substr(0, separator);
			const size_t equals = part.find('=');
			if (equals != std::string::npos)
			{
				const std::string key = Utils::String::toLower(Utils::String::trim(part.substr(0, equals)));
				const std::string val = Utils::String::trim(part.substr(equals + 1));
				if (key == "id" && isValidWalletId(val)) player.id = val;
				else if (key == "playedseconds") player.totalPlayedSeconds = parseAuthoritativeNumber(val, "player.playedSeconds");
				else if (key == "remainingseconds") player.remainingSeconds = parseAuthoritativeNumber(
					val, "player.remainingSeconds", kMaxPixWalletSeconds);
				else if (key == "totalminutespurchased" || key == "minutespurchased")
					player.totalMinutesPurchased = parseAuthoritativeNumber(val, "player.totalMinutesPurchased");
				else if (key == "archived")
					player.archived = (val == "1" || Utils::String::toLower(val) == "true");
				else if (key == "tombstonedat")
					parseStrictNonNegativeLong(val, player.tombstonedAtUnixSeconds);
			}
			if (separator == std::string::npos) break;
			rest = rest.substr(separator + 1);
		}
		return true;
	};

	if (creditFileExists)
	{
		bool stateValid = true;
		bool sawContent = false;
		bool sawSchemaVersion = false;
		bool sawWalletMarker = false;
		long declaredSchemaVersion = 0;
		for (const auto& storedLine : storedLines)
		{
			const std::string line = Utils::String::trim(storedLine);
			if (line.empty() || line[0] == '#' || line[0] == ';') continue;
			const size_t pos = line.find('=');
			if (pos == std::string::npos)
			{
				stateValid = false;
				break;
			}
			sawContent = true;
			const std::string key = Utils::String::toLower(Utils::String::trim(line.substr(0, pos)));
			const std::string val = Utils::String::trim(line.substr(pos + 1));
			if (key == "schemaversion")
			{
				long parsed = 0;
				if (sawSchemaVersion || !parseStrictNonNegativeLong(val, parsed)) stateValid = false;
				else
				{
					sawSchemaVersion = true;
					declaredSchemaVersion = parsed;
				}
			}
			else if (key == "walletschema")
			{
				if (sawWalletMarker || val != "1") stateValid = false;
				sawWalletMarker = true;
			}
			if (!stateValid) break;
		}
		stateValid = stateValid && sawContent;

		const bool authoritativeSchema = sawWalletMarker
			|| (sawSchemaVersion && declaredSchemaVersion >= kSchemaVersion);
		creditHasAuthoritativeWalletSchema = authoritativeSchema;
		if (authoritativeSchema)
		{
			stateValid = stateValid && sawSchemaVersion
				&& declaredSchemaVersion == kSchemaVersion && sawWalletMarker;
			bool sawRemaining = false;
			bool sawGuestId = false;
			bool sawGuestBalance = false;
			bool sawCoins = false;
			bool sawMinutesSold = false;
			bool sawSecondsPlayed = false;
			bool sawCurrentPlayer = false;
			std::unordered_set<std::string> seenAliases;
			std::unordered_set<std::string> seenTransactions;
			auto validateWalletRecord = [&](const std::string& value, bool playerRecord) -> bool
			{
				std::string fieldsText = value;
				if (playerRecord)
				{
					const size_t separator = fieldsText.find(';');
					if (separator == std::string::npos) return false;
					const std::string name = Utils::String::trim(fieldsText.substr(0, separator));
					if (name.empty() || sanitizePlayerName(name) != name) return false;
					fieldsText = fieldsText.substr(separator + 1);
				}
				bool sawId = false;
				bool sawPlayed = !playerRecord;
				bool sawBalance = false;
				bool sawPurchased = !playerRecord;
				bool sawArchived = !playerRecord;
				bool sawTombstonedAt = false;
				std::istringstream fields(fieldsText);
				std::string field;
				while (std::getline(fields, field, ';'))
				{
					const size_t equals = field.find('=');
					if (equals == std::string::npos) return false;
					const std::string fieldKey = Utils::String::toLower(Utils::String::trim(field.substr(0, equals)));
					const std::string fieldValue = Utils::String::trim(field.substr(equals + 1));
					long parsed = 0;
					if (fieldKey == "id")
					{
						if (sawId || !isValidWalletId(fieldValue)) return false;
						sawId = true;
					}
					else if (fieldKey == "playedseconds")
					{
						if (sawPlayed || !parseStrictNonNegativeLong(fieldValue, parsed)) return false;
						sawPlayed = true;
					}
					else if (fieldKey == "remainingseconds")
					{
						if (sawBalance || !parseStrictNonNegativeLong(fieldValue, parsed)
							|| parsed > kMaxPixWalletSeconds) return false;
						sawBalance = true;
					}
					else if (fieldKey == "totalminutespurchased")
					{
						if (sawPurchased || !parseStrictNonNegativeLong(fieldValue, parsed)) return false;
						sawPurchased = true;
					}
					else if (fieldKey == "archived")
					{
						if (sawArchived || (fieldValue != "0" && fieldValue != "1")) return false;
						sawArchived = true;
					}
					else if ((playerRecord && fieldKey == "tombstonedat")
						|| (!playerRecord && fieldKey == "retiredat"))
					{
						if (sawTombstonedAt || !parseStrictNonNegativeLong(fieldValue, parsed)) return false;
						sawTombstonedAt = true;
					}
					else return false;
				}
				return sawId && sawPlayed && sawBalance && sawPurchased
					&& sawArchived && sawTombstonedAt;
			};
			for (const auto& storedLine : storedLines)
			{
				const std::string line = Utils::String::trim(storedLine);
				if (line.empty() || line[0] == '#' || line[0] == ';') continue;
				const size_t pos = line.find('=');
				if (pos == std::string::npos)
				{
					stateValid = false;
					break;
				}
				const std::string key = Utils::String::toLower(Utils::String::trim(line.substr(0, pos)));
				const std::string val = Utils::String::trim(line.substr(pos + 1));
				long parsed = 0;
				auto requireNumber = [&](bool& saw) {
					if (saw || !parseStrictNonNegativeLong(val, parsed)) stateValid = false;
					saw = true;
				};
				auto requireBalance = [&](bool& saw) {
					if (saw || !parseStrictNonNegativeLong(val, parsed)
						|| parsed > kMaxPixWalletSeconds) stateValid = false;
					saw = true;
				};
				if (key == "remainingseconds") requireBalance(sawRemaining);
				else if (key == "guestid")
				{
					if (sawGuestId || !isValidWalletId(val)) stateValid = false;
					sawGuestId = true;
				}
				else if (key == "guestremainingseconds") requireBalance(sawGuestBalance);
				else if (key == "totalcoinsaccepted") requireNumber(sawCoins);
				else if (key == "totalminutessold") requireNumber(sawMinutesSold);
				else if (key == "totalsecondsplayed") requireNumber(sawSecondsPlayed);
				else if (key == "currentplayer")
				{
					if (sawCurrentPlayer || (!val.empty() && sanitizePlayerName(val) != val)) stateValid = false;
					sawCurrentPlayer = true;
				}
				else if (key == "player")
				{
					if (!validateWalletRecord(val, true)) stateValid = false;
				}
				else if (key == "retiredguest")
				{
					if (!validateWalletRecord(val, false)) stateValid = false;
				}
				else if (key == "retiredguestalias")
				{
					if (!isValidWalletId(val) || !seenAliases.insert(val).second
						|| seenAliases.size() > kMaxWalletTombstones)
						stateValid = false;
				}
				else if (key == "pixtransaction")
				{
					if (!isValidPixTransactionId(val) || !seenTransactions.insert(val).second
						|| seenTransactions.size() > kMaxAppliedPixTransactions)
						stateValid = false;
				}
				else if (key != "schemaversion" && key != "walletschema") stateValid = false;
				if (!stateValid)
				{
					LOG(LogError) << "[CreditManager] campo schema 5 invalido: " << key;
					break;
				}
			}
			stateValid = stateValid && sawRemaining && sawGuestId && sawGuestBalance
				&& sawCoins && sawMinutesSold && sawSecondsPlayed && sawCurrentPlayer;
		}
		else
		{
			// Apenas o schema 4 produzido pela versao anterior e migravel. Qualquer
			// campo de carteira schema 5 em arquivo legado torna a origem ambigua.
			bool sawLegacyRemaining = false;
			bool sawLegacyCoins = false;
			bool sawLegacyMinutesSold = false;
			bool sawLegacySecondsPlayed = false;
			bool sawLegacyCurrentPlayer = false;
			std::unordered_set<std::string> seenLegacyTransactions;
			for (const auto& storedLine : storedLines)
			{
				const std::string line = Utils::String::trim(storedLine);
				if (line.empty() || line[0] == '#' || line[0] == ';') continue;
				const size_t pos = line.find('=');
				if (pos == std::string::npos)
				{
					stateValid = false;
					break;
				}
				const std::string key = Utils::String::toLower(Utils::String::trim(line.substr(0, pos)));
				const std::string val = Utils::String::trim(line.substr(pos + 1));
				long parsed = 0;
				auto requireLegacyNumber = [&](bool& saw, long maximum = LONG_MAX) {
					if (saw || !parseLegacyNonNegativeLong(val, parsed) || parsed > maximum)
						stateValid = false;
					saw = true;
				};
				if (key == "schemaversion")
				{
					// Duplicata/valor ja foram validados no primeiro passe.
				}
				else if (key == "remainingseconds")
					requireLegacyNumber(sawLegacyRemaining, kMaxLegacyWalletSeconds);
				else if (key == "totalcoinsaccepted")
					requireLegacyNumber(sawLegacyCoins);
				else if (key == "totalminutessold")
					requireLegacyNumber(sawLegacyMinutesSold);
				else if (key == "totalsecondsplayed")
					requireLegacyNumber(sawLegacySecondsPlayed);
				else if (key == "currentplayer")
				{
					if (sawLegacyCurrentPlayer || (!val.empty() && sanitizePlayerName(val) != val))
						stateValid = false;
					sawLegacyCurrentPlayer = true;
					storedCurrentPlayer = val;
				}
				else if (key == "pixtransaction")
				{
					if (!isValidPixTransactionId(val)
						|| !seenLegacyTransactions.insert(val).second
						|| seenLegacyTransactions.size() > kMaxAppliedPixTransactions)
						stateValid = false;
				}
				else
					stateValid = false;
				if (!stateValid)
				{
					LOG(LogError) << "[CreditManager] campo schema 4 invalido: " << key;
					break;
				}
			}
			stateValid = stateValid && sawSchemaVersion && declaredSchemaVersion == 4
				&& !sawWalletMarker && sawLegacyRemaining && sawLegacyCoins
				&& sawLegacyMinutesSold && sawLegacySecondsPlayed && sawLegacyCurrentPlayer;
		}

		if (!stateValid)
		{
			blockCreditOperationsAndPersistenceUnlocked("estado invalido ou incompleto");
			return;
		}
	}

	if (creditHasAuthoritativeWalletSchema)
	{
		// Schema 5 is the sole authority. The mirror is derived output and must not
		// be opened at all: it may be stale, locked, oversized or attacker-controlled.
		mPlayers.clear();
		mCurrentPlayer.clear();
	}
	else
	{
		long mirrorSchemaVersion = 0;
		bool mirrorFileExists = false;
		const bool mirrorValid = loadPlayers(mirrorSchemaVersion, mirrorFileExists);
		const bool allowedLegacyMirror = mirrorValid
			&& (!mirrorFileExists || mirrorSchemaVersion == 4);
		const bool legacyGuestWithoutMirror = creditFileExists && !mirrorFileExists
			&& storedCurrentPlayer.empty();
		const bool allowedBootstrap = !creditFileExists
			? (mirrorFileExists ? mirrorSchemaVersion == 4 : true)
			: (mirrorFileExists ? mirrorSchemaVersion == 4 : legacyGuestWithoutMirror);
		if (!allowedLegacyMirror || !allowedBootstrap)
		{
			blockCreditOperationsAndPersistenceUnlocked("matriz authority/espelho invalida");
			return;
		}
	}
	if (creditFileExists)
	{

		// Detect the authoritative schema before parsing any values. This prevents
		// a reordered/corrupt file from turning a negative balance such as -10
		// into a positive one via the legacy locale-repair parser.
		for (const auto& candidateLine : storedLines)
		{
			const std::string candidate = Utils::String::trim(candidateLine);
			const size_t candidatePos = candidate.find('=');
			if (candidatePos == std::string::npos) continue;
			const std::string candidateKey = Utils::String::toLower(
				Utils::String::trim(candidate.substr(0, candidatePos)));
			const std::string candidateValue = Utils::String::trim(candidate.substr(candidatePos + 1));
			if (candidateKey == "walletschema" && candidateValue == "1")
			{
				sawWalletSchema = true;
				break;
			}
		}

		for (std::string line : storedLines)
		{
			line = Utils::String::trim(line);
			auto pos = line.find('=');
			if (pos == std::string::npos)
				continue;
			std::string key = Utils::String::toLower(Utils::String::trim(line.substr(0, pos)));
			std::string val = Utils::String::trim(line.substr(pos + 1));
			if (key == "walletschema" && val == "1")
				sawWalletSchema = true;
			else if (key == "remainingseconds")
				machineRemaining = parseAuthoritativeNumber(
					val, "remainingSeconds",
					sawWalletSchema ? kMaxPixWalletSeconds : kMaxLegacyWalletSeconds);
			else if (key == "guestid" && isValidWalletId(val))
				storedGuestId = val;
			else if (key == "guestremainingseconds")
			{
				storedGuestRemaining = parseAuthoritativeNumber(
					val, "guestRemainingSeconds", kMaxPixWalletSeconds);
				sawGuestRemaining = true;
			}
			else if (key == "currentplayer")
				storedCurrentPlayer = sanitizePlayerName(val);
			else if (key == "player")
			{
				CreditPlayer player;
				if (parsePlayerRecord(val, player))
				{
					const size_t maxStoredPlayers = kMaxWalletTombstones + (size_t)kMaxPlayers;
					if (authoritativePlayers.size() < maxStoredPlayers)
						authoritativePlayers.push_back(player);
					else
						truncatedWalletRecords = true;
				}
			}
			else if (key == "retiredguest")
			{
				RetiredGuestWallet retired;
				std::istringstream fields(val);
				std::string field;
				while (std::getline(fields, field, ';'))
				{
					const size_t equals = field.find('=');
					if (equals == std::string::npos)
					{
						if (isValidWalletId(field)) retired.id = field;
						continue;
					}
					const std::string fieldKey = Utils::String::toLower(Utils::String::trim(field.substr(0, equals)));
					const std::string fieldValue = Utils::String::trim(field.substr(equals + 1));
					if (fieldKey == "id" && isValidWalletId(fieldValue)) retired.id = fieldValue;
					else if (fieldKey == "remainingseconds") retired.remainingSeconds = parseAuthoritativeNumber(
						fieldValue, "retiredGuest.remainingSeconds", kMaxPixWalletSeconds);
					else if (fieldKey == "retiredat") parseStrictNonNegativeLong(fieldValue, retired.retiredAtUnixSeconds);
				}
				if (isValidWalletId(retired.id))
				{
					if (mRetiredGuestWallets.size() < kMaxWalletTombstones)
						mRetiredGuestWallets.push_back(retired);
					else
						truncatedWalletRecords = true;
				}
			}
			else if (key == "retiredguestalias" && isValidWalletId(val))
			{
				if (!parsedAliasIds.insert(val).second)
					invalidWalletRecords = true;
				else if (mRetiredGuestAliases.size() < kMaxWalletTombstones)
					mRetiredGuestAliases.push_back(val);
				else
					truncatedWalletRecords = true;
			}
			else if (key == "totalcoinsaccepted")
				mTotalCoinsAccepted = parseAuthoritativeNumber(val, "totalCoinsAccepted");
			else if (key == "totalminutessold")
				mTotalMinutesSold = parseAuthoritativeNumber(val, "totalMinutesSold");
			else if (key == "totalsecondsplayed")
				mTotalSecondsPlayed = parseAuthoritativeNumber(val, "totalSecondsPlayed");
			else if (key == "pixtransaction")
			{
				if (!isValidPixTransactionId(val)
					|| !parsedTransactionIds.insert(val).second)
					invalidWalletRecords = true;
				else if (mAppliedPixTransactions.size() < kMaxAppliedPixTransactions)
					mAppliedPixTransactions.push_back(val);
				else
					truncatedWalletRecords = true;
			}
		}
	}
	if (truncatedWalletRecords)
	{
		blockCreditOperationsAndPersistenceUnlocked("limite de registros excedido");
		return;
	}
	if (creditFileExists && !sawWalletSchema)
	{
		const bool sameEmptyState = storedCurrentPlayer.empty() == mCurrentPlayer.empty();
		if (!sameEmptyState || (!storedCurrentPlayer.empty()
			&& Utils::String::toLower(storedCurrentPlayer) != Utils::String::toLower(mCurrentPlayer)))
			invalidWalletRecords = true;
	}

	// Carrega carteira do jogador ativo; se legado sem remaining por jogador,
	// migra saldo da máquina para o jogador atual uma vez.
	if (sawWalletSchema)
	{
		mPlayers.clear();
		std::unordered_set<std::string> playerNames;
		std::unordered_set<std::string> playerIds;
		int activePlayerCount = 0;
		size_t archivedPlayerCount = 0;
		for (auto& candidate : authoritativePlayers)
		{
			const std::string loweredName = Utils::String::toLower(candidate.name);
			const bool duplicate = playerNames.find(loweredName) != playerNames.end()
				|| playerIds.find(candidate.id) != playerIds.end();
			if (!isValidWalletId(candidate.id) || duplicate
				|| (!candidate.archived && activePlayerCount >= kMaxPlayers)
				|| (candidate.archived && archivedPlayerCount >= kMaxWalletTombstones))
			{
				invalidWalletRecords = true;
				continue;
			}
			playerNames.insert(loweredName);
			playerIds.insert(candidate.id);
			if (candidate.archived) ++archivedPlayerCount; else ++activePlayerCount;
			mPlayers.push_back(candidate);
		}
		mCurrentPlayer = storedCurrentPlayer;
		bool currentFound = mCurrentPlayer.empty();
		for (const auto& player : mPlayers)
		{
			if (!player.archived && Utils::String::toLower(player.name) == Utils::String::toLower(mCurrentPlayer))
			{
				mCurrentPlayer = player.name;
				currentFound = true;
				break;
			}
		}
		if (!currentFound) invalidWalletRecords = true;
	}
	if (isValidWalletId(storedGuestId))
		mGuestWalletId = storedGuestId;
	if (sawGuestRemaining)
		mGuestRemainingSeconds = storedGuestRemaining;
	if (sawWalletSchema)
	{
		std::unordered_set<std::string> playerIds;
		std::unordered_set<std::string> retiredIds;
		std::unordered_set<std::string> aliasIds;
		for (const auto& player : mPlayers)
		{
			if (!isValidWalletId(player.id) || !playerIds.insert(player.id).second)
				invalidWalletRecords = true;
		}
		for (const auto& retired : mRetiredGuestWallets)
		{
			if (!isValidWalletId(retired.id) || !retiredIds.insert(retired.id).second
				|| playerIds.find(retired.id) != playerIds.end())
				invalidWalletRecords = true;
		}
		for (const auto& alias : mRetiredGuestAliases)
		{
			if (!isValidWalletId(alias) || !aliasIds.insert(alias).second
				|| playerIds.find(alias) == playerIds.end()
				|| retiredIds.find(alias) != retiredIds.end())
				invalidWalletRecords = true;
		}
		if (!isValidWalletId(mGuestWalletId)
			|| playerIds.find(mGuestWalletId) != playerIds.end()
			|| retiredIds.find(mGuestWalletId) != retiredIds.end()
			|| aliasIds.find(mGuestWalletId) != aliasIds.end())
			invalidWalletRecords = true;

		long activeWalletSnapshot = mGuestRemainingSeconds;
		if (!mCurrentPlayer.empty())
		{
			bool foundActiveWallet = false;
			for (const auto& player : mPlayers)
			{
				if (!player.archived && Utils::String::toLower(player.name)
					== Utils::String::toLower(mCurrentPlayer))
				{
					activeWalletSnapshot = player.remainingSeconds;
					foundActiveWallet = true;
					break;
				}
			}
			if (!foundActiveWallet) invalidWalletRecords = true;
		}
		if (machineRemaining != activeWalletSnapshot)
			invalidWalletRecords = true;
	}
	if (invalidWalletRecords)
	{
		blockCreditOperationsAndPersistenceUnlocked("registros de carteira inconsistentes");
		return;
	}
	bool appliedLegacyAuthoritySnapshot = false;
	if (!sawWalletSchema && !mCurrentPlayer.empty())
	{
		if (!loadActivePlayerWalletUnlocked())
		{
			blockCreditOperationsAndPersistenceUnlocked("jogador atual legado sem carteira");
			return;
		}
		if (creditFileExists)
		{
			mRemainingSeconds = machineRemaining;
			syncActivePlayerWalletUnlocked();
			appliedLegacyAuthoritySnapshot = true;
		}
	}
	else if (!sawWalletSchema && creditFileExists)
	{
		// Sem jogador: saldo de máquina (modo convidado)
		mRemainingSeconds = machineRemaining;
		appliedLegacyAuthoritySnapshot = true;
	}

	if (sawWalletSchema || sawGuestRemaining)
		loadActivePlayerWalletUnlocked();
	clamp();
	syncActivePlayerWalletUnlocked();
	if (!persistPlayersUnlocked())
	{
		LOG(LogError) << "[CreditManager] falha ao persistir estado atomico das carteiras";
		return;
	}
	if (appliedLegacyAuthoritySnapshot)
		LOG(LogInfo) << "[CreditManager] snapshot schema 4 aplicado a carteira ativa";
	LOG(LogInfo) << "[CreditManager] locadora loaded players=" << mPlayers.size()
		<< " current=" << mCurrentPlayer
		<< " remaining=" << mRemainingSeconds;
}

bool CreditManager::persistCreditUnlocked() const
{
	if (mCreditPersistenceBlocked)
		return false;
	if (activePlayerCountUnlocked() > (size_t)kMaxPlayers)
	{
		LOG(LogError) << "[CreditManager] estado recusado: limite de jogadores ativos excedido";
		return false;
	}
	size_t archivedCount = 0;
	for (const auto& player : mPlayers) if (player.archived) ++archivedCount;
	if (archivedCount > kMaxWalletTombstones
		|| mRetiredGuestWallets.size() > kMaxWalletTombstones
		|| mRetiredGuestAliases.size() > kMaxWalletTombstones
		|| mAppliedPixTransactions.size() > kMaxAppliedPixTransactions)
	{
		LOG(LogError) << "[CreditManager] estado recusado: limite de tombstones/ledger excedido";
		return false;
	}

	// Guarda total de moedas + snapshot do saldo ativo (backup)
	// SEMPRE locale C — nunca "1.234" / "1,234"
	auto out = makePlainOut();
	out << "schemaVersion=" << kSchemaVersion << "\n"
		<< "walletSchema=1\n"
		<< "remainingSeconds=" << mRemainingSeconds << "\n"
		<< "guestId=" << mGuestWalletId << "\n"
		<< "guestRemainingSeconds=" << mGuestRemainingSeconds << "\n"
		<< "totalCoinsAccepted=" << mTotalCoinsAccepted << "\n"
		<< "totalMinutesSold=" << mTotalMinutesSold << "\n"
		<< "totalSecondsPlayed=" << mTotalSecondsPlayed << "\n"
		<< "currentPlayer=" << mCurrentPlayer << "\n";
	for (const auto& player : mPlayers)
	{
		out << "player=" << player.name
			<< ";id=" << player.id
			<< ";playedSeconds=" << player.totalPlayedSeconds
			<< ";remainingSeconds=" << player.remainingSeconds
			<< ";totalMinutesPurchased=" << player.totalMinutesPurchased
			<< ";archived=" << (player.archived ? 1 : 0)
			<< ";tombstonedAt=" << player.tombstonedAtUnixSeconds
			<< "\n";
	}
	for (const auto& retired : mRetiredGuestWallets)
		out << "retiredGuest=id=" << retired.id
			<< ";remainingSeconds=" << retired.remainingSeconds
			<< ";retiredAt=" << retired.retiredAtUnixSeconds << "\n";
	for (const auto& alias : mRetiredGuestAliases)
		out << "retiredGuestAlias=" << alias << "\n";
	for (const auto& transactionId : mAppliedPixTransactions)
		out << "pixTransaction=" << transactionId << "\n";
	const std::string serialized = out.str();
	if (serialized.size() > kMaxFinancialFileBytes)
	{
		LOG(LogError) << "[CreditManager] authority serializada excede limite de reabertura";
		return false;
	}
	return atomicWriteText(creditFilePath(), serialized);
}

bool CreditManager::persistPlayersMirrorUnlocked() const
{
	if (mCreditPersistenceBlocked)
		return false;

	auto out = makePlainOut();
	out << "# TurboRama Locadora - jogadores\n"
		<< "schemaVersion=" << kSchemaVersion << "\n"
		<< "currentPlayer=" << mCurrentPlayer << "\n";
	for (const auto& p : mPlayers)
	{
		out << "player=" << p.name
			<< ";id=" << p.id
			<< ";playedSeconds=" << p.totalPlayedSeconds
			<< ";remainingSeconds=" << p.remainingSeconds
			<< ";totalMinutesPurchased=" << p.totalMinutesPurchased
			<< ";archived=" << (p.archived ? 1 : 0)
			<< ";tombstonedAt=" << p.tombstonedAtUnixSeconds
			<< "\n";
	}
	const std::string serialized = out.str();
	if (serialized.size() > kMaxFinancialFileBytes)
		return false;
	return atomicWriteText(playersFilePath(), serialized);
}

bool CreditManager::persistPlayersUnlocked()
{
	if (!persistCreditUnlocked())
	{
		blockCreditOperationsAndPersistenceUnlocked("falha ao gravar autoridade financeira");
		return false;
	}
	if (!persistPlayersMirrorUnlocked())
		LOG(LogWarning) << "[CreditManager] autoridade gravada; espelho de jogadores sera reconstruido";
	return true;
}

void CreditManager::save() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return;
	CreditManager* self = const_cast<CreditManager*>(this);
	self->syncActivePlayerWalletUnlocked();
	self->persistPlayersUnlocked();
}

void CreditManager::savePlayers() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return;
	CreditManager* self = const_cast<CreditManager*>(this);
	self->syncActivePlayerWalletUnlocked();
	self->persistPlayersUnlocked();
}

void CreditManager::flushNow()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return;
	syncActivePlayerWalletUnlocked();
	persistPlayersUnlocked();
}

void CreditManager::clamp()
{
	// Seguranca: nunca permitir teto ridiculo (ex.: 28 ou 60 por locale corrompido)
	// Minimo operacional locadora = 1 hora; padrao = 8 horas
	if (mMaxRemainingSeconds < 3600)
		mMaxRemainingSeconds = 28800;
	if (mMaxRemainingSeconds > 7L * 24 * 3600)
		mMaxRemainingSeconds = 7L * 24 * 3600;
	if (mMinutesPerCoin < 1 || mMinutesPerCoin > 60)
		mMinutesPerCoin = 30;

	if (mRemainingSeconds < 0) mRemainingSeconds = 0;
	// mMaxRemainingSeconds limits coin/manual additions, not an already-paid PIX
	// wallet. A valid persisted balance must survive restart without truncation.
	if (mRemainingSeconds > kMaxPixWalletSeconds) mRemainingSeconds = kMaxPixWalletSeconds;
	if (mGuestRemainingSeconds < 0) mGuestRemainingSeconds = 0;
	if (mGuestRemainingSeconds > kMaxPixWalletSeconds) mGuestRemainingSeconds = kMaxPixWalletSeconds;
	if (mTotalCoinsAccepted < 0) mTotalCoinsAccepted = 0;
	if (mTickAccumMs < 0) mTickAccumMs = 0;
	if (mSaveAccumMs < 0) mSaveAccumMs = 0;
}

void CreditManager::resetLowTimeWarningsUnlocked()
{
	mLowTimeWarnStage = 0;
	mPendingLowTimeWarning.clear();
}

void CreditManager::updateLowTimeWarningsUnlocked()
{
	if (!mEnabled)
		return;

	const long r = mRemainingSeconds;

	// Com mais de 15 min: limpa estagio (pode avisar de novo se o tempo baixar outra vez)
	if (r > 900)
	{
		mLowTimeWarnStage = 0;
		return;
	}

	// Ordem do mais urgente para o menos — se o tempo "pular" varios limiares
	// (ex.: saiu do jogo), mostra o aviso mais critico ainda nao exibido.
	if (r <= 0 && mLowTimeWarnStage < 7)
	{
		mPendingLowTimeWarning = "TEMPO ESGOTADO! Pressione START e escolha COMPRAR TEMPO COM PIX.";
		mLowTimeWarnStage = 7;
	}
	else if (r <= 10 && mLowTimeWarnStage < 6)
	{
		mPendingLowTimeWarning = "ATENCAO: restam apenas 10 SEGUNDOS!";
		mLowTimeWarnStage = 6;
	}
	else if (r <= 30 && mLowTimeWarnStage < 5)
	{
		mPendingLowTimeWarning = "ATENCAO: restam 30 segundos de credito!";
		mLowTimeWarnStage = 5;
	}
	else if (r <= 60 && mLowTimeWarnStage < 4)
	{
		mPendingLowTimeWarning = "ATENCAO: resta 1 MINUTO de credito!";
		mLowTimeWarnStage = 4;
	}
	else if (r <= 120 && mLowTimeWarnStage < 3)
	{
		mPendingLowTimeWarning = "ATENCAO: restam 2 MINUTOS de credito!";
		mLowTimeWarnStage = 3;
	}
	else if (r <= 300 && mLowTimeWarnStage < 2)
	{
		mPendingLowTimeWarning = "ATENCAO: restam 5 MINUTOS! START > COMPRAR TEMPO COM PIX.";
		mLowTimeWarnStage = 2;
	}
	else if (r <= 900 && mLowTimeWarnStage < 1)
	{
		mPendingLowTimeWarning = "ATENCAO: restam 15 MINUTOS! START > COMPRAR TEMPO COM PIX.";
		mLowTimeWarnStage = 1;
	}
}

std::string CreditManager::pollLowCreditWarning()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mPendingLowTimeWarning.empty())
		return std::string();
	std::string msg = mPendingLowTimeWarning;
	mPendingLowTimeWarning.clear();
	return msg;
}

bool CreditManager::isEnabled() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mEnabled;
}

void CreditManager::setEnabled(bool enabled)
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return;
	const bool previous = mEnabled;
	mEnabled = enabled;
	if (!persistConfigUnlocked())
		mEnabled = previous;
}

bool CreditManager::isShowHud() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mShowHud;
}

long CreditManager::getRemainingSeconds() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mRemainingSeconds;
}

long CreditManager::getTotalCoinsAccepted() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mTotalCoinsAccepted;
}

int CreditManager::getMinutesPerCoin() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mMinutesPerCoin;
}

bool CreditManager::isBlockWithoutCredit() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mBlockWithoutCredit;
}

int CreditManager::getDebounceMs() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mDebounceMs;
}

long CreditManager::getMaxRemainingSeconds() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mMaxRemainingSeconds;
}

bool CreditManager::hasCredit() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	if (!mEnabled) return true;
	if (!mBlockWithoutCredit) return true;
	return mRemainingSeconds > 0;
}

bool CreditManager::addCoin()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked || !mEnabled)
		return false;

	// Garantir defaults saudaveis antes de somar
	if (mMaxRemainingSeconds < 3600)
		mMaxRemainingSeconds = 28800;
	if (mMinutesPerCoin < 1 || mMinutesPerCoin > 60)
		mMinutesPerCoin = 30;

	const long long now = nowMs();
	if (mLastCoinTickMs >= 0)
	{
		const long long delta = now - mLastCoinTickMs;
		if (delta >= 0 && delta < mDebounceMs)
			return false;
	}
	mLastCoinTickMs = now;

	const long before = mRemainingSeconds;
	const long add = (long)mMinutesPerCoin * 60L; // minutos → segundos
	if (mRemainingSeconds > mMaxRemainingSeconds - add)
	{
		LOG(LogWarning) << "[CreditManager] moeda rejeitada: teto manual nao comporta +"
			<< mMinutesPerCoin << "min saldo=" << mRemainingSeconds
			<< " teto=" << mMaxRemainingSeconds;
		return false;
	}
	mRemainingSeconds += add;
	if (mTotalCoinsAccepted < LONG_MAX)
		mTotalCoinsAccepted++;
	recordSaleUnlocked(mMinutesPerCoin);

	// Moeda inicia sessão do jogador ativo
	mSessionRunning = true;
	mSessionPaused = false;
	syncActivePlayerWalletUnlocked();
	const long after = mRemainingSeconds;

	// Credito subiu: rearmar avisos se voltou a 15 min ou mais
	if (mRemainingSeconds >= 900)
		resetLowTimeWarningsUnlocked();
	else
		updateLowTimeWarningsUnlocked();

	if (!persistPlayersUnlocked()) return false;
	LOG(LogInfo) << "[CreditManager] MOEDA +" << mMinutesPerCoin
		<< "min (" << add << "s) before=" << before
		<< " after=" << after
		<< " max=" << mMaxRemainingSeconds
		<< " player=" << mCurrentPlayer;
	return true;
}

void CreditManager::recordSaleUnlocked(int minutes)
{
	if (minutes <= 0)
		return;
	if (mTotalMinutesSold <= LONG_MAX - minutes)
		mTotalMinutesSold += minutes;
	else
		mTotalMinutesSold = LONG_MAX;

	if (!mCurrentPlayer.empty())
	{
		for (auto& p : mPlayers)
		{
			if (Utils::String::toLower(p.name) == Utils::String::toLower(mCurrentPlayer))
			{
				if (p.totalMinutesPurchased <= LONG_MAX - minutes)
					p.totalMinutesPurchased += minutes;
				else
					p.totalMinutesPurchased = LONG_MAX;
				break;
			}
		}
	}
}

void CreditManager::recordPlayerSaleUnlocked(CreditPlayer& player, int minutes)
{
	if (minutes <= 0) return;
	if (player.totalMinutesPurchased <= LONG_MAX - minutes)
		player.totalMinutesPurchased += minutes;
	else
		player.totalMinutesPurchased = LONG_MAX;
}

bool CreditManager::addMinutes(int minutes)
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked || !mEnabled)
		return false;
	if (minutes < 1)
		minutes = 1;
	if (minutes > 480)
		minutes = 480; // max 8h por operacao

	if (mMaxRemainingSeconds < 3600)
		mMaxRemainingSeconds = 28800;
	if (mMinutesPerCoin < 1 || mMinutesPerCoin > 60)
		mMinutesPerCoin = 30;

	const long before = mRemainingSeconds;
	const long add = (long)minutes * 60L;
	if (mRemainingSeconds > mMaxRemainingSeconds - add)
	{
		LOG(LogWarning) << "[CreditManager] minutos manuais rejeitados: teto nao comporta +"
			<< minutes << "min saldo=" << mRemainingSeconds
			<< " teto=" << mMaxRemainingSeconds;
		return false;
	}
	mRemainingSeconds += add;
	recordSaleUnlocked(minutes);

	mSessionRunning = true;
	mSessionPaused = false;
	syncActivePlayerWalletUnlocked();
	const long after = mRemainingSeconds;

	if (mRemainingSeconds >= 900)
		resetLowTimeWarningsUnlocked();
	else
		updateLowTimeWarningsUnlocked();

	if (!persistPlayersUnlocked()) return false;
	LOG(LogInfo) << "[CreditManager] ADD +" << minutes
		<< "min before=" << before << " after=" << after
		<< " max=" << mMaxRemainingSeconds << " player=" << mCurrentPlayer;
	return true;
}

bool CreditManager::isValidPixTransactionId(const std::string& transactionId)
{
	if (transactionId.empty() || transactionId.size() > 64)
		return false;
	for (const char ch : transactionId)
	{
		const bool allowed = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')
			|| (ch >= '0' && ch <= '9') || ch == '-' || ch == '_';
		if (!allowed)
			return false;
	}
	return true;
}

PixCreditResult CreditManager::applyPixCredit(const std::string& transactionId, int minutes)
{
	std::string beneficiaryType;
	std::string beneficiaryId;
	if (!getPixBeneficiary(beneficiaryType, beneficiaryId))
		return PixCreditResult::Rejected;
	return applyPixCredit(transactionId, minutes, beneficiaryType, beneficiaryId);
}

bool CreditManager::getPixBeneficiary(std::string& beneficiaryType, std::string& beneficiaryId) const
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	if (mCurrentPlayer.empty())
	{
		if (!isValidWalletId(mGuestWalletId)) return false;
		beneficiaryType = "guest";
		beneficiaryId = mGuestWalletId;
		return true;
	}
	for (const auto& player : mPlayers)
	{
		if (Utils::String::toLower(player.name) == Utils::String::toLower(mCurrentPlayer)
			&& isValidWalletId(player.id))
		{
			beneficiaryType = "player";
			beneficiaryId = player.id;
			return true;
		}
	}
	return false;
}

bool CreditManager::resolvePixWalletUnlocked(const std::string& beneficiaryType,
	const std::string& beneficiaryId, long*& wallet, CreditPlayer*& player, bool& isActive)
{
	wallet = nullptr;
	player = nullptr;
	isActive = false;
	if (!isValidWalletId(beneficiaryId)) return false;
	if (beneficiaryType == "guest")
	{
		if (beneficiaryId == mGuestWalletId)
		{
			wallet = &mGuestRemainingSeconds;
			isActive = mCurrentPlayer.empty();
			return true;
		}
		if (std::find(mRetiredGuestAliases.begin(), mRetiredGuestAliases.end(), beneficiaryId)
			!= mRetiredGuestAliases.end())
		{
			player = findPlayerByIdUnlocked(beneficiaryId);
			if (player == nullptr) return false;
			wallet = &player->remainingSeconds;
			isActive = !mCurrentPlayer.empty()
				&& Utils::String::toLower(player->name) == Utils::String::toLower(mCurrentPlayer);
			return true;
		}
		for (auto& retired : mRetiredGuestWallets)
		{
			if (retired.id == beneficiaryId)
			{
				wallet = &retired.remainingSeconds;
				return true;
			}
		}
		return false;
	}
	if (beneficiaryType != "player") return false;
	player = findPlayerByIdUnlocked(beneficiaryId);
	if (player == nullptr) return false;
	wallet = &player->remainingSeconds;
	isActive = !mCurrentPlayer.empty()
		&& Utils::String::toLower(player->name) == Utils::String::toLower(mCurrentPlayer);
	return true;
}

bool CreditManager::resolvePixWalletUnlocked(const std::string& beneficiaryType,
	const std::string& beneficiaryId, const long*& wallet, const CreditPlayer*& player) const
{
	wallet = nullptr;
	player = nullptr;
	if (!isValidWalletId(beneficiaryId)) return false;
	if (beneficiaryType == "guest")
	{
		if (beneficiaryId == mGuestWalletId)
		{
			wallet = &mGuestRemainingSeconds;
			return true;
		}
		if (std::find(mRetiredGuestAliases.begin(), mRetiredGuestAliases.end(), beneficiaryId)
			!= mRetiredGuestAliases.end())
		{
			player = findPlayerByIdUnlocked(beneficiaryId);
			if (player == nullptr) return false;
			wallet = &player->remainingSeconds;
			return true;
		}
		for (const auto& retired : mRetiredGuestWallets)
		{
			if (retired.id == beneficiaryId)
			{
				wallet = &retired.remainingSeconds;
				return true;
			}
		}
		return false;
	}
	if (beneficiaryType != "player") return false;
	player = findPlayerByIdUnlocked(beneficiaryId);
	if (player == nullptr) return false;
	wallet = &player->remainingSeconds;
	return true;
}

bool CreditManager::canAcceptPixMinutes(const std::string& beneficiaryType,
	const std::string& beneficiaryId, int minutes) const
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked || !mEnabled || minutes < 1 || minutes > 480) return false;
	const long* wallet = nullptr;
	const CreditPlayer* player = nullptr;
	if (!resolvePixWalletUnlocked(beneficiaryType, beneficiaryId, wallet, player) || wallet == nullptr)
		return false;
	const long add = (long)minutes * 60L;
	return *wallet >= 0 && *wallet <= kMaxPixWalletSeconds - add;
}

PixCreditResult CreditManager::applyPixCredit(const std::string& transactionId, int minutes,
	const std::string& beneficiaryType, const std::string& beneficiaryId)
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked || !mEnabled
		|| !isValidPixTransactionId(transactionId) || minutes < 1 || minutes > 480)
		return PixCreditResult::Rejected;
	if (std::find(mAppliedPixTransactions.begin(), mAppliedPixTransactions.end(), transactionId)
		!= mAppliedPixTransactions.end())
		return PixCreditResult::AlreadyApplied;
	if (mAppliedPixTransactions.size() >= kMaxAppliedPixTransactions)
	{
		LOG(LogError) << "[CreditManager] PIX retryavel: ledger no limite tx=" << transactionId;
		return PixCreditResult::Rejected;
	}

	// Validate the immutable target before promoting a retired guest. This keeps
	// rejected/oversized events side-effect free.
	const long* candidateWallet = nullptr;
	const CreditPlayer* candidatePlayer = nullptr;
	if (!resolvePixWalletUnlocked(beneficiaryType, beneficiaryId, candidateWallet, candidatePlayer)
		|| candidateWallet == nullptr)
		return PixCreditResult::Rejected;
	const long add = (long)minutes * 60L;
	if (*candidateWallet < 0 || *candidateWallet > kMaxPixWalletSeconds - add)
	{
		LOG(LogError) << "[CreditManager] PIX nao aplicado: limite absoluto da carteira tx="
			<< transactionId << " beneficiary=" << beneficiaryId;
		return PixCreditResult::Rejected;
	}
	if (candidatePlayer != nullptr && candidatePlayer->archived
		&& activePlayerCountUnlocked() >= (size_t)kMaxPlayers)
	{
		LOG(LogError) << "[CreditManager] PIX retryavel: limite de jogadores ativos tx="
			<< transactionId;
		return PixCreditResult::Rejected;
	}
	const long before = *candidateWallet;

	const long previousRemaining = mRemainingSeconds;
	const long previousGuestRemaining = mGuestRemainingSeconds;
	const long previousTotalMinutesSold = mTotalMinutesSold;
	const bool previousSessionRunning = mSessionRunning;
	const bool previousSessionPaused = mSessionPaused;
	const int previousLowTimeWarnStage = mLowTimeWarnStage;
	const std::string previousPendingLowTimeWarning = mPendingLowTimeWarning;
	const std::vector<CreditPlayer> previousPlayers = mPlayers;
	const std::vector<RetiredGuestWallet> previousRetiredGuests = mRetiredGuestWallets;
	const std::vector<std::string> previousGuestAliases = mRetiredGuestAliases;
	const std::vector<std::string> previousTransactions = mAppliedPixTransactions;
	auto restoreState = [&]()
	{
		mRemainingSeconds = previousRemaining;
		mGuestRemainingSeconds = previousGuestRemaining;
		mTotalMinutesSold = previousTotalMinutesSold;
		mSessionRunning = previousSessionRunning;
		mSessionPaused = previousSessionPaused;
		mLowTimeWarnStage = previousLowTimeWarnStage;
		mPendingLowTimeWarning = previousPendingLowTimeWarning;
		mPlayers = previousPlayers;
		mRetiredGuestWallets = previousRetiredGuests;
		mRetiredGuestAliases = previousGuestAliases;
		mAppliedPixTransactions = previousTransactions;
	};

	if (beneficiaryType == "guest" && beneficiaryId != mGuestWalletId)
	{
		if (!promoteRetiredGuestUnlocked(beneficiaryId))
		{
			restoreState();
			return PixCreditResult::Rejected;
		}
	}

	long* wallet = nullptr;
	CreditPlayer* player = nullptr;
	bool isActive = false;
	if (!resolvePixWalletUnlocked(beneficiaryType, beneficiaryId, wallet, player, isActive)
		|| wallet == nullptr)
	{
		restoreState();
		return PixCreditResult::Rejected;
	}

	*wallet += add; // credito PIX aprovado nunca e truncado pelo teto manual de 8h
	if (mTotalMinutesSold <= LONG_MAX - minutes) mTotalMinutesSold += minutes;
	else mTotalMinutesSold = LONG_MAX;
	if (player != nullptr)
	{
		player->archived = false;
		player->tombstonedAtUnixSeconds = 0;
		recordPlayerSaleUnlocked(*player, minutes);
	}
	if (isActive)
	{
		mRemainingSeconds = *wallet;
		mSessionRunning = true;
		mSessionPaused = false;
		if (mRemainingSeconds >= 900) resetLowTimeWarningsUnlocked();
		else updateLowTimeWarningsUnlocked();
	}
	mAppliedPixTransactions.push_back(transactionId);

	// Um unico replace atomico contem ledger + carteira nominal + contabilidade.
	if (!persistCreditUnlocked())
	{
		restoreState();
		blockCreditOperationsAndPersistenceUnlocked("falha ao gravar autoridade PIX");
		LOG(LogError) << "[CreditManager] PIX nao aplicado: falha atomica tx=" << transactionId;
		return PixCreditResult::Rejected;
	}
	if (!persistPlayersMirrorUnlocked())
		LOG(LogWarning) << "[CreditManager] espelho de jogadores sera reconstruido tx=" << transactionId;
	LOG(LogInfo) << "[CreditManager] PIX +" << minutes << "min tx=" << transactionId
		<< " beneficiary=" << beneficiaryType << ":" << beneficiaryId
		<< " before=" << before << " after=" << (before + add);
	return PixCreditResult::Applied;
}

void CreditManager::addPlayedToCurrentUnlocked(long seconds)
{
	if (seconds <= 0 || mCurrentPlayer.empty())
		return;
	for (auto& p : mPlayers)
	{
		if (Utils::String::toLower(p.name) == Utils::String::toLower(mCurrentPlayer))
		{
			if (p.totalPlayedSeconds <= LONG_MAX - seconds)
				p.totalPlayedSeconds += seconds;
			else
				p.totalPlayedSeconds = LONG_MAX;
			return;
		}
	}
}

bool CreditManager::applyConsumeUnlocked(long seconds, const char* reason, bool persist)
{
	if (mCreditPersistenceBlocked)
		return false;
	if (seconds <= 0)
		return true;

	const long before = mRemainingSeconds;
	const long consumed = std::min(seconds, before);
	if (consumed <= 0) return true;
	mRemainingSeconds -= consumed;
	addPlayedToCurrentUnlocked(consumed);
	if (mTotalSecondsPlayed <= LONG_MAX - consumed)
		mTotalSecondsPlayed += consumed;
	else
		mTotalSecondsPlayed = LONG_MAX;
	syncActivePlayerWalletUnlocked();
	clamp();

	if (mRemainingSeconds == 0)
	{
		mSessionRunning = false;
		mSessionPaused = false;
	}

	updateLowTimeWarningsUnlocked();

	const long after = mRemainingSeconds;
	if (persist && !persistPlayersUnlocked())
		return false;
	LOG(LogInfo) << "[CreditManager] " << (reason ? reason : "consume")
		<< " " << consumed << "s before=" << before << " after=" << after
		<< " player=" << mCurrentPlayer;
	return true;
}

void CreditManager::tick(int deltaMs)
{
	if (deltaMs <= 0)
		return;
	if (deltaMs > kMaxTickDeltaMs)
		deltaMs = kMaxTickDeltaMs;

	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked || !mEnabled || mInGame
		|| !mSessionRunning || mSessionPaused || mRemainingSeconds <= 0)
		return;

	mTickAccumMs += deltaMs;
	mSaveAccumMs += deltaMs;
	if (mTickAccumMs < 1000)
		return;

	int wholeSec = mTickAccumMs / 1000;
	mTickAccumMs %= 1000;
	if (wholeSec > mRemainingSeconds)
		wholeSec = (int)mRemainingSeconds;

	mRemainingSeconds = std::max(0L, mRemainingSeconds - (long)wholeSec);
	addPlayedToCurrentUnlocked(wholeSec);
	if (mTotalSecondsPlayed <= LONG_MAX - wholeSec)
		mTotalSecondsPlayed += wholeSec;
	else
		mTotalSecondsPlayed = LONG_MAX;
	syncActivePlayerWalletUnlocked();

	if (mRemainingSeconds == 0)
	{
		mSessionRunning = false;
		mSessionPaused = false;
		mTickAccumMs = 0;
		mSaveAccumMs = kSaveIntervalMs;
	}

	// Avisos 2min / 1min / 30s / 10s / esgotado (balao no UI)
	if (wholeSec > 0)
		updateLowTimeWarningsUnlocked();

	if (mSaveAccumMs >= kSaveIntervalMs || mRemainingSeconds == 0)
	{
		mSaveAccumMs = 0;
		if (!persistPlayersUnlocked()) return;
	}
}

bool CreditManager::accountGameElapsedUnlocked(long elapsedSeconds, bool forcePersist)
{
	if (mCreditPersistenceBlocked)
		return false;
	if (!mEnabled || !mInGame || !mGameWasCounting)
		return true;
	if (elapsedSeconds < 0) elapsedSeconds = 0;
	if (elapsedSeconds > kMaxPixWalletSeconds) elapsedSeconds = kMaxPixWalletSeconds;
	if (elapsedSeconds < mGameAccountedSeconds)
		elapsedSeconds = mGameAccountedSeconds;
	const long delta = elapsedSeconds - mGameAccountedSeconds;
	if (delta > 0)
	{
		mGameAccountedSeconds = elapsedSeconds;
		const int addedSaveMs = (int)(std::min(delta, 2000L) * 1000L);
		mSaveAccumMs = mSaveAccumMs > INT_MAX - addedSaveMs
			? INT_MAX : mSaveAccumMs + addedSaveMs;
		const bool saveNow = forcePersist || mSaveAccumMs >= kSaveIntervalMs || delta >= mRemainingSeconds;
		if (!applyConsumeUnlocked(delta, "jogo", saveNow)) return false;
		if (saveNow) mSaveAccumMs = 0;
	}
	else if (forcePersist && mSaveAccumMs > 0)
	{
		if (!persistPlayersUnlocked()) return false;
		mSaveAccumMs = 0;
	}
	return !mCreditPersistenceBlocked && (!mBlockWithoutCredit || mRemainingSeconds > 0);
}

void CreditManager::beginGameSession()
{
	std::lock_guard<std::mutex> lock(mMutex);
	mGameAccountedSeconds = 0;
	mSaveAccumMs = 0;
	if (!mEnabled)
	{
		mInGame = true;
		mGameWasCounting = false;
		return;
	}
	if (mRemainingSeconds > 0)
	{
		mSessionRunning = true;
		mSessionPaused = false;
	}
	mInGame = true;
	mGameWasCounting = (mRemainingSeconds > 0);
	mTickAccumMs = 0;
}

bool CreditManager::updateGameSession(long elapsedSeconds)
{
	std::lock_guard<std::mutex> lock(mMutex);
	return accountGameElapsedUnlocked(elapsedSeconds, false);
}

void CreditManager::endGameSession(long elapsedSeconds)
{
	std::lock_guard<std::mutex> lock(mMutex);
	const bool wasCounting = mGameWasCounting;
	if (mEnabled && wasCounting)
		accountGameElapsedUnlocked(elapsedSeconds, true);
	mInGame = false;
	mGameWasCounting = false;
	mGameAccountedSeconds = 0;
	if (!mEnabled || !wasCounting)
		return;
	if (mRemainingSeconds > 0)
	{
		mSessionRunning = true;
		mSessionPaused = false;
	}
}

void CreditManager::consumeSessionSeconds(long elapsedSeconds)
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (!mEnabled || !mSessionRunning || mSessionPaused || mInGame)
		return;
	applyConsumeUnlocked(elapsedSeconds, "sessao");
}

void CreditManager::startSession()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (!mEnabled || mRemainingSeconds <= 0)
		return;
	mSessionRunning = true;
	mSessionPaused = false;
}

void CreditManager::pauseSession()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (!mSessionRunning)
		return;
	mSessionPaused = true;
	mTickAccumMs = 0;
	syncActivePlayerWalletUnlocked();
	persistPlayersUnlocked();
}

void CreditManager::resumeSession()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (!mEnabled || mRemainingSeconds <= 0)
		return;
	mSessionRunning = true;
	mSessionPaused = false;
}

void CreditManager::stopSession()
{
	std::lock_guard<std::mutex> lock(mMutex);
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	syncActivePlayerWalletUnlocked();
	persistPlayersUnlocked();
	LOG(LogInfo) << "[CreditManager] contador PARADO jogador=" << mCurrentPlayer
		<< " saldo=" << mRemainingSeconds;
}

void CreditManager::endActivePlayerTurn()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return;
	if (!canRotateGuestWalletUnlocked())
	{
		LOG(LogError) << "[CreditManager] fim de turno recusado: guest pago sem vaga de recovery";
		return;
	}
	// Para contador e grava saldo na conta cadastrada; desmarca jogador ativo
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	syncActivePlayerWalletUnlocked();
	const std::string was = mCurrentPlayer;
	mCurrentPlayer.clear();
	// O proximo avulso recebe uma carteira nova; pedidos antigos ficam tombstonados.
	if (!rotateGuestWalletUnlocked()) return;
	persistPlayersUnlocked();
	LOG(LogInfo) << "[CreditManager] turno finalizado jogador=" << was << " (maquina livre)";
}

bool CreditManager::isGuestMode() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mCurrentPlayer.empty();
}

bool CreditManager::hasGuestCredit() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mCurrentPlayer.empty() && mRemainingSeconds > 0;
}

void CreditManager::clearGuestCredit()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return;
	if (!canRotateGuestWalletUnlocked(false)) return;
	// Cliente avulso saiu: fecha o tempo sem cadastrar
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	if (!rotateGuestWalletUnlocked(false)) return;
	// nao mexe nas contas cadastradas
	resetLowTimeWarningsUnlocked();
	if (!persistCreditUnlocked())
	{
		blockCreditOperationsAndPersistenceUnlocked("falha ao limpar credito convidado");
		return;
	}
	LOG(LogInfo) << "[CreditManager] credito AVULSO fechado/zerado";
}

bool CreditManager::switchToPlayer(const std::string& name)
{
	const std::string n = sanitizePlayerName(name);
	if (n.empty())
		return false;

	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	std::string targetPlayer;
	for (const auto& player : mPlayers)
		if (!player.archived && Utils::String::toLower(player.name) == Utils::String::toLower(n))
			targetPlayer = player.name;
	if (targetPlayer.empty()) return false;
	if (mCurrentPlayer.empty() && !canRotateGuestWalletUnlocked())
		return false;

	// 1) Para contador do atual e salva carteira
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	const bool wasGuest = mCurrentPlayer.empty();
	syncActivePlayerWalletUnlocked();
	if (wasGuest && !rotateGuestWalletUnlocked()) return false;

	// 2) Seleciona novo
	mCurrentPlayer = targetPlayer;

	// 3) Carrega saldo do novo (fica PARADO até moeda/continuar)
	loadActivePlayerWalletUnlocked();
	if (!persistPlayersUnlocked()) return false;

	// Novo jogador: rearmar avisos conforme o saldo dele
	if (mRemainingSeconds >= 900)
		resetLowTimeWarningsUnlocked();
	else
	{
		resetLowTimeWarningsUnlocked();
		// nao avisa na troca — so quando o tempo estiver caindo
	}

	LOG(LogInfo) << "[CreditManager] trocou para jogador=" << mCurrentPlayer
		<< " saldo=" << mRemainingSeconds << " (parado)";
	return true;
}

bool CreditManager::isSessionRunning() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mSessionRunning;
}

bool CreditManager::isSessionPaused() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mSessionPaused;
}

bool CreditManager::isCounting() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mEnabled && mSessionRunning && !mSessionPaused && !mInGame && mRemainingSeconds > 0;
}

bool CreditManager::isInGame() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mInGame;
}

std::vector<CreditPlayer> CreditManager::getPlayersCopy() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return std::vector<CreditPlayer>();
	std::vector<CreditPlayer> active;
	for (const auto& player : mPlayers)
		if (!player.archived) active.push_back(player);
	return active;
}

std::string CreditManager::getCurrentPlayerName() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mCurrentPlayer;
}

bool CreditManager::setCurrentPlayer(const std::string& name)
{
	return switchToPlayer(name);
}

bool CreditManager::registerPlayer(const std::string& name)
{
	const std::string n = sanitizePlayerName(name);
	if (n.empty())
		return false;

	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;

	// Se já existe, só troca para ele (sem apagar saldo)
	size_t existingIndex = mPlayers.size();
	for (size_t index = 0; index < mPlayers.size(); ++index)
	{
		if (Utils::String::toLower(mPlayers[index].name) == Utils::String::toLower(n))
		{
			existingIndex = index;
			break;
		}
	}
	const bool wasGuest = mCurrentPlayer.empty();
	if (wasGuest && !canRotateGuestWalletUnlocked()) return false;
	const size_t activeCount = activePlayerCountUnlocked();
	const bool activatesExisting = existingIndex != mPlayers.size()
		&& mPlayers[existingIndex].archived;
	const bool createsNewPlayer = existingIndex == mPlayers.size();
	const bool createsGuestRecovery = wasGuest && guestRotationNeedsActiveSlotUnlocked();
	const size_t requiredSlots = (activatesExisting ? 1u : 0u)
		+ (createsNewPlayer ? 1u : 0u) + (createsGuestRecovery ? 1u : 0u);
	if (activeCount + requiredSlots > (size_t)kMaxPlayers)
		return false;
	if (existingIndex != mPlayers.size())
	{
		const std::string existingName = mPlayers[existingIndex].name;
		// save current first
		mSessionRunning = false;
		mSessionPaused = false;
		mTickAccumMs = 0;
		syncActivePlayerWalletUnlocked();
		if (wasGuest && !rotateGuestWalletUnlocked()) return false;
		mPlayers[existingIndex].archived = false;
		mPlayers[existingIndex].tombstonedAtUnixSeconds = 0;
		mCurrentPlayer = existingName;
		loadActivePlayerWalletUnlocked();
		return persistPlayersUnlocked();
	}

	// Antes de criar, salva o atual
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	syncActivePlayerWalletUnlocked();

	CreditPlayer p;
	bool idCollision = false;
	do
	{
		p.id = generateWalletId();
		idCollision = findPlayerByIdUnlocked(p.id) != nullptr || p.id == mGuestWalletId;
		for (const auto& retired : mRetiredGuestWallets) if (retired.id == p.id) idCollision = true;
		for (const auto& alias : mRetiredGuestAliases) if (alias == p.id) idCollision = true;
	}
	while (idCollision);
	p.name = n;
	p.totalPlayedSeconds = 0;
	p.remainingSeconds = 0;
	mPlayers.push_back(p);
	if (wasGuest && !rotateGuestWalletUnlocked())
	{
		mPlayers.pop_back();
		return false;
	}
	mCurrentPlayer = n;
	mRemainingSeconds = 0;
	if (!persistPlayersUnlocked()) return false;
	LOG(LogInfo) << "[CreditManager] jogador cadastrado=" << n;
	return true;
}

bool CreditManager::removePlayer(const std::string& name)
{
	const std::string n = sanitizePlayerName(name);
	if (n.empty())
		return false;

	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	size_t removeIndex = mPlayers.size();
	for (size_t index = 0; index < mPlayers.size(); ++index)
	{
		if (!mPlayers[index].archived
			&& Utils::String::toLower(mPlayers[index].name) == Utils::String::toLower(n))
		{
			removeIndex = index;
			break;
		}
	}
	if (removeIndex == mPlayers.size()) return false;
	size_t archivedCount = 0;
	for (const auto& player : mPlayers) if (player.archived) ++archivedCount;
	if (archivedCount >= kMaxWalletTombstones) return false;

	const bool removingCurrent = Utils::String::toLower(mCurrentPlayer)
		== Utils::String::toLower(n);
	if (removingCurrent)
		syncActivePlayerWalletUnlocked();
	// Archive before rotating: rotation can append a recovered guest and
	// reallocate mPlayers.
	mPlayers[removeIndex].archived = true;
	mPlayers[removeIndex].tombstonedAtUnixSeconds = (long)std::time(nullptr);
	if (removingCurrent)
	{
		mCurrentPlayer.clear();
		if (!rotateGuestWalletUnlocked())
		{
			mPlayers[removeIndex].archived = false;
			mPlayers[removeIndex].tombstonedAtUnixSeconds = 0;
			mCurrentPlayer = mPlayers[removeIndex].name;
			loadActivePlayerWalletUnlocked();
			return false;
		}
		mSessionRunning = false;
		mSessionPaused = false;
	}
	return persistPlayersUnlocked();
}

long CreditManager::getCurrentPlayerPlayedSeconds() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	for (const auto& p : mPlayers)
	{
		if (Utils::String::toLower(p.name) == Utils::String::toLower(mCurrentPlayer))
			return p.totalPlayedSeconds;
	}
	return 0;
}

long CreditManager::getPlayerRemainingSeconds(const std::string& name) const
{
	std::lock_guard<std::mutex> lock(mMutex);
	const std::string n = Utils::String::toLower(sanitizePlayerName(name));
	// se é o ativo, usa mRemainingSeconds (mais atualizado)
	if (!mCurrentPlayer.empty() && Utils::String::toLower(mCurrentPlayer) == n)
		return mRemainingSeconds;
	for (const auto& p : mPlayers)
	{
		if (!p.archived && Utils::String::toLower(p.name) == n)
			return p.remainingSeconds;
	}
	return 0;
}

std::string CreditManager::formatPlayerHours(const std::string& name) const
{
	std::lock_guard<std::mutex> lock(mMutex);
	long s = 0;
	for (const auto& p : mPlayers)
	{
		if (!p.archived && Utils::String::toLower(p.name) == Utils::String::toLower(name))
		{
			s = p.totalPlayedSeconds;
			break;
		}
	}
	const long h = s / 3600;
	const long m = (s % 3600) / 60;
	char buf[32];
	snprintf(buf, sizeof(buf), "%ldh %02ldm", h, m);
	return std::string(buf);
}

std::string CreditManager::formatPlayerCredit(const std::string& name) const
{
	return formatTimeUnlocked(getPlayerRemainingSeconds(name));
}

std::string CreditManager::formatDuration(long totalSec)
{
	return formatTimeUnlocked(totalSec);
}

long CreditManager::getPlayerMinutesPurchased(const std::string& name) const
{
	std::lock_guard<std::mutex> lock(mMutex);
	const std::string n = Utils::String::toLower(sanitizePlayerName(name));
	for (const auto& p : mPlayers)
	{
		if (!p.archived && Utils::String::toLower(p.name) == n)
			return p.totalMinutesPurchased;
	}
	return 0;
}

bool CreditManager::clearPlayerCredit(const std::string& name)
{
	const std::string n = sanitizePlayerName(name);
	if (n.empty())
		return false;
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	for (auto& p : mPlayers)
	{
		if (!p.archived && Utils::String::toLower(p.name) == Utils::String::toLower(n))
		{
			p.remainingSeconds = 0;
			if (Utils::String::toLower(mCurrentPlayer) == Utils::String::toLower(n))
			{
				mRemainingSeconds = 0;
				mSessionRunning = false;
				mSessionPaused = false;
				mTickAccumMs = 0;
			}
			return persistPlayersUnlocked();
		}
	}
	return false;
}

bool CreditManager::clearActivePlayerCredit()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	if (mCurrentPlayer.empty())
	{
		if (!canRotateGuestWalletUnlocked(false)) return false;
		if (!rotateGuestWalletUnlocked(false)) return false;
		mSessionRunning = false;
		mSessionPaused = false;
		if (!persistCreditUnlocked())
		{
			blockCreditOperationsAndPersistenceUnlocked("falha ao limpar credito convidado");
			return false;
		}
		if (!persistPlayersMirrorUnlocked())
			LOG(LogWarning) << "[CreditManager] credito gravado; espelho de jogadores indisponivel";
		return true;
	}
	for (auto& p : mPlayers)
	{
		if (Utils::String::toLower(p.name) == Utils::String::toLower(mCurrentPlayer))
		{
			p.remainingSeconds = 0;
			break;
		}
	}
	mRemainingSeconds = 0;
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	return persistPlayersUnlocked();
}

bool CreditManager::clearAllPlayersCredit()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	for (auto& p : mPlayers)
		if (!p.archived) p.remainingSeconds = 0;
	if (!mCurrentPlayer.empty()) mRemainingSeconds = 0;
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	return persistPlayersUnlocked();
}

bool CreditManager::clearPlayerPlayHistory(const std::string& name)
{
	const std::string n = sanitizePlayerName(name);
	if (n.empty())
		return false;
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	for (auto& p : mPlayers)
	{
		if (!p.archived && Utils::String::toLower(p.name) == Utils::String::toLower(n))
		{
			p.totalPlayedSeconds = 0;
			return persistPlayersUnlocked();
		}
	}
	return false;
}

bool CreditManager::setPlayerRemainingMinutes(const std::string& name, int minutes)
{
	const std::string n = sanitizePlayerName(name);
	if (n.empty())
		return false;
	if (minutes < 0)
		minutes = 0;
	if (minutes > 480 * 7) // 56h max set
		minutes = 480 * 7;

	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	if (mMaxRemainingSeconds < 3600)
		mMaxRemainingSeconds = 28800;
	const long sec = std::min((long)minutes * 60L, mMaxRemainingSeconds);

	for (auto& p : mPlayers)
	{
		if (!p.archived && Utils::String::toLower(p.name) == Utils::String::toLower(n))
		{
			p.remainingSeconds = sec;
			if (Utils::String::toLower(mCurrentPlayer) == Utils::String::toLower(n))
				mRemainingSeconds = sec;
			mSessionRunning = false;
			mSessionPaused = false;
			return persistPlayersUnlocked();
		}
	}
	return false;
}

bool CreditManager::removeAllPlayers()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	size_t archivedCount = 0;
	size_t activeCount = 0;
	for (const auto& player : mPlayers)
		if (player.archived) ++archivedCount; else ++activeCount;
	if (archivedCount + activeCount > kMaxWalletTombstones)
		return false;
	syncActivePlayerWalletUnlocked();
	const long now = (long)std::time(nullptr);
	for (auto& player : mPlayers)
	{
		player.archived = true;
		player.tombstonedAtUnixSeconds = now;
	}
	mCurrentPlayer.clear();
	// Removing registered accounts is not an instruction to discard or rotate
	// the unrelated guest wallet. Keep its id and balance available.
	mRemainingSeconds = mGuestRemainingSeconds;
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	return persistPlayersUnlocked();
}

CreditAccountingTotals CreditManager::getAccountingTotals() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	CreditAccountingTotals t;
	t.totalCoinsAccepted = mTotalCoinsAccepted;
	t.totalMinutesSold = mTotalMinutesSold;
	t.totalSecondsPlayed = mTotalSecondsPlayed;
	t.priceCentsPerMinute = mPriceCentsPerMinute;
	t.playerCount = 0;
	for (const auto& player : mPlayers) if (!player.archived) t.playerCount++;
	long rem = mGuestRemainingSeconds;
	for (const auto& p : mPlayers)
	{
		if (rem > LONG_MAX - p.remainingSeconds)
		{
			rem = LONG_MAX;
			break;
		}
		rem += p.remainingSeconds;
	}
	for (const auto& retired : mRetiredGuestWallets)
	{
		if (rem > LONG_MAX - retired.remainingSeconds)
		{
			rem = LONG_MAX;
			break;
		}
		rem += retired.remainingSeconds;
	}
	t.totalRemainingSeconds = rem;
	if (mPriceCentsPerMinute > 0 && mTotalMinutesSold > 0)
	{
		if (mTotalMinutesSold > LONG_MAX / mPriceCentsPerMinute)
			t.estimatedRevenueCents = LONG_MAX;
		else
			t.estimatedRevenueCents = mTotalMinutesSold * mPriceCentsPerMinute;
	}
	return t;
}

std::string CreditManager::formatMoneyCents(long cents) const
{
	if (cents <= 0)
		return "R$ 0,00";
	const long reais = cents / 100;
	const long c = cents % 100;
	char buf[48];
	snprintf(buf, sizeof(buf), "R$ %ld,%02ld", reais, c);
	return std::string(buf);
}

long CreditManager::getPriceCentsPerMinute() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return mPriceCentsPerMinute;
}

bool CreditManager::setPriceCentsPerMinute(long cents)
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return false;
	if (cents < 0)
		cents = 0;
	if (cents > 100000)
		cents = 100000;
	const long previous = mPriceCentsPerMinute;
	mPriceCentsPerMinute = cents;
	if (!persistConfigUnlocked())
	{
		mPriceCentsPerMinute = previous;
		return false;
	}
	return true;
}

void CreditManager::resetMachineAccounting()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return;
	mTotalCoinsAccepted = 0;
	mTotalMinutesSold = 0;
	mTotalSecondsPlayed = 0;
	if (!persistCreditUnlocked())
		blockCreditOperationsAndPersistenceUnlocked("falha ao zerar contabilidade");
}

void CreditManager::resetPlayersPurchaseHistory()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCreditPersistenceBlocked) return;
	for (auto& p : mPlayers)
	{
		p.totalMinutesPurchased = 0;
		p.totalPlayedSeconds = 0;
	}
	persistPlayersUnlocked();
}

std::string CreditManager::formatRemaining() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return formatTimeUnlocked(mRemainingSeconds);
}

std::string CreditManager::formatHudLine() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (!mShowHud)
		return std::string();

	std::string line;
	if (!mCurrentPlayer.empty())
		line = mCurrentPlayer + " ";
	else if (mRemainingSeconds > 0)
		line = "Avulso ";
	else
		line = "— ";

	const std::string t = formatTimeUnlocked(mRemainingSeconds);
	if (mInGame)
		line += t;
	else if (!mSessionRunning)
		line += t; // parado
	else if (mSessionPaused)
		line += std::string("|| ") + t;
	else
		line += t;
	return line;
}

bool CreditManager::verifyAdminPassword(const std::string& password) const
{
	std::lock_guard<std::mutex> lock(mMutex);
	const std::string pw = Utils::String::trim(password);
	if (pw.empty())
		return false;
	const bool verified = verifyPasswordHash(pw, mAdminPasswordHash);
	if (verified && isLegacyPasswordHash(mAdminPasswordHash))
	{
		const std::string upgraded = createPasswordHash(pw);
		if (!isLegacyPasswordHash(upgraded))
		{
			CreditManager* self = const_cast<CreditManager*>(this);
			const std::string legacy = self->mAdminPasswordHash;
			self->mAdminPasswordHash = upgraded;
			if (self->persistConfigUnlocked())
				LOG(LogInfo) << "[CreditManager] hash admin legado migrado para PBKDF2-SHA256";
			else
			{
				self->mAdminPasswordHash = legacy;
				LOG(LogError) << "[CreditManager] migracao PBKDF2 revertida: falha de persistencia";
			}
		}
		else
			LOG(LogError) << "[CreditManager] BCrypt indisponivel; hash admin legado nao foi migrado";
	}
	return verified;
}

bool CreditManager::setAdminPassword(const std::string& password)
{
	const std::string pw = Utils::String::trim(password);
	if ((int)pw.size() < kMinPasswordLen)
		return false;
	const std::string encoded = createPasswordHash(pw);
	if (isLegacyPasswordHash(encoded))
	{
		LOG(LogError) << "[CreditManager] nova senha rejeitada: PBKDF2 indisponivel";
		return false;
	}
	std::lock_guard<std::mutex> lock(mMutex);
	const std::string previous = mAdminPasswordHash;
	mAdminPasswordHash = encoded;
	if (!persistConfigUnlocked())
	{
		mAdminPasswordHash = previous;
		return false;
	}
	return true;
}

bool CreditManager::isUsingDefaultAdminPassword() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return verifyPasswordHash("admin", mAdminPasswordHash);
}
