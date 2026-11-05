// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Trayloom.Core;
using Trayloom.Features.Maintenance;

namespace Trayloom.Maintenance.Harness;

/// <summary>
/// Checks of the Maintenance support code: the temp-root rule, the recycle check, the elevated allow-list and the PowerShell copy of it
/// (run non-elevated, printing allow or deny per item; nothing is deleted), and the manifest hash check.
/// </summary>
internal static class SupportChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        TempRoots();
        Recycling();
        var table = AllowTable();
        Allow(table);
        Parity(table);
        Hash();
    }

    private static void Check(bool ok, string what) => _check(ok, "support: " + what);

    private static string Env(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

    private static void TempRoots()
    {
        var local = Env(Environment.SpecialFolder.LocalApplicationData);
        Check(FileOps.IsSafeTempRoot(Path.Combine(local, "Temp")), "local Temp is a safe temp root");
        Check(FileOps.IsSafeTempRoot(Path.Combine(local, "Temp", "2")), "per-session Temp\\2 is a safe temp root");
        Check(!FileOps.IsSafeTempRoot(Path.Combine(local, "Temp", "abc")), "Temp\\abc is not");
        Check(!FileOps.IsSafeTempRoot(Env(Environment.SpecialFolder.UserProfile)), "the profile is not");
        Check(!FileOps.IsSafeTempRoot(@"C:\"), "C:\\ is not");
        Check(!FileOps.IsSafeTempRoot(@"D:\work"), "D:\\work is not");
        Check(!FileOps.IsSafeTempRoot(Env(Environment.SpecialFolder.MyDocuments)), "Documents is not");
        Check(!FileOps.IsSafeTempRoot(@"C:\Temp"), "a two-segment Temp is not (too shallow)");
        var safe = FileOps.SafeTemp;
        Check(safe is null || string.Equals(safe, FileOps.Temp, StringComparison.OrdinalIgnoreCase), "SafeTemp is Temp or null");
        Check(FileOps.IsCriticalOrAncestor(Env(Environment.SpecialFolder.UserProfile)), "FileOps.IsCriticalOrAncestor delegates");
        Check(!FileOps.MayRemove(Path.Combine(Env(Environment.SpecialFolder.UserProfile), "AppData")), "MayRemove refuses an ancestor of a critical root");
        Check(!FileOps.MayRemove(Env(Environment.SpecialFolder.MyDocuments)), "MayRemove refuses Documents");
    }

    private static void Recycling()
    {
        var file = Path.Combine(Path.GetTempPath(), "tl-maint-harness-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(file, "x");
        try
        {
            Check(!FileOps.CanRecycle(file, long.MaxValue), "CanRecycle false for an impossible size");
            Check(!FileOps.CanRecycle(@"\\localhost\c$\x", 1024), "CanRecycle false for a network path");
            Check(FileOps.CanRecycle(file, 1024), "CanRecycle true for 1 KB on a fixed drive");
            Check(WaRecycle.CanRecycle(file, 1024) == FileOps.CanRecycle(file, 1024), "WaRecycle.CanRecycle forwards");
            Check(FileOps.TryRecycle(file + ".missing") == RecycleResult.Missing, "TryRecycle reports a missing path");
            Check(FileOps.TryRecycle(file, long.MaxValue) == RecycleResult.NotRecyclable && File.Exists(file), "TryRecycle refuses a file the bin could not keep, and leaves it");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static List<(Elevated.Item Item, bool Expected, string Why)> AllowTable()
    {
        var profile = Env(Environment.SpecialFolder.UserProfile);
        var local = Env(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Env(Environment.SpecialFolder.ProgramFiles);
        var windows = Env(Environment.SpecialFolder.Windows);
        const string hkcu = @"Registry::HKEY_CURRENT_USER\SOFTWARE\";
        const string hklm = @"Registry::HKEY_LOCAL_MACHINE\SOFTWARE\";
        var t = new List<(Elevated.Item, bool, string)>();

        void Add(string kind, string path, string? name, bool expected, string why) => t.Add((new Elevated.Item(kind, path, name), expected, why));

        Add("file", Path.Combine(local, "Temp", "x", "y.tmp"), null, true, "file in temp");
        Add("file", Path.Combine(programFiles, "Foo", "Bar"), null, true, "file in program files");
        Add("file", programFiles, null, false, "program files root");
        Add("file", Path.Combine(programFiles, "Foo"), null, false, "too shallow");
        Add("file", Path.Combine(windows, "Temp", "x", "y"), null, false, "under windows");
        Add("file", @"C:\", null, false, "drive root");
        Add("file", @"\\server\share\a\b\c", null, false, "unc");
        Add("file", Path.Combine(@"C:\Users\Public", "a", "b"), null, true, "public subfolder");
        Add("file", profile, null, false, "profile");
        Add("file", Path.Combine(profile, "AppData"), null, false, "ancestor of a critical root");
        Add("file", Path.Combine(local, "Programs", "Foo"), null, true, "per-user program folder");
        Add("file", @"foo\bar\baz", null, false, "relative");
        Add("file", @"C:foo\bar\baz", null, false, "drive relative");
        Add("file", Path.Combine(local, "Temp", "..", "..", "..", ".."), null, false, "dot-dot to a critical folder");
        Add("file", @"Q:\a\b\c", null, true, "other drive");
        Add("file", Path.Combine(local, "Packages", "Foo"), null, true, "package folder child");
        Add("file", Path.Combine(local, "Temp", "x\u0001y"), null, false, "control character");
        Add("file", string.Empty, null, false, "empty path");
        Add("file", @"\\?\C:\Foo\Bar\Baz", null, false, "extended prefix");
        Add("reg", hkcu + "Foo", null, true, "hkcu vendor key");
        Add("reg", hklm + @"WOW6432Node\Foo\Bar", null, true, "wow vendor key");
        Add("reg", hklm + @"Microsoft\Windows", null, false, "microsoft");
        Add("reg", hklm + @"Classes\x", null, false, "classes");
        Add("reg", hklm + @"Policies\x", null, false, "policies");
        Add("reg", hklm + @"WOW6432Node\Microsoft\x", null, false, "wow microsoft");
        Add("reg", hklm + "WOW6432Node", null, false, "wow alone");
        Add("reg", hklm + "RegisteredApplications", null, false, "registered applications");
        Add("reg", @"Registry::HKEY_CLASSES_ROOT\Foo", null, false, "classes root hive");
        Add("reg", @"Registry::HKEY_LOCAL_MACHINE\SYSTEM\Foo", null, false, "system hive");
        Add("reg", @"Registry::HKEY_CURRENT_USER\SOFTWARE", null, false, "software itself");
        Add("reg", hkcu + @"Foo\..\Microsoft", null, false, "dot-dot");
        Add("reg", @"registry::hkey_current_user\software\foo", null, true, "case-insensitive");
        Add("reg", @"HKEY_CURRENT_USER\SOFTWARE\Foo", null, false, "no provider prefix");
        Add("reg", @"Registry::HKEY_USERS\.DEFAULT\SOFTWARE\Foo", null, false, "other hive");
        Add("regvalue", hkcu + "Foo", "v", true, "value in vendor key");
        Add("regvalue", hkcu + "Foo", string.Empty, false, "empty name");
        Add("regvalue", hkcu + @"Microsoft\Windows\CurrentVersion\Run", "App", true, "hkcu run");
        Add("regvalue", hklm + @"WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", "App", true, "wow runonce");
        Add("regvalue", hkcu + @"Microsoft\Windows\CurrentVersion\Run\x", "App", false, "below run");
        Add("regvalue", hkcu + @"Microsoft\Windows\CurrentVersion\Uninstall", "App", false, "uninstall");
        Add("regvalue", @"Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules", "r", true, "firewall rules");
        Add("task", @"\Foo", null, true, "root task");
        Add("task", @"\Foo\Bar", null, true, "task in folder");
        Add("task", @"\Microsoft\Windows\X", null, false, "microsoft task");
        Add("task", @"\microsoft\x", null, false, "microsoft task, other case");
        Add("task", "Foo", null, false, "no leading backslash");
        Add("task", @"\Foo\..\Bar", null, false, "dot-dot task");
        Add("task", "\\Fo\"o", null, false, "quote in task");
        Add("task", @"\", null, false, "bare backslash");
        Add("service", "MyService", null, true, "plain service");
        Add("service", "My Service_1.x-y", null, true, "service with allowed punctuation");
        Add("service", "bad/name", null, false, "slash");
        Add("service", new string('a', 129), null, false, "too long");
        Add("service", "a;b", null, false, "semicolon");
        Add("exec", @"C:\a\b\c", null, false, "unknown kind");
        Add("File", Path.Combine(local, "Temp", "x", "y"), null, false, "kind is case-sensitive");
        return t;
    }

    private static void Allow(List<(Elevated.Item Item, bool Expected, string Why)> table)
    {
        foreach (var (item, expected, why) in table)
        {
            Check(Elevated.IsAllowed(item) == expected, $"IsAllowed {item.Kind}: {why}");
        }

        Check(table.Count >= 40, $"allow table has {table.Count} cases");
    }

    private static void Parity(List<(Elevated.Item Item, bool Expected, string Why)> table)
    {
        var json = Path.Combine(Path.GetTempPath(), "tl-maint-parity-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllBytes(json, JsonSerializer.SerializeToUtf8Bytes(table.Select(r => new { kind = r.Item.Kind, path = r.Item.Path, name = r.Item.Name })));
            var script = "$ErrorActionPreference = 'Stop'\n"
                + Elevated.ScriptPreamble()
                + Elevated.ValidationScript + "\n"
                + $"$items = [IO.File]::ReadAllText('{json.Replace("'", "''")}', [Text.Encoding]::UTF8) | ConvertFrom-Json\n"
                + "foreach ($i in @($items)) { if (Test-Allowed ([string]$i.kind) ([string]$i.path) ([string]$i.name)) { 'allow' } else { 'deny' } }\n";
            var (code, output) = RunPowerShell(script);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Check(code == 0 && lines.Length == table.Count, $"parity script ran ({lines.Length} lines, exit {code})");
            if (lines.Length == table.Count)
            {
                for (var n = 0; n < table.Count; n++)
                {
                    var expected = Elevated.IsAllowed(table[n].Item) ? "allow" : "deny";
                    Check(lines[n] == expected, $"parity {table[n].Item.Kind}: {table[n].Why} (C# {expected}, script {lines[n]})");
                }
            }
        }
        finally
        {
            File.Delete(json);
        }
    }

    private static void Hash()
    {
        var items = new[] { new Elevated.Item("file", Path.Combine(Path.GetTempPath(), "x", "y", "z")), new Elevated.Item("task", @"\Foo") };
        var bytes = Elevated.BuildManifest(items);
        Check(bytes.Length > 0 && bytes[0] == (byte)'{', "manifest is UTF-8 JSON without BOM");
        var hash = Elevated.HashHex(bytes);
        var dir = Path.Combine(Path.GetTempPath(), "tl-maint-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manifest = Path.Combine(dir, "manifest.json");
            File.WriteAllBytes(manifest, bytes);
            string Script() => "$ErrorActionPreference = 'Stop'\n"
                + $"$manifestPath = '{manifest.Replace("'", "''")}'\n$expectedHash = '{hash}'\n"
                + Elevated.HashPrologue + "\n'hash-ok'\n";
            var (okCode, okOut) = RunPowerShell(Script());
            Check(okCode == 0 && okOut.Contains("hash-ok"), "an untouched manifest passes the hash check");
            var flipped = (byte[])bytes.Clone();
            flipped[^3] ^= 0x01;
            File.WriteAllBytes(manifest, flipped);
            var (badCode, badOut) = RunPowerShell(Script());
            Check(badCode == 3 && !badOut.Contains("hash-ok"), "a manifest with one flipped byte fails with exit code 3");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static (int Code, string Output) RunPowerShell(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var exe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var info = new ProcessStartInfo(exe, $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        process.StandardError.ReadToEndAsync();
        process.WaitForExit(60_000);
        return (process.ExitCode, output.Result);
    }
}
