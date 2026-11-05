// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Trayloom.Core;

namespace Trayloom.Features.Maintenance;

internal enum CleanerPhase
{
    Idle,
    Scanning,
    Results,
    Cleaning,
    Done,
}

/// <summary>The cleaner state machine (spec 7.1.2/7.1.6): idle -> scanning -> results -> cleaning -> done.</summary>
internal sealed class CleanerModel
{
    private CancellationTokenSource? _cts;
    private System.Threading.Timer? _timer;
    private bool _wakeHooked;
    private bool _observingAutomatic;

    public static CleanerModel Shared { get; } = new();

    public event Action? Changed;

    public CleanerPhase Phase { get; private set; }

    public List<CleanItem> Items { get; private set; } = [];

    public string? ScanningCategory { get; private set; }

    public long Removed { get; private set; }

    public int Failed { get; private set; }

    public bool RecycledAny { get; private set; }

    public bool DeletedAny { get; private set; }

    public string? BackupFolder { get; private set; }

    /// <summary>How many items the Recycle Bin could not keep, so they were left in place.</summary>
    public int TooLargeCount { get; private set; }

    public DateTime? NextFire { get; private set; }

    public long SelectedSize => Items.Where(i => i.Include).Sum(i => i.Size);

    public int SelectedCount => Items.Count(i => i.Include);

    public long TotalSize => Items.Sum(i => i.Size);

    private void Raise() => V.Post(() => Changed?.Invoke());

    private static void Ui(Action action) => V.Post(action);

    // ------------------------------------------------------------------ scan

    public void Scan(bool attended)
    {
        if (Phase != CleanerPhase.Idle && Phase != CleanerPhase.Done)
        {
            return;
        }

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Phase = CleanerPhase.Scanning;
        ScanningCategory = null;
        Items = [];
        Removed = 0;
        Failed = 0;
        TooLargeCount = 0;
        Raise();
        var days = SanitizedScreenshotDays(Host.App.Get<int>("cleanerScreenshotAgeDays"));
        _ = Task.Run(() =>
        {
            try
            {
                var scanner = new CleanerScanner(attended, days, title => Ui(() =>
                {
                    if (!cts.IsCancellationRequested)
                    {
                        ScanningCategory = title;
                        Raise();
                    }
                }), cts.Token);
                var items = scanner.Scan();
                Ui(() =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    Items = items.OrderByDescending(i => i.Size).ToList();
                    Phase = CleanerPhase.Results;
                    Raise();
                });
            }
            catch (OperationCanceledException)
            {
                // reset() already moved the state back to idle
            }
            catch (Exception ex)
            {
                Host.Log("Cleaner scan failed", ex);
                Ui(() =>
                {
                    Items = [];
                    Phase = CleanerPhase.Results;
                    Raise();
                });
            }
        });
    }

    public static int SanitizedScreenshotDays(int stored) => stored <= 0 ? 0 : Math.Min(stored, 3650);

    public void Reset()
    {
        if (Phase == CleanerPhase.Cleaning)
        {
            return;
        }

        _cts?.Cancel();
        Items = [];
        ScanningCategory = null;
        Phase = CleanerPhase.Idle;
        Raise();
    }

    public void SetGroup(CleanerGroup group, bool include)
    {
        foreach (var item in Items.Where(i => i.Group == group))
        {
            item.Include = include;
        }

        Raise();
    }

    public void SetItem(CleanItem item, bool include)
    {
        item.Include = include;
        Raise();
    }

    // ------------------------------------------------------------------ clean

    public void CleanSelected(bool escalate)
    {
        if (Phase != CleanerPhase.Results)
        {
            return;
        }

        var selected = Items.Where(i => i.Include).ToList();
        Phase = CleanerPhase.Cleaning;
        Raise();
        _ = Task.Run(() =>
        {
            long removed = 0;
            var failed = 0;
            var recycled = false;
            var deleted = false;
            var run = new CleanRun();
            // the Recycle Bin first, so the clean does not empty what it just moved
            foreach (var item in selected.Where(i => i.Kind == RemoveKind.EmptyBin))
            {
                if (FileOps.EmptyRecycleBin())
                {
                    removed += item.Size;
                    deleted = true;
                }
                else
                {
                    failed++;
                }
            }

            // every registry key is exported once, before any of its values is deleted
            BackupRegistryKeys(selected.Where(i => i.Kind == RemoveKind.RegistryValue).ToList(), run);
            foreach (var item in selected.Where(i => i.Kind != RemoveKind.EmptyBin))
            {
                try
                {
                    var (freed, fail, wasRecycled) = Remove(item, run);
                    removed += freed;
                    failed += fail;
                    recycled |= wasRecycled;
                    deleted |= !wasRecycled && freed > 0 && item.Kind != RemoveKind.RegistryValue;
                }
                catch
                {
                    failed++;
                }
            }

            Ui(() =>
            {
                Removed = removed;
                Failed = failed;
                RecycledAny = recycled;
                DeletedAny = deleted;
                BackupFolder = run.Backup;
                TooLargeCount = run.TooLarge;
                Items = [];
                Phase = CleanerPhase.Done;
                Raise();
            });
        });
    }

    /// <summary>
    /// Last check before anything is removed. A critical folder (or one that contains one) is refused for every kind. The only
    /// folders allowed to be shallower than <see cref="FileOps.MayRemove"/> permits are Windows\Temp, Prefetch and Minidump, and
    /// only for <see cref="RemoveKind.PermanentContents"/> (their children are removed, never the folder).
    /// </summary>
    internal static bool Validate(CleanItem item, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || SafePaths.IsCriticalOrAncestor(path))
        {
            return false;
        }

        if (!FileOps.MayRemove(path) && !(item.Kind == RemoveKind.PermanentContents && IsShallowSystemFolder(path) && !Win32.IsReparsePoint(path)))
        {
            return false;
        }

        if (item.Root is not null)
        {
            if (!FileOps.IsUnder(path, item.Root))
            {
                return false;
            }

            if (FileOps.HasReparseInChain(path, item.Root) && !string.Equals(path.TrimEnd('\\'), item.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string[] ShallowSystemFolders = ["Temp", "Prefetch", "Minidump"];

    private static bool IsShallowSystemFolder(string path)
    {
        try
        {
            var full = SafePaths.Normalize(path);
            return ShallowSystemFolders.Any(name => string.Equals(full, SafePaths.Normalize(Path.Combine(FileOps.Windows, name)), StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>State shared by the removals of one clean: the registry backup folder and the keys that could not be saved.</summary>
    internal sealed class CleanRun
    {
        public string? Backup { get; set; }

        public int TooLarge { get; set; }

        public HashSet<string> SavedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Turns the per-path recycle results into totals: only <see cref="RecycleResult.Recycled"/> frees space and sets
    /// <c>RecycledAny</c>; <see cref="RecycleResult.NotRecyclable"/> counts as too large and as failed; a missing path is neutral.
    /// </summary>
    internal static (long Freed, int Failed, int TooLarge, bool RecycledAny) TallyRecycle(IEnumerable<(RecycleResult Result, long Size)> results)
    {
        long freed = 0;
        var failed = 0;
        var tooLarge = 0;
        var any = false;
        foreach (var (result, size) in results)
        {
            switch (result)
            {
                case RecycleResult.Recycled:
                    freed += size;
                    any = true;
                    break;
                case RecycleResult.Missing:
                    break;
                case RecycleResult.NotRecyclable:
                    tooLarge++;
                    failed++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        return (freed, failed, tooLarge, any);
    }

    /// <summary>True only when the value's key was exported successfully in this run, so no value is ever deleted without a backup.</summary>
    internal static bool MayDeleteRegistryValue(CleanItem item, CleanRun run) => run.SavedKeys.Contains(KeyId(item));

    private (long Freed, int Failed, bool Recycled) Remove(CleanItem item, CleanRun run)
    {
        switch (item.Kind)
        {
            case RemoveKind.RegistryValue:
                return (0, RemoveRegistryValue(item, run) ? 0 : 1, false);

            case RemoveKind.Recycle:
            {
                var results = new List<(RecycleResult Result, long Size)>();
                var invalid = 0;
                foreach (var path in item.Paths)
                {
                    if (!Validate(item, path) || (item.Identity is not null && Win32.FileIdentity(path) != item.Identity))
                    {
                        invalid++;
                        continue;
                    }

                    // never a silent permanent delete: what the bin cannot keep stays where it is
                    var size = FileOps.DirectorySize(path);
                    results.Add((FileOps.TryRecycle(path, size), size));
                }

                var tally = TallyRecycle(results);
                run.TooLarge += tally.TooLarge;
                return (tally.Freed, tally.Failed + invalid, tally.RecycledAny);
            }

            case RemoveKind.PermanentFiles:
            {
                long freed = 0;
                var failed = 0;
                foreach (var path in item.Paths)
                {
                    if (!Validate(item, path))
                    {
                        failed++;
                        continue;
                    }

                    var r = FileOps.DeletePermanently(path);
                    freed += r.Freed;
                    failed += r.Failed ? 1 : 0;
                }

                return (freed, failed > 0 && freed == 0 ? 1 : 0, false);
            }

            case RemoveKind.PermanentContents:
            {
                if (!Validate(item, item.Path) || (item.Identity is not null && Win32.FileIdentity(item.Path) != item.Identity))
                {
                    return (0, 1, false);
                }

                long freed = 0;
                var failed = false;
                var cutoff = DateTime.UtcNow.AddHours(-item.MinAgeHours);
                try
                {
                    foreach (var entry in new DirectoryInfo(item.Path).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            try
                            {
                                if (entry is DirectoryInfo link)
                                {
                                    link.Delete();
                                }
                                else
                                {
                                    entry.Delete();
                                }
                            }
                            catch
                            {
                                failed = true;
                            }

                            continue;
                        }

                        if (entry.LastWriteTimeUtc > cutoff)
                        {
                            continue;
                        }

                        var r = FileOps.DeletePermanently(entry.FullName);
                        freed += r.Freed;
                        failed |= r.Failed;
                    }
                }
                catch
                {
                    failed = true;
                }

                return (freed, failed && freed == 0 ? 1 : 0, false);
            }

            default:
            {
                long freed = 0;
                var failed = false;
                foreach (var path in item.Paths)
                {
                    if (!Validate(item, path) || (item.Identity is not null && item.Paths.Count == 1 && Win32.FileIdentity(path) != item.Identity))
                    {
                        failed = true;
                        continue;
                    }

                    var r = FileOps.DeletePermanently(path);
                    freed += r.Freed;
                    failed |= r.Failed;
                }

                return (freed, failed && freed == 0 ? 1 : 0, false);
            }
        }
    }

    private static string RegExe => Path.Combine(Environment.SystemDirectory, "reg.exe");

    internal static string KeyId(CleanItem item) => $"{item.Hive}|{item.View}|{item.SubKey}";

    /// <summary>
    /// Exports each distinct (hive, view, key) once into a new folder for this run, before any value is deleted. A key whose export
    /// fails is remembered so that none of its values are removed.
    /// </summary>
    private static void BackupRegistryKeys(List<CleanItem> items, CleanRun run)
    {
        if (items.Count == 0)
        {
            return;
        }

        string folder;
        try
        {
            folder = Path.Combine(AppPaths.Root, "cleaner", "backups", $"{DateTime.Now:yyyyMMdd-HHmmss-fff}");
            Directory.CreateDirectory(folder);
        }
        catch
        {
            return;
        }

        var number = 0;
        foreach (var group in items.GroupBy(KeyId))
        {
            var first = group.First();
            var ok = false;
            try
            {
                number++;
                var hive = first.Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
                var safeName = new string(first.SubKey!.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
                var file = Path.Combine(folder, $"{number:000}-{hive}-{safeName}.reg");
                var info = new ProcessStartInfo(RegExe) { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("export");
                info.ArgumentList.Add($"{hive}\\{first.SubKey}");
                info.ArgumentList.Add(file);
                info.ArgumentList.Add(first.View == RegistryView.Registry32 ? "/reg:32" : "/reg:64");
                using var process = Process.Start(info);
                if (process is not null)
                {
                    if (process.WaitForExit(15_000))
                    {
                        ok = process.ExitCode == 0 && File.Exists(file);
                    }
                    else
                    {
                        // a hung reg.exe must not leave the cleaner in the cleaning phase for good
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                            // already gone
                        }
                    }
                }
            }
            catch
            {
                ok = false;
            }

            if (ok)
            {
                run.Backup = folder;
                run.SavedKeys.Add(group.Key);
            }
        }
    }

    private static bool RemoveRegistryValue(CleanItem item, CleanRun run)
    {
        try
        {
            if (!MayDeleteRegistryValue(item, run))
            {
                return false;
            }

            using var root = RegistryKey.OpenBaseKey(item.Hive, item.View);
            using var key = root.OpenSubKey(item.SubKey!, true);
            if (key is null || item.ValueName is null)
            {
                return false;
            }

            key.DeleteValue(item.ValueName, false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ schedule

    public static DateTime? NextFireDate(DateTime after, string frequency, int hour, int minute, int weekday)
    {
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        weekday = Math.Clamp(weekday, 1, 7);
        if (frequency is not ("daily" or "weekly"))
        {
            return null;
        }

        DateTime At(DateTime day)
        {
            var candidate = new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Local);
            return TimeZoneInfo.Local.IsInvalidTime(candidate) ? candidate.AddHours(1) : candidate;
        }

        if (frequency == "daily")
        {
            var candidate = At(after.Date);
            return candidate > after ? candidate : At(after.Date.AddDays(1));
        }

        var target = (DayOfWeek)(weekday - 1);
        var offset = ((int)target - (int)after.DayOfWeek + 7) % 7;
        var next = At(after.Date.AddDays(offset));
        return next > after ? next : At(after.Date.AddDays(offset + 7));
    }

    public static bool MissedRun(DateTime now, double lastRunEpoch, string frequency, int hour, int minute, int weekday)
    {
        if (frequency is not ("daily" or "weekly") || lastRunEpoch <= 0)
        {
            return false;
        }

        var next = NextFireDate(Fmt.FromEpoch(lastRunEpoch), frequency, hour, minute, weekday);
        return next is not null && next <= now;
    }

    public static (int Hour12, bool IsPm) Hour12Components(int hour24) => (hour24 % 12 == 0 ? 12 : hour24 % 12, hour24 >= 12);

    public static int Hour24(int hour12, bool isPm) => Math.Clamp(hour12, 1, 12) % 12 + (isPm ? 12 : 0);

    private string Frequency => Host.App.Get<string>("cleanerScheduleFrequency") is var f && (f == "daily" || f == "weekly") ? f : "off";

    public void SyncWithPreferences()
    {
        _timer?.Dispose();
        _timer = null;
        if (!Host.IsAttached || Frequency == "off" || !Host.Runtime.IsAvailable("cleaner"))
        {
            NextFire = null;
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
        _cts?.Cancel();
    }

    private void ScheduleNext()
    {
        _timer?.Dispose();
        var now = DateTime.Now;
        var freq = Frequency;
        var hour = Host.App.Get<int>("cleanerScheduleHour");
        var minute = Host.App.Get<int>("cleanerScheduleMinute");
        var weekday = Host.App.Get<int>("cleanerScheduleWeekday");
        DateTime? fire = MissedRun(now, Host.App.Get<double>("cleanerLastAutoRun"), freq, hour, minute, weekday)
            ? now.AddSeconds(120)
            : NextFireDate(now, freq, hour, minute, weekday);
        NextFire = fire;
        Raise();
        if (fire is null)
        {
            return;
        }

        var due = fire.Value - now;
        if (due < TimeSpan.FromSeconds(1))
        {
            due = TimeSpan.FromSeconds(1);
        }

        if (due > TimeSpan.FromDays(40))
        {
            due = TimeSpan.FromDays(40);
        }

        _timer = new System.Threading.Timer(_ => Ui(RunAutomaticCleanup), null, due, Timeout.InfiniteTimeSpan);
    }

    private void RunAutomaticCleanup()
    {
        if (ElevationPolicy.IsElevated)
        {
            // an elevated copy never cleans on its own; try again later
            Host.Log("Cleaner: automatic cleanup skipped while running as administrator");
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => Ui(RunAutomaticCleanup), null, TimeSpan.FromSeconds(3600), Timeout.InfiniteTimeSpan);
            return;
        }

        if (Phase != CleanerPhase.Idle || _observingAutomatic)
        {
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => Ui(RunAutomaticCleanup), null, TimeSpan.FromSeconds(600), Timeout.InfiniteTimeSpan);
            return;
        }

        _observingAutomatic = true;
        void OnChange()
        {
            switch (Phase)
            {
                case CleanerPhase.Results:
                    if (SelectedCount > 0)
                    {
                        CleanSelected(false);
                    }
                    else
                    {
                        Finish(0, 0);
                    }

                    break;
                case CleanerPhase.Done:
                    Finish(Removed, Failed);
                    break;
                case CleanerPhase.Idle:
                    Changed -= OnChange;
                    _observingAutomatic = false;
                    ScheduleNext();
                    break;
            }
        }

        void Finish(long freed, int failed)
        {
            Changed -= OnChange;
            _observingAutomatic = false;
            Reset();
            Host.App.Set("cleanerLastAutoRun", Fmt.Epoch());
            Host.App.Set("cleanerLastAutoFreed", (int)Math.Min(freed, int.MaxValue));
            Host.App.Set("cleanerLastAutoFailed", failed);
            if (Host.App.Get<bool>("cleanerScheduleNotify"))
            {
                var parts = new List<string>();
                if (freed > 0)
                {
                    parts.Add($"{Fmt.Bytes(freed)} freed.");
                }

                if (failed > 0)
                {
                    parts.Add("Some items couldn’t be removed.");
                }

                if (parts.Count == 0)
                {
                    parts.Add(Loc.T("Nothing to clean. Your PC is tidy."));
                }

                Host.Notify(Loc.T("Automatic cleanup"), string.Join(" ", parts));
            }

            ScheduleNext();
        }

        Changed += OnChange;
        Scan(false);
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
            Ui(ScheduleNext);
        }
    }

    private void OnTime(object? sender, EventArgs e) => Ui(ScheduleNext);

    /// <summary>Self-check of the date maths (used by the test harness).</summary>
    public static string? SelfTest()
    {
        var after = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Local);
        if (NextFireDate(after, "daily", 9, 0, 2) != new DateTime(2026, 11, 4, 9, 0, 0, DateTimeKind.Local))
        {
            return "daily after 10:00 should be tomorrow";
        }

        if (NextFireDate(after, "daily", 11, 30, 2) != new DateTime(2026, 11, 3, 11, 30, 0, DateTimeKind.Local))
        {
            return "daily before the time should be today";
        }

        // 2026-11-03 is a Tuesday; weekday 2 = Monday -> 2026-11-09
        if (NextFireDate(after, "weekly", 9, 0, 2) != new DateTime(2026, 11, 9, 9, 0, 0, DateTimeKind.Local))
        {
            return "weekly Monday";
        }

        if (Hour24(12, false) != 0 || Hour24(12, true) != 12 || Hour24(1, true) != 13)
        {
            return "hour24";
        }

        return NextFireDate(after, "off", 9, 0, 2) is null ? null : "off should be null";
    }
}
