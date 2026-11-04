// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>How a browser close ended.</summary>
internal enum BrowserCloseOutcome
{
    Closed,
    ClosedInBackground,
    Refused,
    Cancelled,
}

/// <summary>
/// Chrome and Edge (spec 07 section 3): recognising them, finding the profile folder, the private-window and download checks, and the
/// Restart Manager close. Only the two Chromium browsers Trayloom could verify are closed; the others are only minimized.
/// Browser history, cookies, sessions and tab titles are never read or kept: the only files looked at are <c>Local State</c> (profile
/// display names) and each profile's <c>Preferences</c> (two download-folder keys), in memory, for the length of one close pass.
/// </summary>
internal static class AwayBrowsers
{
    private const int FindTimeoutMs = 1500;
    private const int ExitWaitMs = 10000;
    private const int RecentDownloadMs = 30000;
    private const int MaxFilesLooked = 500;

    private static readonly char[] ZeroWidth = ['\u200B', '\u200C', '\u200D', '\uFEFF'];

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(in Guid folder, uint flags, nint token, [MarshalAs(UnmanagedType.LPWStr)] out string path);

    /// <summary>The kind a browser executable gets, or null when the file is not a browser Trayloom knows.</summary>
    public static AwayAppKind? KindOf(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath))
        {
            return null;
        }

        var file = Path.GetFileName(exePath).ToLowerInvariant();
        return file switch
        {
            "chrome.exe" or "msedge.exe" => AwayAppKind.Browser,
            "firefox.exe" or "brave.exe" or "vivaldi.exe" or "opera.exe" => AwayAppKind.BrowserMin,
            "launcher.exe" when exePath.Contains("\\opera", StringComparison.OrdinalIgnoreCase) => AwayAppKind.BrowserMin,
            _ => null,
        };
    }

    public static bool IsEdge(string exePath) => Path.GetFileName(exePath).Equals("msedge.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>The <c>--user-data-dir</c> of a command line, or an empty string for the default profile folder.</summary>
    public static string UserDataDirOf(string commandLine) => AwayProcess.SwitchValue(commandLine, "--user-data-dir") ?? string.Empty;

    /// <summary>The folder the browser really uses (the default one when none was given).</summary>
    public static string ResolveUserDataDir(string exePath, string udd)
    {
        if (!string.IsNullOrWhiteSpace(udd))
        {
            return udd;
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return IsEdge(exePath) ? Path.Combine(local, "Microsoft", "Edge", "User Data") : Path.Combine(local, "Google", "Chrome", "User Data");
    }

    /// <summary>True when both name the same profile folder (an empty name is the default folder).</summary>
    public static bool SameUserDataDir(string exePath, string a, string b)
    {
        try
        {
            var left = Path.GetFullPath(ResolveUserDataDir(exePath, a)).TrimEnd('\\');
            var right = Path.GetFullPath(ResolveUserDataDir(exePath, b)).TrimEnd('\\');
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------------------------------ private windows

    /// <summary>
    /// Fail-safe private-window check (spec 07 section 3.3): a window counts as a normal one only when its UI Automation root view
    /// carries the browser's plain name suffix. Anything else, an error or a timeout counts as private.
    /// </summary>
    public static bool HasPrivateWindow(string exePath, string commandLine, string udd, IEnumerable<nint> windows)
    {
        if (commandLine.Contains("--incognito", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("--inprivate", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("--guest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var edge = IsEdge(exePath);
        var profiles = edge ? [] : ProfileNames(ResolveUserDataDir(exePath, udd));
        var any = false;
        foreach (var hwnd in windows)
        {
            any = true;
            var name = RootViewName(hwnd);
            if (name is null)
            {
                return true;
            }

            foreach (var mark in ZeroWidth)
            {
                name = name.Replace(mark.ToString(), string.Empty, StringComparison.Ordinal);
            }

            if (!IsNormalName(name, edge, profiles))
            {
                return true;
            }
        }

        // No window to look at is no proof of anything.
        return !any;
    }

    private static bool IsNormalName(string name, bool edge, IReadOnlyList<string> profiles)
    {
        if (edge)
        {
            return name.EndsWith(" - Microsoft Edge", StringComparison.Ordinal);
        }

        if (name.EndsWith(" - Google Chrome", StringComparison.Ordinal))
        {
            return true;
        }

        return profiles.Any(p => p.Length > 0 && name.EndsWith(" - Google Chrome - " + p, StringComparison.Ordinal));
    }

    /// <summary>The name of the window's <c>BrowserRootView</c> (it is the window title plus a private marker); null when not found.</summary>
    private static string? RootViewName(nint hwnd)
    {
        // Chromium builds the accessibility tree of its window frame on the first query: one retry is cheap.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var task = Task.Run(() =>
            {
                try
                {
                    var window = AutomationElement.FromHandle(hwnd);
                    var view = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "BrowserRootView"));
                    return view?.Current.Name;
                }
                catch (Exception)
                {
                    return null;
                }
            });
            if (task.Wait(FindTimeoutMs) && task.Result is { } name)
            {
                return name;
            }

            Thread.Sleep(250);
        }

        return null;
    }

    private static List<string> ProfileNames(string userDataDir)
    {
        var names = new List<string>();
        foreach (var (_, name, _) in Profiles(userDataDir))
        {
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>Profile folder, display name and directory of each profile in <c>Local State</c>; in memory only.</summary>
    private static IEnumerable<(string Directory, string Name, string Path)> Profiles(string userDataDir)
    {
        JsonDocument? document = null;
        try
        {
            using var stream = new FileStream(Path.Combine(userDataDir, "Local State"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            document = JsonDocument.Parse(stream);
        }
        catch (Exception)
        {
            // no Local State: the default profile folder is looked at by the caller
        }

        var found = new List<(string, string, string)>();
        if (document is not null)
        {
            using (document)
            {
                if (document.RootElement.TryGetProperty("profile", out var profile)
                    && profile.TryGetProperty("info_cache", out var cache)
                    && cache.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in cache.EnumerateObject())
                    {
                        var name = entry.Value.ValueKind == JsonValueKind.Object && entry.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                            ? n.GetString() ?? string.Empty
                            : string.Empty;
                        found.Add((entry.Name, name, Path.Combine(userDataDir, entry.Name)));
                    }
                }
            }
        }

        if (found.Count == 0)
        {
            found.Add(("Default", string.Empty, Path.Combine(userDataDir, "Default")));
        }

        return found;
    }

    // ------------------------------------------------------------------------------------------ downloads

    /// <summary>
    /// True when a <c>*.crdownload</c> file changed in the last 30 s in the folders downloads go to: each profile's default and
    /// "save as" folder and the user's Downloads folder. Downloads saved elsewhere are not seen (best effort).
    /// </summary>
    public static bool DownloadInProgress(string exePath, string udd)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, 0, out var downloads) == 0 && !string.IsNullOrEmpty(downloads))
            {
                folders.Add(downloads);
            }
        }
        catch (Exception)
        {
            // fall back to the profile folders below
        }

        foreach (var (_, _, profilePath) in Profiles(ResolveUserDataDir(exePath, udd)))
        {
            try
            {
                var preferences = Path.Combine(profilePath, "Preferences");
                if (!File.Exists(preferences))
                {
                    continue;
                }

                using var stream = new FileStream(preferences, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(stream);
                AddFolder(folders, document.RootElement, "download", "default_directory");
                AddFolder(folders, document.RootElement, "savefile", "default_directory");
            }
            catch (Exception)
            {
                // an unreadable Preferences file only means one folder less to look at
            }
        }

        var cutoff = DateTime.UtcNow - TimeSpan.FromMilliseconds(RecentDownloadMs);
        foreach (var folder in folders)
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                var looked = 0;
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.crdownload"))
                {
                    if (file.LastWriteTimeUtc >= cutoff)
                    {
                        return true;
                    }

                    if (++looked >= MaxFilesLooked)
                    {
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // a folder that cannot be listed is not evidence of a download
            }
        }

        return false;
    }

    private static void AddFolder(HashSet<string> folders, JsonElement root, string section, string key)
    {
        if (root.TryGetProperty(section, out var group)
            && group.ValueKind == JsonValueKind.Object
            && group.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } path)
        {
            folders.Add(path);
        }
    }

    // ------------------------------------------------------------------------------------------ closing

    /// <summary>
    /// Closes one browser instance through the Restart Manager (spec 07 section 3.2). Never forced: if the browser says no, it stays
    /// open. <paramref name="hasWindows"/> tells whether the main process still owns a window.
    /// </summary>
    public static BrowserCloseOutcome Close(uint pid, long start, Func<bool> hasWindows, Func<bool> cancelled, int watchdogMs, ILog log)
    {
        var (code, milliseconds) = AwayRestartManager.Shutdown(pid, start, cancelled, watchdogMs);
        log.Info($"[away] close: rm shutdown {code} in {milliseconds} ms");

        using var handle = AwayProcess.Handle.Open(pid, start);
        if (handle is null || handle.HasExited)
        {
            return BrowserCloseOutcome.Closed;
        }

        if (code != 0)
        {
            return code == 1223 || cancelled() ? BrowserCloseOutcome.Cancelled : BrowserCloseOutcome.Refused;
        }

        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ExitWaitMs)
        {
            if (handle.WaitExit(100))
            {
                return BrowserCloseOutcome.Closed;
            }
        }

        return hasWindows() ? BrowserCloseOutcome.Refused : BrowserCloseOutcome.ClosedInBackground;
    }
}
