// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;
using Trayloom.Features.SystemTools.Wifi;

namespace Trayloom.Features.SystemTools.Bluetooth;

/// <summary>The "Bluetooth" tab of the tray flyout: the radio switch, the shown devices with their battery, and two links into Windows.</summary>
public sealed class BluetoothPanelSection : IPanelSection
{
    public string? FeatureId => BtKeys.FeatureId;

    public string TabId => PanelTabs.Bluetooth;

    public int Order => 100;

    public FrameworkElement CreateView(IPanelContext context)
    {
        if (context.Services.Get<BluetoothService>() is not { } service)
        {
            return new Border { Visibility = Visibility.Collapsed };
        }

        return new BluetoothPanelCard(service, context);
    }
}

/// <summary>Content of <see cref="BluetoothPanelSection"/>; redrawn from the service snapshot.</summary>
internal sealed class BluetoothPanelCard : Border
{
    private readonly BluetoothService _service;
    private readonly IPanelContext _context;
    private bool _loaded;
    private bool _counted;

    public BluetoothPanelCard(BluetoothService service, IPanelContext context)
    {
        _service = service;
        _context = context;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        Rebuild();
    }

    private void Attach()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _service.Changed += OnChanged;
        _context.ActiveChanged += OnActive;
        OnActive();
    }

    private void Detach()
    {
        if (!_loaded)
        {
            return;
        }

        _loaded = false;
        _service.Changed -= OnChanged;
        _context.ActiveChanged -= OnActive;
        Count(false);
    }

    /// <summary>The battery is read again every 30 seconds only while the tab is the visible one.</summary>
    private void OnActive() => Count(_context.IsActive);

    private void Count(bool visible)
    {
        if (visible == _counted)
        {
            return;
        }

        _counted = visible;
        if (visible)
        {
            _service.Attach();
        }
        else
        {
            _service.Detach();
        }
    }

    private void OnChanged() => Dispatcher.BeginInvoke(() =>
    {
        Rebuild();
        _context.RequestRelayout();
    });

    private void Rebuild()
    {
        var s = _service.Snapshot;
        // No Bluetooth hardware: the card hides once Windows said so (the tab hides with it).
        if (s.Loaded && s.Radio == BtRadio.None)
        {
            Visibility = Visibility.Collapsed;
            Child = null;
            return;
        }

        Visibility = Visibility.Visible;
        var group = new PanelRowGroup();
        var switchable = s.Radio is (BtRadio.On or BtRadio.Off) && !s.AccessDenied;
        var caption = s.AccessDenied
            ? BtStrings.AccessDenied
            : s.Radio switch
            {
                BtRadio.On => BtStrings.On,
                BtRadio.Off => BtStrings.Off,
                BtRadio.Disabled => BtStrings.TurnedOffByHardware,
                _ => null,
            };
        var toggle = new PanelToggleRow("", BtStrings.Title, caption, s.Radio == BtRadio.On, value => _service.SetRadio(value, Window.GetWindow(this), _context.SetKeepOpen), true, s.AccessDenied)
        {
            IsEnabled = switchable,
        };
        toggle.Loaded += (_, _) => NameSwitch(toggle);
        group.Add(toggle);

        if (s.Radio is BtRadio.On or BtRadio.Unknown)
        {
            foreach (var device in _service.Visible())
            {
                var line = device.Connected ? (device.Battery is { } percent ? BtStrings.ConnectedLevel(percent) : BtStrings.Connected) : BtStrings.NotConnected;
                var captured = device;
                group.Add(new PanelActionRow(BtLook.KindGlyph(device.Kind), device.Name, line, () =>
                {
                    if (captured.Connected)
                    {
                        _service.OpenBluetoothSettings();
                    }
                    else
                    {
                        _context.InvokeAfterClose(_service.OpenConnect);
                    }
                }, null, true, false, false));
            }
        }

        group.Add(new PanelActionRow("", BtStrings.AddDeviceEllipsis, null, () => _context.InvokeAfterClose(_service.OpenAddDevice)));
        group.Add(new PanelActionRow("", BtStrings.BluetoothSettingsEllipsis, null, () => _context.InvokeAfterClose(_service.OpenBluetoothSettings)));
        Child = group;
    }

    /// <summary>Gives the row's switch its UI Automation id so it can be read (its toggle state) without being operated.</summary>
    private static void NameSwitch(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is MacSwitch sw)
            {
                WifiLook.Automate(sw, "Bluetooth.PanelRadio", BtStrings.Title);
                return;
            }

            NameSwitch(child);
        }
    }
}
