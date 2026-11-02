// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

/// <summary>App updates: schedule, notifications and the Command Bar action. The UI lives in the contributions.</summary>
public sealed class AppUpdatesFeature : IFeature
{
    private IFeatureContext? _context;

    public string Id => "app-updates";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("check", Loc.T("Check for app updates"), () =>
        {
            _ = AppUpdatesService.Shared.CheckAsync(false);
            _context?.Shell.OpenSettings(SettingsPages.AppUpdates);
        }, G.Download),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        Host.Attach(context);
        AppUpdatesService.Shared.ReloadRules();
        AppUpdatesService.Shared.SyncWithPreferences();
        context.App.Changed += OnSetting;
        if (Application.Current is { } app)
        {
            app.Activated += OnActivated;
        }
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.App.Changed -= OnSetting;
        }

        if (Application.Current is { } app)
        {
            app.Activated -= OnActivated;
        }

        AppUpdatesService.Shared.Stop();
        _context = null;
    }

    private void OnActivated(object? sender, EventArgs e) => AppUpdatesService.Shared.ApplicationBecameActive();

    private void OnSetting(string key)
    {
        if (key == "appUpdatesCheckFrequency")
        {
            _context?.Ui.BeginInvoke(AppUpdatesService.Shared.SyncWithPreferences);
        }
    }
}
