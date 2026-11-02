// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

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
        AppPermission.Accessibility => "Accessibility",
        AppPermission.ScreenRecording => "Screen Recording",
        AppPermission.FullDiskAccess => "Administrator access",
        AppPermission.FilesAndFolders => "Files & Folders",
        AppPermission.Notifications => "Notifications",
        AppPermission.AutomationFinder => "Explorer automation",
        AppPermission.AutomationPlayback => "Playback automation",
        AppPermission.AudioCapture => "App audio",
        AppPermission.Microphone => "Microphone",
        AppPermission.Camera => "Camera",
        AppPermission.AppManagement => "App management",
        AppPermission.Calendar => "Calendar",
        _ => p.ToString(),
    };

    public static string Explainer(AppPermission p) => p switch
    {
        AppPermission.Accessibility => "Lets features react to clicks and keys, and move windows.",
        AppPermission.ScreenRecording => "Lets features show window thumbnails and read text on screen.",
        AppPermission.FullDiskAccess => "Lets the cleaner and the uninstaller find leftover files everywhere.",
        AppPermission.FilesAndFolders => "Lets WhatsApp downloads cleanup and the experimental organizer inspect your Downloads folder.",
        AppPermission.Notifications => "Lets the app notify you about alerts you turned on.",
        AppPermission.AutomationFinder => "Lets the app ask File Explorer to move files for you.",
        AppPermission.AutomationPlayback => "Some music apps require permission for their playback controls.",
        AppPermission.AudioCapture => "Lets the mixer adjust each app’s volume, the Dynamic Island bars follow the music and screen recordings include the PC’s sound.",
        AppPermission.Microphone => "Lets screen recordings include your voice when you turn it on.",
        AppPermission.Camera => "Shows your camera only in the preview window, so you can check how you look before a call. Nothing is recorded or leaves your PC.",
        AppPermission.AppManagement => "Lets updates replace or remove apps installed by the package manager.",
        AppPermission.Calendar => "Read your calendars to show upcoming appointments. Events stay on this PC.",
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
                        : new PermissionStatus(PermissionState.Granted, "Limited: running without administrator rights. Windows that run as administrator ignore WinKnife until it is elevated too.");
                case AppPermission.ScreenRecording:
                case AppPermission.AudioCapture:
                    return new PermissionStatus(PermissionState.Granted);
                case AppPermission.FullDiskAccess:
                case AppPermission.AppManagement:
                    return IsElevated
                        ? new PermissionStatus(PermissionState.Granted)
                        : new PermissionStatus(PermissionState.Missing, "Not granted. Windows asks (UAC) when something needs it.");
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
        ? "Restart as administrator"
        : "Request";

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

    /// <summary>Relaunches WinKnife with a UAC prompt (the current instance keeps running until the new one starts).</summary>
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
