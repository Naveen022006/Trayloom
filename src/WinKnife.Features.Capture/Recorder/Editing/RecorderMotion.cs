// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;

namespace WinKnife.Features.Capture;

/// <summary>One pointer sample: time in seconds from the recording start, position in the recorded area (0..1, top-left origin).</summary>
internal readonly record struct PointerSample(double Time, double X, double Y, int ShapeIndex, bool IsVisible);

/// <summary>A mouse button press or release in the recording's clock.</summary>
internal readonly record struct PointerClick(double Time, bool IsDown);

internal enum PointerSmoothing { Off, Light, Smooth, Cinematic }

/// <summary>
/// The maths behind the pointer that glides instead of jittering and the picture that leans in on a click. Pure and offline:
/// the recording is finished, so the smoothing can look at the future as well as the past.
/// </summary>
internal static class RecorderMotion
{
    // ---- uniform grid --------------------------------------------------------------------------------------------------

    private static int FrameCount(double duration, int frameRate) => Math.Max(1, (int)Math.Round(duration * Math.Max(1, frameRate), MidpointRounding.AwayFromZero));

    public static Point[] Resampled(IReadOnlyList<PointerSample> samples, int frameRate, double duration)
    {
        var count = FrameCount(duration, frameRate);
        var result = new Point[count];
        if (samples.Count == 0)
        {
            return result;
        }

        if (samples.Count == 1)
        {
            Array.Fill(result, new Point(samples[0].X, samples[0].Y));
            return result;
        }

        var cursor = 0;
        var step = 1.0 / Math.Max(1, frameRate);
        for (var i = 0; i < count; i++)
        {
            var time = i * step;
            while (cursor + 1 < samples.Count && samples[cursor + 1].Time <= time)
            {
                cursor++;
            }

            if (cursor + 1 >= samples.Count)
            {
                result[i] = new Point(samples[^1].X, samples[^1].Y);
                continue;
            }

            var a = samples[cursor];
            var b = samples[cursor + 1];
            var span = b.Time - a.Time;
            var t = span > 0 ? Math.Clamp((time - a.Time) / span, 0, 1) : 0;
            result[i] = new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        return result;
    }

    private static T[] Nearest<T>(IReadOnlyList<PointerSample> samples, int frameRate, double duration, Func<PointerSample, T> pick, T fallback)
    {
        var count = FrameCount(duration, frameRate);
        var result = new T[count];
        if (samples.Count == 0)
        {
            Array.Fill(result, fallback);
            return result;
        }

        var cursor = 0;
        var step = 1.0 / Math.Max(1, frameRate);
        for (var i = 0; i < count; i++)
        {
            var time = i * step;
            while (cursor + 1 < samples.Count && samples[cursor + 1].Time <= time)
            {
                cursor++;
            }

            result[i] = pick(samples[cursor]);
        }

        return result;
    }

    public static bool[] ResampledVisibility(IReadOnlyList<PointerSample> s, int fps, double duration) => Nearest(s, fps, duration, x => x.IsVisible, true);

    public static int[] ResampledShapes(IReadOnlyList<PointerSample> s, int fps, double duration) => Nearest(s, fps, duration, x => x.ShapeIndex, 0);

    // ---- zero-phase smoothing -----------------------------------------------------------------------------------------

    public static double Cutoff(PointerSmoothing s) => s switch { PointerSmoothing.Light => 4.0, PointerSmoothing.Smooth => 2.0, PointerSmoothing.Cinematic => 1.2, _ => 0 };

    public static PointerSmoothing SanitizedSmoothing(string? raw) => raw?.ToLowerInvariant() switch
    {
        "off" => PointerSmoothing.Off,
        "light" => PointerSmoothing.Light,
        "cinematic" => PointerSmoothing.Cinematic,
        _ => PointerSmoothing.Smooth,
    };

    public static string SmoothingName(PointerSmoothing s) => s.ToString().ToLowerInvariant();

    public const double FiltfiltWidening = 1.553773974;

    public static double OnePoleAlpha(double cutoff, double dt) => cutoff > 0 && dt > 0 ? 1 - Math.Exp(-2 * Math.PI * cutoff * dt) : 1;

    public static double[] Filtfilt(double[] values, double alpha)
    {
        if (values.Length <= 1 || alpha <= 0 || alpha >= 1)
        {
            return values;
        }

        var padding = Math.Min(values.Length, Math.Max(1, (int)Math.Round(3 / alpha, MidpointRounding.AwayFromZero)));
        var padded = new double[values.Length + padding * 2];
        for (var i = 0; i < padding; i++)
        {
            padded[i] = values[0];
            padded[padded.Length - 1 - i] = values[^1];
        }

        Array.Copy(values, 0, padded, padding, values.Length);
        var carry = padded[0];
        for (var i = 0; i < padded.Length; i++)
        {
            carry += alpha * (padded[i] - carry);
            padded[i] = carry;
        }

        carry = padded[^1];
        for (var i = padded.Length - 1; i >= 0; i--)
        {
            carry += alpha * (padded[i] - carry);
            padded[i] = carry;
        }

        var result = new double[values.Length];
        Array.Copy(padded, padding, result, 0, values.Length);
        return result;
    }

    public static Point[] SmoothedPath(Point[] points, PointerSmoothing smoothing, int frameRate)
    {
        if (smoothing == PointerSmoothing.Off || points.Length <= 1)
        {
            return points;
        }

        var dt = 1.0 / Math.Max(1, frameRate);
        var alpha = OnePoleAlpha(Cutoff(smoothing) * FiltfiltWidening, dt);
        var xs = Filtfilt(points.Select(p => p.X).ToArray(), alpha);
        var ys = Filtfilt(points.Select(p => p.Y).ToArray(), alpha);
        var result = new Point[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            result[i] = new Point(xs[i], ys[i]);
        }

        return result;
    }

    // ---- click anchoring -----------------------------------------------------------------------------------------------

    public const double ClickAnchorWindow = 0.12;

    public static double AnchorWeight(double time, IReadOnlyList<PointerClick> clicks, ref int cursor)
    {
        double weight = 0;
        double? pressedAt = null;
        var settled = cursor;
        var index = cursor;
        while (index < clicks.Count)
        {
            var click = clicks[index];
            if (pressedAt is null && click.Time > time + ClickAnchorWindow)
            {
                break;
            }

            index++;
            if (click.IsDown)
            {
                pressedAt = click.Time;
            }
            else
            {
                if (pressedAt is { } down)
                {
                    if (time >= down && time <= click.Time)
                    {
                        weight = 1;
                    }

                    pressedAt = null;
                }

                if (click.Time + ClickAnchorWindow < time)
                {
                    settled = index;
                }
            }

            var distance = Math.Abs(time - click.Time);
            if (distance > ClickAnchorWindow)
            {
                continue;
            }

            weight = Math.Max(weight, Smoothstep(1 - distance / ClickAnchorWindow));
        }

        cursor = settled;
        if (clicks.Count > 0 && clicks[^1].IsDown && time >= clicks[^1].Time)
        {
            return 1;
        }

        return weight;
    }

    public static Point[] Anchored(Point[] smoothed, Point[] raw, IReadOnlyList<PointerClick> clicks, int frameRate)
    {
        if (clicks.Count == 0 || smoothed.Length != raw.Length)
        {
            return smoothed;
        }

        var step = 1.0 / Math.Max(1, frameRate);
        var cursor = 0;
        var result = new Point[smoothed.Length];
        for (var i = 0; i < smoothed.Length; i++)
        {
            var w = AnchorWeight(i * step, clicks, ref cursor);
            result[i] = w > 0 ? new Point(smoothed[i].X + (raw[i].X - smoothed[i].X) * w, smoothed[i].Y + (raw[i].Y - smoothed[i].Y) * w) : smoothed[i];
        }

        return result;
    }

    // ---- shaking -------------------------------------------------------------------------------------------------------

    public static bool IsShaking(Point[] points, int index, int frameRate)
    {
        var span = Math.Max(2, (int)Math.Round(0.1 * Math.Max(1, frameRate), MidpointRounding.AwayFromZero));
        var lower = Math.Max(0, index - span / 2);
        var upper = Math.Min(points.Length - 1, index + span / 2);
        if (upper - lower < 3)
        {
            return false;
        }

        double travel = 0;
        var reversals = 0;
        var previous = 0;
        for (var i = lower + 1; i <= upper; i++)
        {
            var dx = points[i].X - points[i - 1].X;
            var dy = points[i].Y - points[i - 1].Y;
            travel += Math.Sqrt(dx * dx + dy * dy);
            var sign = dx > 0 ? 1 : dx < 0 ? -1 : 0;
            if (sign != 0 && previous != 0 && sign != previous)
            {
                reversals++;
            }

            if (sign != 0)
            {
                previous = sign;
            }
        }

        return travel > 0.015 * 4 && reversals >= 3;
    }

    // ---- easing --------------------------------------------------------------------------------------------------------

    public static double Smoothstep(double t)
    {
        var x = Math.Clamp(t, 0, 1);
        return x * x * (3 - 2 * x);
    }

    public static double Smootherstep(double t)
    {
        var x = Math.Clamp(t, 0, 1);
        return x * x * x * (10 - 15 * x + 6 * x * x);
    }

    public static double OverlayOpacity(double time, double start, double end, double fade)
    {
        var duration = end - start;
        if (duration <= 0 || time < start || time > end)
        {
            return 0;
        }

        var ramp = Math.Min(fade, duration / 2);
        if (ramp <= 0)
        {
            return 1;
        }

        if (time < start + ramp)
        {
            return Smoothstep((time - start) / ramp);
        }

        if (time > end - ramp)
        {
            return Smoothstep((end - time) / ramp);
        }

        return 1;
    }

    // ---- zoom ----------------------------------------------------------------------------------------------------------

    public const double ZoomLeadIn = 0.3;
    public const double ZoomHold = 2.5;
    public const double ZoomMinimumDuration = 0.9;
    public const double ZoomIgnoreTail = 1.0;
    public const double ZoomEndMargin = 0.8;
    public const double TypingZoomTail = 1.4;
    public const double TypingZoomContinuation = 8.0;
    public const double ZoomRampIn = 0.42;
    public const double ZoomRampOut = 0.65;

    /// <summary>Zoom spans (start, end) derived from the click-downs (and typing after them).</summary>
    public static List<(double Start, double End)> ZoomSpans(IReadOnlyList<PointerClick> clicks, IReadOnlyList<double> typingTimes, double duration)
    {
        var result = new List<(double Start, double End)>();
        if (duration <= 0)
        {
            return result;
        }

        var presses = clicks.Where(c => c.IsDown && c.Time < duration - ZoomIgnoreTail).Select(c => c.Time).OrderBy(t => t).ToList();
        if (presses.Count == 0)
        {
            return result;
        }

        var merged = new List<(double Start, double End)>();
        foreach (var press in presses)
        {
            var seg = (Start: Math.Max(0.001, press - ZoomLeadIn), End: press + ZoomHold);
            if (merged.Count > 0 && seg.Start - merged[^1].End <= ZoomHold)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, seg.End));
            }
            else
            {
                merged.Add(seg);
            }
        }

        foreach (var time in typingTimes.Where(double.IsFinite).OrderBy(t => t))
        {
            var index = merged.FindLastIndex(m => time >= m.Start && time <= m.End + TypingZoomContinuation);
            if (index < 0)
            {
                continue;
            }

            merged[index] = (merged[index].Start, Math.Max(merged[index].End, time + TypingZoomTail));
        }

        var limit = Math.Max(0, duration - ZoomEndMargin);
        foreach (var seg in merged)
        {
            var end = Math.Min(seg.End, limit);
            if (end - seg.Start >= ZoomMinimumDuration)
            {
                result.Add((seg.Start, end));
            }
        }

        return result;
    }

    // ---- viewport ------------------------------------------------------------------------------------------------------

    public const double EdgeBand = 0.25;

    public static double TravelParameter(double focus) => Math.Clamp((focus - EdgeBand) / (1 - 2 * EdgeBand), 0, 1);

    public static double TravelParameterExact(double focus, double zoom)
    {
        var visible = 1 / Math.Max(1, zoom);
        var span = 1 - visible;
        return span <= 0.0001 ? 0.5 : Math.Clamp((focus - visible / 2) / span, 0, 1);
    }

    public static Point ViewportOrigin(Point travel, double zoom)
    {
        var span = Math.Max(0, 1 - 1 / Math.Max(1, zoom));
        return new Point(Math.Clamp(travel.X, 0, 1) * span, Math.Clamp(travel.Y, 0, 1) * span);
    }

    // ---- focus clusters ------------------------------------------------------------------------------------------------

    public readonly record struct FocusCluster(double Time, Point Center);

    public static List<FocusCluster> FocusClusters(Point[] points, int frameRate, double zoom)
    {
        var result = new List<FocusCluster>();
        if (points.Length == 0)
        {
            return result;
        }

        var step = 1.0 / Math.Max(1, frameRate);
        var maxW = 0.5 / Math.Max(1, zoom);
        var maxH = 0.7 / Math.Max(1, zoom);
        double minX = points[0].X, maxX = minX, minY = points[0].Y, maxY = minY;
        double startedAt = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            var nx0 = Math.Min(minX, p.X);
            var nx1 = Math.Max(maxX, p.X);
            var ny0 = Math.Min(minY, p.Y);
            var ny1 = Math.Max(maxY, p.Y);
            if (nx1 - nx0 <= maxW && ny1 - ny0 <= maxH)
            {
                minX = nx0;
                maxX = nx1;
                minY = ny0;
                maxY = ny1;
                continue;
            }

            result.Add(new FocusCluster(startedAt, new Point((minX + maxX) / 2, (minY + maxY) / 2)));
            minX = maxX = p.X;
            minY = maxY = p.Y;
            startedAt = i * step;
        }

        result.Add(new FocusCluster(startedAt, new Point((minX + maxX) / 2, (minY + maxY) / 2)));
        return result;
    }

    public static Point Focus(double time, List<FocusCluster> clusters, ref int cursor)
    {
        if (clusters.Count == 0)
        {
            return new Point(0.5, 0.5);
        }

        var current = cursor == 0 ? clusters[0].Center : clusters[cursor - 1].Center;
        while (cursor < clusters.Count && clusters[cursor].Time <= time)
        {
            current = clusters[cursor].Center;
            cursor++;
        }

        return current;
    }

    // ---- spring --------------------------------------------------------------------------------------------------------

    public static double[] Springed(double[] targets, int frameRate)
    {
        if (targets.Length <= 1)
        {
            return targets;
        }

        const double stiffness = 200, mass = 2.25, damping = 40;
        var frameStep = 1.0 / Math.Max(1, frameRate);
        const double sub = 1.0 / 240.0;
        var value = targets[0];
        double velocity = 0;
        var result = new double[targets.Length];
        for (var i = 0; i < targets.Length; i++)
        {
            double elapsed = 0;
            while (elapsed < frameStep)
            {
                var dt = Math.Min(sub, frameStep - elapsed);
                var acc = (stiffness * (targets[i] - value) - damping * velocity) / mass;
                velocity += acc * dt;
                value += velocity * dt;
                elapsed += sub;
            }

            result[i] = value;
        }

        return result;
    }

    // ---- standing still ------------------------------------------------------------------------------------------------

    public const double IdleDelay = 2.5;
    public const double IdleFadeOut = 0.4;
    public const double IdleFadeIn = 0.25;
    public const double IdleTravel = 0.0006;

    public static double[] PointerOpacity(Point[] points, int frameRate)
    {
        if (points.Length <= 1)
        {
            var one = new double[Math.Max(1, points.Length)];
            Array.Fill(one, 1.0);
            return one;
        }

        var step = 1.0 / Math.Max(1, frameRate);
        var stillFor = new double[points.Length];
        for (var i = 1; i < points.Length; i++)
        {
            var dx = points[i].X - points[i - 1].X;
            var dy = points[i].Y - points[i - 1].Y;
            var moved = Math.Sqrt(dx * dx + dy * dy) > IdleTravel;
            stillFor[i] = moved ? 0 : stillFor[i - 1] + step;
        }

        var untilMoves = new double[points.Length];
        untilMoves[^1] = double.MaxValue;
        for (var i = points.Length - 2; i >= 0; i--)
        {
            untilMoves[i] = stillFor[i + 1] == 0 ? 0 : untilMoves[i + 1] + step;
        }

        var result = new double[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var waking = untilMoves[i] <= IdleFadeIn ? 1 - untilMoves[i] / IdleFadeIn : 0;
            var sleeping = stillFor[i] <= IdleDelay ? 0 : Math.Min(1, (stillFor[i] - IdleDelay) / IdleFadeOut);
            var hidden = Math.Max(0, sleeping - waking);
            result[i] = 1 - Smoothstep(hidden);
        }

        return result;
    }

    // ---- click feedback ------------------------------------------------------------------------------------------------

    public const double PressPunchWindow = 0.13;
    public const double PressPunchScale = 0.8;
    public const double RingDuration = 0.4;
    public const double RingRadius = 22;

    public static double PressScale(double time, IReadOnlyList<PointerClick> clicks, ref int cursor)
    {
        double weight = 0;
        double? pressedAt = null;
        var settled = cursor;
        var index = cursor;
        while (index < clicks.Count)
        {
            var click = clicks[index];
            if (pressedAt is null && click.Time > time + PressPunchWindow)
            {
                break;
            }

            index++;
            if (click.IsDown)
            {
                pressedAt = click.Time;
                if (time >= click.Time - PressPunchWindow && time <= click.Time)
                {
                    weight = Math.Max(weight, Smoothstep((time - (click.Time - PressPunchWindow)) / PressPunchWindow));
                }
            }
            else
            {
                if (pressedAt is { } down)
                {
                    if (time >= down && time <= click.Time)
                    {
                        weight = 1;
                    }

                    if (time > click.Time && time <= click.Time + PressPunchWindow)
                    {
                        weight = Math.Max(weight, Smoothstep(1 - (time - click.Time) / PressPunchWindow));
                    }

                    pressedAt = null;
                }

                if (click.Time + PressPunchWindow < time)
                {
                    settled = index;
                }
            }
        }

        cursor = settled;
        if (clicks.Count > 0 && clicks[^1].IsDown && time >= clicks[^1].Time)
        {
            weight = 1;
        }

        return 1 - (1 - PressPunchScale) * Math.Min(1, weight);
    }

    public static double? RingProgress(double time, IReadOnlyList<PointerClick> clicks, ref int cursor)
    {
        double? best = null;
        var index = cursor;
        while (index < clicks.Count)
        {
            var click = clicks[index];
            var elapsed = time - click.Time;
            if (elapsed < 0)
            {
                break;
            }

            index++;
            if (elapsed > RingDuration)
            {
                cursor = index;
                continue;
            }

            if (!click.IsDown)
            {
                continue;
            }

            var progress = elapsed / RingDuration;
            best = Math.Min(best ?? progress, progress);
        }

        return best;
    }
}
