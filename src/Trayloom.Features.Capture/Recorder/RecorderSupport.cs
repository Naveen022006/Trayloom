// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;

namespace Trayloom.Features.Capture;

/// <summary>Pure recorder policies (quality tables, canvas maths, labels, trimming, disk thresholds).</summary>
internal static class RecorderSupport
{
    public const long StartFreeBytes = 2_000_000_000;
    public const long ContinueFreeBytes = 500_000_000;

    public static int EvenSize(double v)
    {
        var n = Math.Max(32, (int)Math.Floor(v));
        return n % 2 == 1 ? n - 1 : n;
    }

    /// <summary>Intersects with the bounds, floors the origin, makes sizes even and shrinks rather than shifts.</summary>
    public static Int32Rect SnapRect(Int32Rect rect, Int32Rect bounds)
    {
        var x1 = Math.Max(rect.X, bounds.X);
        var y1 = Math.Max(rect.Y, bounds.Y);
        var x2 = Math.Min(rect.X + rect.Width, bounds.X + bounds.Width);
        var y2 = Math.Min(rect.Y + rect.Height, bounds.Y + bounds.Height);
        if (x2 <= x1 || y2 <= y1)
        {
            return bounds;
        }

        var w = EvenSize(x2 - x1);
        var h = EvenSize(y2 - y1);
        w = Math.Min(w, (x2 - x1) - (x2 - x1) % 2);
        h = Math.Min(h, (y2 - y1) - (y2 - y1) % 2);
        return new Int32Rect(x1, y1, Math.Max(2, w), Math.Max(2, h));
    }

    public static string ElapsedLabel(double seconds)
    {
        var s = Math.Max(0, (int)Math.Floor(seconds));
        var h = s / 3600;
        var m = s % 3600 / 60;
        var sec = s % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
    }

    public static int SanitizedDelay(int v) => v is 0 or 3 or 5 or 10 ? v : 0;

    public static int SanitizedFps(int v) => v is 30 or 60 ? v : 60;

    public static string SanitizedQuality(string? v) => v is "small" or "balanced" or "high" ? v : "balanced";

    public static double OutputScale(string quality) => quality == "small" ? 0.5 : 1.0;

    public static double BitsPerPixel(string quality) => quality switch { "small" => 0.05, "high" => 0.09, _ => 0.06 };

    public static int KeyframeSeconds(string quality) => quality switch { "high" => 2, _ => 10 };

    public static int AverageBitRate(int w, int h, int fps, string quality) =>
        (int)Math.Clamp((double)w * h * fps * BitsPerPixel(quality), 800_000, 60_000_000);

    public static (double Start, double End) SanitizedTrim(double start, double end, double duration)
    {
        if (!double.IsFinite(end) || end <= 0 || end > duration)
        {
            end = duration;
        }

        start = double.IsFinite(start) ? Math.Clamp(start, 0, duration) : 0;
        if (end - start < 0.2)
        {
            if (start + 0.2 <= duration)
            {
                end = start + 0.2;
            }
            else
            {
                start = Math.Max(0, duration - 0.2);
                end = duration;
            }
        }

        return (start, end);
    }

    public static (int W, int H) GifSize(int srcW, int srcH, string size)
    {
        var longEdge = size switch { "small" => 420, "large" => 800, _ => 600 };
        var f = Math.Min(1.0, (double)longEdge / Math.Max(srcW, srcH));
        return (Math.Max(2, (int)Math.Round(srcW * f / 2) * 2), Math.Max(2, (int)Math.Round(srcH * f / 2) * 2));
    }

    public static long FreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return long.MaxValue;
        }
    }

    public static string FileStamp(DateTime now) => now.ToString("yyyy-MM-dd 'at' HH.mm.ss", CultureInfo.InvariantCulture);

    public static string DefaultName(DateTime now) => $"{Strings.Recorder.FileNamePrefix} {FileStamp(now)}";

    // ---- canvas -------------------------------------------------------------------------------------------------------

    public const double MaximumCanvasEdge = 3840;

    /// <summary>The whole picture, background included. A visible background grows the short side; without one a shape crops the source.</summary>
    public static Size CanvasSize(Size source, double padding, double? ratio, bool cropsToAspect)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            return new Size(EvenSize(source.Width), EvenSize(source.Height));
        }

        var margin = Math.Min(0.35, Math.Max(0, padding));
        var inner = 1 - 2 * margin;
        if (inner <= 0.05)
        {
            return new Size(EvenSize(source.Width), EvenSize(source.Height));
        }

        var width = source.Width / inner;
        var height = source.Height / inner;
        if (ratio is { } r)
        {
            if (cropsToAspect)
            {
                if (width / height > r)
                {
                    width = height * r;
                }
                else
                {
                    height = width / r;
                }
            }
            else if (width / height < r)
            {
                width = height * r;
            }
            else
            {
                height = width / r;
            }
        }

        var longest = Math.Max(width, height);
        if (longest > MaximumCanvasEdge)
        {
            var f = MaximumCanvasEdge / longest;
            width *= f;
            height *= f;
        }

        return new Size(EvenSize(width), EvenSize(height));
    }

    /// <summary>Where the recording sits inside the canvas: centred, own proportions; a crop covers the canvas.</summary>
    public static Rect CardRect(Size canvas, Size source, double padding, bool fillsCanvas)
    {
        if (source.Width <= 0 || source.Height <= 0 || canvas.Width <= 0 || canvas.Height <= 0)
        {
            return new Rect(0, 0, canvas.Width, canvas.Height);
        }

        var margin = Math.Min(0.35, Math.Max(0, padding));
        var availW = canvas.Width * (1 - 2 * margin);
        var availH = canvas.Height * (1 - 2 * margin);
        var fx = availW / source.Width;
        var fy = availH / source.Height;
        var f = fillsCanvas ? Math.Max(fx, fy) : Math.Min(fx, fy);
        var w = Math.Round(source.Width * f);
        var h = Math.Round(source.Height * f);
        return new Rect(Math.Round((canvas.Width - w) / 2), Math.Round((canvas.Height - h) / 2), w, h);
    }

    public static double CardCorner(double factor, Size card) => Math.Round(Math.Clamp(factor, 0, 1) * Math.Min(card.Width, card.Height) * 0.09);

    /// <summary>A point on the stage as the picture's own 0..1 space (aspect-fit letterbox maths); not clamped.</summary>
    public static Point? UnitPoint(Point location, Size view, Size source)
    {
        if (source.Width <= 0 || source.Height <= 0 || view.Width <= 0 || view.Height <= 0)
        {
            return null;
        }

        var fit = Math.Min(view.Width / source.Width, view.Height / source.Height);
        var shownW = source.Width * fit;
        var shownH = source.Height * fit;
        return new Point((location.X - (view.Width - shownW) / 2) / shownW, (location.Y - (view.Height - shownH) / 2) / shownH);
    }

    public static double Smoothstep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static double Smootherstep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * t * (t * (6 * t - 15) + 10);
    }
}
