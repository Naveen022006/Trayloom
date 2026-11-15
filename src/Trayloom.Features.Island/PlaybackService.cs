// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>One audio/video app that publishes system media controls.</summary>
internal sealed class PlayerSource
{
    public required string Aumid { get; init; }

    public string Name { get; set; } = "";

    public ImageSource? Icon { get; set; }

    public bool IsMusicApp { get; set; }

    public int Order { get; set; }
}

/// <summary>A player Trayloom hosts itself (the YouTube Music window): commanded directly, not through the system media controls.</summary>
internal interface IVirtualPlayer
{
    void Play();

    void Pause();

    void Next();

    void Previous();

    void Seek(double seconds);
}

/// <summary>A media session's repeat mode.</summary>
internal enum RepeatKind
{
    None,
    List,
    Track,
}

/// <summary>The repeat button's cycle and its mapping to the media session's modes (pure: tested by the harness).</summary>
internal static class RepeatCycle
{
    /// <summary>The mode a press switches to: none, then list, then track, then none again.</summary>
    public static RepeatKind Next(RepeatKind mode) => mode switch
    {
        RepeatKind.None => RepeatKind.List,
        RepeatKind.List => RepeatKind.Track,
        _ => RepeatKind.None,
    };

    public static RepeatKind? FromWinRt(Windows.Media.MediaPlaybackAutoRepeatMode? mode) => mode switch
    {
        Windows.Media.MediaPlaybackAutoRepeatMode.None => RepeatKind.None,
        Windows.Media.MediaPlaybackAutoRepeatMode.List => RepeatKind.List,
        Windows.Media.MediaPlaybackAutoRepeatMode.Track => RepeatKind.Track,
        _ => null,
    };

    public static Windows.Media.MediaPlaybackAutoRepeatMode ToWinRt(RepeatKind mode) => mode switch
    {
        RepeatKind.List => Windows.Media.MediaPlaybackAutoRepeatMode.List,
        RepeatKind.Track => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
        _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
    };
}

/// <summary>What is playing now (immutable snapshot).</summary>
internal sealed class Playback
{
    public static readonly Playback None = new();

    public bool HasTrack { get; init; }

    public string Title { get; init; } = "";

    public string Artist { get; init; } = "";

    public string Album { get; init; } = "";

    public bool IsPlaying { get; init; }

    public BitmapSource? Artwork { get; init; }

    public Color? Tint { get; init; }

    public string Aumid { get; init; } = "";

    public double Duration { get; init; }

    /// <summary>The length the media session itself reports (0 when it has none); <see cref="Duration"/> differs only when the Web API filled it in.</summary>
    public double SessionDuration { get; init; }

    /// <summary>Duration, position and seeking come from the Web API because the media session has no timeline.</summary>
    public bool TimelineFromWebApi { get; init; }

    public bool CanSeek { get; init; }

    public bool CanPrevious { get; init; }

    public bool CanNext { get; init; }

    public bool CanPlayPause { get; init; }

    public bool? Shuffle { get; init; }

    public bool CanShuffle { get; init; }

    /// <summary>The repeat mode the session reports (null when it has none).</summary>
    public RepeatKind? Repeat { get; init; }

    public bool CanRepeat { get; init; }

    public bool HasPosition { get; init; }

    public double BasePosition { get; init; }

    public DateTime BaseStamp { get; init; }

    public double Rate { get; init; } = 1;

    public string Identity => $"{Aumid}|{Title}|{Artist}|{Album}|{(int)Duration}";

    /// <summary>The song as the media session knows it: <see cref="Identity"/> without a length the Web API added later, so that does not count as a new song.</summary>
    public string SongKey => $"{Aumid}|{Title}|{Artist}|{Album}|{(int)SessionDuration}";

    public double Position
    {
        get
        {
            var p = BasePosition + (IsPlaying ? (DateTime.UtcNow - BaseStamp).TotalSeconds * Rate : 0);
            return Math.Clamp(p, 0, Duration > 0 ? Duration : double.MaxValue);
        }
    }

    public string Display => string.IsNullOrWhiteSpace(Title) ? Strings.MediaNowPlaying : Title;

    public string Subtitle => !string.IsNullOrWhiteSpace(Artist) ? Artist : (!string.IsNullOrWhiteSpace(Album) ? Album : Strings.MediaNowPlaying);
}

/// <summary>
/// System media controls over every session (source list, automatic selection, 1.5 s gap grace, shuffle, tint).
/// UI-thread object: WinRT callbacks are marshalled.
/// </summary>
internal sealed class PlaybackService : IDisposable
{
    /// <summary>The apps the island follows and offers (Windows Media Player and the like are never launched or listed).</summary>
    private static readonly string[] MusicApps = ["spotify", "applemusic", "youtubemusic"];

    private sealed class Entry
    {
        /// <summary>The system media session; null for Trayloom's own YouTube Music player (<see cref="Virtual"/>).</summary>
        public GlobalSystemMediaTransportControlsSession? Session { get; init; }

        /// <summary>Set for Trayloom's own YouTube Music player: it has no system session and is commanded directly.</summary>
        public IVirtualPlayer? Virtual { get; set; }

        /// <summary>The handlers added to <see cref="Session"/>, so they can be detached again.</summary>
        public Action? Unsubscribe { get; set; }

        public string Aumid { get; init; } = "";

        // a press of play / pause the island shows at once; the session's own state replaces it when it agrees or after 1.5 s
        public bool? WantPlaying;
        public DateTime WantAt;
        public double WantPos;

        // the cover of the previous song stays until the new one is loaded (or 600 ms), so the cover does not flash empty
        public bool ArtStale;
        public string ArtUrl = "";

        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public BitmapSource? Art;
        public Color? Tint;
        public string ArtKey = "";
        public bool Playing;
        public bool HasProps;
        public double Duration;
        public double BasePos;
        public DateTime Stamp = DateTime.UtcNow;
        public double Rate = 1;
        public bool CanSeek, CanPrev, CanNext, CanPlay, CanShuffle;

        // a position the island set (Apple Music's scrubber), used until the session delivers a timeline updated after it
        public double SeekPos;
        public DateTime? SeekAt;
        public bool? Shuffle;
        public bool CanRepeat;
        public RepeatKind? Repeat;
        public long StartTicks;
        public bool Paused;
        public int Rev;
    }

    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly Dictionary<string, PlayerSource> _sources = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string? _chosen;
    private string? _lastOther;
    private string? _lastMusic;
    private string? _effective;
    private Playback _current = Playback.None;
    private Playback _held = Playback.None;
    private DispatcherTimer? _grace;
    private bool _disposed;
    private bool _awaiting;
    private bool _commandFailed;
    private string _lastIdentityForEvent = "";
    private int _order;
    private Func<bool> _includeOthers = () => true;

    public PlaybackService(Dispatcher ui, ILog log)
    {
        _ui = ui;
        _log = log;
    }

    /// <summary>The app id Trayloom's own YouTube Music player has as a source (it belongs to <see cref="MusicSource.YouTube"/>).</summary>
    public const string YouTubePlayerAumid = "Trayloom.YouTubeMusic";

    /// <summary>When the person last paused from the island (a strip or a button); null until then. The closed strip stays a few seconds after it.</summary>
    public DateTime? PausedByIslandAt { get; private set; }

    /// <summary>Raised when the person paused from the island, so the closed island can take the strip away after its grace time.</summary>
    public event Action? PausedByIsland;

    private DateTime _lastToggle = DateTime.MinValue;

    /// <summary>Asks Apple Music's own transport to play or pause when its media session ignored a press (set by the Apple Music service).</summary>
    public Action? AppleMusicToggle { get; set; }

    public Playback Current => _current;

    public IReadOnlyList<PlayerSource> Sources => [.. _sources.Values.Where(s => _entries.ContainsKey(s.Aumid)).OrderBy(s => s.Order)];

    public string? ChosenSource => _chosen;

    public string? EffectiveSource => _effective;

    public bool AwaitingPlayback => _awaiting;

    public bool CommandFailed => _commandFailed;

    public event Action? Changed;

    /// <summary>Raised before the new song is published (only when the new reading is playing and differs from the last one of that player).</summary>
    public event Action<Playback>? TrackChanged;

    public Func<bool> IncludeOthers
    {
        get => _includeOthers;
        set => _includeOthers = value;
    }

    public async void Start()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed)
            {
                return;
            }

            _manager = manager;
            manager.SessionsChanged += (_, _) => _ui.BeginInvoke(SyncSessions);
            manager.CurrentSessionChanged += (_, _) => _ui.BeginInvoke(Recompute);
            SyncSessions();
        }
        catch (Exception ex)
        {
            _log.Warn($"Media session manager unavailable: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _grace?.Stop();
        foreach (var e in _entries.Values)
        {
            Unhook(e);
        }

        _entries.Clear();
    }

    public void Choose(string? aumid)
    {
        if (_chosen == aumid)
        {
            return;
        }

        _chosen = aumid;
        _awaiting = true;
        _current = _held = Playback.None;
        Recompute();
        _ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _awaiting = false;
            Changed?.Invoke();
        });
    }

    public void Reevaluate() => Recompute();

    /// <summary>Tells the pages something outside the sessions changed (the list of installed apps).</summary>
    public void NotifyChanged() => Changed?.Invoke();

    // ---------------------------------------------------------------- commands

    /// <summary>
    /// Plays or pauses the current player. The island shows the new state at once (the wanted state stands until the session agrees, or
    /// 1.5 s) and a second press within 200 ms is ignored, so a double click does not toggle twice before the player has answered.
    /// </summary>
    public void TogglePlayPause()
    {
        var entry = CurrentEntry();
        if (entry is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastToggle).TotalMilliseconds < 200)
        {
            return;
        }

        _lastToggle = now;
        var target = !entry.Playing;

        // the position stops or restarts from where it is now
        entry.BasePos = PositionNow(entry, now);
        entry.Stamp = now;
        entry.WantPos = entry.BasePos;
        entry.WantPlaying = target;
        entry.WantAt = now;
        entry.Playing = target;
        if (!target)
        {
            PausedByIslandAt = now;
            PausedByIsland?.Invoke();
        }

        Recompute();
        Command(s => s.TryTogglePlayPauseAsync(), v =>
        {
            if (target)
            {
                v.Play();
            }
            else
            {
                v.Pause();
            }
        });
        Guarded.FireAndLog(
            async () =>
            {
                await Task.Delay((int)OptimisticPlay.TimeoutMs + 50);
                CheckWanted(entry);
            },
            _log,
            "Play state check");
    }

    public void Next() => Command(s => s.TrySkipNextAsync(), v => v.Next());

    public void Previous() => Command(s => s.TrySkipPreviousAsync(), v => v.Previous());

    private static double PositionNow(Entry e, DateTime now)
    {
        var p = e.BasePos + (e.Playing ? Math.Max(0, (now - e.Stamp).TotalSeconds) * e.Rate : 0);
        return Math.Clamp(p, 0, e.Duration > 0 ? e.Duration : double.MaxValue);
    }

    /// <summary>
    /// The wanted play state did not arrive within the time allowed: the shown state goes back to the player's, the failure line shows,
    /// and Apple Music is asked through its own transport once.
    /// </summary>
    private void CheckWanted(Entry e)
    {
        if (_disposed || e.WantPlaying is null)
        {
            return;
        }

        if (e.Virtual is null)
        {
            ReadState(e);
        }

        if (e.WantPlaying is null)
        {
            Recompute();
            return;
        }

        // ReadState keeps the wanted state only inside the time allowed, so by now it has reverted (or the session agreed)
        var wanted = e.WantPlaying;
        e.WantPlaying = null;
        if (e.Virtual is not null)
        {
            // the hosted player reports itself; its next reading is the truth
            Recompute();
            return;
        }

        if (wanted is { } w && e.Playing != w)
        {
            _commandFailed = true;
            _log.Warn("[island] media: play/pause was not taken in time, reverted");
            if (IsAppleMusic(e) && AppleMusicToggle is { } toggle)
            {
                toggle();
            }
        }

        Recompute();
    }

    public void Seek(double seconds)
    {
        if (CurrentEntry() is { } e)
        {
            if (e.Virtual is { } hosted)
            {
                e.BasePos = Math.Clamp(seconds, 0, e.Duration > 0 ? e.Duration : double.MaxValue);
                e.Stamp = DateTime.UtcNow;
                hosted.Seek(seconds);
                Recompute();
                return;
            }

            if (IsAppleMusic(e) && AppleMusicSeek is { } appleSeek)
            {
                // Apple Music accepts the media session's seek request and ignores it: its own scrubber is set instead
                appleSeek(seconds, e.Duration);
                return;
            }

            // A session without a timeline of its own (Spotify): the Web API moves the song and its reading follows at once, so the
            // timeline's hold (SeekRules) is all the display needs. The session has no position to set, so a refused request ends here too.
            if (_current.TimelineFromWebApi && _current.Aumid == e.Aumid)
            {
                _ = ExternalSeek?.Invoke(e.Aumid, seconds);
                return;
            }

            Command(s => s.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(seconds).Ticks + e.StartTicks));
        }
    }

    public void ToggleShuffle()
    {
        if (CurrentEntry() is { Shuffle: { } on } e)
        {
            Command(s => s.TryChangeShuffleActiveAsync(!on));
            _ui.BeginInvoke(async () =>
            {
                await Task.Delay(1500);
                if (e.Session is { } session)
                {
                    RefreshEntry(session);
                }
            });
        }
    }

    /// <summary>Cycles the session's repeat mode: off, then repeat the list, then repeat the song, then off.</summary>
    public void ToggleRepeat()
    {
        if (CurrentEntry() is { Repeat: { } mode } e)
        {
            var next = RepeatCycle.Next(mode);
            Command(s => s.TryChangeAutoRepeatModeAsync(RepeatCycle.ToWinRt(next)));
            _ui.BeginInvoke(async () =>
            {
                // players answer late: read the mode again after a moment
                await Task.Delay(1500);
                if (e.Session is { } session)
                {
                    RefreshEntry(session);
                }
            });
        }
    }

    private static bool IsAppleMusic(Entry e) => IsAppleMusicId(e.Aumid);

    /// <summary>The application id belongs to Apple Music for Windows.</summary>
    internal static bool IsAppleMusicId(string aumid) => aumid.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase);

    private DateTime _appleStoppedAt = DateTime.MinValue;

    /// <summary>Seeks Apple Music through its own scrubber (set by the Apple Music service).</summary>
    public Func<double, double, bool>? AppleMusicSeek { get; set; }

    /// <summary>
    /// The position of the song a player's media session reports without a timeline, read from elsewhere (the player's AUMID in, null out for none).
    /// Used only while the session's length is 0 or less and the song matches; a timeline the session reports itself is never overridden.
    /// </summary>
    public Func<string, ExternalTimeline?>? TimelineFor { get; set; }

    /// <summary>Seeks such a song (the player's AUMID, seconds in); false when it could not be asked. Set only while seeking is on offer: it also decides <see cref="Playback.CanSeek"/>.</summary>
    public Func<string, double, bool>? ExternalSeek { get; set; }

    /// <summary>Raised when an Apple Music seek failed for real (not when a newer one replaced it): the timeline drops the position it holds.</summary>
    public event Action? SeekFailed;

    /// <summary>
    /// Apple Music moved to <paramref name="seconds"/> (UI thread). Its session may not push a new timeline right away, so the island's
    /// reading stands until the session reports one updated after now. Ignored when the song has changed since (<paramref name="identity"/>).
    /// </summary>
    public void NoteSeek(double seconds, string identity)
    {
        if (CurrentEntry() is not { } e || !IsAppleMusic(e) || _current.Identity != identity)
        {
            return;
        }

        e.SeekPos = Math.Clamp(seconds, 0, e.Duration > 0 ? e.Duration : double.MaxValue);
        e.SeekAt = DateTime.UtcNow;
        e.BasePos = e.SeekPos;
        e.Stamp = e.SeekAt.Value;
        Recompute();
    }

    /// <summary>
    /// Trayloom's own YouTube Music player reports what it plays (UI thread), or null when it has closed. It appears as a player of its own
    /// (<see cref="YouTubePlayerAumid"/>, source YouTube Music) next to the system sessions, with the same pages and strip.
    /// </summary>
    public void SetHostedPlayer(IVirtualPlayer? player, HostedReading? reading)
    {
        if (_disposed)
        {
            return;
        }

        var id = YouTubePlayerAumid;
        if (player is null || reading is null)
        {
            if (_entries.Remove(id))
            {
                _log.Info("[island] YouTube Music player: no longer playing");
                Recompute();
            }

            return;
        }

        if (!_entries.TryGetValue(id, out var e))
        {
            e = new Entry { Aumid = id, HasProps = true, CanPlay = true };
            _entries[id] = e;
            if (!_sources.ContainsKey(id))
            {
                _sources[id] = new PlayerSource { Aumid = id, Name = MusicSources.Brand(MusicSource.YouTube), IsMusicApp = true, Order = _order++ };
            }
        }

        e.Virtual = player;
        var now = DateTime.UtcNow;
        var key = $"{reading.Title}|{reading.Artist}";
        if (key != e.ArtKey || reading.ArtworkUrl != e.ArtUrl)
        {
            e.ArtKey = key;
            e.ArtUrl = reading.ArtworkUrl;
            e.ArtStale = e.Art is not null;
            if (reading.ArtworkUrl.Length == 0)
            {
                e.Art = null;
                e.Tint = null;
                e.ArtStale = false;
            }
            else
            {
                var url = reading.ArtworkUrl;
                StreamArt.Load(url, image =>
                {
                    if (!_disposed && e.ArtUrl == url && image is BitmapSource bitmap)
                    {
                        e.Art = bitmap;
                        e.Tint = ArtworkTint(bitmap);
                        e.ArtStale = false;
                        Recompute();
                    }
                });
            }
        }

        e.Title = reading.Title;
        e.Artist = reading.Artist;
        e.Album = "";
        e.Duration = Math.Clamp(reading.Duration, 0, 604800);
        e.CanSeek = e.Duration > 0;
        e.CanPrev = reading.CanPrevious;
        e.CanNext = reading.CanNext;
        e.Rate = 1;
        e.BasePos = Math.Clamp(reading.Position, 0, e.Duration > 0 ? e.Duration : double.MaxValue);
        e.Stamp = now;
        var playing = !reading.Paused;
        if (e.WantPlaying is { } want && playing == want)
        {
            e.WantPlaying = null;
        }

        e.Playing = OptimisticPlay.Shown(playing, e.WantPlaying, e.WantAt, now);
        if (e.WantPlaying is not null && e.Playing != playing)
        {
            e.BasePos = e.WantPos;
            e.Stamp = e.WantAt;
        }

        Recompute();
    }

    /// <summary>An Apple Music seek failed (UI thread): nothing is overridden and the timeline stops holding the released position.</summary>
    public void NoteSeekFailed() => SeekFailed?.Invoke();

    /// <summary>Apple Music's own media session whether or not it is the selected source (null without one).</summary>
    public Playback? AppleMusicNow()
    {
        var e = _entries.Values.FirstOrDefault(IsAppleMusic);
        if (e is null || !e.HasProps)
        {
            return null;
        }

        return new Playback
        {
            HasTrack = !string.IsNullOrEmpty(e.Title) || !string.IsNullOrEmpty(e.Artist) || e.Playing,
            Title = e.Title,
            Artist = e.Artist,
            Album = e.Album,
            IsPlaying = e.Playing,
            Aumid = e.Aumid,
            Duration = e.Duration,
            SessionDuration = e.Duration,
            HasPosition = e.Duration > 0,
            BasePosition = e.BasePos,
            BaseStamp = e.Stamp,
            Rate = e.Rate,
        };
    }

    /// <summary>Apple Music is playing or stopped playing less than <paramref name="span"/> ago.</summary>
    public bool AppleMusicPlayedWithin(TimeSpan span) =>
        _entries.Values.Any(e => IsAppleMusic(e) && e.Playing) || DateTime.UtcNow - _appleStoppedAt < span;

    private Entry? CurrentEntry() => _effective is not null && _entries.TryGetValue(_effective, out var e) ? e : null;

    private async void Command(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> run, Action<IVirtualPlayer>? hosted = null)
    {
        var entry = CurrentEntry();
        if (entry is null)
        {
            return;
        }

        if (entry.Virtual is { } virtualPlayer)
        {
            // Trayloom's own player: no system session, the window runs the script
            try
            {
                hosted?.Invoke(virtualPlayer);
            }
            catch (Exception ex)
            {
                _commandFailed = true;
                _log.Warn($"[island] YouTube Music command failed: {ex.Message}");
            }

            return;
        }

        if (entry.Session is not { } session)
        {
            return;
        }

        // Apple Music tends to bring its window to the front when it gets a media command: the foreground stays where it is
        // (the window in front now, or the top application window when that is the island itself) until 1.5 s after the command.
        ForegroundGuard? guard = null;
        if (entry.Aumid.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // 3 s at most: the command and the 1.5 s grace after it, whatever happens to the awaiting code
                guard = ForegroundGuard.Arm(AmNative.GetForegroundWindow(), line => _log.Info("AppleMusic guard: " + line), 3000);
            }
            catch (Exception ex)
            {
                _log.Warn($"Foreground guard failed: {ex.Message}");
            }
        }

        try
        {
            var ok = await run(session);
            _commandFailed = !ok;
            if (!ok && entry.WantPlaying is not null)
            {
                // the player refused the press: show its state again at once
                entry.WantPlaying = null;
                ReadState(entry);
                Recompute();
            }

            guard?.Restore();
        }
        catch (Exception ex)
        {
            _commandFailed = true;
            entry.WantPlaying = null;
            _log.Warn($"Media command failed: {ex.Message}");
        }
        finally
        {
            guard?.Dispose();
        }

        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- sessions

    private void SyncSessions()
    {
        if (_manager is null || _disposed)
        {
            return;
        }

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try
        {
            sessions = _manager.GetSessions();
        }
        catch
        {
            return;
        }

        var seen = new HashSet<string>();
        foreach (var s in sessions)
        {
            var id = s.SourceAppUserModelId ?? "";
            if (id.Length == 0)
            {
                continue;
            }

            var source = MusicSources.FromPlayer(id, YtmAppIds);
            if (IsOwnPlayer(id))
            {
                // the media session of Trayloom's own web view: the hosted YouTube Music player reports itself
                LogSessionOnce(id, "ignored (Trayloom's own player)");
                continue;
            }

            if (source is null && OnlyThreeApps())
            {
                LogSessionOnce(id, "ignored");
                continue;
            }

            LogSessionOnce(id, source is { } known ? MusicSources.Brand(known) : "other");
            seen.Add(id);
            if (_entries.ContainsKey(id))
            {
                continue;
            }

            if (_entries.Count >= 16)
            {
                break;
            }

            var entry = new Entry { Session = s, Aumid = id };
            _entries[id] = entry;
            if (!_sources.TryGetValue(id, out var src))
            {
                src = new PlayerSource { Aumid = id, Name = source is { } brand ? MusicSources.Brand(brand) : PrettyName(id), IsMusicApp = IsMusic(id), Order = _order++ };
                _sources[id] = src;
                Guarded.FireAndLog(() => LoadIconAsync(src), _log, "Player icon");
            }

            Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> onProperties = (_, _) => _ui.BeginInvoke(() => RefreshEntry(s));
            Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> onInfo = (_, _) => _ui.BeginInvoke(() => RefreshEntry(s));
            s.MediaPropertiesChanged += onProperties;
            s.PlaybackInfoChanged += onInfo;

            // Players report their timeline about once a second while they play. That only moves the position, so it re-reads the
            // state without asking for the song's properties (one more cross-process round trip each time), and a run of reports
            // that arrives before the UI thread gets to it is read once.
            var timelineQueued = 0;
            Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> onTimeline = (_, _) =>
            {
                if (Interlocked.Exchange(ref timelineQueued, 1) == 0)
                {
                    _ui.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        Volatile.Write(ref timelineQueued, 0);
                        RefreshTimeline(s);
                    });
                }
            };
            s.TimelinePropertiesChanged += onTimeline;
            entry.Unsubscribe = () =>
            {
                try
                {
                    s.MediaPropertiesChanged -= onProperties;
                    s.PlaybackInfoChanged -= onInfo;
                    s.TimelinePropertiesChanged -= onTimeline;
                }
                catch
                {
                    // the session is gone already
                }
            };
            RefreshEntry(s);
        }

        foreach (var gone in _entries.Keys.Where(k => !seen.Contains(k) && _entries[k].Virtual is null).ToList())
        {
            if (_entries[gone] is { Playing: true } playing && IsAppleMusic(playing))
            {
                _appleStoppedAt = DateTime.UtcNow;
            }

            Unhook(_entries[gone]);
            _entries.Remove(gone);
        }

        Recompute();
    }

    /// <summary>Only Apple Music, Spotify and YouTube Music are followed (the "Only Apple Music, Spotify and YouTube Music" setting).</summary>
    private bool OnlyThreeApps() => _onlyThree();

    private Func<bool> _onlyThree = () => true;

    /// <summary>Whether sessions of other players are ignored (set by the island core from its settings).</summary>
    public Func<bool> OnlyThree
    {
        get => _onlyThree;
        set => _onlyThree = value;
    }

    /// <summary>Reads the session list again (the setting changed, or the list of installed apps arrived).</summary>
    public void Resync() => SyncSessions();

    private readonly HashSet<string> _loggedSessions = [];

    private void LogSessionOnce(string aumid, string verdict)
    {
        if (_loggedSessions.Add(aumid))
        {
            _log.Info($"[island] media session: {aumid} -> {verdict}");
        }
    }

    /// <summary>The id of a media session Trayloom's own web view publishes (the YouTube Music player window).</summary>
    private static bool IsOwnPlayer(string aumid)
    {
        var id = MusicSources.Normalize(aumid);
        return id.Contains("trayloom", StringComparison.Ordinal) || id.Contains("msedgewebview2", StringComparison.Ordinal);
    }

    private static void Unhook(Entry e)
    {
        e.Paused = true;
        e.Unsubscribe?.Invoke();
        e.Unsubscribe = null;
    }

    /// <summary>A timeline report: position, length, state and controls, read without the song's properties.</summary>
    private void RefreshTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        var id = session.SourceAppUserModelId ?? "";
        if (_disposed || !_entries.TryGetValue(id, out var e) || !ReferenceEquals(e.Session, session))
        {
            return;
        }

        if (!e.HasProps)
        {
            // Not read in full yet: the full read covers the timeline too.
            RefreshEntry(session);
            return;
        }

        var wasPlaying = e.Playing;
        ReadState(e);
        if (wasPlaying && !e.Playing && IsAppleMusic(e))
        {
            _appleStoppedAt = DateTime.UtcNow;
        }

        Recompute();
    }

    private async void RefreshEntry(GlobalSystemMediaTransportControlsSession session)
    {
        var id = session.SourceAppUserModelId ?? "";
        if (!_entries.TryGetValue(id, out var e) || !ReferenceEquals(e.Session, session) || _disposed)
        {
            return;
        }

        var rev = ++e.Rev;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (rev != e.Rev)
            {
                return;
            }

            e.Title = props.Title ?? "";
            e.Artist = string.IsNullOrEmpty(props.Artist) ? props.AlbumArtist ?? "" : props.Artist;
            e.Album = props.AlbumTitle ?? "";
            e.HasProps = true;
            var key = $"{e.Title}|{e.Artist}|{e.Album}";
            var songChanged = key != e.ArtKey;
            if (songChanged)
            {
                // The previous cover stays until the new one is loaded (or 600 ms): an empty cover between two songs is a flash.
                e.ArtKey = key;
                e.ArtStale = e.Art is not null;
                e.SeekAt = null;
                if (e.ArtStale)
                {
                    var stale = key;
                    Guarded.FireAndLog(
                        async () =>
                        {
                            await Task.Delay(600);
                            if (!_disposed && e.ArtStale && e.ArtKey == stale)
                            {
                                e.Art = null;
                                e.Tint = null;
                                e.ArtStale = false;
                                Recompute();
                            }
                        },
                        _log,
                        "Cover timer");
                }
            }

            var wasPlaying = e.Playing;
            ReadState(e);
            if (wasPlaying && !e.Playing && IsAppleMusic(e))
            {
                _appleStoppedAt = DateTime.UtcNow;
            }

            // a state change the player itself reported is a reading: the failure line of an earlier press goes away
            _commandFailed = false;
            Recompute();
            if (props.Thumbnail is not null && (e.Art is null || e.ArtStale))
            {
                var art = await LoadArtworkAsync(props.Thumbnail);
                if (rev == e.Rev && art is not null)
                {
                    e.Art = art;
                    e.Tint = ArtworkTint(art);
                    e.ArtStale = false;
                    Recompute();
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Reading media properties failed: {ex.Message}");
        }
    }

    private static void ReadState(Entry e)
    {
        if (e.Session is not { } session)
        {
            return;
        }

        try
        {
            var info = session.GetPlaybackInfo();
            var tl = session.GetTimelineProperties();
            var sessionPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var now = DateTime.UtcNow;
            if (e.WantPlaying is { } want && sessionPlaying == want)
            {
                e.WantPlaying = null;
            }

            e.Playing = OptimisticPlay.Shown(sessionPlaying, e.WantPlaying, e.WantAt, now);
            var c = info.Controls;
            e.CanPrev = c.IsPreviousEnabled;
            e.CanNext = c.IsNextEnabled;
            e.CanPlay = c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled;
            e.CanShuffle = c.IsShuffleEnabled;
            e.Shuffle = info.IsShuffleActive;
            e.CanRepeat = c.IsRepeatEnabled;
            e.Repeat = RepeatCycle.FromWinRt(info.AutoRepeatMode);
            e.Rate = Math.Clamp(info.PlaybackRate ?? 1.0, 0, 16);
            e.StartTicks = tl.StartTime.Ticks;
            var duration = (tl.EndTime - tl.StartTime).TotalSeconds;
            e.Duration = Math.Clamp(duration > 0 ? duration : 0, 0, 604800);
            e.CanSeek = c.IsPlaybackPositionEnabled && e.Duration > 0;
            e.BasePos = Math.Max(0, (tl.Position - tl.StartTime).TotalSeconds);
            var stamp = tl.LastUpdatedTime.UtcDateTime;
            e.Stamp = stamp.Year < 2000 ? DateTime.UtcNow : stamp;
            if (e.SeekAt is { } seekAt)
            {
                if (SeekRules.OverrideHolds(seekAt, stamp.Year < 2000 ? null : stamp))
                {
                    e.BasePos = e.SeekPos;
                    e.Stamp = seekAt;
                }
                else
                {
                    e.SeekAt = null;
                }
            }

            if (e.WantPlaying is not null && e.Playing != sessionPlaying)
            {
                // the state shown is the wanted one: the position carries on from the press, not from the session's older stamp
                e.BasePos = e.WantPos;
                e.Stamp = e.WantAt;
            }
        }
        catch
        {
            // keep the previous reading
        }
    }

    // ---------------------------------------------------------------- selection

    private string? Select()
    {
        var order = _entries.Values.OrderBy(e => _sources.TryGetValue(e.Aumid, out var s) ? s.Order : 99).ToList();
        if (_chosen is not null && _entries.ContainsKey(_chosen))
        {
            return _chosen;
        }

        bool IsMusicId(Entry e) => _sources.TryGetValue(e.Aumid, out var s) && s.IsMusicApp;
        var playingMusic = order.FirstOrDefault(e => e.Playing && IsMusicId(e));
        if (playingMusic is not null)
        {
            return playingMusic.Aumid;
        }

        if (OnlyThreeApps())
        {
            // Only the three apps are listed, so there are no other players to fall back to: the music app that played last stays
            // (paused), else the first one there is.
            if (_lastMusic is not null && _entries.ContainsKey(_lastMusic))
            {
                return _lastMusic;
            }

            return order.FirstOrDefault(IsMusicId)?.Aumid;
        }

        if (_includeOthers())
        {
            var playingOther = order.FirstOrDefault(e => e.Playing);
            if (playingOther is not null)
            {
                return playingOther.Aumid;
            }
        }

        if (_lastOther is not null && _entries.TryGetValue(_lastOther, out var prev) && !IsMusicId(prev))
        {
            return prev.Aumid;
        }

        var pausedMusic = order.FirstOrDefault(IsMusicId);
        return pausedMusic?.Aumid ?? (_lastMusic is not null && _entries.ContainsKey(_lastMusic) ? _lastMusic : null);
    }

    private void Recompute()
    {
        if (_disposed)
        {
            return;
        }

        var selected = Select();
        _effective = selected;
        Playback next = Playback.None;
        if (selected is not null && _entries.TryGetValue(selected, out var e) && e.HasProps)
        {
            if (_sources.TryGetValue(selected, out var src))
            {
                if (src.IsMusicApp)
                {
                    _lastMusic = selected;
                }
                else if (e.Playing)
                {
                    _lastOther = selected;
                }
            }

            if (e.Playing || !string.IsNullOrEmpty(e.Title) || !string.IsNullOrEmpty(e.Artist))
            {
                // The Web API fills in only a session that reports no length, and only for the song it reports (see TimelineMerge).
                var fill = e.Duration <= 0 && TimelineFor is { } timelineFor
                    ? TimelineMerge.Decide(e.Duration, e.Title, e.Artist, timelineFor(e.Aumid), ExternalSeek is not null)
                    : null;
                next = new Playback
                {
                    HasTrack = !string.IsNullOrEmpty(e.Title) || !string.IsNullOrEmpty(e.Artist) || e.Playing,
                    Title = e.Title,
                    Artist = e.Artist,
                    Album = e.Album,
                    IsPlaying = e.Playing,
                    Artwork = e.Art,
                    Tint = e.Tint,
                    Aumid = e.Aumid,
                    Duration = fill?.Duration ?? e.Duration,
                    SessionDuration = e.Duration,
                    TimelineFromWebApi = fill is not null,
                    CanSeek = fill?.CanSeek ?? SeekRules.CanSeek(IsAppleMusic(e), e.CanSeek, e.Duration, AppleMusicSeek is not null),
                    CanPrevious = e.CanPrev,
                    CanNext = e.CanNext,
                    CanPlayPause = e.CanPlay,
                    Shuffle = e.Shuffle,
                    CanShuffle = e.CanShuffle && e.Shuffle is not null,
                    Repeat = e.Repeat,
                    CanRepeat = e.CanRepeat && e.Repeat is not null,
                    HasPosition = (fill?.Duration ?? e.Duration) > 0,
                    BasePosition = fill?.BasePosition ?? e.BasePos,
                    BaseStamp = fill?.BaseStamp ?? e.Stamp,
                    Rate = fill is not null ? 1 : e.Rate,
                };
            }
        }

        if (!next.HasTrack)
        {
            // gap grace: the last song stays for 1.5 s, the deadline never extends
            if (_held.HasTrack && _grace is null)
            {
                _grace = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromSeconds(1.5) };
                _grace.Tick += (_, _) =>
                {
                    _grace?.Stop();
                    _grace = null;
                    _held = Playback.None;
                    _current = Playback.None;
                    Changed?.Invoke();
                };
                _grace.Start();
                return;
            }

            if (_grace is not null)
            {
                return;
            }

            if (_current.HasTrack || !ReferenceEquals(_current, Playback.None))
            {
                _current = Playback.None;
                _held = Playback.None;
                Changed?.Invoke();
            }

            return;
        }

        _grace?.Stop();
        _grace = null;
        // a length that only arrives from the Web API a moment later is not a new song, so it must not announce one again
        var identity = next.SongKey;
        if (next.IsPlaying && identity != _lastIdentityForEvent && !string.IsNullOrEmpty(next.Title))
        {
            var firstForPlayer = string.IsNullOrEmpty(_lastIdentityForEvent) || !_lastIdentityForEvent.StartsWith(next.Aumid, StringComparison.Ordinal);
            _lastIdentityForEvent = identity;
            if (!firstForPlayer)
            {
                TrackChanged?.Invoke(next);
            }
        }

        if (!next.IsPlaying && string.IsNullOrEmpty(_lastIdentityForEvent))
        {
            _lastIdentityForEvent = identity;
        }

        _held = next;
        _current = next;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- names, icons, tint

    /// <summary>The player counts as a music app: one of the three, or (when other players are followed too) one of the older well-known ones.</summary>
    private bool IsMusic(string aumid)
    {
        if (MusicSources.FromPlayer(aumid, YtmAppIds) is not null)
        {
            return true;
        }

        var id = aumid.ToLowerInvariant();
        return !OnlyThreeApps() && OtherMusicApps.Any(id.Contains);
    }

    /// <summary>Other music players that are still recognised as music while the island also follows players beyond the three (Media Player is not among them).</summary>
    private static readonly string[] OtherMusicApps = ["itunes", "musicbee", "foobar", "aimp", "winamp", "tidal", "deezer", "amazonmusic", "vlc"];

    internal static string NameOf(string appId)
    {
        var hit = _installed?.FirstOrDefault(p => p.Aumid == appId);
        return hit is { Name.Length: > 0 } ? hit.Value.Name : PrettyName(appId);
    }

    internal static string PrettyName(string appId)
    {
        var name = appId;
        var bang = name.IndexOf('!');
        if (bang > 0)
        {
            name = name[..bang];
        }

        name = name.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase);
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1)
        {
            name = name[(dot + 1)..];
        }

        var us = name.IndexOf('_');
        if (us > 0)
        {
            name = name[..us];
        }

        if (name.Contains("zunemusic", StringComparison.OrdinalIgnoreCase) || name.Contains("mediaplayer", StringComparison.OrdinalIgnoreCase))
        {
            return "Media Player";
        }

        return name.Length == 0 ? appId : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private async Task LoadIconAsync(PlayerSource src)
    {
        try
        {
            ImageSource? icon = null;
            if (src.Aumid.Contains('!'))
            {
                try
                {
                    var info = Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(src.Aumid);
                    src.Name = info.DisplayInfo.DisplayName;
                    var logo = info.DisplayInfo.GetLogo(new Windows.Foundation.Size(48, 48));
                    icon = await LoadArtworkAsync(logo, 48);
                }
                catch
                {
                    // fall through to the process icon
                }
            }

            if (icon is null)
            {
                var exe = src.Aumid.Split('!')[0].Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                var dot = exe.LastIndexOf('.');
                var proc = dot >= 0 ? exe[(dot + 1)..] : exe;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (!p.ProcessName.Equals(proc, StringComparison.OrdinalIgnoreCase) && !(proc.Length > 4 && p.ProcessName.Contains(proc[..Math.Min(proc.Length, 6)], StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var path = p.MainModule?.FileName;
                        if (path is not null)
                        {
                            icon = IconOf(path);
                            if (icon is not null)
                            {
                                try
                                {
                                    src.Name = FileVersionInfo.GetVersionInfo(path).FileDescription ?? src.Name;
                                }
                                catch
                                {
                                }

                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }

            if (icon is not null)
            {
                src.Icon = icon;
                Changed?.Invoke();
            }
        }
        catch
        {
        }
    }

    internal static ImageSource? IconOf(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
    }

    internal static async Task<BitmapSource?> LoadArtworkAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference, int decodeWidth = 256)
    {
        try
        {
            using var winrt = await reference.OpenReadAsync();
            if (winrt.Size == 0 || winrt.Size > 16 * 1024 * 1024)
            {
                return null;
            }

            using var input = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(winrt);
            var memory = new MemoryStream();
            await input.CopyToAsync(memory);
            memory.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.StreamSource = memory;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Dominant cover colour (spec 1.4): average to one pixel, reject neutral covers, stretch channels.</summary>
    internal static Color? ArtworkTint(BitmapSource art)
    {
        try
        {
            var small = new TransformedBitmap(art, new ScaleTransform(1.0 / Math.Max(1, art.PixelWidth), 1.0 / Math.Max(1, art.PixelHeight)));
            var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            var px = new byte[4];
            conv.CopyPixels(px, 4, 0);
            double r = px[2] / 255.0, g = px[1] / 255.0, b = px[0] / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (max <= 0.05 || (max - min) / max < 0.12)
            {
                return null;
            }

            double Stretch(double c) => Math.Clamp(((c - min) / (max - min) * 0.86) + 0.06, 0, 1);
            return Color.FromRgb((byte)(Stretch(r) * 255), (byte)(Stretch(g) * 255), (byte)(Stretch(b) * 255));
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- preferred player

    /// <summary>The app id of this session's last music source (what automatic idle behaviour follows); null before any.</summary>
    public string? LastMusic => _lastMusic;

    /// <summary>The idle target of the settings and the apps found right now (see <see cref="IdleTargets.Resolve"/>).</summary>
    public IdleTarget ResolveIdle(string saved, bool apple) =>
        IdleTargets.Resolve(saved, _lastMusic, apple, MusicInstalled.SpotifyAumid(), MusicInstalled.YouTubeMusicAumid());

    private static List<(string Name, string Aumid)>? _installed;
    private static Task<List<(string Name, string Aumid)>>? _installedTask;
    private static HashSet<string> s_ytmAppIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Start menu apps named "YouTube Music" (the Chrome or Edge web app); their media sessions belong to YouTube Music.</summary>
    internal static IReadOnlySet<string> YtmAppIds => s_ytmAppIds;

    /// <summary>Raised (on a pool thread) once the Start menu has been read: the list of installed apps is complete from then on.</summary>
    public static event Action? InstalledLoaded;

    /// <summary>
    /// Installed music apps (Start menu apps that are Apple Music, Spotify or YouTube Music) as read so far. Never waits: the list is empty until
    /// <see cref="InstalledPlayersAsync"/> has finished, which the island starts at launch.
    /// </summary>
    public static List<(string Name, string Aumid)> InstalledPlayers() => _installed ?? [];

    /// <summary>Reads the Start menu once (off the UI thread, bounded), keeps the result and raises <see cref="InstalledLoaded"/>; later calls get the same result.</summary>
    public static Task<List<(string Name, string Aumid)>> InstalledPlayersAsync() => _installedTask ??= Task.Run(LoadInstalledAsync);

    private static async Task<List<(string Name, string Aumid)>> LoadInstalledAsync()
    {
        var list = new List<(string Name, string Aumid)>();
        var ytm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"Get-StartApps | ForEach-Object { $_.Name + '|' + $_.AppID }\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is not null)
            {
                var read = p.StandardOutput.ReadToEndAsync();
                if (await Task.WhenAny(read, Task.Delay(8000)) != read)
                {
                    try
                    {
                        p.Kill(true);
                    }
                    catch
                    {
                        // already gone
                    }
                }
                else
                {
                    foreach (var line in (await read).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Trim().Split('|', 2);
                        if (parts.Length != 2)
                        {
                            continue;
                        }

                        var name = MusicSources.Normalize(parts[0]);
                        if (name.Contains("youtubemusic", StringComparison.Ordinal))
                        {
                            ytm.Add(parts[1]);
                        }

                        if (MusicApps.Any(m => MusicSources.Normalize(parts[1]).Contains(m, StringComparison.Ordinal) || name.Contains(m, StringComparison.Ordinal)))
                        {
                            list.Add((parts[0], parts[1]));
                        }
                    }
                }
            }
        }
        catch
        {
            // no list: the apps are found from their media sessions instead
        }

        s_ytmAppIds = ytm;
        _installed = list;
        try
        {
            InstalledLoaded?.Invoke();
        }
        catch
        {
            // a listener's failure is not the list's
        }

        return list;
    }

    /// <summary>Opens a player by AUMID or exe path without starting playback.</summary>
    public static void OpenPlayer(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid))
        {
            return;
        }

        try
        {
            if (aumid.Contains('\\') && File.Exists(aumid))
            {
                Process.Start(new ProcessStartInfo(aumid) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{aumid}") { UseShellExecute = true });
            }
        }
        catch
        {
        }
    }
}
