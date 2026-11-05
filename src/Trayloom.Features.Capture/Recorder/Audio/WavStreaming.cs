// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Trayloom.Features.Capture;

/// <summary>
/// Reads a 32-bit float stereo WAV (as <see cref="WavWriter"/> writes it) block by block, so a long recording never has to fit in
/// memory. The data length is taken from the file when the header was never patched (a recording that was killed).
/// </summary>
internal sealed class WavReader : IDisposable
{
    private readonly FileStream _fs;
    private readonly long _dataStart;
    private long _position;

    public WavReader(string path)
    {
        _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        try
        {
            var header = new byte[12];
            _fs.ReadExactly(header, 0, 12);
            if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            {
                throw new InvalidDataException("Not a WAV file");
            }

            var chunk = new byte[8];
            var formatOk = false;
            long dataLength = -1;
            while (_fs.Position + 8 <= _fs.Length)
            {
                _fs.ReadExactly(chunk, 0, 8);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
                if (chunk.AsSpan(0, 4).SequenceEqual("fmt "u8))
                {
                    var fmt = new byte[Math.Min(size, 40u)];
                    _fs.ReadExactly(fmt, 0, fmt.Length);
                    var tag = BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(0));
                    var channels = BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(2));
                    var bits = BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(14));
                    formatOk = tag == 3 && channels == 2 && bits == 32;
                    _fs.Position += size - fmt.Length + (size & 1);
                }
                else if (chunk.AsSpan(0, 4).SequenceEqual("data"u8))
                {
                    _dataStart = _fs.Position;
                    var remaining = _fs.Length - _dataStart;
                    dataLength = size == 0 || size > remaining ? remaining : size;
                    break;
                }
                else
                {
                    _fs.Position += size + (size & 1);
                }
            }

            if (!formatOk || dataLength < 0)
            {
                throw new InvalidDataException("Unsupported WAV layout");
            }

            Frames = dataLength / 8;
            _fs.Position = _dataStart;
        }
        catch
        {
            _fs.Dispose();
            throw;
        }
    }

    /// <summary>Number of stereo frames (48 kHz) in the file.</summary>
    public long Frames { get; }

    /// <summary>Moves the read position to a frame (clamped to the file).</summary>
    public void Seek(long frame)
    {
        _position = Math.Clamp(frame, 0, Frames);
        _fs.Position = _dataStart + _position * 8;
    }

    /// <summary>Reads interleaved stereo floats into <paramref name="destination"/>; returns the number of floats read (always even, 0 at the end).</summary>
    public int Read(Span<float> destination)
    {
        var want = (int)Math.Min(destination.Length / 2, Frames - _position);
        if (want <= 0)
        {
            return 0;
        }

        var bytes = MemoryMarshal.AsBytes(destination[..(want * 2)]);
        var total = 0;
        while (total < bytes.Length)
        {
            var n = _fs.Read(bytes[total..]);
            if (n <= 0)
            {
                break;
            }

            total += n;
        }

        var frames = total / 8;
        _position += frames;
        return frames * 2;
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>Waveform bars for the editor, computed from the file in blocks.</summary>
internal static class WaveformPeaks
{
    private const int BlockFrames = 4096;
    private const int SampledBlocks = 48;

    /// <summary>
    /// The loudest sample of each of <paramref name="buckets"/> equal slices (0..1). Short slices are read completely; long ones are
    /// sampled with evenly spaced blocks so a long recording opens quickly. An unreadable file gives an empty array.
    /// </summary>
    public static float[] Compute(string wav, int buckets)
    {
        try
        {
            if (buckets <= 0)
            {
                return Array.Empty<float>();
            }

            using var reader = new WavReader(wav);
            var frames = reader.Frames;
            var result = new float[buckets];
            var block = new float[BlockFrames * 2];
            for (var b = 0; b < buckets; b++)
            {
                var from = b * frames / buckets;
                var to = (b + 1) * frames / buckets;
                float max = 0;
                if (to - from <= (long)BlockFrames * SampledBlocks)
                {
                    max = ScanRange(reader, from, to, block);
                }
                else
                {
                    var span = (to - from) / SampledBlocks;
                    for (var k = 0; k < SampledBlocks; k++)
                    {
                        var start = from + k * span;
                        max = Math.Max(max, ScanRange(reader, start, Math.Min(to, start + BlockFrames), block));
                    }
                }

                result[b] = Math.Min(1, max);
            }

            return result;
        }
        catch
        {
            return Array.Empty<float>();
        }
    }

    private static float ScanRange(WavReader reader, long from, long to, float[] block)
    {
        float max = 0;
        reader.Seek(from);
        var left = to - from;
        while (left > 0)
        {
            var span = block.AsSpan(0, (int)Math.Min(block.Length, left * 2));
            var n = reader.Read(span);
            if (n <= 0)
            {
                break;
            }

            for (var i = 0; i < n; i++)
            {
                var v = Math.Abs(span[i]);
                if (v > max)
                {
                    max = v;
                }
            }

            left -= n / 2;
        }

        return max;
    }
}

/// <summary>Block-wise mixing of the take's audio sidecars over the kept ranges; nothing here holds a whole recording in memory.</summary>
internal static class AudioMix
{
    /// <summary>Output frames handed to the sink at a time (64K frames, 512 KB of floats).</summary>
    public const int BlockFrames = 65536;

    /// <summary>The longest edited audio that is time-stretched; the stretch needs the whole mix in memory (about 100 MB for 5 minutes).</summary>
    public const double MaxStretchSeconds = 600;

    /// <summary>The kept sidecars as (path, gain), in mixing order: system sound, then the microphone.</summary>
    public static List<(string Path, double Gain)> Tracks(Take take, EditDocument doc)
    {
        var tracks = new List<(string, double)>();
        if (take.HasSystemAudio && doc.KeepsSystemAudio)
        {
            tracks.Add((take.SystemWav, doc.SystemAudioGain));
        }

        if (take.HasMicrophone && doc.KeepsMicrophone)
        {
            tracks.Add((take.MicWav, doc.MicrophoneGain));
        }

        return tracks;
    }

    /// <summary>Frames of the mixed output (kept ranges at 1x).</summary>
    public static long TotalFrames(Take take, EditDocument doc) => (long)Math.Round(doc.KeptRanges(take.Duration).Sum(r => r.End - r.Start) * 48000);

    /// <summary>
    /// Mixes the kept ranges of the given tracks and passes interleaved stereo floats to <paramref name="sink"/> in blocks of at most
    /// <paramref name="blockFrames"/> frames. The result is the same as summing the whole files, sample for sample.
    /// </summary>
    public static void MixBlocks(IReadOnlyList<(string Path, double Gain)> tracks, IReadOnlyList<(double Start, double End)> ranges, Action<ReadOnlySpan<float>> sink, CancellationToken ct = default, int blockFrames = BlockFrames)
    {
        var total = (long)Math.Round(ranges.Sum(r => r.End - r.Start) * 48000);
        var readers = new List<WavReader>();
        try
        {
            foreach (var (path, _) in tracks)
            {
                readers.Add(new WavReader(path));
            }

            var mix = new float[blockFrames * 2];
            var scratch = new float[blockFrames * 2];
            var filled = 0; // frames in mix
            long pos = 0;
            foreach (var (a, b) in ranges)
            {
                var frames = (long)Math.Round((b - a) * 48000);
                var from = (long)Math.Round(a * 48000);
                long i = 0;
                while (i < frames && pos + i < total)
                {
                    ct.ThrowIfCancellationRequested();
                    var n = (int)Math.Min(Math.Min(frames - i, total - (pos + i)), blockFrames - filled);
                    for (var t = 0; t < readers.Count; t++)
                    {
                        var reader = readers[t];
                        var gain = (float)tracks[t].Gain;
                        var src = from + i;
                        // a frame is used while the next one still exists (the long-standing rule of the in-memory mixer)
                        var usable = (int)Math.Max(0, Math.Min(n, reader.Frames - 1 - src));
                        if (usable <= 0)
                        {
                            continue;
                        }

                        reader.Seek(src);
                        var read = reader.Read(scratch.AsSpan(0, usable * 2)) / 2;
                        for (var k = 0; k < read * 2; k++)
                        {
                            mix[filled * 2 + k] += scratch[k] * gain;
                        }
                    }

                    filled += n;
                    i += n;
                    if (filled == blockFrames)
                    {
                        sink(mix.AsSpan(0, filled * 2));
                        Array.Clear(mix, 0, mix.Length);
                        filled = 0;
                    }
                }

                pos += frames;
            }

            if (filled > 0)
            {
                sink(mix.AsSpan(0, filled * 2));
            }
        }
        finally
        {
            foreach (var r in readers)
            {
                r.Dispose();
            }
        }
    }

    /// <summary>Writes the 1x mix as a 16-bit stereo 48 kHz WAV file, streaming. False (no file) when the take has no audio to keep.</summary>
    public static bool MixToPcmWav(Take take, EditDocument doc, string path, CancellationToken ct = default)
    {
        var tracks = Tracks(take, doc);
        if (tracks.Count == 0)
        {
            return false;
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        WritePcmHeader(fs, 0);
        long bytes = 0;
        var pcm = new byte[BlockFrames * 4];
        MixBlocks(tracks, doc.KeptRanges(take.Duration), block =>
        {
            ToPcm16(block, pcm);
            fs.Write(pcm, 0, block.Length * 2);
            bytes += block.Length * 2;
        }, ct);
        fs.Position = 0;
        WritePcmHeader(fs, bytes);
        return true;
    }

    /// <summary>Applies the export speed to the 1x mix file with the WSOLA stretch (in memory, so capped) and rewrites it.</summary>
    public static void StretchPcmWav(string path, double speed)
    {
        var floats = new List<float>();
        using (var fs = File.OpenRead(path))
        {
            fs.Position = 44;
            var buf = new byte[1 << 16];
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0)
            {
                n -= n % 2;
                for (var i = 0; i < n; i += 2)
                {
                    floats.Add(BitConverter.ToInt16(buf, i) / 32767f);
                }
            }
        }

        var stretched = TimeStretch.Stretch(floats.ToArray(), speed);
        floats = null;
        var pcm = new byte[stretched.Length * 2];
        ToPcm16(stretched, pcm);
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        WritePcmHeader(output, pcm.Length);
        output.Write(pcm, 0, pcm.Length);
    }

    private static void ToPcm16(ReadOnlySpan<float> block, byte[] pcm)
    {
        for (var i = 0; i < block.Length; i++)
        {
            var v = (short)Math.Clamp(block[i] * 32767f, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)(v >> 8);
        }
    }

    private static void WritePcmHeader(Stream fs, long dataBytes)
    {
        using var bw = new BinaryWriter(fs, System.Text.Encoding.ASCII, true);
        bw.Write("RIFF"u8.ToArray());
        bw.Write((int)(36 + dataBytes));
        bw.Write("WAVEfmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)2);
        bw.Write(48000);
        bw.Write(48000 * 4);
        bw.Write((short)4);
        bw.Write((short)16);
        bw.Write("data"u8.ToArray());
        bw.Write((int)dataBytes);
    }
}
