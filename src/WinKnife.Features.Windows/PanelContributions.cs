// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>Base of the Controls-tab rows of this project (Windows category): one card per row with its caption visible.</summary>
public abstract class WindowControlRow : IPanelSection
{
    /// <summary>Catalog id of the feature the row belongs to.</summary>
    public abstract string? FeatureId { get; }

    public string TabId => PanelTabs.Controls;

    public string? Category => PanelTabs.CategoryWindows;

    public bool SupportsEditing => true;

    public virtual int Order => 0;

    /// <summary>Item name used by the visibility key <c>panelControl&lt;Item&gt;</c>.</summary>
    protected abstract string Item { get; }

    protected abstract string Glyph { get; }

    protected abstract string Title { get; }

    protected abstract string Caption { get; }

    protected abstract string ToggleKey { get; }

    public (int Enabled, int Total) CategoryCounts => (Ambient.App is { } app && app.Get<bool>(ToggleKey) ? 1 : 0, 1);

    /// <summary>Called after the switch changed (exclusive rows turn the other one off).</summary>
    protected virtual void Toggled(IPanelContext context, bool on)
    {
    }

    /// <summary>Optional sub-row under the card (the large-icons switch of the switcher).</summary>
    protected virtual FrameworkElement? SubRow(IPanelContext context) => null;

    public FrameworkElement CreateView(IPanelContext context)
    {
        Ambient.App = context.App;
        Keys.Register(context.App);
        var visibleKey = Keys.PanelControl(Item);
        context.App.Register(visibleKey, true);
        var visible = context.App.Get<bool>(visibleKey);
        if (!visible && !context.IsEditing)
        {
            return new Border { Visibility = Visibility.Collapsed };
        }

        var stack = new StackPanel();
        var row = new PanelToggleRow(Glyph, Title, Caption, context.App.Get<bool>(ToggleKey), on =>
        {
            context.App.Set(ToggleKey, on);
            Toggled(context, on);
        }, showCaption: true);
        var group = new PanelRowGroup().Add(row);
        if (context.IsEditing)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(group);
            var eye = new Button
            {
                Content = UiKit.Icon(visible ? Glyphs.Eye : Glyphs.EyeOff, 14, visible ? ThemeKeys.Accent : ThemeKeys.TextTertiary),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
            };
            eye.Click += (_, _) =>
            {
                context.App.Set(visibleKey, !visible);
                context.RequestRelayout();
            };
            Grid.SetColumn(eye, 1);
            grid.Children.Add(eye);
            group.Opacity = visible ? 1 : 0.5;
            stack.Children.Add(grid);
        }
        else
        {
            stack.Children.Add(group);
            if (context.App.Get<bool>(ToggleKey) && SubRow(context) is { } sub)
            {
                stack.Children.Add(sub);
            }
        }

        return stack;
    }
}

public sealed class SwitcherControlRow : WindowControlRow
{
    public override string? FeatureId => "switcher";

    public override int Order => 0;

    protected override string Item => "Switcher";

    protected override string Glyph => "";

    protected override string Title => Strings.ControlSwitcher;

    protected override string Caption => Strings.SwitcherUseCaption;

    protected override string ToggleKey => Keys.SwitcherEnabled;

    protected override FrameworkElement? SubRow(IPanelContext context)
    {
        var shortcut = context.Shortcuts.GetHotkey("switcher")?.ToString() ?? "Alt+Tab";
        var simple = context.App.Get<bool>(Keys.SwitcherSimple);
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0), Opacity = simple ? 0.5 : 1 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { Margin = new Thickness(42, 0, 8, 0) };
        texts.Children.Add(UiKit.Text(string.Format(Strings.LargeIconsFormat, shortcut), 12, FontWeights.Medium, ThemeKeys.TextPrimary, wrap: true));
        texts.Children.Add(UiKit.Text(Strings.LargeIconsCaption, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
        grid.Children.Add(texts);
        var toggle = new MacSwitch(context.App.Get<bool>(Keys.SwitcherIconRow), SwitchSize.Mini, on => context.App.Set(Keys.SwitcherIconRow, on)) { IsEnabled = !simple, ToolTip = Strings.LargeIconsCaption, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }
}

public sealed class DockPreviewControlRow : WindowControlRow
{
    public override string? FeatureId => "dock-preview";

    public override int Order => 10;

    protected override string Item => "DockPreview";

    protected override string Glyph => "";

    protected override string Title => Strings.DockPreviewName;

    protected override string Caption => Strings.ControlDockPreviewCaption;

    protected override string ToggleKey => Keys.DockPreviewEnabled;
}

public sealed class DockClickControlRow : WindowControlRow
{
    public override string? FeatureId => "dock-click";

    public override int Order => 20;

    protected override string Item => "DockClick";

    protected override string Glyph => "";

    protected override string Title => Strings.ClickMinimize;

    protected override string Caption => Strings.ClickMinimizeCaption;

    protected override string ToggleKey => Keys.DockClickMinimize;

    protected override void Toggled(IPanelContext context, bool on)
    {
        if (on && context.App.Get<bool>(Keys.DockClickHide))
        {
            context.App.Set(Keys.DockClickHide, false);
            context.RequestRelayout();
        }
    }
}

public sealed class DockClickHideControlRow : WindowControlRow
{
    public override string? FeatureId => "dock-click";

    public override int Order => 21;

    protected override string Item => "DockClickHide";

    protected override string Glyph => "";

    protected override string Title => Strings.ClickHide;

    protected override string Caption => Strings.ClickHideCaption;

    protected override string ToggleKey => Keys.DockClickHide;

    protected override void Toggled(IPanelContext context, bool on)
    {
        if (on && context.App.Get<bool>(Keys.DockClickMinimize))
        {
            context.App.Set(Keys.DockClickMinimize, false);
            context.RequestRelayout();
        }
    }
}

public sealed class DockClickCycleControlRow : WindowControlRow
{
    public override string? FeatureId => "dock-click";

    public override int Order => 22;

    protected override string Item => "DockClickCycle";

    protected override string Glyph => "";

    protected override string Title => Strings.ClickCycle;

    protected override string Caption => Strings.ClickCycleCaption;

    protected override string ToggleKey => Keys.DockClickCycle;
}

public sealed class AutoQuitControlRow : WindowControlRow
{
    public override string? FeatureId => "auto-quit";

    public override int Order => 30;

    protected override string Item => "AutoQuit";

    protected override string Glyph => "";

    protected override string Title => Strings.ControlAutoQuit;

    protected override string Caption => Strings.AutoQuitEnableCaption;

    protected override string ToggleKey => Keys.AutoQuitEnabled;
}

/// <summary>The Window layout row of the Utilities tab: a hosted panel with the same actions, snap map and gesture options.</summary>
public sealed class WindowLayoutUtility : IPanelUtility
{
    public string? FeatureId => "window-layout";

    public int Order => 0;

    public string Id => "windowLayout";

    public string Title => Strings.LayoutPageTitle;

    public string Glyph => "";

    public string? Caption => Strings.LayoutIntro;

    public string? SettingsDestination => SettingsPages.WindowLayout;

    public FrameworkElement? CreateHostedView(IPanelContext context) => new WindowLayoutPanelView(context);
}

/// <summary>Hosted Window layout panel (spec 01-menu-panel 6.8), at most 308 wide.</summary>
internal sealed class WindowLayoutPanelView : StackPanel
{
    private readonly IPanelContext _context;
    private readonly IAppSettings _app;
    private readonly WindowLayoutService? _service;
    private readonly TextBlock _result;

    public WindowLayoutPanelView(IPanelContext context)
    {
        _context = context;
        _app = context.App;
        Keys.Register(_app);
        _service = context.Services.Get<WindowLayoutService>();
        MaxWidth = 308;

        // intro card: edge snap, shortcuts, gestures
        var intro = new StackPanel();
        var zone = new EdgeSnapZonePicker(_app, compact: true) { Margin = new Thickness(0, 8, 0, 0) };
        var conflict = SnapConflict.Build(_app);
        var snapCaption = Small(Strings.EdgeSnapCaption, 8);
        void Snap(bool on)
        {
            zone.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            conflict.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            _context.RequestRelayout();
        }

        intro.Children.Add(Check(Keys.EdgeSnapEnabled, Strings.EdgeSnap, Snap));
        intro.Children.Add(snapCaption);
        intro.Children.Add(zone);
        intro.Children.Add(conflict);
        zone.Visibility = conflict.Visibility = _app.Get<bool>(Keys.EdgeSnapEnabled) ? Visibility.Visible : Visibility.Collapsed;
        intro.Children.Add(UiKit.Divider());
        ((FrameworkElement)intro.Children[^1]).Margin = new Thickness(0, 10, 0, 10);
        intro.Children.Add(Small(Strings.LayoutIntro, 0));
        var shortcuts = Check(Keys.LayoutShortcutsEnabled, Strings.Shortcuts, _ => _context.RequestRelayout());
        shortcuts.Margin = new Thickness(0, 8, 0, 0);
        intro.Children.Add(shortcuts);
        intro.Children.Add(Small(Strings.ShortcutsCaption, 4));
        intro.Children.Add(UiKit.Divider());
        ((FrameworkElement)intro.Children[^1]).Margin = new Thickness(0, 10, 0, 10);
        var gesture = new StackPanel();
        void Gesture(bool on)
        {
            gesture.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            _context.RequestRelayout();
        }

        intro.Children.Add(Check(Keys.GestureEnabled, Strings.MoveResize, Gesture));
        BuildGesture(gesture);
        gesture.Visibility = _app.Get<bool>(Keys.GestureEnabled) ? Visibility.Visible : Visibility.Collapsed;
        intro.Children.Add(gesture);
        Children.Add(new PanelCard(intro, 10));

        // action groups
        foreach (var group in new[] { LayoutGroup.Halves, LayoutGroup.Thirds, LayoutGroup.QuarterRows, LayoutGroup.QuarterColumns, LayoutGroup.Sixths, LayoutGroup.Corners, LayoutGroup.Actions })
        {
            var hidden = (_app.Get<string>(Keys.HiddenActions) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
            var actions = LayoutActions.InGroup(group).Where(a => !hidden.Contains(a.Raw)).ToList();
            if (actions.Count == 0)
            {
                continue;
            }

            var card = new StackPanel();
            card.Children.Add(UiKit.Text(LayoutActions.GroupTitle(group).ToUpperInvariant(), 9.5, FontWeights.Bold, ThemeKeys.TextTertiary));
            var grid = new UniformGridPanel(2, 7) { Margin = new Thickness(0, 7, 0, 0) };
            foreach (var action in actions)
            {
                grid.Children.Add(ActionButton(action));
            }

            card.Children.Add(grid);
            var host = new PanelCard(card, 10) { Margin = new Thickness(0, 10, 0, 0) };
            Children.Add(host);
        }

        _result = UiKit.Text(string.Empty, 11, FontWeights.Medium, ThemeKeys.TextSecondary, wrap: true);
        var resultCard = new PanelCard(_result, 10) { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        Children.Add(resultCard);
        if (_service is not null)
        {
            void Show()
            {
                if (_service.LastResult is not { } r)
                {
                    return;
                }

                _result.Text = WindowLayoutEngine.Message(r);
                _result.SetResourceReference(TextBlock.ForegroundProperty, r is LayoutResult.Done or LayoutResult.Restored ? ThemeKeys.Green : ThemeKeys.Orange);
                resultCard.Visibility = Visibility.Visible;
                _context.RequestRelayout();
            }

            Loaded += (_, _) => _service.ResultChanged += Show;
            Unloaded += (_, _) => _service.ResultChanged -= Show;
        }
    }

    private static TextBlock Small(string text, double top)
    {
        var block = UiKit.Text(text, 9.5, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true);
        block.Margin = new Thickness(0, top, 0, 0);
        return block;
    }

    private FrameworkElement Check(string key, string title, Action<bool> changed)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(UiKit.Text(title, 12, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true));
        var toggle = new MacSwitch(_app.Get<bool>(key), SwitchSize.Small, on =>
        {
            _app.Set(key, on);
            changed(on);
        });
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    private void BuildGesture(StackPanel host)
    {
        host.Margin = new Thickness(0, 8, 0, 0);
        var tokens = GestureModifiers.Parse(_app.Get<string>(Keys.GestureModifiers));
        var row = new Grid();
        var hints = new StackPanel();
        void Draw()
        {
            row.Children.Clear();
            row.ColumnDefinitions.Clear();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(UiKit.Text(Strings.KeysToMove, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            var caps = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var token in new[] { "control", "option", "command" })
            {
                var selected = tokens.Contains(token);
                var last = selected && tokens.Count == 1;
                var captured = token;
                caps.Children.Add(FormUi.Keycap(GestureModifiers.Label(token), selected, !last, () =>
                {
                    if (!tokens.Add(captured))
                    {
                        tokens.Remove(captured);
                    }

                    _app.Set(Keys.GestureModifiers, GestureModifiers.Serialize(tokens));
                    Draw();
                }));
            }

            Grid.SetColumn(caps, 1);
            row.Children.Add(caps);
            hints.Children.Clear();
            var chord = string.Join('+', new[] { "control", "option", "command" }.Where(tokens.Contains).Select(GestureModifiers.Label));
            hints.Children.Add(Small($"{chord}  {Strings.DragToMove}", 0));
            hints.Children.Add(Small($"{chord}+Shift  {Strings.DragToResize}", 3));
        }

        Draw();
        host.Children.Add(row);
        host.Children.Add(hints);
        host.Children.Add(Small(Strings.ResizeHint, 6));
    }

    private FrameworkElement ActionButton(LayoutAction action)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = FormUi.ActionGlyph(action, 16, 11);
        glyph.Margin = new Thickness(0, 0, 6, 0);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(glyph);
        var title = UiKit.Text(action.Title, 10.5, FontWeights.SemiBold, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        content.Children.Add(title);
        if (_app.Get<bool>(Keys.LayoutShortcutsEnabled) && _context.Shortcuts.GetHotkey(action.RoleId) is { } hotkey)
        {
            var chip = UiKit.Text(hotkey.ToString(), 9, FontWeights.Medium, ThemeKeys.TextSecondary);
            chip.Margin = new Thickness(4, 0, 0, 0);
            chip.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chip, 2);
            content.Children.Add(chip);
        }

        var button = new Button { Content = content, MinHeight = 28, Padding = new Thickness(7, 3, 7, 3), HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = _service is not null };
        button.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonBordered);
        button.Click += (_, _) => _service?.Run(action);
        return button;
    }
}

/// <summary>Two-column grid with fixed spacing (the action grid of the hosted panel).</summary>
internal sealed class UniformGridPanel : Panel
{
    private readonly int _columns;
    private readonly double _gap;

    public UniformGridPanel(int columns, double gap)
    {
        _columns = columns;
        _gap = gap;
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 280 : available.Width;
        var cell = (width - _gap * (_columns - 1)) / _columns;
        double rowHeight = 0;
        var total = 0.0;
        var index = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(cell, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++index % _columns == 0)
            {
                total += rowHeight + _gap;
                rowHeight = 0;
            }
        }

        if (index % _columns != 0)
        {
            total += rowHeight + _gap;
        }

        return new Size(width, Math.Max(0, total - _gap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cell = (finalSize.Width - _gap * (_columns - 1)) / _columns;
        var y = 0.0;
        double rowHeight = 0;
        var index = 0;
        foreach (UIElement child in InternalChildren)
        {
            var column = index % _columns;
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            child.Arrange(new Rect(column * (cell + _gap), y, cell, child.DesiredSize.Height));
            if (++index % _columns == 0)
            {
                y += rowHeight + _gap;
                rowHeight = 0;
            }
        }

        return finalSize;
    }
}
