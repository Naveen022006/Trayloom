// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Trayloom.Core.Ui;

/// <summary>The app's own Appearance override (General page): follow Windows, or force light / dark.</summary>
public enum AppAppearance
{
    System,
    Light,
    Dark,
}

/// <summary>Resource keys provided by <see cref="ThemeManager"/>. Use them with <c>DynamicResource</c> / <c>SetResourceReference</c>.</summary>
public static class ThemeKeys
{
    // ---- legacy (v1) keys, still provided ----
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

    // ---- Vorssaint visual tokens (spec 01-menu-panel section 1.1) ----
    /// <summary>Tray flyout background.</summary>
    public const string PanelBase = "WK.Panel.Base";

    /// <summary>Fill of a panel card / tab bar / footer button (card.fill).</summary>
    public const string CardFill = "WK.Card.Fill";

    /// <summary>0.7 px card outline (card.border).</summary>
    public const string CardBorder = "WK.Card.Border";

    /// <summary>Control fill (control.fill): idle chips, small buttons.</summary>
    public const string ControlFill = "WK.Control.Fill";

    /// <summary>Tile fill (tile.fill): temperature cells, list rows.</summary>
    public const string TileFill = "WK.Tile.Fill";

    /// <summary>Usage-bar / slider track.</summary>
    public const string Track = "WK.Track";

    /// <summary>In-card divider.</summary>
    public const string Divider = "WK.Divider";

    /// <summary>Row hover (primary 6 %).</summary>
    public const string RowHover = "WK.Row.Hover";

    /// <summary>Primary text.</summary>
    public const string TextPrimary = "WK.Text.Primary";

    /// <summary>Secondary text (captions).</summary>
    public const string TextSecondary = "WK.Text.Secondary";

    /// <summary>Tertiary text (hints, chips).</summary>
    public const string TextTertiary = "WK.Text.Tertiary";

    /// <summary>Idle tab icon.</summary>
    public const string TabIdle = "WK.Tab.Idle";

    /// <summary>Selected tab fill (accent 20 % dark / 13 % light).</summary>
    public const string TabSelectedFill = "WK.Tab.SelectedFill";

    /// <summary>Accent at 12 % (icon tiles).</summary>
    public const string AccentFill12 = "WK.Accent.Fill12";

    /// <summary>Accent at 18 % (selected search row / hub highlight).</summary>
    public const string AccentFill18 = "WK.Accent.Fill18";

    /// <summary>Foreground on accent fills (white).</summary>
    public const string OnAccent = "WK.OnAccent";

    public const string Green = "WK.Green";
    public const string Cyan = "WK.Cyan";
    public const string Mint = "WK.Mint";
    public const string Yellow = "WK.Yellow";
    public const string Red = "WK.Red";
    public const string Orange = "WK.Orange";
    public const string Pink = "WK.Pink";

    /// <summary>Orange at 14 % (EXPERIMENTAL badge background).</summary>
    public const string OrangeFill = "WK.Orange.Fill";

    // ---- Settings window ----
    /// <summary>Settings window / detail background.</summary>
    public const string SettingsWindow = "WK.Settings.Window";

    /// <summary>Settings sidebar background.</summary>
    public const string SettingsSidebar = "WK.Settings.Sidebar";

    /// <summary>Settings card fill (quaternary 35 %).</summary>
    public const string SettingsCard = "WK.Settings.Card";

    /// <summary>Small controls inside a settings card (bordered buttons, pickers).</summary>
    public const string SettingsControl = "WK.Settings.Control";

    // ---- Style keys (Styles.xaml) ----
    public const string WindowStyle = "WK.WindowStyle";
    public const string CardStyle = "WK.CardStyle";
    public const string TitleTextStyle = "WK.TitleTextStyle";
    public const string SubtitleTextStyle = "WK.SubtitleTextStyle";
    public const string MutedTextStyle = "WK.MutedTextStyle";
    public const string IconTextStyle = "WK.IconTextStyle";
    public const string SwitchStyle = "WK.SwitchStyle";
    public const string SwitchSmallStyle = "WK.SwitchSmallStyle";
    public const string SwitchMiniStyle = "WK.SwitchMiniStyle";
    public const string TileStyle = "WK.TileStyle";

    /// <summary>Focus visual (accent ring) for border-based controls made keyboard-reachable by <see cref="UiKit.Clickable{T}"/>.</summary>
    public const string FocusRing = "WK.FocusRing";
    public const string AccentButtonStyle = "WK.AccentButtonStyle";
    public const string ButtonBordered = "WK.Button.Bordered";
    public const string ButtonProminent = "WK.Button.Prominent";
    public const string ButtonPlain = "WK.Button.Plain";
    public const string ButtonDestructive = "WK.Button.Destructive";
    public const string HotkeyRecorderStyle = "WK.HotkeyRecorderStyle";
    public const string TextBoxStyle = "WK.TextBoxStyle";
    public const string ComboBoxStyle = "WK.ComboBoxStyle";
    public const string ScrollBarStyle = "WK.ScrollBarStyle";
}

/// <summary>
/// Installs Trayloom theme resources into the application and keeps them in sync with the Windows
/// "app mode" (light/dark, <c>AppsUseLightTheme</c>) and the app's own <see cref="AppAppearance"/> override. The accent colour is the
/// Windows accent. Also enables the built-in Fluent theme for standard controls.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _palette;
    private static Application? _app;
    private static AppAppearance _appearance = AppAppearance.System;

    /// <summary>True when the effective theme is light.</summary>
    public static bool IsLight { get; private set; } = true;

    /// <summary>The Windows accent colour (fallback #007AFF).</summary>
    public static Color AccentColor { get; private set; } = Color.FromRgb(0x00, 0x7A, 0xFF);

    /// <summary>The app's Appearance override.</summary>
    public static AppAppearance Appearance => _appearance;

    /// <summary>Raised on the UI thread after the palette changed (argument = IsLight).</summary>
    public static event Action<bool>? ThemeChanged;

    /// <summary>Merges the styles and palette into <paramref name="app"/>. Call once before creating windows.</summary>
    public static void Initialize(Application app, AppAppearance appearance = AppAppearance.System)
    {
        if (_app is not null)
        {
            return;
        }

        _app = app;
        _appearance = appearance;
        _palette = new ResourceDictionary();
        app.Resources.MergedDictionaries.Add(_palette);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Trayloom.Core;component/Ui/Styles.xaml", UriKind.Absolute),
        });

        ApplyFluentMode();
        Apply(Effective());
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Sets the app-wide Appearance (System / Light / Dark). Applies immediately.</summary>
    public static void SetAppearance(AppAppearance appearance)
    {
        _appearance = appearance;
        if (_app is null)
        {
            return;
        }

        ApplyFluentMode();
        Apply(Effective());
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

    /// <summary>Reads the Windows taskbar theme (<c>SystemUsesLightTheme</c>) for the tray icon glyph colour.</summary>
    public static bool ReadTaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Makes a native window's title bar follow the effective theme (immersive dark mode) and keep following it.</summary>
    public static void BindWindow(Window window)
    {
        void Update()
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == 0)
            {
                return;
            }

            var dark = IsLight ? 0 : 1;
            Native.NativeMethods.DwmSetWindowAttribute(handle, Native.NativeConstants.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }

        void OnTheme(bool _) => window.Dispatcher.BeginInvoke(Update);
        window.SourceInitialized += (_, _) =>
        {
            Update();
            ThemeChanged += OnTheme;
        };
        window.Closed += (_, _) => ThemeChanged -= OnTheme;
    }

    private static bool Effective() => _appearance switch
    {
        AppAppearance.Light => true,
        AppAppearance.Dark => false,
        _ => ReadSystemIsLight(),
    };

    private static void ApplyFluentMode()
    {
        try
        {
            _app!.ThemeMode = _appearance switch
            {
                AppAppearance.Light => ThemeMode.Light,
                AppAppearance.Dark => ThemeMode.Dark,
                _ => ThemeMode.System,
            };
        }
        catch (Exception)
        {
            // Fluent theme unavailable: fall back to classic controls.
        }
    }

    private static Color ReadAccent()
    {
        try
        {
            var ui = new Windows.UI.ViewManagement.UISettings();
            var c = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch
        {
            return Color.FromRgb(0x00, 0x7A, 0xFF);
        }
    }

    /// <summary>
    /// Uses the Windows accent unless it is unreadable on the current theme (a graphite or near-white accent disappears on the
    /// panel and sidebar); then the Vorssaint blue fallback is used.
    /// </summary>
    private static Color ReadableAccent(Color accent, bool light)
    {
        static double Channel(byte v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Channel(accent.R) + 0.7152 * Channel(accent.G) + 0.0722 * Channel(accent.B);
        var max = Math.Max(accent.R, Math.Max(accent.G, accent.B));
        var min = Math.Min(accent.R, Math.Min(accent.G, accent.B));
        var saturation = max == 0 ? 0 : (max - min) / (double)max;
        var tooDark = !light && luminance < 0.12;
        var tooLight = light && luminance > 0.6;
        var grey = saturation < 0.25;
        return tooDark || tooLight || grey ? (light ? Color.FromRgb(0x00, 0x7A, 0xFF) : Color.FromRgb(0x0A, 0x84, 0xFF)) : accent;
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        _app?.Dispatcher.BeginInvoke(() =>
        {
            var light = Effective();
            var accent = ReadableAccent(ReadAccent(), light);
            if (light != IsLight || accent != AccentColor)
            {
                Apply(light);
            }
        });
    }

    private static void Apply(bool light)
    {
        IsLight = light;
        AccentColor = ReadableAccent(ReadAccent(), light);
        var p = _palette!;
        var accent = AccentColor;

        // Vorssaint tokens: dark values are sampled, light values estimated (spec 01-menu-panel 1.1).
        p[ThemeKeys.PanelBase] = Brush(light ? "#F1F1F1" : "#181818");
        p[ThemeKeys.CardFill] = Brush(light ? "#F7F7F7" : "#292929");
        p[ThemeKeys.CardBorder] = Brush(light ? "#E2E2E2" : "#414141");
        p[ThemeKeys.ControlFill] = Brush(light ? "#EDEDED" : "#313131");
        p[ThemeKeys.TileFill] = Brush(light ? "#EFEFEF" : "#323232");
        p[ThemeKeys.Track] = Brush(light ? "#E4E4E4" : "#383838");
        p[ThemeKeys.Divider] = Brush(light ? "#E3E3E3" : "#4C4C4C");
        p[ThemeKeys.RowHover] = Brush(light ? "#0F000000" : "#0FFFFFFF");
        p[ThemeKeys.TextPrimary] = Brush(light ? "#262626" : "#F2F2F2");
        // Captions, hints and inactive navigation need to remain readable on cards as well as the window background.
        p[ThemeKeys.TextSecondary] = Brush(light ? "#5F5F64" : "#C0C0C0");
        p[ThemeKeys.TextTertiary] = Brush(light ? "#626267" : "#B7B7B7");
        p[ThemeKeys.TabIdle] = Brush(light ? "#626267" : "#B7B7B7");
        p[ThemeKeys.Accent] = Brush(accent);
        p[ThemeKeys.AccentFill12] = Brush(Color.FromArgb(0x1F, accent.R, accent.G, accent.B));
        p[ThemeKeys.AccentFill18] = Brush(Color.FromArgb(0x2E, accent.R, accent.G, accent.B));
        p[ThemeKeys.TabSelectedFill] = Brush(Color.FromArgb(light ? (byte)0x21 : (byte)0x33, accent.R, accent.G, accent.B));
        p[ThemeKeys.OnAccent] = Brush("#FFFFFF");
        p[ThemeKeys.Green] = Brush(light ? "#007030" : "#3BDB63");
        p[ThemeKeys.Cyan] = Brush(light ? "#006E8A" : "#48D8FC");
        p[ThemeKeys.Mint] = Brush(light ? "#007066" : "#2DDFCD");
        p[ThemeKeys.Yellow] = Brush(light ? "#8F5C00" : "#FFD60A");
        p[ThemeKeys.Red] = Brush(light ? "#AD141A" : "#FF453A");
        p[ThemeKeys.Orange] = Brush(light ? "#AD4D00" : "#FF9F0A");
        p[ThemeKeys.Pink] = Brush(light ? "#AD0F57" : "#FF375F");
        p[ThemeKeys.OrangeFill] = Brush(light ? "#24AD4D00" : "#24FF9F0A");

        // Settings window (macOS grouped look: card = quaternary 35 % over the window).
        p[ThemeKeys.SettingsWindow] = Brush(light ? "#F5F5F5" : "#2A2A2A");
        p[ThemeKeys.SettingsSidebar] = Brush(light ? "#EEEEEE" : "#262626");
        p[ThemeKeys.SettingsCard] = Brush(light ? "#EBEBEB" : "#343434");
        p[ThemeKeys.SettingsControl] = Brush(light ? "#E0E0E0" : "#454545");

        // Legacy keys mapped onto the new palette.
        p[ThemeKeys.WindowBackground] = Brush(light ? "#F5F5F5" : "#2A2A2A");
        p[ThemeKeys.Surface] = Brush(light ? "#FFFFFF" : "#333333");
        p[ThemeKeys.SurfaceHover] = Brush(light ? "#EDEDED" : "#3D3D3D");
        p[ThemeKeys.Border] = Brush(light ? "#DCDCDC" : "#454545");
        p[ThemeKeys.Text] = Brush(light ? "#262626" : "#F2F2F2");
        p[ThemeKeys.TextMuted] = Brush(light ? "#5F5F64" : "#C0C0C0");
        p[ThemeKeys.AccentText] = Brush("#FFFFFF");
        p[ThemeKeys.SwitchOff] = Brush(light ? "#D4D4D6" : "#4A4A4C");
        p[ThemeKeys.OverlayBackground] = Brush("#F2000000");
        p[ThemeKeys.OverlayText] = Brush("#FFFFFF");
        ThemeChanged?.Invoke(light);
    }

    private static SolidColorBrush Brush(string hex) => Brush((Color)ColorConverter.ConvertFromString(hex));

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
