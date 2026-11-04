// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>
/// The Windows Restart Manager, used to close a browser the way Windows does when it restarts for an update (spec 07 section 3.2): the
/// browser gets <c>WM_QUERYENDSESSION</c> and <c>WM_ENDSESSION</c>, closes all its windows at once and saves its whole session. The
/// shutdown is always graceful: the action flags are the literal 0, so nothing is ever forced and nothing is ended from outside. The
/// same API reports the command line a browser registered for its own restart.
/// </summary>
internal static class AwayRestartManager
{
    /// <summary>RESTART_MAX_CMD_LINE.</summary>
    private const int MaxRestartCommand = 1024;

    private const int KeyLength = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, int flags, StringBuilder key);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint files, string[]? fileNames, uint applications, RM_UNIQUE_PROCESS[]? processes, uint services, string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmShutdown(uint session, int actionFlags, nint statusCallback);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmCancelCurrentTask(uint session);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationRestartSettings(nint process, StringBuilder? commandLine, ref uint size, out uint flags);

    /// <summary>
    /// The command line (arguments only) the process registered with <c>RegisterApplicationRestart</c>; null when it registered none or
    /// cannot be asked.
    /// </summary>
    public static string? RestartCommand(uint pid)
    {
        var process = WinApi.OpenProcess(AwayProcess.VmRead | AwayProcess.QueryLimited, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var builder = new StringBuilder(MaxRestartCommand + 1);
            var size = (uint)builder.Capacity;
            return GetApplicationRestartSettings(process, builder, ref size, out _) == 0 ? builder.ToString() : null;
        }
        finally
        {
            WinApi.CloseHandle(process);
        }
    }

    /// <summary>
    /// Asks one process to shut down gracefully. Blocks until Windows reports back (usually 0.1 to 0.3 s for a browser); a watchdog
    /// cancels the task after <paramref name="watchdogMs"/> or as soon as <paramref name="cancelled"/> turns true. Returns the Win32
    /// result (0 = every window was closed) and the time it took.
    /// </summary>
    public static (int Code, long Milliseconds) Shutdown(uint pid, long start, Func<bool> cancelled, int watchdogMs)
    {
        var clock = Stopwatch.StartNew();
        var key = new StringBuilder(KeyLength + 1);
        var code = RmStartSession(out var session, 0, key);
        if (code != 0)
        {
            return (code, clock.ElapsedMilliseconds);
        }

        using var done = new ManualResetEventSlim(false);
        try
        {
            var process = new RM_UNIQUE_PROCESS { dwProcessId = pid, LowDateTime = (uint)(start & 0xFFFFFFFF), HighDateTime = (uint)((ulong)start >> 32) };
            code = RmRegisterResources(session, 0, null, 1, [process], 0, null);
            if (code != 0)
            {
                return (code, clock.ElapsedMilliseconds);
            }

            var watchdog = new Thread(() =>
            {
                while (!done.Wait(50))
                {
                    if (clock.ElapsedMilliseconds > watchdogMs || cancelled())
                    {
                        RmCancelCurrentTask(session);
                        return;
                    }
                }
            })
            { IsBackground = true, Name = "Away RM watchdog" };
            watchdog.Start();

            // The action flags are the literal 0: a graceful shutdown that no application can be forced out of.
            code = RmShutdown(session, 0, 0);
            return (code, clock.ElapsedMilliseconds);
        }
        finally
        {
            done.Set();
            RmEndSession(session);
        }
    }
}
