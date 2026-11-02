// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using WinKnife.Core;

namespace WinKnife.Features.Island;

/// <summary>Snapshot of the current media session.</summary>
internal sealed class MediaInfo
{
    public bool HasSession { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    public string Album { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public bool IsPlaying { get; init; }

    public BitmapSource? Artwork { get; init; }

    public TimeSpan Duration { get; init; }

    public bool CanPrevious { get; init; }

    public bool CanNext { get; init; }

    public bool CanPlayPause { get; init; }

    public bool CanSeek { get; init; }

    internal TimeSpan BasePosition { get; init; }

    internal DateTime BaseStamp { get; init; }

    internal double Rate { get; init; } = 1;

    /// <summary>Playback position extrapolated to now.</summary>
    public TimeSpan Position
    {
        get
        {
            var position = BasePosition;
            if (IsPlaying)
            {
                position += TimeSpan.FromSeconds((DateTime.UtcNow - BaseStamp).TotalSeconds * Rate);
            }

            if (Duration > TimeSpan.Zero && position > Duration)
            {
                position = Duration;
            }

            return position < TimeSpan.Zero ? TimeSpan.Zero : position;
        }
    }
}

/// <summary>
/// Now-playing data and transport commands from the Windows System Media Transport Controls. All events and
/// properties are used on the UI thread; WinRT callbacks are marshalled to it.
/// </summary>
internal sealed class MediaService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _key = string.Empty;
    private BitmapSource? _artwork;
    private int _generation;
    private bool _disposed;

    public MediaService(Dispatcher ui, ILog log)
    {
        _ui = ui;
        _log = log;
    }

    public MediaInfo Current { get; private set; } = new();

    /// <summary>Raised on the UI thread when anything about the current media changed.</summary>
    public event Action? Changed;

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
            manager.CurrentSessionChanged += OnManagerChanged;
            manager.SessionsChanged += OnManagerChanged;
            Rebind();
        }
        catch (Exception ex)
        {
            _log.Warn($"Media session manager unavailable: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Unbind();
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnManagerChanged;
            _manager.SessionsChanged -= OnManagerChanged;
            _manager = null;
        }
    }

    public void TogglePlayPause() => Run(s => s.TryTogglePlayPauseAsync());

    public void Next() => Run(s => s.TrySkipNextAsync());

    public void Previous() => Run(s => s.TrySkipPreviousAsync());

    public void Seek(TimeSpan position) => Run(s => s.TryChangePlaybackPositionAsync(position.Ticks + StartOffsetTicks));

    private long StartOffsetTicks { get; set; }

    private void Run(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        try
        {
            _ = command(session);
        }
        catch (Exception ex)
        {
            _log.Warn($"Media command failed: {ex.Message}");
        }
    }

    private void OnManagerChanged(object? sender, object e) => _ui.BeginInvoke(Rebind);

    private void Rebind()
    {
        if (_disposed || _manager is null)
        {
            return;
        }

        GlobalSystemMediaTransportControlsSession? next;
        try
        {
            next = _manager.GetCurrentSession();
        }
        catch
        {
            next = null;
        }

        if (next is null && _session is null)
        {
            return;
        }

        if (next is not null && _session is not null && next.SourceAppUserModelId == _session.SourceAppUserModelId)
        {
            return;
        }

        Unbind();
        _session = next;
        _key = string.Empty;
        _artwork = null;
        if (next is not null)
        {
            next.MediaPropertiesChanged += OnPropertiesChanged;
            next.PlaybackInfoChanged += OnPlaybackChanged;
            next.TimelinePropertiesChanged += OnTimelineChanged;
        }

        Refresh();
    }

    private void Unbind()
    {
        if (_session is null)
        {
            return;
        }

        _session.MediaPropertiesChanged -= OnPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackChanged;
        _session.TimelinePropertiesChanged -= OnTimelineChanged;
        _session = null;
    }

    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => _ui.BeginInvoke(Refresh);

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => _ui.BeginInvoke(Refresh);

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => _ui.BeginInvoke(Refresh);

    /// <summary>Re-reads everything; fetches artwork asynchronously only when the track changed.</summary>
    private async void Refresh()
    {
        var session = _session;
        if (session is null || _disposed)
        {
            Current = new MediaInfo();
            Changed?.Invoke();
            return;
        }

        var generation = ++_generation;
        string title = string.Empty, artist = string.Empty, album = string.Empty;
        Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail = null;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            title = props.Title ?? string.Empty;
            artist = string.IsNullOrEmpty(props.Artist) ? props.AlbumArtist ?? string.Empty : props.Artist;
            album = props.AlbumTitle ?? string.Empty;
            thumbnail = props.Thumbnail;
        }
        catch (Exception ex)
        {
            _log.Warn($"Reading media properties failed: {ex.Message}");
        }

        if (generation != _generation || !ReferenceEquals(session, _session))
        {
            return;
        }

        var key = $"{session.SourceAppUserModelId}|{title}|{artist}|{album}";
        if (key != _key)
        {
            _key = key;
            _artwork = null;
        }

        Publish(session, title, artist, album);

        if (_artwork is null && thumbnail is not null)
        {
            var image = await LoadArtworkAsync(thumbnail);
            if (generation == _generation && ReferenceEquals(session, _session) && image is not null)
            {
                _artwork = image;
                Publish(session, title, artist, album);
            }
        }
    }

    private void Publish(GlobalSystemMediaTransportControlsSession session, string title, string artist, string album)
    {
        try
        {
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var start = timeline.StartTime;
            StartOffsetTicks = start.Ticks;
            var duration = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;
            var stamp = timeline.LastUpdatedTime.UtcDateTime;
            if (stamp.Year < 2000)
            {
                stamp = DateTime.UtcNow;
            }

            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var controls = playback.Controls;
            Current = new MediaInfo
            {
                HasSession = true,
                Title = title,
                Artist = artist,
                Album = album,
                Source = PrettySource(session.SourceAppUserModelId),
                IsPlaying = playing,
                Artwork = _artwork,
                Duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero,
                CanPrevious = controls.IsPreviousEnabled,
                CanNext = controls.IsNextEnabled,
                CanPlayPause = controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
                CanSeek = controls.IsPlaybackPositionEnabled && duration > TimeSpan.Zero,
                BasePosition = position,
                BaseStamp = stamp,
                Rate = playback.PlaybackRate ?? 1.0,
            };
        }
        catch (Exception ex)
        {
            _log.Warn($"Reading playback info failed: {ex.Message}");
            return;
        }

        Changed?.Invoke();
    }

    private static string PrettySource(string appId)
    {
        if (string.IsNullOrEmpty(appId))
        {
            return string.Empty;
        }

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

        var underscore = name.IndexOf('_');
        if (underscore > 0)
        {
            name = name[..underscore];
        }

        return name.Length == 0 ? string.Empty : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static async Task<BitmapSource?> LoadArtworkAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var winrtStream = await reference.OpenReadAsync();
            if (winrtStream.Size == 0 || winrtStream.Size > 16 * 1024 * 1024)
            {
                return null;
            }

            using var input = winrtStream.AsStreamForRead();
            var memory = new MemoryStream();
            await input.CopyToAsync(memory);
            memory.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 256;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
