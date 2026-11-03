// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Trayloom.Features.Capture;

/// <summary>Minimal WASAPI shared-mode capture (render loopback or microphone) to a 32-bit float stereo WAV sidecar.</summary>
internal sealed class WasapiCapture : IDisposable
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);

        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, nint format, nint sessionGuid);

        [PreserveSig] int GetBufferSize(out uint frames);

        [PreserveSig] int GetStreamLatency(out long latency);

        [PreserveSig] int GetCurrentPadding(out uint padding);

        [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closest);

        [PreserveSig] int GetMixFormat(out nint format);

        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

        [PreserveSig] int Start();

        [PreserveSig] int Stop();

        [PreserveSig] int Reset();

        [PreserveSig] int SetEventHandle(nint handle);

        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);

        [PreserveSig] int ReleaseBuffer(uint frames);

        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IidCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    private readonly bool _loopback;
    private readonly string _path;
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _paused;
    private WavWriter? _wav;
    private readonly Stopwatch _clock = new();
    private double _pausedSeconds;
    private double _pauseStartedAt;

    private readonly ProcessLoopbackTarget? _target;

    public WasapiCapture(string wavPath, bool loopback, ProcessLoopbackTarget? target = null)
    {
        _path = wavPath;
        _loopback = loopback;
        _target = target;
    }

    public bool Opened { get; private set; }

    /// <summary>endpoint | process-include | process-exclude: how the system sound is being captured.</summary>
    public string Mode { get; private set; } = "endpoint";

    public double Seconds => _wav is null ? 0 : (double)_wav.FramesWritten / 48000;

    /// <summary>Opens the default endpoint and starts the capture thread. Throws when the device is unavailable.</summary>
    public void Start()
    {
        if (_target is not null && Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
        {
            // the process-loopback client is created by an MTA completion callback: keep every call in the MTA
            Task.Run(Start).GetAwaiter().GetResult();
            return;
        }

        IAudioClient client;
        int channels, rate, bits;
        bool isFloat;
        if (_target is { } target)
        {
            // process loopback: GetMixFormat is not supported on this client, the format is ours (48 kHz stereo float)
            client = (IAudioClient)ProcessLoopbackActivator.Activate(target);
            var fmt = Marshal.AllocHGlobal(18);
            try
            {
                Marshal.WriteInt16(fmt, 0, 3);
                Marshal.WriteInt16(fmt, 2, 2);
                Marshal.WriteInt32(fmt, 4, 48000);
                Marshal.WriteInt32(fmt, 8, 48000 * 8);
                Marshal.WriteInt16(fmt, 12, 8);
                Marshal.WriteInt16(fmt, 14, 32);
                Marshal.WriteInt16(fmt, 16, 0);
                // LOOPBACK | EVENTCALLBACK | AUTOCONVERTPCM | SRC_DEFAULT_QUALITY
                Marshal.ThrowExceptionForHR(client.Initialize(0, 0x00020000 | 0x00040000 | unchecked((int)0x80000000) | 0x08000000, 10_000_000 / 5, 0, fmt, 0));
            }
            finally
            {
                Marshal.FreeHGlobal(fmt);
            }

            var evt = CreateEventW(0, false, false, null);
            Marshal.ThrowExceptionForHR(client.SetEventHandle(evt));
            channels = 2;
            rate = 48000;
            bits = 32;
            isFloat = true;
            Mode = target.IncludeTree ? "process-include" : "process-exclude";
        }
        else
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(_loopback ? 0 : 1, _loopback ? 1 : 0, out var device));
            var iid = IidAudioClient;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out var obj));
            client = (IAudioClient)obj;
            Marshal.ThrowExceptionForHR(client.GetMixFormat(out var fmt));
            channels = Marshal.ReadInt16(fmt, 2);
            rate = Marshal.ReadInt32(fmt, 4);
            bits = Marshal.ReadInt16(fmt, 14);
            var tag = Marshal.ReadInt16(fmt, 0);
            isFloat = tag == 3 || (tag == unchecked((short)0xFFFE) && Marshal.ReadInt32(fmt, 24) == 3);
            var flags = _loopback ? 0x00020000 : 0;
            Marshal.ThrowExceptionForHR(client.Initialize(0, flags, 10_000_000 / 5, 0, fmt, 0));
        }

        var capIid = IidCaptureClient;
        Marshal.ThrowExceptionForHR(client.GetService(ref capIid, out var capObj));
        var capture = (IAudioCaptureClient)capObj;
        _wav = new WavWriter(_path, 48000);
        Opened = true;
        _thread = new Thread(() => Loop(client, capture, channels, rate, bits, isFloat)) { IsBackground = true, Name = "Trayloom audio capture", Priority = ThreadPriority.AboveNormal };
        Marshal.ThrowExceptionForHR(client.Start());
        _clock.Start();
        _thread.Start();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateEventW(nint attributes, bool manualReset, bool initialState, string? name);

    public void SetPaused(bool paused)
    {
        if (paused == _paused)
        {
            return;
        }

        if (paused)
        {
            _pauseStartedAt = _clock.Elapsed.TotalSeconds;
        }
        else
        {
            _pausedSeconds += _clock.Elapsed.TotalSeconds - _pauseStartedAt;
        }

        _paused = paused;
    }

    private unsafe void Loop(IAudioClient client, IAudioCaptureClient capture, int channels, int rate, int bits, bool isFloat)
    {
        var scratch = new float[8192];
        double nextPos = 0; // position in source frames (at the device rate) written so far
        try
        {
            while (!_stop)
            {
                Thread.Sleep(8);
                capture.GetNextPacketSize(out var packet);
                while (packet > 0 && !_stop)
                {
                    capture.GetBuffer(out var data, out var frames, out var bufFlags, out _, out _);
                    var silent = (bufFlags & 2) != 0;
                    if (!_paused)
                    {
                        var stereo = new float[frames * 2];
                        if (!silent)
                        {
                            var bytesPer = bits / 8;
                            for (var i = 0; i < frames; i++)
                            {
                                for (var c = 0; c < 2; c++)
                                {
                                    var ch = Math.Min(c, channels - 1);
                                    var p = (byte*)data + (i * channels + ch) * bytesPer;
                                    stereo[i * 2 + c] = isFloat && bits == 32 ? *(float*)p : bits == 16 ? *(short*)p / 32768f : bits == 32 ? *(int*)p / 2147483648f : bits == 24 ? ((p[0] | p[1] << 8 | (sbyte)p[2] << 16) / 8388608f) : 0;
                                }
                            }
                        }

                        _wav!.Write(Resample(stereo, rate));
                        nextPos += frames;
                    }

                    capture.ReleaseBuffer(frames);
                    capture.GetNextPacketSize(out packet);
                }

                if (!_paused)
                {
                    // Loopback delivers nothing while the PC is silent: pad with zeros up to the wall clock.
                    var expected = (_clock.Elapsed.TotalSeconds - _pausedSeconds) * rate;
                    if (expected - nextPos > rate * 0.05)
                    {
                        var missing = (int)(expected - nextPos);
                        _wav!.Write(new float[Resample(new float[missing * 2], rate).Length]);
                        nextPos += missing;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            CaptureHost.Log("audio capture failed", ex);
        }
        finally
        {
            try
            {
                client.Stop();
            }
            catch
            {
                // gone
            }
        }

        _ = scratch;
    }

    private double _resamplePos;

    /// <summary>Linear resample of interleaved stereo floats to 48 kHz.</summary>
    private float[] Resample(float[] stereo, int rate)
    {
        if (rate == 48000)
        {
            return stereo;
        }

        var ratio = (double)rate / 48000;
        var frames = stereo.Length / 2;
        var outFrames = (int)Math.Max(0, Math.Floor((frames - _resamplePos) / ratio));
        var result = new float[outFrames * 2];
        for (var i = 0; i < outFrames; i++)
        {
            var pos = _resamplePos + i * ratio;
            var i0 = (int)Math.Floor(pos);
            var f = (float)(pos - i0);
            var i1 = Math.Min(frames - 1, i0 + 1);
            i0 = Math.Min(frames - 1, i0);
            result[i * 2] = stereo[i0 * 2] * (1 - f) + stereo[i1 * 2] * f;
            result[i * 2 + 1] = stereo[i0 * 2 + 1] * (1 - f) + stereo[i1 * 2 + 1] * f;
        }

        _resamplePos = _resamplePos + outFrames * ratio - frames;
        return result;
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(1500);
        _wav?.Dispose();
        _wav = null;
    }

    public void Dispose() => Stop();
}

/// <summary>Streaming 32-bit float stereo WAV writer (length fields patched on close).</summary>
internal sealed class WavWriter : IDisposable
{
    private readonly FileStream _fs;
    private readonly int _rate;
    private long _dataBytes;

    public WavWriter(string path, int rate)
    {
        _rate = rate;
        _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        WriteHeader();
    }

    public long FramesWritten => _dataBytes / 8;

    private void WriteHeader()
    {
        using var bw = new BinaryWriter(_fs, System.Text.Encoding.ASCII, true);
        bw.Write("RIFF"u8.ToArray());
        bw.Write((int)(36 + _dataBytes));
        bw.Write("WAVEfmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)3);
        bw.Write((short)2);
        bw.Write(_rate);
        bw.Write(_rate * 8);
        bw.Write((short)8);
        bw.Write((short)32);
        bw.Write("data"u8.ToArray());
        bw.Write((int)_dataBytes);
    }

    public void Write(float[] interleaved)
    {
        var bytes = new byte[interleaved.Length * 4];
        Buffer.BlockCopy(interleaved, 0, bytes, 0, bytes.Length);
        _fs.Write(bytes, 0, bytes.Length);
        _dataBytes += bytes.Length;
    }

    public void Dispose()
    {
        _fs.Seek(0, SeekOrigin.Begin);
        WriteHeader();
        _fs.Dispose();
    }

    /// <summary>Reads a float WAV written by this class into memory (interleaved stereo).</summary>
    public static float[] ReadAll(string path)
    {
        using var fs = File.OpenRead(path);
        fs.Seek(44, SeekOrigin.Begin);
        var bytes = new byte[Math.Max(0, fs.Length - 44)];
        fs.ReadExactly(bytes, 0, bytes.Length);
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * 4);
        return floats;
    }
}
