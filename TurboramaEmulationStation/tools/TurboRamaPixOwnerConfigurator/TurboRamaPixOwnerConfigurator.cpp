#define UNICODE
#define _UNICODE
#include <windows.h>
#include <bcrypt.h>
#include <commctrl.h>
#include <shellapi.h>
#include <winhttp.h>
#include "../../es-app/src/PixBinaryTrust.h"
#include <algorithm>
#include <atomic>
#include <cctype>
#include <climits>
#include <cstring>
#include <cwctype>
#include <fstream>
#include <iomanip>
#include <iterator>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "winhttp.lib")

namespace
{
	constexpr int ID_PROVIDER = 100;
	constexpr int ID_TOKEN = 101;
	constexpr int ID_SHOW = 102;
	constexpr int ID_STORE = 103;
	constexpr int ID_POS = 104;
	constexpr int ID_CEP = 105;
	constexpr int ID_NUMBER = 106;
	constexpr int ID_REFERENCE = 107;
	constexpr int ID_ADAPTER_URL = 108;
	constexpr int ID_ADAPTER_ID = 109;
	constexpr int ID_ENVIRONMENT = 110;
	constexpr int ID_STORE_EXTERNAL = 111;
	constexpr int ID_POS_EXTERNAL = 112;
	constexpr int ID_PRICE15 = 115;
	constexpr int ID_PRICE30 = 130;
	constexpr int ID_PRICE45 = 145;
	constexpr int ID_PRICE60 = 160;
	constexpr int ID_PRICE120 = 220;
	constexpr int ID_CONFIGURE = 300;
	constexpr int ID_LOAD = 301;
	constexpr int ID_CLOSE = 302;
	constexpr int ID_MANAGE = 303;
	constexpr int ID_INVENTORY_STORES = 500;
	constexpr int ID_INVENTORY_POS = 501;
	constexpr int ID_INVENTORY_USE = 502;
	constexpr int ID_INVENTORY_NEW_STORE = 503;
	constexpr int ID_INVENTORY_NEW_POS = 504;
	constexpr int ID_INVENTORY_DELETE_STORE = 505;
	constexpr int ID_INVENTORY_DELETE_POS = 506;
	constexpr int ID_INVENTORY_REFRESH = 507;
	constexpr int ID_INVENTORY_CLOSE = 508;
	constexpr UINT WM_CONFIGURED = WM_APP + 25;
	constexpr UINT WM_IDENTITY_CHECKED = WM_APP + 26;
	constexpr UINT WM_INVENTORY_READY = WM_APP + 27;
	const wchar_t* kClassName = L"TurboRamaPixOwnerConfigurator";
	const wchar_t* kInventoryClassName = L"TurboRamaPixInventoryManager";
	const wchar_t* kTitle = L"LZ Games - Configuração Comercial PIX";

	HWND gWindow{}, gProvider{}, gEnvironment{}, gToken{}, gShow{}, gStore{}, gPos{}, gStoreExternal{}, gPosExternal{};
	HWND gCep{}, gNumber{}, gReference{};
	HWND gAdapterUrl{}, gAdapterId{}, gStatus{}, gConfigure{}, gManage{}, gLoad{}, gClose{};
	HWND gPrice15{}, gPrice30{}, gPrice45{}, gPrice60{}, gPrice120{};
	std::vector<HWND> gMercadoPagoLabels, gAdapterLabels;
	HFONT gTitleFont{}, gHeaderFont{}, gFont{}, gSmallFont{}, gMonoFont{}, gHeroFont{}, gStepFont{};
	HBRUSH gBackgroundBrush{}, gFieldBrush{}, gPanelBrush{};
	HICON gIcon{};
	std::atomic_bool gWorking{ false };

	struct FormData
	{
		bool adapter{};
		bool sandbox{ true };
		std::wstring token, storeName, posName, storeExternalId, posExternalId;
		std::wstring cep, number, reference, adapterUrl, adapterId;
		std::wstring p15, p30, p45, p60, p120;
	};

	struct WorkerResult { bool ok{}; std::wstring message; };
	struct MercadoPagoStore
	{
		std::wstring id, externalId, name;
	};
	struct MercadoPagoPointOfSale
	{
		std::wstring id, externalId, name, storeId, status;
	};
	struct MercadoPagoInventory
	{
		std::wstring accountId;
		std::vector<MercadoPagoStore> stores;
		std::vector<MercadoPagoPointOfSale> points;
	};
	struct KioskIdentityDisplay
	{
		std::wstring currentAccount{ L"conta Windows atual" };
		std::wstring configuredAccount{ L"conta configurada do quiosque" };
	};
	struct ActiveMercadoPagoPair
	{
		bool safeToDelete{};
		std::wstring storeExternalId, posExternalId, error;
	};
	struct InventoryDialogState
	{
		HWND window{}, stores{}, points{}, status{};
		std::wstring token;
		bool sandbox{};
		MercadoPagoInventory inventory;
	};
	struct InventoryWorkerResult
	{
		bool ok{};
		std::wstring message;
		MercadoPagoInventory inventory;
	};
	void updateProvider();

	std::wstring trim(std::wstring value)
	{
		while (!value.empty() && iswspace(value.front())) value.erase(value.begin());
		while (!value.empty() && iswspace(value.back())) value.pop_back();
		return value;
	}

	std::wstring textOf(HWND control)
	{
		const int size = GetWindowTextLengthW(control);
		std::wstring value((size_t)size + 1, L'\0');
		GetWindowTextW(control, value.data(), size + 1);
		value.resize((size_t)size);
		return trim(value);
	}

	std::string utf8(const std::wstring& value)
	{
		if (value.empty()) return {};
		const int size = WideCharToMultiByte(CP_UTF8, 0, value.data(), (int)value.size(), nullptr, 0, nullptr, nullptr);
		std::string result((size_t)size, '\0');
		WideCharToMultiByte(CP_UTF8, 0, value.data(), (int)value.size(), result.data(), size, nullptr, nullptr);
		return result;
	}

	std::wstring wide(const std::string& value)
	{
		if (value.empty()) return {};
		const int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), (int)value.size(), nullptr, 0);
		if (size <= 0) return {};
		std::wstring result((size_t)size, L'\0');
		if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), (int)value.size(), result.data(), size) != size)
			return {};
		return result;
	}

	enum class JsonKind { Null, Boolean, Number, String, Object, Array };
	struct JsonNode
	{
		JsonKind kind{ JsonKind::Null };
		bool boolean{};
		std::string scalar;
		std::vector<std::pair<std::string, JsonNode>> object;
		std::vector<JsonNode> array;

		const JsonNode* member(const std::string& name) const
		{
			for (const auto& entry : object) if (entry.first == name) return &entry.second;
			return nullptr;
		}
	};

	class StrictJsonParser
	{
	public:
		explicit StrictJsonParser(const std::string& source) : mSource(source) {}
		JsonNode parse()
		{
			if (mSource.empty() || mSource.size() > 524288) fail();
			JsonNode value = parseValue(0);
			skipSpace();
			if (mCursor != mSource.size()) fail();
			return value;
		}

	private:
		const std::string& mSource;
		size_t mCursor{};

		[[noreturn]] static void fail() { throw std::invalid_argument("json"); }
		void skipSpace()
		{
			while (mCursor < mSource.size() && (mSource[mCursor] == ' ' || mSource[mCursor] == '\t'
				|| mSource[mCursor] == '\r' || mSource[mCursor] == '\n')) ++mCursor;
		}
		bool take(char value)
		{
			skipSpace();
			if (mCursor < mSource.size() && mSource[mCursor] == value) { ++mCursor; return true; }
			return false;
		}
		void literal(const char* value)
		{
			const size_t length = strlen(value);
			if (mCursor + length > mSource.size() || mSource.compare(mCursor, length, value) != 0) fail();
			mCursor += length;
		}
		static int hex(char value)
		{
			if (value >= '0' && value <= '9') return value - '0';
			if (value >= 'a' && value <= 'f') return value - 'a' + 10;
			if (value >= 'A' && value <= 'F') return value - 'A' + 10;
			return -1;
		}
		unsigned readHex4()
		{
			if (mCursor + 4 > mSource.size()) fail();
			unsigned value = 0;
			for (int index = 0; index < 4; ++index)
			{
				const int digit = hex(mSource[mCursor++]);
				if (digit < 0) fail();
				value = value * 16 + (unsigned)digit;
			}
			return value;
		}
		static void appendUtf8(std::string& output, unsigned value)
		{
			if (value <= 0x7f) output.push_back((char)value);
			else if (value <= 0x7ff)
			{
				output.push_back((char)(0xc0 | (value >> 6)));
				output.push_back((char)(0x80 | (value & 0x3f)));
			}
			else if (value <= 0xffff)
			{
				output.push_back((char)(0xe0 | (value >> 12)));
				output.push_back((char)(0x80 | ((value >> 6) & 0x3f)));
				output.push_back((char)(0x80 | (value & 0x3f)));
			}
			else if (value <= 0x10ffff)
			{
				output.push_back((char)(0xf0 | (value >> 18)));
				output.push_back((char)(0x80 | ((value >> 12) & 0x3f)));
				output.push_back((char)(0x80 | ((value >> 6) & 0x3f)));
				output.push_back((char)(0x80 | (value & 0x3f)));
			}
			else fail();
		}
		std::string parseString()
		{
			skipSpace();
			if (mCursor >= mSource.size() || mSource[mCursor++] != '"') fail();
			std::string output;
			while (mCursor < mSource.size())
			{
				const unsigned char ch = (unsigned char)mSource[mCursor++];
				if (ch == '"') return output;
				if (ch < 0x20) fail();
				if (ch != '\\') { output.push_back((char)ch); continue; }
				if (mCursor >= mSource.size()) fail();
				switch (mSource[mCursor++])
				{
				case '"': output.push_back('"'); break;
				case '\\': output.push_back('\\'); break;
				case '/': output.push_back('/'); break;
				case 'b': output.push_back('\b'); break;
				case 'f': output.push_back('\f'); break;
				case 'n': output.push_back('\n'); break;
				case 'r': output.push_back('\r'); break;
				case 't': output.push_back('\t'); break;
				case 'u':
				{
					unsigned value = readHex4();
					if (value >= 0xd800 && value <= 0xdbff)
					{
						if (mCursor + 2 > mSource.size() || mSource[mCursor++] != '\\' || mSource[mCursor++] != 'u') fail();
						const unsigned low = readHex4();
						if (low < 0xdc00 || low > 0xdfff) fail();
						value = 0x10000 + ((value - 0xd800) << 10) + (low - 0xdc00);
					}
					else if (value >= 0xdc00 && value <= 0xdfff) fail();
					appendUtf8(output, value); break;
				}
				default: fail();
				}
			}
			fail();
		}
		JsonNode parseNumber()
		{
			skipSpace();
			const size_t start = mCursor;
			if (mCursor < mSource.size() && mSource[mCursor] == '-') ++mCursor;
			if (mCursor >= mSource.size()) fail();
			if (mSource[mCursor] == '0') ++mCursor;
			else
			{
				if (!isdigit((unsigned char)mSource[mCursor])) fail();
				while (mCursor < mSource.size() && isdigit((unsigned char)mSource[mCursor])) ++mCursor;
			}
			if (mCursor < mSource.size() && mSource[mCursor] == '.')
			{
				++mCursor; if (mCursor >= mSource.size() || !isdigit((unsigned char)mSource[mCursor])) fail();
				while (mCursor < mSource.size() && isdigit((unsigned char)mSource[mCursor])) ++mCursor;
			}
			if (mCursor < mSource.size() && (mSource[mCursor] == 'e' || mSource[mCursor] == 'E'))
			{
				++mCursor; if (mCursor < mSource.size() && (mSource[mCursor] == '+' || mSource[mCursor] == '-')) ++mCursor;
				if (mCursor >= mSource.size() || !isdigit((unsigned char)mSource[mCursor])) fail();
				while (mCursor < mSource.size() && isdigit((unsigned char)mSource[mCursor])) ++mCursor;
			}
			JsonNode result; result.kind = JsonKind::Number; result.scalar = mSource.substr(start, mCursor - start); return result;
		}
		JsonNode parseValue(int depth)
		{
			if (depth > 32) fail();
			skipSpace(); if (mCursor >= mSource.size()) fail();
			if (mSource[mCursor] == '"') { JsonNode value; value.kind = JsonKind::String; value.scalar = parseString(); return value; }
			if (mSource[mCursor] == '{') return parseObject(depth + 1);
			if (mSource[mCursor] == '[') return parseArray(depth + 1);
			if (mSource[mCursor] == 't') { literal("true"); JsonNode value; value.kind = JsonKind::Boolean; value.boolean = true; return value; }
			if (mSource[mCursor] == 'f') { literal("false"); JsonNode value; value.kind = JsonKind::Boolean; return value; }
			if (mSource[mCursor] == 'n') { literal("null"); return {}; }
			return parseNumber();
		}
		JsonNode parseObject(int depth)
		{
			if (!take('{')) fail(); JsonNode result; result.kind = JsonKind::Object;
			if (take('}')) return result;
			for (;;)
			{
				const std::string name = parseString();
				if (std::any_of(result.object.begin(), result.object.end(), [&](const auto& entry) { return entry.first == name; })) fail();
				if (!take(':')) fail();
				result.object.emplace_back(name, parseValue(depth));
				if (take('}')) return result;
				if (!take(',')) fail();
			}
		}
		JsonNode parseArray(int depth)
		{
			if (!take('[')) fail(); JsonNode result; result.kind = JsonKind::Array;
			if (take(']')) return result;
			for (;;)
			{
				result.array.push_back(parseValue(depth));
				if (take(']')) return result;
				if (!take(',')) fail();
			}
		}
	};

	bool parseJson(const std::string& text, JsonNode& value)
	{
		try { value = StrictJsonParser(text).parse(); return true; }
		catch (const std::exception&) { value = JsonNode{}; return false; }
	}

	std::wstring scalarText(const JsonNode* value)
	{
		if (!value || (value->kind != JsonKind::String && value->kind != JsonKind::Number)) return {};
		return wide(value->scalar);
	}

	struct HttpResult
	{
		DWORD status{};
		std::string body;
	};

	void collectResults(const JsonNode& root, std::vector<JsonNode>& items)
	{
		if (root.kind == JsonKind::Array)
		{
			for (const auto& node : root.array) collectResults(node, items);
			return;
		}
		if (root.kind != JsonKind::Object) return;
		const JsonNode* results = root.member("results");
		if (results && results->kind == JsonKind::Array)
		{
			for (const auto& item : results->array) if (item.kind == JsonKind::Object) items.push_back(item);
		}
	}

	std::wstring apiError(const HttpResult& response)
	{
		JsonNode root;
		if (parseJson(response.body, root) && root.kind == JsonKind::Object)
		{
			for (const char* key : { "message", "error", "cause" })
			{
				const std::wstring value = scalarText(root.member(key));
				if (!value.empty()) return value;
			}
		}
		return L"HTTP " + std::to_wstring(response.status);
	}

	bool mercadoPagoRequest(const wchar_t* host, const std::wstring& path, const std::wstring& token,
		const wchar_t* method, const std::string& body, HttpResult& result, std::wstring& error)
	{
		result = {};
		HINTERNET session = WinHttpOpen(L"TurboRama PIX Owner Configurator/25", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
			WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
		if (!session) { error = L"O Windows nao abriu a conexao HTTPS."; return false; }
		WinHttpSetTimeouts(session, 10000, 15000, 15000, 30000);
		HINTERNET connection = WinHttpConnect(session, host, INTERNET_DEFAULT_HTTPS_PORT, 0);
		if (!connection) { WinHttpCloseHandle(session); error = L"Nao foi possivel conectar ao Mercado Pago."; return false; }
		HINTERNET request = WinHttpOpenRequest(connection, method, path.c_str(), nullptr, WINHTTP_NO_REFERER,
			WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_SECURE);
		if (!request)
		{
			WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
			error = L"Nao foi possivel preparar a chamada HTTPS."; return false;
		}
		DWORD disable = WINHTTP_DISABLE_REDIRECTS;
		WinHttpSetOption(request, WINHTTP_OPTION_DISABLE_FEATURE, &disable, sizeof(disable));
		std::wstring headers = L"Authorization: Bearer " + token + L"\r\nAccept: application/json\r\n";
		if (!body.empty()) headers += L"Content-Type: application/json\r\n";
		const BOOL sent = WinHttpSendRequest(request, headers.c_str(), (DWORD)-1L,
			body.empty() ? WINHTTP_NO_REQUEST_DATA : (LPVOID)body.data(), (DWORD)body.size(), (DWORD)body.size(), 0);
		SecureZeroMemory(headers.data(), headers.size() * sizeof(wchar_t));
		if (!sent || !WinHttpReceiveResponse(request, nullptr))
		{
			WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
			error = L"O Mercado Pago nao respondeu a chamada HTTPS."; return false;
		}
		DWORD statusSize = sizeof(result.status);
		WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
			WINHTTP_HEADER_NAME_BY_INDEX, &result.status, &statusSize, WINHTTP_NO_HEADER_INDEX);
		for (;;)
		{
			DWORD available = 0;
			if (!WinHttpQueryDataAvailable(request, &available) || available == 0) break;
			if (result.body.size() + available > 1024 * 1024)
			{
				WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
				error = L"A resposta do Mercado Pago excedeu o limite seguro."; return false;
			}
			const size_t offset = result.body.size();
			result.body.resize(offset + available);
			DWORD read = 0;
			if (!WinHttpReadData(request, result.body.data() + offset, available, &read))
			{
				WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
				error = L"Nao foi possivel ler a resposta do Mercado Pago."; return false;
			}
			result.body.resize(offset + read);
		}
		WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
		if (result.status < 200 || result.status >= 300)
		{
			error = L"Mercado Pago recusou a chamada: " + apiError(result);
			return false;
		}
		return true;
	}

	bool fetchMercadoPagoInventory(const std::wstring& token, MercadoPagoInventory& inventory, std::wstring& error)
	{
		inventory = {};
		HttpResult response;
		if (!mercadoPagoRequest(L"api.mercadolibre.com", L"/users/me", token, L"GET", {}, response, error)) return false;
		JsonNode account;
		if (!parseJson(response.body, account) || account.kind != JsonKind::Object)
		{ error = L"Mercado Pago retornou usuario invalido."; return false; }
		inventory.accountId = scalarText(account.member("id"));
		if (inventory.accountId.empty()) { error = L"Mercado Pago nao retornou o ID da conta."; return false; }

		const std::wstring storesPath = L"/users/" + inventory.accountId + L"/stores/search?limit=100";
		if (!mercadoPagoRequest(L"api.mercadopago.com", storesPath, token, L"GET", {}, response, error)) return false;
		JsonNode storesRoot;
		std::vector<JsonNode> storeItems;
		if (!parseJson(response.body, storesRoot)) { error = L"Lista de lojas retornou JSON invalido."; return false; }
		collectResults(storesRoot, storeItems);
		for (const auto& item : storeItems)
		{
			MercadoPagoStore store{ scalarText(item.member("id")), scalarText(item.member("external_id")), scalarText(item.member("name")) };
			if (!store.id.empty()) inventory.stores.push_back(std::move(store));
		}

		if (!mercadoPagoRequest(L"api.mercadopago.com", L"/pos?limit=100&offset=0", token, L"GET", {}, response, error)) return false;
		JsonNode posRoot;
		std::vector<JsonNode> posItems;
		if (!parseJson(response.body, posRoot)) { error = L"Lista de PDVs retornou JSON invalido."; return false; }
		collectResults(posRoot, posItems);
		for (const auto& item : posItems)
		{
			MercadoPagoPointOfSale pos{ scalarText(item.member("id")), scalarText(item.member("external_id")),
				scalarText(item.member("name")), scalarText(item.member("store_id")), scalarText(item.member("status")) };
			if (!pos.id.empty()) inventory.points.push_back(std::move(pos));
		}
		return true;
	}

	std::wstring inventorySummary(const MercadoPagoInventory& inventory)
	{
		std::wostringstream text;
		text << L"Conta Mercado Pago: " << inventory.accountId << L"\r\n\r\n";
		text << L"LOJAS\r\n";
		if (inventory.stores.empty()) text << L"(nenhuma loja encontrada)\r\n";
		for (const auto& store : inventory.stores)
			text << L"- " << (store.name.empty() ? L"sem nome" : store.name)
				<< L" | external_id=" << (store.externalId.empty() ? L"(vazio)" : store.externalId)
				<< L" | id=" << store.id << L"\r\n";
		text << L"\r\nPDVs / CAIXAS\r\n";
		if (inventory.points.empty()) text << L"(nenhum PDV encontrado)\r\n";
		for (const auto& pos : inventory.points)
			text << L"- " << (pos.name.empty() ? L"sem nome" : pos.name)
				<< L" | external_id=" << (pos.externalId.empty() ? L"(vazio)" : pos.externalId)
				<< L" | loja=" << pos.storeId
				<< L" | status=" << (pos.status.empty() ? L"active" : pos.status) << L"\r\n";
		return text.str();
	}

	bool chooseSingleCompatiblePair(const MercadoPagoInventory& inventory, MercadoPagoStore& store,
		MercadoPagoPointOfSale& pos, std::wstring& error)
	{
		std::vector<std::pair<MercadoPagoStore, MercadoPagoPointOfSale>> pairs;
		for (const auto& point : inventory.points)
		{
			if (!point.status.empty() && _wcsicmp(point.status.c_str(), L"active") != 0) continue;
			auto found = std::find_if(inventory.stores.begin(), inventory.stores.end(),
				[&](const auto& item) { return item.id == point.storeId; });
			if (found != inventory.stores.end() && !found->externalId.empty() && !point.externalId.empty())
				pairs.emplace_back(*found, point);
		}
		if (pairs.empty()) { error = L"Nenhum par ativo Loja/PDV com external_id foi encontrado nesta conta."; return false; }
		if (pairs.size() > 1) { error = L"Existe mais de um par Loja/PDV ativo. A lista foi mostrada; informe manualmente os external_id corretos."; return false; }
		store = pairs.front().first;
		pos = pairs.front().second;
		return true;
	}

	std::string jsonEscape(const std::wstring& value)
	{
		std::ostringstream output;
		for (unsigned char ch : utf8(value))
		{
			switch (ch)
			{
			case '\\': output << "\\\\"; break;
			case '"': output << "\\\""; break;
			case '\r': output << "\\r"; break;
			case '\n': output << "\\n"; break;
			case '\t': output << "\\t"; break;
			default:
				if (ch < 0x20) output << "\\u" << std::hex << std::setw(4) << std::setfill('0') << (int)ch << std::dec;
				else output << (char)ch;
			}
		}
		return output.str();
	}

	bool readAll(const std::wstring& file, std::string& value)
	{
		std::ifstream stream(file, std::ios::binary);
		if (!stream) return false;
		std::ostringstream output; output << stream.rdbuf(); value = output.str();
		return value.size() <= 1'048'576;
	}

	std::string jsonString(const std::string& json, const std::string& key, const std::string& fallback = {})
	{
		const std::string marker = "\"" + key + "\"";
		auto cursor = json.find(marker);
		if (cursor == std::string::npos) return fallback;
		cursor = json.find(':', cursor + marker.size());
		if (cursor == std::string::npos) return fallback;
		cursor = json.find('"', cursor + 1);
		if (cursor == std::string::npos) return fallback;
		std::string value;
		for (++cursor; cursor < json.size(); ++cursor)
		{
			const char ch = json[cursor];
			if (ch == '"') return value;
			if (ch != '\\') { value.push_back(ch); continue; }
			if (++cursor >= json.size()) return fallback;
			switch (json[cursor])
			{
			case '"': value.push_back('"'); break;
			case '\\': value.push_back('\\'); break;
			case '/': value.push_back('/'); break;
			case 'b': value.push_back('\b'); break;
			case 'f': value.push_back('\f'); break;
			case 'n': value.push_back('\n'); break;
			case 'r': value.push_back('\r'); break;
			case 't': value.push_back('\t'); break;
			default: return fallback;
			}
		}
		return fallback;
	}

	long long jsonInteger(const std::string& json, const std::string& key, long long fallback)
	{
		const std::string marker = "\"" + key + "\"";
		auto cursor = json.find(marker);
		if (cursor == std::string::npos) return fallback;
		cursor = json.find(':', cursor + marker.size());
		if (cursor == std::string::npos) return fallback;
		while (++cursor < json.size() && isspace((unsigned char)json[cursor])) {}
		if (cursor >= json.size()) return fallback;
		char* end = nullptr;
		const auto value = _strtoi64(json.c_str() + cursor, &end, 10);
		return end != json.c_str() + cursor ? value : fallback;
	}

	std::wstring safeAccountLabel(std::wstring value, const std::wstring& fallback)
	{
		value = trim(std::move(value));
		if (value.empty() || value.size() > 256
			|| std::any_of(value.begin(), value.end(), [](wchar_t ch) { return ch < 0x20 || ch == 0x7f; }))
			return fallback;
		return value;
	}

	std::wstring accountNameForSid(PSID sid)
	{
		if (!sid || !IsValidSid(sid)) return {};
		DWORD nameSize = 0, domainSize = 0; SID_NAME_USE use{};
		LookupAccountSidW(nullptr, sid, nullptr, &nameSize, nullptr, &domainSize, &use);
		if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || nameSize == 0) return {};
		std::vector<wchar_t> name(nameSize), domain(domainSize ? domainSize : 1);
		if (!LookupAccountSidW(nullptr, sid, name.data(), &nameSize, domain.data(), &domainSize, &use)) return {};
		std::wstring result;
		if (domainSize && domain[0]) result.assign(domain.data());
		if (!result.empty()) result += L"\\";
		result += name.data();
		return result;
	}

	std::wstring currentProcessAccount()
	{
		HANDLE token{};
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return {};
		DWORD size = 0;
		GetTokenInformation(token, TokenUser, nullptr, 0, &size);
		std::vector<unsigned char> buffer(size);
		const bool read = size >= sizeof(TOKEN_USER)
			&& GetTokenInformation(token, TokenUser, buffer.data(), size, &size) != FALSE;
		std::wstring result = read ? accountNameForSid(((TOKEN_USER*)buffer.data())->User.Sid) : L"";
		CloseHandle(token);
		return result;
	}

	std::wstring configuredKioskAccount()
	{
		std::string json;
		if (!readAll(L"C:\\TurboRama\\Config\\turborama.json", json)) return {};
		JsonNode root;
		if (!parseJson(json, root) || root.kind != JsonKind::Object) return {};
		const JsonNode* configured = root.member("kioskUser");
		if (!configured || configured->kind != JsonKind::String) return {};
		const std::wstring requested = safeAccountLabel(wide(configured->scalar), L"");
		if (requested.empty()) return {};

		DWORD sidSize = 0, domainSize = 0; SID_NAME_USE use{};
		LookupAccountNameW(nullptr, requested.c_str(), nullptr, &sidSize, nullptr, &domainSize, &use);
		if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || sidSize == 0) return requested;
		std::vector<unsigned char> sid(sidSize);
		std::vector<wchar_t> domain(domainSize ? domainSize : 1);
		if (!LookupAccountNameW(nullptr, requested.c_str(), sid.data(), &sidSize, domain.data(), &domainSize, &use)
			|| !IsValidSid(sid.data())) return requested;
		const auto resolved = accountNameForSid(sid.data());
		return resolved.empty() ? requested : resolved;
	}

	KioskIdentityDisplay kioskIdentityDisplay()
	{
		KioskIdentityDisplay display;
		display.currentAccount = safeAccountLabel(currentProcessAccount(), display.currentAccount);
		display.configuredAccount = safeAccountLabel(configuredKioskAccount(), display.configuredAccount);
		return display;
	}

	std::wstring priceText(long long cents)
	{
		if (cents < 0) cents = 0;
		std::wostringstream output;
		output << (cents / 100) << L',' << std::setw(2) << std::setfill(L'0') << (cents % 100);
		return output.str();
	}

	bool writeAll(const std::wstring& file, const std::string& value)
	{
		std::ofstream stream(file, std::ios::binary | std::ios::trunc);
		if (!stream) return false;
		stream.write(value.data(), (std::streamsize)value.size()); stream.flush();
		return stream.good();
	}

	std::wstring moduleDirectory()
	{
		wchar_t path[MAX_PATH * 4]{};
		GetModuleFileNameW(nullptr, path, (DWORD)(std::size(path)));
		std::wstring value(path);
		const auto slash = value.find_last_of(L"\\/");
		return slash == std::wstring::npos ? L"." : value.substr(0, slash);
	}

	std::wstring parentOf(const std::wstring& path)
	{
		const auto slash = path.find_last_of(L"\\/");
		return slash == std::wstring::npos ? L"" : path.substr(0, slash);
	}

	std::wstring join(const std::wstring& left, const std::wstring& right)
	{
		if (left.empty()) return right;
		return left + (left.back() == L'\\' ? L"" : L"\\") + right;
	}

	bool exists(const std::wstring& path)
	{
		const DWORD attributes = GetFileAttributesW(path.c_str());
		return attributes != INVALID_FILE_ATTRIBUTES && !(attributes & FILE_ATTRIBUTE_DIRECTORY);
	}

	bool directoryExists(const std::wstring& path)
	{
		const DWORD attributes = GetFileAttributesW(path.c_str());
		return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY);
	}

	bool bindPixAgentInstallation(const std::wstring& candidate, std::wstring& root,
		std::wstring& executable, std::wstring& assembly, std::wstring& bridge)
	{
		const auto runtime = join(candidate, L"pix-agent\\runtime\\dotnet.exe");
		const auto dll = join(candidate, L"pix-agent\\TurboRamaPixAgent.dll");
		const auto app = join(candidate, L"pix-agent\\TurboRamaPixAgent.exe");
		std::string trustError;
		if (exists(runtime) && exists(dll))
		{
			if (!PixBinaryTrust::verifyCommercialAgentBundle(join(candidate, L"pix-agent"), trustError)
				|| !PixBinaryTrust::verifyTrustedRuntime(runtime, trustError)
				|| !PixBinaryTrust::verifyVendorBinary(dll, trustError)) return false;
			root = candidate; executable = runtime; assembly = dll;
			bridge = join(candidate, L".emulationstation\\pix"); return true;
		}
		if (PixBinaryTrust::required()) return false;
		if (exists(app))
		{
			if (!PixBinaryTrust::verifyVendorBinary(app, trustError)) return false;
			root = candidate; executable = app; assembly.clear();
			bridge = join(candidate, L".emulationstation\\pix"); return true;
		}
		return false;
	}

	bool looksLikeInstalledEmulationStationRoot(const std::wstring& candidate)
	{
		return exists(join(candidate, L"emulationstation.exe"))
			&& directoryExists(join(candidate, L".emulationstation"))
			&& directoryExists(join(candidate, L"pix-agent"));
	}

	bool resolveInstallation(std::wstring& root, std::wstring& executable, std::wstring& assembly, std::wstring& bridge)
	{
		const std::wstring module = moduleDirectory();
		const std::wstring defaultInstall = L"D:\\emulationstation";

		// Se o configurador for aberto a partir de uma pasta de entrega/teste,
		// ele nao deve salvar o PIX nessa pasta: o EmulationStation le a ponte
		// da instalacao real. Usamos a pasta do proprio EXE primeiro somente
		// quando ela parece ser a instalacao completa do EmulationStation.
		if (looksLikeInstalledEmulationStationRoot(module)
			&& bindPixAgentInstallation(module, root, executable, assembly, bridge)) return true;
		if (bindPixAgentInstallation(defaultInstall, root, executable, assembly, bridge)) return true;
		if (bindPixAgentInstallation(module, root, executable, assembly, bridge)) return true;
		return false;
	}

	std::wstring normalizePath(std::wstring value)
	{
		wchar_t full[32768]{};
		const DWORD length = GetFullPathNameW(value.c_str(), (DWORD)std::size(full), full, nullptr);
		if (length > 0 && length < std::size(full)) value.assign(full, length);
		std::replace(value.begin(), value.end(), L'/', L'\\');
		std::transform(value.begin(), value.end(), value.begin(), towlower);
		return value;
	}

	enum class DaemonIdentityState { Absent, Unknown, Found };
	enum class DaemonStatusReadResult { Missing, Invalid, Unknown, Valid };
	struct DaemonStatus
	{
		DWORD pid{};
		ULONGLONG startFileTime{};
		std::string tokenHash;
	};

	const wchar_t* kDaemonSingletonMutex = L"Local\\TurboRamaPixAgent-Daemon-v1";
	const wchar_t* kManagerTokenEnvironment = L"TURBORAMA_PIX_MANAGER_TOKEN";
	const DWORD kDaemonIdentityStartupTimeoutMs = 90000;

	std::wstring daemonPidMutex(DWORD pid)
	{
		return L"Local\\TurboRamaPixAgent-Daemon-v1-" + std::to_wstring(pid);
	}

	DaemonIdentityState mutexState(const std::wstring& name)
	{
		HANDLE mutex = OpenMutexW(SYNCHRONIZE, FALSE, name.c_str());
		if (!mutex)
		{
			const DWORD error = GetLastError();
			return error == ERROR_FILE_NOT_FOUND ? DaemonIdentityState::Absent : DaemonIdentityState::Unknown;
		}
		CloseHandle(mutex);
		return DaemonIdentityState::Found;
	}

	bool uniqueKey(const std::string& json, const std::string& key, size_t& position)
	{
		const std::string marker = "\"" + key + "\"";
		position = json.find(marker);
		return position != std::string::npos && json.find(marker, position + marker.size()) == std::string::npos;
	}

	bool strictUnsigned(const std::string& json, const std::string& key, ULONGLONG& value)
	{
		size_t cursor = 0;
		if (!uniqueKey(json, key, cursor)) return false;
		cursor = json.find(':', cursor + key.size() + 2);
		if (cursor == std::string::npos) return false;
		do { ++cursor; } while (cursor < json.size() && isspace((unsigned char)json[cursor]));
		if (cursor >= json.size() || !isdigit((unsigned char)json[cursor])) return false;
		ULONGLONG parsed = 0;
		while (cursor < json.size() && isdigit((unsigned char)json[cursor]))
		{
			const unsigned digit = (unsigned)(json[cursor++] - '0');
			if (parsed > (ULLONG_MAX - digit) / 10) return false;
			parsed = parsed * 10 + digit;
		}
		while (cursor < json.size() && isspace((unsigned char)json[cursor])) ++cursor;
		if (cursor >= json.size() || (json[cursor] != ',' && json[cursor] != '}')) return false;
		value = parsed;
		return true;
	}

	bool strictAsciiString(const std::string& json, const std::string& key, std::string& value)
	{
		size_t cursor = 0;
		if (!uniqueKey(json, key, cursor)) return false;
		cursor = json.find(':', cursor + key.size() + 2);
		if (cursor == std::string::npos) return false;
		do { ++cursor; } while (cursor < json.size() && isspace((unsigned char)json[cursor]));
		if (cursor >= json.size() || json[cursor++] != '"') return false;
		value.clear();
		while (cursor < json.size() && json[cursor] != '"')
		{
			const unsigned char ch = (unsigned char)json[cursor++];
			if (ch < 0x20 || ch > 0x7e || ch == '\\') return false;
			value.push_back((char)ch);
		}
		if (cursor >= json.size() || json[cursor++] != '"') return false;
		while (cursor < json.size() && isspace((unsigned char)json[cursor])) ++cursor;
		return cursor < json.size() && (json[cursor] == ',' || json[cursor] == '}');
	}

	DaemonStatusReadResult readDaemonStatus(const std::wstring& bridge, DaemonStatus& status)
	{
		status = DaemonStatus{};
		const std::wstring file = join(bridge, L"agent-status.json");
		const DWORD attributes = GetFileAttributesW(file.c_str());
		if (attributes == INVALID_FILE_ATTRIBUTES)
		{
			const DWORD error = GetLastError();
			return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND
				? DaemonStatusReadResult::Missing : DaemonStatusReadResult::Unknown;
		}
		if ((attributes & FILE_ATTRIBUTE_DIRECTORY) || (attributes & FILE_ATTRIBUTE_REPARSE_POINT))
			return DaemonStatusReadResult::Invalid;
		std::string json;
		if (!readAll(file, json)) return DaemonStatusReadResult::Unknown;
		if (json.empty() || json.size() > 16384) return DaemonStatusReadResult::Invalid;
		ULONGLONG schema = 0, pid = 0, start = 0, updated = 0;
		std::string mode, hash;
		if (!strictUnsigned(json, "schemaVersion", schema) || schema != 2
			|| !strictUnsigned(json, "processId", pid) || pid == 0 || pid > MAXDWORD
			|| !strictUnsigned(json, "processStartFileTimeUtc", start) || start == 0
			|| !strictUnsigned(json, "updatedAtUnixSeconds", updated) || updated == 0
			|| !strictAsciiString(json, "mode", mode) || mode != "daemon"
			|| !strictAsciiString(json, "managerTokenHash", hash) || hash.size() != 64
			|| !std::all_of(hash.begin(), hash.end(), [](unsigned char ch) { return isxdigit(ch) != 0; }))
			return DaemonStatusReadResult::Invalid;
		std::transform(hash.begin(), hash.end(), hash.begin(), [](unsigned char ch) { return (char)tolower(ch); });
		status.pid = (DWORD)pid;
		status.startFileTime = start;
		status.tokenHash = hash;
		return DaemonStatusReadResult::Valid;
	}

	ULONGLONG fileTimeValue(const FILETIME& value)
	{
		ULARGE_INTEGER converted{};
		converted.LowPart = value.dwLowDateTime;
		converted.HighPart = value.dwHighDateTime;
		return converted.QuadPart;
	}

	DaemonIdentityState validateDaemon(const DaemonStatus& status, const std::wstring& expectedExecutable,
		HANDLE* retained = nullptr)
	{
		if (retained) *retained = nullptr;
		HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE
			| (retained ? PROCESS_TERMINATE : 0), FALSE, status.pid);
		if (!process)
		{
			const DWORD error = GetLastError();
			return error == ERROR_INVALID_PARAMETER ? DaemonIdentityState::Absent : DaemonIdentityState::Unknown;
		}
		DWORD exitCode{};
		wchar_t path[32768]{}; DWORD length = (DWORD)std::size(path);
		FILETIME creation{}, exit{}, kernel{}, user{};
		if (!GetExitCodeProcess(process, &exitCode)) { CloseHandle(process); return DaemonIdentityState::Unknown; }
		if (exitCode != STILL_ACTIVE) { CloseHandle(process); return DaemonIdentityState::Absent; }
		if (!QueryFullProcessImageNameW(process, 0, path, &length)
			|| !GetProcessTimes(process, &creation, &exit, &kernel, &user))
		{ CloseHandle(process); return DaemonIdentityState::Unknown; }
		if (normalizePath(path) != normalizePath(expectedExecutable)
			|| fileTimeValue(creation) != status.startFileTime)
		{ CloseHandle(process); return DaemonIdentityState::Absent; }
		if (mutexState(kDaemonSingletonMutex) != DaemonIdentityState::Found
			|| mutexState(daemonPidMutex(status.pid)) != DaemonIdentityState::Found)
		{ CloseHandle(process); return DaemonIdentityState::Unknown; }
		if (retained) *retained = process; else CloseHandle(process);
		return DaemonIdentityState::Found;
	}

	bool writeAtomically(const std::wstring& destination, const std::string& value)
	{
		const std::wstring temporary = destination + L"." + std::to_wstring(GetCurrentProcessId())
			+ L"." + std::to_wstring(GetTickCount64()) + L".tmp";
		DeleteFileW(temporary.c_str());
		if (!writeAll(temporary, value)) { DeleteFileW(temporary.c_str()); return false; }
		const bool moved = MoveFileExW(temporary.c_str(), destination.c_str(),
			MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE;
		if (!moved) DeleteFileW(temporary.c_str());
		return moved;
	}

	bool stopOnlyPixAgent(const std::wstring& bridge, const std::wstring& expectedExecutable, std::wstring& error)
	{
		const DaemonIdentityState singleton = mutexState(kDaemonSingletonMutex);
		if (singleton == DaemonIdentityState::Unknown)
		{ error = L"A identidade do servi\u00E7o PIX n\u00E3o pode ser consultada; nenhum processo foi encerrado."; return false; }
		DaemonStatus status;
		const DaemonStatusReadResult statusRead = readDaemonStatus(bridge, status);
		if (statusRead != DaemonStatusReadResult::Valid)
		{
			if (singleton == DaemonIdentityState::Absent
				&& (statusRead == DaemonStatusReadResult::Missing
					|| statusRead == DaemonStatusReadResult::Invalid)) return true;
			error = L"O estado de identidade do servi\u00E7o PIX est\u00E1 ausente ou inv\u00E1lido; nada foi encerrado.";
			return false;
		}
		HANDLE process = nullptr;
		const DaemonIdentityState validated = validateDaemon(status, expectedExecutable, &process);
		if (validated == DaemonIdentityState::Absent && singleton == DaemonIdentityState::Absent) return true;
		if (validated != DaemonIdentityState::Found || !process)
		{ error = L"O processo PIX n\u00E3o corresponde \u00E0 identidade autenticada; nada foi encerrado."; return false; }
		std::ostringstream request;
		request << "{\"schemaVersion\":1,\"mode\":\"daemon\",\"processId\":" << status.pid
			<< ",\"processStartFileTimeUtc\":" << status.startFileTime
			<< ",\"managerTokenHash\":\"" << status.tokenHash << "\"}";
		const std::wstring marker = join(bridge, L"agent-stop.request");
		bool stopped = writeAtomically(marker, request.str())
			&& WaitForSingleObject(process, 5000) == WAIT_OBJECT_0;
		if (!stopped)
		{
			if (validateDaemon(status, expectedExecutable) != DaemonIdentityState::Found)
			{ DeleteFileW(marker.c_str()); CloseHandle(process); error = L"A identidade do daemon mudou durante a parada."; return false; }
			stopped = TerminateProcess(process, 0) != FALSE
				&& WaitForSingleObject(process, 3000) == WAIT_OBJECT_0;
		}
		DeleteFileW(marker.c_str());
		CloseHandle(process);
		if (!stopped) error = L"O Windows n\u00E3o confirmou o encerramento do daemon PIX.";
		return stopped;
	}

	long long parsePrice(const std::wstring& input)
	{
		std::wstring clean;
		for (wchar_t ch : input) if (iswdigit(ch) || ch == L',' || ch == L'.') clean.push_back(ch);
		if (clean.empty()) return -1;
		const auto separator = clean.find_last_of(L",.");
		std::wstring whole = separator == std::wstring::npos ? clean : clean.substr(0, separator);
		std::wstring fraction = separator == std::wstring::npos ? L"00" : clean.substr(separator + 1);
		whole.erase(std::remove_if(whole.begin(), whole.end(), [](wchar_t ch) { return !iswdigit(ch); }), whole.end());
		fraction.erase(std::remove_if(fraction.begin(), fraction.end(), [](wchar_t ch) { return !iswdigit(ch); }), fraction.end());
		if (whole.empty()) whole = L"0";
		if (fraction.empty()) fraction = L"00";
		if (fraction.size() == 1) fraction += L"0";
		if (fraction.size() > 2) fraction.resize(2);
		try { return std::stoll(whole) * 100 + std::stoll(fraction); }
		catch (...) { return -1; }
	}

	bool validExternalId(const std::wstring& value, size_t maximum)
	{
		return value.empty() || (value.size() <= maximum
			&& std::all_of(value.begin(), value.end(), [](wchar_t ch) {
				return (ch >= L'0' && ch <= L'9') || (ch >= L'A' && ch <= L'Z') || (ch >= L'a' && ch <= L'z');
			}));
	}

	bool looksLikeMercadoPagoNumericId(const std::wstring& value)
	{
		return value.size() >= 8 && value.size() <= 24
			&& std::all_of(value.begin(), value.end(), [](wchar_t ch) { return ch >= L'0' && ch <= L'9'; });
	}

	bool isLegacyTestPosId(const std::wstring& value)
	{
		return _wcsicmp(value.c_str(), L"LZPIXCOMP") == 0;
	}

	bool collect(FormData& data, std::wstring& error)
	{
		data.adapter = SendMessageW(gProvider, CB_GETCURSEL, 0, 0) == 1;
		data.sandbox = SendMessageW(gEnvironment, CB_GETCURSEL, 0, 0) != 1;
		data.token = textOf(gToken); data.storeName = textOf(gStore); data.posName = textOf(gPos);
		data.storeExternalId = textOf(gStoreExternal); data.posExternalId = textOf(gPosExternal);
		data.cep = textOf(gCep); data.number = textOf(gNumber); data.reference = textOf(gReference);
		data.adapterUrl = textOf(gAdapterUrl); data.adapterId = textOf(gAdapterId);
		data.p15 = textOf(gPrice15); data.p30 = textOf(gPrice30); data.p45 = textOf(gPrice45);
		data.p60 = textOf(gPrice60); data.p120 = textOf(gPrice120);
		if (data.token.size() < (data.adapter ? 8u : 40u)) { error = L"Informe a credencial completa do provedor."; return false; }
		if (!data.adapter)
		{
			if (data.token.rfind(L"APP_USR-", 0) != 0) { error = L"Use o Access Token completo do ambiente selecionado, iniciado por APP_USR-."; return false; }
			std::wstring digits; for (wchar_t ch : data.cep) if (iswdigit(ch)) digits.push_back(ch); data.cep = digits;
			if (data.storeName.size() < 2 || data.posName.size() < 2) { error = L"Informe os nomes da loja e do caixa."; return false; }
			if (!validExternalId(data.storeExternalId, 60) || !validExternalId(data.posExternalId, 40))
			{ error = L"Os IDs externos aceitam somente letras e numeros (Loja ate 60; PDV ate 40)."; return false; }
			if (looksLikeMercadoPagoNumericId(data.storeExternalId)) data.storeExternalId.clear();
			if (looksLikeMercadoPagoNumericId(data.posExternalId)) data.posExternalId.clear();
			if (isLegacyTestPosId(data.posExternalId)) data.posExternalId.clear();
			if (data.cep.size() != 8 || data.number.empty()) { error = L"Informe CEP com 8 números e o número do estabelecimento."; return false; }
		}
		else if (data.adapterUrl.empty() || data.adapterId.size() < 2)
		{ error = L"Informe o endereço e o identificador do adaptador bancário."; return false; }
		for (const auto& price : { data.p15, data.p30, data.p45, data.p60, data.p120 })
			if (parsePrice(price) < 50) { error = L"Todos os pacotes precisam custar pelo menos R$ 0,50."; return false; }
		return true;
	}

	std::string configurationJson(const FormData& data)
	{
		std::ostringstream json;
		json << "{\n  \"schemaVersion\": 1,\n  \"provider\": \"" << (data.adapter ? "adapter" : "mercadopago") << "\",\n"
			<< "  \"mercadoPagoEnvironment\": \"" << (data.sandbox ? "sandbox" : "production") << "\",\n"
			<< "  \"storeName\": \"" << jsonEscape(data.storeName) << "\",\n"
			<< "  \"storeExternalId\": \"" << jsonEscape(data.storeExternalId) << "\",\n"
			<< "  \"posName\": \"" << jsonEscape(data.posName) << "\",\n"
			<< "  \"posExternalId\": \"" << jsonEscape(data.posExternalId) << "\",\n"
			<< "  \"postalCode\": \"" << jsonEscape(data.cep) << "\",\n"
			<< "  \"streetNumber\": \"" << jsonEscape(data.number) << "\",\n"
			<< "  \"reference\": \"" << jsonEscape(data.reference) << "\",\n"
			<< "  \"adapterBaseUrl\": \"" << jsonEscape(data.adapterUrl) << "\",\n"
			<< "  \"adapterProviderId\": \"" << jsonEscape(data.adapterId) << "\",\n"
			<< "  \"packagePricesCents\": {"
			<< "\"15\":" << parsePrice(data.p15) << ",\"30\":" << parsePrice(data.p30)
			<< ",\"45\":" << parsePrice(data.p45) << ",\"60\":" << parsePrice(data.p60)
			<< ",\"120\":" << parsePrice(data.p120) << "}\n}";
		return json.str();
	}

	std::wstring tempConfigurationFile()
	{
		wchar_t directory[MAX_PATH]{}; GetTempPathW(MAX_PATH, directory);
		return join(directory, L"turborama-owner-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()) + L".json");
	}

	bool createChildWithPipes(const std::wstring& executable, const std::wstring& command, const std::wstring& working,
		const std::string& input, DWORD& exitCode, std::string& output, std::wstring& error,
		bool& exitConfirmed)
	{
		exitConfirmed = true;
		SECURITY_ATTRIBUTES attributes{ sizeof(SECURITY_ATTRIBUTES), nullptr, TRUE };
		HANDLE stdinRead{}, stdinWrite{}, stdoutRead{}, stdoutWrite{};
		auto closePipes = [&]() {
			auto closeOne = [](HANDLE& handle) { if (handle) CloseHandle(handle); handle = nullptr; };
			closeOne(stdinRead); closeOne(stdinWrite); closeOne(stdoutRead); closeOne(stdoutWrite);
		};
		if (!CreatePipe(&stdinRead, &stdinWrite, &attributes, 0) || !CreatePipe(&stdoutRead, &stdoutWrite, &attributes, 0))
		{ closePipes(); error = L"O Windows não conseguiu criar a comunicação segura com o agente PIX."; return false; }
		if (!SetHandleInformation(stdinWrite, HANDLE_FLAG_INHERIT, 0)
			|| !SetHandleInformation(stdoutRead, HANDLE_FLAG_INHERIT, 0))
		{ closePipes(); error = L"O Windows não conseguiu proteger os handles da comunicação com o agente PIX."; return false; }
		STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
		startup.hStdInput = stdinRead; startup.hStdOutput = stdoutWrite; startup.hStdError = stdoutWrite; startup.wShowWindow = SW_HIDE;
		PROCESS_INFORMATION process{}; std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
		std::vector<wchar_t> environment;
		std::string environmentError;
		if (!PixBinaryTrust::buildSanitizedDotnetEnvironment(
			join(working, L"pix-agent\\runtime"), {}, environment, environmentError))
		{
			closePipes();
			error = L"O Windows n\u00E3o conseguiu preparar o ambiente protegido do agente PIX.";
			return false;
		}
		const BOOL created = CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, TRUE,
			CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
			environment.data(), working.c_str(), &startup, &process);
		SecureZeroMemory(environment.data(), environment.size() * sizeof(wchar_t));
		CloseHandle(stdinRead); CloseHandle(stdoutWrite);
		if (!created)
		{
			CloseHandle(stdinWrite); CloseHandle(stdoutRead);
			error = L"Não foi possível iniciar o agente PIX (Windows " + std::to_wstring(GetLastError()) + L")."; return false;
		}
		exitConfirmed = false;
		std::string line = input + "\r\n"; DWORD written{};
		WriteFile(stdinWrite, line.data(), (DWORD)line.size(), &written, nullptr);
		SecureZeroMemory(line.data(), line.size()); CloseHandle(stdinWrite);
		const DWORD wait = WaitForSingleObject(process.hProcess, 120000);
		if (wait != WAIT_OBJECT_0)
		{
			const bool terminated = TerminateProcess(process.hProcess, 21) != FALSE;
			exitConfirmed = terminated && WaitForSingleObject(process.hProcess, 5000) == WAIT_OBJECT_0;
			error = exitConfirmed
				? L"A validacao ultrapassou 2 minutos e o processo foi encerrado."
				: L"A validacao ultrapassou o prazo e o Windows nao confirmou o encerramento; nenhuma outra operacao sera iniciada.";
		}
		else exitConfirmed = true;
		if (exitConfirmed) GetExitCodeProcess(process.hProcess, &exitCode);
		else exitCode = STILL_ACTIVE;
		CloseHandle(process.hThread); CloseHandle(process.hProcess);
		char buffer[4096]; DWORD received{};
		if (exitConfirmed)
			while (ReadFile(stdoutRead, buffer, sizeof(buffer), &received, nullptr) && received) output.append(buffer, buffer + received);
		CloseHandle(stdoutRead);
		return wait == WAIT_OBJECT_0 && exitConfirmed;
	}

	std::string sha256Hex(const std::string& value)
	{
		BCRYPT_ALG_HANDLE algorithm{}; BCRYPT_HASH_HANDLE hash{};
		DWORD objectSize{}, received{}; std::vector<unsigned char> object, digest(32);
		if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) return {};
		if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, (PUCHAR)&objectSize,
			sizeof(objectSize), &received, 0) < 0)
		{ BCryptCloseAlgorithmProvider(algorithm, 0); return {}; }
		object.resize(objectSize);
		if (BCryptCreateHash(algorithm, &hash, object.data(), objectSize, nullptr, 0, 0) < 0)
		{ BCryptCloseAlgorithmProvider(algorithm, 0); return {}; }
		NTSTATUS status = BCryptHashData(hash, (PUCHAR)value.data(), (ULONG)value.size(), 0);
		if (status >= 0) status = BCryptFinishHash(hash, digest.data(), (ULONG)digest.size(), 0);
		BCryptDestroyHash(hash); BCryptCloseAlgorithmProvider(algorithm, 0);
		if (status < 0) return {};
		std::ostringstream output; output << std::hex << std::setfill('0');
		for (unsigned char byte : digest) output << std::setw(2) << (int)byte;
		return output.str();
	}

	bool createManagerToken(std::string& token)
	{
		unsigned char bytes[32]{};
		if (BCryptGenRandom(nullptr, bytes, sizeof(bytes), BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0)
		{ SecureZeroMemory(bytes, sizeof(bytes)); return false; }
		std::ostringstream output; output << std::hex << std::setfill('0');
		for (unsigned char byte : bytes) output << std::setw(2) << (int)byte;
		token = output.str();
		SecureZeroMemory(bytes, sizeof(bytes));
		return token.size() == 64;
	}

	bool daemonEnvironment(const std::wstring& root, const std::string& token, std::vector<wchar_t>& environment)
	{
		std::string ignoredError;
		return PixBinaryTrust::buildSanitizedDotnetEnvironment(
			join(root, L"pix-agent\\runtime"),
			{ { kManagerTokenEnvironment, wide(token) } }, environment, ignoredError);
	}

	bool restartAgent(const std::wstring& root, const std::wstring& executable, const std::wstring& assembly,
		const std::wstring& bridge, std::wstring& error)
	{
		if (mutexState(kDaemonSingletonMutex) != DaemonIdentityState::Absent)
		{ error = L"J\u00E1 existe um daemon PIX ou sua identidade n\u00E3o pode ser consultada."; return false; }
		std::string token;
		if (!createManagerToken(token))
		{ error = L"O Windows n\u00E3o conseguiu gerar a identidade do daemon PIX."; return false; }
		const std::string tokenHash = sha256Hex(token);
		std::vector<wchar_t> environment;
		if (tokenHash.size() != 64 || !daemonEnvironment(root, token, environment))
		{
			SecureZeroMemory(token.data(), token.size());
			if (!environment.empty()) SecureZeroMemory(environment.data(), environment.size() * sizeof(wchar_t));
			error = L"N\u00E3o foi poss\u00EDvel preparar o ambiente seguro do daemon PIX."; return false;
		}
		std::wstring command = L"\"" + executable + L"\" ";
		if (!assembly.empty()) command += L"\"" + assembly + L"\" ";
		command += L"--daemon --bridge \"" + bridge + L"\"";
		std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
		STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_HIDE;
		PROCESS_INFORMATION process{};
		const BOOL created = CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
			CREATE_NO_WINDOW | CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT,
			environment.data(), root.c_str(), &startup, &process);
		const DWORD createError = created ? ERROR_SUCCESS : GetLastError();
		SecureZeroMemory(token.data(), token.size());
		SecureZeroMemory(environment.data(), environment.size() * sizeof(wchar_t));
		if (!created)
		{ error = L"N\u00E3o foi poss\u00EDvel reiniciar o daemon PIX (Windows " + std::to_wstring(createError) + L")."; return false; }
		FILETIME creation{}, exit{}, kernel{}, user{};
		const bool creationRead = GetProcessTimes(process.hProcess, &creation, &exit, &kernel, &user) != FALSE;
		const ULONGLONG startFileTime = creationRead ? fileTimeValue(creation) : 0;
		const bool resumed = creationRead && startFileTime != 0 && ResumeThread(process.hThread) != (DWORD)-1;
		CloseHandle(process.hThread);
		if (!resumed)
		{
			TerminateProcess(process.hProcess, 22);
			const bool stopped = WaitForSingleObject(process.hProcess, 3000) == WAIT_OBJECT_0;
			CloseHandle(process.hProcess);
			error = stopped ? L"O Windows n\u00E3o ativou o daemon PIX." : L"A falha de inicializa\u00E7\u00E3o do daemon n\u00E3o foi encerrada.";
			return false;
		}
		const ULONGLONG deadline = GetTickCount64() + kDaemonIdentityStartupTimeoutMs;
		while (GetTickCount64() < deadline)
		{
			if (WaitForSingleObject(process.hProcess, 0) == WAIT_OBJECT_0) break;
			DaemonStatus status;
			if (readDaemonStatus(bridge, status) == DaemonStatusReadResult::Valid
				&& status.pid == process.dwProcessId && status.startFileTime == startFileTime
				&& status.tokenHash == tokenHash
				&& validateDaemon(status, executable) == DaemonIdentityState::Found)
			{ CloseHandle(process.hProcess); return true; }
			Sleep(50);
		}
		DWORD exitCode = STILL_ACTIVE; GetExitCodeProcess(process.hProcess, &exitCode);
		bool stopped = WaitForSingleObject(process.hProcess, 0) == WAIT_OBJECT_0;
		if (!stopped && TerminateProcess(process.hProcess, 22))
			stopped = WaitForSingleObject(process.hProcess, 3000) == WAIT_OBJECT_0;
		CloseHandle(process.hProcess);
		if (exitCode != STILL_ACTIVE)
			error = L"O daemon PIX encerrou antes de publicar sua identidade (c\u00F3digo " + std::to_wstring(exitCode) + L").";
		else error = stopped ? L"O daemon PIX n\u00E3o confirmou sua identidade no prazo seguro."
			: L"O daemon PIX n\u00E3o confirmou identidade e seu encerramento falhou.";
		return false;
	}

	bool preflightKioskIdentity(std::wstring& error)
	{
		std::wstring root, executable, assembly, bridge;
		if (!resolveInstallation(root, executable, assembly, bridge))
		{
			error = L"Agente PIX não encontrado. Instale primeiro o pacote comercial TurboRama.";
			return false;
		}
		std::wstring command = L"\"" + executable + L"\" ";
		if (!assembly.empty()) command += L"\"" + assembly + L"\" ";
		command += L"--check-kiosk-identity";
		std::string output;
		DWORD exitCode = 999;
		bool exitConfirmed = true;
		if (!createChildWithPipes(executable, command, root, "", exitCode, output, error, exitConfirmed)) return false;
		if (exitCode == 0) return true;
		error = trim(wide(output));
		if (error.empty())
			error = L"A sessão Windows atual não é o usuário automático do quiosque configurado no Launcher. Abra pelo menu do EmulationStation e tente novamente.";
		return false;
	}

	WorkerResult configure(FormData data)
	{
		struct TokenClearGuard
		{
			explicit TokenClearGuard(std::wstring& value) : token(value) {}
			~TokenClearGuard()
			{
				if (!token.empty())
					SecureZeroMemory(token.data(), token.size() * sizeof(wchar_t));
			}
			TokenClearGuard(const TokenClearGuard&) = delete;
			TokenClearGuard& operator=(const TokenClearGuard&) = delete;
			std::wstring& token;
		} tokenClear(data.token);
		WorkerResult result;
		std::wstring root, executable, assembly, bridge;
		if (!resolveInstallation(root, executable, assembly, bridge))
		{ result.message = L"Agente PIX não encontrado. Instale primeiro o pacote comercial TurboRama."; return result; }
		const std::wstring temporary = tempConfigurationFile();
		if (!writeAll(temporary, configurationJson(data)))
		{ result.message = L"Não foi possível preparar o cadastro temporário."; return result; }
		std::wstring stopError;
		if (!stopOnlyPixAgent(bridge, executable, stopError))
		{
			DeleteFileW(temporary.c_str());
			result.message = stopError.empty()
				? L"O daemon PIX nao teve a parada confirmada; configuracao cancelada."
				: stopError;
			return result;
		}
		std::wstring command = L"\"" + executable + L"\" ";
		if (!assembly.empty()) command += L"\"" + assembly + L"\" ";
		command += L"--configure-owner \"" + temporary + L"\" --bridge \"" + bridge + L"\"";
		std::string output; DWORD exitCode = 999; std::wstring error;
		std::string credential = utf8(data.token);
		bool childExitConfirmed = true;
		const bool ran = createChildWithPipes(executable, command, root, credential, exitCode, output, error, childExitConfirmed);
		SecureZeroMemory(credential.data(), credential.size());
		if (childExitConfirmed) DeleteFileW(temporary.c_str());
		else
		{
			result.message = error.empty()
				? L"O Windows nao confirmou a saida do configurador PIX; nenhuma reinicializacao foi iniciada."
				: error;
			return result;
		}
		if (!ran || exitCode != 0)
		{
			result.message = !error.empty() ? error : trim(wide(output));
			if (result.message.empty()) result.message = L"A validacao nao foi concluida. Compras permanecem bloqueadas; um cadastro pendente pode ter sido preservado para retomada segura.";
			std::wstring restartError;
			if (!restartAgent(root, executable, assembly, bridge, restartError) && !restartError.empty())
				result.message += L" O servico anterior tambem nao pode ser reiniciado: " + restartError;
			return result;
		}
		std::wstring restartError;
		if (!restartAgent(root, executable, assembly, bridge, restartError))
		{
			result.message = L"Cadastro salvo, mas o daemon PIX nao confirmou a reinicializacao: " + restartError;
			return result;
		}
		result.ok = true;
		result.message = data.adapter
			? L"Adaptador bancário validado e ativado. O EmulationStation já pode gerar cobranças PIX."
			: L"Conta reconhecida. Loja e caixa foram criados ou reaproveitados e o PIX está pronto.";
		return result;
	}

	bool loadExistingRegistration(std::wstring& message)
	{
		std::wstring root, executable, assembly, bridge;
		if (!resolveInstallation(root, executable, assembly, bridge))
		{
			message = L"Instalação comercial TurboRama não encontrada.";
			return false;
		}
		std::string json;
		if (!readAll(join(bridge, L"owner-settings.json"), json))
		{
			message = L"Ainda não existe cadastro PIX salvo nesta instalação.";
			return false;
		}
		const bool adapter = jsonString(json, "provider", "mercadopago") == "adapter";
		SendMessageW(gProvider, CB_SETCURSEL, adapter ? 1 : 0, 0);
		const bool sandbox = jsonString(json, "mercadoPagoEnvironment", "production") == "sandbox";
		SendMessageW(gEnvironment, CB_SETCURSEL, sandbox ? 0 : 1, 0);
		SetWindowTextW(gStore, wide(jsonString(json, "storeName", "TurboRamaX")).c_str());
		SetWindowTextW(gPos, wide(jsonString(json, "posName", "TurboRama Kiosk")).c_str());
		SetWindowTextW(gStoreExternal, wide(jsonString(json, "storeExternalId")).c_str());
		auto savedPosExternal = wide(jsonString(json, "posExternalId"));
		if (isLegacyTestPosId(savedPosExternal)) savedPosExternal.clear();
		SetWindowTextW(gPosExternal, savedPosExternal.c_str());
		SetWindowTextW(gCep, wide(jsonString(json, "postalCode")).c_str());
		SetWindowTextW(gNumber, wide(jsonString(json, "streetNumber")).c_str());
		SetWindowTextW(gReference, wide(jsonString(json, "reference", "TurboRama")).c_str());
		SetWindowTextW(gAdapterUrl, wide(jsonString(json, "adapterBaseUrl", "http://127.0.0.1:8765/")).c_str());
		SetWindowTextW(gAdapterId, wide(jsonString(json, "adapterProviderId", "meu-banco")).c_str());
		const std::pair<HWND, const char*> prices[] = {
			{gPrice15,"15"},{gPrice30,"30"},{gPrice45,"45"},{gPrice60,"60"},{gPrice120,"120"}
		};
		for (const auto& price : prices)
		{
			const auto text = priceText(jsonInteger(json, price.second, 100));
			SetWindowTextW(price.first, text.c_str());
		}
		updateProvider();
		message = L"Cadastro carregado. Por segurança, cole novamente a credencial somente ao salvar alterações.";
		return true;
	}

	void setStatus(const std::wstring& value, bool error = false)
	{
		SetWindowTextW(gStatus, value.c_str());
		SetWindowLongPtrW(gStatus, GWLP_USERDATA, error ? 1 : 0);
		// O controle e transparente; redesenhar somente o STATIC deixava partes
		// da mensagem anterior no fundo, sobrepondo o novo status. Repintamos o
		// cartao inteiro e seus filhos para sempre limpar o texto anterior.
		RECT statusCard{ 316, 630, 996, 690 };
		RedrawWindow(gWindow, &statusCard, nullptr,
			RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
	}

	void enableForm(bool enabled)
	{
		for (HWND control : { gProvider,gEnvironment,gToken,gShow,gStore,gPos,gStoreExternal,gPosExternal,
			gCep,gNumber,gReference,gAdapterUrl,gAdapterId,gManage,
			gPrice15,gPrice30,gPrice45,gPrice60,gPrice120,gConfigure,gLoad,gClose }) EnableWindow(control, enabled);
	}

	void clearTokenControl()
	{
		if (gToken)
		{
			SetWindowTextW(gToken, L"");
			SendMessageW(gToken, EM_SETPASSWORDCHAR, 0x25CF, 0);
			RedrawWindow(gToken, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW);
		}
		if (gShow)
		{
			SetWindowTextW(gShow, L"MOSTRAR");
			RedrawWindow(gShow, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW);
		}
	}

	void updateProvider()
	{
		const bool adapter = SendMessageW(gProvider, CB_GETCURSEL, 0, 0) == 1;
		for (HWND control : { gEnvironment,gStore,gPos,gStoreExternal,gPosExternal,gCep,gNumber,gReference })
			ShowWindow(control, adapter ? SW_HIDE : SW_SHOW);
		for (HWND control : { gAdapterUrl,gAdapterId }) ShowWindow(control, adapter ? SW_SHOW : SW_HIDE);
		for (HWND control : gMercadoPagoLabels) ShowWindow(control, adapter ? SW_HIDE : SW_SHOW);
		for (HWND control : gAdapterLabels) ShowWindow(control, adapter ? SW_SHOW : SW_HIDE);
		MoveWindow(gProvider, 316, 169, adapter ? 680 : 420, 220, TRUE);
		clearTokenControl();
		setStatus(adapter
			? L"Informe o endpoint e o segredo do adaptador compatível com o contrato TurboRama."
			: (SendMessageW(gEnvironment, CB_GETCURSEL, 0, 0) == 1
				? L"PRODUCAO REAL: a conta, Loja e PDV serao validados antes de liberar cobrancas."
				: L"AMBIENTE DE TESTE: use somente credenciais e compradores de teste do Mercado Pago."));
		InvalidateRect(gWindow, nullptr, TRUE);
	}

	HWND label(HWND parent, const wchar_t* text, int x, int y, int w, int h)
	{
		HWND control = CreateWindowExW(WS_EX_TRANSPARENT, L"STATIC", text, WS_CHILD | WS_VISIBLE, x,y,w,h,parent,nullptr,nullptr,nullptr);
		SendMessageW(control, WM_SETFONT, (WPARAM)gSmallFont, TRUE); return control;
	}

	HWND edit(HWND parent, int id, const wchar_t* value, int x, int y, int w, int h, DWORD extra = 0)
	{
		HWND control = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", value, WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL | extra,
			x,y,w,h,parent,(HMENU)(INT_PTR)id,nullptr,nullptr);
		SendMessageW(control, WM_SETFONT, (WPARAM)gFont, TRUE); SendMessageW(control, EM_SETLIMITTEXT, 4096, 0); return control;
	}

	HWND button(HWND parent, int id, const wchar_t* text, int x, int y, int w, int h)
	{
		HWND control = CreateWindowExW(0, L"BUTTON", text, WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
			x,y,w,h,parent,(HMENU)(INT_PTR)id,nullptr,nullptr);
		SendMessageW(control, WM_SETFONT, (WPARAM)gFont, TRUE); return control;
	}

	void createControls(HWND window)
	{
		gTitleFont = CreateFontW(30,0,0,0,FW_BOLD,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");
		gHeaderFont = CreateFontW(21,0,0,0,FW_SEMIBOLD,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");
		gFont = CreateFontW(17,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");
		gSmallFont = CreateFontW(14,0,0,0,FW_SEMIBOLD,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");
		gMonoFont = CreateFontW(17,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Consolas");
		gHeroFont = CreateFontW(40,0,0,0,FW_BOLD,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");
		gStepFont = CreateFontW(16,0,0,0,FW_SEMIBOLD,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");

		label(window,L"PROVEDOR DE PAGAMENTO",316,146,260,22);
		gProvider = CreateWindowExW(0,L"COMBOBOX",L"",WS_CHILD|WS_VISIBLE|WS_TABSTOP|CBS_DROPDOWNLIST,316,169,420,220,window,(HMENU)(INT_PTR)ID_PROVIDER,nullptr,nullptr);
		SendMessageW(gProvider,WM_SETFONT,(WPARAM)gFont,TRUE); SendMessageW(gProvider,CB_ADDSTRING,0,(LPARAM)L"Mercado Pago — conta própria ou autorizada");
		SendMessageW(gProvider,CB_ADDSTRING,0,(LPARAM)L"Outro banco — adaptador TurboRama"); SendMessageW(gProvider,CB_SETCURSEL,0,0);
		gMercadoPagoLabels.push_back(label(window,L"AMBIENTE",750,146,180,22));
		gEnvironment=CreateWindowExW(0,L"COMBOBOX",L"",WS_CHILD|WS_VISIBLE|WS_TABSTOP|CBS_DROPDOWNLIST,750,169,246,220,window,(HMENU)(INT_PTR)ID_ENVIRONMENT,nullptr,nullptr);
		SendMessageW(gEnvironment,WM_SETFONT,(WPARAM)gFont,TRUE);
		SendMessageW(gEnvironment,CB_ADDSTRING,0,(LPARAM)L"TESTE - sem dinheiro real");
		SendMessageW(gEnvironment,CB_ADDSTRING,0,(LPARAM)L"PRODUCAO - cobrancas reais");
		SendMessageW(gEnvironment,CB_SETCURSEL,0,0);

		label(window,L"ACCESS TOKEN — CREDENCIAL PROTEGIDA",316,218,390,22); gToken=edit(window,ID_TOKEN,L"",316,241,438,38,ES_PASSWORD);
		SendMessageW(gToken,EM_SETPASSWORDCHAR,0x25CF,0); gShow=button(window,ID_SHOW,L"MOSTRAR",766,241,102,38);
		gManage=button(window,ID_MANAGE,L"VER CADASTROS",880,241,116,38);

		gMercadoPagoLabels.push_back(label(window,L"NOME DO ESTABELECIMENTO",316,308,280,22)); gStore=edit(window,ID_STORE,L"TurboRamaX",316,331,330,38);
		gMercadoPagoLabels.push_back(label(window,L"NOME DO CAIXA / PDV",662,308,260,22)); gPos=edit(window,ID_POS,L"TurboRama Kiosk",662,331,334,38);
		gMercadoPagoLabels.push_back(label(window,L"ID EXTERNO DA LOJA (OPCIONAL)",316,380,300,22)); gStoreExternal=edit(window,ID_STORE_EXTERNAL,L"",316,403,330,38);
		gMercadoPagoLabels.push_back(label(window,L"ID EXTERNO DO PDV (OPCIONAL)",662,380,300,22)); gPosExternal=edit(window,ID_POS_EXTERNAL,L"",662,403,334,38);
		SendMessageW(gStoreExternal,EM_SETLIMITTEXT,60,0); SendMessageW(gPosExternal,EM_SETLIMITTEXT,40,0);
		gMercadoPagoLabels.push_back(label(window,L"CEP",316,452,100,22)); gCep=edit(window,ID_CEP,L"57084648",316,475,190,38);
		gMercadoPagoLabels.push_back(label(window,L"NUMERO / COMPLEMENTO",520,452,210,22)); gNumber=edit(window,ID_NUMBER,L"52",520,475,190,38);
		gMercadoPagoLabels.push_back(label(window,L"REFERENCIA",724,452,160,22)); gReference=edit(window,ID_REFERENCE,L"TurboRama",724,475,272,38);

		gAdapterLabels.push_back(label(window,L"ENDERECO SEGURO DO ADAPTADOR",316,308,340,22)); gAdapterUrl=edit(window,ID_ADAPTER_URL,L"http://127.0.0.1:8765/",316,331,680,38);
		gAdapterLabels.push_back(label(window,L"IDENTIFICADOR DO PROVEDOR",316,380,280,22)); gAdapterId=edit(window,ID_ADAPTER_ID,L"meu-banco",316,403,680,38);
		ShowWindow(gAdapterUrl,SW_HIDE); ShowWindow(gAdapterId,SW_HIDE);

		label(window,L"PACOTES DE TEMPO — VALOR EM REAIS",316,535,360,22);
		const int xs[] = {316,456,596,736,876}; const wchar_t* captions[]={L"15 MIN",L"30 MIN",L"45 MIN",L"60 MIN",L"120 MIN"};
		HWND* fields[]={&gPrice15,&gPrice30,&gPrice45,&gPrice60,&gPrice120}; const int ids[]={ID_PRICE15,ID_PRICE30,ID_PRICE45,ID_PRICE60,ID_PRICE120};
		const wchar_t* values[]={L"1,00",L"2,00",L"3,00",L"4,00",L"8,00"};
		for(int i=0;i<5;++i){label(window,captions[i],xs[i],562,118,20);*fields[i]=edit(window,ids[i],values[i],xs[i],580,120,38);}

		gStatus=CreateWindowExW(WS_EX_TRANSPARENT,L"STATIC",L"",WS_CHILD|WS_VISIBLE|SS_LEFT|SS_NOPREFIX,
			350,638,628,46,window,nullptr,nullptr,nullptr);
		SendMessageW(gStatus,WM_SETFONT,(WPARAM)gSmallFont,TRUE);
		gConfigure=button(window,ID_CONFIGURE,L"VALIDAR E ATIVAR PIX",316,701,408,56);
		gLoad=button(window,ID_LOAD,L"CARREGAR CADASTRO",738,701,258,56);
		gClose=button(window,ID_CLOSE,L"FECHAR",88,710,130,38);
		updateProvider();
	}

	void drawButton(const DRAWITEMSTRUCT* item)
	{
		const bool primary = item->CtlID == ID_CONFIGURE;
		const bool pressed = (item->itemState & ODS_SELECTED) != 0;
		const bool disabled = (item->itemState & ODS_DISABLED) != 0;
		const COLORREF fill = disabled ? RGB(36,45,55)
			: primary ? (pressed ? RGB(0,137,174) : RGB(0,190,231)) : (pressed ? RGB(34,48,68) : RGB(18,31,48));
		RECT area=item->rcItem; InflateRect(&area,-1,-1);
		HBRUSH brush=CreateSolidBrush(fill); HPEN pen=CreatePen(PS_SOLID,primary?2:1,primary?RGB(84,225,255):RGB(74,94,119));
		HGDIOBJ oldBrush=SelectObject(item->hDC,brush); HGDIOBJ oldPen=SelectObject(item->hDC,pen);
		RoundRect(item->hDC,area.left,area.top,area.right,area.bottom,12,12);
		SelectObject(item->hDC,oldPen);SelectObject(item->hDC,oldBrush);DeleteObject(pen);DeleteObject(brush);
		SetBkMode(item->hDC,TRANSPARENT); SetTextColor(item->hDC,disabled?RGB(125,135,145):(primary?RGB(5,15,25):RGB(230,239,249))); SelectObject(item->hDC,gFont);
		wchar_t caption[128]{}; GetWindowTextW(item->hwndItem,caption,128); DrawTextW(item->hDC,caption,-1,&area,DT_CENTER|DT_VCENTER|DT_SINGLELINE);
	}

	void fillBox(HDC dc, const RECT& area, COLORREF background, COLORREF border, int radius=14, int width=1)
	{
		HBRUSH brush=CreateSolidBrush(background);HPEN pen=CreatePen(PS_SOLID,width,border);
		HGDIOBJ oldBrush=SelectObject(dc,brush),oldPen=SelectObject(dc,pen);
		RoundRect(dc,area.left,area.top,area.right,area.bottom,radius,radius);
		SelectObject(dc,oldPen);SelectObject(dc,oldBrush);DeleteObject(pen);DeleteObject(brush);
	}

	void drawTextLine(HDC dc,HFONT font,COLORREF color,int x,int y,const wchar_t* value)
	{
		SelectObject(dc,font);SetTextColor(dc,color);SetBkMode(dc,TRANSPARENT);TextOutW(dc,x,y,value,lstrlenW(value));
	}

	void paint(HWND window)
	{
		PAINTSTRUCT ps{}; HDC dc=BeginPaint(window,&ps); RECT client{}; GetClientRect(window,&client);
		FillRect(dc,&client,gBackgroundBrush);
		RECT sidebar{0,0,270,client.bottom}; FillRect(dc,&sidebar,gPanelBrush);
		RECT header{270,0,client.right,116}; HBRUSH headerBrush=CreateSolidBrush(RGB(8,22,37)); FillRect(dc,&header,headerBrush); DeleteObject(headerBrush);
		RECT accent{270,112,client.right,116};HBRUSH accentBrush=CreateSolidBrush(RGB(0,195,235));FillRect(dc,&accent,accentBrush);DeleteObject(accentBrush);
		RECT accentGold{270,112,480,116};HBRUSH gold=CreateSolidBrush(RGB(236,186,58));FillRect(dc,&accentGold,gold);DeleteObject(gold);

		if(gIcon)DrawIconEx(dc,30,24,gIcon,54,54,0,nullptr,DI_NORMAL);
		drawTextLine(dc,gHeaderFont,RGB(244,248,253),98,26,L"LZ GAMES");
		drawTextLine(dc,gSmallFont,RGB(0,210,245),100,57,L"TURBORAMA  •  PIX");
		drawTextLine(dc,gTitleFont,RGB(245,249,255),316,26,L"Central comercial PIX");
		drawTextLine(dc,gFont,RGB(137,161,188),318,68,L"Conecte, valide e ative o recebimento em uma única tela.");

		RECT stepsCard{24,118,246,452};fillBox(dc,stepsCard,RGB(8,20,34),RGB(30,55,77),16);
		drawTextLine(dc,gSmallFont,RGB(236,186,58),44,142,L"CONFIGURAÇÃO ASSISTIDA");
		const wchar_t* steps[]={L"Provedor",L"Credencial segura",L"Estabelecimento",L"Pacotes e preços",L"Validação final"};
		for(int i=0;i<5;++i)
		{
			const int y=188+i*49;RECT circle{42,y-4,70,y+24};fillBox(dc,circle,i==4?RGB(37,52,29):RGB(10,42,60),i==4?RGB(236,186,58):RGB(0,180,222),28);
			wchar_t number[3]{};wsprintfW(number,L"%d",i+1);RECT numberArea=circle;
			SelectObject(dc,gSmallFont);SetTextColor(dc,RGB(242,247,252));SetBkMode(dc,TRANSPARENT);DrawTextW(dc,number,-1,&numberArea,DT_CENTER|DT_VCENTER|DT_SINGLELINE);
			drawTextLine(dc,gStepFont,RGB(222,234,246),84,y,steps[i]);
			if(i<4){RECT connector{55,y+25,57,y+42};HBRUSH line=CreateSolidBrush(RGB(30,69,92));FillRect(dc,&connector,line);DeleteObject(line);}
		}

		RECT safeCard{24,470,246,674};fillBox(dc,safeCard,RGB(7,24,27),RGB(38,91,76),16);
		drawTextLine(dc,gHeaderFont,RGB(114,226,160),44,494,L"SEGURO");
		RECT safeText{44,534,226,640};SelectObject(dc,gSmallFont);SetTextColor(dc,RGB(157,185,183));SetBkMode(dc,TRANSPARENT);
		DrawTextW(dc,L"A credencial é cifrada pelo Windows e nunca aparece em arquivos comuns.\n\nO titular da conta é reconhecido automaticamente.",-1,&safeText,DT_LEFT|DT_WORDBREAK);

		RECT providerCard{292,132,1020,292};fillBox(dc,providerCard,RGB(10,24,40),RGB(34,63,88),16);
		RECT establishmentCard{292,298,1020,518};fillBox(dc,establishmentCard,RGB(10,24,40),RGB(34,63,88),16);
		RECT priceCard{292,525,1020,624};fillBox(dc,priceCard,RGB(10,24,40),RGB(34,63,88),16);
		RECT statusCard{316,630,996,690};fillBox(dc,statusCard,RGB(6,21,31),RGB(32,85,104),14);
		RECT statusAccent{316,630,322,690};HBRUSH statusBrush=CreateSolidBrush(GetWindowLongPtrW(gStatus,GWLP_USERDATA)?RGB(236,85,92):RGB(0,198,231));FillRect(dc,&statusAccent,statusBrush);DeleteObject(statusBrush);
		EndPaint(window,&ps);
	}

	std::wstring friendlyIdentityError(const std::wstring& raw)
	{
		if (raw.find(L"SID local do quiosque") != std::wstring::npos
			|| raw.find(L"privilegios de administrador") != std::wstring::npos)
			return L"Este configurador foi aberto na conta de manutencao ou como administrador. "
				L"Feche-o, volte ao usuario automatico do quiosque e abra normalmente pelo menu do EmulationStation.";
		if (raw.find(L"AutoAdminLogon") != std::wstring::npos
			|| raw.find(L"Winlogon") != std::wstring::npos
			|| raw.find(L"kioskUser") != std::wstring::npos)
			return L"O usuario automatico do quiosque nao esta alinhado com o Launcher. "
				L"Repare o login automatico antes de configurar o PIX.";
		return raw;
	}

	LRESULT CALLBACK windowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
	{
		switch(message)
		{
		case WM_CREATE:
		{
			createControls(window);
			gWorking = true;
			enableForm(false);
			setStatus(L"Confirmando a sessao segura do usuario automatico do quiosque...", false);
			std::thread([window]() {
				auto* result = new WorkerResult;
				result->ok = preflightKioskIdentity(result->message);
				PostMessageW(window, WM_IDENTITY_CHECKED, 0, (LPARAM)result);
			}).detach();
			return 0;
		}
		case WM_PAINT: paint(window); return 0;
		case WM_CTLCOLORSTATIC:
		{
			HDC dc=(HDC)wParam; SetBkMode(dc,TRANSPARENT);
			if((HWND)lParam==gStatus) SetTextColor(dc,GetWindowLongPtrW(gStatus,GWLP_USERDATA)?RGB(255,118,118):RGB(129,224,255));
			else SetTextColor(dc,RGB(150,173,198)); return (LRESULT)GetStockObject(HOLLOW_BRUSH);
		}
		case WM_CTLCOLOREDIT: case WM_CTLCOLORLISTBOX:
		{
			HDC dc=(HDC)wParam; SetTextColor(dc,RGB(238,245,252)); SetBkColor(dc,RGB(13,27,44)); return (LRESULT)gFieldBrush;
		}
		case WM_DRAWITEM: drawButton((DRAWITEMSTRUCT*)lParam); return TRUE;
		case WM_COMMAND:
			switch(LOWORD(wParam))
			{
			case ID_PROVIDER: if(HIWORD(wParam)==CBN_SELCHANGE) updateProvider(); return 0;
			case ID_ENVIRONMENT: if(HIWORD(wParam)==CBN_SELCHANGE) updateProvider(); return 0;
			case ID_SHOW:
			{
				const bool show=SendMessageW(gToken,EM_GETPASSWORDCHAR,0,0)!=0; SendMessageW(gToken,EM_SETPASSWORDCHAR,show?0:0x25CF,0); InvalidateRect(gToken,nullptr,TRUE);
				SetWindowTextW(gShow,show?L"OCULTAR":L"MOSTRAR"); return 0;
			}
			case ID_MANAGE:
			{
				if(gWorking) return 0;
				std::wstring error;
				if(SendMessageW(gProvider, CB_GETCURSEL, 0, 0) != 0)
				{ setStatus(L"A consulta de cadastros existe apenas para Mercado Pago.",true); return 0; }
				if(!preflightKioskIdentity(error)){error=friendlyIdentityError(error);setStatus(error,true);MessageBoxW(window,error.c_str(),kTitle,MB_OK|MB_ICONERROR);return 0;}
				std::wstring token = textOf(gToken);
				if(token.size() < 40 || token.rfind(L"APP_USR-", 0) != 0)
				{ setStatus(L"Cole o Access Token completo antes de consultar a conta Mercado Pago.",true); return 0; }
				gWorking=true; enableForm(false); setStatus(L"Consultando conta, lojas e PDVs no Mercado Pago...",false);
				std::thread([window,token=std::move(token)]() mutable {
					auto* result=new InventoryWorkerResult;
					result->ok=fetchMercadoPagoInventory(token,result->inventory,result->message);
					SecureZeroMemory(token.data(), token.size()*sizeof(wchar_t));
					PostMessageW(window,WM_INVENTORY_READY,0,(LPARAM)result);
				}).detach();
				return 0;
			}
			case ID_CONFIGURE:
			{
				if(gWorking) return 0;
				std::wstring error;
				// Confirma a conta Windows/Winlogon antes de copiar o Access Token
				// do controle da interface. Uma sessao Admin nunca deve gravar a
				// DPAPI que pertence ao usuario automatico do quiosque.
				if(!preflightKioskIdentity(error)){error=friendlyIdentityError(error);setStatus(error,true);MessageBoxW(window,error.c_str(),kTitle,MB_OK|MB_ICONERROR);return 0;}
				const bool mercadoPago = SendMessageW(gProvider, CB_GETCURSEL, 0, 0) == 0;
				const bool production = SendMessageW(gEnvironment, CB_GETCURSEL, 0, 0) == 1;
				if (mercadoPago && production && MessageBoxW(window,
					L"PRODUCAO REAL\n\nEsta operacao valida uma conta real, pode criar ou reutilizar Loja e PDV reais "
					L"e libera cobrancas com dinheiro real. Deseja continuar?",
					kTitle, MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) return 0;
				FormData data;
				if(!collect(data,error)){setStatus(error,true);MessageBoxW(window,error.c_str(),kTitle,MB_OK|MB_ICONERROR);return 0;}
				clearTokenControl();
				gWorking=true; enableForm(false); setStatus(L"Validando credencial, titular, loja e caixa. Aguarde...",false);
				std::thread([window,data=std::move(data)]() mutable { auto* result=new WorkerResult(configure(std::move(data))); PostMessageW(window,WM_CONFIGURED,0,(LPARAM)result); }).detach(); return 0;
			}
			case ID_LOAD:
			{
				std::wstring loadMessage;
				const bool loaded = loadExistingRegistration(loadMessage);
				setStatus(loadMessage, !loaded);
				if (!loaded) MessageBoxW(window, loadMessage.c_str(), kTitle, MB_OK | MB_ICONINFORMATION);
				return 0;
			}
			case ID_CLOSE: if(!gWorking) DestroyWindow(window); return 0;
			} break;
		case WM_IDENTITY_CHECKED:
		{
			auto* result=(WorkerResult*)lParam; gWorking=false;
			if(result->ok)
			{
				enableForm(true);
				setStatus(L"Sessao do quiosque confirmada. Escolha TESTE ou PRODUCAO antes de informar a credencial.",false);
			}
			else
			{
				result->message=friendlyIdentityError(result->message);
				enableForm(false); EnableWindow(gClose,TRUE);
				setStatus(result->message,true);
				MessageBoxW(window,result->message.c_str(),kTitle,MB_OK|MB_ICONERROR);
			}
			delete result; return 0;
		}
		case WM_INVENTORY_READY:
		{
			auto* result=(InventoryWorkerResult*)lParam; gWorking=false; enableForm(true);
			if(result->ok)
			{
				MercadoPagoStore store; MercadoPagoPointOfSale pos; std::wstring selectError;
				const bool single = chooseSingleCompatiblePair(result->inventory, store, pos, selectError);
				if(single)
				{
					SetWindowTextW(gStore, store.name.empty()?L"TurboRama":store.name.c_str());
					SetWindowTextW(gStoreExternal, store.externalId.c_str());
					SetWindowTextW(gPos, pos.name.empty()?L"TurboRama Kiosk":pos.name.c_str());
					SetWindowTextW(gPosExternal, pos.externalId.c_str());
					setStatus(L"Conta consultada. Par Loja/PDV unico encontrado e preenchido na tela.",false);
				}
				else setStatus(selectError,true);
				const std::wstring summary = inventorySummary(result->inventory)
					+ (single ? L"\r\n\r\nO par unico foi preenchido automaticamente na tela."
						: L"\r\n\r\n" + selectError + L"\r\nCopie os external_id corretos para os campos da tela.");
				MessageBoxW(window,summary.c_str(),kTitle,MB_OK|(single?MB_ICONINFORMATION:MB_ICONWARNING));
			}
			else
			{
				setStatus(result->message,true);
				MessageBoxW(window,result->message.c_str(),kTitle,MB_OK|MB_ICONERROR);
			}
			delete result; return 0;
		}
		case WM_CONFIGURED:
		{
			auto* result=(WorkerResult*)lParam; gWorking=false; clearTokenControl(); enableForm(true); setStatus(result->message,!result->ok);
			MessageBoxW(window,result->message.c_str(),kTitle,MB_OK|(result->ok?MB_ICONINFORMATION:MB_ICONERROR)); delete result; return 0;
		}
		case WM_CLOSE: if(!gWorking) DestroyWindow(window); else MessageBoxW(window,L"A validação está em andamento. Aguarde a conclusão.",kTitle,MB_OK|MB_ICONINFORMATION); return 0;
		case WM_DESTROY:
			for(HFONT font:{gTitleFont,gHeaderFont,gFont,gSmallFont,gMonoFont,gHeroFont,gStepFont})if(font)DeleteObject(font);
			if(gBackgroundBrush)DeleteObject(gBackgroundBrush);if(gFieldBrush)DeleteObject(gFieldBrush);if(gPanelBrush)DeleteObject(gPanelBrush);PostQuitMessage(0);return 0;
		}
		return DefWindowProcW(window,message,wParam,lParam);
	}

	bool selfTest()
	{
		FormData data; data.storeName=L"TurboRama";data.posName=L"Kiosk";data.cep=L"57084648";data.number=L"52";data.reference=L"Loja";
		data.adapterUrl=L"http://127.0.0.1:8765/";data.adapterId=L"banco-teste";data.p15=L"1,00";data.p30=L"2,00";data.p45=L"3,00";data.p60=L"4,00";data.p120=L"8,00";
		const auto json=configurationJson(data);
		data.sandbox=false; data.storeExternalId=L"LZLOJA01"; data.posExternalId=L"LZPIX01";
		const auto productionJson=configurationJson(data);
		const std::string saved = R"({"provider":"adapter","storeName":"LZ \"Games\"","packagePricesCents":{"15":750}})";
		ULONGLONG parsedPid = 0;
		return parsePrice(L"7,50")==750 && json.find("\"accessToken\"")==std::string::npos
			&& json.find("\"mercadoPagoEnvironment\": \"sandbox\"")!=std::string::npos
			&& json.find("\"storeExternalId\": \"\"")!=std::string::npos
			&& productionJson.find("\"mercadoPagoEnvironment\": \"production\"")!=std::string::npos
			&& productionJson.find("\"storeExternalId\": \"LZLOJA01\"")!=std::string::npos
			&& productionJson.find("\"posExternalId\": \"LZPIX01\"")!=std::string::npos
			&& validExternalId(std::wstring(60,L'A'),60) && !validExternalId(std::wstring(61,L'A'),60)
			&& validExternalId(std::wstring(40,L'9'),40) && !validExternalId(L"LZ-PIX",40)
			&& looksLikeMercadoPagoNumericId(L"1234567890123456") && !looksLikeMercadoPagoNumericId(L"LZPIXF50555198F64")
			&& strictUnsigned("{\"processId\":123}", "processId", parsedPid) && parsedPid == 123
			&& jsonString(saved,"provider")=="adapter" && jsonString(saved,"storeName")=="LZ \"Games\""
			&& jsonInteger(saved,"15",0)==750 && priceText(750)==L"7,50";
	}
}

int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR,int show)
{
	SetProcessDPIAware(); int count{}; wchar_t** args=CommandLineToArgvW(GetCommandLineW(),&count);
	if(args&&count>1&&std::wstring(args[1])==L"--self-test"){LocalFree(args);return selfTest()?0:20;} if(args)LocalFree(args);
	INITCOMMONCONTROLSEX controls{sizeof(controls),ICC_STANDARD_CLASSES};InitCommonControlsEx(&controls);
	gBackgroundBrush=CreateSolidBrush(RGB(7,17,31));gFieldBrush=CreateSolidBrush(RGB(13,27,44));gPanelBrush=CreateSolidBrush(RGB(5,13,24));
	gIcon=(HICON)LoadImageW(instance,MAKEINTRESOURCEW(1),IMAGE_ICON,64,64,LR_DEFAULTCOLOR);
	WNDCLASSEXW wc{sizeof(wc)};wc.lpfnWndProc=windowProc;wc.hInstance=instance;wc.hIcon=gIcon;wc.hIconSm=gIcon;wc.hCursor=LoadCursor(nullptr,IDC_ARROW);wc.hbrBackground=gBackgroundBrush;wc.lpszClassName=kClassName;
	if(!RegisterClassExW(&wc))return 2;
	RECT desired{0,0,1040,790};AdjustWindowRectEx(&desired,WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX,FALSE,0);
	const int width=desired.right-desired.left,height=desired.bottom-desired.top,x=(GetSystemMetrics(SM_CXSCREEN)-width)/2,y=(GetSystemMetrics(SM_CYSCREEN)-height)/2;
	gWindow=CreateWindowExW(0,kClassName,kTitle,WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX,x,y,width,height,nullptr,nullptr,instance,nullptr);
	if(!gWindow)return 3;ShowWindow(gWindow,show);UpdateWindow(gWindow);MSG message{};while(GetMessageW(&message,nullptr,0,0)>0){TranslateMessage(&message);DispatchMessageW(&message);}return(int)message.wParam;
}
