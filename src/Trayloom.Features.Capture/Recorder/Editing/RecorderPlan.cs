// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;

namespace Trayloom.Features.Capture;

/// <summary>
/// Everything a frame composer needs, solved once end to end for a document and a pointer track: canvas and card geometry, the whole
/// pointer / zoom timeline (smoothing sees the future, a zoom can begin before the click that causes it), overlay opacities and blur
/// coverage. A frame is then an index lookup. The same plan feeds the editor preview and the export.
/// </summary>
internal sealed class RecorderPlan
{
    public Size Source { get; init; }

    public Size Canvas { get; init; }

    public Rect Card { get; init; }

    public int FrameRate { get; init; }

    public int Frames { get; init; }

    public bool ShowsPointer { get; init; }

    public bool ShowsClickRing { get; init; }

    public Point[] Positions { get; init; } = Array.Empty<Point>();

    public double[] Zoom { get; init; } = Array.Empty<double>();

    public Point[] Travel { get; init; } = Array.Empty<Point>();

    public double[] PressScale { get; init; } = Array.Empty<double>();

    public double[] Ring { get; init; } = Array.Empty<double>();

    public IReadOnlyList<CursorShape> Shapes { get; init; } = Array.Empty<CursorShape>();

    public int[] ShapeIndex { get; init; } = Array.Empty<int>();

    public bool[] Visible { get; init; } = Array.Empty<bool>();

    public double[] PointerOpacity { get; init; } = Array.Empty<double>();

    public double PointerScale { get; init; } = 1;

    public double PointerPixelSize { get; init; } = 28;

    public BackdropStyle Style { get; init; } = new();

    public double Corner { get; init; }

    public bool NeedsCanvas { get; init; }

    public List<TextOverlay> Texts { get; init; } = new();

    public double[][] TextOpacity { get; init; } = Array.Empty<double[]>();

    public List<ImageOverlay> Images { get; init; } = new();

    public double[][] ImageOpacity { get; init; } = Array.Empty<double[]>();

    public List<BlurRegion> Blurs { get; init; } = new();

    public bool[][] BlurCovers { get; init; } = Array.Empty<bool[]>();

    /// <summary>Nothing is drawn on the picture: the plain scaling path is valid.</summary>
    public bool IsIdentity { get; init; }

    public int FrameIndex(double editedSeconds) => Math.Min(Math.Max(0, (int)Math.Round(Math.Max(0, editedSeconds) * FrameRate, MidpointRounding.AwayFromZero)), Math.Max(0, Frames - 1));

    /// <summary>True when the blur covers the instant; retiming between two plan samples counts either neighbour so a frame is never uncovered early.</summary>
    public bool BlurCovered(int blurIndex, double editedSeconds)
    {
        if (blurIndex >= BlurCovers.Length)
        {
            return false;
        }

        var position = Math.Max(0, editedSeconds) * FrameRate;
        var last = Math.Max(0, Frames - 1);
        var lower = Math.Min(last, (int)Math.Floor(position));
        var upper = Math.Min(last, (int)Math.Ceiling(position));
        for (var i = lower; i <= upper; i++)
        {
            if (i < BlurCovers[blurIndex].Length && BlurCovers[blurIndex][i])
            {
                return true;
            }
        }

        return false;
    }

    public static RecorderPlan? Build(EditDocument doc, PointerTrack track, IReadOnlyList<double> typing, Size source, int frameRate, double duration, double outputScale = 1)
    {
        if (source.Width <= 0 || source.Height <= 0 || duration <= 0)
        {
            return null;
        }

        frameRate = Math.Max(1, frameRate);
        var style = doc.ResolvedBackdrop;
        var ratio = EditDocument.AspectRatio(doc.ResolvedAspect);
        var hasBackdrop = !style.IsNone;
        var padding = hasBackdrop ? style.Padding * 0.18 : 0;
        var evenSource = new Size(RecorderSupport.EvenSize(source.Width), RecorderSupport.EvenSize(source.Height));
        var fullCanvas = RecorderSupport.CanvasSize(source, padding, ratio, !hasBackdrop);
        var scale = double.IsFinite(outputScale) ? Math.Clamp(outputScale, 0.1, 1) : 1;
        var canvas = scale == 1 ? fullCanvas : new Size(RecorderSupport.EvenSize(fullCanvas.Width * scale), RecorderSupport.EvenSize(fullCanvas.Height * scale));
        var card = RecorderSupport.CardRect(canvas, source, padding, !hasBackdrop && ratio is not null);
        var showsPointer = doc.ShowsPointer && !track.IsEmpty;
        var segments = doc.ActiveZoomSegments(duration);
        var needsCanvas = hasBackdrop || fullCanvas != evenSource;
        var texts = TextOverlay.Normalized(doc.Texts, duration);
        var images = ImageOverlay.Normalized(doc.Images, duration);
        var blurs = doc.Blurs.Where(b => b.End - b.Start >= 0.2).OrderBy(b => b.Start).ToList();
        var identity = !showsPointer && segments.Count == 0 && !needsCanvas && doc.Cuts.Count == 0 && texts.Count == 0 && images.Count == 0 && blurs.Count == 0;

        var (trimStart, trimEnd) = RecorderSupport.SanitizedTrim(doc.TrimStart, doc.TrimEnd, duration);
        var outputDuration = doc.OutputDuration(duration);
        var frames = Math.Max(1, (int)Math.Round(outputDuration * frameRate, MidpointRounding.AwayFromZero));
        var step = 1.0 / frameRate;

        var sourceRaw = RecorderMotion.Resampled(track.Samples, frameRate, duration);
        var sourcePositions = sourceRaw;
        if (showsPointer)
        {
            var smoothed = RecorderMotion.SmoothedPath(sourceRaw, doc.ResolvedSmoothing, frameRate);
            if (ReferenceEquals(smoothed, sourceRaw))
            {
                smoothed = (Point[])sourceRaw.Clone();
            }

            smoothed = RecorderMotion.Anchored(smoothed, sourceRaw, track.Clicks, frameRate);
            if (ReferenceEquals(smoothed, sourceRaw))
            {
                smoothed = (Point[])sourceRaw.Clone();
            }

            for (var i = 0; i < smoothed.Length; i++)
            {
                if (RecorderMotion.IsShaking(sourceRaw, i, frameRate))
                {
                    smoothed[i] = sourceRaw[i];
                }
            }

            sourcePositions = smoothed;
        }

        var sourceShapes = RecorderMotion.ResampledShapes(track.Samples, frameRate, duration);
        var sourceVisible = RecorderMotion.ResampledVisibility(track.Samples, frameRate, duration);
        var clusters = RecorderMotion.FocusClusters(sourcePositions, frameRate, ZoomTimeline.SanitizedAmount(doc.ZoomAmount));

        var sourceTimes = new double[frames];
        for (var i = 0; i < frames; i++)
        {
            sourceTimes[i] = doc.SourceTime(i * step, duration);
        }

        int SourceIndex(double t) => Math.Min(Math.Max(0, (int)Math.Round(t * frameRate, MidpointRounding.AwayFromZero)), Math.Max(0, sourcePositions.Length - 1));

        var positions = new Point[frames];
        var shapeIndex = new int[frames];
        var visible = new bool[frames];
        for (var i = 0; i < frames; i++)
        {
            var s = SourceIndex(sourceTimes[i]);
            positions[i] = s < sourcePositions.Length ? sourcePositions[s] : default;
            shapeIndex[i] = s < sourceShapes.Length ? sourceShapes[s] : 0;
            visible[i] = s >= sourceVisible.Length || sourceVisible[s];
        }

        var zoom = new double[frames];
        Array.Fill(zoom, 1.0);
        var travel = new Point[frames];
        Array.Fill(travel, new Point(0.5, 0.5));
        if (segments.Count > 0)
        {
            var amountTargets = new double[frames];
            var travelX = new double[frames];
            var travelY = new double[frames];
            var focusCursor = 0;
            for (var i = 0; i < frames; i++)
            {
                var time = sourceTimes[i];
                var (progress, amount, focus) = ZoomTimeline.State(time, segments);
                amountTargets[i] = 1 + (amount - 1) * progress;
                if (focus is { } aimed)
                {
                    travelX[i] = RecorderMotion.TravelParameterExact(aimed.X, amount);
                    travelY[i] = RecorderMotion.TravelParameterExact(aimed.Y, amount);
                }
                else
                {
                    var f = RecorderMotion.Focus(time, clusters, ref focusCursor);
                    travelX[i] = RecorderMotion.TravelParameter(f.X);
                    travelY[i] = RecorderMotion.TravelParameter(f.Y);
                }
            }

            var sa = RecorderMotion.Springed(amountTargets, frameRate);
            var sx = RecorderMotion.Springed(travelX, frameRate);
            var sy = RecorderMotion.Springed(travelY, frameRate);
            for (var i = 0; i < frames; i++)
            {
                zoom[i] = Math.Max(1, sa[i]);
                travel[i] = new Point(sx[i], sy[i]);
            }
        }

        var pointerOpacity = new double[frames];
        Array.Fill(pointerOpacity, 1.0);
        var pressScale = new double[frames];
        Array.Fill(pressScale, 1.0);
        var ring = new double[frames];
        Array.Fill(ring, -1.0);
        if (showsPointer)
        {
            var sourceOpacity = RecorderMotion.PointerOpacity(sourcePositions, frameRate);
            var punchCursor = 0;
            var ringCursor = 0;
            for (var i = 0; i < frames; i++)
            {
                var s = SourceIndex(sourceTimes[i]);
                pointerOpacity[i] = s < sourceOpacity.Length ? sourceOpacity[s] : 1;
                pressScale[i] = RecorderMotion.PressScale(sourceTimes[i], track.Clicks, ref punchCursor);
                if (doc.ShowsClickRing && RecorderMotion.RingProgress(sourceTimes[i], track.Clicks, ref ringCursor) is { } p)
                {
                    ring[i] = p;
                }
            }
        }

        var textOpacity = texts.Select(t => Enumerable.Range(0, frames).Select(i => t.OpacityAt(sourceTimes[i])).ToArray()).ToArray();
        var imageOpacity = images.Select(t => Enumerable.Range(0, frames).Select(i => t.OpacityAt(sourceTimes[i])).ToArray()).ToArray();
        var blurCovers = blurs.Select(b => Enumerable.Range(0, frames).Select(i => sourceTimes[i] >= b.Start && sourceTimes[i] <= b.End).ToArray()).ToArray();

        var pixelSize = (track.Shapes.Count > 0 ? track.Shapes[0].Width : 28) * track.DisplayScale * Math.Max(1, track.SystemScale) * Math.Clamp(doc.PointerSize, 0.5, 2.0);
        _ = trimStart;
        _ = trimEnd;
        return new RecorderPlan
        {
            Source = evenSource,
            Canvas = canvas,
            Card = card,
            FrameRate = frameRate,
            Frames = frames,
            ShowsPointer = showsPointer,
            ShowsClickRing = doc.ShowsClickRing,
            Positions = positions,
            Zoom = zoom,
            Travel = travel,
            PressScale = pressScale,
            Ring = ring,
            Shapes = track.Shapes,
            ShapeIndex = shapeIndex,
            Visible = visible,
            PointerOpacity = pointerOpacity,
            PointerScale = track.DisplayScale * Math.Max(1, track.SystemScale) * Math.Clamp(doc.PointerSize, 0.5, 2.0),
            PointerPixelSize = pixelSize,
            Style = style,
            Corner = hasBackdrop ? RecorderSupport.CardCorner(style.CornerRadius, card.Size) : 0,
            NeedsCanvas = needsCanvas,
            Texts = texts,
            TextOpacity = textOpacity,
            Images = images,
            ImageOpacity = imageOpacity,
            Blurs = blurs,
            BlurCovers = blurCovers,
            IsIdentity = identity,
        };
    }
}
