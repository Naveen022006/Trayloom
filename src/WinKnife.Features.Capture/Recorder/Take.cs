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

    public EditDocument Clone() => JsonSerializer.Deserialize<EditDocument>(JsonSerializer.Serialize(this))!;

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

    public static string Root => Path.Combine(Output.DataRoot(), "Recordings");

    public static Take Create()
    {
        var folder = Path.Combine(Root, "Take-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return new Take { Folder = folder };
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
