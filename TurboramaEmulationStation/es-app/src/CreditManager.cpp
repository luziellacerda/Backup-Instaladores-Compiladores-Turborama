#include "CreditManager.h"

#include "Log.h"
#include "Paths.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"
#include "utils/md5.h"

#include <algorithm>
#include <chrono>
#include <climits>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <locale>
#include <sstream>

#ifdef _WIN32
#include <io.h>
#include <windows.h>
#endif

namespace
{
	long long nowMs()
	{
		using namespace std::chrono;
		return duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count();
	}

	FILE* openWriteBinary(const std::string& path)
	{
#if defined(_WIN32)
		return _wfopen(Utils::String::convertToWideString(path).c_str(), L"wb");
#else
		return fopen(path.c_str(), "wb");
#endif
	}

	// CRITICAL: never use global locale for numbers (pt-BR writes "28,800" / "28.800")
	std::ostringstream makePlainOut()
	{
		std::ostringstream out;
		out.imbue(std::locale::classic());
		return out;
	}
}

CreditManager& CreditManager::getInstance()
{
	static CreditManager instance;
	return instance;
}

CreditManager::CreditManager()
	: mEnabled(true)
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
	, mLastCoinTickMs(-1)
	, mSessionRunning(false)
	, mSessionPaused(false)
	, mInGame(false)
	, mGameWasCounting(false)
	, mTickAccumMs(0)
	, mSaveAccumMs(0)
	, mAdminPasswordHash(defaultAdminPasswordHash())
	, mLowTimeWarnStage(0)
{
	load();
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

std::string CreditManager::hashPassword(const std::string& password)
{
	return MD5(password).hexdigest();
}

std::string CreditManager::defaultAdminPasswordHash()
{
	return hashPassword("admin");
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

long CreditManager::parseDigitsLong(const std::string& val)
{
	std::string digits;
	for (char c : val)
	{
		if (c >= '0' && c <= '9')
			digits.push_back(c);
	}
	if (digits.empty())
		return 0L;
	if (digits.size() > 9)
		digits = digits.substr(digits.size() - 9);
	return (long)Utils::String::toInteger(digits);
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
	const std::string dir = Utils::FileSystem::getParent(path);
	if (!dir.empty())
		Utils::FileSystem::createDirectory(dir);

	const std::string tmp = path + ".tmp";
	FILE* f = openWriteBinary(tmp);
	if (!f)
	{
		LOG(LogError) << "[CreditManager] write open failed: " << tmp;
		// last resort direct write
		Utils::FileSystem::writeAllText(path, content);
		return Utils::FileSystem::exists(path);
	}
	const size_t n = content.size();
	const size_t w = fwrite(content.data(), 1, n, f);
	fflush(f);
#ifdef _WIN32
	const int fd = _fileno(f);
	if (fd >= 0)
		_commit(fd);
#endif
	fclose(f);
	if (w != n)
	{
		Utils::FileSystem::removeFile(tmp);
		Utils::FileSystem::writeAllText(path, content);
		return Utils::FileSystem::exists(path);
	}

#if defined(_WIN32)
	// Replace destination atomically when possible (no delete-first race)
	const std::wstring wTmp = Utils::String::convertToWideString(tmp);
	const std::wstring wPath = Utils::String::convertToWideString(path);
	if (MoveFileExW(wTmp.c_str(), wPath.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
		return true;
	// Fallback: remove + move
	DeleteFileW(wPath.c_str());
	if (MoveFileW(wTmp.c_str(), wPath.c_str()))
		return true;
#else
	if (Utils::FileSystem::renameFile(tmp, path, true))
		return true;
#endif

	// Ultimate fallback: overwrite in place
	Utils::FileSystem::writeAllText(path, content);
	Utils::FileSystem::removeFile(tmp);
	const bool ok = Utils::FileSystem::exists(path);
	if (!ok)
		LOG(LogError) << "[CreditManager] all write strategies failed: " << path;
	return ok;
}

void CreditManager::syncActivePlayerWalletUnlocked()
{
	if (mCurrentPlayer.empty())
		return;
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
		return false;
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

void CreditManager::persistConfigUnlocked() const
{
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
	atomicWriteText(configFilePath(), out.str());
}

void CreditManager::loadConfig()
{
	const std::string path = configFilePath();
	if (!Utils::FileSystem::exists(path))
	{
		mAdminPasswordHash = defaultAdminPasswordHash();
		persistConfigUnlocked();
		return;
	}

	std::ifstream in(path);
	if (!in.is_open())
		return;

	bool sawHash = false;
	std::string legacyPlain;
	std::string line;
	bool first = true;
	while (std::getline(in, line))
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
			const long v = parseDigitsLong(val);
			// Se leu lixo (0) ou valor absurdo, usa 30 minutos por moeda (padrao locadora)
			if (v < 1 || v > 60)
				mMinutesPerCoin = 30;
			else
				mMinutesPerCoin = (int)v;
		}
		else if (key == "debouncems")
		{
			const long v = parseDigitsLong(val);
			mDebounceMs = (int)std::max(100L, std::min(5000L, v > 0 ? v : 350L));
		}
		else if (key == "maxremainingseconds")
		{
			// BUG CRITICO: locale pt-BR gravava "28,800" e toInteger lia 28
			// → teto de ~1 minuto. Nunca mais aceitar teto < 1 hora.
			const long v = parseDigitsLong(val);
			if (v < 3600L)
				mMaxRemainingSeconds = 28800L; // 8 horas padrao
			else
				mMaxRemainingSeconds = std::min(7L * 24 * 3600, v);
		}
		else if (key == "pricecentsperminute")
		{
			// 0 = sem preco em R$; 100 = R$ 1,00 por minuto
			mPriceCentsPerMinute = std::max(0L, std::min(100000L, parseDigitsLong(val)));
		}
		else if (key == "adminpasswordhash" && val.size() == 32)
		{
			mAdminPasswordHash = Utils::String::toLower(val);
			sawHash = true;
		}
		else if (key == "adminpassword")
			legacyPlain = val;
	}

	if (!sawHash)
	{
		mAdminPasswordHash = legacyPlain.empty() ? defaultAdminPasswordHash() : hashPassword(legacyPlain);
	}
	// Sempre regrava cfg em locale C (corrige maxRemainingSeconds=28,800 corrompido)
	persistConfigUnlocked();

	LOG(LogInfo) << "[CreditManager] cfg maxRemainingSeconds=" << mMaxRemainingSeconds
		<< " minutesPerCoin=" << mMinutesPerCoin;
}

void CreditManager::loadPlayers()
{
	mPlayers.clear();
	mCurrentPlayer.clear();
	const std::string path = playersFilePath();
	if (!Utils::FileSystem::exists(path))
		return;

	std::ifstream in(path);
	if (!in.is_open())
		return;

	std::string line;
	while (std::getline(in, line))
	{
		line = Utils::String::trim(line);
		if (line.empty() || line[0] == '#')
			continue;
		auto pos = line.find('=');
		if (pos == std::string::npos)
			continue;
		std::string key = Utils::String::toLower(Utils::String::trim(line.substr(0, pos)));
		std::string val = Utils::String::trim(line.substr(pos + 1));

		if (key == "currentplayer")
		{
			mCurrentPlayer = sanitizePlayerName(val);
			continue;
		}
		if (key != "player")
			continue;

		// player=Name;playedSeconds=N;remainingSeconds=M
		CreditPlayer p;
		std::string rest = val;
		auto sc = rest.find(';');
		std::string namePart = (sc == std::string::npos) ? rest : rest.substr(0, sc);
		p.name = sanitizePlayerName(namePart);
		if (p.name.empty())
			continue;
		if (sc != std::string::npos)
			rest = rest.substr(sc + 1);
		else
			rest.clear();

		while (!rest.empty())
		{
			auto sc2 = rest.find(';');
			std::string part = (sc2 == std::string::npos) ? rest : rest.substr(0, sc2);
			auto eq = part.find('=');
			if (eq != std::string::npos)
			{
				std::string k2 = Utils::String::toLower(Utils::String::trim(part.substr(0, eq)));
				std::string v2 = Utils::String::trim(part.substr(eq + 1));
				if (k2 == "playedseconds")
					p.totalPlayedSeconds = std::max(0L, parseDigitsLong(v2));
				else if (k2 == "remainingseconds")
					p.remainingSeconds = std::max(0L, parseDigitsLong(v2));
				else if (k2 == "totalminutespurchased" || k2 == "minutespurchased")
					p.totalMinutesPurchased = std::max(0L, parseDigitsLong(v2));
			}
			if (sc2 == std::string::npos)
				break;
			rest = rest.substr(sc2 + 1);
		}

		bool exists = false;
		for (auto& e : mPlayers)
		{
			if (Utils::String::toLower(e.name) == Utils::String::toLower(p.name))
			{
				e.totalPlayedSeconds = std::max(e.totalPlayedSeconds, p.totalPlayedSeconds);
				e.remainingSeconds = std::max(e.remainingSeconds, p.remainingSeconds);
				e.totalMinutesPurchased = std::max(e.totalMinutesPurchased, p.totalMinutesPurchased);
				exists = true;
				break;
			}
		}
		if (!exists && (int)mPlayers.size() < kMaxPlayers)
			mPlayers.push_back(p);
	}

	if (!mCurrentPlayer.empty())
	{
		bool found = false;
		for (const auto& e : mPlayers)
		{
			if (Utils::String::toLower(e.name) == Utils::String::toLower(mCurrentPlayer))
			{
				mCurrentPlayer = e.name;
				found = true;
				break;
			}
		}
		if (!found)
			mCurrentPlayer.clear();
	}
}

void CreditManager::load()
{
	std::lock_guard<std::mutex> lock(mMutex);
	loadConfig();
	loadPlayers();

	mRemainingSeconds = 0;
	mTotalCoinsAccepted = 0;
	mTotalMinutesSold = 0;
	mTotalSecondsPlayed = 0;
	mSessionRunning = false;
	mSessionPaused = false;
	mInGame = false;
	mGameWasCounting = false;
	mTickAccumMs = 0;
	mSaveAccumMs = 0;

	const std::string path = creditFilePath();
	long machineRemaining = 0;
	if (Utils::FileSystem::exists(path))
	{
		std::ifstream in(path);
		std::string line;
		while (std::getline(in, line))
		{
			line = Utils::String::trim(line);
			auto pos = line.find('=');
			if (pos == std::string::npos)
				continue;
			std::string key = Utils::String::toLower(Utils::String::trim(line.substr(0, pos)));
			std::string val = Utils::String::trim(line.substr(pos + 1));
			if (key == "remainingseconds")
				machineRemaining = parseDigitsLong(val);
			else if (key == "totalcoinsaccepted")
				mTotalCoinsAccepted = parseDigitsLong(val);
			else if (key == "totalminutessold")
				mTotalMinutesSold = parseDigitsLong(val);
			else if (key == "totalsecondsplayed")
				mTotalSecondsPlayed = parseDigitsLong(val);
		}
	}

	// Carrega carteira do jogador ativo; se legado sem remaining por jogador,
	// migra saldo da máquina para o jogador atual uma vez.
	if (!mCurrentPlayer.empty() && loadActivePlayerWalletUnlocked())
	{
		bool anyPlayerHasWallet = false;
		for (const auto& p : mPlayers)
		{
			if (p.remainingSeconds > 0)
			{
				anyPlayerHasWallet = true;
				break;
			}
		}
		if (!anyPlayerHasWallet && machineRemaining > 0)
		{
			mRemainingSeconds = machineRemaining;
			syncActivePlayerWalletUnlocked();
			persistPlayersUnlocked();
			LOG(LogInfo) << "[CreditManager] migrou saldo da maquina para jogador " << mCurrentPlayer;
		}
	}
	else
	{
		// Sem jogador: saldo de máquina (modo convidado)
		mRemainingSeconds = machineRemaining;
	}

	clamp();
	LOG(LogInfo) << "[CreditManager] locadora loaded players=" << mPlayers.size()
		<< " current=" << mCurrentPlayer
		<< " remaining=" << mRemainingSeconds;
}

void CreditManager::persistCreditUnlocked() const
{
	// Guarda total de moedas + snapshot do saldo ativo (backup)
	// SEMPRE locale C — nunca "1.234" / "1,234"
	auto out = makePlainOut();
	out << "schemaVersion=" << kSchemaVersion << "\n"
		<< "remainingSeconds=" << mRemainingSeconds << "\n"
		<< "totalCoinsAccepted=" << mTotalCoinsAccepted << "\n"
		<< "totalMinutesSold=" << mTotalMinutesSold << "\n"
		<< "totalSecondsPlayed=" << mTotalSecondsPlayed << "\n"
		<< "currentPlayer=" << mCurrentPlayer << "\n";
	atomicWriteText(creditFilePath(), out.str());
}

void CreditManager::persistPlayersUnlocked() const
{
	auto out = makePlainOut();
	out << "# TurboRama Locadora - jogadores\n"
		<< "schemaVersion=" << kSchemaVersion << "\n"
		<< "currentPlayer=" << mCurrentPlayer << "\n";
	for (const auto& p : mPlayers)
	{
		out << "player=" << p.name
			<< ";playedSeconds=" << p.totalPlayedSeconds
			<< ";remainingSeconds=" << p.remainingSeconds
			<< ";totalMinutesPurchased=" << p.totalMinutesPurchased
			<< "\n";
	}
	atomicWriteText(playersFilePath(), out.str());
}

void CreditManager::save() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	const_cast<CreditManager*>(this)->syncActivePlayerWalletUnlocked();
	persistCreditUnlocked();
	persistPlayersUnlocked();
}

void CreditManager::savePlayers() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	const_cast<CreditManager*>(this)->syncActivePlayerWalletUnlocked();
	persistPlayersUnlocked();
}

void CreditManager::flushNow()
{
	std::lock_guard<std::mutex> lock(mMutex);
	syncActivePlayerWalletUnlocked();
	persistCreditUnlocked();
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
	if (mRemainingSeconds > mMaxRemainingSeconds) mRemainingSeconds = mMaxRemainingSeconds;
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

	// Com mais de 2 min: limpa estagio (pode avisar de novo se o tempo baixar outra vez)
	if (r > 120)
	{
		mLowTimeWarnStage = 0;
		return;
	}

	// Ordem do mais urgente para o menos — se o tempo "pular" varios limiares
	// (ex.: saiu do jogo), mostra o aviso mais critico ainda nao exibido.
	if (r <= 0 && mLowTimeWarnStage < 5)
	{
		mPendingLowTimeWarning = "TEMPO ESGOTADO! Insira credito / chame o balcao.";
		mLowTimeWarnStage = 5;
	}
	else if (r <= 10 && mLowTimeWarnStage < 4)
	{
		mPendingLowTimeWarning = "ATENCAO: restam apenas 10 SEGUNDOS!";
		mLowTimeWarnStage = 4;
	}
	else if (r <= 30 && mLowTimeWarnStage < 3)
	{
		mPendingLowTimeWarning = "ATENCAO: restam 30 segundos de credito!";
		mLowTimeWarnStage = 3;
	}
	else if (r <= 60 && mLowTimeWarnStage < 2)
	{
		mPendingLowTimeWarning = "ATENCAO: resta 1 MINUTO de credito!";
		mLowTimeWarnStage = 2;
	}
	else if (r <= 120 && mLowTimeWarnStage < 1)
	{
		mPendingLowTimeWarning = "ATENCAO: restam 2 MINUTOS de credito!";
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
	mEnabled = enabled;
	persistConfigUnlocked();
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
	if (!mEnabled) return true;
	if (!mBlockWithoutCredit) return true;
	return mRemainingSeconds > 0;
}

bool CreditManager::addCoin()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (!mEnabled)
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
	long sum = mRemainingSeconds + add;
	if (sum < mRemainingSeconds) // overflow
		sum = mMaxRemainingSeconds;
	mRemainingSeconds = std::min(sum, mMaxRemainingSeconds);
	if (mTotalCoinsAccepted < LONG_MAX)
		mTotalCoinsAccepted++;
	recordSaleUnlocked(mMinutesPerCoin);

	// Moeda inicia sessão do jogador ativo
	mSessionRunning = true;
	mSessionPaused = false;
	syncActivePlayerWalletUnlocked();

	LOG(LogInfo) << "[CreditManager] MOEDA +" << mMinutesPerCoin
		<< "min (" << add << "s) before=" << before
		<< " after=" << mRemainingSeconds
		<< " max=" << mMaxRemainingSeconds
		<< " player=" << mCurrentPlayer;

	// Credito subiu: rearmar avisos se passou de 2 min
	if (mRemainingSeconds > 120)
		resetLowTimeWarningsUnlocked();
	else
		updateLowTimeWarningsUnlocked();

	persistCreditUnlocked();
	persistPlayersUnlocked();
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

bool CreditManager::addMinutes(int minutes)
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (!mEnabled)
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
	long sum = mRemainingSeconds + add;
	if (sum < mRemainingSeconds)
		sum = mMaxRemainingSeconds;
	mRemainingSeconds = std::min(sum, mMaxRemainingSeconds);
	recordSaleUnlocked(minutes);

	mSessionRunning = true;
	mSessionPaused = false;
	syncActivePlayerWalletUnlocked();

	LOG(LogInfo) << "[CreditManager] ADD +" << minutes
		<< "min before=" << before << " after=" << mRemainingSeconds
		<< " max=" << mMaxRemainingSeconds << " player=" << mCurrentPlayer;

	if (mRemainingSeconds > 120)
		resetLowTimeWarningsUnlocked();
	else
		updateLowTimeWarningsUnlocked();

	persistCreditUnlocked();
	persistPlayersUnlocked();
	return true;
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

void CreditManager::applyConsumeUnlocked(long seconds, const char* reason)
{
	if (seconds <= 0)
		return;
	if (seconds > 24 * 3600)
		seconds = 24 * 3600;

	const long before = mRemainingSeconds;
	mRemainingSeconds = std::max(0L, mRemainingSeconds - seconds);
	addPlayedToCurrentUnlocked(seconds);
	if (mTotalSecondsPlayed <= LONG_MAX - seconds)
		mTotalSecondsPlayed += seconds;
	else
		mTotalSecondsPlayed = LONG_MAX;
	syncActivePlayerWalletUnlocked();
	clamp();

	if (mRemainingSeconds == 0)
	{
		mSessionRunning = false;
		mSessionPaused = false;
	}

	LOG(LogInfo) << "[CreditManager] " << (reason ? reason : "consume")
		<< " " << seconds << "s before=" << before << " after=" << mRemainingSeconds
		<< " player=" << mCurrentPlayer;

	updateLowTimeWarningsUnlocked();

	persistCreditUnlocked();
	persistPlayersUnlocked();
}

void CreditManager::tick(int deltaMs)
{
	if (deltaMs <= 0)
		return;
	if (deltaMs > kMaxTickDeltaMs)
		deltaMs = kMaxTickDeltaMs;

	std::lock_guard<std::mutex> lock(mMutex);
	if (!mEnabled || mInGame || !mSessionRunning || mSessionPaused || mRemainingSeconds <= 0)
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
		persistCreditUnlocked();
		persistPlayersUnlocked();
	}
}

void CreditManager::beginGameSession()
{
	std::lock_guard<std::mutex> lock(mMutex);
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

void CreditManager::endGameSession(long elapsedSeconds)
{
	std::lock_guard<std::mutex> lock(mMutex);
	const bool wasCounting = mGameWasCounting;
	mInGame = false;
	mGameWasCounting = false;
	if (!mEnabled || !wasCounting)
		return;
	if (elapsedSeconds < 0) elapsedSeconds = 0;
	if (elapsedSeconds > 12 * 3600) elapsedSeconds = 12 * 3600;
	applyConsumeUnlocked(elapsedSeconds, "jogo");
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
	persistCreditUnlocked();
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
	persistCreditUnlocked();
	persistPlayersUnlocked();
	LOG(LogInfo) << "[CreditManager] contador PARADO jogador=" << mCurrentPlayer
		<< " saldo=" << mRemainingSeconds;
}

void CreditManager::endActivePlayerTurn()
{
	std::lock_guard<std::mutex> lock(mMutex);
	// Para contador e grava saldo na conta cadastrada; desmarca jogador ativo
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	syncActivePlayerWalletUnlocked();
	const std::string was = mCurrentPlayer;
	mCurrentPlayer.clear();
	// Maquina livre: saldo ativo zera (conta cadastrada ja salvou o remaining dela)
	mRemainingSeconds = 0;
	persistCreditUnlocked();
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
	// Cliente avulso saiu: fecha o tempo sem cadastrar
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	mRemainingSeconds = 0;
	// nao mexe em mCurrentPlayer (ja esta vazio) nem nas contas cadastradas
	resetLowTimeWarningsUnlocked();
	persistCreditUnlocked();
	LOG(LogInfo) << "[CreditManager] credito AVULSO fechado/zerado";
}

bool CreditManager::switchToPlayer(const std::string& name)
{
	const std::string n = sanitizePlayerName(name);
	if (n.empty())
		return false;

	std::lock_guard<std::mutex> lock(mMutex);

	// 1) Para contador do atual e salva carteira
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	syncActivePlayerWalletUnlocked();

	// 2) Seleciona novo
	bool found = false;
	for (const auto& p : mPlayers)
	{
		if (Utils::String::toLower(p.name) == Utils::String::toLower(n))
		{
			mCurrentPlayer = p.name;
			found = true;
			break;
		}
	}
	if (!found)
		return false;

	// 3) Carrega saldo do novo (fica PARADO até moeda/continuar)
	loadActivePlayerWalletUnlocked();
	persistCreditUnlocked();
	persistPlayersUnlocked();

	// Novo jogador: rearmar avisos conforme o saldo dele
	if (mRemainingSeconds > 120)
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
	return mPlayers;
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

	// Se já existe, só troca para ele (sem apagar saldo)
	for (auto& p : mPlayers)
	{
		if (Utils::String::toLower(p.name) == Utils::String::toLower(n))
		{
			// save current first
			mSessionRunning = false;
			mSessionPaused = false;
			mTickAccumMs = 0;
			syncActivePlayerWalletUnlocked();
			mCurrentPlayer = p.name;
			loadActivePlayerWalletUnlocked();
			persistCreditUnlocked();
			persistPlayersUnlocked();
			return true;
		}
	}

	if ((int)mPlayers.size() >= kMaxPlayers)
		return false;

	// Antes de criar, salva o atual
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	syncActivePlayerWalletUnlocked();

	CreditPlayer p;
	p.name = n;
	p.totalPlayedSeconds = 0;
	p.remainingSeconds = 0;
	mPlayers.push_back(p);
	mCurrentPlayer = n;
	mRemainingSeconds = 0;
	persistCreditUnlocked();
	persistPlayersUnlocked();
	LOG(LogInfo) << "[CreditManager] jogador cadastrado=" << n;
	return true;
}

bool CreditManager::removePlayer(const std::string& name)
{
	const std::string n = sanitizePlayerName(name);
	if (n.empty())
		return false;

	std::lock_guard<std::mutex> lock(mMutex);
	for (auto it = mPlayers.begin(); it != mPlayers.end(); ++it)
	{
		if (Utils::String::toLower(it->name) == Utils::String::toLower(n))
		{
			if (Utils::String::toLower(mCurrentPlayer) == Utils::String::toLower(n))
			{
				mCurrentPlayer.clear();
				mRemainingSeconds = 0;
				mSessionRunning = false;
				mSessionPaused = false;
			}
			mPlayers.erase(it);
			persistCreditUnlocked();
			persistPlayersUnlocked();
			return true;
		}
	}
	return false;
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
		if (Utils::String::toLower(p.name) == n)
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
		if (Utils::String::toLower(p.name) == Utils::String::toLower(name))
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
		if (Utils::String::toLower(p.name) == n)
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
	for (auto& p : mPlayers)
	{
		if (Utils::String::toLower(p.name) == Utils::String::toLower(n))
		{
			p.remainingSeconds = 0;
			if (Utils::String::toLower(mCurrentPlayer) == Utils::String::toLower(n))
			{
				mRemainingSeconds = 0;
				mSessionRunning = false;
				mSessionPaused = false;
				mTickAccumMs = 0;
			}
			persistCreditUnlocked();
			persistPlayersUnlocked();
			return true;
		}
	}
	return false;
}

bool CreditManager::clearActivePlayerCredit()
{
	std::lock_guard<std::mutex> lock(mMutex);
	if (mCurrentPlayer.empty())
	{
		mRemainingSeconds = 0;
		mSessionRunning = false;
		mSessionPaused = false;
		persistCreditUnlocked();
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
	persistCreditUnlocked();
	persistPlayersUnlocked();
	return true;
}

bool CreditManager::clearAllPlayersCredit()
{
	std::lock_guard<std::mutex> lock(mMutex);
	for (auto& p : mPlayers)
		p.remainingSeconds = 0;
	mRemainingSeconds = 0;
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	persistCreditUnlocked();
	persistPlayersUnlocked();
	return true;
}

bool CreditManager::clearPlayerPlayHistory(const std::string& name)
{
	const std::string n = sanitizePlayerName(name);
	if (n.empty())
		return false;
	std::lock_guard<std::mutex> lock(mMutex);
	for (auto& p : mPlayers)
	{
		if (Utils::String::toLower(p.name) == Utils::String::toLower(n))
		{
			p.totalPlayedSeconds = 0;
			persistPlayersUnlocked();
			return true;
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
	if (mMaxRemainingSeconds < 3600)
		mMaxRemainingSeconds = 28800;
	const long sec = std::min((long)minutes * 60L, mMaxRemainingSeconds);

	for (auto& p : mPlayers)
	{
		if (Utils::String::toLower(p.name) == Utils::String::toLower(n))
		{
			p.remainingSeconds = sec;
			if (Utils::String::toLower(mCurrentPlayer) == Utils::String::toLower(n))
				mRemainingSeconds = sec;
			mSessionRunning = false;
			mSessionPaused = false;
			persistCreditUnlocked();
			persistPlayersUnlocked();
			return true;
		}
	}
	return false;
}

bool CreditManager::removeAllPlayers()
{
	std::lock_guard<std::mutex> lock(mMutex);
	mPlayers.clear();
	mCurrentPlayer.clear();
	mRemainingSeconds = 0;
	mSessionRunning = false;
	mSessionPaused = false;
	mTickAccumMs = 0;
	persistCreditUnlocked();
	persistPlayersUnlocked();
	return true;
}

CreditAccountingTotals CreditManager::getAccountingTotals() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	CreditAccountingTotals t;
	t.totalCoinsAccepted = mTotalCoinsAccepted;
	t.totalMinutesSold = mTotalMinutesSold;
	t.totalSecondsPlayed = mTotalSecondsPlayed;
	t.priceCentsPerMinute = mPriceCentsPerMinute;
	t.playerCount = (int)mPlayers.size();
	long rem = 0;
	for (const auto& p : mPlayers)
		rem += p.remainingSeconds;
	// se ha jogador ativo, saldo dele ja esta em p.remaining (sync)
	// se nao ha players mas ha mRemaining, conta
	if (mPlayers.empty())
		rem = mRemainingSeconds;
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
	if (cents < 0)
		cents = 0;
	if (cents > 100000)
		cents = 100000;
	mPriceCentsPerMinute = cents;
	persistConfigUnlocked();
	return true;
}

void CreditManager::resetMachineAccounting()
{
	std::lock_guard<std::mutex> lock(mMutex);
	mTotalCoinsAccepted = 0;
	mTotalMinutesSold = 0;
	mTotalSecondsPlayed = 0;
	persistCreditUnlocked();
}

void CreditManager::resetPlayersPurchaseHistory()
{
	std::lock_guard<std::mutex> lock(mMutex);
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
	return constantTimeEqual(hashPassword(pw), mAdminPasswordHash);
}

bool CreditManager::setAdminPassword(const std::string& password)
{
	const std::string pw = Utils::String::trim(password);
	if ((int)pw.size() < kMinPasswordLen)
		return false;
	std::lock_guard<std::mutex> lock(mMutex);
	mAdminPasswordHash = hashPassword(pw);
	persistConfigUnlocked();
	return true;
}

bool CreditManager::isUsingDefaultAdminPassword() const
{
	std::lock_guard<std::mutex> lock(mMutex);
	return constantTimeEqual(mAdminPasswordHash, defaultAdminPasswordHash());
}
