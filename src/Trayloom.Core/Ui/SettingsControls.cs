// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Trayloom.Core.Ui;

/// <summary>
/// Vorssaint's <c>SettingsCard</c>: a rounded (16) card with an optional headline title and rows stacked with 13 DIP spacing.
/// Use <see cref="Add"/> / <see cref="AddDivider"/>; <see cref="SettingsRow"/> is the usual child.
/// </summary>
public sealed class SettingsCard : Border
{
    private readonly StackPanel _stack = new();

    /// <summary>Creates a card.</summary>
    public SettingsCard(string? title = null)
    {
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(16);
        this.Themed(BackgroundProperty, ThemeKeys.SettingsCard);
        Child = _stack;
        if (!string.IsNullOrEmpty(title))
        {
            _stack.Children.Add(UiKit.Text(title, 14, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        }
    }

    /// <summary>Appends a child (13 DIP below the previous one). Returns the card for chaining.</summary>
    public SettingsCard Add(UIElement child)
    {
        _stack.AddSpaced(child, 13);
        return this;
    }

    /// <summary>Appends a divider.</summary>
    public SettingsCard AddDivider()
    {
        _stack.AddSpaced(UiKit.Divider(), 13);
        return this;
    }

    /// <summary>Appends a muted caption paragraph.</summary>
    public SettingsCard AddCaption(string text, double leftInset = 0)
    {
        var caption = UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.Margin = new Thickness(leftInset, 0, 0, 0);
        return Add(caption);
    }

    /// <summary>The children panel (for custom layouts).</summary>
    public StackPanel Content => _stack;
}

/// <summary>
/// Vorssaint's <c>SettingsRow</c>: 26 px icon tile, title (+ optional badge) and caption, accessory on the right. Follow-up controls
/// that belong under the row are indented by <see cref="TextInset"/>.
/// </summary>
public sealed class SettingsRow : Grid
{
    /// <summary>Width of tile + gap (26 + 12): left inset of controls under a row.</summary>
    public const double TextInset = 38;

    /// <summary>Narrowest the title and caption column may get before the accessory moves under the text.</summary>
    public const double MinTextWidth = 170;

    /// <summary>Gap kept between the text column and a side-by-side accessory.</summary>
    private const double AccessoryGap = 12;

    private readonly TextBlock _title;
    private readonly TextBlock? _caption;
    private readonly StackPanel _text;
    private readonly FrameworkElement? _accessory;
    private readonly HorizontalAlignment _accessoryAlignment;
    private readonly Thickness _accessoryMargin;
    private readonly double _lead;
    private bool _stacked;

    /// <summary>Creates a row.</summary>
    /// <param name="glyph">Icon glyph (null = no tile and no inset; empty = keep the inset but draw no tile).</param>
    /// <param name="title">Title.</param>
    /// <param name="caption">Optional wrapped caption under the title.</param>
    /// <param name="accessory">Control at the right edge (switch, picker, button...).</param>
    /// <param name="badge">Optional badge text ("BETA").</param>
    public SettingsRow(string? glyph, string title, string? caption = null, UIElement? accessory = null, string? badge = null)
    {
        var tileCol = glyph is null ? 0 : 26;
        var gapCol = glyph is null ? 0 : 12;
        _lead = tileCol + gapCol;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(tileCol) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gapCol) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = AccessoryGap });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // An empty glyph keeps the icon columns so the text lines up with the icon rows around it, but a blank tile is not drawn.
        if (!string.IsNullOrEmpty(glyph))
        {
            var tile = UiKit.IconTile(glyph);
            tile.VerticalAlignment = VerticalAlignment.Top;
            tile.Margin = new Thickness(0, 1, 0, 0);
            Children.Add(tile);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _text = text;

        // A WrapPanel (not a horizontal StackPanel) so the title gets a finite width and wraps instead of being cut off.
        var titleRow = new WrapPanel { Orientation = Orientation.Horizontal };
        _title = UiKit.Text(title, 13, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
        titleRow.Children.Add(_title);
        if (badge is not null)
        {
            var pill = new BadgePill(badge);
            pill.Margin = new Thickness(6, 0, 0, 0);
            titleRow.Children.Add(pill);
        }

        text.Children.Add(titleRow);
        if (!string.IsNullOrEmpty(caption))
        {
            _caption = UiKit.Text(caption, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            _caption.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(_caption);
        }

        SetColumn(text, 2);
        Children.Add(text);
        if (accessory is not null)
        {
            if (accessory is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
            }

            SetColumn(accessory, 4);
            Children.Add(accessory);
            _accessory = accessory as FrameworkElement;
            if (_accessory is not null)
            {
                _accessoryAlignment = _accessory.HorizontalAlignment;
                _accessoryMargin = _accessory.Margin;
            }
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        // Decided here, before the grid measures, so the row settles in one pass instead of flashing a squeezed layout first.
        if (_accessory is not null && !double.IsInfinity(constraint.Width))
        {
            _accessory.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var stack = SettingsLayoutMath.ShouldStack(constraint.Width, _lead, _accessory.DesiredSize.Width, _stacked);
            if (stack != _stacked)
            {
                ApplyStacking(stack);
            }
        }

        return base.MeasureOverride(constraint);
    }

    /// <summary>Moves the accessory under the text (and lets the text use the full width) or puts it back beside it.</summary>
    private void ApplyStacking(bool stack)
    {
        _stacked = stack;
        SetRow(_accessory!, stack ? 1 : 0);
        SetColumn(_accessory!, stack ? 2 : 4);
        SetColumnSpan(_accessory!, stack ? 3 : 1);
        SetColumnSpan(_text, stack ? 3 : 1);
        _accessory!.HorizontalAlignment = stack ? HorizontalAlignment.Left : _accessoryAlignment;
        _accessory.Margin = stack ? new Thickness(_accessoryMargin.Left, _accessoryMargin.Top + 8, _accessoryMargin.Right, _accessoryMargin.Bottom) : _accessoryMargin;
    }

    /// <summary>The title text.</summary>
    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    /// <summary>The caption text (no effect when the row was created without a caption).</summary>
    public string? Caption
    {
        get => _caption?.Text;
        set
        {
            if (_caption is not null)
            {
                _caption.Text = value ?? string.Empty;
            }
        }
    }
}

/// <summary>Page header: title2 bold with an optional callout description (<c>VStack spacing 5</c>).</summary>
public sealed class SettingsPageHeader : StackPanel
{
    private readonly TextBlock? _subtitle;

    /// <summary>Creates the header.</summary>
    public SettingsPageHeader(string title, string? subtitle = null)
    {
        Children.Add(UiKit.Text(title, 18, FontWeights.Bold, ThemeKeys.TextPrimary));
        if (!string.IsNullOrEmpty(subtitle))
        {
            _subtitle = UiKit.Text(subtitle, 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            _subtitle.Margin = new Thickness(0, 5, 0, 0);
            Children.Add(_subtitle);
        }
    }

    /// <summary>Changes the description line.</summary>
    public void SetSubtitle(string? text)
    {
        if (_subtitle is not null)
        {
            _subtitle.Text = text ?? string.Empty;
        }
    }
}

/// <summary>A clickable row that navigates: title, optional caption, trailing chevron (Vorssaint's disclosure / link rows).</summary>
public sealed class ChevronRow : Border
{
    /// <summary>Creates the row; <paramref name="onClick"/> runs on click.</summary>
    public ChevronRow(string? glyph, string title, string? caption, Action onClick)
    {
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(8, 7, 8, 7);
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        var chevron = UiKit.Icon(Glyphs.ChevronRight, 10, ThemeKeys.TextTertiary);
        Child = new SettingsRow(glyph, title, caption, chevron);
        MouseEnter += (_, _) => this.Themed(BackgroundProperty, ThemeKeys.RowHover);
        MouseLeave += (_, _) => Background = Brushes.Transparent;
        MouseLeftButtonUp += (_, _) => onClick();
    }
}

/// <summary>A row with a slider and a live value label (<c>0..1</c> style or any range).</summary>
public sealed class SliderRow : Grid
{
    /// <summary>Creates a slider row.</summary>
    /// <param name="glyph">Row icon.</param>
    /// <param name="title">Title.</param>
    /// <param name="caption">Optional caption.</param>
    /// <param name="min">Minimum.</param>
    /// <param name="max">Maximum.</param>
    /// <param name="value">Initial value.</param>
    /// <param name="format">Label format (e.g. <c>0'%'</c>).</param>
    /// <param name="onChanged">Called while dragging.</param>
    /// <param name="step">Snap step (0 = continuous).</param>
    public SliderRow(string? glyph, string title, string? caption, double min, double max, double value, string format, Action<double> onChanged, double step = 0)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (step > 0)
        {
            slider.TickFrequency = step;
            slider.IsSnapToTickEnabled = true;
        }

        var label = UiKit.Text(slider.Value.ToString(format, System.Globalization.CultureInfo.CurrentCulture), 12, FontWeights.Medium, ThemeKeys.TextSecondary);
        label.Width = 44;
        label.TextAlignment = TextAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Center;
        slider.ValueChanged += (_, e) =>
        {
            label.Text = e.NewValue.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
            onChanged(e.NewValue);
        };
        var accessory = new StackPanel { Orientation = Orientation.Horizontal };
        accessory.Children.Add(slider);
        accessory.Children.Add(label);
        Children.Add(new SettingsRow(glyph, title, caption, accessory));
    }
}

/// <summary>A row with a drop-down picker (menu style).</summary>
public sealed class PickerRow<T> : Grid
{
    /// <summary>Creates a picker row.</summary>
    public PickerRow(string? glyph, string title, string? caption, IReadOnlyList<(T Value, string Label)> items, T selected, Action<T> onChanged, double width = 160)
    {
        var combo = new ComboBox { MinWidth = width, ItemsSource = items.Select(i => i.Label).ToList() };
        var index = items.ToList().FindIndex(i => EqualityComparer<T>.Default.Equals(i.Value, selected));
        combo.SelectedIndex = index < 0 ? 0 : index;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                onChanged(items[combo.SelectedIndex].Value);
            }
        };
        Children.Add(new SettingsRow(glyph, title, caption, combo));
    }
}

/// <summary>
/// <c>SettingsFeatureSwitchRow</c>: a switch for an option that needs another feature. While that feature is uninstalled the
/// row is greyed (45 %), forced off, and a line with an "Open Features" button is shown under it.
/// </summary>
public sealed class FeatureSwitchRow : StackPanel
{
    /// <summary>Creates the row.</summary>
    /// <param name="runtime">Install state.</param>
    /// <param name="shell">Used by the "Open Features" button.</param>
    /// <param name="featureId">Catalog id of the required feature.</param>
    /// <param name="glyph">Row icon.</param>
    /// <param name="title">Title.</param>
    /// <param name="caption">Caption.</param>
    /// <param name="isOn">Current value.</param>
    /// <param name="onToggled">Value changed.</param>
    /// <param name="enableReason">Line shown while the feature is missing (e.g. "Install Clipboard to use this").</param>
    public FeatureSwitchRow(IFeatureRuntime runtime, IShell shell, string featureId, string? glyph, string title, string? caption, bool isOn, Action<bool> onToggled, string enableReason)
    {
        var available = runtime.IsAvailable(featureId);
        var toggle = new MacSwitch(available && isOn, SwitchSize.Regular, onToggled) { IsEnabled = available };
        var row = new SettingsRow(glyph, title, caption, toggle) { Opacity = available ? 1 : 0.45 };
        Children.Add(row);
        if (!available)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(glyph is null ? 0 : SettingsRow.TextInset, 6, 0, 0) };
            line.Children.Add(UiKit.Text(enableReason, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            var open = UiKit.Button(Loc.T("Open Features"), ButtonKind.Bordered, 11.5, onClick: () => shell.OpenFeatures(featureId));
            open.Margin = new Thickness(10, 0, 0, 0);
            line.Children.Add(open);
            Children.Add(line);
        }
    }
}

/// <summary>Pure layout decisions of <see cref="SettingsRow"/> and <see cref="TileGrid"/>, kept free of WPF so a harness can check them.</summary>
public static class SettingsLayoutMath
{
    /// <summary>
    /// Extra width, beyond the stack threshold, a stacked row needs before it goes side by side again. It is wider than a vertical
    /// scroll bar: stacking makes a page taller, which can add a scroll bar and take that width away, and without the margin the row
    /// would flip back and forth.
    /// </summary>
    public const double StackHysteresis = 24;

    /// <summary>Whether a row of <paramref name="width"/> should put its accessory under the text.</summary>
    /// <param name="width">Width offered to the row.</param>
    /// <param name="lead">Width of the icon columns.</param>
    /// <param name="accessoryWidth">Natural width of the accessory.</param>
    /// <param name="wasStacked">The current state, which decides which side of the hysteresis applies.</param>
    public static bool ShouldStack(double width, double lead, double accessoryWidth, bool wasStacked)
    {
        if (accessoryWidth <= 0)
        {
            return false;
        }

        var needed = lead + SettingsRow.MinTextWidth + 12 + accessoryWidth;
        return wasStacked ? width < needed + StackHysteresis : width < needed;
    }

    /// <summary>Number of tile columns that fit: each at least <paramref name="minItemWidth"/> wide, at most <paramref name="maxColumns"/>, at least one.</summary>
    public static int TileColumns(double width, double minItemWidth, double spacing, int maxColumns)
    {
        var fit = (int)Math.Floor((width + spacing) / (minItemWidth + spacing));
        return Math.Clamp(fit, 1, Math.Max(1, maxColumns));
    }

    /// <summary>Width of one tile when <paramref name="columns"/> share <paramref name="width"/> with <paramref name="spacing"/> between them.</summary>
    public static double TileCellWidth(double width, int columns, double spacing) =>
        Math.Max(0, (width - spacing * (columns - 1)) / columns);
}

/// <summary>
/// Equal-width tiles in as many columns as fit (each at least <see cref="MinItemWidth"/>, at most <see cref="MaxColumns"/>), rows as
/// tall as their tallest tile, flush at both edges. Unlike a fixed-column grid it never squeezes a label below a readable width.
/// </summary>
public sealed class TileGrid : Panel
{
    /// <summary>Narrowest a tile may get before the grid drops a column.</summary>
    public double MinItemWidth { get; set; } = 116;

    /// <summary>Most columns the grid will use.</summary>
    public int MaxColumns { get; set; } = 6;

    /// <summary>Gap between tiles, horizontally and vertically.</summary>
    public double Spacing { get; set; } = 10;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        if (count == 0)
        {
            return new Size(0, 0);
        }

        // With unbounded width (a horizontal parent) lay the tiles out as one wide row so the grid still reports a sensible size.
        var width = double.IsInfinity(availableSize.Width)
            ? Math.Min(count, Math.Max(1, MaxColumns)) * (MinItemWidth + Spacing) - Spacing
            : availableSize.Width;
        var columns = SettingsLayoutMath.TileColumns(width, MinItemWidth, Spacing, MaxColumns);
        var cell = SettingsLayoutMath.TileCellWidth(width, columns, Spacing);
        double height = 0;
        double row = 0;
        for (var i = 0; i < count; i++)
        {
            InternalChildren[i].Measure(new Size(cell, double.PositiveInfinity));
            row = Math.Max(row, InternalChildren[i].DesiredSize.Height);
            if (i % columns == columns - 1 || i == count - 1)
            {
                height += row + (i >= columns ? Spacing : 0);
                row = 0;
            }
        }

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        if (count == 0)
        {
            return finalSize;
        }

        var columns = SettingsLayoutMath.TileColumns(finalSize.Width, MinItemWidth, Spacing, MaxColumns);
        var cell = SettingsLayoutMath.TileCellWidth(finalSize.Width, columns, Spacing);
        double y = 0;
        for (var start = 0; start < count; start += columns)
        {
            var end = Math.Min(count, start + columns);
            double row = 0;
            for (var i = start; i < end; i++)
            {
                row = Math.Max(row, InternalChildren[i].DesiredSize.Height);
            }

            for (var i = start; i < end; i++)
            {
                InternalChildren[i].Arrange(new Rect((i - start) * (cell + Spacing), y, cell, row));
            }

            y += row + Spacing;
        }

        return finalSize;
    }
}
