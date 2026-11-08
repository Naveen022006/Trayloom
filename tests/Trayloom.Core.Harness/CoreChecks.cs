// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;

namespace Trayloom.Core.Harness;

/// <summary>
/// Checks of the Core safety helpers: links, paths, elevation policy, process names, atomic writes and the recovery runner.
/// Environment folders are only read; files are written in a temp folder that is removed at the end.
/// </summary>
internal static class CoreChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Links();
        Paths();
        Elevation();
        Processes();
        Atomic();
        SettingsPersistence();
        Recovery();
        LocalRoot();
    }

    private static void Check(bool ok, string what) => _check(ok, "core: " + what);

    private static string Env(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

    private static void Links()
    {
        foreach (var good in new[] { "http://a", "https://a/b?c", "mailto:x@y" })
        {
            Check(SafeLinks.IsWebUrl(good, out var uri) && uri is not null, "web url accepted: " + good);
        }

        var bad = new[]
        {
            "https://user@host", "HTTPS ://x", "javascript:alert(1)", "file:///C:/x", @"C:\x\a.exe", @"\\host\share\a.exe",
            "ms-msdt:x", "search-ms:x", "shell:x", "https://a b", "https://a\u0000b", "http://" + new string('a', 3000), string.Empty, "http://",
        };
        foreach (var text in bad)
        {
            Check(!SafeLinks.IsWebUrl(text, out var uri) && uri is null, "web url refused: " + (text.Length > 40 ? text[..40] + "..." : text.Replace("\u0000", "\\0")));
        }

        Check(!SafeLinks.IsWebUrl("mailto:x@y", out _, allowMailto: false), "mailto refused when not allowed");
        Check(SafeLinks.IsAllowedShellUri("ms-settings:taskbar"), "ms-settings allowed");
        Check(SafeLinks.IsAllowedShellUri("windowsdefender://ransomwareprotection"), "windowsdefender allowed");
        Check(SafeLinks.IsAllowedShellUri("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1"), "ms-windows-store allowed");
        Check(SafeLinks.IsAllowedShellUri("https://github.com/x"), "https allowed as shell uri");
        Check(!SafeLinks.IsAllowedShellUri("ms-msdt:/id x"), "ms-msdt refused");
        Check(!SafeLinks.IsAllowedShellUri(@"C:\Windows\notepad.exe"), "path refused as shell uri");
        Check(!SafeLinks.IsAllowedShellUri("ms-settings:taskbar && calc"), "whitespace in settings uri refused");
    }

    private static void Paths()
    {
        var windows = Env(Environment.SpecialFolder.Windows);
        var profile = Env(Environment.SpecialFolder.UserProfile);
        var local = Env(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Env(Environment.SpecialFolder.ProgramFiles);
        var drive = Path.GetPathRoot(windows) ?? @"C:\";

        Check(SafePaths.IsCriticalOrAncestor(drive), "drive root is critical");
        Check(SafePaths.IsCriticalOrAncestor(windows), "windows is critical");
        Check(SafePaths.IsCriticalOrAncestor(profile), "profile is critical");
        Check(SafePaths.IsCriticalOrAncestor(local), "local app data is critical");
        Check(SafePaths.IsCriticalOrAncestor(programFiles), "program files is critical");
        Check(SafePaths.IsCriticalOrAncestor(Path.Combine(drive, "Users")), @"C:\Users is critical");
        Check(SafePaths.IsCriticalOrAncestor(Path.Combine(profile, "AppData")), "profile AppData is an ancestor of a critical root");
        Check(!SafePaths.IsCriticalOrAncestor(Path.Combine(local, "Temp")), "local temp is not critical");
        Check(!SafePaths.IsCriticalOrAncestor(Path.Combine(windows, "Temp")), "windows temp is not critical (no windows clause)");
        Check(SafePaths.IsUnderWindows(Path.Combine(windows, "Temp")), "windows temp is under windows");
        Check(!SafePaths.IsUnderWindows(local), "local app data is not under windows");

        Check(SafePaths.CriticalRoots().Count > 0 && SafePaths.CriticalRoots().All(r => r.Length > 0 && SafePaths.Normalize(r) == r), "critical roots are non-empty and normalized");

        Check(SafePaths.IsUnder("C:\\x", "C:"), "C:\\x is under C:");
        Check(!SafePaths.IsUnder("C:\\xy", "C:\\x"), "C:\\xy is not under C:\\x");
        Check(SafePaths.IsUnder(@"C:\x\", @"C:\X"), "equal after normalisation, case-insensitive");
        Check(!SafePaths.IsUnder(@"C:\x", string.Empty), "empty root is never a parent");
        Check(SafePaths.Normalize("C:") == @"C:\", "Normalize C: gives C:\\");
        Check(SafePaths.Normalize(@"C:\a\b\") == @"C:\a\b", "Normalize trims the trailing separator");
        Check(SafePaths.SegmentCount(@"C:\a\b\c") == 3 && SafePaths.SegmentCount(@"C:\") == 0, "segment count");
        Check(SafePaths.IsDriveRoot(@"C:\") && SafePaths.IsDriveRoot(@"\\server\share") && !SafePaths.IsDriveRoot(@"C:\a"), "drive roots");

        var common = Env(Environment.SpecialFolder.CommonProgramFiles);
        Check(common.Length > 0 && SafePaths.IsSharedSystemFolder(common) && !SafePaths.IsSharedSystemFolder(Path.Combine(common, "Foo")), "common files is shared, its child is not");
        Check(SafePaths.IsSharedSystemFolder(Path.Combine(local, "Programs")), "per-user Programs is shared");
    }

    private static void Elevation()
    {
        var programFiles = Env(Environment.SpecialFolder.ProgramFiles);
        var local = Env(Environment.SpecialFolder.LocalApplicationData);
        Check(ElevationPolicy.IsAdminOwnedPath(Path.Combine(programFiles, "T", "T.exe")), "Program Files path is admin-owned");
        Check(!ElevationPolicy.IsAdminOwnedPath(Path.Combine(local, "Programs", "Trayloom", "Trayloom.exe")), "per-user install is not admin-owned");
        Check(!ElevationPolicy.IsAdminOwnedPath(string.Empty), "empty path is not admin-owned");
        Check(ElevationPolicy.CanOfferElevation == ElevationPolicy.IsAdminOwnedInstall, "CanOfferElevation follows the install");
        Check(!ElevationPolicy.CanOfferElevation, "the harness itself does not run from Program Files");
    }

    private static void Processes()
    {
        Check(ProcessSafety.IsProtectedName("lsass") && ProcessSafety.IsProtectedName("SVCHOST") && ProcessSafety.IsProtectedName("MsMpEng.exe"), "system processes protected");
        Check(ProcessSafety.IsProtectedName("smartscreen") && ProcessSafety.IsProtectedName("Secure System") && ProcessSafety.IsProtectedName("dwm"), "more protected names");
        Check(!ProcessSafety.IsProtectedName("explorer") && !ProcessSafety.IsProtectedName("notepad"), "explorer and ordinary apps are not protected");
    }

    private static void Atomic()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tl-core-harness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "a.txt");
            Check(AtomicFile.TryCreateNew(file, "one"), "TryCreateNew creates");
            Check(!AtomicFile.TryCreateNew(file, "two"), "TryCreateNew refuses an existing file");
            Check(File.ReadAllText(file) == "one", "content unchanged after the refused create");
            AtomicFile.WriteAllText(file, "three \u2014 x");
            Check(File.ReadAllText(file) == "three \u2014 x", "WriteAllText replaces the content");
            var bytes = File.ReadAllBytes(file);
            Check(bytes.Length > 0 && bytes[0] != 0xEF, "no BOM written");
            AtomicFile.WriteAllText(Path.Combine(dir, "sub", "b.txt"), "new");
            Check(File.ReadAllText(Path.Combine(dir, "sub", "b.txt")) == "new", "WriteAllText creates a new file and folder");
            Check(Directory.EnumerateFiles(dir, "*.tmp", SearchOption.AllDirectories).Count() == 0, "no .tmp files left");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void SettingsPersistence()
    {
        var dir = Path.Combine(Path.GetTempPath(), "trayloom-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        var log = new FakeLog();
        try
        {
            using (var store = new SettingsStore(path, log))
            {
                var tasks = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
                {
                    var scope = store.GetScope("worker" + worker);
                    for (var i = 0; i < 25; i++)
                    {
                        scope.Set("value", i);
                        store.Flush();
                    }
                })).ToArray();
                Task.WaitAll(tasks);
                store.Flush();
            }

            using (var reloaded = new SettingsStore(path, log))
            {
                Check(Enumerable.Range(0, 4).All(i => reloaded.GetScope("worker" + i).Get("value", -1) == 24),
                    "concurrent flushes persist the latest value from every scope");
                reloaded.SuspendSaving();
                reloaded.GetScope("worker0").Set("value", -2);
                reloaded.Flush();
            }

            using (var reloaded = new SettingsStore(path, log))
            {
                Check(reloaded.GetScope("worker0").Get("value", -1) == 24, "suspended saves leave the existing file unchanged");
            }

            Check(!log.Lines.Any(l => l.StartsWith("Failed to save settings")), "concurrent settings writes complete without errors");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private sealed class FakeLog : ILog
    {
        public List<string> Lines { get; } = [];

        public void Info(string message)
        {
            lock (Lines)
            {
                Lines.Add(message);
            }
        }

        public void Warn(string message) => Info(message);

        public void Error(string message, Exception? exception = null) => Info(message);
    }

    private sealed class Hook(string id, Action action) : IStartupRecovery
    {
        public string Id { get; } = id;

        public void Recover(RecoveryStage stage, ILog log) => action();
    }

    private static void Recovery()
    {
        var log = new FakeLog();
        var worked = 0;
        var hooks = new IStartupRecovery[]
        {
            new Hook("throws", () => throw new InvalidOperationException("boom")),
            new Hook("sleeps", () => Thread.Sleep(3000)),
            new Hook("works", () => Interlocked.Increment(ref worked)),
        };
        var watch = Stopwatch.StartNew();
        var lines = StartupRecovery.Run(hooks, RecoveryStage.Startup, log, TimeSpan.FromMilliseconds(500));
        watch.Stop();
        Check(watch.ElapsedMilliseconds <= 700, $"recovery returns within budget + 200 ms ({watch.ElapsedMilliseconds} ms)");
        Check(lines.Count == 3, "one result line per hook");
        Check(lines.Contains("recovery throws: failed"), "a throwing hook is reported failed");
        Check(lines.Contains("recovery sleeps: still running"), "a slow hook is reported still running");
        Check(lines.Contains("recovery works: done") && worked == 1, "a good hook ran and is reported done");
        lock (log.Lines)
        {
            Check(log.Lines.Any(l => l.StartsWith("recovery works: done")) && log.Lines.Any(l => l.StartsWith("recovery sleeps: still running")), "results are logged");
        }

        Check(StartupRecovery.Run([], RecoveryStage.Crash, log, TimeSpan.FromSeconds(1)).Count == 0, "no hooks, no lines");

        // a slow hook must not starve the others: more blocking hooks than pool threads, plus one quick hook that has to run at once
        var blockers = Math.Max(Environment.ProcessorCount * 2, 8);
        var quick = 0;
        var many = new List<IStartupRecovery>();
        for (var n = 0; n < blockers; n++)
        {
            many.Add(new Hook("slow" + n, () => Thread.Sleep(1500)));
        }

        many.Add(new Hook("quick", () => Interlocked.Increment(ref quick)));
        var sw = Stopwatch.StartNew();
        var manyLines = StartupRecovery.Run(many, RecoveryStage.Startup, log, TimeSpan.FromMilliseconds(600));
        sw.Stop();
        Check(quick == 1 && manyLines.Contains("recovery quick: done"), "a quick hook runs although many hooks block");
        Check(sw.ElapsedMilliseconds <= 900, $"the per-stage bound still holds ({sw.ElapsedMilliseconds} ms)");

        // quit budget: the worst case fits inside what the uninstaller and a restarted copy wait
        Check(QuitBudget.ShutdownWait(true) < QuitBudget.ShutdownWait(false), "a finish that already ran shortens the recorder shutdown wait");
        Check(QuitBudget.ShutdownWait(false) == QuitBudget.FinishWait, "without a finish the full wait stays");
        Check(QuitBudget.WorstCase(true, true) <= QuitBudget.OtherProcessWait, "the worst quit (finish + residual + stops + uninstall) fits in the 35 s the uninstaller and --relaunch wait");
        Check(QuitBudget.WorstCase(false, true) <= QuitBudget.OtherProcessWait, "a quit without a prior finish also fits");
        Check(QuitBudget.WorstCase(true, true) > TimeSpan.FromSeconds(15), "the budget is not the old 15 s the old waits assumed");
    }

    private static void LocalRoot()
    {
        var local = Environment.GetEnvironmentVariable("TRAYLOOM_LOCALDATA");
        var data = Environment.GetEnvironmentVariable("TRAYLOOM_DATA");
        var expected = !string.IsNullOrWhiteSpace(local)
            ? local
            : !string.IsNullOrWhiteSpace(data)
                ? Path.Combine(data, "local")
                : Path.Combine(Env(Environment.SpecialFolder.LocalApplicationData), "Trayloom");
        Check(AppPaths.LocalRoot == expected, "AppPaths.LocalRoot follows the environment overrides");
    }
}
