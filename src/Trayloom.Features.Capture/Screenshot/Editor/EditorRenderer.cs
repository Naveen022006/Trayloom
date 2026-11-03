// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Capture;

/// <summary>Renders the document: marks onto the base image (vector batches + pixel passes), watermark, card corners and backdrop.</summary>
internal static class EditorRenderer
{
    private static readonly Typeface TextFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface BoldFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface EmojiFace = new(new FontFamily("Segoe UI Emoji"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // ---- text metrics (used for hit testing too) -------------------------------------------------------------

    public static FormattedText TextOf(Annotation a, double scale)
    {
        var size = Math.Max(8, a.TextSize * scale);
        return new FormattedText(string.IsNullOrEmpty(a.Text) ? " " : a.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextFace, size, new SolidColorBrush(Palette.Get(a.Color)), 1.0);
    }

    public static Rect MarkBounds(Annotation a, EditorDocument doc)
    {
        switch (a.Kind)
        {
            case ToolId.Text:
            {
                var ft = TextOf(a, doc.Scale);
                return new Rect(a.P1.X - 2, a.P1.Y - 2, ft.Width + 4, ft.Height + 4);
            }

            case ToolId.Counter:
            {
                var d = CounterDiameter(a, doc);
                return new Rect(a.P1.X - d / 2, a.P1.Y - d / 2, d, d);
            }

            case ToolId.Sticker:
            {
                var s = StickerSide(doc);
                return new Rect(a.P1.X - s / 2, a.P1.Y - s / 2, s, s);
            }

            case ToolId.Freehand:
            {
                if (a.Points.Count == 0)
                {
                    return Rect.Empty;
                }

                var r = new Rect(a.Points[0], new Size(0, 0));
                foreach (var p in a.Points)
                {
                    r.Union(p);
                }

                return r;
            }

            default:
                return a.Bounds;
        }
    }

    public static double CounterDiameter(Annotation a, EditorDocument doc)
    {
        var reference = a.RefSize > 0 ? a.RefSize : Math.Min(doc.PixelWidth, doc.PixelHeight);
        return Math.Max(22, reference / 24);
    }

    public static double StickerSide(EditorDocument doc)
    {
        var shortSide = Math.Min(doc.PixelWidth, doc.PixelHeight);
        return Math.Min(shortSide, Math.Max(52 * doc.Scale, Math.Min(0.16 * shortSide, 128 * doc.Scale)));
    }

    // ---- annotated image ---------------------------------------------------------------------------------------

    /// <summary>Base image + all marks (no watermark / backdrop). <paramref name="skip"/> hides one mark (while it is edited inline).</summary>
    public static BitmapSource RenderAnnotated(EditorDocument doc, Annotation? skip = null)
    {
        var marks = doc.Marks.Where(m => !ReferenceEquals(m, skip)).ToList();
        var w = doc.PixelWidth;
        var h = doc.PixelHeight;
        var numbers = new Dictionary<Annotation, int>();
        var n = 0;
        foreach (var m in marks.Where(m => m.Kind == ToolId.Counter))
        {
            numbers[m] = ++n;
        }

        if (!marks.Any(m => m.NeedsPixels))
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(doc.Image, new Rect(0, 0, w, h));
                DrawBatch(dc, marks, doc, numbers);
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            ApplyEffect(visual, doc);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        var buf = Pixels.Get(doc.Image);
        var batch = new List<Annotation>();
        var lastSpot = marks.FindLastIndex(m => m.Kind == ToolId.Spotlight);
        void Flush()
        {
            if (batch.Count == 0)
            {
                return;
            }

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                DrawBatch(dc, batch, doc, numbers);
            }

            ApplyEffect(visual, doc);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var overlay = new byte[w * h * 4];
            rtb.CopyPixels(overlay, w * 4, 0);
            CompositeOver(buf, overlay);
            batch.Clear();
        }

        for (var i = 0; i < marks.Count; i++)
        {
            var a = marks[i];
            switch (a.Kind)
            {
                case ToolId.Pixelate:
                    Flush();
                    ApplyBlur(buf, w, h, a, doc);
                    break;
                case ToolId.Highlight:
                    Flush();
                    ApplyHighlight(buf, w, h, a);
                    break;
                case ToolId.Spotlight:
                    if (i == lastSpot)
                    {
                        Flush();
                        ApplySpotlight(buf, w, h, marks.Where(m => m.Kind == ToolId.Spotlight).ToList(), doc.SpotlightDim);
                    }

                    break;
                default:
                    batch.Add(a);
                    break;
            }
        }

        Flush();
        return Pixels.Make(buf, w, h, doc.Image.DpiX, doc.Image.DpiY);
    }

    private static void ApplyEffect(DrawingVisual visual, EditorDocument doc)
    {
        if (doc.Shadows)
        {
            visual.Effect = new DropShadowEffect { BlurRadius = 3 * doc.Scale, ShadowDepth = 1 * doc.Scale, Direction = 270, Color = Colors.Black, Opacity = 0.38 };
        }
    }

    private static void CompositeOver(byte[] dst, byte[] src)
    {
        for (var i = 0; i < dst.Length; i += 4)
        {
            var sa = src[i + 3];
            if (sa == 0)
            {
                continue;
            }

            if (sa == 255)
            {
                dst[i] = src[i];
                dst[i + 1] = src[i + 1];
                dst[i + 2] = src[i + 2];
                dst[i + 3] = 255;
                continue;
            }

            var inv = 255 - sa;
            dst[i] = (byte)(src[i] + dst[i] * inv / 255);
            dst[i + 1] = (byte)(src[i + 1] + dst[i + 1] * inv / 255);
            dst[i + 2] = (byte)(src[i + 2] + dst[i + 2] * inv / 255);
            dst[i + 3] = (byte)(sa + dst[i + 3] * inv / 255);
        }
    }

    // ---- vector marks ------------------------------------------------------------------------------------------

    private static void DrawBatch(DrawingContext dc, IEnumerable<Annotation> marks, EditorDocument doc, Dictionary<Annotation, int> numbers)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, doc.PixelWidth, doc.PixelHeight)));
        foreach (var a in marks)
        {
            DrawMark(dc, a, doc, numbers.TryGetValue(a, out var num) ? num : 0);
        }

        dc.Pop();
    }

    public static void DrawMark(DrawingContext dc, Annotation a, EditorDocument doc, int number)
    {
        var color = Palette.Get(a.Color);
        var brush = new SolidColorBrush(color);
        var stroke = Palette.StrokeWidth(a.Stroke) * doc.Scale;
        var pen = new Pen(brush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (a.Kind)
        {
            case ToolId.Line:
                dc.DrawLine(pen, a.P1, a.P2);
                break;
            case ToolId.Rect:
                dc.DrawRectangle(null, pen, a.Bounds);
                break;
            case ToolId.Ellipse:
            {
                var b = a.Bounds;
                dc.DrawEllipse(null, pen, new Point(b.X + b.Width / 2, b.Y + b.Height / 2), b.Width / 2, b.Height / 2);
                break;
            }

            case ToolId.Redact:
                dc.DrawRectangle(brush, null, a.Bounds);
                break;
            case ToolId.Arrow:
                DrawArrow(dc, a, brush, pen, stroke);
                break;
            case ToolId.Freehand:
                dc.DrawGeometry(null, pen, FreehandGeometry(a.Points));
                break;
            case ToolId.Text:
                if (a.Text.Length > 0)
                {
                    var ft = TextOf(a, doc.Scale);
                    dc.DrawText(ft, a.P1);
                }

                break;
            case ToolId.Counter:
            {
                var d = CounterDiameter(a, doc);
                var center = a.P1;
                dc.DrawEllipse(brush, null, center, d / 2, d / 2);
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), Math.Max(1.5, d * 0.05)), center, d / 2 - 1, d / 2 - 1);
                var textBrush = a.Color == "white" ? new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1C)) : Brushes.White;
                var ft = new FormattedText(number.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BoldFace, d * 0.52, textBrush, 1.0);
                dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
                break;
            }

            case ToolId.Sticker:
            {
                var side = StickerSide(doc);
                var bmp = StickerBitmap(Palette.StickerGlyph(a.Sticker), (int)Math.Max(8, Math.Round(side * 0.82)));
                var sd = side * 0.82;
                dc.DrawImage(bmp, new Rect(a.P1.X - sd / 2, a.P1.Y - sd / 2, sd, sd));
                break;
            }

            case ToolId.Image:
                if (a.Bitmap is not null)
                {
                    dc.DrawImage(a.Bitmap, a.Bounds);
                }

                break;
        }
    }

    private static readonly Dictionary<(string, int), BitmapSource> StickerCache = new();

    private static BitmapSource StickerBitmap(string glyph, int size)
    {
        if (StickerCache.TryGetValue((glyph, size), out var cached))
        {
            return cached;
        }

        var tb = new System.Windows.Controls.TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = size * 0.8, Width = size, Height = size, TextAlignment = TextAlignment.Center, Foreground = Brushes.Black };
        tb.Measure(new Size(size, size));
        tb.Arrange(new Rect(0, 0, size, size));
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(tb);
        rtb.Freeze();
        StickerCache[(glyph, size)] = rtb;
        return rtb;
    }

    private static Geometry FreehandGeometry(List<Point> pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        if (pts.Count == 0)
        {
            return g;
        }

        c.BeginFigure(pts[0], false, false);
        if (pts.Count == 1)
        {
            c.LineTo(new Point(pts[0].X + 0.01, pts[0].Y), true, true);
        }
        else
        {
            for (var i = 1; i < pts.Count - 1; i++)
            {
                var mid = new Point((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2);
                c.QuadraticBezierTo(pts[i], mid, true, true);
            }

            c.LineTo(pts[^1], true, true);
        }

        g.Freeze();
        return g;
    }

    private static void DrawArrow(DrawingContext dc, Annotation a, Brush brush, Pen pen, double stroke)
    {
        var p1 = a.P1;
        var p2 = a.P2;
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1)
        {
            return;
        }

        var ux = dx / len;
        var uy = dy / len;
        var headLen = Math.Max(10, 3.4 * stroke);
        headLen = Math.Min(headLen, Math.Max(0.72 * len, Math.Min(1.2 * stroke, len)));
        var spread = Math.PI / 7;
        Point HeadWing(Point tip, double dirX, double dirY, int side)
        {
            var ang = Math.Atan2(dirY, dirX) + Math.PI + side * spread;
            return new Point(tip.X + Math.Cos(ang) * headLen, tip.Y + Math.Sin(ang) * headLen);
        }

        switch (a.ArrowStyle)
        {
            case "open":
            case "doubleEnded":
            case "scribbly":
            {
                if (a.ArrowStyle == "scribbly")
                {
                    var rnd = new Random(a.Seed);
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(p1, false, false);
                        var steps = Math.Max(4, (int)(len / 14));
                        for (var i = 1; i <= steps; i++)
                        {
                            var t = (double)i / steps;
                            var wob = i == steps ? 0 : (rnd.NextDouble() - 0.5) * stroke * 2.2;
                            c.LineTo(new Point(p1.X + dx * t - uy * wob, p1.Y + dy * t + ux * wob), true, true);
                        }
                    }

                    dc.DrawGeometry(null, pen, g);
                }
                else
                {
                    dc.DrawLine(pen, p1, p2);
                }

                DrawV(dc, pen, p2, HeadWing(p2, ux, uy, 1), HeadWing(p2, ux, uy, -1));
                if (a.ArrowStyle == "doubleEnded")
                {
                    DrawV(dc, pen, p1, HeadWing(p1, -ux, -uy, 1), HeadWing(p1, -ux, -uy, -1));
                }

                break;
            }

            default:
            {
                var half = Math.Max(1, stroke / 2);
                var baseCenter = new Point(p2.X - ux * headLen, p2.Y - uy * headLen);
                var nx = -uy;
                var ny = ux;
                var headHalf = headLen * Math.Tan(spread) + stroke * 0.5;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(p1.X + nx * half * 0.5, p1.Y + ny * half * 0.5), true, true);
                    c.LineTo(new Point(baseCenter.X + nx * half, baseCenter.Y + ny * half), true, true);
                    c.LineTo(new Point(baseCenter.X + nx * headHalf, baseCenter.Y + ny * headHalf), true, true);
                    c.LineTo(p2, true, true);
                    c.LineTo(new Point(baseCenter.X - nx * headHalf, baseCenter.Y - ny * headHalf), true, true);
                    c.LineTo(new Point(baseCenter.X - nx * half, baseCenter.Y - ny * half), true, true);
                    c.LineTo(new Point(p1.X - nx * half * 0.5, p1.Y - ny * half * 0.5), true, true);
                }

                g.Freeze();
                if (a.ArrowStyle == "outline")
                {
                    dc.DrawGeometry(null, new Pen(brush, Math.Max(1.5, stroke * 0.5)) { LineJoin = PenLineJoin.Round }, g);
                }
                else
                {
                    dc.DrawGeometry(brush, new Pen(brush, 0.5) { LineJoin = PenLineJoin.Round }, g);
                    dc.DrawEllipse(brush, null, p1, half * 0.5, half * 0.5);
                }

                break;
            }
        }
    }

    private static void DrawV(DrawingContext dc, Pen pen, Point tip, Point w1, Point w2)
    {
        dc.DrawLine(pen, tip, w1);
        dc.DrawLine(pen, tip, w2);
    }

    // ---- pixel passes --------------------------------------------------------------------------------------------

    private static (int X0, int Y0, int X1, int Y1) Clip(Rect r, int w, int h) =>
        ((int)Math.Max(0, Math.Floor(r.Left)), (int)Math.Max(0, Math.Floor(r.Top)), (int)Math.Min(w, Math.Ceiling(r.Right)), (int)Math.Min(h, Math.Ceiling(r.Bottom)));

    private static void ApplyHighlight(byte[] px, int w, int h, Annotation a)
    {
        var (x0, y0, x1, y1) = Clip(a.Bounds, w, h);
        var c = Palette.Get(a.Color);
        const double alpha = 0.42;
        var mr = 1 - alpha + alpha * c.R / 255.0;
        var mg = 1 - alpha + alpha * c.G / 255.0;
        var mb = 1 - alpha + alpha * c.B / 255.0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var o = (y * w + x) * 4;
                px[o] = (byte)(px[o] * mb);
                px[o + 1] = (byte)(px[o + 1] * mg);
                px[o + 2] = (byte)(px[o + 2] * mr);
            }
        }
    }

    private static void ApplySpotlight(byte[] px, int w, int h, List<Annotation> lights, double dim)
    {
        var mask = new bool[w * h];
        foreach (var l in lights)
        {
            var (x0, y0, x1, y1) = Clip(l.Bounds, w, h);
            if (l.Bounds.Width < 1 || l.Bounds.Height < 1)
            {
                continue;
            }

            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    mask[y * w + x] = true;
                }
            }
        }

        if (!mask.Any(m => m))
        {
            return;
        }

        var keep = 1 - Math.Clamp(dim, 0, 1);
        for (var i = 0; i < w * h; i++)
        {
            if (!mask[i])
            {
                var o = i * 4;
                px[o] = (byte)(px[o] * keep);
                px[o + 1] = (byte)(px[o + 1] * keep);
                px[o + 2] = (byte)(px[o + 2] * keep);
            }
        }
    }

    private static readonly double[] BlurFactors = [0.4, 0.65, 1, 1.5, 2.2];

    private static void ApplyBlur(byte[] px, int w, int h, Annotation a, EditorDocument doc)
    {
        var areas = new List<(int X0, int Y0, int X1, int Y1)>();
        var full = Clip(a.Bounds, w, h);
        if (full.X1 - full.X0 < 1 || full.Y1 - full.Y0 < 1)
        {
            return;
        }

        if (a.TextOnly && doc.TextRuns is { } runs)
        {
            foreach (var r in runs)
            {
                var i = Rect.Intersect(r, a.Bounds);
                if (!i.IsEmpty && i.Width > 0 && i.Height > 0)
                {
                    areas.Add(Clip(i, w, h));
                }
            }
        }
        else
        {
            areas.Add(full);
        }

        var level = Math.Clamp(a.BlurLevel, 1, 5);
        var baseBlock = Math.Max(10, Math.Min(w, h) / 55.0);
        var block = Math.Max(2, (int)Math.Round(baseBlock * BlurFactors[level - 1]));
        var rnd = new Random(a.Seed == 0 ? 1234 : a.Seed);
        foreach (var (x0, y0, x1, y1) in areas)
        {
            switch (a.BlurStyle)
            {
                case "erase":
                    Erase(px, w, h, x0, y0, x1, y1);
                    break;
                case "blur":
                    Mosaic(px, w, x0, y0, x1, y1, Math.Max(2, block / 3), null);
                    BoxBlur(px, w, h, x0, y0, x1, y1, Math.Max(1, (int)Math.Round(0.8 * block)));
                    break;
                default:
                    Mosaic(px, w, x0, y0, x1, y1, block, rnd);
                    break;
            }
        }
    }

    private static void Mosaic(byte[] px, int w, int x0, int y0, int x1, int y1, int block, Random? noise)
    {
        for (var by = y0; by < y1; by += block)
        {
            for (var bx = x0; bx < x1; bx += block)
            {
                var ex = Math.Min(x1, bx + block);
                var ey = Math.Min(y1, by + block);
                long sb = 0, sg = 0, sr = 0, sa = 0;
                var n = 0;
                for (var y = by; y < ey; y++)
                {
                    for (var x = bx; x < ex; x++)
                    {
                        var o = (y * w + x) * 4;
                        sb += px[o];
                        sg += px[o + 1];
                        sr += px[o + 2];
                        sa += px[o + 3];
                        n++;
                    }
                }

                if (n == 0)
                {
                    continue;
                }

                var jitter = noise is null ? 0 : noise.Next(-5, 6);
                byte B(long v) => (byte)Math.Clamp(v / n + jitter, 0, 255);
                var nb = B(sb);
                var ng = B(sg);
                var nr = B(sr);
                var na = (byte)(sa / n);
                for (var y = by; y < ey; y++)
                {
                    for (var x = bx; x < ex; x++)
                    {
                        var o = (y * w + x) * 4;
                        px[o] = nb;
                        px[o + 1] = ng;
                        px[o + 2] = nr;
                        px[o + 3] = na;
                    }
                }
            }
        }
    }

    private static void BoxBlur(byte[] px, int w, int h, int x0, int y0, int x1, int y1, int radius)
    {
        var rw = x1 - x0;
        var rh = y1 - y0;
        var src = new byte[rw * rh * 4];
        for (var y = 0; y < rh; y++)
        {
            Buffer.BlockCopy(px, ((y0 + y) * w + x0) * 4, src, y * rw * 4, rw * 4);
        }

        var tmp = new byte[src.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            BoxH(src, tmp, rw, rh, radius);
            BoxV(tmp, src, rw, rh, radius);
        }

        for (var y = 0; y < rh; y++)
        {
            Buffer.BlockCopy(src, y * rw * 4, px, ((y0 + y) * w + x0) * 4, rw * 4);
        }
    }

    private static void BoxH(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (var y = 0; y < h; y++)
        {
            for (var c = 0; c < 4; c++)
            {
                long sum = 0;
                for (var i = -r; i <= r; i++)
                {
                    sum += src[(y * w + Math.Clamp(i, 0, w - 1)) * 4 + c];
                }

                for (var x = 0; x < w; x++)
                {
                    dst[(y * w + x) * 4 + c] = (byte)(sum / (2 * r + 1));
                    var add = Math.Min(w - 1, x + r + 1);
                    var sub = Math.Max(0, x - r);
                    sum += src[(y * w + add) * 4 + c] - src[(y * w + sub) * 4 + c];
                }
            }
        }
    }

    private static void BoxV(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (var x = 0; x < w; x++)
        {
            for (var c = 0; c < 4; c++)
            {
                long sum = 0;
                for (var i = -r; i <= r; i++)
                {
                    sum += src[(Math.Clamp(i, 0, h - 1) * w + x) * 4 + c];
                }

                for (var y = 0; y < h; y++)
                {
                    dst[(y * w + x) * 4 + c] = (byte)(sum / (2 * r + 1));
                    var add = Math.Min(h - 1, y + r + 1);
                    var sub = Math.Max(0, y - r);
                    sum += src[(add * w + x) * 4 + c] - src[(sub * w + x) * 4 + c];
                }
            }
        }
    }

    private static void Erase(byte[] px, int w, int h, int x0, int y0, int x1, int y1)
    {
        // Fill from the surroundings: inverse-distance blend of the nearest border pixels left/right and above/below.
        var rw = x1 - x0;
        var rh = y1 - y0;
        int Band(int x, int y, int c) => px[(Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)) * 4 + c];
        var left = new int[rh, 4];
        var right = new int[rh, 4];
        var top = new int[rw, 4];
        var bottom = new int[rw, 4];
        for (var y = 0; y < rh; y++)
        {
            for (var c = 0; c < 4; c++)
            {
                left[y, c] = (Band(x0 - 3, y0 + y, c) + Band(x0 - 5, y0 + y, c) + Band(x0 - 7, y0 + y, c)) / 3;
                right[y, c] = (Band(x1 + 2, y0 + y, c) + Band(x1 + 4, y0 + y, c) + Band(x1 + 6, y0 + y, c)) / 3;
            }
        }

        for (var x = 0; x < rw; x++)
        {
            for (var c = 0; c < 4; c++)
            {
                top[x, c] = (Band(x0 + x, y0 - 3, c) + Band(x0 + x, y0 - 5, c) + Band(x0 + x, y0 - 7, c)) / 3;
                bottom[x, c] = (Band(x0 + x, y1 + 2, c) + Band(x0 + x, y1 + 4, c) + Band(x0 + x, y1 + 6, c)) / 3;
            }
        }

        for (var y = 0; y < rh; y++)
        {
            for (var x = 0; x < rw; x++)
            {
                double dl = x + 1, dr = rw - x, dt = y + 1, db = rh - y;
                var o = ((y0 + y) * w + x0 + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    var horizontal = (left[y, c] * dr + right[y, c] * dl) / (dl + dr);
                    var vertical = (top[x, c] * db + bottom[x, c] * dt) / (dt + db);
                    var wh = 1.0 / Math.Min(dl, dr);
                    var wv = 1.0 / Math.Min(dt, db);
                    px[o + c] = (byte)Math.Clamp((horizontal * wh + vertical * wv) / (wh + wv), 0, 255);
                }
            }
        }
    }

    // ---- export ----------------------------------------------------------------------------------------------------

    /// <summary>Base + marks + watermark: what the canvas shows and what direct outputs flatten.</summary>
    public static BitmapSource RenderFlat(EditorDocument doc, Annotation? skip = null)
    {
        var img = RenderAnnotated(doc, skip);
        return doc.Watermark.IsNone ? img : DrawWatermark(img, doc);
    }

    public static int CardCorner(EditorDocument doc, BitmapSource img) =>
        (int)Math.Round(Math.Clamp(doc.Backdrop.CornerRadius, 0, 1) * Math.Min(img.PixelWidth, img.PixelHeight) * 0.2);

    public static int BackdropMargin(BackdropStyle b, int w, int h) =>
        (int)Math.Max(24, Math.Round(Math.Min(w, h) * (0.035 + 0.14 * b.Padding)));

    /// <summary>The final image: corners, backdrop with shadow. <paramref name="withBackdrop"/> false = pinned / direct (no backdrop).</summary>
    public static BitmapSource RenderExport(EditorDocument doc, bool withBackdrop = true)
    {
        var flat = RenderFlat(doc);
        var corner = CardCorner(doc, flat);
        var fill = withBackdrop ? doc.Backdrop.CreateBrush(flat.PixelWidth, flat.PixelHeight) : null;
        if (fill is null && corner == 0)
        {
            return flat;
        }

        var w = flat.PixelWidth;
        var h = flat.PixelHeight;
        var cardRect = new Rect(0, 0, w, h);
        var root = new ContainerVisual();
        double cw = w;
        double ch = h;
        if (fill is not null)
        {
            var m = BackdropMargin(doc.Backdrop, w, h);
            cw = w + 2 * m;
            ch = h + 2 * m;
            cardRect = new Rect(m, m, w, h);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen())
            {
                var blur = doc.Backdrop.Blur * Math.Min(cw, ch) * 0.035;
                var pad = blur > 0 ? blur * 2 : 0;
                dc.DrawRectangle(fill, null, new Rect(-pad, -pad, cw + 2 * pad, ch + 2 * pad));
            }

            if (doc.Backdrop.Blur > 0)
            {
                bg.Effect = new BlurEffect { Radius = doc.Backdrop.Blur * Math.Min(cw, ch) * 0.035, KernelType = KernelType.Gaussian };
            }

            root.Children.Add(bg);
            var shadow = new DrawingVisual();
            using (var dc = shadow.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, new Rect(cardRect.X, cardRect.Y + 6 * doc.Scale, w, h), corner, corner);
            }

            shadow.Effect = new BlurEffect { Radius = 22 * doc.Scale, KernelType = KernelType.Gaussian };
            root.Children.Add(shadow);
        }

        var card = new DrawingVisual();
        using (var dc = card.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(cardRect, corner, corner));
            dc.DrawImage(flat, cardRect);
            dc.Pop();
        }

        root.Children.Add(card);
        var target = new RenderTargetBitmap((int)cw, (int)ch, 96, 96, PixelFormats.Pbgra32);
        var wrap = new DrawingVisual();
        using (var dc = wrap.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, cw, ch)));
            dc.Pop();
        }

        target.Render(root);
        target.Freeze();
        return target;
    }

    private static BitmapSource DrawWatermark(BitmapSource img, EditorDocument doc)
    {
        var wm = doc.Watermark;
        var w = img.PixelWidth;
        var h = img.PixelHeight;
        var shortSide = Math.Min(w, h);
        double mw, mh;
        FormattedText? ft = null;
        BitmapSource? logo = null;
        if (wm.Kind == "text")
        {
            var size = Math.Max(8, Math.Round(shortSide * (0.02 + 0.14 * wm.Size)));
            ft = new FormattedText(wm.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextFace, size, new SolidColorBrush(Palette.Get(wm.Color)), 1.0);
            mw = ft.Width;
            mh = ft.Height;
        }
        else
        {
            logo = Output.LoadImage(wm.ImagePath!);
            if (logo is null)
            {
                return img;
            }

            mw = Math.Max(1, Math.Round(w * (0.05 + 0.45 * wm.Size)));
            mh = mw * logo.PixelHeight / logo.PixelWidth;
        }

        var rad = wm.Rotation * Math.PI / 180;
        var bw = Math.Abs(mw * Math.Cos(rad)) + Math.Abs(mh * Math.Sin(rad));
        var bh = Math.Abs(mw * Math.Sin(rad)) + Math.Abs(mh * Math.Cos(rad));
        var corner = CardCorner(doc, img);
        var margin = Math.Min(0.45 * shortSide, Math.Max(0.05 * shortSide, Math.Ceiling(corner * (1 - Math.Sqrt(0.5))) + 1));
        var ux = wm.Anchor.Contains("Leading", StringComparison.Ordinal) || wm.Anchor == "leading" ? 0 : wm.Anchor.Contains("Trailing", StringComparison.Ordinal) || wm.Anchor == "trailing" ? 1 : 0.5;
        var uy = wm.Anchor.StartsWith("top", StringComparison.Ordinal) ? 0 : wm.Anchor.StartsWith("bottom", StringComparison.Ordinal) ? 1 : 0.5;
        var scale = Math.Min(1, Math.Min((w - 2 * margin) / bw, (h - 2 * margin) / bh));
        if (scale <= 0)
        {
            return img;
        }

        var sw = bw * scale;
        var sh = bh * scale;
        var cx = margin + (w - 2 * margin - sw) * ux + sw / 2;
        var cy = margin + (h - 2 * margin - sh) * uy + sh / 2;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(img, new Rect(0, 0, w, h));
            dc.PushTransform(new TranslateTransform(cx, cy));
            dc.PushTransform(new RotateTransform(-wm.Rotation));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushOpacity(wm.Opacity);
            var area = new Rect(-mw / 2, -mh / 2, mw, mh);
            if (ft is not null)
            {
                dc.DrawText(ft, area.TopLeft);
            }
            else
            {
                dc.DrawImage(logo!, area);
            }

            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        if (doc.Shadows)
        {
            visual.Effect = new DropShadowEffect { BlurRadius = 3 * doc.Scale, ShadowDepth = 1 * doc.Scale, Direction = 270, Opacity = 0.38 };
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
