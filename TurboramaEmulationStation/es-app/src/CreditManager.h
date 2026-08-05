#pragma once

#include <mutex>
#include <string>
#include <vector>

// TurboRama Locadora — credito multi-jogador + contabilidade
// Arquivos em .emulationstation:
//   arcade_credit.cfg / .dat / arcade_players.dat

struct CreditPlayer
{
	std::string id;                    // identificador opaco e estavel da carteira
	std::string name;
	long totalPlayedSeconds = 0;     // tempo consumido (historico)
	long remainingSeconds = 0;       // saldo atual
	long totalMinutesPurchased = 0;  // minutos vendidos a este cliente (historico)
	bool archived = false;            // tombstone para aprovacoes PIX tardias
	long tombstonedAtUnixSeconds = 0;
};

struct RetiredGuestWallet
{
	std::string id;
	long remainingSeconds = 0;
	long retiredAtUnixSeconds = 0;
};

// Totais da maquina (contabilidade)
struct CreditAccountingTotals
{
	long totalCoinsAccepted = 0;
	long totalMinutesSold = 0;       // todos os minutos adicionados (vendas)
	long totalSecondsPlayed = 0;     // tempo consumido na maquina
	long totalRemainingSeconds = 0;  // soma dos saldos de todos os clientes
	int playerCount = 0;
	long priceCentsPerMinute = 0;    // 0 = sem preco; 100 = R$ 1,00 / min
	// receita estimada (centavos) = totalMinutesSold * priceCentsPerMinute
	long estimatedRevenueCents = 0;
};

enum class PixCreditResult { Applied, AlreadyApplied, Rejected };

class CreditManager
{
public:
	static CreditManager& getInstance();

	void load();
	void save() const;
	void savePlayers() const;
	void flushNow();

	bool isEnabled() const;
	void setEnabled(bool enabled);

	long getRemainingSeconds() const;
	long getTotalCoinsAccepted() const;
	int getMinutesPerCoin() const;
	long getPriceCentsPerMinute() const;
	bool setPriceCentsPerMinute(long cents); // 0..100000

	bool hasCredit() const;
	bool addCoin();
	bool addMinutes(int minutes);
	PixCreditResult applyPixCredit(const std::string& transactionId, int minutes);
	PixCreditResult applyPixCredit(const std::string& transactionId, int minutes,
		const std::string& beneficiaryType, const std::string& beneficiaryId);
	bool getPixBeneficiary(std::string& beneficiaryType, std::string& beneficiaryId) const;
	bool canAcceptPixMinutes(const std::string& beneficiaryType,
		const std::string& beneficiaryId, int minutes) const;

	void tick(int deltaMs);
	void beginGameSession();
	// Recebe o tempo total desde o inicio do processo externo e devolve false
	// quando o jogo deve ser encerrado por saldo esgotado.
	bool updateGameSession(long elapsedSeconds);
	void endGameSession(long elapsedSeconds);
	void consumeSessionSeconds(long elapsedSeconds);

	void startSession();
	void pauseSession();
	void resumeSession();
	void stopSession();
	void endActivePlayerTurn();

	// Jogador AVULSO (sem cadastro): credito na maquina sem nome
	bool isGuestMode() const;
	bool hasGuestCredit() const;
	void clearGuestCredit(); // fecha/zera tempo avulso (cliente saiu sem cadastrar)

	bool switchToPlayer(const std::string& name);

	bool isSessionRunning() const;
	bool isSessionPaused() const;
	bool isCounting() const;
	bool isInGame() const;

	std::vector<CreditPlayer> getPlayersCopy() const;
	std::string getCurrentPlayerName() const;
	bool setCurrentPlayer(const std::string& name);
	bool registerPlayer(const std::string& name);
	bool removePlayer(const std::string& name);
	bool removeAllPlayers(); // apaga todos os clientes

	// Gestao de credito do cliente
	bool clearPlayerCredit(const std::string& name);      // zera saldo
	bool clearActivePlayerCredit();
	bool clearAllPlayersCredit();                         // zera saldo de todos
	bool clearPlayerPlayHistory(const std::string& name); // zera horas jogadas
	bool setPlayerRemainingMinutes(const std::string& name, int minutes); // define saldo exato

	// Contabilidade
	CreditAccountingTotals getAccountingTotals() const;
	std::string formatMoneyCents(long cents) const; // "R$ 12,50" ou "—"
	// Zera totais da maquina (moedas/min vendidos/tempo) — NAO apaga clientes nem saldos
	void resetMachineAccounting();
	// Zera historico de compras/jogos dos clientes — mantem nomes e saldos atuais
	void resetPlayersPurchaseHistory();

	long getCurrentPlayerPlayedSeconds() const;
	long getPlayerRemainingSeconds(const std::string& name) const;
	long getPlayerMinutesPurchased(const std::string& name) const;
	std::string formatPlayerHours(const std::string& name) const;
	std::string formatPlayerCredit(const std::string& name) const;

	std::string formatRemaining() const;
	std::string formatHudLine() const;
	static std::string formatDuration(long totalSec);

	bool isBlockWithoutCredit() const;
	int getDebounceMs() const;
	long getMaxRemainingSeconds() const;
	bool isShowHud() const;

	bool verifyAdminPassword(const std::string& password) const;
	bool setAdminPassword(const std::string& password);
	bool isUsingDefaultAdminPassword() const;

	std::string pollLowCreditWarning();

private:
	CreditManager();
	~CreditManager();
	CreditManager(const CreditManager&) = delete;
	CreditManager& operator=(const CreditManager&) = delete;

	std::string creditFilePath() const;
	std::string configFilePath() const;
	std::string playersFilePath() const;
	std::string processLockFilePath() const;
	bool acquireProcessLock();
	void releaseProcessLock();

	void loadConfig();
	bool loadPlayers(long& loadedSchemaVersion, bool& fileExists);
	void blockCreditOperationsAndPersistenceUnlocked(const char* reason);
	void clamp();
	bool persistCreditUnlocked() const;
	bool persistPlayersUnlocked();
	bool persistPlayersMirrorUnlocked() const;
	bool persistConfigUnlocked() const;
	void addPlayedToCurrentUnlocked(long seconds);
	bool applyConsumeUnlocked(long seconds, const char* reason, bool persist = true);
	bool accountGameElapsedUnlocked(long elapsedSeconds, bool forcePersist);
	void syncActivePlayerWalletUnlocked();
	bool loadActivePlayerWalletUnlocked();
	void updateLowTimeWarningsUnlocked();
	void resetLowTimeWarningsUnlocked();
	void recordSaleUnlocked(int minutes);
	void recordPlayerSaleUnlocked(CreditPlayer& player, int minutes);
	static bool isValidPixTransactionId(const std::string& transactionId);
	static bool isValidWalletId(const std::string& walletId);
	static std::string generateWalletId();
	CreditPlayer* findPlayerByIdUnlocked(const std::string& walletId);
	const CreditPlayer* findPlayerByIdUnlocked(const std::string& walletId) const;
	bool resolvePixWalletUnlocked(const std::string& beneficiaryType,
		const std::string& beneficiaryId, long*& wallet, CreditPlayer*& player, bool& isActive);
	bool resolvePixWalletUnlocked(const std::string& beneficiaryType,
		const std::string& beneficiaryId, const long*& wallet, const CreditPlayer*& player) const;
	bool rotateGuestWalletUnlocked(bool preserveBalanceAsRecovery = true);
	bool promoteRetiredGuestUnlocked(const std::string& walletId);
	size_t activePlayerCountUnlocked() const;
	bool guestRotationNeedsActiveSlotUnlocked(bool preserveBalanceAsRecovery = true) const;
	bool canRotateGuestWalletUnlocked(bool preserveBalanceAsRecovery = true) const;
	std::string recoveredGuestPlayerNameUnlocked(const std::string& walletId) const;

	static bool parseLegacyNonNegativeLong(const std::string& val, long& parsed);
	static bool parseStrictNonNegativeLong(const std::string& val, long& parsed);
	static std::string sanitizePlayerName(const std::string& name);
	static bool atomicWriteText(const std::string& path, const std::string& content);
	static std::string legacyPasswordHash(const std::string& password);
	static std::string createPasswordHash(const std::string& password);
	static bool verifyPasswordHash(const std::string& password, const std::string& encodedHash);
	static bool isLegacyPasswordHash(const std::string& encodedHash);
	static bool isSupportedPasswordHash(const std::string& encodedHash);
	static bool constantTimeEqual(const std::string& a, const std::string& b);
	static std::string defaultAdminPasswordHash();
	static std::string formatTimeUnlocked(long totalSec);

	mutable std::mutex mMutex;
	void* mProcessLockHandle;
	int mProcessLockFd;

	bool mEnabled;
	bool mBlockWithoutCredit;
	bool mShowHud;
	int mMinutesPerCoin;
	int mDebounceMs;
	long mMaxRemainingSeconds;
	long mRemainingSeconds;
	long mTotalCoinsAccepted;
	long mTotalMinutesSold;
	long mTotalSecondsPlayed;
	long mPriceCentsPerMinute; // 0 = sem valor em R$
	bool mCreditPersistenceBlocked;
	long long mLastCoinTickMs;

	bool mSessionRunning;
	bool mSessionPaused;
	bool mInGame;
	bool mGameWasCounting;
	long mGameAccountedSeconds;
	int mTickAccumMs;
	int mSaveAccumMs;

	std::vector<CreditPlayer> mPlayers;
	std::string mCurrentPlayer;
	std::string mGuestWalletId;
	long mGuestRemainingSeconds;
	std::vector<RetiredGuestWallet> mRetiredGuestWallets;
	std::vector<std::string> mRetiredGuestAliases;
	std::string mAdminPasswordHash;
	std::vector<std::string> mAppliedPixTransactions;

	int mLowTimeWarnStage;
	std::string mPendingLowTimeWarning;

	static const int kMaxPlayers = 500;
	static const int kMaxTickDeltaMs = 2000;
	static const int kSaveIntervalMs = 5000;
	static const int kSchemaVersion = 5;
	static const int kMinPasswordLen = 8;
	static const long kMaxLegacyWalletSeconds = 7L * 24L * 3600L;
	static const long kMaxPixWalletSeconds = 10L * 365L * 24L * 3600L;
	// Tombstones nao expiram por relogio: uma maquina pode ficar offline e receber
	// depois um evento autenticamente pago. O limite e apenas anti-corrupcao.
	static const size_t kMaxWalletTombstones = 100000;
	// Mantem anos de pagamentos no ledger para impedir reaplicacao de eventos antigos.
	static const size_t kMaxAppliedPixTransactions = 100000;
};
