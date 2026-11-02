// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Tools;

/// <summary>Static tables of the Command Bar: Windows Settings pages (spec 1.11.4) and window-layout titles (1.11.3).</summary>
internal static class BarData
{
    public static (string Name, string Uri, string Keywords)[] SettingsPanes => _loc_SettingsPanes.Value;

    private static readonly LocValue<(string Name, string Uri, string Keywords)[]> _loc_SettingsPanes = new(() => [
        (Loc.T("Display"), "ms-settings:display", "screen monitor resolution scale orientation refresh rate brightness night light hdr"),
        (Loc.T("Night light"), "ms-settings:nightlight", "blue light warm"),
        (Loc.T("Advanced display"), "ms-settings:display-advanced", "refresh rate hz"),
        ("Graphics", "ms-settings:display-advancedgraphics", "gpu"),
        (Loc.T("Sound"), "ms-settings:sound", "volume speakers microphone input output audio"),
        (Loc.T("Volume mixer"), "ms-settings:apps-volume", "app volume"),
        (Loc.T("Notifications"), "ms-settings:notifications", "alerts focus do not disturb"),
        (Loc.T("Focus"), "ms-settings:quiethours", "focus assist"),
        (Loc.T("Power & battery"), "ms-settings:powersleep", "sleep screen timeout power mode"),
        (Loc.T("Battery saver"), "ms-settings:batterysaver", "energy"),
        (Loc.T("Storage"), "ms-settings:storagesense", "disk space cleanup storage sense"),
        ("Multitasking", "ms-settings:multitasking", "snap windows desktops"),
        (Loc.T("Clipboard"), "ms-settings:clipboard", "history"),
        (Loc.T("Nearby sharing"), "ms-settings:crossdevice", "share"),
        (Loc.T("Remote Desktop"), "ms-settings:remotedesktop", "rdp"),
        (Loc.T("System / About"), "ms-settings:about", "device name specs version"),
        ("Troubleshoot", "ms-settings:troubleshoot", ""),
        ("Recovery", "ms-settings:recovery", "reset pc"),
        ("Activation", "ms-settings:activation", "licence key"),
        (Loc.T("Bluetooth & devices"), "ms-settings:bluetooth", "pair"),
        (Loc.T("Printers & scanners"), "ms-settings:printers", "print"),
        ("Mouse", "ms-settings:mousetouchpad", "pointer speed buttons scroll"),
        ("Touchpad", "ms-settings:devices-touchpad", "gestures"),
        ("Typing", "ms-settings:typing", "autocorrect spelling"),
        (Loc.T("Pen & Windows Ink"), "ms-settings:pen", "stylus"),
        ("AutoPlay", "ms-settings:autoplay", ""),
        (Loc.T("USB"), "ms-settings:usb", ""),
        ("Cameras", "ms-settings:camera", "webcam"),
        (Loc.T("Network & internet"), "ms-settings:network-status", "connection"),
        ("Wi-Fi", "ms-settings:network-wifi", "wireless"),
        ("Ethernet", "ms-settings:network-ethernet", "cable lan"),
        ("VPN", "ms-settings:network-vpn", ""),
        (Loc.T("Airplane mode"), "ms-settings:network-airplanemode", "flight radios"),
        (Loc.T("Mobile hotspot"), "ms-settings:network-mobilehotspot", "tethering"),
        ("Proxy", "ms-settings:network-proxy", ""),
        ("Personalization", "ms-settings:personalization", ""),
        (Loc.T("Background"), "ms-settings:personalization-background", "wallpaper desktop"),
        ("Colors", "ms-settings:colors", "accent dark mode light mode theme"),
        ("Themes", "ms-settings:themes", ""),
        (Loc.T("Lock screen"), "ms-settings:lockscreen", ""),
        (Loc.T("Start"), "ms-settings:personalization-start", "menu"),
        (Loc.T("Taskbar"), "ms-settings:taskbar", "tray icons"),
        ("Fonts", "ms-settings:fonts", ""),
        (Loc.T("Installed apps"), "ms-settings:appsfeatures", "uninstall programs"),
        (Loc.T("Default apps"), "ms-settings:defaultapps", "file types browser"),
        (Loc.T("Optional features"), "ms-settings:optionalfeatures", ""),
        (Loc.T("Startup apps"), "ms-settings:startupapps", "login items autostart"),
        (Loc.T("Your info"), "ms-settings:yourinfo", "account profile"),
        (Loc.T("Sign-in options"), "ms-settings:signinoptions", "password pin hello fingerprint"),
        (Loc.T("Email & accounts"), "ms-settings:emailandaccounts", ""),
        (Loc.T("Other users"), "ms-settings:otherusers", "family"),
        (Loc.T("Access work or school"), "ms-settings:workplace", ""),
        (Loc.T("Date & time"), "ms-settings:dateandtime", "time zone clock"),
        (Loc.T("Language & region"), "ms-settings:regionlanguage", "keyboard layout"),
        ("Region", "ms-settings:regionformatting", "formats"),
        ("Speech", "ms-settings:speech", "voice"),
        (Loc.T("Game Bar"), "ms-settings:gaming-gamebar", "xbox"),
        (Loc.T("Game Mode"), "ms-settings:gaming-gamemode", ""),
        (Loc.T("Captures"), "ms-settings:gaming-captures", ""),
        (Loc.T("Accessibility"), "ms-settings:easeofaccess", ""),
        (Loc.T("Text size"), "ms-settings:easeofaccess-display", ""),
        (Loc.T("Mouse pointer & touch"), "ms-settings:easeofaccess-cursorandpointersize", "cursor"),
        ("Magnifier", "ms-settings:easeofaccess-magnifier", "zoom"),
        (Loc.T("Color filters"), "ms-settings:easeofaccess-colorfilter", "color blind"),
        (Loc.T("Contrast themes"), "ms-settings:easeofaccess-highcontrast", ""),
        ("Narrator", "ms-settings:easeofaccess-narrator", "screen reader"),
        (Loc.T("Closed captions"), "ms-settings:easeofaccess-closedcaptioning", ""),
        (Loc.T("Keyboard accessibility"), "ms-settings:easeofaccess-keyboard", "sticky keys"),
        (Loc.T("Privacy & security"), "ms-settings:privacy", ""),
        ("Location", "ms-settings:privacy-location", "gps"),
        (Loc.T("Camera privacy"), "ms-settings:privacy-webcam", ""),
        (Loc.T("Microphone privacy"), "ms-settings:privacy-microphone", ""),
        (Loc.T("Windows Update"), "ms-settings:windowsupdate", "updates"),
        (Loc.T("Update history"), "ms-settings:windowsupdate-history", ""),
        (Loc.T("Delivery Optimization"), "ms-settings:delivery-optimization", ""),
        (Loc.T("Windows Security"), "ms-settings:windowsdefender", "antivirus defender firewall"),
        (Loc.T("Backup"), "ms-settings:backup", ""),
        (Loc.T("Find my device"), "ms-settings:findmydevice", ""),
        (Loc.T("For developers"), "ms-settings:developers", "developer mode"),
        (Loc.T("Windows Insider Program"), "ms-settings:windowsinsider", ""),
        (Loc.T("Device Manager"), "devmgmt.msc", "hardware drivers"),
        (Loc.T("Disk Management"), "diskmgmt.msc", "partitions volumes"),
        ("Services", "services.msc", ""),
        (Loc.T("Task Manager"), "taskmgr", "processes performance"),
        (Loc.T("Control Panel"), "control", ""),
        (Loc.T("Network connections"), "ncpa.cpl", "adapters"),
        (Loc.T("Programs & Features"), "appwiz.cpl", "uninstall"),
        (Loc.T("System properties"), "sysdm.cpl", "environment variables"),
        (Loc.T("Event Viewer"), "eventvwr.msc", "logs"),
    ]);

    /// <summary>Window-layout rows in WindowLayoutAction.allCases order: raw id, title (spec 1.11.3).</summary>
    public static (string Raw, string Title)[] LayoutActions => _loc_LayoutActions.Value;

    private static readonly LocValue<(string Raw, string Title)[]> _loc_LayoutActions = new(() => [
        ("leftHalf", Loc.T("Left")), ("rightHalf", Loc.T("Right")), ("topHalf", Loc.T("Top")), ("bottomHalf", Loc.T("Bottom")), ("centerHalf", Loc.T("Center half")),
        ("leftThird", Loc.T("Left 1/3")), ("centerThird", Loc.T("Center 1/3")), ("rightThird", Loc.T("Right 1/3")), ("leftTwoThirds", Loc.T("Left 2/3")),
        ("rightTwoThirds", Loc.T("Right 2/3")), ("centerTwoThirds", Loc.T("Center 2/3")), ("topThird", Loc.T("Top 1/3")), ("middleThird", Loc.T("Middle 1/3")),
        ("bottomThird", Loc.T("Bottom 1/3")), ("topTwoThirds", Loc.T("Top 2/3")), ("bottomTwoThirds", Loc.T("Bottom 2/3")), ("topQuarter", Loc.T("Top 1/4")),
        ("upperMiddleQuarter", Loc.T("Upper middle 1/4")), ("lowerMiddleQuarter", Loc.T("Lower middle 1/4")), ("bottomQuarter", Loc.T("Bottom 1/4")),
        ("leftQuarter", Loc.T("Left 1/4")), ("leftMiddleQuarter", Loc.T("Left middle 1/4")), ("rightMiddleQuarter", Loc.T("Right middle 1/4")),
        ("rightQuarter", Loc.T("Right 1/4")), ("topLeftSixth", Loc.T("Top left 1/6")), ("topCenterSixth", Loc.T("Top center 1/6")),
        ("topRightSixth", Loc.T("Top right 1/6")), ("bottomLeftSixth", Loc.T("Bottom left 1/6")), ("bottomCenterSixth", Loc.T("Bottom center 1/6")),
        ("bottomRightSixth", Loc.T("Bottom right 1/6")), ("topLeft", Loc.T("Top left")), ("topRight", Loc.T("Top right")), ("bottomLeft", Loc.T("Bottom left")),
        ("bottomRight", Loc.T("Bottom right")), ("maximize", Loc.T("Maximize")), ("marginMaximize", Loc.T("Maximize with Margin")), ("fullScreen", Loc.T("Full Screen")),
        ("center", Loc.T("Center")), ("previousDisplay", Loc.T("Previous display")), ("nextDisplay", Loc.T("Next display")), ("restore", Loc.T("Restore")),
    ]);

    /// <summary>The six skin-tone raw values.</summary>
    public static readonly string[] SkinTones = ["", "light", "mediumLight", "medium", "mediumDark", "dark"];

    public static readonly string[] SkinToneModifiers = ["", "\U0001F3FB", "\U0001F3FC", "\U0001F3FD", "\U0001F3FE", "\U0001F3FF"];
}
