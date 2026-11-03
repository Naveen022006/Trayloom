// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.WindowTools;

/// <summary>Which shortcut opened the switcher.</summary>
internal enum SwitchMode
{
    Apps,
    FrontAppWindows,
    AllWindows,
}

/// <summary>Panel arrangement chosen on the Switcher page.</summary>
internal enum SwitchLayout
{
    Windows,
    Icons,
    Simple,
}

/// <summary>A top-level window listed by the switcher.</summary>
internal sealed class SwitchWindow
{
    public nint Hwnd { get; init; }

    public string Title { get; set; } = string.Empty;

    public bool Minimized { get; init; }

    public bool Fullscreen { get; init; }

    public bool OtherDesktop { get; init; }

    public nint Monitor { get; init; }

    public int Rank { get; init; }

    public int ZIndex { get; init; }

    public bool Closing { get; set; }
}

/// <summary>One entry of the switcher: a window, an app with its windows, or an app without windows.</summary>
internal sealed class SwitchEntry
{
    public string? ExePath { get; init; }

    public uint Pid { get; init; }

    public string AppName { get; init; } = string.Empty;

    public BitmapSource? Icon { get; set; }

    public List<SwitchWindow> Windows { get; init; } = [];

    public int WindowIndex { get; set; }

    public bool NoWindows => Windows.Count == 0;

    public SwitchWindow? Current => Windows.Count == 0 ? null : Windows[Math.Clamp(WindowIndex, 0, Windows.Count - 1)];

    public string Title => Current?.Title is { Length: > 0 } title ? title : AppName;

    /// <summary>App name under the title, only when it differs (case-insensitive) from the title.</summary>
    public string Subtitle => NoWindows
        ? Strings.NoOpenWindow
        : string.Equals(Title, AppName, StringComparison.OrdinalIgnoreCase) ? string.Empty : AppName;

    public bool AllMinimized => Windows.Count > 0 && Windows.All(w => w.Minimized);
}

/// <summary>Snapshot of the switcher options (Vorssaint <c>SwitcherSupport</c>).</summary>
internal sealed class SwitcherOptions
{
    public SwitchLayout Layout { get; init; }

    public bool MergeTabs { get; init; }

    public string Windowless { get; init; } = "finder";

    public string Minimized { get; init; } = "normal";

    public bool TreatHidden { get; init; } = true;

    public bool ShowFullscreen { get; init; } = true;

    public Dictionary<string, string> Rules { get; init; } = [];

    public bool CurrentDesktopOnly { get; init; }

    public bool CurrentDisplayOnly { get; init; }

    public string Placement { get; init; } = "pointer";

    public bool SearchPin { get; init; }

    public bool ShowHints { get; init; } = true;

    public int Delay { get; init; } = 100;

    public bool Instant { get; init; }

    public int MaxColumns { get; init; }

    public bool Minimal { get; init; }

    public double Scale { get; init; } = 1;

    public bool TakeOver { get; init; } = true;

    public List<string> Excluded { get; init; } = [];

    public static double ScaleOf(string? size) => size switch
    {
        "small" => 0.75,
        "large" => 1.4,
        "xlarge" => 1.8,
        _ => 1.0,
    };

    public static SwitcherOptions Read(IAppSettings app)
    {
        var layout = app.Get<bool>(Keys.SwitcherSimple) ? SwitchLayout.Simple : app.Get<bool>(Keys.SwitcherIconRow) ? SwitchLayout.Icons : SwitchLayout.Windows;
        var takeOver = app.Get<bool>(Keys.SwitcherTakeOver);
        return new SwitcherOptions
        {
            Layout = layout,
            MergeTabs = app.Get<bool>(Keys.SwitcherMergeTabs),
            Windowless = takeOver ? "all" : app.Get<string>(Keys.SwitcherWindowless) ?? "finder",
            Minimized = app.Get<string>(Keys.SwitcherMinimized) ?? "normal",
            TreatHidden = app.Get<bool>(Keys.SwitcherTreatHidden),
            ShowFullscreen = app.Get<bool>(Keys.SwitcherFullscreen),
            Rules = new Dictionary<string, string>(app.Get<Dictionary<string, string>>(Keys.SwitcherRules) ?? [], StringComparer.OrdinalIgnoreCase),
            CurrentDesktopOnly = app.Get<bool>(Keys.SwitcherCurrentDesktop),
            CurrentDisplayOnly = app.Get<bool>(Keys.SwitcherCurrentDisplay),
            Placement = app.Get<string>(Keys.SwitcherPlacement) ?? "pointer",
            SearchPin = app.Get<bool>(Keys.SwitcherSearchPin),
            ShowHints = app.Get<bool>(Keys.SwitcherHints),
            Delay = Math.Clamp(app.Get<int>(Keys.SwitcherDelay), 0, 500),
            Instant = app.Get<bool>(Keys.SwitcherInstant),
            MaxColumns = app.Get<int>(Keys.SwitcherMaxColumns),
            Minimal = app.Get<bool>(Keys.MinimalPreviews),
            Scale = ScaleOf(app.Get<string>(Keys.SwitcherPreviewSize)),
            TakeOver = takeOver,
            Excluded = [.. app.Get<List<string>>(Keys.SwitcherExcluded) ?? []],
        };
    }
}

/// <summary>Builds the entry list of a switcher session (spec 05 A1.2).</summary>
internal static class SwitcherModel
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "ForegroundStaging", "TaskListThumbnailWnd", "Windows.UI.Core.CoreWindow",
        "EdgeUiInputTopWndClass", "XamlWindow", "TaskSwitcherWnd", "TaskSwitcherOverlayWnd",
    };

    private static IVirtualDesktopManager? _vdm;
    private static bool _vdmFailed;

    private static IVirtualDesktopManager? Vdm()
    {
        if (_vdm is not null || _vdmFailed)
        {
            return _vdm;
        }

        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
            _vdm = type is null ? null : Activator.CreateInstance(type) as IVirtualDesktopManager;
        }
        catch (Exception)
        {
            _vdm = null;
        }

        _vdmFailed = _vdm is null;
        return _vdm;
    }

    /// <summary>True when the window is on the virtual desktop currently shown (true when unknown).</summary>
    public static bool OnCurrentDesktop(nint hwnd)
    {
        try
        {
            var vdm = Vdm();
            return vdm is null || vdm.IsWindowOnCurrentVirtualDesktop(hwnd, out var on) != 0 || on;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Qualifying top-level windows (visible or cloaked by another desktop), MRU ordered.</summary>
    public static List<SwitchWindow> Windows(bool includeOtherDesktops)
    {
        var list = new List<(nint Hwnd, int Z)>();
        var z = 0;
        WinApi.EnumWindows((hwnd, _) =>
        {
            list.Add((hwnd, z++));
            return true;
        }, 0);

        var result = new List<SwitchWindow>();
        foreach (var (hwnd, zIndex) in list)
        {
            if (Qualifies(hwnd, includeOtherDesktops, out var otherDesktop) is false)
            {
                continue;
            }

            var minimized = WinApi.IsIconic(hwnd);
            var monitor = NativeMethods.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            result.Add(new SwitchWindow
            {
                Hwnd = hwnd,
                Title = WinApi.GetTitle(hwnd),
                Minimized = minimized,
                Fullscreen = !minimized && IsFullscreen(hwnd, monitor),
                OtherDesktop = otherDesktop,
                Monitor = monitor,
                Rank = WindowUseTracker.Rank(hwnd),
                ZIndex = zIndex,
            });
        }

        return [.. result.OrderBy(w => w.Rank).ThenBy(w => w.ZIndex)];
    }

    private static bool? Qualifies(nint hwnd, bool includeOtherDesktops, out bool otherDesktop)
    {
        otherDesktop = false;
        if (hwnd == WinApi.GetShellWindow())
        {
            return false;
        }

        var cloaked = 0;
        WinApi.DwmGetWindowAttribute(hwnd, WinApi.DWMWA_CLOAKED, out cloaked, sizeof(int));
        var visible = WinApi.IsWindowVisible(hwnd);
        if (!visible)
        {
            return false;
        }

        if (cloaked != 0)
        {
            // Cloaked by the shell = on another virtual desktop; cloaked by an app (suspended UWP) = not a real window.
            if ((cloaked & 2) == 0 || !includeOtherDesktops || OnCurrentDesktop(hwnd))
            {
                return false;
            }

            otherDesktop = true;
        }

        var ex = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((ex & WinApi.WS_EX_APPWINDOW) == 0)
        {
            if ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0 || WinApi.GetWindow(hwnd, WinApi.GW_OWNER) != 0)
            {
                return false;
            }
        }

        if (IgnoredClasses.Contains(WinApi.GetClass(hwnd)) || WinApi.GetTitle(hwnd).Length == 0)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId)
        {
            return false;
        }

        if (!WinApi.IsIconic(hwnd))
        {
            NativeMethods.GetWindowRect(hwnd, out var rect);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFullscreen(nint hwnd, nint monitor)
    {
        var style = NativeMethods.GetWindowLongPtr(hwnd, WinApi.GWL_STYLE).ToInt64();
        if ((style & WinApi.WS_CAPTION) == WinApi.WS_CAPTION)
        {
            return false;
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect.Left <= info.rcMonitor.Left && rect.Top <= info.rcMonitor.Top && rect.Right >= info.rcMonitor.Right && rect.Bottom >= info.rcMonitor.Bottom;
    }

    /// <summary>The foreground app's executable path (null when unknown).</summary>
    public static string? ForegroundPath()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        return hwnd == 0 ? null : AppIdentity.OwnerOf(hwnd).Path;
    }

    /// <summary>Builds the entries for a session; <paramref name="initial"/> is the entry to highlight first.</summary>
    public static List<SwitchEntry> Build(SwitcherOptions options, SwitchMode mode, out int initial)
    {
        NativeMethods.GetCursorPos(out var cursor);
        var cursorMonitor = NativeMethods.MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        var windows = Windows(includeOtherDesktops: !options.CurrentDesktopOnly);

        // per-window filters
        var owners = new Dictionary<nint, WindowOwner>();
        WindowOwner OwnerOf(SwitchWindow w) => owners.TryGetValue(w.Hwnd, out var o) ? o : owners[w.Hwnd] = AppIdentity.OwnerOf(w.Hwnd);
        windows = [.. windows.Where(w =>
        {
            var owner = OwnerOf(w);
            if (options.CurrentDisplayOnly && w.Monitor != cursorMonitor)
            {
                return false;
            }

            if (!options.ShowFullscreen && w.Fullscreen)
            {
                return false;
            }

            if (AppIdentity.RuleFor(options.Rules, owner.Path) == "hidden")
            {
                return false;
            }

            return true;
        })];

        // minimized placement (hidden apps are apps whose windows are all minimized)
        if (options.Minimized != "normal")
        {
            var minimizedApps = options.TreatHidden
                ? windows.GroupBy(w => OwnerOf(w).Path).Where(g => g.All(w => w.Minimized)).Select(g => g.Key).ToHashSet()
                : [];
            bool IsMin(SwitchWindow w) => w.Minimized || minimizedApps.Contains(OwnerOf(w).Path);
            windows = options.Minimized == "hidden"
                ? [.. windows.Where(w => !IsMin(w))]
                : [.. windows.Where(w => !IsMin(w)), .. windows.Where(IsMin)];
        }

        var foregroundPath = ForegroundPath();
        var entries = new List<SwitchEntry>();
        var perApp = mode != SwitchMode.AllWindows && options.Layout != SwitchLayout.Windows;
        if (mode == SwitchMode.FrontAppWindows && options.Layout == SwitchLayout.Windows)
        {
            windows = [.. windows.Where(w => string.Equals(OwnerOf(w).Path, foregroundPath, StringComparison.OrdinalIgnoreCase))];
        }

        if (perApp || (options.MergeTabs && mode != SwitchMode.AllWindows))
        {
            foreach (var group in windows.GroupBy(w => OwnerOf(w).Path ?? ("pid" + OwnerOf(w).Pid)))
            {
                var first = OwnerOf(group.First());
                entries.Add(new SwitchEntry
                {
                    ExePath = first.Path,
                    Pid = first.Pid,
                    AppName = AppIdentity.AppName(first.Path),
                    Windows = [.. group],
                });
            }
        }
        else
        {
            foreach (var w in windows)
            {
                var owner = OwnerOf(w);
                entries.Add(new SwitchEntry { ExePath = owner.Path, Pid = owner.Pid, AppName = AppIdentity.AppName(owner.Path), Windows = [w] });
            }
        }

        AddWindowless(options, entries, windows, owners);

        foreach (var entry in entries)
        {
            entry.Icon = entry.Current is { } c ? AppIdentity.IconOfWindow(c.Hwnd, entry.ExePath) : AppIdentity.IconOf(entry.ExePath);
        }

        initial = 0;
        if (entries.Count > 1)
        {
            initial = 1;
            if (mode == SwitchMode.FrontAppWindows && perApp)
            {
                // The windows shortcut stays on the front app and walks its windows.
                var index = entries.FindIndex(e => string.Equals(e.ExePath, foregroundPath, StringComparison.OrdinalIgnoreCase));
                initial = Math.Max(0, index);
                if (index >= 0 && entries[index].Windows.Count > 1)
                {
                    entries[index].WindowIndex = 1;
                }
            }
        }

        return entries;
    }

    private static void AddWindowless(SwitcherOptions options, List<SwitchEntry> entries, List<SwitchWindow> windows, Dictionary<nint, WindowOwner> owners)
    {
        var present = entries.Where(e => e.ExePath is not null).Select(e => e.ExePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe").ToLowerInvariant();
        var candidates = new List<(string Path, uint Pid)>();

        if (options.Windowless == "finder" && !present.Contains(explorerPath) && AppIdentity.AppName(explorerPath).Length > 0)
        {
            candidates.Add((explorerPath, 0));
        }

        if (options.Windowless == "all" || options.Rules.Count > 0)
        {
            foreach (var (path, pid) in TrayApps.RunningWithoutWindows(present))
            {
                var rule = AppIdentity.RuleFor(options.Rules, path);
                if (rule == "hidden" || rule == "windowsOnly")
                {
                    continue;
                }

                if (options.Windowless == "all" || rule == "showWithoutWindows")
                {
                    candidates.Add((path, pid));
                }
            }

            if (!present.Contains(explorerPath) && options.Windowless == "all" && candidates.All(c => c.Path != explorerPath))
            {
                candidates.Add((explorerPath, 0));
            }
        }

        foreach (var (path, pid) in candidates.DistinctBy(c => c.Path))
        {
            if (AppIdentity.RuleFor(options.Rules, path) is "hidden" or "windowsOnly")
            {
                continue;
            }

            entries.Add(new SwitchEntry { ExePath = path, Pid = pid, AppName = AppIdentity.AppName(path) });
        }
    }
}
