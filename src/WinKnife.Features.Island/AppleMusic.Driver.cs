// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Automation;
using Windows.Media.Control;

namespace WinKnife.Features.Island;

internal enum AmAction
{
    /// <summary>Play the song now.</summary>
    Play,
    PlayNext,
    AddToQueue,
    PlayPlaylist,
    ShufflePlaylist,
    ReadQueue,

    /// <summary>Apple Music's own Play/Pause button (resumes what it had loaded; starts the app hidden when it is closed).</summary>
    PlayPause,
    SkipForward,
    SkipBack,

    /// <summary>Sets Apple Music's own scrubber to <see cref="AmCommand.SeekTo"/> (its media session ignores a seek request).</summary>
    Seek,
}

internal enum AmFailure
{
    None,

    /// <summary>Apple Music could not be started or its window did not show up.</summary>
    Unavailable,

    /// <summary>The song or playlist was not found in Apple Music's lists.</summary>
    NotFound,

    /// <summary>The song's menu or the page's button did not appear.</summary>
    NoMenu,

    /// <summary>Apple Music took the command but GSMTC never showed the song.</summary>
    NotConfirmed,

    Cancelled,
    Failed,
}

/// <summary>One thing to do in Apple Music. For a song, <see cref="Playlist"/> is the playlist page it is listed on (null = the library's Songs / Recently added page).</summary>
internal sealed class AmCommand
{
    public required AmAction Action { get; init; }

    public AmTrack? Track { get; init; }

    public AmPlaylist? Playlist { get; init; }

    /// <summary>"songs" or "recent" when no playlist is given.</summary>
    public string Page { get; init; } = "songs";

    public ulong DbId { get; init; }

    public string PlaylistAutomationId { get; init; } = "";

    /// <summary>After a song or playlist has been started: scrub to this position (seconds). Used to continue where a closed Apple Music stopped.</summary>
    public double? ResumeAt { get; init; }

    /// <summary>The position (seconds) of <see cref="AmAction.Seek"/>.</summary>
    public double SeekTo { get; init; }
}

internal sealed record AmQueueItem(string Title, string Subtitle, string Duration, bool Current);

internal sealed class AmResult
{
    public bool Ok { get; init; }

    public AmFailure Failure { get; init; }

    public string Detail { get; init; } = "";

    public IReadOnlyList<AmQueueItem> Queue { get; init; } = [];

    public long Milliseconds { get; set; }

    public static AmResult Success() => new() { Ok = true };

    public static AmResult Fail(AmFailure failure, string detail = "") => new() { Failure = failure, Detail = detail };
}

/// <summary>
/// Drives Apple Music for Windows (a WinUI 3 app without any API) through UI Automation patterns only — never synthetic input.
/// The window is invisible and parked far off-screen while it is used, then minimised again; the previous foreground window is restored.
/// All UIA work runs on one dedicated worker thread; a request replaces any pending one and cancels the one in progress at its next checkpoint.
/// </summary>
internal sealed class AppleMusicDriver : IDisposable
{
    private const string WindowClass = "WinUIDesktopWin32WindowClass";
    private const string PopupClass = "Microsoft.UI.Content.PopupWindowSiteBridge";

    /// <summary>The watchdog fires this long after the job's budget has run out.</summary>
    private const int WatchdogSlackMs = 3000;

    private sealed class Job(AmCommand command)
    {
        public AmCommand Command { get; } = command;

        public TaskCompletionSource<AmResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenSource Cancel { get; } = new();

        /// <summary>The foreground guard of the job (the watchdog drops its hook when the worker is abandoned).</summary>
        public ForegroundGuard? Guard { get; set; }

        /// <summary>Why the job was cancelled (logged as its result): "timeout" or "superseded by …".</summary>
        public volatile string? CancelReason;

        /// <summary>Set (by Interlocked) once the keeper has been told that the driver left Apple Music alone.</summary>
        public int DriverExited;

        /// <summary>Set (by Interlocked) once the job's "result:" line is in the log: a request has exactly one.</summary>
        public int ResultLogged;

        /// <summary>1 while the job holds a start of Apple Music open at the keeper (by Interlocked: the worker and the watchdog both may end it, once).</summary>
        public int Launching;

        /// <summary>The budget (ms) while Apple Music is running, and the longer one that applies after it had to be started.</summary>
        public int WarmMs { get; } = BudgetFor(command, false);

        public int ColdMs { get; } = BudgetFor(command, true);

        /// <summary>The budget in force now (the watchdog fires <see cref="WatchdogSlackMs"/> after it).</summary>
        public int BudgetMs { get; private set; } = BudgetFor(command, false);

        public Timer? Watchdog { get; set; }

        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        /// <summary>Apple Music has to be started: the longer budget applies from now on (counted from the job's start).</summary>
        public void ExtendBudget()
        {
            if (BudgetMs >= ColdMs)
            {
                return;
            }

            BudgetMs = ColdMs;
            try
            {
                Watchdog?.Change(Math.Max(1000, ColdMs + WatchdogSlackMs - (int)Clock.ElapsedMilliseconds), Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static int BudgetFor(AmCommand c, bool cold) => c.Action switch
        {
            AmAction.ReadQueue => 10000,
            AmAction.Seek => 6000,
            AmAction.PlayPause or AmAction.SkipForward or AmAction.SkipBack => cold ? 20000 : 6000,
            _ => cold ? 30000 : 15000,
        };
    }

    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Action<string> _log;
    private Thread? _thread;
    private Thread? _abandoned;
    private Job? _pending;
    private Job? _running;
    private bool _disposed;

    // a worker whose job hung is abandoned: the generation moves on and the old thread exits as soon as it gets control back
    private int _generation;

    private readonly AppleMusicKeeper? _keeper;

    public AppleMusicDriver(Action<string>? log = null, AppleMusicKeeper? keeper = null)
    {
        _log = log ?? (_ => { });
        _keeper = keeper;
    }

    /// <summary>Raised from the worker thread for every diagnostic line (tests print them).</summary>
    public event Action<string>? Trace;

    public Task<AmResult> RunAsync(AmCommand command)
    {
        var job = new Job(command);
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.FromResult(AmResult.Fail(AmFailure.Cancelled));
            }

            // reading the queue never interrupts or replaces a play request
            if (command.Action == AmAction.ReadQueue && (_pending is { Command.Action: not AmAction.ReadQueue } || _running is { Command.Action: not AmAction.ReadQueue }))
            {
                Say("result: Cancelled (ReadQueue skipped: Apple Music is busy with another request)");
                return Task.FromResult(AmResult.Fail(AmFailure.Cancelled, "busy"));
            }

            if (_pending is { } replaced && replaced.Done.TrySetResult(AmResult.Fail(AmFailure.Cancelled, "superseded")))
            {
                // it never got to run: its result line is this one
                LogResult(replaced, $"result: Cancelled ({replaced.Command.Action} superseded by {command.Action} before it started)");
            }

            _pending = job;
            if (_running is { } running)
            {
                running.CancelReason ??= "superseded by " + command.Action;
                running.Cancel.Cancel();
            }

            StartWorker();
        }

        _wake.Set();
        return job.Done.Task;
    }

    /// <summary>Starts the worker thread of the current generation when there is none (the caller holds <see cref="_gate"/>).</summary>
    private void StartWorker()
    {
        if (_thread is not null || _disposed)
        {
            return;
        }

        var generation = _generation;
        var predecessor = _abandoned;
        _abandoned = null;
        _thread = new Thread(() => Worker(generation, predecessor)) { IsBackground = true, Name = "WinKnife AppleMusic UIA" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            if (_pending is { } waiting && waiting.Done.TrySetResult(AmResult.Fail(AmFailure.Cancelled)))
            {
                LogResult(waiting, $"result: Cancelled ({waiting.Command.Action}; the island is closing)");
            }

            _pending = null;
            if (_running is { } active)
            {
                active.CancelReason ??= "the island is closing";
                active.Cancel.Cancel();
            }
        }

        _wake.Set();
    }

    private void Say(string message)
    {
        var line = $"[{_clock.ElapsedMilliseconds,6} ms] {message}";
        _log(line);
        Trace?.Invoke(line);
    }

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>Logs the job's "result:" line, once: later lines of an abandoned or cancelled worker for the same request are dropped.</summary>
    private void LogResult(Job job, string line)
    {
        if (Interlocked.Exchange(ref job.ResultLogged, 1) == 0)
        {
            Say(line);
        }
    }

    private static string Describe(AmResult result) => result.Ok ? "ok" : (result.Failure + " " + result.Detail).TrimEnd();

    /// <summary>Tells the keeper that the driver is done with Apple Music, once (the watchdog and the worker's own unwinding both get there).</summary>
    private void ExitDriverOnce(Job job)
    {
        if (Interlocked.Exchange(ref job.DriverExited, 1) == 0)
        {
            _keeper?.ExitDriver();
        }
    }

    /// <summary>The job starts Apple Music: a new Apple Music process is WinKnife's own meanwhile (once per job).</summary>
    private void BeginLaunchOnce(Job job)
    {
        if (_keeper is not null && Interlocked.Exchange(ref job.Launching, 1) == 0)
        {
            _keeper.BeginLaunch();
        }
    }

    /// <summary>The job's start of Apple Music is over, or the job was abandoned in the middle of it (once per job): the keeper must not stay in "WinKnife is starting it".</summary>
    private void EndLaunchOnce(Job job)
    {
        if (Interlocked.Exchange(ref job.Launching, 0) == 1)
        {
            _keeper?.EndLaunch();
        }
    }

    /// <summary>
    /// The job overran its budget: its worker is stuck in a call into Apple Music that does not return. The worker is abandoned (it exits by
    /// itself if the call ever returns), the request fails now and the next request gets a fresh worker.
    /// </summary>
    private void OnWatchdog(Job job)
    {
        bool restart;
        lock (_gate)
        {
            if (_running != job)
            {
                return;
            }

            _generation++;
            _running = null;
            _abandoned = _thread;
            _thread = null;
            restart = _pending is not null && !_disposed;
        }

        Say($"watchdog: Apple Music did not respond in {job.BudgetMs} ms; worker abandoned");
        LogResult(job, $"result: Failed timeout ({job.Command.Action}, {job.Clock.ElapsedMilliseconds} ms)");
        job.CancelReason = "timeout";
        job.Done.TrySetResult(AmResult.Fail(AmFailure.Failed, "timeout"));
        job.Cancel.Cancel();
        job.Guard?.DisposeNow();
        EndLaunchOnce(job);
        ExitDriverOnce(job);
        if (restart)
        {
            lock (_gate)
            {
                StartWorker();
            }

            _wake.Set();
        }
    }

    private void Worker(int generation, Thread? predecessor)
    {
        // the worker this one replaces is stuck in a call into Apple Music that may still return and finish what it was doing to the windows:
        // it gets a moment to do so before this one touches them (bounded: it may never return)
        predecessor?.Join(2000);
        while (true)
        {
            _wake.WaitOne();
            Job? job;
            lock (_gate)
            {
                if (_disposed || generation != _generation)
                {
                    return;
                }

                job = _pending;
                _pending = null;
                _running = job;
            }

            if (job is null)
            {
                continue;
            }

            var watch = Stopwatch.StartNew();
            AmResult result;

            // UI Automation and window calls into a stuck app can block for a minute: the request fails sooner and this worker is abandoned
            using var watchdog = new Timer(_ => OnWatchdog(job), null, job.BudgetMs + WatchdogSlackMs, Timeout.Infinite);
            job.Watchdog = watchdog;
            try
            {
                // a window an earlier request left transparent or parked (its restore did not get through) is given back before this one starts
                Heal();
                result = Execute(job, job.Cancel.Token);
            }
            catch (OperationCanceledException)
            {
                result = AmResult.Fail(AmFailure.Cancelled, job.CancelReason ?? "");
                LogResult(job, $"result: Cancelled ({job.CancelReason ?? "cancelled"}; {job.Command.Action})");
            }
            catch (Exception ex)
            {
                Say("failed: " + ex.GetType().Name + ": " + ex.Message);
                result = AmResult.Fail(AmFailure.Failed, ex.Message);
            }

            result.Milliseconds = watch.ElapsedMilliseconds;
            LogResult(job, $"result: {Describe(result)} ({job.Command.Action}, {result.Milliseconds} ms)");
            lock (_gate)
            {
                if (generation != _generation)
                {
                    // abandoned by the watchdog while it was stuck: the request has been answered and the queue belongs to the new worker
                    return;
                }

                _running = null;
            }

            job.Done.TrySetResult(result);
        }
    }

    private void Heal()
    {
        try
        {
            AmNative.Heal(Say);
        }
        catch (Exception ex)
        {
            Say("healing the windows failed: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- one command

    private AmResult Execute(Job job, CancellationToken ct)
    {
        // while the driver works its own show / hide / minimise events are none of the keeper's business
        _keeper?.EnterDriver();
        try
        {
            return ExecuteCore(job, ct);
        }
        finally
        {
            ExitDriverOnce(job);
        }
    }

    private AmResult ExecuteCore(Job job, CancellationToken ct)
    {
        var cmd = job.Command;
        var previous = AmNative.GetForegroundWindow();
        Say($"{cmd.Action} '{cmd.Track?.Title ?? cmd.Playlist?.Name}'; foreground before: {AmNative.Describe(previous)}");

        // From here until 1.5 s after this job Apple Music is not allowed to keep the foreground (it is started, restored and invoked below).
        // The hook is dropped when the job's longest budget is over, whatever happens to this thread.
        using var guard = ForegroundGuard.Arm(previous, Say, job.ColdMs + (2 * WatchdogSlackMs));
        job.Guard = guard;
        var transport = cmd.Action is AmAction.PlayPause or AmAction.SkipForward or AmAction.SkipBack;
        if (transport && TransportViaSession(cmd.Action, false, 0, ct) is { } direct)
        {
            // Windows' own media session took the command: Apple Music's window and UI Automation are not involved at all
            guard.Restore();
            LogResult(job, $"result: {Describe(direct)} (media session); foreground after: {AmNative.Describe(AmNative.GetForegroundWindow())}");
            return direct;
        }

        var before = Snapshot();
        var am = AmUia.Find(_keeper);
        if (am is null && cmd.Action is AmAction.Seek or AmAction.ReadQueue)
        {
            // reading the queue or scrubbing never starts Apple Music (that would only be to look at an empty list)
            return AmResult.Fail(AmFailure.Unavailable, "Apple Music is not running");
        }

        if (am is null)
        {
            am = AwaitProcessWindow(job, ct, out var problem);
            if (problem is not null)
            {
                return AmResult.Fail(AmFailure.Unavailable, problem);
            }
        }

        var launched = am is null;
        if (launched)
        {
            job.ExtendBudget();
        }

        am ??= LaunchHidden(job, guard, ct);
        if (am is null)
        {
            return AmResult.Fail(AmFailure.Unavailable, "Apple Music did not start");
        }

        ct.ThrowIfCancellationRequested();
        AmResult result;
        using (new PopupHider(am.Hwnd, am.Pid, Say))
        using (var hidden = new HiddenWindow(am.Hwnd, Say, _keeper))
        {
            if (!hidden.Ready)
            {
                result = AmResult.Fail(AmFailure.Unavailable, "Apple Music did not answer");
            }
            else
            {
                result = cmd.Action switch
                {
                    AmAction.PlayPlaylist or AmAction.ShufflePlaylist => PlayPlaylist(am, cmd, launched, ct),
                    AmAction.ReadQueue => ReadQueue(am, ct),
                    AmAction.PlayPause or AmAction.SkipForward or AmAction.SkipBack => launched ? TransportAfterStart(am, cmd.Action, ct) : PressTransport(am, cmd.Action, 4000, ct),
                    AmAction.Seek => SeekScrubber(am, cmd.SeekTo, ct),
                    _ => PlaySong(am, cmd, launched, launched ? 10000 : 4500, ct),
                };

                if (result.Ok && cmd.ResumeAt is { } resumeAt && cmd.Action is AmAction.Play or AmAction.PlayPlaylist or AmAction.ShufflePlaylist)
                {
                    // continue where a closed Apple Music stopped (a failed seek still leaves the song playing)
                    var seek = SeekScrubber(am, resumeAt, ct);
                    Say(seek.Ok ? $"resumed at {resumeAt:0.#} s" : "could not seek to the old position: " + seek.Detail);
                }
            }

            // Hand the focus back while the window is still parked (and transparent), then minimise without activating anything.
            // If Windows refuses, the minimise below falls back to SW_MINIMIZE, which activates the next window in z-order.
            guard.Restore();
        }

        // If Windows refused earlier, the minimise (SW_MINIMIZE) activated whatever is next in z-order: try again now.
        guard.Restore();
        if (result.Ok && cmd.Action is AmAction.Play or AmAction.PlayPlaylist or AmAction.ShufflePlaylist)
        {
            result = Confirm(cmd, before, ct);
        }

        LogResult(job, $"result: {Describe(result)}; foreground after: {AmNative.Describe(AmNative.GetForegroundWindow())}");
        return result;
    }

    /// <summary>
    /// Apple Music has a process but no window the driver can use. A process that is still starting gets its window within seconds; one that is
    /// quitting (its window was just closed, or it has been without one for a while) is waited for to end, because starting Apple Music again
    /// while the old instance lives only hands the start over to the dying one. <paramref name="problem"/> is set when it is still there afterwards,
    /// unless it has simply been running without a window for a long time and was not just closed: then null is returned (without a problem) and the
    /// caller starts Apple Music again, which makes the running instance show its window.
    /// </summary>
    private AmUia? AwaitProcessWindow(Job job, CancellationToken ct, out string? problem)
    {
        problem = null;
        var quitting = false;
        var closing = false;
        var alive = false;
        foreach (var p in Process.GetProcessesByName("AppleMusic"))
        {
            using (p)
            {
                alive = true;
                var pid = (uint)p.Id;
                if (AmNative.MainWindow(pid, _keeper?.KeptPid == pid ? _keeper.KeptHwnd : 0) != 0)
                {
                    continue;
                }

                var old = false;
                try
                {
                    old = DateTime.Now - p.StartTime > TimeSpan.FromSeconds(10);
                }
                catch
                {
                    // cannot tell its age: treated as young
                }

                var recent = _keeper?.RecentlyClosed(pid) == true;
                closing |= recent;
                quitting |= old || recent;
            }
        }

        if (!alive)
        {
            return null;
        }

        // the waits below (up to 8 s) come before the transport's own 4 s: the budget of a request that has to wait for Apple Music applies
        job.ExtendBudget();
        AmUia? am = null;
        var wait = Stopwatch.StartNew();
        if (quitting)
        {
            Say("Apple Music is quitting; waiting for it to end");
            while (wait.ElapsedMilliseconds < 6000)
            {
                ct.ThrowIfCancellationRequested();
                var ended = true;
                foreach (var p in Process.GetProcessesByName("AppleMusic"))
                {
                    using (p)
                    {
                        ended &= p.WaitForExit(100);
                    }
                }

                if (ended)
                {
                    break;
                }
            }

            am = AmUia.Find(_keeper);
        }
        else
        {
            // it is starting (or only its tray part is up): starting it again would show its window, so wait for the window instead
            Say("Apple Music is starting; waiting for its window");
            while (am is null && wait.ElapsedMilliseconds < 8000)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(200);
                am = AmUia.Find(_keeper);
            }
        }

        if (am is null && AppleMusicKeeper.IsRunning())
        {
            if (quitting && !closing)
            {
                // alive, old, without a window, and nobody closed it: it is not quitting, it has no window to show (it lives on in the background)
                Say("Apple Music is running without a window and was not closed; starting it again to bring its window up");
            }
            else
            {
                problem = "Apple Music is starting or quitting";
            }
        }

        return am;
    }

    // ---------------------------------------------------------------- transport

    /// <summary>
    /// Play/pause, next or previous through Windows' media session of Apple Music: no window, no UI Automation. After a hidden start
    /// (<paramref name="afterStart"/>) the session is awaited for up to <paramref name="waitMs"/> and asked to play (it comes up paused).
    /// Null = there is no session or it refused, so Apple Music's own button has to be pressed.
    /// </summary>
    private AmResult? TransportViaSession(AmAction action, bool afterStart, int waitMs, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        GlobalSystemMediaTransportControlsSession? session;
        while ((session = FindSession()) is null && watch.ElapsedMilliseconds < waitMs)
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(100);
        }

        if (session is null)
        {
            Say(afterStart ? "no media session after start; pressing the button" : "no media session; pressing the button");
            return null;
        }

        try
        {
            var status = session.GetPlaybackInfo().PlaybackStatus;
            if (afterStart && action == AmAction.PlayPause && status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                return AmResult.Success();
            }

            var wantPlaying = action == AmAction.PlayPause && (afterStart || status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            var ok = action switch
            {
                AmAction.SkipForward => session.TrySkipNextAsync().AsTask().GetAwaiter().GetResult(),
                AmAction.SkipBack => session.TrySkipPreviousAsync().AsTask().GetAwaiter().GetResult(),
                _ when afterStart => session.TryPlayAsync().AsTask().GetAwaiter().GetResult(),
                _ => session.TryTogglePlayPauseAsync().AsTask().GetAwaiter().GetResult(),
            };
            Say($"media session {action} (was {status}) => {ok}");
            if (!ok)
            {
                return null;
            }

            if (action != AmAction.PlayPause)
            {
                return AmResult.Success();
            }

            // confirm that it took effect (a session without a loaded song accepts the command and does nothing)
            var confirm = Stopwatch.StartNew();
            while (confirm.ElapsedMilliseconds < 1500)
            {
                ct.ThrowIfCancellationRequested();
                var now = session.GetPlaybackInfo().PlaybackStatus;
                if ((now == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) == wantPlaying && now != status)
                {
                    return AmResult.Success();
                }

                Thread.Sleep(60);
            }

            Say("media session did not change state; pressing the button");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Say("media session failed: " + ex.Message);
            return null;
        }
    }

    private static string TransportId(AmAction action) => action switch
    {
        AmAction.SkipForward => "TransportControl_SkipForward",
        AmAction.SkipBack => "TransportControl_SkipBack",
        _ => "TransportControl_PlayPauseStop",
    };

    /// <summary>
    /// The transport command right after a hidden start, in one poll: Windows registers no media session for Apple Music until something
    /// plays, so the session is used when it shows up and the app's own button as soon as it is enabled (whichever comes first).
    /// </summary>
    private AmResult TransportAfterStart(AmUia am, AmAction action, CancellationToken ct)
    {
        var id = TransportId(action);
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 8000)
        {
            ct.ThrowIfCancellationRequested();
            if (FindSession() is not null)
            {
                if (TransportViaSession(action, true, 0, ct) is { } viaSession)
                {
                    Say($"{action} through the media session after start ({watch.ElapsedMilliseconds} ms)");
                    return viaSession;
                }

                // the session refused: the app's own button is the way
                return PressTransport(am, action, 4000, ct);
            }

            try
            {
                if (am.ById(id) is { Current.IsEnabled: true } button && am.Invoke(button))
                {
                    Say($"pressed {id} after start ({watch.ElapsedMilliseconds} ms)");
                    Thread.Sleep(250);
                    return AmResult.Success();
                }
            }
            catch (ElementNotAvailableException)
            {
            }

            Thread.Sleep(100);
        }

        return AmResult.Fail(AmFailure.NoMenu, id);
    }

    /// <summary>Presses Apple Music's own transport button (used while Windows has no media session for it, e.g. right after a hidden start).</summary>
    private AmResult PressTransport(AmUia am, AmAction action, int wait, CancellationToken ct)
    {
        var id = TransportId(action);
        var button = am.WaitFor(() => am.ById(id) is { Current.IsEnabled: true } b ? b : null, wait, ct);
        if (button is null || !am.Invoke(button))
        {
            return AmResult.Fail(AmFailure.NoMenu, id);
        }

        Say("pressed " + id);
        Thread.Sleep(250);
        return AmResult.Success();
    }

    /// <summary>Sets the position of the playing song through the LCD scrubber's range value (Apple Music ignores the media session's seek).</summary>
    private AmResult SeekScrubber(AmUia am, double seconds, CancellationToken ct)
    {
        var scrubber = am.WaitFor(() => am.ById("LCDScrubber"), 4000, ct);
        if (scrubber is null || !scrubber.TryGetCurrentPattern(RangeValuePattern.Pattern, out var pattern))
        {
            return AmResult.Fail(AmFailure.NoMenu, "scrubber");
        }

        var range = (RangeValuePattern)pattern;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 3000 && range.Current.Maximum <= 0)
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(30);
        }

        var max = range.Current.Maximum;
        if (max <= 0)
        {
            return AmResult.Fail(AmFailure.NoMenu, "scrubber range");
        }

        var min = range.Current.Minimum;
        range.SetValue(Math.Clamp(seconds, min, Math.Max(min, max - 1)));
        Say($"scrubbed to {seconds:0.#} s (of {max:0.#})");
        Thread.Sleep(150);
        return AmResult.Success();
    }

    // ---------------------------------------------------------------- songs

    private AmResult PlaySong(AmUia am, AmCommand cmd, bool launched, int menuWait, CancellationToken ct)
    {
        var track = cmd.Track ?? throw new ArgumentException("track");
        var navId = cmd.Playlist is not null ? cmd.PlaylistAutomationId : (cmd.Page == "recent" ? "Sidebar_Library_RecentlyAdded" : "Sidebar_Library_Songs");
        var nav = am.FindNav(navId, cmd.Playlist?.Name, launched ? 20000 : 8000, ct, Say, out var seen);
        if (nav is null && cmd.Page == "recent" && cmd.Playlist is null)
        {
            nav = am.FindNav("Sidebar_Library_Songs", null, 3000, ct, Say, out _);
        }

        if (nav is null)
        {
            return AmResult.Fail(AmFailure.NotFound, $"sidebar item {navId} ({seen})");
        }

        am.Select(nav);
        Say("navigated to " + navId);
        // the row is found (scrolling the list when needed), its "…" menu is opened; one retry if the menu does not come up
        var squashed = Squash(track.Title);
        List<AutomationElement> items = [];
        var playIndex = -1;
        AutomationElement? found = null;
        for (var attempt = 0; attempt < 2 && found is null; attempt++)
        {
            AutomationElement? row = null;
            var deadline = Stopwatch.StartNew();
            var lastTotal = -1;
            while (deadline.ElapsedMilliseconds < 7000 && row is null)
            {
                ct.ThrowIfCancellationRequested();
                var list = am.RowList();
                if (list is not null)
                {
                    row = FindRowDeep(list, track, ct, out var total);
                    Say(row is null ? $"row not found ({total} rows)" : "row found");
                    if (row is null && (total == lastTotal || total >= 30))
                    {
                        break;
                    }

                    lastTotal = total;
                }

                if (row is null)
                {
                    Thread.Sleep(250);
                }
            }

            if (row is null)
            {
                DumpRows(am, track);
                return AmResult.Fail(AmFailure.NotFound, "song row");
            }

            if (row.TryGetCurrentPattern(VirtualizedItemPattern.Pattern, out var vp))
            {
                ((VirtualizedItemPattern)vp).Realize();
            }

            var buttons = row.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            AutomationElement? more = null;
            foreach (AutomationElement b in buttons)
            {
                if (b.Current.Name == "More")
                {
                    more = b;
                }
            }

            more ??= buttons.Count > 0 ? buttons[buttons.Count - 1] : null;
            if (more is null)
            {
                Thread.Sleep(300);
                continue;
            }

            ct.ThrowIfCancellationRequested();
            Say("invoking More");
            am.Invoke(more);
            found = am.WaitFor(() =>
            {
                items = am.MenuItems();
                playIndex = items.FindIndex(m => m.Current.ClassName == "MenuFlyoutItem" && Squash(m.Current.Name).Contains(squashed, StringComparison.Ordinal) && m.Current.Name.StartsWith("Play", StringComparison.OrdinalIgnoreCase));
                return playIndex >= 0 ? items[playIndex] : null;
            }, attempt == 0 ? menuWait : 5000, ct);
            if (found is null)
            {
                Say("menu did not appear" + (items.Count > 0 ? "; items seen: " + string.Join(" | ", items.Select(m => m.Current.Name)) : ""));
            }
        }

        if (found is null)
        {
            return AmResult.Fail(AmFailure.NoMenu, "menu");
        }

        // "Play “title”", then "Play Next", then "Add to Queue" (matched by name, then by position so other languages work)
        var target = found;
        if (cmd.Action is AmAction.PlayNext or AmAction.AddToQueue)
        {
            var wanted = cmd.Action == AmAction.PlayNext ? "Play Next" : "Add to Queue";
            target = items.FirstOrDefault(m => m.Current.Name == wanted)
                ?? (playIndex + (cmd.Action == AmAction.PlayNext ? 1 : 2) < items.Count ? items[playIndex + (cmd.Action == AmAction.PlayNext ? 1 : 2)] : null);
        }

        if (target is null || !am.Invoke(target))
        {
            return AmResult.Fail(AmFailure.NoMenu, "menu item");
        }

        Say("invoked menu item: " + target.Current.Name);
        Thread.Sleep(250);
        return AmResult.Success();
    }

    private void DumpRows(AmUia am, AmTrack track)
    {
        try
        {
            var list = am.RowList();
            if (list is not null && list.TryGetCurrentPattern(ItemContainerPattern.Pattern, out var p))
            {
                var container = (ItemContainerPattern)p;
                AutomationElement? current = null;
                var n = 0;
                var key = Squash(track.Title);
                var head = key.Length > 5 ? key[..5] : key;
                while ((current = container.FindItemByProperty(current, null, null)) is not null && n < 600)
                {
                    n++;
                    var name = current.Current.Name;
                    if (n <= 3 || n % 40 == 0 || Squash(name).Contains(head, StringComparison.Ordinal))
                    {
                        Say($"row {n}: '{name}'");
                    }
                }

                Say($"{n} rows scanned; wanted '{track.Title}' / '{key}'");
            }
        }
        catch (Exception ex)
        {
            Say("dump failed: " + ex.Message);
        }
    }

    /// <summary>Lower-case text without quotes / apostrophes (Apple Music may show curly ones where the database has straight ones).</summary>
    internal static string Squash(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if ("\"'“”‘’„‟′″«»".Contains(ch))
            {
                continue;
            }

            sb.Append(char.IsWhiteSpace(ch) ? ' ' : char.ToLowerInvariant(ch));
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Rows that are not on screen (or just around it) are unrealised stubs whose name is only a type name, so the list is scrolled
    /// a page at a time (ScrollPattern) until the song's row has been realised.
    /// </summary>
    private AutomationElement? FindRowDeep(AutomationElement list, AmTrack track, CancellationToken ct, out int total)
    {
        var hit = FindRow(list, track, ct, out var realized, out total);
        if (hit is not null || !list.TryGetCurrentPattern(ScrollPattern.Pattern, out var p))
        {
            return hit;
        }

        var scroll = (ScrollPattern)p;
        if (!scroll.Current.VerticallyScrollable)
        {
            return null;
        }

        // about `realized` rows around the viewport exist at a time: move in steps that overlap a little
        var step = Math.Clamp(100.0 * Math.Max(1, realized) / Math.Max(1, total) * 0.8, 6, 45);
        for (var pct = 0.0; pct < 100 + step; pct += step)
        {
            ct.ThrowIfCancellationRequested();
            scroll.SetScrollPercent(ScrollPattern.NoScroll, Math.Min(100, pct));
            Thread.Sleep(60);
            hit = FindRow(list, track, ct, out _, out _);
            if (hit is not null)
            {
                Say($"row found after scrolling to {Math.Min(100, pct):0}%");
                return hit;
            }
        }

        return null;
    }

    private static AutomationElement? FindRow(AutomationElement list, AmTrack track, CancellationToken ct, out int realized, out int total)
    {
        realized = 0;
        total = 0;
        if (!list.TryGetCurrentPattern(ItemContainerPattern.Pattern, out var p))
        {
            return null;
        }

        var container = (ItemContainerPattern)p;
        var title = Squash(track.Title);
        AutomationElement? current = null;
        AutomationElement? best = null;
        var bestScore = 0;
        var scanned = 0;
        while ((current = container.FindItemByProperty(current, null, null)) is not null && scanned < 6000)
        {
            scanned++;
            total = scanned;
            if (scanned % 32 == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            string name;
            try
            {
                if (current.Current.ClassName != "ListViewItem")
                {
                    continue;
                }

                name = current.Current.Name;
            }
            catch
            {
                continue;
            }

            if (name.EndsWith("DataGridRow", StringComparison.Ordinal))
            {
                continue;
            }

            realized++;
            name = Squash(name);
            if (!name.Contains(title, StringComparison.Ordinal))
            {
                continue;
            }

            var album = track.Album.Length == 0 || name.Contains(Squash(track.Album), StringComparison.Ordinal);
            var artist = track.Artist.Length == 0 || name.Contains(Squash(track.Artist), StringComparison.Ordinal);
            var score = 1 + (album ? 2 : 0) + (artist ? 1 : 0);
            if (score > bestScore)
            {
                best = current;
                bestScore = score;
            }

            if (score == 4)
            {
                break;
            }
        }

        return best;
    }

    // ---------------------------------------------------------------- playlists

    private AmResult PlayPlaylist(AmUia am, AmCommand cmd, bool launched, CancellationToken ct)
    {
        var nav = am.FindNav(cmd.PlaylistAutomationId, cmd.Playlist?.Name, launched ? 20000 : 8000, ct, Say, out var seen);
        if (nav is null)
        {
            return AmResult.Fail(AmFailure.NotFound, $"playlist {cmd.Playlist?.Name} ({seen})");
        }

        am.Select(nav);
        var id = cmd.Action == AmAction.ShufflePlaylist ? "ShuffleButtonElement" : "PlayButtonElement";
        var button = am.WaitFor(() => am.ContentPage()?.FindFirst(TreeScope.Descendants, AmUia.Id(id)), 7000, ct);
        if (button is null)
        {
            return AmResult.Fail(AmFailure.NoMenu, id);
        }

        ct.ThrowIfCancellationRequested();
        am.Invoke(button);
        Say("invoked " + id);
        Thread.Sleep(250);
        return AmResult.Success();
    }

    // ---------------------------------------------------------------- queue

    /// <summary>The "Playing Next" list (its id is PlayQueueListView, or PlayQueueListViewManual when the user queued songs by hand).</summary>
    private static AutomationElement? QueueList(AmUia am)
    {
        foreach (AutomationElement l in am.Root.FindAll(TreeScope.Descendants, AmUia.Cls("ListView")))
        {
            if (l.Current.AutomationId.StartsWith("PlayQueueListView", StringComparison.Ordinal) && l.FindFirst(TreeScope.Children, AmUia.Cls("ListViewItem")) is not null)
            {
                return l;
            }
        }

        return null;
    }

    private AmResult ReadQueue(AmUia am, CancellationToken ct)
    {
        var toggle = am.WaitFor(() => am.ById("PlayQueueToggleButton"), 4000, ct);
        if (toggle is null || !toggle.TryGetCurrentPattern(TogglePattern.Pattern, out var tp))
        {
            return AmResult.Fail(AmFailure.NotFound, "queue button");
        }

        var toggler = (TogglePattern)tp;
        var wasOn = toggler.Current.ToggleState == ToggleState.On;
        if (!wasOn)
        {
            toggler.Toggle();
        }

        try
        {
            var list = am.WaitFor(() => QueueList(am), 4000, ct);
            Say("queue list " + (list is null ? "not found" : "ready"));
            var items = new List<AmQueueItem>();
            if (list is not null && list.TryGetCurrentPattern(ItemContainerPattern.Pattern, out var cp))
            {
                var container = (ItemContainerPattern)cp;
                AutomationElement? current = null;
                while ((current = container.FindItemByProperty(current, null, null)) is not null && items.Count < 60)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (current.TryGetCurrentPattern(VirtualizedItemPattern.Pattern, out var vp))
                        {
                            ((VirtualizedItemPattern)vp).Realize();
                        }

                        if (current.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var sp))
                        {
                            ((ScrollItemPattern)sp).ScrollIntoView();
                        }

                        var texts = current.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                        var parts = new List<string>();
                        foreach (AutomationElement t in texts)
                        {
                            parts.Add(t.Current.Name);
                        }

                        if (parts.Count >= 1)
                        {
                            items.Add(new AmQueueItem(parts[0], parts.Count > 1 ? parts[1] : "", parts.Count > 2 ? parts[2] : "", false));
                        }
                        else
                        {
                            items.Add(new AmQueueItem(current.Current.Name, "", "", false));
                        }
                    }
                    catch (ElementNotAvailableException)
                    {
                    }
                }
            }

            Say($"queue: {items.Count} rows");
            return new AmResult { Ok = true, Queue = items };
        }
        finally
        {
            // leave the queue pane the way it was found (checked once more: a toggle that did not take would leave it open on the user's next visit)
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if ((toggler.Current.ToggleState == ToggleState.On) == wasOn)
                    {
                        break;
                    }

                    toggler.Toggle();
                    Thread.Sleep(120);
                }
                catch (ElementNotAvailableException)
                {
                    break;
                }
            }
        }
    }

    // ---------------------------------------------------------------- GSMTC confirmation

    private sealed record Media(string Status, string Title, string Artist);

    // only touched from the worker thread
    private static GlobalSystemMediaTransportControlsSessionManager? _sessions;

    private static GlobalSystemMediaTransportControlsSession? FindSession()
    {
        try
        {
            var manager = _sessions ??= GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
            return manager.GetSessions().FirstOrDefault(s => (s.SourceAppUserModelId ?? "").Contains("AppleMusic", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static Media? Snapshot()
    {
        try
        {
            var session = FindSession();
            if (session is null)
            {
                return null;
            }

            var props = session.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult();
            return new Media(session.GetPlaybackInfo().PlaybackStatus.ToString(), props.Title ?? "", props.Artist ?? "");
        }
        catch
        {
            return null;
        }
    }

    private AmResult Confirm(AmCommand cmd, Media? before, CancellationToken ct)
    {
        var titles = cmd.Track is { } t
            ? new HashSet<string> { WatchService.Normalise(t.Title) }
            : (cmd.Playlist?.Tracks.Select(x => WatchService.Normalise(x.Title)).ToHashSet() ?? []);
        var watch = Stopwatch.StartNew();
        Media? now = null;
        while (watch.ElapsedMilliseconds < 4500)
        {
            ct.ThrowIfCancellationRequested();
            now = Snapshot();
            if (now is { Status: "Playing" })
            {
                var title = WatchService.Normalise(now.Title);
                var match = cmd.Track is not null ? title == titles.First() || title.Contains(titles.First()) : titles.Contains(title) || before?.Title != now.Title || before?.Status != "Playing";
                if (match)
                {
                    Say($"GSMTC confirmed '{now.Title}' after {watch.ElapsedMilliseconds} ms");
                    return AmResult.Success();
                }
            }

            Thread.Sleep(150);
        }

        Say($"GSMTC not confirmed: now {now?.Status} '{now?.Title}'");
        return AmResult.Fail(AmFailure.NotConfirmed, now?.Title ?? "");
    }

    // ---------------------------------------------------------------- launching

    /// <summary>Whether this is the titled main window of Apple Music (not its invisible "WinUI Desktop" helper window).</summary>
    private static bool IsAppWindow(nint hwnd) =>
        AmNative.ClassOf(hwnd) == WindowClass && AmNative.TitleOf(hwnd) is { Length: > 0 } title && title != "WinUI Desktop";

    /// <summary>Starts Apple Music minimised without activation; every window it creates is transparent from the moment it exists.</summary>
    private AmUia? LaunchHidden(Job job, ForegroundGuard guard, CancellationToken ct)
    {
        if (!File.Exists(AmDb.AliasPath))
        {
            return null;
        }

        if (_keeper is { Enabled: true })
        {
            return LaunchKept(job, _keeper, guard, ct);
        }

        Say("Apple Music is not running; starting it hidden");
        var touched = new HashSet<nint>();
        var watch = Stopwatch.StartNew();
        AmUia? am = null;
        AmEventHook? hook = null;
        var closed = false;
        BeginLaunchOnce(job);

        // A window of the process, once the start is over, that is still being made transparent by a hook call stuck inside Apple Music: gets its style back.
        void Settle(nint window)
        {
            Func<bool> restore = () =>
            {
                // the minimise is left out when the person has opened Apple Music meanwhile (checked again by every retry)
                if (AppleMusicKeeper.Instance is not { UserOwned: true } && AmNative.IsWindowVisible(window) && !AmNative.IsIconic(window) && IsAppWindow(window))
                {
                    AmNative.ShowWindow(window, AmNative.SwShowMinNoActive);
                }

                AmNative.RestoreStyleNow(window);
                return true;
            };
            if (!AmNative.Bounded(window, restore, 800, "restoring the window style", Say))
            {
                AmNative.RetryRestore(window, restore, "restoring the window style", Say);
            }
        }

        try
        {
            hook = new AmEventHook(AmNative.EventObjectCreate, AmNative.EventObjectShow, 0, (ev, hwnd) =>
            {
                if (DriverWindowClass(hwnd) is not { } cls)
                {
                    return;
                }

                // recorded under the lock, the calls into Apple Music made outside it: they can take long, and the worker needs the lock
                bool first;
                lock (touched)
                {
                    first = touched.Add(hwnd);
                }

                if (first)
                {
                    AmNative.CancelRetry(hwnd);
                    var ex = AmNative.RememberStyle(hwnd);
                    AmNative.SetWindowLongPtr(hwnd, AmNative.GwlExStyle, (nint)(ex | AmNative.WsExLayered | AmNative.WsExTransparent));
                    AmNative.SetLayeredWindowAttributes(hwnd, 0, 0, AmNative.LwaAlpha);
                }

                if (Volatile.Read(ref closed))
                {
                    // the start is over and this call has only now come back from Apple Music
                    Settle(hwnd);
                }
            });
            var info = new AmNative.StartupInfo { Cb = System.Runtime.InteropServices.Marshal.SizeOf<AmNative.StartupInfo>(), Flags = 1 | 4, ShowWindow = AmNative.SwShowMinNoActive, X = -32000, Y = -32000 }; // STARTF_USESHOWWINDOW | STARTF_USEPOSITION
            var started = AmNative.CreateProcess(null, new System.Text.StringBuilder($"\"{AmDb.AliasPath}\""), 0, 0, false, 0, 0, null, ref info, out var pi);
            if (started)
            {
                AmNative.CloseHandle(pi.Process);
                AmNative.CloseHandle(pi.Thread);
                _keeper?.NoteOwnLaunch((uint)pi.ProcessId);
            }
            else
            {
                return null;
            }

            // A request that replaces this one does not cut the start short: Apple Music is left with a window that is out of sight, not
            // half-started with its window on screen (and taken for the person's own launch). The start is finished first, then it is dropped.
            var cancelSeen = Stopwatch.StartNew();
            var cancelled = false;
            while (watch.ElapsedMilliseconds < 30000)
            {
                if (ct.IsCancellationRequested)
                {
                    if (!cancelled)
                    {
                        cancelled = true;
                        cancelSeen.Restart();
                        Say("start cancelled; finishing it out of sight first");
                    }

                    if (StartedWindow(touched) || cancelSeen.ElapsedMilliseconds > 10000)
                    {
                        break;
                    }
                }

                if (am is null)
                {
                    am = AmUia.Find();
                    if (am is not null)
                    {
                        _keeper?.NoteOwnLaunch(am.Pid);
                    }
                }

                if (am is not null && !AmNative.IsIconic(am.Hwnd))
                {
                    AmNative.ShowWindowAsync(am.Hwnd, AmNative.SwShowMinNoActive);
                }

                if (am is not null && am.ById("TransportControl_PlayPauseStop") is not null)
                {
                    Say($"Apple Music UI ready after {watch.ElapsedMilliseconds} ms");
                    break;
                }

                Thread.Sleep(50);
            }

            // it may restore itself a moment later: keep minimising for two seconds
            for (var i = 0; i < 40 && am is not null && !ct.IsCancellationRequested; i++)
            {
                if (!AmNative.IsIconic(am.Hwnd))
                {
                    AmNative.ShowWindowAsync(am.Hwnd, AmNative.SwShowMinNoActive);
                    Say("late restore -> re-minimised");
                }

                Thread.Sleep(50);
            }

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            // also when the start is cancelled or fails: no window is left transparent, and the main window ends up minimised
            Volatile.Write(ref closed, true);
            hook?.Dispose();
            List<nint> items;
            lock (touched)
            {
                items = [.. touched];
            }

            foreach (var window in items)
            {
                Settle(window);
            }

            EndLaunchOnce(job);
            guard.Restore();
        }

        return am;
    }

    /// <summary>The class of a top-level window of Apple Music's process that the driver has to make transparent (a main window or a popup), else null.</summary>
    private static string? DriverWindowClass(nint hwnd)
    {
        if (AmNative.GetAncestor(hwnd, AmNative.GaRoot) != hwnd)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)AmNative.ProcessOf(hwnd));
            if (process.ProcessName != "AppleMusic")
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        var cls = AmNative.ClassOf(hwnd);
        return cls is WindowClass or PopupClass ? cls : null;
    }

    /// <summary>The titled main window of a start under way has been created (and the hook has made it transparent).</summary>
    private static bool StartedWindow(HashSet<nint> touched)
    {
        List<nint> items;
        lock (touched)
        {
            items = [.. touched];
        }

        return items.Exists(h => AmNative.IsWindow(h) && IsAppWindow(h));
    }

    /// <summary>
    /// Starts Apple Music so that it never has a taskbar button: it is shown without activation (a window hidden from the start never
    /// realises its UI), every window it creates is transparent, click-through and a tool window from the moment it exists, the main window
    /// is parked far off-screen, and once the transport bar is up it is hidden and its styles are put back (the caller ghosts it again for its job).
    /// Whatever happens (a cancel included) the main window is hidden again and every style is put back before this returns.
    /// </summary>
    private AmUia? LaunchKept(Job job, AppleMusicKeeper keeper, ForegroundGuard guard, CancellationToken ct)
    {
        Say("Apple Music is not running; starting it out of sight");
        var touched = new HashSet<nint>();
        var rects = new Dictionary<nint, AmNative.Rect>();
        var watch = Stopwatch.StartNew();
        AmUia? am = null;
        AmEventHook? hook = null;
        var closed = false;
        BeginLaunchOnce(job);

        // A window of the process gets its style back and, for the main window, is hidden (back at the place it first showed up at) and handed
        // to the keeper. Bounded, and retried in the background when Apple Music does not answer: it is never left invisible or parked.
        void Settle(nint window)
        {
            AmNative.Rect home;
            lock (touched)
            {
                rects.TryGetValue(window, out home);
            }

            var main = AmNative.IsWindow(window) && IsAppWindow(window);
            Func<bool> restore = () =>
            {
                if (main)
                {
                    // checked again by every retry: the person may open Apple Music while this is under way
                    var theirs = keeper.UserOwned;
                    if (!theirs && AmNative.IsWindowVisible(window))
                    {
                        AmNative.ShowWindow(window, AmNative.SwHide);
                    }

                    if (home.Right > home.Left)
                    {
                        AmNative.SetWindowPos(window, 0, home.Left, home.Top, home.Right - home.Left, home.Bottom - home.Top, AmNative.SwpNoZOrder | AmNative.SwpNoActivate | AmNative.SwpNoOwnerZOrder);
                    }
                    else if (theirs)
                    {
                        AmNative.Unpark(window);
                    }
                }

                AmNative.RestoreStyleNow(window);
                return true;
            };
            if (!AmNative.Bounded(window, restore, 800, "restoring the window", Say))
            {
                AmNative.RetryRestore(window, restore, "restoring the window", Say);
            }

            if (main)
            {
                // kept even when the start was cancelled: a hidden window nobody keeps cannot be found again
                keeper.Adopt(window, AmNative.ProcessOf(window));
            }
        }

        try
        {
            hook = new AmEventHook(AmNative.EventObjectCreate, AmNative.EventObjectShow, 0, (ev, hwnd) =>
            {
                if (DriverWindowClass(hwnd) is not { } cls)
                {
                    return;
                }

                // recorded under the lock, the calls into Apple Music made outside it: they can take long, and the worker needs the lock
                bool first;
                lock (touched)
                {
                    first = touched.Add(hwnd);
                }

                if (first)
                {
                    AmNative.CancelRetry(hwnd);
                    var ex = AmNative.RememberStyle(hwnd);
                    var ghost = (nint)(ex | AmNative.WsExLayered | AmNative.WsExTransparent);
                    if (cls == WindowClass)
                    {
                        ghost = (nint)((ghost | AmNative.WsExToolWindow) & ~AmNative.WsExAppWindow);
                    }

                    AmNative.SetWindowLongPtr(hwnd, AmNative.GwlExStyle, ghost);
                    AmNative.SetLayeredWindowAttributes(hwnd, 0, 0, AmNative.LwaAlpha);
                }

                if (ev == AmNative.EventObjectShow && cls == WindowClass)
                {
                    if (AmNative.GetWindowRect(hwnd, out var r) && AmNative.OnAnyScreen(r))
                    {
                        lock (touched)
                        {
                            rects.TryAdd(hwnd, r);
                        }

                        AmNative.RememberHome(hwnd, r);
                    }

                    AmNative.SetWindowPos(hwnd, 0, -30000, -30000, 2400, 1500, AmNative.SwpNoZOrder | AmNative.SwpNoActivate | AmNative.SwpNoOwnerZOrder | AmNative.SwpAsyncWindowPos);
                }

                if (Volatile.Read(ref closed))
                {
                    // the start is over and this call has only now come back from Apple Music
                    Settle(hwnd);
                }
            });
            var info = new AmNative.StartupInfo { Cb = System.Runtime.InteropServices.Marshal.SizeOf<AmNative.StartupInfo>(), Flags = 1, ShowWindow = AmNative.SwShowNoActivate }; // STARTF_USESHOWWINDOW
            var started = AmNative.CreateProcess(null, new System.Text.StringBuilder($"\"{AmDb.AliasPath}\""), 0, 0, false, 0, 0, null, ref info, out var pi);
            if (!started)
            {
                return null;
            }

            AmNative.CloseHandle(pi.Process);
            AmNative.CloseHandle(pi.Thread);

            // WinKnife's own process, whenever its windows appear (the keeper must not take them for the person's launch)
            keeper.NoteOwnLaunch((uint)pi.ProcessId);

            // A request that replaces this one does not cut the start short: Apple Music is left with a window that is out of sight, not
            // half-started with its window on screen (and taken for the person's own launch). The start is finished first, then it is dropped.
            var cancelSeen = Stopwatch.StartNew();
            var cancelled = false;
            while (watch.ElapsedMilliseconds < 30000)
            {
                if (ct.IsCancellationRequested)
                {
                    if (!cancelled)
                    {
                        cancelled = true;
                        cancelSeen.Restart();
                        Say("start cancelled; finishing it out of sight first");
                    }

                    if (StartedWindow(touched) || cancelSeen.ElapsedMilliseconds > 10000)
                    {
                        break;
                    }
                }

                if (am is null)
                {
                    am = AmUia.Find(keeper);
                    if (am is not null)
                    {
                        keeper.NoteOwnLaunch(am.Pid);
                    }
                }

                try
                {
                    if (am is not null && am.ById("TransportControl_PlayPauseStop") is not null)
                    {
                        Say($"Apple Music UI ready after {watch.ElapsedMilliseconds} ms");
                        break;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    am = null;
                }

                Thread.Sleep(50);
            }

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            Volatile.Write(ref closed, true);
            hook?.Dispose();
            List<nint> items;
            lock (touched)
            {
                items = [.. touched];
            }

            // Every window it made gets its style back; the main window is hidden first (and goes back to where it first showed up), also
            // when the start was cancelled: it is never left invisible, parked far off-screen and on the taskbar's books.
            foreach (var window in items)
            {
                Settle(window);
            }

            EndLaunchOnce(job);
            guard.Restore();
        }

        return am;
    }

    // ---------------------------------------------------------------- window hiding

    /// <summary>
    /// Makes every popup of Apple Music's main window (the context menu's "PopupHost") transparent and click-through the moment it is
    /// created or shown, and puts the original extended style back afterwards (the popups are cached and reused by the app). Only popup
    /// windows of the main window's own UI thread are touched: any other window of the process (some are made on threads that do not
    /// pump messages yet) would stall the caller on a synchronous cross-process style change.
    /// </summary>
    private sealed class PopupHider : IDisposable
    {
        private readonly AmEventHook _hook;
        private readonly HashSet<nint> _touched = [];
        private readonly HashSet<nint> _styled = [];
        private readonly Action<string> _say;
        private bool _closed;

        public PopupHider(nint main, uint pid, Action<string> say)
        {
            _say = say;
            var mainThread = AmNative.GetWindowThreadProcessId(main, out _);
            _hook = new AmEventHook(AmNative.EventObjectCreate, AmNative.EventObjectShow, pid, (_, hwnd) =>
            {
                if (hwnd == main || AmNative.GetAncestor(hwnd, AmNative.GaRoot) != hwnd || AmNative.ClassOf(hwnd) != PopupClass || AmNative.GetWindowThreadProcessId(hwnd, out _) != mainThread)
                {
                    return;
                }

                bool needStyle;
                lock (_touched)
                {
                    _touched.Add(hwnd);
                    needStyle = !_styled.Contains(hwnd);
                }

                long ex = 0;
                if (needStyle)
                {
                    // the true style is on record before the popup is touched (a cached popup may still be transparent from an earlier job)
                    AmNative.CancelRetry(hwnd);
                    ex = AmNative.RememberStyle(hwnd);
                }

                var done = AmNative.Bounded(
                    hwnd,
                    () =>
                    {
                        if (needStyle)
                        {
                            AmNative.SetWindowLongPtr(hwnd, AmNative.GwlExStyle, (nint)(ex | AmNative.WsExLayered | AmNative.WsExTransparent));
                        }

                        AmNative.SetLayeredWindowAttributes(hwnd, 0, 0, AmNative.LwaAlpha);
                        return true;
                    },
                    400,
                    "hiding a popup",
                    _say);
                bool closed;
                lock (_touched)
                {
                    if (done && needStyle)
                    {
                        _styled.Add(hwnd);
                    }

                    closed = _closed;
                }

                if (closed)
                {
                    // the job is over and this popup has only now been dealt with: it gets its style back
                    AmNative.RestoreStyle(hwnd, _say);
                }
            });
        }

        public void Dispose()
        {
            lock (_touched)
            {
                _closed = true;
            }

            _hook.Dispose();
            List<nint> items;
            lock (_touched)
            {
                items = [.. _touched];
            }

            // every popup, whatever happens to the others: each restore that does not get through is retried in the background
            foreach (var h in items)
            {
                if (AmNative.IsWindow(h))
                {
                    AmNative.RestoreStyle(h, _say);
                }
            }
        }
    }

    /// <summary>
    /// A minimised WinUI window does not realise its UI, so it is restored for the duration: fully transparent and click-through,
    /// without activation, parked far off-screen and wide enough that the NavigationView stays expanded. Disposing puts it back.
    /// Every call into the window is bounded (<see cref="AmNative.Bounded"/>): a stuck Apple Music cannot stall the worker.
    /// </summary>
    private sealed class HiddenWindow : IDisposable
    {
        /// <summary>The newest instance on each window: an older one (of a request the watchdog gave up on) leaves putting the window back to it.</summary>
        private static readonly ConcurrentDictionary<nint, HiddenWindow> Newest = new();

        private readonly nint _hwnd;
        private readonly AmNative.WindowPlacement _original;
        private readonly bool _wasIconic;
        private readonly Action<string> _say;
        private readonly AppleMusicKeeper? _owner;
        private readonly AppleMusicKeeper? _keeper;
        private readonly bool _wasHidden;
        private readonly Task<bool>? _pending;

        public HiddenWindow(nint hwnd, Action<string> say, AppleMusicKeeper? keeper = null)
        {
            _hwnd = hwnd;
            _say = say;

            // the keeper as such (the person opening Apple Music is respected also when keeping it out of sight is switched off)
            _owner = keeper;
            _keeper = keeper is { Enabled: true } ? keeper : null;
            _wasHidden = !AmNative.IsWindowVisible(hwnd);
            AmNative.GetWindowRect(hwnd, out var rect);
            _wasIconic = _wasHidden || AmNative.IsIconic(hwnd) || !OnAnyScreen(rect);
            _original = new AmNative.WindowPlacement { Length = System.Runtime.InteropServices.Marshal.SizeOf<AmNative.WindowPlacement>() };
            AmNative.GetWindowPlacement(hwnd, ref _original);
            AmNative.RememberHome(hwnd, _original.Normal);
            if (!OnAnyScreen(_original.Normal))
            {
                // never remember an off-screen restore position (a previous run may have left one)
                var w = AmNative.GetSystemMetrics(0);
                var h = AmNative.GetSystemMetrics(1);
                _original.Normal = new AmNative.Rect { Left = (w - 1200) / 2, Top = (h - 800) / 2, Right = ((w - 1200) / 2) + 1200, Bottom = ((h - 800) / 2) + 800 };
            }

            if (!_wasIconic)
            {
                // the user has the window open: work on it in place (only its menus are hidden)
                _say("Apple Music window is open; working in place");
                Ready = true;
                return;
            }

            // The window's true style is on record before it is touched (the live style may be a transparent one left behind by a request
            // that was abandoned or whose restore did not get through), and restores still waiting for the window must not run during the job.
            Newest[hwnd] = this;
            AmNative.CancelRetry(hwnd);
            var originalEx = AmNative.RememberStyle(hwnd);
            var ghost = (nint)(originalEx | AmNative.WsExLayered | AmNative.WsExTransparent);
            if (_wasHidden || _keeper is not null)
            {
                // no taskbar button while it is shown for the job
                ghost = (nint)((ghost | AmNative.WsExToolWindow) & ~AmNative.WsExAppWindow);
            }

            var wp = _original;
            wp.ShowCmd = AmNative.SwShowNoActivate;
            wp.Flags = 0;

            // one step, in this order: transparent first, then shown, then parked
            Ready = AmNative.Bounded(
                hwnd,
                () =>
                {
                    AmNative.SetWindowLongPtr(hwnd, AmNative.GwlExStyle, ghost);
                    AmNative.SetLayeredWindowAttributes(hwnd, 0, 0, AmNative.LwaAlpha);
                    var placement = wp;
                    AmNative.SetWindowPlacement(hwnd, ref placement);
                    AmNative.SetWindowPos(hwnd, 0, -30000, -30000, 2400, 1500, AmNative.SwpNoZOrder | AmNative.SwpNoActivate | AmNative.SwpNoOwnerZOrder);
                    return true;
                },
                1500,
                "making the window invisible",
                _say,
                out _pending);
        }

        /// <summary>The window is ready to be worked on (false = Apple Music did not answer; it has not been shown).</summary>
        public bool Ready { get; }

        private static bool OnAnyScreen(AmNative.Rect r) => AmNative.OnAnyScreen(r);

        public void Dispose()
        {
            if (!_wasIconic)
            {
                return;
            }

            if (_pending is { IsCompleted: false } late)
            {
                // The call that makes the window invisible is still running inside Apple Music (it did not answer in time): it could still
                // complete after a restore made now, so the window is put back once that call has ended, however it ended.
                _say("making the window invisible is still running in Apple Music; it is put back when that has ended");
                _ = late.ContinueWith(_ => Finish(), TaskScheduler.Default);
                return;
            }

            Finish();
        }

        private void Finish()
        {
            if (Newest.TryGetValue(_hwnd, out var newest) && !ReferenceEquals(newest, this))
            {
                // a newer request took the window over while this one (given up on by the watchdog) was stuck: that one puts it back
                _say("window already taken over by a newer request; it puts it back");
                return;
            }

            try
            {
                Restore();
            }
            finally
            {
                ((ICollection<KeyValuePair<nint, HiddenWindow>>)Newest).Remove(new KeyValuePair<nint, HiddenWindow>(_hwnd, this));
            }
        }

        private void Restore()
        {
            var rect = _original.Normal;
            if (_keeper is not null)
            {
                // Kept out of sight: hidden while still transparent, back at the user's own restore position (so that is where Windows
                // remembers it), original styles back, then the keeper takes it over (no taskbar button, no Alt+Tab entry).
                // If the person opened Apple Music meanwhile (also while this attempt is retried) it is theirs: moved back, made opaque and shown.
                Func<bool> settle = () =>
                {
                    var theirs = _owner?.UserOwned == true;
                    if (!theirs)
                    {
                        AmNative.ShowWindow(_hwnd, AmNative.SwHide);
                    }

                    AmNative.SetWindowPos(_hwnd, 0, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, AmNative.SwpNoZOrder | AmNative.SwpNoActivate | AmNative.SwpNoOwnerZOrder);
                    AmNative.RestoreStyleNow(_hwnd);
                    if (theirs && (!AmNative.IsWindowVisible(_hwnd) || AmNative.IsIconic(_hwnd)))
                    {
                        AmNative.ShowWindow(_hwnd, AmNative.SwRestore);
                    }

                    return true;
                };
                var done = AmNative.Bounded(_hwnd, settle, 1500, "hiding the window", _say);
                if (!done)
                {
                    AmNative.RetryRestore(_hwnd, settle, "hiding the window", _say);
                }

                // refuses (and shows the window) when the person has Apple Music open
                _keeper.Adopt(_hwnd, AmNative.ProcessOf(_hwnd));
                _say((_owner?.UserOwned == true ? "window given back to the person who opened Apple Music" : done ? "window hidden and kept" : "window kept (Apple Music did not finish hiding it; retrying)") + "; foreground now " + AmNative.Describe(AmNative.GetForegroundWindow()));
                return;
            }

            // Everything happens while the window is still transparent and click-through. It goes back to the user's own restore
            // position first (so that is where Windows remembers it), then it is minimised without activation (not when the person opened it).
            var foreground = AmNative.ProcessOf(AmNative.GetForegroundWindow()) == AmNative.ProcessOf(_hwnd);
            Func<bool> minimise = () =>
            {
                var theirs = _owner?.UserOwned == true;
                if (foreground && !theirs)
                {
                    // Apple Music still owns the activation: hiding the window makes Windows pass it on to the next window in z-order
                    // (minimising alone can leave the minimised window as the foreground window).
                    AmNative.ShowWindow(_hwnd, AmNative.SwHide);
                }

                AmNative.SetWindowPos(_hwnd, 0, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, AmNative.SwpNoZOrder | AmNative.SwpNoActivate | AmNative.SwpNoOwnerZOrder);
                if (theirs)
                {
                    AmNative.RestoreStyleNow(_hwnd);
                    if (!AmNative.IsWindowVisible(_hwnd) || AmNative.IsIconic(_hwnd))
                    {
                        AmNative.ShowWindow(_hwnd, AmNative.SwRestore);
                    }

                    return true;
                }

                AmNative.ShowWindow(_hwnd, AmNative.SwShowMinNoActive);
                if (!AmNative.IsIconic(_hwnd))
                {
                    // some builds do not minimise a layered window: put the style back and do it again
                    AmNative.RestoreStyleNow(_hwnd);
                    AmNative.ShowWindow(_hwnd, AmNative.SwShowMinNoActive);
                }

                AmNative.RestoreStyleNow(_hwnd);
                return true;
            };
            if (!AmNative.Bounded(_hwnd, minimise, 1500, "minimising the window", _say))
            {
                AmNative.RetryRestore(_hwnd, minimise, "minimising the window", _say);
            }

            _say($"window {(_owner?.UserOwned == true ? "given back to the person who opened Apple Music" : "minimised again" + (foreground ? " (hidden first to pass the activation on)" : ""))}; foreground now {AmNative.Describe(AmNative.GetForegroundWindow())}");
        }
    }
}

/// <summary>UI Automation view of the Apple Music window (patterns only).</summary>
internal sealed class AmUia
{
    private AmUia(nint hwnd, uint pid)
    {
        Hwnd = hwnd;
        Pid = pid;
        Root = AutomationElement.FromHandle(hwnd);
    }

    public nint Hwnd { get; }

    public uint Pid { get; }

    public AutomationElement Root { get; }

    /// <summary>Apple Music's main window: a visible one, or the one the <paramref name="keeper"/> keeps hidden.</summary>
    public static AmUia? Find(AppleMusicKeeper? keeper = null)
    {
        foreach (var p in Process.GetProcessesByName("AppleMusic"))
        {
            var pid = (uint)p.Id;
            var kept = keeper is not null && keeper.KeptPid == pid ? keeper.KeptHwnd : 0;
            var h = AmNative.MainWindow(pid, kept);
            if (h != 0 && (AmNative.IsWindowVisible(h) || (kept != 0 && h == kept)))
            {
                return new AmUia(h, pid);
            }
        }

        return null;
    }

    public static Condition Id(string id) => new PropertyCondition(AutomationElement.AutomationIdProperty, id);

    public static Condition Cls(string cls) => new PropertyCondition(AutomationElement.ClassNameProperty, cls);

    public AutomationElement? ById(string id) => Root.FindFirst(TreeScope.Descendants, Id(id));

    public AutomationElement? WaitFor(Func<AutomationElement?> find, int timeoutMs, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            AutomationElement? e = null;
            try
            {
                e = find();
            }
            catch (ElementNotAvailableException)
            {
            }

            if (e is not null)
            {
                return e;
            }

            Thread.Sleep(80);
        }

        return null;
    }

    public bool Invoke(AutomationElement e)
    {
        if (e.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
        {
            ((InvokePattern)p).Invoke();
            return true;
        }

        return false;
    }

    public void Select(AutomationElement e)
    {
        if (e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p))
        {
            ((SelectionItemPattern)p).Select();
        }
    }

    public void ExpandSidebar(AutomationElement? scope = null)
    {
        foreach (var id in new[] { "Sidebar_Header_Library", "Sidebar_Header_Playlists" })
        {
            if ((scope ?? Root).FindFirst(TreeScope.Descendants, Id(id)) is { } e && e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p) && ((ExpandCollapsePattern)p).Current.ExpandCollapseState != ExpandCollapseState.Expanded)
            {
                ((ExpandCollapsePattern)p).Expand();
            }
        }
    }

    /// <summary>
    /// Finds a sidebar entry (a library page or a playlist) while Apple Music may still be loading it: right after a start the Playlists
    /// header is missing or collapsed, and cloud playlists appear (and are realised) late. The pane is searched, not the whole window; the
    /// headers are expanded again on every round; an entry the virtualised list has not realised is scrolled to; a playlist is also matched by
    /// its name among the entries of kind ePlaylist. <paramref name="detail"/> says what was seen (for the failure).
    /// </summary>
    public AutomationElement? FindNav(string id, string? name, int timeoutMs, CancellationToken ct, Action<string> say, out string detail)
    {
        var watch = Stopwatch.StartNew();
        var wanted = name is { Length: > 0 } ? AppleMusicDriver.Squash(name) : null;
        var byId = Id(id);
        var lists = new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tree));
        var playlists = -1;
        var header = "missing";
        long nextLog = 2000;
        AutomationElement? pane = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                pane ??= ById("PaneRoot") ?? Root;
                ExpandSidebar(pane);
                if (watch.ElapsedMilliseconds < timeoutMs && pane.FindFirst(TreeScope.Descendants, byId) is { } direct)
                {
                    detail = "";
                    return direct;
                }

                if (watch.ElapsedMilliseconds < timeoutMs)
                {
                    foreach (AutomationElement list in pane.FindAll(TreeScope.Descendants, lists))
                    {
                        if (list.TryGetCurrentPattern(ItemContainerPattern.Pattern, out var cp)
                            && ((ItemContainerPattern)cp).FindItemByProperty(null, AutomationElement.AutomationIdProperty, id) is { } stub)
                        {
                            if (stub.TryGetCurrentPattern(VirtualizedItemPattern.Pattern, out var vp))
                            {
                                ((VirtualizedItemPattern)vp).Realize();
                            }

                            if (stub.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var sp))
                            {
                                ((ScrollItemPattern)sp).ScrollIntoView();
                            }

                            detail = "";
                            return stub;
                        }

                        if (watch.ElapsedMilliseconds >= timeoutMs)
                        {
                            break;
                        }
                    }
                }

                if (watch.ElapsedMilliseconds < timeoutMs)
                {
                    // every entry of kind ePlaylist (names and ids read in one round trip), by name when the id is not there
                    var request = new CacheRequest();
                    request.Add(AutomationElement.AutomationIdProperty);
                    request.Add(AutomationElement.NameProperty);
                    request.TreeScope = TreeScope.Element;
                    playlists = 0;
                    AutomationElement? byName = null;
                    using (request.Activate())
                    {
                        foreach (AutomationElement e in pane.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                        {
                            if (!e.Cached.AutomationId.Contains("IKIND:ePlaylist", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            playlists++;
                            if (byName is null && wanted is not null && AppleMusicDriver.Squash(e.Cached.Name) == wanted)
                            {
                                byName = e;
                            }
                        }
                    }

                    if (byName is not null)
                    {
                        say($"playlist found by its name '{name}' (the id {id} was not listed)");
                        detail = "";
                        return byName;
                    }

                    header = HeaderState(pane);
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException)
            {
                pane = null;
            }

            if (watch.ElapsedMilliseconds >= timeoutMs)
            {
                break;
            }

            if (watch.ElapsedMilliseconds >= nextLog)
            {
                say($"waiting for sidebar entry {id}: {playlists} playlists listed, Playlists header {header} ({watch.ElapsedMilliseconds} ms)");
                nextLog += 2000;
            }

            Thread.Sleep(300);
        }

        detail = $"{playlists} playlists listed, Playlists header {header}, waited {watch.ElapsedMilliseconds} ms";
        return null;
    }

    private string HeaderState(AutomationElement pane)
    {
        if (pane.FindFirst(TreeScope.Descendants, Id("Sidebar_Header_Playlists")) is not { } e)
        {
            return "missing";
        }

        return e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p) ? (((ExpandCollapsePattern)p).Current.ExpandCollapseState == ExpandCollapseState.Expanded ? "expanded" : "collapsed") : "expanded";
    }

    /// <summary>The current page (the sibling of the pane and the transport bar under the NavigationView).</summary>
    public AutomationElement? ContentPage()
    {
        var nav = ById("NavView");
        if (nav is null)
        {
            return null;
        }

        var walker = TreeWalker.ControlViewWalker;
        for (var c = walker.GetFirstChild(nav); c is not null; c = walker.GetNextSibling(c))
        {
            var id = c.Current.AutomationId;
            if (id is "PaneRoot" or "TransportBar" or "NavigationViewBackButton")
            {
                continue;
            }

            if (c.Current.ControlType == ControlType.Group)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>The list of song rows on the current page, once it has rows.</summary>
    public AutomationElement? RowList()
    {
        var page = ContentPage();
        var list = page?.FindFirst(TreeScope.Descendants, Cls("ListView"));
        return list is not null && list.FindFirst(TreeScope.Children, Cls("ListViewItem")) is not null ? list : null;
    }

    /// <summary>Menu items of every top-level window of the Apple Music process (menus are separate popup windows).</summary>
    public List<AutomationElement> MenuItems()
    {
        var res = new List<AutomationElement>();
        var tops = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, (int)Pid));
        foreach (AutomationElement top in tops)
        {
            foreach (AutomationElement m in top.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)))
            {
                res.Add(m);
            }
        }

        return res;
    }
}
