// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Trayloom.Core.Ui;

/// <summary>Vorssaint's <c>panelCard</c>: radius 10, padding 10, card fill with a 0.7 px border.</summary>
public class PanelCard : Border
{
    /// <summary>Creates a card; <paramref name="padding"/> 0 for row-group cards.</summary>
    public PanelCard(UIElement? child = null, double padding = 10)
    {
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(padding);
        BorderThickness = new Thickness(0.7);
        this.Themed(BackgroundProperty, ThemeKeys.CardFill);
        this.Themed(BorderBrushProperty, ThemeKeys.CardBorder);
        Child = child;
    }
}

/// <summary>A card holding rows separated by 1 px dividers that start at the text column (41) and end 10 from the right.</summary>
public sealed class PanelRowGroup : PanelCard
{
    private readonly StackPanel _stack = new();

    /// <summary>Creates an empty group.</summary>
    public PanelRowGroup()
        : base(null, 0)
    {
        Child = _stack;
    }

    /// <summary>Appends a row (a divider is inserted before every row but the first).</summary>
    public PanelRowGroup Add(UIElement row)
    {
        if (_stack.Children.Count > 0)
        {
            _stack.Children.Add(UiKit.Divider(41, 10));
        }

        _stack.Children.Add(row);
        return this;
    }

    /// <summary>Number of rows.</summary>
    public int RowCount => (_stack.Children.Count + 1) / 2;
}

/// <summary>
/// Utilities-tab row (Vorssaint's <c>UtilityActionButton</c>): accent icon (22 wide), title, optional caption, shortcut chip,
/// chevron. The whole row is clickable.
/// </summary>
public sealed class PanelActionRow : Border
{
    private readonly Action _onClick;

    /// <summary>Creates a row.</summary>
    /// <param name="glyph">Icon.</param>
    /// <param name="title">Title (12 semibold).</param>
    /// <param name="caption">Caption under the title; shown only when <paramref name="showCaption"/>; always used as tooltip.</param>
    /// <param name="onClick">Click handler.</param>
    /// <param name="shortcut">Shortcut chip text (null = none).</param>
    /// <param name="showCaption">Print the caption.</param>
    /// <param name="needsAttention">Orange icon and caption.</param>
    /// <param name="chevron">Trailing chevron.</param>
    public PanelActionRow(string glyph, string title, string? caption, Action onClick, string? shortcut = null, bool showCaption = false, bool needsAttention = false, bool chevron = true)
    {
        _onClick = onClick;
        Padding = new Thickness(10, 8, 10, 8);
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        ToolTip = caption;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = UiKit.Icon(glyph, 15, needsAttention ? ThemeKeys.Orange : ThemeKeys.Accent, FontWeights.SemiBold);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(title, 12.5, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true));
        if (!string.IsNullOrEmpty(caption) && (showCaption || needsAttention))
        {
            var c = UiKit.Text(caption, 10.5, FontWeights.Normal, needsAttention ? ThemeKeys.Orange : ThemeKeys.TextSecondary, wrap: true);
            c.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(c);
        }

        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        if (shortcut is not null)
        {
            var chip = new ShortcutChip(shortcut) { Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(chip, 3);
            grid.Children.Add(chip);
        }

        if (chevron)
        {
            var arrow = UiKit.Icon(Glyphs.ChevronRight, 9, ThemeKeys.TextTertiary, FontWeights.SemiBold);
            arrow.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(arrow, 4);
            grid.Children.Add(arrow);
        }

        Child = grid;
        MouseEnter += (_, _) => this.Themed(BackgroundProperty, ThemeKeys.RowHover);
        MouseLeave += (_, _) => Background = Brushes.Transparent;
        MouseLeftButtonUp += (_, e) =>
        {
            if (!e.Handled)
            {
                _onClick();
            }
        };
    }

    /// <summary>The icon / text grid (for rows that add a mini button below).</summary>
    public Grid Layout => (Grid)Child;
}

/// <summary>Controls / Quick toggles row (Vorssaint's <c>PanelToggleRow</c>): icon (accent when on), title, caption, small switch.</summary>
public sealed class PanelToggleRow : Border
{
    private readonly TextBlock _icon;
    private readonly MacSwitch _switch;

    /// <summary>Creates a toggle row.</summary>
    public PanelToggleRow(string glyph, string title, string? caption, bool isOn, Action<bool> onToggled, bool showCaption = false, bool needsAttention = false)
    {
        Padding = new Thickness(10, 8, 10, 8);
        ToolTip = caption;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _icon = UiKit.Icon(glyph, 15, needsAttention ? ThemeKeys.Orange : isOn ? ThemeKeys.Accent : ThemeKeys.TextSecondary, FontWeights.SemiBold);
        grid.Children.Add(_icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(title, 12.5, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true));
        if (!string.IsNullOrEmpty(caption) && (showCaption || needsAttention))
        {
            var c = UiKit.Text(caption, 10.5, FontWeights.Normal, needsAttention ? ThemeKeys.Orange : ThemeKeys.TextSecondary, wrap: true);
            c.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(c);
        }

        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        _switch = new MacSwitch(isOn, SwitchSize.Small, on =>
        {
            _icon.SetResourceReference(TextBlock.ForegroundProperty, on ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
            onToggled(on);
        });
        _switch.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_switch, 3);
        grid.Children.Add(_switch);
        Child = grid;
    }

    /// <summary>The switch state.</summary>
    public bool IsOn
    {
        get => _switch.IsOn;
        set => _switch.IsOn = value;
    }
}

/// <summary>Mini bordered button used under panel rows ("Grant access", "Recent captures").</summary>
public static class PanelButtons
{
    /// <summary>Creates a mini button (10 pt semibold) with an optional glyph.</summary>
    public static Button Mini(string text, string? glyph, Action onClick)
    {
        var button = UiKit.Button(text, ButtonKind.Bordered, 10, glyph, onClick);
        button.Padding = new Thickness(7, 3, 7, 3);
        button.FontWeight = FontWeights.SemiBold;
        return button;
    }
}
