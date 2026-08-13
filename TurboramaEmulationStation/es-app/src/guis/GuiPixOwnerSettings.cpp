#include "guis/GuiPixOwnerSettings.h"

#include "LocaleES.h"
#include "Paths.h"
#include "PixBinaryTrust.h"
#include "Settings.h"
#include "Window.h"
#include "components/OptionListComponent.h"
#include "guis/GuiMsgBox.h"
#include "guis/GuiSettings.h"
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

void GuiPixOwnerSettings::openHomeQrManager()
{
	const std::vector<int> packages = { 15, 30, 45, 60, 120 };
	int selectedMinutes = Settings::getInstance()->getInt("PixHomeQrMinutes");
	if (std::find(packages.begin(), packages.end(), selectedMinutes) == packages.end())
		selectedMinutes = 15;

	GuiSettings* manager = new GuiSettings(mWindow, _("GERENCIAR QR AUTOMATICO"));
	manager->setSubTitle(_("ESCOLHA O TEMPO E EDITE O VALOR QUE APARECERA NA TELA PRINCIPAL"));
	auto timeChoice = std::make_shared<OptionListComponent<int>>(mWindow, _("TEMPO DO QR PRINCIPAL"), false);
	for (const int minutes : packages)
	{
		const long long cents = mDraft.pricesCents.count(minutes) ? mDraft.pricesCents[minutes] : 0;
		timeChoice->add(std::to_string(minutes) + _(" MINUTOS - ") + formatPrice(cents),
			minutes, minutes == selectedMinutes);
	}
	manager->addWithLabel(_("TEMPO DO QR PRINCIPAL"), timeChoice, true);

	manager->addGroup(_("ALTERAR VALORES DOS PACOTES"));
	for (const int minutes : packages)
	{
		const long long cents = mDraft.pricesCents.count(minutes) ? mDraft.pricesCents[minutes] : 0;
		manager->addEntry(std::to_string(minutes) + _(" MINUTOS: ") + formatPrice(cents), true,
			[this, minutes] { editPrice(minutes); });
	}
	manager->addSaveFunc([this, timeChoice] {
		Settings::getInstance()->setInt("PixHomeQrMinutes", timeChoice->getSelected());
		Settings::getInstance()->saveFile();
		rebuild();
	});
	manager->setCloseButton(_("SALVAR E VOLTAR"));
	mWindow->pushGui(manager);
}

void GuiPixOwnerSettings::openProviderManager()
{
	GuiSettings* manager = new GuiSettings(mWindow, _("ESCOLHER PROVEDOR PIX"));
	manager->setSubTitle(_("A COBRANCA E FEITA PELO PROVEDOR LOCAL; A LICENCA ONLINE E UMA CAMADA SEPARADA"));
	auto provider = std::make_shared<OptionListComponent<std::string>>(mWindow, _("PROVEDOR PIX"), false);
	provider->add(_("MERCADO PAGO DIRETO NESTE COMPUTADOR"), "mercadopago", mDraft.provider == "mercadopago");
	provider->add(_("OUTRO BANCO / ADAPTADOR LOCAL"), "adapter", mDraft.provider == "adapter");
	manager->addWithLabel(_("PROVEDOR PIX"), provider, true);
	manager->addSaveFunc([this, provider] {
		mDraft.provider = provider->getSelected();
		rebuild();
	});
	manager->setCloseButton(_("USAR ESTE PROVEDOR"));
	mWindow->pushGui(manager);
}

void GuiPixOwnerSettings::openOnlineProtectionManager()
{
	GuiSettings* manager = new GuiSettings(mWindow, _("PROTECAO DESTA MAQUINA"));
	manager->setSubTitle(_("USE TPM SOMENTE QUANDO ELE ESTIVER DISPONIVEL E ATIVO NESTE COMPUTADOR"));
	auto profile = std::make_shared<OptionListComponent<std::string>>(mWindow, _("PERFIL DE PROTECAO"), false);
	profile->add(_("SEM TPM - PROTECAO ONLINE"), "SOFTWARE_BOUND_ONLINE",
		mDraft.onlineProtectionProfile == "SOFTWARE_BOUND_ONLINE");
	profile->add(_("TPM DESTA PLACA-MAE"), "TPM_BOUND",
		mDraft.onlineProtectionProfile == "TPM_BOUND");
	manager->addWithLabel(_("PERFIL DE PROTECAO"), profile, true);
	manager->addEntry(_("TROCAR TPM, PLACA-MAE OU PERFIL EXIGE NOVA LIBERACAO NO PAINEL"), false);
	manager->addSaveFunc([this, profile] {
		mDraft.onlineProtectionProfile = profile->getSelected();
		rebuild();
	});
	manager->setCloseButton(_("USAR ESTA PROTECAO"));
	mWindow->pushGui(manager);
}

void GuiPixOwnerSettings::activateOnlineMachine()
{
	mWindow->pushGui(new GuiMsgBox(mWindow,
		_("A ativacao vincula esta instalacao a licenca cadastrada no painel TurboRama.\n\n"
			"O codigo e usado uma unica vez, enviado por canal protegido e nunca e salvo neste computador."),
		_("DIGITAR CODIGO"), [this] {
			editText(_("CODIGO DE ATIVACAO GERADO NO PAINEL"), "", true,
				[this](const std::string& entered) {
					std::string activationCode = entered;
					std::string error;
					const bool activated = PixAgentManager::activateOnline(mDraft, activationCode, error);
#ifdef _WIN32
					if (!activationCode.empty()) SecureZeroMemory(activationCode.data(), activationCode.size());
#else
					std::fill(activationCode.begin(), activationCode.end(), '\0');
#endif
					mDraft = PixAgentManager::loadOwnerSettings();
					if (!activated)
					{
						mWindow->pushGui(new GuiMsgBox(mWindow, error, _("OK"), nullptr, ICON_ERROR));
						return;
					}
					mWindow->pushGui(new GuiMsgBox(mWindow,
						_("MAQUINA ATIVADA.\n\nA licenca foi vinculada. Os precos e a cobranca PIX continuam sendo gerenciados localmente pelo TurboRama."),
						_("OK"), [this] { rebuild(); }, ICON_INFORMATION));
				});
		}, _("CANCELAR"), nullptr, ICON_QUESTION));
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
	std::string signatureError;
	if (!PixBinaryTrust::verifyVendorBinary(executable, signatureError))
	{
		mWindow->pushGui(new GuiMsgBox(mWindow,
			_("O CONFIGURADOR PIX FOI RECUSADO PELA PROTECAO COMERCIAL.\n\n")
				+ signatureError,
			_("OK"), nullptr, ICON_ERROR));
		return;
	}
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
	const std::string providerLabel = mDraft.provider == "adapter"
		? _("OUTRO BANCO / ADAPTADOR") : _("MERCADO PAGO");
	mMenu.addEntry(std::string(_("PROVEDOR ATIVO: ")) + providerLabel, true,
		[this] { openProviderManager(); });

	if (mDraft.onlineLicensingEnabled
		|| mDraft.onlineLicenseId != "CONFIGURE-A-LICENCA")
	{
		mMenu.addGroup(_("LICENCA TURBORAMA ONLINE"));
		mMenu.addEntry(std::string(_("LICENCA: ")) + mDraft.onlineLicenseId, true,
			[this] { editText(_("NUMERO DA LICENCA CRIADA NO PAINEL"), mDraft.onlineLicenseId, false,
				[this](const std::string& value) { mDraft.onlineLicenseId = value; }); });
		mMenu.addEntry(std::string(_("SERVIDOR: ")) + mDraft.onlineBaseUrl, false);
		const std::string protectionLabel = mDraft.onlineProtectionProfile == "TPM_BOUND"
			? _("TPM DESTA PLACA-MAE") : _("SEM TPM - PROTECAO ONLINE");
		mMenu.addEntry(std::string(_("PROTECAO: ")) + protectionLabel, true,
			[this] { openOnlineProtectionManager(); });
		mMenu.addEntry(_("O SERVIDOR RECONHECE A MAQUINA; NAO ALTERA PRECO, CONTA OU PDV"), false);
		mMenu.addEntry(_("SEM SERVIDOR TEMPORARIAMENTE: QUIOSQUE E CREDITOS CONTINUAM FUNCIONANDO"), false);
		mMenu.addEntry(_("ATIVAR ESTA MAQUINA COM CODIGO DO PAINEL"), true,
			[this] { activateOnlineMachine(); });
	}
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
	mMenu.addEntry(_("A CREDENCIAL PERTENCE AO PROVEDOR LOCAL DE PAGAMENTO"), false);

	mMenu.addGroup(_("PRECOS PARA O CLIENTE"));
	for (const int minutes : { 15, 30, 45, 60, 120 })
	{
		const long long cents = mDraft.pricesCents.count(minutes) ? mDraft.pricesCents[minutes] : 0;
		mMenu.addEntry(std::to_string(minutes) + _(" MINUTOS: ") + formatPrice(cents), true,
			[this, minutes] { editPrice(minutes); });
	}

	int homeQrMinutes = Settings::getInstance()->getInt("PixHomeQrMinutes");
	const std::vector<int> homePackages = { 15, 30, 45, 60, 120 };
	if (std::find(homePackages.begin(), homePackages.end(), homeQrMinutes) == homePackages.end())
		homeQrMinutes = 15;
	const long long homeQrCents = mDraft.pricesCents.count(homeQrMinutes)
		? mDraft.pricesCents[homeQrMinutes] : 0;
	mMenu.addGroup(_("QR AUTOMATICO DA TELA PRINCIPAL"));
	mMenu.addEntry(_("GERENCIAR TEMPO E VALOR DO QR"), true,
		[this] { openHomeQrManager(); });
	mMenu.addEntry(std::string(_("CONFIGURADO: ")) + std::to_string(homeQrMinutes)
		+ _(" MINUTOS - ") + formatPrice(homeQrCents), false);
	mMenu.addEntry(_("O QR E GERADO SOZINHO AO ABRIR O TURBORAMA"), false);
	mMenu.addEntry(_("A FRASE DA TELA ACOMPANHA O TEMPO E O VALOR ESCOLHIDOS"), false);

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
		_("Salvar os precos locais e ativar o PIX neste quiosque?\n\n"
			"Para trocar conta, credencial, Loja ou PDV, cancele e use ABRIR CONFIGURADOR PIX AGORA nesta tela."),
		_("SIM, ATIVAR"), [this] {
			std::string error;
			mDraft.enabled = true;
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
				_("DADOS SALVOS.\n\nOs precos permanecem neste computador. O EmulationStation preservou a conta e o provedor atuais; a licenca online continua separada."),
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
