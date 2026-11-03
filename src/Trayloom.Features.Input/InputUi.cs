// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Input;

/// <summary>Small view helpers shared by the Input settings sections (chips, sliders, steppers, badges, disclosure).</summary>
internal static class InputUi
{
    /// <summary>A grouped-form section: small header above a card (the Form sections of the original).</summary>
    public static FrameworkElement FormSection(string? header, SettingsCard card)
    {
        var stack = new StackPanel();
        if (!string.IsNullOrEmpty(header))
        {
            var title = UiKit.Text(header, 14, FontWeights.SemiBold, ThemeKeys.TextPrimary);
            title.Margin = new Thickness(2, 0, 0, 8);
            stack.Children.Add(title);
        }

        stack.Children.Add(card);
        return stack;
    }

    /// <summary>Muted caption (11.5, secondary, wrapping) with an optional left inset.</summary>
    public static TextBlock Caption(string text, double left = 0, string brushKey = ThemeKeys.TextSecondary)
    {
        var block = UiKit.Text(text, 11.5, FontWeights.Normal, brushKey, wrap: true);
        block.Margin = new Thickness(left, 0, 0, 0);
        return block;
    }

    /// <summary>A line with an orange warning glyph and orange text.</summary>
    public static FrameworkElement Warning(string text, double left = 0, string glyph = "")
    {
        var row = new Grid { Margin = new Thickness(left, 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = UiKit.Icon(glyph, 12, ThemeKeys.Orange);
        icon.Margin = new Thickness(0, 1, 6, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(icon);
        var label = UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    /// <summary>The green "active now" capsule (check glyph + medium caption on a green 12 % fill).</summary>
    public static FrameworkElement ActiveBadge(string text, double left = 0)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = UiKit.Icon("", 11, ThemeKeys.Green, FontWeights.SemiBold);
        icon.Margin = new Thickness(0, 0, 5, 0);
        row.Children.Add(icon);
        row.Children.Add(UiKit.Text(text, 11.5, FontWeights.Medium, ThemeKeys.Green));
        return new Border
        {
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(8, 4, 8, 4),
            Child = row,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(left, 0, 0, 0),
            Background = Tinted(ThemeKeys.Green, 0.12),
        };
    }

    /// <summary>A copy of a themed brush with the given opacity.</summary>
    public static Brush Tinted(string key, double opacity)
    {
        var brush = UiKit.BrushOf(key);
        if (brush is SolidColorBrush solid)
        {
            var copy = new SolidColorBrush(solid.Color) { Opacity = opacity };
            copy.Freeze();
            return copy;
        }

        return brush;
    }

    /// <summary>A pill-shaped chip (height 28, radius 8): selected = accent 12 % fill + accent border, otherwise a neutral fill.</summary>
    public static Border Chip(string text, bool selected, Action onClick, string? glyph = null, double radius = 8, double height = 28)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var key = selected ? ThemeKeys.Accent : ThemeKeys.TextPrimary;
        if (glyph is not null)
        {
            var icon = UiKit.Icon(glyph, 12, key, FontWeights.SemiBold);
            icon.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(icon);
        }

        row.Children.Add(UiKit.Text(text, 11.5, FontWeights.Medium, key));
        var chip = new Border
        {
            Height = height,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = row,
            Margin = new Thickness(0, 0, 8, 8),
            SnapsToDevicePixels = true,
        };
        chip.SetResourceReference(Border.BackgroundProperty, selected ? ThemeKeys.AccentFill12 : ThemeKeys.ControlFill);
        chip.BorderBrush = selected ? Tinted(ThemeKeys.Accent, 0.7) : Brushes.Transparent;
        chip.MouseLeftButtonUp += (_, _) => onClick();
        return chip;
    }

    /// <summary>A wrapping row of chips.</summary>
    public static WrapPanel ChipRow(double left = 0) => new() { Margin = new Thickness(left, 0, 0, -8) };

    /// <summary>A key cap (rounded 7) used by the modifier pickers and the super key diagram.</summary>
    public static Border KeyCap(string label, bool selected, double minWidth = 30, double height = 30, double fontSize = 12, bool tinted = true, string? tooltip = null, Action? onClick = null, bool bold = false)
    {
        var text = UiKit.Text(label, fontSize, bold ? FontWeights.SemiBold : FontWeights.Medium, selected && tinted ? ThemeKeys.Accent : ThemeKeys.TextPrimary);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        var cap = new Border
        {
            MinWidth = minWidth,
            Height = height,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Child = text,
            ToolTip = tooltip,
            SnapsToDevicePixels = true,
        };
        if (selected && tinted)
        {
            cap.Background = Tinted(ThemeKeys.Accent, 0.14);
            cap.BorderBrush = Tinted(ThemeKeys.Accent, 0.45);
        }
        else
        {
            cap.Background = Tinted(ThemeKeys.TextPrimary, tinted ? 0.04 : 0.06);
            cap.BorderBrush = Tinted(ThemeKeys.TextPrimary, tinted ? 0.10 : 0.14);
            if (tinted && !selected)
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
            }
        }

        if (onClick is not null)
        {
            cap.Cursor = Cursors.Hand;
            cap.MouseLeftButtonUp += (_, _) => onClick();
        }

        return cap;
    }

    /// <summary>Label (min 96) + slider + readout (60 wide) on one line (the original <c>sliderRow</c>).</summary>
    public static FrameworkElement SliderLine(string label, double min, double max, double step, double value, Func<double, string> format, Action<double> onChanged, double sliderWidth = 260, double labelWidth = 110)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        var text = UiKit.Text(label, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            MaxWidth = sliderWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var readout = UiKit.Text(format(slider.Value), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        readout.TextAlignment = TextAlignment.Right;
        readout.VerticalAlignment = VerticalAlignment.Center;
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = format(e.NewValue);
            onChanged(e.NewValue);
        };
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(readout, 2);
        grid.Children.Add(slider);
        grid.Children.Add(readout);
        return grid;
    }

    /// <summary>A compact "value - +" stepper.</summary>
    public static FrameworkElement Stepper(int min, int max, int value, Func<int, string> format, Action<int> onChanged, int step = 1)
    {
        var current = Math.Clamp(value, min, max);
        var label = UiKit.Text(format(current), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MinWidth = 54;
        label.TextAlignment = TextAlignment.Right;
        label.Margin = new Thickness(0, 0, 8, 0);
        void Apply(int next)
        {
            next = Math.Clamp(next, min, max);
            if (next == current)
            {
                return;
            }

            current = next;
            label.Text = format(current);
            onChanged(current);
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(StepButton("", () => Apply(current - step)));
        var plus = StepButton("", () => Apply(current + step));
        plus.Margin = new Thickness(4, 0, 0, 0);
        row.Children.Add(plus);
        return row;
    }

    private static FrameworkElement StepButton(string glyph, Action onClick)
    {
        var button = new Button { Width = 26, Height = 24, Padding = new Thickness(0), Content = UiKit.Icon(glyph, 10, ThemeKeys.TextPrimary) };
        button.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonBordered);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>A "Label ... control" line without icon tile (for follow-up controls under a row).</summary>
    public static FrameworkElement LabelLine(string label, UIElement accessory, double labelWidth = 0)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = labelWidth > 0 ? new GridLength(labelWidth) : new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = labelWidth > 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var text = UiKit.Text(label, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        if (accessory is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.HorizontalAlignment = labelWidth > 0 ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        }

        Grid.SetColumn(accessory, 1);
        grid.Children.Add(accessory);
        return grid;
    }

    /// <summary>A drop-down with the given items.</summary>
    public static ComboBox Combo<T>(IReadOnlyList<(T Value, string Label)> items, T selected, Action<T> onChanged, double width = 200)
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
        return combo;
    }

    /// <summary>A collapsible area: clickable header over a body.</summary>
    public sealed class Disclosure : StackPanel
    {
        private readonly TextBlock _chevron;
        private readonly FrameworkElement _body;
        private bool _expanded;

        public Disclosure(FrameworkElement header, FrameworkElement body, bool expanded)
        {
            _body = body;
            _expanded = expanded;
            _chevron = UiKit.Icon("", 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
            _chevron.Margin = new Thickness(0, 0, 8, 0);
            var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(_chevron);
            Grid.SetColumn(header, 1);
            row.Children.Add(header);
            row.MouseLeftButtonUp += (_, _) =>
            {
                _expanded = !_expanded;
                Apply();
            };
            Children.Add(row);
            body.Margin = new Thickness(0, 10, 0, 0);
            Children.Add(body);
            Apply();
        }

        private void Apply()
        {
            _body.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            _chevron.RenderTransform = new RotateTransform(_expanded ? 90 : 0, 4.5, 6);
        }
    }

    /// <summary>The active window of the app (owner for dialogs), if any.</summary>
    public static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
        ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w.ShowInTaskbar);
}
