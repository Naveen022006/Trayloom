// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WinKnife.Core.Native;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.WindowTools;

/// <summary>A top-level window that would appear in Alt+Tab.</summary>
internal sealed class WindowInfo
{
    public nint Handle { get; init; }

    public string Title { get; init; } = string.Empty;

    public string ProcessName { get; init; } = string.Empty;

    public bool Minimized { get; init; }

    public BitmapSource? Icon { get; init; }

    public nint Monitor { get; init; }
}

/// <summary>Enumerates the windows Alt+Tab would show (visible, not cloaked, not tool / owned windows).</summary>
internal static class WindowEnumerator
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "TaskListThumbnailWnd",
        "Windows.UI.Core.CoreWindow",
        "EdgeUiInputTopWndClass",
    };

    private static readonly Dictionary<string, BitmapSource?> ExeIconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the windows in Z order (the topmost, most recently used first).</summary>
    public static List<WindowInfo> Enumerate(bool includeMinimized, nint excludeHandle = 0)
    {
        var handles = new List<nint>();
        WinApi.EnumWindows((hwnd, _) =>
        {
            if (hwnd != excludeHandle && IsSwitchable(hwnd))
            {
                handles.Add(hwnd);
            }

            return true;
        }, 0);

        var result = new List<WindowInfo>(handles.Count);
        foreach (var hwnd in handles)
        {
            var minimized = WinApi.IsIconic(hwnd);
            if (minimized && !includeMinimized)
            {
                continue;
            }

            var info = Describe(hwnd, minimized);
            if (info is not null)
            {
                result.Add(info);
            }
        }

        return result;
    }

    /// <summary>The Alt+Tab rules: visible, uncloaked, titled, no tool windows, no owned popups.</summary>
    public static bool IsSwitchable(nint hwnd)
    {
        if (!WinApi.IsWindowVisible(hwnd))
        {
            return false;
        }

        if (WinApi.DwmGetWindowAttribute(hwnd, WinApi.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            // Other virtual desktop, suspended UWP app, etc.
            return false;
        }

        var ex = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        var appWindow = (ex & WinApi.WS_EX_APPWINDOW) != 0;
        if (!appWindow)
        {
            if ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0)
            {
                return false;
            }

            if (WinApi.GetWindow(hwnd, WinApi.GW_OWNER) != 0)
            {
                return false;
            }
        }

        if (hwnd == WinApi.GetShellWindow())
        {
            return false;
        }

        var title = WinApi.GetTitle(hwnd);
        if (title.Length == 0)
        {
            return false;
        }

        var className = WinApi.GetClass(hwnd);
        if (IgnoredClasses.Contains(className))
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

    private static WindowInfo? Describe(nint hwnd, bool minimized)
    {
        var title = WinApi.GetTitle(hwnd);
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);

        // UWP apps are hosted by ApplicationFrameHost; the real process owns a child CoreWindow.
        var realPid = pid;
        var path = pid == 0 ? null : WinApi.GetProcessPath(pid);
        if (path is not null && path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var childPid = FindHostedProcess(hwnd, pid);
            if (childPid != 0)
            {
                realPid = childPid;
                path = WinApi.GetProcessPath(childPid) ?? path;
            }
        }

        var processName = path is null ? string.Empty : System.IO.Path.GetFileNameWithoutExtension(path);
        return new WindowInfo
        {
            Handle = hwnd,
            Title = title,
            ProcessName = processName,
            Minimized = minimized,
            Icon = GetIcon(hwnd, path),
            Monitor = NativeMethods.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST),
        };
    }

    private static uint FindHostedProcess(nint frame, uint framePid)
    {
        uint found = 0;
        WinApi.EnumChildWindows(frame, (child, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(child, out var childPid);
            if (childPid != 0 && childPid != framePid)
            {
                found = childPid;
                return false;
            }

            return true;
        }, 0);
        return found;
    }

    private static BitmapSource? GetIcon(nint hwnd, string? exePath)
    {
        try
        {
            foreach (var kind in new nint[] { 1, 2, 0 })
            {
                if (WinApi.SendMessageTimeoutW(hwnd, WinApi.WM_GETICON, kind, 0, WinApi.SMTO_ABORTIFHUNG, 40, out var icon) != 0 && icon != 0)
                {
                    return FromHIcon(icon);
                }
            }

            var classIcon = WinApi.GetClassLongPtr(hwnd, WinApi.GCLP_HICON);
            if (classIcon == 0)
            {
                classIcon = WinApi.GetClassLongPtr(hwnd, WinApi.GCLP_HICONSM);
            }

            if (classIcon != 0)
            {
                return FromHIcon(classIcon);
            }
        }
        catch (Exception)
        {
            // fall through to the executable icon
        }

        return exePath is null ? null : ExeIcon(exePath);
    }

    private static BitmapSource? FromHIcon(nint handle)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static BitmapSource? ExeIcon(string path)
    {
        if (ExeIconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        BitmapSource? source = null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
            {
                source = FromHIcon(icon.Handle);
            }
        }
        catch (Exception)
        {
            // no icon
        }

        ExeIconCache[path] = source;
        return source;
    }
}
