// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>Themed building blocks of the Dynamic Island settings page (spec 02d section 0).</summary>
internal static class SUi
{
    /// <summary>A <c>choice</c> tile: glyph + title, min height 68, radius 12, accent fill and stroke when selected.</summary>
    public static Border ChoiceTile(string glyph, string title, bool selected, Action onClick, double height = 68)
    {
        var b = new Border { MinHeight = height, Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 0) };
        if (selected)
        {
            b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
            b.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }
        else
        {
            b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            b.BorderBrush = Brushes.Transparent;
        }

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var icon = UiKit.Icon(glyph, 20, selected ? ThemeKeys.Accent : ThemeKeys.TextPrimary);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var t = UiKit.Text(title, 11, FontWeights.Medium, selected ? ThemeKeys.Accent : ThemeKeys.TextPrimary, true);
        t.TextAlignment = TextAlignment.Center;
        t.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(t);
        b.Child = stack;
        b.MouseLeftButtonUp += (_, _) => onClick();
        return b;
    }

    /// <summary>A horizontally equal grid of tiles.</summary>
    public static Grid TileRow(int columns, double gap, params FrameworkElement[] tiles)
    {
        var g = new Grid();
        var rows = (int)Math.Ceiling(tiles.Length / (double)columns);
        for (var c = 0; c < columns; c++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < rows; r++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < tiles.Length; i++)
        {
            var t = tiles[i];
            t.Margin = new Thickness(i % columns == 0 ? 0 : gap / 2, i / columns == 0 ? 0 : gap, i % columns == columns - 1 ? 0 : gap / 2, 0);
            Grid.SetColumn(t, i % columns);
            Grid.SetRow(t, i / columns);
            g.Children.Add(t);
        }

        return g;
    }

    public static TextBlock Caption(string text, double leftInset = 0)
    {
        var t = UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, true);
        t.Margin = new Thickness(leftInset, 0, 0, 0);
        return t;
    }

    public static FrameworkElement Indent(FrameworkElement e, double inset = SettingsRow.TextInset)
    {
        e.Margin = new Thickness(inset + e.Margin.Left, e.Margin.Top, e.Margin.Right, e.Margin.Bottom);
        return e;
    }

    /// <summary>A labelled slider with a monospaced value (name | slider | value, width 38).</summary>
    public static Grid SliderLine(string name, double min, double max, double value, double step, Func<double, string> format, Action<double> changed, double leftInset = 0, double nameWidth = 130)
    {
        var g = new Grid { Margin = new Thickness(leftInset, 0, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nameWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        var label = UiKit.Text(name, 12, FontWeights.Normal, ThemeKeys.TextPrimary);
        label.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(label);
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickFrequency = step, IsSnapToTickEnabled = step > 0, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(slider, 1);
        g.Children.Add(slider);
        var val = UiKit.Text(format(slider.Value), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        val.TextAlignment = TextAlignment.Right;
        val.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Documents.Typography.SetNumeralAlignment(val, FontNumeralAlignment.Tabular);
        Grid.SetColumn(val, 2);
        g.Children.Add(val);
        slider.ValueChanged += (_, e) =>
        {
            val.Text = format(e.NewValue);
            changed(e.NewValue);
        };
        return g;
    }

    /// <summary>A settings switch row bound to a boolean key.</summary>
    public static FrameworkElement SwitchRow(IAppSettings app, string key, string glyph, string title, string? caption = null, Action<bool>? also = null, bool enabled = true, string? badge = null)
    {
        var sw = new MacSwitch(app.Get<bool>(key), SwitchSize.Regular, on =>
        {
            app.Set(key, on);
            also?.Invoke(on);
        })
        { IsEnabled = enabled };
        var row = new SettingsRow(glyph, title, caption, sw, badge);
        if (!enabled)
        {
            row.Opacity = 0.45;
        }

        return row;
    }

    /// <summary>Segmented Dynamic Island | Separate window row.</summary>
    public static FrameworkElement DestinationRow(IAppSettings app, string key, string glyph, string title, string? caption, bool enabled = true)
    {
        var seg = new SegmentedControl([Loc.T("Dynamic Island"), Loc.T("Separate window")], app.Get<bool>(key) ? 0 : 1, false, 12) { IsEnabled = enabled };
        seg.SelectionChanged += i => app.Set(key, i == 0);
        var row = new SettingsRow(glyph, title, caption, seg);
        if (!enabled)
        {
            row.Opacity = 0.45;
        }

        return row;
    }

    public static Border Pill(string text, Action onClick)
    {
        var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 4), Cursor = Cursors.Hand };
        b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        b.Child = UiKit.Text(text, 11.5, FontWeights.Medium, ThemeKeys.TextPrimary);
        b.MouseLeftButtonUp += (_, _) => onClick();
        return b;
    }

    /// <summary>The toggle card of the editors (<c>NotchEditorItem</c>): glyph, title, optional reason, corner badge.</summary>
    public static Border ToggleCard(string glyph, string title, bool included, bool available, string? reason, Action onClick, Action? unavailableAction = null, bool reserveReason = false)
    {
        var card = new Border { MinHeight = 76, Padding = new Thickness(10), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var grid = new Grid();
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var ic = UiKit.Icon(glyph, 20, included && available ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(ic);
        var t = UiKit.Text(title, 11, FontWeights.Medium, available ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary, true);
        t.TextAlignment = TextAlignment.Center;
        t.Height = 28;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(t);
        if (!available && reason is not null || reserveReason)
        {
            var r = UiKit.Text(reason ?? "", 10, FontWeights.Normal, unavailableAction is not null ? ThemeKeys.Accent : ThemeKeys.TextSecondary, true);
            r.TextAlignment = TextAlignment.Center;
            r.MaxHeight = 40;
            r.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(r);
        }

        grid.Children.Add(stack);
        var badge = UiKit.Icon(!available ? (unavailableAction is not null ? "" : "") : (included ? "" : ""), 13, included && available ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        badge.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(badge);
        card.Child = grid;
        card.ToolTip = reason ?? title;
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (available)
            {
                onClick();
            }
            else
            {
                unavailableAction?.Invoke();
            }
        };
        return card;
    }

    /// <summary>Wraps toggle cards into an adaptive grid (min item width 116).</summary>
    public static UniformGrid CardGrid(IEnumerable<FrameworkElement> cards, double minWidth, double width)
    {
        var list = cards.ToList();
        var cols = Math.Max(1, (int)Math.Floor((Math.Max(300, width) + 10) / (minWidth + 10)));
        var g = new UniformGrid { Columns = cols };
        foreach (var c in list)
        {
            c.Margin = new Thickness(0, 0, 10, 10);
            g.Children.Add(c);
        }

        return g;
    }
}
