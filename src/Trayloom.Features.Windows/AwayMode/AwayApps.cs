// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>Where the close job is, for the status line and the dump.</summary>
internal enum AwayCloseJobState
{
    None,
    Waiting,
    Closing,
    Done,
}

/// <summary>Which apps can be closed at all, and what happens to a listed one (spec 07 sections 2.1 and 2.2).</summary>
internal static class AwayApps
{
    /// <summary>Apps that are never closed, whatever the list says (shell, system hosts, consoles and terminals).</summary>
    private static readonly HashSet<string> NeverFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "applicationframehost.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe", "searchhost.exe",
        "textinputhost.exe", "lockapp.exe", "dwm.exe", "conhost.exe", "openconsole.exe", "windowsterminal.exe", "cmd.exe",
        "powershell.exe", "pwsh.exe", "wsl.exe", "mintty.exe",
    };

    private static readonly HashSet<string> NeverClasses = new(StringComparer.Ordinal)
    {
        "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS",
    };

    /// <summary>True for an executable Trayloom never closes: itself, the shell, system hosts, consoles and terminals.</summary>
    public static bool NeverClosable(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (NeverFiles.Contains(Path.GetFileName(path)))
        {
            return true;
        }

        var self = Environment.ProcessPath;
        return self is not null && string.Equals(self, path, StringComparison.OrdinalIgnoreCase);
    }

    public static bool NeverClosableClass(string cls) => NeverClasses.Contains(cls);

    /// <summary>A process started as a COM server or helper has no window the person works in.</summary>
    public static bool IsHelperCommand(string commandLine) =>
        commandLine.Contains("-Embedding", StringComparison.OrdinalIgnoreCase)
        || commandLine.Contains("/Embedding", StringComparison.OrdinalIgnoreCase)
        || commandLine.Contains("/automation", StringComparison.OrdinalIgnoreCase)
        || commandLine.Contains("--type=", StringComparison.Ordinal);

    /// <summary>What the settings list says about a listed app (no process needed).</summary>
    public static string CaptionKind(string? path)
    {
        if (NeverClosable(path))
        {
            return "never";
        }

        return AwayBrowsers.KindOf(path) switch
        {
            AwayAppKind.Browser => "browser",
            AwayAppKind.BrowserMin => "browser-min",
            _ => "app",
        };
    }

    /// <summary>The first word of a command, without its quotes.</summary>
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

    /// <summary>The installed browsers Trayloom can reopen with their tabs (HKCU then HKLM <c>StartMenuInternet</c>), lower-cased.</summary>
    public static List<string> InstalledClosableBrowsers()
    {
        var result = new List<string>();
        foreach (var (hive, view) in new[] { (Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Default), (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64), (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32) })
        {
            try
            {
                using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var clients = root.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet");
                if (clients is null)
                {
                    continue;
                }

                foreach (var name in clients.GetSubKeyNames())
                {
                    using var command = clients.OpenSubKey(name + @"\shell\open\command");
                    var raw = command?.GetValue(null) as string;
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    var exe = FirstToken(raw.Trim());
                    if (File.Exists(exe) && AwayBrowsers.KindOf(exe) == AwayAppKind.Browser)
                    {
                        var lower = exe.ToLowerInvariant();
                        if (!result.Contains(lower))
                        {
                            result.Add(lower);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // a hive that cannot be read adds nothing
            }
        }

        return result;
    }
}

/// <summary>
/// The close job (spec 07 sections 3.2 and 4.2): finds the listed apps that are running, writes <c>closed-apps.json</c>, then closes
/// browsers through the Restart Manager and every other app by posting <c>WM_CLOSE</c> to its windows. Nothing is ever forced or ended
/// from outside: an app that asks a question gets "Cancel" and stays open, one that does not answer in time is left alone. Runs on its
/// own thread so a slow app can never freeze Trayloom.
/// </summary>
internal sealed class AwayCloseJob
{
    public const int CloseTimeoutMs = 10000;
    public const int RmWatchdogMs = 20000;
    public const int MaxCloseApps = 20;
    private const int TrayGoneMs = 3000;
    private const int PromptStableMs = 600;
    private const int CancelGraceMs = 3000;
    private const int WmClose = 0x0010;
    private const int ShowMinNoActive = 7;

    private readonly ILog _log;
    private readonly AwayClosedStore _store;
    private readonly List<string> _closeList;
    private readonly HashSet<string> _ignored;
    private readonly AwayDevFilter? _filter;
    private readonly List<AwayRecord> _records;
    private readonly nint _active;
    private readonly DateTime _since;
    private readonly object _gate = new();
    private readonly List<Target> _targets = [];
    private readonly ManualResetEventSlim _finished = new(false);
    private readonly ManualResetEventSlim _cancelSignal = new(false);
    private long _cancelAt;
    private volatile AwayCloseJobState _state = AwayCloseJobState.None;
    private Thread? _thread;

    public AwayCloseJob(ILog log, AwayClosedStore store, IEnumerable<string> closeList, IEnumerable<string> ignored, AwayDevFilter? filter,
        IEnumerable<AwayRecord> records, nint active, DateTime since)
    {
        _log = log;
        _store = store;
        _closeList = [.. closeList.Select(p => p.ToLowerInvariant())];
        _ignored = [.. ignored.Select(p => p.ToLowerInvariant())];
        _filter = filter;
        _records = [.. records];
        _active = active;
        _since = since;
    }

    public AwayCloseJobState State => _state;

    public bool IsCancelled => _cancelSignal.IsSet;

    /// <summary>Raised on the job's thread when the counts changed (the owner marshals to the UI thread).</summary>
    public event Action? Changed;

    /// <summary>The apps that were closed and have to be opened again.</summary>
    public List<ClosedApp> ClosedApps()
    {
        lock (_gate)
        {
            return _targets.Where(t => t.App.State == AwayAppState.Closed).Select(t => t.App).ToList();
        }
    }

    /// <summary>Every app the job looked at (closed, kept, failed).</summary>
    public List<ClosedApp> AllApps()
    {
        lock (_gate)
        {
            return _targets.Select(t => t.App).ToList();
        }
    }

    /// <summary>Apps that were asked to close when the person came back and may still be on their way out (watched briefly).</summary>
    public List<ClosedApp> LateCandidates()
    {
        lock (_gate)
        {
            return _targets.Where(t => t.App.State == AwayAppState.Kept && t.App.Reason == AwayKeepReason.Cancelled && t.Posted).Select(t => t.App).ToList();
        }
    }

    public int ClosedCount
    {
        get
        {
            lock (_gate)
            {
                return _targets.Count(t => t.App.State == AwayAppState.Closed);
            }
        }
    }

    /// <summary>Apps that stayed open (kept) or were only minimized.</summary>
    public int KeptCount
    {
        get
        {
            lock (_gate)
            {
                return _targets.Count(t => t.App.State == AwayAppState.Kept);
            }
        }
    }

    public void Start()
    {
        _state = AwayCloseJobState.Closing;
        _thread = new Thread(Run) { IsBackground = true, Name = "Away close" };
        _thread.Start();
    }

    /// <summary>The person is back: no further app is started; apps already asked get a few seconds to answer.</summary>
    public void Cancel()
    {
        if (!_cancelSignal.IsSet)
        {
            _cancelAt = Environment.TickCount64;
            _cancelSignal.Set();
        }
    }

    /// <summary>Waits for the close pass (not the late-exit watch) to end; true when it did.</summary>
    public bool WaitFinished(int milliseconds) => _finished.Wait(milliseconds);

    // ------------------------------------------------------------------------------------------ the job

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            Close(clock);
        }
        catch (Exception ex)
        {
            _log.Error("[away] close failed", ex);
        }
        finally
        {
            _state = AwayCloseJobState.Done;
            _finished.Set();
            Changed?.Invoke();
        }

        try
        {
            WatchLateExits();
        }
        catch (Exception ex)
        {
            _log.Error("[away] close watch failed", ex);
        }
        finally
        {
            lock (_gate)
            {
                foreach (var target in _targets)
                {
                    target.Handle?.Dispose();
                    target.Handle = null;
                }
            }
        }
    }

    private void Close(Stopwatch clock)
    {
        Discover();
        List<Target> targets;
        lock (_gate)
        {
            targets = [.. _targets];
        }

        var toClose = targets.Where(t => t.App.State == AwayAppState.Closing).ToList();
        if (toClose.Count == 0)
        {
            foreach (var kept in targets)
            {
                LogKept(kept);
            }

            Summarise(targets, clock);
            return;
        }

        // The record is on disk before any message is sent, so a crash from here on still leaves a way back.
        WriteFile();
        _log.Info($"[away] close: record written ({toClose.Count} apps)");
        foreach (var kept in targets.Where(t => t.App.State == AwayAppState.Kept))
        {
            LogKept(kept);
        }

        Changed?.Invoke();

        var others = toClose.Where(t => t.App.Kind != AwayAppKind.Browser).ToList();
        var browsers = toClose.Where(t => t.App.Kind == AwayAppKind.Browser).ToList();

        // Apps that closed or lost their windows since the pass began were closing already: they are not recorded and never reopened.
        foreach (var target in toClose.ToList())
        {
            if (target.Handle is null || target.Handle.HasExited || !HasWindows(target))
            {
                Drop(target);
                others.Remove(target);
                browsers.Remove(target);
            }
        }

        foreach (var target in others)
        {
            if (_cancelSignal.IsSet)
            {
                Keep(target, AwayKeepReason.Cancelled);
                continue;
            }

            PostClose(target);
        }

        foreach (var target in browsers)
        {
            if (_cancelSignal.IsSet)
            {
                Keep(target, AwayKeepReason.Cancelled);
                continue;
            }

            CloseBrowser(target);
        }

        PollOthers(others.Where(t => t.App.State == AwayAppState.Closing).ToList());
        WriteFile();
        foreach (var target in targets.Where(t => t.App.State == AwayAppState.Kept && t.Posted))
        {
            LogKept(target);
        }

        Summarise(targets, clock);
    }

    private void Summarise(List<Target> targets, Stopwatch clock)
    {
        var browsers = targets.Count(t => t.App.Kind is AwayAppKind.Browser or AwayAppKind.BrowserMin);
        var closed = targets.Count(t => t.App.State == AwayAppState.Closed);
        var kept = targets.Where(t => t.App.State == AwayAppState.Kept && IsLeftOpen(t.App.Reason)).ToList();
        var onlyMinimized = targets.Count(t => t.App.State == AwayAppState.Kept && !IsLeftOpen(t.App.Reason));
        var breakdown = kept.Count == 0
            ? string.Empty
            : " (" + string.Join(", ", kept.GroupBy(t => ReasonWord(t.App.Reason)).Select(g => $"{g.Key} {g.Count()}")) + ")";
        _log.Info($"[away] close: {targets.Count} apps ({browsers} browsers, {targets.Count - browsers} other): closed {closed}, kept {kept.Count}{breakdown}, only minimized {onlyMinimized}, in {clock.Elapsed.TotalSeconds:0.0} s");
    }

    private static bool IsLeftOpen(AwayKeepReason reason) =>
        reason is AwayKeepReason.Prompt or AwayKeepReason.Timeout or AwayKeepReason.NotResponding or AwayKeepReason.Refused
            or AwayKeepReason.Cancelled or AwayKeepReason.Elevated or AwayKeepReason.OtherUser;

    private static string ReasonWord(AwayKeepReason reason) => reason switch
    {
        AwayKeepReason.OtherDesktop => "other-desktop",
        AwayKeepReason.NotResponding => "not-responding",
        AwayKeepReason.OtherUser => "other-user",
        _ => reason.ToString().ToLowerInvariant(),
    };

    private void LogKept(Target target)
    {
        var what = target.App.Kind is AwayAppKind.Browser or AwayAppKind.BrowserMin ? "browser" : "app";
        _log.Info($"[away] close: {what} kept ({ReasonWord(target.App.Reason)})");
    }

    // ------------------------------------------------------------------------------------------ discovery

    /// <summary>Finds the listed apps that are running with a window and decides, for each, whether it can be closed.</summary>
    private void Discover()
    {
        var all = AwayProcess.TopLevelWindows();
        var groups = new Dictionary<uint, Target>();
        var otherDesktop = new HashSet<uint>();
        foreach (var hwnd in all)
        {
            if (!WindowEnumerator.IsSwitchable(hwnd))
            {
                if (LooksLikeAppWindow(hwnd) && AwayProcess.IsOnAnotherDesktop(hwnd))
                {
                    NativeMethods.GetWindowThreadProcessId(hwnd, out var other);
                    otherDesktop.Add(other);
                }

                continue;
            }

            var owner = AppIdentity.OwnerOf(hwnd);
            if (owner.Path is null || owner.Pid == (uint)Environment.ProcessId || !_closeList.Contains(owner.Path) || _ignored.Contains(owner.Path))
            {
                continue;
            }

            if (_filter is not null && !_filter.Allows(owner.Pid, owner.Name))
            {
                continue;
            }

            if (!groups.TryGetValue(owner.Pid, out var target))
            {
                target = new Target(owner.Pid, owner.Path, groups.Count);
                groups[owner.Pid] = target;
            }

            target.Windows.Add(hwnd);
            if (AwayApps.NeverClosableClass(WinApi.GetClass(hwnd)))
            {
                target.Never = true;
            }
        }

        var number = 0;
        foreach (var target in groups.Values.OrderBy(t => t.FirstIndex))
        {
            if (target.Never || AwayApps.NeverClosable(target.Path))
            {
                continue;
            }

            var command = AwayProcess.CommandLine(target.Pid);
            if (command is not null && AwayApps.IsHelperCommand(command))
            {
                continue;
            }

            var app = Describe(target, command, ++number, otherDesktop.Contains(target.Pid));
            lock (_gate)
            {
                _targets.Add(target);
            }

            target.App = app;
        }

        // The limit is applied last, so the apps with the topmost windows are the ones that are closed.
        lock (_gate)
        {
            var closing = 0;
            foreach (var target in _targets)
            {
                if (target.App.State != AwayAppState.Closing)
                {
                    continue;
                }

                if (++closing > MaxCloseApps)
                {
                    target.App.State = AwayAppState.Kept;
                    target.App.Reason = AwayKeepReason.Limit;
                }
            }
        }
    }

    private static bool LooksLikeAppWindow(nint hwnd)
    {
        if (!WinApi.IsWindowVisible(hwnd) || AwayProcess.OwnerWindow(hwnd) != 0)
        {
            return false;
        }

        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE).ToInt64();
        return (ex & NativeConstants.WS_EX_TOOLWINDOW) == 0 && WinApi.GetTitle(hwnd).Length > 0;
    }

    private ClosedApp Describe(Target target, string? command, int number, bool onAnotherDesktop)
    {
        var exe = WinApi.GetProcessPath(target.Pid) ?? target.Path;
        var kind = AwayBrowsers.KindOf(exe) ?? AwayAppKind.App;
        var aumid = kind == AwayAppKind.App ? AwayProcess.Aumid(target.Pid) : null;
        if (aumid is not null)
        {
            kind = AwayAppKind.Packaged;
        }

        var app = new ClosedApp
        {
            Id = "a" + number,
            Kind = kind,
            Exe = exe,
            Aumid = aumid,
            Pid = target.Pid,
            PidStart = AwayProcess.StartTime(target.Pid),
            How = kind == AwayAppKind.Browser ? "rm" : "wm_close",
            Active = _active != 0 && target.Windows.Contains(_active),
        };
        target.Handle = AwayProcess.Handle.Open(target.Pid, app.PidStart);
        app.Windows = DescribeWindows(target);

        var access = AwayProcess.CheckAccess(target.Pid);
        if (access == AwayAccess.Elevated)
        {
            return Keep(app, AwayKeepReason.Elevated);
        }

        if (access == AwayAccess.OtherUser)
        {
            return Keep(app, AwayKeepReason.OtherUser);
        }

        if (onAnotherDesktop)
        {
            return Keep(app, AwayKeepReason.OtherDesktop);
        }

        switch (kind)
        {
            case AwayAppKind.BrowserMin:
                return Keep(app, AwayKeepReason.Unsupported);
            case AwayAppKind.Browser:
                if (command is null)
                {
                    return Keep(app, AwayKeepReason.Unsupported);
                }

                var restart = AwayRestartManager.RestartCommand(target.Pid);
                if (restart is null || !restart.Contains("--restore-last-session", StringComparison.Ordinal))
                {
                    return Keep(app, AwayKeepReason.Unsupported);
                }

                app.Args = restart;
                app.Udd = AwayBrowsers.UserDataDirOf(command);
                if (AwayBrowsers.HasPrivateWindow(exe, command, app.Udd, target.Windows))
                {
                    return Keep(app, AwayKeepReason.Private);
                }

                if (AwayBrowsers.DownloadInProgress(exe, app.Udd))
                {
                    return Keep(app, AwayKeepReason.Download);
                }

                break;
            default:
                if (command is null)
                {
                    _log.Info("[away] close: args=unknown");
                }
                else
                {
                    app.Args = AwayProcess.ArgumentsOf(command);
                }

                if (target.Windows.Any(AwayProcess.IsHung))
                {
                    return Keep(app, AwayKeepReason.NotResponding);
                }

                break;
        }

        return app;
    }

    private static ClosedApp Keep(ClosedApp app, AwayKeepReason reason)
    {
        app.State = AwayAppState.Kept;
        app.Reason = reason;
        return app;
    }

    private List<ClosedWindow> DescribeWindows(Target target)
    {
        var list = new List<ClosedWindow>();
        var extra = 0;
        foreach (var hwnd in target.Windows)
        {
            var record = _records.FirstOrDefault(r => r.Hwnd == hwnd);

            // Read in the window's own DPI context: the rectangle is put back the same way, so an app that is not DPI aware lands exactly
            // where it was.
            int[] normal;
            using (WinApiDpi.EnterWindowDpiContext(hwnd))
            {
                var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                var r = WinApiPlacement.GetWindowPlacement(hwnd, ref placement) ? placement.rcNormalPosition : default;
                normal = [r.Left, r.Top, r.Right, r.Bottom];
            }

            // A window that was already minimized before the person left is not in the minimize record: it comes back minimized.
            list.Add(new ClosedWindow
            {
                Z = record?.Z ?? _records.Count + extra++,
                Show = record is null ? "min" : record.Max ? "max" : "normal",
                Normal = [.. normal],
                OldHwnd = hwnd,
            });
        }

        return list;
    }

    private bool HasWindows(Target target)
    {
        foreach (var hwnd in AwayProcess.TopLevelWindows())
        {
            if (WindowEnumerator.IsSwitchable(hwnd) && WindowBelongs(hwnd, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the window belongs to the target (a UWP frame belongs to the process it hosts).</summary>
    private static bool WindowBelongs(nint hwnd, Target target)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == target.Pid)
        {
            return true;
        }

        return WinApi.GetClass(hwnd) == "ApplicationFrameWindow" && AppIdentity.OwnerOf(hwnd).Pid == target.Pid;
    }

    private void Drop(Target target)
    {
        lock (_gate)
        {
            _targets.Remove(target);
        }

        target.Handle?.Dispose();
        target.Handle = null;
        WriteFile();
    }

    private void Keep(Target target, AwayKeepReason reason)
    {
        Keep(target.App, reason);
        MinimiseAgain(target);
        WriteFile();
        Changed?.Invoke();
    }

    private void MarkClosed(Target target, bool tray = false, bool background = false)
    {
        target.App.State = AwayAppState.Closed;
        target.App.Reason = AwayKeepReason.None;
        target.App.Tray = tray;
        target.App.Background = background;
        WriteFile();
        Changed?.Invoke();
    }

    /// <summary>A prompt may have restored one of the windows: kept apps go back to being minimized, as the rest of the windows are.</summary>
    private static void MinimiseAgain(Target target)
    {
        foreach (var hwnd in target.Windows)
        {
            if (WinApi.IsWindow(hwnd) && !WinApi.IsIconic(hwnd))
            {
                WinApi.ShowWindowAsync(hwnd, ShowMinNoActive);
            }
        }
    }

    private void WriteFile()
    {
        ClosedAppsFile file;
        lock (_gate)
        {
            file = new ClosedAppsFile
            {
                OwnerPid = Environment.ProcessId,
                OwnerStart = AwaySession.CurrentProcessStart(),
                Since = _since.ToUniversalTime(),
                ClosedAt = DateTime.UtcNow,
                Apps = _targets.Select(t => t.App).ToList(),
            };

            if (file.Apps.Count == 0)
            {
                _store.Delete();
                return;
            }
        }

        _store.Write(file);
    }

    // ------------------------------------------------------------------------------------------ browsers

    private void CloseBrowser(Target target)
    {
        var app = target.App;
        var outcome = AwayBrowsers.Close(target.Pid, app.PidStart, () => HasWindows(target), () => _cancelSignal.IsSet, RmWatchdogMs, _log);
        switch (outcome)
        {
            case BrowserCloseOutcome.Closed:
                MarkClosed(target);
                break;
            case BrowserCloseOutcome.ClosedInBackground:
                MarkClosed(target, background: true);
                break;
            case BrowserCloseOutcome.Cancelled:
                Keep(target, AwayKeepReason.Cancelled);
                break;
            default:
                Keep(target, AwayKeepReason.Refused);
                break;
        }

        target.Posted = true;
    }

    // ------------------------------------------------------------------------------------------ other apps

    private void PostClose(Target target)
    {
        target.Posted = true;
        target.PostedAt = Environment.TickCount64;
        foreach (var hwnd in AwayProcess.TopLevelWindows())
        {
            if (WindowBelongs(hwnd, target))
            {
                target.Original.Add(hwnd);
            }
        }

        // Bottom of the Z order first, so the window used last closes last (apps that remember "the last window" keep the right one).
        foreach (var hwnd in Enumerable.Reverse(target.Windows))
        {
            if (WinApi.IsWindow(hwnd) && AwayProcess.OwnerWindow(hwnd) == 0)
            {
                WinApi.PostMessageW(hwnd, WmClose, 0, 0);
            }
        }
    }

    private void PollOthers(List<Target> pending)
    {
        while (pending.Count > 0)
        {
            var now = Environment.TickCount64;
            var all = AwayProcess.TopLevelWindows();
            foreach (var target in pending.ToList())
            {
                if (Evaluate(target, all, now))
                {
                    pending.Remove(target);
                }
            }

            if (pending.Count > 0)
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>One look at an app that was asked to close. True when it is decided.</summary>
    private bool Evaluate(Target target, List<nint> all, long now)
    {
        var app = target.App;
        if (target.Handle is { } handle && handle.HasExited)
        {
            MarkClosed(target);
            return true;
        }

        var mine = all.Where(h => WindowBelongs(h, target)).ToList();
        var shown = mine.Where(WindowEnumerator.IsSwitchable).ToList();
        var isFrame = app.Kind == AwayAppKind.Packaged && mine.Any(h => WinApi.GetClass(h) == "ApplicationFrameWindow");

        if (shown.Count == 0)
        {
            // A UWP frame that is gone is a closed app (the hosted process may linger, suspended).
            if (isFrame || app.Kind == AwayAppKind.Packaged && target.Original.Any(h => WinApi.GetClass(h) == "ApplicationFrameWindow"))
            {
                MarkClosed(target);
                return true;
            }

            if (target.GoneSince == 0)
            {
                target.GoneSince = now;
            }
            else if (now - target.GoneSince >= TrayGoneMs)
            {
                MarkClosed(target, tray: true);
                return true;
            }
        }
        else
        {
            target.GoneSince = 0;
        }

        if (HasPrompt(target, mine, now))
        {
            AnswerPrompt(target, mine);
            return true;
        }

        if (mine.Any(AwayProcess.IsHung))
        {
            Keep(target, AwayKeepReason.NotResponding);
            return true;
        }

        var limit = _cancelSignal.IsSet ? Math.Min(target.PostedAt + CloseTimeoutMs, _cancelAt + CancelGraceMs) : target.PostedAt + CloseTimeoutMs;
        if (now >= limit && shown.Count > 0)
        {
            Keep(target, _cancelSignal.IsSet && now < target.PostedAt + CloseTimeoutMs ? AwayKeepReason.Cancelled : AwayKeepReason.Timeout);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A new visible window owned by one of the app's windows (or a standard dialog), or one of the windows that were asked to close
    /// staying disabled for a moment (a modal loop). Hidden helper windows of the app are not looked at.
    /// </summary>
    private static bool HasPrompt(Target target, List<nint> mine, long now)
    {
        var prompt = false;
        foreach (var hwnd in mine)
        {
            if (!target.Original.Contains(hwnd))
            {
                if (WinApi.IsWindowVisible(hwnd) && (target.Windows.Contains(AwayProcess.OwnerWindow(hwnd)) || WinApi.GetClass(hwnd) == "#32770"))
                {
                    prompt = true;
                }

                continue;
            }

            if (!target.Windows.Contains(hwnd))
            {
                continue;
            }

            if (!WinApi.IsWindowEnabled(hwnd))
            {
                if (!target.DisabledSince.TryGetValue(hwnd, out var since))
                {
                    target.DisabledSince[hwnd] = now;
                }
                else if (now - since >= PromptStableMs)
                {
                    prompt = true;
                }
            }
            else
            {
                target.DisabledSince.Remove(hwnd);
            }
        }

        return prompt;
    }

    /// <summary>"Cancel" for a standard dialog (<c>WM_CLOSE</c> to a <c>#32770</c> is IDCANCEL); any other prompt is left alone.</summary>
    private void AnswerPrompt(Target target, List<nint> mine)
    {
        var dialogs = mine.Where(h => !target.Original.Contains(h) && WinApi.IsWindowVisible(h) && WinApi.GetClass(h) == "#32770").ToList();
        foreach (var dialog in dialogs)
        {
            WinApi.PostMessageW(dialog, WmClose, 0, 0);
        }

        Thread.Sleep(1000);
        var stillThere = dialogs.Count == 0 || dialogs.Any(WinApi.IsWindow);
        _log.Info(stillThere ? "[away] close: prompt left open" : "[away] close: prompt cancelled");
        Keep(target, AwayKeepReason.Prompt);
    }

    /// <summary>
    /// An app that did not close in time may still exit later while the person is away; it closed because Trayloom asked, so it becomes
    /// a closed app (spec 07 section 4.2 step 7). Ends when the person is back.
    /// </summary>
    private void WatchLateExits()
    {
        while (!_cancelSignal.Wait(300))
        {
            List<Target> watch;
            lock (_gate)
            {
                watch = _targets.Where(t => t.App.State == AwayAppState.Kept && t.App.Reason == AwayKeepReason.Timeout && t.Handle is not null).ToList();
            }

            if (watch.Count == 0)
            {
                return;
            }

            foreach (var target in watch)
            {
                if (target.Handle is { } handle && handle.HasExited)
                {
                    _log.Info("[away] close: late exit, now closed");
                    MarkClosed(target);
                }
            }
        }
    }

    /// <summary>The working state of one app during the pass.</summary>
    private sealed class Target(uint pid, string path, int firstIndex)
    {
        public uint Pid { get; } = pid;

        public string Path { get; } = path;

        public int FirstIndex { get; } = firstIndex;

        public List<nint> Windows { get; } = [];

        public HashSet<nint> Original { get; } = [];

        public Dictionary<nint, long> DisabledSince { get; } = [];

        public ClosedApp App { get; set; } = new();

        public AwayProcess.Handle? Handle { get; set; }

        public bool Never { get; set; }

        public bool Posted { get; set; }

        public long PostedAt { get; set; }

        public long GoneSince { get; set; }
    }
}
