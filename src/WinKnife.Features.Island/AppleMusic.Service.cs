// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Island;

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
    /// <summary>0 = Playlists, 1 = Songs, 2 = Recently added.</summary>
    public int Tab { get; set; } = 1;

    public ulong? PlaylistPid { get; set; }

    public string Search { get; set; } = "";

    public Dictionary<string, double> Scroll { get; } = [];
}

/// <summary>A short line under the panel header: a failure (orange) or a confirmation.</summary>
internal sealed record LibraryNote(string Text, bool Error);

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
    private bool _disposed;
    private DateTime _availableAt = DateTime.MinValue;
    private bool _available;
    private int _loadId;
    private AmCommand? _lastCommand;
    private ClosedSnapshot? _snapshot;
    private CancellationTokenSource? _resume;

    public AppleMusicService(IslandCore core)
    {
        _core = core;
        Keeper = new AppleMusicKeeper(core, () => _lastCommand);
        Keeper.ClosedWhilePlaying += OnClosedWhilePlaying;
        core.Playback.AppleMusicSeek = Seek;
        _driver = new Lazy<AppleMusicDriver>(() => new AppleMusicDriver(line => _core.Ctx.Log.Info("AppleMusic " + line), Keeper));
        _noteTimer = new DispatcherTimer(DispatcherPriority.Normal, core.Ui);
        _noteTimer.Tick += (_, _) =>
        {
            _noteTimer.Stop();
            Note = null;
            Changed?.Invoke();
        };
    }

    public LibraryUi Ui { get; } = new();

    public AmDb? Db { get; private set; }

    public LibraryState State { get; private set; } = LibraryState.Idle;

    public LibraryNote? Note { get; private set; }

    /// <summary>The song or playlist (by Pid) a request is running for.</summary>
    public ulong? BusyPid { get; private set; }

    /// <summary>The running request is "Shuffle" of a playlist.</summary>
    public bool BusyShuffle { get; private set; }

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

        if (!string.Equals(p.Title.Trim(), t.Title.Trim(), StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        var detail = p.Artist + " " + p.Album;
        return (t.Album.Length == 0 || detail.Contains(t.Album, StringComparison.OrdinalIgnoreCase)) || (t.Artist.Length > 0 && detail.Contains(t.Artist, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- library file

    /// <summary>Loads the library (again when the file changed); raises <see cref="Changed"/> when done.</summary>
    public void EnsureLoaded(bool force = false)
    {
        if (_disposed || State == LibraryState.Loading)
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
                }

                Changed?.Invoke();
            });
        });
    }

    // ---------------------------------------------------------------- requests

    public void PlaySong(AmTrack track, AmPlaylist? context, string page, AmAction action = AmAction.Play)
    {
        if (Db is not { } db)
        {
            return;
        }

        _ = RunAsync(new AmCommand
        {
            Action = action,
            Track = track,
            Playlist = context,
            Page = page,
            DbId = db.DbId,
            PlaylistAutomationId = context is null ? "" : db.PlaylistAutomationId(context.Pid),
        }, track.Pid);
    }

    public void PlayPlaylist(AmPlaylist playlist, bool shuffle)
    {
        if (Db is not { } db)
        {
            return;
        }

        _ = RunAsync(new AmCommand
        {
            Action = shuffle ? AmAction.ShufflePlaylist : AmAction.PlayPlaylist,
            Playlist = playlist,
            DbId = db.DbId,
            PlaylistAutomationId = db.PlaylistAutomationId(playlist.Pid),
        }, playlist.Pid);
    }

    /// <summary>
    /// Play/pause, next or previous for Apple Music when Windows has no media session for it (the app is closed or idle):
    /// it is started hidden if needed and its own button is pressed, so its window never shows.
    /// </summary>
    public void Transport(AmAction action)
    {
        if (!Available)
        {
            return;
        }

        if (action == AmAction.PlayPause && _snapshot is { } closed && DateTime.UtcNow - closed.At < TimeSpan.FromMinutes(30) && !Resuming
            && !AppleMusicKeeper.IsRunning() && _core.Playback.AppleMusicNow() is not { IsPlaying: true })
        {
            // Apple Music was closed while it played: "play" continues that song instead of failing for lack of anything loaded
            _ = ResumeAfterQuit(closed, false);
            return;
        }

        _ = RunAsync(new AmCommand { Action = action }, 0);
    }

    /// <summary>Sets Apple Music's position (its media session ignores the request).</summary>
    public bool Seek(double seconds)
    {
        if (!Available || _disposed)
        {
            return false;
        }

        _ = Task.Run(async () =>
        {
            var result = await Driver.RunAsync(new AmCommand { Action = AmAction.Seek, SeekTo = seconds });
            if (!result.Ok && result.Failure != AmFailure.Cancelled)
            {
                _core.Ctx.Log.Warn($"Apple Music seek failed: {result.Failure} {result.Detail}");
            }
        });
        return true;
    }

    private async Task RunAsync(AmCommand command, ulong pid)
    {
        if (command.Action is AmAction.Play or AmAction.PlayPlaylist or AmAction.ShufflePlaylist)
        {
            _lastCommand = command;
        }

        var seq = ++_seq;
        BusyPid = pid;
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

        if (_disposed || seq != _seq)
        {
            return;
        }

        BusyPid = null;
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
        else if (result.Failure != AmFailure.Cancelled)
        {
            _core.Ctx.Log.Warn($"Apple Music {command.Action} failed: {result.Failure} {result.Detail}");
            SetNote(new LibraryNote(Message(command.Action, result.Failure), true));
        }

        Changed?.Invoke();
    }

    private static string Message(AmAction action, AmFailure failure) => failure switch
    {
        AmFailure.Unavailable => LibStrings.NotStarted,
        AmFailure.NotFound => LibStrings.NotFound,
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
        if (Keeper.ResumeEnabled)
        {
            _ = ResumeAfterQuit(snapshot, true);
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

    /// <summary>The library song for what was playing (title, then artist and album, like <see cref="IsCurrent"/>).</summary>
    private AmTrack? FindTrack(ClosedSnapshot s)
    {
        var title = s.Title.Trim();
        if (title.Length == 0)
        {
            return null;
        }

        var detail = s.Artist + " " + s.Album;
        int Score(AmTrack t)
        {
            if (!string.Equals(t.Title.Trim(), title, StringComparison.CurrentCultureIgnoreCase))
            {
                return 0;
            }

            var album = t.Album.Length == 0 || detail.Contains(t.Album, StringComparison.OrdinalIgnoreCase);
            var artist = t.Artist.Length > 0 && detail.Contains(t.Artist, StringComparison.OrdinalIgnoreCase);
            return 1 + (album ? 1 : 0) + (artist ? 1 : 0) + (s.Track is { } last && last.Pid == t.Pid ? 2 : 0);
        }

        if (Db is not { } db)
        {
            return s.Track is { } known && Score(known) > 0 ? known : null;
        }

        AmTrack? best = null;
        var bestScore = 0;
        foreach (var t in db.Songs)
        {
            var score = Score(t);
            if (score > bestScore)
            {
                best = t;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// Starts Apple Music again in the background and plays the song that was playing when it was closed, from the same position.
    /// Waits until Apple Music's library agent (which actually plays the audio) has ended: starting while it lives leaves orphan audio nobody can stop.
    /// </summary>
    private async Task ResumeAfterQuit(ClosedSnapshot snapshot, bool announce)
    {
        _resume?.Cancel();
        var cts = _resume = new CancellationTokenSource();
        Resuming = true;
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

            var gone = await Task.Run(() =>
            {
                var watch = Stopwatch.StartNew();
                while (!cts.IsCancellationRequested && watch.ElapsedMilliseconds < 25000)
                {
                    if (CountProcesses("AMPLibraryAgent") == 0)
                    {
                        return true;
                    }

                    Thread.Sleep(200);
                }

                return false;
            });
            if (cts.IsCancellationRequested || _disposed)
            {
                return;
            }

            if (!gone)
            {
                _core.Ctx.Log.Warn("Apple Music's library agent did not end; not starting it again");
                SetNote(new LibraryNote(LibStrings.NotStarted, true));
                return;
            }

            if (AppleMusicKeeper.IsRunning() || _core.Playback.AppleMusicNow() is { IsPlaying: true })
            {
                // the person started Apple Music again themselves
                _snapshot = null;
                return;
            }

            var track = FindTrack(snapshot);
            if (track is null || Db is not { } db)
            {
                _core.Ctx.Log.Warn($"Apple Music resume: '{snapshot.Title}' is not in the library");
                SetNote(new LibraryNote(LibStrings.NotFound, true));
                return;
            }

            var context = snapshot.Playlist is { } p ? db.Playlists.FirstOrDefault(x => x.Pid == p.Pid) : null;
            if (context is not null && !context.Tracks.Any(t => t.Pid == track.Pid))
            {
                context = null;
            }

            _core.Ctx.Log.Info($"Apple Music resume: '{track.Title}' from {(context is null ? "Songs" : context.Name)} at {snapshot.Position:0} s");
            await RunAsync(new AmCommand
            {
                Action = AmAction.Play,
                Track = track,
                Playlist = context,
                Page = "songs",
                DbId = db.DbId,
                PlaylistAutomationId = context is null ? "" : db.PlaylistAutomationId(context.Pid),
                ResumeAt = snapshot.Position,
            }, track.Pid);
        }
        finally
        {
            if (ReferenceEquals(_resume, cts))
            {
                Resuming = false;
                _resume = null;
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
        _resume?.Cancel();
        _noteTimer.Stop();
        _core.Playback.AppleMusicSeek = null;
        Keeper.ClosedWhilePlaying -= OnClosedWhilePlaying;
        if (_driver.IsValueCreated)
        {
            _driver.Value.Dispose();
        }

        // Apple Music goes back to the taskbar (minimised, still playing)
        Keeper.Dispose();
    }
}
