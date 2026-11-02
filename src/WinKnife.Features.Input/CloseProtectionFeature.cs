// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// Protects against closing windows by accident: Alt+F4, Ctrl+W and Ctrl+Q (Ctrl+Shift+W/Q too) are held back until
/// you either keep them pressed for the hold time (about half a second) or press them twice quickly. A small hint at
/// the bottom of the screen shows the progress. Each shortcut can be switched off, and apps can be excluded.
/// The key is swallowed on the first press; once the hold time has passed the same key is injected, so the app sees
/// a normal shortcut. Shortcuts injected by other programs are not filtered.
/// </summary>
public sealed class CloseProtectionFeature : InputFeatureBase
{
    private const int AltF4 = 0, CtrlW = 1, CtrlQ = 2;
    private static readonly ushort[] ComboKeys = [0x73, 0x57, 0x51]; // F4, W, Q
    private static readonly string[] ComboNames = ["Alt+F4", "Ctrl+W", "Ctrl+Q"];

    private readonly object _gate = new();
    private readonly long[] _lastPress = new long[3];
    private readonly long[] _lastEvent = new long[3];
    private readonly bool[] _held = new bool[3];
    private readonly bool[] _passing = new bool[3];
    private readonly bool[] _fired = new bool[3];
    private readonly System.Threading.Timer?[] _timers = new System.Threading.Timer?[3];

    private volatile int _holdMs = 500;
    private volatile int _doubleMs = 600;
    private volatile int _mode; // 0 either, 1 hold only, 2 double press only
    private volatile bool[] _enabled = [true, true, true];
    private volatile string[] _excluded = [];
    private volatile bool _showHint = true;
    private HintOverlay? _overlay;

    public override string Id => "close-protection";

    public override string Name => "Close protection";

    public override string Description => "Hold or double-press Alt+F4, Ctrl+W and Ctrl+Q so a slip of the finger can't close your work.";

    protected override void OnStart()
    {
        for (var i = 0; i < 3; i++)
        {
            var combo = i;
            _timers[i] = new System.Threading.Timer(_ => OnHoldElapsed(combo));
        }

        SubscribeKeyboard(OnKey, HookPriorities.CloseProtection);
    }

    protected override void OnStop()
    {
        foreach (var timer in _timers)
        {
            timer?.Dispose();
        }

        Array.Clear(_timers);
        lock (_gate)
        {
            Array.Clear(_held);
            Array.Clear(_passing);
            Array.Clear(_fired);
        }

        var overlay = _overlay;
        _overlay = null;
        if (overlay is not null)
        {
            Context?.Ui.BeginInvoke(overlay.Close);
        }
    }

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        var s = Context.Settings;
        _holdMs = Math.Clamp(s.Get("holdMs", 500), 200, 3000);
        _doubleMs = Math.Clamp(s.Get("doubleMs", 600), 200, 2000);
        _mode = s.Get("mode", "either") switch { "hold" => 1, "double" => 2, _ => 0 };
        _enabled = [s.Get("altF4", true), s.Get("ctrlW", true), s.Get("ctrlQ", true)];
        _showHint = s.Get("showHint", true);
        _excluded = s.Get("excludedApps", string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddChoice("mode", "To confirm",
            [new Choice<string>("either", "Hold or press twice"), new Choice<string>("hold", "Hold only"), new Choice<string>("double", "Press twice only")],
            "either")
        .AddIntSlider("holdMs", "Hold time (ms)", 200, 3000, 500)
        .AddIntSlider("doubleMs", "Double press window (ms)", 200, 2000, 600)
        .AddToggle("altF4", "Protect Alt+F4", true)
        .AddToggle("ctrlW", "Protect Ctrl+W", true, "Closes tabs and documents in browsers and editors.")
        .AddToggle("ctrlQ", "Protect Ctrl+Q", true)
        .AddToggle("showHint", "Show a hint while confirming", true)
        .AddText("excludedApps", "Don't protect these apps", string.Empty,
            "Comma-separated program names, e.g. WindowsTerminal.exe, Code.exe (Ctrl+W means something else in terminals).");

    // Hook thread.
    private bool OnKey(KeyboardHookEvent e)
    {
        if (e.IsFromWinKnife || e.IsInjected)
        {
            return false;
        }

        var combo = Array.IndexOf(ComboKeys, (ushort)e.VkCode);
        if (combo < 0)
        {
            return false;
        }

        if (e.IsKeyUp)
        {
            return OnKeyUp(combo);
        }

        var now = Environment.TickCount64;
        lock (_gate)
        {
            // Auto-repeat of a press we already decided on.
            if (_passing[combo] || _held[combo])
            {
                if (now - _lastEvent[combo] < 1500)
                {
                    _lastEvent[combo] = now;
                    return _held[combo];
                }

                // Stale state (a key-up got lost): start over.
                _passing[combo] = false;
                _held[combo] = false;
            }
        }

        if (Paused || !IsProtectedChord(combo, e.Modifiers) || IsExcludedApp())
        {
            return false;
        }

        lock (_gate)
        {
            _lastEvent[combo] = now;
            var window = _doubleMs;
            if (_mode != 1 && _lastPress[combo] != 0 && now - _lastPress[combo] <= window)
            {
                // Second press in time: let it through like a normal shortcut.
                _lastPress[combo] = 0;
                _passing[combo] = true;
                HideHint();
                return false;
            }

            _lastPress[combo] = now;
            _held[combo] = true;
            _fired[combo] = false;
            if (_mode != 2)
            {
                _timers[combo]?.Change(_holdMs, Timeout.Infinite);
            }
        }

        if (combo == AltF4)
        {
            // Without this the lone Alt release would highlight the menu bar of the app.
            KeySender.Tap(Win32.VK_UNASSIGNED);
        }

        ShowHint(combo);
        return true;
    }

    private bool OnKeyUp(int combo)
    {
        lock (_gate)
        {
            if (_passing[combo])
            {
                _passing[combo] = false;
                return false;
            }

            if (_held[combo])
            {
                _held[combo] = false;
                _timers[combo]?.Change(Timeout.Infinite, Timeout.Infinite);
                if (!_fired[combo] && _mode == 1)
                {
                    _lastPress[combo] = 0; // hold-only: a short press never counts as a double press
                }

                if (!_fired[combo])
                {
                    ScheduleHintHide(1200);
                }

                return true;
            }
        }

        return false;
    }

    // Timer thread.
    private void OnHoldElapsed(int combo)
    {
        var fire = false;
        lock (_gate)
        {
            if (_held[combo] && !_fired[combo])
            {
                _fired[combo] = true;
                fire = true;
            }
        }

        if (!fire)
        {
            return;
        }

        // The modifier is still physically down; injecting the main key completes the shortcut.
        var modifier = combo == AltF4 ? 0x12 : 0x11;
        if (NativeMethods.GetAsyncKeyState(modifier) < 0)
        {
            KeySender.Tap(ComboKeys[combo]);
        }

        HideHint();
    }

    private static bool IsProtectedChord(int combo, System.Windows.Input.ModifierKeys modifiers)
    {
        const System.Windows.Input.ModifierKeys alt = System.Windows.Input.ModifierKeys.Alt;
        const System.Windows.Input.ModifierKeys ctrl = System.Windows.Input.ModifierKeys.Control;
        const System.Windows.Input.ModifierKeys shift = System.Windows.Input.ModifierKeys.Shift;
        var rest = modifiers & ~shift;
        return combo == AltF4 ? rest == alt : rest == ctrl;
    }

    private bool IsExcludedApp()
    {
        var excluded = _excluded;
        if (excluded.Length == 0)
        {
            return false;
        }

        var name = Win32.ProcessFileName(NativeMethods.GetForegroundWindow());
        return name is not null && excluded.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowHint(int combo)
    {
        if (!_showHint || Context is null)
        {
            return;
        }

        var text = _mode switch
        {
            1 => $"Keep holding {ComboNames[combo]} to confirm",
            2 => $"Press {ComboNames[combo]} again to confirm",
            _ => $"Hold {ComboNames[combo]} or press it again",
        };
        var holdMs = _mode == 2 ? 0 : _holdMs;
        var visibleMs = Math.Max(holdMs, _mode == 1 ? 0 : _doubleMs) + 300;
        Context.Ui.BeginInvoke(() =>
        {
            if (Context is null)
            {
                return;
            }

            _overlay ??= new HintOverlay();
            _overlay.ShowHint(text, holdMs, visibleMs);
        });
    }

    private void ScheduleHintHide(int afterMs) => Context?.Ui.BeginInvoke(() => _overlay?.HideAfter(afterMs));

    private void HideHint() => Context?.Ui.BeginInvoke(() => _overlay?.HideNow());

    private sealed class HintOverlay : OverlayWindow
    {
        private readonly TextBlock _text;
        private readonly Border _fill;
        private readonly ScaleTransform _scale = new(0, 1);
        private readonly DispatcherTimer _hideTimer = new();

        public HintOverlay()
        {
            ClickThrough = true;
            _text = new TextBlock { FontSize = 14, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _text.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.OverlayText);

            _fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), RenderTransform = _scale, RenderTransformOrigin = new System.Windows.Point(0, 0.5) };
            _fill.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);
            var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(70, 255, 255, 255)), Margin = new Thickness(0, 10, 0, 0), Child = _fill };

            var panel = new StackPanel();
            panel.Children.Add(_text);
            panel.Children.Add(track);

            var card = new Border { Width = 320, Padding = new Thickness(18, 12, 18, 14), CornerRadius = new CornerRadius(12), Child = panel };
            card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.OverlayBackground);
            Content = card;
            SizeToContent = SizeToContent.WidthAndHeight;

            _hideTimer.Tick += (_, _) => HideNow();
        }

        public void ShowHint(string text, int holdMs, int visibleMs)
        {
            _text.Text = text;
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.ScaleX = 0;
            if (holdMs > 0)
            {
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(holdMs)));
            }

            Place();
            if (!IsVisible)
            {
                ShowNoActivate();
            }

            Place();
            HideAfter(visibleMs);
        }

        public void HideAfter(int milliseconds)
        {
            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, milliseconds));
            _hideTimer.Start();
        }

        public void HideNow()
        {
            _hideTimer.Stop();
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            if (IsVisible)
            {
                Hide();
            }
        }

        private void Place()
        {
            UpdateLayout();
            var area = Screens.UnderCursor();
            var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)Screens.CursorPosition().X, Y = (int)Screens.CursorPosition().Y }, NativeConstants.MONITOR_DEFAULTTONEAREST);
            var scale = Win32.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
            var size = DesiredSize;
            var width = (int)Math.Ceiling(size.Width * scale);
            var height = (int)Math.Ceiling(size.Height * scale);
            var x = (int)(area.WorkArea.X + ((area.WorkArea.Width - width) / 2));
            var y = (int)(area.WorkArea.Bottom - height - (56 * scale));
            SetPhysicalBounds(x, y, width, height);
        }
    }
}
