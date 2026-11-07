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
    private static readonly string[] MusicApps = ["spotify", "applemusic", "itunes", "zunemusic", "musicbee", "foobar", "aimp", "winamp", "tidal", "deezer", "amazonmusic", "youtubemusic", "mediaplayer", "groove", "vlc"];

    private sealed class Entry
    {
        public required GlobalSystemMediaTransportControlsSession Session { get; init; }

        public string Aumid { get; init; } = "";

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

    // ---------------------------------------------------------------- commands

    public void TogglePlayPause() => Command(s => s.TryTogglePlayPauseAsync());

    public void Next() => Command(s => s.TrySkipNextAsync());

    public void Previous() => Command(s => s.TrySkipPreviousAsync());

    public void Seek(double seconds)
    {
        if (CurrentEntry() is { } e)
        {
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
            _ = e;
            _ui.BeginInvoke(async () =>
            {
                await Task.Delay(1500);
                RefreshEntry(e.Session);
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
                RefreshEntry(e.Session);
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

    private async void Command(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> run)
    {
        var entry = CurrentEntry();
        if (entry is null)
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
            var ok = await run(entry.Session);
            _commandFailed = !ok;
            guard?.Restore();
        }
        catch (Exception ex)
        {
            _commandFailed = true;
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
                src = new PlayerSource { Aumid = id, Name = PrettyName(id), IsMusicApp = IsMusic(id), Order = _order++ };
                _sources[id] = src;
                Guarded.FireAndLog(() => LoadIconAsync(src), _log, "Player icon");
            }

            s.MediaPropertiesChanged += (_, _) => _ui.BeginInvoke(() => RefreshEntry(s));
            s.PlaybackInfoChanged += (_, _) => _ui.BeginInvoke(() => RefreshEntry(s));

            // Players report their timeline about once a second while they play. That only moves the position, so it re-reads the
            // state without asking for the song's properties (one more cross-process round trip each time), and a run of reports
            // that arrives before the UI thread gets to it is read once.
            var timelineQueued = 0;
            s.TimelinePropertiesChanged += (_, _) =>
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
            RefreshEntry(s);
        }

        foreach (var gone in _entries.Keys.Where(k => !seen.Contains(k)).ToList())
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

    private static void Unhook(Entry e) => e.Paused = true;

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
            if (key != e.ArtKey)
            {
                e.ArtKey = key;
                e.Art = null;
                e.Tint = null;
                e.SeekAt = null;
            }

            var wasPlaying = e.Playing;
            ReadState(e);
            if (wasPlaying && !e.Playing && IsAppleMusic(e))
            {
                _appleStoppedAt = DateTime.UtcNow;
            }

            Recompute();
            if (e.Art is null && props.Thumbnail is not null)
            {
                var art = await LoadArtworkAsync(props.Thumbnail);
                if (rev == e.Rev && art is not null)
                {
                    e.Art = art;
                    e.Tint = ArtworkTint(art);
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
        try
        {
            var info = e.Session.GetPlaybackInfo();
            var tl = e.Session.GetTimelineProperties();
            e.Playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
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
        _commandFailed = _commandFailed && false;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- names, icons, tint

    private static bool IsMusic(string aumid)
    {
        var id = aumid.ToLowerInvariant();
        return MusicApps.Any(id.Contains);
    }

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

    public static string? ResolvePreferredPlayer(string chosen, PlaybackService? playback)
    {
        if (!string.IsNullOrEmpty(chosen))
        {
            return chosen;
        }

        if (playback?._lastMusic is { } last)
        {
            return last;
        }

        return InstalledPlayers().FirstOrDefault(p => p.Name.Contains("Spotify", StringComparison.OrdinalIgnoreCase)).Aumid
            ?? InstalledPlayers().FirstOrDefault().Aumid;
    }

    private static List<(string Name, string Aumid)>? _installed;

    /// <summary>Installed music apps (Start menu apps matching the known list).</summary>
    public static List<(string Name, string Aumid)> InstalledPlayers()
    {
        if (_installed is not null)
        {
            return _installed;
        }

        var list = new List<(string, string)>();
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
                var text = p.StandardOutput.ReadToEnd();
                p.WaitForExit(4000);
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Trim().Split('|', 2);
                    if (parts.Length == 2 && MusicApps.Take(MusicApps.Length - 1).Any(m => parts[1].ToLowerInvariant().Contains(m) || parts[0].ToLowerInvariant().Replace(" ", "").Contains(m)))
                    {
                        list.Add((parts[0], parts[1]));
                    }
                }
            }
        }
        catch
        {
        }

        _installed = list;
        return list;
    }

    public static async Task<List<(string Name, string Aumid)>> InstalledPlayersAsync() => await Task.Run(InstalledPlayers);

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
