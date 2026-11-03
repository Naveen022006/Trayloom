// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;

namespace WinKnife.Features.Agents;

/// <summary>
/// The four agent marks as frozen WPF geometries on a 24 x 24 grid, so they stay crisp from 12 px in the closed island to 64 px.
/// They are WinKnife's own vector drawings of each product's well-known silhouette (no artwork is copied from any app): the
/// Claude spark is Claude's own burst outline (CC0 Simple Icons); Codex a six-lobed knot; OpenCode a framed block; Copilot a visored head.
/// </summary>
internal static class AgentMarkGeometry
{
    /// <summary>
    /// Claude's spark on the 24 x 24 grid: the irregular, tapering burst everyone knows from the Claude apps (outline from the
    /// CC0 Simple Icons set), so the island shows the real mark instead of an approximation.
    /// </summary>
    private const string SparkPath =
        "M4.709 15.955l4.72-2.647.08-.23-.08-.128H9.2l-.79-.048-2.698-.073-2.339-.097-2.266-.122-.571-.121L0 11.784l.05"
        + "5-.352.48-.321.686.06 1.52.103 2.278.158 1.652.097 2.449.255h.389l.055-.157-.134-.098-.103-.097-2.358-1.596-2."
        + "552-1.688-1.336-.972-.724-.491-.364-.462-.158-1.008.656-.722.881.06.225.061.893.686 1.908 1.476 2.491 1.833.36"
        + "5.304.145-.103.019-.073-.164-.274-1.355-2.446-1.446-2.49-.644-1.032-.17-.619a2.97 2.97 0 01-.104-.729L6.283.13"
        + "4 6.696 0l.996.134.42.364.62 1.414 1.002 2.229 1.555 3.03.456.898.243.832.091.255h.158V9.01l.128-1.706.237-2.0"
        + "95.23-2.695.08-.76.376-.91.747-.492.584.28.48.685-.067.444-.286 1.851-.559 2.903-.364 1.942h.212l.243-.242.985"
        + "-1.306 1.652-2.064.73-.82.85-.904.547-.431h1.033l.76 1.129-.34 1.166-1.064 1.347-.881 1.142-1.264 1.7-.79 1.36"
        + ".073.11.188-.02 2.856-.606 1.543-.28 1.841-.315.833.388.091.395-.328.807-1.969.486-2.309.462-3.439.813-.042.03"
        + ".049.061 1.549.146.662.036h1.622l3.02.225.79.522.474.638-.079.485-1.215.62-1.64-.389-3.829-.91-1.312-.329h-.18"
        + "2v.11l1.093 1.068 2.006 1.81 2.509 2.33.127.578-.322.455-.34-.049-2.205-1.657-.851-.747-1.926-1.62h-.128v.17l."
        + "444.649 2.345 3.521.122 1.08-.17.353-.608.213-.668-.122-1.374-1.925-1.415-2.167-1.143-1.943-.14.08-.674 7.254-"
        + ".316.37-.729.28-.607-.461-.322-.747.322-1.476.389-1.924.315-1.53.286-1.9.17-.632-.012-.042-.14.018-1.434 1.967"
        + "-2.18 2.945-1.726 1.845-.414.164-.717-.37.067-.662.401-.589 2.388-3.036 1.44-1.882.93-1.086-.006-.158h-.055L4."
        + "132 18.56l-1.13.146-.487-.456.061-.704.231-.243 1.908-1.312-.006.006z";

    private static Geometry? _spark;

    public static Geometry Spark => _spark ??= BuildSpark();

    private static Geometry BuildSpark()
    {
        var geometry = Geometry.Parse("F1 " + SparkPath);
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
