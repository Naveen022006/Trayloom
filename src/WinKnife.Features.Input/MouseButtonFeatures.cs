// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// Remaps the two side buttons (XButton1/XButton2). Defaults: back = Alt+Left, forward = Alt+Right, which is what
/// browsers, Explorer and most apps already do with the native Back/Forward messages, but works in apps that ignore
/// those. Other presets (tab switching, copy/paste, media keys...) or a custom shortcut can be chosen.
/// </summary>
public sealed class SideButtonsFeature : InputFeatureBase
{
    private static readonly IReadOnlyList<Choice<string>> Presets =
    [
        new("back", "Back (Alt+Left)"),
        new("forward", "Forward (Alt+Right)"),
        new("closeTab", "Close tab (Ctrl+W)"),
        new("nextTab", "Next tab (Ctrl+Tab)"),
        new("prevTab", "Previous tab (Ctrl+Shift+Tab)"),
        new("copy", "Copy (Ctrl+C)"),
        new("paste", "Paste (Ctrl+V)"),
        new("taskView", "Task view (Win+Tab)"),
        new("showDesktop", "Show desktop (Win+D)"),
        new("playPause", "Play / pause"),
        new("nextTrack", "Next track"),
        new("prevTrack", "Previous track"),
        new("mute", "Mute"),
        new("custom", "Custom shortcut..."),
        new("none", "Leave unchanged"),
    ];

    private readonly MouseButtonRemapper _remapper = new();

    public override string Id => "side-buttons";

    public override string Name => "Side buttons";

    public override string Description => "Make the two thumb buttons of your mouse go back and forward, or send any shortcut you pick.";

    protected override void OnStart() => SubscribeMouse(e => !Paused && _remapper.Handle(e), HookPriorities.SideButtons);

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        _remapper.Set(null, Resolve("xbutton1", "back"), Resolve("xbutton2", "forward"));
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddChoice("xbutton1", "Back button (XButton1)", Presets, "back")
        .AddHotkey("xbutton1Custom", "Custom shortcut for the back button", null, "Used when the action above is \"Custom shortcut\".")
        .AddChoice("xbutton2", "Forward button (XButton2)", Presets, "forward")
        .AddHotkey("xbutton2Custom", "Custom shortcut for the forward button", null, "Used when the action above is \"Custom shortcut\".")
        .AddNote("Buttons mapped in \"Mouse button shortcuts\" use that mapping instead.");

    private ushort[]? Resolve(string key, string fallback)
    {
        var id = Context!.Settings.Get(key, fallback);
        return id switch
        {
            "back" => [0x12, 0x25],
            "forward" => [0x12, 0x27],
            "closeTab" => [0x11, 0x57],
            "nextTab" => [0x11, 0x09],
            "prevTab" => [0x11, 0x10, 0x09],
            "copy" => [0x11, 0x43],
            "paste" => [0x11, 0x56],
            "taskView" => [0x5B, 0x09],
            "showDesktop" => [0x5B, 0x44],
            "playPause" => [0xB3],
            "nextTrack" => [0xB0],
            "prevTrack" => [0xB1],
            "mute" => [0xAD],
            "custom" => Context.Settings.Get<Hotkey?>(key + "Custom", null) is { } hotkey ? KeySender.ToChord(hotkey) : null,
            _ => null,
        };
    }
}

/// <summary>
/// Maps the middle button and the two side buttons to a key chord of your choice, recorded with the hotkey recorder.
/// Unset buttons behave normally. Takes priority over the side-buttons feature for the same button.
/// </summary>
public sealed class MouseButtonShortcutsFeature : InputFeatureBase
{
    private readonly MouseButtonRemapper _remapper = new();

    public override string Id => "mouse-button-shortcuts";

    public override string Name => "Mouse button shortcuts";

    public override string Description => "Turn the middle and side buttons of your mouse into keyboard shortcuts.";

    protected override void OnStart() => SubscribeMouse(e => !Paused && _remapper.Handle(e), HookPriorities.MouseButtonShortcuts);

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        _remapper.Set(Chord("middle"), Chord("xbutton1"), Chord("xbutton2"));
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddHotkey("middle", "Middle button", null, "Clear the field (Backspace) to keep the normal middle click.")
        .AddHotkey("xbutton1", "Side button 1 (back)", null)
        .AddHotkey("xbutton2", "Side button 2 (forward)", null)
        .AddNote("A shortcut needs at least one modifier (Ctrl, Alt, Shift or Win), or be a function key.");

    private ushort[]? Chord(string key) =>
        Context!.Settings.Get<Hotkey?>(key, null) is { } hotkey ? KeySender.ToChord(hotkey) : null;
}
