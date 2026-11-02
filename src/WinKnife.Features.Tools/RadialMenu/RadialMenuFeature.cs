// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using WpfPath = System.Windows.Shapes.Path;

namespace WinKnife.Features.Tools;

internal sealed record RadialSlot(string Title, string Glyph, Action Run);

/// <summary>Hold a trigger to open a ring of actions around the pointer; move toward one and release to run it.</summary>
public sealed class RadialMenuFeature : ToolFeatureBase
{
    private const int SlotCount = 8;

    private IDisposable? _keyboard;
    private IDisposable? _mouse;
    private RadialWindow? _window;
    private DispatcherTimer? _poll;
    private volatile string _trigger = "hotkey";
    private volatile int _vk;
    private volatile int _mods;
    private volatile bool _open;

    public override string Id => "radial-menu";

    public override string Name => "Radial menu";

    public override string Description => "Hold a shortcut or mouse button to open a ring of your favourite actions, flick toward one and let go.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("show", "Show radial menu at pointer", () => Safe(() => OpenMenu(manual: true)), ""),
    ];

    protected override void OnStart()
    {
        ReloadConfig();
    }

    protected override void OnStop()
    {
        CloseMenu(commit: false);
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _keyboard = null;
        _mouse = null;
    }

    protected override void OnSettingChangedUi(string key)
    {
        if (key is "trigger" or "hotkey")
        {
            ReloadConfig();
        }
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        EnsureDefaults();
        var choices = new List<Choice<string>> { new(string.Empty, "(empty)") };
        foreach (var (feature, action) in Ctx.Actions.All)
        {
            if (feature.Id == Id)
            {
                continue;
            }

            choices.Add(new Choice<string>($"{feature.Id}/{action.Id}", $"{feature.Name}: {action.Title}"));
        }

        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddChoice(
                "trigger",
                "Open the menu with",
                new Choice<string>[]
                {
                    new("hotkey", "Held keyboard shortcut"),
                    new("middle", "Middle mouse button"),
                    new("x1", "Mouse back button"),
                    new("x2", "Mouse forward button"),
                },
                "hotkey",
                "Mouse triggers are swallowed while the menu is used, so choose a button you do not need elsewhere.")
            .AddHotkey("hotkey", "Held shortcut", Hotkey.Parse("Ctrl+Alt+Q"), "Hold to show the ring, release to run the highlighted action.");
        for (var i = 0; i < SlotCount; i++)
        {
            builder.AddChoice($"slot{i}", $"Slot {i + 1}", choices, string.Empty);
        }

        builder.AddNote("Filled slots are spread evenly around the ring, starting at the top and going clockwise. Use at least two.");
        return builder.Build();
    }

    private void EnsureDefaults()
    {
        if (Ctx.Settings.Get("slotsInitialized", false))
        {
            return;
        }

        string[] preferred =
        [
            "screenshot/region", "color-picker/pick", "screen-ocr/capture", "quick-toggles/dark-mode",
            "command-bar/open", "scratchpad/toggle", "kill-process/open", "cleaner/open",
        ];
        var available = Ctx.Actions.All.Select(a => $"{a.Feature.Id}/{a.Action.Id}").ToHashSet();
        var picked = preferred.Where(available.Contains).Take(SlotCount).ToList();
        if (picked.Count < 2)
        {
            return; // other features have not started yet; try again later
        }

        for (var i = 0; i < SlotCount; i++)
        {
            Ctx.Settings.Set($"slot{i}", i < picked.Count ? picked[i] : string.Empty);
        }

        Ctx.Settings.Set("slotsInitialized", true);
    }

    private void ReloadConfig()
    {
        _trigger = Ctx.Settings.Get("trigger", "hotkey");
        var hotkey = Ctx.Settings.Get<Hotkey?>("hotkey", Hotkey.Parse("Ctrl+Alt+Q"));
        _vk = hotkey?.VirtualKey ?? 0;
        _mods = (int)(hotkey?.Modifiers ?? HotkeyModifiers.None);

        _keyboard?.Dispose();
        _mouse?.Dispose();
        _keyboard = null;
        _mouse = null;
        if (_trigger == "hotkey")
        {
            if (_vk != 0)
            {
                _keyboard = Ctx.Hooks.SubscribeKeyboard(OnKeyboard, 100);
            }
        }
        else
        {
            _mouse = Ctx.Hooks.SubscribeMouse(OnMouse, 100);
        }
    }

    // ---- Hook thread: tiny, no allocation-heavy work -------------------------------------------

    private bool OnKeyboard(KeyboardHookEvent e)
    {
        if (e.IsFromWinKnife || e.VkCode != _vk)
        {
            return false;
        }

        if (e.IsKeyDown)
        {
            if (_open)
            {
                return true; // key repeat while open
            }

            if ((int)e.Modifiers != _mods)
            {
                return false;
            }

            _open = true;
            Ctx.Ui.BeginInvoke(() => OpenMenu(manual: false));
            return true;
        }

        if (_open)
        {
            _open = false;
            Ctx.Ui.BeginInvoke(() => CloseMenu(commit: true));
            return true;
        }

        return false;
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsFromWinKnife)
        {
            return false;
        }

        var match = _trigger switch
        {
            "middle" => e.Kind is MouseEventKind.MiddleDown or MouseEventKind.MiddleUp,
            "x1" => e.Kind is MouseEventKind.XButtonDown or MouseEventKind.XButtonUp && e.XButton == 1,
            "x2" => e.Kind is MouseEventKind.XButtonDown or MouseEventKind.XButtonUp && e.XButton == 2,
            _ => false,
        };
        if (!match)
        {
            return false;
        }

        if (e.IsButtonDown)
        {
            if (_open || e.Modifiers != System.Windows.Input.ModifierKeys.None)
            {
                return _open;
            }

            _open = true;
            Ctx.Ui.BeginInvoke(() => OpenMenu(manual: false));
            return true;
        }

        if (_open)
        {
            _open = false;
            Ctx.Ui.BeginInvoke(() => CloseMenu(commit: true));
            return true;
        }

        return false;
    }

    // ---- UI thread ---------------------------------------------------------------------------------

    private List<RadialSlot> ResolveSlots()
    {
        EnsureDefaults();
        var catalog = Ctx.Actions.All;
        var slots = new List<RadialSlot>();
        for (var i = 0; i < SlotCount; i++)
        {
            var key = Ctx.Settings.Get($"slot{i}", string.Empty);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var found = catalog.FirstOrDefault(a => $"{a.Feature.Id}/{a.Action.Id}" == key);
            if (found.Action is { } action)
            {
                slots.Add(new RadialSlot(action.Title, action.Glyph ?? "", () => action.Invoke()));
            }
        }

        return slots;
    }

    private void OpenMenu(bool manual)
    {
        if (!IsRunning || _window is not null)
        {
            return;
        }

        var slots = ResolveSlots();
        if (slots.Count < 2)
        {
            _open = false;
            Ctx.Notifier.Show(Name, "Assign at least two actions to the radial menu in its settings.", NotificationLevel.Info);
            return;
        }

        NativeMethods.GetCursorPos(out var origin);
        _window = new RadialWindow(slots);
        _window.PlaceAt(origin.X, origin.Y);
        _window.ShowNoActivate();
        _window.PlaceAt(origin.X, origin.Y);
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) =>
        {
            NativeMethods.GetCursorPos(out var p);
            _window?.UpdatePointer(p.X, p.Y);
        }, Ctx.Ui);
        _poll.Start();
        if (manual)
        {
            // No key/button to release: a click anywhere completes the selection.
            _open = true;
            _window.MouseDownCommit = () => CloseMenu(commit: true);
            _window.ClickThrough = false;
        }
        else
        {
            _window.ClickThrough = true;
        }
    }

    private void CloseMenu(bool commit)
    {
        _poll?.Stop();
        _poll = null;
        var window = _window;
        _window = null;
        _open = false;
        if (window is null)
        {
            return;
        }

        var selected = commit ? window.Selected : null;
        window.Close();
        if (selected is not null)
        {
            Ctx.Ui.BeginInvoke(() => Safe(selected.Run), DispatcherPriority.Background);
        }
    }
}

internal sealed class RadialWindow : OverlayWindow
{
    private const double Size = 380;
    private const double Outer = 170;
    private const double Inner = 54;
    private static readonly Brush Idle = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x26)));
    private static readonly Brush Hot = Freeze(new SolidColorBrush(Color.FromRgb(0x5B, 0x5B, 0xE8)));

    private readonly List<RadialSlot> _slots;
    private readonly List<WpfPath> _wedges = new();
    private readonly TextBlock _centre = new();
    private int _centreX;
    private int _centreY;
    private int _selected = -1;

    public RadialWindow(List<RadialSlot> slots)
    {
        _slots = slots;
        Width = Size;
        Height = Size;
        Left = -10000;
        Top = -10000;
        var canvas = new Canvas { Width = Size, Height = Size, Background = Brushes.Transparent };
        var n = slots.Count;
        var step = 2 * Math.PI / n;
        for (var i = 0; i < n; i++)
        {
            var centreAngle = i * step;
            var gap = 0.025;
            var wedge = new WpfPath
            {
                Data = Wedge(centreAngle - step / 2 + gap, centreAngle + step / 2 - gap),
                Fill = Idle,
                Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                StrokeThickness = 1,
            };
            _wedges.Add(wedge);
            canvas.Children.Add(wedge);

            var mid = (Outer + Inner) / 2;
            var stack = new StackPanel { Width = 92 };
            var glyph = new TextBlock { Text = slots[i].Glyph, FontFamily = UiKit.IconFont, FontSize = 24, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            var label = new TextBlock { Text = slots[i].Title, FontSize = 11, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis };
            stack.Children.Add(glyph);
            stack.Children.Add(label);
            stack.IsHitTestVisible = false;
            Canvas.SetLeft(stack, Size / 2 + mid * Math.Sin(centreAngle) - 46);
            Canvas.SetTop(stack, Size / 2 - mid * Math.Cos(centreAngle) - 24);
            canvas.Children.Add(stack);
        }

        var hub = new Ellipse { Width = Inner * 2 - 8, Height = Inner * 2 - 8, Fill = Idle, Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)) };
        Canvas.SetLeft(hub, Size / 2 - Inner + 4);
        Canvas.SetTop(hub, Size / 2 - Inner + 4);
        canvas.Children.Add(hub);
        _centre.Foreground = Brushes.White;
        _centre.FontSize = 11;
        _centre.TextAlignment = TextAlignment.Center;
        _centre.TextWrapping = TextWrapping.Wrap;
        _centre.Width = Inner * 2 - 20;
        _centre.Text = "Release to run";
        _centre.IsHitTestVisible = false;
        Canvas.SetLeft(_centre, Size / 2 - _centre.Width / 2);
        Canvas.SetTop(_centre, Size / 2 - 14);
        canvas.Children.Add(_centre);
        Content = canvas;
        MouseLeftButtonDown += (_, _) => MouseDownCommit?.Invoke();
    }

    public Action? MouseDownCommit { get; set; }

    public RadialSlot? Selected => _selected >= 0 ? _slots[_selected] : null;

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Point Polar(double radius, double angle) => new(Size / 2 + radius * Math.Sin(angle), Size / 2 - radius * Math.Cos(angle));

    private static Geometry Wedge(double a0, double a1)
    {
        var large = a1 - a0 > Math.PI;
        var figure = new PathFigure { StartPoint = Polar(Outer, a0), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(Polar(Outer, a1), new Size(Outer, Outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(Polar(Inner, a1), true));
        figure.Segments.Add(new ArcSegment(Polar(Inner, a0), new Size(Inner, Inner), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Centres the ring on a physical-pixel point.</summary>
    public void PlaceAt(int x, int y)
    {
        _centreX = x;
        _centreY = y;
        var side = (int)Math.Round(ToPhysical(Size));
        SetPhysicalBounds(x - side / 2, y - side / 2, side, side);
    }

    public void UpdatePointer(int x, int y)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var dx = (x - _centreX) / scale;
        var dy = (y - _centreY) / scale;
        var index = -1;
        if (Math.Sqrt(dx * dx + dy * dy) > Inner - 6)
        {
            var angle = Math.Atan2(dx, -dy);
            if (angle < 0)
            {
                angle += 2 * Math.PI;
            }

            var step = 2 * Math.PI / _slots.Count;
            index = (int)Math.Floor((angle + step / 2) / step) % _slots.Count;
        }

        if (index == _selected)
        {
            return;
        }

        _selected = index;
        for (var i = 0; i < _wedges.Count; i++)
        {
            _wedges[i].Fill = i == index ? Hot : Idle;
        }

        _centre.Text = index >= 0 ? _slots[index].Title : "Release to run";
    }
}
