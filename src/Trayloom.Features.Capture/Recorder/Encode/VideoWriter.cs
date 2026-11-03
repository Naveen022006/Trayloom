// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Trayloom.Features.Capture;

/// <summary>A frame to encode: BGRA bytes at a presentation time.</summary>
internal sealed record VideoFrame(byte[] Bgra, TimeSpan Time);

/// <summary>PCM audio block for the export mixer (16-bit stereo 48 kHz).</summary>
internal sealed record AudioBlock(byte[] Pcm, TimeSpan Time, TimeSpan Duration);

/// <summary>
/// H.264 / AAC .mp4 writer on top of Media Foundation (MediaStreamSource + MediaTranscoder). Frames are pushed with variable
/// timestamps; <see cref="Complete"/> ends the stream and <see cref="Completion"/> finishes once the file is closed.
/// </summary>
internal sealed class VideoWriter
{
    private readonly BlockingCollection<VideoFrame> _frames = new(8);
    private readonly BlockingCollection<AudioBlock>? _audio;
    private readonly int _w;
    private readonly int _h;
    private readonly int _fps;
    private readonly int _bitrate;
    private readonly string _path;
    private readonly bool _withAudio;
    private TimeSpan _frameDuration;
    private volatile bool _cancelled;

    public VideoWriter(string path, int width, int height, int fps, int bitrate, bool withAudio)
    {
        _path = path;
        _w = width;
        _h = height;
        _fps = Math.Max(1, fps);
        _bitrate = bitrate;
        _withAudio = withAudio;
        _frameDuration = TimeSpan.FromSeconds(1.0 / _fps);
        if (withAudio)
        {
            _audio = new BlockingCollection<AudioBlock>(64);
        }
    }

    public Task? Completion { get; private set; }

    public long FramesWritten { get; private set; }

    public Exception? Failure { get; private set; }

    public double Progress { get; private set; }

    private byte[] FlipRows(byte[] src)
    {
        // Media Foundation reads uncompressed RGB32 buffers as bottom-up DIBs
        var stride = _w * 4;
        var dst = new byte[src.Length];
        for (var y = 0; y < _h; y++)
        {
            System.Buffer.BlockCopy(src, y * stride, dst, (_h - 1 - y) * stride, stride);
        }

        return dst;
    }

    public void Begin()
    {
        Completion = Task.Run(RunAsync);
    }

    /// <summary>Queues a frame; false when the queue is full (the frame is dropped like a busy encoder would).</summary>
    public bool TryAdd(VideoFrame frame, bool block = false)
    {
        try
        {
            return block ? Add(frame) : _frames.TryAdd(frame);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool Add(VideoFrame frame)
    {
        while (!_cancelled)
        {
            if (_frames.TryAdd(frame, 50))
            {
                return true;
            }
        }

        return false;
    }

    public bool AddAudio(AudioBlock block)
    {
        if (_audio is null)
        {
            return false;
        }

        while (!_cancelled)
        {
            try
            {
                if (_audio.TryAdd(block, 50))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    public void Complete()
    {
        _frames.CompleteAdding();
        _audio?.CompleteAdding();
    }

    public void Cancel()
    {
        _cancelled = true;
        Complete();
    }

    private async Task RunAsync()
    {
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(_path)!);
            var file = await folder.CreateFileAsync(Path.GetFileName(_path), CreationCollisionOption.ReplaceExisting);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);

            var videoProps = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_w, (uint)_h);
            videoProps.FrameRate.Numerator = (uint)_fps;
            videoProps.FrameRate.Denominator = 1;
            var video = new VideoStreamDescriptor(videoProps);
            MediaStreamSource source;
            AudioStreamDescriptor? audioDesc = null;
            if (_withAudio)
            {
                audioDesc = new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(48000, 2, 16));
                source = new MediaStreamSource(video, audioDesc) { BufferTime = TimeSpan.Zero, CanSeek = false };
            }
            else
            {
                source = new MediaStreamSource(video) { BufferTime = TimeSpan.Zero, CanSeek = false };
            }

            source.SampleRequested += (_, args) =>
            {
                var deferral = args.Request.GetDeferral();
                try
                {
                    var isAudio = audioDesc is not null && args.Request.StreamDescriptor is AudioStreamDescriptor;
                    if (isAudio)
                    {
                        if (_audio!.TryTake(out var block, Timeout.Infinite))
                        {
                            args.Request.Sample = MediaStreamSample.CreateFromBuffer(block.Pcm.AsBuffer(), block.Time);
                            args.Request.Sample.Duration = block.Duration;
                        }
                        else
                        {
                            args.Request.Sample = null;
                        }
                    }
                    else if (_frames.TryTake(out var frame, Timeout.Infinite))
                    {
                        var sample = MediaStreamSample.CreateFromBuffer(FlipRows(frame.Bgra).AsBuffer(), frame.Time);
                        sample.Duration = _frameDuration;
                        args.Request.Sample = sample;
                        FramesWritten++;
                    }
                    else
                    {
                        args.Request.Sample = null;
                    }
                }
                catch (InvalidOperationException)
                {
                    args.Request.Sample = null;
                }
                finally
                {
                    deferral.Complete();
                }
            };

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Video.Subtype = "H264";
            profile.Video.Width = (uint)_w;
            profile.Video.Height = (uint)_h;
            profile.Video.Bitrate = (uint)_bitrate;
            profile.Video.FrameRate.Numerator = (uint)_fps;
            profile.Video.FrameRate.Denominator = 1;
            profile.Video.PixelAspectRatio.Numerator = 1;
            profile.Video.PixelAspectRatio.Denominator = 1;
            if (_withAudio)
            {
                profile.Audio = AudioEncodingProperties.CreateAac(48000, 2, 160000);
            }
            else
            {
                profile.Audio = null;
            }

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile);
            if (!prepared.CanTranscode)
            {
                throw new InvalidOperationException("Cannot encode: " + prepared.FailureReason);
            }

            var op = prepared.TranscodeAsync();
            op.Progress = (_, p) => Progress = p;
            await op;
        }
        catch (Exception ex)
        {
            Failure = ex;
            _frames.CompleteAdding();
            _audio?.CompleteAdding();
        }
    }
}
