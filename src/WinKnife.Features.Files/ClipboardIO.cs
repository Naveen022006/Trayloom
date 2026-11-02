// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WinKnife.Features.WindowTools;

/// <summary>Registered and standard clipboard formats used by the clipboard features.</summary>
internal static class ClipFormats
{
    public const uint Text = 1;
    public const uint Bitmap = 2;
    public const uint MetafilePict = 3;
    public const uint OemText = 7;
    public const uint Dib = 8;
    public const uint Palette = 9;
    public const uint HDrop = 15;
    public const uint UnicodeText = 13;
    public const uint EnhMetafile = 14;
    public const uint DibV5 = 17;

    /// <summary>Marker added to everything WinKnife writes itself so the clipboard features can ignore it.</summary>
    public static readonly uint OwnWrite = WinApi.RegisterClipboardFormatW("WinKnife.OwnWrite");

    /// <summary>Password managers set this to keep content out of history and cloud sync.</summary>
    public static readonly uint ExcludeFromMonitor = WinApi.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");

    /// <summary>DWORD 0 = keep this out of Windows clipboard history.</summary>
    public static readonly uint CanIncludeInHistory = WinApi.RegisterClipboardFormatW("CanIncludeInClipboardHistory");

    public static readonly uint Png = WinApi.RegisterClipboardFormatW("PNG");
    public static readonly uint Html = WinApi.RegisterClipboardFormatW("HTML Format");
    public static readonly uint FileGroupDescriptorW = WinApi.RegisterClipboardFormatW("FileGroupDescriptorW");
    public static readonly uint FileGroupDescriptor = WinApi.RegisterClipboardFormatW("FileGroupDescriptor");
    public static readonly uint FileContents = WinApi.RegisterClipboardFormatW("FileContents");
    public static readonly uint ShellIdList = WinApi.RegisterClipboardFormatW("Shell IDList Array");
}

/// <summary>Low level clipboard access with retries (the clipboard is a shared, briefly locked resource).</summary>
internal static class ClipboardIO
{
    public const int MaxSnapshotBytes = 64 * 1024 * 1024;

    public static bool IsAvailable(uint format) => WinApi.IsClipboardFormatAvailable(format);

    /// <summary>True when the clipboard content was written by WinKnife.</summary>
    public static bool IsOwnWrite() => IsAvailable(ClipFormats.OwnWrite);

    /// <summary>Opens the clipboard, retrying briefly while another process holds it.</summary>
    public static bool Open(int attempts = 12)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (WinApi.OpenClipboard(0))
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    /// <summary>
    /// True when the content asks to stay out of history: ExcludeClipboardContentFromMonitorProcessing present, or
    /// CanIncludeInClipboardHistory holding 0.
    /// </summary>
    public static bool IsSensitive()
    {
        if (IsAvailable(ClipFormats.ExcludeFromMonitor))
        {
            return true;
        }

        if (!IsAvailable(ClipFormats.CanIncludeInHistory))
        {
            return false;
        }

        var bytes = ReadFormat(ClipFormats.CanIncludeInHistory);
        return bytes is { Length: >= 4 } && BitConverter.ToUInt32(bytes, 0) == 0;
    }

    /// <summary>Reads Unicode text from the clipboard, or null.</summary>
    public static string? ReadText()
    {
        if (!IsAvailable(ClipFormats.UnicodeText))
        {
            return null;
        }

        if (!Open())
        {
            return null;
        }

        try
        {
            var handle = WinApi.GetClipboardData(ClipFormats.UnicodeText);
            if (handle == 0)
            {
                return null;
            }

            var size = (int)Math.Min(WinApi.GlobalSize(handle), int.MaxValue);
            var pointer = WinApi.GlobalLock(handle);
            if (pointer == 0)
            {
                return null;
            }

            try
            {
                var chars = size / 2;
                var text = Marshal.PtrToStringUni(pointer, chars);
                var end = text.IndexOf('\0');
                return end >= 0 ? text[..end] : text;
            }
            finally
            {
                WinApi.GlobalUnlock(handle);
            }
        }
        finally
        {
            WinApi.CloseClipboard();
        }
    }

    /// <summary>Reads the raw bytes of an HGLOBAL based format, or null.</summary>
    public static byte[]? ReadFormat(uint format)
    {
        if (!IsAvailable(format) || !Open())
        {
            return null;
        }

        try
        {
            return CopyFormat(format);
        }
        finally
        {
            WinApi.CloseClipboard();
        }
    }

    private static byte[]? CopyFormat(uint format)
    {
        var handle = WinApi.GetClipboardData(format);
        if (handle == 0)
        {
            return null;
        }

        var size = WinApi.GlobalSize(handle);
        if (size == 0 || size > MaxSnapshotBytes)
        {
            return null;
        }

        var pointer = WinApi.GlobalLock(handle);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            WinApi.GlobalUnlock(handle);
        }
    }

    /// <summary>Process name of the clipboard owner (empty when unknown).</summary>
    public static string OwnerProcessName()
    {
        try
        {
            var owner = WinApi.GetClipboardOwner();
            if (owner == 0)
            {
                return string.Empty;
            }

            WinKnife.Core.Native.NativeMethods.GetWindowThreadProcessId(owner, out var pid);
            if (pid == 0)
            {
                return string.Empty;
            }

            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Replaces the clipboard with plain text. <paramref name="excludeFromHistory"/> hides it from Windows history too.</summary>
    public static bool WriteText(string text, bool excludeFromHistory = false, bool markOwn = true)
    {
        if (!Open())
        {
            return false;
        }

        try
        {
            WinApi.EmptyClipboard();
            var ok = SetBytes(ClipFormats.UnicodeText, Encoding.Unicode.GetBytes(text + "\0"));
            if (markOwn)
            {
                SetBytes(ClipFormats.OwnWrite, [1]);
            }

            if (excludeFromHistory)
            {
                SetBytes(ClipFormats.ExcludeFromMonitor, [1, 0, 0, 0]);
            }

            return ok;
        }
        finally
        {
            WinApi.CloseClipboard();
        }
    }

    /// <summary>Replaces the clipboard with an image given as PNG bytes (PNG + DIB for broad compatibility).</summary>
    public static bool WriteImage(byte[] png)
    {
        byte[]? dib = null;
        try
        {
            using var stream = new MemoryStream(png);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            // CF_DIB (BITMAPINFOHEADER, bottom-up 32 bit) - alpha rides in the 4th byte.
            dib = new byte[40 + pixels.Length];
            BitConverter.GetBytes(40).CopyTo(dib, 0);
            BitConverter.GetBytes(width).CopyTo(dib, 4);
            BitConverter.GetBytes(height).CopyTo(dib, 8);
            BitConverter.GetBytes((short)1).CopyTo(dib, 12);
            BitConverter.GetBytes((short)32).CopyTo(dib, 14);
            BitConverter.GetBytes(pixels.Length).CopyTo(dib, 20);
            for (var y = 0; y < height; y++)
            {
                Buffer.BlockCopy(pixels, y * stride, dib, 40 + (height - 1 - y) * stride, stride);
            }
        }
        catch (Exception)
        {
            dib = null;
        }

        if (!Open())
        {
            return false;
        }

        try
        {
            WinApi.EmptyClipboard();
            var ok = SetBytes(ClipFormats.Png, png);
            if (dib is not null)
            {
                ok |= SetBytes(ClipFormats.Dib, dib);
            }

            SetBytes(ClipFormats.OwnWrite, [1]);
            return ok;
        }
        finally
        {
            WinApi.CloseClipboard();
        }
    }

    /// <summary>Writes a copy of <paramref name="data"/> as a clipboard format. The clipboard must already be open.</summary>
    public static bool SetBytes(uint format, byte[] data)
    {
        var handle = WinApi.GlobalAlloc(WinApi.GMEM_MOVEABLE, (nuint)Math.Max(1, data.Length));
        if (handle == 0)
        {
            return false;
        }

        var pointer = WinApi.GlobalLock(handle);
        if (pointer == 0)
        {
            WinApi.GlobalFree(handle);
            return false;
        }

        Marshal.Copy(data, 0, pointer, data.Length);
        WinApi.GlobalUnlock(handle);
        if (WinApi.SetClipboardData(format, handle) == 0)
        {
            WinApi.GlobalFree(handle);
            return false;
        }

        // The system owns the memory now.
        return true;
    }

    /// <summary>Reads an image from the clipboard and returns it as PNG bytes (preferring the lossless alpha PNG format).</summary>
    public static byte[]? ReadImagePng()
    {
        var png = ReadFormat(ClipFormats.Png);
        if (png is { Length: > 8 })
        {
            return png;
        }

        if (!IsAvailable(ClipFormats.DibV5) && !IsAvailable(ClipFormats.Dib) && !IsAvailable(ClipFormats.Bitmap))
        {
            return null;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var image = Clipboard.GetImage();
                if (image is null)
                {
                    return null;
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }
            catch (COMException)
            {
                Thread.Sleep(20);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }
}

/// <summary>An in-memory copy of every raw (HGLOBAL) clipboard format, used to restore the clipboard after a temporary change.</summary>
internal sealed class ClipboardSnapshot
{
    private readonly List<(uint Format, byte[] Data)> _items = [];

    public bool IsEmpty => _items.Count == 0;

    public bool HasFileData { get; private set; }

    private static bool IsSkipped(uint format) =>
        format is ClipFormats.Bitmap or ClipFormats.MetafilePict or ClipFormats.Palette or ClipFormats.EnhMetafile
        || (format >= 0x80 && format <= 0x8F)
        || (format >= 0x200 && format <= 0x3FF);

    public static ClipboardSnapshot Capture()
    {
        var snapshot = new ClipboardSnapshot();
        if (!ClipboardIO.Open())
        {
            return snapshot;
        }

        try
        {
            long total = 0;
            for (var format = WinApi.EnumClipboardFormats(0); format != 0; format = WinApi.EnumClipboardFormats(format))
            {
                if (format == ClipFormats.FileContents || format == ClipFormats.FileGroupDescriptorW
                    || format == ClipFormats.FileGroupDescriptor)
                {
                    snapshot.HasFileData = true;
                }

                if (IsSkipped(format))
                {
                    continue;
                }

                var handle = WinApi.GetClipboardData(format);
                if (handle == 0)
                {
                    continue;
                }

                var size = WinApi.GlobalSize(handle);
                if (size == 0 || total + (long)size > ClipboardIO.MaxSnapshotBytes)
                {
                    continue;
                }

                var pointer = WinApi.GlobalLock(handle);
                if (pointer == 0)
                {
                    continue;
                }

                try
                {
                    var bytes = new byte[(int)size];
                    Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    snapshot._items.Add((format, bytes));
                    total += bytes.Length;
                }
                finally
                {
                    WinApi.GlobalUnlock(handle);
                }
            }
        }
        finally
        {
            WinApi.CloseClipboard();
        }

        return snapshot;
    }

    /// <summary>Puts the captured formats back (tagged as a WinKnife write so history ignores it).</summary>
    public bool Restore()
    {
        if (_items.Count == 0 || !ClipboardIO.Open())
        {
            return false;
        }

        try
        {
            WinApi.EmptyClipboard();
            foreach (var (format, data) in _items)
            {
                ClipboardIO.SetBytes(format, data);
            }

            ClipboardIO.SetBytes(ClipFormats.OwnWrite, [1]);
            return true;
        }
        finally
        {
            WinApi.CloseClipboard();
        }
    }
}

/// <summary>
/// One shared AddClipboardFormatListener window. Subscribe while a feature runs; the window is created lazily and
/// destroyed with the last subscriber. Must be used on the UI thread.
/// </summary>
internal static class ClipboardMonitor
{
    private static HwndSource? _source;
    private static event Action? Changed;
    private static int _subscribers;

    private sealed class Subscription(Action handler) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Changed -= handler;
            if (--_subscribers <= 0)
            {
                _subscribers = 0;
                Shutdown();
            }
        }
    }

    public static IDisposable Subscribe(Action handler)
    {
        if (_source is null)
        {
            var parameters = new HwndSourceParameters("WinKnife.ClipboardMonitor")
            {
                ParentWindow = WinKnife.Core.Native.NativeConstants.HWND_MESSAGE,
                WindowStyle = 0,
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            WinApi.AddClipboardFormatListener(_source.Handle);
        }

        _subscribers++;
        Changed += handler;
        return new Subscription(handler);
    }

    private static void Shutdown()
    {
        if (_source is null)
        {
            return;
        }

        WinApi.RemoveClipboardFormatListener(_source.Handle);
        _source.Dispose();
        _source = null;
    }

    private static nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WinApi.WM_CLIPBOARDUPDATE)
        {
            handled = true;
            foreach (var handler in Changed?.GetInvocationList() ?? [])
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception)
                {
                    // one subscriber failing must not stop the others
                }
            }
        }

        return 0;
    }
}
