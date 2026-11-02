// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Small helpers to build themed WPF UI in code.</summary>
internal static class UiKit
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static TextBlock Glyph(string glyph, double size = 16, string? brushKey = null)
    {
        var block = new TextBlock
        {
            Text = glyph,
            FontFamily = IconFont,
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey ?? ThemeKeys.Text);
        return block;
    }

    public static TextBlock Label(string text, string? brushKey = null, double size = 13, FontWeight? weight = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey ?? ThemeKeys.Text);
        if (weight is { } w)
        {
            block.FontWeight = w;
        }

        return block;
    }

    public static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.MutedTextStyle);
        return block;
    }

    public static Button Btn(string text, Action click, bool accent = false, string? glyph = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Glyph(glyph, 14, accent ? ThemeKeys.AccentText : null);
            g.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(g);
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = row;
        }

        var button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        if (accent)
        {
            button.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.AccentButtonStyle);
            button.Padding = new Thickness(14, 7, 14, 7);
        }

        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Creates a themed top-level window (not shown).</summary>
    public static Window NewWindow(string title, double width, double height, bool resizable = true)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 420),
            MinHeight = Math.Min(height, 300),
            ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.CanMinimize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
        };
        window.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.WindowStyle);
        window.SourceInitialized += (_, _) => ApplyTitleBarTheme(window);
        return window;
    }

    public static void ApplyTitleBarTheme(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            var dark = ThemeManager.IsLight ? 0 : 1;
            NativeMethods.DwmSetWindowAttribute(handle, NativeConstants.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch
        {
            // cosmetic only
        }
    }

    /// <summary>Shows a window and brings it to the front (single-instance helper).</summary>
    public static void Raise(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", value, units[unit]);
    }

    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var border = new Border { Child = child };
        border.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.CardStyle);
        if (margin is { } m)
        {
            border.Margin = m;
        }

        return border;
    }

    /// <summary>Builds a settings view: the standard builder output plus extra trailing content.</summary>
    public static FrameworkElement WithExtras(FrameworkElement builderOutput, params UIElement[] extras)
    {
        var panel = new StackPanel();
        panel.Children.Add(builderOutput);
        foreach (var extra in extras)
        {
            if (extra is FrameworkElement fe)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 8, fe.Margin.Right, fe.Margin.Bottom);
            }

            panel.Children.Add(extra);
        }

        return panel;
    }
}
