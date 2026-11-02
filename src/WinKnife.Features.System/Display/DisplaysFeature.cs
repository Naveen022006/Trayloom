// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using Microsoft.Win32;
using WinKnife.Core;
using Windows.Devices.Radios;

namespace WinKnife.Features.SystemTools;

/// <summary>Displays: brightness for the built-in panel (WMI) and external monitors (DDC/CI), dimming, on / off and shortcuts.</summary>
public sealed class DisplaysFeature : IFeature
{
    private IFeatureContext? _ctx;
    private DisplayService? _service;
    private IDisposable? _down;
    private IDisposable? _up;

    public string Id => "brightness";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("panel", "Display brightness", () => _ctx?.Shell.OpenPanel(PanelTabs.Brightness), ""),
        new FeatureAction("brighter", "Brightness up", () => _service?.Step(+1, followPointer: true), ""),
        new FeatureAction("dimmer", "Brightness down", () => _service?.Step(-1, followPointer: true), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _ctx = context;
        SystemDefaults.Register(context.App);
        _service = new DisplayService(context);
        _service.Start();
        _down = context.Shortcuts.Bind("displayBrightnessDecrease", () => _service?.Step(-1, context.App.Get<bool>("brightnessKeysEnabled")));
        _up = context.Shortcuts.Bind("displayBrightnessIncrease", () => _service?.Step(+1, context.App.Get<bool>("brightnessKeysEnabled")));
    }

    public void Stop()
    {
        _down?.Dispose();
        _down = null;
        _up?.Dispose();
        _up = null;
        _service?.Stop();
        _service = null;
        _ctx = null;
    }
}

/// <summary>DISPLAYS tab of the tray panel (visible while <c>brightnessControlEnabled</c>).</summary>
public sealed class DisplaysPanelSection : IPanelSection
{
    public string? FeatureId => "brightness";

    public string TabId => PanelTabs.Brightness;

    public FrameworkElement CreateView(IPanelContext context) => DisplayViews.PanelCard(context);
}

/// <summary>Energy page: the Displays card (anchor <c>brightness</c>).</summary>
public sealed class DisplaysSettingsSection : ISettingsSection
{
    public string? FeatureId => "brightness";

    public string PageId => SettingsPages.Energy;

    public string? Anchor => "brightness";

    public string? Title => "Displays";

    public int Order => 100;

    public IReadOnlyList<string> Keywords =>
    [
        "control displays", "brightness", "extra dimming", "dim the picture", "turn off display", "more options", "brightness key steps",
        "display brightness shortcuts", "show brightness when adjusting", "ddc",
    ];

    public FrameworkElement CreateView(ISettingsContext context) => DisplayViews.SettingsCard(context);
}

/// <summary>
/// Bluetooth on sleep (spec 05 E4): switches the Bluetooth radio off when the PC sleeps and back on at wake, only when this app switched it
/// off. The debt is a persisted flag so a PC shut down while asleep still gets Bluetooth back at the next launch.
/// </summary>
public sealed class BluetoothSleepFeature : IFeature
{
    private IFeatureContext? _ctx;

    /// <summary>Null until detection finished.</summary>
    public static bool? HasController { get; private set; }

    public string Id => "bluetooth-sleep";

    public void Start(IFeatureContext context)
    {
        _ctx = context;
        SystemDefaults.Register(context.App);
        SystemEvents.PowerModeChanged += OnPower;
        _ = Task.Run(async () =>
        {
            try
            {
                var radios = await Radio.GetRadiosAsync();
                HasController = radios.Any(r => r.Kind == RadioKind.Bluetooth);
            }
            catch (Exception)
            {
                HasController = false;
            }

            // Pay the debt left by a previous run (also when the feature has since been turned off).
            await PayDebtAsync();
        });
    }

    public void Stop()
    {
        SystemEvents.PowerModeChanged -= OnPower;
        _ctx = null;
    }

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        var ctx = _ctx;
        if (ctx is null)
        {
            return;
        }

        if (e.Mode == PowerModes.Suspend && ctx.App.Get<bool>("bluetoothSleepEnabled"))
        {
            // The suspend notification only allows about two seconds: switch off right away.
            try
            {
                Task.Run(async () =>
                {
                    var radio = await FindBluetoothAsync();
                    if (radio is not null && radio.State == RadioState.On)
                    {
                        var result = await radio.SetStateAsync(RadioState.Off);
                        if (result == RadioAccessStatus.Allowed)
                        {
                            ctx.App.Set("bluetoothSleepRestorePending", ctx.App.Get<bool>("bluetoothSleepRestoreOnWake"));
                        }
                    }
                }).Wait(1800);
            }
            catch (Exception ex)
            {
                ctx.Log.Warn($"Bluetooth could not be switched off for sleep: {ex.Message}");
            }
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _ = Task.Run(PayDebtAsync);
        }
    }

    private async Task PayDebtAsync()
    {
        var ctx = _ctx;
        if (ctx is null || !ctx.App.Get<bool>("bluetoothSleepRestorePending"))
        {
            return;
        }

        try
        {
            var radio = await FindBluetoothAsync();
            if (radio is not null && radio.State == RadioState.Off)
            {
                await radio.SetStateAsync(RadioState.On);
            }
        }
        catch (Exception ex)
        {
            ctx.Log.Warn($"Bluetooth could not be restored: {ex.Message}");
        }
        finally
        {
            // Bluetooth the user switched on meanwhile cancels the debt; the flag is always cleared.
            ctx.App.Set("bluetoothSleepRestorePending", false);
        }
    }

    private static async Task<Radio?> FindBluetoothAsync()
    {
        var access = await Radio.RequestAccessAsync();
        if (access != RadioAccessStatus.Allowed)
        {
            return null;
        }

        var radios = await Radio.GetRadiosAsync();
        return radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
    }
}

/// <summary>Energy page: the Bluetooth on sleep card (anchor <c>bluetoothSleep</c>).</summary>
public sealed class BluetoothSleepSettingsSection : ISettingsSection
{
    public string? FeatureId => "bluetooth-sleep";

    public string PageId => SettingsPages.Energy;

    public string? Anchor => "bluetoothSleep";

    public string? Title => "Bluetooth on sleep";

    public int Order => 300;

    public IReadOnlyList<string> Keywords => ["turn bluetooth off when the pc sleeps", "wakes", "headphones"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        SystemDefaults.Register(c.App);
        var app = c.App;
        var card = new WinKnife.Core.Ui.SettingsCard();
        var detected = BluetoothSleepFeature.HasController;
        if (detected is null)
        {
            // Detection is still running: show the row and re-render once it is known.
            _ = Task.Run(async () =>
            {
                for (var i = 0; i < 20 && BluetoothSleepFeature.HasController is null; i++)
                {
                    await Task.Delay(150);
                }

                c.Ui.BeginInvoke(c.RequestRebuild);
            });
        }

        if (detected == false)
        {
            card.Add(new WinKnife.Core.Ui.SettingsRow(Gl.Bluetooth, Strings.Bluetooth.Title, Strings.Bluetooth.NoController));
            return card;
        }

        card.Add(new WinKnife.Core.Ui.SettingsRow(Gl.Bluetooth, Strings.Bluetooth.SleepRow, Strings.Bluetooth.SleepCaption, new WinKnife.Core.Ui.MacSwitch(app.Get<bool>("bluetoothSleepEnabled"), WinKnife.Core.Ui.SwitchSize.Regular, on =>
        {
            app.Set("bluetoothSleepEnabled", on);
            c.RequestRebuild();
        })));
        if (app.Get<bool>("bluetoothSleepEnabled"))
        {
            card.AddDivider();
            card.Add(new WinKnife.Core.Ui.SettingsRow(Gl.Reset, Strings.Bluetooth.WakeRow, Strings.Bluetooth.WakeCaption, new WinKnife.Core.Ui.MacSwitch(app.Get<bool>("bluetoothSleepRestoreOnWake"), WinKnife.Core.Ui.SwitchSize.Regular, on => app.Set("bluetoothSleepRestoreOnWake", on))));
        }

        return card;
    }
}
