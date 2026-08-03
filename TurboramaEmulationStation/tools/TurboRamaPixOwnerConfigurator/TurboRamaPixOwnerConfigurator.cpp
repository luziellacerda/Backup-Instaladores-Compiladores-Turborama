#define UNICODE
#define _UNICODE
#include <windows.h>
#include <commctrl.h>
#include <shellapi.h>
#include <algorithm>
#include <atomic>
#include <cctype>
#include <cwctype>
#include <fstream>
#include <iomanip>
#include <iterator>
#include <sstream>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#pragma comment(lib, "comctl32.lib")

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
	constexpr int ID_PRICE15 = 115;
	constexpr int ID_PRICE30 = 130;
	constexpr int ID_PRICE45 = 145;
	constexpr int ID_PRICE60 = 160;
	constexpr int ID_PRICE120 = 220;
	constexpr int ID_CONFIGURE = 300;
	constexpr int ID_LOAD = 301;
	constexpr int ID_CLOSE = 302;
	constexpr UINT WM_CONFIGURED = WM_APP + 25;
	const wchar_t* kClassName = L"TurboRamaPixOwnerConfigurator";
	const wchar_t* kTitle = L"LZ Games - Configuração Comercial PIX";

	HWND gWindow{}, gProvider{}, gToken{}, gShow{}, gStore{}, gPos{}, gCep{}, gNumber{}, gReference{};
	HWND gAdapterUrl{}, gAdapterId{}, gStatus{}, gConfigure{}, gLoad{}, gClose{};
	HWND gPrice15{}, gPrice30{}, gPrice45{}, gPrice60{}, gPrice120{};
	std::vector<HWND> gMercadoPagoLabels, gAdapterLabels;
	HFONT gTitleFont{}, gHeaderFont{}, gFont{}, gSmallFont{}, gMonoFont{}, gHeroFont{}, gStepFont{};
	HBRUSH gBackgroundBrush{}, gFieldBrush{}, gPanelBrush{};
	HICON gIcon{};
	std::atomic_bool gWorking{ false };

	struct FormData
	{
		bool adapter{};
		std::wstring token, storeName, posName, cep, number, reference, adapterUrl, adapterId;
		std::wstring p15, p30, p45, p60, p120;
	};

	struct WorkerResult { bool ok{}; std::wstring message; };
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
		const int size = MultiByteToWideChar(CP_UTF8, 0, value.data(), (int)value.size(), nullptr, 0);
		std::wstring result((size_t)size, L'\0');
		MultiByteToWideChar(CP_UTF8, 0, value.data(), (int)value.size(), result.data(), size);
		return result;
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

	bool resolveInstallation(std::wstring& root, std::wstring& executable, std::wstring& assembly, std::wstring& bridge)
	{
		for (const auto& candidate : { moduleDirectory(), std::wstring(L"D:\\emulationstation") })
		{
			const auto runtime = join(candidate, L"pix-agent\\runtime\\dotnet.exe");
			const auto dll = join(candidate, L"pix-agent\\TurboRamaPixAgent.dll");
			const auto app = join(candidate, L"pix-agent\\TurboRamaPixAgent.exe");
			if (exists(runtime) && exists(dll))
			{
				root = candidate; executable = runtime; assembly = dll;
				bridge = join(candidate, L".emulationstation\\pix"); return true;
			}
			if (exists(app))
			{
				root = candidate; executable = app; assembly.clear();
				bridge = join(candidate, L".emulationstation\\pix"); return true;
			}
		}
		return false;
	}

	std::wstring normalizePath(std::wstring value)
	{
		std::replace(value.begin(), value.end(), L'/', L'\\');
		std::transform(value.begin(), value.end(), value.begin(), towlower);
		return value;
	}

	DWORD parseProcessId(const std::string& json)
	{
		const auto key = json.find("\"processId\"");
		if (key == std::string::npos) return 0;
		const auto colon = json.find(':', key);
		if (colon == std::string::npos) return 0;
		char* end = nullptr;
		const unsigned long value = strtoul(json.c_str() + colon + 1, &end, 10);
		return value > 0 && value <= MAXDWORD ? (DWORD)value : 0;
	}

	void stopOnlyPixAgent(const std::wstring& bridge, const std::wstring& expectedExecutable)
	{
		std::string status;
		if (!readAll(join(bridge, L"agent-status.json"), status)) return;
		const DWORD pid = parseProcessId(status);
		if (!pid) return;
		HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE | SYNCHRONIZE, FALSE, pid);
		if (!process) return;
		wchar_t path[MAX_PATH * 4]{}; DWORD length = (DWORD)std::size(path);
		const bool same = QueryFullProcessImageNameW(process, 0, path, &length)
			&& normalizePath(path) == normalizePath(expectedExecutable);
		if (same)
		{
			TerminateProcess(process, 0);
			WaitForSingleObject(process, 5000);
		}
		CloseHandle(process);
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

	bool collect(FormData& data, std::wstring& error)
	{
		data.adapter = SendMessageW(gProvider, CB_GETCURSEL, 0, 0) == 1;
		data.token = textOf(gToken); data.storeName = textOf(gStore); data.posName = textOf(gPos);
		data.cep = textOf(gCep); data.number = textOf(gNumber); data.reference = textOf(gReference);
		data.adapterUrl = textOf(gAdapterUrl); data.adapterId = textOf(gAdapterId);
		data.p15 = textOf(gPrice15); data.p30 = textOf(gPrice30); data.p45 = textOf(gPrice45);
		data.p60 = textOf(gPrice60); data.p120 = textOf(gPrice120);
		if (data.token.size() < (data.adapter ? 8u : 40u)) { error = L"Informe a credencial completa do provedor."; return false; }
		if (!data.adapter)
		{
			if (data.token.rfind(L"APP_USR-", 0) != 0) { error = L"Use o Access Token completo de produção iniciado por APP_USR-."; return false; }
			std::wstring digits; for (wchar_t ch : data.cep) if (iswdigit(ch)) digits.push_back(ch); data.cep = digits;
			if (data.storeName.size() < 2 || data.posName.size() < 2) { error = L"Informe os nomes da loja e do caixa."; return false; }
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
			<< "  \"storeName\": \"" << jsonEscape(data.storeName) << "\",\n"
			<< "  \"storeExternalId\": \"\",\n"
			<< "  \"posName\": \"" << jsonEscape(data.posName) << "\",\n"
			<< "  \"posExternalId\": \"\",\n"
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
		const std::string& input, DWORD& exitCode, std::string& output, std::wstring& error)
	{
		SECURITY_ATTRIBUTES attributes{ sizeof(SECURITY_ATTRIBUTES), nullptr, TRUE };
		HANDLE stdinRead{}, stdinWrite{}, stdoutRead{}, stdoutWrite{};
		if (!CreatePipe(&stdinRead, &stdinWrite, &attributes, 0) || !CreatePipe(&stdoutRead, &stdoutWrite, &attributes, 0))
		{ error = L"O Windows não conseguiu criar a comunicação segura com o agente PIX."; return false; }
		SetHandleInformation(stdinWrite, HANDLE_FLAG_INHERIT, 0); SetHandleInformation(stdoutRead, HANDLE_FLAG_INHERIT, 0);
		STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
		startup.hStdInput = stdinRead; startup.hStdOutput = stdoutWrite; startup.hStdError = stdoutWrite; startup.wShowWindow = SW_HIDE;
		PROCESS_INFORMATION process{}; std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
		const BOOL created = CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, TRUE, CREATE_NO_WINDOW,
			nullptr, working.c_str(), &startup, &process);
		CloseHandle(stdinRead); CloseHandle(stdoutWrite);
		if (!created)
		{
			CloseHandle(stdinWrite); CloseHandle(stdoutRead);
			error = L"Não foi possível iniciar o agente PIX (Windows " + std::to_wstring(GetLastError()) + L")."; return false;
		}
		std::string line = input + "\r\n"; DWORD written{};
		WriteFile(stdinWrite, line.data(), (DWORD)line.size(), &written, nullptr);
		SecureZeroMemory(line.data(), line.size()); CloseHandle(stdinWrite);
		const DWORD wait = WaitForSingleObject(process.hProcess, 120000);
		if (wait != WAIT_OBJECT_0)
		{
			TerminateProcess(process.hProcess, 21); error = L"A validação ultrapassou 2 minutos e foi encerrada com segurança.";
		}
		GetExitCodeProcess(process.hProcess, &exitCode); CloseHandle(process.hThread); CloseHandle(process.hProcess);
		char buffer[4096]; DWORD received{};
		while (ReadFile(stdoutRead, buffer, sizeof(buffer), &received, nullptr) && received) output.append(buffer, buffer + received);
		CloseHandle(stdoutRead);
		return wait == WAIT_OBJECT_0;
	}

	void restartAgent(const std::wstring& root, const std::wstring& executable, const std::wstring& assembly, const std::wstring& bridge)
	{
		std::wstring command = L"\"" + executable + L"\" ";
		if (!assembly.empty()) command += L"\"" + assembly + L"\" ";
		command += L"--bridge \"" + bridge + L"\"";
		std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
		STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_HIDE;
		PROCESS_INFORMATION process{};
		if (CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
			nullptr, root.c_str(), &startup, &process))
		{ CloseHandle(process.hThread); CloseHandle(process.hProcess); }
	}

	WorkerResult configure(FormData data)
	{
		WorkerResult result;
		std::wstring root, executable, assembly, bridge;
		if (!resolveInstallation(root, executable, assembly, bridge))
		{ result.message = L"Agente PIX não encontrado. Instale primeiro o pacote comercial TurboRama."; return result; }
		const std::wstring temporary = tempConfigurationFile();
		if (!writeAll(temporary, configurationJson(data)))
		{ result.message = L"Não foi possível preparar o cadastro temporário."; return result; }
		stopOnlyPixAgent(bridge, executable);
		std::wstring command = L"\"" + executable + L"\" ";
		if (!assembly.empty()) command += L"\"" + assembly + L"\" ";
		command += L"--configure-owner \"" + temporary + L"\" --bridge \"" + bridge + L"\"";
		std::string output; DWORD exitCode = 999; std::wstring error;
		std::string credential = utf8(data.token);
		const bool ran = createChildWithPipes(executable, command, root, credential, exitCode, output, error);
		SecureZeroMemory(credential.data(), credential.size()); SecureZeroMemory(data.token.data(), data.token.size() * sizeof(wchar_t));
		DeleteFileW(temporary.c_str());
		if (!ran || exitCode != 0)
		{
			result.message = !error.empty() ? error : trim(wide(output));
			if (result.message.empty()) result.message = L"O provedor recusou a configuração. Nenhum cadastro anterior foi substituído.";
			restartAgent(root, executable, assembly, bridge); return result;
		}
		restartAgent(root, executable, assembly, bridge);
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
		SetWindowTextW(gStore, wide(jsonString(json, "storeName", "TurboRamaX")).c_str());
		SetWindowTextW(gPos, wide(jsonString(json, "posName", "TurboRama Kiosk")).c_str());
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
		RECT statusCard{ 316, 598, 996, 668 };
		RedrawWindow(gWindow, &statusCard, nullptr,
			RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
	}

	void enableForm(bool enabled)
	{
		for (HWND control : { gProvider,gToken,gShow,gStore,gPos,gCep,gNumber,gReference,gAdapterUrl,gAdapterId,
			gPrice15,gPrice30,gPrice45,gPrice60,gPrice120,gConfigure,gLoad,gClose }) EnableWindow(control, enabled);
	}

	void updateProvider()
	{
		const bool adapter = SendMessageW(gProvider, CB_GETCURSEL, 0, 0) == 1;
		for (HWND control : { gStore,gPos,gCep,gNumber,gReference }) ShowWindow(control, adapter ? SW_HIDE : SW_SHOW);
		for (HWND control : { gAdapterUrl,gAdapterId }) ShowWindow(control, adapter ? SW_SHOW : SW_HIDE);
		for (HWND control : gMercadoPagoLabels) ShowWindow(control, adapter ? SW_HIDE : SW_SHOW);
		for (HWND control : gAdapterLabels) ShowWindow(control, adapter ? SW_SHOW : SW_HIDE);
		SetWindowTextW(gToken, L"");
		SendMessageW(gToken, EM_SETPASSWORDCHAR, 0x25CF, 0);
		setStatus(adapter
			? L"Informe o endpoint e o segredo do adaptador compatível com o contrato TurboRama."
			: L"O User ID será identificado automaticamente pelo Access Token. Não informe ID da aplicação.");
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
		gProvider = CreateWindowExW(0,L"COMBOBOX",L"",WS_CHILD|WS_VISIBLE|WS_TABSTOP|CBS_DROPDOWNLIST,316,169,680,220,window,(HMENU)(INT_PTR)ID_PROVIDER,nullptr,nullptr);
		SendMessageW(gProvider,WM_SETFONT,(WPARAM)gFont,TRUE); SendMessageW(gProvider,CB_ADDSTRING,0,(LPARAM)L"Mercado Pago — conta própria ou autorizada");
		SendMessageW(gProvider,CB_ADDSTRING,0,(LPARAM)L"Outro banco — adaptador TurboRama"); SendMessageW(gProvider,CB_SETCURSEL,0,0);

		label(window,L"ACCESS TOKEN — CREDENCIAL PROTEGIDA",316,218,390,22); gToken=edit(window,ID_TOKEN,L"",316,241,540,38,ES_PASSWORD);
		SendMessageW(gToken,EM_SETPASSWORDCHAR,0x25CF,0); gShow=button(window,ID_SHOW,L"MOSTRAR",868,241,128,38);

		gMercadoPagoLabels.push_back(label(window,L"NOME DO ESTABELECIMENTO",316,308,280,22)); gStore=edit(window,ID_STORE,L"TurboRamaX",316,331,330,38);
		gMercadoPagoLabels.push_back(label(window,L"NOME DO CAIXA / PDV",662,308,260,22)); gPos=edit(window,ID_POS,L"TurboRama Kiosk",662,331,334,38);
		gMercadoPagoLabels.push_back(label(window,L"CEP",316,393,100,22)); gCep=edit(window,ID_CEP,L"57084648",316,416,190,38);
		gMercadoPagoLabels.push_back(label(window,L"NÚMERO / COMPLEMENTO",520,393,210,22)); gNumber=edit(window,ID_NUMBER,L"52",520,416,190,38);
		gMercadoPagoLabels.push_back(label(window,L"REFERÊNCIA",724,393,160,22)); gReference=edit(window,ID_REFERENCE,L"TurboRama",724,416,272,38);

		gAdapterLabels.push_back(label(window,L"ENDEREÇO SEGURO DO ADAPTADOR",316,308,340,22)); gAdapterUrl=edit(window,ID_ADAPTER_URL,L"http://127.0.0.1:8765/",316,331,680,38);
		gAdapterLabels.push_back(label(window,L"IDENTIFICADOR DO PROVEDOR",316,393,280,22)); gAdapterId=edit(window,ID_ADAPTER_ID,L"meu-banco",316,416,680,38);
		ShowWindow(gAdapterUrl,SW_HIDE); ShowWindow(gAdapterId,SW_HIDE);

		label(window,L"PACOTES DE TEMPO — VALOR EM REAIS",316,485,360,22);
		const int xs[] = {316,456,596,736,876}; const wchar_t* captions[]={L"15 MIN",L"30 MIN",L"45 MIN",L"60 MIN",L"120 MIN"};
		HWND* fields[]={&gPrice15,&gPrice30,&gPrice45,&gPrice60,&gPrice120}; const int ids[]={ID_PRICE15,ID_PRICE30,ID_PRICE45,ID_PRICE60,ID_PRICE120};
		const wchar_t* values[]={L"1,00",L"2,00",L"3,00",L"4,00",L"8,00"};
		for(int i=0;i<5;++i){label(window,captions[i],xs[i],513,118,20);*fields[i]=edit(window,ids[i],values[i],xs[i],536,120,38);}

		gStatus=CreateWindowExW(WS_EX_TRANSPARENT,L"STATIC",L"",WS_CHILD|WS_VISIBLE|SS_LEFT|SS_NOPREFIX,
			350,606,628,56,window,nullptr,nullptr,nullptr);
		SendMessageW(gStatus,WM_SETFONT,(WPARAM)gSmallFont,TRUE);
		gConfigure=button(window,ID_CONFIGURE,L"VALIDAR E ATIVAR PIX",316,685,408,56);
		gLoad=button(window,ID_LOAD,L"CARREGAR CADASTRO",738,685,258,56);
		gClose=button(window,ID_CLOSE,L"FECHAR",88,702,130,38);
		updateProvider();
	}

	void drawButton(const DRAWITEMSTRUCT* item)
	{
		const bool primary = item->CtlID == ID_CONFIGURE;
		const bool pressed = (item->itemState & ODS_SELECTED) != 0;
		const COLORREF fill = primary ? (pressed ? RGB(0,137,174) : RGB(0,190,231)) : (pressed ? RGB(34,48,68) : RGB(18,31,48));
		RECT area=item->rcItem; InflateRect(&area,-1,-1);
		HBRUSH brush=CreateSolidBrush(fill); HPEN pen=CreatePen(PS_SOLID,primary?2:1,primary?RGB(84,225,255):RGB(74,94,119));
		HGDIOBJ oldBrush=SelectObject(item->hDC,brush); HGDIOBJ oldPen=SelectObject(item->hDC,pen);
		RoundRect(item->hDC,area.left,area.top,area.right,area.bottom,12,12);
		SelectObject(item->hDC,oldPen);SelectObject(item->hDC,oldBrush);DeleteObject(pen);DeleteObject(brush);
		SetBkMode(item->hDC,TRANSPARENT); SetTextColor(item->hDC,primary?RGB(5,15,25):RGB(230,239,249)); SelectObject(item->hDC,gFont);
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
		RECT establishmentCard{292,298,1020,468};fillBox(dc,establishmentCard,RGB(10,24,40),RGB(34,63,88),16);
		RECT priceCard{292,475,1020,588};fillBox(dc,priceCard,RGB(10,24,40),RGB(34,63,88),16);
		RECT statusCard{316,598,996,668};fillBox(dc,statusCard,RGB(6,21,31),RGB(32,85,104),14);
		RECT statusAccent{316,598,322,668};HBRUSH statusBrush=CreateSolidBrush(GetWindowLongPtrW(gStatus,GWLP_USERDATA)?RGB(236,85,92):RGB(0,198,231));FillRect(dc,&statusAccent,statusBrush);DeleteObject(statusBrush);
		EndPaint(window,&ps);
	}

	LRESULT CALLBACK windowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
	{
		switch(message)
		{
		case WM_CREATE: createControls(window); return 0;
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
			case ID_SHOW:
			{
				const bool show=SendMessageW(gToken,EM_GETPASSWORDCHAR,0,0)!=0; SendMessageW(gToken,EM_SETPASSWORDCHAR,show?0:0x25CF,0); InvalidateRect(gToken,nullptr,TRUE);
				SetWindowTextW(gShow,show?L"OCULTAR":L"MOSTRAR"); return 0;
			}
			case ID_CONFIGURE:
			{
				if(gWorking) return 0; FormData data; std::wstring error;
				if(!collect(data,error)){setStatus(error,true);MessageBoxW(window,error.c_str(),kTitle,MB_OK|MB_ICONERROR);return 0;}
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
		case WM_CONFIGURED:
		{
			auto* result=(WorkerResult*)lParam; gWorking=false; enableForm(true); setStatus(result->message,!result->ok);
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
		const std::string saved = R"({"provider":"adapter","storeName":"LZ \"Games\"","packagePricesCents":{"15":750}})";
		return parsePrice(L"7,50")==750 && json.find("\"accessToken\"")==std::string::npos
			&& json.find("\"storeExternalId\": \"\"")!=std::string::npos && parseProcessId("{\"processId\":123}")==123
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
