// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>Small pieces shared by the settings pages and panels of this project (built on the Core controls).</summary>
internal static class FormUi
{
    /// <summary>A switch row bound to a boolean setting.</summary>
    public static SettingsRow Toggle(IAppSettings app, string key, string? glyph, string title, string? caption, Action<bool>? changed = null, bool enabled = true)
    {
        var toggle = new MacSwitch(app.Get<bool>(key), SwitchSize.Regular, on =>
        {
            app.Set(key, on);
            changed?.Invoke(on);
        })
        {
            IsEnabled = enabled,
        };
        return new SettingsRow(glyph, title, caption, toggle);
    }

    /// <summary>A "Form" section: a small header above a rounded group (spec 05 0.3).</summary>
    public static StackPanel Section(string? header, params UIElement[] children)
    {
        var stack = new StackPanel();
        if (!string.IsNullOrEmpty(header))
        {
            var title = UiKit.Text(header, 12, FontWeights.SemiBold, ThemeKeys.TextSecondary);
            title.Margin = new Thickness(4, 0, 0, 6);
            stack.Children.Add(title);
        }

        var card = new SettingsCard();
        foreach (var child in children)
        {
            card.Add(child);
        }

        stack.Children.Add(card);
        return stack;
    }

    /// <summary>Wrapping row of chips (height 28, padding 10, radius 8; selected = accent 12% fill + 1px accent border).</summary>
    public static WrapPanel ChipRow<T>(IReadOnlyList<(T Value, string Label)> items, Func<T> current, Action<T> select, double left = 0, bool enabled = true)
    {
        var wrap = new WrapPanel { Margin = new Thickness(left, 0, 0, 0), Opacity = enabled ? 1 : 0.45, IsEnabled = enabled };
        void Build()
        {
            wrap.Children.Clear();
            var value = current();
            foreach (var (item, label) in items)
            {
                var selected = EqualityComparer<T>.Default.Equals(item, value);
                var chip = new Border
                {
                    Height = 28,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 0, 10, 0),
                    Margin = new Thickness(0, 0, 8, 8),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Child = UiKit.Text(label, 11, FontWeights.Medium, selected ? ThemeKeys.Accent : ThemeKeys.TextPrimary),
                };
                ((TextBlock)chip.Child).VerticalAlignment = VerticalAlignment.Center;
                if (selected)
                {
                    chip.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
                    chip.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
                }
                else
                {
                    chip.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
                    chip.BorderBrush = Brushes.Transparent;
                }

                var captured = item;
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    select(captured);
                    Build();
                };
                wrap.Children.Add(chip);
            }
        }

        Build();
        return wrap;
    }

    /// <summary>Slider + readout accessory (slider width 140).</summary>
    public static StackPanel SliderAccessory(double min, double max, double value, double step, Func<double, string> format, Action<double> changed, double readoutWidth = 56)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 140, VerticalAlignment = VerticalAlignment.Center, TickFrequency = step, IsSnapToTickEnabled = true };
        var label = UiKit.Text(format(slider.Value), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        label.Width = readoutWidth;
        label.TextAlignment = TextAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        slider.ValueChanged += (_, e) =>
        {
            label.Text = format(e.NewValue);
            changed(e.NewValue);
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(slider);
        panel.Children.Add(label);
        return panel;
    }

    /// <summary>Orange caption (errors and conflicts).</summary>
    public static TextBlock Warning(string text, double left = 0)
    {
        var block = UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        block.Margin = new Thickness(left, 0, 0, 0);
        return block;
    }

    /// <summary>Green "Active now" status line.</summary>
    public static FrameworkElement ActiveBadge(string? text = null)
    {
        text ??= Strings.ActiveNow;
        var border = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left };
        border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var check = UiKit.Icon("", 11, ThemeKeys.Green, FontWeights.SemiBold);
        check.Margin = new Thickness(0, 0, 5, 0);
        row.Children.Add(check);
        row.Children.Add(UiKit.Text(text, 11, FontWeights.Medium, ThemeKeys.Green));
        border.Child = row;
        return border;
    }

    /// <summary>Banner shown while a feature needs administrator rights for some windows (spec 05 0.2).</summary>
    public static FrameworkElement AdminCard(Func<bool> needed)
    {
        var card = new SettingsCard(Strings.PermissionRequired);
        var row = new SettingsRow("", Strings.AdminCaption, null,
            UiKit.Button(Strings.AdminButton, ButtonKind.Bordered, 11.5, null, RestartAsAdmin));
        card.Add(row);
        card.Visibility = needed() ? Visibility.Visible : Visibility.Collapsed;
        return card;
    }

    private static void RestartAsAdmin()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null)
            {
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current?.Shutdown();
        }
        catch (Exception)
        {
            // the user declined the UAC prompt
        }
    }

    /// <summary>A mini screen (outline + filled placement) used for placement glyphs.</summary>
    public static FrameworkElement MiniScreen(double x0, double y0, double x1, double y1, double width, double height, string? fillKey = null, string? outlineKey = null)
    {
        var canvas = new Canvas { Width = width, Height = height };
        var outline = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(2.5),
            BorderThickness = new Thickness(1),
            Opacity = 0.55,
        };
        outline.SetResourceReference(Border.BorderBrushProperty, outlineKey ?? ThemeKeys.Accent);
        canvas.Children.Add(outline);
        const double inset = 2.5;
        var fill = new Border { CornerRadius = new CornerRadius(1.5) };
        fill.SetResourceReference(Border.BackgroundProperty, fillKey ?? ThemeKeys.Accent);
        Canvas.SetLeft(fill, inset + (width - inset * 2) * x0);
        Canvas.SetTop(fill, inset + (height - inset * 2) * y0);
        fill.Width = Math.Max(2, (width - inset * 2) * (x1 - x0));
        fill.Height = Math.Max(2, (height - inset * 2) * (y1 - y0));
        canvas.Children.Add(fill);
        return canvas;
    }

    /// <summary>Glyph of a layout action: a mini screen for placements, a font icon for the rest.</summary>
    public static FrameworkElement ActionGlyph(LayoutAction action, double width = 18, double height = 12, string? fillKey = null)
    {
        if (action.Glyph is { } glyph && action.Kind is not (PlacementKind.Center or PlacementKind.MarginMaximize))
        {
            return UiKit.Icon(glyph, 12, fillKey ?? ThemeKeys.Accent, FontWeights.SemiBold);
        }

        return MiniScreen(action.X0, action.Y0, action.X1, action.Y1, width, height, fillKey);
    }

    /// <summary>A bordered little keycap button (30x25, radius 6).</summary>
    public static Border Keycap(string text, bool selected, bool enabled, Action click)
    {
        var cap = new Border
        {
            Width = text.Length > 3 ? 42 : 36,
            Height = 25,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            Child = UiKit.Text(text, 12, FontWeights.SemiBold, selected ? ThemeKeys.Accent : ThemeKeys.TextSecondary),
            Margin = new Thickness(7, 0, 0, 0),
        };
        ((TextBlock)cap.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)cap.Child).VerticalAlignment = VerticalAlignment.Center;
        if (selected)
        {
            cap.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill18);
            cap.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }
        else
        {
            cap.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            cap.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        }

        cap.MouseLeftButtonUp += (_, _) =>
        {
            if (enabled)
            {
                click();
            }
        };
        return cap;
    }

    /// <summary>Display text of a hotkey with the Windows spelling.</summary>
    public static string HotkeyText(Hotkey? hotkey) => hotkey?.ToString() ?? Strings.None;
}
