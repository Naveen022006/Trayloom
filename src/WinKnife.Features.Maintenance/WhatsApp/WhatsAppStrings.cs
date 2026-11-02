// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.Maintenance;

/// <summary>
/// English UI text of the WhatsApp downloads tool (Vorssaint's <c>WhatsAppDownloadStrings</c> and
/// <c>WhatsAppOrganizerStrings</c>, en-US). Only the platform nouns differ (Recycle Bin, Windows, WinKnife, File Explorer,
/// Windows Security) as the Windows mapping of the spec prescribes. Format strings use .NET placeholders in the same order.
/// </summary>
internal static class WaText
{
    // ----- downloads cleanup
    public const string Title = "WhatsApp downloads";
    public const string HubDescription = "Keeps WhatsApp files in Downloads under control";
    public const string Intro = "Finds files that Windows confirms came from WhatsApp. File contents and chats are never read.";
    public const string Automatic = "Clean up automatically";
    public const string AutomaticCaption = "Checks once a day and sends matching files older than your limit to the Recycle Bin.";
    public const string Folder = "Watched folder";
    public const string AccessReady = "Downloads is accessible";
    public const string AccessDenied = "WinKnife cannot access Downloads. Allow it in Windows Security > Controlled folder access.";
    public const string OpenWindowsSecurity = "Open Windows Security";
    public const string FileTypes = "File types";
    public const string AllTypes = "All";
    public const string Image = "Images";
    public const string Video = "Videos";
    public const string Audio = "Audio and voice notes";
    public const string Document = "Documents";
    public const string Archive = "Archives";
    public const string Other = "Other";
    public const string Retention = "Keep for";
    public const string RetentionCaption = "Recently edited files wait for the full period again.";
    public const string ManualTitle = "Clean now";
    public const string ManualIntro = "Scan at any time. The initial selection follows your types and age limit; you can review every confirmed file.";
    public const string NoFiles = "No confirmed WhatsApp files found in Downloads.";
    public const string SelectRules = "Select by my rules";
    public const string Keep = "Keep";
    public const string ManageAgain = "Manage again";
    public const string Activity = "Activity";
    public const string NeverRun = "No cleanup has run yet.";
    public const string FirstTitle = "What about existing files?";
    public const string FutureOnly = "Only future downloads";
    public const string IncludeExisting = "Include existing files";
    public const string TrashNote = "Files are moved to the Recycle Bin and remain recoverable until you empty it.";
    public const string LocalNote = "Only local file metadata is inspected. WinKnife never reads chats or file contents.";
    public const string NotificationTitle = "WhatsApp cleanup";
    public const string ScanFailed = "Downloads could not be scanned. Check Controlled folder access in Windows Security.";
    public const string ManageButton = "Manage…";

    // ----- shared with the Cleaner page
    public const string Cleaner = "Cleaner";
    public const string Off = "Off";
    public const string Scan = "Scan";
    public const string Scanning = "Scanning…";
    public const string Cleaning = "Cleaning…";
    public const string Cancel = "Cancel";
    public const string Ok = "OK";
    public const string NotifyWhenDone = "Notify when done";
    public const string NotificationsOff = "WinKnife notifications are turned off in the system.";
    public const string OpenNotificationSettings = "Open Notification Settings…";
    public const string ShowInExplorer = "Show in File Explorer";

    public static string DaysFormat(int days) => $"{days} days";

    public static string ResultsFormat(int count, string size) => $"{count} confirmed files · {size}";

    public static string CleanSelectedFormat(int count, string size) => $"Move {count} to Recycle Bin · {size}";

    public static string LastRunFormat(string date, int count, string size, int failed) => $"Last cleanup {date}: {count} files · {size} · {failed} failed";

    public static string NextRunFormat(string date) => $"Next automatic check {date}.";

    public static string FirstMessageFormat(int count) => $"{count} existing files already match your rules. Choose whether automation may manage them or only future downloads.";

    public static string NotificationFormat(int count, string size, int failed) => $"{count} files ({size}) moved to the Recycle Bin. {failed} failed.";

    // ----- organizer
    public const string OrgTitle = "Automatic organization";
    public const string OrgExperimental = "Experimental";
    public const string OrgDescription = "Moves stable WhatsApp downloads to a dedicated folder and detects exact repeat downloads.";
    public const string OrgEnabled = "Organize automatically";
    public const string OrgEnabledCaption = "WhatsApp may download a moved file again. WinKnife cannot prevent the network download, but it can detect and discard an identical extra copy.";
    public const string OrgDestination = "Destination folder";
    public const string OrgChooseFolder = "Choose…";
    public const string OrgUseDefault = "Use Downloads/WhatsApp";
    public const string OrgInvalidDestination = "Choose a folder other than Downloads itself.";
    public const string OrgOrganization = "Folder structure";
    public const string OrgFlat = "No subfolders";
    public const string OrgByType = "By file type";
    public const string OrgByMonth = "By year and month";
    public const string OrgDelay = "Wait before moving";
    public const string OrgDuplicateAction = "When the same file is downloaded again";
    public const string OrgTrashDuplicate = "Move the new copy to the Recycle Bin";
    public const string OrgKeepBoth = "Keep both copies";
    public const string OrgReplaceExisting = "Replace the organized copy";
    public const string OrgDuplicateCaption = "Duplicates are confirmed with a private SHA-256 digest. The organized copy is rechecked before another copy is discarded.";
    public const string OrgOrganizeNow = "Organize eligible files now";
    public const string OrgUndo = "Undo last organization";
    public const string OrgWaiting = "Watching Downloads";
    public const string OrgWorking = "Organizing WhatsApp files…";
    public const string OrgNeverRun = "No organization has run yet.";
    public const string OrgNotificationTitle = "WhatsApp organization";
    public const string OrgPrivacyNote = "To identify exact duplicates, file bytes are read locally only while calculating a cryptographic digest. Contents and chats are never stored or uploaded.";

    public static string MinutesFormat(int minutes) => $"{minutes} minutes";

    public static string OrgResultFormat(int moved, int duplicates, int failed) => $"{moved} moved · {duplicates} duplicates · {failed} failed";

    public static string OrgLastRunFormat(string date, int moved, int duplicates, int failed) => $"Last organization {date}: {moved} moved · {duplicates} duplicates · {failed} failed";

    public static string OrgNotificationFormat(int moved, int duplicates, int failed) => $"{moved} files organized. {duplicates} duplicate downloads handled. {failed} failed.";
}
