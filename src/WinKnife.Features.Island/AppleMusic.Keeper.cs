// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;
using WinKnife.Core;

namespace WinKnife.Features.Island;

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
    private bool _noticed;
    private bool _disposed;

    public AppleMusicKeeper(IslandCore core, Func<AmCommand?> lastCommand)
    {
        _core = core;
        _lastCommand = lastCommand;
        _ui = core.Ui;
        Instance = this;
        _onCrash = (_, _) => ReleaseForExit();
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

        // a window an earlier run of WinKnife left transparent or parked (it was cut off in the middle of a job) is given back; off the UI thread: these are cross-process calls
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

    /// <summary>Greater than 0 while the driver works on Apple Music (its own show / hide / minimise events are none of the keeper's business).</summary>
    public int DriverDepth => Volatile.Read(ref _driverDepth);

    /// <summary>WinKnife itself is starting Apple Music (so a new Apple Music process is not the user's).</summary>
    public bool Launching => Volatile.Read(ref _launchers) > 0;

    /// <summary>
    /// A start of Apple Music by WinKnife begins. A count, not a flag: an abandoned job that ends its start late must not end the start of the
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
    /// A process WinKnife started (or one that showed its first window while WinKnife was starting Apple Music) is not the person's launch,
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
    /// The person opened Apple Music themselves (Start menu, taskbar, jump list, Alt+Tab): WinKnife neither hides nor minimises it again
    /// until the island next starts playback (<see cref="ClearUserOwned"/>).
    /// </summary>
    public bool UserOwned => _userOwned;

    public bool Enabled => _core.App.Get<bool>(K.AppleMusicKeepHidden);

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
    }

    private void Say(string message) => _core.Ctx.Log.Info("AppleMusic keeper: " + message);

    /// <summary>
    /// Apple Music was opened by the person (not by WinKnife): the keep ends and the window stays wherever they put it, whatever the driver
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

        if (kept != 0)
        {
            Persist("");
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
                    continue;
                }

                var theirs = AmNative.IsWindowVisible(h) && !AmNative.IsIconic(h) && !AmNative.IsGhost(h) && AmNative.GetWindowRect(h, out var r) && AmNative.OnAnyScreen(r);
                if (!theirs)
                {
                    Reveal(h);
                }
            }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Makes the window the person's again: moved back from where the driver parked it, its own style back (it is not transparent any more),
    /// shown when hidden or minimised. Bounded; retried in the background when Apple Music does not answer. Not for the UI thread.
    /// </summary>
    private void Reveal(nint hwnd)
    {
        Func<bool> reveal = () =>
        {
            AmNative.Unpark(hwnd);
            AmNative.RestoreStyleNow(hwnd);
            if (!AmNative.IsWindowVisible(hwnd) || AmNative.IsIconic(hwnd))
            {
                FixRect(hwnd);
                AmNative.ShowWindow(hwnd, AmNative.SwRestore);
            }

            return true;
        };
        if (!AmNative.Bounded(hwnd, reveal, 800, "showing the window", Say))
        {
            AmNative.RetryRestore(hwnd, reveal, "showing the window", Say);
        }
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

    /// <summary>WinKnife is about to quit Apple Music on purpose: a close within the next seconds is not resumed.</summary>
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

    /// <summary>Hides the window (when visible), remembers it and persists it for crash recovery.</summary>
    public void Adopt(nint hwnd, uint pid)
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

        lock (_gate)
        {
            _keptHwnd = hwnd;
            _keptPid = pid;
            _mains[hwnd] = pid;
        }

        if (AmNative.IsWindowVisible(hwnd))
        {
            AmNative.ShowWindowAsync(hwnd, AmNative.SwHide);
        }

        Persist($"{pid}:{(long)hwnd}");
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

            Persist("");
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
                Adopt(h, (uint)p.Id);
                return;
            }
        }
    }

    /// <summary>Brings the kept window back (and stops keeping it).</summary>
    public void Show(bool activate)
    {
        nint hwnd;
        lock (_gate)
        {
            hwnd = _keptHwnd;
            _keptHwnd = 0;
            _keptPid = 0;
        }

        Persist("");
        Interlocked.Exchange(ref _shownAt, Environment.TickCount64);
        if (hwnd != 0 && AmNative.IsWindow(hwnd))
        {
            Bring(hwnd, AmNative.SwRestore, activate);
        }

        Raise();
    }

    /// <summary>
    /// WinKnife is leaving (or the setting was turned off): the window goes back to the taskbar, minimised, and Apple Music keeps playing.
    /// Safe to call from any thread, also from the process-exit handlers.
    /// </summary>
    public void ReleaseForExit()
    {
        nint hwnd;
        lock (_gate)
        {
            hwnd = _keptHwnd;
            _keptHwnd = 0;
            _keptPid = 0;
        }

        try
        {
            Persist("");
            if (hwnd != 0 && AmNative.IsWindow(hwnd))
            {
                Bring(hwnd, AmNative.SwShowMinNoActive, false);
            }
        }
        catch
        {
            // leaving: nothing more to do
        }

        Raise();
    }

    /// <summary>Puts the window back on screen (<paramref name="command"/>): the restore position is corrected first, in one bounded step so a stuck Apple Music cannot stall the caller.</summary>
    private void Bring(nint hwnd, int command, bool activate)
    {
        Func<bool> show = () =>
        {
            if (AmNative.IsGhost(hwnd))
            {
                // a window left transparent and parked by the driver comes back as it was, not invisible
                AmNative.Unpark(hwnd);
                AmNative.RestoreStyleNow(hwnd);
            }

            FixRect(hwnd);
            AmNative.ShowWindow(hwnd, command);
            return true;
        };
        if (!AmNative.Bounded(hwnd, show, 800, "show window", Say))
        {
            AmNative.RetryRestore(hwnd, show, "show window", Say);
        }

        if (activate)
        {
            ForegroundGuard.Focus(hwnd);
        }
    }

    /// <summary>Never leave a window whose restore position is off-screen: it would come back invisible.</summary>
    private static void FixRect(nint hwnd)
    {
        var wp = new AmNative.WindowPlacement { Length = System.Runtime.InteropServices.Marshal.SizeOf<AmNative.WindowPlacement>() };
        if (!AmNative.GetWindowPlacement(hwnd, ref wp) || AmNative.OnAnyScreen(wp.Normal))
        {
            return;
        }

        var w = AmNative.GetSystemMetrics(0);
        var h = AmNative.GetSystemMetrics(1);
        var left = Math.Max(0, (w - 1200) / 2);
        var top = Math.Max(0, (h - 800) / 2);
        wp.Normal = new AmNative.Rect { Left = left, Top = top, Right = left + 1200, Bottom = top + 800 };
        wp.Flags = 0;
        if (!AmNative.IsWindowVisible(hwnd))
        {
            // a hidden window stays hidden while its restore position is corrected
            wp.ShowCmd = AmNative.SwHide;
        }

        AmNative.SetWindowPlacement(hwnd, ref wp);
    }

    private void Persist(string value)
    {
        try
        {
            _core.App.Set(K.AppleMusicKeptWindow, value);
        }
        catch
        {
            // the key only serves crash recovery
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

    /// <summary>A window hidden by a previous run that did not get to release it (crash): keep it again, or put it back in the taskbar.</summary>
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

        var parts = text.Split(':');
        if (parts.Length != 2 || !uint.TryParse(parts[0], out var pid) || !long.TryParse(parts[1], out var value))
        {
            Persist("");
            return;
        }

        var hwnd = (nint)value;
        if (!AmNative.IsWindow(hwnd) || AmNative.ProcessOf(hwnd) != pid || !AmNative.IsAppleMusic(pid) || AmNative.IsWindowVisible(hwnd))
        {
            Persist("");
            return;
        }

        if (Enabled)
        {
            Adopt(hwnd, pid);
        }
        else
        {
            lock (_gate)
            {
                _keptHwnd = hwnd;
                _keptPid = pid;
            }

            ReleaseForExit();
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => ReleaseForExit();

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => _ending = true;

    private void OnSetting(string key)
    {
        if (key is not (K.AppleMusicKeepHidden or K.AppleMusicResumeOnQuit))
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
        _hook?.Dispose();
        _hook = null;
        ReleaseForExit();
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
            // the first window of a process Apple Music did not have before: somebody started it (WinKnife's own starts are told apart by Launching)
            if (_seenPids.Count > 128)
            {
                _seenPids.Clear();
            }

            if (Launching)
            {
                // whatever process shows a window while WinKnife starts Apple Music is WinKnife's, also when its start phase is over by the time the rest turns up
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

        // Apple Music restores its own window a moment after WinKnife started it
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
    /// A process of Apple Music appeared that WinKnife did not start: the person opened Apple Music (Start menu, taskbar, pin, link), so its
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
                    _ = Task.Run(() => Bring(kept, AmNative.SwRestore, true));
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

                Persist("");
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
