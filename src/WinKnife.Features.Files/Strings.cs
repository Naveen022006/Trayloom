// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.Files;

/// <summary>English strings of the Clipboard and files group (kept in one place so localisation can be added later).</summary>
internal static class Strings
{
    public static class Clipboard
    {
        public const string Title = "Clipboard";
        public const string Enable = "Save clipboard history";
        public const string Caption = "Stores copied text so you can reuse it later. Everything stays local and can be cleared anytime.";
        public const string LocalNote = "Everything stays on this PC. Very large items are ignored.";
        public const string Active = "Saving new text";
        public const string ShortcutSection = "History shortcut";
        public const string Shortcut = "History shortcut";
        public const string ShortcutCaption = "Opens a quick window with search, pinned items and Ctrl+1 to Ctrl+9 shortcuts for pasting into the previous app.";
        public const string HistoryLayout = "History layout";
        public const string LayoutList = "List";
        public const string LayoutCards = "Cards";
        public const string ShowSourceIcons = "Show source app icons in list view";
        public const string TrayPreview = "Show latest copy in the notification area";
        public const string TrayPreviewCaption = "Shows a shortened preview of your last copy next to the tray icons. Click it to open the history.";
        public const string TrayPreviewLength = "Preview length";
        public const string TrayPreviewSuffix = "characters";
        public const string IncludeImagesFiles = "Also save copied images and files";
        public const string IncludeImagesFilesCaption = "Images join the history and files are remembered as links to their location. Pin and paste them like any text item.";
        public const string SkipSensitive = "Skip text that looks sensitive";
        public const string SkipSensitiveCaption = "May skip ordinary text that resembles passwords or tokens. Content marked as confidential by its source app is always excluded.";
        public const string IgnoredTitle = "Apps to skip";
        public const string IgnoredAdd = "Add an app…";
        public const string IgnoredRemove = "Remove";
        public const string IgnoredCaption = "Nothing you copy in these apps is saved to the history.";
        public const string Limit = "Limit";
        public const string Unlimited = "Unlimited";
        public const string RetentionTitle = "Keep items for";
        public const string Forever = "Forever";
        public const string OneDay = "1 day";
        public const string DaysFormat = "{0} days";
        public const string RetentionCaption = "Older items are removed even below the limit. Pinned items are always kept.";
        public const string ShowInPanel = "Show in panel";
        public const string AutoClearEnable = "Auto clear clipboard with a delay of";
        public const string AutoClearSeconds = "seconds";
        public const string AutoClearOnSleep = "Clear clipboard when the PC sleeps";
        public const string AutoClearOnDisplaySleep = "Clear clipboard on display sleep";
        public const string AutoClearOnScreenLock = "Clear clipboard on screen lock";
        public const string AutoClearCaption = "Clears the system clipboard only. Items already saved stay in the history.";
        public const string PasteImageAsFile = "Paste copied images as files";
        public const string PasteImageAsFileCaption = "When File Explorer is active, Ctrl+V saves a copied image as a PNG in the current folder.";
        public const string Pinned = "Pinned";
        public const string Recent = "Recent";
        public const string ClearRecent = "Clear unpinned";
        public const string ClearRecentConfirmFormat = "Clear unpinned ({0})?";
        public const string ClearRecentConfirmMessage = "Pinned items stay, and so does anything copied after this. This can’t be undone.";
        public const string Cancel = "Cancel";
        public const string Disabled = "Enable history to start saving copied text.";
        public const string Search = "Search copied text";
        public const string Empty = "No saved text";
        public const string NoResults = "No results";
        public const string Pin = "Pin";
        public const string Unpin = "Unpin";
        public const string Copy = "Copy";
        public const string Copied = "Copied";
        public const string Paste = "Paste";
        public const string Delete = "Delete item";
        public const string SelectMultiple = "Add to pile";
        public const string UnselectMultiple = "Remove from pile";
        public const string PasteSelectedFormat = "Paste {0}";
        public const string CopySelectedFormat = "Copy {0}";
        public const string DeleteSelectedFormat = "Delete {0}";
        public const string ClearSelection = "Clear selection";
        public const string MoveUp = "Move up";
        public const string MoveDown = "Move down";
        public const string Edit = "Edit";
        public const string Save = "Save";
        public const string CopyText = "Copy text";
        public const string PreviewLabel = "Preview";
        public const string ImageLabel = "Image";
        public const string FileCountFormat = "{0} files";
        public const string Close = "Close";
        public const string AdminNote = "Run WinKnife as administrator to use this in elevated apps.";
        public const string RejectedShortcut = "Windows rejected this shortcut. Choose another one.";
        public const string PanelShortcutFormat = "History shortcut: {0}";
    }

    public static class PastePlain
    {
        public const string Name = "Paste as plain text";
        public const string Caption = "Pastes what you copied without colors, fonts or formatting. The original stays on the clipboard.";
    }

    public static class CutPaste
    {
        public const string Name = "Cut & paste";
        public const string Enable = "Cut & paste files in File Explorer";
        public const string EnableCaption = "Use Ctrl+X to cut and Ctrl+V to move files and folders in File Explorer, with a floating panel that shows what is cut.";
        public const string ShowHud = "Show floating panel";
        public const string ShowHudCaption = "Display a floating indicator with the cut files while File Explorer is active.";
        public const string ActiveNow = "Ready to cut in File Explorer";
        public const string HowTitle = "How to use";
        public const string Step1 = "Select items in File Explorer and press Ctrl+X to cut them.";
        public const string Step2 = "Open the destination folder and press Ctrl+V to move them there.";
        public const string TextNote = "In text fields (like when renaming), Ctrl+X and Ctrl+V keep working as usual.";
        public const string ReadyTitle = "Cut";
        public const string ReadyHint = "in the destination folder to move";
        public const string Cancel = "Cancel cut";
        public const string DoneTitle = "Moved!";
        public const string MovedSingular = "1 item moved";
        public const string MovedPluralFormat = "{0} items moved";
        public const string SomeFailed = "Some items couldn’t be moved";
        public const string MovingTitle = "Moving…";
        public const string MovingCountFormat = "{0} of {1}";
        public const string RenameTitle = "Rename shortcut";
        public const string RenameEnable = "Use a shortcut to rename";
        public const string RenameCaption = "The shortcut only acts in File Explorer and leaves text fields alone. F2 works as a regular key; on laptops where it is a media key, use Fn-F2 or choose another shortcut.";
        public const string RenameLabel = "Rename";
        public const string PageTitle = "Explorer shortcuts";
        public const string Reset = "Reset";
    }

    public static class Shelf
    {
        public const string Name = "Shelf";
        public const string Enable = "Temporary area for dragging files";
        public const string EnableCaption = "A floating spot to gather files, images and text, then drag them anywhere later.";
        public const string NoPermission = "Requires no permissions.";
        public const string HowTitle = "How to use";
        public const string Step1 = "Open it with the shortcut, or by shaking the mouse during a drag.";
        public const string Step2 = "Drop files, images, links or text onto it to hold them.";
        public const string Step3 = "Drag each item back out to any app when you need it.";
        public const string Destinations = "Where things open";
        public const string DestinationIsland = "Dynamic Island";
        public const string DestinationWindow = "Separate window";
        public const string ShortcutToggle = "Shelf shortcut";
        public const string HotkeyLabel = "Shortcut";
        public const string SelectionToggle = "Add the File Explorer selection with the shortcut";
        public const string SelectionCaption = "With File Explorer in front, the shortcut opens the shelf with the selected files already in it. With nothing selected, it opens as usual.";
        public const string ShakeToggle = "Open by shaking the mouse while dragging";
        public const string ShakeCaption = "Shake the pointer quickly while holding an item to summon it near the cursor.";
        public const string DropZoneToggle = "Keep dragged files near the taskbar";
        public const string DropZoneCaption = "While you drag a file, the shelf appears next to the tray icons. Whatever you drop is kept right there, in a button you shrink and open with a click that goes away once the shelf is empty.";
        public const string DropZoneCaptionTopCenter = "While you drag a file, the shelf appears as a badge at the top center of the screen. Whatever you drop is kept right there, in a badge you open with a click that goes away once the shelf is empty.";
        public const string DockPlacement = "Position";
        public const string DockTray = "Next to the tray icons";
        public const string DockTopCenter = "Top center of the screen";
        public const string DockIslandNote = "The Dynamic Island keeps the top center while it is on.";
        public const string EdgeToggle = "Open near a screen edge";
        public const string EdgeCaption = "Drag a file toward the screen edge to peek the shelf in. Drop it there, or pull back and it retreats.";
        public const string OpenNow = "Open now";
        public const string BehaviorTitle = "After use";
        public const string CloseAfterDrop = "Close after dropping into another app";
        public const string CloseAfterDropCaption = "Closes the shelf when the destination accepts the items. The pin in the panel keeps it open.";
        public const string RemoveAfterDrop = "Remove items after dropping";
        public const string RemoveAfterDropCaption = "Items accepted by another app leave the shelf. Turn this off to keep a copy there. Pinned items always stay.";
        public const string ClearOnClose = "Clear when closed";
        public const string ClearOnCloseCaption = "Empties the shelf only when you click its close button. Automatic hiding and collapsing keep the items.";
        public const string ExclusionsTitle = "Automatic exceptions";
        public const string ExclusionsEmpty = "No apps added.";
        public const string ExclusionsAdd = "Add app…";
        public const string ExclusionsCaption = "Shake and the tray drop zone stay off for drags started in these apps. The shortcut and Open now still work.";
        public const string Pin = "Keep open";
        public const string Unpin = "Allow closing after use";
        public const string Collapse = "Collapse";
        public const string Close = "Close";
        public const string Title = "Shelf";
        public const string Empty = "Drag items here";
        public const string ClearAll = "Clear all";
        public const string RemoveSelected = "Remove selected";
        public const string SelectedFormat = "{0} selected";
        public const string Hint = "Click to select. Drag out to use or right-click for more actions.";
        public const string ItemImage = "Image";
        public const string FileMissing = "The file no longer exists";
        public const string OpenAction = "Open";
        public const string OpenWith = "Open With";
        public const string Share = "Share";
        public const string PinAction = "Pin";
        public const string UnpinAction = "Unpin";
        public const string Remove = "Remove";
        public const string OpenShelf = "Open shelf";
        public const string OpenOpenFormat = "Open the shelf ({0})";
        public const string DropHere = "Drop here";
        public const string ShowInExplorer = "Show in Explorer";
        public const string ItemsFormat = "{0} items";
        public const string ImageSingular = "{0} image";
        public const string ImagePlural = "{0} images";
        public const string FileSingular = "{0} file";
        public const string FilePlural = "{0} files";
        public const string NoteSingular = "{0} note";
        public const string NotePlural = "{0} notes";
        public const string LinkSingular = "{0} link";
        public const string LinkPlural = "{0} links";
    }

    public static class CleanUrl
    {
        public const string Name = "Clean URL";
        public const string Enable = "Clean URLs as you copy them";
        public const string EnableCaption = "Removes tracking parameters from a link the moment it reaches the clipboard.";
        public const string LocalNote = "Local. No network.";
        public const string ActiveNow = "Active now";
        public const string RemovedFormat = "Removed {0}";
        public const string RulesTitle = "Cleaning rules";
        public const string AllSites = "All sites";
        public const string CountSingular = "1 parameter";
        public const string CountPluralFormat = "{0} parameters";
        public const string RemoveSiteButton = "Turn off every rule for this site";
        public const string RestoreSiteButton = "Turn on every rule for this site";
        public const string RemoveButton = "Delete name";
        public const string ParameterPlaceholder = "Parameter name";
        public const string AddButton = "Add";
        public const string MatchCaption = "Write the name to the left of the = , like utm_source. A name that matches takes that one parameter out of the link and leaves the rest as it was.";
        public const string AddSite = "Add a site";
        public const string SitePlaceholder = "example.com";
        public const string RulesCaption = "A site attaches these parameters to its own share links to track where the link came from. Switched on, a name is removed when a link is cleaned; switched off, it stays. Names you add can be deleted.";
        public const string CoverageCaption = "The list covers a site’s different share paths (the web page, the app, a live room), which is why it is long; a real link usually carries only two to four of them.";
        public const string ManualTitle = "Clean now";
        public const string InputPlaceholder = "Paste a URL";
        public const string ClearButton = "Clear field";
        public const string PasteButton = "Paste";
        public const string CopyButton = "Copy";
        public const string OutputPlaceholder = "The clean URL appears here";
        public const string NoUrl = "Paste a valid URL.";
        public const string NoChange = "Nothing to clean.";
        public const string Cleaned = "URL cleaned.";
        public const string Copied = "Copied.";
    }
}
