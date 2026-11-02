// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.SystemTools;

/// <summary>English strings of the Sound features (spec 02a section 3.2-3.5, 01 section 5.3, 05 part D).</summary>
internal static partial class Strings
{
    public static class Mixer
    {
        public static  string Title => Loc.T("Volume mixer");
        public static  string Output => Loc.T("Output");
        public static  string ChooseOutput => Loc.T("Choose system output");
        public static  string SystemSounds => Loc.T("Communications");
        public static  string ChooseSystemSounds => Loc.T("Choose where calls and chat apps play");
        public static  string Microphone => Loc.T("Microphone");
        public static  string ChooseMicrophone => Loc.T("Choose microphone");
        public static  string Default => Loc.T("Default");
        public static  string OutputUnavailable => Loc.T("Output unavailable");
        public static  string MicrophoneUnavailable => Loc.T("Microphone unavailable");
        public static  string MicFallback => Loc.T("Using default until this microphone returns.");
        public static  string NoOutputs => Loc.T("No outputs found");
        public static  string NoMicrophones => Loc.T("No microphones found");
        public static  string CurrentSuffix => Loc.T(" (current)");
        public static  string Empty => Loc.T("Apps that use audio show up here");
        public static  string Actions => Loc.T("Actions");
        public static  string PinToTop => Loc.T("Pin to Top");
        public static  string Unpin => Loc.T("Unpin");
        public static  string MoveUp => Loc.T("Move Up");
        public static  string MoveDown => Loc.T("Move Down");
        public static  string HideFromList => Loc.T("Hide from the list");
        public static  string ChooseOutputTip => Loc.T("Choose output");
        public static  string ResetTip => Loc.T("Reset to 100%");
        public static  string DragTip => Loc.T("Hold Ctrl and drag to reorder");
        public static  string DeviceFallback => Loc.T("Using default until this device returns.");
        public static  string Options => Loc.T("Options");
        public static  string HideInactive => Loc.T("Hide inactive apps");
        public static  string LowerOnDisconnect => Loc.T("Lower volume when headphones disconnect");
        public static  string LowerOnDisconnectCaption => Loc.T("Adjusts output when wired or Bluetooth headphones disconnect.");
        public static  string VolumeAfterDisconnect => Loc.T("Volume after disconnect");
        public static  string FinerSteps => Loc.T("Use finer volume steps");
        public static  string FinerStepsCaption => Loc.T("Turns volume wheels and keys into smaller system volume steps.");
        public static  string FinerStepsFailed => Loc.T("Could not listen for volume keys.");
        public static  string AppsInList => Loc.T("Apps in the list");
        public static  string All => Loc.T("All");
        public static  string ConnectWireless => Loc.T("Connect to a wireless speaker…");
        public static  string PlayingNow => Loc.T("Playing now");

        public static string SwitchFailed(string reason) => Loc.F("Could not switch: {0}", reason);

        public static string Hidden(int count) => $"Hidden: {count}";
    }

    public static class OutputSwitcher
    {
        public static  string Title => Loc.T("Output switcher");
        public static  string Switch => Loc.T("Switch outputs with shortcut");
        public static  string Caption => Loc.T("Choose outputs and use the shortcut to move to the next available one.");
        public static  string Shortcut => Loc.T("Shortcut");
        public static  string OutputsInCycle => Loc.T("Outputs in cycle");
        public static  string SelectOne => Loc.T("Select at least one available output.");
        public static  string Rejected => Loc.T("Windows rejected this shortcut. Choose another one.");
    }

    public static class AudioPriority
    {
        public static  string Title => Loc.T("Audio priority");
        public static  string Caption => Loc.T("Devices are selected in priority order. When a higher-priority device connects, it becomes active. When the active one disconnects, the next available takes over.");
        public static  string OutputPriority => Loc.T("Output priority");
        public static  string InputPriority => Loc.T("Microphone priority");
        public static  string OutputSwitch => Loc.T("Automatically switch to the highest-priority output");
        public static  string InputSwitch => Loc.T("Automatically switch to the highest-priority microphone");
        public static  string Current => Loc.T("Current");
        public static  string Unavailable => Loc.T("Unavailable");
        public static  string MoveUp => Loc.T("Move up");
        public static  string MoveDown => Loc.T("Move down");
    }

    public static class MicMute
    {
        public static  string Title => Loc.T("Mute microphone");
        public static  string Unmute => Loc.T("Unmute microphone");
        public static  string On => Loc.T("Mic Mute on");
        public static  string Off => Loc.T("Mic Mute off");
        public static  string Partial => Loc.T("Some microphones could not be muted");
        public static  string StillMuted => Loc.T("Some microphones are still muted");
        public static  string Caption => Loc.T("Uses each microphone’s mute or input-volume controls. Some apps may still hear you. This is not a privacy lock.");
        public static  string Indicator => Loc.T("Show in the notification area while Mic Mute is on");
        public static  string IndicatorCaption => Loc.T("A red mic indicator shows that Mic Mute is on. It does not guarantee that all apps stop capturing audio.");
        public static  string GlobalShortcut => Loc.T("Global shortcut");
    }

    public static class MusicBlock
    {
        public static  string Title => Loc.T("Media keys");
        public static  string Row => Loc.T("Stop Music from opening on its own");
        public static  string Caption => Loc.T("Blocks the music app opening after a detected media key. Headphone commands without a detected key are left alone.");
        public static  string Unavailable => Loc.T("This protection is unavailable right now. Turn it off and on to try again.");
        public static  string OpenInstead => Loc.T("Open instead");
        public static  string None => Loc.T("None");
        public static  string ChooseApp => Loc.T("Choose app…");
        public static  string PlayAfter => Loc.T("Play after opening replacement");
        public static  string Rejected => Loc.T("Music and iTunes are the apps being blocked, so they can’t open instead. Choose another app.");
        public static  string BlockedSet => Loc.T("Blocked: Media Player, Groove Music, Windows Media Player and iTunes.");
    }
}
