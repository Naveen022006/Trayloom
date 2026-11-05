// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Input;

/// <summary>Which of the two protected shortcuts a section / chord belongs to.</summary>
internal enum ProtectedShortcut
{
    Quit,
    Close,
}

/// <summary>
/// "Quit &amp; close protection": Alt+F4 ("Quit", also Ctrl+Q and Ctrl+Shift+Q) and Ctrl+W ("Close", also Ctrl+F4) are
/// configured independently: confirmation by holding (250-2000 ms, default 800), by pressing twice (200-1500 ms, default
/// 600) or by adding an extra modifier; scope all apps / selected apps only / all except selected apps; optional
/// capsule HUD with a progress bar. The first press is swallowed; once confirmed the shortcut is replayed (tagged) so
/// the app sees a real Alt+F4 / Ctrl+W. Esc cancels a pending hold. Remote desktop and VM clients, where the keys belong
/// to the guest, are never touched. Shortcuts injected by other programs are not filtered.
/// </summary>
public sealed class CloseProtectionFeature : InputFeatureBase
{
    private const int ModeHold = 0, ModeDouble = 1, ModeExtra = 2;
    private const int ScopeAll = 0, ScopeSelected = 1, ScopeExcept = 2;

    private static readonly HashSet<string> PassThrough = new(StringComparer.OrdinalIgnoreCase)
    {
        "mstsc.exe", "vmconnect.exe", "vmware.exe", "vmware-vmx.exe", "vmplayer.exe", "virtualboxvm.exe", "vboxsdl.exe",
        "parsecd.exe", "moonlight.exe", "anydesk.exe", "teamviewer.exe",
    };

    private sealed class Config
    {
        public ProtectedShortcut Kind;
        public bool Enabled;
        public int Mode;
        public int HoldMs = 800;
        public int DoubleMs = 600;
        public ModifierKeys Extra = ModifierKeys.Shift;
        public int Scope;
        public bool Feedback = true;
        public AppRuleSet? Rules;
        public string Label = string.Empty;
    }

    private readonly record struct Chord(int Vk, ModifierKeys Mods, ProtectedShortcut Kind);

    private static readonly Chord[] Chords =
    [
        new(0x73, ModifierKeys.Alt, ProtectedShortcut.Quit),
        new(0x51, ModifierKeys.Control, ProtectedShortcut.Quit),
        new(0x51, ModifierKeys.Control | ModifierKeys.Shift, ProtectedShortcut.Quit),
        new(0x57, ModifierKeys.Control, ProtectedShortcut.Close),
        new(0x73, ModifierKeys.Control, ProtectedShortcut.Close),
    ];

    private readonly object _gate = new();
    private readonly bool[] _swallowUp = new bool[256];
    private readonly bool[] _passing = new bool[256];
    private readonly long[] _lastPress = new long[256];
    private readonly uint[] _lastPressStamp = new uint[256];
    private Config[] _config = [new() { Kind = ProtectedShortcut.Quit, Label = "Alt+F4" }, new() { Kind = ProtectedShortcut.Close, Label = "Ctrl+W" }];
    private System.Threading.Timer? _timer;
    private HudWindow? _hud;

    // pending hold (guarded by _gate)
    private Chord? _pendingChord;
    private Config? _pendingConfig;
    private bool _pendingFired;
    private long _pendingDeadline;

    public override string Id => "close-protection";

    protected override bool IsEnabled() => App.Get<bool>(K.Quit("Enabled")) || App.Get<bool>(K.Close("Enabled"));

    protected override void Engage()
    {
        _timer = new System.Threading.Timer(_ => OnHoldElapsed());
        _config[0].Rules = new AppRuleSet(App, K.Quit(Loc.T("Exceptions")));
        _config[1].Rules = new AppRuleSet(App, K.Close(Loc.T("Exceptions")));
        SubscribeKeyboard(OnKey, HookPriorities.CloseProtection);
    }

    protected override void Disengage()
    {
        _timer?.Dispose();
        _timer = null;
        lock (_gate)
        {
            _pendingChord = null;
            _pendingConfig = null;
            Array.Clear(_swallowUp);
            Array.Clear(_passing);
        }

        foreach (var config in _config)
        {
            config.Rules?.Dispose();
            config.Rules = null;
        }

        var hud = _hud;
        _hud = null;
        if (hud is not null)
        {
            Context?.Ui.BeginInvoke(hud.Close);
        }
    }

    protected override void LoadSettings()
    {
        Read(_config[0], K.Quit);
        Read(_config[1], K.Close);
        _config[0].Label = "Alt+F4";
        _config[1].Label = "Ctrl+W";
    }

    private void Read(Config c, Func<string, string> key)
    {
        c.Enabled = App.Get<bool>(key("Enabled"));
        c.Mode = App.Get<string>(key("Mode")) switch { "doublePress" => ModeDouble, "extraModifier" => ModeExtra, _ => ModeHold };
        c.HoldMs = (int)Math.Clamp(App.Get<double>(key("HoldDurationMs")), 250, 2000);
        c.DoubleMs = (int)Math.Clamp(App.Get<double>(key("DoubleIntervalMs")), 200, 1500);
        c.Extra = App.Get<string>(key("ExtraModifier")) switch
        {
            "option" => ModifierKeys.Alt,
            "control" => ModifierKeys.Control,
            "command" => ModifierKeys.Windows,
            _ => ModifierKeys.Shift,
        };
        var baseMod = c.Kind == ProtectedShortcut.Quit ? ModifierKeys.Alt : ModifierKeys.Control;
        if (c.Extra == baseMod)
        {
            c.Extra = ModifierKeys.Shift;
        }

        c.Scope = App.Get<string>(key("Scope")) switch { "selectedOnly" => ScopeSelected, "allExceptSelected" => ScopeExcept, _ => ScopeAll };
        c.Feedback = App.Get<bool>(key("ShowFeedback"));
    }

    internal static string ExtraText(ProtectedShortcut kind, ModifierKeys extra)
    {
        var baseMods = kind == ProtectedShortcut.Quit ? ModifierKeys.Alt : ModifierKeys.Control;
        var key = kind == ProtectedShortcut.Quit ? "F4" : "W";
        var all = baseMods | extra;
        var parts = new List<string>(4);
        if (all.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (all.HasFlag(ModifierKeys.Shift))
        {
            parts.Add(Loc.T("Shift"));
        }

        if (all.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (all.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(key);
        return string.Join("+", parts);
    }

    // ---------------------------------------------------------------------------------------------- hook thread

    private bool OnKey(KeyboardHookEvent e)
    {
        if (e.IsFromTrayloom || e.IsInjected)
        {
            return false;
        }

        var vk = e.VkCode;
        if ((uint)vk > 255)
        {
            return false;
        }

        if (e.IsKeyUp)
        {
            lock (_gate)
            {
                _passing[vk] = false;
                if (_swallowUp[vk])
                {
                    _swallowUp[vk] = false;
                    if (_pendingChord is { } chord && chord.Vk == vk)
                    {
                        var fired = _pendingFired;
                        EndPending();
                        if (!fired)
                        {
                            HideHud();
                        }
                    }

                    return true;
                }
            }

            return false;
        }

        if (vk == 0x1B)
        {
            lock (_gate)
            {
                if (_pendingChord is not null && !_pendingFired)
                {
                    EndPending(); // Esc cancels (the key-up of the held shortcut is still swallowed)
                    HideHud();
                }
            }

            return false;
        }

        lock (_gate)
        {
            if (_swallowUp[vk])
            {
                return true; // auto-repeat of a press we are holding back
            }

            if (_passing[vk])
            {
                return false;
            }
        }

        if (!TryMatch(e, out var matched, out var config, out var stripExtra))
        {
            return false;
        }

        if (!InScope(config))
        {
            return false;
        }

        switch (config.Mode)
        {
            case ModeHold:
                return StartHold(vk, matched, config, e);
            case ModeDouble:
                return PressTwice(e, vk, matched, config);
            default:
                return ExtraModifier(vk, matched, config, stripExtra);
        }
    }

    private bool TryMatch(KeyboardHookEvent e, out Chord chord, out Config config, out bool stripExtra)
    {
        stripExtra = false;
        foreach (var candidate in Chords)
        {
            if (candidate.Vk != e.VkCode)
            {
                continue;
            }

            var c = _config[(int)candidate.Kind];
            if (!c.Enabled)
            {
                continue;
            }

            // The chord with the extra modifier added confirms (extra-modifier mode).
            if (c.Mode == ModeExtra && e.Modifiers == (candidate.Mods | c.Extra) && candidate.Mods != (candidate.Mods | c.Extra))
            {
                chord = candidate;
                config = c;
                stripExtra = true;
                return true;
            }
        }

        foreach (var candidate in Chords)
        {
            if (candidate.Vk == e.VkCode && e.Modifiers == candidate.Mods && _config[(int)candidate.Kind].Enabled)
            {
                chord = candidate;
                config = _config[(int)candidate.Kind];
                return true;
            }
        }

        chord = default;
        config = _config[0];
        return false;
    }

    private bool InScope(Config c)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var path = AppIdentity.OfWindow(foreground);
        if (path is not null && PassThrough.Contains(System.IO.Path.GetFileName(path)))
        {
            return false;
        }

        var listed = c.Rules is not null && !c.Rules.IsEmpty && c.Rules.MatchesWindow(foreground);
        return c.Scope switch
        {
            ScopeSelected => listed,
            ScopeExcept => !listed,
            _ => true,
        };
    }

    private bool StartHold(int vk, Chord chord, Config config, KeyboardHookEvent e)
    {
        lock (_gate)
        {
            _swallowUp[vk] = true;
            _pendingChord = chord;
            _pendingConfig = config;
            _pendingFired = false;
            _pendingDeadline = Environment.TickCount64 + config.HoldMs;
            _timer?.Change(config.HoldMs, Timeout.Infinite);
        }

        KeepMenuClosed(chord);
        if (config.Feedback)
        {
            ShowHud(string.Format(chord.Kind == ProtectedShortcut.Quit ? Strings.HoldToQuit : Strings.HoldToClose, config.Label), config.HoldMs, config.HoldMs + 400);
        }

        return true;
    }

    private bool PressTwice(KeyboardHookEvent e, int vk, Chord chord, Config config)
    {
        var now = e.Timestamp;
        lock (_gate)
        {
            if (_lastPress[vk] != 0 && unchecked(now - _lastPressStamp[vk]) <= (uint)config.DoubleMs)
            {
                _lastPress[vk] = 0;
                _passing[vk] = true;
                HideHud();
                return false; // the second press goes through like a normal shortcut
            }

            _lastPress[vk] = 1;
            _lastPressStamp[vk] = now;
            _swallowUp[vk] = true;
        }

        KeepMenuClosed(chord);
        if (config.Feedback)
        {
            ShowHud(string.Format(chord.Kind == ProtectedShortcut.Quit ? Strings.PressAgainToQuit : Strings.PressAgainToClose, config.Label), 0, config.DoubleMs + 300);
        }

        return true;
    }

    private bool ExtraModifier(int vk, Chord chord, Config config, bool stripExtra)
    {
        if (stripExtra)
        {
            // Confirmed: replay the base shortcut without the extra modifier (one atomic burst).
            var extra = config.Extra switch
            {
                ModifierKeys.Alt => (ushort)0x12,
                ModifierKeys.Control => (ushort)0x11,
                ModifierKeys.Windows => (ushort)0x5B,
                _ => (ushort)0x10,
            };
            lock (_gate)
            {
                _swallowUp[vk] = true;
            }

            // The user may have released the extra modifier meanwhile: pressing it again would leave it stuck down.
            var extraStillHeld = (NativeMethods.GetAsyncKeyState(extra) & 0x8000) != 0;
            var replay = new List<INPUT>(4)
            {
                KeySender.KeyInput(extra, true),
                KeySender.KeyInput((ushort)vk, false),
                KeySender.KeyInput((ushort)vk, true),
            };
            if (extraStillHeld)
            {
                replay.Add(KeySender.KeyInput(extra, false));
            }

            InputSender.Send([.. replay]);
            HideHud();
            return true;
        }

        lock (_gate)
        {
            _swallowUp[vk] = true;
        }

        KeepMenuClosed(chord);
        if (config.Feedback)
        {
            ShowHud(string.Format(chord.Kind == ProtectedShortcut.Quit ? Strings.UseToQuit : Strings.UseToClose, ExtraText(chord.Kind, config.Extra)), 0, 1800);
        }

        return true;
    }

    // Without this the lone Alt release after a swallowed Alt+F4 would highlight the menu bar of the app.
    private static void KeepMenuClosed(Chord chord)
    {
        if (chord.Mods.HasFlag(ModifierKeys.Alt))
        {
            KeySender.Tap(Win32.VK_UNASSIGNED);
        }
    }

    // Caller holds _gate.
    private void EndPending()
    {
        _pendingChord = null;
        _pendingConfig = null;
        _pendingFired = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    // ---------------------------------------------------------------------------------------------- timer thread

    private void OnHoldElapsed()
    {
        Chord chord;
        lock (_gate)
        {
            if (_pendingChord is not { } pending || _pendingFired || _pendingConfig is null)
            {
                return;
            }

            chord = pending;
            _pendingFired = true;
        }

        // The modifiers are still physically down; sending the main key completes the shortcut.
        var modifierDown = true;
        if (chord.Mods.HasFlag(ModifierKeys.Alt))
        {
            modifierDown &= NativeMethods.GetAsyncKeyState(0x12) < 0;
        }

        if (chord.Mods.HasFlag(ModifierKeys.Control))
        {
            modifierDown &= NativeMethods.GetAsyncKeyState(0x11) < 0;
        }

        if (modifierDown)
        {
            KeySender.Tap((ushort)chord.Vk);
        }

        HideHud();
    }

    // ---------------------------------------------------------------------------------------------- HUD

    private void ShowHud(string title, int holdMs, int visibleMs)
    {
        var context = Context;
        context?.Ui.BeginInvoke(() =>
        {
            if (Context is null)
            {
                return;
            }

            _hud ??= new HudWindow();
            _hud.Present(title, Strings.EscCancels, holdMs, visibleMs);
        });
    }

    private void HideHud() => Context?.Ui.BeginInvoke(() => _hud?.HideNow());

    /// <summary>The capsule HUD (min 300 x 48, +8 with a progress bar): title, "Esc cancels" and a linear progress bar.</summary>
    internal sealed class HudWindow : OverlayWindow
    {
        private readonly TextBlock _title;
        private readonly TextBlock _detail;
        private readonly Border _fill;
        private readonly Border _track;
        private readonly Border _capsule;
        private readonly ScaleTransform _scale = new(0, 1);
        private readonly System.Windows.Threading.DispatcherTimer _hideTimer = new();

        public HudWindow()
        {
            ClickThrough = true;
            _title = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = UiKit.TextFont };
            _detail = new TextBlock { FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromArgb(173, 255, 255, 255)), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = UiKit.TextFont, Margin = new Thickness(0, 1, 0, 0) };
            _fill = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)), RenderTransform = _scale, RenderTransformOrigin = new Point(0, 0.5) };
            _track = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromArgb(41, 255, 255, 255)), Margin = new Thickness(24, 5, 24, 0), Child = _fill };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_title);
            stack.Children.Add(_detail);
            stack.Children.Add(_track);

            _capsule = new Border
            {
                MinWidth = 300,
                Height = 56,
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(28),
                Background = new SolidColorBrush(Color.FromArgb(242, 23, 23, 23)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(41, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = stack,
            };
            Content = _capsule;
            SizeToContent = SizeToContent.WidthAndHeight;
            _hideTimer.Tick += (_, _) => HideNow();
        }

        public void Present(string title, string detail, int holdMs, int visibleMs)
        {
            _title.Text = title;
            _detail.Text = detail;
            var bar = holdMs > 0;
            _track.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
            _capsule.Height = bar ? 56 : 48;
            _capsule.CornerRadius = new CornerRadius(_capsule.Height / 2);
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.ScaleX = 0;
            if (bar)
            {
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(holdMs)));
            }

            Place();
            if (!IsVisible)
            {
                ShowNoActivate();
            }

            Place();
            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, visibleMs));
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
            var cursor = Screens.CursorPosition();
            var area = Screens.FromPoint(cursor);
            var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)cursor.X, Y = (int)cursor.Y }, NativeConstants.MONITOR_DEFAULTTONEAREST);
            var scale = Win32.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
            var size = DesiredSize;
            var width = (int)Math.Ceiling(size.Width * scale);
            var height = (int)Math.Ceiling(size.Height * scale);
            var x = (int)(area.WorkArea.X + ((area.WorkArea.Width - width) / 2));
            var y = (int)(area.WorkArea.Bottom - height - (18 * scale));
            SetPhysicalBounds(x, y, width, height);
        }
    }
}
