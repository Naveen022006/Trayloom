// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>A flyout with master volume and a volume slider, meter and mute button for every app that is making sound.</summary>
public sealed class VolumeMixerFeature : IFeature
{
    private static readonly Hotkey DefaultHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Key.A);

    private IFeatureContext? _context;
    private HotkeyBinding? _hotkey;
    private MixerWindow? _window;

    public string Id => "volume-mixer";

    public string Name => "Volume mixer";

    public string Description => "Set the volume of each app separately, with live level meters, pinned apps and your own order.";

    public FeatureGroup Group => FeatureGroup.Sound;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", "Volume mixer", Toggle, ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _hotkey = new HotkeyBinding(context, "hotkey", DefaultHotkey, Toggle);
    }

    public void Stop()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        if (_window is not null)
        {
            _window.Hide();
            _window.Close();
            _window = null;
        }

        _context = null;
    }

    public FrameworkElement? CreateSettingsView() =>
        _context is null
            ? null
            : new SettingsViewBuilder(_context.Settings)
                .AddHotkey("hotkey", "Open the volume mixer", DefaultHotkey)
                .AddToggle("showInactive", "Show apps that are not playing right now", false, "Pinned apps are always listed while they have an audio session.")
                .AddNote("Pin an app or use the arrows in the mixer to change the order; both are remembered.")
                .Build();

    private void Toggle()
    {
        if (_context is null)
        {
            return;
        }

        _window ??= new MixerWindow(_context);
        _window.Toggle();
    }
}
