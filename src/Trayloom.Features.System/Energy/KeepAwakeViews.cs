// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools;

/// <summary>A selectable capsule with a live-updatable label (keep-awake durations, monitor intervals).</summary>
internal sealed class Chip : Border
{
    private readonly TextBlock _label;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly bool _rounded;
    private bool _selected;

    public Chip(string text, Action? click, bool selected = false, string? glyph = null, double height = 22, double radius = 11, double fontSize = 11, string? tooltip = null)
    {
        _rounded = radius >= 11;
        MinHeight = height;
        CornerRadius = new CornerRadius(radius);
        Padding = new Thickness(8, 0, 8, 0);
        Cursor = Cursors.Hand;
        ToolTip = tooltip;
        if (glyph is not null)
        {
            var icon = UiKit.Icon(glyph, fontSize, ThemeKeys.TextPrimary, FontWeights.SemiBold);
            icon.Margin = new Thickness(0, 0, 4, 0);
            _row.Children.Add(icon);
            Icon = icon;
        }

        _label = UiKit.Text(text, fontSize, FontWeights.Medium, ThemeKeys.TextPrimary);
        _label.FontFamily = UiKit.DisplayFont;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _row.Children.Add(_label);
        Child = _row;
        MouseLeftButtonUp += (_, _) => click?.Invoke();
        IsSelected = selected;
    }

    public TextBlock? Icon { get; }

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            this.Themed(BackgroundProperty, value ? ThemeKeys.Accent : ThemeKeys.ControlFill);
            var key = value ? ThemeKeys.OnAccent : ThemeKeys.TextPrimary;
            _label.SetResourceReference(TextBlock.ForegroundProperty, key);
            Icon?.SetResourceReference(TextBlock.ForegroundProperty, key);
        }
    }

    public bool Rounded => _rounded;
}

/// <summary>The Keep awake views: tray panel card, the two settings cards and the shared pieces (icon picker, automation editor, Until picker).</summary>
internal static class KeepAwakeViews
{
    private static (string Id, string Name)[] Icons => _loc_Icons.Value;

    private static readonly LocValue<(string Id, string Name)[]> _loc_Icons = new(() => [
        ("vorssaint", "Trayloom"), ("coffee", Loc.T("Coffee")), ("eye", Loc.T("Eye")), ("moon", Loc.T("Moon")), ("light", Loc.T("Lightbulb")),
    ]);

    private static (string Id, string Name, string Hex)[] Tints => _loc_Tints.Value;

    private static readonly LocValue<(string Id, string Name, string Hex)[]> _loc_Tints = new(() => [
        ("orange", Loc.T("Orange"), "#FF9500"), ("green", Loc.T("Green"), "#34C759"), ("blue", Loc.T("Blue"), "#0A84FF"),
        ("purple", Loc.T("Purple"), "#BF5AF2"), ("pink", Loc.T("Pink"), "#FF375F"), ("none", Loc.T("No color"), ""),
    ]);

    // ------------------------------------------------------------------------------------------------ panel card

    public static FrameworkElement PanelCard(IContributionServices c)
    {
        var service = KeepAwakeService.Instance;
        var card = new PanelCard();
        if (service is null)
        {
            card.Child = UiKit.Text(Strings.KeepAwake.Idle, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
            return card;
        }

        var app = c.App;
        var stack = new StackPanel();

        // 1. Status row.
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = UiKit.Text(service.StatusText, 11, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: false, trimming: TextTrimming.CharacterEllipsis);
        status.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(status);
        var extend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var (label, minutes) in new[] { ("+15m", 15), ("+30m", 30), ("+1h", 60) })
        {
            var m = minutes;
            var chip = new Chip(label, () => service.Extend(m)) { Margin = new Thickness(3, 0, 0, 0) };
            extend.Children.Add(chip);
        }

        Grid.SetColumn(extend, 1);
        top.Children.Add(extend);
        var toggle = new MacSwitch(service.IsActive, SwitchSize.Regular, on =>
        {
            if (on != service.IsActive)
            {
                service.Toggle();
            }
        });
        Grid.SetColumn(toggle, 2);
        top.Children.Add(toggle);
        stack.Children.Add(top);

        // 2. Chips (two rows of four at 308 pt).
        var chips = new List<(int Minutes, Chip Chip, string Short)>();
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var d in Strings.KeepAwake.Durations)
        {
            var minutes = d.Minutes;
            var chip = new Chip(d.Short, () => OnChip(service, minutes), tooltip: d.Name, height: 24) { Margin = new Thickness(0, 0, 4, 4) };
            chips.Add((minutes, chip, d.Short));
            grid.Children.Add(chip);
        }

        var until = new Chip(Strings.KeepAwake.Until, null, glyph: Gl.Clock, height: 24) { Margin = new Thickness(0, 0, 4, 4) };
        until.MouseLeftButtonUp += (_, _) =>
        {
            if (service.IsActive && service.UntilSession)
            {
                service.Deactivate();
            }
            else
            {
                new UntilPopup(app, service, until).Open();
            }
        };
        grid.Children.Add(until);
        stack.Children.Add(grid);

        // 3. Hint.
        var hint = UiKit.Text(Strings.KeepAwake.ChipHint, 10, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true);
        hint.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(hint);

        // 4. Options disclosure.
        var options = Bits.Disclosure(Strings.KeepAwake.Options, () => OptionsBody(c, service, compact: true), expanded: false, indent: 0);
        options.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(options);

        // 5/6. Divider and closed-lid row.
        stack.Children.Add(UiKit.Divider());
        ((FrameworkElement)stack.Children[^1]).Margin = new Thickness(0, 10, 0, 10);
        var lidCaption = UiKit.Text(string.Empty, 10, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        var lidRow = new Grid();
        lidRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lidRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lidText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lidText.Children.Add(UiKit.Text(Strings.KeepAwake.LidClosed, 12, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true));
        lidText.Children.Add(lidCaption);
        lidRow.Children.Add(lidText);
        var lidSwitch = new MacSwitch(app.Get<bool>("clamshellPreferred"), SwitchSize.Mini, on => service.SetClamshellPreferred(on)) { Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(lidSwitch, 1);
        lidRow.Children.Add(lidSwitch);
        stack.Children.Add(lidRow);

        void Refresh()
        {
            status.Text = service.StatusText;
            if (toggle.IsOn != service.IsActive)
            {
                toggle.IsOn = service.IsActive;
            }

            extend.Visibility = service.IsActive && service.EndsAt is not null && service.Kind == SessionKind.Manual ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (minutes, chip, shortText) in chips)
            {
                var selected = service.IsActive && service.Kind == SessionKind.Manual && !service.UntilSession && service.SelectedMinutes == minutes;
                chip.IsSelected = selected;
                chip.Text = selected && service.EndsAt is { } end ? Countdown(end - DateTime.Now) : shortText;
            }

            var untilOn = service.IsActive && service.UntilSession;
            until.IsSelected = untilOn;
            until.Text = untilOn && service.EndsAt is { } e ? e.ToString("t", CultureInfo.CurrentCulture) : Strings.KeepAwake.Until;
            if (service.BatteryBlocked && !service.IsActive)
            {
                hint.Text = Strings.KeepAwake.BatteryBlocked(service.BatteryPercent);
                hint.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Orange);
            }
            else
            {
                hint.Text = Strings.KeepAwake.ChipHint;
                hint.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextTertiary);
            }

            lidCaption.Text = service.Lid switch
            {
                LidState.Configuring => Strings.KeepAwake.LidConfiguring,
                LidState.Failed => Strings.KeepAwake.LidFailed,
                _ when service.IsActive && app.Get<bool>("clamshellPreferred") => Strings.KeepAwake.LidOn,
                _ when app.Get<bool>("clamshellPreferred") => Strings.KeepAwake.LidNeedsSession,
                _ => Strings.KeepAwake.LidReady,
            };
            lidCaption.SetResourceReference(TextBlock.ForegroundProperty, service.Lid == LidState.Failed ? ThemeKeys.Red : ThemeKeys.TextSecondary);
            if (lidSwitch.IsOn != app.Get<bool>("clamshellPreferred"))
            {
                lidSwitch.IsOn = app.Get<bool>("clamshellPreferred");
            }
        }

        Refresh();
        card.Child = stack;
        Subscribe(card, service, Refresh, c);
        return card;
    }

    private static void OnChip(KeepAwakeService service, int minutes)
    {
        if (service.IsActive && service.Kind == SessionKind.Manual && !service.UntilSession && service.SelectedMinutes == minutes)
        {
            service.Deactivate();
        }
        else
        {
            service.ActivateChip(minutes);
        }
    }

    private static string Countdown(TimeSpan span)
    {
        var total = Math.Max(0, (int)Math.Ceiling(span.TotalSeconds));
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    private static void Subscribe(FrameworkElement owner, KeepAwakeService service, Action refresh, IContributionServices c)
    {
        void Changed() => owner.Dispatcher.BeginInvoke(refresh);
        void SettingChanged(string key)
        {
            if (key.StartsWith("keepAwake", StringComparison.Ordinal) || key is "clamshellPreferred" or "batteryLimitPercent" or "defaultDurationMinutes")
            {
                owner.Dispatcher.BeginInvoke(refresh);
            }
        }

        owner.Loaded += (_, _) =>
        {
            service.Changed += Changed;
            c.App.Changed += SettingChanged;
            refresh();
        };
        owner.Unloaded += (_, _) =>
        {
            service.Changed -= Changed;
            c.App.Changed -= SettingChanged;
        };
    }

    // ------------------------------------------------------------------------------------------------ options (panel and settings)

    private static FrameworkElement OptionsBody(IContributionServices c, KeepAwakeService service, bool compact)
    {
        var app = c.App;
        var stack = new StackPanel();
        stack.Children.Add(IconPicker(app, compact: true));
        stack.AddSpaced(Bits.ToggleLine(Strings.KeepAwake.AllowDisplaySleep, Strings.KeepAwake.AllowDisplaySleepCaption, app.Get<bool>("keepAwakeAllowDisplaySleep"), on => app.Set("keepAwakeAllowDisplaySleep", on), glyph: Gl.Display), 9);
        stack.AddSpaced(Bits.ToggleLine(Strings.KeepAwake.AutoStart, Strings.KeepAwake.AutoStartCaption, app.Get<bool>("keepAwakeAutoStart"), on => app.Set("keepAwakeAutoStart", on), glyph: Gl.PlayCircle), 9);

        var summary = AutomationSummary(app);
        var automation = Bits.Disclosure(Strings.KeepAwake.Automation, () => AutomationBody(c, compact: true), expanded: false, indent: 22, fontSize: 11, leading: UiKit.Icon(Gl.Bolt, 10, ThemeKeys.TextSecondary), trailing: summary, chevronSize: 8.5);
        stack.AddSpaced(automation, 9);

        var jiggle = new StackPanel();
        var interval = IntervalRow(app, compact: true);
        interval.Visibility = app.Get<bool>("keepAwakeMouseJiggleEnabled") ? Visibility.Visible : Visibility.Collapsed;
        jiggle.Children.Add(Bits.ToggleLine(Strings.KeepAwake.MovePointer, Strings.KeepAwake.MovePointerCaption, app.Get<bool>("keepAwakeMouseJiggleEnabled"), on =>
        {
            app.Set("keepAwakeMouseJiggleEnabled", on);
            interval.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }, glyph: Gl.Cursor));
        jiggle.AddSpaced(interval, 6);
        stack.AddSpaced(jiggle, 9);
        return stack;
    }

    private static UIElement AutomationSummary(IAppSettings app)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var any = false;
        void Add(string glyph, bool on)
        {
            if (!on)
            {
                return;
            }

            any = true;
            var circle = UiKit.IconTile(glyph, 17, 9, 8.5);
            circle.Margin = new Thickness(2, 0, 0, 0);
            row.Children.Add(circle);
        }

        Add(Gl.Display, app.Get<bool>("keepAwakeExternalDisplay"));
        Add(Gl.Plug, app.Get<bool>("keepAwakeConnectedToPower"));
        Add(Gl.AllApps, app.Get<bool>("keepAwakeRunningApps"));
        Add(Gl.Lock, app.Get<bool>("keepAwakePauseWhenLocked"));
        if (!any)
        {
            row.Children.Add(UiKit.Text(Strings.KeepAwake.AutomationOff, 9.5, FontWeights.Normal, ThemeKeys.TextTertiary));
        }

        return row;
    }

    private static FrameworkElement AutomationBody(IContributionServices c, bool compact)
    {
        var app = c.App;
        var stack = new StackPanel();
        stack.Children.Add(AutomationEditor(c, compact));
        stack.AddSpaced(Bits.ToggleLine(Strings.KeepAwake.PauseWhenLocked, Strings.KeepAwake.PauseWhenLockedCaption, app.Get<bool>("keepAwakePauseWhenLocked"), on => app.Set("keepAwakePauseWhenLocked", on), glyph: Gl.Lock), 9);
        return stack;
    }

    private static Grid IntervalRow(IAppSettings app, bool compact)
    {
        var values = new[] { 1, 2, 5, 10, 15 };
        var current = Array.IndexOf(values, app.Get<int>("keepAwakeMouseJiggleIntervalMinutes"));
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(UiKit.Text(Strings.KeepAwake.Interval, compact ? 10.5 : 12, FontWeights.Normal, ThemeKeys.TextSecondary));
        var combo = Bits.MiniCombo(values.Select(v => $"{v} min").ToList(), current < 0 ? 2 : current, i => app.Set("keepAwakeMouseJiggleIntervalMinutes", values[i]), 84);
        Grid.SetColumn(combo, 1);
        row.Children.Add(combo);
        row.Margin = new Thickness(compact ? 23 : 0, 0, 0, 0);
        return row;
    }

    // ------------------------------------------------------------------------------------------------ icon picker

    public static FrameworkElement IconPicker(IAppSettings app, bool compact)
    {
        var card = new Border { CornerRadius = new CornerRadius(compact ? 9 : 0), Padding = new Thickness(compact ? 8 : 0) };
        if (compact)
        {
            card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        }

        var stack = new StackPanel();
        var iconName = UiKit.Text(string.Empty, 11, FontWeights.Normal, ThemeKeys.TextTertiary);
        var tintName = UiKit.Text(string.Empty, 11, FontWeights.Normal, ThemeKeys.TextTertiary);
        stack.Children.Add(HeaderLine(Strings.KeepAwake.ActiveIcon, iconName, compact));
        var iconRow = new UniformGrid { Rows = 1, Columns = Icons.Length, Margin = new Thickness(0, 6, 0, 0) };
        var tintRow = new UniformGrid { Rows = 1, Columns = Tints.Length, Margin = new Thickness(0, 6, 0, 0) };
        var iconCells = new List<(string Id, Border Cell)>();
        var tintCells = new List<(string Id, Border Cell)>();

        void Paint()
        {
            var icon = app.Get<string>("keepAwakeActiveIcon");
            var tint = app.Get<string>("keepAwakeIconTint");
            iconName.Text = Icons.FirstOrDefault(i => i.Id == icon).Name ?? "Trayloom";
            tintName.Text = Tints.FirstOrDefault(t => t.Id == tint).Name ?? Loc.T("Orange");
            foreach (var (id, cell) in iconCells)
            {
                Select(cell, id == icon);
            }

            foreach (var (id, cell) in tintCells)
            {
                Select(cell, id == tint);
            }

            // Re-render the glyph previews in the chosen tint.
            foreach (var (id, cell) in iconCells)
            {
                cell.Child = IconPreview(id, tint);
            }
        }

        foreach (var (id, name) in Icons)
        {
            var captured = id;
            var cell = new Border { Height = compact ? 29 : 35, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 5, 0), Cursor = Cursors.Hand, ToolTip = name, BorderThickness = new Thickness(1.2) };
            cell.MouseLeftButtonUp += (_, _) =>
            {
                app.Set("keepAwakeActiveIcon", captured);
                Paint();
            };
            iconCells.Add((id, cell));
            iconRow.Children.Add(cell);
        }

        stack.Children.Add(iconRow);
        var tintHeader = HeaderLine(Strings.KeepAwake.ActiveIconColor, tintName, compact);
        tintHeader.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(tintHeader);
        foreach (var (id, name, hex) in Tints)
        {
            var captured = id;
            var cell = new Border { Height = compact ? 24 : 28, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, ToolTip = name, BorderThickness = new Thickness(1.1) };
            cell.Child = Swatch(hex);
            cell.MouseLeftButtonUp += (_, _) =>
            {
                app.Set("keepAwakeIconTint", captured);
                Paint();
            };
            tintCells.Add((id, cell));
            tintRow.Children.Add(cell);
        }

        stack.Children.Add(tintRow);
        card.Child = stack;
        Paint();
        return card;
    }

    private static Grid HeaderLine(string title, TextBlock value, bool compact)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(UiKit.Text(title, compact ? 11 : 12, FontWeights.Medium, ThemeKeys.TextSecondary));
        Grid.SetColumn(value, 1);
        g.Children.Add(value);
        return g;
    }

    private static void Select(Border cell, bool selected)
    {
        if (selected)
        {
            cell.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill18);
            cell.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }
        else
        {
            cell.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            cell.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        }
    }

    private static UIElement Swatch(string hex)
    {
        var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 14, Height = 14 };
        if (string.IsNullOrEmpty(hex))
        {
            var ring = new Ellipse { StrokeThickness = 1.3, Width = 14, Height = 14 };
            ring.SetResourceReference(Shape.StrokeProperty, ThemeKeys.TextSecondary);
            var slash = new Line { X1 = 2, Y1 = 12, X2 = 12, Y2 = 2, StrokeThickness = 1.3 };
            slash.SetResourceReference(Shape.StrokeProperty, ThemeKeys.TextSecondary);
            holder.Children.Add(ring);
            holder.Children.Add(slash);
        }
        else
        {
            holder.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) });
        }

        return holder;
    }

    private static UIElement IconPreview(string id, string tint)
    {
        Brush? tinted = null;
        var hex = Tints.FirstOrDefault(t => t.Id == tint).Hex;
        if (!string.IsNullOrEmpty(hex))
        {
            tinted = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        FrameworkElement element;
        if (id == "vorssaint")
        {
            var mark = BrandMark.Create(22);
            if (tinted is not null)
            {
                mark.Fill = tinted;
            }

            element = mark;
        }
        else if (id == "coffee")
        {
            var tb = new TextBlock { Text = "☕︎", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 16 };
            ApplyBrush(tb, tinted);
            element = tb;
        }
        else
        {
            var glyph = id switch { "eye" => Gl.Eye, "moon" => Gl.Moon, _ => "" };
            var tb = new TextBlock { Text = glyph, FontFamily = UiKit.IconFont, FontSize = 16 };
            ApplyBrush(tb, tinted);
            element = tb;
        }

        element.HorizontalAlignment = HorizontalAlignment.Center;
        element.VerticalAlignment = VerticalAlignment.Center;
        return element;
    }

    private static void ApplyBrush(TextBlock block, Brush? tint)
    {
        if (tint is not null)
        {
            block.Foreground = tint;
        }
        else
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
        }
    }

    // ------------------------------------------------------------------------------------------------ automation

    public static FrameworkElement AutomationEditor(IContributionServices c, bool compact)
    {
        var app = c.App;
        var stack = new StackPanel();
        var tilesRow = new UniformGrid { Rows = 1, Columns = 3 };
        var tiles = new List<(string Key, Border Tile, TextBlock Icon, TextBlock Check, TextBlock Title)>();
        var matchHost = new ContentControl { Margin = new Thickness(0, 8, 0, 0) };
        var appsHost = new ContentControl { Margin = new Thickness(0, 8, 0, 0) };

        void Rebuild()
        {
            var selectedCount = tiles.Count(t => app.Get<bool>(t.Key));
            foreach (var (key, tile, icon, check, title) in tiles)
            {
                var on = app.Get<bool>(key);
                tile.SetResourceReference(Border.BackgroundProperty, on ? ThemeKeys.AccentFill12 : ThemeKeys.ControlFill);
                tile.SetResourceReference(Border.BorderBrushProperty, on ? ThemeKeys.Accent : ThemeKeys.Divider);
                icon.SetResourceReference(TextBlock.ForegroundProperty, on ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
                check.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }

            matchHost.Content = null;
            if (selectedCount > 1)
            {
                var seg = new SegmentedControl([Strings.KeepAwake.Any, Strings.KeepAwake.All], app.Get<bool>("keepAwakeAutomationRequireAll") ? 1 : 0, false, 11);
                seg.SelectionChanged += i => app.Set("keepAwakeAutomationRequireAll", i == 1);
                matchHost.Content = seg;
            }

            appsHost.Content = app.Get<bool>("keepAwakeRunningApps") ? AppList(app) : null;
        }

        foreach (var (key, glyph, title) in new[] { ("keepAwakeExternalDisplay", Gl.Display, Strings.KeepAwake.ExternalDisplay), ("keepAwakeConnectedToPower", Gl.Plug, Strings.KeepAwake.Power), ("keepAwakeRunningApps", Gl.AllApps, Strings.KeepAwake.Applications) })
        {
            var k = key;
            var tile = new Border { CornerRadius = new CornerRadius(compact ? 9 : 11), Padding = new Thickness(compact ? 6 : 9), MinHeight = compact ? 65 : 78, Margin = new Thickness(0, 0, compact ? 6 : 8, 0), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var inner = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = UiKit.Icon(glyph, compact ? 14 : 17, ThemeKeys.TextSecondary, FontWeights.SemiBold);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            head.Children.Add(icon);
            var check = UiKit.Icon(Gl.CheckCircle, 13, ThemeKeys.Accent);
            Grid.SetColumn(check, 1);
            head.Children.Add(check);
            inner.Children.Add(head);
            var label = UiKit.Text(title, compact ? 11 : 12, FontWeights.Medium, ThemeKeys.TextPrimary, wrap: true);
            label.Margin = new Thickness(0, compact ? 5 : 7, 0, 0);
            inner.Children.Add(label);
            tile.Child = inner;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                app.Set(k, !app.Get<bool>(k));
                Rebuild();
            };
            tiles.Add((key, tile, icon, check, label));
            tilesRow.Children.Add(tile);
        }

        stack.Children.Add(tilesRow);
        stack.Children.Add(matchHost);
        stack.Children.Add(appsHost);
        Rebuild();
        return stack;
    }

    private static FrameworkElement AppList(IAppSettings app)
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(Strings.KeepAwake.SelectedApps, 11, FontWeights.SemiBold, ThemeKeys.TextSecondary));
        var rows = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        void Render()
        {
            rows.Children.Clear();
            foreach (var entry in app.Get<List<string>>("keepAwakeRunningAppBundleIDs"))
            {
                var path = entry;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = UiKit.Text(System.IO.Path.GetFileNameWithoutExtension(path), 11, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
                name.ToolTip = path;
                row.Children.Add(name);
                var remove = Bits.FlatText(Strings.KeepAwake.RemoveApp, () =>
                {
                    app.Set("keepAwakeRunningAppBundleIDs", app.Get<List<string>>("keepAwakeRunningAppBundleIDs").Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList());
                    Render();
                }, 10, ThemeKeys.TextSecondary);
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                rows.Children.Add(row);
            }
        }

        Render();
        stack.Children.Add(rows);
        var add = Bits.Mini(Strings.KeepAwake.AddApp, () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Applications (*.exe)|*.exe", Title = Strings.KeepAwake.AddApp };
            if (dialog.ShowDialog() == true)
            {
                var list = app.Get<List<string>>("keepAwakeRunningAppBundleIDs");
                if (!list.Any(p => string.Equals(p, dialog.FileName, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(dialog.FileName);
                    app.Set("keepAwakeRunningAppBundleIDs", list);
                    Render();
                }
            }
        });
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(add);
        var caption = Bits.Caption(Strings.KeepAwake.AppsCaption, 10);
        caption.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(caption);
        return stack;
    }

    // ------------------------------------------------------------------------------------------------ settings cards

    /// <summary>Settings card 1 (no title): session row, default duration chips, automation, pause while locked.</summary>
    public static FrameworkElement SettingsCard1(ISettingsContext c)
    {
        var service = KeepAwakeService.Instance;
        var app = c.App;
        var card = new SettingsCard();
        var toggle = new MacSwitch(service?.IsActive == true, SwitchSize.Regular, on =>
        {
            if (service is not null && on != service.IsActive)
            {
                service.Toggle();
            }
        });
        var row = new SettingsRow(Gl.Moon, Strings.KeepAwake.Title, service?.StatusText ?? Strings.KeepAwake.Idle, toggle);
        card.Add(row);

        var chips = new List<(int Minutes, Chip Chip)>();
        var wrap = new WrapPanel();
        var current = app.Get<int>("defaultDurationMinutes");
        foreach (var d in Strings.KeepAwake.Durations)
        {
            var minutes = d.Minutes;
            var chip = new Chip(d.Label, null, current == minutes, height: 28, radius: 8, tooltip: d.Name) { Margin = new Thickness(0, 0, 6, 6), MinWidth = 84 };
            chip.MouseLeftButtonUp += (_, _) =>
            {
                app.Set("defaultDurationMinutes", minutes);
                app.Set("keepAwakeSwitchUsesUntil", false);
                foreach (var (m, ch) in chips)
                {
                    ch.IsSelected = m == minutes;
                }
            };
            chips.Add((minutes, chip));
            wrap.Children.Add(chip);
        }

        var durations = new StackPanel();
        durations.Children.Add(UiKit.Text(Strings.KeepAwake.DefaultDuration, 11.5, FontWeights.Medium, ThemeKeys.TextSecondary));
        wrap.Margin = new Thickness(0, 8, 0, 0);
        durations.Children.Add(wrap);
        card.Add(durations);
        card.AddDivider();

        var automation = new StackPanel();
        automation.Children.Add(UiKit.Text(Strings.KeepAwake.Automation, 11.5, FontWeights.Medium, ThemeKeys.TextSecondary));
        var caption = UiKit.Text(app.Get<bool>("keepAwakeAutomationRequireAll") ? Strings.KeepAwake.AutomationCaptionAll : Strings.KeepAwake.AutomationCaptionAny, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.Margin = new Thickness(0, 4, 0, 8);
        automation.Children.Add(caption);
        automation.Children.Add(AutomationEditor(c, compact: false));
        card.Add(automation);
        c.App.Changed += key =>
        {
            if (key == "keepAwakeAutomationRequireAll")
            {
                caption.Dispatcher.BeginInvoke(() => caption.Text = app.Get<bool>(key) ? Strings.KeepAwake.AutomationCaptionAll : Strings.KeepAwake.AutomationCaptionAny);
            }
        };
        card.Add(new SettingsRow(Gl.Lock, Strings.KeepAwake.PauseWhenLocked, Strings.KeepAwake.PauseWhenLockedCaption, new MacSwitch(app.Get<bool>("keepAwakePauseWhenLocked"), SwitchSize.Regular, on => app.Set("keepAwakePauseWhenLocked", on))));

        if (service is not null)
        {
            void Refresh()
            {
                row.Caption = service.StatusText;
                if (toggle.IsOn != service.IsActive)
                {
                    toggle.IsOn = service.IsActive;
                }
            }

            void Changed() => card.Dispatcher.BeginInvoke(Refresh);
            card.Loaded += (_, _) => service.Changed += Changed;
            card.Unloaded += (_, _) => service.Changed -= Changed;
        }

        return card;
    }

    /// <summary>Settings card 2 ("Options").</summary>
    public static FrameworkElement SettingsOptions(ISettingsContext c)
    {
        var app = c.App;
        var service = KeepAwakeService.Instance;
        var card = new SettingsCard(Strings.KeepAwake.Options);
        card.Add(Row(Gl.PlayCircle, Strings.KeepAwake.AutoStart, Strings.KeepAwake.AutoStartCaption, "keepAwakeAutoStart", app));
        card.Add(Row(Gl.Cursor, Strings.KeepAwake.RightClick, Strings.KeepAwake.RightClickCaption, "keepAwakeRightClickToggle", app));
        card.Add(Row(Gl.Timer, Strings.KeepAwake.ShowCountdown, null, "showCountdownInMenuBar", app));
        card.Add(Row(Gl.Display, Strings.KeepAwake.AllowDisplaySleep, Strings.KeepAwake.AllowDisplaySleepCaption, "keepAwakeAllowDisplaySleep", app));

        var jiggleHost = new StackPanel();
        var interval = IntervalRow(app, compact: false);
        interval.Margin = new Thickness(SettingsRow.TextInset, 8, 0, 0);
        interval.Visibility = app.Get<bool>("keepAwakeMouseJiggleEnabled") ? Visibility.Visible : Visibility.Collapsed;
        jiggleHost.Children.Add(new SettingsRow(Gl.Cursor, Strings.KeepAwake.MovePointer, Strings.KeepAwake.MovePointerCaption, new MacSwitch(app.Get<bool>("keepAwakeMouseJiggleEnabled"), SwitchSize.Regular, on =>
        {
            app.Set("keepAwakeMouseJiggleEnabled", on);
            interval.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        })));
        jiggleHost.Children.Add(interval);
        card.Add(jiggleHost);

        card.AddDivider();
        card.Add(IconPicker(app, compact: false));

        if (PowerStatus.HasBattery)
        {
            card.AddDivider();
            var values = new[] { 0, 5, 10, 15, 20 };
            var index = Array.IndexOf(values, app.Get<int>("batteryLimitPercent"));
            var combo = Bits.MiniCombo(values.Select(v => v == 0 ? Strings.KeepAwake.Never : $"{v}%").ToList(), index < 0 ? 2 : index, i => app.Set("batteryLimitPercent", values[i]), 96, 12);
            card.Add(new SettingsRow(Gl.BatteryLow, Strings.KeepAwake.BatteryLimit, Strings.KeepAwake.BatteryLimitCaption, combo));
        }

        card.AddDivider();
        var lidHost = new StackPanel();
        var lidRow = new SettingsRow(Gl.Laptop, Strings.KeepAwake.LidClosed, Strings.KeepAwake.LidExplanation, null);
        var lidSwitch = new MacSwitch(app.Get<bool>("clamshellPreferred"), SwitchSize.Regular, on => service?.SetClamshellPreferred(on));
        var lidRowFull = new SettingsRow(Gl.Laptop, Strings.KeepAwake.LidClosed, Strings.KeepAwake.LidExplanation, lidSwitch);
        lidHost.Children.Add(lidRowFull);
        var failure = UiKit.Text(Strings.KeepAwake.LidFailed, 11.5, FontWeights.Normal, ThemeKeys.Red, wrap: true);
        failure.Margin = new Thickness(SettingsRow.TextInset, 6, 0, 0);
        failure.Visibility = service?.Lid == LidState.Failed ? Visibility.Visible : Visibility.Collapsed;
        lidHost.Children.Add(failure);
        var dimHost = new ContentControl { Margin = new Thickness(SettingsRow.TextInset, 10, 0, 0) };
        void Dim() => dimHost.Content = app.Get<bool>("clamshellPreferred")
            ? new SettingsRow(Gl.Sun, Strings.KeepAwake.DimDisplay, Strings.KeepAwake.DimDisplayCaption, new MacSwitch(app.Get<bool>("dimScreenOnLidClose"), SwitchSize.Regular, on => app.Set("dimScreenOnLidClose", on)))
            : null;
        Dim();
        lidHost.Children.Add(dimHost);
        card.Add(lidHost);
        _ = lidRow;

        void Refresh()
        {
            failure.Visibility = service?.Lid == LidState.Failed ? Visibility.Visible : Visibility.Collapsed;
            lidSwitch.IsEnabled = service?.Lid != LidState.Configuring;
            if (lidSwitch.IsOn != app.Get<bool>("clamshellPreferred"))
            {
                lidSwitch.IsOn = app.Get<bool>("clamshellPreferred");
            }

            Dim();
        }

        if (service is not null)
        {
            void Changed() => card.Dispatcher.BeginInvoke(Refresh);
            card.Loaded += (_, _) => service.Changed += Changed;
            card.Unloaded += (_, _) => service.Changed -= Changed;
        }

        return card;
    }

    private static SettingsRow Row(string glyph, string title, string? caption, string key, IAppSettings app) =>
        new(glyph, title, caption, new MacSwitch(app.Get<bool>(key), SwitchSize.Regular, on => app.Set(key, on)));
}

/// <summary>The Until picker popover (224 wide): two scroll wheels (hour, minute), the resolved day and a Start button.</summary>
internal sealed class UntilPopup
{
    private readonly IAppSettings _app;
    private readonly KeepAwakeService _service;
    private readonly FrameworkElement _anchor;
    private readonly Popup _popup;
    private int _hour;
    private int _minute;
    private TextBlock _summary = null!;
    private TextBlock _hourText = null!;
    private TextBlock _hourPrev = null!;
    private TextBlock _hourNext = null!;
    private TextBlock _minText = null!;
    private TextBlock _minPrev = null!;
    private TextBlock _minNext = null!;

    public UntilPopup(IAppSettings app, KeepAwakeService service, FrameworkElement anchor)
    {
        _app = app;
        _service = service;
        _anchor = anchor;
        var saved = app.Get<double>("keepAwakeUntilTime");
        var start = saved > 0 && KeepAwakeService.FromRefSeconds(saved) > DateTime.Now ? KeepAwakeService.FromRefSeconds(saved) : DateTime.Now.AddHours(1);
        _hour = start.Hour;
        _minute = saved > 0 ? start.Minute : 0;
        _popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
    }

    public void Open()
    {
        var panel = new StackPanel { Width = 204 };
        panel.Children.Add(UiKit.Text(Strings.KeepAwake.UntilHeader, 11, FontWeights.SemiBold, ThemeKeys.TextPrimary));

        var wheels = new Grid { Margin = new Thickness(0, 10, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        wheels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wheels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        wheels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hourWheel = Wheel(Strings.KeepAwake.Hour, 24, () => _hour, v => _hour = v, out _hourPrev, out _hourText, out _hourNext);
        var minuteWheel = Wheel(Strings.KeepAwake.Minute, 60, () => _minute, v => _minute = v, out _minPrev, out _minText, out _minNext);
        var colon = UiKit.Text(":", 24, FontWeights.Normal, ThemeKeys.TextTertiary);
        colon.HorizontalAlignment = HorizontalAlignment.Center;
        colon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(colon, 1);
        Grid.SetColumn(minuteWheel, 2);
        wheels.Children.Add(hourWheel);
        wheels.Children.Add(colon);
        wheels.Children.Add(minuteWheel);
        panel.Children.Add(wheels);
        panel.Children.Add(UiKit.Divider());

        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _summary = UiKit.Text(string.Empty, 10, FontWeights.Normal, ThemeKeys.TextSecondary);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_summary);
        var start = UiKit.Button(Strings.KeepAwake.Start, ButtonKind.Prominent, 11, null, () =>
        {
            _service.ActivateUntil(Resolve());
            _popup.IsOpen = false;
        });
        Grid.SetColumn(start, 1);
        footer.Children.Add(start);
        panel.Children.Add(footer);

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Margin = new Thickness(8),
            BorderThickness = new Thickness(0.7),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 },
        };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
        card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        _popup.Child = card;
        Refresh();
        _popup.IsOpen = true;
    }

    private DateTime Resolve()
    {
        var now = DateTime.Now;
        var target = new DateTime(now.Year, now.Month, now.Day, _hour, _minute, 0);
        return target <= now ? target.AddDays(1) : target;
    }

    private void Refresh()
    {
        _hourText.Text = _hour.ToString("00");
        _hourPrev.Text = ((_hour + 23) % 24).ToString("00");
        _hourNext.Text = ((_hour + 1) % 24).ToString("00");
        _minText.Text = _minute.ToString("00");
        _minPrev.Text = ((_minute + 59) % 60).ToString("00");
        _minNext.Text = ((_minute + 1) % 60).ToString("00");
        var target = Resolve();
        var day = target.Date == DateTime.Today ? Loc.T("Today") : "Tomorrow";
        _summary.Text = Strings.KeepAwake.UntilSummary(day, target - DateTime.Now);
    }

    private Border Wheel(string tooltip, int modulo, Func<int> get, Action<int> set, out TextBlock prev, out TextBlock current, out TextBlock next)
    {
        var p = UiKit.Text("00", 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        var n = UiKit.Text("00", 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        var value = UiKit.Text("00", 26, FontWeights.Medium, ThemeKeys.Accent);
        value.FontFamily = UiKit.DisplayFont;
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        p.HorizontalAlignment = n.HorizontalAlignment = HorizontalAlignment.Center;
        p.Cursor = n.Cursor = Cursors.Hand;
        p.Height = n.Height = 20;
        p.TextAlignment = n.TextAlignment = TextAlignment.Center;
        p.Padding = new Thickness(0, 2, 0, 0);
        n.Padding = new Thickness(0, 2, 0, 0);
        var box = new Border { Width = 52, Height = 36, CornerRadius = new CornerRadius(7), Child = value };
        box.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
        var stack = new StackPanel();
        stack.Children.Add(p);
        stack.Children.Add(box);
        stack.Children.Add(n);
        var host = new Border { Child = stack, Background = Brushes.Transparent, ToolTip = tooltip };
        void Step(int delta)
        {
            set(((get() + delta) % modulo + modulo) % modulo);
            Refresh();
        }

        host.MouseWheel += (_, e) =>
        {
            Step(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        };
        p.MouseLeftButtonUp += (_, _) => Step(-1);
        n.MouseLeftButtonUp += (_, _) => Step(1);
        host.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down)
            {
                Step(e.Key == Key.Up ? -1 : 1);
                e.Handled = true;
            }
        };
        prev = p;
        next = n;
        current = value;
        return host;
    }
}
