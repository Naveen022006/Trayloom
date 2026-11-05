// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.WindowTools;

/// <summary>The apps that can never be quit by "Quit on close" (spec 05 A7.1): the shell, system hosts and Trayloom itself.</summary>
internal static class AutoQuitRules
{
    /// <summary>Rows shown (locked) in the Exceptions list.</summary>
    public static readonly string[] Listed =
    [
        "explorer.exe", "ApplicationFrameHost.exe", "ShellExperienceHost.exe", "SearchHost.exe", "StartMenuExperienceHost.exe",
        "TextInputHost.exe", "taskmgr.exe", "PhoneExperienceHost.exe", "YourPhone.exe",
    ];

    private static readonly HashSet<string> Silent = new(StringComparer.OrdinalIgnoreCase)
    {
        "LockApp.exe", "dwm.exe", "sihost.exe", "taskhostw.exe", "RuntimeBroker.exe", "winlogon.exe", "csrss.exe", "services.exe",
        "svchost.exe", "fontdrvhost.exe", "ctfmon.exe", "Trayloom.exe", "SecurityHealthSystray.exe", "MsMpEng.exe",
    };

    public static bool IsMandatory(string? path, uint pid)
    {
        if (pid == (uint)Environment.ProcessId || path is null)
        {
            return true;
        }

        var file = Path.GetFileName(path);
        return Listed.Any(l => string.Equals(l, file, StringComparison.OrdinalIgnoreCase)) || Silent.Contains(file) || IsInstallerName(file);
    }

    /// <summary>Installers and updaters often show no window for a while but are mid-work: msiexec, setup*, *install*, *update*, *updater*, unins*.</summary>
    internal static bool IsInstallerName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        return name == "msiexec" || name.StartsWith("setup", StringComparison.Ordinal) || name.Contains("install", StringComparison.Ordinal)
            || name.Contains("update", StringComparison.Ordinal) || name.StartsWith("unins", StringComparison.Ordinal);
    }

    public static bool IsListed(string identity) => Listed.Any(l => string.Equals(l, Path.GetFileName(identity), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Quit on close (spec 05 A7): quits an app gracefully (WM_CLOSE) once its last real window is gone. Apps become eligible only after
/// they showed a window while Trayloom was watching; notification-area apps and the Exceptions list are left alone.
/// </summary>
public sealed class AutoQuitFeature : IFeature
{
    private sealed class Watched
    {
        public string? Path { get; init; }

        public int Stamp { get; set; }

        /// <summary>Windows of this app that passed <see cref="IsRealWindow"/> while watched: the only ones Quit may ask to close.</summary>
        public HashSet<nint> Windows { get; } = [];
    }

    private IFeatureContext? _context;
    private WinApi.WinEventProc? _proc;
    private nint _hook;
    private Action<string>? _changed;
    private readonly Dictionary<nint, uint> _windowOwner = [];
    private readonly Dictionary<uint, Watched> _watched = [];

    public string Id => "auto-quit";

    public void Start(IFeatureContext context)
    {
        _context = context;
        Keys.Register(context.App);
        Ambient.App = context.App;
        _changed = key =>
        {
            if (key == Keys.AutoQuitEnabled)
            {
                context.Ui.BeginInvoke(Sync);
            }
        };
        context.App.Changed += _changed;
        Sync();
    }

    public void Stop()
    {
        if (_context is not null && _changed is not null)
        {
            _context.App.Changed -= _changed;
        }

        Unhook();
        _watched.Clear();
        _windowOwner.Clear();
        _context = null;
    }

    private void Sync()
    {
        if (_context?.App.Get<bool>(Keys.AutoQuitEnabled) == true)
        {
            Hook();
        }
        else
        {
            Unhook();
            _watched.Clear();
            _windowOwner.Clear();
        }
    }

    private void Hook()
    {
        if (_hook != 0)
        {
            return;
        }

        // Apps that already show a window are eligible from the start.
        WinApi.EnumWindows((hwnd, _) =>
        {
            Observe(hwnd);
            return true;
        }, 0);
        _proc = OnEvent;
        _hook = WinApi.SetWinEventHook(WinApi.EVENT_OBJECT_CREATE, WinApi.EVENT_OBJECT_HIDE, 0, _proc, 0, 0, WinApi.WINEVENT_OUTOFCONTEXT | WinApi.WINEVENT_SKIPOWNPROCESS);
    }

    private void Unhook()
    {
        if (_hook != 0)
        {
            WinApi.UnhookWinEvent(_hook);
            _hook = 0;
        }

        _proc = null;
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        if (idObject != WinApi.OBJID_WINDOW || idChild != WinApi.CHILDID_SELF || hwnd == 0)
        {
            return;
        }

        if (eventType is WinApi.EVENT_OBJECT_CREATE or WinApi.EVENT_OBJECT_SHOW)
        {
            Observe(hwnd);
            return;
        }

        // destroy / hide of a window we know: look at its app again shortly
        if (_windowOwner.Remove(hwnd, out var pid))
        {
            Schedule(pid);
        }
    }

    private void Observe(nint hwnd)
    {
        if (!IsRealWindow(hwnd))
        {
            return;
        }

        var owner = AppIdentity.OwnerOf(hwnd);
        if (owner.Pid == 0)
        {
            return;
        }

        _windowOwner[hwnd] = owner.Pid;
        if (!_watched.TryGetValue(owner.Pid, out var watched))
        {
            watched = new Watched { Path = owner.Path };
            _watched[owner.Pid] = watched;
        }

        watched.Windows.Add(hwnd);
    }

    /// <summary>A normal top-level window of an app: visible (or minimized), unowned, not a tool window or shell surface.</summary>
    internal static bool IsRealWindow(nint hwnd, bool allowCloaked = true)
    {
        if (hwnd == 0 || !WinApi.IsWindow(hwnd) || !WinApi.IsWindowVisible(hwnd) || WinApi.GetWindow(hwnd, WinApi.GW_OWNER) != 0)
        {
            return false;
        }

        if (WinApi.GetAncestor(hwnd, WinApi.GA_ROOT) != hwnd)
        {
            return false;
        }

        var ex = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((ex & WinApi.WS_EX_APPWINDOW) == 0 && ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0))
        {
            return false;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, WinApi.GWL_STYLE).ToInt64();
        if ((style & WinApi.WS_CAPTION) != WinApi.WS_CAPTION && (style & WinApi.WS_THICKFRAME) == 0 && WinApi.GetTitle(hwnd).Length == 0)
        {
            return false;
        }

        var cls = WinApi.GetClass(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow" or "ForegroundStaging" or "MultitaskingViewFrame")
        {
            return false;
        }

        if (!allowCloaked && WinApi.DwmGetWindowAttribute(hwnd, WinApi.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------------------------------------- checks

    private void Schedule(uint pid)
    {
        if (!_watched.TryGetValue(pid, out var watched))
        {
            return;
        }

        var stamp = ++watched.Stamp;
        foreach (var delay in new[] { 250, 550, 1100 })
        {
            var d = delay;
            _ = _context?.Ui.InvokeAsync(async () =>
            {
                await Task.Delay(d);
                Check(pid, stamp, final: d == 1100);
            });
        }
    }

    private void Check(uint pid, int stamp, bool final)
    {
        if (_context is null || !_watched.TryGetValue(pid, out var watched) || watched.Stamp != stamp)
        {
            return;
        }

        if (CountWindows(pid) > 0 || HasVisibleWindow(pid))
        {
            return;
        }

        if (!final)
        {
            return; // settle looks: quit only when the last look still finds nothing
        }

        if (!ShouldQuit(pid, watched))
        {
            return;
        }

        Quit(pid, watched);
    }

    private static int CountWindows(uint pid)
    {
        var count = 0;
        WinApi.EnumWindows((hwnd, _) =>
        {
            if (IsRealWindow(hwnd) && AppIdentity.OwnerOf(hwnd).Pid == pid)
            {
                count++;
            }

            return true;
        }, 0);
        return count;
    }

    /// <summary>True when the process owns any visible top-level window, owned ones (save prompts, dialogs) included.</summary>
    private static bool HasVisibleWindow(uint pid)
    {
        var found = false;
        WinApi.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid == pid && WinApi.IsWindowVisible(hwnd))
            {
                found = true;
                return false;
            }

            return true;
        }, 0);
        return found;
    }

    private bool ShouldQuit(uint pid, Watched watched)
    {
        if (!DevAllows(watched.Path))
        {
            return false;
        }

        // A window another feature of Trayloom has hidden on purpose (Apple Music kept out of sight by the island) is not a closed window.
        if (WindowGuardianState.IsPidTracked(pid))
        {
            return false;
        }

        var app = _context!.App;
        var path = watched.Path ?? AppIdentity.PathOf(pid);
        if (path is null || AutoQuitRules.IsMandatory(path, pid))
        {
            return false;
        }

        var exceptions = app.Get<List<string>>(Keys.AutoQuitExceptions);
        if (AppIdentity.InList(exceptions, path))
        {
            return false;
        }

        if (TrayApps.IsTrayApp(path) && !AppIdentity.InList(app.Get<List<string>>(Keys.AutoQuitTrayApps), path))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (process.HasExited || process.SessionId == 0)
            {
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }

        // Helper processes of an excepted app (same folder / product) keep it open as well.
        var folder = Path.GetDirectoryName(path);
        foreach (var other in _watched.Where(w => w.Key != pid && w.Value.Path is not null))
        {
            if (AppIdentity.InList(exceptions, other.Value.Path) && string.Equals(Path.GetDirectoryName(other.Value.Path), folder, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    // Development only: with TRAYLOOM_DEV=1 and auto-quit-only.txt in the feature's data folder, act on the listed process names only.
    private bool DevAllows(string? path)
    {
        if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") != "1" || _context is null)
        {
            return true;
        }

        try
        {
            var file = System.IO.Path.Combine(_context.DataDirectory, "auto-quit-only.txt");
            if (!File.Exists(file))
            {
                return true;
            }

            var name = System.IO.Path.GetFileNameWithoutExtension(path ?? string.Empty);
            return File.ReadAllLines(file).Select(l => System.IO.Path.GetFileNameWithoutExtension(l.Trim())).Any(l => l.Length > 0 && string.Equals(l, name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Quit(uint pid, Watched watched)
    {
        _context?.Log.Info(Loc.F("Quit on close: asking {0} (pid {1}) to quit", System.IO.Path.GetFileName(watched.Path), pid));

        // Only windows this app really showed while watched, and only if they still exist and still belong to the app.
        // There is no fallback: an app that stays alive (a save prompt, a background task) is left alone.
        foreach (var hwnd in watched.Windows)
        {
            if (!WinApi.IsWindow(hwnd))
            {
                continue;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid == pid)
            {
                WinApi.PostMessageW(hwnd, WinApi.WM_CLOSE, 0, 0);
            }
        }

        _watched.Remove(pid);
    }
}
