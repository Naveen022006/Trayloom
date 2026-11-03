// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Trayloom.Features.Files;

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

    /// <summary>Marker added to everything Trayloom writes itself so the clipboard features can ignore it.</summary>
    public static readonly uint OwnWrite = WinApi.RegisterClipboardFormatW("Trayloom.OwnWrite");

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
    public static readonly uint PreferredDropEffect = WinApi.RegisterClipboardFormatW("Preferred DropEffect");
    public static readonly uint CanUploadToCloud = WinApi.RegisterClipboardFormatW("CanUploadToCloudClipboard");
    public static readonly uint ViewerIgnore = WinApi.RegisterClipboardFormatW("Clipboard Viewer Ignore");
    public static readonly uint Rtf = WinApi.RegisterClipboardFormatW("Rich Text Format");
    public static readonly uint UrlW = WinApi.RegisterClipboardFormatW("UniformResourceLocatorW");
    public static readonly uint UrlA = WinApi.RegisterClipboardFormatW("UniformResourceLocator");
}

/// <summary>Low level clipboard access with retries (the clipboard is a shared, briefly locked resource).</summary>
internal static class ClipboardIO
{
    public const int MaxSnapshotBytes = 64 * 1024 * 1024;

    public static bool IsAvailable(uint format) => WinApi.IsClipboardFormatAvailable(format);

    /// <summary>True when the clipboard content was written by Trayloom.</summary>
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
        if (IsAvailable(ClipFormats.ExcludeFromMonitor) || IsAvailable(ClipFormats.ViewerIgnore))
        {
            return true;
        }

        foreach (var format in new[] { ClipFormats.CanIncludeInHistory, ClipFormats.CanUploadToCloud })
        {
            if (!IsAvailable(format))
            {
                continue;
            }

            var bytes = ReadFormat(format);
            if (bytes is { Length: >= 4 } && BitConverter.ToUInt32(bytes, 0) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every advertised clipboard format id (empty when the clipboard could not be opened).</summary>
    public static List<uint> Formats()
    {
        var list = new List<uint>();
        if (!Open())
        {
            return list;
        }

        try
        {
            for (var format = WinApi.EnumClipboardFormats(0); format != 0; format = WinApi.EnumClipboardFormats(format))
            {
                list.Add(format);
            }
        }
        finally
        {
            WinApi.CloseClipboard();
        }

        return list;
    }

    /// <summary>Paths of a copied file list (CF_HDROP), or null.</summary>
    public static List<string>? ReadFiles(int maxPaths = 100)
    {
        var bytes = ReadFormat(ClipFormats.HDrop);
        if (bytes is null || bytes.Length < 20)
        {
            return null;
        }

        var offset = BitConverter.ToInt32(bytes, 0);
        var wide = BitConverter.ToInt32(bytes, 16) != 0;
        if (offset < 20 || offset >= bytes.Length)
        {
            return null;
        }

        var text = wide
            ? Encoding.Unicode.GetString(bytes, offset, bytes.Length - offset)
            : Encoding.Default.GetString(bytes, offset, bytes.Length - offset);
        var paths = new List<string>();
        foreach (var part in text.Split('\0'))
        {
            if (part.Length == 0)
            {
                // An empty entry ends the list.
                if (paths.Count > 0)
                {
                    break;
                }

                continue;
            }

            paths.Add(part);
            if (paths.Count >= maxPaths)
            {
                break;
            }
        }

        return paths.Count == 0 ? null : paths;
    }

    /// <summary>The <c>Preferred DropEffect</c> (1 copy, 2 move) of the current clipboard content, or 0 when absent.</summary>
    public static uint ReadDropEffect()
    {
        var bytes = ReadFormat(ClipFormats.PreferredDropEffect);
        return bytes is { Length: >= 4 } ? BitConverter.ToUInt32(bytes, 0) : 0;
    }

    /// <summary>Builds a CF_HDROP payload (wide paths).</summary>
    public static byte[] BuildHDrop(IEnumerable<string> paths)
    {
        var joined = string.Join('\0', paths) + "\0\0";
        var chars = Encoding.Unicode.GetBytes(joined);
        var data = new byte[20 + chars.Length];
        BitConverter.GetBytes(20).CopyTo(data, 0);
        BitConverter.GetBytes(1).CopyTo(data, 16);
        chars.CopyTo(data, 20);
        return data;
    }

    /// <summary>Replaces the clipboard with a file list (copy or move).</summary>
    public static bool WriteFiles(IReadOnlyList<string> paths, bool move = false, bool markOwn = true)
    {
        if (paths.Count == 0 || !Open())
        {
            return false;
        }

        try
        {
            WinApi.EmptyClipboard();
            var ok = SetBytes(ClipFormats.HDrop, BuildHDrop(paths));
            SetBytes(ClipFormats.PreferredDropEffect, BitConverter.GetBytes(move ? WinApi.DROPEFFECT_MOVE : WinApi.DROPEFFECT_COPY));
            if (markOwn)
            {
                SetBytes(ClipFormats.OwnWrite, [1]);
            }

            return ok;
        }
        finally
        {
            WinApi.CloseClipboard();
        }
    }

    /// <summary>Marks the current file list as copy instead of move without touching the files (cancels a cut).</summary>
    public static bool RewriteAsCopy()
    {
        var paths = ReadFiles(10_000);
        return paths is not null && WriteFiles(paths, move: false, markOwn: true);
    }

    /// <summary>Empties the clipboard. Returns the sequence number right after clearing.</summary>
    public static uint Clear()
    {
        if (!Open())
        {
            return WinApi.GetClipboardSequenceNumber();
        }

        try
        {
            WinApi.EmptyClipboard();
        }
        finally
        {
            WinApi.CloseClipboard();
        }

        return WinApi.GetClipboardSequenceNumber();
    }

    /// <summary>Wraps an HTML fragment in the CF_HTML header.</summary>
    public static byte[] BuildHtmlFormat(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string pre = "<html><body><!--StartFragment-->";
        const string post = "<!--EndFragment--></body></html>";
        var headerLength = string.Format(header, 0, 0, 0, 0).Length;
        var startFragment = headerLength + Encoding.UTF8.GetByteCount(pre);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(post);
        return Encoding.UTF8.GetBytes(string.Format(header, headerLength, endHtml, startFragment, endFragment) + pre + fragment + post + "\0");
    }

    /// <summary>Writes text plus an HTML flavour (used for piles that mix text and images).</summary>
    public static bool WriteRich(string plain, string htmlFragment, byte[]? firstPng = null)
    {
        if (!Open())
        {
            return false;
        }

        try
        {
            WinApi.EmptyClipboard();
            var ok = true;
            if (plain.Length > 0)
            {
                ok &= SetBytes(ClipFormats.UnicodeText, Encoding.Unicode.GetBytes(plain + "\0"));
            }

            ok &= SetBytes(ClipFormats.Html, BuildHtmlFormat(htmlFragment));
            if (firstPng is not null)
            {
                SetBytes(ClipFormats.Png, firstPng);
            }

            SetBytes(ClipFormats.OwnWrite, [1]);
            return ok;
        }
        finally
        {
            WinApi.CloseClipboard();
        }
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

            Trayloom.Core.Native.NativeMethods.GetWindowThreadProcessId(owner, out var pid);
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

    /// <summary>
    /// Reads an image from the clipboard and returns it as PNG bytes (the lossless alpha PNG format first, then DIB/DIBV5).
    /// Works on any thread (no WPF clipboard access).
    /// </summary>
    public static byte[]? ReadImagePng()
    {
        var png = ReadFormat(ClipFormats.Png);
        if (png is { Length: > 8 })
        {
            return png;
        }

        var dib = IsAvailable(ClipFormats.DibV5) ? ReadFormat(ClipFormats.DibV5) : null;
        dib ??= IsAvailable(ClipFormats.Dib) ? ReadFormat(ClipFormats.Dib) : null;
        return dib is null ? null : DibToPng(dib);
    }

    /// <summary>Converts a packed DIB (BITMAPINFOHEADER / V4 / V5 + pixels) to PNG bytes.</summary>
    public static byte[]? DibToPng(byte[] dib)
    {
        try
        {
            if (dib.Length < 40)
            {
                return null;
            }

            var headerSize = BitConverter.ToInt32(dib, 0);
            var bitCount = BitConverter.ToInt16(dib, 14);
            var compression = BitConverter.ToInt32(dib, 16);
            var colorsUsed = BitConverter.ToInt32(dib, 32);
            var tableEntries = bitCount <= 8 ? (colorsUsed != 0 ? colorsUsed : 1 << bitCount) : colorsUsed;
            var masks = compression == 3 && headerSize == 40 ? 12 : 0;
            var pixelOffset = 14 + headerSize + masks + tableEntries * 4;
            var file = new byte[14 + dib.Length];
            file[0] = (byte)'B';
            file[1] = (byte)'M';
            BitConverter.GetBytes(file.Length).CopyTo(file, 2);
            BitConverter.GetBytes(pixelOffset).CopyTo(file, 10);
            Buffer.BlockCopy(dib, 0, file, 14, dib.Length);

            using var input = new MemoryStream(file);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            if (headerSize < 108 && bitCount == 32)
            {
                // No alpha information in a plain 32 bit DIB: treat it as opaque.
                frame = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgr32, null, 0);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }
}


/// <summary>An in-memory copy of every raw (HGLOBAL) clipboard format, used to restore the clipboard after a temporary change.</summary>
internal sealed class ClipboardSnapshot
{
    private readonly List<(uint Format, byte[] Data)> _items = [];

    public bool IsEmpty => _items.Count == 0;

    public bool HasFileData { get; private set; }

    /// <summary>True when some advertised format could not be copied (restoring would lose data), so a transient paste must fail open.</summary>
    public bool Unpreservable { get; private set; }

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
                    if (format is ClipFormats.MetafilePict or ClipFormats.EnhMetafile || (format >= 0x80 && format <= 0x8F) || (format >= 0x200 && format <= 0x3FF))
                    {
                        snapshot.Unpreservable = true;
                    }

                    continue;
                }

                var handle = WinApi.GetClipboardData(format);
                if (handle == 0)
                {
                    snapshot.Unpreservable = true;
                    continue;
                }

                var size = WinApi.GlobalSize(handle);
                if (size == 0 || total + (long)size > ClipboardIO.MaxSnapshotBytes)
                {
                    snapshot.Unpreservable = true;
                    continue;
                }

                var pointer = WinApi.GlobalLock(handle);
                if (pointer == 0)
                {
                    snapshot.Unpreservable = true;
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

    /// <summary>Puts the captured formats back (tagged as a Trayloom write so history ignores it).</summary>
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
            var parameters = new HwndSourceParameters("Trayloom.ClipboardMonitor")
            {
                ParentWindow = Trayloom.Core.Native.NativeConstants.HWND_MESSAGE,
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
