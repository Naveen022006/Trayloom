// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

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

    public static IReadOnlyList<LayoutAction> All => _loc_All.Value;

    private static readonly LocValue<IReadOnlyList<LayoutAction>> _loc_All = new(() => [
        new("leftHalf", "left", Loc.T("Left"), LayoutGroup.Halves, PlacementKind.Fraction, 0, 0, 0.5, 1),
        new("rightHalf", "right", Loc.T("Right"), LayoutGroup.Halves, PlacementKind.Fraction, 0.5, 0, 1, 1),
        new("topHalf", "top", Loc.T("Top"), LayoutGroup.Halves, PlacementKind.Fraction, 0, 0, 1, 0.5),
        new("bottomHalf", "bottom", Loc.T("Bottom"), LayoutGroup.Halves, PlacementKind.Fraction, 0, 0.5, 1, 1),
        new("centerHalf", "centerHalf", Loc.T("Center half"), LayoutGroup.Halves, PlacementKind.Fraction, 0.25, 0, 0.75, 1),

        new("leftThird", "leftThird", Loc.T("Left 1/3"), LayoutGroup.Thirds, PlacementKind.Fraction, 0, 0, T, 1),
        new("centerThird", "centerThird", Loc.T("Center 1/3"), LayoutGroup.Thirds, PlacementKind.Fraction, T, 0, S, 1),
        new("rightThird", "rightThird", Loc.T("Right 1/3"), LayoutGroup.Thirds, PlacementKind.Fraction, S, 0, 1, 1),
        new("leftTwoThirds", "leftTwoThirds", Loc.T("Left 2/3"), LayoutGroup.Thirds, PlacementKind.Fraction, 0, 0, S, 1),
        new("rightTwoThirds", "rightTwoThirds", Loc.T("Right 2/3"), LayoutGroup.Thirds, PlacementKind.Fraction, T, 0, 1, 1),
        new("centerTwoThirds", "centerTwoThirds", Loc.T("Center 2/3"), LayoutGroup.Thirds, PlacementKind.Fraction, 1.0 / 6, 0, 5.0 / 6, 1),
        new("topThird", "topThird", Loc.T("Top 1/3"), LayoutGroup.Thirds, PlacementKind.Fraction, 0, 0, 1, T),
        new("middleThird", "middleThird", Loc.T("Middle 1/3"), LayoutGroup.Thirds, PlacementKind.Fraction, 0, T, 1, S),
        new("bottomThird", "bottomThird", Loc.T("Bottom 1/3"), LayoutGroup.Thirds, PlacementKind.Fraction, 0, S, 1, 1),
        new("topTwoThirds", "topTwoThirds", Loc.T("Top 2/3"), LayoutGroup.Thirds, PlacementKind.Fraction, 0, 0, 1, S),
        new("bottomTwoThirds", "bottomTwoThirds", Loc.T("Bottom 2/3"), LayoutGroup.Thirds, PlacementKind.Fraction, 0, T, 1, 1),

        new("topQuarter", "topQuarter", Loc.T("Top 1/4"), LayoutGroup.QuarterRows, PlacementKind.Fraction, 0, 0, 1, 0.25),
        new("upperMiddleQuarter", "upperMiddleQuarter", Loc.T("Upper middle 1/4"), LayoutGroup.QuarterRows, PlacementKind.Fraction, 0, 0.25, 1, 0.5),
        new("lowerMiddleQuarter", "lowerMiddleQuarter", Loc.T("Lower middle 1/4"), LayoutGroup.QuarterRows, PlacementKind.Fraction, 0, 0.5, 1, 0.75),
        new("bottomQuarter", "bottomQuarter", Loc.T("Bottom 1/4"), LayoutGroup.QuarterRows, PlacementKind.Fraction, 0, 0.75, 1, 1),

        new("leftQuarter", "leftQuarter", Loc.T("Left 1/4"), LayoutGroup.QuarterColumns, PlacementKind.Fraction, 0, 0, 0.25, 1),
        new("leftMiddleQuarter", "leftMiddleQuarter", Loc.T("Left middle 1/4"), LayoutGroup.QuarterColumns, PlacementKind.Fraction, 0.25, 0, 0.5, 1),
        new("rightMiddleQuarter", "rightMiddleQuarter", Loc.T("Right middle 1/4"), LayoutGroup.QuarterColumns, PlacementKind.Fraction, 0.5, 0, 0.75, 1),
        new("rightQuarter", "rightQuarter", Loc.T("Right 1/4"), LayoutGroup.QuarterColumns, PlacementKind.Fraction, 0.75, 0, 1, 1),

        new("topLeftSixth", "topLeftSixth", Loc.T("Top left 1/6"), LayoutGroup.Sixths, PlacementKind.Fraction, 0, 0, T, 0.5),
        new("topCenterSixth", "topCenterSixth", Loc.T("Top center 1/6"), LayoutGroup.Sixths, PlacementKind.Fraction, T, 0, S, 0.5),
        new("topRightSixth", "topRightSixth", Loc.T("Top right 1/6"), LayoutGroup.Sixths, PlacementKind.Fraction, S, 0, 1, 0.5),
        new("bottomLeftSixth", "bottomLeftSixth", Loc.T("Bottom left 1/6"), LayoutGroup.Sixths, PlacementKind.Fraction, 0, 0.5, T, 1),
        new("bottomCenterSixth", "bottomCenterSixth", Loc.T("Bottom center 1/6"), LayoutGroup.Sixths, PlacementKind.Fraction, T, 0.5, S, 1),
        new("bottomRightSixth", "bottomRightSixth", Loc.T("Bottom right 1/6"), LayoutGroup.Sixths, PlacementKind.Fraction, S, 0.5, 1, 1),

        new("topLeft", "topLeft", Loc.T("Top left"), LayoutGroup.Corners, PlacementKind.Fraction, 0, 0, 0.5, 0.5),
        new("topRight", "topRight", Loc.T("Top right"), LayoutGroup.Corners, PlacementKind.Fraction, 0.5, 0, 1, 0.5),
        new("bottomLeft", "bottomLeft", Loc.T("Bottom left"), LayoutGroup.Corners, PlacementKind.Fraction, 0, 0.5, 0.5, 1),
        new("bottomRight", "bottomRight", Loc.T("Bottom right"), LayoutGroup.Corners, PlacementKind.Fraction, 0.5, 0.5, 1, 1),

        new("maximize", "maximize", Loc.T("Maximize"), LayoutGroup.Actions, PlacementKind.Maximize),
        new("marginMaximize", "maximizeWithMargin", Loc.T("Maximize with Margin"), LayoutGroup.Actions, PlacementKind.MarginMaximize, 0.05, 0.05, 0.95, 0.95),
        new("fullScreen", "fullScreen", Loc.T("Full Screen"), LayoutGroup.Actions, PlacementKind.FullScreen),
        new("center", "center", Loc.T("Center"), LayoutGroup.Actions, PlacementKind.Center, 0.25, 0.2, 0.75, 0.8),
        new("previousDisplay", "previousDisplay", Loc.T("Previous display"), LayoutGroup.Actions, PlacementKind.PreviousDisplay),
        new("nextDisplay", "nextDisplay", Loc.T("Next display"), LayoutGroup.Actions, PlacementKind.NextDisplay),
        new("restore", "restore", Loc.T("Restore"), LayoutGroup.Actions, PlacementKind.Restore),
    ]);

    private static readonly Dictionary<string, LayoutAction> ByRaw = All.ToDictionary(a => a.Raw, StringComparer.Ordinal);

    public static LayoutAction? Get(string? raw) => raw is not null && ByRaw.TryGetValue(raw, out var a) ? a : null;

    public static LayoutAction Require(string raw) => ByRaw[raw];

    public static string GroupTitle(LayoutGroup group) => group switch
    {
        LayoutGroup.Halves => Loc.T("Halves"),
        LayoutGroup.Thirds => Loc.T("Thirds"),
        LayoutGroup.QuarterRows => Loc.T("Quarter rows"),
        LayoutGroup.QuarterColumns => Loc.T("Quarter columns"),
        LayoutGroup.Sixths => Loc.T("Sixths"),
        LayoutGroup.Corners => Loc.T("Corners"),
        _ => Loc.T("Actions"),
    };

    public static IEnumerable<LayoutAction> InGroup(LayoutGroup group) => All.Where(a => a.Group == group);
}
