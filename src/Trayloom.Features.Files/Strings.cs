// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Files;

/// <summary>English strings of the Clipboard and files group (kept in one place so localisation can be added later).</summary>
internal static class Strings
{
    public static class Clipboard
    {
        public static  string Title => Loc.T("Clipboard");
        public static  string Enable => Loc.T("Save clipboard history");
        public static  string Caption => Loc.T("Stores copied text so you can reuse it later. Everything stays local and can be cleared anytime.");
        public static  string LocalNote => Loc.T("Everything stays on this PC. Very large items are ignored.");
        public static  string Active => Loc.T("Saving new text");
        public static  string ShortcutSection => Loc.T("History shortcut");
        public static  string Shortcut => Loc.T("History shortcut");
        public static  string ShortcutCaption => Loc.T("Opens a quick window with search, pinned items and Ctrl+1 to Ctrl+9 shortcuts for pasting into the previous app.");
        public static  string HistoryLayout => Loc.T("History layout");
        public static  string LayoutList => Loc.T("List");
        public static  string LayoutCards => Loc.T("Cards");
        public static  string ShowSourceIcons => Loc.T("Show source app icons in list view");
        public static  string TrayPreview => Loc.T("Show latest copy in the notification area");
        public static  string TrayPreviewCaption => Loc.T("Shows a shortened preview of your last copy next to the tray icons. Click it to open the history.");
        public static  string TrayPreviewLength => Loc.T("Preview length");
        public static  string TrayPreviewSuffix => Loc.T("characters");
        public static  string IncludeImagesFiles => Loc.T("Also save copied images and files");
        public static  string IncludeImagesFilesCaption => Loc.T("Images join the history and files are remembered as links to their location. Pin and paste them like any text item.");
        public static  string SkipSensitive => Loc.T("Skip text that looks sensitive");
        public static  string SkipSensitiveCaption => Loc.T("May skip ordinary text that resembles passwords or tokens. Content marked as confidential by its source app is always excluded.");
        public static  string IgnoredTitle => Loc.T("Apps to skip");
        public static  string IgnoredAdd => Loc.T("Add an app…");
        public static  string IgnoredRemove => Loc.T("Remove");
        public static  string IgnoredCaption => Loc.T("Nothing you copy in these apps is saved to the history.");
        public static  string Limit => Loc.T("Limit");
        public static  string Unlimited => Loc.T("Unlimited");
        public static  string RetentionTitle => Loc.T("Keep items for");
        public static  string Forever => Loc.T("Forever");
        public static  string OneDay => Loc.T("1 day");
        public static  string DaysFormat => Loc.T("{0} days");
        public static  string RetentionCaption => Loc.T("Older items are removed even below the limit. Pinned items are always kept.");
        public static  string ShowInPanel => Loc.T("Show in panel");
        public static  string AutoClearEnable => Loc.T("Auto clear clipboard with a delay of");
        public static  string AutoClearSeconds => Loc.T("seconds");
        public static  string AutoClearOnSleep => Loc.T("Clear clipboard when the PC sleeps");
        public static  string AutoClearOnDisplaySleep => Loc.T("Clear clipboard on display sleep");
        public static  string AutoClearOnScreenLock => Loc.T("Clear clipboard on screen lock");
        public static  string AutoClearCaption => Loc.T("Clears the system clipboard only. Items already saved stay in the history.");
        public static  string PasteImageAsFile => Loc.T("Paste copied images as files");
        public static  string PasteImageAsFileCaption => Loc.T("When File Explorer is active, Ctrl+V saves a copied image as a PNG in the current folder.");
        public static  string Pinned => Loc.T("Pinned");
        public static  string Recent => Loc.T("Recent");
        public static  string ClearRecent => Loc.T("Clear unpinned");
        public static  string ClearRecentConfirmFormat => Loc.T("Clear unpinned ({0})?");
        public static  string ClearRecentConfirmMessage => Loc.T("Pinned items stay, and so does anything copied after this. This can’t be undone.");
        public static  string Cancel => Loc.T("Cancel");
        public static  string Disabled => Loc.T("Enable history to start saving copied text.");
        public static  string Search => Loc.T("Search copied text");
        public static  string Empty => Loc.T("No saved text");
        public static  string NoResults => Loc.T("No results");
        public static  string Pin => Loc.T("Pin");
        public static  string Unpin => Loc.T("Unpin");
        public static  string Copy => Loc.T("Copy");
        public static  string Copied => Loc.T("Copied");
        public static  string Paste => Loc.T("Paste");
        public static  string Delete => Loc.T("Delete item");
        public static  string SelectMultiple => Loc.T("Add to pile");
        public static  string UnselectMultiple => Loc.T("Remove from pile");
        public static  string PasteSelectedFormat => Loc.T("Paste {0}");
        public static  string CopySelectedFormat => Loc.T("Copy {0}");
        public static  string DeleteSelectedFormat => Loc.T("Delete {0}");
        public static  string ClearSelection => Loc.T("Clear selection");
        public static  string MoveUp => Loc.T("Move up");
        public static  string MoveDown => Loc.T("Move down");
        public static  string Edit => Loc.T("Edit");
        public static  string Save => Loc.T("Save");
        public static  string CopyText => Loc.T("Copy text");
        public static  string PreviewLabel => Loc.T("Preview");
        public static  string ImageLabel => Loc.T("Image");
        public static  string FileCountFormat => Loc.T("{0} files");
        public static  string Close => Loc.T("Close");
        public static  string AdminNote => Loc.T("Run Trayloom as administrator to use this in elevated apps.");
        public static  string RejectedShortcut => Loc.T("Windows rejected this shortcut. Choose another one.");
        public static  string PanelShortcutFormat => Loc.T("History shortcut: {0}");
    }

    public static class PastePlain
    {
        public static  string Name => Loc.T("Paste as plain text");
        public static  string Caption => Loc.T("Pastes what you copied without colors, fonts or formatting. The original stays on the clipboard.");
    }

    public static class CutPaste
    {
        public static  string Name => Loc.T("Cut & paste");
        public static  string Enable => Loc.T("Cut & paste files in File Explorer");
        public static  string EnableCaption => Loc.T("Use Ctrl+X to cut and Ctrl+V to move files and folders in File Explorer, with a floating panel that shows what is cut.");
        public static  string ShowHud => Loc.T("Show floating panel");
        public static  string ShowHudCaption => Loc.T("Display a floating indicator with the cut files while File Explorer is active.");
        public static  string ActiveNow => Loc.T("Ready to cut in File Explorer");
        public static  string HowTitle => Loc.T("How to use");
        public static  string Step1 => Loc.T("Select items in File Explorer and press Ctrl+X to cut them.");
        public static  string Step2 => Loc.T("Open the destination folder and press Ctrl+V to move them there.");
        public static  string TextNote => Loc.T("In text fields (like when renaming), Ctrl+X and Ctrl+V keep working as usual.");
        public static  string ReadyTitle => Loc.T("Cut");
        public static  string ReadyHint => Loc.T("in the destination folder to move");
        public static  string Cancel => Loc.T("Cancel cut");
        public static  string DoneTitle => Loc.T("Moved!");
        public static  string MovedSingular => Loc.T("1 item moved");
        public static  string MovedPluralFormat => Loc.T("{0} items moved");
        public static  string SomeFailed => Loc.T("Some items couldn’t be moved");
        public static  string MovingTitle => Loc.T("Moving…");
        public static  string MovingCountFormat => Loc.T("{0} of {1}");
        public static  string RenameTitle => Loc.T("Rename shortcut");
        public static  string RenameEnable => Loc.T("Use a shortcut to rename");
        public static  string RenameCaption => Loc.T("The shortcut only acts in File Explorer and leaves text fields alone. F2 works as a regular key; on laptops where it is a media key, use Fn-F2 or choose another shortcut.");
        public static  string RenameLabel => Loc.T("Rename");
        public static  string PageTitle => Loc.T("Explorer shortcuts");
        public static  string Reset => Loc.T("Reset");
    }

    public static class Shelf
    {
        public static  string Name => Loc.T("Shelf");
        public static  string Enable => Loc.T("Temporary area for dragging files");
        public static  string EnableCaption => Loc.T("A floating spot to gather files, images and text, then drag them anywhere later.");
        public static  string NoPermission => Loc.T("Requires no permissions.");
        public static  string HowTitle => Loc.T("How to use");
        public static  string Step1 => Loc.T("Open it with the shortcut, or by shaking the mouse during a drag.");
        public static  string Step2 => Loc.T("Drop files, images, links or text onto it to hold them.");
        public static  string Step3 => Loc.T("Drag each item back out to any app when you need it.");
        public static  string Destinations => Loc.T("Where things open");
        public static  string DestinationIsland => Loc.T("Dynamic Island");
        public static  string DestinationWindow => Loc.T("Separate window");
        public static  string ShortcutToggle => Loc.T("Shelf shortcut");
        public static  string HotkeyLabel => Loc.T("Shortcut");
        public static  string SelectionToggle => Loc.T("Add the File Explorer selection with the shortcut");
        public static  string SelectionCaption => Loc.T("With File Explorer in front, the shortcut opens the shelf with the selected files already in it. With nothing selected, it opens as usual.");
        public static  string ShakeToggle => Loc.T("Open by shaking the mouse while dragging");
        public static  string ShakeCaption => Loc.T("Shake the pointer quickly while holding an item to summon it near the cursor.");
        public static  string DropZoneToggle => Loc.T("Keep dragged files near the taskbar");
        public static  string DropZoneCaption => Loc.T("While you drag a file, the shelf appears next to the tray icons. Whatever you drop is kept right there, in a button you shrink and open with a click that goes away once the shelf is empty.");
        public static  string DropZoneCaptionTopCenter => Loc.T("While you drag a file, the shelf appears as a badge at the top center of the screen. Whatever you drop is kept right there, in a badge you open with a click that goes away once the shelf is empty.");
        public static  string DockPlacement => Loc.T("Position");
        public static  string DockTray => Loc.T("Next to the tray icons");
        public static  string DockTopCenter => Loc.T("Top center of the screen");
        public static  string DockIslandNote => Loc.T("The Dynamic Island keeps the top center while it is on.");
        public static  string EdgeToggle => Loc.T("Open near a screen edge");
        public static  string EdgeCaption => Loc.T("Drag a file toward the screen edge to peek the shelf in. Drop it there, or pull back and it retreats.");
        public static  string OpenNow => Loc.T("Open now");
        public static  string BehaviorTitle => Loc.T("After use");
        public static  string CloseAfterDrop => Loc.T("Close after dropping into another app");
        public static  string CloseAfterDropCaption => Loc.T("Closes the shelf when the destination accepts the items. The pin in the panel keeps it open.");
        public static  string RemoveAfterDrop => Loc.T("Remove items after dropping");
        public static  string RemoveAfterDropCaption => Loc.T("Items accepted by another app leave the shelf. Turn this off to keep a copy there. Pinned items always stay.");
        public static  string ClearOnClose => Loc.T("Clear when closed");
        public static  string ClearOnCloseCaption => Loc.T("Empties the shelf only when you click its close button. Automatic hiding and collapsing keep the items.");
        public static  string ExclusionsTitle => Loc.T("Automatic exceptions");
        public static  string ExclusionsEmpty => Loc.T("No apps added.");
        public static  string ExclusionsAdd => Loc.T("Add app…");
        public static  string ExclusionsCaption => Loc.T("Shake and the tray drop zone stay off for drags started in these apps. The shortcut and Open now still work.");
        public static  string Pin => Loc.T("Keep open");
        public static  string Unpin => Loc.T("Allow closing after use");
        public static  string Collapse => Loc.T("Collapse");
        public static  string Close => Loc.T("Close");
        public static  string Title => Loc.T("Shelf");
        public static  string Empty => Loc.T("Drag items here");
        public static  string ClearAll => Loc.T("Clear all");
        public static  string RemoveSelected => Loc.T("Remove selected");
        public static  string SelectedFormat => Loc.T("{0} selected");
        public static  string Hint => Loc.T("Click to select. Drag out to use or right-click for more actions.");
        public static  string ItemImage => Loc.T("Image");
        public static  string FileMissing => Loc.T("The file no longer exists");
        public static  string OpenAction => Loc.T("Open");
        public static  string OpenWith => Loc.T("Open With");
        public static  string Share => Loc.T("Share");
        public static  string PinAction => Loc.T("Pin");
        public static  string UnpinAction => Loc.T("Unpin");
        public static  string Remove => Loc.T("Remove");
        public static  string OpenShelf => Loc.T("Open shelf");
        public static  string OpenOpenFormat => Loc.T("Open the shelf ({0})");
        public static  string DropHere => Loc.T("Drop here");
        public static  string ShowInExplorer => Loc.T("Show in Explorer");
        public static  string ItemsFormat => Loc.T("{0} items");
        public static  string ImageSingular => Loc.T("{0} image");
        public static  string ImagePlural => Loc.T("{0} images");
        public static  string FileSingular => Loc.T("{0} file");
        public static  string FilePlural => Loc.T("{0} files");
        public static  string NoteSingular => Loc.T("{0} note");
        public static  string NotePlural => Loc.T("{0} notes");
        public static  string LinkSingular => Loc.T("{0} link");
        public static  string LinkPlural => Loc.T("{0} links");
    }

    public static class CleanUrl
    {
        public static  string Name => Loc.T("Clean URL");
        public static  string Enable => Loc.T("Clean URLs as you copy them");
        public static  string EnableCaption => Loc.T("Removes tracking parameters from a link the moment it reaches the clipboard.");
        public static  string LocalNote => Loc.T("Local. No network.");
        public static  string ActiveNow => Loc.T("Active now");
        public static  string RemovedFormat => Loc.T("Removed {0}");
        public static  string RulesTitle => Loc.T("Cleaning rules");
        public static  string AllSites => Loc.T("All sites");
        public static  string CountSingular => Loc.T("1 parameter");
        public static  string CountPluralFormat => Loc.T("{0} parameters");
        public static  string RemoveSiteButton => Loc.T("Turn off every rule for this site");
        public static  string RestoreSiteButton => Loc.T("Turn on every rule for this site");
        public static  string RemoveButton => Loc.T("Delete name");
        public static  string ParameterPlaceholder => Loc.T("Parameter name");
        public static  string AddButton => Loc.T("Add");
        public static  string MatchCaption => Loc.T("Write the name to the left of the = , like utm_source. A name that matches takes that one parameter out of the link and leaves the rest as it was.");
        public static  string AddSite => Loc.T("Add a site");
        public const string SitePlaceholder = "example.com";
        public static  string RulesCaption => Loc.T("A site attaches these parameters to its own share links to track where the link came from. Switched on, a name is removed when a link is cleaned; switched off, it stays. Names you add can be deleted.");
        public static  string CoverageCaption => Loc.T("The list covers a site’s different share paths (the web page, the app, a live room), which is why it is long; a real link usually carries only two to four of them.");
        public static  string ManualTitle => Loc.T("Clean now");
        public static  string InputPlaceholder => Loc.T("Paste a URL");
        public static  string ClearButton => Loc.T("Clear field");
        public static  string PasteButton => Loc.T("Paste");
        public static  string CopyButton => Loc.T("Copy");
        public static  string OutputPlaceholder => Loc.T("The clean URL appears here");
        public static  string NoUrl => Loc.T("Paste a valid URL.");
        public static  string NoChange => Loc.T("Nothing to clean.");
        public static  string Cleaned => Loc.T("URL cleaned.");
        public static  string Copied => Loc.T("Copied.");
    }
}
