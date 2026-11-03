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

    private readonly TextBlock _title;
    private readonly TextBlock? _caption;

    /// <summary>Creates a row.</summary>
    /// <param name="glyph">Icon glyph (null = no tile and no inset).</param>
    /// <param name="title">Title.</param>
    /// <param name="caption">Optional wrapped caption under the title.</param>
    /// <param name="accessory">Control at the right edge (switch, picker, button...).</param>
    /// <param name="badge">Optional badge text ("BETA").</param>
    public SettingsRow(string? glyph, string title, string? caption = null, UIElement? accessory = null, string? badge = null)
    {
        var tileCol = glyph is null ? 0 : 26;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(tileCol) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(glyph is null ? 0 : 12) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 12 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (glyph is not null)
        {
            var tile = UiKit.IconTile(glyph);
            tile.VerticalAlignment = VerticalAlignment.Top;
            tile.Margin = new Thickness(0, 1, 0, 0);
            Children.Add(tile);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
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
        }
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
