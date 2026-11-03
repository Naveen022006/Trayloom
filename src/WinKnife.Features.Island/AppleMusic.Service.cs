// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

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

    public AppleMusicService(IslandCore core)
    {
        _core = core;
        _driver = new Lazy<AppleMusicDriver>(() => new AppleMusicDriver(line => _core.Ctx.Log.Info("AppleMusic " + line)));
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

    private async Task RunAsync(AmCommand command, ulong pid)
    {
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

    // ---------------------------------------------------------------- up next

    public async void RefreshQueue()
    {
        if (_disposed || QueueLoading)
        {
            return;
        }

        QueueLoading = true;
        Changed?.Invoke();
        try
        {
            var result = await Driver.RunAsync(new AmCommand { Action = AmAction.ReadQueue });
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
        _noteTimer.Stop();
        if (_driver.IsValueCreated)
        {
            _driver.Value.Dispose();
        }
    }
}
