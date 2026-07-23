#include "guis/GuiCreditPlayerSelect.h"

#include "LocaleES.h"
#include "Settings.h"
#include "Window.h"
#include "guis/GuiMsgBox.h"
#include "guis/GuiTextEditPopup.h"
#include "guis/GuiTextEditPopupKeyboard.h"
#include "utils/StringUtil.h"

#include <algorithm>

GuiCreditPlayerSelect::GuiCreditPlayerSelect(Window* window,
	const std::function<void(const std::string& name)>& onSelected,
	const std::function<void()>& onClosed)
	: GuiComponent(window)
	, mMenu(window, _("ESCOLHER JOGADOR"))
	, mOnSelected(onSelected)
	, mOnClosed(onClosed)
{
	addChild(&mMenu);
	rebuildList();

	if (Renderer::ScreenSettings::fullScreenMenus())
		mMenu.setPosition((Renderer::getScreenWidth() - mMenu.getSize().x()) / 2,
			(Renderer::getScreenHeight() - mMenu.getSize().y()) / 2);
	else
		mMenu.setPosition((Renderer::getScreenWidth() - mMenu.getSize().x()) / 2,
			Renderer::getScreenHeight() * 0.12f);
}

bool GuiCreditPlayerSelect::nameMatchesFilter(const std::string& name, const std::string& filter)
{
	if (filter.empty())
		return true;
	return Utils::String::containsIgnoreCase(name, filter);
}

void GuiCreditPlayerSelect::rebuildList()
{
	mMenu.clear();

	auto& cm = CreditManager::getInstance();
	const std::string active = cm.getCurrentPlayerName();
	auto players = cm.getPlayersCopy();

	// Ordena A-Z (pt-BR friendly: case insensitive)
	std::sort(players.begin(), players.end(), [](const CreditPlayer& a, const CreditPlayer& b) {
		return Utils::String::toLower(a.name) < Utils::String::toLower(b.name);
	});

	// --- BUSCA ---
	mMenu.addGroup(_("BUSCA"));
	{
		std::string searchLabel = mFilter.empty()
			? _("PESQUISAR POR NOME...")
			: (std::string(_("Filtro: ")) + "\"" + mFilter + "\"");
		mMenu.addEntry(searchLabel, true, [this] { openSearch(); });

		if (!mFilter.empty())
		{
			mMenu.addEntry(_("LIMPAR BUSCA (mostrar todos)"), false, [this] {
				mFilter.clear();
				rebuildList();
			});
		}
	}

	// --- LISTA ---
	int shown = 0;
	int total = (int)players.size();
	for (const auto& p : players)
	{
		if (!nameMatchesFilter(p.name, mFilter))
			continue;
		shown++;
	}

	std::string groupTitle = _("JOGADORES");
	groupTitle += " (" + std::to_string(shown) + "/" + std::to_string(total) + ")";
	mMenu.addGroup(groupTitle);

	if (total == 0)
	{
		mMenu.addEntry(_("Nenhum jogador cadastrado"), false);
	}
	else if (shown == 0)
	{
		mMenu.addEntry(_("Nenhum resultado para esta busca"), false);
	}
	else
	{
		// MenuComponent ja tem scroll para listas longas (100+)
		for (const auto& p : players)
		{
			if (!nameMatchesFilter(p.name, mFilter))
				continue;

			std::string label;
			if (!active.empty() && Utils::String::toLower(active) == Utils::String::toLower(p.name))
				label = "* ";
			label += p.name;
			label += "  |  ";
			label += _("saldo ");
			label += cm.formatPlayerCredit(p.name);
			label += "  |  ";
			label += cm.formatPlayerHours(p.name);

			const std::string pname = p.name;
			mMenu.addEntry(label, true, [this, pname] { selectPlayer(pname); });
		}
	}

	// --- ACOES ---
	mMenu.addGroup(_("ACOES"));
	mMenu.addEntry(_("CADASTRAR NOVO JOGADOR"), true, [this] { registerNewPlayer(); });
	mMenu.addEntry(_("VOLTAR"), false, [this] {
		if (mOnClosed)
			mOnClosed();
		delete this;
	});

	mMenu.addButton(_("BUSCAR"), "y", [this] { openSearch(); });
	mMenu.addButton(_("VOLTAR"), "back", [this] {
		if (mOnClosed)
			mOnClosed();
		delete this;
	});

	mMenu.updateSize();
	setSize(mMenu.getSize());

	if (Renderer::ScreenSettings::fullScreenMenus())
		mMenu.setPosition((Renderer::getScreenWidth() - mMenu.getSize().x()) / 2,
			(Renderer::getScreenHeight() - mMenu.getSize().y()) / 2);
	else
		mMenu.setPosition((Renderer::getScreenWidth() - mMenu.getSize().x()) / 2,
			Renderer::getScreenHeight() * 0.12f);
}

void GuiCreditPlayerSelect::openSearch()
{
	auto updateVal = [this](const std::string& newVal) {
		mFilter = Utils::String::trim(newVal);
		rebuildList();
	};

	if (Settings::getInstance()->getBool("UseOSK"))
		mWindow->pushGui(new GuiTextEditPopupKeyboard(mWindow, _("PESQUISAR JOGADOR"), mFilter, updateVal, false));
	else
		mWindow->pushGui(new GuiTextEditPopup(mWindow, _("PESQUISAR JOGADOR"), mFilter, updateVal, false));
}

void GuiCreditPlayerSelect::selectPlayer(const std::string& name)
{
	if (!CreditManager::getInstance().switchToPlayer(name))
	{
		mWindow->pushGui(new GuiMsgBox(mWindow, _("Nao foi possivel liberar este jogador"), _("OK"), nullptr));
		return;
	}

	mWindow->displayNotificationMessage(
		std::string(_("Jogador liberado: ")) + name + "  |  " +
		_("Saldo: ") + CreditManager::getInstance().formatRemaining() +
		"  [" + _("PARADO") + "]", 6);

	const std::string selected = name;
	auto onSelected = mOnSelected;
	delete this;
	if (onSelected)
		onSelected(selected);
}

void GuiCreditPlayerSelect::registerNewPlayer()
{
	auto onName = [this](const std::string& name) {
		if (CreditManager::getInstance().registerPlayer(name))
		{
			mWindow->displayNotificationMessage(
				std::string(_("Cadastrado e liberado: ")) + name + _(" (saldo 0)"), 5);
			const std::string selected = CreditManager::getInstance().getCurrentPlayerName();
			auto onSelected = mOnSelected;
			delete this;
			if (onSelected)
				onSelected(selected);
		}
		else
			mWindow->displayNotificationMessage(_("Nome invalido ou limite de jogadores"));
	};

	if (Settings::getInstance()->getBool("UseOSK"))
		mWindow->pushGui(new GuiTextEditPopupKeyboard(mWindow, _("NOME DO NOVO JOGADOR"), "", onName, false));
	else
		mWindow->pushGui(new GuiTextEditPopup(mWindow, _("NOME DO NOVO JOGADOR"), "", onName, false));
}

bool GuiCreditPlayerSelect::input(InputConfig* config, Input input)
{
	if (config->isMappedTo("y", input) && input.value != 0)
	{
		openSearch();
		return true;
	}

	if (config->isMappedTo(BUTTON_BACK, input) && input.value != 0)
	{
		if (mOnClosed)
			mOnClosed();
		delete this;
		return true;
	}

	if (mMenu.input(config, input))
		return true;

	return GuiComponent::input(config, input);
}

std::vector<HelpPrompt> GuiCreditPlayerSelect::getHelpPrompts()
{
	std::vector<HelpPrompt> prompts = mMenu.getHelpPrompts();
	prompts.push_back(HelpPrompt("y", _("BUSCAR")));
	prompts.push_back(HelpPrompt(BUTTON_BACK, _("VOLTAR")));
	return prompts;
}
