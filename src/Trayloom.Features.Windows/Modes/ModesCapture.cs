// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Features.WindowTools.AwayMode;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>One thing found by "Save what's open now": the item to save, whether it starts ticked, a short detail and the window count.</summary>
internal sealed record CapturedItem(ModeItem Item, bool Selected, string Detail, int Windows);

/// <summary>What is open now, in memory only. Nothing is saved until the person confirms in the capture dialog.</summary>
internal sealed record ModeCapture(IReadOnlyList<CapturedItem> Items, IReadOnlyList<string> Warnings);

/// <summary>
/// "Save what's open now" (spec 08 section 2.5): groups the open windows by app, turns File Explorer windows into folders, and reads the
/// tabs of Chrome and Edge from their session files. It runs once, when the person asks, and keeps nothing: the result is a value the
/// dialog shows, and window handles, titles and tab titles are never part of it.
/// </summary>
internal static class ModesCapture
{
    /// <summary>Blocking; call on a thread in a single-threaded apartment (File Explorer is read through shell COM).</summary>
    public static ModeCapture Run(IAppSettings app, ILog log, Dispatcher ui)
    {
        WindowTools.Keys.Register(app);
        var engine = new WindowLayoutEngine(app, log, ui);
        var found = new List<CapturedItem>();
        var warnings = new List<string>();

        var browsers = new Dictionary<(string Exe, string Udd), string>();
        var warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in GroupByOwner())
        {
            var path = group.Path;
            var command = AwayProcess.CommandLine(group.Pid) ?? string.Empty;
            if (AwayApps.IsHelperCommand(command))
            {
                continue;
            }

            var browser = AwayBrowsers.KindOf(path);
            if (browser == AwayAppKind.Browser)
            {
                browsers.TryAdd((path, AwayBrowsers.ResolveUserDataDir(path, AwayBrowsers.UserDataDirOf(command))), command);
                continue;
            }

            if (browser == AwayAppKind.BrowserMin && warned.Add(path))
            {
                warnings.Add(ModesStrings.CaptureTabsUnsupported(AppIdentity.DisplayName(path)));
            }

            found.Add(AppItem(group, command, engine));
        }

        foreach (var ((exe, udd), command) in browsers)
        {
            found.AddRange(BrowserItems(exe, udd, command, warnings));
        }

        foreach (var (hwnd, folder) in ModesWindows.ExplorerFolders())
        {
            var zone = ModesWindows.DetectZone(engine, hwnd);
            var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
            found.Add(new CapturedItem(
                new ModeItem { Kind = ModeItemKind.Path, Name = Clip(name.Length > 0 ? name : folder), Path = folder, Zone = zone },
                true,
                ZoneTitle(zone) ?? string.Empty,
                1));
        }

        // The same thing found twice (two windows of one program) is one item; a mode holds at most 20.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = found.Where(c => seen.Add(ModesMath.ItemKey(c.Item))).Take(ModesMath.MaxItems).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Item.Id = "i" + (i + 1);
        }

        return new ModeCapture(items, warnings);
    }

    private sealed record OwnerGroup(uint Pid, string Path, List<nint> Windows);

    /// <summary>The switchable windows grouped by the process that owns them, without the shell and Trayloom, topmost group first.</summary>
    private static List<OwnerGroup> GroupByOwner()
    {
        var groups = new List<OwnerGroup>();
        foreach (var hwnd in ModesWindows.Switchable())
        {
            var owner = AppIdentity.OwnerOf(hwnd);
            if (owner.Path is null || ModesWindows.IsShellFile(owner.Path))
            {
                continue;
            }

            var group = groups.FirstOrDefault(g => g.Pid == owner.Pid);
            if (group is null)
            {
                groups.Add(group = new OwnerGroup(owner.Pid, owner.Path, []));
            }

            group.Windows.Add(hwnd);
        }

        return groups;
    }

    private static CapturedItem AppItem(OwnerGroup group, string command, WindowLayoutEngine engine)
    {
        var aumid = AwayProcess.Aumid(group.Pid);
        var zone = ModesWindows.DetectZone(engine, group.Windows[0]);
        var item = new ModeItem
        {
            Kind = ModeItemKind.App,
            Name = Clip(AppIdentity.AppName(group.Path)),
            Exe = aumid is null ? group.Path : null,
            Aumid = aumid,
            Args = aumid is null ? AwayProcess.ArgumentsOf(command) : string.Empty,
            Zone = zone,
        };
        var detail = group.Windows.Count > 1 ? ModesStrings.WindowCount(group.Windows.Count) : ZoneTitle(zone) ?? string.Empty;
        return new CapturedItem(item, true, detail, group.Windows.Count);
    }

    /// <summary>
    /// The windows of one running Chrome or Edge as web items, one per window (more than 50 tabs split into several). When the profile
    /// folder is not the default one, or the session file cannot be read, the browser becomes an app item and a warning says so.
    /// </summary>
    private static IEnumerable<CapturedItem> BrowserItems(string exe, string udd, string command, List<string> warnings)
    {
        var name = AppIdentity.DisplayName(exe);
        var custom = !string.Equals(
            Path.GetFullPath(udd).TrimEnd('\\'),
            Path.GetFullPath(ModesBrowsers.DefaultUserDataDir(exe) ?? udd).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
        var asApp = new CapturedItem(
            new ModeItem { Kind = ModeItemKind.App, Name = Clip(name), Exe = exe, Args = AwayProcess.ArgumentsOf(command) },
            true,
            string.Empty,
            1);
        if (custom)
        {
            return [asApp];
        }

        var items = new List<CapturedItem>();
        var number = 0;
        foreach (var profile in ModesBrowsers.ActiveProfiles(udd, command))
        {
            var windows = ModesBrowsers.ReadSession(udd, profile);
            if (windows is null)
            {
                warnings.Add(ModesStrings.CaptureTabsFailed(name));
                return [asApp];
            }

            foreach (var window in windows)
            {
                foreach (var urls in ModesMath.GroupUrls(window.Urls))
                {
                    number++;
                    var item = new ModeItem { Kind = ModeItemKind.Web, Name = Clip(ModesStrings.BrowserWindow(name, number)), Browser = exe, Profile = profile, Urls = urls };
                    items.Add(new CapturedItem(item, true, ModesStrings.TabCount(urls.Count), 1));
                }
            }
        }

        return items;
    }

    private static string? ZoneTitle(string? zone) => LayoutActions.Get(zone)?.Title;

    private static string Clip(string name) => name.Length <= ModesMath.MaxNameLength ? name : name[..ModesMath.MaxNameLength].TrimEnd();
}
