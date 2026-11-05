// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// The pure rules of Modes (spec 08 section 2.3): ids, sanitising, web addresses, keys and dedupe, argument rules for editors, the
/// file-extension guard and the browser argument list. Nothing here touches a window, a process, the registry or the disk, so the
/// console harness can run all of it.
/// </summary>
internal static class ModesMath
{
    public const int MaxModes = 12;
    public const int MaxItems = 20;
    public const int MaxUrls = 50;
    public const int MaxUrlLength = 2048;
    public const int MaxNameLength = 40;

    /// <summary>The twelve colour names of a mode, in the order of the editor's swatches.</summary>
    public static readonly IReadOnlyList<string> Colors =
        ["blue", "purple", "pink", "red", "orange", "yellow", "green", "mint", "teal", "cyan", "indigo", "graphite"];

    /// <summary>The twelve Segoe Fluent Icons glyphs a mode can wear: code, library, edit, globe, game, music, work, home, lightbulb, chat, calendar, heart.</summary>
    public static readonly IReadOnlyList<string> Glyphs =
        ["", "", "", "", "", "", "", "", "", "", "", ""];

    private static readonly string[] BlockedExtensions =
    [
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".scr",
        ".hta", ".cpl", ".reg", ".lnk", ".url", ".appref-ms",
    ];

    private static readonly string[] Forwarders = ["code.exe", "code - insiders.exe", "cursor.exe", "windsurf.exe"];

    // ------------------------------------------------------------------------------------------ ids

    /// <summary>A short random id for a mode (<c>m3f9a2c1d</c>).</summary>
    public static string NewModeId() => "m" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>An item id (<c>i1</c>, <c>i2</c>...) that no other item of the mode has.</summary>
    public static string NewItemId(Mode mode)
    {
        var taken = mode.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var n = mode.Items.Count + 1;
        while (taken.Contains("i" + n))
        {
            n++;
        }

        return "i" + n;
    }

    // ------------------------------------------------------------------------------------------ sanitising

    /// <summary>
    /// A clean copy of <paramref name="file"/>: unusable items dropped, limits applied, ids made unique, colours, glyphs and zones put
    /// back on their lists, and exactly one front item per mode. The input is never changed.
    /// </summary>
    public static ModesFile Sanitize(ModesFile? file)
    {
        var result = new ModesFile { Version = 1 };
        if (file?.Modes is null)
        {
            return result;
        }

        var modeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in file.Modes.Where(m => m is not null).Take(MaxModes))
        {
            var mode = new Mode
            {
                Id = Unique(string.IsNullOrWhiteSpace(source.Id) ? NewModeId() : source.Id.Trim(), modeIds),
                Name = Truncate(source.Name, MaxNameLength) is { Length: > 0 } name ? name : Loc.T("Mode"),
                Glyph = Glyphs.Contains(source.Glyph) ? source.Glyph : Glyphs[0],
                Color = Colors.Contains(source.Color) ? source.Color : "blue",
                OnLeave = source.OnLeave is "keep" or "minimize" or "close" ? source.OnLeave : "minimize",
                OnEnter = new ModeEnterActions
                {
                    KeepAwake = source.OnEnter?.KeepAwake is "off" or "60" or "120" or "on" ? source.OnEnter.KeepAwake : "off",
                    Volume = source.OnEnter is null || source.OnEnter.Volume < 0 ? -1 : Math.Min(100, source.OnEnter.Volume),
                    Timer = source.OnEnter?.Timer ?? false,
                },
            };

            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in (source.Items ?? []).Where(i => i is not null))
            {
                if (mode.Items.Count >= MaxItems)
                {
                    break;
                }

                if (SanitizeItem(item) is not { } clean)
                {
                    continue;
                }

                clean.Id = Unique(string.IsNullOrWhiteSpace(item.Id) ? "i" + (mode.Items.Count + 1) : item.Id.Trim(), itemIds);
                mode.Items.Add(clean);
            }

            // Exactly one item is brought to the front: the first one marked, else the first one.
            var front = mode.Items.FirstOrDefault(i => i.Front) ?? mode.Items.FirstOrDefault();
            foreach (var item in mode.Items)
            {
                item.Front = ReferenceEquals(item, front);
            }

            result.Modes.Add(mode);
        }

        return result;
    }

    private static ModeItem? SanitizeItem(ModeItem source)
    {
        var item = new ModeItem
        {
            Kind = source.Kind,
            Zone = ZoneOk(source.Zone) ? source.Zone : null,
            Front = source.Front,
        };

        switch (source.Kind)
        {
            case ModeItemKind.App:
                if (string.IsNullOrWhiteSpace(source.Exe) && string.IsNullOrWhiteSpace(source.Aumid))
                {
                    return null;
                }

                item.Exe = string.IsNullOrWhiteSpace(source.Exe) ? null : source.Exe.Trim();
                item.Aumid = string.IsNullOrWhiteSpace(source.Aumid) ? null : source.Aumid.Trim();
                item.Args = source.Args ?? string.Empty;
                item.Dir = source.Dir ?? string.Empty;
                item.Name = Truncate(source.Name, MaxNameLength) is { Length: > 0 } app ? app : DefaultName(item);
                return item;
            case ModeItemKind.Web:
                var urls = (source.Urls ?? []).Select(NormalizeUrl).Where(u => u is not null).Select(u => u!).Distinct(StringComparer.Ordinal).Take(MaxUrls).ToList();
                if (urls.Count == 0)
                {
                    return null;
                }

                item.Urls = urls;
                item.Browser = string.IsNullOrWhiteSpace(source.Browser) ? "default" : source.Browser.Trim();
                item.Profile = source.Profile ?? string.Empty;
                item.Name = Truncate(source.Name, MaxNameLength) is { Length: > 0 } web ? web : DefaultName(item);
                return item;
            case ModeItemKind.Path:
                if (string.IsNullOrWhiteSpace(source.Path))
                {
                    return null;
                }

                item.Path = source.Path.Trim();
                item.Name = Truncate(source.Name, MaxNameLength) is { Length: > 0 } path ? path : DefaultName(item);
                return item;
            default:
                return null;
        }
    }

    /// <summary>A name for an item that has none: the program's file name, the first address's host, or the folder's name.</summary>
    private static string DefaultName(ModeItem item)
    {
        string name;
        switch (item.Kind)
        {
            case ModeItemKind.App:
                name = item.Exe is null ? item.Aumid ?? string.Empty : System.IO.Path.GetFileNameWithoutExtension(item.Exe);
                break;
            case ModeItemKind.Web:
                name = item.Urls.Count > 0 && Uri.TryCreate(item.Urls[0], UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
                break;
            default:
                name = System.IO.Path.GetFileName((item.Path ?? string.Empty).TrimEnd('\\', '/'));
                if (name.Length == 0)
                {
                    name = item.Path ?? string.Empty;
                }

                break;
        }

        return Truncate(name, MaxNameLength) is { Length: > 0 } clean ? clean : Loc.T("Item");
    }

    /// <summary>True for a zone a mode item may use: halves, thirds, corners or maximize.</summary>
    public static bool ZoneOk(string? zone)
    {
        if (zone is null || LayoutActions.Get(zone) is not { } action)
        {
            return false;
        }

        return action.Group is LayoutGroup.Halves or LayoutGroup.Thirds or LayoutGroup.Corners || action.Raw == "maximize";
    }

    private static string Truncate(string? text, int max)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max].TrimEnd();
    }

    /// <summary>The id, or the id with <c>-2</c>, <c>-3</c>... when it is taken; the result is added to <paramref name="taken"/>.</summary>
    private static string Unique(string id, HashSet<string> taken)
    {
        var candidate = id;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            candidate = id + "-" + n;
        }

        return candidate;
    }

    // ------------------------------------------------------------------------------------------ web addresses

    /// <summary>
    /// A clean http or https address, or null. Text without a scheme that looks like a host (a dot, no spaces) gets <c>https://</c>.
    /// Anything that is not a plain web page (<c>javascript:</c>, <c>file:</c>, <c>chrome://</c>, <c>data:</c>), carries a user name
    /// or password, or is longer than 2048 characters is refused.
    /// </summary>
    public static string? NormalizeUrl(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxUrlLength)
        {
            return null;
        }

        if (!text.Contains("://", StringComparison.Ordinal) && text.Contains('.') && !text.Any(char.IsWhiteSpace))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0
            || uri.Host.Length == 0)
        {
            return null;
        }

        var result = uri.AbsoluteUri;
        return result.Length <= MaxUrlLength ? result : null;
    }

    /// <summary>
    /// The addresses in pasted text: split on spaces and line breaks (not commas, which can be part of an address), normalised,
    /// de-duplicated with the first kept, at most 50. <paramref name="rejected"/> counts the entries that were not web pages.
    /// </summary>
    public static List<string> ParseUrlList(string? text, out int rejected)
    {
        rejected = 0;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (NormalizeUrl(part) is not { } url)
            {
                rejected++;
                continue;
            }

            if (result.Count < MaxUrls && seen.Add(url))
            {
                result.Add(url);
            }
        }

        return result;
    }

    /// <summary>Splits the addresses of a captured browser into windows of at most <paramref name="perWindow"/> tabs.</summary>
    public static List<List<string>> GroupUrls(IReadOnlyList<string> urls, int perWindow = MaxUrls)
    {
        var size = Math.Max(1, perWindow);
        var groups = new List<List<string>>();
        for (var i = 0; i < urls.Count; i += size)
        {
            groups.Add([.. urls.Skip(i).Take(size)]);
        }

        return groups;
    }

    /// <summary>
    /// The arguments of a Chromium browser launch: the profile folder when there is one, <c>--new-window</c> and then the addresses.
    /// They are always handed over through <c>ProcessStartInfo.ArgumentList</c>, so an address can never become another switch.
    /// </summary>
    public static IReadOnlyList<string> ChromiumArgs(string? profile, IReadOnlyList<string> urls)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(profile))
        {
            args.Add("--profile-directory=" + profile);
        }

        args.Add("--new-window");
        args.AddRange(urls);
        return args;
    }

    // ------------------------------------------------------------------------------------------ keys

    /// <summary>
    /// What makes two items "the same thing": a switch leaves an item alone when the mode being left and the mode being entered both
    /// have it. Executables and arguments ignore case; the address order matters.
    /// </summary>
    public static string ItemKey(ModeItem item)
    {
        switch (item.Kind)
        {
            case ModeItemKind.App:
                return !string.IsNullOrEmpty(item.Aumid)
                    ? "pkg|" + item.Aumid
                    : "app|" + (item.Exe ?? string.Empty).ToLowerInvariant() + "|" + (item.Args ?? string.Empty).Trim().ToLowerInvariant();
            case ModeItemKind.Web:
                return "web|" + (item.Browser ?? "default").ToLowerInvariant() + "|" + item.Profile + "|" + string.Join("\n", item.Urls);
            default:
                return "path|" + FullPath(item.Path ?? string.Empty).TrimEnd('\\').ToLowerInvariant();
        }
    }

    private static string FullPath(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>The items without repeats by <see cref="ItemKey"/>; the first of each key wins.</summary>
    public static List<ModeItem> Dedupe(IEnumerable<ModeItem> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. items.Where(i => seen.Add(ItemKey(i)))];
    }

    /// <summary>The items of the mode being left that the mode being entered does not also have (all of them when nothing is entered).</summary>
    public static IReadOnlyList<ModeItem> ToPutAway(Mode leaving, Mode? entering)
    {
        var kept = entering is null ? [] : entering.Items.Select(ItemKey).ToHashSet(StringComparer.Ordinal);
        return [.. leaving.Items.Where(i => !kept.Contains(ItemKey(i)))];
    }

    // ------------------------------------------------------------------------------------------ apps

    /// <summary>
    /// The arguments (and working directory) that make an app open a folder. Editors take the folder as the only argument, Windows
    /// Terminal takes <c>-d</c>, File Explorer takes it quoted; any other program keeps <paramref name="currentArgs"/> and starts in the folder.
    /// </summary>
    public static string ArgsForFolder(string exe, string folder, out string dir, string currentArgs = "")
    {
        var file = System.IO.Path.GetFileName(exe ?? string.Empty).ToLowerInvariant();
        if (file is "code.exe" or "code - insiders.exe" or "cursor.exe" or "windsurf.exe" or "explorer.exe")
        {
            dir = string.Empty;
            return "\"" + folder + "\"";
        }

        if (file is "wt.exe" or "windowsterminal.exe")
        {
            dir = folder;
            return "-d \"" + folder + "\"";
        }

        dir = folder;
        return currentArgs;
    }

    /// <summary>True for an editor whose launcher hands the request to the running copy (starting it again focuses or opens the folder).</summary>
    public static bool IsForwarder(string? exe) => Forwarders.Contains(System.IO.Path.GetFileName(exe ?? string.Empty), StringComparer.OrdinalIgnoreCase);

    /// <summary>True for a file type that runs code. Those are added as apps, never "opened" as files.</summary>
    public static bool IsBlockedFile(string? path) =>
        BlockedExtensions.Contains(System.IO.Path.GetExtension(path ?? string.Empty), StringComparer.OrdinalIgnoreCase);
}
