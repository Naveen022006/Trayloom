// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Maintenance;

/// <summary>Cleaner: owns the automatic-cleanup scheduler. The UI lives in the contributions.</summary>
public sealed class CleanerFeature : IFeature
{
    private IFeatureContext? _context;

    public string Id => "cleaner";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", Loc.T("Cleaner"), () => _context?.Shell.OpenSettings(SettingsPages.Cleaner), G.Sparkle),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        Host.Attach(context);
        context.App.Changed += OnSetting;
        CleanerModel.Shared.SyncWithPreferences();
        WaHub.Start();
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.App.Changed -= OnSetting;
        }

        CleanerModel.Shared.Stop();
        WaHub.Stop();
        _context = null;
    }

    private void OnSetting(string key)
    {
        if (key.StartsWith("whatsApp", StringComparison.Ordinal))
        {
            WaHub.OnSetting(key);
        }

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

    public string? Title => Loc.T("Cleaner");

    public IReadOnlyList<string> Keywords => ["Automatic cleanup", "Clean up automatically", "Forgotten screenshots", "caches", "logs", "temp", "WhatsApp downloads", "File types", "Automatic organization"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        Host.Attach(c);
        return new CleanerPage();
    }
}

/// <summary>Utilities row "Cleaner": the hosted compact view.</summary>
public sealed class CleanerUtility : IPanelUtility
{
    public string? FeatureId => "cleaner";

    public int Order => 40;

    public string Id => "cleaner";

    public string Title => Loc.T("Cleaner");

    public string Glyph => G.Sparkle;

    public string? Caption => Loc.T("App leftovers, caches and logs");

    public string? SettingsDestination => SettingsPages.Cleaner;

    public FrameworkElement? CreateHostedView(IPanelContext c)
    {
        Host.Attach(c);
        return new CleanerView(true, c.RequestRelayout, c.SetKeepOpen);
    }
}

/// <summary>
/// The Cleaner page frame (Vorssaint's <c>CleanerSettings</c>): a tool switcher shown only while "WhatsApp downloads" is on
/// ("Cleaner" | "WhatsApp downloads"), then the chosen tool.
/// </summary>
internal sealed class CleanerPage : StackPanel
{
    private readonly ContentControl _body = new();
    private bool _whatsApp;

    public CleanerPage()
    {
        Loaded += (_, _) =>
        {
            Host.App.Changed += OnSetting;
            WaCards.ToolRequested += OnToolRequested;
            if (!Enabled)
            {
                _whatsApp = false;
            }

            ConsumeHint();
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            Host.App.Changed -= OnSetting;
            WaCards.ToolRequested -= OnToolRequested;
        };
        Rebuild();
    }

    private static bool Enabled => Host.App.Get<bool>("whatsAppDownloadsEnabled");

    private void OnSetting(string key)
    {
        if (key == "whatsAppDownloadsEnabled")
        {
            V.Post(() =>
            {
                _whatsApp = Enabled;
                Rebuild();
            });
        }
    }

    private void OnToolRequested() => V.Post(() =>
    {
        ConsumeHint();
        Rebuild();
    });

    /// <summary>A panel surface can ask for the WhatsApp tool; the hint is one-shot and honoured only while the tool is on.</summary>
    private void ConsumeHint()
    {
        if (!WaCards.PendingWhatsAppTool)
        {
            return;
        }

        WaCards.PendingWhatsAppTool = false;
        if (Enabled)
        {
            _whatsApp = true;
        }
    }

    private void Rebuild()
    {
        Children.Clear();
        if (Enabled)
        {
            var switcher = new SegmentedControl([WaText.Cleaner, WaText.Title], _whatsApp ? 1 : 0, true, 12)
            {
                MaxWidth = 440,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(24, 14, 24, 14),
            };
            switcher.SelectionChanged += index =>
            {
                _whatsApp = index == 1;
                Dispatcher.BeginInvoke(Rebuild);
            };
            Children.Add(switcher);
            Children.Add(UiKit.Divider());
        }
        else
        {
            _whatsApp = false;
        }

        _body.Content = Enabled && _whatsApp ? new WaPageView { Margin = new Thickness(0, 16, 0, 0) } : new CleanerView(false);
        Children.Add(_body);
    }
}
