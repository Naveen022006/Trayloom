// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using Microsoft.Win32;
using Trayloom.Core;
using Windows.Devices.Radios;

namespace Trayloom.Features.SystemTools;

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
        new FeatureAction("panel", Loc.T("Display brightness"), () => _ctx?.Shell.OpenPanel(PanelTabs.Brightness), ""),
        new FeatureAction("brighter", Loc.T("Brightness up"), () => _service?.Step(+1, followPointer: true), ""),
        new FeatureAction("dimmer", Loc.T("Brightness down"), () => _service?.Step(-1, followPointer: true), ""),
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

    public string? Title => Loc.T("Displays");

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
            await PayDebtAsync(atStart: true);
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
                        var restore = ctx.App.Get<bool>("bluetoothSleepRestoreOnWake");

                        // On disk before the radio goes off: a crash or a shutdown while asleep must not leave Bluetooth off for good.
                        if (restore)
                        {
                            BluetoothSleepJournal.Write();
                        }

                        var result = await radio.SetStateAsync(RadioState.Off);
                        if (result == RadioAccessStatus.Allowed)
                        {
                            ctx.App.Set("bluetoothSleepRestorePending", restore);
                        }
                        else
                        {
                            BluetoothSleepJournal.Delete();
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
            _ = Task.Run(() => PayDebtAsync());
        }
    }

    /// <summary>
    /// Switches Bluetooth back on after sleep. At wake only this run's pending flag counts; at start a journal left by a crash or a
    /// shutdown while asleep counts too. Whatever happens, the debt for that sleep is settled and the journal is gone afterwards.
    /// </summary>
    private async Task PayDebtAsync(bool atStart = false)
    {
        var ctx = _ctx;
        if (ctx is null || !(ctx.App.Get<bool>("bluetoothSleepRestorePending") || (atStart && BluetoothSleepJournal.Exists())))
        {
            return;
        }

        await BluetoothSleepJournal.Gate.WaitAsync();
        try
        {
            await BluetoothSleepJournal.SettleAsync(AppPaths.FeatureData("bluetooth-sleep"), TurnOnIfOffAsync, 3, TimeSpan.FromSeconds(3), ctx.Log);
        }
        finally
        {
            ctx.App.Set("bluetoothSleepRestorePending", false);
            BluetoothSleepJournal.Gate.Release();
        }
    }

    /// <summary>True when the radio was found (switched on if it was off); false when Windows has not listed it yet.</summary>
    private static async Task<bool> TurnOnIfOffAsync()
    {
        var radio = await FindBluetoothAsync();
        if (radio is null)
        {
            return false;
        }

        if (radio.State == RadioState.Off)
        {
            await radio.SetStateAsync(RadioState.On);
        }

        return true;
    }

    internal static async Task<Radio?> FindBluetoothAsync()
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

/// <summary>The record that Trayloom switched Bluetooth off for sleep (<c>bt-pending.json</c>); cleared once the radio is back on.</summary>
internal static class BluetoothSleepJournal
{
    public const string FileName = "bt-pending.json";

    /// <summary>Shared by the wake-up payment and the recovery hook so the two never switch the radio at the same time.</summary>
    public static readonly SemaphoreSlim Gate = new(1, 1);

    public static string PathIn(string dir) => Path.Combine(dir, FileName);

    public static bool Exists() => File.Exists(PathIn(AppPaths.FeatureData("bluetooth-sleep")));

    public static void Write()
    {
        try
        {
            AtomicFile.WriteAllText(PathIn(AppPaths.FeatureData("bluetooth-sleep")), "{\"off\":true}");
        }
        catch (Exception)
        {
            // The pending flag in the settings still covers a normal wake.
        }
    }

    public static void Delete() => Delete(AppPaths.FeatureData("bluetooth-sleep"));

    public static void Delete(string dir)
    {
        try
        {
            File.Delete(PathIn(dir));
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Runs <paramref name="turnOn"/> up to <paramref name="attempts"/> times (the radio is often not listed for a moment after wake) and
    /// then deletes the journal whatever the outcome: once this run has handled the wake, the user is back in control and a failed attempt
    /// must never turn Bluetooth on at some later time.
    /// </summary>
    public static async Task SettleAsync(string dir, Func<Task<bool>> turnOn, int attempts, TimeSpan delay, ILog log)
    {
        try
        {
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    if (await turnOn())
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    log.Warn($"Bluetooth could not be restored: {ex.Message}");
                }

                if (i < attempts - 1)
                {
                    await Task.Delay(delay);
                }
            }
        }
        finally
        {
            Delete(dir);
        }
    }
}

/// <summary>
/// Switches Bluetooth back on when the journal says Trayloom turned it off for sleep and never turned it back on (a crash, a kill, or a
/// shutdown while asleep), at the next start and on uninstall. Without a journal it does nothing. Bounded to five seconds.
/// </summary>
public sealed class BluetoothRecovery : IStartupRecovery
{
    private readonly Func<string> _dataDir;
    private readonly Func<Task<bool>> _turnOn;

    public BluetoothRecovery()
        : this(() => AppPaths.FeatureData("bluetooth-sleep"), TurnRadioOnAsync)
    {
    }

    internal BluetoothRecovery(Func<string> dataDir, Func<Task<bool>> turnOn)
    {
        _dataDir = dataDir;
        _turnOn = turnOn;
    }

    public string Id => "bluetooth-sleep";

    public void Recover(RecoveryStage stage, ILog log)
    {
        // The crash stage has a 3 second budget and Bluetooth is not urgent: the next normal start does it.
        if (stage == RecoveryStage.Crash)
        {
            return;
        }

        var path = BluetoothSleepJournal.PathIn(_dataDir());
        if (!File.Exists(path))
        {
            return;
        }

        if (!BluetoothSleepJournal.Gate.Wait(1500))
        {
            log.Warn("bluetooth-sleep: busy; Bluetooth is switched back on at the next start.");
            return;
        }

        try
        {
            var task = Task.Run(_turnOn);
            if (task.Wait(1500) && task.Result)
            {
                File.Delete(path);
            }
            else
            {
                log.Warn("bluetooth-sleep: Bluetooth could not be switched back on; it is tried again at the next start.");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"bluetooth-sleep: {ex.Message}");
        }
        finally
        {
            BluetoothSleepJournal.Gate.Release();
        }
    }

    private static async Task<bool> TurnRadioOnAsync()
    {
        var radio = await BluetoothSleepFeature.FindBluetoothAsync();
        if (radio is null)
        {
            return false;
        }

        if (radio.State == RadioState.On)
        {
            return true;
        }

        return radio.State == RadioState.Off && await radio.SetStateAsync(RadioState.On) == RadioAccessStatus.Allowed;
    }
}

/// <summary>Energy page: the Bluetooth on sleep card (anchor <c>bluetoothSleep</c>).</summary>
public sealed class BluetoothSleepSettingsSection : ISettingsSection
{
    public string? FeatureId => "bluetooth-sleep";

    public string PageId => SettingsPages.Energy;

    public string? Anchor => "bluetoothSleep";

    public string? Title => Loc.T("Bluetooth on sleep");

    public int Order => 300;

    public IReadOnlyList<string> Keywords => ["turn bluetooth off when the pc sleeps", "wakes", "headphones"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        SystemDefaults.Register(c.App);
        var app = c.App;
        var card = new Trayloom.Core.Ui.SettingsCard();
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

                _ = c.Ui.BeginInvoke(c.RequestRebuild);
            });
        }

        if (detected == false)
        {
            card.Add(new Trayloom.Core.Ui.SettingsRow(Gl.Bluetooth, Strings.Bluetooth.Title, Strings.Bluetooth.NoController));
            return card;
        }

        card.Add(new Trayloom.Core.Ui.SettingsRow(Gl.Bluetooth, Strings.Bluetooth.SleepRow, Strings.Bluetooth.SleepCaption, new Trayloom.Core.Ui.MacSwitch(app.Get<bool>("bluetoothSleepEnabled"), Trayloom.Core.Ui.SwitchSize.Regular, on =>
        {
            app.Set("bluetoothSleepEnabled", on);
            c.RequestRebuild();
        })));
        if (app.Get<bool>("bluetoothSleepEnabled"))
        {
            card.AddDivider();
            card.Add(new Trayloom.Core.Ui.SettingsRow(Gl.Reset, Strings.Bluetooth.WakeRow, Strings.Bluetooth.WakeCaption, new Trayloom.Core.Ui.MacSwitch(app.Get<bool>("bluetoothSleepRestoreOnWake"), Trayloom.Core.Ui.SwitchSize.Regular, on => app.Set("bluetoothSleepRestoreOnWake", on))));
        }

        return card;
    }
}
