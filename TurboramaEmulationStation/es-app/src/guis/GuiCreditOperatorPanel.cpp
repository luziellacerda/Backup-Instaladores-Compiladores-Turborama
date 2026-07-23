#include "guis/GuiCreditOperatorPanel.h"

#include "CreditManager.h"
#include "LocaleES.h"
#include "Settings.h"
#include "Window.h"
#include "guis/GuiCreditPlayerSelect.h"
#include "guis/GuiMsgBox.h"
#include "guis/GuiTextEditPopup.h"
#include "guis/GuiTextEditPopupKeyboard.h"
#include "renderers/Renderer.h"
#include "utils/Platform.h"
#include "utils/StringUtil.h"

GuiCreditOperatorPanel::GuiCreditOperatorPanel(Window* window)
	: GuiComponent(window)
	, mMenu(window, "TURBORAMA  ·  LOCADORA")
{
	// Parent full-screen (0,0) — o menu fica centrado DENTRO (evita offset errado)
	setPosition(0, 0);
	setSize((float)Renderer::getScreenWidth(), (float)Renderer::getScreenHeight());

	addChild(&mMenu);
	rebuild();
	centerOnScreen();
}

void GuiCreditOperatorPanel::centerOnScreen()
{
	// Nao chamar setSize do parent aqui (evita recursao)
	const float mw = mMenu.getSize().x();
	const float mh = mMenu.getSize().y();
	const float x = (Renderer::getScreenWidth() - mw) * 0.5f;
	const float y = (Renderer::getScreenHeight() - mh) * 0.5f;
	mMenu.setPosition(x > 0 ? x : 0, y > 0 ? y : 0);
}

void GuiCreditOperatorPanel::refreshHeader()
{
	auto& cm = CreditManager::getInstance();

	std::string sub;
	sub += _("Saldo: ");
	sub += cm.formatRemaining();
	sub += "  |  ";

	const std::string jog = cm.getCurrentPlayerName();
	if (jog.empty())
	{
		if (cm.hasGuestCredit() || cm.getRemainingSeconds() > 0)
			sub += _("Avulso");
		else
			sub += _("Livre");
	}
	else
		sub += jog;

	sub += "  |  ";
	if (!cm.isSessionRunning())
		sub += _("PARADO");
	else if (cm.isSessionPaused())
		sub += _("PAUSADO");
	else
		sub += _("CONTANDO");

	mMenu.setSubTitle(sub);
}

void GuiCreditOperatorPanel::rebuild()
{
	mMenu.clear();
	refreshHeader();

	auto& cm = CreditManager::getInstance();

	mMenu.addGroup(_("CREDITO"));
	mMenu.addEntry(
		std::string(_("+ MOEDA (")) + std::to_string(cm.getMinutesPerCoin()) + _(" min)"),
		false, [this] { doAddCoin(); });
	mMenu.addEntry(_("+ 15 minutos"), false, [this] { doAddMinutes(15); });
	mMenu.addEntry(_("+ 30 minutos"), false, [this] { doAddMinutes(30); });
	mMenu.addEntry(_("+ 60 minutos (1 h)"), false, [this] { doAddMinutes(60); });
	mMenu.addEntry(_("+ 120 minutos (2 h)"), false, [this] { doAddMinutes(120); });
	mMenu.addEntry(_("Digitar minutos..."), true, [this] { doAskMinutes(); });

	mMenu.addGroup(_("CONTADOR"));
	mMenu.addEntry(_("Continuar / iniciar"), false, [this] { doResume(); });
	mMenu.addEntry(_("Pausar"), false, [this] { doPause(); });
	mMenu.addEntry(_("Parar"), false, [this] { doStop(); });

	mMenu.addGroup(_("CLIENTE"));
	mMenu.addEntry(_("Escolher / liberar jogador"), true, [this] { doChoosePlayer(); });
	mMenu.addEntry(_("Cadastrar novo jogador"), true, [this] { doRegisterPlayer(); });
	mMenu.addEntry(_("Finalizar jogador cadastrado"), false, [this] { doEndPlayer(); });
	mMenu.addEntry(_("Fechar credito avulso"), false, [this] { doCloseGuest(); });
	mMenu.addEntry(_("Remover jogador ativo"), false, [this] { doRemovePlayer(); });

	mMenu.addGroup(_("TURBO SISTEMA"));
	mMenu.addEntry(_("Abrir Turbo Sistema..."), false, [this] { doOpenTurboSistema(); });
	mMenu.addEntry(_("Trocar de usuario..."), false, [this] { doSwitchUser(); });

	mMenu.addButton(_("FECHAR"), "back", [this] { delete this; });

	centerOnScreen();
}

void GuiCreditOperatorPanel::doAddCoin()
{
	auto& c = CreditManager::getInstance();
	if (c.addCoin())
	{
		const std::string who = c.getCurrentPlayerName().empty() ? _("Avulso") : c.getCurrentPlayerName();
		mWindow->displayNotificationMessage(
			std::string(_("Moeda +")) + std::to_string(c.getMinutesPerCoin()) +
			_(" min | ") + c.formatRemaining() + " | " + who, 5);
	}
	else
		mWindow->displayNotificationMessage(_("Moeda nao registada — aguarde"), 3);
	refreshHeader();
}

void GuiCreditOperatorPanel::doAddMinutes(int mins)
{
	auto& c = CreditManager::getInstance();
	if (c.addMinutes(mins))
	{
		const std::string who = c.getCurrentPlayerName().empty() ? _("Avulso") : c.getCurrentPlayerName();
		mWindow->displayNotificationMessage(
			std::string("+") + std::to_string(mins) + _(" min | ") + c.formatRemaining() + " | " + who, 5);
	}
	else
		mWindow->displayNotificationMessage(_("Nao foi possivel adicionar minutos"), 3);
	refreshHeader();
}

void GuiCreditOperatorPanel::doAskMinutes()
{
	auto onMinutes = [this](const std::string& text) {
		std::string digits;
		for (char ch : text)
			if (ch >= '0' && ch <= '9')
				digits.push_back(ch);
		if (digits.empty())
		{
			mWindow->displayNotificationMessage(_("Digite um numero valido"));
			return;
		}
		int mins = Utils::String::toInteger(digits);
		if (mins < 1) mins = 1;
		if (mins > 480) mins = 480;
		doAddMinutes(mins);
	};

	if (Settings::getInstance()->getBool("UseOSK"))
		mWindow->pushGui(new GuiTextEditPopupKeyboard(mWindow, _("MINUTOS"), "", onMinutes, false));
	else
		mWindow->pushGui(new GuiTextEditPopup(mWindow, _("MINUTOS"), "", onMinutes, false));
}

void GuiCreditOperatorPanel::doResume()
{
	CreditManager::getInstance().resumeSession();
	mWindow->displayNotificationMessage(_("Contador: CONTANDO"), 3);
	refreshHeader();
}

void GuiCreditOperatorPanel::doPause()
{
	CreditManager::getInstance().pauseSession();
	mWindow->displayNotificationMessage(_("Contador: PAUSADO"), 3);
	refreshHeader();
}

void GuiCreditOperatorPanel::doStop()
{
	CreditManager::getInstance().stopSession();
	mWindow->displayNotificationMessage(_("Contador: PARADO"), 3);
	refreshHeader();
}

void GuiCreditOperatorPanel::doEndPlayer()
{
	const std::string was = CreditManager::getInstance().getCurrentPlayerName();
	if (was.empty())
	{
		mWindow->displayNotificationMessage(_("Nenhum cadastrado ativo"));
		return;
	}
	CreditManager::getInstance().endActivePlayerTurn();
	mWindow->displayNotificationMessage(std::string(_("Turno de ")) + was + _(" finalizado"), 4);
	refreshHeader();
}

void GuiCreditOperatorPanel::doCloseGuest()
{
	auto& c = CreditManager::getInstance();
	if (!c.getCurrentPlayerName().empty())
	{
		mWindow->displayNotificationMessage(_("Ha jogador cadastrado — use Finalizar jogador"));
		return;
	}
	if (c.getRemainingSeconds() <= 0)
	{
		mWindow->displayNotificationMessage(_("Nenhum credito avulso"));
		return;
	}
	mWindow->pushGui(new GuiMsgBox(mWindow,
		_("Apagar credito avulso restante?"),
		_("SIM"), [this] {
			CreditManager::getInstance().clearGuestCredit();
			mWindow->displayNotificationMessage(_("Avulso fechado"));
			refreshHeader();
		},
		_("NAO"), nullptr));
}

void GuiCreditOperatorPanel::doChoosePlayer()
{
	mWindow->pushGui(new GuiCreditPlayerSelect(mWindow,
		[this](const std::string&) { refreshHeader(); },
		[this]() { refreshHeader(); }));
}

void GuiCreditOperatorPanel::doRegisterPlayer()
{
	auto onName = [this](const std::string& name) {
		if (CreditManager::getInstance().registerPlayer(name))
		{
			mWindow->displayNotificationMessage(std::string(_("Jogador: ")) + name, 4);
			refreshHeader();
		}
		else
			mWindow->displayNotificationMessage(_("Nome invalido ou limite"));
	};
	if (Settings::getInstance()->getBool("UseOSK"))
		mWindow->pushGui(new GuiTextEditPopupKeyboard(mWindow, _("NOME DO JOGADOR"), "", onName, false));
	else
		mWindow->pushGui(new GuiTextEditPopup(mWindow, _("NOME DO JOGADOR"), "", onName, false));
}

void GuiCreditOperatorPanel::doRemovePlayer()
{
	const std::string cur = CreditManager::getInstance().getCurrentPlayerName();
	if (cur.empty())
	{
		mWindow->displayNotificationMessage(_("Nenhum jogador ativo"));
		return;
	}
	mWindow->pushGui(new GuiMsgBox(mWindow,
		std::string(_("Remover ")) + cur + "?",
		_("SIM"), [this, cur] {
			if (CreditManager::getInstance().removePlayer(cur))
			{
				mWindow->displayNotificationMessage(std::string(_("Removido: ")) + cur);
				refreshHeader();
			}
		},
		_("NAO"), nullptr));
}

void GuiCreditOperatorPanel::doOpenTurboSistema()
{
#ifdef WIN32
	mWindow->pushGui(new GuiMsgBox(mWindow,
		_("Abrir o Turbo Sistema (ambiente do sistema)?\n\nPode voltar ao EmulationStation depois."),
		_("SIM, ABRIR"), [this] {
			CreditManager::getInstance().flushNow();
			mWindow->displayNotificationMessage(_("A abrir Turbo Sistema..."), 2);

			// Abre o ambiente do sistema (shell) sem usar a palavra Windows no menu.
			Utils::Platform::ProcessStartInfo ex("explorer.exe");
			ex.waitForExit = false;
			ex.showWindow = true;
			if (ex.run() != 0)
			{
				Utils::Platform::ProcessStartInfo shell("C:\\Windows\\explorer.exe");
				shell.waitForExit = false;
				shell.showWindow = true;
				shell.run();
			}
		},
		_("NAO"), nullptr));
#else
	mWindow->displayNotificationMessage(_("So disponivel neste sistema"), 3);
#endif
}

void GuiCreditOperatorPanel::doSwitchUser()
{
#ifdef WIN32
	mWindow->pushGui(new GuiMsgBox(mWindow,
		_("Trocar de usuario?\n\nA sessao actual e desligada e aparece o ecran de contas."),
		_("SIM, TROCAR"), [this] {
			CreditManager::getInstance().flushNow();
			mWindow->displayNotificationMessage(_("A trocar de usuario..."), 2);

			Utils::Platform::ProcessStartInfo sw("C:\\Windows\\System32\\tsdiscon.exe");
			sw.waitForExit = false;
			sw.showWindow = false;
			const int r = sw.run();
			if (r != 0)
			{
				Utils::Platform::ProcessStartInfo lo("shutdown /l");
				lo.waitForExit = false;
				lo.showWindow = false;
				lo.run();
			}
		},
		_("NAO"), nullptr));
#else
	mWindow->displayNotificationMessage(_("So disponivel neste sistema"), 3);
#endif
}

bool GuiCreditOperatorPanel::input(InputConfig* config, Input input)
{
	if (config->isMappedTo(BUTTON_BACK, input) && input.value != 0)
	{
		delete this;
		return true;
	}

	if (config->isMappedTo("start", input) && input.value != 0)
		return true;

	if (mMenu.input(config, input))
		return true;

	return GuiComponent::input(config, input);
}

std::vector<HelpPrompt> GuiCreditOperatorPanel::getHelpPrompts()
{
	std::vector<HelpPrompt> prompts = mMenu.getHelpPrompts();
	prompts.push_back(HelpPrompt(BUTTON_BACK, _("FECHAR")));
	return prompts;
}
