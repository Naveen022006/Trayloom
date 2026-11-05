// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Features.WindowTools.AwayMode;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// The switch engine (spec 08 section 2.4). One <see cref="Execute"/> call runs on the "Modes switch" thread, which is in a
/// single-threaded apartment because shell COM is used. It puts the previous mode away with the same unsaved-work safety as Away mode
/// (a window that asks a question is left for the person to answer; nothing is forced, killed or answered), opens or focuses each item
/// of the new mode without duplicates, places the windows in their zones and brings the front item forward. It never synthesizes input.
/// </summary>
internal sealed class ModesSwitcher
{
    public const int AppBudgetMs = 15000;
    public const int WebBudgetMs = 10000;
    public const int PathBudgetMs = 8000;
    public const int CloseTimeoutMs = 10000;

    private const int QuietAfterFirstMs = 1500;
    private const int PollMs = 100;
    private const int PromptStableMs = 600;
    private const int CloseSliceMs = 200;
    private const int ArgumentBudget = 24000;
    private const uint WmClose = 0x0010;

    private readonly IAppSettings _app;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly ModesTracking _tracking;
    private readonly Action<Mode?, Mode?> _onUiThread;
    private readonly ManualResetEventSlim _cancel = new(false);

    /// <param name="app">Flat settings, for the window gaps of the placement engine.</param>
    /// <param name="log">Application log; only ids and counts are written.</param>
    /// <param name="ui">The UI dispatcher.</param>
    /// <param name="tracking">The windows the modes own.</param>
    /// <param name="onUiThread">Runs on the UI thread after the previous mode is put away: keep awake and the island timer of the entered mode.</param>
    public ModesSwitcher(IAppSettings app, ILog log, Dispatcher ui, ModesTracking tracking, Action<Mode?, Mode?> onUiThread)
    {
        _app = app;
        _log = log;
        _ui = ui;
        _tracking = tracking;
        _onUiThread = onUiThread;
    }

    /// <summary>Asks the running switch to stop at its next step (Trayloom is quitting or Modes is being uninstalled). Nothing already started is undone.</summary>
    public void Cancel() => _cancel.Set();

    /// <summary>What a launch needs to find its window afterwards.</summary>
    private sealed record ClaimRule(string? Exe, string? Aumid, bool Chromium, bool ForegroundOk, int BudgetMs, string? TitleHint);

    private sealed record Launched(bool Ok, string? Reason, ClaimRule? Rule);

    private enum ItemResult
    {
        Opened,
        Focused,
        Failed,
    }

    // ------------------------------------------------------------------------------------------ the switch

    /// <summary>
    /// Leaves <paramref name="previous"/> (when there is one and it is not the target) and enters <paramref name="target"/> (null = only
    /// leave). Blocking; call on a thread in a single-threaded apartment.
    /// </summary>
    public ModeSwitchSummary Execute(Mode? previous, Mode? target)
    {
        var clock = Stopwatch.StartNew();
        var prompts = 0;
        if (previous is not null && (target is null || previous.Id != target.Id))
        {
            prompts = PutAway(previous, target);
        }

        if (target is null)
        {
            return new ModeSwitchSummary(previous?.Id ?? string.Empty, 0, 0, 0, [], clock.Elapsed.TotalSeconds, prompts);
        }

        _ui.BeginInvoke(() => _onUiThread(previous, target));
        if (target.OnEnter.Volume >= 0)
        {
            ModeVolume.Set(target.OnEnter.Volume / 100f);
        }

        WindowTools.Keys.Register(_app);
        var engine = new WindowLayoutEngine(_app, _log, _ui);
        var claimed = new HashSet<nint>();
        var problems = new List<(string, string)>();
        int opened = 0, focused = 0, failed = 0;
        foreach (var item in target.Items)
        {
            if (_cancel.IsSet)
            {
                break;
            }

            ItemResult result;
            string? reason = null;
            try
            {
                result = OpenItem(target, item, engine, claimed, out reason);
            }
            catch (Exception ex)
            {
                _log.Warn($"[modes] item failed ({ex.GetType().Name})");
                result = ItemResult.Failed;
                reason = ModesStrings.ReasonFailed;
            }

            switch (result)
            {
                case ItemResult.Opened:
                    opened++;
                    break;
                case ItemResult.Focused:
                    focused++;
                    break;
                default:
                    failed++;
                    problems.Add((item.Name, reason ?? ModesStrings.ReasonFailed));
                    break;
            }
        }

        // Only the front item is activated; every other window was placed or restored without taking the focus.
        if (!_cancel.IsSet && target.Items.FirstOrDefault(i => i.Front) is { } front)
        {
            var window = _tracking.Live(target.Id, front.Id).Select(t => t.Hwnd).FirstOrDefault();
            if (window != 0)
            {
                WinUtil.Activate(window);
            }
        }

        return new ModeSwitchSummary(target.Id, opened, focused, failed, problems, clock.Elapsed.TotalSeconds, prompts);
    }

    // ------------------------------------------------------------------------------------------ putting away

    /// <summary>
    /// Applies the previous mode's "when I leave" choice to the items the new mode does not also have. Returns how many windows asked a
    /// question and were left open for the person to answer.
    /// </summary>
    private int PutAway(Mode previous, Mode? target)
    {
        if (previous.OnLeave == "keep")
        {
            return 0;
        }

        var toMinimize = new List<nint>();
        var toClose = new List<nint>();
        foreach (var item in ModesMath.ToPutAway(previous, target))
        {
            var live = _tracking.Live(previous.Id, item.Id);
            if (live.Count == 0)
            {
                // After a restart nothing is tracked: the windows of the item's program are only ever minimized, never closed.
                if (item.Kind == ModeItemKind.App && !string.IsNullOrEmpty(item.Exe))
                {
                    toMinimize.AddRange(ModesWindows.WindowsOfExe(item.Exe));
                }

                continue;
            }

            foreach (var tracked in live)
            {
                if (previous.OnLeave == "close" && tracked.OpenedByMode && MayClose(tracked.Hwnd))
                {
                    toClose.Add(tracked.Hwnd);
                }
                else
                {
                    toMinimize.Add(tracked.Hwnd);
                }
            }
        }

        foreach (var hwnd in toMinimize.Distinct())
        {
            ModesWindows.Minimize(hwnd);
        }

        var prompts = toClose.Count > 0 ? CloseGently(toClose) : 0;
        _tracking.Prune();
        return prompts;
    }

    /// <summary>The shell, consoles and terminals, and Trayloom itself are never closed.</summary>
    private static bool MayClose(nint hwnd) =>
        !AwayApps.NeverClosable(AppIdentity.OwnerOf(hwnd).Path) && !AwayApps.NeverClosableClass(WinApi.GetClass(hwnd));

    /// <summary>
    /// Posts <c>WM_CLOSE</c> to each window, the bottom of the Z order first, and watches for up to 10 s. A window that opens a question
    /// (a new visible window it owns, a standard dialog of its process, or staying disabled) is a prompt: it is left alone for the
    /// person, and nothing answers it. A window that is still there after the time is minimized instead. Nothing is ever forced.
    /// </summary>
    private int CloseGently(List<nint> windows)
    {
        var zOrder = AwayProcess.TopLevelWindows();
        var before = zOrder.ToHashSet();
        var pending = windows.Where(WinApi.IsWindow).OrderByDescending(h => zOrder.IndexOf(h)).ToList();
        foreach (var hwnd in pending)
        {
            WinApi.PostMessageW(hwnd, WmClose, 0, 0);
        }

        var disabledSince = new Dictionary<nint, long>();
        var prompts = 0;
        var started = Environment.TickCount64;
        while (pending.Count > 0 && Environment.TickCount64 - started < CloseTimeoutMs && !_cancel.Wait(CloseSliceMs))
        {
            var all = AwayProcess.TopLevelWindows();
            var now = Environment.TickCount64;
            foreach (var hwnd in pending.ToList())
            {
                if (!WinApi.IsWindow(hwnd))
                {
                    pending.Remove(hwnd);
                }
                else if (AsksSomething(hwnd, all, before, disabledSince, now))
                {
                    pending.Remove(hwnd);
                    prompts++;
                }
            }
        }

        foreach (var hwnd in pending)
        {
            ModesWindows.Minimize(hwnd);
        }

        return prompts;
    }

    private static bool AsksSomething(nint hwnd, List<nint> all, HashSet<nint> before, Dictionary<nint, long> disabledSince, long now)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        foreach (var other in all)
        {
            if (before.Contains(other) || !WinApi.IsWindowVisible(other))
            {
                continue;
            }

            if (AwayProcess.OwnerWindow(other) == hwnd)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(other, out var otherPid);
            if (otherPid == pid && WinApi.GetClass(other) == "#32770")
            {
                return true;
            }
        }

        if (WinApi.IsWindowEnabled(hwnd))
        {
            disabledSince.Remove(hwnd);
            return false;
        }

        if (!disabledSince.TryGetValue(hwnd, out var since))
        {
            disabledSince[hwnd] = now;
            return false;
        }

        return now - since >= PromptStableMs;
    }

    // ------------------------------------------------------------------------------------------ opening an item

    private ItemResult OpenItem(Mode mode, ModeItem item, WindowLayoutEngine engine, HashSet<nint> claimed, out string? reason)
    {
        reason = null;

        // 1. Something this mode already opened or adopted: bring it back quietly and place it again.
        var live = _tracking.Live(mode.Id, item.Id);
        if (live.Count > 0)
        {
            foreach (var tracked in live)
            {
                Present(tracked.Hwnd, item, engine, claimed);
            }

            return ItemResult.Focused;
        }

        // 2. Something open that this mode did not start (the person's own window, or one from before a restart): adopt the top one.
        if (FindOpen(item) is { } found)
        {
            _tracking.Add(mode.Id, item.Id, ModesTracking.Describe(found, openedByMode: false));
            Present(found, item, engine, claimed);
            return ItemResult.Focused;
        }

        // 3. Start it, then find the window it made.
        var before = AwayProcess.TopLevelWindows().ToHashSet();
        var foregroundBefore = NativeMethods.GetForegroundWindow();
        var launch = Launch(item);
        if (!launch.Ok)
        {
            reason = launch.Reason;
            return ItemResult.Failed;
        }

        if (launch.Rule is null)
        {
            // Handed to the shell (the default browser, a file): there is no window to follow, so nothing is tracked.
            return ItemResult.Opened;
        }

        var window = Claim(launch.Rule, before, claimed, foregroundBefore, out var viaForeground);
        if (window == 0)
        {
            reason = ModesStrings.ReasonNoWindow;
            return ItemResult.Failed;
        }

        _tracking.Add(mode.Id, item.Id, ModesTracking.Describe(window, openedByMode: !viaForeground));
        Present(window, item, engine, claimed);
        return viaForeground ? ItemResult.Focused : ItemResult.Opened;
    }

    private void Present(nint hwnd, ModeItem item, WindowLayoutEngine engine, HashSet<nint> claimed)
    {
        claimed.Add(hwnd);
        ModesWindows.RestoreQuietly(hwnd);
        try
        {
            ModesWindows.ApplyZone(engine, hwnd, item.Zone);
        }
        catch (Exception ex)
        {
            _log.Warn($"[modes] zone failed ({ex.GetType().Name})");
        }
    }

    /// <summary>The topmost window that already shows this item and that the mode did not start, or 0.</summary>
    private static nint FindOpen(ModeItem item)
    {
        switch (item.Kind)
        {
            case ModeItemKind.App:
                // A launcher that forwards to the running copy (VS Code and its relatives) is simply started again: it focuses or opens the folder itself.
                return ModesMath.IsForwarder(item.Exe) ? 0 : ModesWindows.FindAppWindows(item).FirstOrDefault();
            case ModeItemKind.Path:
                if (!string.IsNullOrEmpty(item.Path) && Directory.Exists(item.Path))
                {
                    return ModesWindows.ExplorerFolders().Where(f => ModesWindows.SameFolder(f.Folder, item.Path)).Select(f => f.Hwnd).FirstOrDefault();
                }

                return 0;
            default:
                // Which tabs a browser window holds cannot be known for windows Trayloom did not open itself.
                return 0;
        }
    }

    // ------------------------------------------------------------------------------------------ launching

    private Launched Launch(ModeItem item)
    {
        try
        {
            return item.Kind switch
            {
                ModeItemKind.App => LaunchApp(item),
                ModeItemKind.Web => LaunchWeb(item),
                _ => LaunchPath(item),
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
        {
            return new Launched(false, ModesStrings.ReasonAdmin, null);
        }
        catch (Exception ex)
        {
            _log.Warn($"[modes] launch failed ({ex.GetType().Name} 0x{ex.HResult:X8})");
            return new Launched(false, ModesStrings.ReasonFailed, null);
        }
    }

    private static ProcessStartInfo Plain(string exe, string? workingDirectory = null)
    {
        // CreateProcess, never the shell: an app that needs administrator fails here instead of being started elevated.
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = !string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory) ? workingDirectory : Path.GetDirectoryName(exe) ?? string.Empty,
        };
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("TRAYLOOM_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            info.Environment.Remove(key);
        }

        return info;
    }

    private static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private Launched LaunchApp(ModeItem item)
    {
        if (!string.IsNullOrEmpty(item.Aumid))
        {
            var packaged = Plain(Explorer);
            packaged.ArgumentList.Add("shell:AppsFolder\\" + item.Aumid);
            using (Process.Start(packaged))
            {
            }

            return new Launched(true, null, new ClaimRule(null, item.Aumid, false, true, AppBudgetMs, null));
        }

        var exe = item.Exe ?? string.Empty;
        if (!File.Exists(exe))
        {
            return new Launched(false, ModesStrings.ReasonMissing, null);
        }

        var info = Plain(exe, item.Dir);
        info.Arguments = item.Args;
        using (Process.Start(info))
        {
        }

        return new Launched(true, null, new ClaimRule(exe.ToLowerInvariant(), null, false, true, AppBudgetMs, FolderHint(item.Args)));
    }

    /// <summary>The last folder name in an argument string; a window title that contains it belongs to a launcher that forwarded the request.</summary>
    private static string? FolderHint(string args)
    {
        var first = args.IndexOf('"');
        var last = args.LastIndexOf('"');
        if (first < 0 || last <= first)
        {
            return null;
        }

        var name = Path.GetFileName(args[(first + 1)..last].TrimEnd('\\', '/'));
        return name.Length > 0 ? name : null;
    }

    private Launched LaunchWeb(ModeItem item)
    {
        var urls = item.Urls.Select(ModesMath.NormalizeUrl).Where(u => u is not null).Select(u => u!).ToList();
        if (urls.Count == 0)
        {
            return new Launched(false, ModesStrings.ReasonFailed, null);
        }

        var exe = ModesBrowsers.Resolve(item.Browser);
        if (exe is null)
        {
            // The default browser is not an ordinary program: the shell opens each address.
            foreach (var url in urls)
            {
                using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }))
                {
                }

                if (_cancel.Wait(300))
                {
                    break;
                }
            }

            return new Launched(true, null, null);
        }

        if (ModesBrowsers.IsChromium(exe))
        {
            // One launch gives one new window with all the tabs. Very long lists are sent in pieces that join the window just opened.
            var first = true;
            foreach (var chunk in Chunks(urls))
            {
                var info = Plain(exe);
                foreach (var arg in first ? ModesMath.ChromiumArgs(item.Profile, chunk) : ChunkArgs(item.Profile, chunk))
                {
                    info.ArgumentList.Add(arg);
                }

                using (Process.Start(info))
                {
                }

                if (!first && _cancel.Wait(300))
                {
                    break;
                }

                first = false;
            }

            return new Launched(true, null, new ClaimRule(exe, null, true, false, WebBudgetMs, null));
        }

        if (ModesBrowsers.IsFirefox(exe))
        {
            // Best effort: the first address opens a window, the others join it as tabs, a moment apart.
            for (var i = 0; i < urls.Count; i++)
            {
                var info = Plain(exe);
                info.ArgumentList.Add(i == 0 ? "-new-window" : "-new-tab");
                info.ArgumentList.Add(urls[i]);
                using (Process.Start(info))
                {
                }

                if (i < urls.Count - 1 && _cancel.Wait(400))
                {
                    break;
                }
            }

            return new Launched(true, null, new ClaimRule(exe, null, false, false, WebBudgetMs, null));
        }

        // Another browser: one address per launch, a moment apart.
        for (var i = 0; i < urls.Count; i++)
        {
            var info = Plain(exe);
            info.ArgumentList.Add(urls[i]);
            using (Process.Start(info))
            {
            }

            if (i < urls.Count - 1 && _cancel.Wait(300))
            {
                break;
            }
        }

        return new Launched(true, null, new ClaimRule(exe, null, false, false, WebBudgetMs, null));
    }

    private static IReadOnlyList<string> ChunkArgs(string? profile, IReadOnlyList<string> urls)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(profile))
        {
            args.Add("--profile-directory=" + profile);
        }

        args.AddRange(urls);
        return args;
    }

    /// <summary>Splits addresses so one command line stays far below the 32 767 character limit of CreateProcess.</summary>
    private static IEnumerable<List<string>> Chunks(List<string> urls)
    {
        var current = new List<string>();
        var length = 0;
        foreach (var url in urls)
        {
            if (current.Count > 0 && length + url.Length + 3 > ArgumentBudget)
            {
                yield return current;
                current = [];
                length = 0;
            }

            current.Add(url);
            length += url.Length + 3;
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private Launched LaunchPath(ModeItem item)
    {
        var path = item.Path ?? string.Empty;
        if (Directory.Exists(path))
        {
            var info = Plain(Explorer);
            info.ArgumentList.Add(path);
            using (Process.Start(info))
            {
            }

            return new Launched(true, null, new ClaimRule(Explorer.ToLowerInvariant(), null, false, false, PathBudgetMs, null));
        }

        if (!File.Exists(path))
        {
            return new Launched(false, ModesStrings.ReasonFolderMissing, null);
        }

        if (ModesMath.IsBlockedFile(path))
        {
            return new Launched(false, ModesStrings.ReasonBlockedFile, null);
        }

        using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" }))
        {
        }

        return new Launched(true, null, null);
    }

    // ------------------------------------------------------------------------------------------ claiming the new window

    /// <summary>
    /// Polls for the window a launch made: new since the launch, not already claimed, and belonging to the item. A launcher that hands the
    /// request to a running copy may make no new window; then the foreground window counts when it belongs to the item. Returns 0 when
    /// nothing came within the budget.
    /// </summary>
    private nint Claim(ClaimRule rule, HashSet<nint> before, HashSet<nint> claimed, nint foregroundBefore, out bool viaForeground)
    {
        viaForeground = false;
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < rule.BudgetMs && !_cancel.IsSet)
        {
            foreach (var hwnd in AwayProcess.TopLevelWindows())
            {
                if (!before.Contains(hwnd) && !claimed.Contains(hwnd) && ModesWindows.IsCandidate(hwnd) && Belongs(hwnd, rule))
                {
                    return hwnd;
                }
            }

            if (rule.ForegroundOk)
            {
                var foreground = NativeMethods.GetForegroundWindow();
                if (foreground != 0 && !claimed.Contains(foreground) && ModesWindows.IsCandidate(foreground) && Belongs(foreground, rule)
                    && (foreground != foregroundBefore || TitleMatches(foreground, rule.TitleHint)))
                {
                    viaForeground = true;
                    return foreground;
                }
            }

            _cancel.Wait(PollMs);
        }

        return 0;
    }

    private static bool TitleMatches(nint hwnd, string? hint) =>
        !string.IsNullOrEmpty(hint) && WinApi.GetTitle(hwnd).Contains(hint, StringComparison.OrdinalIgnoreCase);

    /// <summary>Same program (or AppUserModelId) as the item; a Chromium browser also needs a main process on the default profile folder.</summary>
    private static bool Belongs(nint hwnd, ClaimRule rule)
    {
        var owner = AppIdentity.OwnerOf(hwnd);
        var sameApp = rule.Aumid is not null && WinApi.GetWindowAumid(hwnd) == rule.Aumid
            || rule.Exe is not null && string.Equals(owner.Path, rule.Exe, StringComparison.OrdinalIgnoreCase);
        if (!sameApp)
        {
            return false;
        }

        if (!rule.Chromium)
        {
            return true;
        }

        var command = AwayProcess.CommandLine(owner.Pid);
        return command is not null && !command.Contains("--type=", StringComparison.Ordinal)
            && AwayBrowsers.SameUserDataDir(rule.Exe!, string.Empty, AwayBrowsers.UserDataDirOf(command));
    }
}
