// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.SystemTools.Wifi;

/// <summary>English strings of the Wi-Fi feature (spec 06 section 3), verbatim.</summary>
internal static class WifiStrings
{
    public static string Title => Loc.T("Wi-Fi");

    public static string Description => Loc.T("See your network and its signal, turn Wi-Fi on or off and join nearby networks from the Dynamic Island.");

    public static string Summary => Loc.T("Your network, its signal and the networks nearby.");

    // ---- island page
    public static string Disconnect => Loc.T("Disconnect");

    public static string NotConnected => Loc.T("Not connected");

    public static string Connected => Loc.T("Connected");

    public static string ConnectedLevel(string level) => Loc.F("Connected · {0}", level);

    public static string ConnectingTo(string ssid) => Loc.F("Connecting to {0}…", ssid);

    public static string Connecting => Loc.T("Connecting…");

    public static string LevelInternet => Loc.T("Internet");

    public static string LevelNoInternet => Loc.T("No internet");

    public static string LevelSignIn => Loc.T("Sign-in needed");

    public static string Networks => Loc.T("Networks");

    public static string SearchAgain => Loc.T("Search again");

    public static string Searching => Loc.T("Searching for networks…");

    public static string NoNetworks => Loc.T("No networks found.");

    public static string WifiSettings => Loc.T("Wi-Fi settings…");

    public static string MoreNetworks => Loc.T("More networks…");

    public static string Saved => Loc.T("Saved");

    public static string Open => Loc.T("Open");

    public static string Secured => Loc.T("Secured");

    public static string NeedsWindowsSignIn => Loc.T("Needs Windows sign-in");

    public static string WifiOff => Loc.T("Wi-Fi is off.");

    public static string TurnOn => Loc.T("Turn on");

    public static string Off => Loc.T("Off");

    public static string HardwareOff => Loc.T("Wi-Fi is turned off with a switch on the PC.");

    public static string NoAdapter => Loc.T("No Wi-Fi adapter found.");

    public static string NoAdapterHint => Loc.T("Plug in or turn on a Wi-Fi adapter.");

    public static string LocationNeeded => Loc.T("Wi-Fi names need location access.");

    public static string OpenLocationSettings => Loc.T("Open location settings");

    public static string RadioDenied => Loc.T("Windows did not let Trayloom switch Wi-Fi. Use Wi-Fi settings instead.");

    // ---- location captions (3.6)
    public static string LocationOffPc => Loc.T("Location is off on this PC. Windows shows Wi-Fi names only to apps that may use location, because nearby networks can reveal where you are. Trayloom never reads or stores your location.");

    public static string LocationOffDesktop => Loc.T("Location is off for desktop apps. Turn on “Let desktop apps access your location” to see Wi-Fi names here. Trayloom never reads or stores your location.");

    public static string LocationGeneric => Loc.T("Windows did not share Wi-Fi names with Trayloom. Allow location for desktop apps, then search again.");

    // ---- connecting (3.7)
    public static string OpenNetworkNote => Loc.T("Open network. Others nearby can see what you send.");

    public static string JoinInWindows => Loc.T("Join this network in Windows: it needs a sign-in Windows handles.");

    public static string WrongPassword => Loc.T("Wrong password.");

    public static string OutOfRange => Loc.T("The network is out of range.");

    public static string NoAnswer => Loc.T("The network did not answer.");

    public static string WindowsJoinsItself => Loc.T("Windows needs to join this network itself.");

    public static string CouldNotConnect => Loc.T("Couldn’t connect.");

    public static string DisconnectedNote => Loc.T("Disconnected. Windows may reconnect to it later.");

    // ---- password window
    public static string JoinTitle(string ssid) => Loc.F("Join {0}", ssid);

    public static string EnterPassword(string ssid) => Loc.F("Enter the password for “{0}”.", ssid);

    public static string ConnectAutomatically => Loc.T("Connect automatically");

    public static string Cancel => Loc.T("Cancel");

    public static string Connect => Loc.T("Connect");

    public static string WrongPasswordTryAgain => Loc.T("Wrong password. Try again.");

    // ---- tray card
    public static string NetworksEllipsis => Loc.T("Networks…");

    public static string AllowLocation => Loc.T("Allow location to see Wi-Fi names");

    // ---- settings
    public static string SettingsRowTitle => Loc.T("Wi-Fi in the island");

    public static string SettingsRowCaption => Loc.T("Shows your network and the networks nearby on the island’s Wi-Fi page. Trayloom asks Windows for nearby networks only while that page is open, and never stores network names.");

    public static string ShowSavedFirst => Loc.T("Show saved networks first");

    public static string OpenWifiSettings => Loc.T("Open Wi-Fi settings");

    // ---- actions
    public static string TurnWifiOff => Loc.T("Turn Wi-Fi off");

    public static string TurnWifiOn => Loc.T("Turn Wi-Fi on");

    public static string DisconnectFromWifi => Loc.T("Disconnect from Wi-Fi");

    public static string ActionSettings => Loc.T("Wi-Fi settings");
}
