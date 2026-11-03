// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Text;
using Trayloom.Core;

namespace Trayloom.Features.Tools;

/// <summary>One listed app (a process owning at least one real top-level window).</summary>
internal sealed record QuitApp(int Pid, string Name, string Path, IReadOnlyList<nint> Windows, bool IsFront, bool IsKept);

/// <summary>Listing, targeting and soft-quit rules of the Quit apps window (spec 2.3.3 / 2.3.5).</summary>
internal static class QuitAppsModel
{
    /// <summary>Settings key of the kept-apps list.</summary>
    public const string KeptKey = "quitAppsKeptApps";

    /// <summary>File Explorer owns the desktop and taskbar: never listed, never quit.</summary>
    public static readonly string ExplorerKey = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe").ToLowerInvariant();

    /// <summary>The locked pair (File Explorer + Phone Link).</summary>
    public static readonly string[] Mandatory = [ExplorerKey, "phoneexperiencehost.exe"];

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stored kept keys + the mandatory pair, de-duplicated (lower case).</summary>
    public static List<string> Kept(IAppSettings app)
    {
        var list = new List<string>(Mandatory);
        foreach (var k in app.Get<List<string>>(KeptKey, []) ?? [])
        {
            var key = k.Trim().ToLowerInvariant();
            if (key.Length > 0 && !list.Contains(key))
            {
                list.Add(key);
            }
        }

        return list;
    }

    /// <summary>True when a kept key (full path or bare exe name) denotes the exe at <paramref name="path"/>.</summary>
    public static bool KeyMatches(string key, string path)
    {
        var p = path.ToLowerInvariant();
        var file = System.IO.Path.GetFileName(p);
        return key == p || key == file || (key == "phoneexperiencehost.exe" && file == "yourphone.exe");
    }

    /// <summary>Display name: file description, product name or exe name.</summary>
    public static string NameOf(string path)
    {
        lock (Names)
        {
            if (Names.TryGetValue(path, out var cached))
            {
                return cached;
            }
        }

        string name;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription!.Trim()
                : !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName!.Trim()
                : System.IO.Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            name = System.IO.Path.GetFileNameWithoutExtension(path);
        }

        lock (Names)
        {
            Names[path] = name;
        }

        return name;
    }

    /// <summary>Process id that really owns a window (UWP frames hosted by ApplicationFrameHost resolve to the CoreWindow's process).</summary>
    public static int OwnerPid(nint hwnd)
    {
        QuitNative.GetWindowThreadProcessId(hwnd, out var pid);
        var path = N.ProcessPath((int)pid);
        if (path is not null && path.EndsWith("applicationframehost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var real = 0;
            QuitNative.EnumChildWindows(hwnd, (child, _) =>
            {
                if (QuitNative.ClassOf(child) == "Windows.UI.Core.CoreWindow")
                {
                    QuitNative.GetWindowThreadProcessId(child, out var cp);
                    if (cp != pid)
                    {
                        real = (int)cp;
                        return false;
                    }
                }

                return true;
            }, 0);
            if (real != 0)
            {
                return real;
            }
        }

        return (int)pid;
    }

    /// <summary>Lists the open apps: front first, then by name.</summary>
    public static List<QuitApp> Load(int frontPid, IReadOnlyCollection<string> kept)
    {
        var byPid = new Dictionary<int, List<nint>>();
        var self = Environment.ProcessId;
        QuitNative.EnumWindows((hwnd, _) =>
        {
            if (QuitNative.IsAppWindow(hwnd))
            {
                var pid = OwnerPid(hwnd);
                if (pid != 0 && pid != self)
                {
                    if (!byPid.TryGetValue(pid, out var list))
                    {
                        byPid[pid] = list = [];
                    }

                    list.Add(hwnd);
                }
            }

            return true;
        }, 0);

        var apps = new List<QuitApp>();
        foreach (var (pid, windows) in byPid)
        {
            var path = N.ProcessPath(pid);
            if (path is null || path.Equals(ExplorerKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            apps.Add(new QuitApp(pid, NameOf(path), path, windows, pid == frontPid, kept.Any(k => KeyMatches(k, path))));
        }

        return [.. apps.OrderByDescending(a => a.IsFront).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Process id of the foreground window (0 when it is Trayloom itself or unknown).</summary>
    public static int ForegroundPid()
    {
        var hwnd = Plat.Foreground();
        if (hwnd == 0)
        {
            return 0;
        }

        var pid = OwnerPid(hwnd);
        return pid == Environment.ProcessId ? 0 : pid;
    }

    /// <summary>Soft quit: WM_CLOSE to every top-level window of the app. True when at least one message was delivered.</summary>
    public static bool SoftQuit(QuitApp app)
    {
        var targets = new HashSet<nint>(app.Windows);
        QuitNative.EnumWindows((hwnd, _) =>
        {
            QuitNative.GetWindowThreadProcessId(hwnd, out var pid);
            if ((int)pid == app.Pid && QuitNative.IsWindowVisible(hwnd))
            {
                targets.Add(hwnd);
            }

            return true;
        }, 0);
        var delivered = false;
        foreach (var hwnd in targets)
        {
            delivered |= QuitNative.PostMessageW(hwnd, QuitNative.WM_CLOSE, 0, 0);
        }

        return delivered;
    }

    /// <summary>Batch targets: not kept, not the front app when <paramref name="keepingPid"/> is set.</summary>
    public static IEnumerable<QuitApp> Targets(IEnumerable<QuitApp> apps, int keepingPid = 0) => apps.Where(a => !a.IsKept && a.Pid != keepingPid);
}
