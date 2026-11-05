// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>The settings keys of Away mode and their defaults (spec 06 section 4.2).</summary>
internal static class AwayKeys
{
    public const string Enabled = "awayModeEnabled";
    public const string IdleMinutes = "awayModeIdleMinutes";
    public const string LockPc = "awayModeLockPc";
    public const string Cover = "awayModeCover";
    public const string Face = "awayModeFace";
    public const string CameraId = "awayModeCameraId";
    public const string FaceStopMinutes = "awayModeFaceStopMinutes";
    public const string SkipMedia = "awayModeSkipMedia";
    public const string SkipCalls = "awayModeSkipCalls";
    public const string SkipFullScreen = "awayModeSkipFullScreen";
    public const string IgnoredApps = "awayModeIgnoredApps";

    /// <summary>Not in the spec table: remembers that the camera failed last time so the card can say so until the next success.</summary>
    public const string CameraFailed = "awayModeCameraFailed";

    /// <summary>Spec 07 section 1: close the apps on <see cref="CloseList"/> while away and open them again on return.</summary>
    public const string CloseApps = "awayModeCloseApps";
    public const string CloseList = "awayModeCloseList";
    public const string CloseListSeeded = "awayModeCloseListSeeded";
    public const string CloseAfterMinutes = "awayModeCloseAfterMinutes";

    /// <summary>Which apps close: <c>list</c> (the chosen apps), <c>mode</c> (those plus the active mode's apps) or <c>all</c>.</summary>
    public const string CloseScope = "awayModeCloseScope";

    /// <summary>Opt-in: leave when the camera has not seen anyone for <see cref="LeaveCameraSeconds"/> (spec 08 section 1).</summary>
    public const string LeaveCamera = "awayModeLeaveCamera";
    public const string LeaveCameraSeconds = "awayModeLeaveCameraSeconds";

    public static void Register(IAppSettings app)
    {
        app.Register(LeaveCamera, false);
        app.Register(LeaveCameraSeconds, 60);
        app.Register(CloseScope, "list");
        app.Register(Enabled, true);
        app.Register(IdleMinutes, 10);
        app.Register(LockPc, false);
        app.Register(Cover, false);
        app.Register(Face, false);
        app.Register(CameraId, string.Empty);
        app.Register(FaceStopMinutes, 120);
        app.Register(SkipMedia, true);
        app.Register(SkipCalls, true);
        app.Register(SkipFullScreen, true);
        app.Register(IgnoredApps, new List<string>());
        app.Register(CameraFailed, false);
        app.Register(CloseApps, false);
        app.Register(CloseList, new List<string>());
        app.Register(CloseListSeeded, false);
        app.Register(CloseAfterMinutes, 0);
    }
}

/// <summary>
/// Away mode: minimizes every window when the person leaves the PC and brings them back, in the same order, when they return
/// (spec 06 section 4), and, when the person chose apps to close, closes those and opens them again (spec 07). Nothing is hidden or
/// moved off-screen, so the window guardian has nothing to track.
/// </summary>
public sealed class AwayModeFeature : IFeature
{
    private IFeatureContext? _context;
    private AwayController? _controller;

    public string Id => "away-mode";

    public IReadOnlyList<FeatureAction> Actions
    {
        get
        {
            var controller = _controller;
            if (controller is null)
            {
                return [];
            }

            var list = new List<FeatureAction>();
            if (controller.IsAway)
            {
                list.Add(new FeatureAction("restore", AwayStrings.RestoreWindows, () => controller.Return("manual"), ""));
            }
            else
            {
                list.Add(new FeatureAction("away-now", AwayStrings.GoAwayNow, () => controller.GoAway("manual"), ""));
            }

            if (!controller.IsAway && controller.RecoveryCount > 0)
            {
                list.Add(new FeatureAction("reopen", AwayStrings.ReopenAction, controller.ReopenRecovery, ""));
            }

            if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1")
            {
                list.Add(new FeatureAction("simulate-away", "Away mode: simulate away (needs away-mode-only.txt)", () => SimulateAway(controller, _context?.Log)));
                list.Add(new FeatureAction("simulate-return", "Away mode: simulate return", () => controller.Return("simulated")));
                list.Add(new FeatureAction("simulate-face", "Away mode: simulate a face sample", controller.SimulateFace));
                list.Add(new FeatureAction("simulate-absence", "Away mode: simulate a camera sample with nobody there", () => controller.SimulateObservation(new AwayObservation(false, false, true, true))));
                list.Add(new FeatureAction("simulate-motion", "Away mode: simulate a camera sample with movement", () => controller.SimulateObservation(new AwayObservation(false, true, true, true))));
                list.Add(new FeatureAction("simulate-dark", "Away mode: simulate a dark camera sample", () => controller.SimulateObservation(new AwayObservation(false, false, false, true))));
                list.Add(new FeatureAction("dump","Away mode: dump state", controller.Dump));
                list.Add(new FeatureAction("dev-close-now", "Away mode: close the listed apps now (needs away-mode-only.txt)", controller.DevCloseNow));
                list.Add(new FeatureAction("simulate-session-end", "Away mode: simulate a session end while away", controller.SimulateSessionEnd));
            }

            return list;
        }
    }

    public void Start(IFeatureContext context)
    {
        _context = context;
        AwayKeys.Register(context.App);
        ApplyDevArmSeconds(context.Log);
        var session =new AwaySession(context.DataDirectory, context.Log);
        RecoverEarlierRun(context, session);
        var offered = RecoverClosedApps(context);

        var controller = new AwayController(context, context.DataDirectory);
        _controller = controller;
        controller.Start();
        if (offered is not null)
        {
            // Asked, never reopened by itself: at sign-in the person may not be at the PC, or Windows may have started the apps already.
            controller.OfferRecovery(offered);
            context.Log.Info($"[away] closed apps from an earlier run: {offered.Apps.Count} offered");
            context.Notifier.Show(AwayStrings.Title, AwayStrings.NotificationRecovery(offered.Apps.Count));
        }

        context.Services.Provide(controller);
    }

    public void Stop()
    {
        var controller = _controller;
        var context = _context;
        if (controller is null || context is null)
        {
            return;
        }

        context.Services.Withdraw(controller);
        controller.Stop();
        _controller = null;

        // The runtime flips featureAvailable.* before it stops the feature, so "not available" here means "uninstalled".
        var uninstalled = !context.Runtime.IsAvailable("away-mode");
        context.Log.Info($"[away] stop: uninstalled={uninstalled}");
        if (uninstalled)
        {
            try
            {
                File.Delete(Path.Combine(context.DataDirectory, "dump.json"));

                // Apps the person still wants back are never dropped silently: the record goes only when nothing is waiting in it.
                var store = new AwayClosedStore(context.DataDirectory, context.Log);
                if (store.Read() is not { } record || !record.Apps.Any(a => a.State == AwayAppState.Closed))
                {
                    store.Delete();
                }
            }
            catch (Exception)
            {
                // a leftover dump contains counts only
            }
        }
    }

    /// <summary>Developer aid: <c>TRAYLOOM_AWAY_ARM_SECONDS</c> shortens the 30 s without input the camera waits for, so a test does not have to sit still.</summary>
    private static void ApplyDevArmSeconds(ILog log)
    {
        if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") != "1")
        {
            return;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("TRAYLOOM_AWAY_ARM_SECONDS"), out var seconds) && seconds >= 0)
        {
            AwayPresenceClock.ArmIdleMs = seconds * 1000L;
            log.Info($"[away] dev: camera arms after {seconds} s without input");
        }
    }

    private static void SimulateAway(AwayController controller, ILog? log)
    {
        if (AwayDevFilter.Load() is null)
        {
            log?.Info("[away] simulate-away refused: no away-mode-only.txt");
            return;
        }

        controller.GoAway("simulated", simulated: true);
    }

    /// <summary>
    /// A session file whose owner is gone means Trayloom crashed or was killed while away: put the windows that are still minimized
    /// back (no focus change at login), then delete the file (spec 06 section 4.6).
    /// </summary>
    private static void RecoverEarlierRun(IFeatureContext context, AwaySession session)
    {
        var file = session.Read();
        if (file is null)
        {
            if (session.Exists)
            {
                session.Delete();
            }

            return;
        }

        if (OwnerAlive(file))
        {
            return;
        }

        if (DateTime.UtcNow - file.Since > TimeSpan.FromHours(24))
        {
            session.Delete();
            return;
        }

        var records = file.Windows;
        var log = context.Log;
        var worker = new Thread(() =>
        {
            try
            {
                var iconic = records.Count(r => AwayWindows.StillMatches(r) && WinApi.IsIconic(r.Handle));
                AwayWindows.Restore(records, 0, focusActive: false, onlyIconic: true);
                log.Info($"[away] restored {iconic} windows left minimized by an earlier run");
                session.Delete();
            }
            catch (Exception ex)
            {
                log.Error("[away] recovery failed", ex);
            }
        })
        { IsBackground = true, Name = "Away recovery" };
        worker.Start();
    }

    /// <summary>What an earlier run left closed (spec 07 section 7.3); null when there is nothing to offer.</summary>
    private static ClosedAppsFile? RecoverClosedApps(IFeatureContext context)
    {
        try
        {
            var store = new AwayClosedStore(context.DataDirectory, context.Log);
            return store.Recover(AwayReopenJob.IsOpen);
        }
        catch (Exception ex)
        {
            context.Log.Error("[away] closed apps recovery failed", ex);
            return null;
        }
    }

    private static bool OwnerAlive(AwaySessionFile file)
    {
        if (file.OwnerPid == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(file.OwnerPid);
            return !process.HasExited && process.StartTime.ToFileTimeUtc() == file.OwnerStart;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
