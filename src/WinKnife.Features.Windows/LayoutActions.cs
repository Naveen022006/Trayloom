// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.WindowTools;

/// <summary>Family a window layout action belongs to (section of the settings page and the hosted panel).</summary>
internal enum LayoutGroup
{
    Halves,
    Thirds,
    QuarterRows,
    QuarterColumns,
    Sixths,
    Corners,
    Actions,
}

/// <summary>How an action computes its frame.</summary>
internal enum PlacementKind
{
    Fraction,
    Maximize,
    MarginMaximize,
    FullScreen,
    Center,
    PreviousDisplay,
    NextDisplay,
    Restore,
}

/// <summary>One window layout action (Vorssaint <c>WindowLayoutAction</c>).</summary>
/// <param name="Raw">Vorssaint raw value (<c>leftHalf</c>), also the Command Bar row id suffix and the edge-snap storage token.</param>
/// <param name="Role">Core shortcut role suffix (<c>windowLayout.&lt;Role&gt;</c>).</param>
/// <param name="Title">Exact English title.</param>
/// <param name="Group">Family.</param>
/// <param name="Kind">Frame computation.</param>
/// <param name="X0">Left fraction of the usable area.</param>
/// <param name="Y0">Top fraction.</param>
/// <param name="X1">Right fraction.</param>
/// <param name="Y1">Bottom fraction.</param>
internal sealed record LayoutAction(string Raw, string Role, string Title, LayoutGroup Group, PlacementKind Kind, double X0 = 0, double Y0 = 0, double X1 = 1, double Y1 = 1)
{
    public string RoleId => "windowLayout." + Role;

    /// <summary>Segoe Fluent Icons glyph for actions that are not drawn as a mini screen.</summary>
    public string? Glyph => Kind switch
    {
        PlacementKind.Maximize => "",
        PlacementKind.FullScreen => "",
        PlacementKind.PreviousDisplay => "",
        PlacementKind.NextDisplay => "",
        PlacementKind.Restore => "",
        _ => null,
    };
}

/// <summary>The action table (declaration order of Vorssaint's <c>WindowLayoutAction.allCases</c>).</summary>
internal static class LayoutActions
{
    private const double T = 1.0 / 3;
    private const double S = 2.0 / 3;

    public static readonly IReadOnlyList<LayoutAction> All =
    [
        new("leftHalf", "left", "Left", LayoutGroup.Halves, PlacementKind.Fraction, 0, 0, 0.5, 1),
        new("rightHalf", "right", "Right", LayoutGroup.Halves, PlacementKind.Fraction, 0.5, 0, 1, 1),
        new("topHalf", "top", "Top", LayoutGroup.Halves, PlacementKind.Fraction, 0, 0, 1, 0.5),
        new("bottomHalf", "bottom", "Bottom", LayoutGroup.Halves, PlacementKind.Fraction, 0, 0.5, 1, 1),
        new("centerHalf", "centerHalf", "Center half", LayoutGroup.Halves, PlacementKind.Fraction, 0.25, 0, 0.75, 1),

        new("leftThird", "leftThird", "Left 1/3", LayoutGroup.Thirds, PlacementKind.Fraction, 0, 0, T, 1),
        new("centerThird", "centerThird", "Center 1/3", LayoutGroup.Thirds, PlacementKind.Fraction, T, 0, S, 1),
        new("rightThird", "rightThird", "Right 1/3", LayoutGroup.Thirds, PlacementKind.Fraction, S, 0, 1, 1),
        new("leftTwoThirds", "leftTwoThirds", "Left 2/3", LayoutGroup.Thirds, PlacementKind.Fraction, 0, 0, S, 1),
        new("rightTwoThirds", "rightTwoThirds", "Right 2/3", LayoutGroup.Thirds, PlacementKind.Fraction, T, 0, 1, 1),
        new("centerTwoThirds", "centerTwoThirds", "Center 2/3", LayoutGroup.Thirds, PlacementKind.Fraction, 1.0 / 6, 0, 5.0 / 6, 1),
        new("topThird", "topThird", "Top 1/3", LayoutGroup.Thirds, PlacementKind.Fraction, 0, 0, 1, T),
        new("middleThird", "middleThird", "Middle 1/3", LayoutGroup.Thirds, PlacementKind.Fraction, 0, T, 1, S),
        new("bottomThird", "bottomThird", "Bottom 1/3", LayoutGroup.Thirds, PlacementKind.Fraction, 0, S, 1, 1),
        new("topTwoThirds", "topTwoThirds", "Top 2/3", LayoutGroup.Thirds, PlacementKind.Fraction, 0, 0, 1, S),
        new("bottomTwoThirds", "bottomTwoThirds", "Bottom 2/3", LayoutGroup.Thirds, PlacementKind.Fraction, 0, T, 1, 1),

        new("topQuarter", "topQuarter", "Top 1/4", LayoutGroup.QuarterRows, PlacementKind.Fraction, 0, 0, 1, 0.25),
        new("upperMiddleQuarter", "upperMiddleQuarter", "Upper middle 1/4", LayoutGroup.QuarterRows, PlacementKind.Fraction, 0, 0.25, 1, 0.5),
        new("lowerMiddleQuarter", "lowerMiddleQuarter", "Lower middle 1/4", LayoutGroup.QuarterRows, PlacementKind.Fraction, 0, 0.5, 1, 0.75),
        new("bottomQuarter", "bottomQuarter", "Bottom 1/4", LayoutGroup.QuarterRows, PlacementKind.Fraction, 0, 0.75, 1, 1),

        new("leftQuarter", "leftQuarter", "Left 1/4", LayoutGroup.QuarterColumns, PlacementKind.Fraction, 0, 0, 0.25, 1),
        new("leftMiddleQuarter", "leftMiddleQuarter", "Left middle 1/4", LayoutGroup.QuarterColumns, PlacementKind.Fraction, 0.25, 0, 0.5, 1),
        new("rightMiddleQuarter", "rightMiddleQuarter", "Right middle 1/4", LayoutGroup.QuarterColumns, PlacementKind.Fraction, 0.5, 0, 0.75, 1),
        new("rightQuarter", "rightQuarter", "Right 1/4", LayoutGroup.QuarterColumns, PlacementKind.Fraction, 0.75, 0, 1, 1),

        new("topLeftSixth", "topLeftSixth", "Top left 1/6", LayoutGroup.Sixths, PlacementKind.Fraction, 0, 0, T, 0.5),
        new("topCenterSixth", "topCenterSixth", "Top center 1/6", LayoutGroup.Sixths, PlacementKind.Fraction, T, 0, S, 0.5),
        new("topRightSixth", "topRightSixth", "Top right 1/6", LayoutGroup.Sixths, PlacementKind.Fraction, S, 0, 1, 0.5),
        new("bottomLeftSixth", "bottomLeftSixth", "Bottom left 1/6", LayoutGroup.Sixths, PlacementKind.Fraction, 0, 0.5, T, 1),
        new("bottomCenterSixth", "bottomCenterSixth", "Bottom center 1/6", LayoutGroup.Sixths, PlacementKind.Fraction, T, 0.5, S, 1),
        new("bottomRightSixth", "bottomRightSixth", "Bottom right 1/6", LayoutGroup.Sixths, PlacementKind.Fraction, S, 0.5, 1, 1),

        new("topLeft", "topLeft", "Top left", LayoutGroup.Corners, PlacementKind.Fraction, 0, 0, 0.5, 0.5),
        new("topRight", "topRight", "Top right", LayoutGroup.Corners, PlacementKind.Fraction, 0.5, 0, 1, 0.5),
        new("bottomLeft", "bottomLeft", "Bottom left", LayoutGroup.Corners, PlacementKind.Fraction, 0, 0.5, 0.5, 1),
        new("bottomRight", "bottomRight", "Bottom right", LayoutGroup.Corners, PlacementKind.Fraction, 0.5, 0.5, 1, 1),

        new("maximize", "maximize", "Maximize", LayoutGroup.Actions, PlacementKind.Maximize),
        new("marginMaximize", "maximizeWithMargin", "Maximize with Margin", LayoutGroup.Actions, PlacementKind.MarginMaximize, 0.05, 0.05, 0.95, 0.95),
        new("fullScreen", "fullScreen", "Full Screen", LayoutGroup.Actions, PlacementKind.FullScreen),
        new("center", "center", "Center", LayoutGroup.Actions, PlacementKind.Center, 0.25, 0.2, 0.75, 0.8),
        new("previousDisplay", "previousDisplay", "Previous display", LayoutGroup.Actions, PlacementKind.PreviousDisplay),
        new("nextDisplay", "nextDisplay", "Next display", LayoutGroup.Actions, PlacementKind.NextDisplay),
        new("restore", "restore", "Restore", LayoutGroup.Actions, PlacementKind.Restore),
    ];

    private static readonly Dictionary<string, LayoutAction> ByRaw = All.ToDictionary(a => a.Raw, StringComparer.Ordinal);

    public static LayoutAction? Get(string? raw) => raw is not null && ByRaw.TryGetValue(raw, out var a) ? a : null;

    public static LayoutAction Require(string raw) => ByRaw[raw];

    public static string GroupTitle(LayoutGroup group) => group switch
    {
        LayoutGroup.Halves => "Halves",
        LayoutGroup.Thirds => "Thirds",
        LayoutGroup.QuarterRows => "Quarter rows",
        LayoutGroup.QuarterColumns => "Quarter columns",
        LayoutGroup.Sixths => "Sixths",
        LayoutGroup.Corners => "Corners",
        _ => "Actions",
    };

    public static IEnumerable<LayoutAction> InGroup(LayoutGroup group) => All.Where(a => a.Group == group);
}
