// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;
using WinKnife.Core.Native;

namespace WinKnife.Features.WindowTools;

[StructLayout(LayoutKind.Sequential)]
internal struct DWM_THUMBNAIL_PROPERTIES
{
    public uint dwFlags;
    public RECT rcDestination;
    public RECT rcSource;
    public byte opacity;
    [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
    [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx;
    public int cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GUITHREADINFO
{
    public int cbSize;
    public uint flags;
    public nint hwndActive;
    public nint hwndFocus;
    public nint hwndCapture;
    public nint hwndMenuOwner;
    public nint hwndMoveSize;
    public nint hwndCaret;
    public RECT rcCaret;
}

/// <summary>Feature-local Win32 declarations (things Core does not expose).</summary>
internal static class WinApi
{
    public const string User32 = "user32.dll";
    public const string Kernel32 = "kernel32.dll";
    public const string Dwm = "dwmapi.dll";

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    public delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint lParam);

    // Constants
    public const int GWL_STYLE = -16;
    public const long WS_THICKFRAME = 0x00040000;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public const uint GW_OWNER = 4;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;
    public const uint DWM_TNP_RECTDESTINATION = 0x1;
    public const uint DWM_TNP_RECTSOURCE = 0x2;
    public const uint DWM_TNP_OPACITY = 0x4;
    public const uint DWM_TNP_VISIBLE = 0x8;
    public const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x10;
    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;
    public const uint WM_GETICON = 0x007F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_CLIPBOARDUPDATE = 0x031D;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const int GCLP_HICON = -14;
    public const int GCLP_HICONSM = -34;
    public const uint GUI_INMOVESIZE = 0x00000002;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // Window enumeration and properties
    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint lParam);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hwnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hwnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hwnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(nint hwnd);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint hwnd, StringBuilder text, int maxCount);

    [DllImport(User32)]
    public static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hwnd, StringBuilder text, int maxCount);

    [DllImport(User32)]
    public static extern nint GetWindow(nint hwnd, uint cmd);

    [DllImport(User32)]
    public static extern nint GetShellWindow();

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hwnd, int cmd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(nint hwnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport(User32, SetLastError = true)]
    public static extern nint SendMessageTimeoutW(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport(User32, EntryPoint = "GetClassLongPtrW")]
    public static extern nint GetClassLongPtr(nint hwnd, int index);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint lParam);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    // Processes
    [DllImport(Kernel32, SetLastError = true)]
    public static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport(Kernel32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint handle);

    [DllImport(Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref uint size);

    // DWM
    [DllImport(Dwm)]
    public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [DllImport(Dwm, EntryPoint = "DwmGetWindowAttribute")]
    public static extern int DwmGetWindowAttributeRect(nint hwnd, int attribute, out RECT value, int size);

    [DllImport(Dwm)]
    public static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

    [DllImport(Dwm)]
    public static extern int DwmUnregisterThumbnail(nint thumbnail);

    [DllImport(Dwm)]
    public static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref DWM_THUMBNAIL_PROPERTIES properties);

    [DllImport(Dwm)]
    public static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out SIZE size);

    // Clipboard
    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(nint hwnd);

    [DllImport(User32)]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenClipboard(nint newOwner);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseClipboard();

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EmptyClipboard();

    [DllImport(User32)]
    public static extern uint EnumClipboardFormats(uint format);

    [DllImport(User32)]
    public static extern nint GetClipboardData(uint format);

    [DllImport(User32, SetLastError = true)]
    public static extern nint SetClipboardData(uint format, nint mem);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern uint RegisterClipboardFormatW(string name);

    [DllImport(User32)]
    public static extern nint GetClipboardOwner();

    [DllImport(Kernel32)]
    public static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport(Kernel32)]
    public static extern nint GlobalFree(nint mem);

    [DllImport(Kernel32)]
    public static extern nint GlobalLock(nint mem);

    [DllImport(Kernel32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalUnlock(nint mem);

    [DllImport(Kernel32)]
    public static extern nuint GlobalSize(nint mem);

    public const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>Returns the window title, or an empty string.</summary>
    public static string GetTitle(nint hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Returns the window class name, or an empty string.</summary>
    public static string GetClass(nint hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>Returns the full executable path of a process, or null.</summary>
    public static string? GetProcessPath(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(process, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Scale factor (1.0 = 96 dpi) of a monitor.</summary>
    public static double MonitorScale(nint monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch (Exception)
        {
            // fall through
        }

        return 1.0;
    }

    /// <summary>Visible frame of a window (without the invisible resize borders), falling back to GetWindowRect.</summary>
    public static RECT VisibleFrame(nint hwnd)
    {
        if (DwmGetWindowAttributeRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var rect, Marshal.SizeOf<RECT>()) == 0 && rect.Width > 0)
        {
            return rect;
        }

        NativeMethods.GetWindowRect(hwnd, out rect);
        return rect;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public int length;
    public uint flags;
    public uint showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

internal static class WinApiPlacement
{
    [DllImport(WinApi.User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);
}

internal static class WinApiDpi
{
    [DllImport(WinApi.User32)]
    private static extern nint GetWindowDpiAwarenessContext(nint hwnd);

    [DllImport(WinApi.User32)]
    private static extern nint SetThreadDpiAwarenessContext(nint dpiContext);

    /// <summary>
    /// Switches the calling thread to the DPI awareness context of <paramref name="hwnd"/> until the result is disposed, so
    /// work-area queries, GetWindowRect and SetWindowPos all use the same coordinate space as that window
    /// (a window that is not per-monitor aware is otherwise placed with the wrong scale and overshoots the screen edge).
    /// </summary>
    public static IDisposable EnterWindowDpiContext(nint hwnd)
    {
        try
        {
            var context = GetWindowDpiAwarenessContext(hwnd);
            var previous = context == 0 ? 0 : SetThreadDpiAwarenessContext(context);
            return new DpiContextScope(previous);
        }
        catch (Exception)
        {
            return new DpiContextScope(0);
        }
    }

    private sealed class DpiContextScope(nint previous) : IDisposable
    {
        public void Dispose()
        {
            if (previous != 0)
            {
                SetThreadDpiAwarenessContext(previous);
            }
        }
    }
}
