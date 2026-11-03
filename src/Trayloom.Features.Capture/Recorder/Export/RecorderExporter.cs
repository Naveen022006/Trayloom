// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Trayloom.Features.Capture;

/// <summary>
/// Exports a take: MP4 (plain Media Foundation render when nothing is drawn, otherwise frame by frame through the shared composer)
/// and GIF. Audio is mixed from the sidecars; export speed is applied with a pitch-preserving WSOLA stretch.
/// </summary>
internal static class RecorderExporter
{
    private static (int W, int H) OutputSize(Take take, EditDocument doc)
    {
        var scale = RecorderSupport.OutputScale(doc.Quality);
        return (RecorderSupport.EvenSize(take.Width * scale), RecorderSupport.EvenSize(take.Height * scale));
    }

    private static string Staging(string dest, string ext) =>
        Path.Combine(Path.GetDirectoryName(dest)!, ".trayloom-partial-" + Guid.NewGuid().ToString("N")[..10] + ext);

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

    /// <summary>The kept sidecar tracks over the kept ranges (gains applied, ranges concatenated) as 48 kHz stereo floats at 1x speed.</summary>
    public static float[]? MixFloat(Take take, EditDocument doc)
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
        var total = (int)Math.Round(ranges.Sum(r => r.End - r.Start) * 48000);
        var mix = new float[total * 2];
        var pos = 0;
        foreach (var (a, b) in ranges)
        {
            var frames = (int)Math.Round((b - a) * 48000);
            var from = (long)Math.Round(a * 48000);
            for (var i = 0; i < frames && pos + i < total; i++)
            {
                var src = from + i;
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

        return mix;
    }

    /// <summary>The mix at the export speed (WSOLA time stretch keeps the pitch) as 16-bit stereo PCM bytes.</summary>
    public static byte[]? MixAudio(Take take, EditDocument doc, double speed)
    {
        var mix = MixFloat(take, doc);
        if (mix is null)
        {
            return null;
        }

        if (Math.Abs(speed - 1) > 0.0005)
        {
            mix = TimeStretch.Stretch(mix, speed);
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

    // ---- STA helper --------------------------------------------------------------------------------------------------

    /// <summary>WPF visuals are thread-affine: the composer runs on its own STA thread.</summary>
    private static Task<T> RunSta<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            finally
            {
                try
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
                catch
                {
                    // nothing to shut down
                }
            }
        })
        { IsBackground = true, Name = "Trayloom recorder composer" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    /// <summary>The composer plus a decoder: gives the finished BGRA frame for a moment of the edited timeline.</summary>
    private sealed class ComposedFrames
    {
        private const int Lanes = 4;
        private readonly Take _take;
        private readonly EditDocument _doc;
        private readonly FrameSource[] _sources;
        private readonly Image _image = new();
        private readonly Dictionary<int, (double Src, Task<BitmapSource?> Task)> _pending = new();
        private BitmapSource? _lastFrame;
        private Func<int, double>? _timeOf;
        private int _total;
        private int _started = -1;

        public ComposedFrames(Take take, EditDocument doc, double outputScale, double? gifLongEdge)
        {
            _take = take;
            _doc = doc;
            var src = new Size(take.Width, take.Height);
            var plan = RecorderPlan.Build(doc, take.Pointer, take.TypingTimes, src, take.Fps, take.Duration, outputScale)
                ?? throw new InvalidOperationException("No plan");
            if (gifLongEdge is { } edge)
            {
                var longest = Math.Max(plan.Canvas.Width, plan.Canvas.Height);
                if (longest > edge)
                {
                    plan = RecorderPlan.Build(doc, take.Pointer, take.TypingTimes, src, take.Fps, take.Duration, edge / longest)!;
                }
            }

            Plan = plan;
            Composer = new RecorderComposer(plan, _image, false);
            _sources = Enumerable.Range(0, Lanes).Select(_ => FrameSource.OpenAsync(take.VideoPath).GetAwaiter().GetResult()).ToArray();
        }

        public RecorderPlan Plan { get; }

        public RecorderComposer Composer { get; }

        public int Width => (int)Plan.Canvas.Width;

        public int Height => (int)Plan.Canvas.Height;

        /// <summary>Tells the decoder which moments (edited clock) the coming frames need, so it can read ahead on several lanes.</summary>
        public void ReadAhead(int total, Func<int, double> editedTimeOfFrame)
        {
            _total = total;
            _timeOf = editedTimeOfFrame;
        }

        private void StartAhead(int i)
        {
            if (_timeOf is null)
            {
                return;
            }

            for (var j = Math.Max(_started + 1, i); j < Math.Min(_total, i + Lanes * 2); j++)
            {
                var src = _doc.SourceTime(_timeOf(j), _take.Duration);
                if (_pending.TryGetValue(j - 1, out var prev) && Math.Abs(prev.Src - src) < 1e-4)
                {
                    _pending[j] = (src, prev.Task);
                }
                else
                {
                    var source = _sources[j % Lanes];
                    _pending[j] = (src, Task.Run<BitmapSource?>(async () =>
                    {
                        try
                        {
                            return await source.GetFrameAsync(TimeSpan.FromSeconds(src));
                        }
                        catch
                        {
                            return null;
                        }
                    }));
                }

                _started = j;
            }
        }

        /// <summary>Renders the frame for a moment on the edited clock (post trim and cuts, before speed).</summary>
        public byte[] Render(double tEdit, int planIndex, int ordinal)
        {
            StartAhead(ordinal);
            if (_pending.Remove(ordinal, out var entry))
            {
                var frame = entry.Task.GetAwaiter().GetResult();
                if (frame is not null)
                {
                    _lastFrame = frame;
                }
            }
            else
            {
                var src = _doc.SourceTime(tEdit, _take.Duration);
                try
                {
                    _lastFrame = _sources[0].GetFrameAsync(TimeSpan.FromSeconds(src)).GetAwaiter().GetResult();
                }
                catch
                {
                    // keep the previous picture so the length is preserved
                }
            }

            var picture = _lastFrame ?? throw new InvalidOperationException("No decodable frame");
            _image.Source = WithBlurs(picture, tEdit);
            Composer.Apply(planIndex, tEdit);
            return Composer.RenderBgra();
        }

        private BitmapSource WithBlurs(BitmapSource frame, double tEdit)
        {
            if (Plan.Blurs.Count == 0)
            {
                return frame;
            }

            EditorDocument? edit = null;
            BitmapSource image = frame.Format == PixelFormats.Pbgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            for (var i = 0; i < Plan.Blurs.Count; i++)
            {
                if (!Plan.BlurCovered(i, tEdit))
                {
                    continue;
                }

                var b = Plan.Blurs[i];
                edit ??= new EditorDocument(image, 1);
                var rect = new Rect(b.X * image.PixelWidth, b.Y * image.PixelHeight, b.Width * image.PixelWidth, b.Height * image.PixelHeight);
                edit.Marks.Add(new Annotation { Kind = ToolId.Pixelate, P1 = rect.TopLeft, P2 = rect.BottomRight, BlurStyle = "pixelate", BlurLevel = b.Strength, Seed = 7 });
            }

            return edit is null ? frame : EditorRenderer.RenderAnnotated(edit);
        }
    }

    // ---- MP4 --------------------------------------------------------------------------------------------------------------

    public static async Task ExportMp4Async(Take take, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        var doc = take.Document;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var staging = Staging(dest, ".mp4");
        try
        {
            if (!doc.HasPictureEffects(take.Pointer, take.Duration) && Math.Abs(doc.ExportSpeed - 1) < 0.001)
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
            CaptureHost.TrimMemory();
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
        var fps = take.Fps;
        var outDuration = doc.OutputDuration(take.Duration) / speed;
        var frames = Math.Max(1, (int)Math.Floor(outDuration * fps));
        var audio = MixAudio(take, doc, speed);
        var scale = RecorderSupport.OutputScale(doc.Quality);
        var w = 0;
        var h = 0;
        VideoWriter? writer = null;
        const int blockBytes = 48000 / 50 * 4; // 20 ms
        var audioPos = 0;

        await RunSta(() =>
        {
            var composed = new ComposedFrames(take, doc, scale, null);
            composed.ReadAhead(frames, k => (double)k / fps * speed);
            w = composed.Width;
            h = composed.Height;
            writer = new VideoWriter(staging, w, h, fps, RecorderSupport.AverageBitRate(w, h, fps, doc.Quality), false);
            writer.Begin();
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
                    var tEdit = tOut * speed;
                    var bytes = composed.Render(tEdit, composed.Plan.FrameIndex(tEdit), i);
                    if (i > 0 && i % 90 == 0)
                    {
                        // every frame is a large array; collect now and then so a long export does not climb to gigabytes
                        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
                    }

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

            return 0;
        });

        progress?.Report(0.95);
        if (writer!.Completion is { } c)
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
            var longEdge = doc.GifSize switch { "small" => 420.0, "large" => 800.0, _ => 600.0 };
            var outDuration = doc.OutputDuration(take.Duration) / speed;
            var frames = Math.Max(1, (int)Math.Floor(outDuration * fps));
            var collected = await RunSta(() =>
            {
                var list = new List<(BitmapSource Image, ushort Delay)>();
                var composed = new ComposedFrames(take, doc, 1, longEdge);
                composed.ReadAhead(frames, k => Math.Min(outDuration, (double)k / fps) * speed);
                BitmapSource? previous = null;
                for (var i = 0; i < frames; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var tOut = Math.Min(outDuration, (double)i / fps);
                    var tEdit = tOut * speed;
                    BitmapSource? image;
                    try
                    {
                        var bytes = composed.Render(tEdit, composed.Plan.FrameIndex(tEdit), i);
                        image = BitmapSource.Create(composed.Width, composed.Height, 96, 96, PixelFormats.Pbgra32, null, bytes, composed.Width * 4);
                        image.Freeze();
                        previous = image;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        image = previous;
                    }

                    if (image is null)
                    {
                        continue;
                    }

                    var delay = (ushort)Math.Max(1, Math.Round((i + 1) * 100.0 / fps) - Math.Round(i * 100.0 / fps));
                    list.Add((image, delay));
                    progress?.Report(Math.Min(0.95, (double)(i + 1) / frames));
                }

                return list;
            });

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
