// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Windows.Networking.Connectivity;

namespace Trayloom.Features.SystemTools.Wifi;

/// <summary>State of the Wi-Fi radio as far as Trayloom cares.</summary>
internal enum WifiRadioState
{
    /// <summary>Not read yet, or no radio found.</summary>
    None,
    On,
    Off,

    /// <summary>Switched off with a hardware switch (Windows cannot turn it on).</summary>
    Disabled,
}

/// <summary>How a network is joined.</summary>
internal enum WifiSecurity
{
    Open,
    Password,
    Enterprise,
}

/// <summary>What Windows says about the internet behind the connected network.</summary>
internal enum WifiLevel
{
    Internet,
    NoInternet,
    SignInNeeded,
}

/// <summary>The connected network. <see cref="Ssid"/> is empty while location access hides names.</summary>
internal sealed record WifiConnection(string Ssid, int Bars, WifiLevel Level);

/// <summary>One row of the network list. <see cref="Native"/> is the WinRT network handed back to <c>ConnectAsync</c>.</summary>
internal sealed record WifiNetwork(string Ssid, int Bars, int Rssi, WifiSecurity Security, bool Saved, object? Native);

/// <summary>An immutable picture of everything the views draw; replaced as a whole on the UI thread.</summary>
internal sealed record WifiSnapshot
{
    /// <summary>False until the first read finished.</summary>
    public bool Loaded { get; init; }

    public bool HasAdapter { get; init; }

    public WifiRadioState Radio { get; init; }

    public WifiConnection? Connection { get; init; }

    /// <summary>The connected profile is a Wi-Fi one but Windows hid its name.</summary>
    public bool ConnectedWithoutName { get; init; }

    public IReadOnlyList<WifiNetwork> Networks { get; init; } = [];

    public bool Scanning { get; init; }

    public bool Scanned { get; init; }

    /// <summary>The SSID being joined right now (one connect at a time), or null.</summary>
    public string? ConnectingSsid { get; init; }

    /// <summary>Network names are hidden because location access is missing (spec 3.6).</summary>
    public bool LocationNeeded { get; init; }

    /// <summary>The name of <c>WiFiAccessStatus</c>, for the developer log only.</summary>
    public string Access { get; init; } = "Unknown";

    public bool Connected => Connection is not null || ConnectedWithoutName;
}

/// <summary>The list rules and glyph choices of spec 3.4.</summary>
internal static class WifiRules
{
    public const string LockGlyph = "";

    public const int MaxRows = 30;

    /// <summary>Open, password or enterprise, from the authentication type Windows reports.</summary>
    public static WifiSecurity SecurityOf(NetworkAuthenticationType type) => type switch
    {
        NetworkAuthenticationType.None or NetworkAuthenticationType.Open80211 or NetworkAuthenticationType.Owe => WifiSecurity.Open,
        NetworkAuthenticationType.SharedKey80211 or NetworkAuthenticationType.WpaPsk or NetworkAuthenticationType.RsnaPsk or NetworkAuthenticationType.Wpa3Sae => WifiSecurity.Password,
        _ => WifiSecurity.Enterprise,
    };

    /// <summary>True for WEP, whose passwords have other length rules.</summary>
    public static bool IsWep(NetworkAuthenticationType type) => type == NetworkAuthenticationType.SharedKey80211;

    /// <summary>Groups by SSID keeping the strongest entry, drops hidden and Wi-Fi Direct entries and orders the rows.</summary>
    public static List<WifiNetwork> Arrange(IEnumerable<WifiNetwork> found, string? connectedSsid, bool savedFirst)
    {
        var strongest = found
            .Where(n => n.Ssid.Length > 0 && !string.Equals(n.Ssid, connectedSsid, StringComparison.Ordinal))
            .GroupBy(n => n.Ssid, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(n => n.Bars).ThenByDescending(n => n.Rssi).First());
        var ordered = strongest
            .OrderBy(n => savedFirst && n.Saved ? 0 : 1)
            .ThenByDescending(n => n.Bars)
            .ThenByDescending(n => n.Rssi)
            .ThenBy(n => n.Ssid, StringComparer.CurrentCulture);
        return ordered.Take(MaxRows).ToList();
    }

    /// <summary>The signal glyph for 0-5 bars; <paramref name="radioOff"/> for an off radio or no adapter.</summary>
    public static string SignalGlyph(int bars, bool radioOff = false)
    {
        if (radioOff)
        {
            return "";
        }

        return bars switch
        {
            <= 1 => "",
            2 => "",
            3 => "",
            _ => "",
        };
    }

    /// <summary>The caption at the right of a list row.</summary>
    public static string Caption(WifiNetwork n) => n.Security switch
    {
        WifiSecurity.Enterprise => WifiStrings.NeedsWindowsSignIn,
        _ when n.Saved => WifiStrings.Saved,
        WifiSecurity.Open => WifiStrings.Open,
        _ => WifiStrings.Secured,
    };

    public static string LevelText(WifiLevel level) => level switch
    {
        WifiLevel.Internet => WifiStrings.LevelInternet,
        WifiLevel.SignInNeeded => WifiStrings.LevelSignIn,
        _ => WifiStrings.LevelNoInternet,
    };

    /// <summary>The orange caption for a failed connect (spec 3.7).</summary>
    public static string FailureText(WifiConnectOutcome outcome) => outcome switch
    {
        WifiConnectOutcome.InvalidCredential => WifiStrings.WrongPassword,
        WifiConnectOutcome.NetworkNotAvailable => WifiStrings.OutOfRange,
        WifiConnectOutcome.Timeout => WifiStrings.NoAnswer,
        WifiConnectOutcome.Unsupported => WifiStrings.WindowsJoinsItself,
        _ => WifiStrings.CouldNotConnect,
    };

    /// <summary>Whether <paramref name="password"/> can work for the security type (spec 3.7).</summary>
    public static bool PasswordLooksValid(string password, bool wep)
    {
        if (!wep)
        {
            return password.Length >= 8;
        }

        var hex = password.All(Uri.IsHexDigit);
        return password.Length is 5 or 13 || (hex && password.Length is 10 or 26);
    }
}
