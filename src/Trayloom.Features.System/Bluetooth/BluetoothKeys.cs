// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using Trayloom.Core;

namespace Trayloom.Features.SystemTools.Bluetooth;

/// <summary>Settings keys of the Bluetooth feature and the few Windows pages it opens.</summary>
internal static class BtKeys
{
    public const string FeatureId = "notch-bluetooth";

    public const string ConnectAlerts = "notchBluetoothConnectAlerts";

    public const string LowAlerts = "notchBluetoothLowAlerts";

    public const string LowThreshold = "notchBluetoothLowThreshold";

    public const string LowStrip = "notchBluetoothLowStrip";

    /// <summary><c>all</c>, <c>connected</c> or <c>battery</c>.</summary>
    public const string Show = "notchBluetoothShow";

    /// <summary>Comma-separated device keys the person unticked in Settings.</summary>
    public const string Hidden = "notchBluetoothHidden";

    /// <summary>The levels the "Warn at" picker offers.</summary>
    public static readonly int[] Thresholds = [10, 15, 20, 30];

    /// <summary>Registers the defaults (safe to call many times).</summary>
    public static void Register(IAppSettings app)
    {
        app.Register(ConnectAlerts, true);
        app.Register(LowAlerts, true);
        app.Register(LowThreshold, 20);
        app.Register(LowStrip, true);
        app.Register(Show, "all");
        app.Register(Hidden, string.Empty);
    }

    /// <summary>The stored warning level, or 20 when the stored number is not one the picker offers.</summary>
    public static int Threshold(IAppSettings app)
    {
        var value = app.Get(LowThreshold, 20);
        return Array.IndexOf(Thresholds, value) >= 0 ? value : 20;
    }

    /// <summary>The device keys the person hid.</summary>
    public static HashSet<string> HiddenSet(IAppSettings app) =>
        [.. app.Get(Hidden, string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Stores the hidden keys (sorted, so an unchanged set saves nothing new).</summary>
    public static void SetHidden(IAppSettings app, IEnumerable<string> keys) =>
        app.Set(Hidden, string.Join(',', keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));

    /// <summary>Opens a Windows settings page; failures are ignored (the page is a convenience).</summary>
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

    /// <summary>Windows Settings, Bluetooth &amp; devices.</summary>
    public static void OpenSettings() => OpenUri("ms-settings:bluetooth");

    /// <summary>
    /// "Add a device". The page documented for Settings is used for it too: the Add-a-device dialog has no documented address, and
    /// the Settings page opens with the same button on top.
    /// </summary>
    public static void OpenAddDevice() => OpenSettings();

    /// <summary>
    /// "Connect…". Windows 11's Quick Settings Bluetooth list has no documented address either, so the Settings page, which lists every
    /// paired device with a Connect button, is what opens.
    /// </summary>
    public static void OpenConnect() => OpenSettings();
}
