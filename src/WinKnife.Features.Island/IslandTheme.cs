// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinKnife.Features.Island;

/// <summary>Colours and small controls used inside the (always dark) island.</summary>
internal static class IslandTheme
{
    public static readonly Brush Black = Frozen(0xFF, 0x00, 0x00, 0x00);
    public static readonly Brush Text = Frozen(0xFF, 0xFF, 0xFF, 0xFF);
    public static readonly Brush Muted = Frozen(0xFF, 0xA1, 0xA1, 0xAA);
    public static readonly Brush Faint = Frozen(0xFF, 0x63, 0x63, 0x6B);
    public static readonly Brush Surface = Frozen(0xFF, 0x1C, 0x1C, 0x1F);
    public static readonly Brush SurfaceHover = Frozen(0xFF, 0x2C, 0x2C, 0x31);
    public static readonly Brush SurfacePressed = Frozen(0xFF, 0x3A, 0x3A, 0x40);
    public static readonly Brush Orange = Frozen(0xFF, 0xFF, 0x9F, 0x0A);
    public static readonly Brush Green = Frozen(0xFF, 0x30, 0xD1, 0x58);
    public static readonly Brush Blue = Frozen(0xFF, 0x0A, 0x84, 0xFF);
    public static readonly Brush LightHover = Frozen(0xFF, 0xD8, 0xD8, 0xDC);
    public static readonly Brush Red = Frozen(0xFF, 0xFF, 0x45, 0x3A);

    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");

    public static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static TextBlock Glyph(string glyph, double size, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = IconFont,
        FontSize = size,
        Foreground = brush ?? Text,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Label(string text, double size = 13, Brush? brush = null, FontWeight? weight = null) => new()
    {
        Text = text,
        FontFamily = TextFont,
        FontSize = size,
        Foreground = brush ?? Text,
        FontWeight = weight ?? FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A text block with fixed-width numerals so ticking time does not jitter.</summary>
    public static TextBlock Digits(double size, Brush? brush = null, FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            FontFamily = DisplayFont,
            FontSize = size,
            Foreground = brush ?? Text,
            FontWeight = weight ?? FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }
}

/// <summary>A flat button for the dark island: glyph and/or text, hover/pressed feedback, never takes focus.</summary>
internal sealed class IslandButton : Border
{
    private readonly Brush _rest;
    private readonly Brush _hover;
    private bool _down;

    public IslandButton(string? glyph, string? text = null, double size = 36, bool filled = false, Brush? foreground = null, Brush? background = null, Brush? hover = null)
    {
        _hover = hover ?? IslandTheme.SurfaceHover;
        _rest = background ?? (filled ? IslandTheme.Surface : Brushes.Transparent);
        Background = _rest;
        CornerRadius = new CornerRadius(size / 2);
        Cursor = Cursors.Hand;
        Focusable = false;
        MinHeight = size;
        MinWidth = size;

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (glyph is not null)
        {
            Icon = IslandTheme.Glyph(glyph, size * 0.42, foreground);
            row.Children.Add(Icon);
        }

        if (text is not null)
        {
            Caption = IslandTheme.Label(text, 13, foreground, FontWeights.SemiBold);
            Caption.Margin = new Thickness(glyph is null ? 14 : 8, 0, 14, 0);
            row.Children.Add(Caption);
        }

        Child = row;
    }

    public TextBlock? Icon { get; }

    public TextBlock? Caption { get; }

    public event Action? Clicked;

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Background = _hover;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _down = false;
        Background = _rest;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _down = true;
        Background = _hover;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var fire = _down;
        _down = false;
        Background = IsMouseOver ? _hover : _rest;
        e.Handled = true;
        if (fire)
        {
            Clicked?.Invoke();
        }
    }
}
