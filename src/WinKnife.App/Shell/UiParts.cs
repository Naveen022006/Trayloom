// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>Small shared pieces of the first-run windows (hero banner, plain windows, log folder).</summary>
internal static class UiParts
{
    /// <summary>The dark brand gradient used by the hero banners.</summary>
    public static Brush HeroBrush() => new LinearGradientBrush(
    [
        new GradientStop(Color.FromRgb(0x30, 0x30, 0x36), 0),
        new GradientStop(Color.FromRgb(0x12, 0x12, 0x15), 0.45),
        new GradientStop(Colors.Black, 1),
    ], new Point(0, 0), new Point(1, 1));

    /// <summary>A hero banner: brand mark, optional white title and subtitle.</summary>
    public static Border Hero(double height, double logo, string? title, string? subtitle)
    {
        var hero = new Border { Height = height, Background = HeroBrush() };
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        col.Children.Add(BrandMark.Create(logo, ThemeKeys.OnAccent));
        if (title is not null)
        {
            col.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) });
        }

        if (subtitle is not null)
        {
            col.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 255, 255, 255)), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Margin = new Thickness(0, 6, 0, 0) });
        }

        hero.Child = col;
        return hero;
    }

    /// <summary>A themed, fixed-size, centred window without resize.</summary>
    public static Window PlainWindow(string title, double width)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = UiKit.TextFont,
            UseLayoutRounding = true,
        };
        window.SetResourceReference(Window.StyleProperty, ThemeKeys.WindowStyle);
        ThemeManager.BindWindow(window);
        return window;
    }

    /// <summary>Opens the log folder in File Explorer with the current log selected (the "Report a problem" action).</summary>
    public static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            if (File.Exists(AppPaths.LogFile))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{AppPaths.LogFile}\"") { UseShellExecute = false });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogDirectory}\"") { UseShellExecute = false });
            }
        }
        catch (Exception)
        {
            // nothing sensible to do
        }
    }

    /// <summary>A wrapped paragraph with a bottom margin.</summary>
    public static TextBlock Paragraph(string text, double size = 13, string brush = ThemeKeys.TextSecondary, double bottom = 0)
    {
        var block = UiKit.Text(text, size, FontWeights.Normal, brush, wrap: true);
        block.Margin = new Thickness(0, 0, 0, bottom);
        return block;
    }
}
