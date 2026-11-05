// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Trayloom.Core;

namespace Trayloom.Features.Maintenance;

internal enum UnPhase
{
    Empty,
    Scanning,
    Results,
    Removing,
    Done,
}

/// <summary>Winget ownership of the selected program.</summary>
internal enum PackageCheck
{
    None,
    Checking,
    Managed,
    Failed,
}

/// <summary>The uninstaller state machine (spec 7.2.2/7.2.6): empty -> scanning -> results -> removing -> done.</summary>
internal sealed class UninstallerModel
{
    private CancellationTokenSource? _cts;
    private List<InstalledProgram> _programs = [];

    public static UninstallerModel Shared { get; } = new();

    public event Action? Changed;

    public UnPhase Phase { get; private set; } = UnPhase.Empty;

    public InstalledProgram? Target { get; private set; }

    public List<UnItem> Items { get; private set; } = [];

    public UnRefusal? Refusal { get; private set; }

    public long Freed { get; private set; }

    public List<UnItem> Failed { get; private set; } = [];

    public bool RebootRequired { get; private set; }

    public string? BackupFolder { get; private set; }

    public bool FailedNeedsAdmin { get; private set; }

    public string RemovingMessage { get; private set; } = "Removing…";

    public PackageCheck Package { get; private set; }

    public string? WingetId { get; private set; }

    public string? WingetName { get; private set; }

    public bool PackageBusy => WingetOperations.Shared.IsBusy;

    public int SelectedCount => Items.Count(i => i.Include);

    public long SelectedSize => Items.Where(i => i.Include).Sum(i => i.Size);

    public long TotalSize => Items.Sum(i => i.Size);

    public bool ApplicationChecked => Items.Any(i => i.Category == UnCategory.Application && i.Include);

    public bool CanRemove => Phase == UnPhase.Results && SelectedCount > 0 && Package != PackageCheck.Failed && Package != PackageCheck.Checking && !PackageBusy;

    private void Raise() => V.Post(() => Changed?.Invoke());

    public void ClearRefusal()
    {
        if (Refusal is not null)
        {
            Refusal = null;
            Raise();
        }
    }

    public void Reset()
    {
        if (Phase == UnPhase.Removing)
        {
            return;
        }

        _cts?.Cancel();
        Items = [];
        Target = null;
        Package = PackageCheck.None;
        WingetId = null;
        Failed = [];
        Phase = UnPhase.Empty;
        Raise();
    }

    public void Cancel()
    {
        if (Phase == UnPhase.Scanning)
        {
            Reset();
        }
    }

    public async Task<List<InstalledProgram>> LoadProgramsAsync()
    {
        _programs = await Task.Run(() => ProgramCatalog.Enumerate()).ConfigureAwait(true);
        return _programs;
    }

    /// <summary>Selects a dropped / chosen file.</summary>
    public async Task SelectPathAsync(string path)
    {
        if (Phase != UnPhase.Empty)
        {
            return;
        }

        if (_programs.Count == 0)
        {
            await LoadProgramsAsync().ConfigureAwait(true);
        }

        var (selection, refusal) = await Task.Run(() => UninstallerSupport.Evaluate(path, _programs)).ConfigureAwait(true);
        Begin(selection, refusal);
    }

    public void SelectProgram(InstalledProgram program)
    {
        if (Phase != UnPhase.Empty)
        {
            return;
        }

        var (selection, refusal) = UninstallerSupport.Evaluate(program);
        Begin(selection, refusal);
    }

    private void Begin(UnSelection? selection, UnRefusal? refusal)
    {
        if (selection is null)
        {
            Refusal = refusal;
            Raise();
            return;
        }

        Refusal = null;
        _ = ScanAsync(selection.Program);
    }

    private async Task ScanAsync(InstalledProgram program)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Target = program;
        Items = [];
        Failed = [];
        Phase = UnPhase.Scanning;
        Package = WingetCli.IsAvailable ? PackageCheck.Checking : PackageCheck.None;
        WingetId = null;
        Raise();
        try
        {
            var all = _programs.Count > 0 ? _programs : await LoadProgramsAsync().ConfigureAwait(true);
            var scan = Task.Run(() => new LeftoverScanner(program, all, cts.Token).Scan(), cts.Token);
            var check = WingetCli.IsAvailable ? CheckPackageAsync(program) : Task.CompletedTask;
            await Task.WhenAll(scan, check).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Items = scan.Result;
            Phase = UnPhase.Results;
            Raise();
        }
        catch (OperationCanceledException)
        {
            // reset() moved the state
        }
        catch (Exception ex)
        {
            Host.Log("Uninstaller scan failed", ex);
            Phase = UnPhase.Empty;
            Raise();
        }
    }

    /// <summary>Asks winget whether it owns the program (match by display name in <c>winget list</c>).</summary>
    public async Task CheckPackageAsync(InstalledProgram? program = null)
    {
        program ??= Target;
        if (program is null || !WingetCli.IsAvailable)
        {
            Package = PackageCheck.None;
            Raise();
            return;
        }

        Package = PackageCheck.Checking;
        Raise();
        var (result, rows) = await WingetCli.ListAsync().ConfigureAwait(true);
        if (WingetCli.NeedsSourceAgreement(result.Output))
        {
            Package = PackageCheck.None;
        }
        else if (!result.Ok && rows.Count == 0)
        {
            Package = PackageCheck.Failed;
        }
        else
        {
            var name = Norm.Of(program.Name);
            var stripped = Norm.Stripped(program.Name);
            var row = rows.FirstOrDefault(r => r.Source.Equals("winget", StringComparison.OrdinalIgnoreCase) && (Norm.Of(r.Name) == name || Norm.Stripped(r.Name) == stripped) && WingetCli.ValidToken(r.Id));
            if (row is not null)
            {
                Package = PackageCheck.Managed;
                WingetId = row.Id;
                WingetName = row.Name;
                program.WingetId = row.Id;
            }
            else
            {
                Package = PackageCheck.None;
            }
        }

        Raise();
    }

    public void SetItem(UnItem item, bool include)
    {
        item.Include = include;
        Raise();
    }

    // ------------------------------------------------------------------ removal

    /// <summary>A running process found under the program's install folder: identified by pid and start time so a reused pid is never hit.</summary>
    internal sealed record RunningApp(int Pid, long StartFileTime, string Name);

    private bool _closing;

    /// <summary>Items that went to the Recycle Bin in the last run (the Done screen says "Recycle Bin" only when this is above zero).</summary>
    public int RecycledCount { get; private set; }

    /// <summary>Items left in place in the last run because they were too large to stay recoverable in the Recycle Bin.</summary>
    public int TooLargeCount => Failed.Count(f => f.FailReason == "TooLarge");

    /// <summary>
    /// Removes the selected rows. <paramref name="confirmClose"/> lists the running apps and asks to close them (false aborts, nothing is
    /// removed); <paramref name="confirmForce"/> lists the ones that did not close and asks to force quit (false carries on without killing).
    /// Both are called on the UI thread.
    /// </summary>
    public async Task RemoveSelectedAsync(bool silent, Func<IReadOnlyList<string>, bool> confirmClose, Func<IReadOnlyList<string>, bool> confirmForce)
    {
        if (!CanRemove || Target is null || _closing)
        {
            return;
        }

        var target = Target;
        var selected = Items.Where(i => i.Include).ToList();
        var application = selected.FirstOrDefault(i => i.Category == UnCategory.Application);

        // 1. running apps from the program's own (usable) install folder: list, ask, close politely, ask again, only then force.
        // The Removing phase starts first so Cancel cannot reset the wizard while the search or the dialogs are open.
        if (application is not null)
        {
            _closing = true;
            Phase = UnPhase.Removing;
            RemovingMessage = "Checking for running apps…";
            Raise();
            try
            {
                var programs = _programs;
                var running = await Task.Run(() =>
                {
                    var location = UninstallerSupport.UsableInstallLocation(target, programs);
                    return location.Length == 0 ? [] : FindRunning(location);
                }).ConfigureAwait(true);
                if (running.Count > 0)
                {
                    if (!confirmClose(DistinctNames(running)))
                    {
                        Phase = UnPhase.Results;
                        Raise();
                        return;
                    }

                    RemovingMessage = "Closing apps…";
                    Raise();
                    var left = await CloseGracefullyAsync(running).ConfigureAwait(true);
                    if (left.Count > 0 && confirmForce(DistinctNames(left)))
                    {
                        await Task.Run(() => ForceQuit(left)).ConfigureAwait(true);
                    }
                }
            }
            catch (Exception ex)
            {
                // without the user's confirmation nothing is removed
                Host.Log("Uninstaller: closing apps failed", ex);
                Phase = UnPhase.Results;
                Raise();
                return;
            }
            finally
            {
                _closing = false;
            }
        }

        Phase = UnPhase.Removing;
        RemovingMessage = application is not null ? $"Running the uninstaller for {target.Name}…" : "Removing…";
        Failed = [];
        RebootRequired = false;
        BackupFolder = null;
        RecycledCount = 0;
        Raise();

        long freed = 0;
        var failed = new List<UnItem>();
        try
        {
            if (application is not null)
            {
                var ok = await UninstallProgramAsync(target, silent).ConfigureAwait(true);
                if (ok)
                {
                    freed += application.Size;
                }
                else
                {
                    failed.Add(application);
                }
            }

            RemovingMessage = "Removing…";
            Raise();
            var rest = selected.Where(i => i.Category != UnCategory.Application).ToList();
            var (done, bad, backup, stubborn, recycled) = await Task.Run(() => RemoveItems(rest)).ConfigureAwait(true);
            freed += done;
            failed.AddRange(bad);
            BackupFolder = backup;
            RecycledCount = recycled;

            var (elevatedFreed, elevatedFailed) = await RunElevatedAsync(stubborn).ConfigureAwait(true);
            freed += elevatedFreed;
            failed.AddRange(elevatedFailed);
        }
        catch (Exception ex)
        {
            Host.Log("Uninstall failed", ex);
            failed.AddRange(selected.Where(s => !failed.Contains(s)).Take(1));
        }

        Freed = freed;
        Failed = failed;
        FailedNeedsAdmin = failed.Any(f => f.NeedsAdmin && f.FailReason is null) && !FileOps.IsElevated;
        Items = [];
        Phase = UnPhase.Done;
        Raise();
    }

    /// <summary>Re-runs the elevated removal for the rows that failed for lack of administrator rights ("Try again" on the Done screen).</summary>
    public async Task RetryFailedElevatedAsync()
    {
        if (Phase != UnPhase.Done || _closing)
        {
            return;
        }

        var retry = Failed.Where(f => f.NeedsAdmin && f.FailReason is null && Elevated.IsAllowed(ToElevated(f))).ToList();
        if (retry.Count == 0)
        {
            return;
        }

        _closing = true;
        try
        {
            var (freedNow, stillFailed) = await RunElevatedAsync(retry).ConfigureAwait(true);
            Freed += freedNow;
            Failed = Failed.Where(f => !retry.Contains(f)).Concat(stillFailed).ToList();
            FailedNeedsAdmin = Failed.Any(f => f.NeedsAdmin && f.FailReason is null) && !FileOps.IsElevated;
        }
        catch (Exception ex)
        {
            Host.Log("Uninstaller: retry failed", ex);
        }
        finally
        {
            _closing = false;
            Raise();
        }
    }

    /// <summary>
    /// The only elevated path of the uninstaller. Rows that fail <see cref="Elevated.IsAllowed"/> are moved straight to the failed list
    /// (logged by kind) before the Windows prompt; declining the prompt fails all of them.
    /// </summary>
    private static async Task<(long Freed, List<UnItem> Failed)> RunElevatedAsync(List<UnItem> stubborn)
    {
        long freed = 0;
        var failed = new List<UnItem>();
        if (stubborn.Count == 0)
        {
            return (freed, failed);
        }

        var allowed = new List<UnItem>();
        foreach (var item in stubborn)
        {
            if (Elevated.IsAllowed(ToElevated(item)))
            {
                allowed.Add(item);
            }
            else
            {
                item.FailReason = "Refused";
                failed.Add(item);
                Host.Log($"Uninstaller: elevated removal refused a {item.Kind} row");
            }
        }

        if (allowed.Count == 0)
        {
            return (freed, failed);
        }

        var result = await Task.Run(() => Elevated.Remove(allowed.Select(ToElevated).ToList())).ConfigureAwait(true);
        if (result is null)
        {
            failed.AddRange(allowed);
            return (freed, failed);
        }

        foreach (var item in allowed)
        {
            if (result.Any(f => string.Equals(f, item.Path, StringComparison.OrdinalIgnoreCase)))
            {
                failed.Add(item);
            }
            else
            {
                freed += item.Size;
            }
        }

        return (freed, failed);
    }

    private static IReadOnlyList<string> DistinctNames(IEnumerable<RunningApp> apps) =>
        apps.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    internal static Elevated.Item ToElevated(UnItem item) => item.Kind switch
    {
        UnKind.RegistryKey => new Elevated.Item("reg", "Registry::" + item.Path),
        UnKind.RegistryValue => new Elevated.Item("regvalue", "Registry::" + item.Path, item.ValueName),
        UnKind.Task => new Elevated.Item("task", item.Path),
        UnKind.Service => new Elevated.Item("service", item.Path),
        _ => new Elevated.Item("file", item.Path, null, true),
    };

    /// <summary>
    /// Processes whose main module lies under <paramref name="location"/> (a usable install folder). Never this process, another
    /// session's processes, unreadable ones or protected Windows processes. Runs off the UI thread.
    /// </summary>
    internal static List<RunningApp> FindRunning(string location)
    {
        var list = new List<RunningApp>();
        if (string.IsNullOrEmpty(location))
        {
            return list;
        }

        var self = Environment.ProcessId;
        int session;
        using (var me = Process.GetCurrentProcess())
        {
            session = me.SessionId;
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == self || process.SessionId != session)
                {
                    continue;
                }

                var name = process.ProcessName;
                if (ProcessSafety.IsProtectedName(name) || name.Equals("Trayloom", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(path) || !SafePaths.IsUnder(path, location))
                {
                    continue;
                }

                var started = Win32.CreationTime(process.Id);
                if (started == 0)
                {
                    continue;
                }

                list.Add(new RunningApp(process.Id, started, name));
            }
            catch
            {
                // protected or already gone: the module cannot be read, so it is not ours to close
            }
            finally
            {
                process.Dispose();
            }
        }

        return list;
    }

    /// <summary>Sends <c>CloseMainWindow</c> to each app whose start time still matches, waits up to 5 s, and returns the ones still running.</summary>
    internal static async Task<List<RunningApp>> CloseGracefullyAsync(IReadOnlyList<RunningApp> apps)
    {
        foreach (var app in apps)
        {
            try
            {
                if (!IsStillRunning(app))
                {
                    continue;
                }

                using var process = Process.GetProcessById(app.Pid);
                process.CloseMainWindow();
            }
            catch
            {
                // gone already
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && apps.Any(IsStillRunning))
        {
            await Task.Delay(200).ConfigureAwait(true);
        }

        return apps.Where(IsStillRunning).ToList();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    /// <summary>True while the same process (same pid and start time) has not exited; an exited process that others still hold open does not count.</summary>
    private static bool IsStillRunning(RunningApp app)
    {
        var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION | Win32.SYNCHRONIZE, false, app.Pid);
        if (handle == 0)
        {
            return false;
        }

        try
        {
            return Win32.GetProcessTimes(handle, out var creation, out _, out _, out _) && creation == app.StartFileTime && WaitForSingleObject(handle, 0) == 258;
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }

    /// <summary>Ends each app after re-checking that the pid still belongs to the same process (same start time).</summary>
    internal static void ForceQuit(IReadOnlyList<RunningApp> apps)
    {
        foreach (var app in apps)
        {
            try
            {
                if (ProcessSafety.IsProtectedName(app.Name))
                {
                    continue;
                }

                // one handle checks the start time and ends the process, so a reused pid can never be hit
                KillService.KillOne(new KillTarget(app.Pid, app.StartFileTime, app.Name), true);
            }
            catch
            {
                // gone or not allowed
            }
        }
    }

    /// <summary>
    /// One folder per uninstall run holding the <c>.reg</c> exports taken before anything is deleted. Files are numbered, never overwritten,
    /// and each distinct key is exported once per run.
    /// </summary>
    internal sealed class RegistryBackup
    {
        private readonly HashSet<string> _done = new(StringComparer.OrdinalIgnoreCase);
        private int _next = 1;

        private RegistryBackup(string folder)
        {
            Folder = folder;
        }

        /// <summary>The run folder (created by <see cref="BeginRun"/>).</summary>
        public string Folder { get; }

        /// <summary>True once at least one export succeeded.</summary>
        public bool AnyExported { get; private set; }

        /// <summary>Starts a run folder; a folder that already exists (same millisecond) is never reused.</summary>
        public static RegistryBackup BeginRun(string appName, string? baseFolder = null)
        {
            var parent = baseFolder ?? Path.Combine(AppPaths.Root, "uninstaller", "backups");
            var stem = $"{FileOps.SanitizeName(appName)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
            var folder = Path.Combine(parent, stem);
            for (var i = 2; Directory.Exists(folder); i++)
            {
                folder = Path.Combine(parent, $"{stem}-{i}");
            }

            Directory.CreateDirectory(folder);
            return new RegistryBackup(folder);
        }

        /// <summary>Exports <paramref name="key"/> ("HKEY_...\path") once per run. Returns false when the export failed.</summary>
        public bool Export(string key)
        {
            if (_done.Contains(key))
            {
                return true;
            }

            try
            {
                string file;
                do
                {
                    var label = FileOps.SanitizeName(key.Replace('\\', '_'), 80);
                    file = Path.Combine(Folder, $"{_next++:000}-{label}.reg");
                }
                while (File.Exists(file));

                // no /y: reg.exe refuses to overwrite, so an existing backup can never be replaced
                var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                info.ArgumentList.Add("export");
                info.ArgumentList.Add(key);
                info.ArgumentList.Add(file);
                using var process = Process.Start(info);
                if (process is null)
                {
                    return false;
                }

                // drain the pipes so a chatty reg.exe cannot block on a full buffer
                _ = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(20_000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // already gone
                    }

                    return false;
                }

                if (process.ExitCode != 0 || !File.Exists(file))
                {
                    return false;
                }

                _done.Add(key);
                AnyExported = true;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private (long Freed, List<UnItem> Failed, string? Backup, List<UnItem> Stubborn, int Recycled) RemoveItems(List<UnItem> items)
    {
        long freed = 0;
        var recycled = 0;
        var failed = new List<UnItem>();
        var stubborn = new List<UnItem>();

        // back up every distinct registry key before anything is deleted; a row whose key could not be exported is not deleted
        RegistryBackup? backup = null;
        var unsaved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registryKeys = items.Where(i => i.IsRegistry).Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (registryKeys.Count > 0)
        {
            try
            {
                backup = RegistryBackup.BeginRun(Target?.Name ?? "app");
                foreach (var key in registryKeys)
                {
                    if (!backup.Export(key))
                    {
                        unsaved.Add(key);
                    }
                }
            }
            catch
            {
                foreach (var key in registryKeys)
                {
                    unsaved.Add(key);
                }
            }
        }

        foreach (var item in items)
        {
            try
            {
                switch (item.Kind)
                {
                    case UnKind.Path:
                    {
                        if (!StillSafe(item))
                        {
                            failed.Add(item);
                            break;
                        }

                        switch (FileOps.TryRecycle(item.Path, item.Size))
                        {
                            case RecycleResult.Recycled:
                                recycled++;
                                freed += item.Size;
                                break;
                            case RecycleResult.Missing:
                                freed += item.Size;
                                break;
                            case RecycleResult.NotRecyclable:
                                // too large to stay recoverable: left in place, and never sent to the elevated path
                                item.FailReason = "TooLarge";
                                failed.Add(item);
                                break;
                            default:
                                stubborn.Add(item);
                                break;
                        }

                        break;
                    }

                    case UnKind.RegistryKey:
                    case UnKind.RegistryValue:
                    {
                        if (unsaved.Contains(item.Path) || !Elevated.IsAllowed(ToElevated(item)))
                        {
                            item.FailReason ??= unsaved.Contains(item.Path) ? "NoBackup" : "Refused";
                            failed.Add(item);
                            break;
                        }

                        var outcome = RemoveRegistry(item);
                        if (outcome == 1)
                        {
                            freed += item.Size;
                        }
                        else if (outcome == 2)
                        {
                            stubborn.Add(item);
                        }
                        else
                        {
                            failed.Add(item);
                        }

                        break;
                    }

                    case UnKind.Task:
                    case UnKind.Service:
                        stubborn.Add(item);
                        break;
                }
            }
            catch
            {
                failed.Add(item);
            }
        }

        return (freed, failed, backup is { AnyExported: true } ? backup.Folder : null, stubborn, recycled);
    }

    private bool StillSafe(UnItem item)
    {
        if (!FileOps.IsUnder(item.Path, item.Root ?? string.Empty) && item.Root is not null)
        {
            return false;
        }

        if (SafePaths.IsUnderWindows(item.Path))
        {
            return false;
        }

        if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
        {
            return true;
        }

        if (Win32.IsReparsePoint(item.Path) || item.Root is not null && FileOps.HasReparseInChain(item.Path, item.Root))
        {
            return false;
        }

        if (item.Identity is not null && Win32.FileIdentity(item.Path) != item.Identity)
        {
            return false;
        }

        return FileOps.SegmentCount(item.Path) >= 2 && FileOps.MayRemove(item.Path);
    }

    /// <summary>0 failed, 1 removed, 2 needs administrator rights. The key was already backed up by <see cref="RegistryBackup"/>.</summary>
    private int RemoveRegistry(UnItem item)
    {
        var (hive, rest) = SplitHive(item.Path);
        if (hive is null)
        {
            return 0;
        }

        try
        {
            var isMachine = item.Path.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);
            if (isMachine && !FileOps.IsElevated)
            {
                return 2;
            }

            using var root = RegistryKey.OpenBaseKey(hive.Value, item.Path.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase) ? RegistryView.Registry32 : RegistryView.Registry64);
            if (item.Kind == UnKind.RegistryKey)
            {
                var clean = rest.Replace(@"WOW6432Node\", string.Empty, StringComparison.OrdinalIgnoreCase);
                root.DeleteSubKeyTree(item.Path.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase) ? clean : rest, false);
            }
            else
            {
                var cleanRest = item.Path.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase) ? rest.Replace(@"WOW6432Node\", string.Empty, StringComparison.OrdinalIgnoreCase) : rest;
                using var key = root.OpenSubKey(cleanRest, true);
                key?.DeleteValue(item.ValueName ?? string.Empty, false);
            }

            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            return 2;
        }
        catch (System.Security.SecurityException)
        {
            return 2;
        }
        catch
        {
            return 0;
        }
    }

    private static (RegistryHive? Hive, string Remainder) SplitHive(string path)
    {
        var slash = path.IndexOf('\\');
        if (slash < 0)
        {
            return (null, string.Empty);
        }

        var head = path[..slash];
        var rest = path[(slash + 1)..];
        return head.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" => (RegistryHive.CurrentUser, rest),
            "HKEY_LOCAL_MACHINE" => (RegistryHive.LocalMachine, rest),
            _ => (null, string.Empty),
        };
    }

    // ------------------------------------------------------------------ the program's own uninstaller

    private static bool StillInstalled(InstalledProgram program)
    {
        if (program.IsAppx)
        {
            try
            {
                var manager = new Windows.Management.Deployment.PackageManager();
                return manager.FindPackagesForUser(string.Empty, program.PackageFamilyName).Any();
            }
            catch
            {
                return false;
            }
        }

        try
        {
            using var root = RegistryKey.OpenBaseKey(program.Hive, program.View);
            var sub = program.RegistryPath[(program.RegistryPath.IndexOf('\\') + 1)..].Replace(@"WOW6432Node\", string.Empty, StringComparison.OrdinalIgnoreCase);
            using var key = root.OpenSubKey(sub);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> UninstallProgramAsync(InstalledProgram program, bool silent)
    {
        // resolved before the uninstaller runs: afterwards the folder is gone and could no longer be validated
        var programs = _programs;
        var usableLocation = await Task.Run(() => UninstallerSupport.UsableInstallLocation(program, programs, _ => { })).ConfigureAwait(true);
        await Task.Delay(300).ConfigureAwait(true);

        // 1. packaged apps
        if (program.IsAppx)
        {
            try
            {
                var manager = new Windows.Management.Deployment.PackageManager();
                var result = await manager.RemovePackageAsync(program.PackageFullName);
                return string.IsNullOrEmpty(result.ErrorText);
            }
            catch
            {
                return false;
            }
        }

        // 2. winget owns the program
        if (Package == PackageCheck.Managed && WingetId is { } id)
        {
            var finished = new TaskCompletionSource<OpState>();
            void OnFinished(OperationRequest request, OpState state) => finished.TrySetResult(state);
            WingetOperations.Shared.Finished += OnFinished;
            try
            {
                var started = WingetOperations.Shared.Start(new OperationRequest(PkgAction.Uninstall, WingetName ?? program.Name, id, [WingetOperations.UninstallArgs(id, "winget")]));
                if (!started)
                {
                    return false;
                }

                var state = await finished.Task.ConfigureAwait(true);
                await Task.Delay(500).ConfigureAwait(true);
                return state == OpState.Succeeded && !StillInstalled(program);
            }
            finally
            {
                WingetOperations.Shared.Finished -= OnFinished;
            }
        }

        // 3. MSI, quiet or regular uninstall string
        string? exe;
        string args;
        if (program.ProductCode is { } code && (program.WindowsInstaller || program.UninstallString.Contains("msiexec", StringComparison.OrdinalIgnoreCase)))
        {
            exe = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
            args = $"/x {code} {(silent ? "/qn" : "/passive")} /norestart";
        }
        else
        {
            var command = silent && program.QuietUninstallString.Length > 0 ? program.QuietUninstallString : program.UninstallString;
            (exe, args) = SplitCommand(command);
        }

        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        var exit = await Task.Run(() =>
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) is { Length: > 0 } wd && Directory.Exists(wd) ? wd : Environment.SystemDirectory });
                if (process is null)
                {
                    return -1;
                }

                process.WaitForExit();
                return process.ExitCode;
            }
            catch
            {
                return -1;
            }
        }).ConfigureAwait(true);

        if (exit is 3010 or 1641)
        {
            RebootRequired = true;
        }

        // self-deleting uninstallers spawn a child: poll for the uninstall key / install folder for up to 30 s
        for (var i = 0; i < 30; i++)
        {
            if (!StillInstalled(program))
            {
                return true;
            }

            if (exit == 0 && !string.IsNullOrEmpty(usableLocation) && !Directory.Exists(usableLocation))
            {
                return true;
            }

            await Task.Delay(1000).ConfigureAwait(true);
        }

        return !StillInstalled(program);
    }

    /// <summary>Splits an uninstall command line into the executable and its arguments (CommandLineToArgvW rules).</summary>
    public static (string? Exe, string Args) SplitCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return (null, string.Empty);
        }

        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end > 1)
            {
                return (text[1..end], text[(end + 1)..].Trim());
            }
        }

        var match = System.Text.RegularExpressions.Regex.Match(text, @"^(.*?\.(exe|msi|bat|cmd))(\s+(.*))?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var exe = match.Groups[1].Value;
            if (exe.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                return (Path.Combine(Environment.SystemDirectory, "msiexec.exe"), $"/i \"{exe}\" {match.Groups[4].Value}".Trim());
            }

            return (exe, match.Groups[4].Value);
        }

        var pointer = Win32.CommandLineToArgvW(text, out var count);
        if (pointer == 0 || count == 0)
        {
            return (null, string.Empty);
        }

        try
        {
            var first = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, 0));
            var rest = text.Length > (first?.Length ?? 0) ? text[(first?.Length ?? 0)..].Trim() : string.Empty;
            return (first, rest);
        }
        finally
        {
            Win32.LocalFree(pointer);
        }
    }
}
