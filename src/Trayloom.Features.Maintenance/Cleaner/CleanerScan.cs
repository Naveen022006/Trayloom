// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Trayloom.Core;

namespace Trayloom.Features.Maintenance;

/// <summary>The nine display groups of the cleaner (spec 7.1.2), in section order.</summary>
internal enum CleanerGroup
{
    LoginItems,
    SafeCaches,
    Logs,
    Developer,
    Leftovers,
    OtherCaches,
    DeviceBackups,
    Screenshots,
    Trash,
}

/// <summary>How an item is removed.</summary>
internal enum RemoveKind
{
    /// <summary>Delete the path(s) permanently.</summary>
    Permanent,

    /// <summary>Delete the children of the folder that are older than <see cref="CleanItem.MinAgeHours"/> (the folder stays).</summary>
    PermanentContents,

    /// <summary>Delete explicit files permanently.</summary>
    PermanentFiles,

    /// <summary>Move to the Recycle Bin.</summary>
    Recycle,

    /// <summary>Delete a registry Run value (backed up as .reg first).</summary>
    RegistryValue,

    /// <summary>Empty the Recycle Bin.</summary>
    EmptyBin,
}

/// <summary>One row of the scan result.</summary>
internal sealed class CleanItem
{
    public Guid Id { get; } = Guid.NewGuid();

    public CleanerGroup Group { get; init; }

    public RemoveKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Folder or file shown under the name (the first path).</summary>
    public string Path { get; init; } = string.Empty;

    public List<string> Paths { get; init; } = [];

    public long Size { get; set; }

    public string Detail { get; init; } = string.Empty;

    public bool Recommended { get; init; }

    public bool Include { get; set; }

    public int MinAgeHours { get; init; }

    /// <summary>The scan root: the path must stay under it when removed.</summary>
    public string? Root { get; init; }

    public string? Identity { get; init; }

    public bool NeedsAdmin { get; init; }

    // registry value removal
    public RegistryHive Hive { get; init; }

    public RegistryView View { get; init; }

    public string? SubKey { get; init; }

    public string? ValueName { get; init; }
}

/// <summary>Scans the Windows junk locations (spec 7.1.7). Everything is direct enumeration, no shell.</summary>
internal sealed class CleanerScanner
{
    private static readonly string[] ChromiumCaches = ["Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage", "DawnCache", "GrShaderCache", "ShaderCache"];
    private static readonly string[] ElectronCaches = ["Cache", "Code Cache", "GPUCache", "DawnCache", "Cache_Data"];
    private static readonly Regex ScreenshotName = new(@"^Screenshot( \(\d+\)| \d{4}-\d{2}-\d{2} \d{6}( \(\d+\))?)\.(png|jpg|jpeg)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> ProtectedLeftovers = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft", "windows", "windowsapps", "packages", "common files", "programs", "temp", "microsoft edge", "microsoft onedrive", "google", "adobe", "intel", "nvidia", "nvidia corporation",
        "amd", "apple", "oracle", "java", "package cache", "systemapps", "trayloom", "sentry", "crashpad", "squirrel", "squirreltemp", "winsparkle", "crashlytics", "windows nt", "windows defender",
        "windows mail", "windows media player", "windows photo viewer", "windows security", "windows portable devices", "windows sidebar", "modifiablewindowsapps", "internet explorer", "reference assemblies",
        "dotnet", "msbuild", "microsoft sdks", "microsoft.net", "ssh", "regid.1991-06.com.microsoft", "usoshared", "usoprivate", "ubisoft", "steam", "epic", "uninstall information", "installshield installation information",
        "d3dscache", "comms", "connecteddevicesplatform", "history", "tempstate", "vmware", "virtualbox", "docker", "wsl", "git", "nodejs", "python", "visual studio", "jetbrains", "mozilla", "vivaldi", "opera software",
        "brave-browser", "bravesoftware", "discord", "slack", "spotify", "zoom", "dropbox", "onedrive", "teams", "code", "npm", "yarn", "pip", "cache", "caches", "logs", "temp", "ms-playwright", "mongodb", "postgresql", "mysql",
    };

    private static readonly HashSet<string> HiddenCaches = new(StringComparer.OrdinalIgnoreCase)
    {
        "trayloom", "fonts", "themes", "cloudstore", "avid", "native instruments", "fabfilter", "spotify",
    };

    private readonly CancellationToken _ct;
    private readonly bool _attended;
    private readonly int _screenshotDays;
    private readonly Action<string> _progress;
    private readonly bool _elevated = FileOps.IsElevated;
    private readonly HashSet<string> _claimed = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _owners;
    private List<InstalledProgram>? _programs;

    public CleanerScanner(bool attended, int screenshotDays, Action<string> progress, CancellationToken ct)
    {
        _attended = attended;
        _screenshotDays = screenshotDays;
        _progress = progress;
        _ct = ct;
    }

    public List<CleanItem> Scan()
    {
        var items = new List<CleanItem>();
        Step(Loc.T("Leftovers from uninstalled apps"), () => items.AddRange(Leftovers()));
        Step(Loc.T("Orphaned startup items"), () => items.AddRange(StartupItems()));
        Step(Loc.T("Caches"), () => items.AddRange(Caches()));
        Step(Loc.T("Logs"), () => items.AddRange(Logs()));
        Step(Loc.T("Developer junk"), () => items.AddRange(Developer()));
        Step(Loc.T("Recycle Bin"), () => items.AddRange(Bin()));
        Step(Loc.T("iPhone backups"), () => items.AddRange(DeviceBackups()));
        if (_attended && _screenshotDays > 0)
        {
            Step(Loc.T("Forgotten screenshots"), () => items.AddRange(Screenshots()));
        }

        foreach (var item in items)
        {
            item.Include = item.Recommended;
        }

        return items;
    }

    private void Step(string title, Action work)
    {
        _ct.ThrowIfCancellationRequested();
        _progress(title);
        try
        {
            work();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // one unreadable category must not break the scan
        }
    }

    private static bool Exists(string path) => !string.IsNullOrEmpty(path) && (Directory.Exists(path) || File.Exists(path));

    private CleanItem? Folder(CleanerGroup group, string name, string path, string detail, bool recommended, RemoveKind kind = RemoveKind.Permanent, string? root = null, bool admin = false, int minAgeHours = 0)
    {
        if (!Directory.Exists(path) || Win32.IsReparsePoint(path))
        {
            return null;
        }

        var size = kind == RemoveKind.PermanentContents ? ContentsSize(path, minAgeHours) : FileOps.DirectorySize(path, _ct);
        if (size <= 0)
        {
            return null;
        }

        return new CleanItem
        {
            Group = group,
            Kind = kind,
            Name = name,
            Path = path,
            Paths = [path],
            Size = size,
            Detail = detail,
            Recommended = recommended,
            Root = root ?? path,
            Identity = Win32.FileIdentity(path),
            NeedsAdmin = admin,
            MinAgeHours = minAgeHours,
        };
    }

    private long ContentsSize(string path, int minAgeHours)
    {
        long total = 0;
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-minAgeHours);
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                _ct.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LastWriteTimeUtc > cutoff)
                {
                    continue;
                }

                total += entry is FileInfo file ? file.Length : FileOps.DirectorySize(entry.FullName, _ct);
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

        return total;
    }

    private CleanItem? Multi(CleanerGroup group, string name, IEnumerable<string> paths, string detail, bool recommended, string root, bool admin = false)
    {
        var list = paths.Where(p => Directory.Exists(p) && !Win32.IsReparsePoint(p)).ToList();
        if (list.Count == 0)
        {
            return null;
        }

        long size = 0;
        foreach (var p in list)
        {
            size += FileOps.DirectorySize(p, _ct);
        }

        if (size <= 0)
        {
            return null;
        }

        return new CleanItem { Group = group, Kind = RemoveKind.Permanent, Name = name, Path = list[0], Paths = list, Size = size, Detail = detail, Recommended = recommended, Root = root, NeedsAdmin = admin };
    }

    // ------------------------------------------------------------------ caches

    private IEnumerable<CleanItem> Caches()
    {
        var local = FileOps.LocalAppData;
        var roaming = FileOps.AppData;
        var list = new List<CleanItem?>();

        // TEMP/TMP can point at the profile or a data folder; only a folder that really is a Temp folder is ever emptied.
        var safeTemp = FileOps.SafeTemp;
        if (safeTemp is { } temp)
        {
            list.Add(Folder(CleanerGroup.SafeCaches, "Temporary files", temp, "Temp", true, RemoveKind.PermanentContents, temp, false, 1));
        }
        else
        {
            Host.Log("Cleaner: Temp folder skipped (not a Temp folder)");
        }

        var localTemp = Path.Combine(local, "Temp");
        if (safeTemp is null || !string.Equals(Path.GetFullPath(safeTemp), Path.GetFullPath(localTemp), StringComparison.OrdinalIgnoreCase))
        {
            if (FileOps.IsSafeTempRoot(localTemp))
            {
                list.Add(Folder(CleanerGroup.SafeCaches, "Local temporary files", localTemp, "Temp", true, RemoveKind.PermanentContents, localTemp, false, 1));
            }
        }

        // Content.Outlook holds Outlook attachments that were opened from mail, so it is never offered.
        var inet = Path.Combine(local, "Microsoft", "Windows", "INetCache");
        if (Directory.Exists(inet) && !Win32.IsReparsePoint(inet))
        {
            list.Add(Multi(CleanerGroup.SafeCaches, "Internet cache", InternetCacheFolders(inet), "INetCache", true, inet));
        }

        list.Add(Folder(CleanerGroup.SafeCaches, "Shader cache", Path.Combine(local, "D3DSCache"), "D3DSCache", true));
        list.Add(Thumbnails());

        // Chromium browsers per profile
        var chromium = new (string Name, string UserData)[]
        {
            (Loc.T("Google Chrome"), Path.Combine(local, "Google", "Chrome", "User Data")),
            (Loc.T("Microsoft Edge"), Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            ("Vivaldi", Path.Combine(local, "Vivaldi", "User Data")),
        };
        foreach (var (name, userData) in chromium)
        {
            if (!Directory.Exists(userData))
            {
                continue;
            }

            var paths = new List<string>();
            foreach (var profile in Directory.EnumerateDirectories(userData).Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal)))
            {
                paths.AddRange(ChromiumCaches.Select(c => Path.Combine(profile, c)));
            }

            list.Add(Multi(CleanerGroup.SafeCaches, $"{name} cache", paths, name, true, userData));
        }

        var opera = Path.Combine(roaming, "Opera Software", "Opera Stable");
        list.Add(Multi(CleanerGroup.SafeCaches, "Opera cache", new[] { "Cache", "Code Cache", "GPUCache" }.Select(c => Path.Combine(opera, c)), "Opera", true, opera));
        var firefox = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefox))
        {
            list.Add(Multi(CleanerGroup.SafeCaches, "Firefox cache", Directory.EnumerateDirectories(firefox).Select(d => Path.Combine(d, "cache2")), "Firefox", true, firefox));
        }

        // Electron / Chromium based apps: %APPDATA%\<App>\Cache...
        if (Directory.Exists(roaming))
        {
            foreach (var app in Directory.EnumerateDirectories(roaming))
            {
                _ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(app);
                if (HiddenCaches.Contains(name) || name.EndsWith(".localized", StringComparison.OrdinalIgnoreCase) || name.Equals("Opera Software", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) || Win32.IsReparsePoint(app))
                {
                    continue;
                }

                if (!IsElectronProfile(app))
                {
                    continue;
                }

                var paths = ElectronCaches.Select(c => Path.Combine(app, c)).Where(Directory.Exists).ToList();
                if (paths.Count > 0 && !name.Equals("Mozilla", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(Multi(CleanerGroup.SafeCaches, name, paths, "App cache", true, app));
                }
            }
        }

        // package manager caches
        var profile2 = FileOps.Profile;
        var known = new (string Name, string Path, bool Safe)[]
        {
            ("npm", Path.Combine(local, "npm-cache"), true),
            ("yarn", Path.Combine(local, "Yarn", "Cache"), true),
            ("pnpm", Path.Combine(local, "pnpm-cache"), true),
            ("pip", Path.Combine(local, "pip", "Cache"), true),
            ("node-gyp", Path.Combine(local, "node-gyp", "Cache"), true),
            ("electron", Path.Combine(local, "electron", "Cache"), true),
            ("Cypress", Path.Combine(local, "Cypress", "Cache"), false),
            ("puppeteer", Path.Combine(profile2, ".cache", "puppeteer"), true),
            ("typescript", Path.Combine(local, "Microsoft", "TypeScript"), true),
            (Loc.T("winget"), safeTemp is null ? string.Empty : Path.Combine(safeTemp, "WinGet"), true),
            (Loc.T("Microsoft Store"), Path.Combine(local, "Packages", "Microsoft.WindowsStore_8wekyb3d8bbwe", "LocalCache"), false),
            ("ms-playwright", Path.Combine(local, "ms-playwright"), false),
        };
        foreach (var (name, path, safe) in known)
        {
            if (path.Length == 0)
            {
                continue;
            }

            list.Add(Folder(safe ? CleanerGroup.SafeCaches : CleanerGroup.OtherCaches, name, path, "Package cache", safe));
        }

        // system caches (administrator only)
        if (_elevated)
        {
            list.Add(Folder(CleanerGroup.OtherCaches, "Windows Update downloads", Path.Combine(FileOps.Windows, "SoftwareDistribution", "Download"), "SoftwareDistribution", false, RemoveKind.PermanentContents, null, true));
            list.Add(Folder(CleanerGroup.OtherCaches, "Delivery Optimization", Path.Combine(FileOps.ProgramData, "Microsoft", "Windows", "DeliveryOptimization", "Cache"), "DeliveryOptimization", false, RemoveKind.PermanentContents, null, true));
            list.Add(Folder(CleanerGroup.OtherCaches, "Prefetch", Path.Combine(FileOps.Windows, "Prefetch"), "Prefetch", false, RemoveKind.PermanentContents, null, true));
            list.Add(Folder(CleanerGroup.OtherCaches, "Windows temporary files", Path.Combine(FileOps.Windows, "Temp"), "Windows\\Temp", false, RemoveKind.PermanentContents, null, true, 1));
        }

        foreach (var item in list.Where(i => i is not null))
        {
            if (item!.Group == CleanerGroup.SafeCaches && item.Size > 0)
            {
                yield return item;
            }
            else if (item.Size > 0)
            {
                yield return item;
            }
        }
    }

    /// <summary>The child folders of INetCache that may be cleaned: everything except <c>Content.Outlook</c>.</summary>
    internal static IEnumerable<string> InternetCacheFolders(string inet)
    {
        try
        {
            return Directory.EnumerateDirectories(inet).Where(d => !Path.GetFileName(d).Equals("Content.Outlook", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>True when an <c>%APPDATA%\&lt;app&gt;</c> folder looks like a Chromium/Electron profile (has <c>Local State</c> or <c>Preferences</c>).</summary>
    internal static bool IsElectronProfile(string appFolder) =>
        File.Exists(Path.Combine(appFolder, "Local State")) || File.Exists(Path.Combine(appFolder, "Preferences"));

    private CleanItem? Thumbnails()
    {
        var folder = Path.Combine(FileOps.LocalAppData, "Microsoft", "Windows", "Explorer");
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var files = Directory.EnumerateFiles(folder).Where(f => Path.GetFileName(f).StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase)).ToList();
        var size = files.Sum(f => new FileInfo(f).Length);
        return size <= 0
            ? null
            : new CleanItem { Group = CleanerGroup.SafeCaches, Kind = RemoveKind.PermanentFiles, Name = "Thumbnail and icon cache", Path = folder, Paths = files, Size = size, Detail = "Explorer", Recommended = true, Root = folder };
    }

    // ------------------------------------------------------------------ logs

    private IEnumerable<CleanItem> Logs()
    {
        var local = FileOps.LocalAppData;
        var list = new List<CleanItem?>
        {
            Multi(CleanerGroup.Logs, "Windows error reports", new[] { "ReportArchive", "ReportQueue", "Temp" }.Select(d => Path.Combine(local, "Microsoft", "Windows", "WER", d)), "WER", true, Path.Combine(local, "Microsoft", "Windows", "WER")),
            Folder(CleanerGroup.Logs, "Crash dumps", Path.Combine(local, "CrashDumps"), "CrashDumps", false),
        };
        if (_elevated)
        {
            list.Add(Multi(CleanerGroup.Logs, "Windows error reports (system)", new[] { "ReportArchive", "ReportQueue" }.Select(d => Path.Combine(FileOps.ProgramData, "Microsoft", "Windows", "WER", d)), "WER", true, Path.Combine(FileOps.ProgramData, "Microsoft", "Windows", "WER"), true));
            list.Add(Folder(CleanerGroup.Logs, "Minidumps", Path.Combine(FileOps.Windows, "Minidump"), "Minidump", true, RemoveKind.PermanentContents, null, true));
            list.Add(Folder(CleanerGroup.Logs, "DISM logs", Path.Combine(FileOps.Windows, "Logs", "DISM"), "DISM", true, RemoveKind.PermanentContents, null, true));
            var cbs = Path.Combine(FileOps.Windows, "Logs", "CBS");
            if (Directory.Exists(cbs))
            {
                var old = Directory.EnumerateFiles(cbs, "*.log").Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7)).ToList();
                if (old.Count > 0)
                {
                    list.Add(new CleanItem { Group = CleanerGroup.Logs, Kind = RemoveKind.PermanentFiles, Name = "Component servicing logs", Path = cbs, Paths = old, Size = old.Sum(f => new FileInfo(f).Length), Detail = "CBS", Recommended = true, Root = cbs, NeedsAdmin = true });
                }
            }
        }

        // per-app *.log older than seven days
        foreach (var root in new[] { FileOps.AppData, local })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var app in Directory.EnumerateDirectories(root))
            {
                _ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(app);
                if (HiddenCaches.Contains(name) || name.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) || name.Equals("Temp", StringComparison.OrdinalIgnoreCase) || Win32.IsReparsePoint(app))
                {
                    continue;
                }

                var logs = Path.Combine(app, "logs");
                if (!Directory.Exists(logs))
                {
                    logs = Path.Combine(app, "Logs");
                }

                if (!Directory.Exists(logs))
                {
                    continue;
                }

                var files = Directory.EnumerateFiles(logs, "*.log", SearchOption.TopDirectoryOnly).Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7)).ToList();
                var size = files.Sum(f => new FileInfo(f).Length);
                if (files.Count > 0 && size > 0)
                {
                    list.Add(new CleanItem { Group = CleanerGroup.Logs, Kind = RemoveKind.PermanentFiles, Name = name, Path = logs, Paths = files, Size = size, Detail = Loc.T("App logs"), Recommended = true, Root = logs });
                }
            }
        }

        return list.Where(i => i is not null).Select(i => i!);
    }

    // ------------------------------------------------------------------ developer

    private IEnumerable<CleanItem> Developer()
    {
        var local = FileOps.LocalAppData;
        var roaming = FileOps.AppData;
        var profile = FileOps.Profile;
        var list = new List<CleanItem?>();
        var vs = Path.Combine(local, "Microsoft", "VisualStudio");
        if (Directory.Exists(vs))
        {
            foreach (var version in Directory.EnumerateDirectories(vs))
            {
                list.Add(Multi(CleanerGroup.Developer, $"Visual Studio {Path.GetFileName(version)}", new[] { "ComponentModelCache", "Roslyn" }.Select(d => Path.Combine(version, d)), "Visual Studio", true, vs));
            }
        }

        list.Add(Folder(CleanerGroup.Developer, "Visual Studio logs", Path.Combine(local, "Temp", "VSLogs"), "VSLogs", true));
        var jetbrains = Path.Combine(local, "JetBrains");
        if (Directory.Exists(jetbrains))
        {
            foreach (var product in Directory.EnumerateDirectories(jetbrains))
            {
                list.Add(Multi(CleanerGroup.Developer, $"JetBrains {Path.GetFileName(product)}", new[] { "caches", "log" }.Select(d => Path.Combine(product, d)), "JetBrains", true, jetbrains));
            }
        }

        list.Add(Multi(CleanerGroup.Developer, "VS Code caches", new[] { "CachedData", "CachedExtensionVSIXs", "logs" }.Select(d => Path.Combine(roaming, "Code", d)), Loc.T("Code"), true, Path.Combine(roaming, "Code")));
        list.Add(Multi(CleanerGroup.Developer, "NuGet HTTP cache", new[] { "v3-cache", "plugins-cache" }.Select(d => Path.Combine(local, "NuGet", d)), "NuGet", true, Path.Combine(local, "NuGet")));
        list.Add(Folder(CleanerGroup.Developer, "Go build cache", Path.Combine(local, "go-build"), "go-build", true));
        list.Add(Folder(CleanerGroup.Developer, "Android cache", Path.Combine(profile, ".android", "cache"), "Android", true));
        list.Add(Folder(CleanerGroup.Developer, "Gradle caches", Path.Combine(profile, ".gradle", "caches"), "Gradle", false));
        list.Add(Folder(CleanerGroup.Developer, "NuGet packages", Path.Combine(profile, ".nuget", "packages"), "NuGet global packages", false));
        list.Add(Folder(CleanerGroup.Developer, "Maven repository", Path.Combine(profile, ".m2", "repository"), "Maven", false));
        list.Add(Folder(CleanerGroup.Developer, "Cargo registry cache", Path.Combine(profile, ".cargo", "registry", "cache"), "Cargo", false));
        return list.Where(i => i is not null).Select(i => i!);
    }

    // ------------------------------------------------------------------ bin / backups / screenshots

    private IEnumerable<CleanItem> Bin()
    {
        var (bytes, count) = FileOps.RecycleBinInfo();
        if (count > 0)
        {
            yield return new CleanItem { Group = CleanerGroup.Trash, Kind = RemoveKind.EmptyBin, Name = Loc.T("Recycle Bin"), Path = "Recycle Bin", Size = bytes, Detail = Loc.F("{0} items", count), Recommended = false };
        }
    }

    private IEnumerable<CleanItem> DeviceBackups()
    {
        var roots = new[]
        {
            Path.Combine(FileOps.AppData, "Apple Computer", "MobileSync", "Backup"),
            Path.Combine(FileOps.Profile, "Apple", "MobileSync", "Backup"),
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                _ct.ThrowIfCancellationRequested();
                if (Win32.IsReparsePoint(dir))
                {
                    continue;
                }

                var size = FileOps.DirectorySize(dir, _ct);
                if (size <= 0)
                {
                    continue;
                }

                var detail = BackupDetail(dir);
                yield return new CleanItem { Group = CleanerGroup.DeviceBackups, Kind = RemoveKind.Recycle, Name = Path.GetFileName(dir), Path = dir, Paths = [dir], Size = size, Detail = detail, Recommended = false, Root = root, Identity = Win32.FileIdentity(dir) };
            }
        }
    }

    private static string BackupDetail(string dir)
    {
        try
        {
            var info = Path.Combine(dir, "Info.plist");
            if (!File.Exists(info))
            {
                return Path.GetFileName(dir);
            }

            var xml = File.ReadAllText(info);
            string Value(string key)
            {
                var m = Regex.Match(xml, $@"<key>{key}</key>\s*<(string|date)>([^<]*)</", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[2].Value : string.Empty;
            }

            var device = Value("Device Name");
            var date = Value("Last Backup Date");
            var when = DateTime.TryParse(date, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed.ToLocalTime().ToString("d") : string.Empty;
            return device.Length == 0 ? Path.GetFileName(dir) : when.Length == 0 ? device : $"{device}, {when}";
        }
        catch
        {
            return Path.GetFileName(dir);
        }
    }

    private IEnumerable<CleanItem> Screenshots()
    {
        var folders = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"), Path.Combine(FileOps.Profile, "Pictures", "Screenshots") };
        folders.AddRange(TrayloomScreenshotFolders());
        var cutoff = DateTime.Now.AddDays(-_screenshotDays);
        var useAccess = LastAccessTrusted();
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                _ct.ThrowIfCancellationRequested();
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || !ScreenshotName.IsMatch(info.Name) || info.Length <= 0)
                {
                    continue;
                }

                var latest = new[] { info.CreationTime, info.LastWriteTime, useAccess ? info.LastAccessTime : DateTime.MinValue }.Max();
                if (latest > cutoff)
                {
                    continue;
                }

                yield return new CleanItem { Group = CleanerGroup.Screenshots, Kind = RemoveKind.Recycle, Name = info.Name, Path = file, Paths = [file], Size = info.Length, Detail = latest.ToString("d"), Recommended = false, Root = folder, Identity = Win32.FileIdentity(file) };
            }
        }
    }

    private static IEnumerable<string> TrayloomScreenshotFolders()
    {
        var dir = Path.Combine(FileOps.AppData, "Trayloom", "screenshots");
        if (Directory.Exists(dir))
        {
            yield return dir;
        }
    }

    private static bool LastAccessTrusted()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
            var value = Convert.ToInt32(key?.GetValue("NtfsDisableLastAccessUpdate") ?? 0);
            return (value & 1) == 0;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ owners

    private HashSet<string> Owners()
    {
        if (_owners is not null)
        {
            return _owners;
        }

        var set = new HashSet<string>();
        void Add(string? text)
        {
            var n = Norm.Of(text);
            if (n.Length >= 3)
            {
                set.Add(n);
            }

            var s = Norm.Stripped(text);
            if (s.Length >= 3)
            {
                set.Add(s);
            }
        }

        _programs ??= ProgramCatalog.Enumerate();
        foreach (var p in _programs)
        {
            Add(p.Name);
            Add(p.Publisher);
            Add(p.PackageFamilyName.Split('_')[0]);
            Add(Path.GetFileName(p.InstallLocation.TrimEnd('\\')));
            Add(Path.GetFileNameWithoutExtension(Icons.CleanIconPath(p.DisplayIcon) ?? string.Empty));
            foreach (var part in p.Publisher.Split(' ', ',', '.'))
            {
                if (part.Length >= 5)
                {
                    Add(part);
                }
            }
        }

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                Add(process.ProcessName);
                process.Dispose();
            }
        }
        catch
        {
            // best effort
        }

        foreach (var start in new[] { Path.Combine(FileOps.AppData, "Microsoft", "Windows", "Start Menu", "Programs"), Path.Combine(FileOps.ProgramData, "Microsoft", "Windows", "Start Menu", "Programs") })
        {
            try
            {
                if (!Directory.Exists(start))
                {
                    continue;
                }

                foreach (var entry in Directory.EnumerateFileSystemEntries(start, "*", SearchOption.TopDirectoryOnly))
                {
                    Add(Path.GetFileNameWithoutExtension(entry));
                }
            }
            catch
            {
                // unreadable
            }
        }

        _owners = set;
        return set;
    }

    private bool HasLivingOwner(string name)
    {
        var n = Norm.Of(name);
        var s = Norm.Stripped(name);
        foreach (var owner in Owners())
        {
            if (owner == n || owner == s)
            {
                return true;
            }

            if (owner.Length >= 4 && (n.StartsWith(owner) || owner.StartsWith(n) && n.Length >= 4))
            {
                return true;
            }

            if (n.Length >= 5 && owner.Contains(n))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ leftovers

    private IEnumerable<CleanItem> Leftovers()
    {
        var roots = new[] { FileOps.AppData, FileOps.LocalAppData, Path.Combine(FileOps.LocalAppData, "Programs"), FileOps.ProgramData, FileOps.ProgramFiles, FileOps.ProgramFilesX86 }
            .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var root in roots)
        {
            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(root).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var dir in dirs)
            {
                _ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                var n = Norm.Of(name);
                if (n.Length < 4 || ProtectedLeftovers.Contains(name) || name.StartsWith("regid.", StringComparison.OrdinalIgnoreCase) || name.StartsWith('.')
                    || Win32.IsReparsePoint(dir) || HiddenCaches.Contains(name) || HasLivingOwner(name))
                {
                    continue;
                }

                // Program Files / ProgramData folders owned by the OS or a shared runtime are never offered
                if (root.StartsWith(FileOps.ProgramFiles, StringComparison.OrdinalIgnoreCase) && !FileOps.IsElevated)
                {
                    continue;
                }

                var size = FileOps.DirectorySize(dir, _ct);
                if (size <= 0 || !_claimed.Add(dir))
                {
                    continue;
                }

                yield return new CleanItem
                {
                    Group = CleanerGroup.Leftovers,
                    Kind = RemoveKind.Recycle,
                    Name = name,
                    Path = dir,
                    Paths = [dir],
                    Size = size,
                    Detail = Path.GetFileName(root),
                    Recommended = false,
                    Root = root,
                    Identity = Win32.FileIdentity(dir),
                    NeedsAdmin = root.StartsWith(FileOps.ProgramFiles, StringComparison.OrdinalIgnoreCase) || root.StartsWith(FileOps.ProgramFilesX86, StringComparison.OrdinalIgnoreCase) || root.StartsWith(FileOps.ProgramData, StringComparison.OrdinalIgnoreCase),
                };
            }
        }
    }

    // ------------------------------------------------------------------ startup items

    private static bool IsProtectedStartup(string name, string target) =>
        name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) || name.Contains("Windows", StringComparison.OrdinalIgnoreCase) || name.Contains("SecurityHealth", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith(FileOps.Windows, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True only when the target is certainly gone: a drive-letter path on a fixed, ready drive where neither a file nor a folder
    /// exists. Removable, network, optical, unready, UNC or unreadable roots are never treated as missing.
    /// </summary>
    internal static bool TargetIsVerifiablyMissing(string path, Func<string, (DriveType Type, bool Ready)?>? probe = null)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal) || root.Length < 2 || !char.IsAsciiLetter(root[0]) || root[1] != ':')
            {
                return false;
            }

            var info = (probe ?? DefaultProbe)(root);
            if (info is not { Type: DriveType.Fixed, Ready: true })
            {
                return false;
            }

            return !File.Exists(path) && !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static (DriveType Type, bool Ready)? DefaultProbe(string root)
    {
        var drive = new DriveInfo(root);
        return (drive.DriveType, drive.IsReady);
    }

    /// <summary>First executable path of a command line (quoted or not).</summary>
    public static string? ExecutableOf(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var match = Regex.Match(text, @"^([A-Za-z]:\\.*?\.(exe|bat|cmd|com|lnk|msc))(\s|$)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        var space = text.IndexOf(' ');
        return Path.IsPathRooted(text[..(space < 0 ? text.Length : space)]) ? text[..(space < 0 ? text.Length : space)] : null;
    }

    private IEnumerable<CleanItem> StartupItems()
    {
        var views = new (RegistryHive Hive, RegistryView View, string Sub)[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Run"),
        };
        foreach (var (hive, view, sub) in views)
        {
            if (hive == RegistryHive.LocalMachine && !_elevated)
            {
                continue;
            }

            string[] names;
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(sub);
                if (key is null)
                {
                    continue;
                }

                names = key.GetValueNames();
                foreach (var name in names)
                {
                    var command = key.GetValue(name) as string ?? string.Empty;
                    var exe = ExecutableOf(command);
                    if (exe is null || !TargetIsVerifiablyMissing(exe) || IsProtectedStartup(name, exe) || HasLivingOwner(name) || HasLivingOwner(Path.GetFileNameWithoutExtension(exe)))
                    {
                        continue;
                    }

                    var hiveName = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
                    yield return new CleanItem
                    {
                        Group = CleanerGroup.LoginItems,
                        Kind = RemoveKind.RegistryValue,
                        Name = name,
                        Path = $"{hiveName}\\{sub}",
                        Paths = [command],
                        Size = 0,
                        Detail = name,
                        Recommended = _attended,
                        Hive = hive,
                        View = view,
                        SubKey = sub,
                        ValueName = name,
                        NeedsAdmin = hive == RegistryHive.LocalMachine,
                    };
                }
            }
            finally
            {
                // keys disposed above
            }
        }

        foreach (var folder in new[] { Path.Combine(FileOps.AppData, @"Microsoft\Windows\Start Menu\Programs\Startup"), Path.Combine(FileOps.ProgramData, @"Microsoft\Windows\Start Menu\Programs\Startup") })
        {
            if (!Directory.Exists(folder) || (folder.StartsWith(FileOps.ProgramData, StringComparison.OrdinalIgnoreCase) && !_elevated))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                string? target = null;
                try
                {
                    target = ShortcutTarget(file);
                }
                catch
                {
                    // unreadable shortcut
                }

                if (target is null || !TargetIsVerifiablyMissing(target) || IsProtectedStartup(Path.GetFileName(file), target) || HasLivingOwner(Path.GetFileNameWithoutExtension(file)))
                {
                    continue;
                }

                yield return new CleanItem { Group = CleanerGroup.LoginItems, Kind = RemoveKind.Recycle, Name = Path.GetFileNameWithoutExtension(file), Path = file, Paths = [file], Size = new FileInfo(file).Length, Detail = Path.GetFileName(file), Recommended = _attended, Root = folder, Identity = Win32.FileIdentity(file) };
            }
        }
    }

    /// <summary>Target of a .lnk through the shell COM object.</summary>
    public static string? ShortcutTarget(string lnk)
    {
        if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return lnk;
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return null;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(lnk);
            string target = link.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }
}
