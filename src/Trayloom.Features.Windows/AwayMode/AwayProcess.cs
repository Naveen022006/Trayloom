// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;
using Trayloom.Core.Native;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>Why Trayloom may not touch a process at all.</summary>
internal enum AwayAccess
{
    Ok,
    Elevated,
    OtherUser,
}

/// <summary>
/// Read-only process queries for closing and reopening apps (spec 07 sections 2.2, 3.1 and 4): command line, start time, elevation,
/// owner, package identity, hung and virtual-desktop checks. Nothing here writes to a foreign process; the command line comes from
/// <c>NtQueryInformationProcess</c>, which needs only the limited query right and reads no foreign memory.
/// </summary>
internal static class AwayProcess
{
    public const uint Synchronize = 0x00100000;
    public const uint QueryLimited = 0x1000;
    public const uint VmRead = 0x0010;

    private const int ProcessCommandLineInformation = 60;
    private const int TokenUser = 1;
    private const int TokenElevation = 20;
    private const uint TokenQuery = 0x0008;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(nint process, int infoClass, nint buffer, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool ProcessIdToSessionId(uint pid, out uint session);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int infoClass, nint buffer, int length, out int returned);

    [DllImport("advapi32.dll")]
    private static extern bool EqualSid(nint a, nint b);

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(nint hwnd);

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint hwnd, [MarshalAs(UnmanagedType.Bool)] out bool onCurrent);
    }

    [ComImport]
    [Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    private class VirtualDesktopManagerCom
    {
    }

    /// <summary>The raw command line of a process (argv[0] included), or null when it cannot be read.</summary>
    public static string? CommandLine(uint pid)
    {
        var process = WinApi.OpenProcess(QueryLimited, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var size = 1024;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    var status = NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, size, out var needed);
                    if (status == 0)
                    {
                        // UNICODE_STRING { ushort Length; ushort MaximumLength; PWSTR Buffer }, the text follows in the same block.
                        var length = (ushort)Marshal.ReadInt16(buffer);
                        var text = Marshal.ReadIntPtr(buffer, nint.Size);
                        return text == 0 ? string.Empty : Marshal.PtrToStringUni(text, length / 2);
                    }

                    // STATUS_INFO_LENGTH_MISMATCH, STATUS_BUFFER_TOO_SMALL, STATUS_BUFFER_OVERFLOW: ask again with the size it wants.
                    if ((uint)status is 0xC0000004u or 0xC0000023u or 0x80000005u && needed > size)
                    {
                        size = needed + 16;
                        continue;
                    }

                    return null;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return null;
        }
        finally
        {
            WinApi.CloseHandle(process);
        }
    }

    /// <summary>Everything after argv[0], unchanged (an empty string when there is none).</summary>
    public static string ArgumentsOf(string commandLine)
    {
        var text = commandLine.TrimStart();
        int end;
        if (text.StartsWith('"'))
        {
            end = text.IndexOf('"', 1);
            end = end < 0 ? text.Length : end + 1;
        }
        else
        {
            end = text.IndexOf(' ');
            end = end < 0 ? text.Length : end;
        }

        return text[end..].TrimStart();
    }

    /// <summary>The value of <c>name=value</c> or <c>name="value"</c> inside a command line; null when absent.</summary>
    public static string? SwitchValue(string commandLine, string name)
    {
        var index = commandLine.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + name.Length + 1;
        if (start >= commandLine.Length)
        {
            return string.Empty;
        }

        if (commandLine[start] == '"')
        {
            var close = commandLine.IndexOf('"', start + 1);
            return close < 0 ? commandLine[(start + 1)..] : commandLine[(start + 1)..close];
        }

        var space = commandLine.IndexOf(' ', start);
        return space < 0 ? commandLine[start..] : commandLine[start..space];
    }

    /// <summary>Process creation time as a FILETIME; 0 when it cannot be read.</summary>
    public static long StartTime(uint pid)
    {
        var process = WinApi.OpenProcess(QueryLimited, false, pid);
        if (process == 0)
        {
            return 0;
        }

        try
        {
            return WinApi.GetProcessTimes(process, out var creation, out _, out _, out _) ? creation : 0;
        }
        finally
        {
            WinApi.CloseHandle(process);
        }
    }

    /// <summary>
    /// Elevated processes, processes of another user and processes that cannot be queried are never touched (a refused query counts as
    /// elevated: the safe reading).
    /// </summary>
    public static AwayAccess CheckAccess(uint pid)
    {
        var process = WinApi.OpenProcess(QueryLimited, false, pid);
        if (process == 0)
        {
            return AwayAccess.Elevated;
        }

        try
        {
            if (!WinApi.OpenProcessToken(process, TokenQuery, out var token))
            {
                return AwayAccess.Elevated;
            }

            try
            {
                if (GetTokenInformation(token, TokenElevation, out var elevated) && elevated != 0)
                {
                    return AwayAccess.Elevated;
                }

                return SameUser(token) && SameSession(pid) ? AwayAccess.Ok : AwayAccess.OtherUser;
            }
            finally
            {
                WinApi.CloseHandle(token);
            }
        }
        finally
        {
            WinApi.CloseHandle(process);
        }
    }

    private static bool GetTokenInformation(nint token, int infoClass, out int value)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            if (!GetTokenInformation(token, infoClass, buffer, sizeof(int), out _))
            {
                value = 0;
                return false;
            }

            value = Marshal.ReadInt32(buffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool SameSession(uint pid) =>
        ProcessIdToSessionId(pid, out var theirs) && ProcessIdToSessionId((uint)Environment.ProcessId, out var ours) && theirs == ours;

    private static bool SameUser(nint token)
    {
        var self = OwnUserSid(out var selfBuffer);
        var other = UserSidOfToken(token, out var otherBuffer);
        try
        {
            return self != 0 && other != 0 && EqualSid(self, other);
        }
        finally
        {
            if (selfBuffer != 0)
            {
                Marshal.FreeHGlobal(selfBuffer);
            }

            if (otherBuffer != 0)
            {
                Marshal.FreeHGlobal(otherBuffer);
            }
        }
    }

    private static nint OwnUserSid(out nint buffer)
    {
        buffer = 0;
        if (!WinApi.OpenProcessToken(WinApi.GetCurrentProcess(), TokenQuery, out var token))
        {
            return 0;
        }

        try
        {
            return UserSidOfToken(token, out buffer);
        }
        finally
        {
            WinApi.CloseHandle(token);
        }
    }

    private static nint UserSidOfToken(nint token, out nint buffer)
    {
        buffer = Marshal.AllocHGlobal(512);
        if (!GetTokenInformation(token, TokenUser, buffer, 512, out _))
        {
            Marshal.FreeHGlobal(buffer);
            buffer = 0;
            return 0;
        }

        // TOKEN_USER starts with a SID_AND_ATTRIBUTES whose first member is the pointer to the SID.
        return Marshal.ReadIntPtr(buffer);
    }

    /// <summary>The AppUserModelId of a packaged process, or null for a classic one.</summary>
    public static string? Aumid(uint pid)
    {
        var process = WinApi.OpenProcess(QueryLimited, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            uint length = 256;
            var builder = new StringBuilder((int)length);
            return WinApi.GetApplicationUserModelId(process, ref length, builder) == 0 ? builder.ToString() : null;
        }
        finally
        {
            WinApi.CloseHandle(process);
        }
    }

    /// <summary>True when the window does not answer messages (the system's own hung-window test, which never blocks).</summary>
    public static bool IsHung(nint hwnd)
    {
        try
        {
            return IsHungAppWindow(hwnd);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The owner window of a window, or 0.</summary>
    public static nint OwnerWindow(nint hwnd) => WinApi.GetWindow(hwnd, WinApi.GW_OWNER);

    /// <summary>
    /// True for a window that sits on another virtual desktop (cloaked by the shell and not on the current desktop). Anything that cannot
    /// be told counts as the current desktop here: the caller only uses this to keep an app open, never to close one.
    /// </summary>
    public static bool IsOnAnotherDesktop(nint hwnd)
    {
        try
        {
            if (WinApi.DwmGetWindowAttribute(hwnd, WinApi.DWMWA_CLOAKED, out var cloaked, sizeof(int)) != 0 || (cloaked & 2) == 0)
            {
                return false;
            }

            var manager = (IVirtualDesktopManager)new VirtualDesktopManagerCom();
            try
            {
                return manager.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent) == 0 && !onCurrent;
            }
            finally
            {
                Marshal.ReleaseComObject(manager);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Every top-level window handle, topmost first.</summary>
    public static List<nint> TopLevelWindows()
    {
        var list = new List<nint>();
        WinApi.EnumWindows((hwnd, _) =>
        {
            list.Add(hwnd);
            return true;
        }, 0);
        return list;
    }

    /// <summary>
    /// A process opened for waiting on its exit. The creation time is checked, so a reused process id can never be mistaken for the
    /// process that was closed.
    /// </summary>
    public sealed class Handle : IDisposable
    {
        private nint _handle;

        private Handle(nint handle, uint pid, long start)
        {
            _handle = handle;
            Pid = pid;
            Start = start;
        }

        public uint Pid { get; }

        public long Start { get; }

        /// <summary>Opens the process; null when it is gone, was replaced by another with the same id, or cannot be opened.</summary>
        public static Handle? Open(uint pid, long expectedStart = 0)
        {
            var handle = WinApi.OpenProcess(Synchronize | QueryLimited, false, pid);
            if (handle == 0)
            {
                return null;
            }

            var start = WinApi.GetProcessTimes(handle, out var creation, out _, out _, out _) ? creation : 0;
            if (expectedStart != 0 && start != 0 && start != expectedStart)
            {
                WinApi.CloseHandle(handle);
                return null;
            }

            return new Handle(handle, pid, start);
        }

        public bool HasExited => _handle == 0 || WaitForSingleObject(_handle, 0) == 0;

        /// <summary>Waits up to <paramref name="milliseconds"/> for the process to exit; true when it has.</summary>
        public bool WaitExit(int milliseconds) => _handle == 0 || WaitForSingleObject(_handle, (uint)milliseconds) == 0;

        public void Dispose()
        {
            if (_handle != 0)
            {
                WinApi.CloseHandle(_handle);
                _handle = 0;
            }
        }
    }
}
