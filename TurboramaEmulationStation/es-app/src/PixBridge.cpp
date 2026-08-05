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

	bool endsWith(const std::string& value, const std::string& suffix)
	{
		return value.size() >= suffix.size()
			&& value.compare(value.size() - suffix.size(), suffix.size(), suffix) == 0;
	}

	bool readTextLimited(const std::string& path, size_t maximumBytes, std::string& text)
	{
		text.clear();
		std::ifstream input(path, std::ios::binary | std::ios::ate);
		if (!input) return false;
		const std::streamoff length = input.tellg();
		if (length < 0 || (unsigned long long)length > maximumBytes) return false;
		input.seekg(0, std::ios::beg);
		text.resize((size_t)length);
		if (length > 0 && !input.read(&text[0], (std::streamsize)length))
		{
			text.clear();
			return false;
		}
		return true;
	}

	bool readQrPng(const std::string& path, std::vector<unsigned char>& data)
	{
		data.clear();
		std::ifstream input(path, std::ios::binary | std::ios::ate);
		if (!input) return false;
		const std::streamoff length = input.tellg();
		// Um QR normal ocupa poucos KB. O limite tambem impede que um arquivo
		// local inesperado consuma memoria da interface.
		if (length < 64 || length > 2 * 1024 * 1024) return false;
		input.seekg(0, std::ios::beg);
		data.resize((size_t)length);
		if (!input.read((char*)data.data(), length)) { data.clear(); return false; }
		static const unsigned char signature[] = { 137, 80, 78, 71, 13, 10, 26, 10 };
		if (data.size() < sizeof(signature)
			|| !std::equal(signature, signature + sizeof(signature), data.begin()))
		{
			data.clear();
			return false;
		}
		return true;
	}

	std::string safeRejectedReason(const std::string& value)
	{
		std::string clean;
		clean.reserve(std::min<size_t>(value.size(), 300));
		for (const unsigned char character : value)
		{
			if (character >= 32 && character != 127) clean.push_back((char)character);
			if (clean.size() >= 300) break;
		}
		const std::string prefix = "APP_USR-";
		const size_t start = clean.find(prefix);
		if (start != std::string::npos)
		{
			size_t end = start;
			while (end < clean.size() && (std::isalnum((unsigned char)clean[end]) || clean[end] == '-' || clean[end] == '_')) end++;
			clean.replace(start, end - start, "[Access Token oculto]");
		}
		return clean;
	}

	bool extractString(const std::string& json, const std::string& name, const std::string& pattern, std::string& value)
	{
		const std::regex expression("\\\"" + name + "\\\"\\s*:\\s*\\\"(" + pattern + ")\\\"");
		std::sregex_iterator match(json.begin(), json.end(), expression), end;
		if (match == end) return false;
		value = (*match)[1].str();
		if (++match != end) return false;
		return true;
	}

	bool extractLong(const std::string& json, const std::string& name, long long& value)
	{
		const std::regex expression("\\\"" + name + "\\\"\\s*:\\s*([0-9]{1,18})");
		std::sregex_iterator match(json.begin(), json.end(), expression), end;
		if (match == end) return false;
		try { value = std::stoll((*match)[1].str()); }
		catch (...) { return false; }
		if (++match != end) return false;
		return true;
	}

	bool extractBool(const std::string& json, const std::string& name, bool& value)
	{
		const std::regex expression("\\\"" + name + "\\\"\\s*:\\s*(true|false)");
		std::sregex_iterator match(json.begin(), json.end(), expression), end;
		if (match == end) return false;
		value = (*match)[1].str() == "true";
		if (++match != end) return false;
		return true;
	}

	bool readOwnerSetupMessage(std::string& message)
	{
		message.clear();
		std::string json;
		if (!readTextLimited(Utils::FileSystem::combine(pixRoot(), "owner-setup-status.json"), 4096, json)
			|| json.empty()) return false;
		long long schema = 0, updated = 0;
		std::string state, candidate;
		if (!extractLong(json, "schemaVersion", schema) || schema != 1
			|| !extractLong(json, "updatedAtUnixSeconds", updated)
			|| !extractString(json, "state", "[A-Za-z_]{1,32}", state)
			|| !extractString(json, "message", "[^\\\"\\\\\\r\\n]{1,500}", candidate)) return false;
		const long long now = (long long)std::time(nullptr);
		if (updated < now - 120 || updated > now + 120) return false;
		if (state != "error" && state != "waiting_network" && state != "configuring") return false;
		for (const unsigned char character : candidate)
			if (character < 32 || character == 127) return false;
		message = candidate;
		return !message.empty();
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

	bool readSignedQrMatrix(const std::string& path, const std::string& requestId,
		const std::string& root, std::vector<unsigned char>& modules, int& moduleCount)
	{
		modules.clear();
		moduleCount = 0;
		std::string text;
		if (!readTextLimited(path, 70000, text) || text.size() < 200) return false;

		std::vector<std::string> lines;
		std::istringstream input(text);
		std::string line;
		while (std::getline(input, line))
		{
			if (!line.empty() && line.back() == '\r') line.pop_back();
			lines.push_back(line);
		}
		if (!lines.empty() && lines.back().empty()) lines.pop_back();
		if (lines.size() < 25 || lines[0] != "TURBORAMA_QR_MATRIX_V1" || lines[1] != requestId
			|| !std::regex_match(lines[2], std::regex("[0-9]{2,3}"))
			|| !std::regex_match(lines[3], std::regex("[A-Fa-f0-9]{64}"))) return false;

		int size = 0;
		try { size = std::stoi(lines[2]); }
		catch (...) { return false; }
		if (size < 21 || size > 256 || lines.size() != (size_t)size + 4) return false;

		std::string grid;
		modules.reserve((size_t)size * size);
		for (int row = 0; row < size; ++row)
		{
			const std::string& values = lines[(size_t)row + 4];
			if (values.size() != (size_t)size) { modules.clear(); return false; }
			if (row > 0) grid.push_back('\n');
			grid += values;
			for (const char value : values)
			{
				if (value != '0' && value != '1') { modules.clear(); return false; }
				modules.push_back(value == '1' ? 1 : 0);
			}
		}

		std::string keyText;
		if (!readTextLimited(Utils::FileSystem::combine(root, "bridge.key"), 4096, keyText)) return false;
		const std::vector<unsigned char> key = decodeBase64(keyText);
		if (key.size() != 32) { modules.clear(); return false; }
		std::string signature = lines[3];
		std::transform(signature.begin(), signature.end(), signature.begin(),
			[](unsigned char ch) { return (char)std::tolower(ch); });
		const std::string canonical = "1\n" + requestId + "\n" + std::to_string(size) + "\n" + grid;
		if (!constantTimeEqual(hmacSha256Hex(key, canonical), signature))
		{
			modules.clear();
			return false;
		}
		moduleCount = size;
		return true;
	}

	struct ApprovedCredit
	{
		int schemaVersion = 0;
		std::string transactionId;
		int minutes = 0;
		long long amountCents = 0;
		std::string provider;
		std::string providerOrderId;
		long long requestExpiresAt = 0;
		std::string beneficiaryType;
		std::string beneficiaryId;
		long long approvedAt = 0;
		long long eventExpiresAt = 0;
		std::string signature;
	};

	bool validBeneficiary(const std::string& type, const std::string& id)
	{
		return (type == "player" || type == "guest")
			&& id.size() >= 16 && id.size() <= 128
			&& std::all_of(id.begin(), id.end(), [](unsigned char ch) {
				return std::isalnum(ch) || ch == '-' || ch == '_';
			});
	}

	bool readApprovedCredit(const std::string& file, ApprovedCredit& credit)
	{
		std::string json;
		if (!readTextLimited(file, 16384, json) || json.empty()) return false;
		long long schema = 0;
		long long minutes = 0;
		if (!extractLong(json, "schemaVersion", schema) || schema != 2) return false;
		credit.schemaVersion = (int)schema;
		if (!extractString(json, "transactionId", "[A-Za-z0-9_-]{1,64}", credit.transactionId)) return false;
		if (!extractLong(json, "minutes", minutes) || minutes < 1 || minutes > 480) return false;
		credit.minutes = (int)minutes;
		if (!extractLong(json, "amountCents", credit.amountCents) || credit.amountCents < 1 || credit.amountCents > 100000000) return false;
		if (!extractString(json, "provider", "mercadopago|mock|adapter", credit.provider)) return false;
		if (!extractString(json, "providerOrderId", "[A-Za-z0-9_-]{1,128}", credit.providerOrderId)) return false;
		if (!extractLong(json, "requestExpiresAtUnixSeconds", credit.requestExpiresAt)) return false;
		if (!extractString(json, "beneficiaryType", "player|guest", credit.beneficiaryType)) return false;
		if (!extractString(json, "beneficiaryId", "[A-Za-z0-9_-]{16,128}", credit.beneficiaryId)) return false;
		if (!validBeneficiary(credit.beneficiaryType, credit.beneficiaryId)) return false;
		if (!extractLong(json, "approvedAtUnixSeconds", credit.approvedAt)) return false;
		if (!extractLong(json, "eventExpiresAtUnixSeconds", credit.eventExpiresAt)) return false;
		if (!extractString(json, "signature", "[A-Fa-f0-9]{64}", credit.signature)) return false;
		std::transform(credit.signature.begin(), credit.signature.end(), credit.signature.begin(), [](unsigned char ch) { return (char)std::tolower(ch); });
		return true;
	}

	bool readLegacyApprovedCredit(const std::string& file, ApprovedCredit& credit)
	{
		std::string json;
		if (!readTextLimited(file, 16384, json) || json.empty()) return false;
		long long schema = 0, minutes = 0;
		if (!extractLong(json, "schemaVersion", schema) || schema != 1) return false;
		credit.schemaVersion = 1;
		if (!extractString(json, "transactionId", "[A-Za-z0-9_-]{1,64}", credit.transactionId)) return false;
		if (!extractLong(json, "minutes", minutes) || minutes < 1 || minutes > 480) return false;
		credit.minutes = (int)minutes;
		if (!extractLong(json, "amountCents", credit.amountCents)
			|| credit.amountCents < 1 || credit.amountCents > 100000000) return false;
		if (!extractString(json, "provider", "mercadopago|mock|adapter", credit.provider)) return false;
		if (!extractString(json, "providerOrderId", "[A-Za-z0-9_-]{1,128}", credit.providerOrderId)) return false;
		if (!extractLong(json, "approvedAtUnixSeconds", credit.approvedAt)) return false;
		if (!extractString(json, "signature", "[A-Fa-f0-9]{64}", credit.signature)) return false;
		std::transform(credit.signature.begin(), credit.signature.end(), credit.signature.begin(),
			[](unsigned char ch) { return (char)std::tolower(ch); });
		return true;
	}

	bool verifyCreditSignature(const ApprovedCredit& credit, const std::vector<unsigned char>& key)
	{
		if (credit.schemaVersion != 2) return false;
		const std::string payload = "2\n" + credit.transactionId + "\n" + std::to_string(credit.minutes) + "\n"
			+ std::to_string(credit.amountCents) + "\n" + credit.provider + "\n" + credit.providerOrderId + "\n"
			+ std::to_string(credit.requestExpiresAt) + "\n" + credit.beneficiaryType + "\n" + credit.beneficiaryId + "\n"
			+ std::to_string(credit.approvedAt) + "\n" + std::to_string(credit.eventExpiresAt);
		return constantTimeEqual(hmacSha256Hex(key, payload), credit.signature);
	}

	bool verifyLegacyCreditSignature(const ApprovedCredit& credit, const std::vector<unsigned char>& key)
	{
		if (credit.schemaVersion != 1) return false;
		const std::string payload = "1\n" + credit.transactionId + "\n" + std::to_string(credit.minutes) + "\n"
			+ std::to_string(credit.amountCents) + "\n" + credit.provider + "\n" + credit.providerOrderId + "\n"
			+ std::to_string(credit.approvedAt);
		return constantTimeEqual(hmacSha256Hex(key, payload), credit.signature);
	}

	bool stableCreditFieldsMatch(const ApprovedCredit& previous, const ApprovedCredit& current)
	{
		if (previous.transactionId != current.transactionId || previous.minutes != current.minutes
			|| previous.amountCents != current.amountCents || previous.provider != current.provider
			|| previous.providerOrderId != current.providerOrderId) return false;
		// O v1 nao possuia beneficiario nem expiracao do pedido. A existencia de
		// seu recibo assinado ainda prova que esta transacao ja foi aplicada.
		return previous.schemaVersion == 1
			|| (previous.requestExpiresAt == current.requestExpiresAt
				&& previous.beneficiaryType == current.beneficiaryType
				&& previous.beneficiaryId == current.beneficiaryId);
	}

	bool purchaseModeAllowsRequest(const PixPublicOptions& options, std::string& error)
	{
		if (options.provider == "mercadopago" && !options.productionEnabled)
		{
			error = "MODO TESTE: vendas Mercado Pago bloqueadas. Ative o ambiente de producao no configurador PIX antes de vender.";
			return false;
		}
		return true;
	}

	bool purchaseModeGuardSelfTest()
	{
		std::string error;
		PixPublicOptions options;
		options.provider = "mercadopago";
		options.productionEnabled = false;
		if (purchaseModeAllowsRequest(options, error)
			|| error.find("MODO TESTE") == std::string::npos) return false;

		error.clear();
		options.productionEnabled = true;
		if (!purchaseModeAllowsRequest(options, error) || !error.empty()) return false;

		// O mock continua exercitando o fluxo local, mas nunca representa venda real.
		error.clear();
		options.provider = "mock";
		options.productionEnabled = false;
		return purchaseModeAllowsRequest(options, error) && error.empty();
	}

	bool verifyCredit(const ApprovedCredit& credit, const std::string& root, const std::vector<unsigned char>& key)
	{
		// A ponte PIX e alimentada por outro processo. Nao use o cache global do
		// EmulationStation para arquivos que podem aparecer enquanto a interface
		// esta aberta, pois um primeiro resultado "nao existe" fica armazenado.
		if (credit.provider == "mock" && !Utils::FileSystem::exists(
			Utils::FileSystem::combine(root, "allow-mock-credit"), false)) return false;
		const long long now = (long long)std::time(nullptr);
		if (credit.schemaVersion != 2 || credit.approvedAt < 1577836800LL || credit.approvedAt > now + 600
			|| credit.requestExpiresAt < 1577836800LL
			|| credit.requestExpiresAt > credit.approvedAt + 60 * 60
			|| credit.eventExpiresAt != credit.approvedAt + 30LL * 24 * 60 * 60
			|| now > credit.eventExpiresAt) return false;
		return verifyCreditSignature(credit, key);
	}
}

bool PixBridge::loadPublicOptions(PixPublicOptions& options, std::string& error)
{
	options = PixPublicOptions{};
	const std::string file = Utils::FileSystem::combine(pixRoot(), "public-options.json");
	std::string json;
	if (!readTextLimited(file, 65536, json) || json.empty())
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
	// productionEnabled vem do contrato publico publicado pelo agente. Enquanto
	// estiver falso, o frontend trata Mercado Pago como configuracao/teste e nao
	// recebe uma lista utilizavel para venda. O mock permanece disponivel abaixo.
	if (!purchaseModeAllowsRequest(options, error)) return false;
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
		// A interface do cliente deve informar o motivo atual do agente, sem
		// expor qualquer credencial. Antes esta tela dizia apenas "configurando"
		// mesmo quando o token ou a conexao tinham falhado.
		if (!readOwnerSetupMessage(error))
			error = options.provider == "mercadopago" ? "PIX aguardando configuracao do Mercado Pago" : "PIX indisponivel";
		return false;
	}
	return true;
}

bool PixBridge::getCurrentBeneficiary(PixBeneficiary& beneficiary, std::string& error)
{
	beneficiary = PixBeneficiary{};
	if (!CreditManager::getInstance().getPixBeneficiary(beneficiary.type, beneficiary.id)
		|| !validBeneficiary(beneficiary.type, beneficiary.id))
	{
		error = "Nao foi possivel vincular o PIX a uma carteira segura. Selecione o jogador novamente.";
		return false;
	}
	beneficiary.displayName = beneficiary.type == "guest"
		? "JOGADOR AVULSO"
		: CreditManager::getInstance().getCurrentPlayerName();
	if (beneficiary.displayName.empty()) beneficiary.displayName = "JOGADOR CADASTRADO";
	return true;
}

bool PixBridge::createPurchaseRequest(const PixPackage& package, const PixBeneficiary& beneficiary,
	std::string& requestId, std::string& error)
{
	PixPublicOptions options;
	if (!loadPublicOptions(options, error))
	{
		requestId.clear();
		return false;
	}
	// Defesa no ponto de criacao: mesmo que a politica do carregador mude no
	// futuro, nenhuma solicitacao Mercado Pago sai com producao desativada.
	if (!purchaseModeAllowsRequest(options, error))
	{
		requestId.clear();
		return false;
	}
	if (!validBeneficiary(beneficiary.type, beneficiary.id))
	{
		error = "Destino do credito PIX invalido. Abra a tela novamente.";
		return false;
	}
	if (!CreditManager::getInstance().canAcceptPixMinutes(
		beneficiary.type, beneficiary.id, package.minutes))
	{
		error = "Este pacote ultrapassa o limite de tempo da carteira selecionada. Escolha um pacote menor ou use o saldo atual.";
		return false;
	}
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
	const long long requestedAt = (long long)std::time(nullptr);
	const long long expiresAt = requestedAt + (long long)options.paymentExpirationMinutes * 60;
	std::string keyText;
	if (!readTextLimited(Utils::FileSystem::combine(pixRoot(), "bridge.key"), 4096, keyText))
	{
		error = "Servico PIX ainda esta preparando a chave segura. Tente novamente em alguns segundos.";
		requestId.clear();
		return false;
	}
	const std::vector<unsigned char> signingKey = decodeBase64(keyText);
	if (signingKey.size() != 32)
	{
		error = "Servico PIX ainda esta preparando a chave segura. Tente novamente em alguns segundos.";
		requestId.clear();
		return false;
	}
	const std::string canonical = "2\n" + requestId + "\n" + std::to_string(package.minutes) + "\n"
		+ std::to_string(package.amountCents) + "\n" + std::to_string(requestedAt) + "\n"
		+ std::to_string(expiresAt) + "\n" + beneficiary.type + "\n" + beneficiary.id;
	const std::string signature = hmacSha256Hex(signingKey, canonical);
	if (signature.size() != 64)
	{
		error = "Nao foi possivel assinar o pedido PIX";
		requestId.clear();
		return false;
	}
	const std::string json = "{\n  \"schemaVersion\": 2,\n  \"id\": \"" + requestId
		+ "\",\n  \"minutes\": " + std::to_string(package.minutes)
		+ ",\n  \"amountCents\": " + std::to_string(package.amountCents)
		+ ",\n  \"requestedAtUnixSeconds\": " + std::to_string(requestedAt)
		+ ",\n  \"expiresAtUnixSeconds\": " + std::to_string(expiresAt)
		+ ",\n  \"beneficiaryType\": \"" + beneficiary.type
		+ "\",\n  \"beneficiaryId\": \"" + beneficiary.id
		+ "\",\n  \"signature\": \"" + signature + "\"\n}\n";
	if (!writeAtomically(destination, json))
	{
		error = "Nao foi possivel enviar o pedido ao servico PIX";
		if (lastPixWriteError != 0) error += " (Windows " + std::to_string(lastPixWriteError) + ")";
		requestId.clear();
		return false;
	}
	return true;
}

bool PixBridge::createPurchaseRequest(const PixPackage& package,
	std::string& requestId, std::string& error)
{
	PixBeneficiary beneficiary;
	return getCurrentBeneficiary(beneficiary, error)
		&& createPurchaseRequest(package, beneficiary, requestId, error);
}

PixPurchaseInfo PixBridge::getPurchaseInfo(const std::string& requestId)
{
	PixPurchaseInfo info;
	if (!std::regex_match(requestId, std::regex("[A-Za-z0-9_-]{1,64}"))) return info;
	const std::string root = pixRoot();
	for (const char* prefix : { "conflicting-processed-", "unverified-processed-" })
	{
		const std::string conflict = Utils::FileSystem::combine(root,
			"reconciliation/" + std::string(prefix) + requestId + ".credit.json");
		if (!Utils::FileSystem::exists(conflict, false)) continue;
		info.state = PixPurchaseState::SecurityError;
		std::string reason;
		readTextLimited(conflict + ".reason.txt", 1024, reason);
		info.error = reason.empty()
			? "Conflito com comprovante PIX anterior; verificacao manual necessaria."
			: safeRejectedReason(reason);
		return info;
	}
	if (Utils::FileSystem::exists(Utils::FileSystem::combine(root,
		"processed/" + requestId + ".credit.json"), false))
	{
		info.state = PixPurchaseState::Completed;
		return info;
	}
	if (Utils::FileSystem::exists(Utils::FileSystem::combine(root,
		"approved/" + requestId + ".credit.json"), false))
	{
		info.state = PixPurchaseState::Approved;
		return info;
	}
	const std::string qr = Utils::FileSystem::combine(root, "qr/" + requestId + ".png");
	if (Utils::FileSystem::exists(qr, false))
	{
		info.qrImagePath = qr;
		readQrPng(qr, info.qrImageData);
	}
	const std::string matrix = Utils::FileSystem::combine(root, "qr/" + requestId + ".matrix");
	if (Utils::FileSystem::exists(matrix, false)
		&& readSignedQrMatrix(matrix, requestId, root, info.qrModules, info.qrModuleCount))
		info.qrMatrixPath = matrix;
	std::string session;
	readTextLimited(Utils::FileSystem::combine(root, "sessions/" + requestId + ".session.json"), 65536, session);
	std::string status;
	std::string beneficiaryType, beneficiaryId;
	long long sessionSchema = 0;
	if (!session.empty()
		&& extractLong(session, "schemaVersion", sessionSchema) && sessionSchema == 2
		&& extractString(session, "beneficiaryType", "player|guest", beneficiaryType)
		&& extractString(session, "beneficiaryId", "[A-Za-z0-9_-]{16,128}", beneficiaryId)
		&& validBeneficiary(beneficiaryType, beneficiaryId)
		&& extractString(session, "status", "pending|approved|completed|cancelled|security_error", status))
	{
		if (status == "completed" || status == "approved") info.state = PixPurchaseState::Approved;
		else if (status == "cancelled") info.state = PixPurchaseState::Cancelled;
		else if (status == "security_error") info.state = PixPurchaseState::SecurityError;
		else info.state = PixPurchaseState::Pending;
		return info;
	}
	for (const auto& file : Utils::FileSystem::getDirContent(
		Utils::FileSystem::combine(root, "reconciliation"), false, false))
	{
		const std::string name = filenameOf(file);
		if (name.find(requestId) == std::string::npos || endsWith(name, ".reason.txt")) continue;
		std::string reconciliationState, reconciliationJson;
		readTextLimited(file, 65536, reconciliationJson);
		if (name.find("legacy-already-applied-") != std::string::npos
			|| name.find("already_applied_audit_only") != std::string::npos
			|| extractString(reconciliationJson, "state",
				"already_applied_audit_only", reconciliationState))
		{
			info.state = PixPurchaseState::Completed;
			return info;
		}
		info.state = PixPurchaseState::SecurityError;
		std::string reason;
		readTextLimited(file + ".reason.txt", 1024, reason);
		info.error = reason.empty()
			? "Pagamento anterior preservado para conciliacao manual do beneficiario."
			: safeRejectedReason(reason);
		return info;
	}
	for (const auto& file : Utils::FileSystem::getDirContent(Utils::FileSystem::combine(root, "rejected"), false, false))
	{
		const std::string name = filenameOf(file);
		const bool matchingRequest = endsWith(name, ".request.json")
			&& name.find(requestId + ".request.json") != std::string::npos;
		const bool matchingFinancialState = (endsWith(name, ".session.json") || endsWith(name, ".credit.json"))
			&& name.find(requestId) != std::string::npos;
		if (matchingRequest || matchingFinancialState)
		{
			info.state = matchingFinancialState ? PixPurchaseState::SecurityError : PixPurchaseState::Rejected;
			std::string reason;
			if (readTextLimited(file + ".reason.txt", 1024, reason)) info.error = safeRejectedReason(reason);
			return info;
		}
	}
	if (Utils::FileSystem::exists(Utils::FileSystem::combine(root,
		"requests/" + requestId + ".request.json"), false))
		info.state = PixPurchaseState::Generating;
	return info;
}

bool PixBridge::verifyApprovedEventFileForTest(const std::string& file, const std::string& root)
{
	ApprovedCredit credit;
	std::string keyText;
	if (!readTextLimited(Utils::FileSystem::combine(root, "bridge.key"), 4096, keyText)) return false;
	const std::vector<unsigned char> key = decodeBase64(keyText);
	return key.size() == 32 && readApprovedCredit(file, credit) && verifyCredit(credit, root, key);
}

bool PixBridge::runQrCacheRegressionTest()
{
	if (!purchaseModeGuardSelfTest()) return false;

	const std::string root = pixRoot();
	for (const char* directory : { "qr", "sessions", "approved", "processed", "rejected", "requests" })
		Utils::FileSystem::createDirectory(Utils::FileSystem::combine(root, directory));

	const std::string requestId = "pix-cache-regression-v22";
	const std::string matrix = Utils::FileSystem::combine(root, "qr/" + requestId + ".matrix");
	Utils::FileSystem::removeFile(matrix);
	// 32 bytes nulos em Base64. Esta chave existe somente na pasta descartavel
	// recebida pelo argumento de teste e nunca toca a instalacao do cliente.
	const std::string keyText = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
	if (!writeAtomically(Utils::FileSystem::combine(root, "bridge.key"), keyText)) return false;
	const std::vector<unsigned char> key = decodeBase64(keyText);
	if (key.size() != 32) return false;

	// Esta chamada registra no cache global que o QR ainda nao existe.
	const PixPurchaseInfo before = getPurchaseInfo(requestId);
	if (before.qrModuleCount != 0 || !before.qrModules.empty()) return false;

	const int size = 21;
	std::string grid;
	for (int row = 0; row < size; ++row)
	{
		if (row > 0) grid.push_back('\n');
		for (int column = 0; column < size; ++column)
			grid.push_back(((row * 3 + column * 5) % 7) < 3 ? '1' : '0');
	}
	const std::string canonical = "1\n" + requestId + "\n" + std::to_string(size) + "\n" + grid;
	const std::string contents = "TURBORAMA_QR_MATRIX_V1\n" + requestId + "\n"
		+ std::to_string(size) + "\n" + hmacSha256Hex(key, canonical) + "\n" + grid + "\n";
	// writeAtomically usa MoveFileEx, como o agente externo, e deliberadamente
	// nao limpa o cache privado do frontend.
	if (!writeAtomically(matrix, contents)) return false;

	const PixPurchaseInfo after = getPurchaseInfo(requestId);
	return after.qrModuleCount == size
		&& after.qrModules.size() == (size_t)size * size
		&& after.qrMatrixPath == matrix;
}

std::vector<std::string> PixBridge::processApprovedCredits()
{
	std::vector<std::string> messages;
	const std::string root = pixRoot();
	const std::string approved = Utils::FileSystem::combine(root, "approved");
	const std::string processed = Utils::FileSystem::combine(root, "processed");
	const std::string rejected = Utils::FileSystem::combine(root, "rejected");
	const std::string reconciliation = Utils::FileSystem::combine(root, "reconciliation");
	Utils::FileSystem::createDirectory(approved);
	Utils::FileSystem::createDirectory(processed);
	Utils::FileSystem::createDirectory(rejected);
	Utils::FileSystem::createDirectory(reconciliation);

	// A chave pode ficar momentaneamente indisponivel durante instalacao, antivirus ou
	// sincronizacao. Nesse caso deixamos os eventos intactos para a proxima tentativa.
	std::string keyText;
	if (!readTextLimited(Utils::FileSystem::combine(root, "bridge.key"), 4096, keyText))
		return messages;
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
			long long detectedSchema = 0;
			std::string invalidJson;
			const bool legacyV1 = readTextLimited(file, 16384, invalidJson)
				&& extractLong(invalidJson, "schemaVersion", detectedSchema) && detectedSchema == 1;
			const bool legacyAlreadyApplied = legacyV1 && Utils::FileSystem::exists(
				Utils::FileSystem::combine(processed, fileName), false);
			const std::string isolated = Utils::FileSystem::combine(
				legacyV1 ? reconciliation : rejected,
				(legacyAlreadyApplied ? "legacy-already-applied-"
					: legacyV1 ? "legacy-unassigned-" : "invalid-") + filenameOf(file));
			if (Utils::FileSystem::renameFile(file, isolated, true))
				Utils::FileSystem::writeAllText(isolated + ".reason.txt", legacyAlreadyApplied
					? "Evento PIX v1 ja consta em processed. Preservado somente para auditoria; nao atribuir novo credito."
					: legacyV1
					? "Evento PIX v1 aprovado sem beneficiario verificavel. Reconciliar manualmente com o provedor antes de atribuir."
					: "Evento PIX recusado por schema, assinatura, expiracao ou campos invalidos.");
			continue;
		}

		const std::string destination = Utils::FileSystem::combine(processed, fileName);
		if (Utils::FileSystem::exists(destination, false))
		{
			ApprovedCredit previous;
			bool authenticatedTombstone = readApprovedCredit(destination, previous)
				&& previous.transactionId == credit.transactionId
				&& verifyCreditSignature(previous, signingKey);
			if (!authenticatedTombstone)
			{
				previous = ApprovedCredit{};
				authenticatedTombstone = readLegacyApprovedCredit(destination, previous)
					&& previous.transactionId == credit.transactionId
					&& verifyLegacyCreditSignature(previous, signingKey);
			}
			if (authenticatedTombstone && stableCreditFieldsMatch(previous, credit))
			{
				// Tombstone permanente: a expiracao limita o transporte de um evento
				// novo, mas nunca reabre uma transacao que ja foi creditada. Mantemos
				// o recibo original e descartamos apenas a copia reemitida.
				Utils::FileSystem::removeFile(file);
				continue;
			}

			// Um marker processado corrompido ou conflitante pode representar
			// credito antigo cujo ledger ja foi compactado. Falha fechado: nunca
			// removemos o tombstone para aplicar automaticamente o novo evento.
			LOG(LogWarning) << "[PixBridge] conflito com comprovante processado; credito bloqueado: " << destination;
			const std::string isolated = Utils::FileSystem::combine(reconciliation,
				(authenticatedTombstone ? "conflicting-processed-" : "unverified-processed-") + fileName);
			if (Utils::FileSystem::renameFile(file, isolated, true))
				Utils::FileSystem::writeAllText(isolated + ".reason.txt", authenticatedTombstone
					? "Evento PIX conflita com um recibo permanente ja processado. Verificar manualmente; nenhum novo credito foi aplicado."
					: "Existe um marker processed nao verificavel. Preservado para auditoria; nenhum novo credito foi aplicado automaticamente.");
			continue;
		}

		const PixCreditResult result = CreditManager::getInstance().applyPixCredit(
			credit.transactionId, credit.minutes, credit.beneficiaryType, credit.beneficiaryId);
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
