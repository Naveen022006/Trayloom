// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.Capture;

/// <summary>English strings of the capture tools (kept in one place so localisation can be added later).</summary>
internal static class Strings
{
    public const string ScreenCapture = "Screen capture";
    public const string ShowMenuOnShortcut = "Show capture menu when using keyboard shortcut";
    public const string ShortcutRejected = "Windows rejected this shortcut. Choose another one.";
    public const string Reset = "Reset";
    public const string Cancel = "Cancel";
    public const string Done = "Done";
    public const string Close = "Close";
    public const string Remove = "Remove";
    public const string None = "None";
    public const string Off = "Off";
    public const string CaptureFailed = "The screen could not be captured";
    public const string MoreOptions = "More options";
    public const string SaveTo = "Save to";
    public const string Choose = "Choose…";
    public const string Desktop = "Desktop";

    public static string Seconds(int n) => n + " s";

    public static class Tools
    {
        public const string Screenshot = "Screenshot";
        public const string Recording = "Screen recording";
        public const string Text = "Copy text from screen";
        public const string Color = "Color picker";
    }

    public static class Screenshot
    {
        public const string PageTitle = "Screenshot";
        public const string FileNamePrefix = "Screenshot";
        public const string CaptureNow = "Capture now";
        public const string SaveLabel = "Save";
        public const string CopyLabel = "Copy";
        public const string EditLabel = "Edit";
        public const string SaveAs = "Save As…";
        public const string DragAndDrop = "Drag and drop";
        public const string ShadowsLabel = "Shadows";
        public const string BackgroundLabel = "Background";
        public const string WatermarkLabel = "Watermark";
        public const string ThicknessLabel = "Thickness";
        public const string FontSizeLabel = "Font size";
        public const string MarginLabel = "Margin";
        public const string CornersLabel = "Corners";
        public const string BlurLabel = "Blur";
        public const string HintDrag = "Drag to select an area";
        public const string HintClick = "Click a window to capture it";
        public const string ScrollingHint = "S toggles scrolling";
        public const string ScrollingOn = "Scrolling on";
        public const string ScrollingTitle = "Scrolling screenshot";
        public const string ScrollingCaption = "Choose an area, scroll with your mouse or trackpad, then press Enter or Done to join it.";
        public const string ScrollingSelectionHint = "Drag only the part of the page that moves.";
        public const string ScrollingProgress = "Scroll with your mouse or trackpad. Press Enter or choose Done.";
        public const string ScrollingLimit = "Capture stopped at the safe limit";
        public const string ScrollingPartial = "The page changed. The completed part was kept.";
        public const string Cancelled = "Cancelled.";
        public const string ScrollingCapture = "Scrolling capture";
        public const string PanelCaption = "Capture an area, window or the whole screen";
        public const string CaptureWholeScreen = "Capture the whole screen";
        public const string EditLatest = "Edit latest screenshot";
        public const string EditClipboard = "Edit clipboard image";
        public const string UploadLatest = "Upload latest screenshot";
        public const string FreezeToggle = "Freeze the screen while selecting";
        public const string FreezeCaption = "The picture stops while you choose the area, so nothing moves away or changes during the selection.";
        public const string HideWindows = "Hide WinKnife windows";
        public const string Delay = "Delay";
        public const string IncludePointer = "Include the pointer";
        public const string LastRegion = "Show the last capture outline";
        public const string HighlightWindows = "Highlight the window under the pointer";
        public const string HighlightCaption = "Turn off the blue window highlight while choosing an area. You can still click to capture a window.";
        public const string LoupeStartsOn = "Start selection with the magnifier on";
        public const string LoupeRemember = "Remember the magnifier’s last zoom";
        public const string LoupeDefaultZoom = "Default magnifier zoom";
        public const string LoupeWheel = "Wheel zoom";
        public const string LoupeFast = "Fast";
        public const string LoupeStepped = "Step by step";
        public const string LoupeCaption = "Hold Alt to temporarily use the other mode.";
        public const string PreviewPosition = "Preview position";
        public const string PreviewFocus = "Focus the preview automatically";
        public const string PreviewFocusCaption = "Shortcuts work the moment the preview appears, but the keyboard leaves the app you were using until it closes.";
        public const string DefaultAction = "Default action";
        public const string DefaultActionCaption = "Runs automatically right after capture.";
        public const string ConfirmationPreview = "Show confirmation preview";
        public const string ConfirmationDuration = "Confirmation duration";
        public const string ConfirmationCaption = "After a successful automatic action, keep the preview available for editing or discarding.";
        public const string UntilDismissed = "Until dismissed";
        public const string AutoCopy = "Copy to the clipboard automatically";
        public const string AutoCopyCaption = "Every capture goes to the clipboard as soon as it is taken, ready to paste. Saving a file stays a separate choice.";
        public const string CopyImageOnly = "Copy as image only";
        public const string CopyImageOnlyCaption = "Helps images paste into browsers and chat apps. Turn off for apps that need a file path.";
        public const string AddToShelf = "Add to the shelf automatically";
        public const string AddToShelfCaption = "Every capture also goes to the shelf, ready to drag into any app. One that opens in the editor goes there with Add to Shelf. Works while the shelf is on.";
        public const string Subfolder = "Subfolder pattern";
        public const string SubfolderCaption = "Optional. Creates dated subfolders inside the folder above using %y, %year, %mo, %month, %d, %h, %mi and %s, for example %y-%mo for 24-03.";
        public const string FileName = "File name";
        public const string FileNameCaption = "Optional. Overrides the default name using the same %y, %year, %mo, %month, %d, %h, %mi and %s tokens, plus %# for an auto-incrementing number (%## pads to 2 digits, %### to 3, and so on).";
        public const string StartsAt = "Starts at";
        public const string NextFormat = "Next: {0}";
        public const string Downscale = "Save at 1x size";
        public const string DownscaleCaption = "High-DPI captures are saved at their logical size, which makes smaller files.";
        public const string EditorTools = "Editor tools";
        public const string ToolShortcuts = "Use tool shortcuts";
        public const string ToolShortcutsCaption = "Click a shortcut to record a key. Digits 1–9 move the tool. Delete clears its shortcut.";
        public const string ToolReserved = "This key belongs to the editor.";
        public const string ToolRecording = "Press the new shortcut Escape cancels. Delete clears.";
        public const string MoveUp = "Move up";
        public const string MoveDown = "Move down";
        public const string RecentCaptures = "Recent captures";
        public const string RecentEmpty = "Take a screenshot or save a recording to find it here.";
        public const string ClearHistory = "Clear history";
        public const string RemoveFromHistory = "Remove from history";
        public const string Restore = "Restore";
        public const string Open = "Open";
        public const string Recording = "Recording";
        public const string TakeFirst = "Take a screenshot first";
        public const string CopyImageFirst = "Copy an image first";
        public const string Copied = "Screenshot copied";
        public const string SavedFormat = "Saved to {0}";
        public const string SavedAndCopiedFormat = "Saved to {0} and copied";
        public const string TextCopied = "Text copied";
        public const string QrCode = "QR code";
        public const string DiscardTitle = "Discard this screenshot?";
        public const string DiscardMessage = "It was not copied or saved yet.";
        public const string Discard = "Discard";

        public static readonly (string Value, string Label)[] DefaultActions =
        [
            ("", "Ask each time"), ("save", "Save"), ("saveAndCopy", "Save & Copy"), ("copy", "Copy"), ("edit", "Edit"),
        ];

        public static readonly (string Value, string Label)[] PreviewPositions =
        [
            ("", "Automatic"), ("topLeft", "Top left"), ("topRight", "Top right"), ("bottomLeft", "Bottom left"), ("bottomRight", "Bottom right"),
        ];
    }

    public static class Recorder
    {
        public const string RecordNow = "Record now";
        public const string Stop = "Stop recording";
        public const string PanelCaption = "Record an area, window or the whole screen";
        public const string Countdown = "Countdown";
        public const string SystemAudio = "Record the sound of the PC";
        public const string SystemAudioCaption = "Everything you hear goes into the recording, on its own track, so you can silence it later.";
        public const string Microphone = "Record the microphone";
        public const string MicrophoneCaption = "Your voice goes into its own track and stays adjustable in the editor.";
        public const string OpenEditor = "Open the editor after recording";
        public const string OpenEditorCaption = "The recording opens ready to trim, mute and save. Turn this off to get the file straight away.";
        public const string AutoZoom = "Add zooms automatically";
        public const string AutoZoomCaption = "Turn this off to start new recordings without zooms. You can still add them in the editor.";
        public const string Quality = "Quality";
        public const string QualityCaption = "Balanced fits most uses. High keeps every detail and makes bigger files.";
        public const string FrameRate = "Frames per second";
        public const string GifSize = "GIF size";
        public const string GifSmoothness = "GIF smoothness";
        public const string FileNamePrefix = "Recording";
        public const string SelectionPurpose = "Choose what to record";
        public const string RecordFailed = "The screen could not be recorded";
        public const string NoSpaceTitle = "Not enough space to record";
        public const string NoSpaceMessage = "Free up some space on the disk and try again.";
        public const string StoppedNoSpace = "Recording stopped, the disk is almost full";
        public const string MicUnavailable = "Microphone unavailable";
        public const string IndicatorTooltip = "Recording controls";
        public const string Pause = "Pause recording";
        public const string Resume = "Resume recording";
        public const string EditorTitle = "Recording";
        public const string Saving = "Saving…";
        public const string SavedFormat = "Saved to {0}";
        public const string Copied = "Recording copied";
        public const string ExportFailed = "The recording could not be saved";
        public const string DiscardTitle = "Delete this recording?";
        public const string DiscardMessage = "It has not been saved anywhere yet.";
        public const string DiscardSavedMessage = "Saved and copied files will stay where they are.";
    }

    public static class Ocr
    {
        public const string Caption = "Select an area of the screen and the recognized text is copied, ready to paste.";
        public const string Copied = "Text copied";
        public const string NoText = "No text found";
        public const string RemoveBreaks = "Remove line breaks";
        public const string RemoveBreaksCaption = "Removes line breaks so copied text pastes as one paragraph.";
        public const string ReadQr = "Read QR codes";
        public const string ReadQrCaption = "If the area has a QR code, its content is shown to copy or open.";
        public const string QrCopied = "QR code copied";
        public const string QrTitle = "QR code";
        public const string Copy = "Copy";
        public const string OpenLink = "Open link";
        public const string NoLanguage = "No OCR language pack is installed. Add one in Settings > Time & language > Language & region > Add a language > Optical character recognition.";
    }

    public static class Color
    {
        public const string Caption = "Grab the color of any pixel on screen and copy it in your favorite format.";
        public const string PickNow = "Pick color";
        public const string Format = "Copied format";
        public const string BareHex = "Copy without the # prefix";
    }
}
