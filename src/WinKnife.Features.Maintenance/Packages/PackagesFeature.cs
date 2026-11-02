// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

/// <summary>Packages (winget): the catalog entry "homebrew". The UI lives in the contributions.</summary>
public sealed class PackagesFeature : IFeature
{
    public string Id => "homebrew";

    public void Start(IFeatureContext context)
    {
        Host.Attach(context);
    }

    public void Stop()
    {
        WingetOperations.Shared.Cancel();
    }
}

/// <summary>Settings page "Packages": the two-pane winget browser.</summary>
public sealed class PackagesSettingsSection : ISettingsSection
{
    public string? FeatureId => "homebrew";

    public string PageId => SettingsPages.Packages;

    public string? Title => "Packages";

    public IReadOnlyList<string> Keywords => ["winget", "Search packages", "Install", "Uninstall", "Update all", "Update sources"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        Host.Attach(c);
        return new PackagesPageView();
    }
}

/// <summary>Utilities row "Packages": the hosted winget package manager panel.</summary>
public sealed class PackagesUtility : IPanelUtility
{
    public string? FeatureId => "homebrew";

    public int Order => 50;

    public string Id => "homebrew";

    public string Title => "Packages";

    public string Glyph => G.Package;

    public string? Caption => "Search, install and remove apps with winget.";

    public string? SettingsDestination => SettingsPages.Packages;

    public bool HostedKeepsPanelOpen => false;

    public FrameworkElement? CreateHostedView(IPanelContext c)
    {
        Host.Attach(c);
        return new PackagesPanelView(c.SetKeepOpen, c.RequestRelayout);
    }
}
