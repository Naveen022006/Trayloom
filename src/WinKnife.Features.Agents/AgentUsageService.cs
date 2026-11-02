// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Agents;

/// <summary>What the page, the strip and the settings read besides the snapshot.</summary>
public sealed record AgentServiceStatus
{
    public bool Running { get; init; }

    /// <summary>When the Claude app last saved the plan's limits; null when it never has.</summary>
    public DateTime? ClaudeAppChecked { get; init; }

    public bool ClaudeAccountChecking { get; init; }

    public ClaudeFailure? ClaudeAccountFailure { get; init; }

    public ClaudeAccountProfile ClaudeProfile { get; init; } = new();

    public ClaudeAccountProfile.Identity? ClaudeAccountIdentity { get; init; }

    /// <summary>The day of the price list in use.</summary>
    public DateTime? PricesUpdated { get; init; }
}

/// <summary>
/// Reads Claude Code, Codex and GitHub Copilot usage from local session logs, and OpenCode usage from its database, while the AI
/// section is on, along with the plan limits the Claude app saves. The files are read where they are, incrementally, and nothing is
/// copied or sent: only counters are kept, in memory and in WinKnife's private folder so the next launch reads only what the agents
/// wrote meanwhile. Claude Code checks account-wide limits through its own CLI.
/// </summary>
/// <remarks>Reading happens on a private worker thread; the UI thread only receives published snapshots.</remarks>
public sealed class AgentUsageService : IDisposable
{
    /// <summary>The history the island can show: thirteen weeks for the activity map.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(AgentUsageSnapshot.DayCount);

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PublishDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(5);

    private readonly AgentSettings _settings;
    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly AgentWorker _worker = new("WinKnife agent usage");
    private readonly string _home = AgentLogRoot.Home;

    // ---- control state (worker thread)
    private bool _running;
    private bool _paused;
    private int _session;
    private CancellationTokenSource _cancellation = new();
    private Timer? _tickTimer;
    private List<AgentProvider> _providers = [];
    private bool _pricesInFlight;
    private DateTime _pricesAttempted = DateTime.MinValue;
    private bool _pricesFailed;
    private DateTime? _pricesSaved;
    private ClaudeServer.Request? _claudeRequest;
    private DateTime _claudeAttempted = DateTime.MinValue;
    private int _claudeUnreachable;
    private bool _online = true;
    private bool _pageVisible;
    private ClaudeAccountProfile _claudeProfile = new();
    private ClaudeFailure? _claudeFailure;
    private bool _claudeChecking;

    // ---- reader state (worker thread)
    private int _readerSession = -1;
    private HashSet<AgentProvider> _enabled = [];
    private AgentUsageStore _store = new();
    private readonly Dictionary<string, AgentLogCursor> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private List<AgentLogRoot> _watchedRoots = [];
    private readonly HashSet<string> _changedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _rescanRequested;
    private bool _watchFlushScheduled;
    private Timer? _pollTimer;
    private (DateTime Date, double Awake)? _offlineSince;
    private bool _publishScheduled;
    private AgentUsageSnapshot _published = new();
    private AgentPlan? _claudePlan;
    private string? _claudeOrganization;
    private ClaudeAccountProfile.Identity? _claudeIdentity;
    private ClaudeAccountProfile _readerClaudeProfile = new();
    private DateTime? _claudeProfileModified;
    private DateTime? _claudeAppModified;
    private List<ClaudeAppUsage.Sample> _claudeAppSamples = [];
    private AgentLimits? _claudeAccountLimits;
    private AgentLimits? _claudeCachedLimits;
    private List<string> _claudeAccountRaw = [];
    private List<string> _claudeCachedRaw = [];
    private DateTime? _claudeCachedFetched;
    private List<LimitSourceInfo> _claudeSources = [];
    private AgentPriceList? _shippedPrices;
    private Dictionary<AgentProvider, AgentLimits> _previousLimits = [];
    private readonly Dictionary<string, (AgentProvider Provider, LimitWindow Window)> _warned = [];
    private DateTime? _budgetDay;
    private DateTime _lastRootCheck = DateTime.MinValue;
    private long? _savedMark;
    private DateTime _lastSave = DateTime.MinValue;
    private Queue<(string Path, AgentProvider Provider)>? _initialFiles;
    private bool _loaded;

    private volatile AgentUsageSnapshot _snapshot = new();
    private volatile AgentServiceStatus _status = new();

    public AgentUsageService(AgentSettings settings, Dispatcher ui, ILog log)
    {
        _settings = settings;
        _ui = ui;
        _log = log;
    }

    /// <summary>The latest published snapshot (not loaded until the first read finished).</summary>
    public AgentUsageSnapshot Snapshot => _snapshot;

    public AgentServiceStatus Status => _status;

    public bool IsRunning => _status.Running;

    /// <summary>Raised on the UI thread when the snapshot or the status changed.</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread for alerts that passed the person's choices (finish, limit warning, renewal, budget).</summary>
    public event Action<AgentUsageEvent>? Alert;

    /// <summary>Raised on the UI thread when the set of working agents changed (starts or ends a turn).</summary>
    public event Action? LiveChanged;

    // ------------------------------------------------------------------------------------------ control

    /// <summary>Called whenever any agent setting changes: starts, restarts or stops the service.</summary>
    public void SyncWithPreferences() => _worker.Post(SyncCore);

    private void SyncCore()
    {
        if (!_settings.IsEnabled())
        {
            StopCore(false);
            return;
        }

        var wanted = _settings.Providers();
        var profile = ClaudeAccountProfile.Selected(_settings.App);
        var showingPage = _pageVisible;
        // An agent turned off is no longer read at all, and one turned on is read from its start: both take a fresh reading.
        if (_running && (!wanted.SequenceEqual(_providers) || !profile.Equals(_claudeProfile)))
        {
            StopCore(false, false);
        }

        _claudeProfile = profile;
        _pageVisible = showingPage;
        if (!_running)
        {
            _running = true;
            _session++;
            _cancellation = new CancellationTokenSource();
            _providers = wanted;
            StartReading(_session, wanted.ToHashSet(), _cancellation, profile);
        }
        else if (_paused)
        {
            ResumeCore();
        }

        PublishStatus();
        SyncPrices();
        RefreshClaudeLimits(DateTime.UtcNow, false);
    }

    /// <summary>Stops reading while the island is away (display asleep or PC locked) and keeps what was read.</summary>
    public void Pause() => _worker.Post(() =>
    {
        _pageVisible = false;
        if (!_running || _paused)
        {
            return;
        }

        _paused = true;
        CancelClaudeLimits();
        _tickTimer?.Dispose();
        _tickTimer = null;
        _pollTimer?.Dispose();
        _pollTimer = null;
        StopWatchers();
        SaveProgress();
    });

    /// <summary>Reads what the agents wrote meanwhile, from where each log stopped.</summary>
    public void Resume() => _worker.Post(() =>
    {
        if (_running && _paused)
        {
            ResumeCore();
        }
    });

    private void ResumeCore()
    {
        _paused = false;
        StartTimer();
        if (_readerSession < 0)
        {
            return;
        }

        Watch(AgentLogRoot.All(_readerClaudeProfile).Where(r => _enabled.Contains(r.Provider)).ToList());
        FilesChanged([], true);
        StartPolling();
        SchedulePublish();
    }

    /// <summary>Stops the service; waits (briefly) until progress is saved so quitting never loses it.</summary>
    public void Stop(bool keepProgress = true)
    {
        _cancellation.Cancel();
        _worker.Run(() => StopCore(keepProgress && _settings.IsEnabled()), TimeSpan.FromSeconds(8));
    }

    private void StopCore(bool keepProgress, bool settleArchive = true)
    {
        _pageVisible = false;
        if (_running)
        {
            _cancellation.Cancel();
        }

        if (settleArchive)
        {
            if (keepProgress)
            {
                SaveProgress();
            }
            else
            {
                AgentUsageArchive.Remove();
            }
        }

        if (!_running)
        {
            return;
        }

        _running = false;
        CancelClaudeLimits();
        _claudeAttempted = DateTime.MinValue;
        _claudeFailure = null;
        _claudeUnreachable = 0;
        _online = true;
        _paused = false;
        _session++;
        _tickTimer?.Dispose();
        _tickTimer = null;
        _providers = [];
        _pricesInFlight = false;
        _pricesSaved = null;
        _readerSession = -1;
        _pollTimer?.Dispose();
        _pollTimer = null;
        StopWatchers();
        _watchedRoots = [];
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _offlineSince = null;
        _store = new AgentUsageStore();
        _cursors.Clear();
        _published = new AgentUsageSnapshot();
        _previousLimits.Clear();
        _warned.Clear();
        _budgetDay = null;
        _claudePlan = null;
        _claudeOrganization = null;
        _claudeIdentity = null;
        _claudeProfileModified = null;
        _claudeAppModified = null;
        _claudeAppSamples = [];
        _claudeAccountLimits = null;
        _claudeCachedLimits = null;
        _claudeAccountRaw = [];
        _claudeCachedRaw = [];
        _claudeCachedFetched = null;
        _claudeSources = [];
        _savedMark = null;
        _lastSave = DateTime.MinValue;
        _initialFiles = null;
        _loaded = false;
        _snapshot = new AgentUsageSnapshot();
        PublishStatus();
        RaiseChanged(true);
    }

    /// <summary>Limits an agent read from the account on request, newer than its logs until it writes again.</summary>
    public void NoteLimits(AgentLimits reading) => _worker.Post(() =>
    {
        if (_readerSession < 0 || !_enabled.Contains(reading.Provider))
        {
            return;
        }

        _store.UpdateLimits(reading);
        CheckLimits();
        SchedulePublish();
    });

    /// <summary>Reads local fallbacks immediately and asks Claude for fresh account limits. A Settings preview reads too, without counting as the page.</summary>
    public void PageDidAppear(bool preview = false) => _worker.Post(() =>
    {
        if (!preview)
        {
            _pageVisible = true;
        }

        if (!_running)
        {
            return;
        }

        RefreshClaudeLimits(DateTime.UtcNow, true);
        if (_readerSession < 0)
        {
            return;
        }

        ReadClaudePlan();
        ReadClaudeApp(DateTime.UtcNow);
        CheckLimits();
        if (_loaded)
        {
            Publish();
        }
    });

    public void PageDidDisappear() => _worker.Post(() => _pageVisible = false);

    /// <summary>A deliberate retry after choosing or signing in to an account.</summary>
    public void CheckClaudeAccount() => _worker.Post(() =>
    {
        if (!_running || _paused || _claudeRequest is not null)
        {
            return;
        }

        _claudeAttempted = DateTime.MinValue;
        RefreshClaudeLimits(DateTime.UtcNow, true);
    });

    // ------------------------------------------------------------------------------------------ reading

    private void StartTimer()
    {
        _tickTimer?.Dispose();
        _tickTimer = new Timer(_ => _worker.Post(TickCore), null, Tick, Tick);
    }

    private void StartReading(int session, HashSet<AgentProvider> providers, CancellationTokenSource cancellation, ClaudeAccountProfile profile)
    {
        StartTimer();
        _readerSession = session;
        _enabled = providers;
        _readerClaudeProfile = profile;
        _store = new AgentUsageStore();
        _cursors.Clear();
        _loaded = false;
        // Prices first, so the first read is already priced.
        LoadPrices();
        var horizon = DateTime.UtcNow - Horizon;
        var roots = AgentLogRoot.All(profile).Where(r => providers.Contains(r.Provider)).ToList();
        var files = AgentLogReader.Discover(roots, horizon);
        // Resumes where the last launch stopped, among the logs there now.
        if (AgentUsageArchive.Load() is { } saved && saved.Providers.ToHashSet().SetEquals(providers))
        {
            var found = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var (store, cursors) = AgentUsageArchive.Resume(saved, found, horizon);
            _store = store;
            foreach (var (path, cursor) in cursors)
            {
                _cursors[path] = cursor;
            }
        }

        _initialFiles = new Queue<(string, AgentProvider)>(files);
        _worker.Post(() => StepInitial(session, cancellation, roots));
    }

    /// <summary>Reads the logs one at a time, so other posted work (limits, page opening) interleaves with a long first read.</summary>
    private void StepInitial(int session, CancellationTokenSource cancellation, List<AgentLogRoot> roots)
    {
        if (session != _readerSession || cancellation.IsCancellationRequested || _initialFiles is null)
        {
            return;
        }

        if (_paused)
        {
            _worker.Post(TimeSpan.FromSeconds(1), () => StepInitial(session, cancellation, roots));
            return;
        }

        if (_initialFiles.Count > 0)
        {
            var (path, provider) = _initialFiles.Dequeue();
            Read(path, provider);
            _worker.Post(() => StepInitial(session, cancellation, roots));
            return;
        }

        _initialFiles = null;
        var now = DateTime.UtcNow;
        // A turn left open by a crash would otherwise stay working.
        _store.CloseIdleTurns(now, AgentSettings.IdleTurn);
        CloseEndedTurns(roots, true);
        // The account Claude Code uses picks the Claude app's readings.
        ReadClaudePlan();
        ReadClaudeApp(now);
        _store.ReportsTransitions = true;
        _previousLimits = new Dictionary<AgentProvider, AgentLimits>(_store.Limits);
        // A budget already passed before launch is history, not news.
        var today = now.ToLocalTime().Date;
        if (_settings.DailyBudget() is { } budget && _store.Records.Where(r => r.Date.ToLocalTime() >= today).Sum(r => r.Cost ?? 0) >= budget)
        {
            _budgetDay = today;
        }

        _loaded = true;
        Watch(roots);
        StartPolling();
        WatchNetwork();
        Publish();
        SaveProgress();
    }

    private void SaveProgress()
    {
        if (_readerSession < 0 || !_loaded)
        {
            return;
        }

        var mark = ProgressMark();
        _lastSave = DateTime.UtcNow;
        if (mark == _savedMark)
        {
            return;
        }

        // A log that started over while running still counts what its old contents gave. Left out, the next launch reads it as rewritten.
        var kept = _cursors.Values.Where(c => !c.Restarted && c.Provider != AgentProvider.OpenCode).ToList();
        var saved = new List<AgentLogCursor.SavedCursor>();
        foreach (var c in kept)
        {
            SafeHandle(c, h => saved.Add(c.Save(h)));
        }

        var contents = new AgentUsageArchive.Contents { Providers = _enabled.ToList(), Store = _store.Save(), Cursors = saved };
        if (AgentUsageArchive.Save(contents))
        {
            _savedMark = mark;
        }
    }

    private static void SafeHandle(AgentLogCursor cursor, Action<Microsoft.Win32.SafeHandles.SafeFileHandle?> action)
    {
        try
        {
            using var handle = File.OpenHandle(cursor.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            action(handle);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            action(null);
        }
    }

    /// <summary>Changes whenever a log is read further, replaced or let go. OpenCode, which is never saved, leaves it alone.</summary>
    private long ProgressMark()
    {
        long mark = _store.Records.Count(r => r.Provider != AgentProvider.OpenCode);
        foreach (var c in _cursors.Values)
        {
            if (c.Provider == AgentProvider.OpenCode)
            {
                continue;
            }

            mark += HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(c.Path), c.Offset, c.Identity);
        }

        return mark;
    }

    private void StartPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = new Timer(_ => _worker.Post(PollCore), null, Poll, Poll);
    }

    private bool _pollBusy;

    private void PollCore()
    {
        if (_readerSession < 0 || !_loaded || _paused || _pollBusy)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var wanted = _store.Turns.Count > 0 || _store.Waiting.Count > 0 || _cursors.Values.Any(c => now - c.Modified < PollWindow);
        if (!wanted)
        {
            return;
        }

        _pollBusy = true;
        try
        {
            var read = PollOpenLogs(PollWindow);
            var stopped = _store.CloseSettledTurns(now);
            // After the logs too, which can hold a reply or a command's result written meanwhile.
            if (_offlineSince is { } offline
                && _store.CloseOfflineTurns(offline.Date, TimeSpan.FromSeconds(AgentNative.AwakeSeconds - offline.Awake), RunningCommands))
            {
                stopped = true;
            }

            // After the logs, so a turn its last lines ended ends as usual.
            if (!(CloseEndedTurns(_watchedRoots) || read || stopped))
            {
                return;
            }

            CheckLimits();
            SchedulePublish();
        }
        finally
        {
            _pollBusy = false;
        }
    }

    /// <summary>Reads the logs that grew, were replaced or disappeared since the last look, among those an agent may still be writing.</summary>
    private bool PollOpenLogs(TimeSpan window)
    {
        if (_readerSession < 0)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        // A turn gone quiet, as while it waits for an approval, is noticed as soon as its work resumes.
        var working = new HashSet<string>(_store.Turns.Keys.Concat(_store.Waiting.Keys), StringComparer.OrdinalIgnoreCase);
        foreach (var key in working.ToList())
        {
            var mark = key.IndexOf('#');
            if (mark >= 0)
            {
                working.Add(key[..mark]);
            }
        }

        var changed = false;
        foreach (var (path, cursor) in _cursors.ToList())
        {
            if (!working.Contains(path) && now - cursor.Modified >= window)
            {
                continue;
            }

            if (cursor.Provider == AgentProvider.OpenCode)
            {
                // A database changes in place: its write-ahead log grows instead.
                if (OpenCodeDatabase.Modified(path) is { } m && m <= cursor.Modified)
                {
                    continue;
                }

                if (Read(path, cursor.Provider))
                {
                    changed = true;
                }

                continue;
            }

            var info = new FileInfo(path);
            if (info.Exists && (ulong)info.Length == cursor.Offset)
            {
                continue;
            }

            if (Read(path, cursor.Provider))
            {
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Claude Code retries for minutes without a word while the PC is offline, then gives up.</summary>
    private void WatchNetwork()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        _online = NetworkInterface.GetIsNetworkAvailable();
        if (!_online)
        {
            _offlineSince = (DateTime.UtcNow, AgentNative.AwakeSeconds);
        }
    }

    private void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs e) => _worker.Post(() =>
    {
        if (_readerSession < 0)
        {
            return;
        }

        if (e.IsAvailable)
        {
            _offlineSince = null;
        }
        else if (_offlineSince is null)
        {
            _offlineSince = (DateTime.UtcNow, AgentNative.AwakeSeconds);
        }

        if (e.IsAvailable != _online)
        {
            _online = e.IsAvailable;
            // Back online, the checks start over from the shortest wait.
            if (_online)
            {
                _claudeUnreachable = 0;
            }
        }
    });

    /// <summary>The logs whose turn waits on a shell command of its own.</summary>
    private HashSet<string> RunningCommands() => _cursors.Where(kv => kv.Value.State.RunningCommands.Count > 0).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ends the Claude turns whose process is gone. True when one was showing.</summary>
    private bool CloseEndedTurns(IEnumerable<AgentLogRoot> roots, bool atLaunch = false)
    {
        if (!_store.ShowsClaudeTurn)
        {
            return false;
        }

        var folders = roots.Where(r => r.Provider == AgentProvider.Claude)
            .Select(r => Path.Combine(Path.GetDirectoryName(r.Path.TrimEnd('\\', '/')) ?? "", "sessions")).Distinct(StringComparer.OrdinalIgnoreCase);
        return _store.CloseEndedTurns(AgentSessionRegistry.Read(folders), atLaunch);
    }

    /// <summary>True when the log had entries to apply, or was gone and took a working turn with it.</summary>
    private bool Read(string path, AgentProvider provider)
    {
        var cancellation = _cancellation;
        if (cancellation.IsCancellationRequested)
        {
            return false;
        }

        if (!File.Exists(path))
        {
            _cursors.Remove(path);
            return _store.Forget(path);
        }

        if (!_cursors.TryGetValue(path, out var cursor))
        {
            cursor = new AgentLogCursor(path, provider);
            _cursors[path] = cursor;
        }

        var changed = false;
        var now = DateTime.UtcNow;
        var reader = AgentReaders.For(provider);

        void Consume(ReadOnlyMemory<byte> line)
        {
            // Apply in log order while the chunk is alive instead of retaining every parsed entry.
            var entries = reader.Parse(line, cursor.State, now);
            if (entries.Count == 0)
            {
                return;
            }

            changed = true;
            var isSubagent = provider == AgentProvider.OpenCode && cursor.State.ParentSession.Length > 0;
            var turnFile = provider == AgentProvider.OpenCode && cursor.State.Session.Length > 0 ? $"{path}#{cursor.State.Session}" : path;
            var tracksTurns = !isSubagent && cursor.TracksTurns;
            var parent = isSubagent ? $"{path}#{cursor.State.ParentSession}" : cursor.Parent ?? cursor.CodexParent(_cursors);
            var finished = _store.Apply(entries, turnFile, provider, tracksTurns, parent, cursor.Modified, now);
            foreach (var e in finished)
            {
                Report(e);
            }
        }

        bool Continue() => !cancellation.IsCancellationRequested;

        if (reader.ReadsFullHistoryFirst && cursor.Offset == 0)
        {
            AgentLogReader.ReadCopilotHistory(cursor, Continue, Consume);
        }
        else
        {
            AgentLogReader.ReadAppended(cursor, DateTime.UtcNow - Horizon, Continue, null, Consume);
        }

        return changed;
    }

    // ------------------------------------------------------------------------------------------ watching

    private void StopWatchers()
    {
        foreach (var w in _watchers)
        {
            try
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _watchers.Clear();
    }

    private void Watch(List<AgentLogRoot> roots)
    {
        var existing = roots.Where(r => r.Exists).ToList();
        _watchedRoots = existing;
        _lastRootCheck = DateTime.UtcNow;
        StopWatchers();
        foreach (var root in existing)
        {
            try
            {
                var watcher = new FileSystemWatcher(root.Path)
                {
                    IncludeSubdirectories = root.Provider != AgentProvider.OpenCode,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
                };
                watcher.Changed += (_, e) => OnFileEvent(e.FullPath);
                watcher.Created += (_, e) => OnFileEvent(e.FullPath);
                watcher.Deleted += (_, e) => OnFileEvent(e.FullPath);
                watcher.Renamed += (_, e) =>
                {
                    OnFileEvent(e.OldFullPath);
                    OnFileEvent(e.FullPath);
                };
                watcher.Error += (_, _) =>
                {
                    lock (_changedPaths)
                    {
                        _rescanRequested = true;
                    }

                    ScheduleWatchFlush();
                };
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // The poll and the 30 second sweep still find this root's logs.
            }
        }
    }

    private void OnFileEvent(string path)
    {
        if (!AgentLogReader.IsLog(path))
        {
            return;
        }

        lock (_changedPaths)
        {
            _changedPaths.Add(path);
        }

        ScheduleWatchFlush();
    }

    /// <summary>File events are debounced for a second, so a busy agent costs one pass a second at most.</summary>
    private void ScheduleWatchFlush()
    {
        lock (_changedPaths)
        {
            if (_watchFlushScheduled)
            {
                return;
            }

            _watchFlushScheduled = true;
        }

        _worker.Post(TimeSpan.FromSeconds(1), () =>
        {
            string[] paths;
            bool rescan;
            lock (_changedPaths)
            {
                paths = _changedPaths.ToArray();
                _changedPaths.Clear();
                rescan = _rescanRequested;
                _rescanRequested = false;
                _watchFlushScheduled = false;
            }

            if (!_paused)
            {
                FilesChanged(paths, rescan);
            }
        });
    }

    private void FilesChanged(string[] paths, bool rescan)
    {
        if (_readerSession < 0 || !_loaded || _watchedRoots.Count == 0)
        {
            return;
        }

        var changed = false;
        if (rescan)
        {
            foreach (var file in AgentLogReader.Discover(_watchedRoots, DateTime.UtcNow - Horizon))
            {
                if (Read(file.Path, file.Provider))
                {
                    changed = true;
                }
            }
        }
        else
        {
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var actual = path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;
                var root = _watchedRoots.FirstOrDefault(r => r.Accepts(actual));
                if (root is null)
                {
                    continue;
                }

                if (Read(actual, root.Provider))
                {
                    changed = true;
                }
            }
        }

        // Saved tool output and lines with nothing to keep do not change the summary or require a display update.
        if (!changed)
        {
            return;
        }

        CheckLimits();
        SchedulePublish();
    }

    // ------------------------------------------------------------------------------------------ publishing

    /// <summary>Bursts of writes publish once.</summary>
    private void SchedulePublish()
    {
        if (_publishScheduled)
        {
            return;
        }

        _publishScheduled = true;
        _worker.Post(PublishDelay, () =>
        {
            _publishScheduled = false;
            if (_loaded)
            {
                Publish();
            }
        });
    }

    private void TickCore()
    {
        if (!_running)
        {
            return;
        }

        SyncPrices();
        RefreshClaudeLimits(DateTime.UtcNow, false);
        if (_readerSession < 0 || !_loaded)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var before = Inputs();
        _store.CloseIdleTurns(now, AgentSettings.IdleTurn);
        _store.DropRecords(now - Horizon);
        // A log kept open through a long pause reports nothing when work resumes; the day's logs are looked at less often than recent ones.
        var changed = PollOpenLogs(TimeSpan.FromHours(24));
        // A folder that appears later, like a first Codex session, is picked up without a restart.
        if (now - _lastRootCheck > TimeSpan.FromMinutes(5))
        {
            var roots = AgentLogRoot.All(_readerClaudeProfile).Where(r => _enabled.Contains(r.Provider)).ToList();
            if (!roots.Where(r => r.Exists).Select(r => r.Path).SequenceEqual(_watchedRoots.Select(r => r.Path), StringComparer.OrdinalIgnoreCase))
            {
                foreach (var file in AgentLogReader.Discover(roots, now - Horizon))
                {
                    if (Read(file.Path, file.Provider))
                    {
                        changed = true;
                    }
                }

                Watch(roots);
            }

            _lastRootCheck = now;
        }

        ReadClaudePlan();
        ReadClaudeApp(now);
        CheckLimits();
        ReportRenewals(now);
        if (now - _lastSave >= SaveInterval)
        {
            SaveProgress();
        }

        // Publish only when stored values or sliding time windows change.
        if (changed || Inputs() != before || AgentUsageSummary.MovesWithClock(_published, now))
        {
            SchedulePublish();
        }
    }

    /// <summary>A cheap fingerprint of the stored state a snapshot is made from, compared across a tick.</summary>
    private long Inputs()
    {
        var h = new HashCode();
        h.Add(_store.Records.Count);
        foreach (var t in _store.Turns.Values)
        {
            h.Add(t.Id, StringComparer.OrdinalIgnoreCase);
            h.Add(t.LastActivity);
            h.Add(t.Tokens.Total);
        }

        foreach (var l in _store.Limits.Values)
        {
            h.Add(l.ObservedAt);
            h.Add(l.Provider);
            foreach (var w in l.Windows)
            {
                h.Add(w.UsedPercent);
                h.Add(w.ResetsAt);
            }
        }

        h.Add(_store.CodexPlan);
        h.Add(_claudePlan);
        h.Add(_claudeOrganization);
        h.Add(_claudeIdentity);
        h.Add(_claudeAppSamples.Count);
        return h.ToHashCode();
    }

    /// <summary>Builds the snapshot and hands it to the UI thread.</summary>
    private void Publish()
    {
        var session = _readerSession;
        if (session < 0)
        {
            return;
        }

        var plans = new Dictionary<AgentProvider, AgentPlan>();
        if (_claudePlan is not null)
        {
            plans[AgentProvider.Claude] = _claudePlan;
        }

        if (AgentPlans.Codex(_store.CodexPlan) is { } codex)
        {
            plans[AgentProvider.Codex] = codex;
        }

        var previousLive = _published.Live.Select(l => l.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var next = AgentUsageSummary.Snapshot(_store.Records, _store.Limits, _store.Live, plans, _enabled, DateTime.UtcNow);
        next.LimitSources = BuildLimitSources(next);
        _published = next;
        _snapshot = next;
        CheckBudget(next);
        PublishStatus();
        var liveChanged = !previousLive.SequenceEqual(next.Live.Select(l => l.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        RaiseChanged(liveChanged);
    }

    private List<LimitSourceInfo> BuildLimitSources(AgentUsageSnapshot next)
    {
        var list = new List<LimitSourceInfo>(_claudeSources);
        if (_enabled.Contains(AgentProvider.Codex))
        {
            var inUse = _store.Limits.GetValueOrDefault(AgentProvider.Codex);
            foreach (var origin in new[] { LimitOrigins.CodexServer, LimitOrigins.CodexLog })
            {
                var reading = _store.Readings.Values.FirstOrDefault(r => r.Provider == AgentProvider.Codex && r.Origin == origin);
                var info = new LimitSourceInfo
                {
                    Origin = origin, Provider = AgentProvider.Codex, Name = LimitOrigins.Name(origin),
                    Where = origin == LimitOrigins.CodexServer ? "codex app-server, account/rateLimits/read (asked when the Codex limits card shows)" : "~/.codex/sessions/**/rollout-*.jsonl, token_count events",
                    ObservedAt = reading?.ObservedAt, InUse = reading is not null && ReferenceEquals(reading, inUse),
                };
                if (reading is null)
                {
                    info.Status = origin == LimitOrigins.CodexServer ? "Not asked yet, or Codex could not answer" : "No token_count event with limits in the logs";
                }
                else
                {
                    info.Status = info.InUse ? "In use: the newest reading" : "Not used: an older reading than the one in use";
                    foreach (var w in reading.Windows)
                    {
                        info.Raw.Add($"window {w.Minutes?.ToString() ?? "?"} min: used {w.UsedPercent:0.#}%, reset {(w.ResetsAt is { } r ? r.ToString("u") : "none given")}" + (w.Note is { } n ? " - " + n : ""));
                    }
                }

                list.Add(info);
            }
        }

        return list;
    }

    private void PublishStatus()
    {
        DateTime? checkedAt = null;
        if (_enabled.Contains(AgentProvider.Claude))
        {
            checkedAt = _claudeAppSamples.LastOrDefault(s => _claudeOrganization is null || s.Organization is null || s.Organization == _claudeOrganization)?.Date;
        }

        var listed = AgentPricing.List.Updated;
        _status = new AgentServiceStatus
        {
            Running = _running,
            ClaudeAppChecked = checkedAt,
            ClaudeAccountChecking = _claudeChecking,
            ClaudeAccountFailure = _claudeFailure,
            ClaudeProfile = _claudeProfile,
            ClaudeAccountIdentity = _claudeIdentity,
            PricesUpdated = listed == AgentPriceList.Empty.Updated ? null : listed,
        };
    }

    private void RaiseChanged(bool liveChanged)
    {
        _ui.BeginInvoke(() =>
        {
            Changed?.Invoke();
            if (liveChanged)
            {
                LiveChanged?.Invoke();
            }
        });
    }

    // ------------------------------------------------------------------------------------------ alerts

    /// <summary>Filters by the person's choices and hands the event to the UI thread.</summary>
    private void Report(AgentUsageEvent e)
    {
        switch (e)
        {
            case AgentUsageEvent.Finished f:
                if (!_providers.Contains(f.Provider) || _settings.FinishMinimum() is not { } minimum || f.Duration < minimum)
                {
                    return;
                }

                break;
            case AgentUsageEvent.LimitWarning w:
                if (!_providers.Contains(w.Provider) || _settings.LimitThreshold() is null)
                {
                    return;
                }

                break;
            case AgentUsageEvent.LimitReset r:
                if (!_providers.Contains(r.Provider) || _settings.LimitThreshold() is null)
                {
                    return;
                }

                break;
            case AgentUsageEvent.BudgetReached:
                if (_settings.DailyBudget() is null)
                {
                    return;
                }

                break;
        }

        _ui.BeginInvoke(() => Alert?.Invoke(e));
    }

    /// <summary>Runs whenever limits may have changed.</summary>
    private void CheckLimits()
    {
        if (!_store.ReportsTransitions)
        {
            return;
        }

        var threshold = _settings.LimitThreshold() ?? AgentSettings.DefaultLimitThreshold;
        foreach (var (provider, limits) in _store.Limits)
        {
            _previousLimits.TryGetValue(provider, out var previous);
            foreach (var window in AgentLimitSupport.Crossings(previous, limits, threshold))
            {
                _warned[window.Id] = (provider, window);
                Report(new AgentUsageEvent.LimitWarning(provider, window));
            }
        }

        // A banked reset renews a warned window before its time, which is news now rather than at the renewal it replaced.
        foreach (var (id, entry) in _warned.ToList())
        {
            if (!_store.Limits.TryGetValue(entry.Provider, out var reading) || reading.Windows.FirstOrDefault(w => w.Id == id) is not { } window
                || window.UsedPercent >= threshold || !AgentLimitSupport.Renewed(entry.Window, window, reading.ObservedAt))
            {
                continue;
            }

            _warned.Remove(id);
            Report(new AgentUsageEvent.LimitReset(entry.Provider, window));
        }

        _previousLimits = new Dictionary<AgentProvider, AgentLimits>(_store.Limits);
    }

    /// <summary>A warned window that renews brings its agent back: worth a word.</summary>
    private void ReportRenewals(DateTime now)
    {
        foreach (var (id, entry) in _warned.ToList())
        {
            if (entry.Window.ResetsAt is { } resets && resets <= now)
            {
                _warned.Remove(id);
                Report(new AgentUsageEvent.LimitReset(entry.Provider, entry.Window));
            }
        }
    }

    private void CheckBudget(AgentUsageSnapshot snapshot)
    {
        if (!_store.ReportsTransitions || _settings.DailyBudget() is not { } budget)
        {
            return;
        }

        var today = snapshot.Now.ToLocalTime().Date;
        var spent = snapshot.Usage(AgentPeriod.Today).Total.Cost;
        if (spent < budget || _budgetDay == today)
        {
            return;
        }

        _budgetDay = today;
        Report(new AgentUsageEvent.BudgetReached(spent, budget));
    }

    // ------------------------------------------------------------------------------------------ plans and limits

    /// <summary>The plan and visible identity come from Claude Code's profile. Other fields are discarded.</summary>
    private void ReadClaudePlan()
    {
        if (!_enabled.Contains(AgentProvider.Claude))
        {
            return;
        }

        DateTime? modified = null;
        try
        {
            var info = new FileInfo(_readerClaudeProfile.ProfileFile(_home));
            modified = info.Exists ? info.LastWriteTimeUtc : null;
        }
        catch (IOException)
        {
        }

        if (modified == _claudeProfileModified && _readerClaudeProfile.IsAvailable(_home))
        {
            return;
        }

        _claudeProfileModified = modified;
        var reading = _readerClaudeProfile.Read(_home);
        if (reading.Identity != _claudeIdentity)
        {
            _claudeAccountLimits = null;
            _store.ClearLimits(AgentProvider.Claude);
            _previousLimits.Remove(AgentProvider.Claude);
            foreach (var key in _warned.Where(kv => kv.Value.Provider == AgentProvider.Claude).Select(kv => kv.Key).ToList())
            {
                _warned.Remove(key);
            }
        }

        _claudeCachedLimits = reading.CachedLimits;
        _claudeCachedRaw = reading.CachedRaw;
        _claudeCachedFetched = reading.UsageFetched;
        if (reading.Identity is not { } identity)
        {
            _claudePlan = null;
            _claudeOrganization = null;
            _claudeIdentity = null;
            _claudeAccountLimits = null;
            _store.ClearLimits(AgentProvider.Claude);
            return;
        }

        _claudeIdentity = identity;
        _claudeOrganization = identity.Organization;
        _claudePlan = reading.Plan;
    }

    /// <summary>Reads the limits the Claude app saved when the file changes, and places them at now on every call.</summary>
    private void ReadClaudeApp(DateTime now)
    {
        if (!_enabled.Contains(AgentProvider.Claude))
        {
            _store.ClearLimits(AgentProvider.Claude);
            _claudeAccountLimits = null;
            _claudeAppModified = null;
            _claudeAppSamples = [];
            return;
        }

        var account = ClaudeServer.Current(_claudeAccountLimits, now);
        var cached = ClaudeServer.Current(_claudeCachedLimits, now);
        // Desktop history identifies an organization, not an individual account. A chosen profile uses only its own CLI reading.
        if (!_readerClaudeProfile.IsDefault)
        {
            _claudeAppModified = null;
            _claudeAppSamples = [];
            var own = new[] { account, cached }.Where(l => l is not null).OrderByDescending(l => l!.ObservedAt).FirstOrDefault();
            if (own is not null)
            {
                _store.SetLimits(own);
            }
            else
            {
                _store.ClearLimits(AgentProvider.Claude);
            }

            return;
        }

        var path = ClaudeAppUsage.HistoryPath();
        DateTime? modified = null;
        try
        {
            modified = path is not null && File.Exists(path) ? new FileInfo(path).LastWriteTimeUtc : null;
        }
        catch (IOException)
        {
        }

        if (modified != _claudeAppModified)
        {
            _claudeAppModified = modified;
            _claudeAppSamples = modified is null ? [] : ClaudeAppUsage.ReadFile(path) ?? [];
        }

        // Claude Code's own first request can place the session's start.
        var start = ClaudeAppUsage.SessionStart(_store.Records, _claudeAppSamples, _claudeOrganization);
        var desktop = ClaudeAppUsage.Limits(_claudeAppSamples, now, start, _claudeOrganization);
        // The newest observation wins, whichever source it came from; on a tie the more direct source does. The desktop app's saved
        // history never outranks a later reading from Claude Code itself.
        var latest = new[] { desktop, account, cached }.Where(l => l is not null).Select(l => l!)
            .OrderByDescending(l => l.ObservedAt).ThenBy(l => LimitOrigins.Rank(l.Origin)).FirstOrDefault();
        _claudeSources = DescribeClaudeSources(account, cached, desktop, latest);
        if (latest is not null)
        {
            _store.SetLimits(latest);
        }
        else
        {
            _store.ClearLimits(AgentProvider.Claude);
        }
    }

    /// <summary>Every place Claude's limits can be read, with what each one says now (the "Where these numbers come from" list).</summary>
    private List<LimitSourceInfo> DescribeClaudeSources(AgentLimits? account, AgentLimits? cached, AgentLimits? desktop, AgentLimits? chosen)
    {
        var home = _home;
        string Why(AgentLimits? reading) => ReferenceEquals(reading, chosen) ? "In use: the newest reading" : "Not used: an older reading than the one in use";
        var list = new List<LimitSourceInfo>
        {
            new()
            {
                Origin = LimitOrigins.ClaudeCli, Provider = AgentProvider.Claude, Name = "Claude Code (live check)", Where = "claude.exe, control request get_usage (no prompt is sent)",
                ObservedAt = _claudeAccountLimits?.ObservedAt, Raw = _claudeAccountRaw, InUse = account is not null && ReferenceEquals(account, chosen),
                Status = _claudeChecking ? "Checking now" : account is null ? (_claudeFailure is { } f ? AgentStrings.ClaudeCode(f) : "Not asked yet, or every window has since expired") : Why(account),
            },
            new()
            {
                Origin = LimitOrigins.ClaudeCache, Provider = AgentProvider.Claude, Name = "Claude Code (saved copy)", Where = _readerClaudeProfile.ProfileFile(home) + " > cachedUsageUtilization",
                ObservedAt = _claudeCachedFetched, Raw = _claudeCachedRaw, InUse = cached is not null && ReferenceEquals(cached, chosen),
                Status = cached is null ? (_claudeCachedFetched is null ? "No saved copy in this profile" : "Saved copy has no window that is still running") : Why(cached),
            },
        };
        var path = ClaudeAppUsage.HistoryPath();
        var sample = _claudeAppSamples.LastOrDefault(s => _claudeOrganization is null || s.Organization is null || s.Organization == _claudeOrganization);
        list.Add(new LimitSourceInfo
        {
            Origin = LimitOrigins.ClaudeDesktop, Provider = AgentProvider.Claude, Name = "Claude desktop app history", Where = path ?? "plan-usage-history.json (Claude app not found)",
            ObservedAt = sample?.Date,
            Raw = sample is null ? [] : [string.Join(", ", sample.Used.Select(kv => kv.Key + " " + kv.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%")) + " (percentages only: the app does not save reset times)"],
            InUse = desktop is not null && ReferenceEquals(desktop, chosen),
            Status = path is null ? "Claude app not found on this PC" : desktop is null ? "Latest sample is too old or has no running window" : Why(desktop),
        });
        return list;
    }

    /// <summary>Every minute only while the page shows; failures back off, and looking at the page does not bypass that.</summary>
    private void RefreshClaudeLimits(DateTime now, bool pageShows)
    {
        if (!ClaudeLimitsDue(now, pageShows))
        {
            return;
        }

        _claudeAttempted = now;
        _claudeChecking = true;
        PublishStatus();
        RaiseChanged(false);
        var request = new ClaudeServer.Request();
        _claudeRequest = request;
        var session = _session;
        var profile = _claudeProfile;
        var home = _home;
        _ = Task.Run(() =>
        {
            (ClaudeSummary? Summary, ClaudeFailure? Failure) result;
            var path = Environment.GetEnvironmentVariable("PATH");
            if (ClaudeServer.Executable(home, path) is { } executable)
            {
                var env = ClaudeServer.BuildEnvironment(Environment.GetEnvironmentVariables(), home, profile);
                env["PATH"] = ClaudeServer.SearchPath(env.GetValueOrDefault("PATH", ""), home);
                result = request.Run(executable, home, env, profile);
            }
            else
            {
                result = (null, ClaudeFailure.Missing);
            }

            _worker.Post(() =>
            {
                if (!_running || _paused || _session != session || !ReferenceEquals(_claudeRequest, request))
                {
                    return;
                }

                _claudeRequest = null;
                _claudeChecking = false;
                if (result.Failure is { } failure)
                {
                    NoteClaudeCheck(failure);
                    PublishStatus();
                    RaiseChanged(false);
                    return;
                }

                NoteClaudeCheck(null);
                if (_readerSession != session || !_enabled.Contains(AgentProvider.Claude))
                {
                    return;
                }

                ReadClaudePlan();
                if (_readerClaudeProfile.Equals(profile) && result.Summary is { } summary && Equals(_claudeIdentity, summary.Identity))
                {
                    _claudeAccountLimits = summary.Limits;
                    _claudeAccountRaw = summary.Raw ?? [];
                    ReadClaudeApp(DateTime.UtcNow);
                    CheckLimits();
                    SchedulePublish();
                }

                PublishStatus();
                RaiseChanged(false);
            });
        });
    }

    private void CancelClaudeLimits()
    {
        if (_claudeRequest is not { } request)
        {
            return;
        }

        request.Cancel();
        _claudeRequest = null;
        _claudeChecking = false;
        // Resume asks again instead of waiting out a cancelled check.
        _claudeAttempted = DateTime.MinValue;
    }

    /// <summary>Never while the feature is off, asleep, offline or already checking, nor while nothing shows or alerts on the limits.</summary>
    private bool ClaudeLimitsDue(DateTime now, bool pageShows)
    {
        if (!_running || _paused || !_online || !_providers.Contains(AgentProvider.Claude) || _claudeRequest is not null
            || !_settings.LimitsInUse(_pageVisible || pageShows))
        {
            return false;
        }

        return now - _claudeAttempted >= ClaudeServer.Interval(_claudeFailure, _claudeUnreachable, _pageVisible);
    }

    private void NoteClaudeCheck(ClaudeFailure? failure)
    {
        _claudeFailure = failure;
        _claudeUnreachable = failure == ClaudeFailure.Unreachable ? _claudeUnreachable + 1 : 0;
    }

    // ------------------------------------------------------------------------------------------ prices

    /// <summary>The newer of the list inside the app and the last one downloaded.</summary>
    private void LoadPrices()
    {
        _shippedPrices ??= AgentPriceList.Bundled();
        var cached = AgentPriceSource.Cached();
        AgentPricing.Install(AgentPriceList.Newer(_shippedPrices, cached?.List) ?? AgentPriceList.Empty);
        _pricesSaved = cached?.Saved ?? DateTime.MinValue;
        SyncPrices();
    }

    /// <summary>Looks for a newer price list at most once a day, and again a few hours after a failure.</summary>
    private void SyncPrices()
    {
        if (!_running || !_settings.UpdatesPrices() || _pricesInFlight || _pricesSaved is not { } saved || !AgentPriceSource.IsConfigured)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var wait = _pricesFailed ? AgentPriceSource.RetryInterval : AgentPriceSource.RefreshInterval;
        if (now - saved < AgentPriceSource.RefreshInterval || now - _pricesAttempted < wait)
        {
            return;
        }

        _pricesInFlight = true;
        _pricesAttempted = now;
        var session = _session;
        _ = Task.Run(async () =>
        {
            var result = await AgentPriceSource.Download().ConfigureAwait(false);
            _worker.Post(() =>
            {
                if (_session != session)
                {
                    return;
                }

                _pricesInFlight = false;
                if (result is null)
                {
                    _pricesFailed = true;
                    return;
                }

                _pricesFailed = false;
                _pricesSaved = DateTime.UtcNow;
                AgentPriceSource.Save(result.Value.Data);
                if (_readerSession == session && AgentPricing.Install(AgentPriceList.Newer(_shippedPrices, result.Value.List) ?? result.Value.List))
                {
                    _store.Reprice();
                    if (_loaded)
                    {
                        Publish();
                    }
                }
            });
        });
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _tickTimer?.Dispose();
        _pollTimer?.Dispose();
        StopWatchers();
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _worker.Dispose();
    }
}

/// <summary>
/// Where the price list comes from: the copy inside the app, the last one downloaded, and the one published with WinKnife, fetched
/// at most once a day while the AI section is on and the person keeps prices up to date. The request carries no usage.
/// </summary>
internal static class AgentPriceSource
{
    /// <summary>
    /// WinKnife's own published copy of the price list. Empty until it is hosted: WinKnife never points at another project, so no
    /// download is attempted and the bundled list is used.
    /// </summary>
    public const string Remote = "";

    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(6);

    public static bool IsConfigured => Remote.Length > 0;

    private static string CachePath => Path.Combine(AgentLogRoot.LocalRoot, "agent-prices.json");

    /// <summary>The last list downloaded and when it was saved.</summary>
    public static (AgentPriceList List, DateTime Saved)? Cached()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }

            var list = AgentPriceList.Decode(File.ReadAllBytes(CachePath));
            return list is null ? null : (list, File.GetLastWriteTimeUtc(CachePath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(byte[] data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllBytes(CachePath, data);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The published list, checked before it is handed over; null on any failure.</summary>
    public static async Task<(byte[] Data, AgentPriceList List)?> Download()
    {
        try
        {
            var uri = new Uri(Remote);
            using var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinKnife/" + CodexServer.Version);
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            // Redirects are followed only to the same HTTPS host.
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                var target = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (target.Scheme != Uri.UriSchemeHttps || target.Host != uri.Host)
                {
                    return null;
                }

                response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            }

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                ms.Write(buffer, 0, read);
                if (ms.Length > AgentPriceList.MaximumSize)
                {
                    return null;
                }
            }

            var data = ms.ToArray();
            return AgentPriceList.Decode(data) is { } list ? (data, list) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException or IOException)
        {
            return null;
        }
    }
}
