#include "guis/GuiPixOwnerSettings.h"

#include "LocaleES.h"
#include "Settings.h"
#include "Window.h"
#include "guis/GuiMsgBox.h"
#include "guis/GuiTextEditPopup.h"
#include "guis/GuiTextEditPopupKeyboard.h"
#include "renderers/Renderer.h"

#include <algorithm>
#include <cctype>
#include <iomanip>
#include <sstream>

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

void GuiPixOwnerSettings::rebuild()
{
	mMenu.clear();
	mMenu.setSubTitle(std::string(_("STATUS: ")) + PixAgentManager::statusText());

	mMenu.addGroup(_("DADOS DO PROPRIETARIO / MERCADO PAGO"));
	mMenu.addEntry(std::string(_("USER ID DA CONTA: ")) + (mDraft.accountId.empty() ? _("NAO INFORMADO") : mDraft.accountId), true,
		[this] { editText(_("USER ID NUMERICO DO MERCADO PAGO"), mDraft.accountId, false, [this](const std::string& value) { mDraft.accountId = value; }); });
	mMenu.addEntry(std::string(_("IDENTIFICADOR DA LOJA: ")) + mDraft.storeExternalId, true,
		[this] { editText(_("IDENTIFICADOR DA LOJA - SOMENTE LETRAS E NUMEROS"), mDraft.storeExternalId, false, [this](const std::string& value) { mDraft.storeExternalId = value; }); });
	mMenu.addEntry(std::string(_("NOME DA LOJA: ")) + mDraft.storeName, true,
		[this] { editText(_("NOME DO ESTABELECIMENTO"), mDraft.storeName, false, [this](const std::string& value) { mDraft.storeName = value; }); });
	mMenu.addEntry(std::string(_("IDENTIFICADOR DO CAIXA: ")) + mDraft.posExternalId, true,
		[this] { editText(_("IDENTIFICADOR DO CAIXA - SOMENTE LETRAS E NUMEROS"), mDraft.posExternalId, false, [this](const std::string& value) { mDraft.posExternalId = value; }); });
	mMenu.addEntry(std::string(_("NOME DO CAIXA: ")) + mDraft.posName, true,
		[this] { editText(_("NOME DO CAIXA PIX"), mDraft.posName, false, [this](const std::string& value) { mDraft.posName = value; }); });

	mMenu.addGroup(_("ENDERECO DO ESTABELECIMENTO"));
	mMenu.addEntry(std::string(_("CEP: ")) + (mDraft.postalCode.empty() ? _("NAO INFORMADO") : mDraft.postalCode), true,
		[this] { editText(_("CEP - 8 NUMEROS"), mDraft.postalCode, false, [this](const std::string& value) { mDraft.postalCode = value; }); });
	mMenu.addEntry(std::string(_("NUMERO / COMPLEMENTO: ")) + (mDraft.streetNumber.empty() ? _("NAO INFORMADO") : mDraft.streetNumber), true,
		[this] { editText(_("NUMERO OU NUMERO COM COMPLEMENTO"), mDraft.streetNumber, false, [this](const std::string& value) { mDraft.streetNumber = value; }); });
	mMenu.addEntry(std::string(_("REFERENCIA: ")) + mDraft.reference, true,
		[this] { editText(_("REFERENCIA DO ESTABELECIMENTO"), mDraft.reference, false, [this](const std::string& value) { mDraft.reference = value; }); });

	mMenu.addGroup(_("CREDENCIAL PROTEGIDA"));
	const bool tokenReady = !mPendingAccessToken.empty() || PixAgentManager::hasProtectedToken();
	mMenu.addEntry(std::string(_("ACCESS TOKEN: ")) + (tokenReady ? _("CONFIGURADO E PROTEGIDO") : _("NAO CONFIGURADO")), true,
		[this] { editText(_("ACCESS TOKEN - INICIA COM APP_USR-"), "", true, [this](const std::string& value) { mPendingAccessToken = value; }); });

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
		_("Salvar os dados deste estabelecimento e ativar o PIX?\n\n"
			"O Access Token sera criptografado pelo Windows e nao aparecera no menu do cliente."),
		_("SIM, ATIVAR"), [this] {
			std::string error;
			if (!PixAgentManager::saveOwnerSettings(mDraft, mPendingAccessToken, error))
			{
				mWindow->pushGui(new GuiMsgBox(mWindow, error, _("OK"), nullptr, ICON_ERROR));
				return;
			}
			mPendingAccessToken.clear();
			mDraft = PixAgentManager::loadOwnerSettings();
			if (!PixAgentManager::restartIfConfigured(error))
			{
				mWindow->pushGui(new GuiMsgBox(mWindow,
					_("Os dados foram salvos, mas o servico PIX nao iniciou:\n\n") + error,
					_("OK"), nullptr, ICON_ERROR));
				return;
			}
			mWindow->pushGui(new GuiMsgBox(mWindow,
				_("DADOS SALVOS.\n\nO sistema esta localizando o endereco pelo CEP e configurando a loja e o caixa no Mercado Pago. Aguarde alguns segundos e use VERIFICAR SERVICO PIX."),
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
