// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>What a reopen pass did, for the log line and the cards.</summary>
internal sealed record AwayReopenSummary(int Apps, int Opened, int Failed, int AlreadyOpen, int WindowsPlaced, int WindowsRecorded, double Seconds);

/// <summary>
/// Opens the apps Trayloom closed (spec 07 sections 3.5, 4.5 and 6): starts each with its recorded command line (never elevated, never
/// while it is already running), finds the windows it opens, pairs them with the recorded ones and puts them where they were. Only what
/// Trayloom closed is ever started. Launching is quick and happens on the calling thread; waiting for windows runs on its own thread.
/// </summary>
internal sealed class AwayReopenJob
{
    public const int BudgetMs = 30000;
    public const int BrowserBudgetMs = 20000;
    public const int LateWatchMs = 10000;
    private const int QuietMs = 2000;
    private const int ShowMaximized = 3;
    private const int ShowRestore = 9;

    private readonly ILog _log;
    private readonly AwayClosedStore _store;
    private readonly ClosedAppsFile _file;
    private readonly List<Slot> _slots = [];
    private readonly Dictionary<long, nint> _replaced = [];
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private HashSet<nint> _before = [];
    private Thread? _thread;
    private AwayReopenSummary? _summary;

    /// <param name="apps">Closed apps to open now.</param>
    /// <param name="late">Apps that were asked to close and may still be exiting; they are opened if they exit within a few seconds.</param>
    public AwayReopenJob(ILog log, AwayClosedStore store, ClosedAppsFile file, IEnumerable<ClosedApp> apps, IEnumerable<ClosedApp> late)
    {
        _log = log;
        _store = store;
        _file = file;
        foreach (var app in apps.OrderBy(a => a.Kind == AwayAppKind.Browser ? 0 : 1))
        {
            _slots.Add(new Slot(app, false));
        }

        foreach (var app in late)
        {
            _slots.Add(new Slot(app, true));
        }
    }

    /// <summary>Apps this job is working on.</summary>
    public int Count => _slots.Count;

    /// <summary>True when at least one app is opened right away (not only watched for a late exit).</summary>
    public bool HasImmediate => _slots.Any(s => !s.Late);

    /// <summary>Old window handle to the handle of the window that took its place.</summary>
    public IReadOnlyDictionary<long, nint> Replaced
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<long, nint>(_replaced);
            }
        }
    }

    public AwayReopenSummary? Summary => _summary;

    /// <summary>True while at least one app is still being opened.</summary>
    public bool Running => _thread is { IsAlive: true };

    /// <summary>
    /// True when the app is open again already: a process of its executable (for a browser: on the same profile folder) owns a visible
    /// window. Launching would only add a window.
    /// </summary>
    public static bool IsOpen(ClosedApp app)
    {
        foreach (var hwnd in AwayProcess.TopLevelWindows())
        {
            if (!WinApi.IsWindowVisible(hwnd) || AwayProcess.OwnerWindow(hwnd) != 0 || WinApi.GetTitle(hwnd).Length == 0)
            {
                continue;
            }

            if (app.Kind == AwayAppKind.Packaged && app.Aumid is not null && WinApi.GetWindowAumid(hwnd) == app.Aumid)
            {
                return true;
            }

            var owner = AppIdentity.OwnerOf(hwnd);
            if (owner.Path is null || !string.Equals(owner.Path, app.Exe, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var command = AwayProcess.CommandLine(owner.Pid);
            if (app.Kind != AwayAppKind.Browser)
            {
                // Another copy of the same program with other arguments (another file, another profile) is not this app: only a copy
                // started the same way counts. A command line that cannot be read counts as open, the safe reading.
                if (app.Kind == AwayAppKind.Packaged || command is null
                    || string.Equals(AwayProcess.ArgumentsOf(command).Trim(), app.Args.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (command is not null && !command.Contains("--type=", StringComparison.Ordinal)
                && AwayBrowsers.SameUserDataDir(app.Exe, app.Udd, AwayBrowsers.UserDataDirOf(command)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Starts every app (synchronously, it takes milliseconds) and then watches for their windows on a background thread.</summary>
    public bool Start()
    {
        if (_slots.Count == 0)
        {
            return false;
        }

        _before = [.. AwayProcess.TopLevelWindows()];
        foreach (var slot in _slots)
        {
            slot.Budget = slot.App.Kind == AwayAppKind.Browser ? BrowserBudgetMs : BudgetMs;
            if (slot.Late)
            {
                slot.Handle = AwayProcess.Handle.Open(slot.App.Pid, slot.App.PidStart);
                slot.Deadline = Environment.TickCount64 + LateWatchMs;
                continue;
            }

            Launch(slot);
        }

        _thread = new Thread(Collect) { IsBackground = true, Name = "Away reopen" };
        _thread.Start();
        return true;
    }

    /// <summary>
    /// Waits for the windows, updates <c>closed-apps.json</c> (only apps that did not open again stay in it) and, when
    /// <paramref name="records"/> are given, puts all windows in their recorded order and focuses the one the person was working in.
    /// </summary>
    public AwayReopenSummary Finish(IReadOnlyList<AwayRecord>? records, nint active)
    {
        return Finish(records, active, out _);
    }

    /// <summary>As <see cref="Finish(IReadOnlyList{AwayRecord}?, nint)"/>, and hands back the windows in the order they were stacked.</summary>
    public AwayReopenSummary Finish(IReadOnlyList<AwayRecord>? records, nint active, out List<AwayRecord> stack)
    {
        stack = [];
        _thread?.Join(BudgetMs + 5000);
        var failed = _slots.Where(s => s.App.State == AwayAppState.Failed).ToList();
        var already = _slots.Count(s => s.AlreadyOpen);
        var opened = _slots.Count(s => s.App.State == AwayAppState.Reopened && !s.AlreadyOpen);
        var recorded = _slots.Where(s => !s.AlreadyOpen && !s.Skipped).Sum(s => s.App.Windows.Count);
        var placed = _slots.Sum(s => s.Placed);

        if (records is not null)
        {
            stack = AwayWindows.Restack(records, Replaced, active, focus: true);
        }

        _file.Apps = failed.Select(s => s.App).ToList();
        if (_file.Apps.Count == 0)
        {
            _store.Delete();
        }
        else
        {
            _store.Write(_file);
        }

        _summary = new AwayReopenSummary(_slots.Count - already - _slots.Count(s => s.Skipped), opened, failed.Count, already, placed, recorded, _clock.Elapsed.TotalSeconds);
        _log.Info($"[away] reopen: {opened}/{_summary.Apps} apps, windows {placed}/{recorded} placed, {failed.Count} failed, {already} already open, in {_summary.Seconds:0.0} s");
        return _summary;
    }

    // ------------------------------------------------------------------------------------------ launching

    private void Launch(Slot slot)
    {
        var app = slot.App;
        slot.Phase = Phase.Collecting;
        slot.StartedAt = Environment.TickCount64;
        slot.Deadline = slot.StartedAt + slot.Budget;
        try
        {
            if (!app.Tray && !app.Background && IsOpen(app))
            {
                slot.AlreadyOpen = true;
                slot.Phase = Phase.Done;
                app.State = AwayAppState.Reopened;
                return;
            }

            app.State = AwayAppState.Reopening;
            if (app.Kind == AwayAppKind.Packaged && !string.IsNullOrEmpty(app.Aumid))
            {
                using var shell = Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + app.Aumid) { UseShellExecute = true });
                return;
            }

            if (!File.Exists(app.Exe))
            {
                Fail(slot);
                return;
            }

            // CreateProcess, never the shell: an app that needs administrator fails here instead of being started elevated.
            var info = new ProcessStartInfo(app.Exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(app.Exe) ?? string.Empty,
                Arguments = app.Tray ? string.Empty : app.Args,
            };
            foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("TRAYLOOM_", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                info.Environment.Remove(key);
            }

            using var process = Process.Start(info);
        }
        catch (Exception ex)
        {
            _log.Info($"[away] reopen: launch failed ({ex.GetType().Name} 0x{ex.HResult:X8})");
            Fail(slot);
        }
    }

    private static void Fail(Slot slot)
    {
        slot.Phase = Phase.Done;
        slot.App.State = AwayAppState.Failed;
        slot.App.Reason = AwayKeepReason.Failed;
    }

    // ------------------------------------------------------------------------------------------ windows

    private void Collect()
    {
        try
        {
            while (_slots.Any(s => s.Phase != Phase.Done))
            {
                var now = Environment.TickCount64;
                var all = AwayProcess.TopLevelWindows();
                foreach (var slot in _slots.Where(s => s.Phase != Phase.Done))
                {
                    Step(slot, all, now);
                }

                Thread.Sleep(100);
            }
        }
        catch (Exception ex)
        {
            _log.Error("[away] reopen failed", ex);
            foreach (var slot in _slots.Where(s => s.Phase != Phase.Done))
            {
                Fail(slot);
            }
        }
        finally
        {
            foreach (var slot in _slots)
            {
                slot.Handle?.Dispose();
                slot.Handle = null;
            }
        }
    }

    private void Step(Slot slot, List<nint> all, long now)
    {
        if (slot.Late && slot.Phase == Phase.Pending)
        {
            if (slot.Handle is null || slot.Handle.HasExited)
            {
                Launch(slot);
            }
            else if (now > slot.Deadline)
            {
                // It never exited: it was not closed, so nothing is opened (it stays as the person left it).
                slot.Phase = Phase.Done;
                slot.Skipped = true;
                slot.App.State = AwayAppState.Kept;
            }

            return;
        }

        foreach (var hwnd in all)
        {
            if (_before.Contains(hwnd) || slot.Found.Contains(hwnd) || _slots.Any(s => s != slot && s.Found.Contains(hwnd)))
            {
                continue;
            }

            if (Belongs(hwnd, slot.App))
            {
                slot.Found.Add(hwnd);
                slot.LastNewAt = now;
            }
        }

        var enough = slot.Found.Count >= slot.App.Windows.Count;
        var quiet = slot.Found.Count > 0 && now - slot.LastNewAt >= QuietMs;
        if (enough || quiet || now >= slot.Deadline)
        {
            Complete(slot);
        }
    }

    /// <summary>A new, titled, unowned window of the app (a browser: of its main process on the same profile folder).</summary>
    private static bool Belongs(nint hwnd, ClosedApp app)
    {
        if (!WinApi.IsWindowVisible(hwnd) || AwayProcess.OwnerWindow(hwnd) != 0 || WinApi.GetTitle(hwnd).Length == 0)
        {
            return false;
        }

        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE).ToInt64();
        if ((ex & NativeConstants.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        var owner = AppIdentity.OwnerOf(hwnd);
        var sameApp = app.Kind == AwayAppKind.Packaged && app.Aumid is not null && WinApi.GetWindowAumid(hwnd) == app.Aumid
            || owner.Path is not null && string.Equals(owner.Path, app.Exe, StringComparison.OrdinalIgnoreCase);
        if (!sameApp)
        {
            return false;
        }

        if (app.Kind != AwayAppKind.Browser)
        {
            return true;
        }

        var command = AwayProcess.CommandLine(owner.Pid);
        return command is not null && !command.Contains("--type=", StringComparison.Ordinal)
            && AwayBrowsers.SameUserDataDir(app.Exe, app.Udd, AwayBrowsers.UserDataDirOf(command));
    }

    private void Complete(Slot slot)
    {
        slot.Phase = Phase.Done;
        var app = slot.App;
        if (slot.Found.Count == 0)
        {
            Fail(slot);
            return;
        }

        Place(slot);
        app.State = AwayAppState.Reopened;
        app.Reason = slot.Found.Count < app.Windows.Count ? AwayKeepReason.Partial : AwayKeepReason.None;
    }

    /// <summary>Pairs new windows with recorded ones by the smallest distance, then puts each where its partner was (spec 07 section 6.3).</summary>
    private void Place(Slot slot)
    {
        var recorded = slot.App.Windows;
        var candidates = new List<(int Recorded, int Found, long Distance)>();
        for (var f = 0; f < slot.Found.Count; f++)
        {
            var current = NormalRect(slot.Found[f]);
            for (var r = 0; r < recorded.Count; r++)
            {
                var n = recorded[r].Normal;
                var distance = Math.Abs(current[0] - n[0]) + Math.Abs(current[1] - n[1]) + Math.Abs(current[2] - n[2]) + Math.Abs(current[3] - n[3]);
                candidates.Add((r, f, distance));
            }
        }

        var usedRecorded = new HashSet<int>();
        var usedFound = new HashSet<int>();
        foreach (var pair in candidates.OrderBy(c => c.Distance).ThenBy(c => c.Found))
        {
            if (usedRecorded.Contains(pair.Recorded) || usedFound.Contains(pair.Found))
            {
                continue;
            }

            usedRecorded.Add(pair.Recorded);
            usedFound.Add(pair.Found);
            var hwnd = slot.Found[pair.Found];
            Apply(hwnd, recorded[pair.Recorded]);
            lock (_gate)
            {
                if (recorded[pair.Recorded].OldHwnd != 0)
                {
                    _replaced[recorded[pair.Recorded].OldHwnd] = hwnd;
                }
            }

            slot.Placed++;
        }
    }

    private static int[] NormalRect(nint hwnd)
    {
        using var scope = WinApiDpi.EnterWindowDpiContext(hwnd);
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!WinApiPlacement.GetWindowPlacement(hwnd, ref placement))
        {
            return [0, 0, 0, 0];
        }

        var r = placement.rcNormalPosition;
        return [r.Left, r.Top, r.Right, r.Bottom];
    }

    private static void Apply(nint hwnd, ClosedWindow window)
    {
        try
        {
            // The monitors are read in the window's DPI context too, so the rectangle and the screens share one coordinate space.
            using var _ = WinApiDpi.EnterWindowDpiContext(hwnd);
            var monitors = WindowPlacementMath.GetMonitors();
            var workspace = new RECT { Left = window.Normal[0], Top = window.Normal[1], Right = window.Normal[2], Bottom = window.Normal[3] };
            var screen = WindowPlacementMath.WorkspaceToScreen(workspace, monitors);

            // A display may be gone: a window that would not be comfortably visible is moved onto one that is there.
            if (!WindowPlacementMath.IsComfortablyVisible(screen, monitors))
            {
                screen = WindowPlacementMath.Sanitize(screen, monitors);
            }

            if (WinApi.IsIconic(hwnd))
            {
                WinApi.ShowWindowAsync(hwnd, WinApi.SW_SHOWNOACTIVATE);
            }
            else if (WinApi.IsZoomed(hwnd))
            {
                WinApi.ShowWindowAsync(hwnd, ShowRestore);
            }

            NativeMethods.SetWindowPos(hwnd, 0, screen.Left, screen.Top, screen.Width, screen.Height,
                NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE | WinApi.SWP_ASYNCWINDOWPOS);

            if (window.Show == "max")
            {
                WinApi.ShowWindowAsync(hwnd, ShowMaximized);
            }
            else if (window.Show == "min")
            {
                WinApi.ShowWindowAsync(hwnd, WinApi.SW_SHOWMINNOACTIVE);
            }
        }
        catch (Exception)
        {
            // placement is best effort; the window is open either way
        }
    }

    private enum Phase
    {
        Pending,
        Collecting,
        Done,
    }

    /// <summary>The working state of one app being opened.</summary>
    private sealed class Slot(ClosedApp app, bool late)
    {
        public ClosedApp App { get; } = app;

        public bool Late { get; } = late;

        public Phase Phase { get; set; } = Phase.Pending;

        public List<nint> Found { get; } = [];

        public AwayProcess.Handle? Handle { get; set; }

        public int Budget { get; set; }

        public long StartedAt { get; set; }

        public long Deadline { get; set; }

        public long LastNewAt { get; set; }

        public int Placed { get; set; }

        public bool AlreadyOpen { get; set; }

        public bool Skipped { get; set; }
    }
}
