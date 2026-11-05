// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.App;

/// <summary>Everything the app-owned pages and windows need, in one place.</summary>
internal sealed class ShellServices
{
    public ShellServices(CoreHost host, AppShell shell)
    {
        Host = host;
        Shell = shell;
    }

    public CoreHost Host { get; }

    public AppShell Shell { get; }

    public IAppSettings App => Host.App;

    public FeatureRuntime Runtime => Host.Runtime;

    public ContributionRegistry Contributions => Host.Contributions;

    public IShortcutService Shortcuts => Host.Shortcuts;

    public ILog Log => Host.Log;

    /// <summary>The update notifier (set when the shell starts).</summary>
    public UpdateService? Updates { get; set; }
}
