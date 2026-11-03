// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;
using Trayloom.Core.Native;

namespace Trayloom.Features.WindowTools;

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

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Explicit)]
internal struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public nint pwszVal;
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);

    [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);

    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
}

[ComImport]
[Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManager
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrent);

    [PreserveSig] int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

    [PreserveSig] int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
}

internal static partial class WinApi
{
    public delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time);

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint WINEVENT_OUTOFCONTEXT = 0;
    public const uint WINEVENT_SKIPOWNPROCESS = 2;
    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;
    public const uint GA_ROOT = 2;
    public const uint GA_ROOTOWNER = 3;
    public const uint GW_HWNDNEXT = 2;
    public const uint GW_HWNDPREV = 3;
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_MINIMIZE = 6;
    public const int SW_SHOWMINNOACTIVE = 7;
    public const long WS_CAPTION = 0x00C00000;
    public const long WS_POPUP = unchecked((int)0x80000000);
    public const long WS_MINIMIZEBOX = 0x00020000;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_QUIT = 0x0012;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;
    public const uint SWP_NOSENDCHANGING = 0x0400;
    public const uint ABM_GETSTATE = 0x4;
    public const uint ABM_GETTASKBARPOS = 0x5;
    public const uint ABM_SETSTATE = 0xA;
    public const uint ABS_AUTOHIDE = 0x1;
    public const uint ABS_ALWAYSONTOP = 0x2;
    public const uint PROCESS_TERMINATE_LIMITED = 0x1000;
    public const uint SMTO_NORMAL = 0;

    [DllImport(User32)]
    public static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc proc, uint processId, uint threadId, uint flags);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hook);

    [DllImport(User32)]
    public static extern nint WindowFromPoint(POINT point);

    [DllImport(User32)]
    public static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindowAsync(nint hwnd, int cmd);

    [DllImport(User32)]
    public static extern uint MapVirtualKeyW(uint code, uint mapType);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetKeyboardState(byte[] state);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer, int bufferSize, uint flags, nint layout);

    [DllImport(User32)]
    public static extern nint GetKeyboardLayout(uint threadId);

    [DllImport(User32)]
    public static extern nint GetDesktopWindow();

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern nint FindWindowW(string? className, string? windowName);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern nint FindWindowExW(nint parent, nint after, string? className, string? windowName);

    [DllImport(User32)]
    public static extern int GetSystemMetrics(int index);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessageW(uint threadId, uint msg, nint wParam, nint lParam);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowEnabled(nint hwnd);

    [DllImport(User32)]
    public static extern nint SetCapture(nint hwnd);

    [DllImport(User32)]
    public static extern int GetWindowRgn(nint hwnd, nint region);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("shell32.dll")]
    public static extern nuint SHAppBarMessage(uint message, ref APPBARDATA data);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    [DllImport("shell32.dll")]
    public static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PROPVARIANT value);

    [DllImport(Kernel32, CharSet = CharSet.Unicode)]
    public static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder name);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [DllImport(Kernel32)]
    public static extern nint GetCurrentProcess();

    [DllImport("advapi32.dll")]
    public static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll")]
    public static extern bool GetTokenInformation(nint token, int infoClass, out int info, int length, out int returned);

    /// <summary>AppUserModelId of a window (System.AppUserModel.ID), or null.</summary>
    public static string? GetWindowAumid(nint hwnd)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store is null)
            {
                return null;
            }

            try
            {
                var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
                if (store.GetValue(ref key, out var value) != 0)
                {
                    return null;
                }

                try
                {
                    return value.vt == 31 && value.pwszVal != 0 ? Marshal.PtrToStringUni(value.pwszVal) : null;
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>True when the executable of the process is elevated and we are not (best effort, unused result means "unknown").</summary>
    public static bool IsProcessElevated(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            return false;
        }

        try
        {
            if (!OpenProcessToken(process, 0x0008, out var token))
            {
                return false;
            }

            try
            {
                return GetTokenInformation(token, 20, out var elevated, sizeof(int), out _) && elevated != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
