#pragma once

#include "GuiComponent.h"
#include "components/MenuComponent.h"
#include <string>

// Painel LOCADORA estavel (MenuComponent — sem render custom / sem recursao setSize).
class GuiCreditOperatorPanel : public GuiComponent
{
public:
	GuiCreditOperatorPanel(Window* window);

	bool input(InputConfig* config, Input input) override;
	std::vector<HelpPrompt> getHelpPrompts() override;

private:
	void rebuild();
	void refreshHeader();
	void centerOnScreen();

	void doAddCoin();
	void doAddMinutes(int mins);
	void doAskMinutes();
	void doResume();
	void doPause();
	void doStop();
	void doEndPlayer();
	void doCloseGuest();
	void doChoosePlayer();
	void doRegisterPlayer();
	void doRemovePlayer();
	void doOpenTurboSistema();
	void doSwitchUser();

	MenuComponent mMenu;
};
