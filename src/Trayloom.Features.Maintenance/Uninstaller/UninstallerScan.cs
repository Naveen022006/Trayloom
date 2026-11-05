// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Trayloom.Core;

namespace Trayloom.Features.Maintenance;

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

    /// <summary>Why a removal failed for a reason an administrator prompt cannot fix ("TooLarge", "Refused", "NoBackup"); null otherwise.</summary>
    public string? FailReason { get; set; }

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
        RefusalKind.NotAnApp => Loc.T("Only programs can be uninstalled."),
        RefusalKind.SystemApp => Loc.F("{0} can’t be uninstalled because it’s part of Windows.", Name),
        RefusalKind.Shared => Loc.F("{0} can’t be uninstalled here because it shares files with Windows or other apps.", Name),
        RefusalKind.Nested => Loc.F("{0} can’t be uninstalled on its own because it’s part of another app.", Name),
        RefusalKind.ThisApp => Loc.T("Trayloom can’t uninstall itself here. Use Settings › Advanced instead."),
        RefusalKind.Link => Loc.F("{0} can’t be uninstalled because it’s a link to an app stored elsewhere.", Name),
        _ => Loc.F("{0} can’t be uninstalled because it can’t be identified as a program.", Name),
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
        return program.Name.Equals("Trayloom", StringComparison.OrdinalIgnoreCase)
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle file, StringBuilder path, uint size, uint flags);

    private static readonly Regex DriveLetterPath = new(@"^[A-Za-z]:\\", RegexOptions.Compiled);

    /// <summary>
    /// Turns a path taken from the registry or a scheduled task into a plain normalised path: environment variables expanded, the NT
    /// prefixes <c>\??\</c>, <c>\\?\</c> and <c>\\.\</c> removed, <c>\SystemRoot\</c> and a leading <c>System32\</c> mapped to the Windows
    /// folder. Returns an empty string when the result is not a local drive-letter path.
    /// </summary>
    public static string NormalizeTarget(string? text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var value = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"')).Replace('/', '\\');
            foreach (var prefix in new[] { @"\??\", @"\\?\", @"\\.\" })
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    value = value[prefix.Length..];
                    break;
                }
            }

            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (value.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                value = Path.Combine(windows, value[@"\SystemRoot\".Length..]);
            }
            else if (value.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase) || value.StartsWith(@"SysWOW64\", StringComparison.OrdinalIgnoreCase))
            {
                value = Path.Combine(windows, value);
            }

            return DriveLetterPath.IsMatch(value) && value.IndexOf(':', 2) < 0 ? SafePaths.Normalize(value) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// True when a scan target (a service image, task action, Run value, firewall program or shortcut target) lies under
    /// <paramref name="install"/> after normalisation, and never when it lies under the Windows folder.
    /// </summary>
    public static bool TargetUnder(string? target, string install)
    {
        if (string.IsNullOrEmpty(install))
        {
            return false;
        }

        var normalized = NormalizeTarget(target);
        return normalized.Length > 0 && !SafePaths.IsUnderWindows(normalized) && SafePaths.IsUnder(normalized, install);
    }

    /// <summary>The real final path of a folder (links in any parent resolved), or empty when it cannot be read.</summary>
    internal static string FinalPath(string folder)
    {
        try
        {
            // FILE_FLAG_BACKUP_SEMANTICS opens a directory; the handle follows links on purpose so the final target is reported
            using var handle = Win32.CreateFileW(folder, 0, 7, nint.Zero, 3, 0x02000000, nint.Zero);
            if (handle.IsInvalid)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder(1024);
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity)
            {
                return string.Empty;
            }

            var text = buffer.ToString();
            return text.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? string.Empty : text.StartsWith(@"\\?\", StringComparison.Ordinal) ? text[4..] : text;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static readonly string[] MicrosoftOwnedProgramFolders =
    [
        "Windows Defender", "Windows Defender Advanced Threat Protection", "Windows NT", "Windows Mail", "Windows Media Player", "Windows Photo Viewer", "Windows Sidebar",
        "Windows Portable Devices", "Windows Security", "Windows Kits", "WindowsPowerShell", "Internet Explorer", "dotnet", "Reference Assemblies", "MSBuild", "Microsoft",
        "Uninstall Information", "WindowsApps", "ModifiableWindowsApps", "Common Files",
    ];

    /// <summary>Folders an installer must never be allowed to claim as its own (beyond the critical roots): shared, Microsoft-owned or temp.</summary>
    private static bool IsBroadFolder(string full)
    {
        string Special(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var programFiles = new[] { Special(Environment.SpecialFolder.ProgramFiles), Special(Environment.SpecialFolder.ProgramFilesX86) }.Where(p => p.Length > 0).ToList();
        var local = Special(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Special(Environment.SpecialFolder.ApplicationData);
        var programData = Special(Environment.SpecialFolder.CommonApplicationData);
        var systemDrive = Path.GetPathRoot(Special(Environment.SpecialFolder.Windows)) ?? @"C:\";

        // app packages and the user's alias folder
        var packaged = programFiles.SelectMany(p => new[] { Path.Combine(p, "WindowsApps"), Path.Combine(p, "ModifiableWindowsApps") }).ToList();
        if (local.Length > 0)
        {
            packaged.Add(Path.Combine(local, "Microsoft", "WindowsApps"));
        }

        if (packaged.Any(p => SafePaths.IsUnder(full, p)))
        {
            return true;
        }

        // the Windows-owned parts of the profile and ProgramData
        var windowsOwned = new List<string>();
        foreach (var (root, _) in new[] { (local, 0), (roaming, 0), (programData, 0) })
        {
            if (root.Length > 0)
            {
                windowsOwned.Add(Path.Combine(root, "Microsoft", "Windows"));
            }
        }

        if (windowsOwned.Any(p => SafePaths.IsUnder(full, p)))
        {
            return true;
        }

        // the temp folder itself, and anything the Cleaner treats as a temp root
        if (string.Equals(full, SafePaths.Normalize(FileOps.Temp), StringComparison.OrdinalIgnoreCase) || FileOps.IsSafeTempRoot(full))
        {
            return true;
        }

        var parent = Path.GetDirectoryName(full) ?? string.Empty;
        if (parent.Length > 0)
        {
            // direct children of either Common Files, other users' profiles, and Microsoft-owned Program Files folders
            var commonFiles = new[] { Special(Environment.SpecialFolder.CommonProgramFiles), Special(Environment.SpecialFolder.CommonProgramFilesX86) };
            if (commonFiles.Any(c => c.Length > 0 && SafePaths.IsUnder(full, c)))
            {
                return true;
            }

            if (string.Equals(parent, SafePaths.Normalize(Path.Combine(systemDrive, "Users")), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (programFiles.Any(p => string.Equals(parent, SafePaths.Normalize(p), StringComparison.OrdinalIgnoreCase))
                && MicrosoftOwnedProgramFolders.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The folders another program's registry entry points into: install location, icon folder, uninstaller folders.</summary>
    private static IEnumerable<string> ClaimedFolders(InstalledProgram other)
    {
        var candidates = new List<string>();
        void Add(string? path)
        {
            var normalized = NormalizeTarget(path);
            if (normalized.Length > 0)
            {
                candidates.Add(normalized);
            }
        }

        Add(other.InstallLocation);
        foreach (var file in new[] { Icons.CleanIconPath(other.DisplayIcon), CleanerScanner.ExecutableOf(other.UninstallString), CleanerScanner.ExecutableOf(other.QuietUninstallString) })
        {
            var normalized = NormalizeTarget(file);
            if (normalized.Length > 0 && !SafePaths.IsUnderWindows(normalized) && Path.GetDirectoryName(normalized) is { Length: > 0 } dir)
            {
                candidates.Add(dir);
            }
        }

        return candidates.Where(c => !SafePaths.IsUnderWindows(c));
    }

    /// <summary>
    /// The install folder of a program when it is safe to treat as the program's own: an existing real folder (no link in any part of the
    /// path, checked through its final path) at least two levels deep, that is not a critical root (or an ancestor of one), not under
    /// Windows, not a shared, Microsoft-owned or temp folder, and not shared with another listed program (by install location, icon
    /// folder or uninstaller folder; packaged apps included). Returns an empty string otherwise, so a broad or forged
    /// <c>InstallLocation</c> can never drive a kill or a removal. The log line carries the reason only, never the path.
    /// Touches the disk: call it off the UI thread.
    /// </summary>
    public static string UsableInstallLocation(InstalledProgram p, IReadOnlyList<InstalledProgram> all, Action<string>? log = null)
    {
        log ??= m => Host.Log(m);
        var raw = p.InstallLocation;
        if (p.IsAppx || string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string Refuse(string reason)
        {
            log($"Uninstaller: install location of {p.Name} not used ({reason})");
            return string.Empty;
        }

        string full;
        try
        {
            var text = raw.Trim().Trim('"').Replace('/', '\\');
            if (text.Any(char.IsControl) || !DriveLetterPath.IsMatch(text) || text.IndexOf(':', 2) >= 0)
            {
                return Refuse("not a local drive path");
            }

            full = SafePaths.Normalize(text);
            if (!Directory.Exists(full))
            {
                return Refuse("folder missing");
            }

            if (Win32.IsReparsePoint(full))
            {
                return Refuse("link");
            }

            var final = FinalPath(full);
            if (final.Length == 0 || !string.Equals(SafePaths.Normalize(final), full, StringComparison.OrdinalIgnoreCase))
            {
                // a link in a parent folder, a substituted drive or stream syntax
                return Refuse("path is an alias");
            }
        }
        catch
        {
            return Refuse("unreadable");
        }

        if (SafePaths.IsCriticalOrAncestor(full) || SafePaths.IsUnderWindows(full))
        {
            return Refuse("critical folder");
        }

        if (SafePaths.IsSharedSystemFolder(full) || IsBroadFolder(full))
        {
            return Refuse("shared system folder");
        }

        if (SafePaths.SegmentCount(full) < 2)
        {
            return Refuse("too shallow");
        }

        foreach (var other in all)
        {
            if (ReferenceEquals(other, p) || other.Identity == p.Identity)
            {
                continue;
            }

            if (ClaimedFolders(other).Any(c => SafePaths.IsUnder(c, full)))
            {
                return Refuse("shared with another program");
            }
        }

        return full;
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
        "Package Cache", "SystemApps", "Trayloom", "Sentry", "CrashPad", "Squirrel", "SquirrelTemp", "WinSparkle", "Crashlytics", "Classes", "Policies", "WOW6432Node", "Clients",
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

    /// <summary>The usable install folder; empty when the registry value was missing or too broad (see <see cref="UninstallerSupport.UsableInstallLocation"/>).</summary>
    public string InstallFolder => _install;

    public LeftoverScanner(InstalledProgram program, List<InstalledProgram> all, CancellationToken ct)
    {
        _p = program;
        _ct = ct;
        _others = all.Where(o => !ReferenceEquals(o, program) && o.Identity != program.Identity).ToList();
        _install = UninstallerSupport.UsableInstallLocation(program, all).TrimEnd('\\');
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

    private UnMatch? MatchName(string name, bool allowPublisher = true)
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

        if (allowPublisher && _publisher.Length >= 4 && n == _publisher && !PublisherShared)
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
        if (isPath && SafePaths.IsUnderWindows(path))
        {
            // nothing under %WINDIR% is ever offered
            return;
        }

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

        if (FileOps.SafeTemp is { } safeTemp && !SafePaths.IsUnderWindows(safeTemp))
        {
            ScanRoot(safeTemp, UnCategory.Caches, 1, false);
        }

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

                // a vendor-level folder (depth 2 roots) that matches only through the publisher name is never added itself (V-13); its children are searched
                if (match == UnMatch.Related && depth > 1 && MatchName(name, false) is null)
                {
                    match = null;
                }

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
                    if (UninstallerSupport.TargetUnder(target, _install))
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
                    if (UninstallerSupport.TargetUnder(target, _install))
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
                    // a vendor key that matches only through the publisher name is never added itself (V-13); its children are searched
                    if (match is not null && (match == UnMatch.Exact || MatchName(vendor, false) is not null))
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
                            if (UninstallerSupport.TargetUnder(exe, _install))
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
                        if (match.Success && UninstallerSupport.TargetUnder(match.Groups[1].Value, _install))
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
                    if (UninstallerSupport.TargetUnder(exe, _install))
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
                // kernel and file-system drivers are never offered: Windows refuses to delete them and the script says no
                var type = key?.GetValue("Type") is int t ? t : 0;
                var image = CleanerScanner.ExecutableOf(key?.GetValue("ImagePath") as string ?? string.Empty) ?? (key?.GetValue("ImagePath") as string);
                if ((type & 3) == 0 && UninstallerSupport.TargetUnder(image, _install))
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
