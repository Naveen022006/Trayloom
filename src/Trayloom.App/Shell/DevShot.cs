// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Drawing = System.Drawing;

namespace Trayloom.App;

/// <summary>
/// Developer flag <c>--shot &lt;file.png&gt; [match]</c>: saves a picture of one of the app's own windows (no screen access, no input).
/// <c>match</c> is a title part, <c>type:Name</c> (window class name contains) or <c>all</c> (every visible window, written as
/// <c>file-N-Type.png</c>). Without a match the last window with a title is used.
/// </summary>
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

    /// <summary>Developer flag <c>--shot-full &lt;file.png&gt;</c>: the whole Settings page, including what is scrolled out of view.</summary>
    public static void TakeFull(string file, int delayMs = 1200)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var window = Application.Current.Windows.OfType<SettingsWindow>().LastOrDefault(w => w.IsVisible);
                if (window is null)
                {
                    File.WriteAllText(file + ".err", "no settings window");
                    return;
                }

                window.DetailScrollForShot.UpdateLayout();
                var content = window.DetailContentForShot;
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
                var width = Math.Max(1, content.ActualWidth);
                var total = Math.Max(1, content.ActualHeight + 8);
                const double Slice = 1000;
                var count = (int)Math.Ceiling(total / Slice);
                var stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, Path.GetFileNameWithoutExtension(file));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
                var background = window.TryFindResource(Trayloom.Core.Ui.ThemeKeys.SettingsWindow) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black;
                for (var i = 0; i < count; i++)
                {
                    var top = i * Slice;
                    var height = Math.Min(Slice, total - top);
                    var drawing = new System.Windows.Media.DrawingVisual();
                    using (var context = drawing.RenderOpen())
                    {
                        context.DrawRectangle(background, null, new System.Windows.Rect(0, 0, width, height));
                        context.DrawRectangle(
                            new System.Windows.Media.VisualBrush(content) { Stretch = System.Windows.Media.Stretch.None, AlignmentX = System.Windows.Media.AlignmentX.Left, AlignmentY = System.Windows.Media.AlignmentY.Top, ViewboxUnits = System.Windows.Media.BrushMappingMode.Absolute, Viewbox = new System.Windows.Rect(0, top, width, height), ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute, Viewport = new System.Windows.Rect(0, 0, width, height) },
                            null,
                            new System.Windows.Rect(0, 0, width, Math.Min(height, Math.Max(0, content.ActualHeight - top))));
                    }

                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(drawing);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(count == 1 ? file : $"{stem}.{i}.png");
                    encoder.Save(stream);
                }
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
        if (string.Equals(titlePart, "all", StringComparison.OrdinalIgnoreCase))
        {
            var stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, Path.GetFileNameWithoutExtension(file));
            for (var i = 0; i < windows.Count; i++)
            {
                SaveWindow(windows[i], $"{stem}-{i}-{windows[i].GetType().Name}.png");
            }

            return;
        }

        var window = titlePart is null
            ? windows.LastOrDefault(w => w.Title.Length > 0) ?? windows.LastOrDefault()
            : titlePart.StartsWith("type:", StringComparison.OrdinalIgnoreCase)
                ? windows.LastOrDefault(w => w.GetType().Name.Contains(titlePart[5..], StringComparison.OrdinalIgnoreCase))
                : windows.LastOrDefault(w => w.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));
        if (window is null)
        {
            File.WriteAllText(file + ".err", "no window matched; windows: " + string.Join(", ", Application.Current.Windows.OfType<Window>().Select(w => $"'{w.Title}' {w.GetType().Name} vis={w.IsVisible} w={w.ActualWidth}")));
            return;
        }

        SaveWindow(window, file);
    }

    private static void SaveWindow(Window window, string file)
    {
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
