// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using Microsoft.Win32;
using Trayloom.Features.WindowTools.AwayMode;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>What a browser read of open tabs returned: one list of addresses per window, or the reason there is none.</summary>
internal sealed record BrowserTabsResult(bool Ok, IReadOnlyList<IReadOnlyList<string>> Windows, string? Problem);

/// <summary>A browser the person can pick for a web item. <paramref name="Id"/> is <c>"default"</c> or the lower-cased path of the executable.</summary>
internal sealed record BrowserChoice(string Id, string Name, bool Chromium);

/// <summary>
/// Browsers for Modes (spec 08 sections 2.4 and 2.5): the default browser, the installed ones, Chromium profiles, and the
/// on-demand reader of the open tabs. Browser history, cookies and page titles are never read: the only files looked at are
/// <c>Local State</c> (profile names) and the newest session file, in memory, when the person asks for the tabs.
/// </summary>
internal static class ModesBrowsers
{
    /// <summary>The largest session file the reader opens.</summary>
    public const long MaxSessionBytes = 64L * 1024 * 1024;

    private static readonly string[] ChromiumFiles = ["chrome.exe", "msedge.exe", "brave.exe", "vivaldi.exe"];

    /// <summary>True for a Chromium-family browser whose launch takes <c>--profile-directory</c> and <c>--new-window</c>.</summary>
    public static bool IsChromium(string? exe) => ChromiumFiles.Contains(Path.GetFileName(exe ?? string.Empty), StringComparer.OrdinalIgnoreCase);

    public static bool IsFirefox(string? exe) => string.Equals(Path.GetFileName(exe ?? string.Empty), "firefox.exe", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------------------------------ choices

    /// <summary>The default browser first, then every browser that registered itself with Windows.</summary>
    public static IReadOnlyList<BrowserChoice> Choices()
    {
        var list = new List<BrowserChoice> { new("default", ModesStrings.DefaultBrowser, IsChromium(DefaultBrowserExe())) };
        foreach (var (exe, name) in Installed())
        {
            list.Add(new BrowserChoice(exe, name, IsChromium(exe)));
        }

        return list;
    }

    /// <summary>The executable a browser id stands for, or null (the default browser is unknown or is not a classic program).</summary>
    public static string? Resolve(string? browserId)
    {
        if (string.IsNullOrWhiteSpace(browserId) || browserId == "default")
        {
            return DefaultBrowserExe();
        }

        var id = browserId.Trim();
        if (File.Exists(id))
        {
            return id.ToLowerInvariant();
        }

        // A bare file name ("chrome.exe") finds the installed browser of that name.
        var byName = Installed().FirstOrDefault(b => string.Equals(Path.GetFileName(b.Exe), id, StringComparison.OrdinalIgnoreCase));
        return byName.Exe;
    }

    /// <summary>
    /// The executable of the default browser: the https handler's <c>ProgId</c> of the current user, then that class's open command.
    /// Null when the handler is not an ordinary program (a packaged browser), in which case the caller hands the address to the shell.
    /// </summary>
    public static string? DefaultBrowserExe()
    {
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            if (choice?.GetValue("ProgId") is not string progId || progId.Length == 0)
            {
                return null;
            }

            using var command = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
            return command?.GetValue(null) is string raw && FirstToken(raw.Trim()) is { Length: > 0 } exe && File.Exists(exe) ? exe.ToLowerInvariant() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<(string Exe, string Name)> Installed()
    {
        var result = new List<(string, string)>();
        foreach (var (hive, view) in new[] { (RegistryHive.CurrentUser, RegistryView.Default), (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var clients = root.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet");
                if (clients is null)
                {
                    continue;
                }

                foreach (var key in clients.GetSubKeyNames())
                {
                    using var entry = clients.OpenSubKey(key);
                    using var command = entry?.OpenSubKey(@"shell\open\command");
                    if (command?.GetValue(null) as string is not { Length: > 0 } raw)
                    {
                        continue;
                    }

                    var exe = FirstToken(raw.Trim()).ToLowerInvariant();
                    if (!File.Exists(exe) || result.Any(r => r.Item1 == exe))
                    {
                        continue;
                    }

                    var name = entry?.GetValue(null) as string;
                    result.Add((exe, string.IsNullOrWhiteSpace(name) ? AppIdentity.DisplayName(exe) : name.Trim()));
                }
            }
            catch (Exception)
            {
                // a hive that cannot be read adds nothing
            }
        }

        return result;
    }

    /// <summary>The first word of a command line, without its quotes.</summary>
    private static string FirstToken(string command)
    {
        if (command.StartsWith('"'))
        {
            var close = command.IndexOf('"', 1);
            return close < 0 ? command[1..] : command[1..close];
        }

        var space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }

    // ------------------------------------------------------------------------------------------ Chromium profiles

    /// <summary>The folder a Chromium browser keeps its profiles in, when it is run without <c>--user-data-dir</c>; null for a browser that is not Chromium.</summary>
    public static string? DefaultUserDataDir(string exe)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.GetFileName(exe).ToLowerInvariant() switch
        {
            "chrome.exe" => Path.Combine(local, "Google", "Chrome", "User Data"),
            "msedge.exe" => Path.Combine(local, "Microsoft", "Edge", "User Data"),
            "brave.exe" => Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
            "vivaldi.exe" => Path.Combine(local, "Vivaldi", "User Data"),
            _ => null,
        };
    }

    /// <summary>Profile folder and display name of each profile of a Chromium browser, from its <c>Local State</c> file; <c>Default</c> when there is none.</summary>
    public static IReadOnlyList<(string Dir, string Name)> ChromiumProfiles(string? browserId)
    {
        var result = new List<(string, string)>();
        var exe = Resolve(browserId);
        if (exe is not null && DefaultUserDataDir(exe) is { } udd)
        {
            using var state = ReadLocalState(udd);
            if (state is not null
                && state.RootElement.TryGetProperty("profile", out var profile)
                && profile.TryGetProperty("info_cache", out var cache)
                && cache.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in cache.EnumerateObject())
                {
                    var name = entry.Value.ValueKind == JsonValueKind.Object && entry.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString()
                        : null;
                    result.Add((entry.Name, string.IsNullOrWhiteSpace(name) ? entry.Name : name.Trim()));
                }
            }
        }

        if (result.Count == 0)
        {
            result.Add(("Default", "Default"));
        }

        return result;
    }

    /// <summary>
    /// The profile folders a running browser has open: the <c>--profile-directory</c> switch of its command line if present, else
    /// <c>profile.last_active_profiles</c> of <c>Local State</c>, else <c>Default</c>.
    /// </summary>
    public static IReadOnlyList<string> ActiveProfiles(string userDataDir, string commandLine)
    {
        if (AwayProcess.SwitchValue(commandLine, "--profile-directory") is { Length: > 0 } fromSwitch)
        {
            return [fromSwitch];
        }

        var result = new List<string>();
        using var state = ReadLocalState(userDataDir);
        if (state is not null
            && state.RootElement.TryGetProperty("profile", out var profile)
            && profile.TryGetProperty("last_active_profiles", out var active)
            && active.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(active.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Where(s => s.Length > 0));
        }

        if (result.Count == 0)
        {
            result.Add("Default");
        }

        return result;
    }

    private static JsonDocument? ReadLocalState(string userDataDir)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(userDataDir, "Local State"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonDocument.Parse(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------------------------------ tab reader

    /// <summary>
    /// Reads the windows and tabs of Chrome or Edge from the newest session file of a profile. Runs only when the person presses
    /// "Add current browser tabs" or "Save what's open now"; nothing is kept and nothing is sent anywhere. Blocking: call off the UI thread.
    /// </summary>
    public static BrowserTabsResult ReadTabs(string? browserId, string? profile)
    {
        var exe = Resolve(browserId);
        if (exe is null || AwayBrowsers.KindOf(exe) != AwayAppKind.Browser)
        {
            return new BrowserTabsResult(false, [], ModesStrings.TabsChromeEdgeOnly);
        }

        var udd = DefaultUserDataDir(exe)!;
        var folder = string.IsNullOrWhiteSpace(profile) ? ActiveProfiles(udd, string.Empty)[0] : profile;
        var windows = ReadSession(udd, folder);
        return windows is null
            ? new BrowserTabsResult(false, [], ModesStrings.TabsUnreadable(AppIdentity.DisplayName(exe)))
            : new BrowserTabsResult(true, [.. windows.Select(w => (IReadOnlyList<string>)w.Urls)], null);
    }

    /// <summary>
    /// The parsed windows of the newest <c>Sessions\Session_*</c> file of a profile (or <c>Current Session</c> on old versions);
    /// null when there is none, it is too big, or the parser refuses it.
    /// </summary>
    public static List<SessionWindow>? ReadSession(string userDataDir, string profile)
    {
        // The profile name comes from a saved mode or the editor: it must be one folder name and nothing else.
        if (profile.Length == 0 || profile.Contains('\\') || profile.Contains('/') || profile.Contains(".."))
        {
            return null;
        }

        try
        {
            var folder = Path.Combine(userDataDir, profile);
            var sessions = Path.Combine(folder, "Sessions");
            var file = Directory.Exists(sessions)
                ? new DirectoryInfo(sessions).EnumerateFiles("Session_*").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
                : null;
            if (file is null && File.Exists(Path.Combine(folder, "Current Session")))
            {
                file = Path.Combine(folder, "Current Session");
            }

            if (file is null)
            {
                return null;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxSessionBytes)
            {
                return null;
            }

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return ChromiumSession.Parse(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
