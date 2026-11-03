// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;

namespace Trayloom.Core.Native;

/// <summary>
/// Gives foreign windows back to the person: visible again, opaque, on screen, minimised in the taskbar. Runs in the guardian process
/// (after Trayloom was killed) and at the start of Trayloom (after a crash). Every call into another process is bounded, so a hung
/// window cannot stall the rescue of the others.
/// </summary>
public static class WindowRescue
{
    private const string MainClass = "WinUIDesktopWin32WindowClass";
    private const string PopupClass = "Microsoft.UI.Content.PopupWindowSiteBridge";
    private const string HelperTitle = "WinUI Desktop";
    private const long GiveUpMs = 30000;

    /// <summary>One window and everything recorded about it.</summary>
    private sealed class Group(nint hwnd, List<TrackedWindow> records)
    {
        public nint Hwnd { get; } = hwnd;

        public List<TrackedWindow> Records { get; } = records;

        public TrackedWindow First => Records[0];

        public bool Has(string kind) => Records.Exists(r => r.Kind == kind);

        public long? ExStyle => Records.Find(r => r.Kind == WindowGuardianState.Ghost)?.ExStyle;

        public int[]? Home => Records.Find(r => r.Home is { Length: 4 })?.Home;
    }

    /// <summary>
    /// Repairs every tracked window, then (with <paramref name="heuristics"/>) any window of the same processes that is plainly left in a
    /// bad state although nothing records it. Returns the records that could not be resolved (empty = all well).
    /// </summary>
    public static IReadOnlyList<TrackedWindow> RescueAll(GuardianSnapshot? state, ILog log, bool heuristics)
    {
        var records = state?.Windows ?? [];
        using var mutex = AcquireMutex(log);
        var unresolved = new List<TrackedWindow>();
        try
        {
            var groups = records.GroupBy(r => r.Hwnd).Select(g => new Group((nint)g.Key, [.. g])).ToList();
            var threads = new List<Thread>();
            var open = new List<Group>();
            foreach (var group in groups)
            {
                var g = group;
                var thread = new Thread(() =>
                {
                    var ok = false;
                    try
                    {
                        ok = RescueWindow(g, log);
                    }
                    catch (Exception ex)
                    {
                        log.Error("rescue of " + Hex(g.Hwnd) + " failed", ex);
                    }

                    if (!ok)
                    {
                        lock (open)
                        {
                            open.Add(g);
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "Trayloom window rescue",
                };
                thread.Start();
                threads.Add(thread);
            }

            foreach (var thread in threads)
            {
                thread.Join();
            }

            foreach (var g in open)
            {
                unresolved.AddRange(g.Records);
            }

            if (heuristics)
            {
                try
                {
                    HeuristicPass(state, log);
                }
                catch (Exception ex)
                {
                    log.Error("heuristic rescue failed", ex);
                }
            }
        }
        finally
        {
            try
            {
                mutex?.ReleaseMutex();
            }
            catch
            {
                // not owned
            }
        }

        return unresolved;
    }

    private static Mutex? AcquireMutex(ILog log)
    {
        try
        {
            var mutex = new Mutex(false, @"Local\Trayloom.WindowRescue");
            try
            {
                if (!mutex.WaitOne(10000))
                {
                    log.Warn("another rescue holds the window rescue lock for 10 s; going on anyway");
                }
            }
            catch (AbandonedMutexException)
            {
                // the other rescue died; the lock is ours
            }

            return mutex;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- one recorded window

    private static bool RescueWindow(Group g, ILog log)
    {
        var hwnd = g.Hwnd;
        var first = g.First;
        var kinds = string.Join('+', g.Records.Select(r => r.Kind));
        var deadline = Environment.TickCount64 + GiveUpMs;
        var logged = false;
        Thread? inflight = null;
        while (true)
        {
            if (!GuardianNative.IsWindow(hwnd))
            {
                log.Info($"rescue {Hex(hwnd)} {first.Process} [{kinds}]: window is gone");
                return true;
            }

            if (IdentityChanged(g))
            {
                log.Info($"rescue {Hex(hwnd)} {first.Process} [{kinds}]: handle now belongs to another window ({Describe(hwnd)}); dropped");
                return true;
            }

            if (!logged)
            {
                logged = true;
                log.Info($"rescue {Hex(hwnd)} {first.Process}/{first.Class} [{kinds}]: before {Describe(hwnd)}");
            }

            if (inflight is not { IsAlive: true } && Responsive(hwnd))
            {
                var verified = false;
                var done = GuardianNative.RunBounded(() => verified = Attempt(g, log), 3000, out var thread);
                if (!done)
                {
                    inflight = thread;
                    log.Warn($"rescue {Hex(hwnd)}: the window did not answer in 3 s; trying again");
                }
                else if (verified)
                {
                    log.Info($"rescue {Hex(hwnd)} {first.Process} [{kinds}]: after  {Describe(hwnd)}");
                    return true;
                }
            }

            if (Environment.TickCount64 > deadline)
            {
                log.Warn($"rescue {Hex(hwnd)} {first.Process} [{kinds}]: gave up after 30 s; {Describe(hwnd)}");
                return false;
            }

            Thread.Sleep(500);
        }
    }

    private static bool IdentityChanged(Group g)
    {
        var first = g.First;
        if (GuardianNative.ProcessOf(g.Hwnd) != first.Pid)
        {
            return true;
        }

        if (!string.Equals(GuardianNative.ClassOf(g.Hwnd), first.Class, StringComparison.Ordinal))
        {
            return true;
        }

        var name = GuardianNative.ProcessNameOf(first.Pid);
        return name.Length > 0 && !string.Equals(name, first.Process, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Responsive(nint hwnd)
    {
        var answered = false;
        var done = GuardianNative.RunBounded(() => answered = GuardianNative.SendMessageTimeoutW(hwnd, 0, 0, 0, GuardianNative.SmtoAbortIfHung, 2000, out _) != 0, 3000, out _);
        return done && answered;
    }

    /// <summary>One repair of one window, in the spec's order; true = the window is now verified well. Runs on its own thread.</summary>
    private static bool Attempt(Group g, ILog log)
    {
        var hwnd = g.Hwnd;
        var monitors = WindowPlacementMath.GetMonitors();
        var cls = GuardianNative.ClassOf(hwnd);

        // the class alone does not make a main window: Apple Music's invisible "WinUI Desktop" helper has it too and must never be shown
        var isMain = cls == MainClass && IsTitled(hwnd);

        // 1. transparent: its own style back
        var wasIconic = GuardianNative.IsIconic(hwnd);
        var ex = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE);
        if (IsGhost(hwnd) || g.Has(WindowGuardianState.Ghost))
        {
            var wanted = g.ExStyle ?? Deghost(ex, cls);
            if (wanted != ex)
            {
                NativeMethods.SetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE, (nint)wanted);
            }

            if (((long)NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE) & NativeConstants.WS_EX_LAYERED) != 0)
            {
                GuardianNative.SetLayeredWindowAttributes(hwnd, 0, 255, GuardianNative.LwaAlpha);
            }
        }

        if (!isMain)
        {
            return GuardianNative.IsWindow(hwnd) && !IsGhost(hwnd);
        }

        // changing the tool window style of a minimised window can restore it: a window that was in the taskbar stays there
        if (wasIconic && GuardianNative.IsWindowVisible(hwnd) && !GuardianNative.IsIconic(hwnd))
        {
            GuardianNative.ShowWindow(hwnd, GuardianNative.SwShowMinNoActive);
        }

        // 2. where it should be
        var visible = GuardianNative.IsWindowVisible(hwnd);
        var iconic = GuardianNative.IsIconic(hwnd);
        var current = CurrentScreenRect(hwnd, iconic, monitors);
        var target = Target(g, current, monitors);

        // 3. visible, minimised, on screen
        var parked = !iconic && current.Left <= -10000;

        // a maximised window fills its monitor (its rectangle reaches past the edges by its frame): it is the person's, never moved or resized
        var zoomed = !iconic && !parked && GuardianNative.IsZoomed(hwnd);
        if (!visible || parked)
        {
            if (!iconic)
            {
                if (zoomed)
                {
                    ShowMinimisedKeepingZoom(hwnd);
                }
                else
                {
                    NativeMethods.SetWindowPos(hwnd, 0, target.Left, target.Top, target.Width, target.Height, GuardianNative.SwpNoZOrder | GuardianNative.SwpNoActivate | GuardianNative.SwpNoOwnerZOrder);
                    GuardianNative.ShowWindow(hwnd, GuardianNative.SwShowMinNoActive);
                }
            }
            else
            {
                FixRestorePosition(hwnd, current, target, monitors);
                GuardianNative.ShowWindow(hwnd, GuardianNative.SwShowMinNoActive);
            }
        }
        else if (!iconic && !zoomed && !WindowPlacementMath.IsComfortablyVisible(current, monitors))
        {
            NativeMethods.SetWindowPos(hwnd, 0, target.Left, target.Top, target.Width, target.Height, GuardianNative.SwpNoZOrder | GuardianNative.SwpNoActivate | GuardianNative.SwpNoOwnerZOrder | GuardianNative.SwpAsyncWindowPos);
        }
        else if (iconic && !WindowPlacementMath.IsComfortablyVisible(current, monitors))
        {
            FixRestorePosition(hwnd, current, target, monitors);
        }

        // 4. verify
        if (!GuardianNative.IsWindow(hwnd) || !GuardianNative.IsWindowVisible(hwnd) || IsGhost(hwnd))
        {
            return false;
        }

        if (GuardianNative.IsIconic(hwnd))
        {
            return true;
        }

        NativeMethods.GetWindowRect(hwnd, out var after);
        return (GuardianNative.IsZoomed(hwnd) && after.Left > -10000) || WindowPlacementMath.IsComfortablyVisible(after, monitors);
    }

    /// <summary>The first place that is comfortably visible: where the person had it, where it is, the middle of the primary screen.</summary>
    private static RECT Target(Group g, RECT current, IReadOnlyList<MonitorBounds> monitors)
    {
        RECT? home = g.Home is { Length: 4 } h ? new RECT { Left = h[0], Top = h[1], Right = h[2], Bottom = h[3] } : null;
        var centred = WindowPlacementMath.Centered(monitors);
        foreach (var candidate in new RECT?[] { home, current, centred })
        {
            if (candidate is { } c && WindowPlacementMath.IsComfortablyVisible(c, monitors))
            {
                return c;
            }
        }

        return WindowPlacementMath.Sanitize(home ?? current, monitors);
    }

    /// <summary>The rectangle in screen coordinates: the window's own, or for a minimised one the restore rectangle (workspace coordinates).</summary>
    private static RECT CurrentScreenRect(nint hwnd, bool iconic, IReadOnlyList<MonitorBounds> monitors)
    {
        if (!iconic)
        {
            NativeMethods.GetWindowRect(hwnd, out var r);
            return r;
        }

        var wp = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GuardianNative.GetWindowPlacement(hwnd, ref wp))
        {
            return default;
        }

        var tool = ((long)NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE) & NativeConstants.WS_EX_TOOLWINDOW) != 0;
        return tool ? wp.rcNormalPosition : WindowPlacementMath.WorkspaceToScreen(wp.rcNormalPosition, monitors);
    }

    /// <summary>
    /// Shows a hidden maximised window minimised so that it comes back maximised (a plain SW_SHOWMINNOACTIVE on a hidden window forgets
    /// that it was maximised).
    /// </summary>
    private static void ShowMinimisedKeepingZoom(nint hwnd)
    {
        var wp = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GuardianNative.GetWindowPlacement(hwnd, ref wp))
        {
            GuardianNative.ShowWindow(hwnd, GuardianNative.SwShowMinNoActive);
            return;
        }

        wp.showCmd = GuardianNative.SwShowMinNoActive;
        wp.flags |= GuardianNative.WpfRestoreToMaximized | GuardianNative.WpfAsyncWindowPlacement;
        GuardianNative.SetWindowPlacement(hwnd, ref wp);
    }

    /// <summary>A minimised window comes back at its restore rectangle: when that is off-screen it is corrected without restoring the window.</summary>
    private static void FixRestorePosition(nint hwnd, RECT current, RECT target, IReadOnlyList<MonitorBounds> monitors)
    {
        if (WindowPlacementMath.IsComfortablyVisible(current, monitors))
        {
            return;
        }

        var wp = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GuardianNative.GetWindowPlacement(hwnd, ref wp))
        {
            return;
        }

        var tool = ((long)NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE) & NativeConstants.WS_EX_TOOLWINDOW) != 0;
        wp.rcNormalPosition = tool ? target : WindowPlacementMath.ScreenToWorkspace(target, monitors);
        wp.showCmd = GuardianNative.SwShowMinNoActive;
        wp.flags = GuardianNative.WpfAsyncWindowPlacement;
        GuardianNative.SetWindowPlacement(hwnd, ref wp);
    }

    // ---------------------------------------------------------------- without a record

    /// <summary>
    /// Windows of Apple Music (and of the processes named in the records) that are plainly left in a bad state although nothing says so:
    /// transparent ones are made opaque, a visible main window parked off-screen is brought back and minimised, and a hidden main window
    /// is shown minimised when the record was lost or says it was kept and the process has no main window the person can see.
    /// </summary>
    private static void HeuristicPass(GuardianSnapshot? state, ILog log)
    {
        // TRAYLOOM_GUARDIAN_PROCESSES (comma separated) replaces the default "AppleMusic": tests use it so that they never look at a real Apple Music
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Environment.GetEnvironmentVariable("TRAYLOOM_GUARDIAN_PROCESSES") is { Length: > 0 } custom)
        {
            foreach (var name in custom.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                names.Add(name);
            }
        }
        else
        {
            names.Add("AppleMusic");
        }

        foreach (var r in state?.Windows ?? [])
        {
            if (r.Process.Length > 0)
            {
                names.Add(r.Process);
            }
        }

        var cache = new Dictionary<uint, string>();
        var windows = new List<(nint Hwnd, uint Pid, string Class)>();
        GuardianNative.EnumWindows((h, _) =>
        {
            var cls = GuardianNative.ClassOf(h);
            if (cls is MainClass or PopupClass)
            {
                var pid = GuardianNative.ProcessOf(h);
                if (!cache.TryGetValue(pid, out var name))
                {
                    cache[pid] = name = GuardianNative.ProcessNameOf(pid);
                }

                if (names.Contains(name))
                {
                    windows.Add((h, pid, cls));
                }
            }

            return true;
        }, 0);

        var monitors = WindowPlacementMath.GetMonitors();
        var stateMissing = state is null;
        foreach (var byPid in windows.GroupBy(w => w.Pid))
        {
            var pid = byPid.Key;
            var titled = byPid.Where(w => w.Class == MainClass && IsTitled(w.Hwnd)).Select(w => w.Hwnd).ToList();
            var anyVisible = titled.Exists(GuardianNative.IsWindowVisible);
            var listedKept = state?.Windows.Any(r => r.Pid == pid && r.Kind == WindowGuardianState.Kept) == true;
            foreach (var (hwnd, _, cls) in byPid)
            {
                var ghost = IsGhost(hwnd);
                var isTitledMain = titled.Contains(hwnd);
                var visible = GuardianNative.IsWindowVisible(hwnd);
                var iconic = GuardianNative.IsIconic(hwnd);
                var parked = isTitledMain && visible && !iconic && NativeMethods.GetWindowRect(hwnd, out var rect) && rect.Left <= -10000;
                var hiddenMain = isTitledMain && !visible && !anyVisible && (stateMissing || listedKept);
                if (!ghost && !parked && !hiddenMain)
                {
                    continue;
                }

                var why = (ghost ? "transparent " : "") + (parked ? "parked " : "") + (hiddenMain ? "hidden " : "");
                var window = hwnd;
                var windowClass = cls;
                log.Info($"heuristic rescue {Hex(window)} {why.Trim()}: before {Describe(window)}");
                if (!Responsive(window))
                {
                    log.Warn($"heuristic rescue {Hex(window)}: the window does not answer; left as it is");
                    continue;
                }

                GuardianNative.RunBounded(() =>
                {
                    if (ghost)
                    {
                        var ex = (long)NativeMethods.GetWindowLongPtr(window, NativeConstants.GWL_EXSTYLE);
                        NativeMethods.SetWindowLongPtr(window, NativeConstants.GWL_EXSTYLE, (nint)Deghost(ex, windowClass));
                        if (((long)NativeMethods.GetWindowLongPtr(window, NativeConstants.GWL_EXSTYLE) & NativeConstants.WS_EX_LAYERED) != 0)
                        {
                            GuardianNative.SetLayeredWindowAttributes(window, 0, 255, GuardianNative.LwaAlpha);
                        }
                    }

                    if (parked || hiddenMain)
                    {
                        var iconicNow = GuardianNative.IsIconic(window);
                        var current = CurrentScreenRect(window, iconicNow, monitors);
                        var target = WindowPlacementMath.Sanitize(current, monitors);
                        if (!iconicNow && GuardianNative.IsZoomed(window) && current.Left > -10000)
                        {
                            // a hidden maximised window: not moved, shown minimised (it comes back maximised)
                            ShowMinimisedKeepingZoom(window);
                            return;
                        }

                        if (!iconicNow)
                        {
                            NativeMethods.SetWindowPos(window, 0, target.Left, target.Top, target.Width, target.Height, GuardianNative.SwpNoZOrder | GuardianNative.SwpNoActivate | GuardianNative.SwpNoOwnerZOrder);
                        }
                        else
                        {
                            FixRestorePosition(window, current, target, monitors);
                        }

                        GuardianNative.ShowWindow(window, GuardianNative.SwShowMinNoActive);
                    }
                }, 3000, out _);
                log.Info($"heuristic rescue {Hex(window)}: after  {Describe(window)}");
            }
        }
    }

    private static bool IsTitled(nint hwnd)
    {
        var title = GuardianNative.TitleOf(hwnd);
        return title.Length > 0 && title != HelperTitle;
    }

    // ---------------------------------------------------------------- helpers

    private static bool IsGhost(nint hwnd)
    {
        var ex = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE);
        if ((ex & NativeConstants.WS_EX_LAYERED) == 0 || (ex & NativeConstants.WS_EX_TRANSPARENT) == 0)
        {
            return false;
        }

        return GuardianNative.GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags) && (flags & GuardianNative.LwaAlpha) != 0 && alpha == 0;
    }

    /// <summary>The style of a ghost without the ghost: the best guess when nobody recorded the real one.</summary>
    private static long Deghost(long ex, string cls)
    {
        ex &= ~(NativeConstants.WS_EX_LAYERED | NativeConstants.WS_EX_TRANSPARENT);
        if (cls == MainClass)
        {
            ex &= ~NativeConstants.WS_EX_TOOLWINDOW;
        }

        return ex;
    }

    private static string Hex(nint hwnd) => "0x" + hwnd.ToString("X", CultureInfo.InvariantCulture);

    /// <summary>One line about a window: whether it is there, visible, minimised, transparent, its rectangle and extended style.</summary>
    internal static string Describe(nint hwnd)
    {
        if (!GuardianNative.IsWindow(hwnd))
        {
            return "gone";
        }

        NativeMethods.GetWindowRect(hwnd, out var r);
        var ex = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE);
        return string.Create(CultureInfo.InvariantCulture, $"visible={(GuardianNative.IsWindowVisible(hwnd) ? 1 : 0)} iconic={(GuardianNative.IsIconic(hwnd) ? 1 : 0)} ghost={(IsGhost(hwnd) ? 1 : 0)} rect=[{r.Left},{r.Top},{r.Right},{r.Bottom}] ex=0x{ex:X}");
    }
}
