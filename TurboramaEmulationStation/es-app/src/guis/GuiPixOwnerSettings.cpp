#include "guis/GuiPixOwnerSettings.h"

#include "LocaleES.h"
#include "Paths.h"
#include "Settings.h"
#include "Window.h"
#include "guis/GuiMsgBox.h"
#include "guis/GuiTextEditPopup.h"
#include "guis/GuiTextEditPopupKeyboard.h"
#include "renderers/Renderer.h"
#include "utils/FileSystemUtil.h"
#include "utils/StringUtil.h"

#include <algorithm>
#include <cctype>
#include <iomanip>
#include <sstream>
#include <vector>

#ifdef _WIN32
#include <windows.h>
#endif

GuiPixOwnerSettings::GuiPixOwnerSettings(Window* window)
	: GuiComponent(window)
	, mMenu(window, _("PIX DO ESTABELECIMENTO"))
	, mDraft(PixAgentManager::loadOwnerSettings())
{
	setPosition(0, 0);
	setSize((float)Renderer::getScreenWidth(), (float)Renderer::getScreenHeight());
	mMenu.setMaxHeight(Renderer::getScreenHeight() * 0.92f);
	addChild(&mMenu);
	rebuild();
}

void GuiPixOwnerSettings::centerOnScreen()
{
	const float x = (Renderer::getScreenWidth() - mMenu.getSize().x()) * 0.5f;
	const float y = (Renderer::getScreenHeight() - mMenu.getSize().y()) * 0.5f;
	mMenu.setPosition(std::max(0.0f, x), std::max(0.0f, y));
}

std::string GuiPixOwnerSettings::formatPrice(long long cents) const
{
	std::ostringstream output;
	output << "R$ " << (cents / 100) << ',' << std::setw(2) << std::setfill('0') << (cents % 100);
	return output.str();
}

bool GuiPixOwnerSettings::parsePrice(const std::string& value, long long& cents) const
{
	std::string clean;
	for (unsigned char ch : value)
		if (std::isdigit(ch) || ch == ',' || ch == '.') clean.push_back((char)ch);
	if (clean.empty()) return false;
	const size_t separator = clean.find_last_of(",.");
	std::string whole = separator == std::string::npos ? clean : clean.substr(0, separator);
	std::string fraction = separator == std::string::npos ? "00" : clean.substr(separator + 1);
	whole.erase(std::remove_if(whole.begin(), whole.end(), [](unsigned char ch) { return !std::isdigit(ch); }), whole.end());
	fraction.erase(std::remove_if(fraction.begin(), fraction.end(), [](unsigned char ch) { return !std::isdigit(ch); }), fraction.end());
	if (whole.empty()) whole = "0";
	if (fraction.empty()) fraction = "00";
	if (fraction.size() == 1) fraction.push_back('0');
	if (fraction.size() > 2) fraction.resize(2);
	try { cents = std::stoll(whole) * 100 + std::stoll(fraction); }
	catch (...) { return false; }
	return cents >= 50 && cents <= 100000000;
}

void GuiPixOwnerSettings::editText(const std::string& title, const std::string& current, bool password,
	const std::function<void(const std::string&)>& callback)
{
	auto accepted = [this, callback](const std::string& value) {
		callback(value);
		rebuild();
	};
	if (Settings::getInstance()->getBool("UseOSK"))
		mWindow->pushGui(new GuiTextEditPopupKeyboard(mWindow, title, password ? "" : current, accepted, false, "OK", password));
	else
		mWindow->pushGui(new GuiTextEditPopup(mWindow, title, password ? "" : current, accepted, false, "OK", password));
}

void GuiPixOwnerSettings::editPrice(int minutes)
{
	const long long current = mDraft.pricesCents.count(minutes) ? mDraft.pricesCents[minutes] : 0;
	editText(std::to_string(minutes) + _(" MINUTOS - PRECO EM REAIS"), formatPrice(current), false,
		[this, minutes](const std::string& value) {
			long long cents = 0;
			if (!parsePrice(value, cents))
			{
				mWindow->pushGui(new GuiMsgBox(mWindow, _("Digite um preco valido. Exemplo: 7,50"), _("OK"), nullptr, ICON_ERROR));
				return;
			}
			mDraft.pricesCents[minutes] = cents;
		});
}

void GuiPixOwnerSettings::launchOwnerConfigurator()
{
#ifdef _WIN32
	HANDLE processToken = nullptr;
	TOKEN_ELEVATION elevation{};
	DWORD elevationSize = 0;
	const bool elevationKnown = OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &processToken) != FALSE
		&& GetTokenInformation(processToken, TokenElevation, &elevation, sizeof(elevation), &elevationSize) != FALSE;
	if (processToken != nullptr) CloseHandle(processToken);
	if (!elevationKnown || elevation.TokenIsElevated != 0)
	{
		mWindow->pushGui(new GuiMsgBox(mWindow,
			_("O CONFIGURADOR PIX PRECISA DA SESSAO NORMAL DA CONTA WINDOWS ARCADE.\n\n"
				"Feche o EmulationStation aberto como administrador e entre novamente pelo Launcher do quiosque."),
			_("OK"), nullptr, ICON_ERROR));
		return;
	}

	const std::string configuredDirectory = Paths::getExePath();
	if (configuredDirectory.empty())
	{
		mWindow->pushGui(new GuiMsgBox(mWindow,
			_("Nao foi possivel identificar a pasta desta instalacao do EmulationStation."),
			_("OK"), nullptr, ICON_ERROR));
		return;
	}

	const std::string workingDirectory = Utils::FileSystem::getAbsolutePath(configuredDirectory);
	const std::string configurator = Utils::FileSystem::combine(
		workingDirectory, "CONFIGURAR-USER-TOKEN-PIX.exe");
	if (!Utils::FileSystem::isAbsolute(configurator)
		|| !Utils::FileSystem::isRegularFile(configurator))
	{
		mWindow->pushGui(new GuiMsgBox(mWindow,
			_("O CONFIGURADOR PIX NAO FOI ENCONTRADO NESTA INSTALACAO.\n\n"
				"Reinstale ou repare o modulo PIX e tente novamente."),
			_("OK"), nullptr, ICON_ERROR));
		return;
	}

	const std::wstring executable = Utils::String::convertToWideString(configurator);
	const std::wstring directory = Utils::String::convertToWideString(workingDirectory);
	std::wstring commandLine = L"\"" + executable + L"\"";
	std::vector<wchar_t> mutableCommand(commandLine.begin(), commandLine.end());
	mutableCommand.push_back(L'\0');

	STARTUPINFOW startup{};
	startup.cb = sizeof(startup);
	startup.dwFlags = STARTF_USESHOWWINDOW;
	startup.wShowWindow = SW_SHOWNORMAL;
	PROCESS_INFORMATION process{};
	if (!CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
		0, nullptr, directory.c_str(), &startup, &process))
	{
		const DWORD windowsError = GetLastError();
		mWindow->pushGui(new GuiMsgBox(mWindow,
			_("NAO FOI POSSIVEL ABRIR O CONFIGURADOR PIX.\n\n"
				"Confirme que o EmulationStation esta aberto normalmente na conta Windows Arcade, "
				"sem Executar como administrador.\n\nErro do Windows: ") + std::to_string(windowsError),
			_("OK"), nullptr, ICON_ERROR));
		return;
	}

	CloseHandle(process.hThread);
	CloseHandle(process.hProcess);
	mWindow->displayNotificationMessage(
		_("CONFIGURADOR PIX ABERTO NESTA MESMA SESSAO. CONCLUA O CADASTRO E VOLTE A ESTA TELA."), 8);
#else
	mWindow->pushGui(new GuiMsgBox(mWindow,
		_("O configurador protegido do PIX esta disponivel somente no Windows."),
		_("OK"), nullptr, ICON_ERROR));
#endif
}

void GuiPixOwnerSettings::rebuild()
{
	mMenu.clear();
	mMenu.setSubTitle(std::string(_("STATUS: ")) + PixAgentManager::statusText());

	mMenu.addGroup(_("PROVEDOR PIX"));
	mMenu.addEntry(std::string(_("PROVEDOR ATIVO: ")) + (mDraft.provider == "adapter" ? _("OUTRO BANCO / ADAPTADOR") : _("MERCADO PAGO")), true,
		[this] { mDraft.provider = mDraft.provider == "adapter" ? "mercadopago" : "adapter"; rebuild(); });

	if (mDraft.provider == "adapter")
	{
		mMenu.addGroup(_("ADAPTADOR BANCARIO"));
		mMenu.addEntry(std::string(_("ENDERECO DO ADAPTADOR: ")) + mDraft.adapterBaseUrl, true,
			[this] { editText(_("URL HTTPS OU ENDERECO LOCAL DO ADAPTADOR"), mDraft.adapterBaseUrl, false, [this](const std::string& value) { mDraft.adapterBaseUrl = value; }); });
		mMenu.addEntry(std::string(_("IDENTIFICADOR DO PROVEDOR: ")) + mDraft.adapterProviderId, true,
			[this] { editText(_("IDENTIFICADOR DO ADAPTADOR"), mDraft.adapterProviderId, false, [this](const std::string& value) { mDraft.adapterProviderId = value; }); });
	}
	else
	{
		mMenu.addGroup(_("DADOS DO PROPRIETARIO / MERCADO PAGO"));
		mMenu.addEntry(std::string(_("USER ID RECONHECIDO PELO TOKEN: ")) + (mDraft.accountId.empty() ? _("AGUARDANDO CONFIGURADOR") : mDraft.accountId), false);
		mMenu.addEntry(std::string(_("LOJA GERENCIADA AUTOMATICAMENTE: ")) + mDraft.storeExternalId, false);
		mMenu.addEntry(std::string(_("NOME DA LOJA: ")) + mDraft.storeName, true,
			[this] { editText(_("NOME DO ESTABELECIMENTO"), mDraft.storeName, false, [this](const std::string& value) { mDraft.storeName = value; }); });
		mMenu.addEntry(std::string(_("CAIXA/PDV GERENCIADO AUTOMATICAMENTE: ")) + mDraft.posExternalId, false);
		mMenu.addEntry(std::string(_("NOME DO CAIXA: ")) + mDraft.posName, true,
			[this] { editText(_("NOME DO CAIXA PIX"), mDraft.posName, false, [this](const std::string& value) { mDraft.posName = value; }); });

		mMenu.addGroup(_("ENDERECO DO ESTABELECIMENTO"));
		mMenu.addEntry(std::string(_("CEP: ")) + (mDraft.postalCode.empty() ? _("NAO INFORMADO") : mDraft.postalCode), true,
			[this] { editText(_("CEP - 8 NUMEROS"), mDraft.postalCode, false, [this](const std::string& value) { mDraft.postalCode = value; }); });
		mMenu.addEntry(std::string(_("NUMERO / COMPLEMENTO: ")) + (mDraft.streetNumber.empty() ? _("NAO INFORMADO") : mDraft.streetNumber), true,
			[this] { editText(_("NUMERO OU NUMERO COM COMPLEMENTO"), mDraft.streetNumber, false, [this](const std::string& value) { mDraft.streetNumber = value; }); });
		mMenu.addEntry(std::string(_("REFERENCIA: ")) + mDraft.reference, true,
			[this] { editText(_("REFERENCIA DO ESTABELECIMENTO"), mDraft.reference, false, [this](const std::string& value) { mDraft.reference = value; }); });
	}

	mMenu.addGroup(_("CREDENCIAL PROTEGIDA"));
	mMenu.addEntry(_("ABRIR CONFIGURADOR PIX AGORA"), true,
		[this] { launchOwnerConfigurator(); });
	mMenu.addEntry(_("SESSAO OBRIGATORIA: CONTA WINDOWS ARCADE"), false);
	mMenu.addEntry(_("ABRA NORMALMENTE - NAO USE EXECUTAR COMO ADMINISTRADOR"), false);

	mMenu.addGroup(_("PRECOS PARA O CLIENTE"));
	for (const int minutes : { 15, 30, 45, 60, 120 })
	{
		const long long cents = mDraft.pricesCents.count(minutes) ? mDraft.pricesCents[minutes] : 0;
		mMenu.addEntry(std::to_string(minutes) + _(" MINUTOS: ") + formatPrice(cents), true,
			[this, minutes] { editPrice(minutes); });
	}

	mMenu.addGroup(_("ATIVACAO"));
	mMenu.addEntry(_("SALVAR, CONFIGURAR E ATIVAR PIX"), true, [this] { saveAndActivate(); });
	mMenu.addEntry(_("REINICIAR E VERIFICAR SERVICO PIX"), true, [this] {
		std::string error;
		if (PixAgentManager::restartIfConfigured(error))
			mWindow->displayNotificationMessage(_("Servico PIX iniciado. Aguarde a verificacao."), 5);
		else
			mWindow->pushGui(new GuiMsgBox(mWindow, error, _("OK"), nullptr, ICON_ERROR));
		rebuild();
	});
	mMenu.addButton(_("VOLTAR"), "back", [this] { delete this; });
	mMenu.updateSize();
	centerOnScreen();
}

void GuiPixOwnerSettings::saveAndActivate()
{
	mWindow->pushGui(new GuiMsgBox(mWindow,
		_("Salvar as alteracoes deste estabelecimento e ativar o PIX?\n\n"
			"Para trocar conta, credencial, Loja ou PDV, cancele e use ABRIR CONFIGURADOR PIX AGORA nesta tela."),
		_("SIM, ATIVAR"), [this] {
			std::string error;
			if (!PixAgentManager::saveOwnerSettings(mDraft, "", error))
			{
				mWindow->pushGui(new GuiMsgBox(mWindow, error, _("OK"), nullptr, ICON_ERROR));
				return;
			}
			mDraft = PixAgentManager::loadOwnerSettings();
			if (!PixAgentManager::restartIfConfigured(error))
			{
				mWindow->pushGui(new GuiMsgBox(mWindow,
					_("Os dados foram salvos, mas o servico PIX nao iniciou:\n\n") + error,
					_("OK"), nullptr, ICON_ERROR));
				return;
			}
			mWindow->pushGui(new GuiMsgBox(mWindow,
				_("DADOS SALVOS.\n\nO EmulationStation preservou a conta e o provedor atuais. Para trocar o proprietario ou validar uma nova credencial, use ABRIR CONFIGURADOR PIX AGORA nesta tela."),
				_("OK"), [this] { rebuild(); }, ICON_INFORMATION));
		},
		_("CANCELAR"), nullptr, ICON_QUESTION));
}

bool GuiPixOwnerSettings::input(InputConfig* config, Input input)
{
	if (config->isMappedTo(BUTTON_BACK, input) && input.value != 0)
	{
		delete this;
		return true;
	}
	return GuiComponent::input(config, input);
}

std::vector<HelpPrompt> GuiPixOwnerSettings::getHelpPrompts()
{
	return { HelpPrompt(BUTTON_OK, _("ALTERAR")), HelpPrompt(BUTTON_BACK, _("VOLTAR")) };
}
