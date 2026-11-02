// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.SystemTools;

/// <summary>Asks the panel to open straight into a metric detail view (set by the tray metric items, consumed by the tab sections).</summary>
internal static class DetailRouter
{
    private static (MetricKind Kind, string Tab)? _pending;

    public static void Request(IShell shell, MetricKind kind)
    {
        var tab = MetricDetail.TabOf(kind);
        _pending = (kind, tab);
        shell.OpenPanel(tab);
    }

    /// <summary>Called by a tab section while it builds: when a detail was requested for this tab, shows it once the view is in place.</summary>
    public static void Consume(IPanelContext c, string tabId)
    {
        if (_pending is not { } pending || pending.Tab != tabId)
        {
            return;
        }

        _pending = null;
        c.Ui.BeginInvoke(() =>
        {
            var (title, glyph) = MetricDetail.Describe(pending.Kind);
            c.ShowDetail(title, glyph, MetricDetail.Create(c, pending.Kind));
        });
    }
}

/// <summary>
/// Common behaviour of the system-monitor features: ref-count the shared <see cref="MonitorService"/>, and run the tray metric display and
/// the alerts while at least one monitor feature is installed.
/// </summary>
public abstract class MonitorFeatureBase : IFeature
{
    private static int _users;
    private static MetricTray? _tray;
    private static MonitorAlerts? _alerts;
    private IFeatureContext? _context;

    public abstract string Id { get; }

    public IReadOnlyList<FeatureAction> Actions =>
        FirstOwner == Id && _context is not null
            ? [new FeatureAction("open", Loc.T("System monitor"), () => _context?.Shell.OpenPanel(PanelTabs.System), "")]
            : [];

    private static string? FirstOwner { get; set; }

    public void Start(IFeatureContext context)
    {
        _context = context;
        SystemDefaults.Register(context.App);
        MonitorService.Acquire(context);
        FirstOwner ??= Id;
        if (_users++ == 0)
        {
            _tray = new MetricTray(context);
            _alerts = new MonitorAlerts(context);
            _tray.Start();
            _alerts.Start();
        }
        else
        {
            _tray?.Refresh();
            _alerts?.Refresh();
        }
    }

    public void Stop()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        _context = null;
        if (FirstOwner == Id)
        {
            FirstOwner = null;
        }

        if (--_users <= 0)
        {
            _users = 0;
            _tray?.Stop();
            _tray = null;
            _alerts?.Stop();
            _alerts = null;
        }
        else
        {
            _tray?.Refresh();
            _alerts?.Refresh();
        }

        MonitorService.Release(context);
    }
}

public sealed class MonitorCpuFeature : MonitorFeatureBase
{
    public override string Id => "monitor-cpu";
}

public sealed class MonitorGpuFeature : MonitorFeatureBase
{
    public override string Id => "monitor-gpu";
}

public sealed class MonitorMemoryFeature : MonitorFeatureBase
{
    public override string Id => "monitor-memory";
}

public sealed class MonitorNetworkFeature : MonitorFeatureBase
{
    public override string Id => "monitor-network";
}

public sealed class MonitorDiskFeature : MonitorFeatureBase
{
    public override string Id => "monitor-disk";
}

public sealed class MonitorPowerFeature : MonitorFeatureBase
{
    public override string Id => "monitor-power";
}

public sealed class ConnectedDevicesFeature : MonitorFeatureBase
{
    public override string Id => "connected-devices";
}
