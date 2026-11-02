// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.SystemTools;

/// <summary>Keep awake: prevents the PC from sleeping on demand, from automation conditions or with a lid-closed override.</summary>
public sealed class KeepAwakeFeature : IFeature
{
    private KeepAwakeService? _service;
    private IDisposable? _shortcut;

    public string Id => "keep-awake";

    public IReadOnlyList<FeatureAction> Actions
    {
        get
        {
            var service = _service;
            var list = new List<FeatureAction>
            {
                new("toggle", service?.IsActive == true ? Loc.T("Disable keep awake") : Loc.T("Enable keep awake"), () => service?.Toggle(), "\uE708"),
            };
            foreach (var d in Strings.KeepAwake.Durations.Where(d => d.Minutes > 0))
            {
                var minutes = d.Minutes;
                var id = minutes switch { 15 => "15m", 60 => "1h", 120 => "2h", _ => minutes + "m" };
                list.Add(new FeatureAction(id, "Keep awake for " + d.Name, () => service?.ActivateChip(minutes), "\uE916"));
            }

            list.Add(new FeatureAction("forever", Loc.T("Keep awake indefinitely"), () => service?.ActivateChip(0), "\uE708"));
            return list;
        }
    }

    public void Start(IFeatureContext context)
    {
        SystemDefaults.Register(context.App);
        _service = new KeepAwakeService(context);
        _service.Start();
        _shortcut = context.Shortcuts.Bind("keepAwake", () => _service?.Toggle());
    }

    public void Stop()
    {
        _shortcut?.Dispose();
        _shortcut = null;
        _service?.Stop();
        _service = null;
    }
}

/// <summary>The KEEP AWAKE tab of the tray panel.</summary>
public sealed class KeepAwakePanelSection : IPanelSection
{
    public string? FeatureId => "keep-awake";

    public string TabId => PanelTabs.KeepAwake;

    public FrameworkElement CreateView(IPanelContext context)
    {
        SystemDefaults.Register(context.App);
        return KeepAwakeViews.PanelCard(context);
    }
}

/// <summary>Energy page: the two Keep awake cards (anchor <c>keepAwake</c>).</summary>
public sealed class KeepAwakeSettingsSection : ISettingsSection
{
    public string? FeatureId => "keep-awake";

    public string PageId => SettingsPages.Energy;

    public string? Anchor => "keepAwake";

    public string? Title => Loc.T("Keep awake");

    public int Order => 10;

    public IReadOnlyList<string> Keywords =>
    [
        "default duration", "automation", "external display", "power", "applications", "pause while locked", "pointer", "interval",
        "active icon", "battery", "lid closed", "dim the display", "countdown", "right-click", "allow the display to sleep",
    ];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        SystemDefaults.Register(context.App);
        var stack = new System.Windows.Controls.StackPanel();
        stack.Children.Add(KeepAwakeViews.SettingsCard1(context));
        var options = KeepAwakeViews.SettingsOptions(context);
        options.Margin = new Thickness(0, 20, 0, 0);
        stack.Children.Add(options);
        return stack;
    }
}
