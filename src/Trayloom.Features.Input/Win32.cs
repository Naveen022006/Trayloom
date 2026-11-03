// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using Trayloom.Core.Native;

namespace Trayloom.Features.Input;

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

/// <summary>Win32 declarations that only the Input features need (shared ones live in Core).</summary>
internal static class Win32
{
    private const string User32 = "user32.dll";

    public const uint GA_ROOT = 2;
    public const int GWL_STYLE = -16;
    public const uint GUI_INMOVESIZE = 0x2;
    public const uint GUI_INMENUMODE = 0x4;
    public const uint GUI_POPUPMENUMODE = 0x10;
    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;
    public const int VK_MBUTTON = 0x04;
    public const int VK_BACK = 0x08;
    public const int VK_TAB = 0x09;
    public const int VK_RETURN = 0x0D;
    public const int VK_CAPITAL = 0x14;
    public const ushort VK_UNASSIGNED = 0xE8;

    [DllImport(User32)]
    public static extern nint WindowFromPoint(POINT pt);

    [DllImport(User32)]
    public static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hWnd, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] buffer, int maxCount);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hWnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);

    [DllImport(User32)]
    public static extern nint GetKeyboardLayout(uint idThread);

    [DllImport(User32)]
    public static extern int ToUnicodeEx(
        uint virtualKey,
        uint scanCode,
        byte[] keyState,
        [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] buffer,
        int bufferSize,
        uint flags,
        nint keyboardLayout);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint period);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageNameW(nint process, uint flags, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] name, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint handle);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    public const uint GW_HWNDPREV = 3;
    public const uint SMTO_ABORTIFHUNG = 0x2;
    public const uint WM_APPCOMMAND = 0x0319;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint SC_CLOSE = 0xF060;
    public const uint SPI_GETWHEELSCROLLLINES = 0x0068;

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport(User32)]
    public static extern nint GetWindow(nint hwnd, uint command);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hwnd);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern nint SendMessageTimeoutW(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfoW(uint action, uint param, out uint value, uint flags);

    [DllImport(User32)]
    public static extern nint GetShellWindow();

    /// <summary>True while any of Ctrl, Alt, Shift or Win is physically down.</summary>
    public static bool AnyModifierDown() =>
        NativeMethods.GetAsyncKeyState(0x10) < 0 || NativeMethods.GetAsyncKeyState(0x11) < 0 || NativeMethods.GetAsyncKeyState(0x12) < 0
        || NativeMethods.GetAsyncKeyState(0x5B) < 0 || NativeMethods.GetAsyncKeyState(0x5C) < 0;

    /// <summary>Window title text (empty when none).</summary>
    public static string WindowText(nint hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var read = GetWindowTextW(hwnd, buffer, buffer.Length);
        return read > 0 ? new string(buffer, 0, read) : string.Empty;
    }

    [DllImport(User32, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] buffer, int maxCount);

    /// <summary>Window class name of <paramref name="hwnd"/> (empty when it cannot be read).</summary>
    public static string ClassName(nint hwnd)
    {
        var buffer = new char[256];
        var length = GetClassNameW(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    /// <summary>File name (e.g. <c>chrome.exe</c>) of the process owning the window, or null.</summary>
    public static string? ProcessFileName(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0)
        {
            return null;
        }

        try
        {
            var buffer = new char[520];
            var size = (uint)buffer.Length;
            return QueryFullProcessImageNameW(process, 0, buffer, ref size)
                ? Path.GetFileName(new string(buffer, 0, (int)size))
                : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>True when the window is cloaked by DWM (other virtual desktop, hidden UWP frame...).</summary>
    public static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
}
