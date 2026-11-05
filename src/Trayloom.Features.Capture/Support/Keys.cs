// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Capture;

/// <summary>Flat settings keys (Vorssaint names) used by the capture tools, with their registered defaults.</summary>
internal static class K
{
    // Shortcuts
    public const string ScreenshotShortcutEnabled = "screenshotShortcutEnabled";
    public const string RecorderShortcutEnabled = "recorderShortcutEnabled";
    public const string OcrShortcutEnabled = "screenOCRShortcutEnabled";
    public const string ColorShortcutEnabled = "colorPickerShortcutEnabled";
    public const string RecentShortcutEnabled = "recentCapturesShortcutEnabled";
    public const string FullScreenShortcutEnabled = "screenshotFullScreenShortcutEnabled";
    public const string LastCaptureShortcutEnabled = "screenshotLastCaptureShortcutEnabled";
    public const string ClipboardShortcutEnabled = "screenshotClipboardShortcutEnabled";
    public const string UploadShortcutEnabled = "screenshotUploadShortcutEnabled";
    public const string ShowMenuScreenshot = "screenshotShowCaptureMenuOnShortcut";
    public const string ShowMenuRecorder = "recorderShowCaptureMenuOnShortcut";
    public const string ShowMenuOcr = "screenOCRShowCaptureMenuOnShortcut";
    public const string ShowMenuColor = "colorPickerShowCaptureMenuOnShortcut";

    // Screenshot behaviour
    public const string Freeze = "screenshotFreeze";
    public const string HideOwn = "screenshotHideVorssaintWindows";
    public const string Delay = "screenshotDelay";
    public const string IncludePointer = "screenshotIncludePointer";
    public const string ShowLastRegion = "screenshotShowLastRegion";
    public const string HighlightWindows = "screenshotHighlightWindows";
    public const string LoupeStartsOn = "screenshotLoupeStartsOn";
    public const string LoupeRememberZoom = "screenshotLoupeRememberZoom";
    public const string LoupeLastZoom = "screenshotLoupeLastZoom";
    public const string LoupeDefaultZoom = "screenshotLoupeDefaultZoom";
    public const string LoupeStepped = "screenshotLoupeSteppedZoomByDefault";
    public const string PreviewPosition = "screenshotPreviewPosition";
    public const string PreviewTakesFocus = "screenshotPreviewTakesFocus";
    public const string DefaultAction = "screenshotDefaultAction";
    public const string PreviewEnabled = "screenshotPreviewEnabled";
    public const string PreviewDuration = "screenshotPreviewDuration";

    // Output
    public const string CopyToClipboard = "screenshotCopyToClipboard";
    public const string CopyImageOnly = "screenshotCopyImageOnly";
    public const string AddToShelf = "screenshotAddToShelf";
    public const string SaveFolder = "screenshotSaveFolder";
    public const string SaveSubfolder = "screenshotSaveSubfolder";
    public const string FileNamePattern = "screenshotFileNamePattern";
    public const string FileNumberStart = "screenshotFileNumberStart";
    public const string FileNumberNext = "screenshotFileNumberNext";
    public const string Downscale = "screenshotDownscale";
    public const string RecentCapturesEnabled = "recentCapturesEnabled";

    // Editor
    public const string ToolOrder = "screenshotToolOrder";
    public const string ToolShortcuts = "screenshotToolShortcuts";
    public const string ToolShortcutsEnabled = "screenshotToolShortcutsEnabled";
    public const string LastTool = "screenshotLastTool";
    public const string LastColor = "screenshotLastColor";
    public const string LastStroke = "screenshotLastStroke";
    public const string LastTextSize = "screenshotLastTextSize";
    public const string LastBlurLevel = "screenshotLastBlurLevel";
    public const string LastBlurStyle = "screenshotLastBlurStyle";
    public const string LastBlurTextOnly = "screenshotLastBlurTextOnly";
    public const string SpotlightDim = "screenshotSpotlightDim";
    public const string LastArrowStyle = "screenshotLastArrowStyle";
    public const string LastSticker = "screenshotLastSticker";
    public const string Shadows = "screenshotAnnotationShadows";
    public const string BackdropStyle = "screenshotBackdropStyle";
    public const string BackdropPresets = "screenshotBackdropPresets";
    public const string WatermarkStyle = "screenshotWatermarkStyle";
    public const string WatermarkPresets = "screenshotWatermarkPresets";

    // Links (hidden on Windows until a backend exists)
    public const string SharingEnabled = "screenshotSharingEnabled";
    public const string UploadDuration = "screenshotUploadDuration";
    public const string RecorderSharingEnabled = "recorderSharingEnabled";

    // Recorder
    public const string RecCountdown = "recorderCountdown";
    public const string RecSystemAudio = "recorderSystemAudio";
    public const string RecMicrophone = "recorderMicrophone";
    public const string RecOpenEditor = "recorderOpenEditor";
    public const string RecAutoZoom = "recorderAutomaticZoom";
    public const string RecSaveFolder = "recorderSaveFolder";
    public const string RecQuality = "recorderQuality";
    public const string RecFrameRate = "recorderFrameRate";
    public const string RecGifSize = "recorderGIFSize";
    public const string RecGifFps = "recorderGIFFrameRate";
    public const string RecPresets = "recorderEditorPresets";

    // OCR / colour
    public const string OcrRemoveBreaks = "screenOCRRemoveLineBreaks";
    public const string OcrQr = "screenOCRDetectQRCodes";
    public const string ColorFormat = "colorPickerFormat";
    public const string ColorBareHex = "colorPickerBareHex";

    public const string DefaultToolOrder = "select,arrow,pixelate,crop,text,sticker,rect,highlight,freehand,line,ellipse,counter,redact,spotlight,expandCanvas";

    /// <summary>Registers every default (call from Start and from section constructors; repeating is fine).</summary>
    public static void RegisterDefaults(IAppSettings s)
    {
        s.RegisterDefaults(new Dictionary<string, object?>
        {
            [ScreenshotShortcutEnabled] = false,
            [RecorderShortcutEnabled] = false,
            [OcrShortcutEnabled] = false,
            [ColorShortcutEnabled] = false,
            [RecentShortcutEnabled] = false,
            [FullScreenShortcutEnabled] = false,
            [LastCaptureShortcutEnabled] = false,
            [ClipboardShortcutEnabled] = false,
            [UploadShortcutEnabled] = false,
            [ShowMenuScreenshot] = true,
            [ShowMenuRecorder] = true,
            [ShowMenuOcr] = true,
            [ShowMenuColor] = true,
            [Freeze] = true,
            [HideOwn] = true,
            [Delay] = 0,
            [IncludePointer] = false,
            [ShowLastRegion] = true,
            [HighlightWindows] = true,
            [LoupeStartsOn] = false,
            [LoupeRememberZoom] = false,
            [LoupeLastZoom] = 1.0,
            [LoupeDefaultZoom] = 1.0,
            [LoupeStepped] = false,
            [PreviewPosition] = "",
            [PreviewTakesFocus] = true,
            [DefaultAction] = "",
            [PreviewEnabled] = true,
            [PreviewDuration] = 3,
            [CopyToClipboard] = false,
            [CopyImageOnly] = false,
            [AddToShelf] = false,
            [SaveFolder] = "",
            [SaveSubfolder] = "",
            [FileNamePattern] = "",
            [FileNumberStart] = 1,
            [FileNumberNext] = 1,
            [Downscale] = false,
            [RecentCapturesEnabled] = true,
            [ToolOrder] = DefaultToolOrder,
            [ToolShortcuts] = "",
            [ToolShortcutsEnabled] = true,
            [LastTool] = "arrow",
            [LastColor] = "red",
            [LastStroke] = "medium",
            [LastTextSize] = 19,
            [LastBlurLevel] = 3,
            [LastBlurStyle] = "pixelate",
            [LastBlurTextOnly] = false,
            [SpotlightDim] = 0.5,
            [LastArrowStyle] = "filled",
            [LastSticker] = "check",
            [Shadows] = false,
            [BackdropStyle] = "",
            [BackdropPresets] = "[]",
            [WatermarkStyle] = "",
            [WatermarkPresets] = "[]",
            [SharingEnabled] = false,
            [UploadDuration] = 3600,
            [RecorderSharingEnabled] = false,
            [RecCountdown] = 3,
            [RecSystemAudio] = true,
            [RecMicrophone] = false,
            [RecOpenEditor] = true,
            [RecAutoZoom] = true,
            [RecSaveFolder] = "",
            [RecQuality] = "balanced",
            [RecFrameRate] = 60,
            [RecGifSize] = "medium",
            [RecGifFps] = 12,
            [RecPresets] = "[]",
            [OcrRemoveBreaks] = false,
            [OcrQr] = true,
            [ColorFormat] = "hex",
            [ColorBareHex] = false,
        });
    }
}
