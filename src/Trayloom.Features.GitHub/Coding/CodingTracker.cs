// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.GitHub;

/// <summary>Win32 calls of the foreground hook. Kept here so this feature needs nothing from the other projects.</summary>
internal static class CodingNative
{
    public const uint EventSystemForeground = 3;
    public const uint WinEventOutOfContext = 0;
    public const uint WinEventSkipOwnProcess = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint WmGetText = 0x000D;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;

    public delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(nint hwnd, uint message, nint wParam, StringBuilder lParam, uint flags, uint timeoutMs, out nint result);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref uint size);

    public static nint InstallForegroundHook(WinEventProc callback) =>
        SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, callback, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);

    public static void RemoveHook(nint hook)
    {
        if (hook != 0)
        {
            UnhookWinEvent(hook);
        }
    }

    public static uint ProcessOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    /// <summary>The executable file name of a process, or null when it cannot be opened (for example an elevated one).</summary>
    public static string? ImageOf(uint pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(handle, 0, sb, ref size) ? Path.GetFileName(sb.ToString()) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// The title of a foreign window. It is asked for with a timeout and without waiting on a hung window, so a stuck editor cannot
    /// block the thread that asks.
    /// </summary>
    public static string TitleOf(nint hwnd)
    {
        var sb = new StringBuilder(1024);
        var answered = SendMessageTimeoutW(hwnd, WmGetText, sb.Capacity, sb, SmtoBlock | SmtoAbortIfHung, 250, out _);
        return answered == 0 ? "" : sb.ToString();
    }
}

/// <summary>
/// Finds the repository you are working in and follows it: branch, changes, pull request and checks (spec 09 part 2). Everything it
/// reads stays on this PC; only with GitHub connected does it send one query for the branch. State lives on the UI thread, work
/// runs on the thread pool and comes back through the dispatcher. Names of repositories, branches, titles and paths are never logged.
/// </summary>
internal sealed class CodingTracker : IDisposable
{
    private static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SessionsEvery = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ActivityEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FoldersEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StorageEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan GitVisible = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GitIdle = TimeSpan.FromSeconds(60);

    private readonly IFeatureContext _ctx;
    private readonly GitHubRuntime _runtime;
    private readonly RepoIndex _index;
    private readonly GitProbeCache _cache = new();
    private readonly CodingNative.WinEventProc _hookProc;
    private CancellationTokenSource _cts = new();
    private DispatcherTimer? _clock;
    private DispatcherTimer? _debounce;
    private FileSystemWatcher? _watcher;
    private string _watchedDir = "";
    private nint _hook;
    private Action? _changed;
    private int _listeners;
    private bool _started;
    private bool _on;
    private bool _disposed;

    // what the signals say
    private nint _fgHwnd;
    private int _fgSeq;
    private bool _fgIsCoding;
    private RepoRef? _fgRepo;
    private (RepoRef Repo, DateTime At)? _lastCoding;
    private IReadOnlyList<(RepoRef Repo, DateTime UpdatedAt)> _agents = [];
    private IReadOnlyList<(RepoRef Repo, DateTime At)> _gitActivity = [];
    private DateTime _nextSessions;
    private DateTime _nextActivity;
    private DateTime _nextFolders;
    private DateTime _nextStorage;

    // the active repository
    private RepoRef? _active;
    private CodingReason? _reason;
    private GitState? _git;
    private GitError _gitError;
    private DateTime? _gitAt;
    private GhSlug? _slug;
    private string? _gitDefault;
    private string? _pushedSha;
    private bool _gitBusy;
    private DateTime _nextGit;

    // what GitHub said about the branch
    private BranchPull? _pull;
    private CheckSummary? _checks;
    private string? _checksKey;
    private string? _ghDefault;
    private DateTime? _ghAt;
    private GhError _ghError;
    private bool _ghFound = true;
    private bool _ghBusy;
    private bool _connected;
    private int _ghRemaining = int.MaxValue;
    private DateTime _nextGh;
    private DateTime _burstUntil;

    private string _sig = "";
    private string _shape = "";

    public CodingTracker(IFeatureContext ctx, GitHubRuntime runtime)
    {
        _ctx = ctx;
        _runtime = runtime;
        _index = new RepoIndex(Path.Combine(ctx.DataDirectory, "coding-repos.json"));
        _hookProc = OnForegroundEvent;
    }

    /// <summary>The latest picture of the repository you are working in. Replaced as a whole, never edited.</summary>
    public CodingSnapshot Snapshot { get; private set; } = CodingSnapshot.Empty;

    /// <summary>The island context the strip was last created with; used to ask the island to redraw.</summary>
    public IIslandContext? IslandContext { get; set; }

    /// <summary>
    /// Raised on the UI thread when something the strip or the card shows changed. Whoever is subscribed is looking at it, so the
    /// number of subscribers also decides how often Git is asked.
    /// </summary>
    public event Action? Changed
    {
        add
        {
            _changed += value;
            _listeners++;
        }
        remove
        {
            _changed -= value;
            _listeners = Math.Max(0, _listeners - 1);
        }
    }

    /// <summary>True when the setting is on and the tracker is running.</summary>
    public bool IsOn => _on;

    // ---------------------------------------------------------------- life cycle

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _index.Load();
        _connected = _runtime.Connected;
        _ctx.App.Changed += OnSettingChanged;
        _runtime.Changed += OnRuntimeChanged;
        OnUi(ApplyEnabled);
    }

    /// <summary>Undoes everything Start did: the hook, the timers, the watcher and any git process still running.</summary>
    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _ctx.App.Changed -= OnSettingChanged;
        _runtime.Changed -= OnRuntimeChanged;
        OnUi(ApplyEnabled);
    }

    /// <summary>Deletes the repository index (uninstall).</summary>
    public void Erase() => _index.Erase();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        _cts.Dispose();
    }

    private void OnUi(Action action)
    {
        if (_ctx.Ui.CheckAccess())
        {
            action();
        }
        else
        {
            _ctx.Ui.Invoke(action);
        }
    }

    private void ApplyEnabled()
    {
        var on = _started && !_disposed && _ctx.App.Get<bool>(CodingKeys.Enabled);
        if (on == _on)
        {
            return;
        }

        _on = on;
        if (on)
        {
            Begin();
        }
        else
        {
            End();
        }
    }

    private void Begin()
    {
        _cts = new CancellationTokenSource();
        _hook = CodingNative.InstallForegroundHook(_hookProc);
        var now = DateTime.UtcNow;
        _nextSessions = _nextActivity = _nextFolders = _nextStorage = now;
        _clock = new DispatcherTimer(DispatcherPriority.Background, _ctx.Ui) { Interval = TickEvery };
        _clock.Tick += (_, _) => Tick();
        _clock.Start();
        _fgHwnd = CodingNative.GetForegroundWindow();
        if (_fgHwnd != 0)
        {
            ResolveForeground(_fgHwnd);
        }

        Tick();
    }

    private void End()
    {
        CodingNative.RemoveHook(_hook);
        _hook = 0;
        _clock?.Stop();
        _clock = null;
        _debounce?.Stop();
        _debounce = null;
        DisposeWatcher();
        _cts.Cancel();
        GitCli.KillAll();
        _fgSeq++;
        _fgIsCoding = false;
        _fgRepo = null;
        _lastCoding = null;
        _agents = [];
        _gitActivity = [];
        _gitBusy = false;
        _ghBusy = false;
        SwitchRepo(null);
        Publish();
        GitHubGuard.FireAndLog(() => Task.Run(_index.Save), _ctx.Log, "coding index save");
    }

    private void OnSettingChanged(string key)
    {
        if (key is not (CodingKeys.Enabled or CodingKeys.Strip or CodingKeys.Folders or CodingKeys.Alerts))
        {
            return;
        }

        _ctx.Ui.BeginInvoke(() =>
        {
            switch (key)
            {
                case CodingKeys.Enabled:
                    ApplyEnabled();
                    break;
                case CodingKeys.Strip:
                    (IslandContext ?? _runtime.IslandContext)?.Invalidate();
                    break;
                case CodingKeys.Folders:
                    _nextFolders = DateTime.UtcNow;
                    break;
            }
        });
    }

    private void OnRuntimeChanged()
    {
        var connected = _runtime.Connected;
        if (connected == _connected)
        {
            return;
        }

        _connected = connected;
        if (!_on)
        {
            return;
        }

        if (connected)
        {
            _nextGh = DateTime.UtcNow;
            RefreshGitHub();
        }
        else
        {
            ClearGitHub();
            Publish();
        }
    }

    // ---------------------------------------------------------------- the clock

    private void Tick()
    {
        if (!_on)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_fgIsCoding && _fgHwnd != 0)
        {
            // A title changes without a focus change (a new folder in the same window), so a coding window in front is read again.
            ResolveForeground(_fgHwnd);
        }

        if (now >= _nextSessions)
        {
            _nextSessions = now + SessionsEvery;
            ReadSessions();
        }

        if (now >= _nextActivity)
        {
            _nextActivity = now + ActivityEvery;
            ReadActivity();
        }

        if (now >= _nextFolders)
        {
            _nextFolders = now + FoldersEvery;
            ScanFolders();
        }

        if (now >= _nextStorage)
        {
            _nextStorage = now + StorageEvery;
            ScanStorage();
        }

        Recompute();
        if (_active is not null)
        {
            if (now >= _nextGit)
            {
                RefreshGit();
            }

            if (now >= _nextGh)
            {
                RefreshGitHub();
            }
        }

        GitHubGuard.FireAndLog(() => Task.Run(_index.Save), _ctx.Log, "coding index save");
    }

    /// <summary>The island page opened: Git and GitHub are asked again when what they last said is older than 20 and 30 seconds.</summary>
    public void PageOpened()
    {
        if (!_on || _active is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_gitAt is not { } git || now - git > GitVisible)
        {
            RefreshGit();
        }

        if (_ghAt is not { } gh || now - gh > TimeSpan.FromSeconds(30))
        {
            RefreshGitHub();
        }
    }

    // ---------------------------------------------------------------- where you are working

    private void OnForegroundEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == 0 || !_on)
        {
            return;
        }

        _fgHwnd = hwnd;
        ResolveForeground(hwnd);
    }

    private void ResolveForeground(nint hwnd)
    {
        var seq = ++_fgSeq;
        GitHubGuard.FireAndLog(() => Task.Run(() =>
        {
            var found = ReadWindow(hwnd);
            _ctx.Ui.BeginInvoke(() => ApplyForeground(seq, found));
        }), _ctx.Log, "coding foreground");
    }

    private sealed record WindowRead(bool Skip, bool Coding, RepoRef? Repo);

    private WindowRead ReadWindow(nint hwnd)
    {
        var pid = CodingNative.ProcessOf(hwnd);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
        {
            return new WindowRead(true, false, null);
        }

        var image = CodingNative.ImageOf(pid);
        if (image is null || !CodingTitles.IsCodingApp(image))
        {
            return new WindowRead(false, false, null);
        }

        var hint = CodingTitles.Parse(image, CodingNative.TitleOf(hwnd));
        RepoRef? repo = null;
        if (hint.Kind == TitleHintKind.Path)
        {
            repo = hint.Candidates.Select(RepoIndex.FindRoot).FirstOrDefault(r => r is not null);
        }
        else if (hint.Kind == TitleHintKind.Folder)
        {
            repo = hint.Candidates.Select(_index.ByName).FirstOrDefault(r => r is not null);
        }

        return new WindowRead(false, true, repo);
    }

    private void ApplyForeground(int seq, WindowRead read)
    {
        if (!_on || seq != _fgSeq || read.Skip)
        {
            return;
        }

        _fgIsCoding = read.Coding;
        _fgRepo = read.Repo;
        if (read.Repo is { } repo)
        {
            var now = DateTime.UtcNow;
            _lastCoding = (repo, now);
            _index.Add(repo, now);
        }

        Recompute();
    }

    private void ReadSessions()
    {
        GitHubGuard.FireAndLog(() => Task.Run(() =>
        {
            var list = ReadClaudeSessions();
            _ctx.Ui.BeginInvoke(() =>
            {
                if (_on)
                {
                    _agents = list;
                    Recompute();
                }
            });
        }), _ctx.Log, "coding sessions");
    }

    /// <summary>
    /// Claude Code publishes one small file per open session. Only the process id, the working folder and the update time are read, from
    /// <c>sessions\*.json</c> (never from the credential files), and a session counts while its process lives and it updated within 15 minutes.
    /// </summary>
    private List<(RepoRef Repo, DateTime UpdatedAt)> ReadClaudeSessions()
    {
        var found = new Dictionary<string, (RepoRef Repo, DateTime UpdatedAt)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(config))
            {
                config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            }

            var folder = Path.Combine(config, "sessions");
            if (!Directory.Exists(folder))
            {
                return [];
            }

            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    if (new FileInfo(file).Length >= 1 << 16)
                    {
                        continue;
                    }

                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var doc = JsonDocument.Parse(stream);
                    var root = doc.RootElement;
                    var updated = ReadTime(root, "updatedAt");
                    if (updated is null || DateTime.UtcNow - updated.Value > CodingFocus.AgentWindow
                        || !(root.TryGetProperty("pid", out var pidValue) && pidValue.ValueKind == JsonValueKind.Number && pidValue.TryGetInt32(out var pid) && IsAlive(pid)))
                    {
                        continue;
                    }

                    if (root.StrOrNull("cwd") is { Length: > 0 } cwd && RepoIndex.FindRoot(cwd) is { } repo)
                    {
                        _index.Add(repo, updated.Value);
                        if (!found.TryGetValue(repo.Root, out var old) || old.UpdatedAt < updated.Value)
                        {
                            found[repo.Root] = (repo, updated.Value);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    // a session file being rewritten is read again in 15 seconds
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }

        return [.. found.Values];
    }

    private static DateTime? ReadTime(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number > 0)
        {
            // Claude Code writes epoch milliseconds; seconds are accepted too.
            return number > 1e11 ? DateTimeOffset.FromUnixTimeMilliseconds((long)number).UtcDateTime : DateTimeOffset.FromUnixTimeSeconds((long)number).UtcDateTime;
        }

        return value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // access denied still means the process exists
            return true;
        }
    }

    private void ReadActivity()
    {
        GitHubGuard.FireAndLog(() => Task.Run(() =>
        {
            var list = new List<(RepoRef Repo, DateTime At)>();
            foreach (var repo in _index.Recent(20))
            {
                var at = RepoIndex.ActivityTime(repo.Root);
                if (at > DateTime.MinValue)
                {
                    list.Add((repo, at));
                }
            }

            _ctx.Ui.BeginInvoke(() =>
            {
                if (_on)
                {
                    _gitActivity = list;
                    Recompute();
                }
            });
        }), _ctx.Log, "coding activity");
    }

    private void ScanFolders()
    {
        var setting = _ctx.App.Get<string>(CodingKeys.Folders);
        if (string.IsNullOrWhiteSpace(setting))
        {
            return;
        }

        GitHubGuard.FireAndLog(() => Task.Run(() => _index.ScanFolders(setting)), _ctx.Log, "coding folders");
    }

    private void ScanStorage()
    {
        GitHubGuard.FireAndLog(() => Task.Run(() => _index.ScanEditorStorage(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))), _ctx.Log, "coding editor storage");
    }

    // ---------------------------------------------------------------- the active repository

    private static bool SameRoot(RepoRef? a, RepoRef? b) =>
        a is null ? b is null : b is not null && string.Equals(a.Root, b.Root, StringComparison.OrdinalIgnoreCase);

    private void Recompute()
    {
        if (!_on)
        {
            return;
        }

        var (repo, reason) = CodingFocus.Decide(new CodingInput(_fgRepo, _fgIsCoding, _lastCoding, _agents, _gitActivity, DateTime.UtcNow));
        if (!SameRoot(repo, _active))
        {
            SwitchRepo(repo);
        }

        _reason = reason;
        Publish();
    }

    private void SwitchRepo(RepoRef? repo)
    {
        _active = repo;
        _git = null;
        _gitError = GitError.None;
        _gitAt = null;
        _slug = null;
        _gitDefault = null;
        _pushedSha = null;
        _burstUntil = DateTime.MinValue;
        ClearGitHub();
        ConfigureWatcher(repo);
        var now = DateTime.UtcNow;
        _nextGit = now;
        _nextGh = now;
        if (repo is not null)
        {
            RefreshGit();
        }
    }

    private void ClearGitHub()
    {
        _pull = null;
        _checks = null;
        _checksKey = null;
        _ghDefault = null;
        _ghAt = null;
        _ghError = GhError.None;
        _ghFound = true;
        _ghRemaining = int.MaxValue;
    }

    // ---------------------------------------------------------------- git

    private void ConfigureWatcher(RepoRef? repo)
    {
        DisposeWatcher();
        if (repo is null || RepoIndex.GitDir(repo.Root) is not { } dir)
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += (_, e) => OnGitFile(e.FullPath);
            watcher.Created += (_, e) => OnGitFile(e.FullPath);
            watcher.Deleted += (_, e) => OnGitFile(e.FullPath);
            watcher.Renamed += (_, e) => OnGitFile(e.FullPath);
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            _watchedDir = dir;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // without a watcher the timer still refreshes
        }
    }

    private void DisposeWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Runs on a thread-pool thread. The object store and LFS cache change constantly and say nothing about the branch.</summary>
    private void OnGitFile(string fullPath)
    {
        var dir = _watchedDir;
        if (dir.Length == 0 || !fullPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = fullPath[dir.Length..].TrimStart('\\', '/');
        if (relative.StartsWith("objects", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("lfs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ctx.Ui.BeginInvoke(KickDebounce);
    }

    private void KickDebounce()
    {
        if (!_on || _active is null || _debounce is { IsEnabled: true })
        {
            return;
        }

        _debounce ??= new DispatcherTimer(DispatcherPriority.Background, _ctx.Ui) { Interval = TimeSpan.FromSeconds(1.5) };
        _debounce.Tick -= OnDebounce;
        _debounce.Tick += OnDebounce;
        _debounce.Start();
    }

    private void OnDebounce(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        if (_on && _active is not null)
        {
            RefreshGit();
        }
    }

    private void RefreshGit()
    {
        if (_active is not { } repo || _gitBusy)
        {
            return;
        }

        _gitBusy = true;
        var ct = _cts.Token;
        GitHubGuard.FireAndLog(async () =>
        {
            GitProbe? probe = null;
            try
            {
                probe = await GitCli.ProbeAsync(repo.Root, _cache, ct).ConfigureAwait(false);
            }
            finally
            {
                var result = probe;
                _ = _ctx.Ui.BeginInvoke(() => ApplyGit(repo, result));
            }
        }, _ctx.Log, "coding git");
    }

    private void ApplyGit(RepoRef repo, GitProbe? probe)
    {
        _gitBusy = false;
        if (!_on || !SameRoot(_active, repo))
        {
            return;
        }

        var now = DateTime.UtcNow;
        _nextGit = now + (_listeners > 0 ? GitVisible : GitIdle);
        if (probe is null)
        {
            _gitError = GitError.Failed;
            _git = null;
            Publish();
            return;
        }

        var before = _git;
        var beforeSha = _pushedSha;
        _git = probe.State;
        _gitError = probe.Error;
        _gitAt = now;
        if (probe.Error != GitError.None || probe.State is null)
        {
            Publish();
            return;
        }

        var slugChanged = !Equals(_slug, probe.Slug);
        _slug = probe.Slug;
        _gitDefault = probe.DefaultBranch;
        _pushedSha = probe.PushedSha;
        var branchChanged = before is not null && !string.Equals(before.Branch, probe.State.Branch, StringComparison.Ordinal);
        if (slugChanged || branchChanged)
        {
            ClearGitHub();
            _nextGh = now;
        }

        var pushed = before is not null && ((probe.PushedSha is not null && probe.PushedSha != beforeSha) || (before.Ahead > 0 && probe.State.Ahead == 0));
        if (pushed)
        {
            // Checks start a moment after a push, so GitHub is asked at once and then every 15 seconds for two minutes.
            _burstUntil = now + TimeSpan.FromMinutes(2);
            _nextGh = now;
        }

        Publish();
        if (_nextGh <= now)
        {
            RefreshGitHub();
        }
    }

    // ---------------------------------------------------------------- github

    private sealed record GhFetch(GhError Error, DateTime? RetryAt, CodingRemote? Remote);

    private void RefreshGitHub()
    {
        if (_ghBusy || _active is not { } repo || !_runtime.Connected || _slug is not { } slug || _git?.Branch is not { } branch)
        {
            return;
        }

        var transport = _runtime.CreateTransport();
        if (transport is null)
        {
            return;
        }

        _ghBusy = true;
        var viaCli = _runtime.Source == "cli";
        var pushed = _pushedSha;
        var ct = _cts.Token;
        GitHubGuard.FireAndLog(async () =>
        {
            GhFetch? fetch = null;
            try
            {
                fetch = await FetchAsync(transport, viaCli, slug, branch, pushed, ct).ConfigureAwait(false);
            }
            finally
            {
                var result = fetch;
                _ = _ctx.Ui.BeginInvoke(() => ApplyGitHub(repo, slug, branch, result));
            }
        }, _ctx.Log, "coding github");
    }

    private static async Task<GhFetch> FetchAsync(IGhTransport transport, bool viaCli, GhSlug slug, string branch, string? pushed, CancellationToken ct)
    {
        string query;
        try
        {
            query = CodingChecks.BuildQuery(slug, branch, pushed, withParent: true);
        }
        catch (ArgumentException)
        {
            return new GhFetch(GhError.Unexpected, null, null);
        }

        RawResponse response;
        try
        {
            response = await transport.GraphQlAsync(query, false, ct).ConfigureAwait(false);
        }
        catch (GhTransportException ex)
        {
            return new GhFetch(ex.Error, null, null);
        }

        if (response.Status == 401)
        {
            return new GhFetch(viaCli ? GhError.SignedOut : GhError.TokenRejected, null, null);
        }

        if (response.Status is 403 or 429)
        {
            return new GhFetch(response.RateExhausted ? GhError.RateLimited : GhError.Unexpected, response.RetryAt, null);
        }

        if (response.Status != 200)
        {
            return new GhFetch(GhError.Unexpected, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            var data = doc.RootElement.Obj("data");
            if (data.ValueKind != JsonValueKind.Object)
            {
                return new GhFetch(GhError.Unexpected, null, null);
            }

            return new GhFetch(GhError.None, null, CodingChecks.Parse(data, slug, pushed));
        }
        catch (JsonException)
        {
            return new GhFetch(GhError.Unexpected, null, null);
        }
    }

    private void ApplyGitHub(RepoRef repo, GhSlug slug, string branch, GhFetch? fetch)
    {
        _ghBusy = false;
        var now = DateTime.UtcNow;
        if (!_on || !SameRoot(_active, repo) || !Equals(_slug, slug) || _git?.Branch != branch)
        {
            // The answer is for a repository or branch you have left; the next tick asks about the current one.
            _nextGh = now;
            return;
        }

        if (fetch is not { Remote: { } remote } || fetch.Error != GhError.None)
        {
            var error = fetch?.Error ?? GhError.Unexpected;
            _ghError = error;
            var wait = error switch
            {
                GhError.RateLimited => TimeSpan.FromMinutes(5),
                GhError.SignedOut or GhError.TokenRejected => TimeSpan.FromMinutes(10),
                _ => TimeSpan.FromMinutes(2),
            };
            if (error == GhError.RateLimited && fetch?.RetryAt is { } retry && retry - now > wait)
            {
                wait = retry - now;
            }

            _nextGh = now + wait;
            Publish();
            return;
        }

        _ghError = GhError.None;
        _ghAt = now;
        _ghFound = remote.Found;
        _ghRemaining = remote.Remaining;
        _ghDefault = remote.DefaultBranch ?? _ghDefault;
        _pull = remote.Pull;
        var key = remote.ChecksSha is null ? null : slug + "@" + remote.ChecksSha;
        if (remote.Checks is { } checks && key is not null)
        {
            if (CodingChecks.Finished(_checks, checks, key == _checksKey) && _ctx.App.Get<bool>(CodingKeys.Alerts))
            {
                AnnounceFinished(checks, branch);
            }

            _checks = checks;
            _checksKey = key;
        }
        else
        {
            _checks = null;
            _checksKey = null;
        }

        var delay = now < _burstUntil ? TimeSpan.FromSeconds(15) : _checks is { State: CheckState.Running } ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(120);
        if (_ghRemaining < 200 && delay < TimeSpan.FromMinutes(5))
        {
            delay = TimeSpan.FromMinutes(5);
        }

        _nextGh = now + delay;
        Publish();
    }

    private void AnnounceFinished(CheckSummary checks, string branch)
    {
        var passed = checks.State != CheckState.Failing;
        var repo = _active?.Name ?? "";
        _runtime.Queue(new GitHubNotice(
            passed ? "" : "",
            passed ? CodingText.ChecksPassed : GhText.NoticeChecks,
            repo + " · " + branch,
            passed ? GitHubLook.Green : GitHubLook.Red));
    }

    // ---------------------------------------------------------------- publishing

    private void Publish()
    {
        var snapshot = _active is null
            ? CodingSnapshot.Empty
            : new CodingSnapshot(_active, _reason, _git, _gitError, _slug, _ghDefault ?? _gitDefault, _pull, _checks, _gitAt, _ghAt, _ghError, _ghFound);
        Snapshot = snapshot;
        var sig = Signature(snapshot);
        var shape = Shape(snapshot);
        if (sig != _sig)
        {
            _sig = sig;
            _changed?.Invoke();
        }

        if (shape != _shape)
        {
            _shape = shape;
            (IslandContext ?? _runtime.IslandContext)?.Invalidate();
        }
    }

    /// <summary>Everything the card shows, so a refresh that found nothing new does not redraw it.</summary>
    internal static string Signature(CodingSnapshot s)
    {
        var g = s.Git;
        var c = s.Checks;
        return string.Join('|',
            s.Repo?.Root, s.Reason, s.GitError, s.Slug, s.DefaultBranch, s.GitHubError, s.GitHubFound,
            g?.Branch, g?.Detached, g?.HeadSha, g?.Upstream, g?.Ahead, g?.Behind, g?.Staged, g?.Unstaged, g?.Untracked, g?.Conflicts, g?.UntrackedUnknown, g?.Both,
            s.Pull?.Number, s.Pull?.Title, s.Pull?.IsDraft, s.Pull?.ReviewDecision,
            c?.State, c?.Total, c?.Passed, c?.Failed, c?.Running, c?.Skipped, c is null ? "" : string.Join(',', c.Failing), c?.Url);
    }

    /// <summary>What decides the width of the strip: the repository, the branch, the number of digits and which parts exist.</summary>
    internal static string Shape(CodingSnapshot s)
    {
        if (s.Repo is null || s.Git is not { } g)
        {
            return "";
        }

        return string.Join('|', s.Repo.Root, s.BranchLabel, Digits(g.Changes), Digits(g.Ahead), Digits(g.Behind), s.Checks is { State: not CheckState.None });
    }

    private static int Digits(int n) => n <= 0 ? 0 : n.ToString(CultureInfo.InvariantCulture).Length;

    // ---------------------------------------------------------------- developer aids

    /// <summary>Writes the state to the log without any name, path or title.</summary>
    internal void LogState()
    {
        var s = Snapshot;
        var g = s.Git;
        _ctx.Log.Info($"[github] coding: on={_on} active={s.Repo is not null} reason={s.Reason} gitError={s.GitError} git={g is not null} detached={g?.Detached} changes={g?.Changes} ahead={g?.Ahead} behind={g?.Behind} upstream={g?.Upstream is not null} slug={s.Slug is not null} gitHubError={s.GitHubError} found={s.GitHubFound} pull={s.Pull is not null} checks={s.Checks?.State} total={s.Checks?.Total} running={s.Checks?.Running} failed={s.Checks?.Failed} remaining={(_ghRemaining == int.MaxValue ? "?" : _ghRemaining.ToString(CultureInfo.InvariantCulture))} listeners={_listeners}");
    }

    /// <summary>Queues the "checks passed" notice for the repository in front, without asking anything.</summary>
    internal void SimulateFinished()
    {
        _runtime.Queue(new GitHubNotice("", CodingText.ChecksPassed, (Snapshot.Repo?.Name ?? "example") + " · " + (Snapshot.BranchLabel ?? "main"), GitHubLook.Green));
    }
}
