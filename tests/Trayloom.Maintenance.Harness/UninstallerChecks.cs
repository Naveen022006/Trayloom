// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Features.Maintenance;

namespace Trayloom.Maintenance.Harness;

/// <summary>
/// Checks of the Uninstaller: the usable install location rule (V-01), the scan with a broad location, per-run registry backups (V-07),
/// the elevated allow-list for scan rows, Kill Process protection, the winget locate rule and the terminal command. Everything runs on
/// temp folders and an HKCU test key; nothing real is closed, uninstalled or deleted.
/// </summary>
internal static class UninstallerChecks
{
    private const string TestKey = @"Software\TrayloomTest";

    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        UsableLocation();
        Aliases();
        SharedVendors();
        Depth();
        Targets();
        BroadLocationScan();
        Backups();
        ElevatedRows();
        KillProtection();
        Winget();
        Commands();
    }

    private static void Check(bool ok, string what) => _check(ok, "uninstaller: " + what);

    private static InstalledProgram Program(string name, string location, string key = "k") => new()
    {
        Key = key + "-" + name,
        RegistryPath = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + key + "-" + name,
        Name = name,
        InstallLocation = location,
        UninstallString = "cmd.exe /c exit 0",
    };

    private static string Usable(InstalledProgram p, params InstalledProgram[] others) =>
        UninstallerSupport.UsableInstallLocation(p, [p, .. others], _ => { });

    private static void UsableLocation()
    {
        string Env(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var broad = new Dictionary<string, string>
        {
            ["drive root"] = Path.GetPathRoot(Env(Environment.SpecialFolder.Windows))!,
            ["System32"] = Path.Combine(Env(Environment.SpecialFolder.Windows), "System32"),
            ["Windows"] = Env(Environment.SpecialFolder.Windows),
            ["Program Files"] = Env(Environment.SpecialFolder.ProgramFiles),
            ["Common Files"] = Path.Combine(Env(Environment.SpecialFolder.ProgramFiles), "Common Files"),
            ["LocalAppData\\Programs"] = Path.Combine(Env(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            ["profile"] = Env(Environment.SpecialFolder.UserProfile),
            ["AppData"] = Env(Environment.SpecialFolder.ApplicationData),
            ["missing folder"] = Path.Combine(Path.GetTempPath(), "tl-wp1-missing-" + Guid.NewGuid().ToString("N")),
            ["relative"] = "Program Files\\App",
            ["UNC"] = @"\\server\share\App",
            ["empty"] = string.Empty,
        };
        foreach (var (label, path) in broad)
        {
            Check(Usable(Program("Broad " + label, path)).Length == 0, $"install location '{label}' is not used");
        }

        var root = Path.Combine(Path.GetTempPath(), "tl-wp1-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = Path.Combine(root, "App");
            var vendor = Path.Combine(root, "Vendor");
            var vendorApp = Path.Combine(vendor, "App");
            Directory.CreateDirectory(app);
            Directory.CreateDirectory(vendorApp);

            var good = Program("Solo", app);
            Check(string.Equals(Usable(good), app, StringComparison.OrdinalIgnoreCase), "a temp App folder with no other program is used");
            Check(string.Equals(Usable(Program("Solo", app + "\\")), app, StringComparison.OrdinalIgnoreCase), "a trailing backslash is normalised");

            var parent = Program("Parent", vendor, "p");
            var child = Program("Child", vendorApp, "c");
            Check(Usable(parent, child).Length == 0, "a vendor folder holding another program is not used");
            Check(string.Equals(Usable(child, parent), vendorApp, StringComparison.OrdinalIgnoreCase), "the child folder itself is still used");

            var twin = Program("Twin", app, "t");
            Check(Usable(good, twin).Length == 0, "a folder shared by two programs is not used");

            var appx = new InstalledProgram { Key = "x", Name = "Packaged", InstallLocation = app, IsAppx = true, PackageFullName = "x" };
            Check(Usable(appx).Length == 0, "a packaged app never uses its folder");

            // a junction is a link, not a folder of the program
            var junction = Path.Combine(root, "Link");
            if (MakeJunction(junction, app))
            {
                Check(Usable(Program("Linked", junction)).Length == 0, "a junction is not used");
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void Aliases()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var drive = Path.GetPathRoot(windows)!;
        var bypass = new Dictionary<string, string>
        {
            ["NT device path to drivers"] = @"\??\" + Path.Combine(windows, "System32", "drivers"),
            ["extended path"] = @"\\?\" + Path.Combine(windows, "System32", "drivers"),
            ["device path"] = @"\\.\" + Path.Combine(windows, "System32", "drivers"),
            ["NT device path to LocalAppData"] = @"\??\" + local,
            ["NTFS stream syntax"] = windows + @"::$INDEX_ALLOCATION\System32\drivers",
            ["second colon"] = drive + @"Users:x\y",
            ["forward slashes into Windows"] = windows.Replace('\\', '/') + "/System32/drivers",
            ["dot segments into Windows"] = Path.Combine(Path.GetTempPath(), @"..\..\..\..\..\Windows\System32\drivers"),
        };
        foreach (var (label, path) in bypass)
        {
            Check(Usable(Program("Alias " + label, path)).Length == 0, $"alias '{label}' is not used");
        }

        var legacy = Path.Combine(drive, "Documents and Settings", Environment.UserName, "AppData", "Local");
        if (Directory.Exists(legacy))
        {
            Check(Usable(Program("Alias legacy profile", legacy)).Length == 0, "the 'Documents and Settings' junction into the profile is not used");
        }

        var root = Path.Combine(Path.GetTempPath(), "tl-wp1-al-" + Guid.NewGuid().ToString("N"));
        try
        {
            var real = Path.Combine(root, "Real");
            Directory.CreateDirectory(Path.Combine(real, "Sub"));
            var link = Path.Combine(root, "Link");
            if (MakeJunction(link, real))
            {
                Check(Usable(Program("Alias parent junction", Path.Combine(link, "Sub"))).Length == 0, "a folder behind a junction in a parent is not used");
            }

            Check(Usable(Program("Alias temp", Path.GetTempPath())).Length == 0, "%TEMP% itself is not used");
            Check(Usable(Program("Alias temp child", Path.Combine(real, "Sub"))).Length > 0, "a real folder under %TEMP% is used");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void SharedVendors()
    {
        var root = Path.Combine(Path.GetTempPath(), "tl-wp1-sv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vendor = Path.Combine(root, "Vendor");
            Directory.CreateDirectory(Path.Combine(vendor, "B"));
            var a = Program("VendorA", vendor, "a");
            var b = new InstalledProgram { Key = "b", Name = "VendorB", UninstallString = "\"" + Path.Combine(vendor, "B", "uninst.exe") + "\" /S" };
            Check(Usable(a, b).Length == 0, "a vendor folder is refused when another program's UninstallString lies below it");
            var c = new InstalledProgram { Key = "c", Name = "VendorC", UninstallString = "x", DisplayIcon = Path.Combine(vendor, "B", "c.exe") + ",0" };
            Check(Usable(a, c).Length == 0, "a vendor folder is refused when another program's DisplayIcon lies below it");
            var d = new InstalledProgram { Key = "d", Name = "VendorD", IsAppx = true, InstallLocation = Path.Combine(vendor, "B"), PackageFullName = "d" };
            Check(Usable(a, d).Length == 0, "a packaged program below the folder blocks it too");
            var e = new InstalledProgram { Key = "e", Name = "Elsewhere", UninstallString = Path.Combine(Environment.SystemDirectory, "msiexec.exe") + " /x {1}" };
            Check(Usable(a, e).Length > 0, "an msiexec uninstall string elsewhere does not block it");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void Depth()
    {
        string Env(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var programFiles = Env(Environment.SpecialFolder.ProgramFiles);
        Check(Usable(Program("PF", programFiles)).Length == 0, "Program Files itself is refused");
        foreach (var path in new[] { Path.Combine(programFiles, "Git"), Path.Combine(Env(Environment.SpecialFolder.ProgramFilesX86), "Steam") })
        {
            if (Directory.Exists(path))
            {
                Check(Usable(Program("Normal " + Path.GetFileName(path), path)).Length > 0, $"{Path.GetFileName(path)} under Program Files is accepted (two segments)");
            }
        }

        var twoSegments = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "tl-wp1-d-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(twoSegments, "App"));
            Check(SafePaths.SegmentCount(Path.Combine(twoSegments, "App")) == 2, "the fixture really is two segments deep");
            Check(Usable(Program("TwoSeg", Path.Combine(twoSegments, "App"))).Length > 0, "a two-segment install folder is accepted");
            Check(Usable(Program("OneSeg", twoSegments)).Length == 0, "a one-segment folder is refused");
        }
        catch (UnauthorizedAccessException)
        {
            // no right to create a folder at the drive root: the Program Files rows above cover the rule when those folders exist
        }
        finally
        {
            TryDelete(twoSegments);
        }

        var users = Path.Combine(Path.GetPathRoot(programFiles)!, "Users");
        var other = Directory.Exists(users) ? Directory.GetDirectories(users).FirstOrDefault(d => !string.Equals(d, Env(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase)) : null;
        if (other is not null)
        {
            Check(Usable(Program("OtherProfile", other)).Length == 0, "another user's profile folder is refused");
        }
    }

    private static void Targets()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var install = Path.Combine(Path.GetTempPath(), "tl-wp1-t-" + Guid.NewGuid().ToString("N"), "App");
        Check(UninstallerSupport.TargetUnder(@"\??\" + Path.Combine(install, "svc.exe"), install), "an NT-prefixed target under the install folder is recognised");
        Check(!UninstallerSupport.TargetUnder(Path.Combine(install, "..", "Other", "svc.exe"), install), "a dot-dot target outside the folder is not under it");
        Check(!UninstallerSupport.TargetUnder(@"\??\" + Path.Combine(windows, "system32", "drivers", "x.sys"), Path.Combine(windows, "system32")), "a driver image is never under any install folder");
        Check(!UninstallerSupport.TargetUnder(@"\SystemRoot\System32\drivers\x.sys", Path.Combine(windows, "System32")), "a \\SystemRoot\\ image is never under any install folder");
        Check(!UninstallerSupport.TargetUnder(@"System32\drivers\x.sys", Path.Combine(windows, "System32")), "a relative System32 image is never under any install folder");
        Check(UninstallerSupport.NormalizeTarget(@"\SystemRoot\System32\drivers\x.sys").StartsWith(windows, StringComparison.OrdinalIgnoreCase), "\\SystemRoot\\ maps to the Windows folder");
        Check(UninstallerSupport.NormalizeTarget(@"\\server\share\x.exe").Length == 0, "a UNC target is not a local path");
        Check(UninstallerSupport.NormalizeTarget("notepad.exe").Length == 0, "a bare name is not a local path");
    }

    private static bool MakeJunction(string link, string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            process?.WaitForExit(10_000);
            return Directory.Exists(link);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                // remove a junction first so the recursive delete never follows it
                foreach (var dir in Directory.GetDirectories(path))
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(dir);
                    }
                }

                Directory.Delete(path, true);
            }
        }
        catch
        {
            // leave the temp folder
        }
    }

    private static void BroadLocationScan()
    {
        var program = Program("TrayloomTest Broad", Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var scanner = new LeftoverScanner(program, [program], cts.Token);
        Check(scanner.InstallFolder.Length == 0, "a drive-root InstallLocation leaves the scanner without an install folder");
        List<UnItem> items;
        try
        {
            items = scanner.Scan();
        }
        catch (Exception ex)
        {
            Check(false, "the scan with a broad location completed (" + ex.GetType().Name + ")");
            return;
        }

        Check(!items.Any(i => i.Kind is UnKind.Service or UnKind.Task), "no service or task rows for a broad location");
        Check(!items.Any(i => i.Kind == UnKind.RegistryValue), "no Run or firewall rows for a broad location");
        Check(!items.Any(i => i.Kind == UnKind.Path && SafePaths.IsUnderWindows(i.Path)), "no row under %WINDIR%");
        var app = items.FirstOrDefault(i => i.Category == UnCategory.Application);
        Check(app is not null && app.Path == program.RegistryPath && string.IsNullOrEmpty(app.Root), "the application row falls back to the registry path with no root");
    }

    private static void Backups()
    {
        var work = Path.Combine(Path.GetTempPath(), "tl-wp1-bk-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var a = Registry.CurrentUser.CreateSubKey(TestKey + @"\UnBackup\A"))
            {
                a.SetValue("Marker", "alpha-marker-value");
            }

            using (var b = Registry.CurrentUser.CreateSubKey(TestKey + @"\UnBackup\B"))
            {
                b.SetValue("Marker", "bravo-marker-value");
            }

            var keyA = @"HKEY_CURRENT_USER\" + TestKey + @"\UnBackup\A";
            var keyB = @"HKEY_CURRENT_USER\" + TestKey + @"\UnBackup\B";
            var run1 = UninstallerModel.RegistryBackup.BeginRun("Test App", work);
            var run2 = UninstallerModel.RegistryBackup.BeginRun("Test App", work);
            Check(!string.Equals(run1.Folder, run2.Folder, StringComparison.OrdinalIgnoreCase), "two runs started together get different folders");

            Check(run1.Export(keyA) && run1.Export(keyB), "both keys export");
            Check(run1.Export(keyA), "exporting the same key again in a run is a no-op that succeeds");
            var files1 = Directory.GetFiles(run1.Folder, "*.reg");
            Check(files1.Length == 2, "the run folder holds exactly two files (one per distinct key)");
            Check(files1.Select(File.ReadAllText).Any(t => t.Contains("alpha-marker-value")) && files1.Select(File.ReadAllText).Any(t => t.Contains("bravo-marker-value")), "the files contain both keys' values");

            Check(run2.Export(keyA), "a second run exports the same key");
            var files2 = Directory.GetFiles(run2.Folder, "*.reg");
            Check(files2.Length == 1 && files1.All(File.Exists), "the second run does not touch the first run's files");

            Check(!run1.Export(@"HKEY_CURRENT_USER\" + TestKey + @"\DoesNotExist-" + Guid.NewGuid().ToString("N")), "a missing key fails to export");
        }
        finally
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(TestKey, false);
            }
            catch
            {
                // already gone
            }

            TryDelete(work);
        }
    }

    private static void ElevatedRows()
    {
        UnItem Row(UnKind kind, string path, string? value = null) => new() { Kind = kind, Path = path, ValueName = value, Name = "x" };
        var temp = Path.Combine(Path.GetTempPath(), "tl-wp1-row-" + Guid.NewGuid().ToString("N"), "App");
        Check(Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.Path, temp))), "a path row in the profile is allowed");
        Check(!Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.Path, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "x", "y")))), "a path row under %WINDIR% is refused");
        Check(!Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.Path, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)))), "Program Files itself is refused");
        Check(!Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.Path, @"C:\"))), "a drive root is refused");
        Check(Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.RegistryKey, @"HKEY_LOCAL_MACHINE\SOFTWARE\SomeVendor\SomeApp"))), "a vendor registry key is allowed");
        Check(!Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.RegistryKey, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows"))), "a Microsoft registry key is refused");
        Check(!Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.RegistryKey, @"HKEY_LOCAL_MACHINE\SOFTWARE"))), "the SOFTWARE hive root is refused");
        Check(!Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.RegistryValue, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion", "Name"))), "a value under a Microsoft key is refused");
        Check(Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.RegistryValue, @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "MyApp"))), "a Run value is allowed");
        Check(!Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.Task, @"\"))), "the task root is refused");
        Check(!Elevated.IsAllowed(UninstallerModel.ToElevated(Row(UnKind.Service, "bad name; calc"))), "a service name with shell characters is refused");
    }

    private static void KillProtection()
    {
        Check(KillService.IsProtected(1234, "svchost", 1), "svchost is protected in Kill Process");
        Check(KillService.IsProtected(1234, "MsMpEng", 1), "MsMpEng is protected in Kill Process");
        Check(KillService.IsProtected(1234, "csrss", 1), "csrss is protected in Kill Process");
        Check(KillService.IsProtected(4, "anything", 1), "pid 4 is protected");
        Check(KillService.IsProtected(1234, "notepad", 0), "an unreadable identity is protected");
        Check(!KillService.IsProtected(1234, "notepad", 1), "notepad is not protected");
        Check(!KillService.IsProtected(1234, "explorer", 1), "explorer can be killed on purpose");

        // the install-folder search never returns anything for an empty location
        Check(UninstallerModel.FindRunning(string.Empty).Count == 0, "no processes are listed for an empty location");
        var empty = Path.Combine(Path.GetTempPath(), "tl-wp1-nothing-" + Guid.NewGuid().ToString("N"));
        Check(UninstallerModel.FindRunning(empty).Count == 0, "no processes are listed for a folder nothing runs from");
    }

    private static void Winget()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Check(WingetCli.IsTrustedLocation(Path.Combine(local, "Microsoft", "WindowsApps", "winget.exe")), "the WindowsApps alias is trusted");
        Check(WingetCli.IsTrustedLocation(Path.Combine(programFiles, "WindowsApps", "Microsoft.DesktopAppInstaller_1.0_x64__8wekyb3d8bbwe", "winget.exe")), "winget under Program Files\\WindowsApps is trusted");
        Check(!WingetCli.IsTrustedLocation(@"C:\Tools\winget.exe"), "a winget.exe in another folder is not trusted");
        Check(!WingetCli.IsTrustedLocation(Path.Combine(local, "Microsoft", "WindowsApps", "evil.exe")), "another exe name is not trusted");
        Check(!WingetCli.IsTrustedLocation("winget.exe"), "a relative path is not trusted");
        Check(!WingetCli.IsTrustedLocation(Path.Combine(local, "Microsoft", "WindowsApps", "x", "winget.exe")), "a subfolder of the user alias folder is not trusted");
    }

    private static void Commands()
    {
        var good = OperationStatusView.BuildTerminalScript([["uninstall", "--id", "Vendor.App", "--exact", "--silent"]], null);
        Check(good is null, "no command is built without a trusted winget path");
        good = OperationStatusView.BuildTerminalScript([["uninstall", "--id", "Vendor.App", "--exact", "--silent"]], @"C:\Program Files\WindowsApps\p\winget.exe");
        Check(good is not null && good.Contains("'Vendor.App'") && good.StartsWith("& 'C:"), "a plain winget command builds");
        var quoted = OperationStatusView.BuildTerminalScript([["list"]], "C:\\Users\\D\u2019Souza\\winget.exe");
        Check(quoted is not null && quoted.Contains("D\u2019\u2019Souza"), "a typographic quote in the path is doubled");
        Check(OperationStatusView.BuildTerminalScript([["uninstall", "--id", "x\n"]], "C:\\w\\winget.exe") is null, "a trailing newline in an argument is refused");
        Check(OperationStatusView.BuildTerminalScript([["uninstall", "--id", "x; calc"]], "C:\\w\\winget.exe") is null, "an argument with a separator is refused");
        Check(OperationStatusView.BuildTerminalScript([["uninstall", "--id", "x' ; calc ; '"]], "C:\\w\\winget.exe") is null, "an argument with a quote is refused");
        Check(OperationStatusView.BuildTerminalScript([["uninstall", "--id", "$(calc)"]], "C:\\w\\winget.exe") is null, "an argument with a sub-expression is refused");
        Check(OperationStatusView.BuildTerminalScript([]) is null, "no commands build nothing");
        var two = OperationStatusView.BuildTerminalScript([["source", "update"], ["upgrade", "--all"]], @"C:\Program Files\WindowsApps\p\winget.exe");
        Check(two is not null && two.Contains("; & ") && two.Contains(@"& 'C:\Program Files\WindowsApps\p\winget.exe' 'source'"), "several commands join with the resolved winget path");

        var (exe, args) = UninstallerModel.SplitCommand(@"D:\Setup\app.msi /quiet");
        Check(exe is not null && exe.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(exe) && args.Contains("app.msi"), "an .msi uninstall string runs msiexec by full path");
    }
}
