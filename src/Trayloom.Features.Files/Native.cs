// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;
using Trayloom.Core.Native;

namespace Trayloom.Features.Files;

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

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHFILEINFO
{
    public nint hIcon;
    public int iIcon;
    public uint dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CURSORINFO
{
    public int cbSize;
    public int flags;
    public nint hCursor;
    public POINT ptScreenPos;
}

[StructLayout(LayoutKind.Sequential)]
internal struct APPBARDATA
{
    public uint cbSize;
    public nint hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public nint lParam;
}

/// <summary>The few Win32 entry points this project needs on top of <see cref="NativeMethods"/>.</summary>
internal static class WinApi
{
    public const string User32 = "user32.dll";
    public const string Kernel32 = "kernel32.dll";
    public const string Shell32 = "shell32.dll";
    public const string Ole32 = "ole32.dll";

    public delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time);

    public const uint WM_CLIPBOARDUPDATE = 0x031D;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_WTSSESSION_CHANGE = 0x02B1;
    public const uint WM_EXITSIZEMOVE = 0x0232;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const nint MA_NOACTIVATE = 3;
    public const uint GUI_INMOVESIZE = 0x00000002;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const uint GMEM_MOVEABLE = 0x0002;
    public const uint GMEM_ZEROINIT = 0x0040;
    public const int SW_RESTORE = 9;
    public const uint GA_ROOT = 2;
    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_LARGEICON = 0x0;
    public const uint SHGFI_SMALLICON = 0x1;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    public const uint ABM_GETTASKBARPOS = 0x5;
    public const uint DROPEFFECT_COPY = 1;
    public const uint DROPEFFECT_MOVE = 2;

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hwnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hwnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hwnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hwnd, int cmd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(nint hwnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hwnd, StringBuilder text, int maxCount);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern nint FindWindowW(string? className, string? windowName);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern nint FindWindowExW(nint parent, nint after, string? className, string? windowName);

    [DllImport(User32)]
    public static extern nint WindowFromPoint(POINT point);

    [DllImport(User32)]
    public static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorInfo(ref CURSORINFO info);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint icon);

    [DllImport(User32)]
    public static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hook);

    [DllImport(User32, SetLastError = true)]
    public static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid powerSetting, uint flags);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterPowerSettingNotification(nint handle);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSUnRegisterSessionNotification(nint hwnd);

    [DllImport(Kernel32)]
    public static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport(Kernel32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint handle);

    [DllImport(Kernel32, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref uint size);

    [DllImport(Shell32, CharSet = CharSet.Unicode)]
    public static extern nint SHGetFileInfoW(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport(Shell32)]
    public static extern nint SHAppBarMessage(uint message, ref APPBARDATA data);

    [DllImport(Shell32, CharSet = CharSet.Unicode)]
    public static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

    [DllImport(Shell32, CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHParseDisplayName(string name, nint bindingContext, out nint pidl, uint sfgaoIn, out uint sfgaoOut);

    // ---- clipboard ----

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport(User32)]
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

    [DllImport(User32, SetLastError = true)]
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

    /// <summary>Full executable path of the process owning <paramref name="hwnd"/>, or null.</summary>
    public static string? GetWindowProcessPath(nint hwnd, out uint pid)
    {
        pid = 0;
        if (hwnd == 0)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
        return pid == 0 ? null : GetProcessPath(pid);
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHFILEOPSTRUCT
{
    public nint hwnd;
    public uint wFunc;
    public string pFrom;
    public string? pTo;
    public ushort fFlags;
    [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
    public nint hNameMappings;
    public string? lpszProgressTitle;
}
