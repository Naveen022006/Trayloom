// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace WinKnife.Features.Capture;

/// <summary>Exports a take: MP4 (plain Media Foundation render, or frame-by-frame when blurs / speed are used) and GIF.</summary>
internal static class RecorderExporter
{
    private static (int W, int H) OutputSize(Take take, EditDocument doc)
    {
        var scale = RecorderSupport.OutputScale(doc.Quality);
        return (RecorderSupport.EvenSize(take.Width * scale), RecorderSupport.EvenSize(take.Height * scale));
    }

    private static string Staging(string dest, string ext) =>
        Path.Combine(Path.GetDirectoryName(dest)!, ".winknife-partial-" + Guid.NewGuid().ToString("N")[..10] + ext);

    private static void Commit(string staging, string dest)
    {
        if (File.Exists(dest))
        {
            File.Replace(staging, dest, null);
        }
        else
        {
            File.Move(staging, dest);
        }
    }

    // ---- audio mix --------------------------------------------------------------------------------------------------

    /// <summary>Mixes the kept sidecar tracks over the kept ranges (gains applied, speed by resampling) into 16-bit PCM bytes.</summary>
    public static byte[]? MixAudio(Take take, EditDocument doc, double speed)
    {
        var tracks = new List<(float[] Data, double Gain)>();
        if (take.HasSystemAudio && doc.KeepsSystemAudio)
        {
            tracks.Add((WavWriter.ReadAll(take.SystemWav), doc.SystemAudioGain));
        }

        if (take.HasMicrophone && doc.KeepsMicrophone)
        {
            tracks.Add((WavWriter.ReadAll(take.MicWav), doc.MicrophoneGain));
        }

        if (tracks.Count == 0)
        {
            return null;
        }

        var ranges = doc.KeptRanges(take.Duration);
        var outFrames = (int)(ranges.Sum(r => r.End - r.Start) * 48000 / speed);
        var mix = new float[outFrames * 2];
        var pos = 0;
        foreach (var (a, b) in ranges)
        {
            var frames = (int)((b - a) * 48000 / speed);
            for (var i = 0; i < frames && pos + i < outFrames; i++)
            {
                var src = (long)((a * 48000) + i * speed);
                foreach (var (data, gain) in tracks)
                {
                    if ((src + 1) * 2 < data.Length)
                    {
                        mix[(pos + i) * 2] += data[src * 2] * (float)gain;
                        mix[(pos + i) * 2 + 1] += data[src * 2 + 1] * (float)gain;
                    }
                }
            }

            pos += frames;
        }

        var pcm = new byte[mix.Length * 2];
        for (var i = 0; i < mix.Length; i++)
        {
            var v = (short)Math.Clamp(mix[i] * 32767f, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)(v >> 8);
        }

        return pcm;
    }

    private static void WritePcmWav(string path, byte[] pcm)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + pcm.Length);
        bw.Write("WAVEfmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)2);
        bw.Write(48000);
        bw.Write(48000 * 4);
        bw.Write((short)4);
        bw.Write((short)16);
        bw.Write("data"u8.ToArray());
        bw.Write(pcm.Length);
        bw.Write(pcm);
    }

    // ---- MP4 --------------------------------------------------------------------------------------------------------------

    public static async Task ExportMp4Async(Take take, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        var doc = take.Document;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var staging = Staging(dest, ".mp4");
        try
        {
            if (doc.Blurs.Count == 0 && Math.Abs(doc.ExportSpeed - 1) < 0.001)
            {
                await PlainRenderAsync(take, doc, staging, progress, ct);
            }
            else
            {
                await FrameRenderAsync(take, doc, staging, progress, ct);
            }

            ct.ThrowIfCancellationRequested();
            Commit(staging, dest);
            progress?.Report(1);
        }
        finally
        {
            try
            {
                File.Delete(staging);
            }
            catch
            {
                // gone
            }
        }
    }

    private static async Task PlainRenderAsync(Take take, EditDocument doc, string staging, IProgress<double>? progress, CancellationToken ct)
    {
        var composition = new MediaComposition();
        var file = await StorageFile.GetFileFromPathAsync(take.VideoPath);
        foreach (var (a, b) in doc.KeptRanges(take.Duration))
        {
            var clip = await MediaClip.CreateFromFileAsync(file);
            var total = clip.OriginalDuration;
            clip.TrimTimeFromStart = TimeSpan.FromSeconds(a);
            clip.TrimTimeFromEnd = total - TimeSpan.FromSeconds(Math.Min(b, total.TotalSeconds));
            composition.Clips.Add(clip);
        }

        string? wav = null;
        if (MixAudio(take, doc, 1) is { } pcm)
        {
            wav = Path.Combine(take.Folder, "mix-" + Guid.NewGuid().ToString("N")[..6] + ".wav");
            WritePcmWav(wav, pcm);
            var track = await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(wav));
            composition.BackgroundAudioTracks.Add(track);
        }

        var (w, h) = OutputSize(take, doc);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)w;
        profile.Video.Height = (uint)h;
        profile.Video.Bitrate = (uint)RecorderSupport.AverageBitRate(w, h, take.Fps, doc.Quality);
        profile.Video.FrameRate.Numerator = (uint)take.Fps;
        profile.Video.FrameRate.Denominator = 1;
        profile.Audio = wav is null ? null : AudioEncodingProperties.CreateAac(48000, 2, 160000);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(staging)!);
        var dest = await folder.CreateFileAsync(Path.GetFileName(staging), CreationCollisionOption.ReplaceExisting);
        var op = composition.RenderToFileAsync(dest, MediaTrimmingPreference.Precise, profile);
        op.Progress = (_, p) => progress?.Report(Math.Min(0.95, p / 100.0));
        using (ct.Register(() => op.Cancel()))
        {
            var result = await op;
            if (result != Windows.Media.Transcoding.TranscodeFailureReason.None)
            {
                throw new InvalidOperationException("Export failed: " + result);
            }
        }

        if (wav is not null)
        {
            try
            {
                File.Delete(wav);
            }
            catch
            {
                // sweep later
            }
        }
    }

    private static async Task FrameRenderAsync(Take take, EditDocument doc, string staging, IProgress<double>? progress, CancellationToken ct)
    {
        var speed = Math.Clamp(doc.ExportSpeed, 0.25, 4);
        var (w, h) = OutputSize(take, doc);
        var fps = take.Fps;
        var outDuration = doc.OutputDuration(take.Duration) / speed;
        var frames = Math.Max(1, (int)Math.Floor(outDuration * fps));
        var audio = MixAudio(take, doc, speed);
        var writer = new VideoWriter(staging, w, h, fps, RecorderSupport.AverageBitRate(w, h, fps, doc.Quality), false);
        writer.Begin();
        var source = await FrameSource.OpenAsync(take.VideoPath);
        var audioPos = 0;
        const int blockBytes = 48000 / 50 * 4; // 20 ms
        try
        {
            for (var i = 0; i < frames; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (writer.Failure is not null)
                {
                    throw writer.Failure;
                }

                var tOut = (double)i / fps;
                var src = doc.SourceTime(tOut * speed, take.Duration);
                var frame = await source.GetFrameAsync(TimeSpan.FromSeconds(src));
                var composed = ComposeFrame(frame, doc, src, w, h);
                var bytes = Pixels.Get(composed);
                if (!writer.TryAdd(new VideoFrame(bytes, TimeSpan.FromSeconds(tOut)), block: true))
                {
                    break;
                }

                if (audio is not null)
                {
                    var until = (int)Math.Min(audio.Length, (tOut + 1.0 / fps) * 48000 * 4);
                    until -= until % 4;
                    while (audioPos + blockBytes <= until)
                    {
                        var block = new byte[blockBytes];
                        Buffer.BlockCopy(audio, audioPos, block, 0, blockBytes);
                        writer.AddAudio(new AudioBlock(block, TimeSpan.FromSeconds(audioPos / 4.0 / 48000), TimeSpan.FromMilliseconds(20)));
                        audioPos += blockBytes;
                    }
                }

                progress?.Report(Math.Min(0.9, (double)(i + 1) / frames * 0.9));
            }

            if (audio is not null)
            {
                while (audioPos < audio.Length)
                {
                    var len = Math.Min(blockBytes, audio.Length - audioPos);
                    len -= len % 4;
                    if (len <= 0)
                    {
                        break;
                    }

                    var block = new byte[len];
                    Buffer.BlockCopy(audio, audioPos, block, 0, len);
                    writer.AddAudio(new AudioBlock(block, TimeSpan.FromSeconds(audioPos / 4.0 / 48000), TimeSpan.FromSeconds(len / 4.0 / 48000)));
                    audioPos += len;
                }
            }
        }
        finally
        {
            writer.Complete();
        }

        progress?.Report(0.95);
        if (writer.Completion is { } c)
        {
            await c;
        }

        if (writer.Failure is not null)
        {
            throw writer.Failure;
        }

        if (audio is not null)
        {
            var wav = Path.Combine(take.Folder, "mix-" + Guid.NewGuid().ToString("N")[..6] + ".wav");
            WritePcmWav(wav, audio);
            var muxed = staging + ".mux.mp4";
            var composition = new MediaComposition();
            composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(staging)));
            composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(wav)));
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Video.Width = (uint)w;
            profile.Video.Height = (uint)h;
            profile.Video.Bitrate = (uint)RecorderSupport.AverageBitRate(w, h, fps, doc.Quality);
            profile.Audio = AudioEncodingProperties.CreateAac(48000, 2, 160000);
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(staging)!);
            var target = await folder.CreateFileAsync(Path.GetFileName(muxed), CreationCollisionOption.ReplaceExisting);
            var result = await composition.RenderToFileAsync(target, MediaTrimmingPreference.Precise, profile);
            if (result != Windows.Media.Transcoding.TranscodeFailureReason.None)
            {
                throw new InvalidOperationException("Audio mux failed: " + result);
            }

            File.Move(muxed, staging, true);
            try
            {
                File.Delete(wav);
            }
            catch
            {
                // swept with the take
            }
        }
    }

    /// <summary>Applies the blur regions active at <paramref name="sourceTime"/> and scales to the output size.</summary>
    private static BitmapSource ComposeFrame(BitmapSource frame, EditDocument doc, double sourceTime, int w, int h)
    {
        BitmapSource image = frame.Format == PixelFormats.Pbgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var active = doc.Blurs.Where(b => sourceTime >= b.Start && sourceTime <= b.End).ToList();
        if (active.Count > 0)
        {
            var edit = new EditorDocument(image, 1);
            foreach (var b in active)
            {
                var rect = new Rect(b.X * image.PixelWidth, b.Y * image.PixelHeight, b.Width * image.PixelWidth, b.Height * image.PixelHeight);
                edit.Marks.Add(new Annotation { Kind = ToolId.Pixelate, P1 = rect.TopLeft, P2 = rect.BottomRight, BlurStyle = "pixelate", BlurLevel = b.Strength, Seed = 7 });
            }

            image = EditorRenderer.RenderAnnotated(edit);
        }

        if (image.PixelWidth == w && image.PixelHeight == h)
        {
            return image;
        }

        var tb = new TransformedBitmap(image, new ScaleTransform((double)w / image.PixelWidth, (double)h / image.PixelHeight));
        return Pixels.Make(Pixels.Get(tb), tb.PixelWidth, tb.PixelHeight);
    }

    // ---- GIF --------------------------------------------------------------------------------------------------------------------

    public static async Task ExportGifAsync(Take take, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        var doc = take.Document;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var staging = Staging(dest, ".gif");
        try
        {
            var speed = Math.Clamp(doc.ExportSpeed, 0.25, 4);
            var fps = doc.GifFrameRate is 8 or 12 or 15 ? doc.GifFrameRate : 12;
            var (gw, gh) = RecorderSupport.GifSize(take.Width, take.Height, doc.GifSize);
            var outDuration = doc.OutputDuration(take.Duration) / speed;
            var frames = Math.Max(1, (int)Math.Floor(outDuration * fps));
            var collected = new List<(BitmapSource Image, ushort Delay)>();

            var source = await FrameSource.OpenAsync(take.VideoPath);
            BitmapSource? previous = null;
            for (var i = 0; i < frames; i++)
            {
                ct.ThrowIfCancellationRequested();
                var tOut = Math.Min(outDuration, (double)i / fps);
                var src = doc.SourceTime(tOut * speed, take.Duration);
                BitmapSource? composed;
                try
                {
                    var frame = await source.GetFrameAsync(TimeSpan.FromSeconds(src));
                    composed = ComposeFrame(frame, doc, src, gw, gh);
                    previous = composed;
                }
                catch
                {
                    composed = previous;
                }

                if (composed is null)
                {
                    continue;
                }
                var delay = (ushort)Math.Max(1, Math.Round((i + 1) * 100.0 / fps) - Math.Round(i * 100.0 / fps));
                collected.Add((composed, delay));
                progress?.Report(Math.Min(0.95, (double)(i + 1) / frames));
                collected.Add((composed, delay));
            }

            if (collected.Count == 0)
            {
                throw new InvalidOperationException("No frames");
            }

            await Task.Run(() =>
            {
                var encoder = new GifBitmapEncoder();
                var meta = new BitmapMetadata("gif");
                meta.SetQuery("/appext/Application", Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                meta.SetQuery("/appext/Data", new byte[] { 3, 1, 0, 0, 0 });
                try
                {
                    encoder.Metadata = meta;
                }
                catch
                {
                    // loop extension is best effort
                }

                foreach (var (image, delay) in collected)
                {
                    var fm = new BitmapMetadata("gif");
                    fm.SetQuery("/grctlext/Delay", delay);
                    fm.SetQuery("/grctlext/Disposal", (byte)1);
                    encoder.Frames.Add(BitmapFrame.Create(image, null, fm, null));
                }

                using var fs = File.Create(staging);
                encoder.Save(fs);
            });

            Commit(staging, dest);
            progress?.Report(1);
        }
        finally
        {
            try
            {
                File.Delete(staging);
            }
            catch
            {
                // gone
            }
        }
    }

    // ---- frame access -----------------------------------------------------------------------------------------------------

    public static async Task<BitmapSource> FrameAtAsync(string path, TimeSpan time, int maxSide)
    {
        var source = await FrameSource.OpenAsync(path);
        var frame = await source.GetFrameAsync(time);
        return Output.Thumbnail(frame, maxSide);
    }
}

/// <summary>Random access to decoded frames of a video file (MediaComposition thumbnails with nearest-frame precision).</summary>
internal sealed class FrameSource
{
    private readonly MediaComposition _composition;
    private readonly int _w;
    private readonly int _h;

    private FrameSource(MediaComposition composition, int w, int h)
    {
        _composition = composition;
        _w = w;
        _h = h;
    }

    public int Width => _w;

    public int Height => _h;

    public static async Task<FrameSource> OpenAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var clip = await MediaClip.CreateFromFileAsync(file);
        var props = clip.GetVideoEncodingProperties();
        var comp = new MediaComposition();
        comp.Clips.Add(clip);
        return new FrameSource(comp, (int)props.Width, (int)props.Height);
    }

    public async Task<BitmapSource> GetFrameAsync(TimeSpan time)
    {
        var max = _composition.Duration - TimeSpan.FromMilliseconds(1);
        if (time > max)
        {
            time = max < TimeSpan.Zero ? TimeSpan.Zero : max;
        }

        using var stream = await _composition.GetThumbnailAsync(time, _w, _h, VideoFramePrecision.NearestFrame);
        var net = stream.AsStreamForRead();
        var ms = new MemoryStream();
        await net.CopyToAsync(ms);
        ms.Position = 0;
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
