// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

/// <summary>Cleaner: owns the automatic-cleanup scheduler. The UI lives in the contributions.</summary>
public sealed class CleanerFeature : IFeature
{
    private IFeatureContext? _context;

    public string Id => "cleaner";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", "Cleaner", () => _context?.Shell.OpenSettings(SettingsPages.Cleaner), G.Sparkle),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        Host.Attach(context);
        context.App.Changed += OnSetting;
        CleanerModel.Shared.SyncWithPreferences();
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.App.Changed -= OnSetting;
        }

        CleanerModel.Shared.Stop();
        _context = null;
    }

    private void OnSetting(string key)
    {
        if (key.StartsWith("cleanerSchedule", StringComparison.Ordinal))
        {
            _context?.Ui.BeginInvoke(CleanerModel.Shared.SyncWithPreferences);
        }
    }
}

/// <summary>Settings page "Cleaner": hosts the full view.</summary>
public sealed class CleanerSettingsSection : ISettingsSection
{
    public string? FeatureId => "cleaner";

    public string PageId => SettingsPages.Cleaner;

    public string? Title => "Cleaner";

    public IReadOnlyList<string> Keywords => ["Automatic cleanup", "Clean up automatically", "Forgotten screenshots", "caches", "logs", "temp"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        Host.Attach(c);
        return new CleanerView(false);
    }
}

/// <summary>Utilities row "Cleaner": the hosted compact view.</summary>
public sealed class CleanerUtility : IPanelUtility
{
    public string? FeatureId => "cleaner";

    public int Order => 40;

    public string Id => "cleaner";

    public string Title => "Cleaner";

    public string Glyph => G.Sparkle;

    public string? Caption => "App leftovers, caches and logs";

    public string? SettingsDestination => SettingsPages.Cleaner;

    public FrameworkElement? CreateHostedView(IPanelContext c)
    {
        Host.Attach(c);
        return new CleanerView(true, c.RequestRelayout, c.SetKeepOpen);
    }
}
