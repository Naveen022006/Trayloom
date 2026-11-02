// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;

namespace WinKnife.Features.Tools;

/// <summary>An installed app (Start menu / shell:AppsFolder).</summary>
internal sealed record BarApp(string Name, string Launch, string StableKey, string Keywords);

/// <summary>An open top-level window.</summary>
internal sealed record BarWindowInfo(nint Handle, string Title, string AppName, string? ExePath);

/// <summary>A menu command of the foreground app.</summary>
internal sealed record BarMenuItem(string Title, string[] Path, string? Shortcut, nint Window, int CommandId);

/// <summary>A clipboard history item read from the Clipboard feature's index.</summary>
internal sealed record BarClipItem(string Id, string Kind, string Text, List<string> Files, string? ImagePath, DateTime CopiedAt, bool Pinned);

/// <summary>A snippet as stored by the text snippets feature.</summary>
internal sealed class BarSnippet
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("trigger")] public string Trigger { get; set; } = "";

    [JsonPropertyName("replacement")] public string Replacement { get; set; } = "";

    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    [JsonPropertyName("folder")] public string Folder { get; set; } = "";
}

/// <summary>Data providers of the Command Bar: apps, windows, menus, selection, clipboard, snippets, typing.</summary>
internal static class BarProviders
{
    // ------------------------------------------------------------------ apps

    private static IReadOnlyList<BarApp> _apps = [];
    private static int _scanning;

    public static IReadOnlyList<BarApp> Apps => _apps;

    /// <summary>Raised (background thread) when a scan found a different app list.</summary>
    public static event Action? AppsChanged;

    /// <summary>Rescans installed apps in the background (every open); <paramref name="done"/> runs when something changed.</summary>
    public static void RescanApps(Action? done = null)
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                List<BarApp> list;
                try
                {
                    list = ScanAppsFolder();
                }
                catch (Exception ex)
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "wk-bar-apps.log"), ex + Environment.NewLine);
                    list = [];
                }

                if (list.Count == 0)
                {
                    list = ScanStartMenu();
                }

                var changed = list.Count != _apps.Count || !list.Select(a => a.StableKey).SequenceEqual(_apps.Select(a => a.StableKey));
                if (changed)
                {
                    _apps = list;
                    AppsChanged?.Invoke();
                    done?.Invoke();
                }
            }
            catch
            {
                // keep the previous list
            }
            finally
            {
                Volatile.Write(ref _scanning, 0);
            }
        })
        { IsBackground = true, Name = "BarAppScan" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static List<BarApp> ScanAppsFolder()
    {
        var found = new Dictionary<string, BarApp>(StringComparer.OrdinalIgnoreCase);
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null)
        {
            return [];
        }

        dynamic shell = Activator.CreateInstance(type)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        if (folder is null)
        {
            return [];
        }

        foreach (var item in folder.Items())
        {
            try
            {
                string name = item.Name;
                string path = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var key = path.Contains('!') || !path.Contains('\\') ? "app.aumid." + path : "app.path." + path.ToLowerInvariant();
                found.TryAdd(key, new BarApp(name, "shell:AppsFolder\\" + path, key, ""));
            }
            catch
            {
                // one bad item
            }
        }

        return [.. found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static List<BarApp> ScanStartMenu()
    {
        var found = new Dictionary<string, BarApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
        })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    found.TryAdd("app.path." + name.ToLowerInvariant(), new BarApp(name, file, "app.path." + name.ToLowerInvariant(), ""));
                }
            }
        }

        return [.. found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Lower-cased names of processes that own a visible window (for the green dot).</summary>
    public static HashSet<string> RunningNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle != 0)
                    {
                        set.Add(BarSearch.Normalize(p.ProcessName));
                    }
                }
                catch
                {
                    // access denied
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // ignored
        }

        return set;
    }

    public static bool Launch(BarApp app) => Plat.Open(app.Launch);

    // ------------------------------------------------------------------ windows

    private static readonly ConcurrentDictionary<int, string> AppNameCache = new();
    private static IReadOnlyList<BarWindowInfo> _windows = [];
    private static DateTime _windowsAt = DateTime.MinValue;

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern nint GetMenu(nint hwnd);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMenuItemInfoW(nint menu, uint item, bool byPosition, ref MENUITEMINFO info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MENUITEMINFO
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public nint hSubMenu;
        public nint hbmpChecked;
        public nint hbmpUnchecked;
        public nint dwItemData;
        public nint dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

    /// <summary>Open windows (cached for 4 seconds).</summary>
    public static IReadOnlyList<BarWindowInfo> Windows(bool force = false)
    {
        if (!force && DateTime.UtcNow - _windowsAt < TimeSpan.FromSeconds(4))
        {
            return _windows;
        }

        var self = Environment.ProcessId;
        var list = new List<BarWindowInfo>();
        N.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != 0)
                {
                    return true;
                }

                var ex = GetWindowLongW(hwnd, -20);
                if ((ex & 0x80) != 0 && (ex & 0x40000) == 0)
                {
                    return true;
                }

                if (N.DwmGetWindowAttribute(hwnd, N.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                {
                    return true;
                }

                var title = N.WindowText(hwnd).Trim();
                if (title.Length == 0)
                {
                    return true;
                }

                var cls = N.ClassName(hwnd);
                if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                {
                    return true;
                }

                var pid = Plat.ProcessOf(hwnd);
                if (pid == self)
                {
                    return true;
                }

                var exe = N.ProcessPath(pid);
                var app = AppNameCache.GetOrAdd(pid, _ => AppNameOf(exe));
                if (string.Equals(title, app, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                list.Add(new BarWindowInfo(hwnd, title, app, exe));
            }
            catch
            {
                // skip
            }

            return true;
        }, 0);
        _windows = list;
        _windowsAt = DateTime.UtcNow;
        return list;
    }

    private static string AppNameOf(string? exe)
    {
        if (exe is null)
        {
            return "Window";
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            var name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : info.ProductName;
            return string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(exe) : name.Trim();
        }
        catch
        {
            return Path.GetFileNameWithoutExtension(exe);
        }
    }

    // ------------------------------------------------------------------ menus

    /// <summary>Reads the Win32 menu bar of a window (depth 4, 400 items); empty for menu-less apps.</summary>
    public static List<BarMenuItem> ReadMenus(nint hwnd, string[]? skipTop = null)
    {
        var result = new List<BarMenuItem>();
        try
        {
            var root = GetAncestor(hwnd, 2);
            if (root == 0)
            {
                root = hwnd;
            }

            var menu = GetMenu(root);
            if (menu == 0)
            {
                return result;
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(350);
            Walk(menu, root, [], 0, result, deadline, skipTop ?? ["Window", "Help", "?"]);
        }
        catch
        {
            // ignored
        }

        return result;
    }

    private static void Walk(nint menu, nint window, string[] path, int depth, List<BarMenuItem> into, DateTime deadline, string[] skipTop)
    {
        if (depth >= 4 || into.Count >= 400 || DateTime.UtcNow > deadline)
        {
            return;
        }

        var count = GetMenuItemCount(menu);
        for (var i = 0; i < count && into.Count < 400; i++)
        {
            var buffer = Marshal.AllocHGlobal(512 * 2);
            try
            {
                var info = new MENUITEMINFO { cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(), fMask = 0x1 | 0x2 | 0x4 | 0x10 | 0x40, dwTypeData = buffer, cch = 511 };
                if (!GetMenuItemInfoW(menu, (uint)i, true, ref info))
                {
                    continue;
                }

                if ((info.fType & 0x800) != 0 || (info.fState & 0x3) != 0)
                {
                    continue;
                }

                var text = Marshal.PtrToStringUni(buffer, (int)Math.Min(info.cch, 511)) ?? "";
                var tab = text.IndexOf('\t');
                var shortcut = tab >= 0 ? text[(tab + 1)..] : null;
                var title = (tab >= 0 ? text[..tab] : text).Replace("&", "").Trim();
                if (title.Length == 0)
                {
                    continue;
                }

                if (depth == 0 && skipTop.Contains(title, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (info.hSubMenu != 0)
                {
                    Walk(info.hSubMenu, window, [.. path, title], depth + 1, into, deadline, skipTop);
                }
                else if (path.Length > 0)
                {
                    into.Add(new BarMenuItem(title, path, shortcut, window, (int)info.wID));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public static bool PressMenu(BarMenuItem item) => PostMessageW(item.Window, 0x0111, (nint)item.CommandId, 0);

    // ------------------------------------------------------------------ selection (UI Automation)

    /// <summary>The selected text of the focused element (null when none or too long) and whether typing would replace it.</summary>
    public static (string Text, bool TypesOver)? ReadSelection(int timeoutMs = 300)
    {
        (string, bool)? result = null;
        var task = Task.Run(() =>
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                if (focused is null || focused.Current.ProcessId == Environment.ProcessId)
                {
                    return;
                }

                if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) && pattern is TextPattern text)
                {
                    var ranges = text.GetSelection();
                    if (ranges.Length == 0)
                    {
                        return;
                    }

                    var sb = new StringBuilder();
                    foreach (var range in ranges)
                    {
                        sb.Append(range.GetText(20001));
                        if (sb.Length > 20000)
                        {
                            return;
                        }
                    }

                    var trimmed = sb.ToString().Trim();
                    if (trimmed.Length == 0)
                    {
                        return;
                    }

                    var readOnly = focused.Current.ControlType == ControlType.Document;
                    if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) && vp is ValuePattern value && value.Current.IsReadOnly)
                    {
                        readOnly = true;
                    }

                    result = (trimmed, !readOnly);
                }
            }
            catch
            {
                // no selection access
            }
        });
        return task.Wait(timeoutMs) ? result : null;
    }

    /// <summary>True when the focused element is a password field.</summary>
    public static bool FocusIsPassword()
    {
        try
        {
            return AutomationElement.FocusedElement?.Current.IsPassword == true;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ clipboard history / snippets

    private static List<BarClipItem> _clips = [];
    private static DateTime _clipsStamp = DateTime.MinValue;

    private sealed class ClipDto
    {
        public string Id { get; set; } = "";

        public string Kind { get; set; } = "text";

        public string Text { get; set; } = "";

        public List<string> FilePaths { get; set; } = [];

        public string? ImageFile { get; set; }

        public DateTime CopiedAt { get; set; }

        public DateTime? PinnedAt { get; set; }
    }

    private static string ClipboardRoot()
    {
        var data = Environment.GetEnvironmentVariable("WINKNIFE_DATA");
        return string.IsNullOrWhiteSpace(data)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinKnife")
            : Path.Combine(data, "local");
    }

    /// <summary>Clipboard history entries (read from the Clipboard feature's index file), newest first.</summary>
    public static IReadOnlyList<BarClipItem> Clips()
    {
        try
        {
            var path = Path.Combine(ClipboardRoot(), "clipboard.json");
            if (!File.Exists(path))
            {
                return [];
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (stamp != _clipsStamp)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var dtos = JsonSerializer.Deserialize<List<ClipDto>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                var images = Path.Combine(ClipboardRoot(), "Clipboard");
                _clips =
                [
                    .. dtos.OrderByDescending(d => d.CopiedAt).Select(d => new BarClipItem(d.Id, d.Kind, d.Text, d.FilePaths, d.ImageFile is null ? null : Path.Combine(images, d.ImageFile), d.CopiedAt, d.PinnedAt is not null)),
                ];
                _clipsStamp = stamp;
            }
        }
        catch
        {
            // keep the previous list
        }

        return _clips;
    }

    public static List<BarSnippet> Snippets(IAppSettings app)
    {
        try
        {
            return [.. (app.Get<List<BarSnippet>?>("textSnippets") ?? []).Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Name))];
        }
        catch
        {
            return [];
        }
    }

    // ------------------------------------------------------------------ typing at the caret / paste

    /// <summary>Restores the previous window, waits for the chord keys to be released, then runs <paramref name="send"/>.</summary>
    public static void TypeAtCaret(Dispatcher ui, nint target, Action send)
    {
        if (target != 0)
        {
            Plat.Activate(target);
        }

        Plat.AfterModifiersReleased(ui, () =>
        {
            if (BarProviders.FocusIsPassword())
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            send();
        });
    }

    public static void PasteClip(Dispatcher ui, nint target, BarClipItem clip)
    {
        try
        {
            if (clip.Kind == "files" && clip.Files.Count > 0)
            {
                var list = new System.Collections.Specialized.StringCollection();
                list.AddRange([.. clip.Files]);
                Clipboard.SetFileDropList(list);
            }
            else if (clip.Kind == "image" && clip.ImagePath is not null && File.Exists(clip.ImagePath))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(clip.ImagePath);
                image.EndInit();
                image.Freeze();
                Clipboard.SetImage(image);
            }
            else
            {
                Clipboard.SetText(clip.Text);
            }
        }
        catch
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        Plat.After(ui, 150, () => TypeAtCaret(ui, target, () => InputSender.SendChord(0x11, 0x56)));
    }
}
