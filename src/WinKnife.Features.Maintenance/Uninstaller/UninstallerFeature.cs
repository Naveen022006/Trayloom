// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

/// <summary>Uninstaller: the catalog entry "uninstaller". The UI lives in the contributions.</summary>
public sealed class UninstallerFeature : IFeature
{
    private IFeatureContext? _context;

    public string Id => "uninstaller";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", Loc.T("Uninstall an app…"), () => _context?.Shell.OpenSettings(SettingsPages.Uninstaller), G.Trash),
    ];

    private IUninstallService? _service;

    public void Start(IFeatureContext context)
    {
        _context = context;
        Host.Attach(context);
        _service = new UninstallServiceImpl();
        context.Services.Provide(_service);
    }

    public void Stop()
    {
        if (_service is not null)
        {
            _context?.Services.Withdraw(_service);
            _service = null;
        }

        UninstallerModel.Shared.Cancel();
        _context = null;
    }

    /// <summary>Starts an uninstall scan for a program picked elsewhere (Command Bar, shell integration).</summary>
    public static Task SelectAsync(string path) => UninstallerModel.Shared.SelectPathAsync(path);
}

/// <summary>Settings page "Uninstaller": the wizard.</summary>
public sealed class UninstallerSettingsSection : ISettingsSection
{
    public string? FeatureId => "uninstaller";

    public string PageId => SettingsPages.Uninstaller;

    public string? Title => Loc.T("Uninstaller");

    public IReadOnlyList<string> Keywords => ["Drag a program here", "Choose program", "remove apps", "leftovers", "Show in Command Bar"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        Host.Attach(c);
        return new UninstallerView(false);
    }
}

/// <summary>Utilities row "Uninstaller": the hosted compact wizard.</summary>
public sealed class UninstallerUtility : IPanelUtility
{
    public string? FeatureId => "uninstaller";

    public int Order => 90;

    public string Id => "uninstaller";

    public string Title => Loc.T("Uninstaller");

    public string Glyph => G.Trash;

    public string? Caption => Loc.T("Removes an app together with the caches, preferences, logs and leftovers it leaves behind.");

    public string? SettingsDestination => SettingsPages.Uninstaller;

    public FrameworkElement? CreateHostedView(IPanelContext c)
    {
        Host.Attach(c);
        return new UninstallerView(true, c.RequestRelayout, c.SetKeepOpen);
    }
}
