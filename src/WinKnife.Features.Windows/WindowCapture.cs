// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinKnife.Core.Native;

namespace WinKnife.Features.WindowTools;

/// <summary>
/// Window thumbnails for the switcher and taskbar preview (spec 05 A1.3): a bitmap of the window taken with
/// <c>PrintWindow(PW_RENDERFULLCONTENT)</c> on a worker thread. The last bitmap of every window is cached so minimized and
/// other-desktop windows keep showing what they looked like (no Screen Recording permission exists on Windows).
/// </summary>
internal static class WindowCapture
{
    private const uint PwRenderFullContent = 2;
    private static readonly ConcurrentDictionary<nint, BitmapSource> Cache = new();
    private static readonly ConcurrentDictionary<nint, int> Busy = new();

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    /// <summary>The last captured bitmap of a window, or null.</summary>
    public static BitmapSource? Cached(nint hwnd) => Cache.TryGetValue(hwnd, out var image) ? image : null;

    /// <summary>Drops cache entries of windows that no longer exist.</summary>
    public static void Prune()
    {
        foreach (var hwnd in Cache.Keys)
        {
            if (!WinApi.IsWindow(hwnd))
            {
                Cache.TryRemove(hwnd, out _);
            }
        }
    }

    /// <summary>Captures a window on a worker thread; the frozen bitmap is cached and returned (null when it cannot be captured now).</summary>
    public static Task<BitmapSource?> CaptureAsync(nint hwnd, int maxWidth = 640)
    {
        if (!Busy.TryAdd(hwnd, 1))
        {
            return Task.FromResult<BitmapSource?>(null);
        }

        return Task.Run(() =>
        {
            try
            {
                return CaptureNow(hwnd, maxWidth);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Busy.TryRemove(hwnd, out _);
            }
        });
    }

    private static BitmapSource? CaptureNow(nint hwnd, int maxWidth)
    {
        if (!WinApi.IsWindow(hwnd) || WinApi.IsIconic(hwnd) || !WinApi.IsWindowVisible(hwnd))
        {
            return null;
        }

        NativeMethods.GetWindowRect(hwnd, out var rect);
        var width = rect.Width;
        var height = rect.Height;
        if (width < 8 || height < 8 || width > 16000 || height > 16000)
        {
            return null;
        }

        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(hwnd, memory, PwRenderFullContent))
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            BitmapSource result = source;
            if (width > maxWidth)
            {
                var scale = maxWidth / (double)width;
                result = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
            }

            var opaque = new FormatConvertedBitmap(result, System.Windows.Media.PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            Cache[hwnd] = opaque;
            return opaque;
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }
}

/// <summary>
/// Most-recently-used window order (foreground WinEvents), the capture of the window that just lost focus, and the
/// "an elevated window was in front" flag that drives the administrator card.
/// </summary>
internal static class WindowUseTracker
{
    private static readonly List<nint> Order = [];
    private static readonly object Gate = new();
    private static WinApi.WinEventProc? _proc;
    private static nint _hook;
    private static int _users;
    private static nint _previous;

    /// <summary>True once a foreground window of an elevated process was seen while WinKnife is not elevated.</summary>
    public static bool ElevatedSeen { get; private set; }

    /// <summary>Starts tracking (reference counted). Call on the UI thread.</summary>
    public static void Acquire()
    {
        if (_users++ > 0)
        {
            return;
        }

        _proc = OnEvent;
        _hook = WinApi.SetWinEventHook(WinApi.EVENT_SYSTEM_FOREGROUND, WinApi.EVENT_SYSTEM_FOREGROUND, 0, _proc, 0, 0, WinApi.WINEVENT_OUTOFCONTEXT);
        Touch(NativeMethods.GetForegroundWindow());
    }

    public static void Release()
    {
        if (--_users > 0)
        {
            return;
        }

        _users = 0;
        if (_hook != 0)
        {
            WinApi.UnhookWinEvent(_hook);
            _hook = 0;
        }

        _proc = null;
    }

    /// <summary>MRU index of a window (0 = most recent); int.MaxValue when unknown.</summary>
    public static int Rank(nint hwnd)
    {
        lock (Gate)
        {
            var index = Order.IndexOf(hwnd);
            return index < 0 ? int.MaxValue : index;
        }
    }

    public static void Touch(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        lock (Gate)
        {
            Order.Remove(hwnd);
            Order.Insert(0, hwnd);
            if (Order.Count > 400)
            {
                Order.RemoveRange(300, Order.Count - 300);
            }
        }
    }

    private static void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        if (hwnd == 0 || idObject != WinApi.OBJID_WINDOW)
        {
            return;
        }

        var previous = _previous;
        _previous = hwnd;
        Touch(hwnd);
        if (previous != 0 && previous != hwnd && WinApi.IsWindow(previous) && !WinApi.IsIconic(previous))
        {
            _ = WindowCapture.CaptureAsync(previous);
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (!ElevatedSeen && pid != (uint)Environment.ProcessId && !AppIdentity.IsAdmin() && WinApi.IsProcessElevated(pid))
        {
            ElevatedSeen = true;
        }
    }
}
