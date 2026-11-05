// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Trayloom.Features.Maintenance;

/// <summary>An installed program: a registry Uninstall entry or an AppX/MSIX package.</summary>
internal sealed class InstalledProgram
{
    public string Key { get; init; } = string.Empty;

    /// <summary>Full registry path of the Uninstall key (hive\path) or empty for AppX.</summary>
    public string RegistryPath { get; init; } = string.Empty;

    public RegistryHive Hive { get; init; }

    public RegistryView View { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    public string InstallLocation { get; init; } = string.Empty;

    public string DisplayIcon { get; init; } = string.Empty;

    public string UninstallString { get; init; } = string.Empty;

    public string QuietUninstallString { get; init; } = string.Empty;

    public bool WindowsInstaller { get; init; }

    public bool NoRemove { get; init; }

    public long EstimatedSizeKb { get; init; }

    public bool IsAppx { get; init; }

    public string PackageFullName { get; init; } = string.Empty;

    public string PackageFamilyName { get; init; } = string.Empty;

    public string? LogoPath { get; init; }

    /// <summary>winget id once correlated.</summary>
    public string? WingetId { get; set; }

    public string? ProductCode =>
        !IsAppx && Regex.IsMatch(Key, @"^\{[0-9A-Fa-f-]{36}\}$") ? Key : null;

    /// <summary>Executable that represents the program (DisplayIcon exe, or the main exe in the install folder).</summary>
    public string? MainExecutable
    {
        get
        {
            var icon = Icons.CleanIconPath(DisplayIcon);
            if (!string.IsNullOrEmpty(icon) && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon))
            {
                return icon;
            }

            try
            {
                if (!string.IsNullOrEmpty(InstallLocation) && Directory.Exists(InstallLocation))
                {
                    var exes = Directory.EnumerateFiles(InstallLocation, "*.exe", SearchOption.TopDirectoryOnly)
                        .Where(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var match = exes.FirstOrDefault(f => Norm.Of(Path.GetFileNameWithoutExtension(f)) == Norm.Of(Name));
                    return match ?? exes.FirstOrDefault();
                }
            }
            catch
            {
                // unreadable folder
            }

            return null;
        }
    }

    /// <summary>Icon for lists.</summary>
    public ImageSource? Icon(bool large = false)
    {
        if (IsAppx && !string.IsNullOrEmpty(LogoPath) && File.Exists(LogoPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(LogoPath);
                bitmap.DecodePixelWidth = large ? 64 : 32;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                // fall through to the generic icon
            }
        }

        var icon = Icons.CleanIconPath(DisplayIcon);
        if (!string.IsNullOrEmpty(icon) && File.Exists(icon))
        {
            return Icons.Of(icon, large);
        }

        return Icons.Of(MainExecutable ?? (Directory.Exists(InstallLocation) ? InstallLocation : null), large);
    }

    public string Identity => IsAppx ? PackageFullName : Key;
}

/// <summary>Name normalisation shared by the matching code.</summary>
internal static class Norm
{
    private static readonly Regex Version = new(@"[\s_-]+(\d+(\.\d+)*|nightly|beta|alpha|dev|canary|preview|insider|stable|release|rc|lts|x64|x86|64-bit|32-bit|64bit|32bit)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Lower-case letters and digits only.</summary>
    public static string Of(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[text.Length];
        var n = 0;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[n++] = char.ToLowerInvariant(ch);
            }
        }

        return new string(buffer[..n]);
    }

    /// <summary>Normalised name with trailing version / channel / bitness words removed (applied repeatedly).</summary>
    public static string Stripped(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var value = text.Trim();
        for (var i = 0; i < 3; i++)
        {
            var next = Version.Replace(value, string.Empty);
            if (next == value)
            {
                break;
            }

            value = next;
        }

        return Of(value);
    }
}

/// <summary>Enumerates installed programs from the three Uninstall hives and the AppX packages (spec 7.2.7).</summary>
internal static class ProgramCatalog
{
    private static readonly string[] SharedNames =
    [
        "Microsoft Visual C++", "Microsoft .NET", "Microsoft Windows Desktop Runtime", "Microsoft ASP.NET", "Microsoft Edge", "Microsoft Update Health Tools",
        "Windows SDK", "Windows Software Development Kit", "Microsoft WebView2", "Windows Driver", "Microsoft Visual Studio Installer", "Windows App Runtime",
        "Microsoft Windows Desktop Runtime", "Microsoft Policy Platform", "Windows Subsystem", "Microsoft Edge WebView2",
    ];

    private static readonly string[] ReleaseSkip = ["Update", "Hotfix", "Security Update", "Service Pack", "Update Rollup"];

    /// <summary>
    /// Every Uninstall entry of all four hive/view pairs with no filtering (system components, entries without a display name, updates
    /// and shared runtimes included), plus the packaged apps. Used only to see which folders other products claim.
    /// </summary>
    public static List<InstalledProgram> EnumerateClaims(IReadOnlyList<InstalledProgram> visible)
    {
        var list = new List<InstalledProgram>(visible.Where(v => v.IsAppx));
        void AddKeys(RegistryKey? uninstall, RegistryHive hive, RegistryView view, string pathBase)
        {
            if (uninstall is null)
            {
                return;
            }

            foreach (var name in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key is null)
                    {
                        continue;
                    }

                    list.Add(new InstalledProgram
                    {
                        Key = name,
                        RegistryPath = pathBase + @"\" + name,
                        Hive = hive,
                        View = view,
                        Name = (key.GetValue("DisplayName") as string) is { Length: > 0 } display ? display : name,
                        Publisher = key.GetValue("Publisher") as string ?? string.Empty,
                        InstallLocation = (key.GetValue("InstallLocation") as string ?? string.Empty).Trim().Trim('"'),
                        DisplayIcon = key.GetValue("DisplayIcon") as string ?? string.Empty,
                        UninstallString = key.GetValue("UninstallString") as string ?? string.Empty,
                        QuietUninstallString = key.GetValue("QuietUninstallString") as string ?? string.Empty,
                    });
                }
                catch
                {
                    // unreadable entry
                }
            }
        }

        try
        {
            if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1" && Environment.GetEnvironmentVariable("TRAYLOOM_UNINSTALLER_TEST_KEY") is { Length: > 0 } testKey)
            {
                using var testRoot = Registry.CurrentUser.OpenSubKey(testKey);
                AddKeys(testRoot, RegistryHive.CurrentUser, RegistryView.Registry64, @"HKEY_CURRENT_USER\" + testKey);
                return list;
            }

            foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Registry64), (RegistryHive.CurrentUser, RegistryView.Registry32) })
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                AddKeys(uninstall, hive, view, (hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER") + @"\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            }
        }
        catch
        {
            // hive unavailable
        }

        return list;
    }

    /// <summary>Lists the programs (registry entries first, then packages).</summary>
    public static List<InstalledProgram> Enumerate(bool includeAppx = true)
    {
        var list = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // development only: read just the sub keys of HKCU\<TRAYLOOM_UNINSTALLER_TEST_KEY>, never the real Uninstall keys
        if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1" && Environment.GetEnvironmentVariable("TRAYLOOM_UNINSTALLER_TEST_KEY") is { Length: > 0 } testKey)
        {
            try
            {
                using var testRoot = Registry.CurrentUser.OpenSubKey(testKey);
                foreach (var name in testRoot?.GetSubKeyNames() ?? [])
                {
                    using var key = testRoot!.OpenSubKey(name);
                    if (Read(key, name, RegistryHive.CurrentUser, RegistryView.Registry64, @"HKEY_CURRENT_USER\" + testKey + "\\" + name) is { } program)
                    {
                        list.Add(program);
                    }
                }
            }
            catch
            {
                // test key unreadable
            }

            return list;
        }

        void AddHive(RegistryHive hive, RegistryView view)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    return;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var key = uninstall.OpenSubKey(name);
                        var program = Read(key, name, hive, view);
                        if (program is null)
                        {
                            continue;
                        }

                        var identity = $"{program.Name}|{program.Version}|{program.Publisher}";
                        if (seen.Add(identity))
                        {
                            list.Add(program);
                        }
                    }
                    catch
                    {
                        // unreadable entry
                    }
                }
            }
            catch
            {
                // hive unavailable
            }
        }

        AddHive(RegistryHive.LocalMachine, RegistryView.Registry64);
        AddHive(RegistryHive.LocalMachine, RegistryView.Registry32);
        AddHive(RegistryHive.CurrentUser, RegistryView.Registry64);
        AddHive(RegistryHive.CurrentUser, RegistryView.Registry32);

        if (includeAppx)
        {
            try
            {
                list.AddRange(Packages());
            }
            catch
            {
                // package manager unavailable
            }
        }

        return list;
    }

    private static InstalledProgram? Read(RegistryKey? key, string name, RegistryHive hive, RegistryView view, string? registryPathOverride = null)
    {
        if (key is null)
        {
            return null;
        }

        var display = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(display))
        {
            return null;
        }

        if (Convert.ToInt32(key.GetValue("SystemComponent") ?? 0) == 1)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(key.GetValue("ParentKeyName") as string) || !string.IsNullOrEmpty(key.GetValue("ParentDisplayName") as string))
        {
            return null;
        }

        var release = key.GetValue("ReleaseType") as string ?? string.Empty;
        if (ReleaseSkip.Any(r => release.Equals(r, StringComparison.OrdinalIgnoreCase)) || Regex.IsMatch(display, @"^KB\d+", RegexOptions.IgnoreCase))
        {
            return null;
        }

        if (SharedNames.Any(s => display.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var uninstall = key.GetValue("UninstallString") as string ?? string.Empty;
        var quiet = key.GetValue("QuietUninstallString") as string ?? string.Empty;
        var path = (hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER") + (view == RegistryView.Registry32 && hive == RegistryHive.LocalMachine ? @"\SOFTWARE\WOW6432Node" : @"\SOFTWARE") + @"\Microsoft\Windows\CurrentVersion\Uninstall\" + name;
        if (view == RegistryView.Registry32 && hive == RegistryHive.CurrentUser)
        {
            path = @"HKEY_CURRENT_USER\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + name;
        }

        return new InstalledProgram
        {
            Key = name,
            RegistryPath = registryPathOverride ?? path,
            Hive = hive,
            View = view,
            Name = display.Trim(),
            Version = (key.GetValue("DisplayVersion") as string ?? string.Empty).Trim(),
            Publisher = (key.GetValue("Publisher") as string ?? string.Empty).Trim(),
            InstallLocation = (key.GetValue("InstallLocation") as string ?? string.Empty).Trim().Trim('"'),
            DisplayIcon = key.GetValue("DisplayIcon") as string ?? string.Empty,
            UninstallString = uninstall,
            QuietUninstallString = quiet,
            WindowsInstaller = Convert.ToInt32(key.GetValue("WindowsInstaller") ?? 0) == 1,
            NoRemove = Convert.ToInt32(key.GetValue("NoRemove") ?? 0) == 1,
            EstimatedSizeKb = Convert.ToInt64(key.GetValue("EstimatedSize") ?? 0),
        };
    }

    private static IEnumerable<InstalledProgram> Packages()
    {
        var manager = new Windows.Management.Deployment.PackageManager();
        foreach (var package in manager.FindPackagesForUser(string.Empty))
        {
            InstalledProgram? program = null;
            try
            {
                if (package.IsFramework || package.IsResourcePackage)
                {
                    continue;
                }

                if (package.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System)
                {
                    continue;
                }

                var name = package.DisplayName;
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string install;
                try
                {
                    install = package.InstalledLocation?.Path ?? string.Empty;
                }
                catch
                {
                    install = string.Empty;
                }

                string? logo = null;
                try
                {
                    logo = package.Logo?.LocalPath;
                }
                catch
                {
                    // no logo
                }

                var v = package.Id.Version;
                program = new InstalledProgram
                {
                    Key = package.Id.FamilyName,
                    Name = name.Trim(),
                    Version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                    Publisher = package.PublisherDisplayName ?? string.Empty,
                    InstallLocation = install,
                    IsAppx = true,
                    PackageFullName = package.Id.FullName,
                    PackageFamilyName = package.Id.FamilyName,
                    LogoPath = logo,
                };
            }
            catch
            {
                // skip packages that cannot be read
            }

            if (program is not null)
            {
                yield return program;
            }
        }
    }
}
