// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

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

    private sealed class Job(AmCommand command)
    {
        public AmCommand Command { get; } = command;

        public TaskCompletionSource<AmResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenSource Cancel { get; } = new();
    }

    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Action<string> _log;
    private Thread? _thread;
    private Job? _pending;
    private Job? _running;
    private bool _disposed;

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
                return Task.FromResult(AmResult.Fail(AmFailure.Cancelled, "busy"));
            }

            _pending?.Done.TrySetResult(AmResult.Fail(AmFailure.Cancelled, "superseded"));
            _pending = job;
            _running?.Cancel.Cancel();
            if (_thread is null)
            {
                _thread = new Thread(Worker) { IsBackground = true, Name = "WinKnife AppleMusic UIA" };
                _thread.SetApartmentState(ApartmentState.MTA);
                _thread.Start();
            }
        }

        _wake.Set();
        return job.Done.Task;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending?.Done.TrySetResult(AmResult.Fail(AmFailure.Cancelled));
            _pending = null;
            _running?.Cancel.Cancel();
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

    private void Worker()
    {
        while (true)
        {
            _wake.WaitOne();
            Job? job;
            lock (_gate)
            {
                if (_disposed)
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

            // UI Automation calls into a stuck app can block for a minute: report failure sooner (the worker unwinds on its own)
            using var watchdog = new Timer(
                _ =>
                {
                    Say("watchdog: Apple Music did not respond");
                    job.Done.TrySetResult(AmResult.Fail(AmFailure.Failed, "timeout"));
                    job.Cancel.Cancel();
                },
                null,
                job.Command.Action == AmAction.ReadQueue ? 15000 : 30000,
                Timeout.Infinite);
            try
            {
                result = Execute(job.Command, job.Cancel.Token);
            }
            catch (OperationCanceledException)
            {
                result = AmResult.Fail(AmFailure.Cancelled);
            }
            catch (Exception ex)
            {
                Say("failed: " + ex.GetType().Name + ": " + ex.Message);
                result = AmResult.Fail(AmFailure.Failed, ex.Message);
            }

            result.Milliseconds = watch.ElapsedMilliseconds;
            lock (_gate)
            {
                _running = null;
            }

            job.Done.TrySetResult(result);
        }
    }

    // ---------------------------------------------------------------- one command

    private AmResult Execute(AmCommand cmd, CancellationToken ct)
    {
        // while the driver works its own show / hide / minimise events are none of the keeper's business
        _keeper?.EnterDriver();
        try
        {
            return ExecuteCore(cmd, ct);
        }
        finally
        {
            _keeper?.ExitDriver();
        }
    }

    private AmResult ExecuteCore(AmCommand cmd, CancellationToken ct)
    {
        var previous = AmNative.GetForegroundWindow();
        Say($"{cmd.Action} '{cmd.Track?.Title ?? cmd.Playlist?.Name}'; foreground before: {AmNative.Describe(previous)}");

        // From here until 1.5 s after this job Apple Music is not allowed to keep the foreground (it is started, restored and invoked below).
        using var guard = ForegroundGuard.Arm(previous, Say);
        var transport = cmd.Action is AmAction.PlayPause or AmAction.SkipForward or AmAction.SkipBack;
        if (transport && TransportViaSession(cmd.Action, false, 0, ct) is { } direct)
        {
            // Windows' own media session took the command: Apple Music's window and UI Automation are not involved at all
            guard.Restore();
            Say($"result: {(direct.Ok ? "ok" : direct.Failure + " " + direct.Detail)} (media session); foreground after: {AmNative.Describe(AmNative.GetForegroundWindow())}");
            return direct;
        }

        var before = Snapshot();
        var am = AmUia.Find(_keeper);
        if (am is null && cmd.Action == AmAction.Seek)
        {
            return AmResult.Fail(AmFailure.Unavailable, "Apple Music is not running");
        }

        if (am is null && Process.GetProcessesByName("AppleMusic").Length > 0)
        {
            // it is starting (or only its tray part is up): starting it again would show its window, so wait for the window instead
            var wait = Stopwatch.StartNew();
            while (am is null && wait.ElapsedMilliseconds < 8000)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(200);
                am = AmUia.Find(_keeper);
            }
        }

        var launched = am is null;
        am ??= LaunchHidden(guard, ct);
        if (am is null)
        {
            return AmResult.Fail(AmFailure.Unavailable, "Apple Music did not start");
        }

        ct.ThrowIfCancellationRequested();
        if (launched && transport && TransportViaSession(cmd.Action, true, 3000, ct) is { } afterStart)
        {
            guard.Restore();
            Say($"result: {(afterStart.Ok ? "ok" : afterStart.Failure + " " + afterStart.Detail)} (media session after start); foreground after: {AmNative.Describe(AmNative.GetForegroundWindow())}");
            return afterStart;
        }

        AmResult result;
        using (new PopupHider(am.Hwnd, am.Pid))
        using (new HiddenWindow(am.Hwnd, Say, _keeper))
        {
            result = cmd.Action switch
            {
                AmAction.PlayPlaylist or AmAction.ShufflePlaylist => PlayPlaylist(am, cmd, ct),
                AmAction.ReadQueue => ReadQueue(am, ct),
                AmAction.PlayPause or AmAction.SkipForward or AmAction.SkipBack => PressTransport(am, cmd.Action, launched ? 10000 : 4000, ct),
                AmAction.Seek => SeekScrubber(am, cmd.SeekTo, ct),
                _ => PlaySong(am, cmd, launched ? 10000 : 4500, ct),
            };

            if (result.Ok && cmd.ResumeAt is { } resumeAt && cmd.Action is AmAction.Play or AmAction.PlayPlaylist or AmAction.ShufflePlaylist)
            {
                // continue where a closed Apple Music stopped (a failed seek still leaves the song playing)
                var seek = SeekScrubber(am, resumeAt, ct);
                Say(seek.Ok ? $"resumed at {resumeAt:0.#} s" : "could not seek to the old position: " + seek.Detail);
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

        Say($"result: {(result.Ok ? "ok" : result.Failure + " " + result.Detail)}; foreground after: {AmNative.Describe(AmNative.GetForegroundWindow())}");
        return result;
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

    /// <summary>Presses Apple Music's own transport button (used while Windows has no media session for it, e.g. right after a hidden start).</summary>
    private AmResult PressTransport(AmUia am, AmAction action, int wait, CancellationToken ct)
    {
        var id = action switch
        {
            AmAction.SkipForward => "TransportControl_SkipForward",
            AmAction.SkipBack => "TransportControl_SkipBack",
            _ => "TransportControl_PlayPauseStop",
        };
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

    private AmResult PlaySong(AmUia am, AmCommand cmd, int menuWait, CancellationToken ct)
    {
        var track = cmd.Track ?? throw new ArgumentException("track");
        var navId = cmd.Playlist is not null ? cmd.PlaylistAutomationId : (cmd.Page == "recent" ? "Sidebar_Library_RecentlyAdded" : "Sidebar_Library_Songs");
        am.ExpandSidebar();
        var nav = am.WaitFor(() => am.ById(navId), 6000, ct);
        if (nav is null && cmd.Page == "recent" && cmd.Playlist is null)
        {
            nav = am.WaitFor(() => am.ById("Sidebar_Library_Songs"), 3000, ct);
        }

        if (nav is null)
        {
            return AmResult.Fail(AmFailure.NotFound, "sidebar item " + navId);
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

    private AmResult PlayPlaylist(AmUia am, AmCommand cmd, CancellationToken ct)
    {
        am.ExpandSidebar();
        var nav = am.WaitFor(() => am.ById(cmd.PlaylistAutomationId), 6000, ct);
        if (nav is null)
        {
            return AmResult.Fail(AmFailure.NotFound, "playlist " + cmd.Playlist?.Name);
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

    /// <summary>Starts Apple Music minimised without activation; every window it creates is transparent from the moment it exists.</summary>
    private AmUia? LaunchHidden(ForegroundGuard guard, CancellationToken ct)
    {
        if (!File.Exists(AmDb.AliasPath))
        {
            return null;
        }

        if (_keeper is { Enabled: true })
        {
            return LaunchKept(_keeper, guard, ct);
        }

        Say("Apple Music is not running; starting it hidden");
        var touched = new Dictionary<nint, long>();
        var watch = Stopwatch.StartNew();
        using var hook = new AmEventHook(AmNative.EventObjectCreate, AmNative.EventObjectShow, 0, (ev, hwnd) =>
        {
            if (AmNative.GetAncestor(hwnd, AmNative.GaRoot) != hwnd)
            {
                return;
            }

            string name;
            try
            {
                name = Process.GetProcessById((int)AmNative.ProcessOf(hwnd)).ProcessName;
            }
            catch
            {
                return;
            }

            if (name != "AppleMusic")
            {
                return;
            }

            var cls = AmNative.ClassOf(hwnd);
            if (cls is not (WindowClass or PopupClass))
            {
                return;
            }

            lock (touched)
            {
                if (!touched.ContainsKey(hwnd))
                {
                    var ex = AmNative.GetWindowLongPtr(hwnd, AmNative.GwlExStyle);
                    touched[hwnd] = ex;
                    AmNative.SetWindowLongPtr(hwnd, AmNative.GwlExStyle, (nint)(ex | AmNative.WsExLayered | AmNative.WsExTransparent));
                    AmNative.SetLayeredWindowAttributes(hwnd, 0, 0, AmNative.LwaAlpha);
                }
            }
        });
        var info = new AmNative.StartupInfo { Cb = System.Runtime.InteropServices.Marshal.SizeOf<AmNative.StartupInfo>(), Flags = 1 | 4, ShowWindow = AmNative.SwShowMinNoActive, X = -32000, Y = -32000 }; // STARTF_USESHOWWINDOW | STARTF_USEPOSITION
        var started = AmNative.CreateProcess(null, new System.Text.StringBuilder($"\"{AmDb.AliasPath}\""), 0, 0, false, 0, 0, null, ref info, out var pi);
        if (started)
        {
            AmNative.CloseHandle(pi.Process);
            AmNative.CloseHandle(pi.Thread);
        }
        else
        {
            return null;
        }

        AmUia? am = null;
        while (watch.ElapsedMilliseconds < 30000)
        {
            ct.ThrowIfCancellationRequested();
            am ??= AmUia.Find();
            if (am is not null && !AmNative.IsIconic(am.Hwnd))
            {
                AmNative.ShowWindow(am.Hwnd, AmNative.SwShowMinNoActive);
            }

            if (am is not null && am.ById("TransportControl_PlayPauseStop") is not null)
            {
                Say($"Apple Music UI ready after {watch.ElapsedMilliseconds} ms");
                break;
            }

            Thread.Sleep(50);
        }

        // it may restore itself a moment later: keep minimising for two seconds
        for (var i = 0; i < 40 && am is not null; i++)
        {
            if (!AmNative.IsIconic(am.Hwnd))
            {
                AmNative.ShowWindow(am.Hwnd, AmNative.SwShowMinNoActive);
                Say("late restore -> re-minimised");
            }

            Thread.Sleep(50);
        }

        hook.Dispose();
        lock (touched)
        {
            foreach (var (h, ex) in touched)
            {
                if (AmNative.IsWindow(h))
                {
                    AmNative.SetWindowLongPtr(h, AmNative.GwlExStyle, (nint)ex);
                }
            }
        }

        guard.Restore();
        return am;
    }

    /// <summary>
    /// Starts Apple Music so that it never has a taskbar button: it is shown without activation (a window hidden from the start never
    /// realises its UI), every window it creates is transparent, click-through and a tool window from the moment it exists, the main window
    /// is parked far off-screen, and once the transport bar is up it is hidden and its styles are put back (the caller ghosts it again for its job).
    /// </summary>
    private AmUia? LaunchKept(AppleMusicKeeper keeper, ForegroundGuard guard, CancellationToken ct)
    {
        Say("Apple Music is not running; starting it out of sight");
        var touched = new Dictionary<nint, long>();
        var rects = new Dictionary<nint, AmNative.Rect>();
        var watch = Stopwatch.StartNew();
        AmUia? am = null;
        keeper.Launching = true;
        try
        {
            using (var hook = new AmEventHook(AmNative.EventObjectCreate, AmNative.EventObjectShow, 0, (ev, hwnd) =>
            {
                if (AmNative.GetAncestor(hwnd, AmNative.GaRoot) != hwnd)
                {
                    return;
                }

                string name;
                try
                {
                    name = Process.GetProcessById((int)AmNative.ProcessOf(hwnd)).ProcessName;
                }
                catch
                {
                    return;
                }

                if (name != "AppleMusic")
                {
                    return;
                }

                var cls = AmNative.ClassOf(hwnd);
                if (cls is not (WindowClass or PopupClass))
                {
                    return;
                }

                lock (touched)
                {
                    if (!touched.ContainsKey(hwnd))
                    {
                        var ex = AmNative.GetWindowLongPtr(hwnd, AmNative.GwlExStyle);
                        touched[hwnd] = ex;
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
                        if (!rects.ContainsKey(hwnd) && AmNative.GetWindowRect(hwnd, out var r) && AmNative.OnAnyScreen(r))
                        {
                            rects[hwnd] = r;
                        }

                        AmNative.SetWindowPos(hwnd, 0, -30000, -30000, 2400, 1500, AmNative.SwpNoZOrder | AmNative.SwpNoActivate | AmNative.SwpNoOwnerZOrder);
                    }
                }
            }))
            {
                var info = new AmNative.StartupInfo { Cb = System.Runtime.InteropServices.Marshal.SizeOf<AmNative.StartupInfo>(), Flags = 1, ShowWindow = AmNative.SwShowNoActivate }; // STARTF_USESHOWWINDOW
                var started = AmNative.CreateProcess(null, new System.Text.StringBuilder($"\"{AmDb.AliasPath}\""), 0, 0, false, 0, 0, null, ref info, out var pi);
                if (!started)
                {
                    return null;
                }

                AmNative.CloseHandle(pi.Process);
                AmNative.CloseHandle(pi.Thread);
                while (watch.ElapsedMilliseconds < 30000)
                {
                    ct.ThrowIfCancellationRequested();
                    am ??= AmUia.Find(keeper);
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

                if (am is not null)
                {
                    AmNative.ShowWindow(am.Hwnd, AmNative.SwHide);
                    if (rects.TryGetValue(am.Hwnd, out var home))
                    {
                        AmNative.SetWindowPos(am.Hwnd, 0, home.Left, home.Top, home.Right - home.Left, home.Bottom - home.Top, AmNative.SwpNoZOrder | AmNative.SwpNoActivate | AmNative.SwpNoOwnerZOrder);
                    }
                }
            }

            lock (touched)
            {
                foreach (var (h, ex) in touched)
                {
                    if (AmNative.IsWindow(h))
                    {
                        AmNative.SetWindowLongPtr(h, AmNative.GwlExStyle, (nint)ex);
                    }
                }
            }
        }
        finally
        {
            keeper.Launching = false;
        }

        guard.Restore();
        return am;
    }

    // ---------------------------------------------------------------- window hiding

    /// <summary>
    /// Makes every extra top-level window of Apple Music (the context menu's "PopupHost") transparent and click-through the moment it is
    /// created or shown, and puts the original extended style back afterwards (the popups are cached and reused by the app).
    /// </summary>
    private sealed class PopupHider : IDisposable
    {
        private readonly AmEventHook _hook;
        private readonly Dictionary<nint, long> _touched = [];

        public PopupHider(nint main, uint pid)
        {
            _hook = new AmEventHook(AmNative.EventObjectCreate, AmNative.EventObjectShow, pid, (_, hwnd) =>
            {
                if (hwnd == main || AmNative.GetAncestor(hwnd, AmNative.GaRoot) != hwnd)
                {
                    return;
                }

                lock (_touched)
                {
                    if (!_touched.ContainsKey(hwnd))
                    {
                        var ex = AmNative.GetWindowLongPtr(hwnd, AmNative.GwlExStyle);
                        _touched[hwnd] = ex;
                        AmNative.SetWindowLongPtr(hwnd, AmNative.GwlExStyle, (nint)(ex | AmNative.WsExLayered | AmNative.WsExTransparent));
                    }
                }

                AmNative.SetLayeredWindowAttributes(hwnd, 0, 0, AmNative.LwaAlpha);
            });
        }

        public void Dispose()
        {
            _hook.Dispose();
            lock (_touched)
            {
                foreach (var (h, ex) in _touched)
                {
                    if (AmNative.IsWindow(h))
                    {
                        AmNative.SetWindowLongPtr(h, AmNative.GwlExStyle, (nint)ex);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A minimised WinUI window does not realise its UI, so it is restored for the duration: fully transparent and click-through,
    /// without activation, parked far off-screen and wide enough that the NavigationView stays expanded. Disposing puts it back.
    /// </summary>
    private sealed class HiddenWindow : IDisposable
    {
        private readonly nint _hwnd;
        private readonly AmNative.WindowPlacement _original;
        private readonly bool _wasIconic;
        private readonly nint _originalEx;
        private readonly Action<string> _say;
        private readonly AppleMusicKeeper? _keeper;
        private readonly bool _wasHidden;

        public HiddenWindow(nint hwnd, Action<string> say, AppleMusicKeeper? keeper = null)
        {
            _hwnd = hwnd;
            _say = say;
            _keeper = keeper is { Enabled: true } ? keeper : null;
            _wasHidden = !AmNative.IsWindowVisible(hwnd);
            AmNative.GetWindowRect(hwnd, out var rect);
            _wasIconic = _wasHidden || AmNative.IsIconic(hwnd) || !OnAnyScreen(rect);
            _original = new AmNative.WindowPlacement { Length = System.Runtime.InteropServices.Marshal.SizeOf<AmNative.WindowPlacement>() };
            AmNative.GetWindowPlacement(hwnd, ref _original);
            if (!OnAnyScreen(_original.Normal))
            {
                // never remember an off-screen restore position (a previous run may have left one)
                var w = AmNative.GetSystemMetrics(0);
                var h = AmNative.GetSystemMetrics(1);
                _original.Normal = new AmNative.Rect { Left = (w - 1200) / 2, Top = (h - 800) / 2, Right = ((w - 1200) / 2) + 1200, Bottom = ((h - 800) / 2) + 800 };
            }

            _originalEx = AmNative.GetWindowLongPtr(hwnd, AmNative.GwlExStyle);
            if (!_wasIconic)
            {
                // the user has the window open: work on it in place (only its menus are hidden)
                _say("Apple Music window is open; working in place");
                return;
            }

            var ghost = (nint)(_originalEx | AmNative.WsExLayered | AmNative.WsExTransparent);
            if (_wasHidden || _keeper is not null)
            {
                // no taskbar button while it is shown for the job
                ghost = (nint)((ghost | AmNative.WsExToolWindow) & ~AmNative.WsExAppWindow);
            }

            AmNative.SetWindowLongPtr(hwnd, AmNative.GwlExStyle, ghost);
            AmNative.SetLayeredWindowAttributes(hwnd, 0, 0, AmNative.LwaAlpha);
            var wp = _original;
            wp.ShowCmd = AmNative.SwShowNoActivate;
            wp.Flags = 0;
            AmNative.SetWindowPlacement(hwnd, ref wp);
            AmNative.SetWindowPos(hwnd, 0, -30000, -30000, 2400, 1500, AmNative.SwpNoZOrder | AmNative.SwpNoActivate | AmNative.SwpNoOwnerZOrder);
        }

        private static bool OnAnyScreen(AmNative.Rect r) => AmNative.OnAnyScreen(r);

        public void Dispose()
        {
            if (!_wasIconic)
            {
                return;
            }

            if (_keeper is not null)
            {
                // Kept out of sight: hidden while still transparent, back at the user's own restore position (so that is where Windows
                // remembers it), original styles back, then the keeper takes it over (no taskbar button, no Alt+Tab entry).
                AmNative.ShowWindow(_hwnd, AmNative.SwHide);
                var rect = _original.Normal;
                AmNative.SetWindowPos(_hwnd, 0, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, AmNative.SwpNoZOrder | AmNative.SwpNoActivate | AmNative.SwpNoOwnerZOrder);
                AmNative.SetWindowLongPtr(_hwnd, AmNative.GwlExStyle, _originalEx);
                _keeper.Adopt(_hwnd, AmNative.ProcessOf(_hwnd));
                _say("window hidden and kept; foreground now " + AmNative.Describe(AmNative.GetForegroundWindow()));
                return;
            }

            // Everything happens while the window is still transparent and click-through. It goes back to the user's own restore
            // position first (so that is where Windows remembers it), then it is minimised without activation.
            var foreground = AmNative.ProcessOf(AmNative.GetForegroundWindow()) == AmNative.ProcessOf(_hwnd);
            if (foreground)
            {
                // Apple Music still owns the activation: hiding the window makes Windows pass it on to the next window in z-order
                // (minimising alone can leave the minimised window as the foreground window).
                AmNative.ShowWindow(_hwnd, AmNative.SwHide);
            }

            var n = _original.Normal;
            AmNative.SetWindowPos(_hwnd, 0, n.Left, n.Top, n.Right - n.Left, n.Bottom - n.Top, AmNative.SwpNoZOrder | AmNative.SwpNoActivate | AmNative.SwpNoOwnerZOrder);
            AmNative.ShowWindow(_hwnd, AmNative.SwShowMinNoActive);
            if (!AmNative.IsIconic(_hwnd))
            {
                // some builds do not minimise a layered window: put the style back and do it again
                AmNative.SetWindowLongPtr(_hwnd, AmNative.GwlExStyle, _originalEx);
                AmNative.ShowWindow(_hwnd, AmNative.SwShowMinNoActive);
            }

            AmNative.SetWindowLongPtr(_hwnd, AmNative.GwlExStyle, _originalEx);
            _say($"window minimised again{(foreground ? " (hidden first to pass the activation on)" : "")}; foreground now {AmNative.Describe(AmNative.GetForegroundWindow())}");
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

    public void ExpandSidebar()
    {
        foreach (var id in new[] { "Sidebar_Header_Library", "Sidebar_Header_Playlists" })
        {
            if (ById(id) is { } e && e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p) && ((ExpandCollapsePattern)p).Current.ExpandCollapseState != ExpandCollapseState.Expanded)
            {
                ((ExpandCollapsePattern)p).Expand();
            }
        }
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
