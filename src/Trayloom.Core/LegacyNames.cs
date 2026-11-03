// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Core;

/// <summary>
/// The names this app used before it was renamed from WinKnife to Trayloom. They exist ONLY so an existing install can be
/// migrated (settings folder, start-at-login entry, Apps &amp; features entry, shortcuts, a still-running old instance, Claude hook
/// path). Nothing else in the code base may use the old name: new code uses the Trayloom names.
/// </summary>
public static class LegacyNames
{
    /// <summary>Product name.</summary>
    public const string AppName = "WinKnife";

    /// <summary>Process name of the old executable (<c>WinKnife.exe</c>).</summary>
    public const string ProcessName = "WinKnife";

    /// <summary>File name of the old executable.</summary>
    public const string ExeName = "WinKnife.exe";

    /// <summary>
    /// Folder name used under <c>%APPDATA%</c> (profile), <c>%LOCALAPPDATA%</c> (caches, clipboard history, recordings),
    /// <c>%LOCALAPPDATA%\Programs</c> (the installed copy), the temp folder and the Pictures / Videos folders.
    /// </summary>
    public const string FolderName = "WinKnife";

    /// <summary>The old log file name inside the logs folder.</summary>
    public const string LogFileName = "winknife.log";

    /// <summary>Name of the start-at-sign-in value under <c>HKCU\...\CurrentVersion\Run</c> (and <c>Explorer\StartupApproved\Run</c>).</summary>
    public const string RunValueName = "WinKnife";

    /// <summary>HKCU sub key of the old Apps &amp; features (uninstall) entry.</summary>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinKnife";

    /// <summary>Start menu / Desktop shortcut file name.</summary>
    public const string ShortcutFileName = "WinKnife.lnk";

    /// <summary>Single-instance mutex of the old app.</summary>
    public const string MutexName = @"Local\WinKnife.SingleInstance";

    /// <summary>Event that asks a running old instance to quit (what <c>WinKnife.exe --quit</c> signals).</summary>
    public const string QuitEventName = @"Local\WinKnife.Quit";

    /// <summary>Marker property of settings backup files written by the old app (still accepted on import).</summary>
    public const string BackupVersionKey = "winknifeBackupVersion";

    /// <summary>Old install manifest file name inside the install folder.</summary>
    public const string InstallManifestName = ".winknife-files.txt";

    /// <summary>The old Claude Code hook script folder lives in the local data folder; its path contains this segment.</summary>
    public const string HookFolderSegment = "/WinKnife/";
}
