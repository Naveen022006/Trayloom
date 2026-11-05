// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

internal enum LibraryState
{
    Idle,
    Loading,
    Ready,
    Missing,
    Unreadable,
}

/// <summary>What the Library panel remembers while the music page is rebuilt (track changes rebuild the page).</summary>
internal sealed class LibraryUi
{
    /// <summary>0 = Playlists, 1 = Songs, 2 = Recently added, 3 = Albums, 4 = Artists (shown in the order Playlists, Songs, Albums, Artists, Recently added).</summary>
    public int Tab { get; set; } = 1;

    public ulong? PlaylistPid { get; set; }

    public string Search { get; set; } = "";

    public Dictionary<string, double> Scroll { get; } = [];
}

/// <summary>A short line under the panel header: a failure (orange) or a confirmation.</summary>
internal sealed record LibraryNote(string Text, bool Error);

/// <summary>Where a transport press came from: a button of the island, or a keyboard shortcut (which has no page to show progress on).</summary>
internal enum TransportOrigin
{
    Island,
    Shortcut,
}

/// <summary>
/// The Apple Music library for the island: the song / playlist snapshot (reloaded when the file changes), the requests to play,
/// queue or shuffle things (served by <see cref="AppleMusicDriver"/> off the UI thread) and the state the panel shows.
/// UI-thread object.
/// </summary>
internal sealed class AppleMusicService : IDisposable
{
    private readonly IslandCore _core;
    private readonly Lazy<AppleMusicDriver> _driver;
    private readonly DispatcherTimer _noteTimer;
    private int _seq;
    private int _busySeq;
    private bool _disposed;
    private DateTime _availableAt = DateTime.MinValue;
    private bool _available;
    private int _loadId;
    private AmCommand? _lastCommand;
    private ClosedSnapshot? _snapshot;
    private CancellationTokenSource? _resume;
    private TaskCompletionSource? _expedite;

    // the library file is watched while a page of the music UI is on screen (see WatchLibrary)
    private readonly DispatcherTimer _watchTimer;
    private bool _reloading;
    private DateTime? _seenStamp;
    private DateTime? _failedStamp;

    // what IsCurrent compares tracks with, folded once per media session sample instead of once per row
    private string _currentSource = "";
    private string _currentTitle = "";
    private string _currentDetail = "";

    // "Play" with nothing loaded: choosing and starting something sensible counts as busy from the press on
    private bool _choosing;
    private DateTime _requestAt = DateTime.MinValue;
    private bool _noticeShown;
    private DateTime _noticeAt = DateTime.MinValue;

    // the song Apple Music played last: the position is kept in memory and stored (settings.json) on a change of song, a pause, at most once a minute while playing, and when the service ends (see KeepLastPlayed)
    private string _lastKey = "";
    private bool _lastPlaying;
    private DateTime _lastWritten = DateTime.MinValue;
    private DateTime _lastSampled = DateTime.MinValue;
    private LastPlayed? _lastPlayed;
    private string _lastRaw = "";
    private bool _lastDirty;

    public AppleMusicService(IslandCore core)
    {
        _core = core;
        Keeper = new AppleMusicKeeper(core, () => _lastCommand);
        Keeper.ClosedWhilePlaying += OnClosedWhilePlaying;
        core.Playback.Changed += OnPlaybackChanged;
        core.Playback.AppleMusicSeek = Seek;
        _driver = new Lazy<AppleMusicDriver>(() => new AppleMusicDriver(line => _core.Ctx.Log.Info("AppleMusic " + line), Keeper));
        _noteTimer = new DispatcherTimer(DispatcherPriority.Normal, core.Ui);
        _noteTimer.Tick += (_, _) =>
        {
            _noteTimer.Stop();
            Note = null;
            Changed?.Invoke();
        };
        _watchTimer = new DispatcherTimer(DispatcherPriority.Background, core.Ui) { Interval = TimeSpan.FromSeconds(5) };
        _watchTimer.Tick += (_, _) => WatchLibrary();
    }

    public LibraryUi Ui { get; } = new();

    public AmDb? Db { get; private set; }

    public LibraryState State { get; private set; } = LibraryState.Idle;

    public LibraryNote? Note { get; private set; }

    /// <summary>The song or playlist (by Pid) a request is running for.</summary>
    public ulong? BusyPid { get; private set; }

    /// <summary>The running request is "Shuffle" of a playlist.</summary>
    public bool BusyShuffle { get; private set; }

    /// <summary>What the running request does (also set for the transport buttons, which have no <see cref="BusyPid"/>); null = nothing runs.</summary>
    public AmAction? BusyAction { get; private set; }

    /// <summary>Apple Music is being started or driven for a request of the island, or is being started again after it was closed.</summary>
    public bool Busy => BusyAction is not null || Resuming || _choosing;

    /// <summary>How many pages that show Apple Music's progress (the Music page, the Controls card) are on screen.</summary>
    public int Viewers { get; private set; }

    /// <summary>A page that shows the progress of Apple Music requests came on screen.</summary>
    public void ViewerShown()
    {
        Viewers++;
        if (Viewers == 1 && !_disposed)
        {
            _seenStamp = null;
            _watchTimer.Start();
        }
    }

    /// <summary>A page that shows the progress of Apple Music requests went away.</summary>
    public void ViewerHidden()
    {
        Viewers = Math.Max(0, Viewers - 1);
        if (Viewers == 0)
        {
            _watchTimer.Stop();
        }
    }

    public IReadOnlyList<AmQueueItem>? Queue { get; private set; }

    public bool QueueLoading { get; private set; }

    public AppleMusicDriver Driver => _driver.Value;

    /// <summary>Hides Apple Music's window while the island plays it and notices when it is closed.</summary>
    public AppleMusicKeeper Keeper { get; }

    /// <summary>Apple Music is being started again after it was closed while playing.</summary>
    public bool Resuming { get; private set; }

    public event Action? Changed;

    /// <summary>Apple Music for Windows is installed (its command alias or its library file exists).</summary>
    public bool Available
    {
        get
        {
            if ((DateTime.UtcNow - _availableAt).TotalSeconds > 20)
            {
                _availableAt = DateTime.UtcNow;
                try
                {
                    _available = File.Exists(AmDb.AliasPath) || File.Exists(AmDb.LibraryPath);
                }
                catch
                {
                    _available = false;
                }
            }

            return _available;
        }
    }

    public bool IsCurrent(AmTrack t)
    {
        var p = _core.Playback.Current;
        if (!p.HasTrack || !p.Aumid.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // the session puts the album behind the artist ("Artist — Album"): the sample is split and folded once, not per row
        var source = $"{p.Title}|{p.Artist}|{p.Album}";
        if (source != _currentSource)
        {
            _currentSource = source;
            _currentTitle = p.Title;
            _currentDetail = AmIdle.DetailKey(p.Artist, p.Album);
        }

        return AmIdle.IsPlaying(t, _currentTitle, _currentDetail);
    }

    // ---------------------------------------------------------------- library file

    /// <summary>Loads the library (again when the file changed); raises <see cref="Changed"/> when done.</summary>
    public void EnsureLoaded(bool force = false)
    {
        if (_disposed || State == LibraryState.Loading || _reloading)
        {
            return;
        }

        string path;
        DateTime stamp;
        try
        {
            path = AmDb.LibraryPath;
            if (!File.Exists(path))
            {
                Db = null;
                State = LibraryState.Missing;
                Changed?.Invoke();
                return;
            }

            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            State = LibraryState.Unreadable;
            Changed?.Invoke();
            return;
        }

        if (!force && Db is not null && Db.Stamp == stamp && State == LibraryState.Ready)
        {
            return;
        }

        var hadData = Db is not null;
        if (!hadData)
        {
            State = LibraryState.Loading;
            Changed?.Invoke();
        }

        var id = ++_loadId;
        _reloading = true;
        _failedStamp = null;
        _ = Task.Run(() =>
        {
            AmDb? db = null;
            Exception? error = null;
            for (var attempt = 0; attempt < 3 && db is null; attempt++)
            {
                try
                {
                    db = AmDb.Load(path);
                }
                catch (Exception ex)
                {
                    error = ex;
                    Thread.Sleep(200);
                }
            }

            _core.Ui.BeginInvoke(() =>
            {
                _reloading = false;
                if (_disposed || id != _loadId)
                {
                    return;
                }

                if (db is not null)
                {
                    Db = db;
                    State = LibraryState.Ready;
                }
                else
                {
                    _core.Ctx.Log.Warn("Apple Music library could not be read: " + error?.Message);
                    State = Db is null ? LibraryState.Unreadable : LibraryState.Ready;
                    _failedStamp = stamp;
                }

                Changed?.Invoke();
            });
        });
    }

    /// <summary>
    /// While a page of the music UI is on screen, polls the library file's stamp every 5 s and reloads it once it has changed and rested
    /// (<see cref="AmReload.ShouldLoad"/>); loads never run in parallel (<see cref="EnsureLoaded"/>). Cheap: one file-info call.
    /// </summary>
    private void WatchLibrary()
    {
        if (_disposed || Viewers == 0 || _reloading || State == LibraryState.Loading || Db is not { } db)
        {
            return;
        }

        try
        {
            if (File.Exists(AmDb.LibraryPath) && AmReload.ShouldLoad(db.Stamp, _failedStamp, File.GetLastWriteTimeUtc(AmDb.LibraryPath), ref _seenStamp))
            {
                _core.Ctx.Log.Info("Apple Music library changed; reloading it");
                EnsureLoaded();
            }
        }
        catch
        {
            // the file is busy or gone: the next poll looks again
        }
    }

    // ---------------------------------------------------------------- requests

    public void PlaySong(AmTrack track, AmPlaylist? context, string page, AmAction action = AmAction.Play)
    {
        if (Db is not { } db)
        {
            return;
        }

        if (action != AmAction.AddToQueue)
        {
            CancelResume();
        }

        var position = db.PagePosition(track, context);
        Guarded.FireAndLog(() => RunAsync(new AmCommand
        {
            Action = action,
            Track = track,
            Playlist = context,
            Page = page,
            PageIndex = position.Index,
            PageCount = position.Count,
            DbId = db.DbId,
            PlaylistAutomationId = context is null ? "" : db.PlaylistAutomationId(context.Pid),
        }, track.Pid), _core.Ctx.Log, "Apple Music " + action);
    }

    public void PlayPlaylist(AmPlaylist playlist, bool shuffle)
    {
        if (Db is not { } db)
        {
            return;
        }

        CancelResume();
        Guarded.FireAndLog(() => RunAsync(new AmCommand
        {
            Action = shuffle ? AmAction.ShufflePlaylist : AmAction.PlayPlaylist,
            Playlist = playlist,
            DbId = db.DbId,
            PlaylistAutomationId = db.PlaylistAutomationId(playlist.Pid),
        }, playlist.Pid), _core.Ctx.Log, "Apple Music playlist");
    }

    /// <summary>
    /// Play/pause, next or previous for Apple Music when Windows has no media session for it (the app is closed or idle):
    /// it is started hidden if needed and its own button is pressed, so its window never shows. With nothing loaded in Apple Music (it
    /// would only fail) the island plays something sensible instead: the last played song, Favourite Songs shuffled, or a library song.
    /// </summary>
    public void Transport(AmAction action, TransportOrigin origin = TransportOrigin.Island)
    {
        if (_disposed)
        {
            return;
        }

        if (!Available)
        {
            if (origin == TransportOrigin.Shortcut)
            {
                Toast(LibStrings.NotStarted);
            }

            return;
        }

        if (Busy)
        {
            if (Resuming && action == AmAction.PlayPause)
            {
                // the press joins the resume that is already under way (one job, no second start): it just stops waiting for the library agent
                _core.Ctx.Log.Info("AppleMusic: play pressed while resuming; the resume goes ahead now");
                _expedite?.TrySetResult();
                return;
            }

            OnRepeatedPress(action, origin);
            return;
        }

        if (action == AmAction.PlayPause)
        {
            // Apple Music was closed while it played: "play" continues that song instead of failing for lack of anything loaded
            // (whether or not Apple Music runs again by now: a restarted one has nothing loaded either)
            if (_snapshot is { } closed && DateTime.UtcNow - closed.At < TimeSpan.FromMinutes(30)
                && _core.Playback.AppleMusicNow() is var now && now is not { IsPlaying: true } && (now is null || string.IsNullOrEmpty(now.Title)))
            {
                Guarded.FireAndLog(() => ResumeAfterQuit(closed, false), _core.Ctx.Log, "Apple Music resume");
                return;
            }
        }
        else
        {
            // skipping is the person's own choice of what happens next
            CancelResume();
        }

        // Windows has a session for Apple Music only while it has something loaded: without one the buttons would fail
        var loaded = _core.Playback.AppleMusicNow() is { } current && !string.IsNullOrEmpty(current.Title);
        Guarded.FireAndLog(() => TransportAsync(action, origin, loaded), _core.Ctx.Log, "Apple Music " + action);
    }

    /// <summary>A press while a request still runs: it is not started again (that would cancel its own cold start), but the person is told.</summary>
    private void OnRepeatedPress(AmAction action, TransportOrigin origin)
    {
        var elapsed = (DateTime.UtcNow - _requestAt).TotalSeconds;
        _core.Ctx.Log.Info($"AppleMusic {action}: already running for {elapsed:0.0} s; the repeated press is ignored");
        var toast = origin == TransportOrigin.Shortcut || Viewers == 0;
        if (elapsed > 3)
        {
            SetNote(new LibraryNote(LibStrings.StillStarting, false));
            Changed?.Invoke();
            if (toast && (DateTime.UtcNow - _noticeAt).TotalSeconds > 3)
            {
                Toast(LibStrings.StillStarting);
            }
        }
        else if (toast && !_noticeShown)
        {
            Toast(LibStrings.StartingApple);
        }
    }

    /// <summary>A short notice on the closed island (a shortcut, or no page of the island shows the progress): what Apple Music is doing or why it did not.</summary>
    private void Toast(string title)
    {
        _noticeShown = true;
        _noticeAt = DateTime.UtcNow;
        _core.ShowNotice(new Notice { Event = NoticeEvents.AppleMusic, Glyph = G.Music, Title = title });
    }

    private void BeginRequest()
    {
        _requestAt = DateTime.UtcNow;
        _noticeShown = false;
    }

    /// <summary>
    /// The transport press as one job: Apple Music's own button when it has something loaded, else (or when the button turns out to be dead)
    /// something sensible is played. <see cref="Busy"/> stays true from the press to the end, so the person sees a spinner at once.
    /// </summary>
    private async Task TransportAsync(AmAction action, TransportOrigin origin, bool loaded)
    {
        var toast = origin == TransportOrigin.Shortcut || Viewers == 0;
        BeginRequest();
        _choosing = true;
        SetNote(null);
        Changed?.Invoke();
        try
        {
            if (loaded)
            {
                var before = _seq;
                var result = await RunAsync(new AmCommand { Action = action }, 0, toast, NothingToPress);
                if (result.Ok || _disposed || _seq != before + 1 || !NothingToPress(result))
                {
                    return;
                }

                _core.Ctx.Log.Info($"AppleMusic {action}: nothing is loaded in Apple Music ({result.Failure}); playing something instead");
            }

            await PlaySomethingAsync(action, toast);
        }
        finally
        {
            _choosing = false;
            if (!_disposed)
            {
                Changed?.Invoke();
            }
        }
    }

    /// <summary>
    /// Apple Music's own button could not be used because nothing is loaded. A button that was merely not found (NoMenu: UI Automation
    /// hiccup, another player is the current media session) says nothing about that while Apple Music is playing: a press meant as Pause must
    /// not start the last played song over it.
    /// </summary>
    private bool NothingToPress(AmResult result) =>
        result.Failure is AmFailure.NothingLoaded || (result.Failure is AmFailure.NoMenu && _core.Playback.AppleMusicNow()?.IsPlaying != true);

    /// <summary>
    /// Play, Next or Previous with nothing loaded in Apple Music: starts the last played song (from where it was left, when that was recently),
    /// else Favourite Songs shuffled, else a library song; when Apple Music cannot find or start the first, the next is tried once.
    /// </summary>
    private async Task PlaySomethingAsync(AmAction action, bool toast)
    {
        if (toast && !_noticeShown)
        {
            Toast(LibStrings.StartingApple);
        }

        EnsureLoaded();
        var wait = Stopwatch.StartNew();
        while (Db is null && State is LibraryState.Idle or LibraryState.Loading && wait.ElapsedMilliseconds < 3000 && !_disposed)
        {
            await Task.Delay(50);
        }

        if (_disposed)
        {
            return;
        }

        var db = Db;
        var plan = db is null ? new IdlePlan([]) : AmIdle.Choose(db, ReadLastPlayed(), DateTime.UtcNow, Random.Shared);
        if (db is null || plan.First is null)
        {
            _core.Ctx.Log.Warn($"AppleMusic {action}: nothing to play (library {State})");
            SetNote(new LibraryNote(LibStrings.NothingToResume, true));
            Changed?.Invoke();
            if (toast)
            {
                Toast(LibStrings.NothingToResume);
            }

            MusicPage.OpenLibrary(_core);
            return;
        }

        // at most two attempts: the first is the best choice, the second the fallback when Apple Music cannot find or start it
        var steps = plan.Steps.Take(2).ToList();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var last = i == steps.Count - 1;
            var command = step.Kind == IdleStepKind.ShufflePlaylist
                ? new AmCommand
                {
                    Action = AmAction.ShufflePlaylist,
                    Playlist = step.Playlist,
                    DbId = db.DbId,
                    PlaylistAutomationId = db.PlaylistAutomationId(step.Playlist!.Pid),
                }
                : new AmCommand
                {
                    Action = AmAction.Play,
                    Track = step.Track,
                    Playlist = step.Playlist,
                    Page = step.Page,
                    PageIndex = step.Track is null ? -1 : db.PagePosition(step.Track, step.Playlist).Index,
                    PageCount = step.Track is null ? 0 : db.PagePosition(step.Track, step.Playlist).Count,
                    DbId = db.DbId,
                    PlaylistAutomationId = step.Playlist is null ? "" : db.PlaylistAutomationId(step.Playlist.Pid),
                    ResumeAt = step.ResumeAt,
                    SeekDuration = step.Track is { } resumed ? resumed.DurationMs / 1000.0 : 0,
                };
            _core.Ctx.Log.Info($"AppleMusic {action} with nothing loaded: {step.Why} ({step.Track?.Title ?? step.Playlist?.Name}{(step.ResumeAt is { } at ? $" from {at:0} s" : "")})");
            var before = _seq;
            var result = await RunAsync(command, step.Track?.Pid ?? step.Playlist!.Pid, toast, r => !last && r.Failure is AmFailure.NotFound or AmFailure.NotConfirmed);
            if (result.Ok || _disposed || _seq != before + 1 || result.Failure is not (AmFailure.NotFound or AmFailure.NotConfirmed))
            {
                return;
            }
        }
    }

    /// <summary>The last played song as stored (parsed once per stored value).</summary>
    private LastPlayed? ReadLastPlayed()
    {
        var json = _core.App.Get<string>(K.AppleMusicLastPlayed) ?? "";
        if (json != _lastRaw)
        {
            _lastRaw = json;
            _lastPlayed = LastPlayed.Parse(json);
        }

        return _lastPlayed;
    }

    /// <summary>Keeps the song Apple Music plays as the one an idle Play continues with: when it changes, when it pauses, and every 20 s while it plays.</summary>
    private void OnPlaybackChanged()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_core.Playback.AppleMusicNow() is not { } now || string.IsNullOrWhiteSpace(now.Title))
            {
                return;
            }

            // Windows folds the album into the artist ("Artist — Album"): the song is kept with the artist alone and the album apart
            var (artist, album) = AmIdle.SplitNowPlaying(now.Artist, now.Album);
            var key = $"{now.Title}|{artist}|{album}";
            var changed = key != _lastKey;
            var paused = _lastPlaying && !now.IsPlaying;
            var due = now.IsPlaying && (DateTime.UtcNow - _lastSampled).TotalSeconds >= 20;
            _lastPlaying = now.IsPlaying;
            if (!changed && !paused && !due)
            {
                return;
            }

            _lastKey = key;
            var stored = ReadLastPlayed();
            var same = stored is not null && AmIdle.SameSong(stored.Title, stored.Artist, now.Title, artist);
            if (same && !now.IsPlaying && now.Position < 1)
            {
                // Apple Music coming up with the song loaded but at its start must not wipe the position that was left
                return;
            }

            // the song's Pid (and the playlist it came from) is known from the island's own last request, when that was this song
            ulong trackPid = 0, playlistPid = 0;
            if (_lastCommand is { } command)
            {
                if (command.Track is { } track && AmIdle.SameTitle(track.Title, now.Title))
                {
                    trackPid = track.Pid;
                    playlistPid = command.Playlist?.Pid ?? 0;
                }
                else if (command.Playlist is { } playlist && playlist.Tracks.FirstOrDefault(t => AmIdle.SameTitle(t.Title, now.Title)) is { } inList)
                {
                    trackPid = inList.Pid;
                    playlistPid = playlist.Pid;
                }
            }

            if (trackPid == 0 && stored is { TrackPid: not 0 } && same)
            {
                // the same song sampled again (Apple Music restarted, a pause, the periodic sample): what is known about it is kept
                trackPid = stored.TrackPid;
                playlistPid = stored.PlaylistPid;
            }

            if (trackPid == 0 && Db is { } db && AmIdle.UniqueTrack(db.AllTracks, now.Title, artist, album) is { } unique)
            {
                trackPid = unique.Pid;
            }

            KeepLastPlayed(new LastPlayed(now.Title, artist, album, now.Position, now.Duration, trackPid, playlistPid, DateTime.UtcNow), changed || paused);
        }
        catch (Exception ex)
        {
            _core.Ctx.Log.Warn("Apple Music last played could not be kept: " + ex.Message);
        }
    }

    /// <summary>
    /// The newest position is kept in memory (what an idle Play reads); the setting is written when <paramref name="store"/> (a new song, a
    /// pause) or a minute after the last write, not on every sample: each write rewrites settings.json and tells the whole app that a setting changed.
    /// </summary>
    private void KeepLastPlayed(LastPlayed value, bool store)
    {
        _lastSampled = DateTime.UtcNow;
        _lastPlayed = value;
        _lastDirty = true;
        if (store || (DateTime.UtcNow - _lastWritten).TotalSeconds >= 60)
        {
            StoreLastPlayed();
        }
    }

    /// <summary>Writes the newest last played song to the setting, when it is not there yet.</summary>
    private void StoreLastPlayed()
    {
        if (!_lastDirty || _lastPlayed is not { } value)
        {
            return;
        }

        _lastDirty = false;
        _lastWritten = DateTime.UtcNow;
        _lastRaw = value.ToJson();
        _core.App.Set(K.AppleMusicLastPlayed, _lastRaw);
    }

    /// <summary>The song is known for sure (it was closed in the middle of it): stored at once.</summary>
    private void WriteLastPlayed(LastPlayed value) => KeepLastPlayed(value, true);

    /// <summary>The person asked for something else while Apple Music was being started again: the resume is dropped.</summary>
    private void CancelResume()
    {
        if (_resume is null && _snapshot is null)
        {
            return;
        }

        _resume?.Cancel();
        _snapshot = null;
    }

    /// <summary>Sets Apple Music's position (its media session ignores the request).</summary>
    public bool Seek(double seconds, double duration)
    {
        if (!Available || _disposed)
        {
            return false;
        }

        // UI thread: the song the seek is for (a seek that ends after the song has changed must not move the new one)
        var identity = _core.Playback.Current.Identity;
        Guarded.FireAndLog(() => Task.Run(async () =>
        {
            var result = await Driver.RunAsync(new AmCommand { Action = AmAction.Seek, SeekTo = seconds, SeekDuration = duration });
            if (!result.Ok && result.Failure == AmFailure.Cancelled && result.Detail != "timeout")
            {
                // replaced by a newer seek (or cancelled by another request): that one decides how the position looks
                return;
            }

            _ = _core.Ui.BeginInvoke(new Action(() =>
            {
                if (_disposed)
                {
                    return;
                }

                if (result.Ok)
                {
                    _core.Playback.NoteSeek(seconds, identity);
                    return;
                }

                _core.Ctx.Log.Warn($"Apple Music seek failed: {result.Failure} {result.Detail}");
                _core.Playback.NoteSeekFailed();
                Toast(LibStrings.SeekFailed);
            }));
        }), _core.Ctx.Log, "Apple Music seek");
        return true;
    }

    /// <summary>
    /// Runs one request and returns how it ended. <paramref name="toast"/>: a failure is also shown as a notice on the closed island.
    /// <paramref name="defer"/>: tells a failure the caller deals with itself (it tries something else), which is then not shown.
    /// </summary>
    private async Task<AmResult> RunAsync(AmCommand command, ulong pid, bool toast = false, Func<AmResult, bool>? defer = null)
    {
        if (_disposed)
        {
            return AmResult.Fail(AmFailure.Cancelled, "disposed");
        }

        // pressing again what is already starting must not cancel its own (cold) start
        if (BusyAction == command.Action && BusyPid is { } running && running == pid)
        {
            var elapsed = (DateTime.UtcNow - _requestAt).TotalSeconds;
            _core.Ctx.Log.Info($"AppleMusic {command.Action}: already running for {elapsed:0.0} s; the repeated request is ignored");
            if (elapsed > 3)
            {
                SetNote(new LibraryNote(LibStrings.StillStarting, false));
                Changed?.Invoke();
            }

            return AmResult.Fail(AmFailure.Cancelled, "repeated");
        }

        if (!Busy)
        {
            BeginRequest();
        }

        if (command.Action is AmAction.Play or AmAction.PlayPlaylist or AmAction.ShufflePlaylist)
        {
            _lastCommand = command;
        }

        if (command.Action is AmAction.Play or AmAction.PlayPlaylist or AmAction.ShufflePlaylist or AmAction.PlayPause or AmAction.SkipForward or AmAction.SkipBack)
        {
            // the island plays again: Apple Music's window is the driver's to manage (a window the person has open is still worked on in place)
            Keeper.ClearUserOwned();
        }

        var seq = ++_seq;
        _busySeq = seq;
        BusyPid = pid;
        BusyAction = command.Action;
        BusyShuffle = command.Action == AmAction.ShufflePlaylist;
        SetNote(null);
        Changed?.Invoke();
        AmResult result;
        try
        {
            result = await Driver.RunAsync(command);
        }
        catch (Exception ex)
        {
            result = AmResult.Fail(AmFailure.Failed, ex.Message);
        }

        if (_disposed)
        {
            return result;
        }

        // a failure is always on record, also when a newer request has taken over the island's state in the meantime
        if (!result.Ok && result.Failure != AmFailure.Cancelled)
        {
            _core.Ctx.Log.Warn($"Apple Music {command.Action} failed: {result.Failure} {result.Detail}");
        }

        if (_busySeq == seq)
        {
            // the request that set the busy state ends it, whatever the result
            BusyPid = null;
            BusyAction = null;
            BusyShuffle = false;
        }

        if (seq != _seq)
        {
            Changed?.Invoke();
            return result;
        }

        if (result.Ok)
        {
            if (command.Action is AmAction.Play or AmAction.PlayPlaylist or AmAction.ShufflePlaylist or AmAction.PlayPause)
            {
                _snapshot = null;
            }

            SetNote(command.Action switch
            {
                AmAction.PlayNext => new LibraryNote(LibStrings.AddedNext, false),
                AmAction.AddToQueue => new LibraryNote(LibStrings.AddedQueue, false),
                _ => null,
            });
        }
        else if (result.Failure != AmFailure.Cancelled && defer?.Invoke(result) != true)
        {
            var message = Message(command.Action, result.Failure);
            SetNote(new LibraryNote(message, true));
            if (toast)
            {
                Toast(message);
            }
        }

        Changed?.Invoke();
        return result;
    }

    private static string Message(AmAction action, AmFailure failure) => failure switch
    {
        AmFailure.Unavailable => LibStrings.NotStarted,
        AmFailure.NotFound => LibStrings.NotFound,
        AmFailure.NothingLoaded => LibStrings.NothingToResume,
        AmFailure.NoMenu when action is AmAction.PlayPause or AmAction.SkipForward or AmAction.SkipBack => LibStrings.NothingToResume,
        AmFailure.NoMenu => LibStrings.NoResponse,
        AmFailure.NotConfirmed => action is AmAction.PlayPlaylist or AmAction.ShufflePlaylist ? LibStrings.PlaylistNotStarted : LibStrings.SongNotStarted,
        _ => LibStrings.NoResponse,
    };

    private void SetNote(LibraryNote? note)
    {
        Note = note;
        _noteTimer.Stop();
        if (note is not null)
        {
            _noteTimer.Interval = TimeSpan.FromSeconds(note.Error ? 5 : 2.5);
            _noteTimer.Start();
        }
    }

    // ---------------------------------------------------------------- closed while playing

    private void OnClosedWhilePlaying(ClosedSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        _core.Ctx.Log.Info($"Apple Music was closed while playing '{snapshot.Title}' at {snapshot.Position:0} s");
        _snapshot = snapshot;

        // the position at the moment it closed is the most exact one there is
        var (artist, album) = AmIdle.SplitNowPlaying(snapshot.Artist, snapshot.Album);
        var carried = ReadLastPlayed() is { } stored && stored.TrackPid != 0 && AmIdle.SameSong(stored.Title, stored.Artist, snapshot.Title, artist) ? stored : null;
        WriteLastPlayed(new LastPlayed(snapshot.Title, artist, album, snapshot.Position, snapshot.Duration, snapshot.Track?.Pid ?? carried?.TrackPid ?? 0, snapshot.Playlist?.Pid ?? carried?.PlaylistPid ?? 0, snapshot.At));
        if (Keeper.ResumeEnabled)
        {
            Guarded.FireAndLog(() => ResumeAfterQuit(snapshot, true), _core.Ctx.Log, "Apple Music resume");
        }
    }

    private static int CountProcesses(string name)
    {
        var all = Process.GetProcessesByName(name);
        var n = all.Length;
        foreach (var p in all)
        {
            p.Dispose();
        }

        return n;
    }

    /// <summary>The track (library song or playlist-only) for what was playing; see <see cref="AmIdle.FindTrack"/>.</summary>
    private AmTrack? FindTrack(ClosedSnapshot s) =>
        Db is { } db ? AmIdle.FindTrack(db.AllTracks, s) : (s.Track is { } known && AmIdle.FindTrack([known], s) is not null ? known : null);

    /// <summary>
    /// Starts Apple Music again in the background and plays the song that was playing when it was closed, from the same position.
    /// Gives Apple Music's library agent (which actually plays the audio) a moment to end first: starting while it lives can leave orphan
    /// audio nobody can stop, but any start of Apple Music keeps the agent alive, so the wait is short and never a reason to give up.
    /// A press of Play meanwhile (<see cref="_expedite"/>) ends the wait at once.
    /// </summary>
    private async Task ResumeAfterQuit(ClosedSnapshot snapshot, bool announce)
    {
        _resume?.Cancel();
        var cts = _resume = new CancellationTokenSource();
        var expedite = _expedite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Resuming = true;
        BeginRequest();
        Changed?.Invoke();
        if (announce)
        {
            _core.ShowNotice(new Notice
            {
                Event = NoticeEvents.AppleMusic,
                Glyph = G.Music,
                Title = LibStrings.ClosedTitle,
                Detail = LibStrings.Resuming,
                OnClick = () => cts.Cancel(),
            });
        }

        try
        {
            EnsureLoaded();
            var wait = Stopwatch.StartNew();
            while (Db is null && State is LibraryState.Idle or LibraryState.Loading && wait.ElapsedMilliseconds < 8000 && !cts.IsCancellationRequested)
            {
                await Task.Delay(100);
            }

            // up to 5 s (1.5 s when the person asked for it) for the agent to end, or for Apple Music to be running again, or for a press of Play
            var agentMs = announce ? 5000 : 1500;
            var agentGone = await Task.Run(() =>
            {
                var watch = Stopwatch.StartNew();
                while (!cts.IsCancellationRequested && !expedite.Task.IsCompleted && watch.ElapsedMilliseconds < agentMs)
                {
                    if (CountProcesses("AMPLibraryAgent") == 0)
                    {
                        return true;
                    }

                    if (AppleMusicKeeper.IsRunning())
                    {
                        break;
                    }

                    Thread.Sleep(100);
                }

                return CountProcesses("AMPLibraryAgent") == 0;
            });
            if (cts.IsCancellationRequested || _disposed)
            {
                return;
            }

            if (!agentGone)
            {
                _core.Ctx.Log.Info("Apple Music resume: the library agent is still running; continuing");
            }

            if (_core.Playback.AppleMusicNow() is { IsPlaying: true })
            {
                // the person (or a restarted Apple Music) is playing already
                _snapshot = null;
                return;
            }

            if (Db is not { } db || AmResume.Pick(db, snapshot) is not { } pick)
            {
                _core.Ctx.Log.Warn($"Apple Music resume: '{snapshot.Title}' is not in the library");
                SetNote(new LibraryNote(LibStrings.NotFound, true));
                return;
            }

            var track = pick.Track;
            var context = pick.Context;

            _core.Ctx.Log.Info($"Apple Music resume: '{track.Title}' from {(context is null ? "Songs" : context.Name)} at {snapshot.Position:0} s");
            var position = db.PagePosition(track, context);
            await RunAsync(new AmCommand
            {
                Action = AmAction.Play,
                Track = track,
                Playlist = context,
                Page = "songs",
                PageIndex = position.Index,
                PageCount = position.Count,
                DbId = db.DbId,
                PlaylistAutomationId = context is null ? "" : db.PlaylistAutomationId(context.Pid),
                ResumeAt = snapshot.Position,
                SeekDuration = snapshot.Duration > 0 ? snapshot.Duration : track.DurationMs / 1000.0,
            }, track.Pid);
        }
        finally
        {
            if (ReferenceEquals(_resume, cts))
            {
                Resuming = false;
                _resume = null;
                _expedite = null;
                if (!_disposed)
                {
                    Changed?.Invoke();
                }
            }
        }
    }

    // ---------------------------------------------------------------- up next

    /// <summary>When the queue was last read, and for which song.</summary>
    public DateTime QueueReadAt { get; private set; } = DateTime.MinValue;

    public string QueueIdentity { get; private set; } = "";

    /// <summary>Reads the queue only when none was read yet, or when the song has changed and the last read is older than 10 s.</summary>
    public void RefreshQueueIfStale()
    {
        if (Queue is null || (_core.Playback.Current.Identity != QueueIdentity && (DateTime.UtcNow - QueueReadAt).TotalSeconds > 10))
        {
            RefreshQueue();
        }
    }

    public async void RefreshQueue()
    {
        if (_disposed || QueueLoading)
        {
            return;
        }

        QueueLoading = true;
        var identity = _core.Playback.Current.Identity;
        Changed?.Invoke();
        try
        {
            var result = await Driver.RunAsync(new AmCommand { Action = AmAction.ReadQueue });
            QueueReadAt = DateTime.UtcNow;
            QueueIdentity = identity;
            if (!_disposed && result.Ok)
            {
                // Apple Music's queue list exposes a run of identical rows for the songs that follow the first of a context (a UI
                // Automation quirk of its virtualised list): a run is shown once.
                Queue = [.. result.Queue.Where((q, i) => i == 0 || q.Title != result.Queue[i - 1].Title || q.Subtitle != result.Queue[i - 1].Subtitle)];
            }
            else if (!_disposed && result.Failure != AmFailure.Cancelled)
            {
                Queue = [];
            }
        }
        catch
        {
            Queue = [];
        }

        QueueLoading = false;
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try
        {
            // the position kept in memory since the last write
            StoreLastPlayed();
        }
        catch
        {
            // the settings store is going away with the feature
        }

        _resume?.Cancel();
        _noteTimer.Stop();
        _watchTimer.Stop();
        _core.Playback.AppleMusicSeek = null;
        _core.Playback.Changed -= OnPlaybackChanged;
        Keeper.ClosedWhilePlaying -= OnClosedWhilePlaying;
        if (_driver.IsValueCreated)
        {
            _driver.Value.Dispose();
        }

        // Apple Music goes back to the taskbar (minimised, still playing)
        Keeper.Dispose();
    }
}
