// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;

namespace Trayloom.Features.Agents;

/// <summary>
/// The four agent marks as frozen WPF geometries on a 24 x 24 grid, so they stay crisp from 12 px in the closed island to 64 px.
/// They are Trayloom's own vector drawings of each product's well-known silhouette (no artwork is copied from any app): the
/// Claude spark is Claude's own burst outline (CC0 Simple Icons); Codex a six-lobed knot; OpenCode a framed block; Copilot a visored head.
/// </summary>
internal static class AgentMarkGeometry
{
    /// <summary>Claude's spark on the 24 x 24 grid (one shared outline: <see cref="Trayloom.Core.Ui.ClaudeMark.Spark"/>).</summary>
    public static Geometry Spark => Trayloom.Core.Ui.ClaudeMark.Spark;

    private static Geometry? _knot;

    /// <summary>Six overlapping capsule loops around a hexagonal centre; drawn as an outline (stroke 1.55).</summary>
    public static Geometry Knot => _knot ??= BuildKnot();

    private static Geometry BuildKnot()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var i = 0; i < 6; i++)
        {
            var loop = new RectangleGeometry(new Rect(12 - 3.15, 12 - 4.9 - 6.6, 6.3, 13.2), 3.15, 3.15)
            {
                Transform = new RotateTransform(i * 60, 12, 12),
            };
            group.Children.Add(loop);
        }

        group.Freeze();
        return group;
    }

    private static Geometry? _frame;

    /// <summary>OpenCode: a tall frame with an open window.</summary>
    public static Geometry Frame => _frame ??= Freeze(new GeometryGroup
    {
        FillRule = FillRule.EvenOdd,
        Children =
        {
            new RectangleGeometry(new Rect(4.5, 2.5, 15, 19), 1.2, 1.2),
            new RectangleGeometry(new Rect(8.6, 6.6, 6.8, 10.8)),
        },
    });

    private static Geometry? _frameInner;

    /// <summary>The lower half of OpenCode's window, drawn lighter.</summary>
    public static Geometry FrameInner => _frameInner ??= Freeze(new RectangleGeometry(new Rect(8.6, 12, 6.8, 5.4)));

    private static Geometry? _head;

    /// <summary>Copilot's face: a rounded visor with two slit eyes cut out and two side ears.</summary>
    public static Geometry Head => _head ??= Freeze(new GeometryGroup
    {
        FillRule = FillRule.EvenOdd,
        Children =
        {
            new RectangleGeometry(new Rect(3.8, 8.2, 16.4, 11.8), 5.4, 5.4),
            new RectangleGeometry(new Rect(8.0, 12.1, 2.0, 4.1), 1.0, 1.0),
            new RectangleGeometry(new Rect(14.0, 12.1, 2.0, 4.1), 1.0, 1.0),
        },
    });

    private static Geometry? _ears;

    public static Geometry Ears => _ears ??= Freeze(new GeometryGroup
    {
        Children =
        {
            new RectangleGeometry(new Rect(1.2, 11.2, 2.0, 5.2), 1.0, 1.0),
            new RectangleGeometry(new Rect(20.8, 11.2, 2.0, 5.2), 1.0, 1.0),
        },
    });

    private static Geometry? _crown;

    /// <summary>The arch over Copilot's head (stroked).</summary>
    public static Geometry Crown => _crown ??= Freeze(Geometry.Parse("M7.4,7.4 C7.4,2.2 16.6,2.2 16.6,7.4"));

    private static Geometry Freeze(Geometry g)
    {
        g.Freeze();
        return g;
    }
}
