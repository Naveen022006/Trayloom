// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Native;

namespace WinKnife.Features.Input;

/// <summary>Names and numbering of mouse inputs (CG numbering of the original: 2 middle, 3 back, 4 forward, negative = side wheel).</summary>
internal static class MouseInputs
{
    public const int Middle = 2;
    public const int Back = 3;
    public const int Forward = 4;
    public const int SideWheelRight = -1;
    public const int SideWheelLeft = -2;

    public static string Name(int button) => button switch
    {
        Middle => Strings.MiddleButton,
        Back => Strings.BackSideButton,
        Forward => Strings.ForwardSideButton,
        SideWheelLeft => Strings.SideWheelLeft,
        SideWheelRight => Strings.SideWheelRight,
        _ => string.Format(Strings.OtherButtonFormat, button + 1),
    };

    /// <summary>True when another feature (the radial menu) owns the side button.</summary>
    public static bool ClaimedByRadial(IAppSettings app, int button)
    {
        var legacy = app.Get<string>("radialMenuMouseButton", "off");
        return (button == Back && legacy == "back") || (button == Forward && legacy == "forward");
    }
}

/// <summary>
/// Hand-over between the Mouse button shortcuts settings card and the engine: while the card waits for "press an extra
/// button", the engine swallows the next input and reports it here instead of acting on it.
/// </summary>
internal static class MouseButtonCapture
{
    private static readonly object Gate = new();
    private static Action<int>? _handler;
    private static bool _allowMiddle;

    /// <summary>True while the engine subscribes to the mouse (it can see presses).</summary>
    public static volatile bool Watching;

    public static bool IsCapturing => _handler is not null;

    public static void Begin(Action<int> onCaptured, bool allowMiddle)
    {
        lock (Gate)
        {
            _handler = onCaptured;
            _allowMiddle = allowMiddle;
        }
    }

    public static void Cancel()
    {
        lock (Gate)
        {
            _handler = null;
        }
    }

    /// <summary>Engine side: offers an input; returns true when it was taken by the capture.</summary>
    public static bool Offer(int button)
    {
        Action<int>? handler;
        lock (Gate)
        {
            if (_handler is null || (button == MouseInputs.Middle && !_allowMiddle))
            {
                return false;
            }

            handler = _handler;
            _handler = null;
        }

        handler(button);
        return true;
    }
}

/// <summary>
/// "Use side buttons for Back and Forward": swallows the Back / Forward buttons and sends the window under the pointer
/// the browser Back / Forward app command (<c>WM_APPCOMMAND</c>), falling back to Alt+Left / Alt+Right when the app
/// does not answer. Apps that handle the buttons themselves (VM and remote clients, games, Firefox...) and the apps of
/// the "Apps to leave alone" list get the untouched events. A button owned by Mouse button shortcuts, the desktop drag
/// gesture or the radial menu is not touched.
/// </summary>
public sealed class SideButtonsFeature : InputFeatureBase
{
    private static readonly HashSet<string> PassThrough = new(StringComparer.OrdinalIgnoreCase)
    {
        "mstsc.exe", "vmconnect.exe", "vmware.exe", "vmware-vmx.exe", "vmplayer.exe", "virtualboxvm.exe", "vboxsdl.exe",
        "parsecd.exe", "moonlight.exe", "anydesk.exe", "teamviewer.exe", "firefox.exe", "photoshop.exe", "steam.exe",
    };

    private readonly bool[] _swallowUp = new bool[2];
    private AppRuleSet? _rules;

    public override string Id => "side-buttons";

    protected override bool IsEnabled() => App.Get<bool>(K.NavEnabled);

    protected override void Engage()
    {
        _rules = new AppRuleSet(App, K.NavExceptions);
        Array.Clear(_swallowUp);
        SubscribeMouse(OnMouse, HookPriorities.SideButtons);
    }

    protected override void Disengage()
    {
        _rules?.Dispose();
        _rules = null;
    }

    private bool OwnedElsewhere(int button)
    {
        var shortcuts = App.Get<Dictionary<string, string>?>(K.ButtonShortcuts);
        if (App.Get<bool>(K.ButtonShortcutsEnabled) && shortcuts is not null && shortcuts.ContainsKey(button.ToString()))
        {
            return true;
        }

        return (App.Get<bool>(K.SpacesGestureEnabled) && App.Get<int>(K.SpacesGestureButton) == button) || MouseInputs.ClaimedByRadial(App, button);
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsFromWinKnife || e.IsInjected || e.XButton is not (1 or 2))
        {
            return false;
        }

        var slot = e.XButton - 1;
        if (e.Kind == MouseEventKind.XButtonUp)
        {
            if (_swallowUp[slot])
            {
                _swallowUp[slot] = false;
                return true;
            }

            return false;
        }

        if (e.Kind != MouseEventKind.XButtonDown)
        {
            return false;
        }

        var button = e.XButton == 1 ? MouseInputs.Back : MouseInputs.Forward;
        if (OwnedElsewhere(button) || _rules is { IsEmpty: false } rules && rules.MatchesPoint(e.X, e.Y))
        {
            return false;
        }

        var hwnd = Win32.WindowFromPoint(new POINT { X = e.X, Y = e.Y });
        if (hwnd == 0)
        {
            return false;
        }

        var root = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
        var name = System.IO.Path.GetFileName(AppIdentity.OfWindow(root) ?? string.Empty);
        if (name.Length > 0 && PassThrough.Contains(name))
        {
            return false;
        }

        _swallowUp[slot] = true;
        var back = e.XButton == 1;
        ThreadPool.UnsafeQueueUserWorkItem(_ => Navigate(hwnd, back), null);
        return true;
    }

    private static void Navigate(nint hwnd, bool back)
    {
        try
        {
            const int command = 1; // APPCOMMAND_BROWSER_BACKWARD; 2 = FORWARD
            var appCommand = back ? command : command + 1;
            var lParam = (nint)((appCommand | 0x8000) << 16); // FAPPCOMMAND_MOUSE
            var ok = Win32.SendMessageTimeoutW(hwnd, Win32.WM_APPCOMMAND, hwnd, lParam, Win32.SMTO_ABORTIFHUNG, 250, out var result);
            if (ok == 0 || result == 0)
            {
                KeySender.SendChord([0x12, back ? (ushort)0x25 : (ushort)0x27]);
            }
        }
        catch
        {
            // nothing sensible to do
        }
    }
}

/// <summary>
/// "Use extra buttons as shortcuts" and "Switch desktops by dragging a button". Inputs: the Back / Forward side buttons
/// and the two side-wheel (tilt) directions, each mapped to a key combination stored in <c>mouseButtonShortcuts</c>
/// (button number to shortcut text). While an input has a shortcut it stops doing what it did before. The desktop
/// gesture holds the chosen button back: dragging left or right moves one virtual desktop per 220 px (0.35 s cooldown),
/// up opens Task View and down shows the desktop (150 px, once); a press without travel is replayed as an ordinary click.
/// </summary>
public sealed class MouseButtonShortcutsFeature : InputFeatureBase
{
    private const int StepX = 220;
    private const int StepY = 150;
    private const int CooldownMs = 350;
    private const int BurstGapMs = 250;

    private readonly Dictionary<int, bool> _swallowUp = [];
    private readonly uint[] _lastWheelPacket = new uint[2];
    private readonly bool[] _wheelInBurst = new bool[2];

    private volatile Dictionary<int, ushort[]> _chords = [];
    private volatile bool _shortcuts;
    private volatile bool _gesture;
    private volatile int _gestureButton;
    private volatile bool _followsDrag;
    private AppRuleSet? _rules;

    // gesture tracker (hook thread only)
    private bool _dragging;
    private bool _dragFired;
    private int _anchorX;
    private int _anchorY;
    private int _lockedAxis; // 0 none, 1 horizontal, 2 vertical
    private long _lastStepTick;

    public override string Id => "mouse-button-shortcuts";

    protected override bool IsEnabled() => App.Get<bool>(K.ButtonShortcutsEnabled) || App.Get<bool>(K.SpacesGestureEnabled);

    protected override void Engage()
    {
        _rules = new AppRuleSet(App, K.ButtonExceptions);
        _swallowUp.Clear();
        _dragging = false;
        SubscribeMouse(OnMouse, HookPriorities.MouseButtonShortcuts);
        MouseButtonCapture.Watching = true;
    }

    protected override void Disengage()
    {
        MouseButtonCapture.Watching = false;
        MouseButtonCapture.Cancel();
        if (_dragging)
        {
            _dragging = false;
        }

        _rules?.Dispose();
        _rules = null;
    }

    protected override void LoadSettings()
    {
        _shortcuts = App.Get<bool>(K.ButtonShortcutsEnabled);
        _gesture = App.Get<bool>(K.SpacesGestureEnabled);
        _gestureButton = App.Get<int>(K.SpacesGestureButton);
        _followsDrag = App.Get<bool>(K.SpacesGestureFollows);
        var map = new Dictionary<int, ushort[]>();
        foreach (var (key, text) in App.Get<Dictionary<string, string>?>(K.ButtonShortcuts) ?? [])
        {
            if (int.TryParse(key, out var button) && Hotkey.TryParse(text, out var hotkey))
            {
                map[button] = KeySender.ToChord(hotkey);
            }
        }

        _chords = map;
    }

    private static int ButtonOf(MouseHookEvent e) => e.Kind switch
    {
        MouseEventKind.MiddleDown or MouseEventKind.MiddleUp => MouseInputs.Middle,
        MouseEventKind.XButtonDown or MouseEventKind.XButtonUp => e.XButton == 1 ? MouseInputs.Back : MouseInputs.Forward,
        _ => 0,
    };

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsFromWinKnife || e.IsInjected)
        {
            return false;
        }

        switch (e.Kind)
        {
            case MouseEventKind.HorizontalWheel:
                return OnSideWheel(e);
            case MouseEventKind.Move:
                if (_dragging)
                {
                    TrackDrag(e);
                }

                return false;
            case MouseEventKind.MiddleDown or MouseEventKind.XButtonDown:
                return OnDown(e);
            case MouseEventKind.MiddleUp or MouseEventKind.XButtonUp:
                return OnUp(e);
            default:
                return false;
        }
    }

    private bool OnDown(MouseHookEvent e)
    {
        var button = ButtonOf(e);
        if (button == 0)
        {
            return false;
        }

        if (MouseButtonCapture.IsCapturing && MouseButtonCapture.Offer(button))
        {
            _swallowUp[button] = true;
            return true;
        }

        if (_rules is { IsEmpty: false } rules && rules.MatchesPoint(e.X, e.Y))
        {
            return false;
        }

        if (_shortcuts && button != MouseInputs.Middle && !MouseInputs.ClaimedByRadial(App, button) && _chords.TryGetValue(button, out var chord))
        {
            _swallowUp[button] = true;
            KeySender.SendChord(chord);
            return true;
        }

        if (_gesture && _gestureButton == button && !_dragging)
        {
            _dragging = true;
            _dragFired = false;
            _anchorX = e.X;
            _anchorY = e.Y;
            _lockedAxis = 0;
            _swallowUp[button] = true;
            return true; // held back: replayed as a click when released without travel
        }

        return false;
    }

    private bool OnUp(MouseHookEvent e)
    {
        var button = ButtonOf(e);
        if (button == 0 || !_swallowUp.TryGetValue(button, out var swallow) || !swallow)
        {
            return false;
        }

        _swallowUp[button] = false;
        if (_dragging && _gestureButton == button)
        {
            _dragging = false;
            if (!_dragFired)
            {
                var mouse = button == MouseInputs.Middle ? MouseButton.Middle : button == MouseInputs.Back ? MouseButton.XButton1 : MouseButton.XButton2;
                InputSender.SendMouseButton(mouse, true);
                InputSender.SendMouseButton(mouse, false);
            }
        }

        return true;
    }

    private void TrackDrag(MouseHookEvent e)
    {
        var dx = e.X - _anchorX;
        var dy = e.Y - _anchorY;
        var now = Environment.TickCount64;
        if (_lockedAxis != 2 && Math.Abs(dx) >= StepX && (_lockedAxis == 1 || Math.Abs(dx) >= Math.Abs(dy)))
        {
            if (now - _lastStepTick < CooldownMs)
            {
                // banked travel is capped at one step
                _anchorX = e.X - Math.Sign(dx) * StepX;
                return;
            }

            _lockedAxis = 1;
            _lastStepTick = now;
            _dragFired = true;
            var right = dx > 0;
            if (_followsDrag)
            {
                right = !right;
            }

            KeySender.SendChord([0x11, 0x5B, right ? (ushort)0x27 : (ushort)0x25]); // Ctrl+Win+Right / Left
            _anchorX = e.X - Math.Sign(dx) * Math.Max(0, Math.Min(StepX, Math.Abs(dx) - StepX));
            return;
        }

        if (_lockedAxis == 0 && Math.Abs(dy) >= StepY && Math.Abs(dy) > Math.Abs(dx))
        {
            _lockedAxis = 2;
            _dragFired = true;
            KeySender.SendChord(dy < 0 ? [0x5B, 0x09] : [0x5B, 0x44]); // Win+Tab / Win+D
        }
    }

    private bool OnSideWheel(MouseHookEvent e)
    {
        if (!_shortcuts || e.WheelDelta == 0)
        {
            return false;
        }

        // Windows reports a positive horizontal delta for "right".
        var right = e.WheelDelta > 0;
        var button = right ? MouseInputs.SideWheelRight : MouseInputs.SideWheelLeft;
        var slot = right ? 1 : 0;
        if (MouseButtonCapture.IsCapturing && MouseButtonCapture.Offer(button))
        {
            _lastWheelPacket[slot] = e.Timestamp;
            return true;
        }

        if (!_chords.TryGetValue(button, out var chord) || (_rules is { IsEmpty: false } rules && rules.MatchesPoint(e.X, e.Y)))
        {
            return false;
        }

        var gap = unchecked(e.Timestamp - _lastWheelPacket[slot]);
        _lastWheelPacket[slot] = e.Timestamp;
        if (gap > BurstGapMs)
        {
            KeySender.SendChord(chord); // only the first packet of a burst counts
        }

        return true;
    }
}
