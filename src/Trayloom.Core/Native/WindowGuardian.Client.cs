// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32.SafeHandles;

namespace Trayloom.Core.Native;

/// <summary>
/// The owner's side of the window guardian: makes sure a guardian process runs while any foreign window is tracked (started through a
/// short-lived launcher so the guardian is not in Trayloom's process tree), starts it again if it dies, and stops every guardian when an
/// installer needs the executable.
/// </summary>
public static class WindowGuardianClient
{
    private const int MinSpawnGapMs = 10000;
    private const int MaxSpawnsPerHour = 5;
    private const int RecheckMs = 3000;

    private static readonly object Gate = new();
    private static readonly Queue<long> Spawns = new();
    private static long _lastSpawn;
    private static bool _spawning;
    private static bool _gaveUp;
    private static volatile bool _stopping;
    private static Timer? _recheck;

    /// <summary>One watch per live guardian process (by pid), each with the handle it waits on.</summary>
    private static readonly Dictionary<uint, ProcessWatch> Watches = [];

    /// <summary>A wait handle that owns a process handle (so it is closed with the watch, and no other handle is created and dropped on the way).</summary>
    private sealed class ProcessWait : WaitHandle
    {
        public ProcessWait(nint handle) => SafeWaitHandle = new SafeWaitHandle(handle, true);
    }

    private sealed record ProcessWatch(RegisteredWaitHandle Registration, ProcessWait Handle);

    /// <summary>Starts the guardian for this process unless one runs (cheap to call; throttled to 1 start per 10 s and 5 per hour).</summary>
    public static void Ensure()
    {
        if (_stopping || Environment.ProcessPath is null || MutexExists(WindowGuardian.GuardianMutexName(WindowGuardianState.OwnerPid)))
        {
            return;
        }

        var wait = 0L;
        lock (Gate)
        {
            if (_spawning)
            {
                return;
            }

            var now = Environment.TickCount64;
            while (Spawns.Count > 0 && now - Spawns.Peek() > 3600000)
            {
                Spawns.Dequeue();
            }

            if (Spawns.Count >= MaxSpawnsPerHour)
            {
                if (!_gaveUp)
                {
                    _gaveUp = true;
                    WindowGuardianState.Log.Warn("the window guardian was started 5 times within an hour; not starting it again for now");
                }

                return;
            }

            if (_lastSpawn != 0 && now - _lastSpawn < MinSpawnGapMs)
            {
                wait = MinSpawnGapMs - (now - _lastSpawn);
            }
            else
            {
                _spawning = true;
                _lastSpawn = now;
                Spawns.Enqueue(now);
                _gaveUp = false;
            }
        }

        if (wait > 0)
        {
            ScheduleRecheck((int)wait + 50);
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => Spawn());
        ScheduleRecheck(RecheckMs);
    }

    private static void Spawn()
    {
        try
        {
            var exe = Environment.ProcessPath!;
            var start = new ProcessStartInfo(exe, $"--window-guardian-launch {WindowGuardianState.OwnerPid.ToString(CultureInfo.InvariantCulture)} {WindowGuardianState.OwnerStart.ToString(CultureInfo.InvariantCulture)}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var launcher = Process.Start(start);
            if (launcher is null || !launcher.WaitForExit(5000))
            {
                WindowGuardianState.Log.Warn("the window guardian launcher did not finish in 5 s");
                return;
            }

            var pid = launcher.ExitCode;
            if (pid <= 0)
            {
                WindowGuardianState.Log.Warn("the window guardian could not be started (launcher exit code " + pid.ToString(CultureInfo.InvariantCulture) + ")");
                return;
            }

            ConfirmAndWatch((uint)pid);
        }
        catch (Exception ex)
        {
            WindowGuardianState.Log.Warn("the window guardian could not be started: " + ex.Message);
        }
        finally
        {
            lock (Gate)
            {
                _spawning = false;
            }
        }
    }

    /// <summary>
    /// The launcher reported a guardian pid; that process may be a duplicate that ends at once because a guardian of this owner already
    /// holds the mutex. Only a process that holds its own pid mutex (taken after the owner mutex) is a guardian worth watching; a duplicate
    /// leaves the watch of the real one alone.
    /// </summary>
    private static void ConfirmAndWatch(uint pid)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 3000)
        {
            if (MutexExists(WindowGuardian.PidMutexName((int)pid)))
            {
                WatchGuardian(pid);
                return;
            }

            if (!GuardianNative.IsProcessAlive(pid, 0))
            {
                break;
            }

            Thread.Sleep(50);
        }

        if (GuardianNative.IsProcessAlive(pid, 0))
        {
            // slow to start: it is the one the launcher made
            WatchGuardian(pid);
            return;
        }

        if (!MutexExists(WindowGuardian.GuardianMutexName(WindowGuardianState.OwnerPid)))
        {
            WindowGuardianState.Log.Warn("the window guardian ended at once and none holds the owner mutex");
            return;
        }

        // a duplicate: a guardian of this owner runs already; make sure something watches it
        lock (Gate)
        {
            if (Watches.Count > 0)
            {
                return;
            }
        }

        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Trayloom")))
        {
            try
            {
                if (p.Id != Environment.ProcessId && IsGuardian(p.Id))
                {
                    WatchGuardian((uint)p.Id);
                }
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    /// <summary>When the guardian ends while windows are still tracked (and Trayloom is not leaving) a new one is started.</summary>
    private static void WatchGuardian(uint pid)
    {
        lock (Gate)
        {
            if (Watches.ContainsKey(pid))
            {
                return;
            }
        }

        var handle = GuardianNative.OpenProcess(GuardianNative.Synchronize, false, pid);
        if (handle == 0)
        {
            return;
        }

        var wait = new ProcessWait(handle);
        lock (Gate)
        {
            if (Watches.ContainsKey(pid))
            {
                wait.Dispose();
                return;
            }

            var registration = ThreadPool.RegisterWaitForSingleObject(
                wait,
                (_, _) =>
                {
                    lock (Gate)
                    {
                        Watches.Remove(pid);
                    }

                    wait.Dispose();
                    if (!_stopping && !Environment.HasShutdownStarted && WindowGuardianState.HasTracked)
                    {
                        Ensure();
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: true);
            Watches[pid] = new ProcessWatch(registration, wait);
        }
    }

    private static void ScheduleRecheck(int ms)
    {
        lock (Gate)
        {
            _recheck?.Dispose();
            _recheck = new Timer(
                _ =>
                {
                    if (!_stopping && !Environment.HasShutdownStarted && WindowGuardianState.HasTracked && !MutexExists(WindowGuardian.GuardianMutexName(WindowGuardianState.OwnerPid)))
                    {
                        Ensure();
                    }
                },
                null,
                ms,
                Timeout.Infinite);
        }
    }

    /// <summary>
    /// Makes every guardian leave and waits (at most <paramref name="ms"/>) until no other Trayloom process runs as one. For the installer
    /// and the uninstaller, which replace or delete the executable. Returns whether none is left.
    /// </summary>
    public static bool StopAndWait(int ms)
    {
        _stopping = true;
        try
        {
            using var quit = new EventWaitHandle(false, EventResetMode.ManualReset, WindowGuardian.QuitEventName);
            quit.Set();
            try
            {
                var watch = Stopwatch.StartNew();
                while (true)
                {
                    if (!AnyOtherGuardian())
                    {
                        return true;
                    }

                    if (watch.ElapsedMilliseconds >= ms)
                    {
                        return false;
                    }

                    Thread.Sleep(200);
                }
            }
            finally
            {
                quit.Reset();
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            _stopping = false;

            // The quit event reaches every guardian, also the one of this process (an installer run from a copy that itself runs the island,
            // with windows still tracked): the watch did not start another while stopping, so it is done here. A no-op when none is owed.
            if (!Environment.HasShutdownStarted && WindowGuardianState.HasTracked)
            {
                Ensure();
            }
        }
    }

    /// <summary>Whether this Trayloom process is a guardian (it must not be killed with the rest of the old instance).</summary>
    public static bool IsGuardian(int pid) => MutexExists(WindowGuardian.PidMutexName(pid));

    private static bool AnyOtherGuardian()
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Trayloom")))
        {
            try
            {
                if (p.Id != Environment.ProcessId && IsGuardian(p.Id))
                {
                    return true;
                }
            }
            finally
            {
                p.Dispose();
            }
        }

        return false;
    }

    private static bool MutexExists(string name)
    {
        try
        {
            using var mutex = Mutex.OpenExisting(name);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}
