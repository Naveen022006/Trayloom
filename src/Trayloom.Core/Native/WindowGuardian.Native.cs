// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;

namespace Trayloom.Core.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public int length;
    public int flags;
    public int showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GUARD_STARTUPINFO
{
    public int cb;
    public nint lpReserved;
    public nint lpDesktop;
    public nint lpTitle;
    public int dwX;
    public int dwY;
    public int dwXSize;
    public int dwYSize;
    public int dwXCountChars;
    public int dwYCountChars;
    public int dwFillAttribute;
    public int dwFlags;
    public short wShowWindow;
    public short cbReserved2;
    public nint lpReserved2;
    public nint hStdInput;
    public nint hStdOutput;
    public nint hStdError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GUARD_PROCESS_INFORMATION
{
    public nint hProcess;
    public nint hThread;
    public int dwProcessId;
    public int dwThreadId;
}

/// <summary>The few Win32 calls the window guardian uses (nothing that loads WPF).</summary>
internal static class GuardianNative
{
    internal delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint data);

    internal delegate bool WindowEnumProc(nint hwnd, nint data);

    internal const uint Synchronize = 0x00100000;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const int StillActive = 259;
    internal const int SmShuttingDown = 0x2000;
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const uint LwaAlpha = 2;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint SwpAsyncWindowPos = 0x4000;
    internal const int SwShowMinNoActive = 7;
    internal const int WpfAsyncWindowPlacement = 4;
    internal const int WpfRestoreToMaximized = 2;
    internal const long WsExAppWindow = 0x40000;
    internal const uint DetachedProcess = 0x8;
    internal const uint CreateNewProcessGroup = 0x200;
    internal const uint CreateBreakawayFromJob = 0x01000000;
    internal const uint BelowNormalPriorityClass = 0x4000;
    internal const int ErrorAccessDenied = 5;
    internal const int ErrorInvalidParameter = 87;

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(WindowEnumProc callback, nint data);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SendMessageTimeoutW(nint hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll")]
    internal static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    internal static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    internal static extern bool GetExitCodeProcess(nint process, out int exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet")]
    internal static extern bool EmptyWorkingSet(nint process);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcessW(string? application, StringBuilder command, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string? directory, ref GUARD_STARTUPINFO info, out GUARD_PROCESS_INFORMATION process);

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
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    /// <summary>The executable name (without extension) of a process, "" when it cannot be told.</summary>
    internal static string ProcessNameOf(uint pid)
    {
        if (pid == 0)
        {
            return "";
        }

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == 0)
        {
            return "";
        }

        try
        {
            var name = new StringBuilder(1024);
            var size = name.Capacity;
            return QueryFullProcessImageNameW(handle, 0, name, ref size) ? Path.GetFileNameWithoutExtension(name.ToString()) : "";
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>The creation time (FILETIME, UTC) of this process.</summary>
    internal static long OwnStartTime() => GetProcessTimes(GetCurrentProcess(), out var creation, out _, out _, out _) ? creation : 0;

    /// <summary>The process with this id and creation time still runs (the pid alone can be reused).</summary>
    internal static bool IsProcessAlive(uint pid, long start)
    {
        if (pid == 0)
        {
            return false;
        }

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == 0)
        {
            // no such process; access denied means it exists (and is somebody else's)
            return Marshal.GetLastWin32Error() == ErrorAccessDenied;
        }

        try
        {
            if (!GetProcessTimes(handle, out var creation, out _, out _, out _))
            {
                return true;
            }

            if (start != 0 && creation != start)
            {
                return false;
            }

            return !GetExitCodeProcess(handle, out var code) || code == StillActive;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Runs <paramref name="work"/> on its own thread and waits at most <paramref name="ms"/>: false = it is still running (a cross-process call stuck inside a hung window).</summary>
    internal static bool RunBounded(Action work, int ms, out Thread thread)
    {
        thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch
            {
                // a window that vanishes under the call
            }
        })
        {
            IsBackground = true,
            Name = "Trayloom window rescue",
        };
        thread.Start();
        return thread.Join(ms);
    }
}
