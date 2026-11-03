// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Island;

/// <summary>What Apple Music was playing when its window was closed (and with it the music).</summary>
internal sealed record ClosedSnapshot(string Title, string Artist, string Album, double Position, double Duration, AmTrack? Track, AmPlaylist? Playlist, DateTime At);

/// <summary>
/// Keeps Apple Music's main window out of sight while the island plays it: the window is hidden (no taskbar button, no Alt+Tab entry)
/// and audio, media keys and the island's transport keep working. Closing Apple Music quits it (and stops the music), so the keeper also
/// notices a close while playing and lets the service start it again in the background. A single system-wide WinEvent hook feeds it.
/// Thread-safe: the driver's worker thread adopts windows, everything else runs on the UI thread.
/// </summary>
internal sealed class AppleMusicKeeper : IDisposable
{
    private const string MainClass = "WinUIDesktopWin32WindowClass";
    private const string HelperTitle = "WinUI Desktop";
    private static readonly TimeSpan PlayingGrace = TimeSpan.FromSeconds(2);

    private readonly IslandCore _core;
    private readonly Func<AmCommand?> _lastCommand;
    private readonly Dispatcher _ui;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<uint, (bool Am, long Tick)> _pids = new();
    private readonly Dictionary<nint, uint> _mains = [];
    private readonly HashSet<uint> _closing = [];
    private readonly ConcurrentDictionary<uint, long> _closedAt = new();
    private readonly ConcurrentDictionary<uint, byte> _seenPids = new();
    private readonly uint _own = (uint)Environment.ProcessId;
    private readonly UnhandledExceptionEventHandler _onCrash;
    private AmEventHook? _hook;
    private DispatcherTimer? _launchTimer;
    private int _driverDepth;
    private long _driverEnd;
    private long _shownAt;
    private long _expectQuitUntil;
    private long _launchEndedAt;
    private readonly ConcurrentDictionary<uint, long> _ownPids = new();
    private int _launchers;
    private volatile bool _userOwned;
    private volatile nint _keptHwnd;
    private volatile uint _keptPid;
    private volatile bool _ending;
    private volatile nint _pendingRelease;
    private readonly ConcurrentDictionary<nint, ReleaseJob> _releases = new();
    private DispatcherTimer? _pollTimer;
    private bool _noticed;
    private bool _disposed;

    public AppleMusicKeeper(IslandCore core, Func<AmCommand?> lastCommand)
    {
        _core = core;
        _lastCommand = lastCommand;
        _ui = core.Ui;
        Instance = this;
        _onCrash = (_, _) => ReleaseForExit(true);
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException += _onCrash;
        SystemEvents.SessionEnding += OnSessionEnding;
        core.App.Changed += OnSetting;
        foreach (var h in AmNative.MainWindows())
        {
            if (IsMainWindow(h))
            {
                _mains[h] = AmNative.ProcessOf(h);
            }
        }

        // processes that already run are nobody's new launch
        foreach (var p in Process.GetProcessesByName("AppleMusic"))
        {
            _seenPids[(uint)p.Id] = 0;
            p.Dispose();
        }

        Recover();
        Arm();

        // a window an earlier run of Trayloom left transparent or parked (it was cut off in the middle of a job) is given back; off the UI thread: these are cross-process calls
        _ = Task.Run(() =>
        {
            try
            {
                AmNative.Heal(Say);
            }
            catch
            {
                // a window that vanishes under it
            }
        });
    }

    /// <summary>The running keeper (read by <see cref="ForegroundGuard"/>).</summary>
    public static AppleMusicKeeper? Instance { get; private set; }

    /// <summary>The window being kept hidden (0 = none).</summary>
    public nint KeptHwnd => _keptHwnd;

    public uint KeptPid => _keptPid;

    /// <summary>
    /// A window whose release could not be verified within a minute (Apple Music did not take the show): it is still recorded for the window
    /// guardian, and the island offers "Show Apple Music" for it (0 = none).
    /// </summary>
    public nint PendingRelease => _pendingRelease;

    /// <summary>A main window of Apple Music that nobody can see although it is not minimised (hidden, transparent or parked): 0 = none.</summary>
    public nint StuckWindow() => AmNative.StuckMainWindow();

    /// <summary>Greater than 0 while the driver works on Apple Music (its own show / hide / minimise events are none of the keeper's business).</summary>
    public int DriverDepth => Volatile.Read(ref _driverDepth);

    /// <summary>Trayloom itself is starting Apple Music (so a new Apple Music process is not the user's).</summary>
    public bool Launching => Volatile.Read(ref _launchers) > 0;

    /// <summary>
    /// A start of Apple Music by Trayloom begins. A count, not a flag: an abandoned job that ends its start late must not end the start of the
    /// job that replaced it. Every <see cref="BeginLaunch"/> is paired with exactly one <see cref="EndLaunch"/>.
    /// </summary>
    public void BeginLaunch() => Interlocked.Increment(ref _launchers);

    public void EndLaunch()
    {
        if (Interlocked.Decrement(ref _launchers) < 0)
        {
            Interlocked.Exchange(ref _launchers, 0);
        }

        // Apple Music restores its own window a moment after its start: that is not the person either
        Interlocked.Exchange(ref _launchEndedAt, Environment.TickCount64);
    }

    /// <summary>
    /// A process Trayloom started (or one that showed its first window while Trayloom was starting Apple Music) is not the person's launch,
    /// whenever its windows turn up, also when the start phase has ended by then (30 s).
    /// </summary>
    public void NoteOwnLaunch(uint pid)
    {
        if (pid == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (_ownPids.Count > 32)
        {
            foreach (var (key, tick) in _ownPids)
            {
                if (now - tick >= 30000)
                {
                    _ownPids.TryRemove(key, out _);
                }
            }
        }

        _ownPids[pid] = now;
    }

    public bool IsOwnLaunch(uint pid) => _ownPids.TryGetValue(pid, out var tick) && Environment.TickCount64 - tick < 30000;

    /// <summary>
    /// The person opened Apple Music themselves (Start menu, taskbar, jump list, Alt+Tab): Trayloom neither hides nor minimises it again
    /// until the island next starts playback (<see cref="ClearUserOwned"/>).
    /// </summary>
    public bool UserOwned => _userOwned;

    /// <summary>Keeping Apple Music out of sight is on, and the island that does the keeping is on (nobody is there to bring the window back otherwise).</summary>
    public bool Enabled => _core.App.Get<bool>(K.AppleMusicKeepHidden) && _core.App.Get<bool>(K.Enabled);

    public bool ResumeEnabled => Enabled && _core.App.Get<bool>(K.AppleMusicResumeOnQuit);

    /// <summary>The driver is working or just finished (the foreground guard is still on watch for a moment).</summary>
    public bool DriverActive => DriverDepth > 0 || Environment.TickCount64 - Interlocked.Read(ref _driverEnd) < ForegroundGuard.GraceMs + 400;

    /// <summary>The window was shown by <see cref="Show"/> a moment ago (the foreground guard leaves it alone).</summary>
    public bool JustShown => Environment.TickCount64 - Interlocked.Read(ref _shownAt) < 3000;

    public event Action? Changed;

    /// <summary>Apple Music's window was closed (and the music stopped) while it was playing.</summary>
    public event Action<ClosedSnapshot>? ClosedWhilePlaying;

    public bool IsKept(nint hwnd) => hwnd != 0 && hwnd == _keptHwnd;

    public void EnterDriver() => Interlocked.Increment(ref _driverDepth);

    public void ExitDriver()
    {
        Interlocked.Exchange(ref _driverEnd, Environment.TickCount64);
        Interlocked.Decrement(ref _driverDepth);

        // the Music page leaves out its Show button while the driver is at work (DriverActive lasts a little longer than the job): it looks again after that
        _ = Task.Delay(ForegroundGuard.GraceMs + 450).ContinueWith(_ => Raise(), TaskScheduler.Default);
    }

    private void Say(string message) => _core.Ctx.Log.Info("AppleMusic keeper: " + message);

    /// <summary>
    /// Apple Music was opened by the person (not by Trayloom): the keep ends and the window stays wherever they put it, whatever the driver
    /// is doing. Safe to call from any thread.
    /// </summary>
    public void MarkUserOwned(string why)
    {
        if (_disposed || _userOwned)
        {
            return;
        }

        _userOwned = true;
        Say($"Apple Music opened by the user ({why}): it stays shown");
        nint kept;
        lock (_gate)
        {
            kept = _keptHwnd;
            _keptHwnd = 0;
            _keptPid = 0;
        }

        // also without a kept window: a window the driver has made transparent for its job (the first job after a start) is the person's now
        RevealLater(kept, 400);
        Raise();
    }

    /// <summary>The island is about to start playback itself: the window is the driver's to manage again (it works in place when it is on screen).</summary>
    public void ClearUserOwned()
    {
        if (_userOwned)
        {
            _userOwned = false;
            Say("user ownership cleared: the island plays again");
        }
    }

    /// <summary>
    /// The person opened Apple Music: after <paramref name="ms"/> the window the keeper had hidden (<paramref name="kept"/>) is shown if Apple
    /// Music did not do it itself, and any window the driver made transparent or parked for its job is given back (it may be on screen as far
    /// as Windows is concerned and still invisible to them).
    /// </summary>
    private void RevealLater(nint kept, int ms)
    {
        _ = Task.Delay(ms).ContinueWith(_ =>
        {
            if (_disposed)
            {
                return;
            }

            var targets = new HashSet<nint>();
            if (kept != 0)
            {
                targets.Add(kept);
            }

            foreach (var h in AmNative.MainWindows())
            {
                if (AmNative.IsGhost(h))
                {
                    targets.Add(h);
                }
            }

            foreach (var h in targets)
            {
                if (!AmNative.IsWindow(h))
                {
                    WindowGuardianState.UntrackWindow(h);
                    continue;
                }

                if (h == _keptHwnd)
                {
                    // kept again meanwhile (the island plays again): not the person's any more
                    continue;
                }

                var theirs = AmNative.IsWindowVisible(h) && !AmNative.IsIconic(h) && !AmNative.IsGhost(h) && AmNative.GetWindowRect(h, out var r) && AmNative.Comfortable(h, r);
                if (theirs)
                {
                    // on screen and the person's: nothing is owed any more
                    WindowGuardianState.UntrackWindow(h);
                }
                else
                {
                    // forced: the window is the person's whatever the driver is doing (its job gives way, see HiddenWindow.Restore)
                    BeginRelease(h, AmNative.SwRestore, false, false, "the person opened Apple Music", true);
                }
            }
        }, TaskScheduler.Default);
    }

    /// <summary>A process of Apple Music has just lost its main window or is being waited for to end (it is quitting, not starting).</summary>
    public bool RecentlyClosed(uint pid) => _closedAt.TryGetValue(pid, out var tick) && Environment.TickCount64 - tick < 10000;

    private void NoteClosed(uint pid)
    {
        if (_closedAt.Count > 64)
        {
            _closedAt.Clear();
        }

        _closedAt[pid] = Environment.TickCount64;
    }

    /// <summary>Trayloom is about to quit Apple Music on purpose: a close within the next seconds is not resumed.</summary>
    public void ExpectQuit() => Interlocked.Exchange(ref _expectQuitUntil, Environment.TickCount64 + 15000);

    /// <summary>An Apple Music process is running.</summary>
    public static bool IsRunning() => Process.GetProcessesByName("AppleMusic").Length > 0;

    /// <summary>Apple Music has a main window that is on screen or in the taskbar (not hidden by the keeper, not parked or transparent).</summary>
    public bool HasVisibleWindow()
    {
        foreach (var p in Process.GetProcessesByName("AppleMusic"))
        {
            var h = AmNative.MainWindow((uint)p.Id, _keptHwnd);
            if (h != 0 && h != _keptHwnd && AmNative.IsWindowVisible(h) && !AmNative.IsGhost(h))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- keeping

    /// <summary>
    /// Hides the window (when visible), remembers it and persists it for crash recovery. Refused (the window is given back, minimised) when
    /// keeping is off by now, e.g. the island was switched off while a job was under way, unless <paramref name="explicitly"/> (the person's Hide).
    /// </summary>
    public void Adopt(nint hwnd, uint pid, bool explicitly = false)
    {
        if (hwnd == 0 || !AmNative.IsWindow(hwnd))
        {
            return;
        }

        if (_userOwned)
        {
            // the person has Apple Music open: nothing is hidden or kept (a window the driver hid a moment ago is shown again)
            Say("not kept: the person has Apple Music open");
            RevealLater(hwnd, 0);
            return;
        }

        if (!explicitly && !Enabled)
        {
            // keeping was turned off (or the island switched off) while the driver was at work: nobody would bring this window back
            Say($"not kept 0x{hwnd:X}: keeping Apple Music out of sight is off; it goes back to the taskbar");
            BeginRelease(hwnd, AmNative.SwShowMinNoActive, false, false, "keeping is off");
            return;
        }

        if (_releases.TryGetValue(hwnd, out var asked) && asked.Activate)
        {
            // the person pressed "Show Apple Music" (or opened it) while the driver was at work: the release goes on once the job is over
            Say($"not kept 0x{hwnd:X}: the person asked for the window");
            return;
        }

        // any other release of this window that is still under way would undo the keep: it ends here
        CancelRelease(hwnd);
        lock (_gate)
        {
            _keptHwnd = hwnd;
            _keptPid = pid;
            _mains[hwnd] = pid;
        }

        // recorded on disk before the window is hidden: whatever happens to Trayloom from here on, the guardian brings it back
        var home = AmNative.SafeHome(hwnd);
        AmNative.TrackKept(hwnd, pid, home);
        WindowGuardianState.Untrack(hwnd, WindowGuardianState.Releasing);
        Say($"keeping 0x{hwnd:X}: {AmNative.RectText(hwnd)}; comes back to [{home.Left},{home.Top},{home.Right},{home.Bottom}]");
        if (AmNative.IsWindowVisible(hwnd))
        {
            AmNative.ShowWindowAsync(hwnd, AmNative.SwHide);
        }

        StartPoll();
        if (_userOwned)
        {
            // opened by the person while this was under way (MarkUserOwned has not seen this window as kept)
            lock (_gate)
            {
                if (_keptHwnd == hwnd)
                {
                    _keptHwnd = 0;
                    _keptPid = 0;
                }
            }

            RevealLater(hwnd, 0);
        }

        Arm();
        Raise();
    }

    /// <summary>Hides Apple Music's main window now (the kept one again, or the visible one, which is then kept).</summary>
    public void HideNow()
    {
        // the person asked for this: it ends their ownership of the window (Adopt would refuse otherwise)
        ClearUserOwned();
        var kept = _keptHwnd;
        if (kept != 0 && AmNative.IsWindow(kept))
        {
            if (AmNative.IsWindowVisible(kept))
            {
                AmNative.ShowWindowAsync(kept, AmNative.SwHide);
            }

            Raise();
            return;
        }

        foreach (var p in Process.GetProcessesByName("AppleMusic"))
        {
            var h = AmNative.MainWindow((uint)p.Id);
            if (h != 0 && AmNative.IsWindowVisible(h) && !AmNative.IsGhost(h))
            {
                Adopt(h, (uint)p.Id, true);
                return;
            }
        }
    }

    /// <summary>Brings the kept window back (and stops keeping it); with nothing kept, the window a failed release left or any hidden or transparent one.</summary>
    public void Show(bool activate)
    {
        nint hwnd;
        lock (_gate)
        {
            hwnd = _keptHwnd;
            _keptHwnd = 0;
            _keptPid = 0;
        }

        if (hwnd == 0)
        {
            hwnd = _pendingRelease;
        }

        if (hwnd == 0 && !DriverActive)
        {
            // not while the driver works: its window is transparent or parked on purpose, and showing it would break the job
            hwnd = AmNative.StuckMainWindow();
        }

        Interlocked.Exchange(ref _shownAt, Environment.TickCount64);
        if (hwnd != 0 && AmNative.IsWindow(hwnd))
        {
            BeginRelease(hwnd, AmNative.SwRestore, activate, false, "Show Apple Music");
        }

        Raise();
    }

    /// <summary>
    /// Trayloom is leaving (or the setting was turned off, or the island was switched off): the window goes back to the taskbar, minimised, and
    /// Apple Music keeps playing. Safe to call from any thread, also from the process-exit handlers. <paramref name="leaving"/>: the first
    /// attempt is made here (bounded) and ignores a running driver job; what that does not settle goes on in the background, and what is still
    /// open at exit stays recorded for the window guardian.
    /// </summary>
    public void ReleaseForExit(bool leaving = false)
    {
        nint hwnd;
        lock (_gate)
        {
            hwnd = _keptHwnd;
            _keptHwnd = 0;
            _keptPid = 0;
        }

        if (hwnd == 0)
        {
            hwnd = _pendingRelease;
        }

        try
        {
            if (hwnd != 0 && AmNative.IsWindow(hwnd))
            {
                // leaving: now, on this thread, whatever the driver is doing; otherwise on a pool thread, after the driver's job (a job
                // in flight is not taken apart under its feet, and its end Adopts nothing any more since keeping is off)
                BeginRelease(hwnd, AmNative.SwShowMinNoActive, false, leaving, leaving ? "Trayloom is leaving" : "the keep was turned off", leaving);
            }
        }
        catch
        {
            // leaving: nothing more to do
        }

        Raise();
    }

    // ---------------------------------------------------------------- releasing

    /// <summary>What a release in progress wants (it may be changed by a later request for the same window).</summary>
    private sealed class ReleaseJob(int command, bool activate, bool force)
    {
        public volatile int Command = command;
        public volatile bool Activate = activate;

        /// <summary>Trayloom is leaving: the release does not wait for a driver job.</summary>
        public volatile bool Force = force;

        /// <summary>The window was taken over again (kept, or by a start of Apple Music): the release must not touch it any more.</summary>
        public volatile bool Cancelled;
    }

    /// <summary>
    /// Ends a release in progress for the window: it was kept again (or taken for a start), and a release that went on would show it. The
    /// "releasing" record is the caller's to replace.
    /// </summary>
    public void CancelRelease(nint hwnd)
    {
        if (_releases.TryRemove(hwnd, out var job))
        {
            job.Cancelled = true;
        }

        if (_pendingRelease == hwnd)
        {
            _pendingRelease = 0;
        }
    }

    private void RemoveJob(nint hwnd, ReleaseJob job) => ((ICollection<KeyValuePair<nint, ReleaseJob>>)_releases).Remove(new KeyValuePair<nint, ReleaseJob>(hwnd, job));

    /// <summary>The release has been overtaken: cancelled, or the window is kept again.</summary>
    private bool Superseded(nint hwnd, ReleaseJob job) => job.Cancelled || _keptHwnd == hwnd;

    /// <summary>A driver job works on the window (it is invisible by design); a release waits for it unless Trayloom is leaving.</summary>
    private bool WaitForDriver(ReleaseJob job) => !job.Force && DriverDepth > 0;

    /// <summary>
    /// Gives a window back and does not stop until that is verified (visible, opaque, on screen) or a minute has passed. The record for the
    /// window guardian changes to "releasing" first and is removed only after the verification; a release that fails leaves
    /// <see cref="PendingRelease"/> set and the record in place. <paramref name="sync"/>: the first attempt is made on this thread (bounded).
    /// A release does not touch the window while a driver job works on it (unless <paramref name="force"/>: Trayloom is leaving) and ends
    /// quietly when the window is kept again meanwhile (<see cref="CancelRelease"/>).
    /// </summary>
    private void BeginRelease(nint hwnd, int command, bool activate, bool sync, string why, bool force = false)
    {
        var job = new ReleaseJob(command, activate, force);
        if (!_releases.TryAdd(hwnd, job))
        {
            if (_releases.TryGetValue(hwnd, out var running))
            {
                running.Command = command;
                running.Activate = running.Activate || activate;
                running.Force = running.Force || force;
                if (sync && TryRelease(hwnd, running))
                {
                    FinishRelease(hwnd, running, "verified");
                }
            }

            return;
        }

        var pid = AmNative.ProcessOf(hwnd);
        var home = AmNative.SafeHome(hwnd);
        WindowGuardianState.Track(hwnd, pid, AmNative.AppleMusicProcess, AmNative.MainWindowClass, WindowGuardianState.Releasing, null, [home.Left, home.Top, home.Right, home.Bottom]);
        if (_keptHwnd != hwnd)
        {
            WindowGuardianState.Untrack(hwnd, WindowGuardianState.Kept);
        }

        Say($"releasing 0x{hwnd:X} ({why}): before {AmNative.RectText(hwnd)}");
        if (sync && TryRelease(hwnd, job))
        {
            FinishRelease(hwnd, job, "verified");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ReleaseLoop(hwnd, job, sync);
            }
            catch (Exception ex)
            {
                RemoveJob(hwnd, job);
                _core.Ctx.Log.Error("AppleMusic keeper: release failed", ex);
            }
        });
    }

    private async Task ReleaseLoop(nint hwnd, ReleaseJob job, bool firstDone)
    {
        var until = Environment.TickCount64 + 60000;
        var waitedForDriver = 0;
        var delay = 250;
        if (firstDone)
        {
            await Task.Delay(delay).ConfigureAwait(false);
        }

        while (true)
        {
            if (Superseded(hwnd, job))
            {
                RemoveJob(hwnd, job);
                Say($"release of 0x{hwnd:X} ended: the window is kept again or taken for a start");
                return;
            }

            if (!AmNative.IsWindow(hwnd))
            {
                FinishRelease(hwnd, job, "window is gone");
                return;
            }

            if (WaitForDriver(job) && waitedForDriver < 300000)
            {
                // the driver's job gives the window back itself; the minute starts to run once it is over (bounded: a job that never ends)
                waitedForDriver += delay;
                until += delay;
                await Task.Delay(delay).ConfigureAwait(false);
                continue;
            }

            if (TryRelease(hwnd, job))
            {
                FinishRelease(hwnd, job, "verified");
                return;
            }

            if (Environment.TickCount64 > until)
            {
                RemoveJob(hwnd, job);
                _pendingRelease = hwnd;
                Say($"release of 0x{hwnd:X} gave up after 60 s: {AmNative.RectText(hwnd)}; it stays recorded for the window guardian");
                Raise();
                return;
            }

            await Task.Delay(delay).ConfigureAwait(false);
            delay = Math.Min(delay * 2, 2000);
        }
    }

    /// <summary>One bounded attempt: its own style back, off the parking spot, a place on screen, shown. True only when the window is then verified.</summary>
    private bool TryRelease(nint hwnd, ReleaseJob job)
    {
        if (Superseded(hwnd, job) || WaitForDriver(job))
        {
            return false;
        }

        var command = job.Command;
        Func<bool> show = () =>
        {
            // checked again on the thread that makes the calls: the keeper or the driver may have taken the window back meanwhile
            if (Superseded(hwnd, job) || WaitForDriver(job))
            {
                return false;
            }

            AmNative.Unpark(hwnd);
            if (AmNative.IsGhost(hwnd) || AmNative.HasStyleRecord(hwnd))
            {
                AmNative.RestoreStyleNow(hwnd);
            }

            AmNative.FixPlacement(hwnd);
            var visible = AmNative.IsWindowVisible(hwnd);
            var iconic = AmNative.IsIconic(hwnd);
            if (command == AmNative.SwRestore ? !visible || iconic : !(visible && iconic))
            {
                AmNative.ShowWindow(hwnd, command);
            }

            return true;
        };
        AmNative.Bounded(hwnd, show, 800, "showing the window", Say);
        return Verified(hwnd);
    }

    private static bool Verified(nint hwnd) =>
        AmNative.IsWindow(hwnd) && AmNative.IsWindowVisible(hwnd) && !AmNative.IsGhost(hwnd) && (AmNative.IsIconic(hwnd) || (AmNative.GetWindowRect(hwnd, out var r) && AmNative.Comfortable(hwnd, r)));

    private void FinishRelease(nint hwnd, ReleaseJob job, string how)
    {
        var gone = !AmNative.IsWindow(hwnd);
        if (!gone && Superseded(hwnd, job))
        {
            // the window was taken back while the last attempt ran: its records are the new owner's now
            RemoveJob(hwnd, job);
            Say($"release of 0x{hwnd:X} ended: the window is kept again or taken for a start");
            return;
        }

        if (gone)
        {
            WindowGuardianState.UntrackWindow(hwnd);
        }
        else
        {
            // only what the release itself recorded: a newer record of another kind (kept, parked, ghost) belongs to whoever took the window back
            WindowGuardianState.Untrack(hwnd, WindowGuardianState.Releasing);
            if (_keptHwnd != hwnd)
            {
                WindowGuardianState.Untrack(hwnd, WindowGuardianState.Kept);
            }
        }

        RemoveJob(hwnd, job);
        if (_pendingRelease == hwnd)
        {
            _pendingRelease = 0;
        }

        Say($"released 0x{hwnd:X} ({how}): {AmNative.RectText(hwnd)}");
        if (job.Activate && AmNative.IsWindow(hwnd))
        {
            ForegroundGuard.Focus(hwnd);
        }

        Raise();
    }

    // ---------------------------------------------------------------- the person's launches

    private void StartPoll()
    {
        if (_pollTimer is not null || _disposed)
        {
            return;
        }

        // The window events miss a launch whose second process ends before it creates a window: while a window is kept, new Apple Music
        // processes are also looked for once a second.
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += (_, _) =>
        {
            if (_disposed || _keptHwnd == 0)
            {
                _pollTimer?.Stop();
                _pollTimer = null;
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    var found = new List<uint>();
                    foreach (var process in Process.GetProcessesByName("AppleMusic"))
                    {
                        found.Add((uint)process.Id);
                        process.Dispose();
                    }

                    _ui.BeginInvoke(() =>
                    {
                        foreach (var pid in found)
                        {
                            if (!_disposed && _keptHwnd != 0 && pid != _keptPid && !Launching && !IsOwnLaunch(pid) && _seenPids.TryAdd(pid, 0))
                            {
                                OnLaunched(pid);
                            }
                        }
                    });
                }
                catch
                {
                    // the next tick looks again
                }
            });
        };
        _pollTimer.Start();
    }

    private void Persist(string value)
    {
        try
        {
            _core.App.Set(K.AppleMusicKeptWindow, value);
        }
        catch
        {
            // the key is a leftover of older versions; the guardian state replaced it
        }
    }

    private void Raise()
    {
        if (_disposed)
        {
            return;
        }

        if (_ui.CheckAccess())
        {
            Changed?.Invoke();
        }
        else
        {
            _ui.BeginInvoke(() =>
            {
                if (!_disposed)
                {
                    Changed?.Invoke();
                }
            });
        }
    }

    // ---------------------------------------------------------------- lifetime

    /// <summary>
    /// Older versions recorded the hidden window in a setting (written with a delay, so a kill lost it); the window guardian state replaced it. A
    /// leftover value is cleared, and a window it still names that is hidden is given back (never kept again).
    /// </summary>
    private void Recover()
    {
        string text;
        try
        {
            text = _core.App.Get<string>(K.AppleMusicKeptWindow) ?? "";
        }
        catch
        {
            return;
        }

        if (text.Length == 0)
        {
            return;
        }

        Persist("");
        var parts = text.Split(':');
        if (parts.Length != 2 || !uint.TryParse(parts[0], out var pid) || !long.TryParse(parts[1], out var value))
        {
            return;
        }

        var hwnd = (nint)value;
        if (AmNative.IsWindow(hwnd) && AmNative.ProcessOf(hwnd) == pid && AmNative.IsAppleMusic(pid) && !AmNative.IsWindowVisible(hwnd))
        {
            BeginRelease(hwnd, AmNative.SwShowMinNoActive, false, false, "left hidden by an earlier version");
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => ReleaseForExit(true);

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => _ending = true;

    private void OnSetting(string key)
    {
        if (key is not (K.AppleMusicKeepHidden or K.AppleMusicResumeOnQuit or K.Enabled))
        {
            return;
        }

        _ui.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (!Enabled)
            {
                ReleaseForExit();
            }

            Arm();
        });
    }

    /// <summary>Starts the event hook while the feature has work for it, stops it otherwise.</summary>
    private void Arm()
    {
        if (_disposed)
        {
            return;
        }

        var wanted = Enabled && (File.Exists(AmDb.AliasPath) || File.Exists(AmDb.LibraryPath));
        if (wanted && _hook is null)
        {
            try
            {
                _hook = new AmEventHook(AmNative.EventSystemMinimizeStart, AmNative.EventObjectHide, 0, OnHook);
            }
            catch
            {
                _hook = null;
            }
        }
        else if (!wanted && _hook is not null)
        {
            _hook.Dispose();
            _hook = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException -= _onCrash;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _core.App.Changed -= OnSetting;
        _launchTimer?.Stop();
        _pollTimer?.Stop();
        _hook?.Dispose();
        _hook = null;
        ReleaseForExit(true);
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    // ---------------------------------------------------------------- events

    private bool IsAppleMusicPid(uint pid)
    {
        var now = Environment.TickCount64;
        if (_pids.TryGetValue(pid, out var hit) && now - hit.Tick < 30000)
        {
            return hit.Am;
        }

        var am = AmNative.IsAppleMusic(pid);
        _pids[pid] = (am, now);
        if (_pids.Count > 512)
        {
            _pids.Clear();
        }

        return am;
    }

    private static bool IsMainWindow(nint hwnd)
    {
        if (AmNative.ClassOf(hwnd) != MainClass)
        {
            return false;
        }

        var title = AmNative.TitleOf(hwnd);
        return title.Length > 0 && title != HelperTitle;
    }

    /// <summary>Runs on the hook thread for every create / destroy / show / hide / minimise-start of any window: cheap filters first.</summary>
    private void OnHook(uint ev, nint hwnd)
    {
        if (ev is not (AmNative.EventSystemMinimizeStart or AmNative.EventObjectCreate or AmNative.EventObjectDestroy or AmNative.EventObjectShow or AmNative.EventObjectHide))
        {
            return;
        }

        var pid = AmNative.ProcessOf(hwnd);
        if (pid == 0 && ev == AmNative.EventObjectDestroy)
        {
            lock (_gate)
            {
                _mains.TryGetValue(hwnd, out pid);
            }
        }

        if (pid == 0 || pid == _own || !IsAppleMusicPid(pid))
        {
            return;
        }

        if (ev == AmNative.EventObjectCreate)
        {
            // the first window of a process Apple Music did not have before: somebody started it (Trayloom's own starts are told apart by Launching)
            if (_seenPids.Count > 128)
            {
                _seenPids.Clear();
            }

            if (Launching)
            {
                // whatever process shows a window while Trayloom starts Apple Music is Trayloom's, also when its start phase is over by the time the rest turns up
                NoteOwnLaunch(pid);
            }

            if (_seenPids.TryAdd(pid, 0) && !Launching && pid != _keptPid && !IsOwnLaunch(pid))
            {
                _ui.BeginInvoke(() => OnLaunched(pid));
            }

            return;
        }

        var known = false;
        lock (_gate)
        {
            known = _mains.ContainsKey(hwnd);
        }

        if (!known && ev != AmNative.EventObjectDestroy)
        {
            if (!IsMainWindow(hwnd))
            {
                return;
            }

            lock (_gate)
            {
                _mains[hwnd] = pid;
            }

            known = true;
        }

        if (!known && hwnd != _keptHwnd)
        {
            return;
        }

        switch (ev)
        {
            case AmNative.EventObjectShow:
                if (hwnd == _keptHwnd)
                {
                    _ui.BeginInvoke(() => OnShown(hwnd));
                }

                break;
            case AmNative.EventSystemMinimizeStart:
                _ui.BeginInvoke(() => OnMinimized(hwnd, pid));
                break;
            case AmNative.EventObjectHide:
                _ui.BeginInvoke(() => OnGone(hwnd, pid, false));
                break;
            case AmNative.EventObjectDestroy:
                NoteClosed(pid);
                _ui.BeginInvoke(() => OnGone(hwnd, pid, true));
                break;
        }
    }

    /// <summary>
    /// Somebody else showed the kept window: it is not kept any more (never fight the user). The driver's own shows are always transparent
    /// or parked far off-screen, so a window that is neither is the person's, also while the driver is at work.
    /// </summary>
    private void OnShown(nint hwnd)
    {
        if (_disposed || hwnd != _keptHwnd || JustShown || !AmNative.IsWindowVisible(hwnd) || AmNative.IsIconic(hwnd) || AmNative.IsGhost(hwnd))
        {
            return;
        }

        // Apple Music restores its own window a moment after Trayloom started it
        if (Launching || Environment.TickCount64 - Interlocked.Read(ref _launchEndedAt) < 3000)
        {
            return;
        }

        if (!AmNative.GetWindowRect(hwnd, out var rect) || rect.Left <= -10000)
        {
            return;
        }

        MarkUserOwned("shown");
    }

    /// <summary>
    /// A process of Apple Music appeared that Trayloom did not start: the person opened Apple Music (Start menu, taskbar, pin, link), so its
    /// window is theirs. The second process of that kind usually ends again at once, after handing over to the running one.
    /// </summary>
    private void OnLaunched(uint pid)
    {
        if (_disposed || Launching || pid == _keptPid || IsOwnLaunch(pid))
        {
            return;
        }

        try
        {
            using var other = Process.GetProcessById((int)pid);
            if (DateTime.Now - other.StartTime > TimeSpan.FromSeconds(20))
            {
                // an old process showing a window for the first time (the keeper started after it): not a launch
                return;
            }
        }
        catch
        {
            // already gone again: it was a launch that handed over to the running instance
        }

        var kept = _keptHwnd;
        MarkUserOwned("launched");
        if (kept != 0 && _launchTimer is null)
        {
            // Apple Music normally shows its running window itself; after a moment it is shown and activated if it did not
            _launchTimer = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromMilliseconds(300) };
            _launchTimer.Tick += (_, _) =>
            {
                _launchTimer?.Stop();
                _launchTimer = null;
                if (!_disposed && !Launching && AmNative.IsWindow(kept) && !AmNative.IsWindowVisible(kept))
                {
                    JustShownNow();
                    BeginRelease(kept, AmNative.SwRestore, true, false, "Apple Music was launched by the person", true);
                }
            };
            _launchTimer.Start();
        }
    }

    private void JustShownNow() => Interlocked.Exchange(ref _shownAt, Environment.TickCount64);

    /// <summary>The user minimised Apple Music while it plays: it goes to the island instead of the taskbar.</summary>
    private void OnMinimized(nint hwnd, uint pid)
    {
        if (_disposed || _userOwned || !Enabled || DriverActive || hwnd == _keptHwnd || AmNative.MainWindow(pid) != hwnd || _core.Playback.AppleMusicNow() is not { IsPlaying: true })
        {
            return;
        }

        var timer = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_disposed || _userOwned || !Enabled || DriverActive || !AmNative.IsWindow(hwnd) || !AmNative.IsIconic(hwnd) || _keptHwnd != 0)
            {
                return;
            }

            Adopt(hwnd, pid);
            if (!_noticed)
            {
                _noticed = true;
                _core.ShowNotice(new Notice { Event = NoticeEvents.AppleMusic, Glyph = G.Music, Title = LibStrings.KeptTitle, Detail = LibStrings.KeptDetail });
            }
        };
        timer.Start();
    }

    /// <summary>A main window was hidden or destroyed.</summary>
    private void OnGone(nint hwnd, uint pid, bool destroyed)
    {
        if (_disposed)
        {
            return;
        }

        if (hwnd == _keptHwnd)
        {
            if (destroyed && !DriverActive)
            {
                // the hidden window is gone (Apple Music was ended from outside): nothing is kept any more, and nobody asked for the music
                lock (_gate)
                {
                    _keptHwnd = 0;
                    _keptPid = 0;
                }

                WindowGuardianState.UntrackWindow(hwnd);
                Raise();
            }

            return;
        }

        if (DriverActive || !Enabled || _closing.Contains(pid))
        {
            return;
        }

        if (!_core.Playback.AppleMusicPlayedWithin(PlayingGrace) || _core.Playback.AppleMusicNow() is not { } now)
        {
            return;
        }

        var last = _lastCommand();
        var snapshot = new ClosedSnapshot(now.Title, now.Artist, now.Album, now.Position, now.Duration, last?.Track, last?.Playlist, DateTime.UtcNow);
        _closing.Add(pid);
        NoteClosed(pid);
        _ = Task.Run(() =>
        {
            var exited = false;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                exited = process.WaitForExit(3000);
            }
            catch (ArgumentException)
            {
                exited = true;
            }
            catch
            {
                // cannot tell
            }

            _ui.BeginInvoke(() =>
            {
                _closing.Remove(pid);
                if (_disposed || !exited || _ending || Environment.HasShutdownStarted || Environment.TickCount64 < Interlocked.Read(ref _expectQuitUntil))
                {
                    return;
                }

                ClosedWhilePlaying?.Invoke(snapshot);
            });
        });
    }
}
