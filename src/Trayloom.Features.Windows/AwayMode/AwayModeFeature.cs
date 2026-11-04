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

    public static void Register(IAppSettings app)
    {
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
    }
}

/// <summary>
/// Away mode: minimizes every window when the person leaves the PC and brings them back, in the same order, when they return
/// (spec 06 section 4). Nothing is closed, hidden or moved off-screen, so the window guardian has nothing to track.
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

            if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1")
            {
                list.Add(new FeatureAction("simulate-away", "Away mode: simulate away (needs away-mode-only.txt)", () => SimulateAway(controller, _context?.Log)));
                list.Add(new FeatureAction("simulate-return", "Away mode: simulate return", () => controller.Return("simulated")));
                list.Add(new FeatureAction("simulate-face", "Away mode: simulate a face sample", controller.SimulateFace));
                list.Add(new FeatureAction("dump", "Away mode: dump state", controller.Dump));
            }

            return list;
        }
    }

    public void Start(IFeatureContext context)
    {
        _context = context;
        AwayKeys.Register(context.App);
        var session = new AwaySession(context.DataDirectory, context.Log);
        RecoverEarlierRun(context, session);

        var controller = new AwayController(context, context.DataDirectory);
        _controller = controller;
        controller.Start();
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
            }
            catch (Exception)
            {
                // a leftover dump contains counts only
            }
        }
    }

    private static void SimulateAway(AwayController controller, ILog? log)
    {
        if (AwayWindows.DevOnlyProcesses() is null)
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
