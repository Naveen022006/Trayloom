// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Capture;

/// <summary>Always-dark building blocks for the editor windows (they ignore the app's light/dark choice, like the original).</summary>
internal static class Ed
{
    public static readonly Color Surface = Color.FromRgb(0x1D, 0x1D, 0x1D);
    public static readonly Brush SurfaceBrush = Freeze(new SolidColorBrush(Surface));
    public static readonly Brush Plate = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2F)));
    public static readonly Brush PlateStroke = Freeze(new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)));
    public static readonly Brush Fg = Freeze(new SolidColorBrush(Color.FromArgb(0xEB, 255, 255, 255)));
    public static readonly Brush FgDim = Freeze(new SolidColorBrush(Color.FromArgb(0xA0, 255, 255, 255)));
    public static readonly Brush FgFaint = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)));
    public static readonly Brush Hover = Freeze(new SolidColorBrush(Color.FromArgb(0x16, 255, 255, 255)));
    public static readonly Brush Faint = Freeze(new SolidColorBrush(Color.FromArgb(0x0E, 255, 255, 255)));
    public static readonly Color AccentColor = Color.FromRgb(0x0A, 0x84, 0xFF);
    public static readonly Brush Accent = Freeze(new SolidColorBrush(AccentColor));
    public static readonly Brush AccentFill = Freeze(new SolidColorBrush(Color.FromArgb(0x38, 0x0A, 0x84, 0xFF)));
    public static readonly FontFamily Text = new("Segoe UI Variable Text, Segoe UI");

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    public static TextBlock Icon(string glyph, double size = 13, Brush? brush = null) => new()
    {
        Text = glyph, FontFamily = UiKit.IconFont, FontSize = size, Foreground = brush ?? Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Label(string text, double size = 12, FontWeight? weight = null, Brush? brush = null) => new()
    {
        Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = brush ?? Fg, FontFamily = Text, VerticalAlignment = VerticalAlignment.Center,
    };

    public static Border Divider(double height = 16) => new() { Width = 1, Height = height, Background = PlateStroke, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

    public static Border Capsule(UIElement child, double radius = 12, Thickness? padding = null) => new()
    {
        Child = child, CornerRadius = new CornerRadius(radius), Background = Plate, BorderBrush = PlateStroke, BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(9, 5, 9, 5),
        Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.16 },
    };

    public static Window DarkWindow(string title, double w, double h)
    {
        var win = new Window { Title = title, Width = w, Height = h, Background = SurfaceBrush, Foreground = Fg, FontFamily = Text, WindowStartupLocation = WindowStartupLocation.CenterScreen, UseLayoutRounding = true };
        win.SourceInitialized += (_, _) => DarkTitle(win);
        return win;
    }

    public static void DarkTitle(Window win)
    {
        var h = new System.Windows.Interop.WindowInteropHelper(win).Handle;
        var on = 1;
        N.DwmSetWindowAttribute(h, 20, ref on, sizeof(int));
        var caption = unchecked((int)0x001D1D1D);
        N.DwmSetWindowAttribute(h, 35, ref caption, sizeof(int));
    }

    public static Slider NewSlider(double min, double max, double value, double width, Action<double> changed, double step = 0)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, Width = width, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
        if (step > 0)
        {
            s.TickFrequency = step;
            s.IsSnapToTickEnabled = true;
        }

        s.ValueChanged += (_, e) => changed(e.NewValue);
        return s;
    }
}

/// <summary>A small flat button (icon and/or text) with hover, active and disabled states.</summary>
internal class EdButton : Border
{
    private bool _active;
    private bool _prominent;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public EdButton(string? glyph, string? text, string? tooltip, Action? click, double iconSize = 13, double width = 0, double height = 24, bool prominent = false)
    {
        _prominent = prominent;
        Child = _row;
        CornerRadius = new CornerRadius(7);
        Height = height;
        MinWidth = width;
        Cursor = Cursors.Hand;
        Background = prominent ? Ed.Accent : Brushes.Transparent;
        if (tooltip is not null)
        {
            ToolTip = tooltip;
        }

        if (glyph is not null)
        {
            Glyph = Ed.Icon(glyph, iconSize, prominent ? Brushes.White : Ed.Fg);
            _row.Children.Add(Glyph);
        }

        if (text is not null)
        {
            Label = Ed.Label(text, 12, FontWeights.SemiBold, prominent ? Brushes.White : Ed.Fg);
            Label.Margin = new Thickness(glyph is null ? 0 : 6, 0, 0, 0);
            _row.Children.Add(Label);
        }

        Padding = text is null ? new Thickness(0) : new Thickness(10, 0, 10, 0);
        MouseEnter += (_, _) => Paint(true);
        MouseLeave += (_, _) => Paint(false);
        MouseLeftButtonUp += (_, e) =>
        {
            if (IsEnabled && click is not null && IsMouseOver)
            {
                e.Handled = true;
                click();
            }
        };
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.4;
    }

    public TextBlock? Glyph { get; }

    public TextBlock? Label { get; }

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            Paint(IsMouseOver);
        }
    }

    private void Paint(bool hover)
    {
        if (_prominent)
        {
            Background = hover && IsEnabled ? new SolidColorBrush(Color.FromRgb(0x2E, 0x96, 0xFF)) : Ed.Accent;
            return;
        }

        Background = _active ? Ed.AccentFill : hover && IsEnabled ? Ed.Hover : Brushes.Transparent;
        if (Glyph is not null)
        {
            Glyph.Foreground = _active ? Ed.Accent : Ed.Fg;
        }
    }
}
