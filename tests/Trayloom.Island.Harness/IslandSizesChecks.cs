// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>Pure checks of the island size presets, the custom limits and the zoom clamp (<see cref="IslandSizes"/>). No window, no settings store.</summary>
internal static class IslandSizesChecks
{
    public static void Run(Action<bool, string> check)
    {
        Presets(check);
        Custom(check);
        Budgets(check);
        Zoom(check);
    }

    private static void Presets(Action<bool, string> check)
    {
        check(IslandSizes.DefaultPreset == "large", "sizes: the default preset is large");
        check(IslandSizes.WidthFor("compact", 999) == 480, "sizes: compact is 480 wide");
        check(IslandSizes.WidthFor("spacious", 999) == 560, "sizes: spacious is 560 wide");
        check(IslandSizes.WidthFor("large", 999) == 680, "sizes: large is 680 wide");
        check(IslandSizes.WidthFor(null, 999) == 680 && IslandSizes.WidthFor("", 999) == 680 && IslandSizes.WidthFor("huge", 999) == 680, "sizes: an unset or unknown preset reads as the default");
        check(IslandSizes.Normalize("compact") == "compact" && IslandSizes.Normalize("spacious") == "spacious", "sizes: an explicit compact or spacious choice is kept");
        check(IslandSizes.Normalize("custom") == "custom" && IslandSizes.Normalize("large") == "large", "sizes: custom and large are kept");
    }

    private static void Custom(Action<bool, string> check)
    {
        check(IslandSizes.ClampWidth(100) == 360 && IslandSizes.ClampWidth(360) == 360, "sizes: custom width has a floor of 360");
        check(IslandSizes.ClampWidth(960) == 960 && IslandSizes.ClampWidth(5000) == 960, "sizes: custom width has a ceiling of 960");
        check(IslandSizes.ClampWidth(700) == 700, "sizes: a custom width in range is unchanged");
        check(IslandSizes.ClampHeight(10) == 260 && IslandSizes.ClampHeight(900) == 900 && IslandSizes.ClampHeight(5000) == 900, "sizes: custom height runs from 260 to 900");
        check(IslandSizes.ClampWidth(double.NaN) == 360 && IslandSizes.ClampHeight(double.PositiveInfinity) == 260, "sizes: a non-finite custom size falls back to the minimum");
        check(IslandSizes.WidthFor("custom", 820) == 820 && IslandSizes.WidthFor("custom", 2000) == 960, "sizes: the custom preset uses the clamped custom width");
    }

    private static void Budgets(Action<bool, string> check)
    {
        check(IslandSizes.BudgetFor("compact", 480, 76) == 180, "budget: compact is 180");
        check(IslandSizes.BudgetFor("spacious", 480, 76) == 264, "budget: spacious is 264");
        check(IslandSizes.BudgetFor("large", 480, 76) == 380, "budget: large is 380");
        check(IslandSizes.BudgetFor(null, 480, 76) == 380, "budget: an unset preset is the large budget");
        check(IslandSizes.BudgetFor("custom", 480, 76) == 404, "budget: custom is its height minus the chrome");
        check(IslandSizes.BudgetFor("custom", 900, 76) == 824 && IslandSizes.BudgetFor("custom", 5000, 76) == 824, "budget: custom height tops out at 900");
        check(IslandSizes.BudgetFor("custom", 260, 300) == 120, "budget: a custom size never leaves less than 120 for the page");
    }

    private static void Zoom(Action<bool, string> check)
    {
        check(IslandSizes.MaxZoom == 1.5 && IslandSizes.MinZoom == 0.9, "zoom: the range is 0.9 to 1.5");
        check(IslandSizes.ClampZoom(2, 1.1) == 1.5 && IslandSizes.ClampZoom(0.1, 1.1) == 0.9 && IslandSizes.ClampZoom(1.4, 1.1) == 1.4, "zoom: values clamp into the range, 1.4 is allowed");
        check(IslandSizes.ClampZoom(double.NaN, 1.1) == 1.1 && IslandSizes.ClampZoom(double.PositiveInfinity, 1.1) == 1.1, "zoom: a non-finite value falls back to the default");
    }
}
