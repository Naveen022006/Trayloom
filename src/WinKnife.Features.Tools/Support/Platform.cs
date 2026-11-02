// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Small Win32 helpers shared by the Tools features.</summary>
internal static class Plat
{
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfoW(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    /// <summary>The cursor position in physical pixels.</summary>
    public static Point Cursor() => Screens.CursorPosition();

    /// <summary>Work area (physical px) of the monitor under the cursor.</summary>
    public static MonitorArea Monitor() => Screens.UnderCursor();

    /// <summary>The foreground top-level window.</summary>
    public static nint Foreground() => NativeMethods.GetForegroundWindow();

    /// <summary>Process id of a window (0 when unknown).</summary>
    public static int ProcessOf(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    /// <summary>Brings a foreign window to the front (restores it when minimized), using the AttachThreadInput trick.</summary>
    public static bool Activate(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            return false;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, 9);
        }

        var fg = NativeMethods.GetForegroundWindow();
        var fgThread = fg == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(fg, out _);
        var me = GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            BringWindowToTop(hwnd);
            return NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(me, fgThread, false);
            }
        }
    }

    /// <summary>Waits until no modifier key is held (poll 15 ms, up to 100 tries), settles 60 ms, then runs <paramref name="then"/> on the UI thread.</summary>
    public static void AfterModifiersReleased(Dispatcher ui, Action then, int maxPolls = 100)
    {
        var polls = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            polls++;
            if (!ModifiersHeld() || polls >= maxPolls)
            {
                timer.Stop();
                var settle = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(60) };
                settle.Tick += (_, _) =>
                {
                    settle.Stop();
                    then();
                };
                settle.Start();
            }
        };
        timer.Start();
    }

    /// <summary>True while Ctrl, Alt, Shift or Win is physically down.</summary>
    public static bool ModifiersHeld() =>
        (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0 || (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0
        || (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0 || (NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0
        || (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0;

    /// <summary>Runs <paramref name="action"/> after <paramref name="delayMs"/> on the UI thread.</summary>
    public static void After(Dispatcher ui, int delayMs, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    /// <summary>Opens a file, folder or URL with its default handler. False when it fails.</summary>
    public static bool Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Shows a window at physical-pixel coordinates: first moves it without resizing (so WPF picks the monitor DPI), then sets the real position.</summary>
    public static void PlaceWindow(Window window, double leftPx, double topPx, double widthDip, double heightDip)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        NativeMethods.SetWindowPos(handle, 0, (int)leftPx, (int)topPx, 0, 0, NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
        var dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
        window.Left = leftPx / dpi;
        window.Top = topPx / dpi;
        if (widthDip > 0)
        {
            window.Width = widthDip;
        }

        if (heightDip > 0)
        {
            window.Height = heightDip;
        }
    }

    /// <summary>Shell icon (large) of a path or <c>shell:AppsFolder\AUMID</c> item as a frozen bitmap (null when none). UI thread not required.</summary>
    public static BitmapSource? ShellImage(string path, int size = 32)
    {
        var key = path + "|" + size;
        if (ShellImageCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        BitmapSource? image = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, 0, ref iid, out var factory);
            var hr = factory.GetImage(new SIZE { cx = size, cy = size }, 0x0, out var hbitmap);
            if (hr == 0 && hbitmap != 0)
            {
                try
                {
                    image = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                }
                finally
                {
                    DeleteObject(hbitmap);
                }
            }
        }
        catch
        {
            image = null;
        }

        if (image is null && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            image = FileIcon(path);
        }

        ShellImageCache[key] = image;
        return image;
    }

    private static BitmapSource? FileIcon(string path)
    {
        try
        {
            var info = new SHFILEINFO();
            var result = SHGetFileInfoW(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 | 0x0);
            if (result == 0 || info.hIcon == 0)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    private static readonly ConcurrentDictionary<string, BitmapSource?> ShellImageCache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid, out IShellItemImageFactory factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out nint hbitmap);
    }

    /// <summary>The executable path of a window's process (null when it cannot be read).</summary>
    public static string? ExePathOfWindow(nint hwnd) => N.ProcessPath(ProcessOf(hwnd));
}

/// <summary>Watches clicks outside a frame (low-level mouse hook, never consumes) and fires <c>onOutside</c> on the UI thread.</summary>
internal sealed class OutsideClickMonitor : IDisposable
{
    private readonly IDisposable _subscription;

    public OutsideClickMonitor(IInputHooks hooks, Dispatcher ui, Func<Rect> framePhysical, Action onOutside, Func<bool>? active = null)
    {
        _subscription = hooks.SubscribeMouse(e =>
        {
            if (e.Kind is MouseEventKind.LeftDown or MouseEventKind.RightDown or MouseEventKind.MiddleDown or MouseEventKind.XButtonDown)
            {
                // The frame / active callbacks read WPF windows: never on the hook thread. Decide on the UI thread.
                var x = e.X;
                var y = e.Y;
                ui.BeginInvoke(() =>
                {
                    if (!(active?.Invoke() ?? true))
                    {
                        return;
                    }

                    var frame = framePhysical();
                    frame.Inflate(2, 2);
                    if (!frame.Contains(new Point(x, y)) && !IsKeyboardWindow(x, y))
                    {
                        onOutside();
                    }
                });
            }

            return false;
        });
    }

    public void Dispose() => _subscription.Dispose();

    private static bool IsKeyboardWindow(int x, int y)
    {
        var hwnd = WindowFromPoint(new POINT { X = x, Y = y });
        if (hwnd == 0)
        {
            return false;
        }

        var root = GetAncestor(hwnd, 2);
        var sb = new StringBuilder(64);
        GetClassNameW(root == 0 ? hwnd : root, sb, sb.Capacity);
        var cls = sb.ToString();
        return cls is "IPTip_Main_Window" or "OSKMainClass" or "ApplicationFrameWindow" && IsOnScreenKeyboardProcess(root);
    }

    private static bool IsOnScreenKeyboardProcess(nint hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Contains("osk", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("TabTip", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("TextInputHost", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);
}

/// <summary>Raises <see cref="ForegroundChanged"/> (UI thread) whenever another window becomes the foreground window.</summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);

    private readonly Dispatcher _ui;
    private readonly WinEventProc _proc;
    private nint _hook;

    public ForegroundWatcher(Dispatcher ui)
    {
        _ui = ui;
        _proc = OnEvent;
        _hook = SetWinEventHook(0x0003, 0x0003, 0, _proc, 0, 0, 0x0000);
    }

    /// <summary>Raised with the new foreground window handle.</summary>
    public event Action<nint>? ForegroundChanged;

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWinEvent(_hook);
            _hook = 0;
        }
    }

    private void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject == 0 && hwnd != 0)
        {
            _ui.BeginInvoke(() => ForegroundChanged?.Invoke(hwnd));
        }
    }

    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
