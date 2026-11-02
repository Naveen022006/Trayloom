// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>
/// Common behaviour of the system-monitor features: ref-count the shared <see cref="MonitorHub"/>, expose the shared
/// "System monitor" flyout action (only from the first installed monitor feature) and a flyout hotkey.
/// </summary>
public abstract class MonitorFeatureBase : IFeature
{
    private HotkeyBinding? _hotkey;
    private IFeatureContext? _context;

    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public FeatureGroup Group => FeatureGroup.Monitor;

    internal abstract MonitorMetric Metric { get; }

    /// <summary>Default hotkey of the shared flyout (only one monitor feature suggests one).</summary>
    protected virtual Hotkey? DefaultHotkey => null;

    public IReadOnlyList<FeatureAction> Actions =>
        MonitorHub.IsOwner(Id)
            ? [new FeatureAction("flyout", "System monitor", MonitorHub.ToggleFlyout, "")]
            : [];

    public void Start(IFeatureContext context)
    {
        _context = context;
        MonitorHub.Acquire(Id, Metric, context);
        _hotkey = new HotkeyBinding(context, "hotkey", DefaultHotkey, MonitorHub.ToggleFlyout);
    }

    public void Stop()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        MonitorHub.Release(Id);
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        return _context is null ? null : new SettingsViewBuilder(_context.Settings)
            .AddToggle("pinned", "Show in the always-on-top readout", false, "A small readout in the top-right corner of the main screen. The sampler only runs while it or the flyout is visible.")
            .AddHotkey("hotkey", "Open the System monitor flyout", DefaultHotkey, "One shared flyout shows every installed monitor feature.")
            .Build();
    }
}

public sealed class MonitorCpuFeature : MonitorFeatureBase
{
    public override string Id => "monitor-cpu";

    public override string Name => "CPU monitor";

    public override string Description => "Live CPU usage with a per-core view and a history graph.";

    internal override MonitorMetric Metric => MonitorMetric.Cpu;

    protected override Hotkey? DefaultHotkey => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Key.P);
}

public sealed class MonitorGpuFeature : MonitorFeatureBase
{
    public override string Id => "monitor-gpu";

    public override string Name => "GPU monitor";

    public override string Description => "Live GPU utilisation from the Windows GPU engine counters.";

    internal override MonitorMetric Metric => MonitorMetric.Gpu;
}

public sealed class MonitorMemoryFeature : MonitorFeatureBase
{
    public override string Id => "monitor-memory";

    public override string Name => "Memory monitor";

    public override string Description => "See how much RAM is in use and how it changes over time.";

    internal override MonitorMetric Metric => MonitorMetric.Memory;
}

public sealed class MonitorNetworkFeature : MonitorFeatureBase
{
    public override string Id => "monitor-network";

    public override string Name => "Network monitor";

    public override string Description => "Live download and upload speed across your active network adapters.";

    internal override MonitorMetric Metric => MonitorMetric.Network;
}

public sealed class MonitorDiskFeature : MonitorFeatureBase
{
    public override string Id => "monitor-disk";

    public override string Name => "Disk monitor";

    public override string Description => "Free space on every drive plus live disk read and write activity.";

    internal override MonitorMetric Metric => MonitorMetric.Disk;
}

public sealed class MonitorPowerFeature : MonitorFeatureBase
{
    public override string Id => "monitor-power";

    public override string Name => "Battery and power";

    public override string Description => "Battery level, charging state and estimated time remaining.";

    internal override MonitorMetric Metric => MonitorMetric.Power;
}

public sealed class ConnectedDevicesFeature : MonitorFeatureBase
{
    public override string Id => "connected-devices";

    public override string Name => "Connected devices";

    public override string Description => "Lists the USB devices currently plugged into this PC.";

    internal override MonitorMetric Metric => MonitorMetric.Devices;
}
