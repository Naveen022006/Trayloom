// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;

namespace WinKnife.Features.Island;

/// <summary>Win32 interop of the Apple Music driver (window styles, placement, WinEvent hooks, CreateProcess).</summary>
internal static class AmNative
{
    internal delegate bool EnumProc(nint hwnd, nint lParam);

    internal delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    internal const int SwHide = 0;
    internal const int SwShowNoActivate = 4;
    internal const int SwMinimize = 6;
    internal const int SwShowMinNoActive = 7;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const int GwlExStyle = -20;
    internal const long WsExLayered = 0x80000;
    internal const long WsExTransparent = 0x20;
    internal const uint LwaAlpha = 2;
    internal const uint EventObjectCreate = 0x8000;
    internal const uint EventObjectShow = 0x8002;
    internal const uint GaRoot = 2;
    internal const uint WmQuit = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect Normal;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        public int Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public nint Reserved3;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumProc proc, nint lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern void SwitchToThisWindow(nint hwnd, bool altTab);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    internal static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcess(string? application, StringBuilder command, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string? directory, ref StartupInfo info, out ProcessInformation process);

    [DllImport("kernel32.dll")]
    internal static extern bool CloseHandle(nint handle);

    internal static string ClassOf(nint hwnd)
    {
        var s = new StringBuilder(256);
        GetClassNameW(hwnd, s, 256);
        return s.ToString();
    }

    internal static string TitleOf(nint hwnd)
    {
        var s = new StringBuilder(256);
        GetWindowTextW(hwnd, s, 256);
        return s.ToString();
    }

    internal static uint ProcessOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    internal static List<nint> TopLevelWindows(uint pid)
    {
        var list = new List<nint>();
        EnumWindows((h, _) =>
        {
            if (ProcessOf(h) == pid)
            {
                list.Add(h);
            }

            return true;
        }, 0);
        return list;
    }

    internal static string Describe(nint hwnd)
    {
        if (hwnd == 0)
        {
            return "(none)";
        }

        if (!IsWindow(hwnd))
        {
            return $"0x{hwnd:X} (gone)";
        }

        string name;
        try
        {
            name = System.Diagnostics.Process.GetProcessById((int)ProcessOf(hwnd)).ProcessName;
        }
        catch
        {
            name = "?";
        }

        return $"0x{hwnd:X} {name}/{ClassOf(hwnd)} '{TitleOf(hwnd)}'";
    }
}

/// <summary>A WinEvent hook (create / show) on its own message-loop thread; the callback runs on that thread.</summary>
internal sealed class AmEventHook : IDisposable
{
    private readonly AmNative.WinEventProc _proc;
    private readonly Thread _thread;
    private uint _threadId;

    public AmEventHook(uint minEvent, uint maxEvent, uint processId, Action<uint, nint> callback)
    {
        _proc = (_, ev, hwnd, idObject, _, _, _) =>
        {
            if (idObject != 0 || hwnd == 0)
            {
                return;
            }

            try
            {
                callback(ev, hwnd);
            }
            catch
            {
                // a vanished window must never break the hook thread
            }
        };
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = AmNative.GetCurrentThreadId();
            var hook = AmNative.SetWinEventHook(minEvent, maxEvent, 0, _proc, processId, 0, 0);
            ready.Set();
            while (AmNative.GetMessage(out _, 0, 0, 0) > 0)
            {
            }

            if (hook != 0)
            {
                AmNative.UnhookWinEvent(hook);
            }
        })
        {
            IsBackground = true,
            Name = "WinKnife AppleMusic hook",
        };
        _thread.Start();
        ready.Wait();
    }

    public void Dispose()
    {
        AmNative.PostThreadMessage(_threadId, AmNative.WmQuit, 0, 0);
        _thread.Join(1000);
    }
}
