// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

internal enum UpdateSource
{
    Package,
    Store,
    Online,
}

/// <summary>One row of the update list.</summary>
internal sealed record UpdateItem(string Id, UpdateSource Source, string Name, string Installed, string Latest, string RuleId, string? Token, string? OpenPath, string? IconPath, string? StoreProductId)
{
    public bool Selectable => Source != UpdateSource.Online;

    public string VersionSummary => $"{Installed} → {Latest}";
}

/// <summary>An exception rule: version null = never check the app; otherwise skip that release.</summary>
internal sealed class UpdateRule
{
    [JsonPropertyName("bundleID")]
    public string BundleId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    public bool Hides(UpdateItem item) =>
        BundleId.Equals(item.RuleId, StringComparison.OrdinalIgnoreCase)
        && (Version is null || VersionCompare.Compare(VersionCompare.Core(Version), VersionCompare.Core(item.Latest)) == 0);
}

/// <summary>The hardened HTTPS text loader used for publisher feeds (spec 7.3.4/7.3.6).</summary>
internal static class FeedLoader
{
    private const int ByteLimit = 2 * 1024 * 1024;

    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false, UseProxy = true })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public static bool IsPublicUrl(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || !uri.IsDefaultPort)
        {
            return false;
        }

        var host = uri.IdnHost;
        return host.Contains('.') && host.Any(char.IsLetter) && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) && !IPAddress.TryParse(host, out _) && host.All(c => c < 128);
    }

    /// <summary>Returns (status, text). Status 404/410 means the feed is absent; 0 means it could not be loaded.</summary>
    public static async Task<(int Status, string? Text)> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overall.CancelAfter(TimeSpan.FromSeconds(20));
            var current = new Uri(url);
            for (var hop = 0; hop <= 5; hop++)
            {
                if (!IsPublicUrl(current))
                {
                    return (0, null);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, overall.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    return ((int)response.StatusCode, null);
                }

                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > ByteLimit)
                {
                    return (0, null);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(overall.Token).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var chunk = new byte[16384];
                int n;
                while ((n = await stream.ReadAsync(chunk, overall.Token).ConfigureAwait(false)) > 0)
                {
                    buffer.Write(chunk, 0, n);
                    if (buffer.Length > ByteLimit)
                    {
                        return (0, null);
                    }
                }

                return (200, Encoding.UTF8.GetString(buffer.ToArray()));
            }
        }
        catch
        {
            // network problem: unchecked
        }

        return (0, null);
    }
}

/// <summary>The update checker: sources, rules, schedule, notifications (spec 7.3).</summary>
internal sealed class AppUpdatesService
{
    private const int CatchUpSeconds = 180;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private int _generation;
    private bool _pendingAutomatic;
    private bool _handoffPending;
    private System.Threading.Timer? _timer;
    private bool _wakeHooked;
    private bool _checkAgainAfterScan;
    private DateTime? _nextCheck;

    public AppUpdatesService()
    {
        WingetOperations.Shared.Finished += OnOperationFinished;
    }

    public static AppUpdatesService Shared { get; } = new();

    public event Action? Changed;

    public List<UpdateItem> Items { get; private set; } = [];

    public HashSet<string> SelectedIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<UpdateRule> Rules { get; private set; } = [];

    public bool IsChecking { get; private set; }

    public bool HasCheckedThisSession { get; private set; }

    public bool PackageManagerAvailable { get; private set; } = true;

    public bool StoreComplete { get; private set; } = true;

    public bool OnlineAvailable { get; private set; } = true;

    public List<string> UncheckedAppNames { get; private set; } = [];

    public List<string> UncomparableAppNames { get; private set; } = [];

    public string? LastError { get; private set; }

    public DateTime? NextCheck => _nextCheck;

    public bool IsBusy => IsChecking || WingetOperations.Shared.IsBusy;

    public DateTime? LastCheck
    {
        get
        {
            var epoch = Host.App.Get<double>("appUpdatesLastCheck");
            return epoch > 0 ? Fmt.FromEpoch(epoch) : null;
        }
    }

    public bool IncludeWinget => Host.App.Get<bool>("appUpdatesIncludeHomebrewApps");

    public bool IncludeStore => Host.App.Get<bool>("appUpdatesIncludeAppStore");

    public bool IncludeOnline => Host.App.Get<bool>("appUpdatesIncludeOnlineCatalog");

    public string Frequency => Host.App.Get<string>("appUpdatesCheckFrequency") switch { "daily" => "daily", "weekly" => "weekly", _ => "off" };

    public bool CoverageIncomplete => HasCheckedThisSession && !IsChecking && ((IncludeStore && !StoreComplete) || (IncludeOnline && !OnlineAvailable));

    public bool VersionsUncompared => IncludeOnline && HasCheckedThisSession && !IsChecking && UncomparableAppNames.Count > 0;

    private void Raise() => V.Post(() => Changed?.Invoke());

    // ------------------------------------------------------------------ rules

    public void ReloadRules()
    {
        try
        {
            var json = Host.App.Get<string>("appUpdatesRules");
            var rules = JsonSerializer.Deserialize<List<UpdateRule>>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? [];
            Rules = rules
                .Where(r => !string.IsNullOrWhiteSpace(r.BundleId) && !string.IsNullOrWhiteSpace(r.Name) && !r.BundleId.Any(char.IsWhiteSpace) && !r.BundleId.Contains('/'))
                .GroupBy(r => r.BundleId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }
        catch
        {
            Rules = [];
        }
    }

    private void SaveRules()
    {
        Host.App.Set("appUpdatesRules", JsonSerializer.Serialize(Rules));
    }

    public void SkipVersion(UpdateItem item)
    {
        if (IsChecking || VersionCompare.IsUncomparable(item.Latest))
        {
            return;
        }

        Rules.RemoveAll(r => r.BundleId.Equals(item.RuleId, StringComparison.OrdinalIgnoreCase));
        Rules.Add(new UpdateRule { BundleId = item.RuleId, Name = item.Name, Version = VersionCompare.Core(item.Latest) });
        SaveRules();
        Apply();
    }

    public void ExcludeApp(UpdateItem item) => Exclude(item.RuleId, item.Name);

    public void Exclude(string ruleId, string name)
    {
        if (IsChecking || string.IsNullOrWhiteSpace(ruleId))
        {
            return;
        }

        Rules.RemoveAll(r => r.BundleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
        Rules.Add(new UpdateRule { BundleId = ruleId, Name = name, Version = null });
        SaveRules();
        Apply();
    }

    public void RemoveRule(UpdateRule rule)
    {
        if (IsChecking)
        {
            return;
        }

        Rules.Remove(rule);
        SaveRules();
        if (rule.Version is null)
        {
            HasCheckedThisSession = false;
        }

        Apply();
    }

    // ------------------------------------------------------------------ selection

    private List<UpdateItem> _all = [];

    private void Apply()
    {
        Items = _all.Where(i => !Rules.Any(r => r.Hides(i))).ToList();
        SelectedIds.IntersectWith(Items.Where(i => i.Selectable).Select(i => i.Id));
        Raise();
    }

    public void SelectAll()
    {
        foreach (var item in Items.Where(i => i.Selectable))
        {
            SelectedIds.Add(item.Id);
        }

        Raise();
    }

    public void SelectNone()
    {
        SelectedIds.Clear();
        Raise();
    }

    public void Toggle(UpdateItem item)
    {
        if (!item.Selectable)
        {
            return;
        }

        if (!SelectedIds.Add(item.Id))
        {
            SelectedIds.Remove(item.Id);
        }

        Raise();
    }

    // ------------------------------------------------------------------ check

    public void CheckIfNeeded()
    {
        if (IsChecking)
        {
            return;
        }

        var last = LastCheck;
        if (_handoffPending || !HasCheckedThisSession && last is null || !HasCheckedThisSession || last is null || DateTime.Now - last > StaleAfter)
        {
            _ = CheckAsync(false);
        }
    }

    /// <summary>Called when the app became active again after a hand-off to a store / updater.</summary>
    public void ApplicationBecameActive()
    {
        if (_handoffPending && !IsChecking)
        {
            _handoffPending = false;
            _ = CheckAsync(false);
        }
    }

    public void SourceSelectionDidChange()
    {
        if (IsChecking)
        {
            _checkAgainAfterScan = true;
            _generation++;
        }
        else
        {
            _ = CheckAsync(false);
        }
    }

    public async Task CheckAsync(bool automatic)
    {
        if (!Host.Runtime.IsAvailable("app-updates"))
        {
            return;
        }

        if (IsChecking)
        {
            _pendingAutomatic |= automatic;
            return;
        }

        ReloadRules();
        var generation = ++_generation;
        IsChecking = true;
        LastError = null;
        Raise();

        var includeWinget = IncludeWinget;
        var includeStore = IncludeStore;
        var includeOnline = IncludeOnline;
        var items = new List<UpdateItem>();
        var uncheckedNames = new List<string>();
        var uncomparable = new List<string>();
        var packageAvailable = WingetCli.IsAvailable;
        var storeComplete = true;
        var onlineAvailable = true;
        try
        {
            var swatch = System.Diagnostics.Stopwatch.StartNew();
            var programs = await Task.Run(() => ProgramCatalog.Enumerate()).ConfigureAwait(true);
            Host.Log($"app-updates: programs {programs.Count} in {swatch.ElapsedMilliseconds} ms");
            var wingetCovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (packageAvailable && (includeWinget || includeStore || includeOnline))
            {
                var (result, rows) = await WingetCli.UpgradesAsync(includeOnline).ConfigureAwait(true);
                if (!result.Ok && rows.Count == 0 && result.Output.Length > 0 && !result.Output.Contains("No installed package found", StringComparison.OrdinalIgnoreCase)
                    && !result.Output.Contains("No applicable upgrade", StringComparison.OrdinalIgnoreCase))
                {
                    LastError = WingetCli.NeedsSourceAgreement(result.Output)
                        ? "winget needs your confirmation before using its sources. Open Packages to accept the source terms."
                        : WingetOperations.VisibleError(result.Output);
                    if (includeStore)
                    {
                        storeComplete = false;
                    }
                }

                foreach (var row in rows)
                {
                    if (row.IsOther || !row.HasUpdate)
                    {
                        continue;
                    }

                    var program = MatchProgram(programs, row);
                    wingetCovered.Add(Norm.Of(row.Name));
                    var installed = program is { Version.Length: > 0 } ? program.Version : row.Version;
                    if (row.Source.Equals("msstore", StringComparison.OrdinalIgnoreCase))
                    {
                        if (includeStore && !VersionCompare.IsUncomparable(row.Available))
                        {
                            items.Add(new UpdateItem($"appStore:{row.Id}", UpdateSource.Store, row.Name, installed, VersionCompare.Core(row.Available), row.Id, row.Id, null, program?.DisplayIcon, row.Id));
                        }

                        continue;
                    }

                    if (VersionCompare.IsUncomparable(row.Version))
                    {
                        // winget cannot tell what is installed: other installed apps (online source)
                        if (includeOnline && program is not null)
                        {
                            if (VersionCompare.IsUncomparable(program.Version))
                            {
                                uncomparable.Add(program.Name);
                            }
                            else if (VersionCompare.IsNewer(row.Available, program.Version))
                            {
                                items.Add(new UpdateItem($"online:{row.Id}", UpdateSource.Online, program.Name, program.Version, VersionCompare.Core(row.Available), row.Id, null, program.MainExecutable, program.DisplayIcon, null));
                            }
                        }
                        else if (includeOnline)
                        {
                            uncomparable.Add(row.Name);
                        }

                        continue;
                    }

                    if (!includeWinget || VersionCompare.IsUncomparable(row.Available))
                    {
                        continue;
                    }

                    if (VersionCompare.IsUncomparable(installed) || !VersionCompare.IsNewer(row.Available, installed))
                    {
                        continue;
                    }

                    items.Add(new UpdateItem($"packageManager:{row.Id}", UpdateSource.Package, program?.Name ?? row.Name, installed, VersionCompare.Core(row.Available), row.Id, row.Id, program?.MainExecutable, program?.DisplayIcon ?? program?.MainExecutable, null));
                }
            }

            Host.Log($"app-updates: winget done at {swatch.ElapsedMilliseconds} ms, items {items.Count}");
            if (includeOnline)
            {
                var feeds = await CheckElectronFeeds(programs, items, wingetCovered, uncheckedNames).ConfigureAwait(true);
                onlineAvailable = feeds;
            }

            Host.Log($"app-updates: feeds done at {swatch.ElapsedMilliseconds} ms");
            if (generation != _generation)
            {
                // a source switch happened while scanning: discard
                IsChecking = false;
                if (_checkAgainAfterScan)
                {
                    _checkAgainAfterScan = false;
                    _ = CheckAsync(false);
                }

                return;
            }

            _all = items
                .GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(i => (int)i.Source).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var previous = new HashSet<string>(Items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
            var visible = _all.Where(i => !Rules.Any(r => r.Hides(i))).ToList();
            foreach (var item in visible.Where(i => i.Selectable && !previous.Contains(i.Id)))
            {
                SelectedIds.Add(item.Id);
            }

            PackageManagerAvailable = packageAvailable;
            StoreComplete = storeComplete;
            OnlineAvailable = onlineAvailable;
            UncheckedAppNames = uncheckedNames.Distinct().ToList();
            UncomparableAppNames = uncomparable.Distinct().ToList();
            HasCheckedThisSession = true;
            Host.App.Set("appUpdatesLastCheck", Fmt.Epoch());
            Host.App.Set("appUpdatesLastCount", visible.Count);
            IsChecking = false;
            Apply();
            if (automatic || _pendingAutomatic)
            {
                _pendingAutomatic = false;
                Announce(visible);
                ScheduleNext();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            IsChecking = false;
            Raise();
            Host.Log("App updates check failed", ex);
        }
    }

    private static InstalledProgram? MatchProgram(List<InstalledProgram> programs, PackageEntry row)
    {
        var name = Norm.Of(row.Name);
        var stripped = Norm.Stripped(row.Name);
        var idTail = Norm.Of(row.Id.Split('.').LastOrDefault());
        return programs.FirstOrDefault(p => !p.IsAppx && (Norm.Of(p.Name) == name || Norm.Stripped(p.Name) == stripped))
            ?? programs.FirstOrDefault(p => Norm.Of(p.Name) == name || Norm.Stripped(p.Name) == stripped || p.PackageFamilyName.StartsWith(row.Id, StringComparison.OrdinalIgnoreCase))
            ?? programs.FirstOrDefault(p => idTail.Length >= 4 && Norm.Of(p.Name) == idTail);
    }

    /// <summary>Electron apps declare their feed in resources\app-update.yml; the manifest is latest.yml.</summary>
    private static async Task<bool> CheckElectronFeeds(List<InstalledProgram> programs, List<UpdateItem> items, HashSet<string> covered, List<string> unchecked_)
    {
        var candidates = new List<(InstalledProgram Program, string Url)>();
        foreach (var program in programs.Where(p => !p.IsAppx && p.InstallLocation.Length > 0 && !covered.Contains(Norm.Of(p.Name)) && !VersionCompare.IsUncomparable(p.Version)))
        {
            try
            {
                var yml = Path.Combine(program.InstallLocation, "resources", "app-update.yml");
                if (!File.Exists(yml) || new FileInfo(yml).Length > 64 * 1024)
                {
                    continue;
                }

                var url = FeedUrl(File.ReadAllText(yml));
                if (url is not null)
                {
                    candidates.Add((program, url));
                }
            }
            catch
            {
                // unreadable folder
            }
        }

        if (candidates.Count == 0)
        {
            return true;
        }

        var allOk = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var gate = new SemaphoreSlim(4);
        var tasks = candidates.Select(async candidate =>
        {
            await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                var (status, text) = await FeedLoader.GetAsync(candidate.Url, deadline.Token).ConfigureAwait(false);
                if (status == 200 && text is not null)
                {
                    var version = Regex.Match(text, @"^version:\s*['""]?([0-9][0-9A-Za-z.\-+]*)", RegexOptions.Multiline).Groups[1].Value;
                    if (version.Length > 0 && Regex.IsMatch(version, @"^\d+(\.\d+)*$") && VersionCompare.IsNewer(version, candidate.Program.Version))
                    {
                        lock (items)
                        {
                            items.Add(new UpdateItem($"online:{candidate.Program.Key}", UpdateSource.Online, candidate.Program.Name, candidate.Program.Version, version, candidate.Program.Key, null, candidate.Program.MainExecutable, candidate.Program.DisplayIcon, null));
                        }
                    }
                }
                else if (status == 0)
                {
                    allOk = false;
                    lock (unchecked_)
                    {
                        unchecked_.Add(candidate.Program.Name);
                    }
                }
            }
            catch
            {
                allOk = false;
                lock (unchecked_)
                {
                    unchecked_.Add(candidate.Program.Name);
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            allOk = false;
        }

        return allOk;
    }

    private static string? FeedUrl(string yml)
    {
        string Value(string key)
        {
            var m = Regex.Match(yml, $@"^{key}:\s*['""]?([^'""\r\n#]+)", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
        }

        if (Value("private").Equals("true", StringComparison.OrdinalIgnoreCase) || Value("token").Length > 0 || yml.Contains("requestHeaders") || Value("channel") is { Length: > 0 } and not "latest")
        {
            return null;
        }

        var provider = Value("provider");
        if (provider == "generic" && Value("url") is { Length: > 0 } baseUrl)
        {
            return baseUrl.TrimEnd('/') + "/latest.yml";
        }

        if (provider == "github" && Value("owner") is { Length: > 0 } owner && Value("repo") is { Length: > 0 } repo
            && Regex.IsMatch(owner, @"^[A-Za-z0-9_.-]+$") && Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]+$"))
        {
            return $"https://github.com/{owner}/{repo}/releases/latest/download/latest.yml";
        }

        return null;
    }

    // ------------------------------------------------------------------ updating

    public void UpdateSelected()
    {
        if (IsBusy)
        {
            return;
        }

        var selected = Items.Where(i => SelectedIds.Contains(i.Id) && i.Selectable).ToList();
        var packages = selected.Where(i => i.Source == UpdateSource.Package && i.Token is not null && WingetCli.ValidToken(i.Token)).ToList();
        var store = selected.Where(i => i.Source == UpdateSource.Store).ToList();
        if (packages.Count > 0)
        {
            var commands = packages.Select(p => WingetOperations.UpgradeArgs(p.Token!, Loc.T("winget"))).ToList();
            var target = packages.Count == 1 ? packages[0].Name : "packages";
            var action = packages.Count == 1 ? PkgAction.Upgrade : PkgAction.UpgradeMany;
            WingetOperations.Shared.Start(new OperationRequest(action, target, packages.Count == 1 ? packages[0].Token : null, commands));
        }

        if (store.Count == 1)
        {
            OpenStore(store[0]);
        }
        else if (store.Count > 1)
        {
            OpenStoreUpdates();
        }
    }

    public void Update(UpdateItem item)
    {
        if (IsBusy)
        {
            return;
        }

        switch (item.Source)
        {
            case UpdateSource.Package when item.Token is not null && WingetCli.ValidToken(item.Token):
                WingetOperations.Shared.Start(new OperationRequest(PkgAction.Upgrade, item.Name, item.Token, [WingetOperations.UpgradeArgs(item.Token, Loc.T("winget"))]));
                break;
            case UpdateSource.Store:
                OpenStore(item);
                break;
            case UpdateSource.Online:
                HandOff(item);
                break;
        }
    }

    private void OpenStore(UpdateItem item)
    {
        _handoffPending = true;
        UiKit_OpenUrl(!string.IsNullOrEmpty(item.StoreProductId) && Regex.IsMatch(item.StoreProductId, "^[A-Za-z0-9]{8,16}$")
            ? $"ms-windows-store://pdp/?ProductId={item.StoreProductId}"
            : "ms-windows-store://downloadsandupdates");
    }

    public void OpenStoreUpdates()
    {
        _handoffPending = true;
        UiKit_OpenUrl("ms-windows-store://downloadsandupdates");
    }

    private void HandOff(UpdateItem item)
    {
        if (item.OpenPath is { Length: > 0 } path && File.Exists(path))
        {
            _handoffPending = true;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Raise();
            }
        }
    }

    private static void UiKit_OpenUrl(string url) => WinKnife.Core.Ui.UiKit.OpenUrl(url);

    private void OnOperationFinished(OperationRequest request, OpState state)
    {
        if (request.Action is PkgAction.Upgrade or PkgAction.UpgradeMany or PkgAction.UpgradeAll && state == OpState.Succeeded && Host.IsAttached)
        {
            _ = CheckAsync(false);
        }
        else
        {
            Raise();
        }
    }

    // ------------------------------------------------------------------ schedule + notifications

    private void Announce(List<UpdateItem> visible)
    {
        try
        {
            var current = visible.Select(i => i.Id).ToList();
            var notified = Host.App.Get<List<string>>("appUpdatesNotifiedIDs") ?? [];
            var kept = notified.Where(current.Contains).ToList();
            var fresh = current.Where(id => !kept.Contains(id)).ToList();
            if (Host.App.Get<bool>("appUpdatesNotify") && fresh.Count > 0)
            {
                Host.Notify(Loc.T("App updates"), current.Count == 1 ? Loc.T("One app has a newer version.") : Loc.F("{0} apps have a newer version.", current.Count));
                kept.AddRange(fresh);
            }

            Host.App.Set("appUpdatesNotifiedIDs", kept);
        }
        catch
        {
            // keep going
        }
    }

    public static DateTime? NextCheckDate(DateTime? lastCheck, string frequency, DateTime now)
    {
        if (frequency == "off")
        {
            return null;
        }

        var interval = frequency == "weekly" ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
        if (lastCheck is null)
        {
            return now.AddSeconds(CatchUpSeconds);
        }

        var due = lastCheck.Value + interval;
        return due <= now ? now.AddSeconds(CatchUpSeconds) : due;
    }

    public void SyncWithPreferences()
    {
        _timer?.Dispose();
        _timer = null;
        if (!Host.IsAttached || Frequency == "off" || !Host.Runtime.IsAvailable("app-updates"))
        {
            _nextCheck = null;
            UnhookWake();
            Raise();
            return;
        }

        HookWake();
        ScheduleNext();
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        UnhookWake();
    }

    private void ScheduleNext()
    {
        _timer?.Dispose();
        var next = NextCheckDate(LastCheck, Frequency, DateTime.Now);
        _nextCheck = next;
        Raise();
        if (next is null)
        {
            return;
        }

        var due = next.Value - DateTime.Now;
        if (due < TimeSpan.Zero)
        {
            due = TimeSpan.FromSeconds(5);
        }

        if (due > TimeSpan.FromDays(40))
        {
            due = TimeSpan.FromDays(40);
        }

        _timer = new System.Threading.Timer(_ => Application.Current?.Dispatcher.BeginInvoke(() => _ = CheckAsync(true)), null, due, Timeout.InfiniteTimeSpan);
    }

    private void HookWake()
    {
        if (_wakeHooked)
        {
            return;
        }

        _wakeHooked = true;
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.TimeChanged += OnTime;
    }

    private void UnhookWake()
    {
        if (!_wakeHooked)
        {
            return;
        }

        _wakeHooked = false;
        SystemEvents.PowerModeChanged -= OnPower;
        SystemEvents.TimeChanged -= OnTime;
    }

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Application.Current?.Dispatcher.BeginInvoke(ScheduleNext);
        }
    }

    private void OnTime(object? sender, EventArgs e) => Application.Current?.Dispatcher.BeginInvoke(ScheduleNext);
}
