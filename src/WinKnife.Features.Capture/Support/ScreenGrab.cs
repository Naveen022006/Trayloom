// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core.Native;

namespace WinKnife.Features.Tools;

/// <summary>A captured copy of the whole virtual desktop (physical pixels).</summary>
/// <param name="Bitmap">Frozen bitmap, 96 DPI, one bitmap pixel per screen pixel.</param>
/// <param name="X">Virtual-screen left coordinate of the bitmap's first pixel.</param>
/// <param name="Y">Virtual-screen top coordinate of the bitmap's first pixel.</param>
internal sealed record ScreenFrame(BitmapSource Bitmap, int X, int Y)
{
    public int Width => Bitmap.PixelWidth;

    public int Height => Bitmap.PixelHeight;

    /// <summary>Crops a rectangle given in frame pixels (clamped to the frame).</summary>
    public BitmapSource Crop(Int32Rect rect)
    {
        var x = Math.Clamp(rect.X, 0, Width - 1);
        var y = Math.Clamp(rect.Y, 0, Height - 1);
        var w = Math.Clamp(rect.Width, 1, Width - x);
        var h = Math.Clamp(rect.Height, 1, Height - y);
        var cropped = new CroppedBitmap(Bitmap, new Int32Rect(x, y, w, h));
        cropped.Freeze();
        return cropped;
    }

    /// <summary>Reads one pixel (frame coordinates); returns null when outside the frame.</summary>
    public Color? PixelAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return null;
        }

        var px = new byte[4];
        Bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return Color.FromRgb(px[2], px[1], px[0]);
    }
}

internal static class ScreenGrab
{
    /// <summary>Bounds of the virtual desktop in physical pixels.</summary>
    public static Int32Rect VirtualScreen() => new(
        N.GetSystemMetrics(N.SM_XVIRTUALSCREEN),
        N.GetSystemMetrics(N.SM_YVIRTUALSCREEN),
        N.GetSystemMetrics(N.SM_CXVIRTUALSCREEN),
        N.GetSystemMetrics(N.SM_CYVIRTUALSCREEN));

    /// <summary>Captures the whole virtual desktop (all monitors) with BitBlt.</summary>
    public static ScreenFrame CaptureVirtualScreen()
    {
        var area = VirtualScreen();
        return new ScreenFrame(CaptureRect(area), area.X, area.Y);
    }

    /// <summary>Captures an arbitrary rectangle in physical virtual-screen pixels.</summary>
    public static BitmapSource CaptureRect(Int32Rect area)
    {
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

            var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // Drop the (undefined) alpha channel and normalise to 96 DPI.
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
            var stride = area.Width * 4;
            var pixels = new byte[stride * area.Height];
            converted.CopyPixels(pixels, stride, 0);
            var result = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        finally
        {
            N.SelectObject(memDc, old);
            N.DeleteObject(hBitmap);
            N.DeleteDC(memDc);
            N.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>Visible top-level windows (front to back) with their visible frame bounds in physical pixels.</summary>
    public static List<(nint Handle, string Title, Int32Rect Bounds)> TopLevelWindows(nint exclude = 0)
    {
        var list = new List<(nint, string, Int32Rect)>();
        N.EnumWindows((hwnd, _) =>
        {
            if (hwnd == exclude || !N.IsWindowVisible(hwnd) || N.IsIconic(hwnd))
            {
                return true;
            }

            if (N.DwmGetWindowAttribute(hwnd, N.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            var title = N.WindowText(hwnd);
            var cls = N.ClassName(hwnd);
            if (title.Length == 0 || cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow")
            {
                return true;
            }

            if (N.DwmGetWindowAttribute(hwnd, N.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) != 0)
            {
                NativeMethods.GetWindowRect(hwnd, out rect);
            }

            if (rect.Width > 8 && rect.Height > 8)
            {
                list.Add((hwnd, title, new Int32Rect(rect.Left, rect.Top, rect.Width, rect.Height)));
            }

            return true;
        }, 0);
        return list;
    }
}

internal static class BitmapUtil
{
    /// <summary>Encodes a bitmap as PNG bytes.</summary>
    public static byte[] ToPng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static void SavePng(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ToPng(bitmap));
    }

    /// <summary>Puts an image on the clipboard (as a DIB, plus PNG for apps that prefer it).</summary>
    public static void CopyToClipboard(BitmapSource bitmap)
    {
        var data = new DataObject();
        data.SetImage(bitmap);
        data.SetData("PNG", new MemoryStream(ToPng(bitmap)));
        Clipboard.SetDataObject(data, true);
    }

    /// <summary>Default screenshot folder: Pictures\Screenshots.</summary>
    public static string ScreenshotsFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
}
