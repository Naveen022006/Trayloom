// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// Ignores a second press of the same mouse button that follows the first within a few milliseconds (a worn switch
/// "double-clicking" on its own). The bounced press and its matching release are both swallowed. Events injected by
/// other programs are never filtered.
/// </summary>
public sealed class ClickDebounceFeature : InputFeatureBase
{
    // Index: 0 left, 1 right, 2 middle, 3 XButton1, 4 XButton2. Hook-thread only.
    private readonly uint[] _lastDown = new uint[5];
    private readonly bool[] _hasDown = new bool[5];
    private readonly bool[] _swallowUp = new bool[5];

    private volatile int _windowMs = 40;
    private volatile int _buttonMask = 0b00001; // left only by default

    public override string Id => "click-debounce";

    public override string Name => "Extra click filter";

    public override string Description => "Ignore accidental double clicks caused by a worn mouse button.";

    protected override void OnStart()
    {
        Array.Clear(_hasDown);
        Array.Clear(_swallowUp);
        SubscribeMouse(OnMouse, HookPriorities.ClickDebounce);
    }

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        var s = Context.Settings;
        _windowMs = Math.Clamp(s.Get("windowMs", 40), 5, 250);
        _buttonMask =
            (s.Get("left", true) ? 1 : 0) |
            (s.Get("right", false) ? 2 : 0) |
            (s.Get("middle", false) ? 4 : 0) |
            (s.Get("xbuttons", false) ? 24 : 0);
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddIntSlider("windowMs", "Ignore a repeat click within (ms)", 5, 250, 40,
            "A second press of the same button faster than this is treated as a glitch. Normal double clicks are far slower.")
        .AddToggle("left", "Filter the left button", true)
        .AddToggle("right", "Filter the right button", false)
        .AddToggle("middle", "Filter the middle button", false)
        .AddToggle("xbuttons", "Filter the side buttons", false);

    private static int ButtonIndex(MouseHookEvent e) => e.Kind switch
    {
        MouseEventKind.LeftDown or MouseEventKind.LeftUp => 0,
        MouseEventKind.RightDown or MouseEventKind.RightUp => 1,
        MouseEventKind.MiddleDown or MouseEventKind.MiddleUp => 2,
        MouseEventKind.XButtonDown or MouseEventKind.XButtonUp => e.XButton == 1 ? 3 : 4,
        _ => -1,
    };

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsInjected || !(e.IsButtonDown || e.IsButtonUp))
        {
            return false;
        }

        var index = ButtonIndex(e);
        if (index < 0)
        {
            return false;
        }

        if (e.IsButtonUp)
        {
            if (_swallowUp[index])
            {
                _swallowUp[index] = false;
                return true;
            }

            return false;
        }

        if (Paused || (_buttonMask & (1 << index)) == 0)
        {
            return false;
        }

        if (_hasDown[index] && unchecked(e.Timestamp - _lastDown[index]) < (uint)_windowMs)
        {
            _swallowUp[index] = true;
            return true;
        }

        _hasDown[index] = true;
        _lastDown[index] = e.Timestamp;
        return false;
    }
}

/// <summary>
/// Ignores a repeated key-down of the same key arriving within a few milliseconds of the previous press that is not
/// auto-repeat (keyboard switch chatter). The bounced press and its release are swallowed. Modifier and lock keys
/// and injected input are never filtered.
/// </summary>
public sealed class KeyDebounceFeature : InputFeatureBase
{
    private readonly uint[] _lastDown = new uint[256];
    private readonly bool[] _hasDown = new bool[256];
    private readonly bool[] _isDown = new bool[256];
    private readonly bool[] _swallowed = new bool[256];

    private volatile int _windowMs = 40;

    public override string Id => "key-debounce";

    public override string Name => "Keyboard debounce";

    public override string Description => "Ignore accidental repeated key presses caused by a chattering key switch.";

    protected override void OnStart()
    {
        Array.Clear(_hasDown);
        Array.Clear(_isDown);
        Array.Clear(_swallowed);
        SubscribeKeyboard(OnKey, HookPriorities.KeyDebounce);
    }

    protected override void LoadSettings()
    {
        if (Context is not null)
        {
            _windowMs = Math.Clamp(Context.Settings.Get("windowMs", 40), 5, 250);
        }
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddIntSlider("windowMs", "Ignore a repeat press within (ms)", 5, 250, 40,
            "A key pressed again faster than this (after being released) is treated as chatter. Holding a key to auto-repeat still works.")
        .AddNote("Fast typists can double-tap a letter in about 80 ms, so keep this well below that.");

    private static bool IsExempt(int vk) =>
        vk is >= 0x10 and <= 0x14 or 0x5B or 0x5C or 0x90 or 0x91 or >= 0xA0 and <= 0xA5;

    private bool OnKey(KeyboardHookEvent e)
    {
        var vk = e.VkCode;
        if (e.IsInjected || (uint)vk > 255 || IsExempt(vk))
        {
            return false;
        }

        if (e.IsKeyUp)
        {
            _isDown[vk] = false;
            if (_swallowed[vk])
            {
                _swallowed[vk] = false;
                return true;
            }

            return false;
        }

        if (_isDown[vk])
        {
            // Auto-repeat of the current press: follows the fate of that press.
            return _swallowed[vk];
        }

        _isDown[vk] = true;
        if (Paused)
        {
            return false;
        }

        if (_hasDown[vk] && unchecked(e.Timestamp - _lastDown[vk]) < (uint)_windowMs)
        {
            _swallowed[vk] = true;
            return true;
        }

        _hasDown[vk] = true;
        _lastDown[vk] = e.Timestamp;
        return false;
    }
}
