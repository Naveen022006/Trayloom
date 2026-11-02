// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Input;

/// <summary>Flat <see cref="IAppSettings"/> keys (Vorssaint <c>DefaultsKey</c> names) and their registered defaults.</summary>
internal static class K
{
    public const string ScrollInvertV = "scrollInverterEnabled";
    public const string ScrollInvertH = "scrollInverterHorizontalEnabled";
    public const string ScrollInvertExceptions = "scrollInverterExceptions";
    public const string ScrollHorizontal = "scrollHorizontalEnabled";
    public const string ScrollHorizontalModifier = "scrollHorizontalModifier";
    public const string PinchZoom = "pinchZoomEnabled";
    public const string PinchZoomModifier = "pinchZoomModifier";

    public const string FocusEnabled = "focusFollowsMouseEnabled";
    public const string FocusDelay = "focusFollowsMouseDelayMilliseconds";
    public const string FocusRaise = "focusFollowsMouseRaise";
    public const string FocusWaitForStop = "focusFollowsMouseWaitForStop";
    public const string FocusExceptions = "focusFollowsMouseExceptions";
    public const string FocusSkipFullscreen = "skipFullscreen";

    public const string SmoothEnabled = "smoothScrollEnabled";
    public const string SmoothStep = "smoothScrollStep";
    public const string SmoothResponse = "smoothScrollResponse";
    public const string SmoothCoast = "smoothScrollCoast";
    public const string SmoothExceptions = "smoothScrollExceptions";

    public const string LinearEnabled = "linearScrollEnabled";
    public const string LinearLines = "linearScrollLines";
    public const string LinearExceptions = "linearScrollExceptions";

    public const string AccelDisabled = "mouseAccelerationDisabled";

    public const string NavEnabled = "mouseNavigationEnabled";
    public const string NavExceptions = "mouseNavigationExceptions";

    public const string ButtonShortcutsEnabled = "mouseButtonShortcutsEnabled";
    public const string ButtonShortcuts = "mouseButtonShortcuts";
    public const string SpacesGestureEnabled = "mouseSpacesGestureEnabled";
    public const string SpacesGestureButton = "mouseSpacesGestureButton";
    public const string SpacesGestureFollows = "mouseSpacesGestureFollowsDrag";
    public const string ButtonExceptions = "mouseButtonExceptions";

    public const string ClickDebounceEnabled = "mouseClickDebounceEnabled";
    public const string ClickDebounceWindow = "mouseClickDebounceWindowMs";

    public const string KeyDebounceEnabled = "keyboardDebounceEnabled";
    public const string KeyDebounceWindow = "keyboardDebounceWindowMs";
    public const string KeyDebounceKeys = "keyboardDebounceKeyWindows";

    public const string SuperEnabled = "superKeyEnabled";
    public const string SuperSource = "superKeySource";
    public const string SuperModifiers = "superKeyModifiers";
    public const string SuperSolo = "superKeySoloAction";
    public const string SuperExceptions = "superKeyExceptions";

    public const string SnippetsEnabled = "textSnippetsEnabled";
    public const string Snippets = "textSnippets";
    public const string LibraryEnabled = "snippetLibraryEnabled";
    public const string SoundEnabled = "snippetSoundEnabled";
    public const string SoundName = "snippetSoundName";

    public static string Quit(string suffix) => "quitProtectionQuit" + suffix;

    public static string Close(string suffix) => "quitProtectionClose" + suffix;

    /// <summary>Registers every default of the Input group (idempotent).</summary>
    public static void RegisterDefaults(IAppSettings app)
    {
        var none = new List<string>();
        app.RegisterDefaults(new Dictionary<string, object?>
        {
            [ScrollInvertV] = false,
            [ScrollInvertH] = false,
            [ScrollInvertExceptions] = none,
            [ScrollHorizontal] = false,
            [ScrollHorizontalModifier] = "shift",
            [PinchZoom] = false,
            [PinchZoomModifier] = "control",
            [FocusEnabled] = false,
            [FocusDelay] = 250,
            [FocusRaise] = true,
            [FocusWaitForStop] = true,
            [FocusExceptions] = none,
            [FocusSkipFullscreen] = true,
            [SmoothEnabled] = false,
            [SmoothStep] = 40,
            [SmoothResponse] = 65,
            [SmoothCoast] = 0,
            [SmoothExceptions] = none,
            [LinearEnabled] = false,
            [LinearLines] = 3,
            [LinearExceptions] = none,
            [AccelDisabled] = false,
            [NavEnabled] = false,
            [NavExceptions] = none,
            [ButtonShortcutsEnabled] = false,
            [ButtonShortcuts] = new Dictionary<string, string>(),
            [SpacesGestureEnabled] = false,
            [SpacesGestureButton] = 0,
            [SpacesGestureFollows] = false,
            [ButtonExceptions] = none,
            [ClickDebounceEnabled] = false,
            [ClickDebounceWindow] = 25,
            [KeyDebounceEnabled] = false,
            [KeyDebounceWindow] = 5,
            [KeyDebounceKeys] = string.Empty,
            [SuperEnabled] = false,
            [SuperSource] = "capsLock",
            [SuperModifiers] = "control+option+shift+command",
            [SuperSolo] = "none",
            [SuperExceptions] = none,
            [SnippetsEnabled] = false,
            [LibraryEnabled] = false,
            [SoundEnabled] = false,
            [SoundName] = string.Empty,
            [Quit("Enabled")] = false,
            [Close("Enabled")] = false,
            [Quit("Mode")] = "hold",
            [Close("Mode")] = "hold",
            [Quit("HoldDurationMs")] = 800.0,
            [Close("HoldDurationMs")] = 800.0,
            [Quit("DoubleIntervalMs")] = 600.0,
            [Close("DoubleIntervalMs")] = 600.0,
            [Quit("ExtraModifier")] = "shift",
            [Close("ExtraModifier")] = "shift",
            [Quit("Scope")] = "all",
            [Close("Scope")] = "all",
            [Quit("Exceptions")] = none,
            [Close("Exceptions")] = none,
            [Quit("ShowFeedback")] = true,
            [Close("ShowFeedback")] = true,
            ["panelControlMouseScroll"] = true,
            ["panelControlLinearScroll"] = true,
            ["panelControlFocusFollowsMouse"] = true,
            ["panelControlMouseAcceleration"] = true,
            ["panelControlMouseNavigation"] = true,
            ["panelControlMouseButtonShortcuts"] = true,
            ["panelControlMouseClickDebounce"] = true,
            ["panelControlKeyDebounce"] = true,
            ["panelControlTextSnippets"] = true,
            ["panelControlSuperKey"] = true,
        });
    }
}
