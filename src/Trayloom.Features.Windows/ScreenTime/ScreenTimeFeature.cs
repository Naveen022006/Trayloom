// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools.ScreenTime;

/// <summary>
/// Screen time (spec 06 section 1): counts the time each app is in front while the person uses the PC, per day, locally. The runtime is
/// published for the island page, the resting strip and the settings card; day files stay on exit and go away only on uninstall.
/// </summary>
public sealed class ScreenTimeFeature : IFeature
{
    private IFeatureContext? _context;
    private ScreenTimeRuntime? _runtime;

    public string Id => ScreenTimeKeys.FeatureId;

    private static bool DevActions => Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1";

    public void Start(IFeatureContext context)
    {
        _context = context;
        ScreenTimeKeys.Register(context.App);
        var store = new ScreenTimeStore(context.DataDirectory, context.Log);
        _runtime = new ScreenTimeRuntime(context.App, context.Log, context.Ui, store);
        context.Services.Provide(_runtime);
        _runtime.Start();
    }

    public void Stop()
    {
        var context = _context;
        var runtime = _runtime;
        if (context is null || runtime is null)
        {
            return;
        }

        runtime.Stop();
        context.Services.Withdraw(runtime);
        // The runtime flips featureAvailable.* before it stops the feature, so "not available" here means uninstalled.
        var uninstalled = !context.Runtime.IsAvailable(ScreenTimeKeys.FeatureId);
        context.Log.Info($"[screen-time] stop: uninstalled={uninstalled}, hook released");
        if (uninstalled)
        {
            runtime.Store.DeleteAll();
        }

        _runtime = null;
        _context = null;
    }

    public IReadOnlyList<FeatureAction> Actions
    {
        get
        {
            if (_runtime is not { } runtime || _context is not { } context)
            {
                return [];
            }

            var list = new List<FeatureAction>
            {
                new("today", ScreenTimeStrings.ActionToday(ScreenTimeFormat.Long(runtime.Today.Total)), () => context.Shell.OpenSettings("island#screenTime"), ""),
            };
            if (DevActions)
            {
                list.Add(new FeatureAction("dump", "Screen time: dump", runtime.Dump));
                list.Add(new FeatureAction("flush", "Screen time: flush", () =>
                {
                    runtime.Tracker.Flush(force: true);
                    context.Log.Info("[screen-time] flush");
                }));
                list.Add(new FeatureAction("dev-add-minute", "Screen time: add a minute to the front app", runtime.Tracker.AddMinute));
            }

            return list;
        }
    }
}
