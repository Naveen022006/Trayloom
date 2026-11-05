// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trayloom.Features.Capture;

internal sealed class CutRange
{
    public double Start { get; set; }

    public double End { get; set; }
}

internal sealed class BlurRegion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public double Start { get; set; }

    public double End { get; set; }

    public double X { get; set; } = 0.35;

    public double Y { get; set; } = 0.40;

    public double Width { get; set; } = 0.30;

    public double Height { get; set; } = 0.20;

    public int Strength { get; set; } = 3;
}

/// <summary>The editing state saved next to a take (edit.json). Times are on the recording's own clock.</summary>
internal sealed class EditDocument
{
    public double TrimStart { get; set; }

    public double TrimEnd { get; set; }

    public string Quality { get; set; } = "balanced";

    public double ExportSpeed { get; set; } = 1;

    public bool KeepsSystemAudio { get; set; } = true;

    public bool KeepsMicrophone { get; set; } = true;

    public double SystemAudioGain { get; set; } = 1;

    public double MicrophoneGain { get; set; } = 1;

    public string GifSize { get; set; } = "medium";

    public int GifFrameRate { get; set; } = 12;

    public List<CutRange> Cuts { get; set; } = new();

    public List<BlurRegion> Blurs { get; set; } = new();

    /// <summary>The background behind the recording as BackdropStyle JSON ("" = none).</summary>
    public string Backdrop { get; set; } = string.Empty;

    /// <summary>original | wide | square | vertical.</summary>
    public string Aspect { get; set; } = "original";

    public bool ShowsPointer { get; set; } = true;

    public string PointerSmoothing { get; set; } = "smooth";

    public double PointerSize { get; set; } = 1;

    public bool ShowsClickRing { get; set; } = true;

    public bool ZoomEnabled { get; set; } = true;

    public double ZoomAmount { get; set; } = 1.8;

    public bool ZoomsOnTyping { get; set; }

    public List<ZoomSegment> ZoomSegments { get; set; } = new();

    /// <summary>Set once the first zooms were generated so reopening never regenerates over hand edits.</summary>
    public bool ZoomsGenerated { get; set; }

    public List<TextOverlay> Texts { get; set; } = new();

    public List<ImageOverlay> Images { get; set; } = new();

    public EditDocument Clone() => JsonSerializer.Deserialize<EditDocument>(JsonSerializer.Serialize(this))!;

    [JsonIgnore]
    public BackdropStyle ResolvedBackdrop => BackdropStyle.Parse(Backdrop);

    [JsonIgnore]
    public string ResolvedAspect => Aspect is "wide" or "square" or "vertical" ? Aspect : "original";

    [JsonIgnore]
    public PointerSmoothing ResolvedSmoothing => RecorderMotion.SanitizedSmoothing(PointerSmoothing);

    public static double? AspectRatio(string aspect) => aspect switch { "wide" => 16.0 / 9.0, "square" => 1.0, "vertical" => 9.0 / 16.0, _ => null };

    /// <summary>The zooms as they will be applied: normalised, and empty when the whole effect is switched off.</summary>
    public List<ZoomSegment> ActiveZoomSegments(double duration) => ZoomEnabled ? ZoomTimeline.Normalized(ZoomSegments, duration) : new();

    /// <summary>Turning zoom back on with an empty list recovers the click-based zooms.</summary>
    public void RestoreAutomaticZooms(IReadOnlyList<PointerClick> clicks, IReadOnlyList<double> typing, double duration)
    {
        if (!ZoomEnabled || ZoomSegments.Count > 0)
        {
            return;
        }

        var generated = ZoomTimeline.Generated(clicks, ZoomsOnTyping ? typing : Array.Empty<double>(), duration, ZoomTimeline.SanitizedAmount(ZoomAmount));
        if (generated.Count > 0)
        {
            ZoomSegments = generated;
            ZoomsGenerated = true;
        }
    }

    /// <summary>One click on a look: a set of values, nothing stored about which look is active.</summary>
    public void ApplyLook(string look)
    {
        switch (look)
        {
            case "raw":
                Backdrop = string.Empty;
                Aspect = "original";
                ShowsPointer = true;
                PointerSmoothing = "off";
                ShowsClickRing = false;
                ZoomEnabled = false;
                break;
            case "clean":
                Backdrop = string.Empty;
                Aspect = "original";
                ShowsPointer = true;
                PointerSmoothing = "smooth";
                ShowsClickRing = true;
                ZoomEnabled = true;
                break;
            case "studio":
                Backdrop = new BackdropStyle { Kind = "preset", PresetId = "graphite", Padding = 0.45, CornerRadius = 0.35 }.ToJson();
                ShowsPointer = true;
                PointerSmoothing = "smooth";
                ShowsClickRing = true;
                ZoomEnabled = true;
                break;
        }
    }

    public bool MatchesLook(string look)
    {
        var e = Clone();
        e.ApplyLook(look);
        return Backdrop == e.Backdrop && Aspect == e.Aspect && ShowsPointer == e.ShowsPointer && PointerSmoothing == e.PointerSmoothing && ShowsClickRing == e.ShowsClickRing && ZoomEnabled == e.ZoomEnabled;
    }

    /// <summary>Whether two documents would draw a different picture (trim and audio do not).</summary>
    public bool AffectsPicture(EditDocument o) =>
        Backdrop != o.Backdrop || Aspect != o.Aspect || ShowsPointer != o.ShowsPointer || PointerSmoothing != o.PointerSmoothing || PointerSize != o.PointerSize
        || ShowsClickRing != o.ShowsClickRing || ZoomEnabled != o.ZoomEnabled || ZoomAmount != o.ZoomAmount
        || JsonSerializer.Serialize(ZoomSegments) != JsonSerializer.Serialize(o.ZoomSegments) || JsonSerializer.Serialize(Cuts) != JsonSerializer.Serialize(o.Cuts)
        || JsonSerializer.Serialize(Texts) != JsonSerializer.Serialize(o.Texts) || JsonSerializer.Serialize(Images) != JsonSerializer.Serialize(o.Images)
        || JsonSerializer.Serialize(Blurs) != JsonSerializer.Serialize(o.Blurs);

    /// <summary>A damaged or hand-edited document can never wedge the editor.</summary>
    public void Sanitize(double duration)
    {
        Quality = RecorderSupport.SanitizedQuality(Quality);
        ExportSpeed = double.IsFinite(ExportSpeed) && ExportSpeed > 0 ? Math.Clamp(ExportSpeed, 0.25, 4) : 1;
        GifSize = GifSize is "small" or "medium" or "large" ? GifSize : "medium";
        GifFrameRate = GifFrameRate is 8 or 12 or 15 ? GifFrameRate : 12;
        Aspect = ResolvedAspect;
        PointerSmoothing = RecorderMotion.SmoothingName(ResolvedSmoothing);
        PointerSize = double.IsFinite(PointerSize) ? Math.Clamp(PointerSize, 0.5, 2.0) : 1;
        ZoomAmount = ZoomTimeline.SanitizedAmount(ZoomAmount);
        SystemAudioGain = double.IsFinite(SystemAudioGain) ? Math.Clamp(SystemAudioGain, 0, 1) : 1;
        MicrophoneGain = double.IsFinite(MicrophoneGain) ? Math.Clamp(MicrophoneGain, 0, 1) : 1;
        var style = ResolvedBackdrop;
        Backdrop = style.IsNone ? string.Empty : style.ToJson();
        ZoomSegments = ZoomTimeline.Normalized(ZoomSegments, duration);
        Texts = TextOverlay.Normalized(Texts, duration);
        Images = ImageOverlay.Normalized(Images, duration);
    }

    /// <summary>True once something worth warning about before closing has changed.</summary>
    public bool IsEdited(double duration)
    {
        var (s, e) = RecorderSupport.SanitizedTrim(TrimStart, TrimEnd, duration);
        return s > 0.01 || e < duration - 0.01 || !KeepsSystemAudio || !KeepsMicrophone || SystemAudioGain != 1 || MicrophoneGain != 1
            || Cuts.Count > 0 || ZoomSegments.Count > 0 || Texts.Count > 0 || Blurs.Count > 0 || Images.Count > 0 || Math.Abs(ExportSpeed - 1) > 0.0001;
    }

    /// <summary>True when the exported picture differs from the raw recording (anything drawn, framed or zoomed).</summary>
    public bool HasPictureEffects(PointerTrack track, double duration) =>
        Blurs.Count > 0 || Texts.Count > 0 || Images.Count > 0 || !ResolvedBackdrop.IsNone || ResolvedAspect != "original"
        || (ShowsPointer && !track.IsEmpty) || ActiveZoomSegments(duration).Count > 0;

    /// <summary>The kept source ranges: the trimmed span minus the (merged) cuts.</summary>
    public List<(double Start, double End)> KeptRanges(double duration)
    {
        var (s, e) = RecorderSupport.SanitizedTrim(TrimStart, TrimEnd, duration);
        var cuts = Cuts.Where(c => c.End - c.Start >= 0.1).OrderBy(c => c.Start).ToList();
        var result = new List<(double, double)>();
        var at = s;
        foreach (var c in cuts)
        {
            var cs = Math.Max(c.Start, s);
            var ce = Math.Min(c.End, e);
            if (ce <= cs)
            {
                continue;
            }

            if (cs > at + 0.001)
            {
                result.Add((at, cs));
            }

            at = Math.Max(at, ce);
        }

        if (e > at + 0.001)
        {
            result.Add((at, e));
        }

        return result;
    }

    public double OutputDuration(double duration) => KeptRanges(duration).Sum(r => r.End - r.Start);

    public double SourceTime(double outputTime, double duration)
    {
        var t = Math.Max(0, outputTime);
        foreach (var (a, b) in KeptRanges(duration))
        {
            if (t <= b - a + 1e-9)
            {
                return a + t;
            }

            t -= b - a;
        }

        return KeptRanges(duration).LastOrDefault().End;
    }

    public double? OutputTime(double sourceTime, double duration)
    {
        double acc = 0;
        foreach (var (a, b) in KeptRanges(duration))
        {
            if (sourceTime >= a && sourceTime <= b)
            {
                return acc + sourceTime - a;
            }

            acc += b - a;
        }

        return null;
    }
}

/// <summary>A recording that exists only while its editor is open (or until it is saved): folder, files, duration.</summary>
internal sealed class Take
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public required string Folder { get; init; }

    public string VideoPath => Path.Combine(Folder, "take.mp4");

    public string SystemWav => Path.Combine(Folder, "system.wav");

    public string MicWav => Path.Combine(Folder, "mic.wav");

    public bool HasSystemAudio => File.Exists(SystemWav);

    public bool HasMicrophone => File.Exists(MicWav);

    public double Duration { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int Fps { get; set; } = 30;

    public EditDocument Document { get; set; } = new();

    public string PointerPath => Path.Combine(Folder, "pointer.bin");

    public string TypingPath => Path.Combine(Folder, "typing.json");

    private PointerTrack? _pointer;
    private List<double>? _typing;

    /// <summary>The recorded pointer track (empty when the take has none).</summary>
    public PointerTrack Pointer => _pointer ??= PointerTrack.Load(PointerPath);

    public IReadOnlyList<double> TypingTimes => _typing ??= TypingTrack.Load(TypingPath);

    public static string Root => Path.Combine(Output.DataRoot(), "Recordings");

    public static Take Create()
    {
        var folder = Path.Combine(Root, "Take-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return new Take { Folder = folder };
    }

    /// <summary>Copies a picture into the take (<c>&lt;take&gt;\&lt;guid&gt;\&lt;file name&gt;</c>) after checking it decodes and fits; null on any failure.</summary>
    public string? ImportImage(string source)
    {
        try
        {
            var info = new FileInfo(source);
            if (!info.Exists || info.Length == 0 || info.Length > 64L * 1024 * 1024 || RecorderSupport.FreeBytes(Folder) < info.Length + RecorderSupport.ContinueFreeBytes)
            {
                return null;
            }

            var probe = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(info.FullName), System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None);
            if (probe.PixelWidth < 1 || probe.PixelHeight < 1)
            {
                return null;
            }

            var dir = Path.Combine(Folder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, info.Name);
            File.Copy(info.FullName, dest, false);
            return dest;
        }
        catch
        {
            return null;
        }
    }

    public void SaveDocument()
    {
        try
        {
            File.WriteAllText(Path.Combine(Folder, "edit.json"), JsonSerializer.Serialize(Document, Json));
            File.WriteAllText(Path.Combine(Folder, "meta.json"), JsonSerializer.Serialize(new { duration = Duration, width = Width, height = Height, fps = Fps }, Json));
        }
        catch
        {
            // not critical
        }
    }

    public void Delete()
    {
        try
        {
            Directory.Delete(Folder, true);
        }
        catch
        {
            // files may still be in use by the player; the sweep removes them later
        }
    }

    private string MarkerPath(string name) => Path.Combine(Folder, name);

    /// <summary>True once the take has been saved, copied out or exported at least once.</summary>
    public bool IsExported => File.Exists(MarkerPath("exported"));

    /// <summary>True once the person chose to throw the take away.</summary>
    public bool IsDiscarded => File.Exists(MarkerPath("discarded"));

    /// <summary>Remembers on disk that the take has been exported, so the sweep may remove it after a day.</summary>
    public void MarkExported() => WriteMarker("exported");

    /// <summary>Remembers on disk that the person discarded the take, so the sweep may remove it soon.</summary>
    public void MarkDiscarded() => WriteMarker("discarded");

    private void WriteMarker(string name)
    {
        try
        {
            File.WriteAllText(MarkerPath(name), DateTime.UtcNow.ToString("O"));
        }
        catch
        {
            // the marker only shortens how long the files are kept
        }
    }

    /// <summary>
    /// A take is worth offering back when its video was finished (meta.json is written once the file is closed) and nothing was
    /// exported or discarded. A video that was never closed has no index and cannot be played.
    /// </summary>
    public static bool IsFinished(string folder) => File.Exists(Path.Combine(folder, "take.mp4")) && File.Exists(Path.Combine(folder, "meta.json"));

    /// <summary>Opens a take left on disk (video, meta.json and edit.json); null when it cannot be read.</summary>
    public static Take? Load(string folder)
    {
        try
        {
            if (!IsFinished(folder))
            {
                return null;
            }

            using var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "meta.json")));
            var root = meta.RootElement;
            var take = new Take
            {
                Folder = folder,
                Duration = root.GetProperty("duration").GetDouble(),
                Width = root.GetProperty("width").GetInt32(),
                Height = root.GetProperty("height").GetInt32(),
                Fps = root.TryGetProperty("fps", out var fps) ? fps.GetInt32() : 30,
            };
            var editPath = Path.Combine(folder, "edit.json");
            if (File.Exists(editPath))
            {
                take.Document = JsonSerializer.Deserialize<EditDocument>(File.ReadAllText(editPath), Json) ?? new EditDocument();
            }

            return take.Duration > 0 && take.Width > 0 && take.Height > 0 ? take : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Takes in the Recordings folder that finished but were never exported or discarded, newest first, excluding the owned ones.</summary>
    public static List<(string Folder, DateTime Time)> Recoverable(IEnumerable<string> owned)
    {
        var result = new List<(string, DateTime)>();
        try
        {
            if (!Directory.Exists(Root))
            {
                return result;
            }

            var keep = new HashSet<string>(owned, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in Directory.EnumerateDirectories(Root, "Take-*"))
            {
                if (keep.Contains(dir) || !IsFinished(dir) || File.Exists(Path.Combine(dir, "exported")) || File.Exists(Path.Combine(dir, "discarded")))
                {
                    continue;
                }

                result.Add((dir, Directory.GetLastWriteTime(dir)));
            }
        }
        catch
        {
            // listing only
        }

        return result.OrderByDescending(r => r.Item2).ToList();
    }

    /// <summary>
    /// Housekeeping for takes no editor owns: discarded ones go after an hour, exported ones after a day, and one that was never
    /// exported waits up to a week so it can be recovered (a take without a finished video is junk after an hour).
    /// </summary>
    public static void Sweep(IEnumerable<string> owned)
    {
        try
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            var keep = new HashSet<string>(owned, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in Directory.EnumerateDirectories(Root, "Take-*"))
            {
                var age = (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir)).TotalHours;
                var action = TakeSweep.Decide(age, IsFinished(dir), File.Exists(Path.Combine(dir, "exported")), File.Exists(Path.Combine(dir, "discarded")), keep.Contains(dir));
                if (action == TakeAction.Delete)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch
                    {
                        // in use
                    }
                }
            }
        }
        catch
        {
            // housekeeping only
        }
    }
}

internal enum TakeAction
{
    Keep,
    Delete,
    Recoverable,
}

/// <summary>What the sweep does with a take folder.</summary>
internal static class TakeSweep
{
    public static TakeAction Decide(double ageHours, bool hasMaster, bool exported, bool discarded, bool owned)
    {
        if (owned)
        {
            return TakeAction.Keep;
        }

        if (discarded)
        {
            return ageHours > 1 ? TakeAction.Delete : TakeAction.Keep;
        }

        if (exported)
        {
            return ageHours > 24 ? TakeAction.Delete : TakeAction.Keep;
        }

        if (!hasMaster)
        {
            return ageHours > 1 ? TakeAction.Delete : TakeAction.Keep;
        }

        return ageHours > 168 ? TakeAction.Delete : TakeAction.Recoverable;
    }
}
