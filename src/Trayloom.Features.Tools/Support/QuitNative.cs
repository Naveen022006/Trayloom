// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;

namespace Trayloom.Features.Tools;

/// <summary>P/Invoke used by the Quit apps window listing and the Quick panel.</summary>
internal static class QuitNative
{
    public delegate bool EnumProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(nint parent, EnumProc proc, nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLengthW(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);

    public const uint GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80;
    public const long WS_EX_APPWINDOW = 0x40000;
    public const uint WM_CLOSE = 0x0010;

    public static string ClassOf(nint hwnd)
    {
        var sb = new StringBuilder(128);
        GetClassNameW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>True for windows the taskbar / Alt+Tab would list: visible, unowned, not a tool window (unless app window), not cloaked.</summary>
    public static bool IsAppWindow(nint hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != 0)
        {
            return false;
        }

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0)
        {
            return false;
        }

        DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int));
        return cloaked == 0 && GetWindowTextLengthW(hwnd) > 0;
    }
}
