// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinKnife.Core.Ui;

/// <summary>Resource keys provided by <see cref="ThemeManager"/>. Use them with <c>DynamicResource</c>.</summary>
public static class ThemeKeys
{
    public const string WindowBackground = "WK.WindowBackground";
    public const string Surface = "WK.Surface";
    public const string SurfaceHover = "WK.SurfaceHover";
    public const string Border = "WK.Border";
    public const string Text = "WK.Text";
    public const string TextMuted = "WK.TextMuted";
    public const string Accent = "WK.Accent";
    public const string AccentText = "WK.AccentText";
    public const string SwitchOff = "WK.SwitchOff";

    /// <summary>Dark translucent surface for floating overlays (island, toasts); identical in both themes.</summary>
    public const string OverlayBackground = "WK.OverlayBackground";
    public const string OverlayText = "WK.OverlayText";

    // Style keys (defined in Styles.xaml)
    public const string WindowStyle = "WK.WindowStyle";
    public const string CardStyle = "WK.CardStyle";
    public const string TitleTextStyle = "WK.TitleTextStyle";
    public const string SubtitleTextStyle = "WK.SubtitleTextStyle";
    public const string MutedTextStyle = "WK.MutedTextStyle";
    public const string IconTextStyle = "WK.IconTextStyle";
    public const string SwitchStyle = "WK.SwitchStyle";
    public const string TileStyle = "WK.TileStyle";
    public const string AccentButtonStyle = "WK.AccentButtonStyle";
}

/// <summary>
/// Installs WinKnife theme resources into the application and keeps them in sync with the Windows
/// "app mode" (light/dark, <c>AppsUseLightTheme</c>). Also enables the built-in Fluent theme for standard controls.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _palette;
    private static Application? _app;

    /// <summary>True when Windows apps use the light theme.</summary>
    public static bool IsLight { get; private set; } = true;

    /// <summary>Raised on the UI thread after the palette changed.</summary>
    public static event Action<bool>? ThemeChanged;

    /// <summary>Merges the styles and palette into <paramref name="app"/>. Call once before creating windows.</summary>
    public static void Initialize(Application app)
    {
        if (_app is not null)
        {
            return;
        }

        _app = app;
        try
        {
            app.ThemeMode = ThemeMode.System;
        }
        catch (Exception)
        {
            // Fluent theme unavailable: fall back to classic controls.
        }

        _palette = new ResourceDictionary();
        app.Resources.MergedDictionaries.Add(_palette);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/WinKnife.Core;component/Ui/Styles.xaml", UriKind.Absolute),
        });

        Apply(ReadSystemIsLight());
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Reads the Windows "choose your default app mode" setting.</summary>
    public static bool ReadSystemIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        _app?.Dispatcher.BeginInvoke(() =>
        {
            var light = ReadSystemIsLight();
            if (light != IsLight)
            {
                Apply(light);
            }
        });
    }

    private static void Apply(bool light)
    {
        IsLight = light;
        var p = _palette!;
        p[ThemeKeys.WindowBackground] = Brush(light ? "#F3F3F6" : "#1F1F23");
        p[ThemeKeys.Surface] = Brush(light ? "#FFFFFF" : "#2A2A2F");
        p[ThemeKeys.SurfaceHover] = Brush(light ? "#EDEDF2" : "#36363C");
        p[ThemeKeys.Border] = Brush(light ? "#DEDEE5" : "#3C3C44");
        p[ThemeKeys.Text] = Brush(light ? "#1B1B1F" : "#F3F3F5");
        p[ThemeKeys.TextMuted] = Brush(light ? "#686873" : "#A4A4AF");
        p[ThemeKeys.Accent] = Brush(light ? "#4F4FD8" : "#7C7CF2");
        p[ThemeKeys.AccentText] = Brush(light ? "#FFFFFF" : "#101014");
        p[ThemeKeys.SwitchOff] = Brush(light ? "#E4E4EA" : "#3A3A42");
        p[ThemeKeys.OverlayBackground] = Brush("#F2000000");
        p[ThemeKeys.OverlayText] = Brush("#FFFFFF");
        ThemeChanged?.Invoke(light);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
