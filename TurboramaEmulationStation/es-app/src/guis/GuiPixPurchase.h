#pragma once

#include "GuiComponent.h"
#include "PixBridge.h"
#include "components/ImageComponent.h"
#include "components/MenuComponent.h"
#include "components/NinePatchComponent.h"
#include "components/TextComponent.h"

class GuiPixPurchase : public GuiComponent
{
public:
	explicit GuiPixPurchase(Window* window);
	bool input(InputConfig* config, Input input) override;
	void update(int deltaTime) override;
	void render(const Transform4x4f& parentTrans) override;
	std::vector<HelpPrompt> getHelpPrompts() override;

private:
	void buildPackageMenu();
	void confirmPackage(const PixPackage& package);
	void startPurchase(const PixPackage& package);
	void showWaitingLayout();
	void pollPurchase();
	void renderQrMatrix(const Transform4x4f& transform);
	void finishWithMessage(const std::string& message, bool success);
	void closePurchase();
	std::string formatPrice(long long cents) const;

	MenuComponent mMenu;
	NinePatchComponent mPanel;
	ImageComponent mQrImage;
	TextComponent mTitle;
	TextComponent mStatus;
	TextComponent mPackageText;
	TextComponent mInstruction;
	PixPublicOptions mOptions;
	PixPackage mSelectedPackage;
	std::string mRequestId;
	std::string mLoadedQrPath;
	std::vector<unsigned char> mQrModules;
	Vector2f mQrAreaPosition = Vector2f::Zero();
	float mQrAreaSize = 0;
	int mQrModuleCount = 0;
	int mPollElapsedMs = 0;
	int mElapsedMs = 0;
	bool mWaiting = false;
	bool mFinishing = false;
	bool mBackButtonAdded = false;
};
