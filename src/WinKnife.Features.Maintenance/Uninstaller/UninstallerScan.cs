// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinKnife.Features.Maintenance;

internal enum UnCategory
{
    Application,
    Support,
    Caches,
    Preferences,
    Containers,
    Logs,
    SavedState,
    Other,
}

internal enum UnMatch
{
    Exact,
    Related,
}

internal enum UnKind
{
    Program,
    Path,
    RegistryKey,
    RegistryValue,
    Task,
    Service,
}

/// <summary>One row of the uninstaller review list.</summary>
internal sealed class UnItem
{
    public Guid Id { get; } = Guid.NewGuid();

    public UnCategory Category { get; init; }

    public UnMatch Match { get; init; }

    public UnKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>File path, or "HKEY_...\key" for registry rows, or the task / service name.</summary>
    public string Path { get; init; } = string.Empty;

    public string? ValueName { get; init; }

    public long Size { get; set; }

    public bool Include { get; set; }

    public string? Identity { get; init; }

    public string? Root { get; init; }

    public bool NeedsAdmin { get; init; }

    public bool IsRegistry => Kind is UnKind.RegistryKey or UnKind.RegistryValue;
}

/// <summary>A program that passed the selection rules.</summary>
internal sealed record UnSelection(InstalledProgram Program);

/// <summary>Why a selection was refused (spec 7.2.4).</summary>
internal enum RefusalKind
{
    NotAnApp,
    SystemApp,
    Shared,
    Nested,
    ThisApp,
    Link,
    Unidentified,
}

internal sealed record UnRefusal(RefusalKind Kind, string Name)
{
    public string Message => Kind switch
    {
        RefusalKind.NotAnApp => "Only programs can be uninstalled.",
        RefusalKind.SystemApp => $"{Name} can’t be uninstalled because it’s part of Windows.",
        RefusalKind.Shared => $"{Name} can’t be uninstalled here because it shares files with Windows or other apps.",
        RefusalKind.Nested => $"{Name} can’t be uninstalled on its own because it’s part of another app.",
        RefusalKind.ThisApp => "WinKnife can’t uninstall itself here. Use Settings › Advanced instead.",
        RefusalKind.Link => $"{Name} can’t be uninstalled because it’s a link to an app stored elsewhere.",
        _ => $"{Name} can’t be uninstalled because it can’t be identified as a program.",
    };
}

/// <summary>Selection rules and drop resolution (spec 7.2.4 / 7.2.7).</summary>
internal static class UninstallerSupport
{
    private static readonly string[] Windowsy = ["Windows", "Microsoft Windows", "Microsoft Visual C++", "Microsoft .NET", "Windows Software Development Kit"];

    /// <summary>Evaluates an installed program picked from the list.</summary>
    public static (UnSelection? Selection, UnRefusal? Refusal) Evaluate(InstalledProgram program)
    {
        if (IsThisApp(program))
        {
            return (null, new UnRefusal(RefusalKind.ThisApp, program.Name));
        }

        if (program.NoRemove || Windowsy.Any(w => program.Name.StartsWith(w, StringComparison.OrdinalIgnoreCase) && program.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)))
        {
            return (null, new UnRefusal(RefusalKind.SystemApp, program.Name));
        }

        if (!program.IsAppx && string.IsNullOrWhiteSpace(program.UninstallString) && string.IsNullOrWhiteSpace(program.QuietUninstallString))
        {
            return (null, new UnRefusal(RefusalKind.Unidentified, program.Name));
        }

        return (new UnSelection(program), null);
    }

    public static bool IsThisApp(InstalledProgram program)
    {
        var self = Environment.ProcessPath;
        var folder = self is null ? null : Path.GetDirectoryName(self);
        return program.Name.Equals("WinKnife", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(program.InstallLocation) && FileOps.IsUnder(folder, program.InstallLocation));
    }

    /// <summary>Resolves a dropped or chosen file to a program (shortcut targets are resolved, never refused).</summary>
    public static (UnSelection? Selection, UnRefusal? Refusal) Evaluate(string path, List<InstalledProgram> programs)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return (null, new UnRefusal(RefusalKind.NotAnApp, name));
            }

            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                return (null, new UnRefusal(RefusalKind.Link, name));
            }

            var resolved = path;
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                resolved = CleanerScanner.ShortcutTarget(path) ?? path;
            }

            InstalledProgram? match = null;
            if (resolved.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                var code = MsiProductCode(resolved);
                match = programs.FirstOrDefault(p => code is not null && string.Equals(p.ProductCode, code, StringComparison.OrdinalIgnoreCase));
            }
            else if (Directory.Exists(resolved))
            {
                match = programs.FirstOrDefault(p => p.InstallLocation.Length > 0 && string.Equals(p.InstallLocation.TrimEnd('\\'), resolved.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            }
            else if (resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                match = programs.FirstOrDefault(p => !p.IsAppx && Icons.CleanIconPath(p.DisplayIcon)?.Equals(resolved, StringComparison.OrdinalIgnoreCase) == true)
                    ?? programs.Where(p => p.InstallLocation.Length > 0 && FileOps.IsUnder(resolved, p.InstallLocation)).OrderByDescending(p => p.InstallLocation.Length).FirstOrDefault();
            }

            if (match is null)
            {
                return (null, new UnRefusal(resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || resolved.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? RefusalKind.Unidentified : RefusalKind.NotAnApp, Path.GetFileNameWithoutExtension(resolved)));
            }

            // an exe inside another program's folder that is not its main program is "part of another app"
            if (resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && match.InstallLocation.Length > 0
                && Path.GetDirectoryName(resolved)?.TrimEnd('\\').Length > match.InstallLocation.TrimEnd('\\').Length + 1
                && Icons.CleanIconPath(match.DisplayIcon)?.Equals(resolved, StringComparison.OrdinalIgnoreCase) != true)
            {
                return (null, new UnRefusal(RefusalKind.Nested, match.Name));
            }

            return Evaluate(match);
        }
        catch
        {
            return (null, new UnRefusal(RefusalKind.Unidentified, name));
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiOpenPackageW(string path, out nint handle);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiGetProductPropertyW(nint handle, string name, StringBuilder value, ref int size);

    [DllImport("msi.dll")] private static extern uint MsiCloseHandle(nint handle);

    private static string? MsiProductCode(string path)
    {
        if (MsiOpenPackageW(path, out var handle) != 0)
        {
            return null;
        }

        try
        {
            var builder = new StringBuilder(64);
            var size = builder.Capacity;
            return MsiGetProductPropertyW(handle, "ProductCode", builder, ref size) == 0 ? builder.ToString() : null;
        }
        finally
        {
            MsiCloseHandle(handle);
        }
    }

    /// <summary>The programs the picker offers: the ones the selection check accepts, sorted by name.</summary>
    public static List<InstalledProgram> Offered(List<InstalledProgram> programs) =>
        programs.Where(p => Evaluate(p).Selection is not null && p.Publisher != "Microsoft Windows")
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
}

/// <summary>Finds what a program leaves behind (spec 7.2.5 Windows table). Exact matches start checked, related ones do not.</summary>
internal sealed class LeftoverScanner
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "mac", "macos", "osx", "helper", "agent", "daemon", "service", "desktop", "client", "launcher", "plugin", "extension", "web", "free", "pro", "lite", "plus", "ui", "xpc", "login",
        "updater", "installer", "renderer", "gpu", "worker", "crashpad", "broker", "utility", "alert", "network", "audio", "server", "shared", "core", "common", "framework", "bundle", "process",
        "handler", "tool", "runtime", "electron", "java", "python", "node", "mono", "wine", "microsoft", "windows", "system", "program", "programs", "files", "data", "temp", "cache",
    };

    private static readonly HashSet<string> ProtectedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "WindowsApps", "Packages", "Common Files", "Programs", "Temp", "Google", "Adobe", "Intel", "NVIDIA", "NVIDIA Corporation", "AMD", "Apple", "Oracle", "Java",
        "Package Cache", "SystemApps", "WinKnife", "Sentry", "CrashPad", "Squirrel", "SquirrelTemp", "WinSparkle", "Crashlytics", "Classes", "Policies", "WOW6432Node", "Clients",
    };

    private readonly InstalledProgram _p;
    private readonly List<InstalledProgram> _others;
    private readonly CancellationToken _ct;
    private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _exes = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _publisher;
    private readonly string _install;
    private readonly List<UnItem> _found = [];

    public LeftoverScanner(InstalledProgram program, List<InstalledProgram> all, CancellationToken ct)
    {
        _p = program;
        _ct = ct;
        _others = all.Where(o => !ReferenceEquals(o, program) && o.Identity != program.Identity).ToList();
        _install = program.InstallLocation.TrimEnd('\\');
        _publisher = Norm.Of(program.Publisher);

        void Id(string? text)
        {
            var n = Norm.Of(text?.Trim('{', '}'));
            if (n.Length >= 4)
            {
                _ids.Add(n);
            }
        }

        void Token(string? text)
        {
            foreach (var n in new[] { Norm.Of(text), Norm.Stripped(text) })
            {
                if (n.Length >= 3 && !Ignored.Contains(n))
                {
                    _tokens.Add(n);
                }
            }
        }

        Id(program.Key);
        Id(program.PackageFamilyName);
        Id(program.PackageFullName);
        Token(program.Name);
        if (_install.Length > 0)
        {
            Token(Path.GetFileName(_install));
            try
            {
                if (Directory.Exists(_install))
                {
                    foreach (var exe in Directory.EnumerateFiles(_install, "*.exe", SearchOption.TopDirectoryOnly).Take(40))
                    {
                        var b = Path.GetFileNameWithoutExtension(exe);
                        if (!b.StartsWith("unins", StringComparison.OrdinalIgnoreCase) && !b.Contains("update", StringComparison.OrdinalIgnoreCase))
                        {
                            _exes.Add(b);
                            Token(b);
                        }
                    }
                }
            }
            catch
            {
                // unreadable folder
            }
        }

        if (program.MainExecutable is { } main)
        {
            _exes.Add(Path.GetFileNameWithoutExtension(main));
            Token(Path.GetFileNameWithoutExtension(main));
        }
    }

    private bool PublisherShared => _publisher.Length > 0 && _others.Any(o => Norm.Of(o.Publisher) == _publisher);

    private UnMatch? MatchName(string name)
    {
        var n = Norm.Of(Path.GetFileNameWithoutExtension(name).Length > 0 && name.Contains('.') && name.Length - Path.GetFileNameWithoutExtension(name).Length <= 5 ? Path.GetFileNameWithoutExtension(name) : name);
        if (n.Length < 3 || ProtectedFolders.Contains(name))
        {
            return null;
        }

        if (_ids.Contains(n))
        {
            return UnMatch.Exact;
        }

        if (_tokens.Contains(n))
        {
            return OwnedByOther(n) ? null : UnMatch.Related;
        }

        foreach (var token in _tokens)
        {
            if (token.Length >= 5 && n.StartsWith(token) && !Regex.IsMatch(name, @"[_-]\d{4}-\d{2}-\d{2}") && !OwnedByOther(n))
            {
                return UnMatch.Related;
            }
        }

        if (_publisher.Length >= 4 && n == _publisher && !PublisherShared)
        {
            return UnMatch.Related;
        }

        return null;
    }

    /// <summary>The name also belongs to another installed program (shared vendor folders).</summary>
    private bool OwnedByOther(string normalized) =>
        _others.Any(o => Norm.Of(o.Name) == normalized || Norm.Stripped(o.Name) == normalized || Norm.Of(Path.GetFileName(o.InstallLocation.TrimEnd('\\'))) == normalized);

    private void Add(UnCategory category, UnMatch match, UnKind kind, string name, string path, string? root, bool admin = false, long size = -1, string? value = null)
    {
        if (_found.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && f.ValueName == value))
        {
            return;
        }

        var isPath = kind == UnKind.Path;
        _found.Add(new UnItem
        {
            Category = category,
            Match = match,
            Kind = kind,
            Name = name,
            Path = path,
            ValueName = value,
            Size = size >= 0 ? size : (isPath ? FileOps.DirectorySize(path, _ct) : 0),
            Include = match == UnMatch.Exact,
            Identity = isPath ? Win32.FileIdentity(path) : null,
            Root = root,
            NeedsAdmin = admin,
        });
    }

    public List<UnItem> Scan()
    {
        // 1. the application itself
        var size = _p.IsAppx ? 0 : Directory.Exists(_install) ? FileOps.DirectorySize(_install, _ct) : _p.EstimatedSizeKb * 1024;
        _found.Add(new UnItem
        {
            Category = UnCategory.Application,
            Match = UnMatch.Exact,
            Kind = UnKind.Program,
            Name = _p.Name,
            Path = string.IsNullOrEmpty(_install) ? (_p.IsAppx ? _p.PackageFullName : _p.RegistryPath) : _install,
            Size = size,
            Include = true,
            Identity = Directory.Exists(_install) ? Win32.FileIdentity(_install) : null,
            Root = _install,
        });

        var elevated = FileOps.IsElevated;
        // 2. support / caches under the user and machine data roots (depth 2 for Vendor\App)
        foreach (var (root, category) in new[]
        {
            (FileOps.AppData, UnCategory.Support), (FileOps.LocalAppData, UnCategory.Support), (FileOps.ProgramData, UnCategory.Support),
            (Path.Combine(FileOps.LocalAppData, "Programs"), UnCategory.Support),
        })
        {
            ScanRoot(root, category, 2, root.StartsWith(FileOps.ProgramData, StringComparison.OrdinalIgnoreCase));
        }

        ScanRoot(FileOps.Temp, UnCategory.Caches, 1, false);
        ScanRoot(Path.Combine(FileOps.Profile, ".config"), UnCategory.Support, 1, false);
        ScanRoot(Path.Combine(FileOps.Profile, ".cache"), UnCategory.Caches, 1, false);

        // 3. packaged app data
        if (!string.IsNullOrEmpty(_p.PackageFamilyName))
        {
            var packages = Path.Combine(FileOps.LocalAppData, "Packages", _p.PackageFamilyName);
            if (Directory.Exists(packages) && !Win32.IsReparsePoint(packages))
            {
                Add(UnCategory.Containers, UnMatch.Exact, UnKind.Path, _p.PackageFamilyName, packages, Path.Combine(FileOps.LocalAppData, "Packages"));
            }
        }

        // 4. logs / crash reports
        ScanCrashReports();

        // 5. start menu, pins, desktop shortcuts
        ScanShortcuts();

        // 6. registry (preferences + run values + firewall + shell extensions)
        ScanRegistry(elevated);

        // 7. tasks and services (inside the install folder)
        ScanTasks();
        ScanServices();

        // de-duplicate: shortest first, exact beats related, nested paths collapse
        var items = _found.OrderBy(i => i.Category == UnCategory.Application ? 0 : 1).ThenBy(i => i.Path.Length).ToList();
        var accepted = new List<UnItem>();
        foreach (var item in items)
        {
            if (item.Kind == UnKind.Path && accepted.Any(a => a.Kind is UnKind.Path or UnKind.Program && a.Category != UnCategory.Application && FileOps.IsUnder(item.Path, a.Path)))
            {
                continue;
            }

            if (item.Kind == UnKind.Path && accepted.Any(a => a.Kind == UnKind.Program && a.Path.Length > 0 && FileOps.IsUnder(item.Path, a.Path)))
            {
                continue;
            }

            accepted.Add(item);
        }

        return accepted.OrderBy(i => (int)i.Category).ThenByDescending(i => i.Size).ToList();
    }

    private void ScanRoot(string root, UnCategory category, int depth, bool admin)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            {
                _ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.') && category != UnCategory.Support || Win32.IsReparsePoint(entry) || ProtectedFolders.Contains(name) && depth == 1 && false)
                {
                    continue;
                }

                if (FileOps.IsUnder(entry, _install) && _install.Length > 0)
                {
                    continue;
                }

                var match = MatchName(name);
                if (match is not null)
                {
                    var cat = category == UnCategory.Support && name.Equals("logs", StringComparison.OrdinalIgnoreCase) ? UnCategory.Logs : category;
                    Add(cat, match.Value, UnKind.Path, name, entry, root, admin);
                    continue;
                }

                if (depth > 1 && Directory.Exists(entry) && !ProtectedFolders.Contains(name) && !name.Equals("Packages", StringComparison.OrdinalIgnoreCase))
                {
                    var vendor = Norm.Of(name);
                    // look one level into vendor folders (Vendor\App); a related vendor match never selects the vendor itself
                    foreach (var child in SafeChildren(entry))
                    {
                        var childName = Path.GetFileName(child);
                        if (Win32.IsReparsePoint(child))
                        {
                            continue;
                        }

                        var childMatch = MatchName(childName);
                        if (childMatch is not null)
                        {
                            Add(category, childMatch.Value, UnKind.Path, $"{name}\\{childName}", child, entry, admin);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // unreadable root
        }
    }

    private static IEnumerable<string> SafeChildren(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).Take(400).ToList();
        }
        catch
        {
            return [];
        }
    }

    private void ScanCrashReports()
    {
        foreach (var folder in new[] { Path.Combine(FileOps.LocalAppData, "CrashDumps"), Path.Combine(FileOps.LocalAppData, "Microsoft", "Windows", "WER", "ReportArchive") })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    var name = Path.GetFileName(entry);
                    if (_exes.Any(e => e.Length >= 3 && (name.StartsWith(e + ".", StringComparison.OrdinalIgnoreCase) || name.StartsWith(e + "_", StringComparison.OrdinalIgnoreCase) || name.Contains("_" + e + "_", StringComparison.OrdinalIgnoreCase))))
                    {
                        Add(UnCategory.Logs, UnMatch.Exact, UnKind.Path, name, entry, folder);
                    }
                }
            }
            catch
            {
                // unreadable
            }
        }
    }

    private void ScanShortcuts()
    {
        var places = new[]
        {
            Path.Combine(FileOps.AppData, @"Microsoft\Windows\Start Menu\Programs"),
            Path.Combine(FileOps.ProgramData, @"Microsoft\Windows\Start Menu\Programs"),
            Path.Combine(FileOps.AppData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
            Path.Combine(FileOps.AppData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\StartMenu"),
        };
        foreach (var place in places.Where(Directory.Exists))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(place, "*.lnk", SearchOption.AllDirectories).Take(2000))
                {
                    _ct.ThrowIfCancellationRequested();
                    var target = SafeTarget(file);
                    if (target is not null && _install.Length > 0 && FileOps.IsUnder(target, _install))
                    {
                        Add(UnCategory.SavedState, UnMatch.Exact, UnKind.Path, Path.GetFileNameWithoutExtension(file), file, place, place.StartsWith(FileOps.ProgramData, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // unreadable
            }
        }

        foreach (var desktop in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }.Where(Directory.Exists))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(desktop, "*.lnk"))
                {
                    var target = SafeTarget(file);
                    if (target is not null && _install.Length > 0 && FileOps.IsUnder(target, _install))
                    {
                        Add(UnCategory.Other, UnMatch.Exact, UnKind.Path, Path.GetFileNameWithoutExtension(file), file, desktop, desktop == Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
                    }
                }
            }
            catch
            {
                // unreadable
            }
        }
    }

    private static string? SafeTarget(string lnk)
    {
        try
        {
            return CleanerScanner.ShortcutTarget(lnk);
        }
        catch
        {
            return null;
        }
    }

    private void ScanRegistry(bool elevated)
    {
        var hives = new (RegistryHive Hive, RegistryView View, string Sub, string Label)[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64, "Software", "HKEY_CURRENT_USER"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, "SOFTWARE", "HKEY_LOCAL_MACHINE"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, "SOFTWARE", "HKEY_LOCAL_MACHINE"),
        };
        foreach (var (hive, view, sub, label) in hives)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var software = root.OpenSubKey(sub);
                if (software is null)
                {
                    continue;
                }

                foreach (var vendor in software.GetSubKeyNames())
                {
                    _ct.ThrowIfCancellationRequested();
                    if (ProtectedFolders.Contains(vendor) || vendor.Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var vendorPath = (view == RegistryView.Registry32 && hive == RegistryHive.LocalMachine ? $@"{label}\SOFTWARE\WOW6432Node\" : $@"{label}\{sub}\") + vendor;
                    var match = MatchName(vendor);
                    if (match is not null && match == UnMatch.Exact || match is not null && vendor.Length >= 3)
                    {
                        Add(UnCategory.Preferences, match.Value, UnKind.RegistryKey, vendor, vendorPath, null, hive == RegistryHive.LocalMachine);
                        continue;
                    }

                    using var vendorKey = software.OpenSubKey(vendor);
                    if (vendorKey is null)
                    {
                        continue;
                    }

                    foreach (var app in vendorKey.GetSubKeyNames())
                    {
                        var appMatch = MatchName(app);
                        if (appMatch is not null)
                        {
                            Add(UnCategory.Preferences, appMatch.Value, UnKind.RegistryKey, $"{vendor}\\{app}", $@"{vendorPath}\{app}", null, hive == RegistryHive.LocalMachine);
                        }
                    }
                }

                if (_install.Length > 0)
                {
                    using var run = root.OpenSubKey($@"{sub}\Microsoft\Windows\CurrentVersion\Run");
                    if (run is not null)
                    {
                        foreach (var name in run.GetValueNames())
                        {
                            var exe = CleanerScanner.ExecutableOf(run.GetValue(name) as string ?? string.Empty);
                            if (exe is not null && FileOps.IsUnder(exe, _install))
                            {
                                var runLabel = view == RegistryView.Registry32 && hive == RegistryHive.LocalMachine ? $@"{label}\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" : $@"{label}\{sub}\Microsoft\Windows\CurrentVersion\Run";
                                Add(UnCategory.Other, UnMatch.Exact, UnKind.RegistryValue, name, runLabel, null, hive == RegistryHive.LocalMachine, 0, name);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // unreadable hive
            }
        }

        // firewall rules pointing into the install folder
        if (_install.Length > 0)
        {
            try
            {
                using var rules = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules");
                if (rules is not null)
                {
                    foreach (var name in rules.GetValueNames())
                    {
                        var text = rules.GetValue(name) as string ?? string.Empty;
                        var match = Regex.Match(text, @"(?:^|\|)App=([^|]+)");
                        if (match.Success && FileOps.IsUnder(Environment.ExpandEnvironmentVariables(match.Groups[1].Value), _install))
                        {
                            Add(UnCategory.Other, UnMatch.Exact, UnKind.RegistryValue, $"Firewall rule {name}", @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules", null, true, 0, name);
                        }
                    }
                }
            }
            catch
            {
                // unreadable
            }
        }
    }

    private void ScanTasks()
    {
        if (_install.Length == 0)
        {
            return;
        }

        var tasks = Path.Combine(FileOps.Windows, "System32", "Tasks");
        try
        {
            foreach (var file in Directory.EnumerateFiles(tasks, "*", SearchOption.AllDirectories))
            {
                _ct.ThrowIfCancellationRequested();
                var relative = file[(tasks.Length + 1)..];
                if (relative.StartsWith("Microsoft\\", StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length > 256 * 1024)
                {
                    continue;
                }

                var xml = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(xml, @"<Command>([^<]+)</Command>"))
                {
                    var exe = Environment.ExpandEnvironmentVariables(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim('"'));
                    if (FileOps.IsUnder(exe, _install))
                    {
                        Add(UnCategory.Other, UnMatch.Exact, UnKind.Task, Path.GetFileName(file), "\\" + relative, null, true, 0);
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // tasks not readable without administrator rights
        }
    }

    private void ScanServices()
    {
        if (_install.Length == 0)
        {
            return;
        }

        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null)
            {
                return;
            }

            foreach (var name in services.GetSubKeyNames())
            {
                _ct.ThrowIfCancellationRequested();
                using var key = services.OpenSubKey(name);
                var image = CleanerScanner.ExecutableOf(key?.GetValue("ImagePath") as string ?? string.Empty);
                if (image is not null && FileOps.IsUnder(image, _install))
                {
                    Add(UnCategory.Other, UnMatch.Exact, UnKind.Service, $"Service {name}", name, null, true, 0);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // unreadable
        }
    }
}
