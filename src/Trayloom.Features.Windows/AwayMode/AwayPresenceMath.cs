// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>
/// What one camera sample showed. <see cref="Usable"/> is false for a dark or flat frame (a covered lens, a dark room), which never
/// counts as nobody being there; <see cref="MotionKnown"/> is false for the first sample, which has no earlier frame to compare with.
/// </summary>
internal readonly record struct AwayObservation(bool Face, bool Motion, bool Usable, bool MotionKnown);

/// <summary>
/// The pure picture maths of the camera-based leaving check: a tiny brightness grid per frame and a frame difference that ignores
/// exposure changes and sensor noise. Only grids of 3 KB are compared; the frames themselves are never kept.
/// </summary>
internal static class AwayPresenceMath
{
    public const int GridWidth = 64;
    public const int GridHeight = 48;

    /// <summary>A darker average than this is a dark room or a covered lens, not an empty chair.</summary>
    public const double MinMeanLuma = 20;

    /// <summary>A picture flatter than this (standard deviation of the grid) has nothing in it to compare.</summary>
    public const double MinSpread = 5;

    public const int BaseThreshold = 12;
    public const int MaxThreshold = 40;

    /// <summary>At least this share of the cells must change (about 37 of 3 072: a head turn or a hand).</summary>
    public const double MotionShare = 0.012;

    /// <summary>
    /// The oldest earlier grid a frame is compared with. The relaxed rate takes a sample every 3 s, and the next frame always arrives a
    /// little after that, so a 3 s limit would leave every relaxed sample without a comparison and the absence would never be counted.
    /// </summary>
    public const long MaxGridAgeMs = 4500;

    /// <summary>True when a grid taken <paramref name="ageMs"/> ago can still show movement.</summary>
    public static bool Comparable(long ageMs) => ageMs >= 0 && ageMs <= MaxGridAgeMs;

    /// <summary>
    /// Block means of an 8-bit gray frame. The frame may have a stride larger than its width; blocks at the right and bottom edge
    /// take the remaining pixels, so a frame whose size does not divide evenly is still covered completely.
    /// </summary>
    public static byte[] Grid(ReadOnlySpan<byte> gray, int start, int width, int height, int stride)
    {
        var grid = new byte[GridWidth * GridHeight];
        if (width <= 0 || height <= 0)
        {
            return grid;
        }

        for (var gy = 0; gy < GridHeight; gy++)
        {
            var y0 = gy * height / GridHeight;
            var y1 = Math.Max((gy + 1) * height / GridHeight, y0 + 1);
            y1 = Math.Min(y1, height);
            y0 = Math.Min(y0, y1 - 1);
            for (var gx = 0; gx < GridWidth; gx++)
            {
                var x0 = gx * width / GridWidth;
                var x1 = Math.Max((gx + 1) * width / GridWidth, x0 + 1);
                x1 = Math.Min(x1, width);
                x0 = Math.Min(x0, x1 - 1);

                long sum = 0;
                for (var y = y0; y < y1; y++)
                {
                    var row = start + (y * stride);
                    for (var x = x0; x < x1; x++)
                    {
                        sum += gray[row + x];
                    }
                }

                var count = (x1 - x0) * (y1 - y0);
                grid[(gy * GridWidth) + gx] = (byte)((sum + (count / 2)) / count);
            }
        }

        return grid;
    }

    /// <summary>Average brightness and its spread (standard deviation) over the cells.</summary>
    public static (double Mean, double Spread) Stats(ReadOnlySpan<byte> grid)
    {
        if (grid.Length == 0)
        {
            return (0, 0);
        }

        double sum = 0;
        foreach (var value in grid)
        {
            sum += value;
        }

        var mean = sum / grid.Length;
        double squares = 0;
        foreach (var value in grid)
        {
            var delta = value - mean;
            squares += delta * delta;
        }

        return (mean, Math.Sqrt(squares / grid.Length));
    }

    /// <summary>A frame is usable when it is bright enough and has enough contrast; unusable frames are never counted as absence.</summary>
    public static bool Usable((double Mean, double Spread) s) => s.Mean >= MinMeanLuma && s.Spread >= MinSpread;

    /// <summary>
    /// The share of cells that changed between two grids. Each grid is first shifted by its own mean so an auto-exposure step cancels,
    /// and the threshold grows with the typical change (the median), so a noisy sensor does not look like movement.
    /// </summary>
    public static double ChangedShare(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current)
    {
        var count = Math.Min(previous.Length, current.Length);
        if (count == 0)
        {
            return 0;
        }

        double sumPrevious = 0;
        double sumCurrent = 0;
        for (var i = 0; i < count; i++)
        {
            sumPrevious += previous[i];
            sumCurrent += current[i];
        }

        var meanPrevious = sumPrevious / count;
        var meanCurrent = sumCurrent / count;
        var differences = new double[count];
        var histogram = new int[256];
        for (var i = 0; i < count; i++)
        {
            var d = Math.Abs((current[i] - meanCurrent) - (previous[i] - meanPrevious));
            differences[i] = d;
            histogram[Math.Min(255, (int)Math.Round(d))]++;
        }

        // The median comes from a counting sort over 0..255.
        var median = 0;
        var seen = 0;
        for (; median < 256; median++)
        {
            seen += histogram[median];
            if (seen * 2 >= count)
            {
                break;
            }
        }

        var threshold = Math.Clamp(BaseThreshold + (2 * median), BaseThreshold, MaxThreshold);
        var changed = 0;
        foreach (var d in differences)
        {
            if (d > threshold)
            {
                changed++;
            }
        }

        return (double)changed / count;
    }

    public static bool Motion(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current) => ChangedShare(previous, current) >= MotionShare;
}
