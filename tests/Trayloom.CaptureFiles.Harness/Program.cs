// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Windows;
using Trayloom.Features.Capture;
using Trayloom.Features.Files;

namespace Trayloom.CaptureFiles.Harness;

/// <summary>
/// Console harness for WP-6: the take sweep table, the GIF cap, the streamed WAV reader and mixer (against the old in-memory mix),
/// the waveform peaks, dropped files with Mark-of-the-Web, shelf link filtering and the QR link rule. Works in a temp folder only.
/// Exit code 0 = all passed.
/// </summary>
internal static class Program
{
    private static int s_failed;
    private static int s_checks;

    [STAThread]
    private static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tl-wp6-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            Sweep();
            Gif();
            Audio(dir);
            Dropped(dir);
            Links();
            Unique(dir);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // temp
            }
        }

        Console.WriteLine($"{s_checks - s_failed}/{s_checks} checks passed");
        return s_failed == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string what)
    {
        s_checks++;
        if (!ok)
        {
            s_failed++;
            Console.WriteLine("FAIL: " + what);
        }
    }

    private static void Sweep()
    {
        Check(TakeSweep.Decide(1000, true, false, false, true) == TakeAction.Keep, "owned is kept");
        Check(TakeSweep.Decide(0.5, true, false, true, false) == TakeAction.Keep, "discarded under 1 h kept");
        Check(TakeSweep.Decide(1.5, true, false, true, false) == TakeAction.Delete, "discarded over 1 h deleted");
        Check(TakeSweep.Decide(23, true, true, false, false) == TakeAction.Keep, "exported under 24 h kept");
        Check(TakeSweep.Decide(25, true, true, false, false) == TakeAction.Delete, "exported over 24 h deleted");
        Check(TakeSweep.Decide(30, true, false, false, false) == TakeAction.Recoverable, "unexported with master is recoverable");
        Check(TakeSweep.Decide(167, true, false, false, false) == TakeAction.Recoverable, "recoverable up to a week");
        Check(TakeSweep.Decide(169, true, false, false, false) == TakeAction.Delete, "unexported deleted after a week");
        Check(TakeSweep.Decide(0.5, false, false, false, false) == TakeAction.Keep, "no master under 1 h kept");
        Check(TakeSweep.Decide(2, false, false, false, false) == TakeAction.Delete, "no master over 1 h deleted");
    }

    private static void Gif()
    {
        Check(GifLimits.MaxSeconds == 60, "GIF cap is 60 s");
        Check(GifLimits.Allows(60) && GifLimits.Allows(12), "GIF allows up to the cap");
        Check(!GifLimits.Allows(61) && !GifLimits.Allows(600), "GIF refuses over the cap");
    }

    private static void Audio(string dir)
    {
        var sys = Path.Combine(dir, "system.wav");
        var mic = Path.Combine(dir, "mic.wav");
        foreach (var (path, freq) in new[] { (sys, 440.0), (mic, 660.0) })
        {
            using var w = new WavWriter(path, 48000);
            var data = new float[48000 * 3 * 2];
            for (var i = 0; i < data.Length / 2; i++)
            {
                data[i * 2] = (float)(0.5 * Math.Sin(i * freq * 2 * Math.PI / 48000));
                data[i * 2 + 1] = (float)(0.25 * Math.Cos(i * freq * 2 * Math.PI / 48000));
            }

            w.Write(data);
        }

        using (var r = new WavReader(sys))
        {
            Check(r.Frames == 48000 * 3, "WavReader frame count");
            var buf = new float[20];
            r.Seek(100);
            Check(r.Read(buf) == 20, "WavReader reads a block");
        }

        var take = new Take { Folder = dir, Duration = 3 };
        var doc = new EditDocument { TrimStart = 0.2, TrimEnd = 2.9, SystemAudioGain = 0.8, MicrophoneGain = 0.6 };
        doc.Cuts.Add(new CutRange { Start = 1.0, End = 1.5 });
        take.Document = doc;

        // the previous in-memory mix, kept here as the reference
        var a = WavWriter.ReadAll(sys);
        var b = WavWriter.ReadAll(mic);
        var tracks = new List<(float[] Data, double Gain)> { (a, doc.SystemAudioGain), (b, doc.MicrophoneGain) };
        var ranges = doc.KeptRanges(take.Duration);
        var total = (int)Math.Round(ranges.Sum(r => r.End - r.Start) * 48000);
        var expected = new float[total * 2];
        var pos = 0;
        foreach (var (s, e) in ranges)
        {
            var frames = (int)Math.Round((e - s) * 48000);
            var from = (long)Math.Round(s * 48000);
            for (var i = 0; i < frames && pos + i < total; i++)
            {
                var src = from + i;
                foreach (var (data, gain) in tracks)
                {
                    if ((src + 1) * 2 < data.Length)
                    {
                        expected[(pos + i) * 2] += data[src * 2] * (float)gain;
                        expected[(pos + i) * 2 + 1] += data[src * 2 + 1] * (float)gain;
                    }
                }
            }

            pos += frames;
        }

        var mix = RecorderExporter.MixFloat(take, doc);
        Check(mix is not null && mix.Length == expected.Length, "MixFloat length");
        Check(mix is not null && MaxDiff(mix, expected) <= 1e-6, "MixFloat equals the old in-memory mix");

        var blocks = new List<float>();
        AudioMix.MixBlocks(AudioMix.Tracks(take, doc), ranges, block => blocks.AddRange(block.ToArray()), default, 10000);
        Check(blocks.Count == expected.Length && MaxDiff(blocks.ToArray(), expected) <= 1e-6, "small blocks give the same mix");

        var wav = Path.Combine(dir, "mix.wav");
        Check(AudioMix.MixToPcmWav(take, doc, wav) && new FileInfo(wav).Length == 44 + total * 4, "streamed PCM WAV size");

        var peaks = WaveformPeaks.Compute(sys, 50);
        Check(peaks.Length == 50 && peaks.All(p => p is > 0.3f and <= 0.5001f), "waveform peaks follow the signal");
        Check(WaveformPeaks.Compute(Path.Combine(dir, "missing.wav"), 10).Length == 0, "waveform of a missing file is empty");
    }

    private static double MaxDiff(float[] x, float[] y)
    {
        double max = 0;
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            max = Math.Max(max, Math.Abs(x[i] - y[i]));
        }

        return max;
    }

    private static void Dropped(string dir)
    {
        var shelf = Path.Combine(dir, "shelf");
        Directory.CreateDirectory(shelf);
        var path = ShelfDrops.WriteDropped(shelf, "note.txt", new MemoryStream("hello"u8.ToArray()));
        Check(path is not null && File.ReadAllText(path) == "hello", "dropped file is written");
        string? zone = null;
        try
        {
            zone = path is null ? null : File.ReadAllText(path + ":Zone.Identifier");
        }
        catch
        {
            // no alternate streams on this volume
        }

        Check(zone is not null && zone.Contains("ZoneId=3"), "dropped file carries Zone.Identifier ZoneId=3");
        Check(ShelfDrops.WriteDropped(shelf, "note.txt", new MemoryStream("other"u8.ToArray())) is null && File.ReadAllText(path!) == "hello", "an existing name is refused and left alone");
        Check(ShelfDrops.WriteDropped(shelf, "..\\evil.txt", new MemoryStream([1])) is { } p2 && Path.GetDirectoryName(p2) == shelf, "a path in the name is reduced to the file name");
        Check(ShelfDrops.IsRisky("a.EXE") && ShelfDrops.IsRisky("x.settingcontent-ms") && !ShelfDrops.IsRisky("a.png"), "risky extensions");
        Check(ShelfDrops.IsUnder(shelf, path!) && !ShelfDrops.IsUnder(shelf, Path.Combine(dir, "other.txt")), "IsUnder");
    }

    private static IDataObject Url(string format, string url)
    {
        var data = new DataObject();
        var bytes = format.EndsWith('W') ? System.Text.Encoding.Unicode.GetBytes(url + "\0") : System.Text.Encoding.Default.GetBytes(url + "\0");
        data.SetData(format, new MemoryStream(bytes));
        return data;
    }

    private static void Links()
    {
        Check(ShelfStore.ReadLink(Url("UniformResourceLocatorW", "file:///C:/x.exe")) is null, "file: URL is not a link (W)");
        Check(ShelfStore.ReadLink(Url("UniformResourceLocator", "file:///C:/x.exe")) is null, "file: URL is not a link (A)");
        Check(ShelfStore.ReadLink(Url("UniformResourceLocatorW", "ms-msdt:/id x")) is null, "ms-msdt is not a link");
        Check(ShelfStore.ReadLink(Url("UniformResourceLocatorW", "https://a.b")) == "https://a.b", "https URL is a link");
        Check(ShelfStore.ReadLink(Url("UniformResourceLocator", "mailto:a@b.c")) == "mailto:a@b.c", "mailto is a link");
        Check(OcrSupport.OpenableUrl("https://a.b/c") is "https://a.b/c", "QR https opens");
        Check(OcrSupport.OpenableUrl("mailto:a@b.c") is null && OcrSupport.OpenableUrl("file:///C:/x") is null && OcrSupport.OpenableUrl("C:\\x.exe") is null, "QR refuses mailto, file and paths");
    }

    private static void Unique(string dir)
    {
        var a = Output.WriteUnique(dir, "cap", ".png", [1]);
        var b = Output.WriteUnique(dir, "cap", ".png", [2]);
        Check(a != b && File.ReadAllBytes(a)[0] == 1 && File.ReadAllBytes(b)[0] == 2, "WriteUnique never overwrites");
    }
}
