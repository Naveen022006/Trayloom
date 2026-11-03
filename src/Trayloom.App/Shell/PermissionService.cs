// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>Status of a capability row.</summary>
internal enum PermissionState
{
    Granted,
    Missing,
    Unknown,
}

/// <summary>A capability status with an optional note shown under the status chip.</summary>
internal sealed record PermissionStatus(PermissionState State, string? Note = null);

/// <summary>
/// The Windows capability mapping of Vorssaint's permission rows (spec 02-settings 6.3): each row becomes a capability check,
/// "Request" becomes "Restart as administrator" / a privacy-settings jump.
/// </summary>
internal static class PermissionService
{
    /// <summary>Rows shown on Windows (Finder / playback automation are not permissions here).</summary>
    public static readonly AppPermission[] Rows =
    [
        AppPermission.Accessibility, AppPermission.ScreenRecording, AppPermission.FullDiskAccess, AppPermission.FilesAndFolders,
        AppPermission.Notifications, AppPermission.AudioCapture, AppPermission.Microphone, AppPermission.Camera,
        AppPermission.AppManagement, AppPermission.Calendar,
    ];

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static string Name(AppPermission p) => p switch
    {
        AppPermission.Accessibility => Loc.T("Accessibility"),
        AppPermission.ScreenRecording => Loc.T("Screen Recording"),
        AppPermission.FullDiskAccess => Loc.T("Administrator access"),
        AppPermission.FilesAndFolders => Loc.T("Files & Folders"),
        AppPermission.Notifications => Loc.T("Notifications"),
        AppPermission.AutomationFinder => Loc.T("Explorer automation"),
        AppPermission.AutomationPlayback => Loc.T("Playback automation"),
        AppPermission.AudioCapture => Loc.T("App audio"),
        AppPermission.Microphone => Loc.T("Microphone"),
        AppPermission.Camera => Loc.T("Camera"),
        AppPermission.AppManagement => Loc.T("App management"),
        AppPermission.Calendar => Loc.T("Calendar"),
        _ => p.ToString(),
    };

    public static string Explainer(AppPermission p) => p switch
    {
        AppPermission.Accessibility => Loc.T("Lets features react to clicks and keys, and move windows."),
        AppPermission.ScreenRecording => Loc.T("Lets features show window thumbnails and read text on screen."),
        AppPermission.FullDiskAccess => Loc.T("Lets the cleaner and the uninstaller find leftover files everywhere."),
        AppPermission.FilesAndFolders => Loc.T("Lets WhatsApp downloads cleanup and the experimental organizer inspect your Downloads folder."),
        AppPermission.Notifications => Loc.T("Lets the app notify you about alerts you turned on."),
        AppPermission.AutomationFinder => Loc.T("Lets the app ask File Explorer to move files for you."),
        AppPermission.AutomationPlayback => Loc.T("Some music apps require permission for their playback controls."),
        AppPermission.AudioCapture => Loc.T("Lets the mixer adjust each app’s volume, the Dynamic Island bars follow the music and screen recordings include the PC’s sound."),
        AppPermission.Microphone => Loc.T("Lets screen recordings include your voice when you turn it on."),
        AppPermission.Camera => Loc.T("Shows your camera only in the preview window, so you can check how you look before a call. Nothing is recorded or leaves your PC."),
        AppPermission.AppManagement => Loc.T("Lets updates replace or remove apps installed by the package manager."),
        AppPermission.Calendar => Loc.T("Read your calendars to show upcoming appointments. Events stay on this PC."),
        _ => string.Empty,
    };

    public static string Glyph(AppPermission p) => p switch
    {
        AppPermission.Accessibility => "",
        AppPermission.ScreenRecording => "",
        AppPermission.FullDiskAccess => "",
        AppPermission.FilesAndFolders => "",
        AppPermission.Notifications => "",
        AppPermission.AutomationFinder or AppPermission.AutomationPlayback => "",
        AppPermission.AudioCapture => "",
        AppPermission.Microphone => "",
        AppPermission.Camera => "",
        AppPermission.AppManagement => "",
        AppPermission.Calendar => "",
        _ => "",
    };

    public static PermissionStatus Check(AppPermission p)
    {
        try
        {
            switch (p)
            {
                case AppPermission.Accessibility:
                    return IsElevated
                        ? new PermissionStatus(PermissionState.Granted)
                        : new PermissionStatus(PermissionState.Granted, Loc.T("Limited: running without administrator rights. Windows that run as administrator ignore Trayloom until it is elevated too."));
                case AppPermission.ScreenRecording:
                case AppPermission.AudioCapture:
                    return new PermissionStatus(PermissionState.Granted);
                case AppPermission.FullDiskAccess:
                case AppPermission.AppManagement:
                    return IsElevated
                        ? new PermissionStatus(PermissionState.Granted)
                        : new PermissionStatus(PermissionState.Missing, Loc.T("Not granted. Windows asks (UAC) when something needs it."));
                case AppPermission.FilesAndFolders:
                    return ControlledFolderAccess() ? new PermissionStatus(PermissionState.Unknown) : new PermissionStatus(PermissionState.Granted);
                case AppPermission.Notifications:
                    return ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled") == 0
                        ? new PermissionStatus(PermissionState.Missing)
                        : new PermissionStatus(PermissionState.Granted);
                case AppPermission.Microphone:
                    return Consent("microphone");
                case AppPermission.Camera:
                    return Consent("webcam");
                case AppPermission.Calendar:
                    return Consent("appointments");
                default:
                    return new PermissionStatus(PermissionState.Granted);
            }
        }
        catch
        {
            return new PermissionStatus(PermissionState.Unknown);
        }
    }

    /// <summary>True when a request flow exists for a not-granted row.</summary>
    public static bool CanRequest(AppPermission p, PermissionStatus status) =>
        status.State != PermissionState.Granted && p is AppPermission.Accessibility or AppPermission.FullDiskAccess or AppPermission.AppManagement;

    public static string RequestLabel(AppPermission p) => p is AppPermission.Accessibility or AppPermission.FullDiskAccess or AppPermission.AppManagement
        ? Loc.T("Restart as administrator")
        : Loc.T("Request");

    public static void Request(AppPermission p)
    {
        if (p is AppPermission.Accessibility or AppPermission.FullDiskAccess or AppPermission.AppManagement)
        {
            RestartElevated();
        }
    }

    /// <summary>The Windows Settings URI for the row (null = nothing to open).</summary>
    public static string? SettingsUri(AppPermission p) => p switch
    {
        AppPermission.ScreenRecording => "ms-settings:privacy-graphicscaptureprogrammatic",
        AppPermission.FilesAndFolders => "windowsdefender://RansomwareProtection",
        AppPermission.Notifications => "ms-settings:notifications",
        AppPermission.Microphone => "ms-settings:privacy-microphone",
        AppPermission.Camera => "ms-settings:privacy-webcam",
        AppPermission.Calendar => "ms-settings:privacy-calendar",
        _ => null,
    };

    public static void OpenSettings(AppPermission p)
    {
        if (SettingsUri(p) is { } uri)
        {
            UiKit.OpenUrl(uri);
        }
    }

    /// <summary>Relaunches Trayloom with a UAC prompt (the current instance keeps running until the new one starts).</summary>
    public static void RestartElevated()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe, $"--relaunch {Environment.ProcessId}") { UseShellExecute = true, Verb = "runas" });
            Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        }
        catch
        {
            // user cancelled the UAC prompt
        }
    }

    private static PermissionStatus Consent(string capability)
    {
        var value = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}")?.GetValue("Value") as string;
        return value switch
        {
            "Deny" => new PermissionStatus(PermissionState.Missing),
            _ => new PermissionStatus(PermissionState.Granted),
        };
    }

    private static bool ControlledFolderAccess() =>
        ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access", "EnableControlledFolderAccess") == 1;

    private static int? ReadDword(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(name) as int?;
    }
}
