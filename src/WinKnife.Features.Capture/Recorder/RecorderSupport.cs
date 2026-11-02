// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;

namespace WinKnife.Features.Capture;

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
