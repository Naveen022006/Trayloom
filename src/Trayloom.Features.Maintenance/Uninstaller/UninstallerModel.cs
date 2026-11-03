// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

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

    public async Task RemoveSelectedAsync(bool silent)
    {
        if (!CanRemove || Target is null)
        {
            return;
        }

        var target = Target;
        var selected = Items.Where(i => i.Include).ToList();
        Phase = UnPhase.Removing;
        RemovingMessage = selected.Any(i => i.Category == UnCategory.Application) ? $"Running the uninstaller for {target.Name}…" : "Removing…";
        Failed = [];
        RebootRequired = false;
        BackupFolder = null;
        Raise();

        long freed = 0;
        var failed = new List<UnItem>();
        try
        {
            var application = selected.FirstOrDefault(i => i.Category == UnCategory.Application);
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
            var (done, bad, backup, stubborn) = await Task.Run(() => RemoveItems(rest)).ConfigureAwait(true);
            freed += done;
            failed.AddRange(bad);
            BackupFolder = backup;

            if (stubborn.Count > 0)
            {
                var result = await Task.Run(() => Elevated.Remove(stubborn.Select(ToElevated).ToList())).ConfigureAwait(true);
                if (result is null)
                {
                    failed.AddRange(stubborn);
                }
                else
                {
                    foreach (var item in stubborn)
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
                }
            }
        }
        catch (Exception ex)
        {
            Host.Log("Uninstall failed", ex);
            failed.AddRange(selected.Where(s => !failed.Contains(s)).Take(1));
        }

        Freed = freed;
        Failed = failed;
        FailedNeedsAdmin = failed.Any(f => f.NeedsAdmin) && !FileOps.IsElevated;
        Items = [];
        Phase = UnPhase.Done;
        Raise();
    }

    private static Elevated.Item ToElevated(UnItem item) => item.Kind switch
    {
        UnKind.RegistryKey => new Elevated.Item("reg", "Registry::" + item.Path),
        UnKind.RegistryValue => new Elevated.Item("regvalue", "Registry::" + item.Path, item.ValueName),
        UnKind.Task => new Elevated.Item("task", item.Path),
        UnKind.Service => new Elevated.Item("service", item.Path),
        _ => new Elevated.Item("file", item.Path, null, true),
    };

    private (long Freed, List<UnItem> Failed, string? Backup, List<UnItem> Stubborn) RemoveItems(List<UnItem> items)
    {
        long freed = 0;
        var failed = new List<UnItem>();
        var stubborn = new List<UnItem>();
        string? backup = null;
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

                        if (FileOps.Recycle(item.Path))
                        {
                            freed += item.Size;
                        }
                        else if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
                        {
                            freed += item.Size;
                        }
                        else
                        {
                            stubborn.Add(item);
                        }

                        break;
                    }

                    case UnKind.RegistryKey:
                    case UnKind.RegistryValue:
                    {
                        var outcome = RemoveRegistry(item, ref backup);
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

        return (freed, failed, backup, stubborn);
    }

    private bool StillSafe(UnItem item)
    {
        if (!FileOps.IsUnder(item.Path, item.Root ?? string.Empty) && item.Root is not null)
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

    /// <summary>0 failed, 1 removed, 2 needs administrator rights.</summary>
    private int RemoveRegistry(UnItem item, ref string? backup)
    {
        var (hive, rest) = SplitHive(item.Path);
        if (hive is null)
        {
            return 0;
        }

        try
        {
            var folder = Path.Combine(FileOps.AppData, "Trayloom", "uninstaller", "backups");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{Path.GetFileName(Target?.Name ?? "app")}-{DateTime.Now:yyyyMMdd-HHmmss}.reg".Replace(' ', '-'));
            var exportKey = item.Kind == UnKind.RegistryKey ? item.Path : item.Path;
            var info = new ProcessStartInfo("reg.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("export");
            info.ArgumentList.Add(exportKey);
            info.ArgumentList.Add(file);
            info.ArgumentList.Add("/y");
            using (var process = Process.Start(info))
            {
                process?.WaitForExit(20_000);
                if (process is null || process.ExitCode != 0)
                {
                    return 0;
                }
            }

            backup = folder;
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

    private void CloseProcesses(InstalledProgram program)
    {
        if (string.IsNullOrEmpty(program.InstallLocation) || !Directory.Exists(program.InstallLocation))
        {
            return;
        }

        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == self)
                {
                    continue;
                }

                var path = process.MainModule?.FileName;
                if (path is null || !FileOps.IsUnder(path, program.InstallLocation))
                {
                    continue;
                }

                process.CloseMainWindow();
                if (!process.WaitForExit(3000))
                {
                    process.Kill();
                }
            }
            catch
            {
                // protected process
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private async Task<bool> UninstallProgramAsync(InstalledProgram program, bool silent)
    {
        await Task.Run(() => CloseProcesses(program)).ConfigureAwait(true);
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
            exe = "msiexec.exe";
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

            if (exit == 0 && string.IsNullOrEmpty(program.InstallLocation) == false && !Directory.Exists(program.InstallLocation))
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
                return ("msiexec.exe", $"/i \"{exe}\" {match.Groups[4].Value}".Trim());
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
