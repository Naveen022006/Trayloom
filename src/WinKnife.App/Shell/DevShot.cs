// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Drawing = System.Drawing;

namespace WinKnife.App;

/// <summary>Developer flag <c>--shot &lt;file.png&gt; [title part]</c>: saves a picture of one of the app's own windows (no screen access, no input).</summary>
internal static class DevShot
{
    public static void Take(string file, string? titlePart, int delayMs = 900)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                Save(file, titlePart);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(file + ".err", ex.ToString()); } catch (Exception) { }
            }
        };
        timer.Start();
    }

    private static void Save(string file, string? titlePart)
    {
        var windows = Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible && w.ActualWidth > 40).ToList();
        var window = titlePart is null
            ? windows.LastOrDefault(w => w.Title.Length > 0) ?? windows.LastOrDefault()
            : windows.LastOrDefault(w => w.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));
        if (window is null)
        {
            File.WriteAllText(file + ".err", "no window matched; windows: " + string.Join(", ", Application.Current.Windows.OfType<Window>().Select(w => $"'{w.Title}' vis={w.IsVisible} w={w.ActualWidth}")));
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        GetWindowRect(handle, out var rect);
        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        using var bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            var dc = graphics.GetHdc();
            try
            {
                PrintWindow(handle, dc, 2);
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        bitmap.Save(file, Drawing.Imaging.ImageFormat.Png);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);
}
