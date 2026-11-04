// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Net.Http;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace Trayloom.Features.Island;

/// <summary>Where Spotify says playback is, as read from the Web API (used only to fill in a timeline the media session lacks).</summary>
/// <param name="Title">The song title.</param>
/// <param name="Artist">The artists, joined with ", ".</param>
/// <param name="Position">Seconds into the song at <paramref name="StampUtc"/>.</param>
/// <param name="Duration">Length in seconds; 0 when unknown.</param>
/// <param name="Playing">False while paused.</param>
/// <param name="StampUtc">When <paramref name="Position"/> was true (the middle of the request, to halve the network delay).</param>
internal sealed record ExternalTimeline(string Title, string Artist, double Position, double Duration, bool Playing, DateTime StampUtc);

/// <summary>What the Web API adds to a media session reading that has no timeline of its own.</summary>
/// <param name="Duration">Length in seconds (greater than 0).</param>
/// <param name="BasePosition">Seconds into the song at <paramref name="BaseStamp"/>.</param>
/// <param name="BaseStamp">When <paramref name="BasePosition"/> was true.</param>
/// <param name="CanSeek">The island may offer a draggable timeline (seeking goes through the Web API).</param>
internal readonly record struct TimelineFill(double Duration, double BasePosition, DateTime BaseStamp, bool CanSeek);

/// <summary>
/// The pure decisions of using the Web API's position where the media session has none (kept apart from the network and WPF so the harness can
/// check them). The Web API never replaces a real session timeline and never decides which song is playing: it only fills in a song the session
/// already reports.
/// </summary>
internal static class TimelineMerge
{
    /// <summary>The session reports no length (0 or less) and the Web API is playing the same song, with a length.</summary>
    public static bool Applies(double sessionDuration, string? title, string? artist, ExternalTimeline? external) =>
        sessionDuration <= 0 && external is { Duration: > 0 } && SpotifyMap.SameSong(title, artist, external.Title, external.Artist);

    /// <summary>The fill for a session reading, or null when the session's own timeline stands.</summary>
    public static TimelineFill? Decide(double sessionDuration, string? title, string? artist, ExternalTimeline? external, bool canSeek) =>
        Applies(sessionDuration, title, artist, external)
            ? new TimelineFill(external!.Duration, Math.Clamp(external.Position, 0, external.Duration), external.StampUtc, canSeek)
            : null;

    /// <summary>Where the song is at <paramref name="nowUtc"/>: the reading's position plus the time since it was taken, while playing.</summary>
    public static double PositionAt(ExternalTimeline timeline, DateTime nowUtc)
    {
        var elapsed = timeline.Playing ? Math.Max(0, (nowUtc - timeline.StampUtc).TotalSeconds) : 0;
        return Math.Clamp(timeline.Position + elapsed, 0, timeline.Duration > 0 ? timeline.Duration : double.MaxValue);
    }
}

/// <summary>Playback commands of the Spotify service: play, queue, shuffle, the timeline poll and seeking.</summary>
internal sealed partial class SpotifyService
{
    private static readonly TimeSpan TimelinePoll = TimeSpan.FromSeconds(5);

    /// <summary>A reading that fails to arrive is kept this long (extrapolated) before the timeline is dropped, so one dropped request does not flicker the display.</summary>
    private static readonly TimeSpan TimelineGrace = TimeSpan.FromSeconds(20);

    private CancellationTokenSource? _timelineCts;

    /// <summary>Seek requests not answered yet; no reading counts while there are any (it would show the old position).</summary>
    private int _seeksInFlight;

    /// <summary>Readings requested before this moment are ignored: they may predate the last seek.</summary>
    private DateTime _ignorePollsBefore = DateTime.MinValue;

    /// <summary>The playback position while <see cref="WatchTimeline"/> is on and something plays on this account; otherwise null.</summary>
    public ExternalTimeline? Timeline { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Timeline"/> changed in a way the display must follow (a new song, pause or resume, a jump).</summary>
    public event Action? TimelineChanged;

    // ------------------------------------------------------------------ play, queue, shuffle

    /// <inheritdoc />
    public void Play(StreamItem item, StreamItem? context, IReadOnlyList<StreamItem>? siblings = null) =>
        Guarded.FireAndLog(() => PlayAsync(item, context, siblings, shuffle: false), _log, "Spotify play");

    /// <summary>Plays an album, playlist or artist with shuffle on.</summary>
    public void Shuffle(StreamItem container) =>
        Guarded.FireAndLog(() => PlayAsync(container, null, null, shuffle: true), _log, "Spotify shuffle");

    /// <inheritdoc />
    public void Queue(StreamItem item) =>
        Guarded.FireAndLog(() => QueueAsync(item), _log, "Spotify queue");

    private async Task PlayAsync(StreamItem item, StreamItem? context, IReadOnlyList<StreamItem>? siblings, bool shuffle)
    {
        if (_busyKey is not null || _tokens is null)
        {
            return;
        }

        var body = SpotifyPlayBody.For(item, context, siblings);
        if (body is null || CanPlayHere == false)
        {
            // Local files cannot be started through the API; a free account cannot play from here: Spotify's own app can.
            OpenExternally(item);
            if (CanPlayHere == false)
            {
                SetNote(StreamStrings.Message(StreamStatus.PremiumRequired, StreamKind.Spotify), error: false);
            }

            return;
        }

        _busyKey = item.Key;
        ClearNote();
        RaiseChanged();
        try
        {
            var ct = _disposeCts.Token;
            if (await StartPlaybackAsync(item, body, ct) && shuffle)
            {
                await SendWriteAsync(HttpMethod.Put, "me/player/shuffle", "me/player/shuffle?state=true", null, ct);
            }
        }
        finally
        {
            _busyKey = null;
            RaiseChanged();
        }
    }

    /// <summary>Starts playback, falling back to another device or to launching the app when none is active. True when it started.</summary>
    private async Task<bool> StartPlaybackAsync(StreamItem item, string body, CancellationToken ct)
    {
        var reply = await SendWriteAsync(HttpMethod.Put, "me/player/play", "me/player/play", body, ct);
        if (reply.Ok)
        {
            SetPremium(true);
            InvalidateQueue();
            await NoteActiveDeviceAsync(ct);
            return true;
        }

        switch (reply.Status)
        {
            case StreamStatus.NoDevice:
                return await PlayOnAnotherDeviceAsync(item, body, ct);
            case StreamStatus.PremiumRequired:
                OnPremiumRequired(item);
                return false;
            default:
                SetNote(StreamStrings.Message(reply.Status, StreamKind.Spotify, reply.RetryIn), error: true);
                return false;
        }
    }

    private async Task<bool> PlayOnAnotherDeviceAsync(StreamItem item, string body, CancellationToken ct)
    {
        var (reply, devices) = await DevicesAsync(ct);
        if (!reply.Ok)
        {
            SetNote(StreamStrings.Message(reply.Status, StreamKind.Spotify, reply.RetryIn), error: true);
            return false;
        }

        var installed = await IsAppInstalledAsync();
        var choice = SpotifyDevicePlan.Choose(devices, Environment.MachineName, installed);
        switch (choice.Action)
        {
            case DeviceAction.UseDevice:
                return await PlayOnDeviceAsync(item, body, choice, ct);

            case DeviceAction.LaunchApp:
                SetNote(StreamStrings.StartingSpotify, error: false, milliseconds: 0);
                MusicLinks.Open("spotify:", _log);
                for (var i = 0; i < 10; i++)
                {
                    await Task.Delay(1500, ct);
                    var (poll, found) = await DevicesAsync(ct);
                    if (!poll.Ok)
                    {
                        continue;
                    }

                    var next = SpotifyDevicePlan.Choose(found, Environment.MachineName, appInstalled: true);
                    if (next.Action == DeviceAction.UseDevice)
                    {
                        return await PlayOnDeviceAsync(item, body, next, ct);
                    }
                }

                SetNote(StreamStrings.Message(StreamStatus.NoDevice, StreamKind.Spotify), error: true);
                return false;

            default:
                // Neither a device nor the app: the web player is the way left.
                MusicLinks.Open("https://open.spotify.com", _log);
                SetNote(StreamStrings.Message(StreamStatus.AppMissing, StreamKind.Spotify), error: true);
                return false;
        }
    }

    private async Task<bool> PlayOnDeviceAsync(StreamItem item, string body, DeviceChoice choice, CancellationToken ct)
    {
        var path = "me/player/play?device_id=" + Uri.EscapeDataString(choice.DeviceId ?? "");
        var reply = await SendWriteAsync(HttpMethod.Put, "me/player/play", path, body, ct);
        if (reply.Ok)
        {
            SetPremium(true);
            InvalidateQueue();
            SetNote(StreamStrings.PlayingOn(choice.DeviceName ?? "Spotify"), error: false);
            return true;
        }

        if (reply.Status == StreamStatus.PremiumRequired)
        {
            OnPremiumRequired(item);
        }
        else
        {
            SetNote(StreamStrings.Message(reply.Status, StreamKind.Spotify, reply.RetryIn), error: true);
        }

        return false;
    }

    private async Task NoteActiveDeviceAsync(CancellationToken ct)
    {
        var (reply, devices) = await DevicesAsync(ct, quiet: true);
        if (reply.Ok && devices.FirstOrDefault(d => d.IsActive) is { Name.Length: > 0 } active)
        {
            SetNote(StreamStrings.PlayingOn(active.Name), error: false);
        }
    }

    private async Task<(Reply Reply, IReadOnlyList<SpotifyDevice> Devices)> DevicesAsync(CancellationToken ct, bool quiet = false)
    {
        var reply = await GetAsync("me/player/devices", "me/player/devices", ct, quiet);
        if (!reply.Ok)
        {
            return (reply, Array.Empty<SpotifyDevice>());
        }

        try
        {
            using var doc = JsonDocument.Parse(reply.Body);
            return (reply, SpotifyMap.Devices(doc.RootElement));
        }
        catch (JsonException)
        {
            return (reply with { Status = StreamStatus.Failed }, Array.Empty<SpotifyDevice>());
        }
    }

    /// <summary>Spotify refused to play because the account is not Premium: remember it, open the song in Spotify instead and say so.</summary>
    private void OnPremiumRequired(StreamItem item)
    {
        SetPremium(false);
        OpenExternally(item);
        SetNote(StreamStrings.Message(StreamStatus.PremiumRequired, StreamKind.Spotify), error: false);
    }

    private async Task QueueAsync(StreamItem item)
    {
        if (_busyKey is not null || _tokens is null || item.Kind != StreamItemKind.Track || !item.Playable
            || !SpotifyUri.TryParse(item.Uri, out _, out _))
        {
            return;
        }

        if (CanPlayHere == false)
        {
            OnPremiumRequired(item);
            return;
        }

        _busyKey = item.Key;
        ClearNote();
        RaiseChanged();
        try
        {
            // Never retried: a repeated request would queue the song twice.
            var reply = await SendWriteAsync(HttpMethod.Post, "me/player/queue", "me/player/queue?uri=" + Uri.EscapeDataString(item.Uri), null, _disposeCts.Token);
            switch (reply.Status)
            {
                case StreamStatus.Ok:
                    SetPremium(true);
                    InvalidateQueue();
                    SetNote(StreamStrings.AddedToQueue, error: false);
                    break;
                case StreamStatus.NoDevice:
                    SetNote(StreamStrings.StartSomethingFirst, error: true);
                    break;
                case StreamStatus.PremiumRequired:
                    OnPremiumRequired(item);
                    break;
                default:
                    SetNote(StreamStrings.Message(reply.Status, StreamKind.Spotify, reply.RetryIn), error: true);
                    break;
            }
        }
        finally
        {
            _busyKey = null;
            RaiseChanged();
        }
    }

    // ------------------------------------------------------------------ opening in Spotify

    /// <inheritdoc />
    public void OpenExternally(StreamItem item)
    {
        var type = SpotifyUri.TypeOf(item.Kind);
        var uri = SpotifyUri.Build(type, item.Id);
        var link = SpotifyUri.LinkFor(type, item.Id);
        if (uri.Length == 0)
        {
            return;
        }

        Guarded.FireAndLog(
            async () => MusicLinks.Open(await IsAppInstalledAsync() ? uri : link, _log),
            _log,
            "Spotify open");
    }

    /// <inheritdoc />
    public void OpenSearchExternally(string query)
    {
        var text = query.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (text.Length > 200)
        {
            text = text[..200];
        }

        var escaped = Uri.EscapeDataString(text);
        Guarded.FireAndLog(
            async () => MusicLinks.Open(await IsAppInstalledAsync() ? "spotify:search:" + escaped : "https://open.spotify.com/search/" + escaped, _log),
            _log,
            "Spotify search");
    }

    /// <summary>True when the Spotify desktop app answers <c>spotify:</c> links (registry), else when a Start-menu app named Spotify exists (listed off the UI thread).</summary>
    private static async Task<bool> IsAppInstalledAsync()
    {
        if (ProtocolRegistered())
        {
            return true;
        }

        var players = await PlaybackService.InstalledPlayersAsync();
        return players.Any(p => p.Name.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ProtocolRegistered()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey("spotify");
            return key?.GetValue("URL Protocol") is not null;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ up next

    /// <summary>The "Up next" list (<c>GET me/player/queue</c>), cached for 30 s; <c>Retry(QueueRequest)</c> refreshes it.</summary>
    public StreamLoad GetQueue() => Get(QueueRequest);

    /// <summary>Forgets the cached "Up next" list (and a failed attempt at it) so the next view asks again; raises nothing.</summary>
    public void InvalidateQueue()
    {
        _queueCache.Remove(QueueRequest);
        _failed.Remove(QueueRequest);
    }

    // ------------------------------------------------------------------ timeline

    /// <summary>Starts or stops polling what is playing (every 5 s). Off, <see cref="Timeline"/> is null and nothing is requested.</summary>
    public void WatchTimeline(bool on)
    {
        if (!on)
        {
            StopTimeline();
            return;
        }

        if (_timelineCts is not null || _disposed)
        {
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        _timelineCts = cts;
        Guarded.FireAndLog(() => TimelineLoopAsync(cts), _log, "Spotify timeline");
    }

    private void StopTimeline()
    {
        var cts = _timelineCts;
        _timelineCts = null;
        cts?.Cancel();
        SetTimeline(null);
    }

    private async Task TimelineLoopAsync(CancellationTokenSource cts)
    {
        var ct = cts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (Connection == StreamConnection.Connected && !IsBlocked(out _))
                {
                    await PollTimelineAsync(ct);
                }
                else
                {
                    SetTimeline(null);
                }

                await Task.Delay(TimelinePoll, ct);
            }
        }
        finally
        {
            // Whatever ended the loop, the next WatchTimeline(true) must be able to start a new one.
            if (ReferenceEquals(_timelineCts, cts))
            {
                _timelineCts = null;
            }

            cts.Dispose();
        }
    }

    private async Task PollTimelineAsync(CancellationToken ct)
    {
        var before = DateTime.UtcNow;
        var reply = await GetAsync("me/player/currently-playing", "me/player/currently-playing?additional_types=track", ct, quiet: true);
        var after = DateTime.UtcNow;
        if (ct.IsCancellationRequested || _seeksInFlight > 0 || before < _ignorePollsBefore)
        {
            return;
        }

        if (!reply.Ok)
        {
            // A request that failed says nothing about the song: the last reading stays (and moves on its own) for a while.
            if (Timeline is { } last && DateTime.UtcNow - last.StampUtc > TimelineGrace)
            {
                SetTimeline(null);
            }

            return;
        }

        // 204 (nothing playing) comes back as success with no body.
        SetTimeline(ReadTimeline(reply.Body, before + (after - before) / 2));
    }

    /// <summary>Reads a <c>currently-playing</c> answer; null when nothing plays, an episode or an unreadable answer.</summary>
    internal static ExternalTimeline? ReadTimeline(string? json, DateTime stampUtc)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object
                || (Str(item, "type") is { } type && type != "track"))
            {
                return null;
            }

            var artists = new List<string>();
            if (item.TryGetProperty("artists", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var artist in list.EnumerateArray())
                {
                    if (Str(artist, "name") is { Length: > 0 } name)
                    {
                        artists.Add(name);
                    }
                }
            }

            var position = root.TryGetProperty("progress_ms", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() / 1000.0 : 0;
            var duration = item.TryGetProperty("duration_ms", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() / 1000.0 : 0;
            var playing = root.TryGetProperty("is_playing", out var ip) && ip.ValueKind == JsonValueKind.True;
            return new ExternalTimeline(Str(item, "name") ?? "", string.Join(", ", artists), position, duration, playing, stampUtc);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SetTimeline(ExternalTimeline? next)
    {
        var old = Timeline;
        Timeline = next;
        if (_disposed || (old is null && next is null))
        {
            return;
        }

        // A fresh reading that matches where the old one predicts we are is not news: the display already moves on its own.
        var moved = old is null || next is null
            || old.Title != next.Title || old.Artist != next.Artist || old.Playing != next.Playing
            || Math.Abs(old.Duration - next.Duration) > 0.5
            || Math.Abs(TimelineMerge.PositionAt(old, next.StampUtc) - next.Position) > 1.5;
        if (moved)
        {
            RaiseTimelineChanged();
        }
    }

    private void RaiseTimelineChanged()
    {
        if (_core.Ui.CheckAccess())
        {
            TimelineChanged?.Invoke();
        }
        else
        {
            _core.Ui.BeginInvoke(() =>
            {
                if (!_disposed)
                {
                    TimelineChanged?.Invoke();
                }
            });
        }
    }

    /// <summary>
    /// Jumps to <paramref name="seconds"/> (<c>PUT me/player/seek</c>), moving <see cref="Timeline"/> at once so the display does not
    /// wait for the answer. False when there is no timeline to seek in, no sign-in, or the account is known to be free.
    /// </summary>
    public bool TrySeek(double seconds)
    {
        var timeline = Timeline;
        if (timeline is null || _tokens is null || CanPlayHere == false || IsBlocked(out _))
        {
            return false;
        }

        var target = Math.Max(0, timeline.Duration > 0 ? Math.Min(seconds, timeline.Duration) : seconds);
        Timeline = timeline with { Position = target, StampUtc = DateTime.UtcNow };
        RaiseTimelineChanged();
        var milliseconds = (long)Math.Round(target * 1000);

        // A reading asked for before Spotify has moved would put the thumb back: none counts until the seek is answered.
        _seeksInFlight++;
        Guarded.FireAndLog(
            async () =>
            {
                try
                {
                    var reply = await SendWriteAsync(HttpMethod.Put, "me/player/seek", $"me/player/seek?position_ms={milliseconds}", null, _disposeCts.Token);
                    if (reply.Status == StreamStatus.PremiumRequired)
                    {
                        SetPremium(false);
                    }
                }
                finally
                {
                    _seeksInFlight--;
                    _ignorePollsBefore = DateTime.UtcNow;
                }
            },
            _log,
            "Spotify seek");
        return true;
    }
}
