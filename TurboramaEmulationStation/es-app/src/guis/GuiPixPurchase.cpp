#include "guis/GuiPixPurchase.h"

#include "LocaleES.h"
#include "Window.h"
#include "guis/GuiMsgBox.h"
#include "renderers/Renderer.h"
#include "resources/Font.h"

#include <algorithm>
#include <iomanip>
#include <sstream>

GuiPixPurchase::GuiPixPurchase(Window* window)
	: GuiComponent(window)
	, mMenu(window, _("COMPRAR TEMPO COM PIX"))
	, mPanel(window, ":/frame.png", 0x16A8FFFF, 0x101724F8)
	, mQrImage(window, true)
	, mTitle(window, _("PAGAMENTO PIX"), Font::get(FONT_SIZE_LARGE), 0xFFFFFFFF, ALIGN_CENTER)
	, mStatus(window, _("GERANDO QR PIX..."), Font::get(FONT_SIZE_MEDIUM), 0xF4C95DFF, ALIGN_CENTER)
	, mPackageText(window, "", Font::get(FONT_SIZE_MEDIUM), 0xFFFFFFFF, ALIGN_CENTER)
	, mInstruction(window, _("Abra o aplicativo do seu banco e leia o QR Code.\nO tempo sera liberado automaticamente apos a confirmacao."), Font::get(FONT_SIZE_SMALL), 0xDDE7F0FF, ALIGN_CENTER)
{
	setSize((float)Renderer::getScreenWidth(), (float)Renderer::getScreenHeight());
	addChild(&mMenu); addChild(&mPanel); addChild(&mTitle); addChild(&mStatus);
	addChild(&mPackageText); addChild(&mQrImage); addChild(&mInstruction);
	mPanel.setVisible(false); mTitle.setVisible(false); mStatus.setVisible(false);
	mPackageText.setVisible(false); mQrImage.setVisible(false); mInstruction.setVisible(false);
	buildPackageMenu();
}

std::string GuiPixPurchase::formatPrice(long long cents) const
{
	std::ostringstream out;
	out << "R$ " << (cents / 100) << ',' << std::setw(2) << std::setfill('0') << (cents % 100);
	return out.str();
}

void GuiPixPurchase::buildPackageMenu()
{
	mMenu.clear();
	std::string error;
	if (!PixBridge::loadPublicOptions(mOptions, error))
	{
		mMenu.addGroup(_("SERVICO INDISPONIVEL"));
		mMenu.addEntry(error, false);
		mMenu.addEntry(_("TENTAR NOVAMENTE"), true, [this] { buildPackageMenu(); });
	}
	else
	{
		mMenu.addGroup(mOptions.provider == "mock" ? _("MODO DE TESTE - NAO REALIZA COBRANCA") : _("PAGAMENTO SEGURO VIA MERCADO PAGO"));
		for (const auto& package : mOptions.packages)
		{
			const std::string label = std::to_string(package.minutes) + _(" MINUTOS  -  ") + formatPrice(package.amountCents);
			mMenu.addEntry(label, true, [this, package] { confirmPackage(package); });
		}
		mMenu.addGroup(_("COMO FUNCIONA"));
		mMenu.addEntry(_("Escolha o tempo, leia o QR e aguarde a confirmacao"), false);
	}
	if (!mBackButtonAdded)
	{
		mMenu.addButton(_("VOLTAR"), "back", [this] { delete this; });
		mBackButtonAdded = true;
	}
	mMenu.updateSize();
	mMenu.setPosition((Renderer::getScreenWidth() - mMenu.getSize().x()) / 2, (Renderer::getScreenHeight() - mMenu.getSize().y()) / 2);
}

void GuiPixPurchase::confirmPackage(const PixPackage& package)
{
	const std::string text = _("CONFIRMAR COMPRA?\n\n") + std::to_string(package.minutes) + _(" minutos por ")
		+ formatPrice(package.amountCents) + _("\n\nO tempo sera adicionado somente apos o pagamento confirmado.");
	mWindow->pushGui(new GuiMsgBox(mWindow, text, _("GERAR PIX"), [this, package] { startPurchase(package); }, _("CANCELAR"), nullptr, ICON_QUESTION));
}

void GuiPixPurchase::startPurchase(const PixPackage& package)
{
	std::string error, requestId;
	if (!PixBridge::createPurchaseRequest(package, requestId, error))
	{
		mWindow->pushGui(new GuiMsgBox(mWindow, error, _("OK"), nullptr, ICON_ERROR));
		return;
	}
	mSelectedPackage = package; mRequestId = requestId; mElapsedMs = 0; mPollElapsedMs = 1000; mWaiting = true;
	showWaitingLayout();
}

void GuiPixPurchase::showWaitingLayout()
{
	mMenu.setVisible(false);
	const float width = (float)Renderer::getScreenWidth(), height = (float)Renderer::getScreenHeight();
	mPanel.setPosition(width * 0.17f, height * 0.055f); mPanel.setSize(width * 0.66f, height * 0.86f);
	mPanel.setCornerSize(height * 0.025f, height * 0.025f); mPanel.setVisible(true);
	mTitle.setPosition(width * 0.20f, height * 0.09f); mTitle.setSize(width * 0.60f, height * 0.10f); mTitle.setVisible(true);
	mStatus.setPosition(width * 0.20f, height * 0.19f); mStatus.setSize(width * 0.60f, height * 0.07f); mStatus.setVisible(true);
	mPackageText.setText(std::to_string(mSelectedPackage.minutes) + _(" MINUTOS  |  ") + formatPrice(mSelectedPackage.amountCents));
	mPackageText.setPosition(width * 0.20f, height * 0.255f); mPackageText.setSize(width * 0.60f, height * 0.065f); mPackageText.setVisible(true);
	mQrImage.setPosition(width * 0.365f, height * 0.325f); mQrImage.setMaxSize(width * 0.27f, height * 0.43f);
	mInstruction.setPosition(width * 0.22f, height * 0.765f); mInstruction.setSize(width * 0.56f, height * 0.105f); mInstruction.setVisible(true);
	updateHelpPrompts();
}

void GuiPixPurchase::pollPurchase()
{
	const PixPurchaseInfo info = PixBridge::getPurchaseInfo(mRequestId);
	if (!info.qrImagePath.empty() && info.qrImagePath != mLoadedQrPath)
	{
		mLoadedQrPath = info.qrImagePath; mQrImage.setImage(mLoadedQrPath); mQrImage.setVisible(true);
	}
	const int remainingSeconds = std::max(0, mOptions.paymentExpirationMinutes * 60 - mElapsedMs / 1000);
	const std::string clock = std::to_string(remainingSeconds / 60) + ':' + (remainingSeconds % 60 < 10 ? "0" : "") + std::to_string(remainingSeconds % 60);
	switch (info.state)
	{
	case PixPurchaseState::Generating: mStatus.setText(_("GERANDO QR PIX...  ") + clock); break;
	case PixPurchaseState::Pending: mStatus.setText(_("AGUARDANDO PAGAMENTO  |  EXPIRA EM ") + clock); break;
	case PixPurchaseState::Approved:
		for (const auto& message : PixBridge::processApprovedCredits()) mWindow->displayNotificationMessage(message, 7);
		if (PixBridge::getPurchaseInfo(mRequestId).state == PixPurchaseState::Completed)
			finishWithMessage(_("PAGAMENTO CONFIRMADO!\n\nO tempo foi adicionado e ja esta disponivel para jogar."), true);
		else
			mStatus.setText(_("PAGAMENTO CONFIRMADO  |  FINALIZANDO CREDITO..."));
		break;
	case PixPurchaseState::Completed:
		finishWithMessage(_("PAGAMENTO CONFIRMADO!\n\nO tempo foi adicionado e ja esta disponivel para jogar."), true); break;
	case PixPurchaseState::Cancelled: finishWithMessage(_("Este QR PIX expirou ou foi cancelado. Nenhum tempo foi cobrado."), false); break;
	case PixPurchaseState::SecurityError:
	case PixPurchaseState::Rejected: finishWithMessage(_("O pedido PIX nao pode ser validado. Nenhum tempo foi liberado."), false); break;
	case PixPurchaseState::Unknown: mStatus.setText(mElapsedMs < 15000 ? _("ENVIANDO PEDIDO AO SERVICO PIX...") : _("SERVICO PIX DEMORANDO. AGUARDE OU VOLTE.")); break;
	}
}

void GuiPixPurchase::finishWithMessage(const std::string& message, bool success)
{
	if (mFinishing) return;
	mFinishing = true; Window* window = mWindow; delete this;
	window->pushGui(new GuiMsgBox(window, message, _("OK"), nullptr, success ? ICON_INFORMATION : ICON_ERROR));
}

void GuiPixPurchase::closePurchase()
{
	if (!mWaiting) { delete this; return; }
	mWindow->pushGui(new GuiMsgBox(mWindow, _("SAIR DESTA TELA?\n\nSe o PIX for pago, o tempo ainda sera liberado automaticamente."),
		_("SAIR"), [this] { delete this; }, _("CONTINUAR AQUI"), nullptr, ICON_QUESTION));
}

bool GuiPixPurchase::input(InputConfig* config, Input input)
{
	if (config->isMappedTo(BUTTON_BACK, input) && input.value != 0) { closePurchase(); return true; }
	if (!mWaiting && mMenu.input(config, input)) return true;
	return GuiComponent::input(config, input);
}

void GuiPixPurchase::update(int deltaTime)
{
	GuiComponent::update(deltaTime);
	if (!mWaiting || mFinishing) return;
	mElapsedMs += std::max(0, deltaTime); mPollElapsedMs += std::max(0, deltaTime);
	if (mPollElapsedMs >= 1000) { mPollElapsedMs = 0; pollPurchase(); }
}

void GuiPixPurchase::render(const Transform4x4f& parentTrans)
{
	Transform4x4f trans = parentTrans * getTransform();
	if (mWaiting) { Renderer::setMatrix(trans); Renderer::drawRect(0.f, 0.f, mSize.x(), mSize.y(), 0x020711E8); }
	renderChildren(trans);
}

std::vector<HelpPrompt> GuiPixPurchase::getHelpPrompts()
{
	if (mWaiting) return { HelpPrompt(BUTTON_BACK, _("VOLTAR")) };
	return mMenu.getHelpPrompts();
}
