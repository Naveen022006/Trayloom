// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using Microsoft.Win32;
using Trayloom.Core;

namespace Trayloom.App;

/// <summary>
/// One-time migration of an existing install from the old name (see <see cref="LegacyNames"/>) to Trayloom: asks a still-running old
/// instance to quit, moves the settings folder and the local data folder, re-registers start-at-sign-in (keeping on/off and the
/// Task Manager "disabled" state) and, when installing, removes the old shortcuts and Apps &amp; features entry. Everything is
/// idempotent and best effort: a failure is logged and never stops the app from starting.
/// </summary>
internal static class LegacyMigration
{
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// Runs the start-up part. Call once, as the first instance, before anything reads settings (the settings folder is moved here).
    /// </summary>
    public static void Run()
    {
        var notes = new List<(bool Warn, string Text)>();
        var marker = Path.Combine(AppPaths.Root, "legacy-migrated");
        try
        {
            if (File.Exists(marker))
            {
                return; // done on an earlier start
            }
        }
        catch (Exception)
        {
            // unreadable: run the migration, it is idempotent
        }

        var completed = false;
        try
        {
            QuitLegacyInstance(notes);
            var dataOverridden = Env("TRAYLOOM_DATA") is not null;
            if (!dataOverridden)
            {
                var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyNames.FolderName);
                if (LooksLikeOldSettingsFolder(old, notes) && MoveFolder(old, AppPaths.Root, notes))
                {
                    RenameLegacyLogs(notes);
                    RewriteMovedPaths(old, AppPaths.Root, notes);
                }
            }

            if (!dataOverridden && Env("TRAYLOOM_LOCALDATA") is null)
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var oldLocal = Path.Combine(local, LegacyNames.FolderName);
                if (LooksLikeOldLocalFolder(oldLocal, notes))
                {
                    MoveFolder(oldLocal, Path.Combine(local, "Trayloom"), notes);
                }
            }

            MigrateAutoStart(notes, null);
            completed = true;
        }
        catch (Exception ex)
        {
            notes.Add((true, "stopped: " + ex.Message));
        }
        finally
        {
            Write(notes, new FileLog());
        }

        if (completed)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
            }
            catch (Exception)
            {
                // the next start repeats the (idempotent) migration
            }
        }
    }

    /// <summary>Only a folder whose settings.json is a Trayloom-style settings object (with "scopes") is moved: a folder that merely has the old name is not ours.</summary>
    private static bool LooksLikeOldSettingsFolder(string folder, List<(bool Warn, string Text)> notes)
    {
        try
        {
            var file = Path.Combine(folder, "settings.json");
            if (!File.Exists(file))
            {
                return false;
            }

            var ok = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file)) is System.Text.Json.Nodes.JsonObject root && root["scopes"] is System.Text.Json.Nodes.JsonObject;
            if (!ok)
            {
                notes.Add((true, $"{folder} has a settings.json that is not a settings file; it was not moved."));
            }

            return ok;
        }
        catch (Exception ex)
        {
            notes.Add((true, $"could not check {folder}: {ex.Message}"));
            return false;
        }
    }

    /// <summary>Only a local data folder with one of the known children is moved.</summary>
    private static bool LooksLikeOldLocalFolder(string folder, List<(bool Warn, string Text)> notes)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return false;
            }

            var known = File.Exists(Path.Combine(folder, "clipboard.json"))
                || Directory.Exists(Path.Combine(folder, "Clipboard"))
                || Directory.Exists(Path.Combine(folder, "RecentCaptures"))
                || Directory.Exists(Path.Combine(folder, "Recordings"));
            if (!known)
            {
                notes.Add((true, $"{folder} holds none of the known data folders; it was not moved."));
            }

            return known;
        }
        catch (Exception ex)
        {
            notes.Add((true, $"could not check {folder}: {ex.Message}"));
            return false;
        }
    }

    /// <summary>
    /// Called by the installer: removes the old Start menu / Desktop shortcuts, the old Apps &amp; features entry and the old
    /// start-at-login value (re-registered for <paramref name="installedExe"/>). The old program folder is left alone.
    /// </summary>
    public static void RemoveLegacyInstallRegistrations(string installedExe, ILog log)
    {
        var notes = new List<(bool Warn, string Text)>();
        RemoveFile(Path.Combine(InstallManager.StartMenuDirectory, LegacyNames.ShortcutFileName), notes);
        RemoveFile(Path.Combine(InstallManager.DesktopDirectory, LegacyNames.ShortcutFileName), notes);
        if (Env("TRAYLOOM_UNINSTALL_KEY") is null)
        {
            try
            {
                string? folder = null;
                using (var existing = Registry.CurrentUser.OpenSubKey(LegacyNames.UninstallKey))
                {
                    if (existing is not null)
                    {
                        folder = existing.GetValue("InstallLocation") as string ?? string.Empty;
                    }
                }

                if (folder is not null)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(LegacyNames.UninstallKey, throwOnMissingSubKey: false);
                    notes.Add((false, $"removed the old Apps & features entry; the old program folder {folder} was left in place and can be deleted by hand."));
                }
            }
            catch (Exception ex)
            {
                notes.Add((true, "could not remove the old Apps & features entry: " + ex.Message));
            }
        }

        MigrateAutoStart(notes, installedExe);
        Write(notes, log);
    }

    // ------------------------------------------------------------------------------------------------ old instance

    /// <summary>Asks a running old instance to quit (the same signal <c>--quit</c> uses) and waits until its mutex is free.</summary>
    private static void QuitLegacyInstance(List<(bool Warn, string Text)> notes)
    {
        try
        {
            using var quit = EventWaitHandle.OpenExisting(LegacyNames.QuitEventName);
            quit.Set();
            notes.Add((false, "an old instance is running; asked it to quit."));
        }
        catch (Exception)
        {
            return; // not running
        }

        try
        {
            using var mutex = new Mutex(false, LegacyNames.MutexName);
            var got = false;
            try
            {
                got = mutex.WaitOne(15_000);
            }
            catch (AbandonedMutexException)
            {
                got = true;
            }

            if (got)
            {
                mutex.ReleaseMutex();
                return;
            }

            notes.Add((true, "the old instance did not quit in time; closing it."));
            var oldProgramFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", LegacyNames.FolderName);
            foreach (var process in Process.GetProcessesByName(LegacyNames.ProcessName).Where(p => p.Id != Environment.ProcessId))
            {
                try
                {
                    // only the old installed copy: a development build or another program with that name is not ours to kill
                    var image = process.MainModule?.FileName;
                    if (image is null || !SafePaths.IsUnder(image, oldProgramFolder))
                    {
                        continue;
                    }

                    process.Kill();
                    process.WaitForExit(5_000);
                }
                catch (Exception)
                {
                    // access denied or already gone
                }
            }
        }
        catch (Exception ex)
        {
            notes.Add((true, "stopping the old instance failed: " + ex.Message));
        }
    }

    // ------------------------------------------------------------------------------------------------ folders

    /// <summary>Moves <paramref name="from"/> to <paramref name="to"/> when only the old one exists. Returns true when the move/copy happened.</summary>
    private static bool MoveFolder(string from, string to, List<(bool Warn, string Text)> notes)
    {
        if (!Directory.Exists(from) || Directory.Exists(to) || string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(to))!);
            Directory.Move(from, to);
            notes.Add((false, $"moved {from} to {to}."));
            return true;
        }
        catch (Exception ex)
        {
            notes.Add((true, $"could not move {from} ({ex.Message}); copying it instead and leaving the old folder."));
        }

        var staging = to + ".migrating";
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            CopyDirectory(from, staging);
            Directory.Move(staging, to);
            notes.Add((false, $"copied {from} to {to}; the old folder was left in place."));
            return true;
        }
        catch (Exception ex)
        {
            notes.Add((true, $"could not copy {from} to {to}: {ex.Message}"));
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch (Exception)
            {
                // best effort
            }

            return false;
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(from))
        {
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }

    /// <summary>The log files carry the product name (<c>winknife.log</c>, <c>winknife.1.log</c>); keep their history under the new name.</summary>
    private static void RenameLegacyLogs(List<(bool Warn, string Text)> notes)
    {
        try
        {
            if (!Directory.Exists(AppPaths.LogDirectory))
            {
                return;
            }

            var legacy = Path.GetFileNameWithoutExtension(LegacyNames.LogFileName);
            var current = Path.GetFileNameWithoutExtension(AppPaths.LogFile);
            foreach (var file in Directory.GetFiles(AppPaths.LogDirectory, legacy + "*.log"))
            {
                var name = Path.GetFileName(file);
                var target = Path.Combine(AppPaths.LogDirectory, current + name[legacy.Length..]);
                if (!File.Exists(target))
                {
                    File.Move(file, target);
                }
            }
        }
        catch (Exception ex)
        {
            notes.Add((true, "could not rename the old log files: " + ex.Message));
        }
    }

    /// <summary>Settings may hold absolute paths into the old folder (JSON-escaped); point them at the new one.</summary>
    private static void RewriteMovedPaths(string oldRoot, string newRoot, List<(bool Warn, string Text)> notes)
    {
        try
        {
            var file = AppPaths.SettingsFile;
            if (!File.Exists(file))
            {
                return;
            }

            var text = File.ReadAllText(file);
            var rewritten = text
                .Replace(oldRoot.Replace("\\", "\\\\"), newRoot.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase)
                .Replace(oldRoot, newRoot, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(text, rewritten, StringComparison.Ordinal))
            {
                AtomicFile.WriteAllText(file, rewritten);
                notes.Add((false, "updated paths in settings.json that pointed into the old folder."));
            }
        }
        catch (Exception ex)
        {
            notes.Add((true, "could not update paths in settings.json: " + ex.Message));
        }
    }

    // ------------------------------------------------------------------------------------------------ start at sign-in

    /// <summary>
    /// If the old Run value exists, removes it and registers the Trayloom one for <paramref name="exe"/> (default: the running exe)
    /// unless that already exists; the Task Manager enabled/disabled flag moves along.
    /// </summary>
    private static void MigrateAutoStart(List<(bool Warn, string Text)> notes, string? exe)
    {
        try
        {
            string? legacyValue;
            using (var run = Registry.CurrentUser.OpenSubKey(AutoStart.RunKeyPath, writable: true))
            {
                if (run is null || run.GetValue(LegacyNames.RunValueName) is null)
                {
                    return;
                }

                legacyValue = run.GetValue(LegacyNames.RunValueName) as string;
                run.DeleteValue(LegacyNames.RunValueName, throwOnMissingValue: false);
            }

            var wasEnabled = !string.IsNullOrWhiteSpace(legacyValue);
            if (wasEnabled)
            {
                var target = exe ?? Environment.ProcessPath;
                if (target is not null && File.Exists(target))
                {
                    // Also re-points a Trayloom entry that still targets the folder the zip was extracted to.
                    AutoStart.SetEnabled(true, target);
                }
            }

            if (Env("TRAYLOOM_RUN_KEY") is null)
            {
                using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
                if (approved?.GetValue(LegacyNames.RunValueName) is byte[] flags)
                {
                    if (approved.GetValue(AutoStart.ValueName) is null)
                    {
                        approved.SetValue(AutoStart.ValueName, flags, RegistryValueKind.Binary);
                    }

                    approved.DeleteValue(LegacyNames.RunValueName, throwOnMissingValue: false);
                }
            }

            notes.Add((false, $"start at sign-in: the old entry was removed; the new one is {(wasEnabled ? "registered" : "off")}{(wasEnabled && AutoStart.IsDisabledByUser ? " (switched off in Task Manager)" : string.Empty)}."));
        }
        catch (Exception ex)
        {
            notes.Add((true, "could not migrate the start-at-sign-in entry: " + ex.Message));
        }
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void RemoveFile(string path, List<(bool Warn, string Text)> notes)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                notes.Add((false, "removed the old shortcut " + path));
            }
        }
        catch (Exception ex)
        {
            notes.Add((true, $"could not remove {path}: {ex.Message}"));
        }
    }

    private static void Write(List<(bool Warn, string Text)> notes, ILog log)
    {
        foreach (var (warn, text) in notes)
        {
            var line = "Migration from the old name: " + text;
            if (warn)
            {
                log.Warn(line);
            }
            else
            {
                log.Info(line);
            }
        }
    }
}
