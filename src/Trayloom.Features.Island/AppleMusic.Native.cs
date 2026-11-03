// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Trayloom.Core.Native;

namespace Trayloom.Features.Island;

/// <summary>Win32 interop of the Apple Music driver (window styles, placement, WinEvent hooks, CreateProcess).</summary>
internal static class AmNative
{
    internal delegate bool EnumProc(nint hwnd, nint lParam);

    internal delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    internal const int SwHide = 0;
    internal const int SwShow = 5;
    internal const int SwShowNoActivate = 4;
    internal const int SwMinimize = 6;
    internal const int SwShowMinNoActive = 7;
    internal const int SwRestore = 9;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint SwpAsyncWindowPos = 0x4000;
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const uint SmtoErrorOnExit = 0x0020;
    internal const int GwlExStyle = -20;
    internal const long WsExLayered = 0x80000;
    internal const long WsExTransparent = 0x20;
    internal const uint LwaAlpha = 2;
    internal const long WsExToolWindow = 0x80;
    internal const long WsExAppWindow = 0x40000;
    internal const long WsExNoActivate = 0x08000000;
    internal const uint GwOwner = 4;
    internal const uint EventSystemForeground = 3;
    internal const uint EventSystemMinimizeStart = 0x16;
    internal const uint EventObjectCreate = 0x8000;
    internal const uint EventObjectDestroy = 0x8001;
    internal const uint EventObjectShow = 0x8002;
    internal const uint EventObjectHide = 0x8003;
    internal const uint GaRoot = 2;
    internal const uint WmQuit = 0x0012;
    internal const string MainWindowClass = "WinUIDesktopWin32WindowClass";
    internal const string PopupWindowClass = "Microsoft.UI.Content.PopupWindowSiteBridge";

    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect Normal;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        public int Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public nint Reserved3;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumProc proc, nint lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern bool AttachThreadInput(uint from, uint to, bool attach);

    [DllImport("user32.dll")]
    internal static extern bool BringWindowToTop(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll")]
    internal static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    internal static extern void SwitchToThisWindow(nint hwnd, bool altTab);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindowAsync(nint hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool IsHungAppWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    internal static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcess(string? application, StringBuilder command, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string? directory, ref StartupInfo info, out ProcessInformation process);

    [DllImport("kernel32.dll")]
    internal static extern bool CloseHandle(nint handle);

    /// <summary>
    /// Runs a cross-process window call (style, placement, show / hide) that must never be able to stall the caller: a window of a hung
    /// or not-yet-pumping Apple Music turns these into synchronous waits of unbounded length. The window must answer a WM_NULL within 200 ms,
    /// then <paramref name="call"/> runs on its own thread and is waited for at most <paramref name="ms"/>; on a timeout the call is left to
    /// finish by itself (several calls that must stay in order belong in one <paramref name="call"/>). False = it did not (or could not) run to the end.
    /// </summary>
    internal static bool Bounded(nint hwnd, Func<bool> call, int ms, string what, Action<string>? say = null) => Bounded(hwnd, call, ms, what, say, out _);

    /// <summary>
    /// As <see cref="Bounded(nint, Func{bool}, int, string, Action{string}?)"/>; <paramref name="running"/> is the call's task when it was started
    /// and had not ended in time (it is still inside Apple Music and may complete later, which a restore of the same window has to wait for).
    /// </summary>
    internal static bool Bounded(nint hwnd, Func<bool> call, int ms, string what, Action<string>? say, out Task<bool>? running)
    {
        running = null;
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            return false;
        }

        if (IsHungAppWindow(hwnd))
        {
            say?.Invoke($"{what}: Apple Music is not responding; skipped");
            return false;
        }

        if (SendMessageTimeout(hwnd, 0, 0, 0, SmtoAbortIfHung | SmtoErrorOnExit, 200, out _) == 0)
        {
            say?.Invoke($"{what}: Apple Music did not answer in 200 ms; skipped");
            return false;
        }

        var task = Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            if (task.Wait(ms))
            {
                return task.Result;
            }
        }
        catch (AggregateException)
        {
            // the window vanished under the call
            return false;
        }

        say?.Invoke($"{what}: Apple Music did not answer in {ms} ms; skipped");
        _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        running = task;
        return false;
    }

    // ---------------------------------------------------------------- what the driver does to Apple Music's windows, and giving it back

    /// <summary>
    /// The true extended style (the one from before the driver made the window transparent) of every window the driver has ghosted and not
    /// yet given back, process-wide. Whoever ghosts a window first records it; every later capture and every restore reads it instead of the
    /// window's live style, which may be a ghost left behind by an earlier job (or by an abandoned job that is still running).
    /// </summary>
    private static readonly ConcurrentDictionary<nint, long> OriginalStyles = new();

    /// <summary>Where the person had the same windows (restore rectangle): the place a parked window goes back to.</summary>
    private static readonly ConcurrentDictionary<nint, Rect> Homes = new();

    private sealed record RestoreJob(Func<bool> Attempt, string What, long Until);

    private static readonly ConcurrentDictionary<nint, RestoreJob> Restores = new();

    /// <summary>Records the true extended style of a window about to be ghosted (the first record wins) and returns it.</summary>
    internal static long RememberStyle(nint hwnd)
    {
        if (OriginalStyles.TryGetValue(hwnd, out var known))
        {
            return known;
        }

        var live = (long)GetWindowLongPtr(hwnd, GwlExStyle);
        var original = IsGhost(hwnd) ? Deghost(live, ClassOf(hwnd)) : live;
        if (OriginalStyles.TryAdd(hwnd, original))
        {
            // on disk before the window is touched: whatever happens to Trayloom from here on, the guardian can give the style back
            WindowGuardianState.Track(hwnd, ProcessOf(hwnd), AppleMusicProcess, ClassOf(hwnd), WindowGuardianState.Ghost, original);
            return original;
        }

        return OriginalStyles.GetOrAdd(hwnd, original);
    }

    /// <summary>Windows a driver job (a <c>HiddenWindow</c>) is working on right now, with the number of jobs on each (an abandoned job may overlap its successor).</summary>
    private static readonly ConcurrentDictionary<nint, int> DriverWindows = new();

    /// <summary>The driver starts working on the window (it is invisible by design until <see cref="DriverReleases"/>).</summary>
    internal static void DriverTakes(nint hwnd) => DriverWindows.AddOrUpdate(hwnd, 1, (_, n) => n + 1);

    /// <summary>The driver is done with the window.</summary>
    internal static void DriverReleases(nint hwnd)
    {
        while (DriverWindows.TryGetValue(hwnd, out var n))
        {
            var done = n <= 1
                ? ((ICollection<KeyValuePair<nint, int>>)DriverWindows).Remove(new KeyValuePair<nint, int>(hwnd, n))
                : DriverWindows.TryUpdate(hwnd, n - 1, n);
            if (done)
            {
                return;
            }
        }
    }

    /// <summary>A driver job is working on the window right now.</summary>
    internal static bool IsDriverWindow(nint hwnd) => DriverWindows.ContainsKey(hwnd);

    /// <summary>The real extended style of the window is on record (it was made transparent and not given back yet).</summary>
    internal static bool HasStyleRecord(nint hwnd) => OriginalStyles.ContainsKey(hwnd);

    /// <summary>Records where the person had the window (ignored unless it is comfortably visible: anything else is a parked or lost window's place, not theirs).</summary>
    internal static void RememberHome(nint hwnd, Rect home)
    {
        if (Comfortable(hwnd, home))
        {
            if (Homes.Count > 64)
            {
                Homes.Clear();
            }

            Homes[hwnd] = home;
        }
    }

    /// <summary>The style of a ghost without the ghost: the best guess when nobody recorded the real one (a previous run of Trayloom was cut off).</summary>
    private static long Deghost(long ex, string cls)
    {
        ex &= ~(WsExLayered | WsExTransparent);
        if (cls == MainWindowClass)
        {
            ex &= ~WsExToolWindow;
        }

        return ex;
    }

    /// <summary>The name the guardian records for Apple Music's windows.</summary>
    internal const string AppleMusicProcess = "AppleMusic";

    internal static RECT ToCore(Rect r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };

    internal static Rect FromCore(RECT r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };

    /// <summary>The window's caption strip is clearly inside some monitor's work area (not hiding under the taskbar, not parked).</summary>
    internal static bool Comfortable(Rect r) => WindowPlacementMath.IsComfortablyVisible(ToCore(r), WindowPlacementMath.GetMonitors());

    /// <summary>
    /// As <see cref="Comfortable(Rect)"/> for the rectangle of this window: a maximised window (also a hidden one) is the person's whole
    /// screen and always comfortable, whatever its frame does to the rectangle at this scaling. Not for a minimised window (its rectangle is
    /// the parking spot of the taskbar button, or its restore rectangle, which is a normal one).
    /// </summary>
    internal static bool Comfortable(nint hwnd, Rect r) => (!IsIconic(hwnd) && IsZoomed(hwnd) && r.Left > -10000) || Comfortable(r);

    /// <summary>The rectangle itself when comfortably visible, else a usable one on the nearest monitor (centred on the primary one when it is parked or invalid).</summary>
    internal static Rect Sanitized(Rect r) => FromCore(WindowPlacementMath.Sanitize(ToCore(r), WindowPlacementMath.GetMonitors()));

    /// <summary>A WINDOWPLACEMENT rectangle (workspace coordinates) in screen coordinates.</summary>
    internal static Rect WorkspaceToScreen(Rect r) => FromCore(WindowPlacementMath.WorkspaceToScreen(ToCore(r), WindowPlacementMath.GetMonitors()));

    /// <summary>A screen rectangle as WINDOWPLACEMENT wants it.</summary>
    internal static Rect ScreenToWorkspace(Rect r) => FromCore(WindowPlacementMath.ScreenToWorkspace(ToCore(r), WindowPlacementMath.GetMonitors()));

    /// <summary>
    /// Where the window is, in screen coordinates: its own rectangle (valid for a hidden window too), or for a minimised one the restore
    /// rectangle (workspace coordinates, except for a tool window, whose placement is in screen coordinates).
    /// </summary>
    internal static bool CurrentScreenHome(nint hwnd, out Rect home)
    {
        home = default;
        if (!IsIconic(hwnd))
        {
            return GetWindowRect(hwnd, out home);
        }

        var wp = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref wp))
        {
            return false;
        }

        home = ((long)GetWindowLongPtr(hwnd, GwlExStyle) & WsExToolWindow) != 0 ? wp.Normal : WorkspaceToScreen(wp.Normal);
        return true;
    }

    /// <summary>A place for the window that the person can see: where it is now when that is comfortable, else where they had it, else a corrected one.</summary>
    internal static Rect SafeHome(nint hwnd)
    {
        CurrentScreenHome(hwnd, out var now);
        if (Comfortable(hwnd, now))
        {
            return now;
        }

        if (Homes.TryGetValue(hwnd, out var known) && Comfortable(known))
        {
            return known;
        }

        return Sanitized(now);
    }

    private static int[] HomeArray(Rect r) => [r.Left, r.Top, r.Right, r.Bottom];

    /// <summary>Records the window as hidden by the keeper (before it is hidden), with the place it comes back to.</summary>
    internal static void TrackKept(nint hwnd, uint pid, Rect home) =>
        WindowGuardianState.Track(hwnd, pid, AppleMusicProcess, MainWindowClass, WindowGuardianState.Kept, null, HomeArray(home));

    /// <summary>Records the window as parked far off-screen (before it is moved), with the place it comes back to.</summary>
    internal static void TrackParked(nint hwnd, Rect home) =>
        WindowGuardianState.Track(hwnd, ProcessOf(hwnd), AppleMusicProcess, MainWindowClass, WindowGuardianState.Parked, null, HomeArray(home));

    /// <summary>The titled main window of an Apple Music process that nobody can see although it is not minimised: hidden, transparent or parked. 0 = none.</summary>
    internal static nint StuckMainWindow()
    {
        // the windows of a start by Trayloom are transparent or parked on purpose until it is over
        if (AppleMusicKeeper.Instance is { Launching: true })
        {
            return 0;
        }

        foreach (var h in MainWindows())
        {
            var title = TitleOf(h);
            if (title.Length == 0 || title == "WinUI Desktop")
            {
                continue;
            }

            // a window the driver has made invisible for its job at this moment is not stuck: it is given back by the job
            if (IsDriverWindow(h))
            {
                continue;
            }

            if (!IsWindowVisible(h) || IsGhost(h) || (!IsIconic(h) && GetWindowRect(h, out var r) && r.Left <= -10000))
            {
                return h;
            }
        }

        return 0;
    }

    /// <summary>The window's rectangle (and visibility) for a log line.</summary>
    internal static string RectText(nint hwnd)
    {
        if (!IsWindow(hwnd) || !GetWindowRect(hwnd, out var r))
        {
            return "[gone]";
        }

        var text = $"[{r.Left},{r.Top},{r.Right},{r.Bottom}]";
        if (IsIconic(hwnd))
        {
            var wp = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (GetWindowPlacement(hwnd, ref wp))
            {
                var n = WorkspaceToScreen(wp.Normal);
                text += $" restore=[{n.Left},{n.Top},{n.Right},{n.Bottom}]";
            }
        }

        return $"{text} visible={(IsWindowVisible(hwnd) ? 1 : 0)} iconic={(IsIconic(hwnd) ? 1 : 0)}";
    }

    /// <summary>
    /// Makes sure the window will not come back off-screen: a window that is not minimised is moved (also while hidden), a minimised one gets
    /// a corrected restore rectangle (the placement is in workspace coordinates). Inside <see cref="Bounded(nint, Func{bool}, int, string, Action{string}?)"/>.
    /// </summary>
    internal static void FixPlacement(nint hwnd)
    {
        if (!CurrentScreenHome(hwnd, out var now) || Comfortable(hwnd, now))
        {
            // nothing to correct; a maximised window is never moved or resized
            return;
        }

        var target = Homes.TryGetValue(hwnd, out var known) && Comfortable(known) ? known : Sanitized(now);
        if (!IsIconic(hwnd))
        {
            SetWindowPos(hwnd, 0, target.Left, target.Top, target.Right - target.Left, target.Bottom - target.Top, SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
            return;
        }

        var wp = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref wp))
        {
            return;
        }

        wp.Normal = ((long)GetWindowLongPtr(hwnd, GwlExStyle) & WsExToolWindow) != 0 ? target : ScreenToWorkspace(target);
        wp.ShowCmd = SwShowMinNoActive;
        wp.Flags = 4; // WPF_ASYNCWINDOWPLACEMENT
        SetWindowPlacement(hwnd, ref wp);
    }

    /// <summary>
    /// Puts the window's own extended style back (and full opacity, should it stay layered) and forgets the record. A synchronous
    /// cross-process call: run it inside <see cref="Bounded(nint, Func{bool}, int, string, Action{string}?)"/>.
    /// </summary>
    internal static void RestoreStyleNow(nint hwnd)
    {
        long ex;
        if (!OriginalStyles.TryGetValue(hwnd, out ex))
        {
            ex = (long)GetWindowLongPtr(hwnd, GwlExStyle);
            if (IsGhost(hwnd))
            {
                ex = Deghost(ex, ClassOf(hwnd));
            }
        }

        SetWindowLongPtr(hwnd, GwlExStyle, (nint)ex);
        if ((ex & WsExLayered) != 0)
        {
            SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
        }

        OriginalStyles.TryRemove(hwnd, out _);
        if (!IsGhost(hwnd))
        {
            WindowGuardianState.Untrack(hwnd, WindowGuardianState.Ghost);
        }
    }

    /// <summary>A window parked far off-screen goes back to where the person had it (or to the screen's centre). Inside <see cref="Bounded(nint, Func{bool}, int, string, Action{string}?)"/>.</summary>
    internal static void Unpark(nint hwnd)
    {
        if (IsIconic(hwnd) || !GetWindowRect(hwnd, out var r) || r.Left > -10000)
        {
            // not parked (any more)
            WindowGuardianState.Untrack(hwnd, WindowGuardianState.Parked);
            return;
        }

        var home = Homes.TryGetValue(hwnd, out var known) && Comfortable(known) ? known : Sanitized(r);
        SetWindowPos(hwnd, 0, home.Left, home.Top, home.Right - home.Left, home.Bottom - home.Top, SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
        if (GetWindowRect(hwnd, out var after) && after.Left > -10000)
        {
            WindowGuardianState.Untrack(hwnd, WindowGuardianState.Parked);
        }
    }

    /// <summary>Gives the window its style back, bounded; when Apple Music does not answer the restore is retried in the background.</summary>
    internal static void RestoreStyle(nint hwnd, Action<string>? say = null)
    {
        Func<bool> restore = () =>
        {
            RestoreStyleNow(hwnd);
            return true;
        };
        if (!Bounded(hwnd, restore, 400, "restoring the window style", say))
        {
            RetryRestore(hwnd, restore, "restoring the window style", say);
        }
    }

    /// <summary>
    /// A restore that did not get through (Apple Music was busy, which right after "UI ready" or the start of a song is common) is never
    /// dropped: <paramref name="attempt"/> is run again every 250 ms for up to 10 s (a later request heals what is still wrong after that).
    /// A newer retry for the same window replaces the older one; <paramref name="attempt"/> must be safe to repeat.
    /// </summary>
    internal static void RetryRestore(nint hwnd, Func<bool> attempt, string what, Action<string>? say)
    {
        if (!IsWindow(hwnd))
        {
            OriginalStyles.TryRemove(hwnd, out _);
            WindowGuardianState.UntrackWindow(hwnd);
            return;
        }

        var job = new RestoreJob(attempt, what, Environment.TickCount64 + 10000);
        while (true)
        {
            if (Restores.TryAdd(hwnd, job))
            {
                _ = Task.Run(() => RetryLoop(hwnd, say));
                return;
            }

            if (Restores.TryGetValue(hwnd, out var old) && Restores.TryUpdate(hwnd, job, old))
            {
                return;
            }
        }
    }

    /// <summary>A job is about to use the window again: restores still waiting for it would take it out from under the job.</summary>
    internal static void CancelRetry(nint hwnd) => Restores.TryRemove(hwnd, out _);

    private static async Task RetryLoop(nint hwnd, Action<string>? say)
    {
        while (Restores.TryGetValue(hwnd, out var job))
        {
            await Task.Delay(250).ConfigureAwait(false);
            if (!Restores.TryGetValue(hwnd, out job))
            {
                return;
            }

            var done = false;
            if (!IsWindow(hwnd))
            {
                OriginalStyles.TryRemove(hwnd, out _);
                WindowGuardianState.UntrackWindow(hwnd);
                done = true;
            }
            else if (Environment.TickCount64 > job.Until)
            {
                say?.Invoke($"{job.What}: Apple Music did not take it in 10 s; the next request tries again (the window guardian keeps the record)");
                done = true;
            }
            else if (Bounded(hwnd, job.Attempt, 1500, job.What, null))
            {
                done = true;
            }

            if (done)
            {
                // a newer retry may have replaced this one meanwhile: then it goes on
                ((ICollection<KeyValuePair<nint, RestoreJob>>)Restores).Remove(new KeyValuePair<nint, RestoreJob>(hwnd, job));
            }
        }
    }

    /// <summary>
    /// The safety net behind every restore: any Apple Music window that is still transparent (or whose real style is still on record), or
    /// that is visible and parked far off-screen, is given back, whoever left it so. Run when a request starts and when Trayloom starts, i.e.
    /// when no job of this process is meant to be using the windows. A parked window that was on screen is minimised (it was meant to be out
    /// of sight) unless the person has opened Apple Music.
    /// </summary>
    internal static void Heal(Action<string>? say = null)
    {
        foreach (var h in OriginalStyles.Keys)
        {
            if (!IsWindow(h))
            {
                OriginalStyles.TryRemove(h, out _);
                WindowGuardianState.UntrackWindow(h);
            }
        }

        foreach (var h in Homes.Keys)
        {
            if (!IsWindow(h))
            {
                Homes.TryRemove(h, out _);
            }
        }

        var windows = new List<(nint Hwnd, bool Main)>();
        EnumWindows((h, _) =>
        {
            var cls = ClassOf(h);
            if ((cls == MainWindowClass || cls == PopupWindowClass) && IsAppleMusic(ProcessOf(h)))
            {
                windows.Add((h, cls == MainWindowClass));
            }

            return true;
        }, 0);
        foreach (var (hwnd, main) in windows)
        {
            var ghost = IsGhost(hwnd);
            var parked = main && IsWindowVisible(hwnd) && !IsIconic(hwnd) && GetWindowRect(hwnd, out var rect) && rect.Left <= -10000;
            if (!ghost && !parked && !OriginalStyles.ContainsKey(hwnd))
            {
                continue;
            }

            var window = hwnd;
            say?.Invoke("healing a window left behind: " + Describe(window) + (ghost ? " (transparent)" : "") + (parked ? " (parked off-screen)" : "") + " " + RectText(window));
            Func<bool> fix = () =>
            {
                // still transparent while it is moved and minimised: nothing flashes on screen
                if (main)
                {
                    Unpark(window);
                    if (parked && AppleMusicKeeper.Instance is not { UserOwned: true })
                    {
                        ShowWindow(window, SwShowMinNoActive);
                    }
                }

                RestoreStyleNow(window);
                return true;
            };
            if (!Bounded(window, fix, 1500, "healing the window", say))
            {
                RetryRestore(window, fix, "healing the window", say);
            }

            say?.Invoke("healed: " + RectText(window));
        }
    }

    /// <summary>The person has used mouse or keyboard after <paramref name="tick"/> (an <see cref="Environment.TickCount64"/> value).</summary>
    internal static bool UserInputSince(long tick)
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            // cannot tell: not a reason to reject the sample
            return true;
        }

        return unchecked((int)(info.Time - (uint)tick)) > 0;
    }

    /// <summary>The rectangle (virtual screen) shows at least a little of the desktop.</summary>
    internal static bool OnAnyScreen(Rect r)
    {
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        return r.Right > left + 40 && r.Left < left + width - 40 && r.Bottom > top + 40 && r.Top < top + height - 40;
    }

    /// <summary>The window is on another virtual desktop (or otherwise cloaked by DWM).</summary>
    internal static bool IsCloaked(nint hwnd) => DwmGetWindowAttribute(hwnd, 14, out var cloaked, 4) == 0 && cloaked != 0;

    /// <summary>The window is fully transparent and click-through (how the driver works on Apple Music's windows), so nobody sees it.</summary>
    internal static bool IsGhost(nint hwnd)
    {
        var ex = (long)GetWindowLongPtr(hwnd, GwlExStyle);
        if ((ex & WsExLayered) == 0 || (ex & WsExTransparent) == 0)
        {
            return false;
        }

        return GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags) && (flags & LwaAlpha) != 0 && alpha == 0;
    }

    /// <summary>Whether the process is Apple Music (looked up by process name).</summary>
    internal static bool IsAppleMusic(uint pid)
    {
        if (pid == 0)
        {
            return false;
        }

        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName.Equals("AppleMusic", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Apple Music's main windows (not its popups).</summary>
    internal static List<nint> MainWindows()
    {
        var list = new List<nint>();
        EnumWindows((h, _) =>
        {
            if (ClassOf(h) == "WinUIDesktopWin32WindowClass" && IsAppleMusic(ProcessOf(h)))
            {
                list.Add(h);
            }

            return true;
        }, 0);
        return list;
    }

    /// <summary>
    /// The titled main window of an Apple Music process (class WinUIDesktopWin32WindowClass, title not empty and not the "WinUI Desktop" of the
    /// invisible helper window every launch creates). <paramref name="preferred"/> (the window the keeper hides) wins, then a visible window; among
    /// several (the mini player is a second titled window) the one with the biggest restore rectangle is the main window.
    /// </summary>
    internal static nint MainWindow(uint pid, nint preferred = 0)
    {
        nint best = 0;
        long bestScore = -1;
        foreach (var h in TopLevelWindows(pid))
        {
            if (ClassOf(h) != "WinUIDesktopWin32WindowClass")
            {
                continue;
            }

            var title = TitleOf(h);
            if (title.Length == 0 || title == "WinUI Desktop")
            {
                continue;
            }

            var wp = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            var area = GetWindowPlacement(h, ref wp) ? Math.Max(0L, (long)(wp.Normal.Right - wp.Normal.Left) * (wp.Normal.Bottom - wp.Normal.Top)) : 0L;
            var score = (h == preferred && preferred != 0 ? 1L << 60 : 0L) + (IsWindowVisible(h) ? 1L << 50 : 0L) + area;
            if (score > bestScore)
            {
                best = h;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>True when Apple Music has no window the user can see: none at all, or all minimised, hidden, parked off-screen or transparent.</summary>
    internal static bool MainWindowsHidden()
    {
        foreach (var h in MainWindows())
        {
            if (!IsWindowVisible(h) || IsIconic(h) || IsGhost(h))
            {
                continue;
            }

            GetWindowRect(h, out var rect);
            if (OnAnyScreen(rect))
            {
                return false;
            }
        }

        return true;
    }

    internal static string ClassOf(nint hwnd)
    {
        var s = new StringBuilder(256);
        GetClassNameW(hwnd, s, 256);
        return s.ToString();
    }

    internal static string TitleOf(nint hwnd)
    {
        var s = new StringBuilder(256);
        GetWindowTextW(hwnd, s, 256);
        return s.ToString();
    }

    internal static uint ProcessOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    internal static List<nint> TopLevelWindows(uint pid)
    {
        var list = new List<nint>();
        EnumWindows((h, _) =>
        {
            if (ProcessOf(h) == pid)
            {
                list.Add(h);
            }

            return true;
        }, 0);
        return list;
    }

    internal static string Describe(nint hwnd)
    {
        if (hwnd == 0)
        {
            return "(none)";
        }

        if (!IsWindow(hwnd))
        {
            return $"0x{hwnd:X} (gone)";
        }

        string name;
        try
        {
            name = System.Diagnostics.Process.GetProcessById((int)ProcessOf(hwnd)).ProcessName;
        }
        catch
        {
            name = "?";
        }

        return $"0x{hwnd:X} {name}/{ClassOf(hwnd)} '{TitleOf(hwnd)}'";
    }
}

/// <summary>A WinEvent hook (create / show) on its own message-loop thread; the callback runs on that thread.</summary>
internal sealed class AmEventHook : IDisposable
{
    private readonly AmNative.WinEventProc _proc;
    private readonly Thread _thread;
    private uint _threadId;

    public AmEventHook(uint minEvent, uint maxEvent, uint processId, Action<uint, nint> callback)
    {
        _proc = (_, ev, hwnd, idObject, _, _, _) =>
        {
            if (idObject != 0 || hwnd == 0)
            {
                return;
            }

            try
            {
                callback(ev, hwnd);
            }
            catch
            {
                // a vanished window must never break the hook thread
            }
        };
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = AmNative.GetCurrentThreadId();
            var hook = AmNative.SetWinEventHook(minEvent, maxEvent, 0, _proc, processId, 0, 0);
            ready.Set();
            while (AmNative.GetMessage(out _, 0, 0, 0) > 0)
            {
            }

            if (hook != 0)
            {
                AmNative.UnhookWinEvent(hook);
            }
        })
        {
            IsBackground = true,
            Name = "Trayloom AppleMusic hook",
        };
        _thread.Start();
        ready.Wait();
    }

    public void Dispose()
    {
        AmNative.PostThreadMessage(_threadId, AmNative.WmQuit, 0, 0);
        _thread.Join(1000);
    }
}

/// <summary>
/// Keeps Apple Music from taking the activation while Trayloom drives it in the background. A system-wide foreground WinEvent hook
/// (on its own message-loop thread) watches for any Apple Music window becoming the foreground window; when that happens while no
/// Apple Music window is visible to the user, the foreground goes back to the window the user was working in and Apple Music's main
/// window is minimised again without activation. Armed at the start of a job and kept for <see cref="GraceMs"/> after <see cref="Dispose"/>
/// (and never longer than the lifetime it was armed with). It stands down for good as soon as the person opens Apple Music themselves
/// (the foreground came from the Start menu, the taskbar, a jump list or Alt+Tab). No input is ever synthesised.
/// </summary>
internal sealed class ForegroundGuard : IDisposable
{
    internal const int GraceMs = 1500;

    private static readonly string[] ShellSurfaceClasses =
    [
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "MultitaskingViewFrame",
        "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow",
    ];

    private static readonly string[] ShellSurfaceProcesses = ["ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "SearchUI", "explorer"];

    /// <summary>The last foreground window that was not Apple Music's, when it took the foreground.</summary>
    private sealed record Sample(string Class, string Process, long Tick);

    private readonly Action<string> _say;
    private volatile bool _enforce;
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly object _gate = new();
    private readonly HashSet<nint> _visibleAtArm = [];
    private nint _target;
    private int _reclaims;
    private long _focusedAt;
    private long _actedAt;
    private volatile Sample? _lastNonAm;
    private AmEventHook? _hook;
    private Timer? _lifetime;

    private ForegroundGuard(nint previous, Action<string>? say, int maxMs)
    {
        _say = say ?? (_ => { });

        // When the user has Apple Music open on screen it is none of our business who has the foreground (and that window is never touched).
        foreach (var h in AmNative.MainWindows())
        {
            if (AmNative.IsWindowVisible(h) && !AmNative.IsIconic(h) && !AmNative.IsGhost(h) && AmNative.GetWindowRect(h, out var r) && AmNative.OnAnyScreen(r))
            {
                _visibleAtArm.Add(h);
            }
        }

        _enforce = AmNative.MainWindowsHidden();
        _target = PickTarget(previous);
        if (_enforce)
        {
            _hook = new AmEventHook(AmNative.EventSystemForeground, AmNative.EventSystemForeground, 0, (_, hwnd) => OnForeground(hwnd));
        }

        if (maxMs > 0)
        {
            // hard limit: the hook never outlives its job, whatever happens to the code that should dispose it
            _lifetime = new Timer(_ => DisposeNow(), null, maxMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Starts guarding. <paramref name="previous"/> is the foreground window of the moment (it is only used as the target when it is neither Trayloom's
    /// nor Apple Music's). The hook is dropped after <paramref name="maxMs"/> at the latest, even when <see cref="Dispose"/> is never called.
    /// </summary>
    public static ForegroundGuard Arm(nint previous, Action<string>? say = null, int maxMs = 60000) => new(previous, say, maxMs);

    /// <summary>Gives the foreground back now when Apple Music (or nothing) has it.</summary>
    public void Restore() => Reclaim("restore", false);

    public void Dispose()
    {
        var hook = Interlocked.Exchange(ref _hook, null);
        if (hook is not null)
        {
            _ = Task.Delay(GraceMs).ContinueWith(_ => hook.Dispose(), TaskScheduler.Default);
        }
    }

    /// <summary>Drops the hook now, without the grace period (the job it served was abandoned).</summary>
    public void DisposeNow()
    {
        Interlocked.Exchange(ref _lifetime, null)?.Dispose();
        _enforce = false;
        Interlocked.Exchange(ref _hook, null)?.Dispose();
    }

    private static string ProcessName(uint pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private static bool IsShellSurface(Sample s)
    {
        // the desktop itself (explorer's) is what Windows activates when a window goes away: not a way into Apple Music
        if (s.Class is "Progman" or "WorkerW")
        {
            return false;
        }

        if (Array.IndexOf(ShellSurfaceClasses, s.Class) >= 0 || s.Class.StartsWith("Shell_", StringComparison.Ordinal))
        {
            return true;
        }

        // File Explorer windows belong to explorer.exe too, but they are an application the person works in, not a way into Apple Music
        return Array.Exists(ShellSurfaceProcesses, n => n.Equals(s.Process, StringComparison.OrdinalIgnoreCase)) && s.Class is not ("CabinetWClass" or "ExploreWClass");
    }

    /// <summary>Every foreground change (runs on the hook thread): what had the foreground before Apple Music decides whether the person opened it.</summary>
    private void OnForeground(nint hwnd)
    {
        var pid = AmNative.ProcessOf(hwnd);
        if (pid == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (pid == _ownPid || !AmNative.IsAppleMusic(pid))
        {
            // The foreground Trayloom handed back itself says nothing about the person; nor does what Windows activates because of the
            // minimise that the reclaim posted (the posted minimise is carried out a moment later, with no user input anywhere near it).
            if (now - Interlocked.Read(ref _focusedAt) > 600 && now - Interlocked.Read(ref _actedAt) > 1500)
            {
                _lastNonAm = new Sample(AmNative.ClassOf(hwnd), pid == _ownPid ? "Trayloom" : ProcessName(pid), now);
            }

            return;
        }

        // a shell surface alone is not enough: the person has to have used mouse or keyboard since it took the foreground (clicked the tile)
        if (_lastNonAm is { } last && now - last.Tick < 5000 && IsShellSurface(last) && AmNative.UserInputSince(last.Tick))
        {
            var from = $"{last.Process}/{last.Class}";
            _say($"Apple Music took the foreground from {from}: opened by the user; the guard stands down");
            _enforce = false;
            AppleMusicKeeper.Instance?.MarkUserOwned("activated from " + from);
            return;
        }

        Reclaim("Apple Music took the foreground (" + AmNative.Describe(hwnd) + ")", true);
    }

    private bool Usable(nint hwnd) =>
        hwnd != 0 && AmNative.IsWindow(hwnd) && AmNative.IsWindowVisible(hwnd) && !AmNative.IsIconic(hwnd) && !AmNative.IsCloaked(hwnd)
        && AmNative.ProcessOf(hwnd) is var pid && pid != _ownPid && !AmNative.IsAppleMusic(pid);

    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow"];

    /// <summary>The window that should have the foreground: <paramref name="previous"/> when it is the user's own, else the top real application window.</summary>
    private nint PickTarget(nint previous)
    {
        if (Usable(previous))
        {
            return previous;
        }

        nint found = 0;
        AmNative.EnumWindows((h, _) =>
        {
            if (!Usable(h) || AmNative.GetWindow(h, AmNative.GwOwner) != 0 || AmNative.TitleOf(h).Length == 0)
            {
                return true;
            }

            var ex = (long)AmNative.GetWindowLongPtr(h, AmNative.GwlExStyle);
            if ((ex & (AmNative.WsExToolWindow | AmNative.WsExNoActivate)) != 0 || Array.IndexOf(ShellClasses, AmNative.ClassOf(h)) >= 0)
            {
                return true;
            }

            AmNative.GetWindowRect(h, out var r);
            if (r.Right - r.Left < 50 || r.Bottom - r.Top < 50)
            {
                return true;
            }

            found = h;
            return false;
        }, 0);
        return found;
    }

    private void Reclaim(string why, bool counted)
    {
        // the person opened Apple Music (or the keeper just showed it): nothing here may move it or the foreground
        if (!_enforce || AppleMusicKeeper.Instance is { UserOwned: true } or { JustShown: true })
        {
            return;
        }

        lock (_gate)
        {
            if (counted && ++_reclaims > 20)
            {
                return;
            }

            var now = AmNative.GetForegroundWindow();
            var fromAppleMusic = now != 0 && AmNative.IsAppleMusic(AmNative.ProcessOf(now));
            if (now != 0 && !fromAppleMusic)
            {
                return;
            }

            if (!Usable(_target))
            {
                _target = PickTarget(0);
            }

            if (_target != 0 && now != _target)
            {
                Interlocked.Exchange(ref _focusedAt, Environment.TickCount64);
                var ok = Focus(_target);
                Interlocked.Exchange(ref _focusedAt, Environment.TickCount64);
                Interlocked.Exchange(ref _actedAt, Environment.TickCount64);
                _say($"{why}; foreground back to {AmNative.Describe(_target)} => {ok}; foreground now {AmNative.Describe(AmNative.GetForegroundWindow())}");
            }

            foreach (var h in AmNative.MainWindows())
            {
                // a window that was on screen for the person when the job began is theirs: never touched
                if (AmNative.IsWindowVisible(h) && !AmNative.IsIconic(h) && !AmNative.IsGhost(h) && !_visibleAtArm.Contains(h))
                {
                    // posted, not sent: a stuck Apple Music must not be able to stall the thread that reclaims the foreground
                    Interlocked.Exchange(ref _actedAt, Environment.TickCount64);
                    if (AppleMusicKeeper.Instance?.IsKept(h) == true)
                    {
                        // a window the keeper hides has no taskbar button to go back to: it is hidden again
                        AmNative.ShowWindowAsync(h, AmNative.SwHide);
                        _say("Apple Music main window had been shown; hidden again");
                    }
                    else
                    {
                        AmNative.ShowWindowAsync(h, AmNative.SwShowMinNoActive);
                        _say("Apple Music main window had been restored; minimised again without activation");
                    }
                }
            }
        }
    }

    private static bool HasForeground(nint target)
    {
        var f = AmNative.GetForegroundWindow();
        return f != 0 && (f == target || AmNative.GetAncestor(f, AmNative.GaRoot) == target);
    }

    internal static bool Focus(nint target)
    {
        AmNative.SetForegroundWindow(target);
        if (HasForeground(target))
        {
            return true;
        }

        // Windows only lets the foreground owner (or a thread sharing its input queue) hand the activation on: join the owner's input queue for a moment.
        var fg = AmNative.GetForegroundWindow();
        var fgThread = fg != 0 ? AmNative.GetWindowThreadProcessId(fg, out _) : 0;
        var me = AmNative.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && AmNative.AttachThreadInput(me, fgThread, true);
        try
        {
            AmNative.SetForegroundWindow(target);
            AmNative.BringWindowToTop(target);
        }
        finally
        {
            if (attached)
            {
                AmNative.AttachThreadInput(me, fgThread, false);
            }
        }

        if (HasForeground(target))
        {
            return true;
        }

        AmNative.SwitchToThisWindow(target, true);
        return HasForeground(target);
    }
}
