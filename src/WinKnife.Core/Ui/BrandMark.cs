// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace WinKnife.Core.Ui;

/// <summary>
/// The WinKnife mark (a small pocket knife, 48 x 28 design box). Used in the flyout header, the About badge, onboarding and
/// the tray icon. Never the Vorssaint saturn.
/// </summary>
public static class BrandMark
{
    /// <summary>Design width of <see cref="Geometry"/>.</summary>
    public const double Width = 48;

    /// <summary>Design height of <see cref="Geometry"/>.</summary>
    public const double Height = 28;

    private const string PathData =
        "F0 M5,9 H20 Q21.6,9 23,10 C31,10 39,6.5 46.5,1.6 C45.5,10.5 40,17.6 23,19 Q21.6,19 20,19 H5 A5,5 0 0 1 5,9 Z " +
        "M8.4,14 A2.2,2.2 0 1 0 12.8,14 A2.2,2.2 0 1 0 8.4,14 Z " +
        "M24,22 H40 Q42,22 42,24 Q42,26 40,26 H24 Q22,26 22,24 Q22,22 24,22 Z";

    private static readonly Geometry Shape = Freeze(Geometry.Parse(PathData));

    /// <summary>The mark geometry in a 48 x 28 box.</summary>
    public static Geometry Geometry => Shape;

    /// <summary>Creates a path element of the given width (height follows the aspect ratio) filled with a theme brush.</summary>
    public static Path Create(double width, string brushKey = ThemeKeys.TextPrimary)
    {
        var path = new Path
        {
            Data = Shape,
            Stretch = Stretch.Uniform,
            Width = width,
            Height = width * Height / Width,
        };
        path.SetResourceReference(Shape_Fill, brushKey);
        return path;
    }

    private static DependencyProperty Shape_Fill => System.Windows.Shapes.Shape.FillProperty;

    /// <summary>Renders the mark into a square bitmap (tray icon) with the given colour. <paramref name="badge"/> draws a small coloured dot.</summary>
    public static BitmapSource Render(int size, Color color, Color? badge = null)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var scale = size * 0.92 / Width;
            var h = Height * scale;
            dc.PushTransform(new TranslateTransform((size - Width * scale) / 2, (size - h) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(new SolidColorBrush(color), null, Shape);
            dc.Pop();
            dc.Pop();
            if (badge is { } b)
            {
                var r = size * 0.2;
                dc.DrawEllipse(new SolidColorBrush(b), null, new Point(size - r - 0.5, size - r - 0.5), r, r);
            }
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static Geometry Freeze(Geometry g)
    {
        g.Freeze();
        return g;
    }
}
