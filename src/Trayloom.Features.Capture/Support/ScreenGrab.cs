// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Trayloom.Features.Capture;

/// <summary>One monitor in physical (virtual-screen) pixels.</summary>
internal sealed record MonitorInfo(nint Handle, Int32Rect Bounds, Int32Rect Work, double Scale, int Index, string Device, bool Primary);

/// <summary>A pickable top-level window.</summary>
internal sealed record WindowInfo(nint Handle, string Title, Int32Rect Bounds, int ProcessId, nint Owner, string ClassName);

/// <summary>A pixel buffer of a screen area plus its position (physical pixels, 96-DPI bitmap, one pixel per screen pixel).</summary>
internal sealed record ScreenFrame(BitmapSource Bitmap, int X, int Y)
{
    public int Width => Bitmap.PixelWidth;

    public int Height => Bitmap.PixelHeight;

    /// <summary>Crops a rectangle given in frame pixels (clamped).</summary>
    public BitmapSource Crop(Int32Rect rect) => ScreenGrab.Crop(Bitmap, rect);

    /// <summary>Reads one pixel (frame coordinates); null outside the frame.</summary>
    public Color? PixelAt(int x, int y) => ScreenGrab.Pixel(Bitmap, x, y);
}

internal static class ScreenGrab
{
    public static Int32Rect VirtualScreen() => new(
        N.GetSystemMetrics(N.SM_XVIRTUALSCREEN),
        N.GetSystemMetrics(N.SM_YVIRTUALSCREEN),
        N.GetSystemMetrics(N.SM_CXVIRTUALSCREEN),
        N.GetSystemMetrics(N.SM_CYVIRTUALSCREEN));

    public static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        N.EnumDisplayMonitors(0, 0, (nint mon, nint dc, ref N.RECT r, nint data) =>
        {
            var info = new N.MONITORINFOEX { cbSize = Marshal.SizeOf<N.MONITORINFOEX>() };
            if (N.GetMonitorInfoW(mon, ref info))
            {
                var scale = 1.0;
                if (N.GetDpiForMonitor(mon, 0, out var dx, out _) == 0 && dx > 0)
                {
                    scale = dx / 96.0;
                }

                list.Add(new MonitorInfo(
                    mon,
                    new Int32Rect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Width, info.rcMonitor.Height),
                    new Int32Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height),
                    scale,
                    list.Count,
                    info.szDevice,
                    (info.dwFlags & 1) != 0));
            }

            return true;
        }, 0);
        return list;
    }

    public static MonitorInfo MonitorAt(int x, int y)
    {
        var all = Monitors();
        foreach (var m in all)
        {
            var b = m.Bounds;
            if (x >= b.X && y >= b.Y && x < b.X + b.Width && y < b.Y + b.Height)
            {
                return m;
            }
        }

        return all.FirstOrDefault(m => m.Primary) ?? all[0];
    }

    public static (int X, int Y) CursorPos()
    {
        N.GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    public static MonitorInfo MonitorUnderCursor()
    {
        var (x, y) = CursorPos();
        return MonitorAt(x, y);
    }

    /// <summary>Captures an arbitrary rectangle in physical virtual-screen pixels with BitBlt.</summary>
    public static BitmapSource CaptureRect(Int32Rect area, bool includePointer = false)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            throw new InvalidOperationException("Empty capture area.");
        }

        var screenDc = N.GetDC(0);
        var memDc = N.CreateCompatibleDC(screenDc);
        var hBitmap = N.CreateCompatibleBitmap(screenDc, area.Width, area.Height);
        var old = N.SelectObject(memDc, hBitmap);
        try
        {
            if (!N.BitBlt(memDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y, N.SRCCOPY | N.CAPTUREBLT))
            {
                throw new InvalidOperationException("The screen could not be captured.");
            }

            if (includePointer)
            {
                DrawPointer(memDc, area.X, area.Y);
            }

            return FromHBitmap(memDc, hBitmap, area.Width, area.Height, forceOpaque: true);
        }
        finally
        {
            N.SelectObject(memDc, old);
            N.DeleteObject(hBitmap);
            N.DeleteDC(memDc);
            N.ReleaseDC(0, screenDc);
        }
    }

    public static ScreenFrame CaptureVirtualScreen(bool includePointer = false)
    {
        var area = VirtualScreen();
        return new ScreenFrame(CaptureRect(area, includePointer), area.X, area.Y);
    }

    /// <summary>Captures one window's own pixels (PrintWindow, so covered windows are fine). Null when it fails.</summary>
    public static BitmapSource? CaptureWindow(WindowInfo window, bool includePointer = false)
    {
        try
        {
            N.GetWindowRect(window.Handle, out var full);
            if (full.Width <= 0 || full.Height <= 0)
            {
                return null;
            }

            var screenDc = N.GetDC(0);
            var memDc = N.CreateCompatibleDC(screenDc);
            var hBitmap = N.CreateCompatibleBitmap(screenDc, full.Width, full.Height);
            var old = N.SelectObject(memDc, hBitmap);
            try
            {
                if (!N.PrintWindow(window.Handle, memDc, 2))
                {
                    return null;
                }

                if (includePointer)
                {
                    DrawPointer(memDc, full.Left, full.Top);
                }

                var whole = FromHBitmap(memDc, hBitmap, full.Width, full.Height, forceOpaque: true);
                var b = window.Bounds;
                var crop = new Int32Rect(b.X - full.Left, b.Y - full.Top, b.Width, b.Height);
                var cropped = Crop(whole, crop);
                if (IsAllDark(cropped))
                {
                    return null;
                }

                return ApplyWindowCorners(cropped, window);
            }
            finally
            {
                N.SelectObject(memDc, old);
                N.DeleteObject(hBitmap);
                N.DeleteDC(memDc);
                N.ReleaseDC(0, screenDc);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Makes the four corners transparent like a Windows 11 rounded window (radius 8 px x scale).</summary>
    private static BitmapSource ApplyWindowCorners(BitmapSource source, WindowInfo window)
    {
        if (N.IsZoomed(window.Handle) || Environment.OSVersion.Version.Build < 22000)
        {
            return source;
        }

        var mon = MonitorAt(window.Bounds.X + window.Bounds.Width / 2, window.Bounds.Y + window.Bounds.Height / 2);
        var radius = (int)Math.Round(8 * mon.Scale);
        var w = source.PixelWidth;
        var h = source.PixelHeight;
        if (w < radius * 3 || h < radius * 3)
        {
            return source;
        }

        var px = Pixels.Get(source);
        void Corner(int ox, int oy, int cx, int cy)
        {
            for (var y = 0; y < radius; y++)
            {
                for (var x = 0; x < radius; x++)
                {
                    var dx = (ox == 0 ? radius - 0.5 - x : x + 0.5);
                    var dy = (oy == 0 ? radius - 0.5 - y : y + 0.5);
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > radius)
                    {
                        var ix = (cx + x) + 0;
                        var iy = (cy + y) + 0;
                        var o = (iy * w + ix) * 4;
                        px[o] = px[o + 1] = px[o + 2] = px[o + 3] = 0;
                    }
                }
            }
        }

        Corner(0, 0, 0, 0);
        Corner(1, 0, w - radius, 0);
        Corner(0, 1, 0, h - radius);
        Corner(1, 1, w - radius, h - radius);
        return Pixels.Make(px, w, h);
    }

    private static bool IsAllDark(BitmapSource bmp)
    {
        var px = Pixels.Get(bmp);
        var step = Math.Max(1, px.Length / 4 / 2000);
        for (var i = 0; i < px.Length; i += 4 * step)
        {
            if (px[i] > 8 || px[i + 1] > 8 || px[i + 2] > 8)
            {
                return false;
            }
        }

        return true;
    }

    private static void DrawPointer(nint dc, int originX, int originY)
    {
        var ci = new N.CURSORINFO { cbSize = Marshal.SizeOf<N.CURSORINFO>() };
        if (!N.GetCursorInfo(ref ci) || (ci.flags & N.CURSOR_SHOWING) == 0 || ci.hCursor == 0)
        {
            return;
        }

        if (N.GetIconInfo(ci.hCursor, out var ii))
        {
            N.DrawIconEx(dc, ci.ptScreenPos.X - originX - ii.xHotspot, ci.ptScreenPos.Y - originY - ii.yHotspot, ci.hCursor, 0, 0, 0, 0, N.DI_NORMAL);
            if (ii.hbmColor != 0)
            {
                N.DeleteObject(ii.hbmColor);
            }

            if (ii.hbmMask != 0)
            {
                N.DeleteObject(ii.hbmMask);
            }
        }
    }

    private static BitmapSource FromHBitmap(nint dc, nint hBitmap, int w, int h, bool forceOpaque)
    {
        var bmi = new N.BITMAPINFO
        {
            bmiHeader = new N.BITMAPINFOHEADER { biSize = Marshal.SizeOf<N.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 },
        };
        var pixels = new byte[w * h * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            N.GetDIBits(dc, hBitmap, 0, (uint)h, handle.AddrOfPinnedObject(), ref bmi, 0);
        }
        finally
        {
            handle.Free();
        }

        if (forceOpaque)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        return Pixels.Make(pixels, w, h);
    }

    public static BitmapSource Crop(BitmapSource source, Int32Rect rect)
    {
        var x = Math.Clamp(rect.X, 0, source.PixelWidth - 1);
        var y = Math.Clamp(rect.Y, 0, source.PixelHeight - 1);
        var w = Math.Clamp(rect.Width + Math.Min(0, rect.X), 1, source.PixelWidth - x);
        var h = Math.Clamp(rect.Height + Math.Min(0, rect.Y), 1, source.PixelHeight - y);
        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
        // Re-base to a standalone 96-DPI bitmap so downstream code can treat (0,0) as the origin.
        return Pixels.Make(Pixels.Get(cropped), w, h, source.DpiX, source.DpiY);
    }

    public static Color? Pixel(BitmapSource bmp, int x, int y)
    {
        if (x < 0 || y < 0 || x >= bmp.PixelWidth || y >= bmp.PixelHeight)
        {
            return null;
        }

        var bgra = new byte[4];
        var src = bmp.Format == PixelFormats.Pbgra32 || bmp.Format == PixelFormats.Bgra32 || bmp.Format == PixelFormats.Bgr32
            ? bmp
            : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        src.CopyPixels(new Int32Rect(x, y, 1, 1), bgra, 4, 0);
        return Color.FromRgb(bgra[2], bgra[1], bgra[0]);
    }

    /// <summary>Visible top-level windows (front to back) with their visible frame bounds in physical pixels.</summary>
    public static List<WindowInfo> TopLevelWindows(IEnumerable<nint>? exclude = null)
    {
        var skip = exclude is null ? new HashSet<nint>() : new HashSet<nint>(exclude);
        var self = Environment.ProcessId;
        var list = new List<WindowInfo>();
        N.EnumWindows((hwnd, _) =>
        {
            if (skip.Contains(hwnd) || !N.IsWindowVisible(hwnd) || N.IsIconic(hwnd))
            {
                return true;
            }

            if (N.DwmGetWindowAttribute(hwnd, N.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            var exStyle = N.GetWindowLongPtr(hwnd, N.GWL_EXSTYLE).ToInt64();
            if ((exStyle & N.WS_EX_TRANSPARENT) != 0 && (exStyle & N.WS_EX_LAYERED) != 0)
            {
                return true;
            }

            var title = N.WindowText(hwnd);
            var cls = N.ClassName(hwnd);
            if (title.Length == 0 || cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow")
            {
                return true;
            }

            N.GetWindowThreadProcessId(hwnd, out var pid);
            if (N.DwmGetWindowAttribute(hwnd, N.DWMWA_EXTENDED_FRAME_BOUNDS, out N.RECT rect, Marshal.SizeOf<N.RECT>()) != 0)
            {
                N.GetWindowRect(hwnd, out rect);
            }

            if (rect.Width > 8 && rect.Height > 8)
            {
                _ = self;
                list.Add(new WindowInfo(hwnd, title, new Int32Rect(rect.Left, rect.Top, rect.Width, rect.Height), pid, N.GetWindow(hwnd, N.GW_OWNER), cls));
            }

            return true;
        }, 0);
        return list;
    }
}

/// <summary>Raw pixel helpers for BGRA(premultiplied) bitmaps.</summary>
internal static class Pixels
{
    public static byte[] Get(BitmapSource source)
    {
        var src = source;
        if (src.Format != PixelFormats.Pbgra32)
        {
            src = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        }

        var stride = src.PixelWidth * 4;
        var pixels = new byte[stride * src.PixelHeight];
        src.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    public static BitmapSource Make(byte[] pixels, int w, int h, double dpiX = 96, double dpiY = 96)
    {
        var bmp = BitmapSource.Create(w, h, dpiX, dpiY, PixelFormats.Pbgra32, null, pixels, w * 4);
        bmp.Freeze();
        return bmp;
    }
}
