#pragma once

#include "GuiComponent.h"
#include "PixAgentManager.h"
#include "components/MenuComponent.h"

#include <functional>
#include <string>

class GuiPixOwnerSettings : public GuiComponent
{
public:
	explicit GuiPixOwnerSettings(Window* window);
	bool input(InputConfig* config, Input input) override;
	std::vector<HelpPrompt> getHelpPrompts() override;

private:
	void rebuild();
	void centerOnScreen();
	void editText(const std::string& title, const std::string& current, bool password,
		const std::function<void(const std::string&)>& callback);
	void editPrice(int minutes);
	void launchOwnerConfigurator();
	void saveAndActivate();
	std::string formatPrice(long long cents) const;
	bool parsePrice(const std::string& value, long long& cents) const;

	MenuComponent mMenu;
	PixOwnerSettings mDraft;
};
