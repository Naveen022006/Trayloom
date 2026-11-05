// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.SystemTools.Bluetooth;

/// <summary>
/// Bluetooth in the Dynamic Island and the tray (spec 09 part 1): paired devices, their battery, the radio switch and the connect and
/// low-battery alerts. It replaces the island's own accessory alerts while it is installed. <see cref="Stop"/> withdraws the service and
/// drops every watcher.
/// </summary>
public sealed class BluetoothFeature : IFeature
{
    private IFeatureContext? _context;
    private BluetoothService? _service;

    public string Id => BtKeys.FeatureId;

    public void Start(IFeatureContext context)
    {
        _context = context;
        BtKeys.Register(context.App);
        var service = new BluetoothService(context);
        _service = service;
        context.Services.Provide(service);
        context.Services.Provide<IBluetoothState>(service);
        service.Start();
    }

    public void Stop()
    {
        if (_service is { } service)
        {
            _context?.Services.Withdraw<IBluetoothState>(service);
            _context?.Services.Withdraw(service);
            service.Dispose();
        }

        _service = null;
        _context = null;
    }

    public IReadOnlyList<FeatureAction> Actions
    {
        get
        {
            if (_service is not { } service)
            {
                return [];
            }

            var list = new List<FeatureAction>();
            if (service.Snapshot.Radio is BtRadio.On or BtRadio.Off)
            {
                var on = service.Snapshot.Radio == BtRadio.On;
                list.Add(new FeatureAction("toggle-radio", on ? BtStrings.TurnBluetoothOff : BtStrings.TurnBluetoothOn, () => service.SetRadio(!on), ""));
            }

            list.Add(new FeatureAction("settings", BtStrings.ActionSettings, BtKeys.OpenSettings, ""));
            list.Add(new FeatureAction("add-device", BtStrings.ActionAdd, BtKeys.OpenAddDevice, ""));

            // Developer aids: they read, or queue a notice, and never touch the radio or a device.
            if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1")
            {
                list.Add(new FeatureAction("dump", "Bluetooth: log devices", service.LogDevices));
                list.Add(new FeatureAction("simulate-connect", "Bluetooth: simulate a connect notice", service.SimulateConnect));
                list.Add(new FeatureAction("simulate-low", "Bluetooth: simulate low battery", service.SimulateLow));
            }

            return list;
        }
    }
}
