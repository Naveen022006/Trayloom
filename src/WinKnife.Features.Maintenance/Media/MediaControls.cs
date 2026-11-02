// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Maintenance;

/// <summary>Small input controls of the media workspace.</summary>
internal static class MediaControls
{
    /// <summary>Label left, "value suffix" right (mono), with minus / plus buttons (macOS Stepper).</summary>
    public static FrameworkElement Stepper(string label, int value, int min, int max, int step, string suffix, Action<int> onChanged, bool compact, double labelWidth = 0)
    {
        var current = Math.Clamp(value, min, max);
        var valueText = UiKit.Text(string.Empty, compact ? 10.5 : 11, FontWeights.Normal, ThemeKeys.TextSecondary);
        valueText.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        valueText.VerticalAlignment = VerticalAlignment.Center;
        valueText.TextAlignment = TextAlignment.Right;
        valueText.MinWidth = 64;
        void Show() => valueText.Text = suffix.StartsWith('%') ? $"{current}%" : $"{current}{suffix}";
        Show();
        Button Make(string glyph, int delta)
        {
            var button = new Button { Content = UiKit.Icon(glyph, 10, ThemeKeys.TextPrimary, FontWeights.SemiBold), Width = 22, Height = 20, Padding = new Thickness(0) };
            button.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonBordered);
            button.Click += (_, _) =>
            {
                var next = Math.Clamp(current + delta, min, max);
                if (next != current)
                {
                    current = next;
                    Show();
                    onChanged(current);
                }
            };
            return button;
        }

        var labelBlock = UiKit.Text(label, compact ? 11 : 12, FontWeights.Normal, ThemeKeys.TextPrimary);
        if (labelWidth > 0)
        {
            labelBlock.Width = labelWidth;
        }

        var right = V.Row(8, valueText, V.Row(2, Make(G.Add, step), Make("", -step)));
        var grid = V.Split(labelBlock, right);
        return grid;
    }

    /// <summary>The monospaced decimal field of the trim row (76 x 30, radius 7).</summary>
    public static FrameworkElement NumberField(string label, double value, int fractionDigits, double minimum, string suffix, Action<double> onChanged, bool compact)
    {
        var box = new TextBox
        {
            Text = value.ToString(fractionDigits == 0 ? "0" : "0.#", CultureInfo.CurrentCulture),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = compact ? 13 : 14,
            FontWeight = FontWeights.Medium,
            Width = compact ? 62 : 76,
            Height = compact ? 28 : 30,
            Padding = new Thickness(9, 0, 9, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
            }
        };
        void Commit()
        {
            if (double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                parsed = Math.Max(minimum, Math.Round(parsed, fractionDigits));
                box.Text = parsed.ToString(fractionDigits == 0 ? "0" : "0.#", CultureInfo.CurrentCulture);
                onChanged(parsed);
            }
            else
            {
                box.Text = value.ToString(fractionDigits == 0 ? "0" : "0.#", CultureInfo.CurrentCulture);
            }
        }

        var labelBlock = UiKit.Text(label, compact ? 10.5 : 11, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        labelBlock.Width = compact ? 48 : 62;
        labelBlock.VerticalAlignment = VerticalAlignment.Center;
        var unit = UiKit.Text(suffix, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        unit.Margin = new Thickness(5, 0, 0, 0);
        unit.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(labelBlock);
        row.Children.Add(box);
        row.Children.Add(unit);
        return row;
    }

    /// <summary>Low / Medium / High buttons with the nearest level selected and a description below.</summary>
    public static FrameworkElement Compression(double quality, Action<double> onPick, bool compact)
    {
        var column = new StackPanel();
        column.Children.Add(UiKit.Text("Compression", compact ? 10 : 11, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var selected = Ms.NearestLevel(quality);
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        string[] names = ["Low", "Medium", "High"];
        for (var i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var level = i;
            var on = i == selected;
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(CircleGlyph(i, on));
            var label = UiKit.Text(names[i], compact ? 11 : 12, FontWeights.Medium, on ? ThemeKeys.Accent : ThemeKeys.TextPrimary);
            label.Margin = new Thickness(6, 0, 0, 0);
            content.Children.Add(label);
            var border = new Border { Height = compact ? 28 : 32, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(0.8), Cursor = Cursors.Hand, Child = content, Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0) };
            if (on)
            {
                var accent = (UiKit.BrushOf(ThemeKeys.Accent) as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
                border.Background = new SolidColorBrush(Color.FromArgb(0x29, accent.R, accent.G, accent.B));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x73, accent.R, accent.G, accent.B));
            }
            else
            {
                border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
                border.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
            }

            border.MouseLeftButtonUp += (_, _) => onPick(Ms.Levels[level]);
            Grid.SetColumn(border, i);
            grid.Children.Add(border);
        }

        column.Children.Add(grid);
        var description = V.Secondary(selected switch
        {
            0 => "High quality, large file size",
            1 => "Balanced quality and file size",
            _ => "Low quality, small file size",
        }, compact ? 9.5 : 10.5);
        description.Margin = new Thickness(0, 5, 0, 0);
        column.Children.Add(description);
        return column;
    }

    private static FrameworkElement CircleGlyph(int level, bool on)
    {
        var brushKey = on ? ThemeKeys.Accent : ThemeKeys.TextPrimary;
        var canvas = new Grid { Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center };
        var ring = new Ellipse { StrokeThickness = 1.4 };
        ring.SetResourceReference(Shape.StrokeProperty, brushKey);
        canvas.Children.Add(ring);
        if (level == 2)
        {
            var fill = new Ellipse { Margin = new Thickness(0) };
            fill.SetResourceReference(Shape.FillProperty, brushKey);
            canvas.Children.Add(fill);
        }
        else if (level == 1)
        {
            var half = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 5.5,0 A 5.5,5.5 0 0 0 5.5,11 Z"), Stretch = Stretch.None };
            half.SetResourceReference(Shape.FillProperty, brushKey);
            canvas.Children.Add(half);
        }

        return canvas;
    }

    /// <summary>Segmented control wrapper with equal widths.</summary>
    public static SegmentedControl Segmented(IEnumerable<string> items, int selected, Action<int> onChanged, double fontSize = 12)
    {
        var control = new SegmentedControl(items, selected, true, fontSize);
        control.SelectionChanged += onChanged;
        return control;
    }

    public static CheckBoxRow Check(string label, bool value, Action<bool> onChanged, double size = 11.5) => new(label, value, onChanged, size);
}

/// <summary>A check mark with a label to its right.</summary>
internal sealed class CheckBoxRow : StackPanel
{
    private readonly CheckMark _check;

    public CheckBoxRow(string label, bool value, Action<bool> onChanged, double size)
    {
        Orientation = Orientation.Horizontal;
        _check = new CheckMark(value, onChanged);
        Children.Add(_check);
        var text = UiKit.Text(label, size, FontWeights.Normal, ThemeKeys.TextPrimary);
        text.Margin = new Thickness(7, 0, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(text);
        MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is not CheckMark && IsEnabled)
            {
                var next = _check.IsChecked != true;
                _check.IsChecked = next;
                onChanged(next);
            }
        };
        Background = Brushes.Transparent;
    }
}
