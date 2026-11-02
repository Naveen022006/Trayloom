// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinKnife.Features.Capture;

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

    /// <summary>Deletes takes older than 24 h (or without a master and older than 1 h) that no editor owns.</summary>
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
                if (keep.Contains(dir))
                {
                    continue;
                }

                var age = DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir);
                var master = File.Exists(Path.Combine(dir, "take.mp4"));
                if (age > TimeSpan.FromHours(24) || (!master && age > TimeSpan.FromHours(1)))
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
