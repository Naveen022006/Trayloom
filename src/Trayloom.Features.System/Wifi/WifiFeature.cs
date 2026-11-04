// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using Trayloom.Core;
using Windows.Devices.Radios;

namespace Trayloom.Features.SystemTools.Wifi;

/// <summary>Settings keys of the Wi-Fi feature.</summary>
internal static class WifiKeys
{
    public const string FeatureId = "notch-wifi";

    public const string ShowSaved = "notchWifiShowSaved";

    /// <summary>Registers the default (safe to call many times).</summary>
    public static void Register(IAppSettings app) => app.Register(ShowSaved, true);

    /// <summary>Opens a Windows settings page or flyout; failures are ignored (the page is a convenience).</summary>
    public static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // no handler for the scheme on this PC
        }
    }
}

/// <summary>
/// Wi-Fi in the Dynamic Island, the tray's Network tab and the Command Bar (spec 06 section 3). Nothing is read from Windows until a view
/// asks; <see cref="Stop"/> withdraws the service and drops every subscription.
/// </summary>
public sealed class WifiFeature : IFeature
{
    private IFeatureContext? _context;
    private WifiService? _service;

    public string Id => WifiKeys.FeatureId;

    public void Start(IFeatureContext context)
    {
        _context = context;
        WifiKeys.Register(context.App);
        _service = new WifiService(context);
        _service.Start();
        context.Services.Provide(_service);

        // Radio presence only (no network names, no location prompt), so the toggle action knows whether to exist.
        WifiGuard.FireAndLog(_service.PeekRadioAsync, context.Log, "radio peek");
    }

    public void Stop()
    {
        if (_service is { } service)
        {
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
            if (_service is not { } service || _context is not { } context)
            {
                return [];
            }

            var snapshot = service.Snapshot;
            var list = new List<FeatureAction>();
            if (snapshot.Radio is WifiRadioState.On or WifiRadioState.Off)
            {
                var on = snapshot.Radio == WifiRadioState.On;
                list.Add(new FeatureAction("toggle-radio", on ? WifiStrings.TurnWifiOff : WifiStrings.TurnWifiOn, () => ToggleRadio(service, context, !on), ""));
            }

            if (service.CheapConnected())
            {
                list.Add(new FeatureAction("disconnect", WifiStrings.DisconnectFromWifi, () => WifiGuard.FireAndLog(service.DisconnectAsync, context.Log, "disconnect"), ""));
            }

            list.Add(new FeatureAction("settings", WifiStrings.ActionSettings, () => WifiKeys.OpenUri("ms-settings:network-wifi"), ""));

            // Developer aids: they read, never change, anything (spec 0.3.2 forbids exercising the radio and disconnect in automation).
            if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1")
            {
                list.Add(new FeatureAction("scan", "Wi-Fi: scan once", () => WifiGuard.FireAndLog(async () =>
                {
                    await service.LoadAsync(false);
                    await service.ScanAsync(true);
                    service.LogScan();
                }, context.Log, "scan")));
                list.Add(new FeatureAction("state", "Wi-Fi: log state", () => WifiGuard.FireAndLog(async () =>
                {
                    await service.RefreshAsync();
                    service.LogState();
                }, context.Log, "state")));
                list.Add(new FeatureAction("dump", "Wi-Fi: write dump.json", () => WifiGuard.FireAndLog(async () =>
                {
                    await service.RefreshAsync();
                    service.Dump();
                }, context.Log, "dump")));
            }

            return list;
        }
    }

    /// <summary>Switches the radio on request and reports a refusal in the log (the island page shows it too when it is open).</summary>
    internal static void ToggleRadio(WifiService service, IContributionServices context, bool on)
    {
        WifiGuard.FireAndLog(async () =>
        {
            var status = await service.SetRadioAsync(on);
            if (status != RadioAccessStatus.Allowed)
            {
                context.Log.Warn($"[wifi] radio change refused: {status}");
            }
        }, context.Log, "radio change");
    }
}
