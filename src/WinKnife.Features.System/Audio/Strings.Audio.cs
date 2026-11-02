// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.SystemTools;

/// <summary>English strings of the Sound features (spec 02a section 3.2-3.5, 01 section 5.3, 05 part D).</summary>
internal static partial class Strings
{
    public static class Mixer
    {
        public const string Title = "Volume mixer";
        public const string Output = "Output";
        public const string ChooseOutput = "Choose system output";
        public const string SystemSounds = "Communications";
        public const string ChooseSystemSounds = "Choose where calls and chat apps play";
        public const string Microphone = "Microphone";
        public const string ChooseMicrophone = "Choose microphone";
        public const string Default = "Default";
        public const string OutputUnavailable = "Output unavailable";
        public const string MicrophoneUnavailable = "Microphone unavailable";
        public const string MicFallback = "Using default until this microphone returns.";
        public const string NoOutputs = "No outputs found";
        public const string NoMicrophones = "No microphones found";
        public const string CurrentSuffix = " (current)";
        public const string Empty = "Apps that use audio show up here";
        public const string Actions = "Actions";
        public const string PinToTop = "Pin to Top";
        public const string Unpin = "Unpin";
        public const string MoveUp = "Move Up";
        public const string MoveDown = "Move Down";
        public const string HideFromList = "Hide from the list";
        public const string ChooseOutputTip = "Choose output";
        public const string ResetTip = "Reset to 100%";
        public const string DragTip = "Hold Ctrl and drag to reorder";
        public const string DeviceFallback = "Using default until this device returns.";
        public const string Options = "Options";
        public const string HideInactive = "Hide inactive apps";
        public const string LowerOnDisconnect = "Lower volume when headphones disconnect";
        public const string LowerOnDisconnectCaption = "Adjusts output when wired or Bluetooth headphones disconnect.";
        public const string VolumeAfterDisconnect = "Volume after disconnect";
        public const string FinerSteps = "Use finer volume steps";
        public const string FinerStepsCaption = "Turns volume wheels and keys into smaller system volume steps.";
        public const string FinerStepsFailed = "Could not listen for volume keys.";
        public const string AppsInList = "Apps in the list";
        public const string All = "All";
        public const string ConnectWireless = "Connect to a wireless speaker…";
        public const string PlayingNow = "Playing now";

        public static string SwitchFailed(string reason) => $"Could not switch: {reason}";

        public static string Hidden(int count) => $"Hidden: {count}";
    }

    public static class OutputSwitcher
    {
        public const string Title = "Output switcher";
        public const string Switch = "Switch outputs with shortcut";
        public const string Caption = "Choose outputs and use the shortcut to move to the next available one.";
        public const string Shortcut = "Shortcut";
        public const string OutputsInCycle = "Outputs in cycle";
        public const string SelectOne = "Select at least one available output.";
        public const string Rejected = "Windows rejected this shortcut. Choose another one.";
    }

    public static class AudioPriority
    {
        public const string Title = "Audio priority";
        public const string Caption = "Devices are selected in priority order. When a higher-priority device connects, it becomes active. When the active one disconnects, the next available takes over.";
        public const string OutputPriority = "Output priority";
        public const string InputPriority = "Microphone priority";
        public const string OutputSwitch = "Automatically switch to the highest-priority output";
        public const string InputSwitch = "Automatically switch to the highest-priority microphone";
        public const string Current = "Current";
        public const string Unavailable = "Unavailable";
        public const string MoveUp = "Move up";
        public const string MoveDown = "Move down";
    }

    public static class MicMute
    {
        public const string Title = "Mute microphone";
        public const string Unmute = "Unmute microphone";
        public const string On = "Mic Mute on";
        public const string Off = "Mic Mute off";
        public const string Partial = "Some microphones could not be muted";
        public const string StillMuted = "Some microphones are still muted";
        public const string Caption = "Uses each microphone’s mute or input-volume controls. Some apps may still hear you. This is not a privacy lock.";
        public const string Indicator = "Show in the notification area while Mic Mute is on";
        public const string IndicatorCaption = "A red mic indicator shows that Mic Mute is on. It does not guarantee that all apps stop capturing audio.";
        public const string GlobalShortcut = "Global shortcut";
    }

    public static class MusicBlock
    {
        public const string Title = "Media keys";
        public const string Row = "Stop Music from opening on its own";
        public const string Caption = "Blocks the music app opening after a detected media key. Headphone commands without a detected key are left alone.";
        public const string Unavailable = "This protection is unavailable right now. Turn it off and on to try again.";
        public const string OpenInstead = "Open instead";
        public const string None = "None";
        public const string ChooseApp = "Choose app…";
        public const string PlayAfter = "Play after opening replacement";
        public const string Rejected = "Music and iTunes are the apps being blocked, so they can’t open instead. Choose another app.";
        public const string BlockedSet = "Blocked: Media Player, Groove Music, Windows Media Player and iTunes.";
    }
}
