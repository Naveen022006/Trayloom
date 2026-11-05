// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Maintenance;

namespace Trayloom.Maintenance.Harness;

/// <summary>
/// Checks of the Cleaner: the removal validation, the offline-drive rule, the INetCache filter, the Electron marker rule and the
/// WhatsApp zone rule. Only pure logic and temp folders; nothing real is touched.
/// </summary>
internal static class CleanerChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Validation();
        Missing();
        InetCache();
        Electron();
        Zone();
    }

    private static void Check(bool ok, string what) => _check(ok, "cleaner: " + what);

    private static CleanItem Item(RemoveKind kind, string path, string? root = null) =>
        new() { Kind = kind, Path = path, Paths = [path], Root = root };

    private static void Validation()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
        var users = Path.GetDirectoryName(profile) ?? @"C:\Users";
        foreach (var kind in Enum.GetValues<RemoveKind>())
        {
            foreach (var path in new[] { profile, users, docs, systemDrive })
            {
                Check(!CleanerModel.Validate(Item(kind, path), path), $"Validate refuses {path} for {kind}");
            }
        }

        var winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        Check(CleanerModel.Validate(Item(RemoveKind.PermanentContents, winTemp), winTemp), "Validate accepts Windows\\Temp for PermanentContents");
        Check(!CleanerModel.Validate(Item(RemoveKind.Permanent, winTemp), winTemp), "Validate refuses Windows\\Temp for Permanent");
        Check(!CleanerModel.Validate(Item(RemoveKind.Recycle, winTemp), winTemp), "Validate refuses Windows\\Temp for Recycle");
        var other = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");
        Check(!CleanerModel.Validate(Item(RemoveKind.PermanentContents, other), other), "Validate refuses Windows\\System32 for PermanentContents");

        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "tl-cleaner-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(local);
        try
        {
            var item = Item(RemoveKind.PermanentContents, local, local);
            Check(CleanerModel.Validate(item, local), "Validate accepts a folder under the user's Temp");
            Check(!CleanerModel.Validate(Item(RemoveKind.PermanentContents, local, Path.Combine(local, "other")), local), "Validate refuses a path outside its scan root");
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    private static void Missing()
    {
        // a path that cannot exist, so the probe decides
        const string path = @"Q:\tl-no-such-folder\app.exe";
        Check(CleanerScanner.TargetIsVerifiablyMissing(path, _ => (DriveType.Fixed, true)), "fixed and ready and missing is missing");
        Check(!CleanerScanner.TargetIsVerifiablyMissing(path, _ => (DriveType.Removable, true)), "removable is never missing");
        Check(!CleanerScanner.TargetIsVerifiablyMissing(path, _ => (DriveType.Network, true)), "network is never missing");
        Check(!CleanerScanner.TargetIsVerifiablyMissing(path, _ => (DriveType.CDRom, true)), "optical is never missing");
        Check(!CleanerScanner.TargetIsVerifiablyMissing(path, _ => (DriveType.NoRootDirectory, false)), "no root directory is never missing");
        Check(!CleanerScanner.TargetIsVerifiablyMissing(path, _ => (DriveType.Fixed, false)), "a fixed drive that is not ready is never missing");
        Check(!CleanerScanner.TargetIsVerifiablyMissing(path, _ => null), "an unknown drive is never missing");
        Check(!CleanerScanner.TargetIsVerifiablyMissing(path, _ => throw new IOException("boom")), "a throwing probe is never missing");
        Check(!CleanerScanner.TargetIsVerifiablyMissing(@"\\server\share\app.exe", _ => (DriveType.Fixed, true)), "a UNC path is never missing");
        Check(!CleanerScanner.TargetIsVerifiablyMissing(Environment.ProcessPath ?? @"C:\Windows\explorer.exe", _ => (DriveType.Fixed, true)), "an existing file is not missing");
    }

    private static string MakeTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tl-cleaner-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void InetCache()
    {
        var dir = MakeTemp();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Content.Outlook"));
            Directory.CreateDirectory(Path.Combine(dir, "IE"));
            Directory.CreateDirectory(Path.Combine(dir, "Low"));
            var names = CleanerScanner.InternetCacheFolders(dir).Select(Path.GetFileName).ToList();
            Check(names.Count == 2 && names.Contains("IE") && names.Contains("Low"), "INetCache keeps IE and Low");
            Check(!names.Contains("Content.Outlook"), "INetCache drops Content.Outlook");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void Electron()
    {
        var dir = MakeTemp();
        try
        {
            var plain = Path.Combine(dir, "plain");
            var state = Path.Combine(dir, "state");
            var prefs = Path.Combine(dir, "prefs");
            Directory.CreateDirectory(Path.Combine(plain, "Cache"));
            Directory.CreateDirectory(state);
            Directory.CreateDirectory(prefs);
            File.WriteAllText(Path.Combine(state, "Local State"), "{}");
            File.WriteAllText(Path.Combine(prefs, "Preferences"), "{}");
            Check(!CleanerScanner.IsElectronProfile(plain), "a folder with only Cache is not an Electron profile");
            Check(CleanerScanner.IsElectronProfile(state), "Local State marks an Electron profile");
            Check(CleanerScanner.IsElectronProfile(prefs), "Preferences marks an Electron profile");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void Zone()
    {
        Check(WaRules.ZoneTextIsWhatsApp("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://web.whatsapp.com/x"), "a WhatsApp HostUrl matches");
        Check(!WaRules.ZoneTextIsWhatsApp("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://web.whatsapp.com/\r\nHostUrl=https://example.com/a"), "a WhatsApp ReferrerUrl alone does not match");
    }
}
