#include "PixBridge.h"

#include "CreditManager.h"
#include "Log.h"
#include "Paths.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"

#include <algorithm>
#include <cctype>
#include <ctime>
#include <iomanip>
#include <regex>
#include <sstream>
#include <vector>
#include <fstream>

#ifdef _WIN32
#include <windows.h>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")
#endif

namespace
{
	unsigned long lastPixWriteError = 0;
	std::string pixRoot()
	{
		return Utils::FileSystem::combine(Paths::getUserEmulationStationPath(), "pix");
	}

	std::string filenameOf(const std::string& path)
	{
		const size_t separator = path.find_last_of("/\\");
		return separator == std::string::npos ? path : path.substr(separator + 1);
	}

	bool extractString(const std::string& json, const std::string& name, const std::string& pattern, std::string& value)
	{
		std::smatch match;
		const std::regex expression("\\\"" + name + "\\\"\\s*:\\s*\\\"(" + pattern + ")\\\"");
		if (!std::regex_search(json, match, expression)) return false;
		value = match[1].str();
		return true;
	}

	bool extractLong(const std::string& json, const std::string& name, long long& value)
	{
		std::smatch match;
		const std::regex expression("\\\"" + name + "\\\"\\s*:\\s*([0-9]{1,18})");
		if (!std::regex_search(json, match, expression)) return false;
		try { value = std::stoll(match[1].str()); }
		catch (...) { return false; }
		return true;
	}

	bool extractBool(const std::string& json, const std::string& name, bool& value)
	{
		std::smatch match;
		const std::regex expression("\\\"" + name + "\\\"\\s*:\\s*(true|false)");
		if (!std::regex_search(json, match, expression)) return false;
		value = match[1].str() == "true";
		return true;
	}

	std::string utcIso8601()
	{
		const std::time_t now = std::time(nullptr);
		std::tm utc{};
#ifdef _WIN32
		gmtime_s(&utc, &now);
#else
		gmtime_r(&now, &utc);
#endif
		std::ostringstream out;
		out << std::put_time(&utc, "%Y-%m-%dT%H:%M:%SZ");
		return out.str();
	}

	std::string randomRequestId()
	{
#ifdef _WIN32
		unsigned char bytes[16]{};
		if (BCryptGenRandom(nullptr, bytes, sizeof(bytes), BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0) return {};
		std::ostringstream out;
		// Insira o timestamp como texto ja formatado, pois o aplicativo usa a
		// localidade do Windows na interface e o operador numerico << poderia
		// adicionar separadores (ex.: pix-1,785,612,507-...).
		out << "pix-" << std::to_string((long long)std::time(nullptr)) << "-" << std::hex << std::setfill('0');
		for (unsigned char byte : bytes) out << std::setw(2) << (int)byte;
		return out.str();
#else
		return {};
#endif
	}

#ifdef _WIN32
	std::wstring extendedWindowsPath(const std::string& path)
	{
		std::wstring wide = Utils::String::convertToWideString(path);
		std::replace(wide.begin(), wide.end(), L'/', L'\\');
		if (wide.rfind(L"\\\\?\\", 0) == 0) return wide;
		if (wide.rfind(L"\\\\", 0) == 0) return L"\\\\?\\UNC\\" + wide.substr(2);
		return L"\\\\?\\" + wide;
	}
#endif

	bool writeAtomically(const std::string& destination, const std::string& content)
	{
		lastPixWriteError = 0;
		const std::string temp = destination + "." + randomRequestId() + ".tmp";
		if (temp.find("..tmp") != std::string::npos) return false;
#ifdef _WIN32
		const std::wstring wideTemp = extendedWindowsPath(temp);
		HANDLE file = CreateFileW(wideTemp.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH, nullptr);
		if (file == INVALID_HANDLE_VALUE) { lastPixWriteError = GetLastError(); return false; }
		DWORD written = 0;
		const bool stored = WriteFile(file, content.data(), (DWORD)content.size(), &written, nullptr) != FALSE
			&& written == content.size() && FlushFileBuffers(file) != FALSE;
		CloseHandle(file);
		if (!stored) { lastPixWriteError = GetLastError(); Utils::FileSystem::removeFile(temp); return false; }
#else
		std::ofstream output(temp, std::ios::binary | std::ios::trunc);
		if (!output) return false;
		output.write(content.data(), (std::streamsize)content.size());
		output.flush();
		if (!output.good()) { output.close(); Utils::FileSystem::removeFile(temp); return false; }
#endif
#ifdef _WIN32
		const bool moved = MoveFileExW(extendedWindowsPath(temp).c_str(),
			extendedWindowsPath(destination).c_str(), MOVEFILE_WRITE_THROUGH) != FALSE;
#else
		const bool moved = Utils::FileSystem::renameFile(temp, destination, false);
#endif
		if (!moved)
		{
		#ifdef _WIN32
			lastPixWriteError = GetLastError();
		#endif
			Utils::FileSystem::removeFile(temp);
			return false;
		}
		return true;
	}

	std::vector<unsigned char> decodeBase64(const std::string& encoded)
	{
		static const std::string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
		std::vector<unsigned char> output;
		unsigned int value = 0;
		int bits = -8;
		for (const unsigned char ch : encoded)
		{
			if (ch == '=') break;
			const size_t index = alphabet.find((char)ch);
			if (index == std::string::npos)
			{
				if (ch == ' ' || ch == '\r' || ch == '\n' || ch == '\t') continue;
				return {};
			}
			value = (value << 6) + (unsigned int)index;
			bits += 6;
			if (bits >= 0)
			{
				output.push_back((unsigned char)((value >> bits) & 0xFF));
				bits -= 8;
			}
		}
		return output;
	}

	std::string hmacSha256Hex(const std::vector<unsigned char>& key, const std::string& payload)
	{
#ifdef _WIN32
		BCRYPT_ALG_HANDLE algorithm = nullptr;
		BCRYPT_HASH_HANDLE hash = nullptr;
		DWORD objectSize = 0;
		DWORD received = 0;
		std::vector<unsigned char> object;
		std::vector<unsigned char> digest(32);
		if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, BCRYPT_ALG_HANDLE_HMAC_FLAG) < 0) return {};
		if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, (PUCHAR)&objectSize, sizeof(objectSize), &received, 0) < 0) { BCryptCloseAlgorithmProvider(algorithm, 0); return {}; }
		object.resize(objectSize);
		if (BCryptCreateHash(algorithm, &hash, object.data(), objectSize, (PUCHAR)key.data(), (ULONG)key.size(), 0) < 0) { BCryptCloseAlgorithmProvider(algorithm, 0); return {}; }
		NTSTATUS status = BCryptHashData(hash, (PUCHAR)payload.data(), (ULONG)payload.size(), 0);
		if (status >= 0) status = BCryptFinishHash(hash, digest.data(), (ULONG)digest.size(), 0);
		BCryptDestroyHash(hash);
		BCryptCloseAlgorithmProvider(algorithm, 0);
		if (status < 0) return {};
		std::ostringstream out;
		out << std::hex << std::setfill('0');
		for (const unsigned char byte : digest) out << std::setw(2) << (int)byte;
		return out.str();
#else
		(void)key; (void)payload;
		return {};
#endif
	}

	bool constantTimeEqual(const std::string& left, const std::string& right)
	{
		if (left.size() != right.size()) return false;
		unsigned char difference = 0;
		for (size_t i = 0; i < left.size(); ++i) difference |= (unsigned char)(left[i] ^ right[i]);
		return difference == 0;
	}

	struct ApprovedCredit
	{
		std::string transactionId;
		int minutes = 0;
		long long amountCents = 0;
		std::string provider;
		std::string providerOrderId;
		long long approvedAt = 0;
		std::string signature;
	};

	bool readApprovedCredit(const std::string& file, ApprovedCredit& credit)
	{
		const std::string json = Utils::FileSystem::readAllText(file);
		if (json.empty() || json.size() > 16384) return false;
		long long schema = 0;
		long long minutes = 0;
		if (!extractLong(json, "schemaVersion", schema) || schema != 1) return false;
		if (!extractString(json, "transactionId", "[A-Za-z0-9_-]{1,64}", credit.transactionId)) return false;
		if (!extractLong(json, "minutes", minutes) || minutes < 1 || minutes > 480) return false;
		credit.minutes = (int)minutes;
		if (!extractLong(json, "amountCents", credit.amountCents) || credit.amountCents < 1 || credit.amountCents > 100000000) return false;
		if (!extractString(json, "provider", "mercadopago|mock|adapter", credit.provider)) return false;
		if (!extractString(json, "providerOrderId", "[A-Za-z0-9_-]{1,128}", credit.providerOrderId)) return false;
		if (!extractLong(json, "approvedAtUnixSeconds", credit.approvedAt)) return false;
		if (!extractString(json, "signature", "[A-Fa-f0-9]{64}", credit.signature)) return false;
		std::transform(credit.signature.begin(), credit.signature.end(), credit.signature.begin(), [](unsigned char ch) { return (char)std::tolower(ch); });
		return true;
	}

	bool verifyCredit(const ApprovedCredit& credit, const std::string& root, const std::vector<unsigned char>& key)
	{
		if (credit.provider == "mock" && !Utils::FileSystem::exists(Utils::FileSystem::combine(root, "allow-mock-credit"))) return false;
		const long long now = (long long)std::time(nullptr);
		if (credit.approvedAt < 1577836800LL || credit.approvedAt > now + 600) return false;
		const std::string payload = "1\n" + credit.transactionId + "\n" + std::to_string(credit.minutes) + "\n"
			+ std::to_string(credit.amountCents) + "\n" + credit.provider + "\n" + credit.providerOrderId + "\n" + std::to_string(credit.approvedAt);
		return constantTimeEqual(hmacSha256Hex(key, payload), credit.signature);
	}
}

bool PixBridge::loadPublicOptions(PixPublicOptions& options, std::string& error)
{
	options = PixPublicOptions{};
	const std::string file = Utils::FileSystem::combine(pixRoot(), "public-options.json");
	const std::string json = Utils::FileSystem::readAllText(file);
	if (json.empty() || json.size() > 65536)
	{
		error = "Servico PIX nao iniciado";
		return false;
	}
	long long schema = 0;
	long long expiration = 0;
	if (!extractLong(json, "schemaVersion", schema) || schema != 1
		|| !extractString(json, "provider", "mercadopago|mock|adapter", options.provider)
		|| !extractBool(json, "ready", options.ready)
		|| !extractBool(json, "productionEnabled", options.productionEnabled)
		|| !extractLong(json, "paymentExpirationMinutes", expiration)
		|| !extractLong(json, "generatedAtUnixSeconds", options.generatedAtUnixSeconds))
	{
		error = "Configuracao publica PIX invalida";
		return false;
	}
	options.paymentExpirationMinutes = (int)expiration;
	const long long now = (long long)std::time(nullptr);
	if (expiration < 1 || expiration > 60 || options.generatedAtUnixSeconds < now - 120 || options.generatedAtUnixSeconds > now + 120)
	{
		error = "Servico PIX sem resposta";
		return false;
	}
	const std::regex packageExpression("\\{\\s*\\\"minutes\\\"\\s*:\\s*([0-9]{1,3})\\s*,\\s*\\\"amountCents\\\"\\s*:\\s*([0-9]{1,12})\\s*\\}");
	for (std::sregex_iterator it(json.begin(), json.end(), packageExpression), end; it != end; ++it)
	{
		try
		{
			const int minutes = std::stoi((*it)[1].str());
			const long long cents = std::stoll((*it)[2].str());
			if (minutes >= 1 && minutes <= 480 && cents >= 1 && cents <= 100000000)
				options.packages.push_back({ minutes, cents });
		}
		catch (...) { }
	}
	if (options.packages.empty())
	{
		error = "Nenhum pacote PIX disponivel";
		return false;
	}
	if (!options.ready)
	{
		error = options.provider == "mercadopago" ? "PIX aguardando configuracao do Mercado Pago" : "PIX indisponivel";
		return false;
	}
	return true;
}

bool PixBridge::createPurchaseRequest(const PixPackage& package, std::string& requestId, std::string& error)
{
	PixPublicOptions options;
	if (!loadPublicOptions(options, error)) return false;
	const auto match = std::find_if(options.packages.begin(), options.packages.end(), [&](const PixPackage& item) {
		return item.minutes == package.minutes && item.amountCents == package.amountCents;
	});
	if (match == options.packages.end())
	{
		error = "Pacote PIX desatualizado. Abra a tela novamente.";
		return false;
	}
	requestId = randomRequestId();
	if (requestId.empty())
	{
		error = "Nao foi possivel criar um identificador seguro";
		return false;
	}
	const std::string requests = Utils::FileSystem::combine(pixRoot(), "requests");
	Utils::FileSystem::createDirectory(requests);
	const std::string destination = Utils::FileSystem::combine(requests, requestId + ".request.json");
	const std::string json = "{\n  \"id\": \"" + requestId + "\",\n  \"minutes\": " + std::to_string(package.minutes)
		+ ",\n  \"amountCents\": " + std::to_string(package.amountCents) + ",\n  \"requestedAt\": \"" + utcIso8601() + "\"\n}\n";
	if (!writeAtomically(destination, json))
	{
		error = "Nao foi possivel enviar o pedido ao servico PIX";
		if (lastPixWriteError != 0) error += " (Windows " + std::to_string(lastPixWriteError) + ")";
		requestId.clear();
		return false;
	}
	return true;
}

PixPurchaseInfo PixBridge::getPurchaseInfo(const std::string& requestId)
{
	PixPurchaseInfo info;
	if (!std::regex_match(requestId, std::regex("[A-Za-z0-9_-]{1,64}"))) return info;
	const std::string root = pixRoot();
	if (Utils::FileSystem::exists(Utils::FileSystem::combine(root, "processed/" + requestId + ".credit.json")))
	{
		info.state = PixPurchaseState::Completed;
		return info;
	}
	if (Utils::FileSystem::exists(Utils::FileSystem::combine(root, "approved/" + requestId + ".credit.json")))
	{
		info.state = PixPurchaseState::Approved;
		return info;
	}
	const std::string qr = Utils::FileSystem::combine(root, "qr/" + requestId + ".png");
	if (Utils::FileSystem::exists(qr)) info.qrImagePath = qr;
	const std::string session = Utils::FileSystem::readAllText(Utils::FileSystem::combine(root, "sessions/" + requestId + ".session.json"));
	std::string status;
	if (!session.empty() && extractString(session, "status", "pending|approved|completed|cancelled|security_error", status))
	{
		if (status == "completed" || status == "approved") info.state = PixPurchaseState::Approved;
		else if (status == "cancelled") info.state = PixPurchaseState::Cancelled;
		else if (status == "security_error") info.state = PixPurchaseState::SecurityError;
		else info.state = PixPurchaseState::Pending;
		return info;
	}
	for (const auto& file : Utils::FileSystem::getDirContent(Utils::FileSystem::combine(root, "rejected"), false, false))
		if (filenameOf(file).find(requestId + ".request.json") != std::string::npos)
		{
			info.state = PixPurchaseState::Rejected;
			return info;
		}
	if (Utils::FileSystem::exists(Utils::FileSystem::combine(root, "requests/" + requestId + ".request.json")))
		info.state = PixPurchaseState::Generating;
	return info;
}

bool PixBridge::verifyApprovedEventFileForTest(const std::string& file, const std::string& root)
{
	ApprovedCredit credit;
	const std::vector<unsigned char> key = decodeBase64(
		Utils::FileSystem::readAllText(Utils::FileSystem::combine(root, "bridge.key")));
	return key.size() == 32 && readApprovedCredit(file, credit) && verifyCredit(credit, root, key);
}

std::vector<std::string> PixBridge::processApprovedCredits()
{
	std::vector<std::string> messages;
	const std::string root = pixRoot();
	const std::string approved = Utils::FileSystem::combine(root, "approved");
	const std::string processed = Utils::FileSystem::combine(root, "processed");
	const std::string rejected = Utils::FileSystem::combine(root, "rejected");
	Utils::FileSystem::createDirectory(approved);
	Utils::FileSystem::createDirectory(processed);
	Utils::FileSystem::createDirectory(rejected);

	// A chave pode ficar momentaneamente indisponivel durante instalacao, antivirus ou
	// sincronizacao. Nesse caso deixamos os eventos intactos para a proxima tentativa.
	const std::string keyText = Utils::FileSystem::readAllText(Utils::FileSystem::combine(root, "bridge.key"));
	const std::vector<unsigned char> signingKey = decodeBase64(keyText);
	if (signingKey.size() != 32)
	{
		if (!Utils::FileSystem::getDirContent(approved, false, false).empty())
			LOG(LogWarning) << "[PixBridge] chave PIX ausente ou invalida; eventos preservados para nova tentativa";
		return messages;
	}

	for (const std::string& file : Utils::FileSystem::getDirContent(approved, false, false))
	{
		if (file.size() < 12 || file.substr(file.size() - 12) != ".credit.json")
			continue;
		ApprovedCredit credit;
		const std::string fileName = filenameOf(file);
		const std::string expectedId = fileName.substr(0, fileName.size() - 12);
		if (!readApprovedCredit(file, credit) || credit.transactionId != expectedId || !verifyCredit(credit, root, signingKey))
		{
			LOG(LogWarning) << "[PixBridge] evento invalido: " << file;
			Utils::FileSystem::renameFile(file, Utils::FileSystem::combine(rejected, "invalid-" + filenameOf(file)), true);
			continue;
		}

		const std::string destination = Utils::FileSystem::combine(processed, fileName);
		if (Utils::FileSystem::exists(destination))
		{
			ApprovedCredit previous;
			if (readApprovedCredit(destination, previous) && previous.transactionId == credit.transactionId
				&& verifyCredit(previous, root, signingKey))
			{
				// Segunda barreira de idempotencia, independente do tamanho do ledger.
				Utils::FileSystem::renameFile(file, destination, true);
				continue;
			}
			LOG(LogWarning) << "[PixBridge] comprovante processado invalido isolado: " << destination;
			Utils::FileSystem::renameFile(destination,
				Utils::FileSystem::combine(rejected, "invalid-processed-" + fileName), true);
		}

		const PixCreditResult result = CreditManager::getInstance().applyPixCredit(credit.transactionId, credit.minutes);
		if (result == PixCreditResult::Rejected)
		{
			LOG(LogWarning) << "[PixBridge] credito recusado: " << credit.transactionId;
			continue;
		}

		if (!Utils::FileSystem::renameFile(file, destination, true))
		{
			LOG(LogWarning) << "[PixBridge] nao foi possivel finalizar evento: " << credit.transactionId;
			continue;
		}
		if (result == PixCreditResult::Applied)
			messages.push_back("PIX CONFIRMADO: +" + std::to_string(credit.minutes) + " minutos");
	}
	return messages;
}
