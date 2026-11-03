// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>Common shape of the cards on the "Mouse &amp; Touchpad" page.</summary>
public abstract class MouseSectionBase : ISettingsSection
{
    public string PageId => SettingsPages.Mouse;

    public abstract string? Anchor { get; }

    public virtual string? FeatureId => null;

    public virtual IReadOnlyList<string> FeatureIds => FeatureId is null ? [] : [FeatureId];

    public abstract int Order { get; }

    public abstract string? Title { get; }

    public virtual IReadOnlyList<string> Keywords => [];

    public abstract FrameworkElement CreateView(ISettingsContext context);

    protected static void Prepare(ISettingsContext c) => K.RegisterDefaults(c.App);
}

// ------------------------------------------------------------------------------------------------------ legend

/// <summary>The legend card: every installed mouse feature with an on/off dot; a click jumps to its card.</summary>
public sealed class MouseLegendSection : MouseSectionBase
{
    private sealed record Entry(string FeatureId, string Anchor, Func<IAppSettings, bool> IsOn);

    private static readonly Entry[] Entries =
    [
        new("scroll-inverter", "scrollDirection", a => a.Get<bool>(K.ScrollInvertV) || a.Get<bool>(K.ScrollInvertH)),
        new("scroll-horizontal", "scrollDirection", a => a.Get<bool>(K.ScrollHorizontal)),
        new("scroll-zoom", "scrollZoom", a => a.Get<bool>(K.PinchZoom) && a.Get<string>(K.PinchZoomModifier) != "none"),
        new("focus-follows-mouse", "focusFollowsMouse", a => a.Get<bool>(K.FocusEnabled)),
        new("smooth-scroll", "smoothScroll", a => a.Get<bool>(K.SmoothEnabled)),
        new("linear-scroll", "linearScroll", a => a.Get<bool>(K.LinearEnabled)),
        new("mouse-acceleration", "mouseAcceleration", a => a.Get<bool>(K.AccelDisabled)),
        new("mouse-navigation", "mouseNavigation", a => a.Get<bool>(K.NavEnabled)),
        new("mouse-button-shortcuts", "mouseButtonShortcuts", a => a.Get<bool>(K.ButtonShortcutsEnabled) || a.Get<bool>(K.SpacesGestureEnabled)),
        new("mouse-click-debounce", "mouseClickDebounce", a => a.Get<bool>(K.ClickDebounceEnabled)),
    ];

    public override string? Anchor => "legend";

    public override int Order => 0;

    public override string? Title => null;

    public override IReadOnlyList<string> FeatureIds => [.. Entries.Select(e => e.FeatureId)];

    public override FrameworkElement CreateView(ISettingsContext c)
    {
        Prepare(c);
        var card = new SettingsCard();
        var wrap = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        var dots = new List<(Ellipse Dot, TextBlock Label, Entry Entry)>();
        foreach (var entry in Entries.Where(e => c.Runtime.IsAvailable(e.FeatureId)))
        {
            var title = FeatureCatalog.Get(entry.FeatureId)?.Title ?? entry.FeatureId;
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
            var label = UiKit.Text(title, 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
            label.Margin = new Thickness(10, 0, 0, 0);
            var chevron = UiKit.Icon("", 9, ThemeKeys.TextTertiary, FontWeights.SemiBold);
            chevron.HorizontalAlignment = HorizontalAlignment.Right;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(dot);
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            Grid.SetColumn(chevron, 2);
            grid.Children.Add(chevron);
            var cell = new Border { Padding = new Thickness(8), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = grid };
            cell.MouseEnter += (_, _) => cell.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
            cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
            var anchor = entry.Anchor;
            cell.MouseLeftButtonUp += (_, _) => c.Shell.OpenSettings(SettingsPages.Join(SettingsPages.Mouse, anchor));
            wrap.Children.Add(cell);
            dots.Add((dot, label, entry));
        }

        void Paint()
        {
            foreach (var (dot, label, entry) in dots)
            {
                var on = entry.IsOn(c.App);
                if (on)
                {
                    dot.SetResourceReference(Shape.FillProperty, ThemeKeys.Accent);
                    label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
                }
                else
                {
                    dot.Fill = InputUi.Tinted(ThemeKeys.TextSecondary, 0.35);
                    label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
                }
            }
        }

        Paint();
        card.Add(wrap);
        var live = new LiveView(c);
        live.OnChange(Paint);
        return live.Attach(card);
    }
}

// ------------------------------------------------------------------------------------------------------ scrolling

/// <summary>"Scrolling" card: invert vertical / horizontal, scroll sideways with a key, notes and the exceptions list.</summary>
public sealed class ScrollingSection : MouseSectionBase
{
    public override string? Anchor => "scrollDirection";

    public override int Order => 10;

    public override string? Title => Strings.Scrolling;

    public override IReadOnlyList<string> FeatureIds => ["scroll-inverter", "scroll-horizontal"];

    public override IReadOnlyList<string> Keywords => ["Invert vertical scrolling", "Invert horizontal scrolling", "Scroll sideways while holding a key", "Modifier key", "wheel", "touchpad"];

    public override FrameworkElement CreateView(ISettingsContext c)
    {
        Prepare(c);
        var app = c.App;
        var live = new LiveView(c);
        var card = new SettingsCard(Strings.Scrolling);
        var invert = c.Runtime.IsAvailable("scroll-inverter");
        var sideways = c.Runtime.IsAvailable("scroll-horizontal");

        if (invert)
        {
            card.Add(Rows.Toggle(app, "", Strings.InvertVertical, null, K.ScrollInvertV));
            card.Add(Rows.Toggle(app, "", Strings.InvertHorizontal, null, K.ScrollInvertH));
        }

        if (sideways)
        {
            card.Add(Rows.Toggle(app, "", Strings.ScrollSideways, Strings.ScrollSidewaysCaption, K.ScrollHorizontal));
            var block = Rows.Indented(8);
            Rows.AddTo(block, Rows.SubTitle(Strings.ModifierKey));
            var chips = InputUi.ChipRow();
            void Render()
            {
                chips.Children.Clear();
                var current = app.Get<string>(K.ScrollHorizontalModifier);
                foreach (var (value, label) in new[] { ("shift", Strings.Shift), ("option", Strings.Alt), ("control", Strings.Ctrl), ("command", Strings.Win) })
                {
                    var v = value;
                    chips.Children.Add(KeyCapChip(label, current == value, () =>
                    {
                        app.Set(K.ScrollHorizontalModifier, v);
                        Render();
                    }));
                }
            }

            Render();
            Rows.AddTo(block, chips);
            card.Add(live.Show(block, () => app.Get<bool>(K.ScrollHorizontal)));
        }

        if (invert)
        {
            var badge = InputUi.ActiveBadge(Strings.InvertingNow, Rows.Inset);
            card.Add(live.Show(badge, () => (app.Get<bool>(K.ScrollInvertV) || app.Get<bool>(K.ScrollInvertH)) && c.Runtime.IsRunning("scroll-inverter")));
            card.Add(InputUi.Caption(Strings.TouchpadUntouched));
        }

        var exceptions = new AppBundleList(app, K.ScrollInvertExceptions, Strings.CaptionScrollDirection);
        card.Add(live.Show(exceptions, () => app.Get<bool>(K.ScrollInvertV) || app.Get<bool>(K.ScrollInvertH) || app.Get<bool>(K.ScrollHorizontal)
            || (app.Get<bool>(K.PinchZoom) && app.Get<string>(K.PinchZoomModifier) != "none")));
        return live.Attach(card);
    }

    private static Border KeyCapChip(string label, bool selected, Action onClick)
    {
        var cap = InputUi.KeyCap(label, selected, 0, 28, 12, tinted: true, onClick: onClick);
        cap.Margin = new Thickness(0, 0, 8, 8);
        cap.Padding = new Thickness(11, 0, 11, 0);
        return cap;
    }
}

/// <summary>"Zoom" card: modifier + wheel zooms the app.</summary>
public sealed class ScrollZoomSection : MouseSectionBase
{
    public override string? Anchor => "scrollZoom";

    public override int Order => 20;

    public override string? Title => Strings.ZoomTitle;

    public override string? FeatureId => "scroll-zoom";

    public override IReadOnlyList<string> Keywords => ["Pinch Zoom", "ctrl wheel", "magnify"];

    public override FrameworkElement CreateView(ISettingsContext c)
    {
        Prepare(c);
        var app = c.App;
        var live = new LiveView(c);
        var card = new SettingsCard(Strings.ZoomTitle);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        var label = UiKit.Text(Strings.PinchZoom, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        var combo = InputUi.Combo<string>(
            [("none", Strings.None), ("option", Strings.Alt), ("control", Strings.Ctrl), ("command", Strings.Win)],
            app.Get<string>(K.PinchZoomModifier),
            v => app.Set(K.PinchZoomModifier, v),
            160);
        combo.Margin = new Thickness(0, 0, 16, 0);
        combo.IsEnabled = app.Get<bool>(K.PinchZoom);
        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);
        var toggle = new MacSwitch(app.Get<bool>(K.PinchZoom), SwitchSize.Regular, on =>
        {
            app.Set(K.PinchZoom, on);
            combo.IsEnabled = on;
        });
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(toggle);
        card.Add(grid);
        card.Add(InputUi.Caption(Strings.ZoomNote));

        if (!c.Runtime.AnyAvailable(["scroll-inverter", "scroll-horizontal"]))
        {
            var exceptions = new AppBundleList(app, K.ScrollInvertExceptions, Strings.CaptionScrollDirection);
            card.Add(live.Show(exceptions, () => app.Get<bool>(K.PinchZoom) && app.Get<string>(K.PinchZoomModifier) != "none"));
        }

        return live.Attach(card);
    }
}

// ------------------------------------------------------------------------------------------------------ focus

/// <summary>"Focus follows mouse" card.</summary>
public sealed class FocusFollowsMouseSection : MouseSectionBase
{
    public override string? Anchor => "focusFollowsMouse";

    public override int Order => 30;

    public override string? Title => Strings.FocusTitle;

    public override string? FeatureId => "focus-follows-mouse";

    public override IReadOnlyList<string> Keywords => ["Hover delay", "Bring the window to the front", "Wait for the pointer to stop"];

    public override FrameworkElement CreateView(ISettingsContext c)
    {
        Prepare(c);
        var app = c.App;
        var live = new LiveView(c);
        var card = new SettingsCard();
        card.Add(Rows.Toggle(app, "", Strings.FocusTitle, Strings.FocusCaption, K.FocusEnabled));

        var block = Rows.Indented(10);
        Rows.AddTo(block, InputUi.SliderLine(Strings.HoverDelay, 100, 3000, 50, Math.Clamp(app.Get<int>(K.FocusDelay), 100, 3000), v => Loc.F("{0} ms", (int)v), v => app.Set(K.FocusDelay, (int)v), labelWidth: 96));
        Rows.AddTo(block, SwitchLine(Strings.BringToFront, app, K.FocusRaise));
        Rows.AddTo(block, live.Show(SwitchLine(Strings.WaitForStop, app, K.FocusWaitForStop), () => !app.Get<bool>(K.FocusRaise)));
        Rows.AddTo(block, new AppBundleList(app, K.FocusExceptions, Strings.CaptionFocus));
        card.Add(live.Show(block, () => app.Get<bool>(K.FocusEnabled)));
        return live.Attach(card);
    }

    internal static FrameworkElement SwitchLine(string label, IAppSettings app, string key)
    {
        var toggle = new MacSwitch(app.Get<bool>(key), SwitchSize.Small, on => app.Set(key, on));
        return InputUi.LabelLine(label, toggle);
    }
}

// ------------------------------------------------------------------------------------------------------ smooth

/// <summary>"Smooth scrolling" card.</summary>
public sealed class SmoothScrollSection : MouseSectionBase
{
    public override string? Anchor => "smoothScroll";

    public override int Order => 40;

    public override string? Title => Strings.SmoothTitle;

    public override string? FeatureId => "smooth-scroll";

    public override IReadOnlyList<string> Keywords => ["Scrolling speed", "Response", "Coast", "More options"];

    public override FrameworkElement CreateView(ISettingsContext c)
    {
        Prepare(c);
        var app = c.App;
        var live = new LiveView(c);
        var card = new SettingsCard();
        card.Add(Rows.Toggle(app, "", Strings.SmoothTitle, Strings.SmoothCaption, K.SmoothEnabled));

        var block = Rows.Indented(10);
        var step = app.Get<int>(K.SmoothStep);
        Rows.AddTo(block, InputUi.SliderLine(Strings.ScrollingSpeed, 20, 100, 10, step is >= 20 and <= 100 ? step : 40, v => ((int)v).ToString(), v => app.Set(K.SmoothStep, (int)v), labelWidth: 120));

        var more = new StackPanel();
        more.Children.Add(InputUi.SliderLine(Strings.Response, 0, 100, 5, Math.Clamp(app.Get<int>(K.SmoothResponse), 0, 100), v => $"{(int)v}%", v => app.Set(K.SmoothResponse, (int)v), labelWidth: 120));
        var coast = InputUi.SliderLine(Strings.Coast, 0, 100, 5, Math.Clamp(app.Get<int>(K.SmoothCoast), 0, 100), v => $"{(int)v}%", v => app.Set(K.SmoothCoast, (int)v), labelWidth: 120);
        coast.Margin = new Thickness(0, 6, 0, 0);
        more.Children.Add(coast);
        Rows.AddTo(block, new InputUi.Disclosure(UiKit.Text(Strings.MoreOptions, 12, FontWeights.Medium, ThemeKeys.TextPrimary), more, expanded: false));
        Rows.AddTo(block, new AppBundleList(app, K.SmoothExceptions, Strings.CaptionSmooth));
        card.Add(live.Show(block, () => app.Get<bool>(K.SmoothEnabled)));
        return live.Attach(card);
    }
}

// ------------------------------------------------------------------------------------------------------ linear

/// <summary>"Linear scrolling" card.</summary>
public sealed class LinearScrollSection : MouseSectionBase
{
    public override string? Anchor => "linearScroll";

    public override int Order => 50;

    public override string? Title => Strings.LinearTitle;

    public override string? FeatureId => "linear-scroll";

    public override IReadOnlyList<string> Keywords => ["Lines per step"];

    public override FrameworkElement CreateView(ISettingsContext c)
    {
        Prepare(c);
        var app = c.App;
        var live = new LiveView(c);
        var card = new SettingsCard();
        card.Add(Rows.Toggle(app, "", Strings.LinearTitle, Strings.LinearCaption, K.LinearEnabled));
        var block = Rows.Indented(10);
        var lines = app.Get<int>(K.LinearLines);
        Rows.AddTo(block, InputUi.SliderLine(Strings.LinesPerStep, 1, 10, 1, lines is >= 1 and <= 10 ? lines : 3, v => ((int)v).ToString(), v => app.Set(K.LinearLines, (int)v), labelWidth: 120));
        Rows.AddTo(block, new AppBundleList(app, K.LinearExceptions, Strings.CaptionLinear));
        card.Add(live.Show(block, () => app.Get<bool>(K.LinearEnabled)));
        return live.Attach(card);
    }
}

// ------------------------------------------------------------------------------------------------------ acceleration

/// <summary>"Disable mouse acceleration" card.</summary>
public sealed class MouseAccelerationSection : MouseSectionBase
{
    public override string? Anchor => "mouseAcceleration";

    public override int Order => 60;

    public override string? Title => Strings.AccelTitle;

    public override string? FeatureId => "mouse-acceleration";

    public override IReadOnlyList<string> Keywords => ["pointer precision", "pointer acceleration"];

    public override FrameworkElement CreateView(ISettingsContext c)
    {
        Prepare(c);
        var card = new SettingsCard();
        card.Add(Rows.Toggle(c.App, "", Strings.AccelTitle, Strings.AccelCaption, K.AccelDisabled));
        return card;
    }
}

// ------------------------------------------------------------------------------------------------------ navigation

/// <summary>"Navigation" card: side buttons for Back and Forward.</summary>
public sealed class MouseNavigationSection : MouseSectionBase
{
    public override string? Anchor => "mouseNavigation";

    public override int Order => 70;

    public override string? Title => Strings.SideButtonsTitle;

    public override string? FeatureId => "mouse-navigation";

    public override IReadOnlyList<string> Keywords => ["Back", "Forward", "side buttons", "thumb buttons"];

    public override FrameworkElement CreateView(ISettingsContext c)
    {
        Prepare(c);
        var app = c.App;
        var live = new LiveView(c);
        var card = new SettingsCard(Strings.Navigation);
        card.Add(Rows.Toggle(app, "", Strings.UseSideButtons, Strings.UseSideButtonsCaption, K.NavEnabled));
        var block = Rows.Indented(10);
        Rows.AddTo(block, live.Show(InputUi.ActiveBadge(Strings.SideButtonsActive), () => app.Get<bool>(K.NavEnabled) && c.Runtime.IsRunning("mouse-navigation")));
        Rows.AddTo(block, new AppBundleList(app, K.NavExceptions, Strings.CaptionNavigation));
        card.Add(live.Show(block, () => app.Get<bool>(K.NavEnabled)));
        return live.Attach(card);
    }
}

// ------------------------------------------------------------------------------------------------------ click filter

/// <summary>"Extra click filter" card.</summary>
public sealed class ClickFilterSection : MouseSectionBase
{
    public override string? Anchor => "mouseClickDebounce";

    public override int Order => 90;

    public override string? Title => Strings.ClickFilterTitle;

    public override string? FeatureId => "mouse-click-debounce";

    public override IReadOnlyList<string> Keywords => ["Filter window", "double click", "worn mouse button"];

    public override FrameworkElement CreateView(ISettingsContext c)
    {
        Prepare(c);
        var app = c.App;
        var live = new LiveView(c);
        var card = new SettingsCard();
        card.Add(Rows.Toggle(app, "", Strings.ClickFilterTitle, Strings.ClickFilterCaption, K.ClickDebounceEnabled));
        var block = Rows.Indented(6);
        var window = app.Get<int>(K.ClickDebounceWindow);
        Rows.AddTo(block, InputUi.LabelLine(Strings.FilterWindow, InputUi.Stepper(5, 100, window is >= 5 and <= 100 ? window : 25, v => Loc.F("{0} ms", v), v => app.Set(K.ClickDebounceWindow, v))));
        Rows.AddTo(block, InputUi.Caption(Strings.FilterWindowCaption));
        card.Add(live.Show(block, () => app.Get<bool>(K.ClickDebounceEnabled)));
        return live.Attach(card);
    }
}

// ------------------------------------------------------------------------------------------------------ mouse buttons

/// <summary>"Mouse button shortcuts" card: extra buttons and side-wheel directions press key combinations; desktop drag gesture.</summary>
public sealed class MouseButtonShortcutsSection : MouseSectionBase
{
    public override string? Anchor => "mouseButtonShortcuts";

    public override int Order => 80;

    public override string? Title => Strings.MouseButtonShortcuts;

    public override string? FeatureId => "mouse-button-shortcuts";

    public override IReadOnlyList<string> Keywords => ["Back side button", "Forward side button", "Side wheel", "Switch desktops by dragging a button", "extra buttons"];

    public override FrameworkElement CreateView(ISettingsContext c)
    {
        Prepare(c);
        return new MouseButtonsCard(c).View;
    }
}

/// <summary>The view of <see cref="MouseButtonShortcutsSection"/> (stateful: capture mode, pending rows).</summary>
internal sealed class MouseButtonsCard
{
    private readonly SettingsCard _card;
    private readonly ISettingsContext _c;
    private readonly IAppSettings _app;
    private readonly LiveView _live;
    private readonly StackPanel _mappings = new();
    private readonly StackPanel _gesture = new();
    private int? _pendingButton;
    private int _captureKind; // 0 idle, 1 shortcut capture, 2 gesture capture
    private string? _feedback;

    public MouseButtonsCard(ISettingsContext c)
    {
        _card = new SettingsCard(Strings.MouseButtonShortcuts);
        _c = c;
        _app = c.App;
        _live = new LiveView(c);

        _card.Add(Rows.Toggle(_app, "", Strings.UseExtraButtons, Strings.UseExtraButtonsCaption, K.ButtonShortcutsEnabled, on =>
        {
            if (!on)
            {
                Cancel();
            }
        }));

        _mappings.Margin = new Thickness(Rows.Inset, 0, 0, 0);
        _card.Add(_live.Show(_mappings, () => _app.Get<bool>(K.ButtonShortcutsEnabled)));
        _card.AddDivider();

        _card.Add(Rows.Toggle(_app, "", Strings.SwitchDesktopsByDragging, Strings.SwitchDesktopsCaption, K.SpacesGestureEnabled, on =>
        {
            if (!on)
            {
                Cancel();
                _app.Set(K.SpacesGestureButton, 0);
            }

            RenderGesture();
        }));
        _gesture.Margin = new Thickness(Rows.Inset, 0, 0, 0);
        _card.Add(_live.Show(_gesture, () => _app.Get<bool>(K.SpacesGestureEnabled)));

        var exceptions = new AppBundleList(_app, K.ButtonExceptions, Strings.CaptionButtons);
        _card.Add(_live.Show(exceptions, () => _app.Get<bool>(K.ButtonShortcutsEnabled) || _app.Get<bool>(K.SpacesGestureEnabled)));

        RenderMappings();
        RenderGesture();
        _live.Attach(_card);
        _card.Unloaded += (_, _) => Cancel();
    }

    public FrameworkElement View => _card;

    private Dictionary<string, string> Map() => new(_app.Get<Dictionary<string, string>?>(K.ButtonShortcuts) ?? []);

    private void Cancel()
    {
        MouseButtonCapture.Cancel();
        _captureKind = 0;
        _feedback = null;
    }

    // ---------------------------------------------------------------------------------------------- shortcuts

    private void RenderMappings()
    {
        _mappings.Children.Clear();
        var map = Map();
        var rows = map.Keys.Select(k => int.TryParse(k, out var n) ? n : int.MinValue).Where(n => n != int.MinValue).OrderBy(n => n).ToList();
        if (_pendingButton is { } p && !rows.Contains(p))
        {
            rows.Add(p);
            rows.Sort();
        }

        if (rows.Count == 0 && _captureKind != 1)
        {
            _mappings.Children.Add(InputUi.Caption(Strings.NoShortcutsYet));
        }

        foreach (var button in rows)
        {
            _mappings.AddSpaced(MappingRow(button, map.TryGetValue(button.ToString(), out var text) && Hotkey.TryParse(text, out var hk) ? hk : null), 10);
        }

        _mappings.AddSpaced(CaptureRow(forGesture: false), 10);
    }

    private FrameworkElement MappingRow(int button, Hotkey? hotkey)
    {
        var stack = new StackPanel();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = UiKit.Text(MouseInputs.Name(button), 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        name.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(name);

        var recorder = new HotkeyRecorder { Hotkey = hotkey, Width = 150, MinWidth = 150 };
        recorder.HotkeyChanged += (_, value) =>
        {
            if (value is null)
            {
                return;
            }

            var map = Map();
            map[button.ToString()] = value.ToString();
            _app.Set(K.ButtonShortcuts, map);
            if (_pendingButton == button)
            {
                _pendingButton = null;
            }

            RenderMappings();
        };
        Grid.SetColumn(recorder, 1);
        grid.Children.Add(recorder);

        var remove = new Button { Content = UiKit.Icon("", 14, ThemeKeys.TextSecondary), ToolTip = Strings.Remove, Padding = new Thickness(3), Margin = new Thickness(8, 0, 0, 0) };
        remove.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
        remove.Click += (_, _) =>
        {
            var map = Map();
            map.Remove(button.ToString());
            _app.Set(K.ButtonShortcuts, map);
            if (_pendingButton == button)
            {
                _pendingButton = null;
            }

            RenderMappings();
        };
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);
        stack.Children.Add(grid);

        if (MouseInputs.ClaimedByRadial(_app, button) && hotkey is not null)
        {
            var note = InputUi.Warning(Strings.RadialWaits, 0, "");
            note.Margin = new Thickness(0, 5, 0, 0);
            stack.Children.Add(note);
        }

        return stack;
    }

    // ---------------------------------------------------------------------------------------------- capture

    private FrameworkElement CaptureRow(bool forGesture)
    {
        var stack = new StackPanel();
        var kind = forGesture ? 2 : 1;
        if (_captureKind != kind)
        {
            var add = UiKit.Button(forGesture ? Strings.ChooseAButton : Strings.AddAButton, ButtonKind.Bordered, 12, "", () => BeginCapture(forGesture));
            add.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(add);
            if (_feedback is not null && _captureKind == 0)
            {
                var warn = InputUi.Warning(_feedback);
                warn.Margin = new Thickness(0, 6, 0, 0);
                stack.Children.Add(warn);
            }

            return stack;
        }

        var watching = MouseButtonCapture.Watching;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(UiKit.Icon(watching ? "" : "", 13, watching ? ThemeKeys.TextSecondary : ThemeKeys.Orange));
        var text = UiKit.Text(watching ? (forGesture ? Strings.NowPressMiddle : Strings.NowPressExtra) : Strings.CannotWatchMouse, 12.5, FontWeights.Normal, ThemeKeys.TextPrimary);
        text.Margin = new Thickness(8, 0, 0, 0);
        left.Children.Add(text);
        row.Children.Add(left);
        var cancel = UiKit.Button(Strings.Cancel, ButtonKind.Bordered, 12, null, () =>
        {
            Cancel();
            RenderMappings();
            RenderGesture();
        });
        Grid.SetColumn(cancel, 1);
        row.Children.Add(cancel);
        stack.Children.Add(row);
        if (_feedback is not null)
        {
            var warn = InputUi.Warning(_feedback);
            warn.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(warn);
        }

        var hint = InputUi.Caption(Strings.CaptureHint);
        hint.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(hint);
        return stack;
    }

    private void BeginCapture(bool forGesture)
    {
        _feedback = null;
        _captureKind = forGesture ? 2 : 1;
        MouseButtonCapture.Begin(button => _c.Ui.BeginInvoke(() => OnCaptured(button, forGesture)), allowMiddle: true);
        RenderMappings();
        RenderGesture();
    }

    private void OnCaptured(int button, bool forGesture)
    {
        var map = Map();
        var gestureButton = _app.Get<int>(K.SpacesGestureButton);
        string? error = null;
        if (forGesture)
        {
            if (button < 0 || (button != MouseInputs.Middle && button < MouseInputs.Back))
            {
                error = Strings.CannotBeHeld;
            }
            else if (MouseInputs.ClaimedByRadial(_app, button))
            {
                error = Strings.RadialClaimed;
            }
            else if (map.ContainsKey(button.ToString()))
            {
                error = Strings.AlreadyHasShortcut;
            }
        }
        else
        {
            if (button == MouseInputs.Middle || button is not (MouseInputs.Back or MouseInputs.Forward or MouseInputs.SideWheelLeft or MouseInputs.SideWheelRight))
            {
                error = Strings.CannotTakeShortcut;
            }
            else if (MouseInputs.ClaimedByRadial(_app, button))
            {
                error = Strings.RadialClaimed;
            }
            else if (map.ContainsKey(button.ToString()) || gestureButton == button)
            {
                error = Strings.AlreadyListed;
            }
        }

        _captureKind = 0;
        _feedback = error;
        if (error is null)
        {
            if (forGesture)
            {
                _app.Set(K.SpacesGestureButton, button);
            }
            else
            {
                _pendingButton = button;
            }
        }

        RenderMappings();
        RenderGesture();
    }

    // ---------------------------------------------------------------------------------------------- desktop drag

    private void RenderGesture()
    {
        _gesture.Children.Clear();
        var bound = _app.Get<int>(K.SpacesGestureButton);
        if (bound != 0)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = UiKit.Text(MouseInputs.Name(bound), 13, FontWeights.Normal, ThemeKeys.TextPrimary);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            var remove = new Button { Content = UiKit.Icon("", 14, ThemeKeys.TextSecondary), ToolTip = Strings.Remove, Padding = new Thickness(3) };
            remove.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
            remove.Click += (_, _) =>
            {
                _app.Set(K.SpacesGestureButton, 0);
                RenderGesture();
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            _gesture.Children.Add(row);
        }
        else
        {
            _gesture.Children.Add(CaptureRow(forGesture: true));
        }

        var follows = new MacSwitch(_app.Get<bool>(K.SpacesGestureFollows), SwitchSize.Regular, on => _app.Set(K.SpacesGestureFollows, on));
        var followRow = new SettingsRow(null, Strings.DesktopsFollowDrag, Strings.DesktopsFollowDragCaption, follows);
        _gesture.AddSpaced(followRow, 10);
    }
}
