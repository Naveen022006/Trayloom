// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>Reads the system pointer image (colour and monochrome cursors) as straight-alpha BGRA, keyed by a content hash.</summary>
internal static class CursorCatalog
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public short bmPlanes, bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("gdi32.dll")] private static extern int GetObjectW(nint h, int cb, out BITMAP bmp);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(nint icon, out N.ICONINFO info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(nint hdc, int x, int y, nint icon, int w, int h, int step, nint brush, uint flags);

    /// <summary>Straight-alpha BGRA pixels of the cursor plus its size and hot spot, or null when it cannot be read.</summary>
    public static (byte[] Bgra, int Width, int Height, int HotX, int HotY)? Capture(nint cursor)
    {
        if (cursor == 0 || !GetIconInfo(cursor, out var info))
        {
            return null;
        }

        try
        {
            var probe = info.hbmColor != 0 ? info.hbmColor : info.hbmMask;
            if (probe == 0 || GetObjectW(probe, Marshal.SizeOf<BITMAP>(), out var bmp) == 0)
            {
                return null;
            }

            var w = bmp.bmWidth;
            var h = info.hbmColor != 0 ? bmp.bmHeight : bmp.bmHeight / 2;
            if (w <= 0 || h <= 0 || w > 512 || h > 512)
            {
                return null;
            }

            var onBlack = Render(cursor, w, h, 0x00);
            var onWhite = Render(cursor, w, h, 0xFF);
            if (onBlack is null || onWhite is null)
            {
                return null;
            }

            var result = new byte[w * h * 4];
            for (var i = 0; i < w * h; i++)
            {
                var o = i * 4;
                int b0 = onBlack[o], g0 = onBlack[o + 1], r0 = onBlack[o + 2];
                int b1 = onWhite[o], g1 = onWhite[o + 1], r1 = onWhite[o + 2];
                var diff = (r1 - r0 + g1 - g0 + b1 - b0) / 3;
                int a;
                if (diff < -128)
                {
                    // an inverting pixel (XOR cursor): show it as an opaque dark mark so it stays visible
                    result[o] = result[o + 1] = result[o + 2] = 0;
                    result[o + 3] = 255;
                    continue;
                }

                a = Math.Clamp(255 - diff, 0, 255);
                if (a == 0)
                {
                    continue;
                }

                result[o] = (byte)Math.Min(255, b0 * 255 / a);
                result[o + 1] = (byte)Math.Min(255, g0 * 255 / a);
                result[o + 2] = (byte)Math.Min(255, r0 * 255 / a);
                result[o + 3] = (byte)a;
            }

            return (result, w, h, info.xHotspot, info.yHotspot);
        }
        finally
        {
            if (info.hbmColor != 0)
            {
                DeleteObject(info.hbmColor);
            }

            if (info.hbmMask != 0)
            {
                DeleteObject(info.hbmMask);
            }
        }
    }

    private static byte[]? Render(nint cursor, int w, int h, byte background)
    {
        var bmi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var dc = CreateCompatibleDC(0);
        var dib = CreateDIBSection(dc, ref bmi, 0, out var bits, 0, 0);
        if (dib == 0 || bits == 0)
        {
            DeleteDC(dc);
            return null;
        }

        var old = SelectObject(dc, dib);
        try
        {
            var bytes = new byte[w * h * 4];
            Array.Fill(bytes, background);
            Marshal.Copy(bytes, 0, bits, bytes.Length);
            DrawIconEx(dc, 0, 0, cursor, 0, 0, 0, 0, 3);
            Marshal.Copy(bits, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(dib);
            DeleteDC(dc);
        }
    }

    public static ulong Hash(byte[] bgra, int w, int h, int hotX, int hotY)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in bgra)
        {
            hash = (hash ^ b) * 1099511628211UL;
        }

        foreach (var v in new[] { w, h, hotX, hotY })
        {
            hash = (hash ^ (uint)v) * 1099511628211UL;
        }

        return hash;
    }

    public static byte[] EncodePng(byte[] bgra, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>
/// Records the pointer (125 Hz position / shape / visibility), the mouse buttons (shared low-level hook) and the typing times
/// (key-down transitions only, never the key) as tracks on the recording's clock. Nothing is synthesised: it only reads.
/// </summary>
internal sealed class PointerRecorder : IDisposable
{
    private readonly Func<long, double?> _eventTime;
    private readonly System.Windows.Int32Rect _region;
    private readonly ConcurrentQueue<(long Qpc, bool Down)> _clickQueue = new();
    private readonly ConcurrentQueue<long> _typingQueue = new();
    private readonly bool[] _down = new bool[256];
    private readonly PointerTrack _track = new();
    private readonly List<double> _typing = new();
    private readonly Dictionary<ulong, int> _shapeByHash = new();
    private IDisposable? _mouseSub;
    private IDisposable? _keySub;
    private Thread? _thread;
    private volatile bool _stop;
    private nint _lastCursor;
    private int _lastShape;
    private long _lastShapeCheck;
    private static long _freq;

    /// <param name="hooks">Shared hooks (may be null in tests; clicks and typing are then only recorded via <see cref="RecordClick"/>).</param>
    /// <param name="region">The recorded rectangle in virtual-screen pixels.</param>
    /// <param name="eventTime">Maps a QPC instant to recording seconds, or null before the first frame / inside a pause.</param>
    public PointerRecorder(IInputHooks? hooks, System.Windows.Int32Rect region, Func<long, double?> eventTime)
    {
        N.QueryPerformanceFrequency(out _freq);
        _region = region;
        _eventTime = eventTime;
        if (hooks is not null)
        {
            try
            {
                _mouseSub = hooks.SubscribeMouse(OnMouse);
            }
            catch (Exception ex)
            {
                CaptureHost.Log("pointer: mouse hook unavailable", ex);
            }

            try
            {
                _keySub = hooks.SubscribeKeyboard(OnKey);
            }
            catch (Exception ex)
            {
                CaptureHost.Log("pointer: keyboard hook unavailable", ex);
            }
        }
    }

    public int SampleCount => _track.Samples.Count;

    public PointerTrack Track => _track;

    public IReadOnlyList<double> TypingTimes => _typing;

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsFromWinKnife)
        {
            return false;
        }

        switch (e.Kind)
        {
            case MouseEventKind.LeftDown or MouseEventKind.RightDown:
                _clickQueue.Enqueue((Stopwatch.GetTimestamp(), true));
                break;
            case MouseEventKind.LeftUp or MouseEventKind.RightUp:
                _clickQueue.Enqueue((Stopwatch.GetTimestamp(), false));
                break;
        }

        return false;
    }

    private bool OnKey(KeyboardHookEvent e)
    {
        if (e.IsFromWinKnife)
        {
            return false;
        }

        var vk = e.VkCode & 0xFF;
        if (e.IsKeyDown)
        {
            if (!_down[vk])
            {
                _down[vk] = true;
                _typingQueue.Enqueue(Stopwatch.GetTimestamp());
            }
        }
        else
        {
            _down[vk] = false;
        }

        return false;
    }

    /// <summary>Test / fallback entry: records a button transition at the given QPC instant.</summary>
    public void RecordClick(long qpc, bool down) => _clickQueue.Enqueue((qpc, down));

    public void RecordTyping(long qpc) => _typingQueue.Enqueue(qpc);

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "WinKnife pointer sampler", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    private void Loop()
    {
        timeBeginPeriod(1);
        try
        {
            var next = Stopwatch.GetTimestamp();
            var period = Stopwatch.Frequency / 125;
            while (!_stop)
            {
                Tick();
                next += period;
                var wait = next - Stopwatch.GetTimestamp();
                if (wait < -period * 4)
                {
                    next = Stopwatch.GetTimestamp();
                    continue;
                }

                while (!_stop && (wait = next - Stopwatch.GetTimestamp()) > 0)
                {
                    if (wait > Stopwatch.Frequency / 800)
                    {
                        Thread.Sleep(1);
                    }
                    else
                    {
                        Thread.SpinWait(50);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            CaptureHost.Log("pointer sampler failed", ex);
        }
        finally
        {
            timeEndPeriod(1);
        }
    }

    private void Tick()
    {
        DrainQueues();
        var now = Stopwatch.GetTimestamp();
        var t = _eventTime(now);
        if (t is null)
        {
            return;
        }

        var info = new N.CURSORINFO { cbSize = Marshal.SizeOf<N.CURSORINFO>() };
        if (!N.GetCursorInfo(ref info))
        {
            return;
        }

        var visible = (info.flags & 1) != 0 && (info.flags & 2) == 0;
        var x = (info.ptScreenPos.X - _region.X) / (double)Math.Max(1, _region.Width);
        var y = (info.ptScreenPos.Y - _region.Y) / (double)Math.Max(1, _region.Height);
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return;
        }

        if (info.hCursor != 0 && (info.hCursor != _lastCursor || _track.Shapes.Count == 0 || now - _lastShapeCheck > Stopwatch.Frequency))
        {
            _lastShapeCheck = now;
            _lastCursor = info.hCursor;
            _lastShape = ShapeIndexFor(info.hCursor, _lastShape);
        }

        _track.Samples.Add(new PointerSample(t.Value, x, y, _lastShape, visible));
    }

    private int ShapeIndexFor(nint cursor, int fallback)
    {
        var cap = CursorCatalog.Capture(cursor);
        if (cap is not { } c)
        {
            return fallback;
        }

        var hash = CursorCatalog.Hash(c.Bgra, c.Width, c.Height, c.HotX, c.HotY);
        if (_shapeByHash.TryGetValue(hash, out var existing))
        {
            return existing;
        }

        if (_track.Shapes.Count >= 60)
        {
            return fallback;
        }

        _track.Shapes.Add(new CursorShape { HotX = c.HotX, HotY = c.HotY, Width = c.Width, Height = c.Height, Png = CursorCatalog.EncodePng(c.Bgra, c.Width, c.Height) });
        var index = _track.Shapes.Count - 1;
        _shapeByHash[hash] = index;
        return index;
    }

    private void DrainQueues()
    {
        while (_clickQueue.TryDequeue(out var c))
        {
            if (_eventTime(c.Qpc) is { } t)
            {
                _track.Clicks.Add(new PointerClick(t, c.Down));
            }
        }

        while (_typingQueue.TryDequeue(out var q))
        {
            if (_eventTime(q) is { } t)
            {
                _typing.Add(t);
            }
        }
    }

    /// <summary>Stops sampling (waits for the thread) and releases the hooks.</summary>
    public void Stop()
    {
        _stop = true;
        _mouseSub?.Dispose();
        _keySub?.Dispose();
        _mouseSub = _keySub = null;
        _thread?.Join(1500);
        DrainQueues();
    }

    /// <summary>Writes pointer.bin (when it holds samples) and typing.json (when it holds times) into the take folder.</summary>
    public void Save(string folder)
    {
        if (_track.Samples.Count > 0)
        {
            File.WriteAllBytes(Path.Combine(folder, "pointer.bin"), _track.Encode());
        }

        if (_typing.Count > 0)
        {
            TypingTrack.Save(Path.Combine(folder, "typing.json"), _typing);
        }
    }

    public void Dispose() => Stop();
}
