// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Updates;

namespace Trayloom.App;

/// <summary>Where the update check stands.</summary>
internal enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Failed,
}

/// <summary>
/// Tells the user when a newer Trayloom exists. Notify only: it asks GitHub for the release list (about once a day, or when the user
/// presses "Check now") and offers a link to the release page; nothing is downloaded or run.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private const string KeyLastChecked = "updateLastCheckedUtc";
    private const string KeyLastAttempt = "updateLastAttemptUtc";
    private const string KeyBackoff = "updateBackoffUntilUtc";
    private const string KeyNotified = "updateNotifiedVersion";

    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Reevaluate = TimeSpan.FromHours(6);
    private static readonly TimeSpan AutoInterval = TimeSpan.FromHours(20);
    private static readonly TimeSpan ManualGap = TimeSpan.FromSeconds(10);

    private readonly IAppSettings _settings;
    private readonly ILog _log;
    private readonly INotifier _notifier;
    private readonly Dispatcher _ui;
    private readonly HttpMessageHandler? _handler;
    private DispatcherTimer? _first;
    private DispatcherTimer? _timer;
    private DateTime _lastManualUtc = DateTime.MinValue;
    private bool _disposed;

    public UpdateService(IAppSettings settings, ILog log, INotifier notifier, Dispatcher ui, HttpMessageHandler? handler = null)
    {
        _settings = settings;
        _log = log;
        _notifier = notifier;
        _ui = ui;
        _handler = handler;
    }

    public UpdateState State { get; private set; } = UpdateState.Idle;

    public string? LatestVersion { get; private set; }

    public string? ReleaseUrl { get; private set; }

    public string? FailureText { get; private set; }

    public DateTime? LastCheckedUtc { get; private set; }

    /// <summary>Raised on the UI thread whenever the state changed.</summary>
    public event Action? Changed;

    private static string CachePath => Path.Combine(AppPaths.Root, "update-cache.json");

    /// <summary>Starts the automatic schedule: the first look 2 minutes after start, then a 6-hourly re-evaluation.</summary>
    public void Start()
    {
        // the old settings page wrote a fake "last checked" text; it means nothing
        _settings.Reset("updateLastChecked");
        if (DateTime.TryParse(_settings.Get<string>(KeyLastChecked), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last))
        {
            LastCheckedUtc = last.ToUniversalTime();
        }

        if (InstallManager.IsDevelopmentBuild)
        {
            return;
        }

        _first = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = FirstDelay };
        _first.Tick += (_, _) =>
        {
            _first!.Stop();
            _ = RunAutomaticAsync();
        };
        _first.Start();
        _timer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = Reevaluate };
        _timer.Tick += (_, _) => _ = RunAutomaticAsync();
        _timer.Start();
    }

    private async Task RunAutomaticAsync()
    {
        try
        {
            if (_disposed || !_settings.Get<bool>(AppKeys.AutoCheckUpdates) || State == UpdateState.Checking)
            {
                return;
            }

            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return; // offline: skip, and do not count it as an attempt
            }

            var now = DateTime.UtcNow;
            if (ReadUtc(KeyBackoff) is { } backoff && backoff > now)
            {
                return;
            }

            if (ReadUtc(KeyLastAttempt) is { } attempt && now - attempt < AutoInterval)
            {
                return;
            }

            await CheckAsync(manual: false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Warn("Automatic update check failed: " + ex.Message);
        }
    }

    /// <summary>Checks now. A manual check ignores the schedule and the back-off but runs at most once every 10 seconds.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (_disposed || State == UpdateState.Checking)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (manual)
        {
            if (now - _lastManualUtc < ManualGap)
            {
                return;
            }

            _lastManualUtc = now;
        }

        SetState(UpdateState.Checking, null);
        WriteUtc(KeyLastAttempt, now);
        try
        {
            if (!ReleaseVersion.TryParse(AppInfo.Version, out var current))
            {
                Fail(manual, Loc.F("GitHub answered {0}.", "?"));
                return;
            }

            var cache = ReadCache();
            using var checker = new UpdateChecker(AppInfo.Version, _handler);
            var result = await checker.FetchAsync(cache?.ETag).ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            List<ReleaseInfo> releases;
            switch (result.Status)
            {
                case FetchStatus.Ok:
                    releases = ReleasePicker.Parse(result.Json ?? "[]", UpdateChecker.RepoUrl);
                    WriteCache(result.ETag, releases);
                    break;
                case FetchStatus.NotModified when cache is not null:
                    releases = cache.Releases;
                    break;
                case FetchStatus.RateLimited:
                    WriteUtc(KeyBackoff, result.RetryAtUtc ?? now.AddHours(1));
                    Fail(manual, Loc.T("GitHub’s rate limit was reached. Try again later."));
                    return;
                case FetchStatus.NoConnection:
                    Fail(manual, Loc.T("No connection to GitHub."));
                    return;
                default:
                    Fail(manual, Loc.F("GitHub answered {0}.", result.HttpStatus));
                    return;
            }

            _settings.Reset(KeyBackoff);
            var best = ReleasePicker.Newest(releases, current, WantsPrereleases());
            var checkedAt = DateTime.UtcNow;
            WriteUtc(KeyLastChecked, checkedAt);
            LastCheckedUtc = checkedAt;
            if (best is null)
            {
                SetState(UpdateState.UpToDate, null);
                return;
            }

            LatestVersion = best.Version.ToString();
            ReleaseUrl = best.Url;
            SetState(UpdateState.Available, null);
            if (!manual && _settings.Get<string>(KeyNotified) != LatestVersion)
            {
                _settings.Set(KeyNotified, LatestVersion);
                _notifier.Show(Loc.T("Trayloom update"), Loc.F("Update available: {0}. Open About in Settings to get it.", LatestVersion));
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Update check failed: " + ex.Message);
            Fail(manual, Loc.T("No connection to GitHub."));
        }
    }

    /// <summary>Beta updates when the user asked for them; a beta build that never saved the choice wants them too (spec 02e section 9.2).</summary>
    private bool WantsPrereleases() =>
        _settings.IsSet(AppKeys.IncludeBetaUpdates) ? _settings.Get<bool>(AppKeys.IncludeBetaUpdates) : AppInfo.IsBeta;

    private void Fail(bool manual, string reason)
    {
        if (manual)
        {
            SetState(UpdateState.Failed, reason);
        }
        else
        {
            _log.Info("Automatic update check: " + reason);
            SetState(State == UpdateState.Available ? UpdateState.Available : UpdateState.Idle, null);
        }
    }

    private void SetState(UpdateState state, string? failure)
    {
        State = state;
        FailureText = failure;
        if (_ui.CheckAccess())
        {
            Changed?.Invoke();
        }
        else
        {
            _ui.BeginInvoke(() => Changed?.Invoke());
        }
    }

    private DateTime? ReadUtc(string key)
    {
        try
        {
            return DateTime.TryParse(_settings.Get<string>(key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                ? value.ToUniversalTime()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void WriteUtc(string key, DateTime value) => _settings.Set(key, value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));

    private sealed record Cache(string? ETag, List<ReleaseInfo> Releases);

    private static Cache? ReadCache()
    {
        try
        {
            if (!File.Exists(CachePath) || new FileInfo(CachePath).Length > 256 * 1024)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(CachePath));
            var root = doc.RootElement;
            var etag = root.TryGetProperty("etag", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var list = new List<ReleaseInfo>();
            if (root.TryGetProperty("releases", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var tag = item.TryGetProperty("tag", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                    var pre = item.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
                    if (ReleasePicker.FromTag(tag, pre, UpdateChecker.RepoUrl) is { } info)
                    {
                        list.Add(info);
                    }
                }
            }

            return new Cache(etag, list);
        }
        catch
        {
            return null;
        }
    }

    private void WriteCache(string? etag, List<ReleaseInfo> releases)
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                etag,
                releases = releases.Select(r => new { tag = r.Tag, prerelease = r.Prerelease }),
            });
            AtomicFile.WriteAllText(CachePath, json);
        }
        catch (Exception ex)
        {
            _log.Warn("Could not save the update cache: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _first?.Stop();
        _timer?.Stop();
    }
}
