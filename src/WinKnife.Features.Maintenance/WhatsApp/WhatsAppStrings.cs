// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

/// <summary>
/// English UI text of the WhatsApp downloads tool (Vorssaint's <c>WhatsAppDownloadStrings</c> and
/// <c>WhatsAppOrganizerStrings</c>, en-US). Only the platform nouns differ (Recycle Bin, Windows, WinKnife, File Explorer,
/// Windows Security) as the Windows mapping of the spec prescribes. Format strings use .NET placeholders in the same order.
/// </summary>
internal static class WaText
{
    // ----- downloads cleanup
    public static  string Title => Loc.T("WhatsApp downloads");
    public static  string HubDescription => Loc.T("Keeps WhatsApp files in Downloads under control");
    public static  string Intro => Loc.T("Finds files that Windows confirms came from WhatsApp. File contents and chats are never read.");
    public static  string Automatic => Loc.T("Clean up automatically");
    public static  string AutomaticCaption => Loc.T("Checks once a day and sends matching files older than your limit to the Recycle Bin.");
    public static  string Folder => Loc.T("Watched folder");
    public static  string AccessReady => Loc.T("Downloads is accessible");
    public static  string AccessDenied => Loc.T("WinKnife cannot access Downloads. Allow it in Windows Security > Controlled folder access.");
    public static  string OpenWindowsSecurity => Loc.T("Open Windows Security");
    public static  string FileTypes => Loc.T("File types");
    public static  string AllTypes => Loc.T("All");
    public static  string Image => Loc.T("Images");
    public static  string Video => Loc.T("Videos");
    public static  string Audio => Loc.T("Audio and voice notes");
    public static  string Document => Loc.T("Documents");
    public static  string Archive => Loc.T("Archives");
    public static  string Other => Loc.T("Other");
    public static  string Retention => Loc.T("Keep for");
    public static  string RetentionCaption => Loc.T("Recently edited files wait for the full period again.");
    public static  string ManualTitle => Loc.T("Clean now");
    public static  string ManualIntro => Loc.T("Scan at any time. The initial selection follows your types and age limit; you can review every confirmed file.");
    public static  string NoFiles => Loc.T("No confirmed WhatsApp files found in Downloads.");
    public static  string SelectRules => Loc.T("Select by my rules");
    public static  string Keep => Loc.T("Keep");
    public static  string ManageAgain => Loc.T("Manage again");
    public static  string Activity => Loc.T("Activity");
    public static  string NeverRun => Loc.T("No cleanup has run yet.");
    public static  string FirstTitle => Loc.T("What about existing files?");
    public static  string FutureOnly => Loc.T("Only future downloads");
    public static  string IncludeExisting => Loc.T("Include existing files");
    public static  string TrashNote => Loc.T("Files are moved to the Recycle Bin and remain recoverable until you empty it.");
    public static  string LocalNote => Loc.T("Only local file metadata is inspected. WinKnife never reads chats or file contents.");
    public static  string NotificationTitle => Loc.T("WhatsApp cleanup");
    public static  string ScanFailed => Loc.T("Downloads could not be scanned. Check Controlled folder access in Windows Security.");
    public static  string ManageButton => Loc.T("Manage…");

    // ----- shared with the Cleaner page
    public static  string Cleaner => Loc.T("Cleaner");
    public static  string Off => Loc.T("Off");
    public static  string Scan => Loc.T("Scan");
    public static  string Scanning => Loc.T("Scanning…");
    public static  string Cleaning => Loc.T("Cleaning…");
    public static  string Cancel => Loc.T("Cancel");
    public static  string Ok => Loc.T("OK");
    public static  string NotifyWhenDone => Loc.T("Notify when done");
    public static  string NotificationsOff => Loc.T("WinKnife notifications are turned off in the system.");
    public static  string OpenNotificationSettings => Loc.T("Open Notification Settings…");
    public static  string ShowInExplorer => Loc.T("Show in File Explorer");

    public static string DaysFormat(int days) => Loc.F("{0} days", days);

    public static string ResultsFormat(int count, string size) => Loc.F("{0} confirmed files · {1}", count, size);

    public static string CleanSelectedFormat(int count, string size) => Loc.F("Move {0} to Recycle Bin · {1}", count, size);

    public static string LastRunFormat(string date, int count, string size, int failed) => Loc.F("Last cleanup {0}: {1} files · {2} · {3} failed", date, count, size, failed);

    public static string NextRunFormat(string date) => Loc.F("Next automatic check {0}.", date);

    public static string FirstMessageFormat(int count) => Loc.F("{0} existing files already match your rules. Choose whether automation may manage them or only future downloads.", count);

    public static string NotificationFormat(int count, string size, int failed) => Loc.F("{0} files ({1}) moved to the Recycle Bin. {2} failed.", count, size, failed);

    // ----- organizer
    public static  string OrgTitle => Loc.T("Automatic organization");
    public static  string OrgExperimental => Loc.T("Experimental");
    public static  string OrgDescription => Loc.T("Moves stable WhatsApp downloads to a dedicated folder and detects exact repeat downloads.");
    public static  string OrgEnabled => Loc.T("Organize automatically");
    public static  string OrgEnabledCaption => Loc.T("WhatsApp may download a moved file again. WinKnife cannot prevent the network download, but it can detect and discard an identical extra copy.");
    public static  string OrgDestination => Loc.T("Destination folder");
    public static  string OrgChooseFolder => Loc.T("Choose…");
    public static  string OrgUseDefault => Loc.T("Use Downloads/WhatsApp");
    public static  string OrgInvalidDestination => Loc.T("Choose a folder other than Downloads itself.");
    public static  string OrgOrganization => Loc.T("Folder structure");
    public static  string OrgFlat => Loc.T("No subfolders");
    public static  string OrgByType => Loc.T("By file type");
    public static  string OrgByMonth => Loc.T("By year and month");
    public static  string OrgDelay => Loc.T("Wait before moving");
    public static  string OrgDuplicateAction => Loc.T("When the same file is downloaded again");
    public static  string OrgTrashDuplicate => Loc.T("Move the new copy to the Recycle Bin");
    public static  string OrgKeepBoth => Loc.T("Keep both copies");
    public static  string OrgReplaceExisting => Loc.T("Replace the organized copy");
    public static  string OrgDuplicateCaption => Loc.T("Duplicates are confirmed with a private SHA-256 digest. The organized copy is rechecked before another copy is discarded.");
    public static  string OrgOrganizeNow => Loc.T("Organize eligible files now");
    public static  string OrgUndo => Loc.T("Undo last organization");
    public static  string OrgWaiting => Loc.T("Watching Downloads");
    public static  string OrgWorking => Loc.T("Organizing WhatsApp files…");
    public static  string OrgNeverRun => Loc.T("No organization has run yet.");
    public static  string OrgNotificationTitle => Loc.T("WhatsApp organization");
    public static  string OrgPrivacyNote => Loc.T("To identify exact duplicates, file bytes are read locally only while calculating a cryptographic digest. Contents and chats are never stored or uploaded.");

    public static string MinutesFormat(int minutes) => Loc.F("{0} minutes", minutes);

    public static string OrgResultFormat(int moved, int duplicates, int failed) => Loc.F("{0} moved · {1} duplicates · {2} failed", moved, duplicates, failed);

    public static string OrgLastRunFormat(string date, int moved, int duplicates, int failed) => Loc.F("Last organization {0}: {1} moved · {2} duplicates · {3} failed", date, moved, duplicates, failed);

    public static string OrgNotificationFormat(int moved, int duplicates, int failed) => Loc.F("{0} files organized. {1} duplicate downloads handled. {2} failed.", moved, duplicates, failed);
}
