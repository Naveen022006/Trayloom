// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using Microsoft.Win32;
using Trayloom.Core;

namespace Trayloom.Features.GitHub;

/// <summary>The settings keys of the extension (spec 06 section 2.3).</summary>
internal static class GhKeys
{
    public const string Connected = "notchGitHubConnected";
    public const string Source = "notchGitHubSource";
    public const string Login = "notchGitHubLogin";
    public const string Interval = "notchGitHubInterval";
    public const string Repos = "notchGitHubRepos";
    public const string Notices = "notchGitHubNotices";

    public static readonly int[] Intervals = [2, 5, 15, 30];

    public static void Register(IAppSettings app)
    {
        app.Register(Connected, false);
        app.Register(Source, "");
        app.Register(Login, "");
        app.Register(Interval, 5);
        app.Register(Repos, "");
        app.Register(Notices, true);
    }
}

/// <summary>How the GitHub CLI connect check ended.</summary>
public enum CliConnectResult
{
    Ok,
    Missing,
    SignedOut,
    TimedOut,
    StartFailed,
}

/// <summary>How the token check ended.</summary>
public enum TokenConnectResult
{
    Ok,
    Rejected,
    Offline,
}

/// <summary>A notice for the closed island, queued after a poll found something new (spec 06 section 2.9).</summary>
public sealed record GitHubNotice(string Glyph, string Title, string Detail, Color Tint);

/// <summary>Fire-and-forget with a top-level catch, so an exception is logged instead of taking the process down.</summary>
internal static class GitHubGuard
{
    internal static async void FireAndLog(Func<Task> work, ILog log, string what)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            try
            {
                log.Error("[github] " + what + " failed (" + ex.GetType().Name + " " + ex.HResult + ")");
            }
            catch
            {
                // a logger that fails has nothing left to report to
            }
        }
    }
}

/// <summary>
/// State and schedule of the GitHub extension: the connection, the polling timer, the latest snapshot and the queued island notice.
/// Published through the service hub so the stateless island and settings contributions can reach it.
/// </summary>
public sealed class GitHubRuntime : IDisposable
{
    private readonly IFeatureContext _ctx;
    private readonly string _dir;
    private readonly GitHubStore _store;
    private readonly GitHubTokenVault _vault;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private Timer? _timer;
    private CancellationTokenSource _cts = new();
    private GitHubSnapshot? _snapshot;
    private string? _etag;
    private int _serverPollSeconds;
    private int _failures;
    private bool _halted;
    private bool _lowRate;
    private bool _paused;
    private bool _started;
    private int _runPolls;
    private DateTime _lastRefresh = DateTime.MinValue;
    private DateTime _lastPollEnd = DateTime.MinValue;
    private DateTime? _nextPoll;
    private long _lastDurationMs;
    private int _lastNotifStatus;
    private int _lastGraphStatus;
    private GitHubNotice? _notice;

    internal GitHubRuntime(IFeatureContext ctx)
    {
        _ctx = ctx;
        _dir = ctx.DataDirectory;
        _store = new GitHubStore(_dir);
        _vault = new GitHubTokenVault(_dir);
    }

    /// <summary>The newest island context handed to a contribution; used to ask the island to re-render.</summary>
    public IIslandContext? IslandContext { get; set; }

    public IContributionServices Services => _ctx;

    public bool Connected => _ctx.App.Get<bool>(GhKeys.Connected);

    public string Source => _ctx.App.Get<string>(GhKeys.Source);

    public string Login => _snapshot is { Login.Length: > 0 } s ? s.Login : _ctx.App.Get<string>(GhKeys.Login);

    public GitHubSnapshot? Snapshot => _snapshot;

    public bool IsPolling { get; private set; }

    /// <summary>The tile the island page shows (remembered for the run); -1 means the default.</summary>
    public int SelectedTile { get; set; } = -1;

    /// <summary>When the next poll is due, if one is scheduled.</summary>
    public DateTime? NextPollUtc => _nextPoll;

    /// <summary>Raised on the UI thread when the snapshot, the connection or the polling state changed.</summary>
    public event Action? Changed;

    internal void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (Connected && _store.Load() is { } cache)
        {
            _snapshot = cache.Snapshot;
            _etag = cache.NotificationsEtag;
            _serverPollSeconds = cache.PollInterval;
        }

        _ctx.App.Changed += OnSettingChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        if (Connected)
        {
            LogSource();
            Schedule(TimeSpan.FromSeconds(20));
        }
    }

    internal void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _ctx.App.Changed -= OnSettingChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        StopTimer();
        _cts.Cancel();
        GhCli.KillAll();
        _notice = null;
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    // ---------------------------------------------------------------- connecting

    private void LogSource()
    {
        if (Source == "cli")
        {
            GitHubGuard.FireAndLog(async () => _ctx.Log.Info("[github] source=cli gh=" + (await GhCli.VersionAsync(_cts.Token).ConfigureAwait(false) ?? "?")), _ctx.Log, "version check");
        }
        else if (Source == "token")
        {
            _ctx.Log.Info("[github] source=token");
        }
    }

    /// <summary>The installed gh version, or null when gh is missing. Safe to call off the UI thread.</summary>
    public static async Task<string?> FindCliAsync(CancellationToken ct)
    {
        if (GhCli.Find(refresh: true) is null)
        {
            return null;
        }

        return await GhCli.VersionAsync(ct).ConfigureAwait(false) ?? "";
    }

    /// <summary>Checks that gh is signed in to github.com and connects through it (spec 06 section 2.2 A).</summary>
    public async Task<CliConnectResult> ConnectCliAsync()
    {
        var ct = _cts.Token;
        if (await FindCliAsync(ct).ConfigureAwait(false) is null)
        {
            return CliConnectResult.Missing;
        }

        var status = await GhCli.RunAsync(["auth", "status", "--hostname", "github.com"], ct).ConfigureAwait(false);
        if (status.StartFailed)
        {
            return CliConnectResult.StartFailed;
        }

        if (status.TimedOut)
        {
            return CliConnectResult.TimedOut;
        }

        if (status.ExitCode != 0)
        {
            return CliConnectResult.SignedOut;
        }

        var user = await GhCli.RunAsync(["api", "--hostname", "github.com", "user", "--jq", ".login"], ct).ConfigureAwait(false);
        if (user.TimedOut)
        {
            return CliConnectResult.TimedOut;
        }

        var login = user.Stdout.Trim();
        if (user.ExitCode != 0 || login.Length == 0)
        {
            return user.SignedOut ? CliConnectResult.SignedOut : CliConnectResult.StartFailed;
        }

        _ = _ctx.Ui.BeginInvoke(() => FinishConnect("cli", login));
        return CliConnectResult.Ok;
    }

    /// <summary>Checks the token with <c>GET /user</c>, stores it encrypted and connects (spec 06 section 2.2 B and 2.8).</summary>
    public async Task<TokenConnectResult> ConnectTokenAsync(string token)
    {
        RawResponse response;
        try
        {
            response = await TokenTransport.SendAsync(token, System.Net.Http.HttpMethod.Get, "user", null, null, _cts.Token).ConfigureAwait(false);
        }
        catch (GhTransportException ex)
        {
            return ex.Error == GhError.Offline ? TokenConnectResult.Offline : TokenConnectResult.Rejected;
        }

        if (response.Status != 200)
        {
            return TokenConnectResult.Rejected;
        }

        string login;
        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            login = doc.RootElement.Str("login");
        }
        catch (JsonException)
        {
            return TokenConnectResult.Rejected;
        }

        _vault.Save(token);
        _ = _ctx.Ui.BeginInvoke(() => FinishConnect("token", login));
        return TokenConnectResult.Ok;
    }

    private void FinishConnect(string source, string login)
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _etag = null;
        _snapshot = null;
        _failures = 0;
        _halted = false;
        _runPolls = 0;
        _ctx.App.Set(GhKeys.Source, source);
        _ctx.App.Set(GhKeys.Login, login);
        _ctx.App.Set(GhKeys.Connected, true);
        LogSource();
        RaiseChanged();
        PollNow();
    }

    /// <summary>Stops polling, deletes the token and the cache and clears the connection. Nothing is sent to GitHub.</summary>
    public void Disconnect()
    {
        StopTimer();
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        GhCli.KillAll();
        _vault.Delete();
        _store.Delete();
        lock (_lock)
        {
            _snapshot = null;
        }

        _etag = null;
        _notice = null;
        _failures = 0;
        _halted = false;
        _ctx.App.Set(GhKeys.Connected, false);
        _ctx.App.Set(GhKeys.Source, "");
        _ctx.App.Set(GhKeys.Login, "");
        _ctx.Log.Info("[github] disconnected");
        RaiseChanged();
    }

    /// <summary>Deletes everything the extension keeps (uninstall).</summary>
    internal void Erase()
    {
        Disconnect();
        try
        {
            File.Delete(Path.Combine(_dir, "dump.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a locked dump file is not worth failing the uninstall
        }
    }

    // ---------------------------------------------------------------- scheduling

    private void OnSettingChanged(string key)
    {
        if (key is GhKeys.Interval or GhKeys.Repos && Connected && !_halted)
        {
            Schedule(key == GhKeys.Repos ? TimeSpan.FromSeconds(1) : NextDelay());
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            Pause();
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            Resume();
        }
    }

    private void OnPowerChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Pause();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            Resume();
        }
    }

    private void Pause()
    {
        _paused = true;
        StopTimer();
    }

    private void Resume()
    {
        _paused = false;
        if (!Connected || _halted)
        {
            return;
        }

        var age = DateTime.UtcNow - _lastPollEnd;
        Schedule(age >= IntervalSpan() ? TimeSpan.FromSeconds(3) : NextDelay());
    }

    private TimeSpan IntervalSpan()
    {
        var minutes = _ctx.App.Get<int>(GhKeys.Interval);
        return TimeSpan.FromMinutes(GhKeys.Intervals.Contains(minutes) ? minutes : 5);
    }

    private TimeSpan NextDelay()
    {
        var delay = IntervalSpan();
        if (_snapshot is { Error: GhError.RateLimited, RetryAt: { } retry })
        {
            return TimeSpan.FromSeconds(Math.Max(30, (retry - DateTime.UtcNow).TotalSeconds + 5));
        }

        if (_failures > 0)
        {
            delay = TimeSpan.FromMinutes(Math.Min(60, delay.TotalMinutes * Math.Pow(2, Math.Min(_failures, 3))));
        }

        if (_lowRate)
        {
            delay *= 2;
        }

        return _serverPollSeconds > delay.TotalSeconds ? TimeSpan.FromSeconds(_serverPollSeconds) : delay;
    }

    private void Schedule(TimeSpan delay)
    {
        if (!_started || _paused || !Connected)
        {
            return;
        }

        lock (_lock)
        {
            _timer?.Dispose();
            _nextPoll = DateTime.UtcNow + delay;
            _timer = new Timer(_ => GitHubGuard.FireAndLog(() => PollAsync(), _ctx.Log, "poll"), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void StopTimer()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _nextPoll = null;
        }
    }

    /// <summary>The island page opened: poll at once when the data is older than two minutes.</summary>
    public void OnPageOpened()
    {
        if (Connected && !_halted && !IsPolling && DateTime.UtcNow - _lastPollEnd > TimeSpan.FromMinutes(2) && (_snapshot is null || DateTime.UtcNow - _snapshot.FetchedAt > TimeSpan.FromMinutes(2)))
        {
            PollNow();
        }
    }

    /// <summary>"Refresh": polls at once, but at most once per 15 seconds. Also retries after a sign-out.</summary>
    public bool RequestRefresh()
    {
        if (!Connected || DateTime.UtcNow - _lastRefresh < TimeSpan.FromSeconds(15))
        {
            return false;
        }

        _lastRefresh = DateTime.UtcNow;
        _halted = false;
        PollNow();
        return true;
    }

    private void PollNow() => GitHubGuard.FireAndLog(() => PollAsync(), _ctx.Log, "poll");

    // ---------------------------------------------------------------- the poll

    private async Task PollAsync()
    {
        if (!_started || !Connected || _paused || !await _gate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        var ct = _cts.Token;
        try
        {
            IsPolling = true;
            RaiseChanged();
            var cli = Source == "cli";
            IGhTransport transport = cli ? new GhCliTransport() : new TokenTransport(_vault.Load);
            if (!cli && Source != "token")
            {
                return;
            }

            var prior = _snapshot;
            var repos = GitHubQuery.ParseRepos(_ctx.App.Get<string>(GhKeys.Repos)) ?? [];
            var client = new GitHubClient(transport, cli);
            var watch = Stopwatch.StartNew();
            var outcome = await client.PollAsync(_etag, repos, prior, ct).ConfigureAwait(false);
            watch.Stop();
            ct.ThrowIfCancellationRequested();
            if (!Connected)
            {
                return;
            }

            _lastDurationMs = watch.ElapsedMilliseconds;
            _lastNotifStatus = outcome.NotificationsStatus;
            _lastGraphStatus = outcome.GraphQlStatus;
            _lastPollEnd = DateTime.UtcNow;
            if (outcome.Snapshot is { } fresh)
            {
                Succeeded(outcome, fresh, prior, client);
            }
            else
            {
                Failed(outcome, prior);
            }
        }
        finally
        {
            IsPolling = false;
            _gate.Release();
            RaiseChanged();
        }

        if (!_halted)
        {
            Schedule(NextDelay());
        }
    }

    private void Succeeded(PollOutcome outcome, GitHubSnapshot fresh, GitHubSnapshot? prior, GitHubClient client)
    {
        _failures = 0;
        _halted = false;
        _etag = outcome.Etag;
        _serverPollSeconds = outcome.PollSeconds ?? _serverPollSeconds;
        _lowRate = outcome.Remaining < 100;
        lock (_lock)
        {
            _snapshot = fresh;
        }

        if (fresh.Login.Length > 0 && fresh.Login != _ctx.App.Get<string>(GhKeys.Login))
        {
            _ctx.App.Set(GhKeys.Login, fresh.Login);
        }

        _store.Save(new GitHubCache(1, fresh.Login, fresh.FetchedAt, _etag, _serverPollSeconds, fresh));
        var notifications = outcome.NotificationsStatus == 304 ? $"notifications 304 ({outcome.Cached} cached)" : $"notifications {outcome.NotificationsStatus}";
        var remaining = outcome.Remaining == int.MaxValue ? "?" : outcome.Remaining.ToString(CultureInfo.InvariantCulture);
        _ctx.Log.Info($"[github] poll: {notifications}, graphql {outcome.GraphQlStatus} cost={outcome.Cost} remaining={remaining}, {_lastDurationMs} ms");
        if (client.PartialErrors > 0)
        {
            _ctx.Log.Info($"[github] graphql partial errors: {client.PartialErrors}");
        }

        // The first poll of a run, and a poll with nothing to compare to, only fill the page: otherwise every start would announce old news.
        if (_runPolls > 0 && prior is { FetchedAt.Year: > 1 })
        {
            DetectNotice(prior, fresh);
        }

        _runPolls++;
    }

    private void Failed(PollOutcome outcome, GitHubSnapshot? prior)
    {
        var basis = prior ?? new GitHubSnapshot { Login = _ctx.App.Get<string>(GhKeys.Login) };
        var retry = outcome.Error == GhError.RateLimited ? outcome.RetryAt ?? DateTime.UtcNow.AddMinutes(15) : (DateTime?)null;
        lock (_lock)
        {
            _snapshot = basis with { Error = outcome.Error, RetryAt = retry };
        }

        switch (outcome.Error)
        {
            case GhError.SignedOut or GhError.TokenRejected:
                _halted = true;
                _ctx.Log.Info("[github] signed out");
                break;
            case GhError.RateLimited:
                _ctx.Log.Info("[github] rate limited until " + retry!.Value.ToString("HH:mm", CultureInfo.InvariantCulture) + "Z");
                break;
            case GhError.GhMissing:
                _halted = true;
                _ctx.Log.Info("[github] poll failed: GhMissing");
                break;
            default:
                _failures++;
                _ctx.Log.Info("[github] poll failed: " + outcome.Error);
                break;
        }

        _runPolls++;
    }

    // ---------------------------------------------------------------- notices

    private void DetectNotice(GitHubSnapshot prev, GitHubSnapshot cur)
    {
        if (!_ctx.App.Get<bool>(GhKeys.Notices))
        {
            return;
        }

        var failed = cur.Repos.FirstOrDefault(r => r.State == CheckState.Failing && prev.Repos.FirstOrDefault(p => p.Name == r.Name)?.State != CheckState.Failing);
        if (failed is not null)
        {
            Queue(new GitHubNotice("", GhText.NoticeChecks, failed.Name, Color.FromRgb(0xFF, 0x45, 0x3A)));
        }
        else if (cur.ReviewCount > prev.ReviewCount)
        {
            var newest = cur.Review.OrderByDescending(p => p.UpdatedAt).FirstOrDefault();
            Queue(new GitHubNotice("", GhText.NoticeReview, newest?.Title ?? "", Color.FromRgb(0xFF, 0x9F, 0x0A)));
        }
        else if (cur.NotificationsAvailable && cur.Notifications.Count > prev.Notifications.Count)
        {
            Queue(new GitHubNotice("", GhText.NoticeNew, GhText.NewNotifications(cur.Notifications.Count - prev.Notifications.Count), Colors.White));
        }
    }

    /// <summary>A transport for the connection in use, or null when there is none. The coding tracker sends its one query through it.</summary>
    internal IGhTransport? CreateTransport() => Source switch { "cli" => new GhCliTransport(), "token" => new TokenTransport(_vault.Load), _ => null };

    internal void Queue(GitHubNotice notice)
    {
        _notice = notice;
        _ctx.Ui.BeginInvoke(() => IslandContext?.Invalidate());
    }

    /// <summary>The queued notice, once.</summary>
    public GitHubNotice? TakeNotice()
    {
        var notice = _notice;
        _notice = null;
        return notice;
    }

    // ---------------------------------------------------------------- developer aid

    internal void Dump()
    {
        var s = _snapshot;
        var info = new Dictionary<string, object?>
        {
            ["connected"] = Connected,
            ["source"] = Source,
            ["ghPath"] = GhCli.Find(),
            ["ghVersion"] = GhCli.CachedVersion,
            ["lastPollUtc"] = _lastPollEnd == DateTime.MinValue ? null : _lastPollEnd,
            ["lastPollMs"] = _lastDurationMs,
            ["notificationsStatus"] = _lastNotifStatus,
            ["graphqlStatus"] = _lastGraphStatus,
            ["unread"] = s?.Notifications.Count,
            ["mine"] = s?.MineCount,
            ["review"] = s?.ReviewCount,
            ["failing"] = s?.FailingCount,
            ["repos"] = s?.Repos.Count,
            ["nextPollUtc"] = _nextPoll,
            ["error"] = (s?.Error ?? GhError.None).ToString(),
            ["polling"] = IsPolling,
        };
        try
        {
            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, "dump.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ctx.Log.Warn("[github] dump failed (" + ex.GetType().Name + ")");
        }

        _ctx.Log.Info($"[github] dump: connected={Connected} source={Source} unread={s?.Notifications.Count} mine={s?.MineCount} review={s?.ReviewCount} failing={s?.FailingCount} repos={s?.Repos.Count} error={s?.Error ?? GhError.None} notifications={_lastNotifStatus} graphql={_lastGraphStatus}");
    }

    private void RaiseChanged() => _ctx.Ui.BeginInvoke(() => Changed?.Invoke());
}
