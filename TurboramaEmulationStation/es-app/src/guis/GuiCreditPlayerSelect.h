#pragma once

#include "GuiComponent.h"
#include "components/MenuComponent.h"
#include "CreditManager.h"
#include <functional>
#include <string>
#include <vector>

// Tela de locadora: lista rolavel de jogadores + busca por nome.
// Suporta dezenas/centenas de cadastros (scroll nativo do MenuComponent).
class GuiCreditPlayerSelect : public GuiComponent
{
public:
	// onSelected(name) called after successful switch; may be null
	// onClosed() called when user backs out without select; may be null
	GuiCreditPlayerSelect(Window* window,
		const std::function<void(const std::string& name)>& onSelected = nullptr,
		const std::function<void()>& onClosed = nullptr);

	bool input(InputConfig* config, Input input) override;
	std::vector<HelpPrompt> getHelpPrompts() override;

private:
	void rebuildList();
	void openSearch();
	void selectPlayer(const std::string& name);
	void registerNewPlayer();

	static bool nameMatchesFilter(const std::string& name, const std::string& filter);

	MenuComponent mMenu;
	std::string mFilter;
	std::function<void(const std::string&)> mOnSelected;
	std::function<void()> mOnClosed;
};
