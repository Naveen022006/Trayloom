// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>English strings of the capture tools (kept in one place so localisation can be added later).</summary>
internal static class Strings
{
    public static  string ScreenCapture => Loc.T("Screen capture");
    public static  string ShowMenuOnShortcut => Loc.T("Show capture menu when using keyboard shortcut");
    public static  string ShortcutRejected => Loc.T("Windows rejected this shortcut. Choose another one.");
    public const string Reset = "Reset";
    public const string Cancel = "Cancel";
    public const string Done = "Done";
    public const string Close = "Close";
    public const string Remove = "Remove";
    public const string None = "None";
    public const string Off = "Off";
    public static  string CaptureFailed => Loc.T("The screen could not be captured");
    public static  string MoreOptions => Loc.T("More options");
    public static  string SaveTo => Loc.T("Save to");
    public const string Choose = "Choose…";
    public const string Desktop = "Desktop";

    public static string Seconds(int n) => n + " s";

    public static class Tools
    {
        public static  string Screenshot => Loc.T("Screenshot");
        public static  string Recording => Loc.T("Screen recording");
        public static  string Text => Loc.T("Copy text from screen");
        public static  string Color => Loc.T("Color picker");
    }

    public static class Screenshot
    {
        public static  string PageTitle => Loc.T("Screenshot");
        public static  string FileNamePrefix => Loc.T("Screenshot");
        public static  string CaptureNow => Loc.T("Capture now");
        public static  string SaveLabel => Loc.T("Save");
        public static  string CopyLabel => Loc.T("Copy");
        public static  string EditLabel => Loc.T("Edit");
        public static  string SaveAs => Loc.T("Save As…");
        public static  string DragAndDrop => Loc.T("Drag and drop");
        public static  string ShadowsLabel => Loc.T("Shadows");
        public static  string BackgroundLabel => Loc.T("Background");
        public static  string WatermarkLabel => Loc.T("Watermark");
        public static  string ThicknessLabel => Loc.T("Thickness");
        public static  string FontSizeLabel => Loc.T("Font size");
        public static  string MarginLabel => Loc.T("Margin");
        public static  string CornersLabel => Loc.T("Corners");
        public static  string BlurLabel => Loc.T("Blur");
        public static  string HintDrag => Loc.T("Drag to select an area");
        public static  string HintClick => Loc.T("Click a window to capture it");
        public static  string ScrollingHint => Loc.T("S toggles scrolling");
        public static  string ScrollingOn => Loc.T("Scrolling on");
        public static  string ScrollingTitle => Loc.T("Scrolling screenshot");
        public static  string ScrollingCaption => Loc.T("Choose an area, scroll with your mouse or trackpad, then press Enter or Done to join it.");
        public static  string ScrollingSelectionHint => Loc.T("Drag only the part of the page that moves.");
        public static  string ScrollingProgress => Loc.T("Scroll with your mouse or trackpad. Press Enter or choose Done.");
        public static  string ScrollingLimit => Loc.T("Capture stopped at the safe limit");
        public static  string ScrollingPartial => Loc.T("The page changed. The completed part was kept.");
        public static  string Cancelled => Loc.T("Cancelled.");
        public static  string ScrollingCapture => Loc.T("Scrolling capture");
        public static  string PanelCaption => Loc.T("Capture an area, window or the whole screen");
        public static  string CaptureWholeScreen => Loc.T("Capture the whole screen");
        public static  string EditLatest => Loc.T("Edit latest screenshot");
        public static  string EditClipboard => Loc.T("Edit clipboard image");
        public static  string UploadLatest => Loc.T("Upload latest screenshot");
        public static  string FreezeToggle => Loc.T("Freeze the screen while selecting");
        public static  string FreezeCaption => Loc.T("The picture stops while you choose the area, so nothing moves away or changes during the selection.");
        public static  string HideWindows => Loc.T("Hide WinKnife windows");
        public static  string Delay => Loc.T("Delay");
        public static  string IncludePointer => Loc.T("Include the pointer");
        public static  string LastRegion => Loc.T("Show the last capture outline");
        public static  string HighlightWindows => Loc.T("Highlight the window under the pointer");
        public static  string HighlightCaption => Loc.T("Turn off the blue window highlight while choosing an area. You can still click to capture a window.");
        public static  string LoupeStartsOn => Loc.T("Start selection with the magnifier on");
        public static  string LoupeRemember => Loc.T("Remember the magnifier’s last zoom");
        public static  string LoupeDefaultZoom => Loc.T("Default magnifier zoom");
        public static  string LoupeWheel => Loc.T("Wheel zoom");
        public static  string LoupeFast => Loc.T("Fast");
        public static  string LoupeStepped => Loc.T("Step by step");
        public static  string LoupeCaption => Loc.T("Hold Alt to temporarily use the other mode.");
        public static  string PreviewPosition => Loc.T("Preview position");
        public static  string PreviewFocus => Loc.T("Focus the preview automatically");
        public static  string PreviewFocusCaption => Loc.T("Shortcuts work the moment the preview appears, but the keyboard leaves the app you were using until it closes.");
        public static  string DefaultAction => Loc.T("Default action");
        public static  string DefaultActionCaption => Loc.T("Runs automatically right after capture.");
        public static  string ConfirmationPreview => Loc.T("Show confirmation preview");
        public static  string ConfirmationDuration => Loc.T("Confirmation duration");
        public static  string ConfirmationCaption => Loc.T("After a successful automatic action, keep the preview available for editing or discarding.");
        public static  string UntilDismissed => Loc.T("Until dismissed");
        public static  string AutoCopy => Loc.T("Copy to the clipboard automatically");
        public static  string AutoCopyCaption => Loc.T("Every capture goes to the clipboard as soon as it is taken, ready to paste. Saving a file stays a separate choice.");
        public static  string CopyImageOnly => Loc.T("Copy as image only");
        public static  string CopyImageOnlyCaption => Loc.T("Helps images paste into browsers and chat apps. Turn off for apps that need a file path.");
        public static  string AddToShelf => Loc.T("Add to the shelf automatically");
        public static  string AddToShelfCaption => Loc.T("Every capture also goes to the shelf, ready to drag into any app. One that opens in the editor goes there with Add to Shelf. Works while the shelf is on.");
        public static  string Subfolder => Loc.T("Subfolder pattern");
        public static  string SubfolderCaption => Loc.T("Optional. Creates dated subfolders inside the folder above using %y, %year, %mo, %month, %d, %h, %mi and %s, for example %y-%mo for 24-03.");
        public static  string FileName => Loc.T("File name");
        public static  string FileNameCaption => Loc.T("Optional. Overrides the default name using the same %y, %year, %mo, %month, %d, %h, %mi and %s tokens, plus %# for an auto-incrementing number (%## pads to 2 digits, %### to 3, and so on).");
        public static  string StartsAt => Loc.T("Starts at");
        public static  string NextFormat => Loc.T("Next: {0}");
        public static  string Downscale => Loc.T("Save at 1x size");
        public static  string DownscaleCaption => Loc.T("High-DPI captures are saved at their logical size, which makes smaller files.");
        public static  string EditorTools => Loc.T("Editor tools");
        public static  string ToolShortcuts => Loc.T("Use tool shortcuts");
        public static  string ToolShortcutsCaption => Loc.T("Click a shortcut to record a key. Digits 1–9 move the tool. Delete clears its shortcut.");
        public static  string ToolReserved => Loc.T("This key belongs to the editor.");
        public static  string ToolRecording => Loc.T("Press the new shortcut Escape cancels. Delete clears.");
        public static  string MoveUp => Loc.T("Move up");
        public static  string MoveDown => Loc.T("Move down");
        public static  string RecentCaptures => Loc.T("Recent captures");
        public static  string RecentEmpty => Loc.T("Take a screenshot or save a recording to find it here.");
        public static  string ClearHistory => Loc.T("Clear history");
        public static  string RemoveFromHistory => Loc.T("Remove from history");
        public static  string Restore => Loc.T("Restore");
        public static  string Open => Loc.T("Open");
        public static  string Recording => Loc.T("Recording");
        public static  string TakeFirst => Loc.T("Take a screenshot first");
        public static  string CopyImageFirst => Loc.T("Copy an image first");
        public static  string Copied => Loc.T("Screenshot copied");
        public static  string SavedFormat => Loc.T("Saved to {0}");
        public static  string SavedAndCopiedFormat => Loc.T("Saved to {0} and copied");
        public static  string TextCopied => Loc.T("Text copied");
        public static  string QrCode => Loc.T("QR code");
        public static  string DiscardTitle => Loc.T("Discard this screenshot?");
        public static  string DiscardMessage => Loc.T("It was not copied or saved yet.");
        public static  string Discard => Loc.T("Discard");

        public static (string Value, string Label)[] DefaultActions => _loc_DefaultActions.Value;

    private static readonly LocValue<(string Value, string Label)[]> _loc_DefaultActions = new(() => [
            ("", Loc.T("Ask each time")), ("save", Loc.T("Save")), ("saveAndCopy", Loc.T("Save & Copy")), ("copy", Loc.T("Copy")), ("edit", Loc.T("Edit")),
        ]);

        public static (string Value, string Label)[] PreviewPositions => _loc_PreviewPositions.Value;

    private static readonly LocValue<(string Value, string Label)[]> _loc_PreviewPositions = new(() => [
            ("", Loc.T("Automatic")), ("topLeft", Loc.T("Top left")), ("topRight", Loc.T("Top right")), ("bottomLeft", Loc.T("Bottom left")), ("bottomRight", Loc.T("Bottom right")),
        ]);
    }

    public static class Recorder
    {
        public static  string RecordNow => Loc.T("Record now");
        public static  string Stop => Loc.T("Stop recording");
        public static  string PanelCaption => Loc.T("Record an area, window or the whole screen");
        public static  string Countdown => Loc.T("Countdown");
        public static  string SystemAudio => Loc.T("Record the sound of the PC");
        public static  string SystemAudioCaption => Loc.T("Everything you hear goes into the recording, on its own track, so you can silence it later.");
        public static  string Microphone => Loc.T("Record the microphone");
        public static  string MicrophoneCaption => Loc.T("Your voice goes into its own track and stays adjustable in the editor.");
        public static  string OpenEditor => Loc.T("Open the editor after recording");
        public static  string OpenEditorCaption => Loc.T("The recording opens ready to trim, mute and save. Turn this off to get the file straight away.");
        public static  string AutoZoom => Loc.T("Add zooms automatically");
        public static  string AutoZoomCaption => Loc.T("Turn this off to start new recordings without zooms. You can still add them in the editor.");
        public static  string Quality => Loc.T("Quality");
        public static  string QualityCaption => Loc.T("Balanced fits most uses. High keeps every detail and makes bigger files.");
        public static  string FrameRate => Loc.T("Frames per second");
        public static  string GifSize => Loc.T("GIF size");
        public static  string GifSmoothness => Loc.T("GIF smoothness");
        public static  string FileNamePrefix => Loc.T("Recording");
        public static  string SelectionPurpose => Loc.T("Choose what to record");
        public static  string RecordFailed => Loc.T("The screen could not be recorded");
        public static  string NoSpaceTitle => Loc.T("Not enough space to record");
        public static  string NoSpaceMessage => Loc.T("Free up some space on the disk and try again.");
        public static  string StoppedNoSpace => Loc.T("Recording stopped, the disk is almost full");
        public static  string MicUnavailable => Loc.T("Microphone unavailable");
        public static  string IndicatorTooltip => Loc.T("Recording controls");
        public static  string Pause => Loc.T("Pause recording");
        public static  string Resume => Loc.T("Resume recording");
        public static  string EditorTitle => Loc.T("Recording");
        public static  string Saving => Loc.T("Saving…");
        public static  string SavedFormat => Loc.T("Saved to {0}");
        public static  string Copied => Loc.T("Recording copied");
        public static  string ExportFailed => Loc.T("The recording could not be saved");
        public static  string DiscardTitle => Loc.T("Delete this recording?");
        public static  string DiscardMessage => Loc.T("It has not been saved anywhere yet.");
        public static  string DiscardSavedMessage => Loc.T("Saved and copied files will stay where they are.");
    }

    public static class Ocr
    {
        public static  string Caption => Loc.T("Select an area of the screen and the recognized text is copied, ready to paste.");
        public static  string Copied => Loc.T("Text copied");
        public static  string NoText => Loc.T("No text found");
        public static  string RemoveBreaks => Loc.T("Remove line breaks");
        public static  string RemoveBreaksCaption => Loc.T("Removes line breaks so copied text pastes as one paragraph.");
        public static  string ReadQr => Loc.T("Read QR codes");
        public static  string ReadQrCaption => Loc.T("If the area has a QR code, its content is shown to copy or open.");
        public static  string QrCopied => Loc.T("QR code copied");
        public static  string QrTitle => Loc.T("QR code");
        public static  string Copy => Loc.T("Copy");
        public static  string OpenLink => Loc.T("Open link");
        public static  string NoLanguage => Loc.T("No OCR language pack is installed. Add one in Settings > Time & language > Language & region > Add a language > Optical character recognition.");
    }

    public static class Color
    {
        public static  string Caption => Loc.T("Grab the color of any pixel on screen and copy it in your favorite format.");
        public static  string PickNow => Loc.T("Pick color");
        public static  string Format => Loc.T("Copied format");
        public static  string BareHex => Loc.T("Copy without the # prefix");
    }
}
