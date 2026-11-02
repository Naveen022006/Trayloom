// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>What the settings page needs from the running feature.</summary>
internal interface IRadialMenuService
{
    /// <summary>True when a profile hotkey could not be registered.</summary>
    bool RegistrationFailed { get; }

    /// <summary>True while the mouse hook can see extra buttons.</summary>
    bool WatchingMouse { get; }

    /// <summary>The last extra mouse button pressed while the test row was on screen (back, forward) or null.</summary>
    string? LastMouseButtonSeen { get; }

    /// <summary>Raised on the UI thread when an extra button was seen.</summary>
    event Action? ButtonSeen;

    /// <summary>Opens the wheel of a profile without a trigger (works while the feature is off).</summary>
    void TryIt(RadialProfile profile);

    /// <summary>The settings page shows the button test row: report presses (and keep the hook alive).</summary>
    void SetButtonTest(bool active);
}

/// <summary>
/// Radial menu: a wheel of up to 12 actions around the pointer, opened by a per-profile hotkey or side mouse button
/// (spec 04-tools section 3). Hold to select, or press to keep it open.
/// </summary>
public sealed class RadialMenuFeature : ToolsFeatureBase, IRadialMenuService
{
    private readonly List<IDisposable> _hotkeys = [];
    private readonly HashSet<string> _failed = [];
    private readonly bool[] _swallowed = new bool[256];
    private List<RadialProfile> _profiles = [];
    private RadialWheelWindow? _window;
    private RadialNowPlayingService? _nowPlaying;
    private IDisposable? _mouseSub;
    private IDisposable? _keySub;
    private OutsideClickMonitor? _outside;
    private ForegroundWatcher? _foreground;
    private DispatcherTimer? _tick;
    private int _buttonTests;
    private volatile int _claimBack = -1;
    private volatile int _claimForward = -1;
    private volatile bool _sessionActive;

    // session
    private RadialProfile? _profile;
    private List<List<RadialItem>> _stack = [];
    private List<string> _trail = [];
    private int? _highlight;
    private bool _pointerActivated;
    private Point _openPointer;
    private Point _lastCursor;
    private Rect _frame;
    private Point _center;
    private double _scale = 1;
    private bool _holdPhase;
    private string? _heldButton;
    private Hotkey? _heldHotkey;
    private string _mode = "pressOrHold";
    private bool _light = true;

    /// <inheritdoc />
    public override string Id => "radial-menu";

    /// <inheritdoc />
    public bool RegistrationFailed => _failed.Count > 0;

    /// <inheritdoc />
    public bool WatchingMouse => _mouseSub is not null;

    /// <inheritdoc />
    public string? LastMouseButtonSeen { get; private set; }

    /// <inheritdoc />
    public event Action? ButtonSeen;

    /// <inheritdoc />
    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("try", Loc.T("Radial menu"), () => Safe(() => TryItCore(_profiles.FirstOrDefault() ?? RadialModel.Seed(Ctx))), ""),
    ];

    /// <inheritdoc />
    protected override void RegisterDefaults(IAppSettings app)
    {
        ToolsDefaults.Register(app);
        app.Register("radialMenuEnabled", false);
        app.Register("radialMenuAtPointer", true);
        app.Register("radialMenuActivationMode", "pressOrHold");
        app.Register("radialMenuProfiles", "");
        app.Register("panelControlRadialMenu", true);
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        _window = new RadialWheelWindow();
        _window.Clicked += OnWheelClicked;
        _nowPlaying = new RadialNowPlayingService(Ctx);
        _nowPlaying.Changed += RefreshContent;
        Ctx.Services.Provide<IRadialMenuService>(this);
        Sync();
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        Ctx.Services.Withdraw<IRadialMenuService>(this);
        EndSessionNow();
        DisposeHotkeys();
        _mouseSub?.Dispose();
        _mouseSub = null;
        _nowPlaying?.Dismiss();
        _nowPlaying = null;
        _window?.Close();
        _window = null;
    }

    /// <inheritdoc />
    protected override void OnSettingChanged(string key)
    {
        if (key is "radialMenuEnabled" or RadialModel.StoreKey or "radialMenuActivationMode")
        {
            Sync();
        }
    }

    private bool Enabled => App.Get<bool>("radialMenuEnabled");

    // ------------------------------------------------------------------ registration

    private void Sync()
    {
        _profiles = RadialModel.Load(Ctx);
        DisposeHotkeys();
        _failed.Clear();
        if (Enabled)
        {
            foreach (var profile in _profiles)
            {
                if (!Hotkey.TryParse(profile.Shortcut, out var hotkey))
                {
                    continue;
                }

                var captured = profile;
                var candidate = hotkey;
                if (HotkeyMap.IsReserved(candidate) && HotkeyMap.Fallback(candidate) is { } fallback)
                {
                    candidate = fallback;
                }

                if (!Ctx.Hotkeys.TryRegister(candidate, () => Safe(() => OnHotkey(captured)), out var registration))
                {
                    if (HotkeyMap.Fallback(candidate) is { } second && Ctx.Hotkeys.TryRegister(second, () => Safe(() => OnHotkey(captured)), out registration))
                    {
                        _hotkeys.Add(registration);
                        continue;
                    }

                    _failed.Add(profile.Id);
                    continue;
                }

                _hotkeys.Add(registration);
            }
        }

        _claimBack = Enabled ? _profiles.FindIndex(p => p.MouseButton == "back") : -1;
        _claimForward = Enabled ? _profiles.FindIndex(p => p.MouseButton == "forward") : -1;
        SyncMouseHook();
        if (!Enabled && _sessionActive && _profile is not null && _heldButton is not null)
        {
            EndSession();
        }
    }

    private void DisposeHotkeys()
    {
        foreach (var h in _hotkeys)
        {
            h.Dispose();
        }

        _hotkeys.Clear();
    }

    private void SyncMouseHook()
    {
        var need = _claimBack >= 0 || _claimForward >= 0 || _buttonTests > 0;
        if (need && _mouseSub is null)
        {
            _mouseSub = Ctx.Hooks.SubscribeMouse(OnMouse, 100);
        }
        else if (!need && _mouseSub is not null)
        {
            _mouseSub.Dispose();
            _mouseSub = null;
        }
    }

    /// <inheritdoc />
    public void SetButtonTest(bool active)
    {
        if (active)
        {
            _buttonTests++;
            LastMouseButtonSeen = null;
        }
        else
        {
            _buttonTests = Math.Max(0, _buttonTests - 1);
            LastMouseButtonSeen = null;
        }

        SyncMouseHook();
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.Kind is not (MouseEventKind.XButtonDown or MouseEventKind.XButtonUp) || e.XButton == 0 || e.IsFromWinKnife)
        {
            return false;
        }

        var which = e.XButton == 1 ? "back" : "forward";
        var claim = e.XButton == 1 ? _claimBack : _claimForward;
        var down = e.Kind == MouseEventKind.XButtonDown;
        Ctx.Ui.BeginInvoke(() =>
        {
            if (!IsRunning)
            {
                return;
            }

            if (down && _buttonTests > 0)
            {
                LastMouseButtonSeen = which;
                ButtonSeen?.Invoke();
            }

            if (claim >= 0)
            {
                if (down)
                {
                    OnButtonDown(which, claim);
                }
                else
                {
                    OnButtonUp(which);
                }
            }
        });
        return claim >= 0;
    }

    // ------------------------------------------------------------------ triggers

    private void OnHotkey(RadialProfile profile)
    {
        if (_sessionActive)
        {
            var same = _profile?.Id == profile.Id;
            EndSessionNow();
            if (same)
            {
                return;
            }
        }

        BeginSession(profile, requestedHold: true, heldButton: null);
    }

    private void OnButtonDown(string which, int profileIndex)
    {
        if (profileIndex >= _profiles.Count)
        {
            return;
        }

        if (_sessionActive)
        {
            if (!_holdPhase)
            {
                EndSession();
            }

            return;
        }

        BeginSession(_profiles[profileIndex], requestedHold: false, heldButton: which);
    }

    private void OnButtonUp(string which)
    {
        if (_sessionActive && _holdPhase && _heldButton == which)
        {
            HandleHoldReleased();
        }
    }

    void IRadialMenuService.TryIt(RadialProfile profile) => TryItCore(profile);

    private void TryItCore(RadialProfile profile)
    {
        EndSessionNow();
        BeginSession(profile, requestedHold: false, heldButton: null);
    }

    // ------------------------------------------------------------------ session

    private void BeginSession(RadialProfile profile, bool requestedHold, string? heldButton)
    {
        if (_window is null)
        {
            return;
        }

        var items = AvailableItems(profile.Items);
        if (items.Count == 0)
        {
            SystemSounds.Beep.Play();
            return;
        }

        _light = ThemeManager.IsLight;
        _profile = profile;
        _nowPlaying?.Dismiss();
        if (ContainsNowPlaying(items))
        {
            _nowPlaying?.Refresh();
        }
        else
        {
            _nowPlaying?.Reset();
        }

        _stack = [items];
        _trail = [];
        _highlight = null;
        _pointerActivated = false;
        var cursor = Screens.CursorPosition();
        _openPointer = cursor;
        _lastCursor = cursor;
        var modeValue = App.Get<string>("radialMenuActivationMode", "pressOrHold");
        _mode = modeValue is "press" or "hold" ? modeValue : "pressOrHold";
        _heldHotkey = Hotkey.TryParse(profile.Shortcut, out var hk) ? hk : null;
        var hasModifiers = _heldHotkey is not null && _heldHotkey.Modifiers != HotkeyModifiers.None;
        _heldButton = heldButton;
        _holdPhase = _mode != "press" && (heldButton is not null || (requestedHold && hasModifiers));

        Place(cursor);
        _window.ResetFace();
        RefreshContent(bloom: true);
        _window.Open();
        UpdateHub();
        _sessionActive = true;
        Array.Clear(_swallowed);
        _keySub = Ctx.Hooks.SubscribeKeyboard(OnKey, 1000);
        _outside = new OutsideClickMonitor(Ctx.Hooks, Ctx.Ui, () => _frame, () => EndSession(), () => _sessionActive);
        _foreground = new ForegroundWatcher(Ctx.Ui);
        _foreground.ForegroundChanged += OnForeground;
        _tick = new DispatcherTimer(DispatcherPriority.Render, Ctx.Ui) { Interval = TimeSpan.FromMilliseconds(16) };
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();
    }

    private void Place(Point cursor)
    {
        var area = Screens.FromPoint(cursor).WorkArea;
        var scale = RadialNative.ScaleAt(cursor.X, cursor.Y);
        var panel = RadialGeometry.PanelSize * scale;
        var wanted = App.Get<bool>("radialMenuAtPointer", true) ? cursor : new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
        var cx = wanted.X;
        var cy = wanted.Y;
        if (area.Width > panel && area.Height > panel)
        {
            cx = Math.Clamp(cx, area.Left + panel / 2, area.Right - panel / 2);
            cy = Math.Clamp(cy, area.Top + panel / 2, area.Bottom - panel / 2);
        }

        _center = new Point(cx, cy);
        _scale = scale;
        _frame = new Rect(cx - panel / 2, cy - panel / 2, panel, panel);
        Plat.PlaceWindow(_window!, _frame.Left, _frame.Top, RadialGeometry.PanelSize, RadialGeometry.PanelSize);
        var dpi = VisualTreeHelper.GetDpi(_window!).DpiScaleX;
        if (Math.Abs(dpi - scale) > 0.01)
        {
            _scale = dpi;
            panel = RadialGeometry.PanelSize * dpi;
            _frame = new Rect(cx - panel / 2, cy - panel / 2, panel, panel);
            Plat.PlaceWindow(_window!, _frame.Left, _frame.Top, RadialGeometry.PanelSize, RadialGeometry.PanelSize);
        }
    }

    private List<RadialItem> Current => _stack.Count > 0 ? _stack[^1] : [];

    private void RefreshContent() => RefreshContent(bloom: false);

    private void RefreshContent(bool bloom)
    {
        if (_window is null || _profile is null)
        {
            return;
        }

        var color = RadialColors.Resolve(_profile.Color, _light);
        var np = _nowPlaying;
        _window.SetItems(Current, color, item => MakeChipIcon(item, np), bloom);
        _window.SetHighlight(_highlight);
    }

    private FrameworkElement MakeChipIcon(RadialItem item, RadialNowPlayingService? np)
    {
        ImageSource? special = null;
        if (item.Kind == RadialKinds.Media && item.Payload == "nowPlaying" && item.SymbolName.Length == 0 && np is { State: RadialNowPlayingState.Playing, Snapshot: { } snap })
        {
            special = Plat.ShellImage(@"shell:AppsFolder\" + snap.AppId, 34);
        }

        return RadialWheelWindow.MakeIcon(item, _light, special);
    }

    private void UpdateHub()
    {
        if (_window is null)
        {
            return;
        }

        if (_highlight is { } h && h < Current.Count)
        {
            var item = Current[h];
            var name = RadialModel.DisplayName(item, _nowPlaying?.Label);
            _window.SetHubFace("item." + item.Id + name, () =>
            {
                var text = UiKit.Text(name, 11, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true);
                text.TextAlignment = TextAlignment.Center;
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                text.MaxHeight = 48;
                text.Margin = new Thickness(8, 0, 8, 0);
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.VerticalAlignment = VerticalAlignment.Center;
                text.Foreground = new SolidColorBrush(_light ? Colors.Black : Colors.White);
                return text;
            });
        }
        else if (_stack.Count > 1)
        {
            var parent = _trail.Count > 0 ? _trail[^1] : "";
            var label = parent.Length > 0 ? parent : Loc.T("Submenu");
            _window.SetHubFace("back." + label, () =>
            {
                var stack = new System.Windows.Controls.StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var fg = new SolidColorBrush(_light ? Color.FromArgb(0x99, 0, 0, 0) : Color.FromArgb(0xB3, 255, 255, 255));
                var chevron = UiKit.Icon("", 12, ThemeKeys.TextSecondary, FontWeights.SemiBold);
                chevron.Foreground = fg;
                stack.Children.Add(chevron);
                var text = UiKit.Text(label, 9, FontWeights.Medium, ThemeKeys.TextSecondary, wrap: true);
                text.Foreground = fg;
                text.TextAlignment = TextAlignment.Center;
                text.MaxHeight = 26;
                text.Margin = new Thickness(8, 2, 8, 0);
                stack.Children.Add(text);
                return stack;
            });
        }
        else
        {
            _window.SetHubFace("brand", () =>
            {
                var mark = BrandMark.Create(34);
                mark.Fill = new SolidColorBrush(_light ? Colors.Black : Colors.White);
                mark.HorizontalAlignment = HorizontalAlignment.Center;
                mark.VerticalAlignment = VerticalAlignment.Center;
                return mark;
            });
        }
    }

    private void SetHighlight(int? index)
    {
        if (_highlight == index)
        {
            return;
        }

        _highlight = index;
        _window?.SetHighlight(index);
        UpdateHub();
    }

    private void OnTick()
    {
        if (!_sessionActive || _window is null)
        {
            return;
        }

        if (_holdPhase && _heldHotkey is { } hk && !RequiredModifiersHeld(hk))
        {
            HandleHoldReleased();
            if (!_sessionActive)
            {
                return;
            }
        }

        var cursor = Screens.CursorPosition();
        if (cursor != _lastCursor)
        {
            _lastCursor = cursor;
            RefreshHighlight(cursor);
        }
    }

    private void RefreshHighlight(Point cursor)
    {
        if (!_pointerActivated)
        {
            var travelled = Math.Sqrt(Math.Pow(cursor.X - _openPointer.X, 2) + Math.Pow(cursor.Y - _openPointer.Y, 2)) / _scale;
            if (travelled < RadialGeometry.MoveActivation)
            {
                return;
            }

            _pointerActivated = true;
        }

        var dx = (cursor.X - _center.X) / _scale;
        var dyUp = (_center.Y - cursor.Y) / _scale;
        SetHighlight(RadialGeometry.SliceAt(dx, dyUp, Current.Count));
    }

    private static bool RequiredModifiersHeld(Hotkey hk)
    {
        static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        var m = hk.Modifiers;
        return (!m.HasFlag(HotkeyModifiers.Control) || Down(0x11)) && (!m.HasFlag(HotkeyModifiers.Alt) || Down(0x12))
            && (!m.HasFlag(HotkeyModifiers.Shift) || Down(0x10)) && (!m.HasFlag(HotkeyModifiers.Win) || Down(0x5B) || Down(0x5C));
    }

    private void HandleHoldReleased()
    {
        _holdPhase = false;
        RefreshHighlight(Screens.CursorPosition());
        if (_highlight is { } h)
        {
            Select(h);
        }
        else if (_mode == "hold")
        {
            EndSession();
        }
    }

    /// <summary>Test hook: keep the wheel open when another app takes the foreground.</summary>
    internal static bool IgnoreForegroundChanges = false;

    private void OnForeground(nint hwnd)
    {
        if (IgnoreForegroundChanges || !_sessionActive || Plat.ProcessOf(hwnd) == Environment.ProcessId)
        {
            return;
        }

        var cls = N.ClassName(hwnd);
        if (cls is "ForegroundStaging" or "XamlExplorerHostIslandWindow" or "Shell_TrayWnd" or "TopLevelWindowForOverflowXamlIsland")
        {
            return;
        }

        EndSession();
    }

    // ------------------------------------------------------------------ keys (hook thread)

    private bool OnKey(KeyboardHookEvent e)
    {
        if (!_sessionActive)
        {
            return false;
        }

        var vk = e.VkCode;
        var handled = vk is 0x1B or 0x0D or (>= 0x25 and <= 0x28) or (>= 0x31 and <= 0x39) or (>= 0x61 and <= 0x69);
        if (!handled || vk >= _swallowed.Length)
        {
            return false;
        }

        if (e.IsKeyDown)
        {
            var repeat = _swallowed[vk];
            _swallowed[vk] = true;
            var arrow = vk is >= 0x25 and <= 0x28;
            if (!repeat || arrow)
            {
                Ctx.Ui.BeginInvoke(() => OnSessionKey(vk));
            }

            return true;
        }

        if (_swallowed[vk])
        {
            _swallowed[vk] = false;
            return true;
        }

        return false;
    }

    private void OnSessionKey(int vk)
    {
        if (!_sessionActive)
        {
            return;
        }

        switch (vk)
        {
            case 0x1B:
                StepBack();
                break;
            case 0x0D:
                if (_highlight is { } h)
                {
                    Select(h);
                }

                break;
            case 0x25 or 0x26:
                MoveHighlight(-1);
                break;
            case 0x27 or 0x28:
                MoveHighlight(1);
                break;
            default:
                var digit = vk >= 0x61 ? vk - 0x61 : vk - 0x31;
                if (digit < Current.Count)
                {
                    Select(digit);
                }

                break;
        }
    }

    private void MoveHighlight(int delta)
    {
        var count = Current.Count;
        if (count == 0)
        {
            return;
        }

        if (_mode != "hold")
        {
            _holdPhase = false;
        }

        int next;
        if (_highlight is null)
        {
            next = delta < 0 ? count - 1 : 0;
        }
        else
        {
            next = (_highlight.Value + delta + count) % count;
        }

        SetHighlight(next);
    }

    // ------------------------------------------------------------------ selection

    private void OnWheelClicked(Point rel)
    {
        if (!_sessionActive)
        {
            return;
        }

        var distance = Math.Sqrt(rel.X * rel.X + rel.Y * rel.Y);
        if (distance > RadialGeometry.WheelDiameter / 2)
        {
            EndSession();
        }
        else if (distance < RadialGeometry.DeadZone)
        {
            StepBack();
        }
        else if (_highlight is { } h)
        {
            Select(h);
        }
        else
        {
            // armed by position even when the pointer has not moved yet
            var idx = RadialGeometry.SliceAt(rel.X, -rel.Y, Current.Count);
            if (idx is { } i)
            {
                Select(i);
            }
        }
    }

    private void StepBack()
    {
        if (_stack.Count > 1)
        {
            _stack.RemoveAt(_stack.Count - 1);
            if (_trail.Count > 0)
            {
                _trail.RemoveAt(_trail.Count - 1);
            }

            _highlight = null;
            _pointerActivated = false;
            _openPointer = Screens.CursorPosition();
            RefreshContent(bloom: true);
            UpdateHub();
        }
        else
        {
            EndSession();
        }
    }

    private void Select(int index)
    {
        if (index < 0 || index >= Current.Count)
        {
            return;
        }

        var item = Current[index];
        if (item.Kind == RadialKinds.Submenu)
        {
            _stack.Add(item.Children);
            _trail.Add(item.Name);
            _highlight = null;
            _holdPhase = false;
            _pointerActivated = false;
            _openPointer = Screens.CursorPosition();
            RefreshContent(bloom: true);
            UpdateHub();
            return;
        }

        var anchor = _center;
        EndSession();
        Run(item, anchor);
    }

    private void EndSession()
    {
        if (!_sessionActive)
        {
            return;
        }

        _sessionActive = false;
        TearDownMonitors();
        var window = _window;
        window?.Close(() => { });
        _profile = null;
        _stack = [];
        _holdPhase = false;
        _heldButton = null;
    }

    private void EndSessionNow()
    {
        if (!_sessionActive && _window is not { IsVisible: true })
        {
            return;
        }

        _sessionActive = false;
        TearDownMonitors();
        _window?.HideNow();
        _nowPlaying?.Dismiss();
        _profile = null;
        _stack = [];
        _holdPhase = false;
        _heldButton = null;
    }

    private void TearDownMonitors()
    {
        _tick?.Stop();
        _tick = null;
        _keySub?.Dispose();
        _keySub = null;
        _outside?.Dispose();
        _outside = null;
        if (_foreground is not null)
        {
            _foreground.ForegroundChanged -= OnForeground;
            _foreground.Dispose();
            _foreground = null;
        }

        Array.Clear(_swallowed);
    }

    // ------------------------------------------------------------------ items

    private List<RadialItem> AvailableItems(IEnumerable<RadialItem> items)
    {
        var result = new List<RadialItem>();
        foreach (var item in items)
        {
            var ok = item.Kind switch
            {
                RadialKinds.Tool => ToolActions.IsAvailable(Ctx, item.Payload) && (item.Payload != "shelf" || App.Get<bool>("shelfEnabled", false)),
                RadialKinds.QuickToggle => QuickToggleOps.Available(Ctx, item.Payload),
                RadialKinds.WindowLayout => Ctx.Runtime.IsAvailable("window-layout"),
                _ => true,
            };
            if (!ok)
            {
                continue;
            }

            if (item.Kind == RadialKinds.Submenu)
            {
                var children = AvailableItems(item.Children);
                if (children.Count == 0)
                {
                    continue;
                }

                var copy = item.Clone();
                copy.Children = children;
                result.Add(copy);
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private static bool ContainsNowPlaying(IEnumerable<RadialItem> items) =>
        items.Any(i => (i.Kind == RadialKinds.Media && i.Payload == "nowPlaying") || (i.Kind == RadialKinds.Submenu && ContainsNowPlaying(i.Children)));

    // ------------------------------------------------------------------ running

    private void Run(RadialItem item, Point anchor)
    {
        switch (item.Kind)
        {
            case RadialKinds.App or RadialKinds.File:
                var target = item.Payload.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? item.Payload : RadialModel.Expand(item.Payload);
                var exists = target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || File.Exists(target) || Directory.Exists(target);
                if (!exists || !Plat.Open(target))
                {
                    SystemSounds.Beep.Play();
                }

                break;
            case RadialKinds.Url:
                if (RadialModel.NormalizedUrl(item.Payload) is not { } url || !Plat.Open(url))
                {
                    SystemSounds.Beep.Play();
                }

                break;
            case RadialKinds.Shortcut:
                if (Hotkey.TryParse(item.Payload, out var hotkey))
                {
                    Plat.AfterModifiersReleased(Ctx.Ui, () => PressShortcut(hotkey));
                }

                break;
            case RadialKinds.Tool:
                Plat.After(Ctx.Ui, 150, () =>
                {
                    if (!ToolActions.Run(Ctx, item.Payload))
                    {
                        SystemSounds.Beep.Play();
                    }
                });
                break;
            case RadialKinds.QuickToggle:
                Plat.After(Ctx.Ui, 150, () => Safe(() => QuickToggleOps.Run(Ctx, item.Payload)));
                break;
            case RadialKinds.WindowLayout:
                Plat.After(Ctx.Ui, 150, () =>
                {
                    if (!RunLayout(item.Payload))
                    {
                        SystemSounds.Beep.Play();
                    }
                });
                break;
            case RadialKinds.Media:
                if (item.Payload == "nowPlaying")
                {
                    _nowPlaying?.Present(anchor);
                }
                else
                {
                    var vk = item.Payload switch { "playPause" => (ushort)0xB3, "nextTrack" => (ushort)0xB0, _ => (ushort)0xB1 };
                    Plat.AfterModifiersReleased(Ctx.Ui, () =>
                    {
                        InputSender.SendKey(vk, false, true);
                        InputSender.SendKey(vk, true, true);
                    });
                }

                break;
        }
    }

    private bool RunLayout(string raw)
    {
        var def = RadialCatalog.Layouts.FirstOrDefault(l => l.Raw == raw);
        var candidates = new[] { raw, def.Role, def.Title };
        foreach (var (feature, action) in Ctx.Actions.All)
        {
            if (FeatureCatalog.Resolve(feature.Id) != "window-layout")
            {
                continue;
            }

            if (candidates.Any(c => c is not null && (action.Id.Equals(c, StringComparison.OrdinalIgnoreCase) || action.Title.Equals(c, StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    action.Invoke();
                    return true;
                }
                catch (Exception ex)
                {
                    Ctx.Log.Error("Window layout slice failed", ex);
                    return false;
                }
            }
        }

        return false;
    }

    private void PressShortcut(Hotkey hotkey)
    {
        var mods = new List<ushort>();
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            mods.Add(0x11);
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            mods.Add(0x12);
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            mods.Add(0x10);
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            mods.Add(0x5B);
        }

        var vk = (ushort)hotkey.VirtualKey;
        var extended = hotkey.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Insert or Key.Delete or Key.Home or Key.End or Key.PageUp or Key.PageDown;
        foreach (var m in mods)
        {
            InputSender.SendKey(m);
        }

        InputSender.SendKey(vk, false, extended);
        Plat.After(Ctx.Ui, 40, () =>
        {
            InputSender.SendKey(vk, true, extended);
            for (var i = mods.Count - 1; i >= 0; i--)
            {
                InputSender.SendKey(mods[i], true);
            }
        });
    }
}
