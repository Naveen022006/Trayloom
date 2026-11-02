// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// One chosen key (Caps Lock by default) acts as a modifier combination (Ctrl+Alt+Shift+Win by default) while it is
/// held. The physical key is swallowed; the modifier key-downs are injected on press and released on release (an
/// unassigned key is tapped in between when nothing else was pressed, so the Start menu / Alt menu bar do not pop up).
/// Optionally a quick tap sends Escape instead. Note: Ctrl+Alt+Shift+Win is also the Office hotkey on Windows, so
/// choose a smaller combination if you use Microsoft 365.
/// </summary>
public sealed class SuperKeyFeature : InputFeatureBase
{
    private static readonly IReadOnlyList<Choice<int>> Keys =
    [
        new(0x14, "Caps Lock"),
        new(0x5D, "Menu key"),
        new(0x91, "Scroll Lock"),
        new(0x13, "Pause"),
        new(0x2D, "Insert"),
        new(0xA3, "Right Ctrl"),
        new(0x7C, "F13"),
    ];

    private volatile int _triggerKey = 0x14;
    private volatile ushort[] _modifiers = [0xA2, 0xA4, 0xA0, 0x5B];
    private volatile bool _tapEscape;
    private bool _held;
    private bool _used;
    private ushort[] _activeModifiers = [];

    public override string Id => "super-key";

    public override string Name => "Super key";

    public override string Description => "Turn one key, such as Caps Lock, into Ctrl+Alt+Shift+Win (or any combination) while you hold it.";

    protected override void OnStart()
    {
        _held = false;
        SubscribeKeyboard(OnKey, HookPriorities.SuperKey);
    }

    protected override void OnStop() => Release(sendTap: false);

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        var s = Context.Settings;
        _triggerKey = s.Get("key", 0x14);
        var list = new List<ushort>(4);
        if (s.Get("ctrl", true))
        {
            list.Add(0xA2);
        }

        if (s.Get("alt", true))
        {
            list.Add(0xA4);
        }

        if (s.Get("shift", true))
        {
            list.Add(0xA0);
        }

        if (s.Get("win", true))
        {
            list.Add(0x5B);
        }

        _modifiers = [.. list];
        _tapEscape = s.Get("tapEscape", false);
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddChoice("key", "Super key", Keys, 0x14, "The physical key that becomes the modifier combination. Its normal function is disabled.")
        .AddToggle("ctrl", "Ctrl", true)
        .AddToggle("alt", "Alt", true)
        .AddToggle("shift", "Shift", true)
        .AddToggle("win", "Windows", true)
        .AddToggle("tapEscape", "A quick tap sends Escape", false,
            "Press and release the super key without pressing anything else to send Escape.");

    private bool OnKey(KeyboardHookEvent e)
    {
        if (e.IsFromWinKnife)
        {
            return false;
        }

        if (e.VkCode != _triggerKey)
        {
            if (_held && e.IsKeyDown)
            {
                _used = true;
            }

            return false;
        }

        if (e.IsKeyDown)
        {
            if (!_held)
            {
                if (Paused || _modifiers.Length == 0)
                {
                    return false;
                }

                _held = true;
                _used = false;
                _activeModifiers = _modifiers;
                KeySender.SendKeys(_activeModifiers, keyUp: false);
            }

            return true; // swallow the press and its auto-repeat
        }

        if (_held)
        {
            Release(sendTap: !_used && _tapEscape);
            return true;
        }

        return false;
    }

    // Hook thread (and Stop on the UI thread, after the subscription is gone or about to be).
    private void Release(bool sendTap)
    {
        if (!_held)
        {
            return;
        }

        _held = false;
        if (!_used)
        {
            // Nothing else was pressed: tap an unassigned key so releasing Win/Alt cannot open a menu.
            KeySender.Tap(Win32.VK_UNASSIGNED);
        }

        var reversed = new ushort[_activeModifiers.Length];
        for (var i = 0; i < reversed.Length; i++)
        {
            reversed[i] = _activeModifiers[^(i + 1)];
        }

        KeySender.SendKeys(reversed, keyUp: true);
        _activeModifiers = [];
        if (sendTap)
        {
            KeySender.Tap(0x1B);
        }
    }
}
