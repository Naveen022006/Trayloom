// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;

namespace WinKnife.Features.Maintenance;

internal enum WaOrganizerPhase
{
    Idle,
    Waiting,
    Organizing,
    Undoing,
    Done,
    Failed,
}

/// <summary>Digest record of an organized file (exact-duplicate detection).</summary>
internal sealed record WaRecord(string Digest, string DestinationPath, string OriginalName, long Size, DateTime OrganizedAt);

/// <summary>One reversible step of an organization pass: "move" puts a file back, "trash" recycles a copy again.</summary>
internal sealed record WaUndoAction(string Kind, string CurrentPath, string? RestorePath);

/// <summary>Everything one pass did, kept for seven days so it can be undone.</summary>
internal sealed record WaUndoTransaction(Guid Id, List<WaUndoAction> Actions, List<WaRecord> RecordsBefore, List<WaRecord> RecordsAfter, DateTime CreatedAt);

/// <summary>The inputs of one pass.</summary>
internal sealed record WaOrganizerSettings(string Destination, int DelayMinutes, HashSet<WaCategory> Categories, WaLayout Layout, WaDuplicateAction DuplicateAction);

/// <summary>What one pass did.</summary>
internal sealed record WaRunResult(int Moved, int Duplicates, int Failed, List<WaRecord> Records, WaUndoTransaction? Undo, DateTime? NextEligible);

/// <summary>
/// Experimental organizer for confirmed WhatsApp downloads (Vorssaint's <c>WhatsAppDownloadOrganizer</c>). It watches only the
/// top level of Downloads, waits until a file is stable, then moves it to the configured folder. File bytes are read only to
/// calculate a local SHA-256 digest used for exact duplicate detection.
/// </summary>
internal sealed class WaOrganizer
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _timer;
    private Guid _token = Guid.NewGuid();

    public static WaOrganizer Shared { get; } = new();

    public event Action? Changed;

    public WaOrganizerPhase Phase { get; private set; } = WaOrganizerPhase.Idle;

    public DateTime? NextCheck { get; private set; }

    public int DoneMoved { get; private set; }

    public int DoneDuplicates { get; private set; }

    public int DoneFailed { get; private set; }

    public bool IsBusy => Phase is WaOrganizerPhase.Organizing or WaOrganizerPhase.Undoing;

    public bool CanUndo => ValidUndoTransactions().Count > 0;

    private void Raise() => V.Post(() => Changed?.Invoke());

    private static string Downloads => WaFiles.DownloadsPath();

    /// <summary>The configured destination, or Downloads\WhatsApp (also when the configured one is Downloads itself).</summary>
    public static string DestinationPath(string? configured = null, string? downloads = null)
    {
        var root = Path.GetFullPath(downloads ?? Downloads);
        configured ??= Host.IsAttached ? Host.App.Get<string>("whatsAppOrganizerDestinationPath") : string.Empty;
        var fallback = Path.Combine(root, "WhatsApp");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallback;
        }

        try
        {
            var full = Path.GetFullPath(configured).TrimEnd('\\');
            return string.Equals(full, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? fallback : full;
        }
        catch
        {
            return fallback;
        }
    }

    public static HashSet<string> ManagedDestinationPaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in LoadRecords())
        {
            try
            {
                set.Add(Path.GetFullPath(record.DestinationPath));
            }
            catch
            {
                // malformed record
            }
        }

        return set;
    }

    // ------------------------------------------------------------------ lifecycle

    public void Sync()
    {
        StopMonitoring();
        if (!Host.IsAttached || !Host.Runtime.IsAvailable("cleaner") || !Host.App.Get<bool>("whatsAppDownloadsEnabled")
            || !Host.App.Get<bool>("whatsAppOrganizerEnabled") || !Host.App.Get<bool>("whatsAppDownloadsAccessConfirmed"))
        {
            Phase = WaOrganizerPhase.Idle;
            Raise();
            return;
        }

        StartMonitoring(Downloads);
        Schedule(TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        _token = Guid.NewGuid();
        StopMonitoring();
        Phase = WaOrganizerPhase.Idle;
        Raise();
    }

    public bool SetDestination(string? path)
    {
        var root = Path.GetFullPath(Downloads).TrimEnd('\\');
        if (path is not null)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path).TrimEnd('\\');
            }
            catch
            {
                return false;
            }

            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full))
            {
                return false;
            }

            Host.App.Set("whatsAppOrganizerDestinationPath", full);
        }
        else
        {
            Host.App.Set("whatsAppOrganizerDestinationPath", string.Empty);
        }

        Sync();
        return true;
    }

    public void RunNow() => Run(true);

    private void StartMonitoring(string root)
    {
        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.Attributes,
            };
            void Touch(object? s, EventArgs e) => V.Post(() => Schedule(TimeSpan.FromSeconds(2)));
            watcher.Created += Touch;
            watcher.Changed += Touch;
            watcher.Renamed += Touch;
            watcher.Deleted += Touch;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch
        {
            Schedule(TimeSpan.FromSeconds(300));
        }
    }

    private void StopMonitoring()
    {
        _timer?.Dispose();
        _timer = null;
        NextCheck = null;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private bool Armed => Host.IsAttached && Host.App.Get<bool>("whatsAppDownloadsEnabled") && Host.App.Get<bool>("whatsAppOrganizerEnabled");

    private void Schedule(TimeSpan delay)
    {
        if (!Armed)
        {
            return;
        }

        if (delay < TimeSpan.FromSeconds(1))
        {
            delay = TimeSpan.FromSeconds(1);
        }

        var date = DateTime.Now + delay;
        if (NextCheck is { } existing && existing <= date)
        {
            return;
        }

        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => V.Post(() => Run(false)), null, delay, Timeout.InfiniteTimeSpan);
        NextCheck = date;
        // A fresh Done stays visible as the just-finished feedback; everything else folds into the quiet waiting state.
        if (!IsBusy && Phase is not WaOrganizerPhase.Done)
        {
            Phase = WaOrganizerPhase.Waiting;
            Raise();
        }
    }

    private void ScheduleAt(DateTime? date)
    {
        if (date is null)
        {
            // Nothing is pending: a slow poll catches what the file watcher cannot (a watched folder that was replaced).
            Schedule(TimeSpan.FromMinutes(1));
            return;
        }

        Schedule(date.Value - DateTime.Now);
    }

    // ------------------------------------------------------------------ pass

    private void Run(bool manual)
    {
        _timer?.Dispose();
        _timer = null;
        NextCheck = null;
        // A pass already executing keeps its phase untouched: a second pass would race the first.
        if (IsBusy)
        {
            Schedule(TimeSpan.FromSeconds(60));
            return;
        }

        if (!Armed || !Host.Runtime.IsAvailable("cleaner"))
        {
            Phase = WaOrganizerPhase.Idle;
            Raise();
            return;
        }

        var manager = WaManager.Shared;
        if (manager.Phase is WaPhase.Scanning or WaPhase.Cleaning || (!manual && manager.ReviewVisible))
        {
            Schedule(TimeSpan.FromSeconds(300));
            return;
        }

        var settings = ReadSettings();
        var root = Downloads;
        var token = Guid.NewGuid();
        _token = token;
        Phase = WaOrganizerPhase.Organizing;
        Raise();
        Task.Run(() =>
        {
            var result = Organize(root, settings, DateTime.Now);
            V.Post(() =>
            {
                if (_token != token)
                {
                    return;
                }

                Finish(result, manual, manager);
            });
        });
    }

    private void Finish(WaRunResult result, bool manual, WaManager manager)
    {
        var touched = result.Moved + result.Duplicates + result.Failed > 0;
        if (touched || result.Undo is not null)
        {
            SaveRecords(result.Records);
        }

        if (result.Undo is not null)
        {
            var transactions = ValidUndoTransactions();
            transactions.Add(result.Undo);
            SaveUndoTransactions(transactions.TakeLast(20).ToList());
        }

        if (touched || manual)
        {
            var app = Host.App;
            app.Set("whatsAppOrganizerLastRun", Fmt.Epoch());
            app.Set("whatsAppOrganizerLastMoved", result.Moved);
            app.Set("whatsAppOrganizerLastDuplicates", result.Duplicates);
            app.Set("whatsAppOrganizerLastFailed", result.Failed);
            DoneMoved = result.Moved;
            DoneDuplicates = result.Duplicates;
            DoneFailed = result.Failed;
            Phase = WaOrganizerPhase.Done;
        }
        else
        {
            Phase = WaOrganizerPhase.Waiting;
        }

        if (result.Moved + result.Duplicates > 0 && Host.App.Get<bool>("whatsAppDownloadsNotify"))
        {
            Host.Notify(WaText.OrgNotificationTitle, WaText.OrgNotificationFormat(result.Moved, result.Duplicates, result.Failed));
        }

        Raise();
        if (manager.ReviewVisible && (touched || manual))
        {
            manager.Scan();
        }

        ScheduleAt(result.NextEligible);
    }

    private static WaOrganizerSettings ReadSettings()
    {
        var app = Host.App;
        return new WaOrganizerSettings(
            DestinationPath(),
            WaRules.SanitizedOrganizerDelayMinutes(app.Get<int>("whatsAppOrganizerDelayMinutes")),
            WaRules.DecodedCategories(app.Get<string>("whatsAppOrganizerCategories")),
            WaRules.ParseLayout(app.Get<string>("whatsAppOrganizerLayout")),
            WaRules.ParseDuplicateAction(app.Get<string>("whatsAppOrganizerDuplicateAction")));
    }

    private sealed record Source(string Path, string Fingerprint, long Size, DateTime DownloadedAt, DateTime ModifiedAt, WaCategory Category);

    /// <summary>One organization pass over the top level of <paramref name="root"/> (also driven directly by the test harness).</summary>
    internal static WaRunResult Organize(string root, WaOrganizerSettings settings, DateTime now)
    {
        var recordsBefore = LoadRecords();
        List<Source> files;
        try
        {
            files = SourceFiles(root, settings);
        }
        catch
        {
            return new WaRunResult(0, 0, 1, recordsBefore, null, null);
        }

        var records = recordsBefore.Where(r => File.Exists(r.DestinationPath)).ToList();
        var undoActions = new List<WaUndoAction>();
        var moved = 0;
        var duplicates = 0;
        var failed = 0;
        DateTime? nextEligible = null;

        foreach (var source in files.OrderBy(f => f.DownloadedAt))
        {
            if (!WaRules.IsStableForOrganization(source.DownloadedAt, source.ModifiedAt, now, settings.DelayMinutes))
            {
                var baseTime = source.DownloadedAt > source.ModifiedAt ? source.DownloadedAt : source.ModifiedAt;
                var eligible = baseTime.AddSeconds((settings.DelayMinutes * 60) + 1);
                nextEligible = nextEligible is null || eligible < nextEligible ? eligible : nextEligible;
                continue;
            }

            try
            {
                if (WaFiles.Fingerprint(source.Path) != source.Fingerprint)
                {
                    failed++;
                    continue;
                }

                var digest = WaFiles.Sha256(source.Path);
                var duplicateIndex = FirstValidRecordIndex(digest, records);
                if (!SourceStillMatches(source))
                {
                    failed++;
                    continue;
                }

                if (duplicateIndex is { } index)
                {
                    var existing = records[index].DestinationPath;
                    switch (settings.DuplicateAction)
                    {
                        case WaDuplicateAction.TrashNew:
                        {
                            var receipt = WaRecycle.Trash(source.Path, source.Size) ?? throw new IOException("recycle failed");
                            if (!string.IsNullOrEmpty(receipt.BinPath))
                            {
                                undoActions.Insert(0, new WaUndoAction("move", receipt.BinPath, source.Path));
                            }

                            duplicates++;
                            continue;
                        }

                        case WaDuplicateAction.ReplaceExisting:
                        {
                            var actions = ReplaceVerified(source.Path, existing, digest, source.Size);
                            records[index] = new WaRecord(digest, existing, Path.GetFileName(source.Path), source.Size, now.ToUniversalTime());
                            undoActions.InsertRange(0, actions);
                            moved++;
                            duplicates++;
                            continue;
                        }
                    }
                }

                var folder = WaRules.OrganizerRelativeComponents(settings.Layout, source.Category, source.DownloadedAt)
                    .Aggregate(settings.Destination, Path.Combine);
                Directory.CreateDirectory(folder);
                var destination = UniqueDestination(folder, Path.GetFileName(source.Path));
                var moves = MoveVerified(source.Path, destination, digest, source.Size);
                undoActions.InsertRange(0, moves);
                records.Add(new WaRecord(digest, destination, Path.GetFileName(source.Path), source.Size, now.ToUniversalTime()));
                moved++;
            }
            catch
            {
                failed++;
            }
        }

        records = [.. records.OrderBy(r => r.OrganizedAt).TakeLast(5_000)];
        var undo = undoActions.Count == 0
            ? null
            : new WaUndoTransaction(Guid.NewGuid(), undoActions, recordsBefore, records, now.ToUniversalTime());
        return new WaRunResult(moved, duplicates, failed, records, undo, nextEligible);
    }

    private static List<Source> SourceFiles(string root, WaOrganizerSettings settings)
    {
        var rootFull = Path.GetFullPath(root);
        var list = new List<Source>();
        foreach (var info in new DirectoryInfo(rootFull).EnumerateFiles())
        {
            // Cheap checks first: nothing reads a stream for a file whose type is switched off.
            if (!WaFiles.IsUsableFile(info) || !settings.Categories.Contains(WaRules.CategoryOf(info.Extension)) || !WaRules.IsDirectChild(info.FullName, rootFull))
            {
                continue;
            }

            if (!WaFiles.IsConfirmedWhatsApp(info.FullName) || WaFiles.Fingerprint(info.FullName) is not { } fingerprint)
            {
                continue;
            }

            list.Add(new Source(info.FullName, fingerprint, info.Length, info.CreationTime, info.LastWriteTime, WaRules.CategoryOf(info.Extension)));
        }

        return list;
    }

    private static int? FirstValidRecordIndex(string digest, List<WaRecord> records)
    {
        var i = 0;
        while (i < records.Count)
        {
            if (records[i].Digest != digest)
            {
                i++;
                continue;
            }

            string? current = null;
            try
            {
                current = File.Exists(records[i].DestinationPath) ? WaFiles.Sha256(records[i].DestinationPath) : null;
            }
            catch
            {
                // unreadable: treated as missing
            }

            if (current != digest)
            {
                records.RemoveAt(i);
                continue;
            }

            return i;
        }

        return null;
    }

    private static string UniqueDestination(string folder, string preferredName)
    {
        var preferred = Path.Combine(folder, preferredName);
        if (!File.Exists(preferred) && !Directory.Exists(preferred))
        {
            return preferred;
        }

        var ext = Path.GetExtension(preferredName);
        var stem = Path.GetFileNameWithoutExtension(preferredName);
        for (var counter = 2; ; counter++)
        {
            var candidate = Path.Combine(folder, ext.Length == 0 ? $"{stem} {counter}" : $"{stem} {counter}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static List<WaUndoAction> MoveVerified(string source, string destination, string digest, long size)
    {
        var sourceVolume = WaFiles.VolumeOf(source);
        var destinationVolume = WaFiles.VolumeOf(Path.GetDirectoryName(destination)!);
        if (sourceVolume is not null && sourceVolume == destinationVolume
            && string.Equals(Path.GetPathRoot(Path.GetFullPath(source)), Path.GetPathRoot(Path.GetFullPath(destination)), StringComparison.OrdinalIgnoreCase))
        {
            // Same volume: this is a rename, so no byte moves and the digest cannot change.
            File.Move(source, destination);
            return [new WaUndoAction("move", destination, source)];
        }

        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".winknife-{Guid.NewGuid():N}.partial");
        try
        {
            File.Copy(source, temporary, false);
            if (WaFiles.Sha256(temporary) != digest)
            {
                throw new IOException("verification failed");
            }

            File.Move(temporary, destination);
            var trashed = WaRecycle.Trash(source, size);
            if (trashed is null)
            {
                _ = WaRecycle.Trash(destination, size);
                throw new IOException("source could not be removed");
            }

            var actions = new List<WaUndoAction> { new("trash", destination, null) };
            if (!string.IsNullOrEmpty(trashed.BinPath))
            {
                actions.Add(new WaUndoAction("move", trashed.BinPath, source));
            }

            return actions;
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch
            {
                // nothing to clean
            }

            throw;
        }
    }

    private static List<WaUndoAction> ReplaceVerified(string source, string existing, string digest, long size)
    {
        var staging = Path.Combine(Path.GetDirectoryName(existing)!, $".winknife-replaced-{Guid.NewGuid():N}.partial");
        File.Move(existing, staging);
        try
        {
            var actions = MoveVerified(source, existing, digest, size);
            var trashedExisting = WaRecycle.Trash(staging, new FileInfo(staging).Length) ?? throw new IOException("recycle failed");
            if (!string.IsNullOrEmpty(trashedExisting.BinPath))
            {
                actions.Add(new WaUndoAction("move", trashedExisting.BinPath, existing));
            }

            return actions;
        }
        catch
        {
            if (!File.Exists(existing))
            {
                try
                {
                    File.Move(staging, existing);
                }
                catch
                {
                    // the staged copy stays beside the destination
                }
            }
            else
            {
                _ = WaRecycle.Trash(staging, 0);
            }

            throw;
        }
    }

    private static bool SourceStillMatches(Source source)
    {
        try
        {
            var info = new FileInfo(source.Path);
            return WaFiles.Fingerprint(source.Path) == source.Fingerprint
                && info.Length == source.Size
                && Math.Abs((info.LastWriteTime - source.ModifiedAt).TotalMilliseconds) < 1;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ persistence

    public static List<WaRecord> LoadRecords() => Load<WaRecord>("whatsAppOrganizerRecords");

    public static void SaveRecords(List<WaRecord> records) => Host.App.Set("whatsAppOrganizerRecords", JsonSerializer.Serialize(records, Json));

    private static List<WaUndoTransaction> LoadUndoTransactions() => Load<WaUndoTransaction>("whatsAppOrganizerUndoTransaction");

    private static void SaveUndoTransactions(List<WaUndoTransaction> transactions) => Host.App.Set("whatsAppOrganizerUndoTransaction", JsonSerializer.Serialize(transactions, Json));

    private static List<T> Load<T>(string key)
    {
        try
        {
            var text = Host.App.Get<string>(key);
            return string.IsNullOrWhiteSpace(text) ? [] : JsonSerializer.Deserialize<List<T>>(text, Json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    internal static List<WaUndoTransaction> ValidUndoTransactions(DateTime? now = null)
    {
        var reference = (now ?? DateTime.UtcNow).ToUniversalTime();
        return LoadUndoTransactions()
            .Where(t => WaRules.OrganizerUndoIsValid(t.CreatedAt.ToUniversalTime(), reference))
            .OrderBy(t => t.CreatedAt)
            .ToList();
    }

    // ------------------------------------------------------------------ undo

    public void UndoLast(Guid? transactionId = null)
    {
        if (IsBusy)
        {
            return;
        }

        var transactions = ValidUndoTransactions();
        var transaction = transactionId is { } id ? transactions.FirstOrDefault(t => t.Id == id) : transactions.LastOrDefault();
        if (transaction is null || !RecordsAllowUndo(transaction, LoadRecords()))
        {
            return;
        }

        var token = Guid.NewGuid();
        _token = token;
        Phase = WaOrganizerPhase.Undoing;
        Raise();
        Task.Run(() =>
        {
            var failed = PerformUndo(transaction);
            V.Post(() =>
            {
                if (_token != token)
                {
                    return;
                }

                if (failed == 0)
                {
                    SaveRecords(RecordsAfterUndo(transaction, LoadRecords()));
                    SaveUndoTransactions(transactions.Where(t => t.Id != transaction.Id).ToList());
                    DoneMoved = 0;
                    DoneDuplicates = 0;
                    DoneFailed = 0;
                    Phase = WaOrganizerPhase.Done;
                }
                else
                {
                    Phase = WaOrganizerPhase.Failed;
                }

                Raise();
                WaManager.Shared.Scan();
                Schedule(TimeSpan.FromSeconds(2));
            });
        });
    }

    private static Dictionary<string, WaRecord> RecordMap(IEnumerable<WaRecord> records)
    {
        var map = new Dictionary<string, WaRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            map[record.DestinationPath] = record;
        }

        return map;
    }

    private static HashSet<string> AffectedRecordPaths(WaUndoTransaction transaction)
    {
        var before = RecordMap(transaction.RecordsBefore);
        var after = RecordMap(transaction.RecordsAfter);
        var paths = new HashSet<string>(before.Keys, StringComparer.OrdinalIgnoreCase);
        paths.UnionWith(after.Keys);
        paths.RemoveWhere(p => before.TryGetValue(p, out var b) && after.TryGetValue(p, out var a) && b == a);
        return paths;
    }

    internal static bool RecordsAllowUndo(WaUndoTransaction transaction, List<WaRecord> current)
    {
        var expected = RecordMap(transaction.RecordsAfter);
        var now = RecordMap(current);
        return AffectedRecordPaths(transaction).All(p =>
        {
            var hasNow = now.TryGetValue(p, out var n);
            var hasExpected = expected.TryGetValue(p, out var e);
            return hasNow == hasExpected && (!hasNow || n == e);
        });
    }

    internal static List<WaRecord> RecordsAfterUndo(WaUndoTransaction transaction, List<WaRecord> current)
    {
        var before = RecordMap(transaction.RecordsBefore);
        var result = RecordMap(current);
        foreach (var path in AffectedRecordPaths(transaction))
        {
            if (before.TryGetValue(path, out var record))
            {
                result[path] = record;
            }
            else
            {
                result.Remove(path);
            }
        }

        return [.. result.Values.OrderBy(r => r.OrganizedAt)];
    }

    /// <summary>Runs the steps of a transaction in order; returns how many failed.</summary>
    internal static int PerformUndo(WaUndoTransaction transaction)
    {
        var failed = 0;
        foreach (var action in transaction.Actions)
        {
            if (!File.Exists(action.CurrentPath))
            {
                failed++;
                continue;
            }

            try
            {
                if (action.Kind == "move")
                {
                    if (action.RestorePath is null)
                    {
                        failed++;
                        continue;
                    }

                    if (WaRecycle.IsBinContent(action.CurrentPath))
                    {
                        if (!WaRecycle.Restore(action.CurrentPath, action.RestorePath))
                        {
                            failed++;
                        }

                        continue;
                    }

                    if (File.Exists(action.RestorePath) || Directory.Exists(action.RestorePath))
                    {
                        failed++;
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(action.RestorePath)!);
                    File.Move(action.CurrentPath, action.RestorePath);
                }
                else if (WaRecycle.Trash(action.CurrentPath, new FileInfo(action.CurrentPath).Length) is null)
                {
                    failed++;
                }
            }
            catch
            {
                failed++;
            }
        }

        return failed;
    }
}
