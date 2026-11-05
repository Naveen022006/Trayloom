// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.GitHub;

/// <summary>
/// The installable "GitHub" Dynamic Island extension. Installing it does nothing online: the connect flow in Settings is the opt-in
/// (spec 06 section 2). Owns the runtime while installed.
/// </summary>
public sealed class GitHubFeature : IFeature
{
    private static readonly bool Dev = Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1";

    private GitHubRuntime? _runtime;
    private CodingTracker? _tracker;
    private IFeatureContext? _context;

    public string Id => GhText.FeatureId;

    public void Start(IFeatureContext context)
    {
        _context = context;
        GhKeys.Register(context.App);
        _runtime = new GitHubRuntime(context);
        context.Services.Provide(_runtime);
        _runtime.Start();
        CodingKeys.Register(context.App);
        _tracker = new CodingTracker(context, _runtime);
        context.Services.Provide(_tracker);
        _tracker.Start();
    }

    public void Stop()
    {
        if (_runtime is null || _context is null)
        {
            return;
        }

        // The runtime flips featureAvailable.* before it stops the feature: not available any more means "uninstalled", so the data goes too.
        var uninstalled = !_context.Runtime.IsAvailable(GhText.FeatureId);
        _context.Log.Info("[github] stop: uninstalled=" + uninstalled);
        if (_tracker is not null)
        {
            // The tracker goes first: it asks the runtime for its transport, so the runtime must outlive it.
            _context.Services.Withdraw(_tracker);
            if (uninstalled)
            {
                _tracker.Erase();
            }

            _tracker.Dispose();
            _tracker = null;
        }

        _context.Services.Withdraw(_runtime);
        if (uninstalled)
        {
            _runtime.Erase();
        }

        _runtime.Dispose();
        _runtime = null;
    }

    public IReadOnlyList<FeatureAction> Actions
    {
        get
        {
            if (_runtime is not { } runtime || _context is not { } context)
            {
                return [];
            }

            var list = new List<FeatureAction>();
            if (runtime.Connected)
            {
                list.Add(new FeatureAction("refresh", Loc.T("Refresh GitHub status"), () => runtime.RequestRefresh(), ""));
            }

            list.Add(new FeatureAction("open-notifications", Loc.T("Open GitHub notifications"), () => GitHubLinks.Open(GitHubLinks.Notifications), ""));
            if (Dev)
            {
                list.Add(new FeatureAction("dump", "GitHub: dump state", runtime.Dump));
                list.Add(new FeatureAction("connect-cli", "GitHub: connect through the CLI", () =>
                    GitHubGuard.FireAndLog(async () =>
                    {
                        var result = await runtime.ConnectCliAsync().ConfigureAwait(false);
                        context.Log.Info("[github] connect-cli: " + result);
                    }, context.Log, "connect-cli")));
                list.Add(new FeatureAction("simulate-notice", "GitHub: simulate a notice", () =>
                    runtime.Queue(new GitHubNotice("", GhText.NoticeNew, GhText.NewNotifications(3), System.Windows.Media.Colors.White))));
            }

            if (Dev && _tracker is { } tracker)
            {
                list.Add(new FeatureAction("log-coding", "GitHub: log coding state", tracker.LogState));
                list.Add(new FeatureAction("simulate-checks-finished", "GitHub: simulate checks finished", tracker.SimulateFinished));
            }

            return list;
        }
    }
}
