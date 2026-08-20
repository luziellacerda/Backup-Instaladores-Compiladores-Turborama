#include "views/gamelist/CarouselGameListView.h"

#include "utils/FileSystemUtil.h"
#include "views/UIModeController.h"
#include "views/ViewController.h"
#include "CollectionSystemManager.h"
#include "Settings.h"
#include "SystemData.h"
#include "SystemConf.h"
#include "FileData.h"
#include "LocaleES.h"
#include "GameNameFormatter.h"

CarouselGameListView::CarouselGameListView(Window* window, FolderData* root)
	: ISimpleGameListView(window, root),
	mList(window), mDetails(this, &mList, mWindow, DetailedContainer::DetailedView),
	mSelectedHeroPageOuterFar(window, false, true),
	mSelectedHeroPageOuterNear(window, false, true),
	mSelectedHeroPageFar(window, false, true),
	mSelectedHeroPageNear(window, false, true),
	mSelectedHeroCover(window, false, true),
	mSelectedHeroPageOuterFarEnabled(false),
	mSelectedHeroPageOuterNearEnabled(false),
	mSelectedHeroPageFarEnabled(false),
	mSelectedHeroPageNearEnabled(false),
	mSelectedHeroCoverEnabled(false)
{
	// Let DetailedContainer handle extras with activation scripts
	mExtraMode = ThemeData::ExtraImportType::WITHOUT_ACTIVATESTORYBOARD;

	mList.setSize(mSize.x(), mSize.y() * 0.8f);
	mList.setPosition(0, mSize.y() * 0.2f);
	mList.setDefaultZIndex(20);	
	mList.setCursorChangedCallback([&](const CursorState& /*state*/) { updateInfoPanel(); });

	updateInfoPanel();
		
	addChild(&mList);
	addChild(&mSelectedHeroPageOuterFar);
	addChild(&mSelectedHeroPageOuterNear);
	addChild(&mSelectedHeroPageFar);
	addChild(&mSelectedHeroPageNear);
	addChild(&mSelectedHeroCover);

	populateList(root->getChildrenListToDisplay());
}

void CarouselGameListView::onThemeChanged(const std::shared_ptr<ThemeData>& theme)
{
	ISimpleGameListView::onThemeChanged(theme);

	mList.applyTheme(theme, getName(), "gamecarousel", ThemeFlags::ALL);
	mDetails.onThemeChanged(theme);
	mSelectedHeroPageOuterFarEnabled = applySelectedHeroCoverTheme(theme, mSelectedHeroPageOuterFar, "selectedHeroPageOuterFar");
	mSelectedHeroPageOuterNearEnabled = applySelectedHeroCoverTheme(theme, mSelectedHeroPageOuterNear, "selectedHeroPageOuterNear");
	mSelectedHeroPageFarEnabled = applySelectedHeroCoverTheme(theme, mSelectedHeroPageFar, "selectedHeroPageFar");
	mSelectedHeroPageNearEnabled = applySelectedHeroCoverTheme(theme, mSelectedHeroPageNear, "selectedHeroPageNear");
	mSelectedHeroCoverEnabled = applySelectedHeroCoverTheme(theme, mSelectedHeroCover, "selectedHeroCover");

	sortChildren();
	updateInfoPanel();
}

void CarouselGameListView::updateInfoPanel()
{
	if (mRoot->getSystem()->isCollection())
		updateHelpPrompts();
	
	updateThemeExtrasBindings();

	FileData* file = (mList.size() == 0 || mList.isScrolling()) ? NULL : getCursor();
	bool isClearing = mList.getObjects().size() == 0 && mList.getCursorIndex() == 0 && mList.getScrollingVelocity() == 0;
	const int lastCursor = mList.getLastCursor();
	int moveBy = lastCursor >= 0 ? mList.getCursorIndex() - lastCursor : 0;
	const int itemCount = static_cast<int>(mList.getObjects().size());
	if (lastCursor >= 0 && itemCount > 1)
	{
		if (moveBy > itemCount / 2)
			moveBy -= itemCount;
		else if (moveBy < -(itemCount / 2))
			moveBy += itemCount;
	}
	updateSelectedHeroCovers(file, moveBy);
	mDetails.updateControls(file, isClearing, moveBy);
}

bool CarouselGameListView::applySelectedHeroCoverTheme(const std::shared_ptr<ThemeData>& theme, ImageComponent& image, const std::string& element)
{
	image.setImage("");
	image.setVisible(false);

	const ThemeData::ThemeElement* themeElement = theme->getElement(getName(), element, "image");
	if (themeElement == nullptr || themeElement->extra != 0)
		return false;

	image.applyTheme(theme, getName(), element, ThemeFlags::ALL ^ ThemeFlags::PATH ^ ThemeFlags::VISIBLE);
	return true;
}

std::string CarouselGameListView::getSelectedHeroCoverPath(FileData* file) const
{
	if (file == nullptr || file->isPlaceHolder())
		return "";

	const std::string directory = Utils::FileSystem::getParent(file->getPath());
	const std::string stem = Utils::FileSystem::getStem(file->getPath());
	const std::string revistaDirectory = Utils::FileSystem::combine(file->getSystem()->getStartPath(), "media/revista");
	const std::string mediaDirectories[] = {
		Utils::FileSystem::combine(revistaDirectory, Utils::FileSystem::getFileName(directory)),
		revistaDirectory
	};
	const std::string extensions[] = { ".png", ".jpg", ".jpeg" };

	for (const auto& mediaDirectory : mediaDirectories)
	{
		for (const auto& extension : extensions)
		{
			const std::string candidate = Utils::FileSystem::combine(mediaDirectory, stem + extension);
			if (Utils::FileSystem::exists(candidate))
				return candidate;
		}
	}

	return "";
}

FileData* CarouselGameListView::getNeighbourGame(int offset)
{
	const std::vector<IBindable*> objects = mList.getObjects();
	if (objects.empty() || offset == 0)
		return nullptr;

	const int size = static_cast<int>(objects.size());
	const int direction = offset < 0 ? -1 : 1;
	int remaining = offset < 0 ? -offset : offset;
	int index = mList.getCursorIndex();

	for (int attempts = 0; attempts < size * remaining; ++attempts)
	{
		index = (index + direction + size) % size;
		FileData* candidate = dynamic_cast<FileData*>(objects[index]);
		if (candidate == nullptr || candidate->isPlaceHolder())
			continue;

		if (--remaining == 0)
			return candidate;
	}

	return nullptr;
}

void CarouselGameListView::playSelectedHeroCoverStoryboard(ImageComponent& image, int moveBy)
{
	if (moveBy == 0 || !image.isVisible())
		return;

	const std::string event = moveBy > 0 ? "activateNext" : "activatePrev";
	if (!image.storyBoardExists(event))
		return;

	image.deselectStoryboard(true);
	image.selectStoryboard(event);
	if (image.isShowing())
		image.startStoryboard();
}

void CarouselGameListView::updateSelectedHeroCovers(FileData* selected, int moveBy)
{
	if (selected == nullptr || selected->isPlaceHolder() ||
		(!mSelectedHeroPageOuterFarEnabled && !mSelectedHeroPageOuterNearEnabled &&
		 !mSelectedHeroPageFarEnabled && !mSelectedHeroPageNearEnabled && !mSelectedHeroCoverEnabled))
	{
		mSelectedHeroPageOuterFar.setImage("");
		mSelectedHeroPageOuterNear.setImage("");
		mSelectedHeroPageFar.setImage("");
		mSelectedHeroPageNear.setImage("");
		mSelectedHeroCover.setImage("");
		mSelectedHeroPageOuterFar.setVisible(false);
		mSelectedHeroPageOuterNear.setVisible(false);
		mSelectedHeroPageFar.setVisible(false);
		mSelectedHeroPageNear.setVisible(false);
		mSelectedHeroCover.setVisible(false);
		return;
	}

	std::vector<FileData*> usedGames;
	usedGames.push_back(selected);
	auto takeUniqueGame = [&usedGames](FileData* candidate) -> FileData*
	{
		if (candidate == nullptr)
			return nullptr;

		for (FileData* used : usedGames)
		{
			if (used == candidate)
				return nullptr;
		}

		usedGames.push_back(candidate);
		return candidate;
	};

	FileData* previous = takeUniqueGame(getNeighbourGame(-1));
	FileData* next = takeUniqueGame(getNeighbourGame(1));
	FileData* previousFar = takeUniqueGame(getNeighbourGame(-2));
	FileData* nextFar = takeUniqueGame(getNeighbourGame(2));

	const std::string previousFarPath = getSelectedHeroCoverPath(previousFar);
	const std::string previousPath = getSelectedHeroCoverPath(previous);
	const std::string currentPath = getSelectedHeroCoverPath(selected);
	const std::string nextPath = getSelectedHeroCoverPath(next);
	const std::string nextFarPath = getSelectedHeroCoverPath(nextFar);

	mSelectedHeroPageOuterFar.setImage(mSelectedHeroPageOuterFarEnabled ? previousFarPath : "");
	mSelectedHeroPageOuterNear.setImage(mSelectedHeroPageOuterNearEnabled ? nextFarPath : "");
	mSelectedHeroPageFar.setImage(mSelectedHeroPageFarEnabled ? previousPath : "");
	mSelectedHeroPageNear.setImage(mSelectedHeroPageNearEnabled ? nextPath : "");
	mSelectedHeroCover.setImage(mSelectedHeroCoverEnabled ? currentPath : "");
	mSelectedHeroPageOuterFar.setVisible(mSelectedHeroPageOuterFarEnabled && !previousFarPath.empty());
	mSelectedHeroPageOuterNear.setVisible(mSelectedHeroPageOuterNearEnabled && !nextFarPath.empty());
	mSelectedHeroPageFar.setVisible(mSelectedHeroPageFarEnabled && !previousPath.empty());
	mSelectedHeroPageNear.setVisible(mSelectedHeroPageNearEnabled && !nextPath.empty());
	mSelectedHeroCover.setVisible(mSelectedHeroCoverEnabled && !currentPath.empty());

	playSelectedHeroCoverStoryboard(mSelectedHeroPageOuterFar, moveBy);
	playSelectedHeroCoverStoryboard(mSelectedHeroPageOuterNear, moveBy);
	playSelectedHeroCoverStoryboard(mSelectedHeroPageFar, moveBy);
	playSelectedHeroCoverStoryboard(mSelectedHeroPageNear, moveBy);
	playSelectedHeroCoverStoryboard(mSelectedHeroCover, moveBy);
}

void CarouselGameListView::onFileChanged(FileData* file, FileChangeType change)
{
	if(change == FILE_METADATA_CHANGED)
	{
		// might switch to a detailed view
		ViewController::get()->reloadGameListView(this);
		return;
	}

	ISimpleGameListView::onFileChanged(file, change);
}

void CarouselGameListView::populateList(const std::vector<FileData*>& files)
{
	updateHeaderLogoAndText();

	mList.clear();

	if (files.size() > 0)
	{
		bool showParentFolder = mRoot->getSystem()->getShowParentFolder();
		if (showParentFolder && mCursorStack.size())
			mList.add(". .", createParentFolderData());

		GameNameFormatter formatter(mRoot->getSystem());

		for (auto file : files)		
			mList.add(formatter.getDisplayName(file), file);

		// if we have the ".." PLACEHOLDER, then select the first game instead of the placeholder
		if (showParentFolder && mCursorStack.size() && mList.size() > 1 && mList.getCursorIndex() == 0)
			mList.setCursorIndex(1);
	}
	else
	{
		addPlaceholder();
	}

	updateFolderPath();

	if (mShowing)
		onShow();
}

void CarouselGameListView::onShow()
{
	ISimpleGameListView::onShow();
	updateInfoPanel();
}

FileData* CarouselGameListView::getCursor()
{
	if (mList.size() == 0)
		return nullptr;

	return dynamic_cast<FileData*>(mList.getSelected());	
}

void CarouselGameListView::resetLastCursor()
{
	mList.resetLastCursor();
}

void CarouselGameListView::setCursor(FileData* cursor)
{
	if (cursor && !mList.setCursor(cursor) && !cursor->isPlaceHolder())
	{
		std::stack<FileData*> stack;
		auto childrenToDisplay = mRoot->findChildrenListToDisplayAtCursor(cursor, stack);
		if (childrenToDisplay != nullptr)
		{
			mCursorStack = stack;
			populateList(*childrenToDisplay.get());
			mList.setCursor(cursor);
		}
	}
}

void CarouselGameListView::addPlaceholder()
{
	// empty list - add a placeholder
	FileData* placeholder = createNoEntriesPlaceholder();
	mList.add(placeholder->getName(), placeholder);
}

std::string CarouselGameListView::getQuickSystemSelectRightButton()
{
	if (mList.isHorizontalCarousel())
		return "r2";

	return "right";
}

std::string CarouselGameListView::getQuickSystemSelectLeftButton()
{
	if (mList.isHorizontalCarousel())
		return "l2";

	return "left";
}

void CarouselGameListView::launch(FileData* game)
{
	ViewController::get()->launch(game);
}

void CarouselGameListView::remove(FileData *game)
{
	mList.remove(game);
	mRoot->removeFromVirtualFolders(game);
	delete game;

	if (mList.size() == 0)
		addPlaceholder();

	ViewController::get()->reloadGameListView(this);
}

void CarouselGameListView::setCursorIndex(int cursor)
{
	mList.setCursorIndex(cursor);
}

int CarouselGameListView::getCursorIndex()
{
	return mList.getCursorIndex();
}

std::vector<FileData*> CarouselGameListView::getFileDataEntries()
{
	std::vector<FileData*> ret;

	for (auto item : mList.getObjects())
	{
		FileData* data = dynamic_cast<FileData*>(item);
		if (data != nullptr)
			ret.push_back(data);
	}

	return ret;
}

void CarouselGameListView::update(int deltaTime)
{
	mDetails.update(deltaTime);
	ISimpleGameListView::update(deltaTime);
}

bool CarouselGameListView::onMouseWheel(int delta)
{
	return mList.onMouseWheel(delta);
}
