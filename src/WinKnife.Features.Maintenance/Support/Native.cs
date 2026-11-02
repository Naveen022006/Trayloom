// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;

namespace WinKnife.Features.Tools;

/// <summary>P/Invoke declarations used only by the Tools project.</summary>
internal static class N
{
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
    public const uint SRCCOPY = 0x00CC0020;
    public const uint CAPTUREBLT = 0x40000000;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_COMMAND = 0x0111;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public static readonly nint HWND_BROADCAST = new(0xFFFF);
    public const uint SPI_SETDESKWALLPAPER = 0x0014;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint SHERB_NOCONFIRMATION = 0x1;
    public const uint SHERB_NOPROGRESSUI = 0x2;
    public const uint SHERB_NOSOUND = 0x4;
    public const int SW_SHOWNOACTIVATE = 4;

    private const string User32 = "user32.dll";
    private const string Gdi32 = "gdi32.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string Shell32 = "shell32.dll";

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport(User32)] public static extern int GetSystemMetrics(int index);
    [DllImport(User32)] public static extern nint GetDC(nint hwnd);
    [DllImport(User32)] public static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport(User32)] public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
    [DllImport(User32)] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport(User32)] public static extern bool IsIconic(nint hwnd);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(nint hwnd, StringBuilder text, int max);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern nint FindWindowW(string? cls, string? title);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);
    [DllImport(User32)] public static extern nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern nint SendMessageTimeoutW(nint hwnd, uint msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);
    [DllImport(User32)] public static extern bool LockWorkStation();
    [DllImport(User32)] public static extern bool EnumChildWindows(nint parent, EnumWindowsProc proc, nint lParam);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfoW(uint action, uint param, string? vparam, uint winIni);
    [DllImport(User32)] public static extern bool ShowWindow(nint hwnd, int cmd);

    [DllImport(Gdi32)] public static extern nint CreateCompatibleDC(nint hdc);
    [DllImport(Gdi32)] public static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);
    [DllImport(Gdi32)] public static extern nint SelectObject(nint hdc, nint obj);
    [DllImport(Gdi32)] public static extern bool BitBlt(nint dest, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport(Gdi32)] public static extern bool DeleteObject(nint obj);
    [DllImport(Gdi32)] public static extern bool DeleteDC(nint hdc);

    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attr, out WinKnife.Core.Native.RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);

    [DllImport(Kernel32)] public static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport(Kernel32)] public static extern bool CloseHandle(nint handle);
    [DllImport(Kernel32, CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref int size);

    [DllImport(Shell32, CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBinW(nint hwnd, string? root, uint flags);
    [DllImport(Shell32, CharSet = CharSet.Unicode)] public static extern int SHQueryRecycleBinW(string? root, ref SHQUERYRBINFO info);
    [DllImport(Shell32)] public static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcpTable(nint table, ref int size, bool order, int family, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedUdpTable(nint table, ref int size, bool order, int family, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    public static string WindowText(nint hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassNameW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Full image path of a process id, or null when it cannot be queried.</summary>
    public static string? ProcessPath(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageNameW(handle, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
