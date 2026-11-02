// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.Maintenance;

internal enum WaPhase
{
    Idle,
    Scanning,
    Results,
    Cleaning,
    Done,
    Failed,
}

internal enum WaAccess
{
    Unknown,
    Available,
    Denied,
}

/// <summary>One confirmed WhatsApp download.</summary>
internal sealed class WaCandidate
{
    public required string Id { get; init; }

    public required string Path { get; init; }

    public long Size { get; init; }

    public DateTime DownloadedAt { get; init; }

    public DateTime ModifiedAt { get; init; }

    public WaCategory Category { get; init; }

    public required string ManagedRoot { get; init; }

    /// <summary>Filed away by the organizer: manual review lists it, the automation never touches it.</summary>
    public bool Organized { get; init; }

    public bool EligibleForRules { get; init; }

    public bool EligibleForAutomaticCleanup { get; init; }

    public bool Excluded { get; set; }

    public bool Include { get; set; }

    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>The per-pass snapshot of the cleanup settings.</summary>
internal sealed record WaSettingsSnapshot(int RetentionDays, HashSet<WaCategory> Categories, bool IncludeExisting, DateTime? AutomaticStartDate)
{
    public static WaSettingsSnapshot Read()
    {
        var app = Host.App;
        var start = app.Get<double>("whatsAppDownloadsAutomaticStartDate");
        return new WaSettingsSnapshot(
            WaRules.SanitizedRetentionDays(app.Get<int>("whatsAppDownloadsRetentionDays")),
            WaRules.DecodedCategories(app.Get<string>("whatsAppDownloadsCategories")),
            app.Get<bool>("whatsAppDownloadsIncludeExisting"),
            start > 0 ? Fmt.FromEpoch(start) : null);
    }
}

/// <summary>
/// Scans only the top level of Downloads, surfaces the files Windows itself attributes to WhatsApp and moves the reviewed or
/// eligible ones to the Recycle Bin (Vorssaint's <c>WhatsAppDownloadManager</c>). It never reads file contents and never
/// reaches into WhatsApp's packaged store.
/// </summary>
internal sealed class WaManager
{
    private Guid _token = Guid.NewGuid();

    public static WaManager Shared { get; } = new();

    /// <summary>Raised after every state change (marshalled to the UI thread).</summary>
    public event Action? Changed;

    /// <summary>Raised when only the checkbox selection changed.</summary>
    public event Action? SelectionChanged;

    public WaPhase Phase { get; private set; } = WaPhase.Idle;

    public WaAccess Access { get; private set; } = WaAccess.Unknown;

    public List<WaCandidate> Candidates { get; private set; } = [];

    public bool ReviewVisible { get; private set; }

    public int DoneMoved { get; private set; }

    public long DoneBytes { get; private set; }

    public int DoneFailed { get; private set; }

    public int SelectedCount => Candidates.Count(c => c.Include);

    public long SelectedBytes => Candidates.Where(c => c.Include).Sum(c => c.Size);

    public int EligibleCount => Candidates.Count(c => c.EligibleForRules && !c.Excluded);

    public long TotalBytes => Candidates.Sum(c => c.Size);

    public string DownloadsPath => WaFiles.DownloadsPath();

    public void SetReviewVisible(bool visible) => ReviewVisible = visible;

    public void Reset()
    {
        _token = Guid.NewGuid();
        Candidates = [];
        Phase = WaPhase.Idle;
        Raise();
    }

    private void Raise() => V.Post(() => Changed?.Invoke());

    private static HashSet<string> Exclusions() => [.. Host.App.Get<List<string>>("whatsAppDownloadsExclusions") ?? []];

    private static void SaveExclusions(IEnumerable<string> ids) => Host.App.Set("whatsAppDownloadsExclusions", ids.OrderBy(i => i, StringComparer.Ordinal).ToList());

    public void Scan()
    {
        if (Phase is WaPhase.Scanning or WaPhase.Cleaning)
        {
            return;
        }

        var root = DownloadsPath;
        var token = Guid.NewGuid();
        _token = token;
        Candidates = [];
        Phase = WaPhase.Scanning;
        Raise();

        var settings = WaSnapshot();
        var excluded = Exclusions();
        Task.Run(() =>
        {
            List<WaCandidate>? found = null;
            try
            {
                found = Collect(root, settings, excluded, DateTime.Now);
            }
            catch
            {
                // reported as a failed scan
            }

            V.Post(() =>
            {
                if (_token != token)
                {
                    return;
                }

                if (found is null)
                {
                    Host.App.Set("whatsAppDownloadsAccessConfirmed", false);
                    Access = WaAccess.Denied;
                    Candidates = [];
                    Phase = WaPhase.Failed;
                }
                else
                {
                    Host.App.Set("whatsAppDownloadsAccessConfirmed", true);
                    Access = WaAccess.Available;
                    Candidates = found;
                    Phase = WaPhase.Results;
                }

                Raise();
            });
        });
    }

    private static WaSettingsSnapshot WaSnapshot() => WaSettingsSnapshot.Read();

    /// <summary>The scan itself (also used by the test harness): top level of <paramref name="root"/> plus organized files.</summary>
    internal static List<WaCandidate> Collect(string root, WaSettingsSnapshot settings, HashSet<string> excluded, DateTime now)
    {
        var rootFull = Path.GetFullPath(root);
        var found = new List<WaCandidate>();
        foreach (var file in new DirectoryInfo(rootFull).EnumerateFiles())
        {
            if (Candidate(file.FullName, rootFull, false, settings, excluded, now) is { } c)
            {
                found.Add(c);
            }
        }

        foreach (var organized in WaOrganizer.ManagedDestinationPaths())
        {
            var folder = Path.GetDirectoryName(organized);
            if (folder is null || WaRules.IsDirectChild(organized, rootFull))
            {
                continue;
            }

            // Organized files stay reviewable by hand but are never trashed automatically: the person filed them away to
            // keep them, and the page only promises the automation watches the top level of Downloads.
            if (Candidate(organized, folder, true, settings, excluded, now) is { } c)
            {
                found.Add(c);
            }
        }

        found.Sort((a, b) => a.DownloadedAt != b.DownloadedAt
            ? b.DownloadedAt.CompareTo(a.DownloadedAt)
            : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return found;
    }

    /// <summary>Builds the candidate of one path, or null when it is not a confirmed, regular, visible file in its managed folder.</summary>
    internal static WaCandidate? Candidate(string path, string root, bool organized, WaSettingsSnapshot settings, HashSet<string> excluded, DateTime now)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!WaRules.IsDirectChild(full, root) || WaFiles.IsInsidePackageStore(full))
            {
                return null;
            }

            var info = new FileInfo(full);
            if (!WaFiles.IsUsableFile(info) || !WaFiles.IsConfirmedWhatsApp(full))
            {
                return null;
            }

            var fingerprint = WaFiles.Fingerprint(full);
            if (fingerprint is null)
            {
                return null;
            }

            var downloadedAt = info.CreationTime;
            var modifiedAt = info.LastWriteTime;
            var category = WaRules.CategoryOf(info.Extension);
            var rules = WaRules.IsEligibleForRules(category, downloadedAt, modifiedAt, now, settings.RetentionDays, settings.Categories);
            var automatic = !organized && WaRules.IsEligibleForAutomaticCleanup(category, downloadedAt, modifiedAt, now, settings.RetentionDays, settings.Categories, settings.IncludeExisting, settings.AutomaticStartDate);
            var isExcluded = excluded.Contains(fingerprint);
            return new WaCandidate
            {
                Id = fingerprint,
                Path = full,
                Size = info.Length,
                DownloadedAt = downloadedAt,
                ModifiedAt = modifiedAt,
                Category = category,
                ManagedRoot = Path.GetFullPath(root),
                Organized = organized,
                EligibleForRules = rules,
                EligibleForAutomaticCleanup = automatic,
                Excluded = isExcluded,
                Include = rules && !isExcluded,
            };
        }
        catch
        {
            return null;
        }
    }

    public void SetInclude(bool include, string id)
    {
        var candidate = Candidates.FirstOrDefault(c => c.Id == id);
        if (candidate is null || candidate.Excluded)
        {
            return;
        }

        candidate.Include = include;
        V.Post(() => SelectionChanged?.Invoke());
    }

    public void SelectRules()
    {
        foreach (var c in Candidates)
        {
            c.Include = c.EligibleForRules && !c.Excluded;
        }

        Raise();
    }

    public void SelectAutomaticRules()
    {
        foreach (var c in Candidates)
        {
            c.Include = c.EligibleForAutomaticCleanup && !c.Excluded;
        }
    }

    public void Exclude(string id)
    {
        var candidate = Candidates.FirstOrDefault(c => c.Id == id);
        if (candidate is null)
        {
            return;
        }

        var set = Exclusions();
        set.Add(id);
        SaveExclusions(set);
        candidate.Excluded = true;
        candidate.Include = false;
        Raise();
    }

    public void RemoveExclusion(string id)
    {
        var candidate = Candidates.FirstOrDefault(c => c.Id == id);
        if (candidate is null)
        {
            return;
        }

        var set = Exclusions();
        set.Remove(id);
        SaveExclusions(set);
        candidate.Excluded = false;
        candidate.Include = candidate.EligibleForRules;
        Raise();
    }

    public void Reveal(string id)
    {
        if (Candidates.FirstOrDefault(c => c.Id == id) is { } candidate)
        {
            FileOps.Reveal(candidate.Path);
        }
    }

    /// <summary>Moves the checked candidates to the Recycle Bin after re-validating each one.</summary>
    public void CleanSelected(bool automatic)
    {
        var chosen = Candidates.Where(c => c.Include).ToList();
        if (chosen.Count == 0)
        {
            return;
        }

        Phase = WaPhase.Cleaning;
        var token = Guid.NewGuid();
        _token = token;
        Raise();
        var settings = WaSnapshot();
        Task.Run(() =>
        {
            var movedIds = new HashSet<string>();
            long movedBytes = 0;
            var failed = 0;
            var excludedNow = Exclusions();
            foreach (var original in chosen)
            {
                // Same file, same place, still confirmed: the id is the volume serial plus file index.
                var current = Candidate(original.Path, original.ManagedRoot, original.Organized, settings, [], DateTime.Now);
                if (current is null || current.Id != original.Id)
                {
                    failed++;
                    continue;
                }

                if (automatic && (!current.EligibleForAutomaticCleanup || excludedNow.Contains(current.Id)))
                {
                    continue;
                }

                if (FileOps.HasReparseInChain(current.Path, current.ManagedRoot) || WaFiles.Fingerprint(current.Path) != original.Id)
                {
                    failed++;
                    continue;
                }

                if (WaRecycle.Trash(current.Path, current.Size) is not null)
                {
                    movedIds.Add(current.Id);
                    movedBytes += current.Size;
                }
                else
                {
                    failed++;
                }
            }

            V.Post(() =>
            {
                if (_token != token)
                {
                    return;
                }

                Candidates.RemoveAll(c => movedIds.Contains(c.Id));
                RecordCleanup(movedIds.Count, movedBytes, failed, automatic);
                DoneMoved = movedIds.Count;
                DoneBytes = movedBytes;
                DoneFailed = failed;
                Phase = WaPhase.Done;
                Raise();
            });
        });
    }

    /// <summary>A scheduled pass that found nothing is still proof the automation is alive.</summary>
    public void CompleteAutomaticWithoutCleaning()
    {
        RecordCleanup(0, 0, 0, true);
        DoneMoved = 0;
        DoneBytes = 0;
        DoneFailed = 0;
        Phase = WaPhase.Done;
        Raise();
    }

    private static void RecordCleanup(int moved, long bytes, int failed, bool automatic)
    {
        var app = Host.App;
        app.Set("whatsAppDownloadsLastCleanup", Fmt.Epoch());
        app.Set("whatsAppDownloadsLastCleanupCount", moved);
        app.Set("whatsAppDownloadsLastCleanupBytes", bytes);
        app.Set("whatsAppDownloadsLastCleanupFailed", failed);
        app.Set("whatsAppDownloadsLastCleanupAutomatic", automatic);
    }
}
