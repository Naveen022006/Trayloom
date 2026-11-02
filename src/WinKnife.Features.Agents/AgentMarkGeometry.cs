// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;

namespace WinKnife.Features.Agents;

/// <summary>
/// The four agent marks as frozen WPF geometries on a 24 x 24 grid, so they stay crisp from 12 px in the closed island to 64 px.
/// They are WinKnife's own vector drawings of each product's well-known silhouette (no artwork is copied from any app): the
/// Claude spark is a filled, irregular twelve-ray starburst; Codex a six-lobed knot; OpenCode a framed block; Copilot a visored head.
/// </summary>
internal static class AgentMarkGeometry
{
    /// <summary>Ray angles (degrees clockwise from up) and lengths (grid units) of the Claude spark: uneven on purpose.</summary>
    private static readonly (double Angle, double Length)[] SparkRays =
    [
        (0, 11.5), (27, 8.2), (54, 10.8), (83, 8.8), (112, 11.5), (139, 7.8),
        (168, 10.6), (196, 8.9), (225, 11.4), (255, 8.2), (290, 10.9), (322, 8.6),
    ];

    private static Geometry? _spark;

    /// <summary>A filled burst: every ray is a tapered stalk with a rounded tip, all growing out of one hub.</summary>
    public static Geometry Spark => _spark ??= BuildSpark();

    private static Geometry BuildSpark()
    {
        const double cx = 12, cy = 12, baseHalf = 1.05, tipHalf = 0.78;
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geometry.Open())
        {
            foreach (var (angle, length) in SparkRays)
            {
                var a = angle * Math.PI / 180;
                Point d = new(Math.Sin(a), -Math.Cos(a));
                Point n = new(Math.Cos(a), Math.Sin(a));
                var stalk = length - tipHalf;
                Point At(double along, double side) => new(cx + d.X * along + n.X * side, cy + d.Y * along + n.Y * side);
                ctx.BeginFigure(At(0, baseHalf), true, true);
                ctx.LineTo(At(stalk, tipHalf), true, false);
                ctx.ArcTo(At(stalk, -tipHalf), new Size(tipHalf, tipHalf), 0, false, SweepDirection.Counterclockwise, true, false);
                ctx.LineTo(At(0, -baseHalf), true, false);
            }

            // The hub that joins the rays.
            ctx.BeginFigure(new Point(cx + 2.0, cy), true, true);
            ctx.ArcTo(new Point(cx - 2.0, cy), new Size(2.0, 2.0), 0, false, SweepDirection.Counterclockwise, true, false);
            ctx.ArcTo(new Point(cx + 2.0, cy), new Size(2.0, 2.0), 0, false, SweepDirection.Counterclockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

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
