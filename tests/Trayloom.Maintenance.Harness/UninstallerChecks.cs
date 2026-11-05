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
        var good = OperationStatusView.BuildTerminalScript([["uninstall", "--id", "Vendor.App", "--exact", "--silent"]]);
        Check(good is not null && good.Contains("'Vendor.App'") && good.StartsWith("& 'winget'"), "a plain winget command builds");
        Check(OperationStatusView.BuildTerminalScript([["uninstall", "--id", "x; calc"]]) is null, "an argument with a separator is refused");
        Check(OperationStatusView.BuildTerminalScript([["uninstall", "--id", "x' ; calc ; '"]]) is null, "an argument with a quote is refused");
        Check(OperationStatusView.BuildTerminalScript([["uninstall", "--id", "$(calc)"]]) is null, "an argument with a sub-expression is refused");
        Check(OperationStatusView.BuildTerminalScript([]) is null, "no commands build nothing");
        var two = OperationStatusView.BuildTerminalScript([["source", "update"], ["upgrade", "--all"]], @"C:\Program Files\WindowsApps\p\winget.exe");
        Check(two is not null && two.Contains("; & ") && two.Contains(@"& 'C:\Program Files\WindowsApps\p\winget.exe' 'source'"), "several commands join with the resolved winget path");

        var (exe, args) = UninstallerModel.SplitCommand(@"D:\Setup\app.msi /quiet");
        Check(exe is not null && exe.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(exe) && args.Contains("app.msi"), "an .msi uninstall string runs msiexec by full path");
    }
}
