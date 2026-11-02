// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json.Serialization;
using System.Windows;

namespace WinKnife.Features.Capture;

/// <summary>One lean-in. Generated from the clicks to begin with, then owned by whoever touches it.</summary>
internal sealed class ZoomSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public double Start { get; set; }

    public double End { get; set; }

    public double Amount { get; set; } = 1.8;

    /// <summary>Where it looks, 0..1 in the recorded area; null = follow the pointer.</summary>
    public double? FocusX { get; set; }

    public double? FocusY { get; set; }

    [JsonIgnore]
    public double Duration => Math.Max(0, End - Start);

    [JsonIgnore]
    public bool FollowsPointer => FocusX is null || FocusY is null;

    [JsonIgnore]
    public Point? Focus => FocusX is { } x && FocusY is { } y ? new Point(x, y) : null;

    public ZoomSegment Copy() => (ZoomSegment)MemberwiseClone();
}

/// <summary>The nine places a caption or picture can take.</summary>
internal static class Anchors
{
    public static readonly string[] All = ["topLeading", "top", "topTrailing", "leading", "center", "trailing", "bottomLeading", "bottom", "bottomTrailing"];

    public static Point Unit(string anchor) => anchor switch
    {
        "topLeading" => new Point(0, 0),
        "top" => new Point(0.5, 0),
        "topTrailing" => new Point(1, 0),
        "leading" => new Point(0, 0.5),
        "center" => new Point(0.5, 0.5),
        "trailing" => new Point(1, 0.5),
        "bottomLeading" => new Point(0, 1),
        "bottom" => new Point(0.5, 1),
        _ => new Point(1, 1),
    };

    /// <summary>Top-left origin (WPF space) of content of the given size on a canvas, with a margin off every edge it touches.</summary>
    public static Point Origin(string anchor, Size content, Size canvas)
    {
        var margin = Math.Min(canvas.Width, canvas.Height) * 0.05;
        var u = Unit(anchor);
        return new Point(margin + (canvas.Width - content.Width - margin * 2) * u.X, margin + (canvas.Height - content.Height - margin * 2) * u.Y);
    }
}

internal static class TextPalette
{
    public static readonly string[] All = ["white", "black", "accent", "yellow", "red", "green"];

    public static (double R, double G, double B) Components(string palette) => palette switch
    {
        "black" => (0.06, 0.06, 0.07),
        "accent" => (0.04, 0.52, 1.0),
        "yellow" => (1.0, 0.80, 0.0),
        "red" => (0.96, 0.26, 0.21),
        "green" => (0.20, 0.78, 0.35),
        _ => (1, 1, 1),
    };
}

/// <summary>A line of text over the recording for a while.</summary>
internal sealed class TextOverlay
{
    public const double Fade = 0.25;
    public const double DefaultLength = 3.0;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;

    public double Start { get; set; }

    public double End { get; set; }

    public string Anchor { get; set; } = "bottom";

    /// <summary>Height of the type as a share of the picture height (0.03..0.16).</summary>
    public double Size { get; set; } = 0.06;

    public string Palette { get; set; } = "white";

    [JsonIgnore]
    public double Duration => Math.Max(0, End - Start);

    public double OpacityAt(double time) => RecorderMotion.OverlayOpacity(time, Start, End, Fade);

    public TextOverlay Copy() => (TextOverlay)MemberwiseClone();

    public TextOverlay? Sanitized(double recording)
    {
        if (recording <= 0 || !double.IsFinite(Start) || !double.IsFinite(End))
        {
            return null;
        }

        var c = Copy();
        c.Text = Text.Length > 200 ? Text[..200] : Text;
        c.Start = Math.Clamp(Start, 0, recording);
        c.End = Math.Clamp(End, 0, recording);
        c.Size = double.IsFinite(Size) ? Math.Clamp(Size, 0.03, 0.16) : 0.06;
        if (!Anchors.All.Contains(c.Anchor))
        {
            c.Anchor = "bottom";
        }

        if (!TextPalette.All.Contains(c.Palette))
        {
            c.Palette = "white";
        }

        return c.Duration >= 0.2 ? c : null;
    }

    public static List<TextOverlay> Normalized(IEnumerable<TextOverlay> list, double recording) =>
        list.Select(t => t.Sanitized(recording)).Where(t => t is not null).Select(t => t!).OrderBy(t => t.Start).ToList();
}

/// <summary>A picture laid over the recording for a while (a private copy sits in the take folder).</summary>
internal sealed class ImageOverlay
{
    public const double Fade = 0.25;
    public const double DefaultSize = 0.18;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Path { get; set; } = string.Empty;

    public double Start { get; set; }

    public double End { get; set; }

    public string Anchor { get; set; } = "bottomTrailing";

    /// <summary>How wide the picture is drawn as a share of the canvas width (0.04..0.6).</summary>
    public double Size { get; set; } = DefaultSize;

    public double Opacity { get; set; } = 1;

    [JsonIgnore]
    public double Duration => Math.Max(0, End - Start);

    public double OpacityAt(double time) => Opacity * RecorderMotion.OverlayOpacity(time, Start, End, Fade);

    public ImageOverlay Copy() => (ImageOverlay)MemberwiseClone();

    public ImageOverlay? Sanitized(double recording)
    {
        if (recording <= 0 || !double.IsFinite(Start) || !double.IsFinite(End) || string.IsNullOrEmpty(Path) || !System.IO.Path.IsPathRooted(Path))
        {
            return null;
        }

        var c = Copy();
        c.Start = Math.Clamp(Start, 0, recording);
        c.End = Math.Clamp(End, 0, recording);
        c.Size = double.IsFinite(Size) ? Math.Clamp(Size, 0.04, 0.6) : DefaultSize;
        c.Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.05, 1) : 1;
        if (!Anchors.All.Contains(c.Anchor))
        {
            c.Anchor = "bottomTrailing";
        }

        return c.Duration >= 0.2 ? c : null;
    }

    public static List<ImageOverlay> Normalized(IEnumerable<ImageOverlay> list, double recording) =>
        list.Select(t => t.Sanitized(recording)).Where(t => t is not null).Select(t => t!).OrderBy(t => t.Start).ToList();

    /// <summary>Drawn size from the picture's own proportions, never larger than the canvas minus its margins.</summary>
    public static Size? DrawnSize(Size source, double size, Size canvas)
    {
        if (source.Width <= 0 || source.Height <= 0 || canvas.Width <= 0 || canvas.Height <= 0 || size <= 0)
        {
            return null;
        }

        var margin = Math.Min(canvas.Width, canvas.Height) * 0.05;
        var availW = canvas.Width - 2 * margin;
        var availH = canvas.Height - 2 * margin;
        var w = canvas.Width * size;
        var h = w * source.Height / source.Width;
        var scale = Math.Min(1, Math.Min(availW / w, availH / h));
        w *= scale;
        h *= scale;
        var dw = Math.Round(w);
        var dh = Math.Round(h);
        return dw >= 1 && dh >= 1 ? new Size(dw, dh) : null;
    }
}

/// <summary>Zoom list maths: normalising, slots for new zooms, and move / resize that never overlap neighbours.</summary>
internal static class ZoomTimeline
{
    public const double MinimumSegment = 0.4;
    public const double AddedLength = 2.0;

    public static double SanitizedAmount(double raw) => double.IsFinite(raw) ? Math.Clamp(raw, 1.2, 3.0) : 1.8;

    public static List<ZoomSegment> Normalized(IEnumerable<ZoomSegment> segments, double duration)
    {
        var result = new List<ZoomSegment>();
        if (duration <= 0)
        {
            return result;
        }

        var clamped = new List<ZoomSegment>();
        foreach (var s in segments)
        {
            if (!double.IsFinite(s.Start) || !double.IsFinite(s.End) || !double.IsFinite(s.Amount))
            {
                continue;
            }

            var c = s.Copy();
            c.Start = Math.Clamp(s.Start, 0, duration);
            c.End = Math.Clamp(s.End, 0, duration);
            c.Amount = SanitizedAmount(s.Amount);
            if (c.FocusX is { } x)
            {
                c.FocusX = Math.Clamp(x, 0, 1);
            }

            if (c.FocusY is { } y)
            {
                c.FocusY = Math.Clamp(y, 0, 1);
            }

            if (c.Duration >= MinimumSegment)
            {
                clamped.Add(c);
            }
        }

        foreach (var s in clamped.OrderBy(s => s.Start))
        {
            if (result.Count > 0 && s.Start < result[^1].End)
            {
                s.Start = result[^1].End;
                if (s.Duration < MinimumSegment)
                {
                    continue;
                }
            }

            result.Add(s);
        }

        return result;
    }

    public static List<ZoomSegment> Generated(IReadOnlyList<PointerClick> clicks, IReadOnlyList<double> typing, double duration, double amount) =>
        RecorderMotion.ZoomSpans(clicks, typing, duration).Select(s => new ZoomSegment { Start = s.Start, End = s.End, Amount = amount }).ToList();

    public static (double Start, double End)? SlotForNew(double time, IReadOnlyList<ZoomSegment> existing, double duration)
    {
        if (duration <= MinimumSegment)
        {
            return null;
        }

        var start = Math.Max(0, Math.Min(time, duration - MinimumSegment));
        var limit = duration;
        foreach (var s in existing.OrderBy(s => s.Start))
        {
            if (s.End <= start)
            {
                continue;
            }

            if (s.Start <= start)
            {
                start = s.End;
                continue;
            }

            limit = s.Start;
            break;
        }

        return limit - start >= MinimumSegment ? (start, Math.Min(limit, start + AddedLength)) : null;
    }

    private static (double Lower, double Upper) Room(ZoomSegment segment, IEnumerable<ZoomSegment> others, double duration)
    {
        double lower = 0;
        var upper = duration;
        foreach (var o in others)
        {
            if (o.Id == segment.Id)
            {
                continue;
            }

            if (o.End <= segment.Start)
            {
                lower = Math.Max(lower, o.End);
            }

            if (o.Start >= segment.End)
            {
                upper = Math.Min(upper, o.Start);
            }
        }

        return (lower, upper);
    }

    public static (double Start, double End) Moved(ZoomSegment segment, double start, IEnumerable<ZoomSegment> others, double duration)
    {
        var (lower, upper) = Room(segment, others, duration);
        var span = segment.Duration;
        var s = Math.Max(lower, Math.Min(start, upper - span));
        return (s, s + span);
    }

    public static (double Start, double End) Resized(ZoomSegment segment, bool startEdge, double time, IEnumerable<ZoomSegment> others, double duration)
    {
        var (lower, upper) = Room(segment, others, duration);
        return startEdge
            ? (Math.Max(lower, Math.Min(time, segment.End - MinimumSegment)), segment.End)
            : (segment.Start, Math.Min(upper, Math.Max(time, segment.Start + MinimumSegment)));
    }

    /// <summary>How far into a lean-in the picture is at a moment; the segment that owns the instant wins over one fading out.</summary>
    public static (double Progress, double Amount, Point? Focus) State(double time, IReadOnlyList<ZoomSegment> segments)
    {
        ZoomSegment? fading = null;
        foreach (var s in segments)
        {
            if (time >= s.Start - 0.001 && time <= s.End)
            {
                return StateOf(s, time);
            }

            if (time > s.End && time <= s.End + RecorderMotion.ZoomRampOut)
            {
                fading = s;
            }
        }

        return fading is null ? (0, 1, null) : StateOf(fading, time);
    }

    private static (double, double, Point?) StateOf(ZoomSegment s, double time)
    {
        var rampIn = Math.Min(RecorderMotion.ZoomRampIn, s.Duration / 2);
        double progress;
        if (time >= s.End)
        {
            progress = RecorderMotion.Smootherstep(1 - (time - s.End) / RecorderMotion.ZoomRampOut);
        }
        else if (rampIn > 0 && time <= s.Start + rampIn)
        {
            progress = RecorderMotion.Smootherstep((time - s.Start) / rampIn);
        }
        else
        {
            progress = 1;
        }

        return (progress, s.Amount, s.Focus);
    }
}
