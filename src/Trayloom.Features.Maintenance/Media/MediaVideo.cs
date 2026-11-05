// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Trayloom.Features.Maintenance;

internal sealed record VideoRequest(string Input, string Output, bool OutputManual, double Start, double End, double Quality, int MaxDimension, string Sizing, long TargetBytes);

internal sealed record GifRequest(string Input, string Output, bool OutputManual, double Start, double End, int Width, double Fps, bool Loops, string Sizing, long TargetBytes);

/// <summary>Video and GIF work on Media Foundation (<c>MediaTranscoder</c>, <c>MediaComposition</c>); ffmpeg is only a fallback.</summary>
internal static class MediaVideo
{
    private const int MaximumGifFrames = 300;

    /// <summary>Reads duration, size, frame rate and tracks of a video file.</summary>
    public static async Task<VideoInfo?> ProbeAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
            var props = await file.Properties.GetVideoPropertiesAsync().AsTask().ConfigureAwait(false);
            var profile = await MediaEncodingProfile.CreateFromFileAsync(file).AsTask().ConfigureAwait(false);
            var hasVideo = profile?.Video is not null && props.Width > 0 && props.Height > 0;
            var width = (int)props.Width;
            var height = (int)props.Height;
            if (props.Orientation is VideoOrientation.Rotate90 or VideoOrientation.Rotate270)
            {
                (width, height) = (height, width);
            }

            var fps = 30.0;
            if (profile?.Video?.FrameRate is { Denominator: > 0 } rate)
            {
                fps = Math.Clamp((double)rate.Numerator / rate.Denominator, 1, 60);
            }

            return new VideoInfo(props.Duration.TotalSeconds, width, height, fps, profile?.Audio is not null, hasVideo, (long)props.Bitrate);
        }
        catch
        {
            return null;
        }
    }

    private static (double Start, double End) SanitizedTrim(double start, double end, double duration)
    {
        var s = Math.Clamp(start, 0, duration);
        var e = end <= 0 ? duration : end;
        e = Math.Clamp(e, s, duration);
        return (s, e);
    }

    private static int EvenAtLeast2(double value) => Math.Max(2, (int)Math.Round(value) / 2 * 2);

    /// <summary>Scales the display size so the long side is at most <paramref name="maxDimension"/> (never enlarging), even sides.</summary>
    public static (int W, int H) ScaledEven(int width, int height, int maxDimension)
    {
        var longSide = Math.Max(width, height);
        var scale = longSide > maxDimension ? (double)maxDimension / longSide : 1;
        return (EvenAtLeast2(width * scale), EvenAtLeast2(height * scale));
    }

    private static (int W, int H) Aligned16(int width, int height) => (Math.Max(16, width / 16 * 16), Math.Max(16, height / 16 * 16));

    private static long BitrateFor(double quality, int width, int height, double fps, long sourceBitrate)
    {
        var bpp = quality >= 0.82 ? 0.16 : quality >= 0.4 ? 0.10 : 0.05;
        var computed = Math.Clamp(width * (double)height * fps * bpp, 300_000, 40_000_000);
        if (sourceBitrate > 0)
        {
            computed = Math.Min(computed, Math.Max(300_000, sourceBitrate));
        }

        return (long)computed;
    }

    private static MediaEncodingProfile ProfileFor(int width, int height, double fps, long videoBitrate, long audioBitrate, bool hasAudio)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.Bitrate = (uint)Math.Min(videoBitrate, uint.MaxValue);
        var rate = Math.Clamp(fps, 1, 60);
        profile.Video.FrameRate.Numerator = (uint)Math.Round(rate * 1000);
        profile.Video.FrameRate.Denominator = 1000;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        if (hasAudio && profile.Audio is not null)
        {
            profile.Audio.Bitrate = (uint)audioBitrate;
        }
        else
        {
            profile.Audio = null;
        }

        return profile;
    }

    private static async Task TranscodeAsync(StorageFile source, string tempPath, MediaEncodingProfile profile, double start, double stop, double trimDuration, Action<double> progress, CancellationToken ct)
    {
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(tempPath)!).AsTask(ct).ConfigureAwait(false);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var destination = await folder.CreateFileAsync(Path.GetFileName(tempPath), CreationCollisionOption.ReplaceExisting).AsTask(ct).ConfigureAwait(false);
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = attempt == 0 };
            if (start > 0)
            {
                transcoder.TrimStartTime = TimeSpan.FromSeconds(start);
            }

            if (stop > 0)
            {
                transcoder.TrimStopTime = TimeSpan.FromSeconds(stop);
            }

            var prepared = await transcoder.PrepareFileTranscodeAsync(source, destination, profile).AsTask(ct).ConfigureAwait(false);
            if (!prepared.CanTranscode)
            {
                if (attempt == 0)
                {
                    continue;
                }

                throw prepared.FailureReason == TranscodeFailureReason.CodecNotFound
                    ? new MediaException(MediaFailureKind.Unsupported)
                    : new MediaException(MediaFailureKind.Message, "This video can’t be processed with the built-in encoder.");
            }

            var operation = prepared.TranscodeAsync();
            using var registration = ct.Register(() => operation.Cancel());
            try
            {
                await operation.AsTask(ct, new Progress<double>(p => progress(Math.Clamp(p / 100.0, 0, 1)))).ConfigureAwait(false);
                return;
            }
            catch (Exception) when (attempt == 0 && !ct.IsCancellationRequested)
            {
                // retry once in software
            }
        }

        throw new MediaException(MediaFailureKind.Unsupported);
    }

    // ------------------------------------------------------------------ video

    public static async Task<MediaResult> CompressAsync(VideoRequest r, Action<double> progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var info = await ProbeAsync(r.Input).ConfigureAwait(false) ?? throw new MediaException(MediaFailureKind.Unsupported);
        if (!info.HasVideo)
        {
            throw new MediaException(MediaFailureKind.NoVideoTrack);
        }

        var (start, end) = SanitizedTrim(r.Start, r.End, info.Duration);
        var duration = end - start;
        if (duration <= 0.05)
        {
            throw new MediaException(MediaFailureKind.Unsupported);
        }

        var source = await StorageFile.GetFileFromPathAsync(r.Input).AsTask(ct).ConfigureAwait(false);
        var temp = StagedOutput.TempFor(r.Output);
        Directory.CreateDirectory(Path.GetDirectoryName(r.Output)!);
        var stop = end < info.Duration - 0.01 ? end : 0;
        try
        {
            if (r.Sizing == "targetSize")
            {
                await CompressToTargetAsync(r, info, source, temp, start, stop, duration, progress, ct).ConfigureAwait(false);
            }
            else
            {
                var (w, h) = Aligned16(ScaledEven(info.Width, info.Height, r.MaxDimension).W, ScaledEven(info.Width, info.Height, r.MaxDimension).H);
                var scaled = ScaledEven(info.Width, info.Height, r.MaxDimension);
                (w, h) = Aligned16(scaled.W, scaled.H);
                var video = BitrateFor(r.Quality, w, h, Math.Min(info.Fps, 60), info.Bitrate);
                var profile = ProfileFor(w, h, info.Fps, video, r.Quality < 0.4 ? 64_000 : 128_000, info.HasAudio);
                await TranscodeAsync(source, temp, profile, start, stop, duration, progress, ct).ConfigureAwait(false);
            }

            StagedOutput.Commit(temp, r.Output, r.OutputManual, ct);
        }
        catch
        {
            StagedOutput.TryDelete(temp);
            throw;
        }

        progress(1);
        return new MediaResult
        {
            Tool = MediaTool.Video,
            Input = r.Input,
            Outputs = [r.Output],
            OriginalBytes = new FileInfo(r.Input).Length,
            OutputBytes = new FileInfo(r.Output).Length,
            Elapsed = watch.Elapsed,
        };
    }

    private static async Task CompressToTargetAsync(VideoRequest r, VideoInfo info, StorageFile source, string temp, double start, double stop, double duration, Action<double> progress, CancellationToken ct)
    {
        var scale = 1.0;
        const int passes = 3;
        for (var pass = 0; pass < passes; pass++)
        {
            ct.ThrowIfCancellationRequested();
            var audio = info.HasAudio ? (r.TargetBytes * 8.0 / duration < 1_200_000 ? 64_000 : 128_000) : 0;
            var budget = Math.Floor(r.TargetBytes * 8 * 0.94 / duration) - audio;
            var video = budget * Math.Min(1, scale);
            if (video < 240_000)
            {
                throw new MediaException(MediaFailureKind.TargetTooSmall);
            }

            var sourcePixels = (double)info.Width * info.Height;
            var affordable = video / (Math.Clamp(info.Fps, 1, 60) * 0.07);
            var ratio = Math.Min(1, Math.Sqrt(affordable / sourcePixels));
            var longEdge = Math.Max(info.Width, info.Height);
            var floor = Math.Min(1, 480.0 / longEdge);
            var size = ScaledEven(info.Width, info.Height, (int)Math.Round(longEdge * Math.Max(ratio, floor)));
            var profile = ProfileFor(size.W, size.H, info.Fps, (long)video, audio > 0 ? audio : 128_000, info.HasAudio);
            var passIndex = pass;
            await TranscodeAsync(source, temp, profile, start, stop, duration, p => progress((passIndex + p) / passes), ct).ConfigureAwait(false);
            var actual = new FileInfo(temp).Length;
            if (actual <= r.TargetBytes)
            {
                return;
            }

            if (pass == passes - 1)
            {
                StagedOutput.TryDelete(temp);
                throw new MediaException(MediaFailureKind.TargetTooSmall);
            }

            scale = Math.Min(scale * 0.9, scale * r.TargetBytes / actual * 0.94);
        }
    }

    // ------------------------------------------------------------------ GIF

    /// <summary>Finds ffmpeg: the app folder first, then Trayloom's tools folder, then absolute PATH entries only.</summary>
    public static string? FindFfmpeg()
    {
        try
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(beside))
            {
                return beside;
            }

            var local = Path.Combine(Trayloom.Core.AppPaths.LocalRoot,"tools", "ffmpeg.exe");
            if (File.Exists(local))
            {
                return local;
            }

            foreach (var raw in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = raw.Trim().Trim('"');
                if (dir.Length == 0 || dir == "." || !Path.IsPathFullyQualified(dir))
                {
                    continue;
                }

                var candidate = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<MediaResult> MakeGifAsync(GifRequest r, Action<double> progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var info = await ProbeAsync(r.Input).ConfigureAwait(false) ?? throw new MediaException(MediaFailureKind.Unsupported);
        if (!info.HasVideo)
        {
            throw new MediaException(MediaFailureKind.NoVideoTrack);
        }

        var (start, end) = SanitizedTrim(r.Start, r.End, info.Duration);
        var duration = end - start;
        if (duration <= 0.05)
        {
            throw new MediaException(MediaFailureKind.Unsupported);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(r.Output)!);
        var temp = StagedOutput.TempFor(r.Output);
        try
        {
            if (r.Sizing == "targetSize")
            {
                var width = Math.Clamp(info.Width, 160, 1600);
                var fps = Math.Max(6, Math.Min(15, Math.Floor(MaximumGifFrames / duration)));
                for (var pass = 0; pass < 4; pass++)
                {
                    var passIndex = pass;
                    await EncodeGifAsync(r.Input, temp, info, start, end, width, fps, r.Loops, p => progress((passIndex + p) / 4), ct).ConfigureAwait(false);
                    var actual = new FileInfo(temp).Length;
                    if (actual <= r.TargetBytes)
                    {
                        break;
                    }

                    var ratio = Math.Min(0.9, r.TargetBytes * 0.94 / actual);
                    var nextFps = Math.Max(6, Math.Round(fps * ratio));
                    var remaining = Math.Min(1, ratio / (nextFps / fps));
                    var nextWidth = Math.Max(160, (int)Math.Round(width * Math.Sqrt(remaining)));
                    if (pass == 3 || (nextFps == fps && nextWidth == width))
                    {
                        StagedOutput.TryDelete(temp);
                        throw new MediaException(MediaFailureKind.TargetTooSmall);
                    }

                    fps = nextFps;
                    width = nextWidth;
                }
            }
            else
            {
                await EncodeGifAsync(r.Input, temp, info, start, end, r.Width, r.Fps, r.Loops, progress, ct).ConfigureAwait(false);
            }

            StagedOutput.Commit(temp, r.Output, r.OutputManual, ct);
        }
        catch
        {
            StagedOutput.TryDelete(temp);
            throw;
        }

        progress(1);
        return new MediaResult
        {
            Tool = MediaTool.Gif,
            Input = r.Input,
            Outputs = [r.Output],
            OriginalBytes = new FileInfo(r.Input).Length,
            OutputBytes = new FileInfo(r.Output).Length,
            Elapsed = watch.Elapsed,
        };
    }

    private static async Task EncodeGifAsync(string input, string temp, VideoInfo info, double start, double end, int width, double fps, bool loops, Action<double> progress, CancellationToken ct)
    {
        var duration = end - start;
        var frames = (int)Math.Ceiling(duration * fps);
        if (frames > MaximumGifFrames)
        {
            throw new MediaException(MediaFailureKind.GifTooLong, null, Math.Max(1, (int)Math.Floor(MaximumGifFrames / fps)));
        }

        var scaled = ScaledEven(info.Width, info.Height, width);
        if (FindFfmpeg() is { } ffmpeg)
        {
            await FfmpegGifAsync(ffmpeg, input, temp, start, duration, scaled.W, fps, loops, progress, ct).ConfigureAwait(false);
            return;
        }

        await FrameGifAsync(input, temp, start, end, frames, scaled.W, scaled.H, fps, loops, progress, ct).ConfigureAwait(false);
    }

    private static async Task FfmpegGifAsync(string ffmpeg, string input, string output, double start, double duration, int width, double fps, bool loops, Action<double> progress, CancellationToken ct)
    {
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[]
        {
            "-y", "-ss", start.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "-t", duration.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "-i", input,
            "-vf", $"fps={fps.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)},scale={width}:-2:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse",
            "-loop", loops ? "0" : "-1", "-f", "gif", output,
        })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new MediaException(MediaFailureKind.Unsupported);
        using var registration = ct.Register(() =>
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // already ended
            }
        });
        var time = new Regex(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                var m = time.Match(line);
                if (m.Success && duration > 0)
                {
                    var seconds = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                    progress(Math.Clamp(seconds / duration, 0, 0.98));
                }
            }
        });
        _ = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0 || !File.Exists(output))
        {
            throw new MediaException(MediaFailureKind.Unsupported);
        }
    }

    private static async Task FrameGifAsync(string input, string temp, double start, double end, int frames, int width, int height, double fps, bool loops, Action<double> progress, CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(input).AsTask(ct).ConfigureAwait(false);
        var clip = await MediaClip.CreateFromFileAsync(file).AsTask(ct).ConfigureAwait(false);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        var delay = (ushort)Math.Max(2, Math.Round(100.0 / fps));
        var sources = new List<BitmapSource>();
        for (var i = 0; i < frames; i++)
        {
            ct.ThrowIfCancellationRequested();
            var time = Math.Min(end, start + i / fps);
            using var stream = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(time), width, height, VideoFramePrecision.NearestFrame).AsTask(ct).ConfigureAwait(false);
            using var read = stream.AsStreamForRead();
            using var copy = new MemoryStream();
            await read.CopyToAsync(copy, ct).ConfigureAwait(false);
            copy.Position = 0;
            var decoder = BitmapDecoder.Create(copy, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var source = decoder.Frames[0];
            source.Freeze();
            sources.Add(source);
            progress((i + 1.0) / frames * 0.9);
        }

        ct.ThrowIfCancellationRequested();
        EncodeFrames(sources, delay, temp, loops);
    }

    /// <summary>Encodes the frames in one synchronous block (WPF encoders have thread affinity).</summary>
    private static void EncodeFrames(List<BitmapSource> sources, ushort delay, string temp, bool loops)
    {
        var encoder = new GifBitmapEncoder();
        foreach (var source in sources)
        {
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", delay);
            metadata.SetQuery("/grctlext/Disposal", (byte)1);
            encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        }

        using (var output = File.Create(temp))
        {
            encoder.Save(output);
        }

        if (loops)
        {
            AddLoopExtension(temp);
        }
    }

    /// <summary>Inserts the NETSCAPE2.0 loop extension (infinite) after the global colour table.</summary>
    internal static void AddLoopExtension(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 14 || Encoding.ASCII.GetString(bytes, 0, 3) != "GIF" || Encoding.ASCII.GetString(bytes).Contains("NETSCAPE2.0", StringComparison.Ordinal))
        {
            return;
        }

        var packed = bytes[10];
        var offset = 13;
        if ((packed & 0x80) != 0)
        {
            offset += 3 * (1 << ((packed & 7) + 1));
        }

        byte[] extension = [0x21, 0xFF, 0x0B, .. "NETSCAPE2.0"u8.ToArray(), 0x03, 0x01, 0x00, 0x00, 0x00];
        var result = new byte[bytes.Length + extension.Length];
        Buffer.BlockCopy(bytes, 0, result, 0, offset);
        Buffer.BlockCopy(extension, 0, result, offset, extension.Length);
        Buffer.BlockCopy(bytes, offset, result, offset + extension.Length, bytes.Length - offset);
        File.WriteAllBytes(path, result);
    }
}
