// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Trayloom.Core.Native;

/// <summary>
/// The window guardian: <c>Trayloom.exe --window-guardian &lt;ownerPid&gt; &lt;ownerStart&gt;</c>, a tiny process that runs only while Trayloom
/// has a foreign window in a state the person cannot see. When Trayloom ends in any way (also Task Manager's End task / End process
/// tree) it gives the windows back. It never loads WPF and never touches Apple Music's driver; it only reads the state file.
/// </summary>
public static class WindowGuardian
{
    private const string LaunchArg = "--window-guardian-launch";
    private const string GuardArg = "--window-guardian";
    private const int DefaultIdleMs = 120000;

    /// <summary>The event that makes every guardian leave (an installer or uninstaller needs the executable).</summary>
    public const string QuitEventName = @"Local\Trayloom.WindowGuardian.Quit";

    /// <summary>The name of the mutex a guardian of this owner holds (it is running while it exists).</summary>
    public static string GuardianMutexName(uint ownerPid) => $@"Local\Trayloom.WindowGuardian.{ownerPid.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The name of the mutex every guardian process holds under its own pid.</summary>
    public static string PidMutexName(int pid) => $@"Local\Trayloom.WindowGuardian.Pid.{pid.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Entry point for the <c>--window-guardian*</c> arguments; the return value is the process exit code.</summary>
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length >= 3 && args[0] == LaunchArg && uint.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var launchOwner) && long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var launchStart))
            {
                return Launch(launchOwner, launchStart);
            }

            if (args.Length >= 3 && args[0] == GuardArg && uint.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var owner) && long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var start))
            {
                return Guard(owner, start);
            }

            return 2;
        }
        catch (Exception ex)
        {
            try
            {
                WindowGuardianState.Log.Error("window guardian failed", ex);
            }
            catch
            {
                // nothing left to do
            }

            return 1;
        }
    }

    // ---------------------------------------------------------------- launcher

    /// <summary>
    /// Starts the guardian detached from this process (own process group, outside the job, below-normal priority) and exits with its pid
    /// (-1 on failure), so the guardian is an orphan: End process tree on Trayloom does not reach it.
    /// </summary>
    private static int Launch(uint ownerPid, long ownerStart)
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return -1;
        }

        var flags = GuardianNative.DetachedProcess | GuardianNative.CreateNewProcessGroup | GuardianNative.CreateBreakawayFromJob | GuardianNative.BelowNormalPriorityClass;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var command = new StringBuilder($"\"{exe}\" {GuardArg} {ownerPid.ToString(CultureInfo.InvariantCulture)} {ownerStart.ToString(CultureInfo.InvariantCulture)}");
            var info = new GUARD_STARTUPINFO { cb = Marshal.SizeOf<GUARD_STARTUPINFO>() };
            if (GuardianNative.CreateProcessW(exe, command, 0, 0, false, flags, 0, null, ref info, out var process))
            {
                GuardianNative.CloseHandle(process.hThread);
                GuardianNative.CloseHandle(process.hProcess);
                return process.dwProcessId;
            }

            // a job that does not allow breakaway: start the guardian in it rather than not at all
            if (Marshal.GetLastWin32Error() != GuardianNative.ErrorAccessDenied)
            {
                break;
            }

            flags &= ~GuardianNative.CreateBreakawayFromJob;
        }

        return -1;
    }

    // ---------------------------------------------------------------- guardian

    private static int Guard(uint ownerPid, long ownerStart)
    {
        var mutex = TryCreateMutex(GuardianMutexName(ownerPid));
        if (mutex is null)
        {
            return 0;
        }

        using var pidMutex = new Mutex(false, PidMutexName(Environment.ProcessId));
        var log = new FileLog(Path.Combine(AppPaths.LogDirectory, "window-guardian.log"));
        log.Info($"guardian {Environment.ProcessId} watching owner {ownerPid}");

        var ownerHandle = GuardianNative.OpenProcess(GuardianNative.Synchronize | GuardianNative.ProcessQueryLimitedInformation, false, ownerPid);
        var ownerGone = ownerHandle == 0 || !GuardianNative.IsProcessAlive(ownerPid, ownerStart);
        if (ownerGone)
        {
            log.Info("the owner is already gone");
            if (ownerHandle != 0)
            {
                GuardianNative.CloseHandle(ownerHandle);
            }

            RescueOwnerWindows(ownerPid, log);
            mutex.Dispose();
            return 0;
        }

        using var owner = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(ownerHandle, true) };
        using var changed = new EventWaitHandle(false, EventResetMode.AutoReset, WindowGuardianState.ChangedEventName(ownerPid));
        using var quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName);
        WaitHandle[] handles = [owner, changed, quit];
        var idleMs = IdleMs();
        long? idleDeadline = null;

        void Observe()
        {
            var empty = IsEmpty(ownerPid);
            if (empty && idleDeadline is null)
            {
                idleDeadline = Environment.TickCount64 + idleMs;
            }
            else if (!empty)
            {
                idleDeadline = null;
            }
        }

        Observe();
        GuardianNative.EmptyWorkingSet(GuardianNative.GetCurrentProcess());
        while (true)
        {
            var timeout = 60000;
            if (idleDeadline is { } deadline)
            {
                timeout = (int)Math.Clamp(deadline - Environment.TickCount64, 0, 60000);
            }

            var which = WaitHandle.WaitAny(handles, timeout);
            if (which == 0)
            {
                log.Info("the owner has ended");
                if (GuardianNative.GetSystemMetrics(GuardianNative.SmShuttingDown) != 0)
                {
                    log.Info("the system is shutting down; the next start repairs what is left");
                    mutex.Dispose();
                    return 0;
                }

                Thread.Sleep(300);
                RescueOwnerWindows(ownerPid, log);
                mutex.Dispose();
                return 0;
            }

            if (which == 2)
            {
                // an installer needs the executable: leave, after one last rescue when the owner is dead
                if (!GuardianNative.IsProcessAlive(ownerPid, ownerStart))
                {
                    RescueOwnerWindows(ownerPid, log);
                }

                mutex.Dispose();
                return 0;
            }

            if (which == 1)
            {
                Observe();
                GuardianNative.EmptyWorkingSet(GuardianNative.GetCurrentProcess());
                continue;
            }

            // a timeout: the idle time is over, or just the minute
            if (idleDeadline is { } d && Environment.TickCount64 >= d)
            {
                mutex.Dispose();
                if (IsEmpty(ownerPid))
                {
                    log.Info("nothing tracked for a while; leaving");
                    return 0;
                }

                // a window was tracked while the mutex was let go: stay, unless another guardian took over
                mutex = TryCreateMutex(GuardianMutexName(ownerPid));
                if (mutex is null)
                {
                    return 0;
                }

                idleDeadline = null;
            }

            GuardianNative.EmptyWorkingSet(GuardianNative.GetCurrentProcess());
        }
    }

    /// <summary>The owner (dead) left records: rescue at 0 s, 1.5 s and 4 s (a late reaction of the window is caught by the next pass), then settle the file.</summary>
    private static void RescueOwnerWindows(uint ownerPid, FileLog log)
    {
        var state = WindowGuardianState.Read(out var unreadable);
        if (state is not null && state.OwnerPid != ownerPid)
        {
            log.Info("the state file belongs to another owner now; nothing to do");
            return;
        }

        if (state is not null && state.Windows.Count == 0)
        {
            WindowGuardianState.DeleteIfOwner(ownerPid);
            return;
        }

        if (state is null && !unreadable)
        {
            // no file at all: the heuristics may still find a window left behind
            log.Info("no state file; looking for windows left behind");
        }

        IReadOnlyList<TrackedWindow> left = [];
        var delays = new[] { 0, 1500, 4000 };
        var passed = 0;
        for (var i = 0; i < delays.Length; i++)
        {
            Thread.Sleep(delays[i] - passed);
            passed = delays[i];

            // Trayloom may have been started again meanwhile (and have hidden the same window for a new job): this snapshot is the dead
            // owner's and must not be acted on once the file is somebody else's, or has been settled by the new start.
            if (i > 0 && SnapshotSuperseded(ownerPid, state is not null))
            {
                log.Info("the state file belongs to a newer Trayloom now (or was settled); the remaining passes are skipped");
                return;
            }

            left = WindowRescue.RescueAll(state, log, heuristics: true);
        }

        if (left.Count == 0)
        {
            WindowGuardianState.DeleteIfOwner(ownerPid);
        }
        else if (state is not null)
        {
            log.Warn($"{left.Count} record(s) could not be resolved; kept for the next start");

            // read-check-write under the state file lock: a newer owner's file is never overwritten
            if (!WindowGuardianState.WriteFileIfOwner(ownerPid, state with { Windows = left }))
            {
                log.Info("the state file belongs to a newer Trayloom now; the unresolved records were not written back");
            }
        }
    }

    /// <summary>The file no longer belongs to the dead owner (another owner wrote it), or it had records and has been removed by whoever settled them.</summary>
    private static bool SnapshotSuperseded(uint ownerPid, bool hadFile)
    {
        var current = WindowGuardianState.Read(out var unreadable);
        if (current is not null)
        {
            return current.OwnerPid != ownerPid;
        }

        return hadFile && !unreadable;
    }

    private static bool IsEmpty(uint ownerPid)
    {
        var state = WindowGuardianState.Read(out _);
        return state is null || state.OwnerPid != ownerPid || state.Windows.Count == 0;
    }

    private static int IdleMs() =>
        int.TryParse(Environment.GetEnvironmentVariable("TRAYLOOM_GUARDIAN_IDLE_MS"), NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && ms > 0 ? ms : DefaultIdleMs;

    private static Mutex? TryCreateMutex(string name)
    {
        try
        {
            var mutex = new Mutex(false, name, out var created);
            if (created)
            {
                return mutex;
            }

            mutex.Dispose();
            return null;
        }
        catch
        {
            return null;
        }
    }
}
