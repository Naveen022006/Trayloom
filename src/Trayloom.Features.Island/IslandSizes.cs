// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>
/// The size presets of the open island and the limits of the custom size, in one place. The window, the layout editor, the content preview and the
/// Settings sliders all read them here, so a number can never differ between what is previewed and what opens.
/// </summary>
internal static class IslandSizes
{
    public const string Compact = "compact";
    public const string Spacious = "spacious";
    public const string Large = "large";
    public const string Custom = "custom";

    /// <summary>The preset used when the setting is unset or holds a value this version does not know.</summary>
    public const string DefaultPreset = Large;

    public const double CompactWidth = 480;
    public const double SpaciousWidth = 560;
    public const double LargeWidth = 680;

    public const double CompactBudget = 180;
    public const double SpaciousBudget = 264;
    public const double LargeBudget = 380;

    public const double MinCustomWidth = 360;
    public const double MaxCustomWidth = 960;
    public const double MinCustomHeight = 260;
    public const double MaxCustomHeight = 900;

    /// <summary>The smallest content height a custom size may leave once the chrome of the page is taken off.</summary>
    public const double MinCustomBudget = 120;

    /// <summary>The height of the Library under the player: what it opens at, and how far its grip can drag it (the screen may give less).</summary>
    public const double MinLibraryHeight = 200;
    public const double DefaultLibraryHeight = 420;
    public const double MaxLibraryHeight = 900;

    /// <summary>Zoom limits of the island (the whole stage is scaled by it); the default stays 1.10.</summary>
    public const double MinZoom = 0.9;
    public const double MaxZoom = 1.5;

    /// <summary>Maps a stored preset to a known one; anything else becomes <see cref="DefaultPreset"/>.</summary>
    public static string Normalize(string? preset) => preset switch
    {
        Compact => Compact,
        Spacious => Spacious,
        Large => Large,
        Custom => Custom,
        _ => DefaultPreset,
    };

    public static double ClampWidth(double width) => double.IsFinite(width) ? Math.Clamp(width, MinCustomWidth, MaxCustomWidth) : MinCustomWidth;

    public static double ClampHeight(double height) => double.IsFinite(height) ? Math.Clamp(height, MinCustomHeight, MaxCustomHeight) : MinCustomHeight;

    public static double ClampLibraryHeight(double height) => double.IsFinite(height) ? Math.Clamp(height, MinLibraryHeight, MaxLibraryHeight) : DefaultLibraryHeight;

    /// <summary>The Library's height for the height the person wants and the room the screen leaves; it never goes below the minimum, because a list of one row is useless.</summary>
    public static double LibraryHeightFor(double wanted, double room) => Math.Max(MinLibraryHeight, Math.Min(ClampLibraryHeight(wanted), room));

    public static double ClampZoom(double zoom, double fallback) => double.IsFinite(zoom) ? Math.Clamp(zoom, MinZoom, MaxZoom) : fallback;

    /// <summary>The open island's width in units for a preset (the custom width is clamped).</summary>
    public static double WidthFor(string? preset, double customWidth) => Normalize(preset) switch
    {
        Compact => CompactWidth,
        Spacious => SpaciousWidth,
        Custom => ClampWidth(customWidth),
        _ => LargeWidth,
    };

    /// <summary>
    /// The height in units a page may use under the top row for a preset. A custom size gives its maximum height minus
    /// <paramref name="chrome"/> (the band and margins around the page).
    /// </summary>
    public static double BudgetFor(string? preset, double customHeight, double chrome) => Normalize(preset) switch
    {
        Compact => CompactBudget,
        Spacious => SpaciousBudget,
        Custom => Math.Max(MinCustomBudget, ClampHeight(customHeight) - chrome),
        _ => LargeBudget,
    };

    public static string Preset(IAppSettings app) => Normalize(app.Get<string>(K.Size));

    public static bool IsCustom(IAppSettings app) => Preset(app) == Custom;

    public static double Width(IAppSettings app) => WidthFor(app.Get<string>(K.Size), app.Get<double>(K.CustomWidth));

    public static double CustomHeight(IAppSettings app) => ClampHeight(app.Get<double>(K.CustomHeight));

    public static double Budget(IAppSettings app, double chrome) => BudgetFor(app.Get<string>(K.Size), app.Get<double>(K.CustomHeight), chrome);
}
