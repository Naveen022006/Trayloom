// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>The Advanced page (spec 02a section 7): backup, reset, uninstall.</summary>
internal static class AdvancedPage
{
    public static FrameworkElement Build(ShellServices s)
    {
        var stack = new StackPanel();
        stack.AddSpaced(new SettingsPageHeader(Loc.T("Advanced")), 20);

        // Backup
        var status = new StackPanel();
        var backup = new SettingsCard(Loc.T("Backup"));
        backup.AddCaption(Loc.T("Take your setup to another PC: export every preference to a file and import it there. Your Scratchpad notes, clipboard history, Shelf items and system permissions never leave this PC."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(UiKit.Button(Loc.T("Export settings…"), ButtonKind.Bordered, 13, "", () => Export(s, status)));
        var import = UiKit.Button(Loc.T("Import settings…"), ButtonKind.Bordered, 13, "", () => Import(s, status));
        import.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(import);
        backup.Add(buttons);
        backup.Add(status);
        stack.AddSpaced(backup, 20);

        // Reset
        var reset = new SettingsCard(Loc.T("Reset"));
        reset.AddCaption(Loc.T("Removes the startup entry and the closed-lid rule Trayloom may have added. Useful to start fresh or before uninstalling. The app stays installed."));
        var resetResult = new StackPanel();
        reset.Add(UiKit.Button(Loc.T("Reset startup and power rules"), ButtonKind.Destructive, 13, "", () =>
        {
            var owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
            var choice = Dialogs.Show(owner, Loc.T("Reset startup and power rules?"), Loc.T("Trayloom will stop starting with Windows and give back any power setting it changed. Your settings are kept."), [Loc.T("Cancel"), Loc.T("Reset")], destructiveIndex: 1);
            if (choice != 1)
            {
                return;
            }

            resetResult.Children.Clear();
            try
            {
                AutoStart.SetEnabled(false);
                s.App.Set(AppKeys.LaunchAtLoginWanted, false);
                resetResult.Children.Add(Result("Done.", true));
            }
            catch (Exception ex)
            {
                s.Log.Error("Reset failed", ex);
                resetResult.Children.Add(Result(Loc.T("Some entries could not be removed. Try again."), false));
            }
        }));
        reset.Add(resetResult);
        stack.AddSpaced(reset, 20);

        // Uninstall
        var uninstall = new SettingsCard(Loc.T("Uninstall"));
        uninstall.AddCaption(Loc.T("Does all of the above, puts back any Windows change Trayloom made and removes its Claude Code hook, then deletes your data: preferences, Scratchpad notes, clipboard history, recent screenshots, unsaved recordings and caches. The app quits when done. Delete the app folder afterwards to finish. You can reinstall anytime."));
        uninstall.Add(UiKit.Button(Loc.T("Uninstall Trayloom completely"), ButtonKind.Destructive, 13, "", () =>
        {
            var owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
            var choice = Dialogs.Show(owner, Loc.T("Uninstall Trayloom?"), Loc.T("Trayloom will remove its startup entry and preferences, then quit. This can’t be undone from the app."), [Loc.T("Cancel"), Loc.T("Uninstall Trayloom completely")], destructiveIndex: 1);
            if (choice != 1)
            {
                return;
            }

            try
            {
                AutoStart.SetEnabled(false);
                s.Host.Store.Flush();
                s.Shell.QuitAndDeleteData();
            }
            catch (Exception ex)
            {
                s.Log.Error("Uninstall failed", ex);
                Dialogs.Show(owner, Loc.T("Uninstall stopped"), ex.Message, [Loc.T("OK")]);
            }
        }));
        stack.AddSpaced(uninstall, 20);
        return stack;
    }

    private static UIElement Result(string text, bool ok)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        if (ok)
        {
            row.Children.Add(UiKit.Icon("", 13, ThemeKeys.Green));
        }

        var label = UiKit.Text(text, 12, FontWeights.Normal, ok ? ThemeKeys.Green : ThemeKeys.Orange, wrap: true);
        label.Margin = new Thickness(ok ? 6 : 0, 0, 0, 0);
        row.Children.Add(label);
        return row;
    }

    private static void Export(ShellServices s, StackPanel status)
    {
        status.Children.Clear();
        var dialog = new SaveFileDialog { FileName = "Trayloom Settings.json", Filter = "Trayloom backup (*.json)|*.json", DefaultExt = ".json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            s.Host.Store.Flush();
            var scopes = JsonNode.Parse(File.ReadAllText(AppPaths.SettingsFile)) as JsonObject ?? new JsonObject();
            var root = new JsonObject
            {
                ["trayloomBackupVersion"] = 1,
                ["trayloomBackupAppVersion"] = AppInfo.Version,
                ["settings"] = scopes,
            };
            File.WriteAllText(dialog.FileName, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            status.Children.Add(Result(Loc.T("Backup saved"), true));
        }
        catch (Exception ex)
        {
            s.Log.Error("Export failed", ex);
            status.Children.Add(Result(Loc.T("Could not save the backup."), false));
        }
    }

    private static void Import(ShellServices s, StackPanel status)
    {
        status.Children.Clear();
        var dialog = new OpenFileDialog { Filter = "Trayloom backup (*.json)|*.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        JsonObject? settings;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(dialog.FileName)) as JsonObject;
            settings = root is not null && (root["trayloomBackupVersion"] is not null || root[LegacyNames.BackupVersionKey] is not null) ? root["settings"] as JsonObject : null;
        }
        catch
        {
            settings = null;
        }

        if (settings is null || settings["scopes"] is not JsonObject)
        {
            status.Children.Add(Result(Loc.T("This file is not a valid Trayloom backup."), false));
            return;
        }

        var owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        var choice = Dialogs.Show(owner, Loc.T("Import these settings?"), Loc.T("Your current settings are replaced by the file’s and the app restarts. Nothing else on this PC is touched."), [Loc.T("Import and restart"), Loc.T("Cancel")]);
        if (choice != 0)
        {
            return;
        }

        try
        {
            s.Host.Store.Flush();
            AtomicFile.WriteAllText(AppPaths.SettingsFile, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            s.Shell.Restart(skipSave: true);
        }
        catch (Exception ex)
        {
            s.Log.Error("Import failed", ex);
            status.Children.Add(Result(Loc.T("This file is not a valid Trayloom backup."), false));
        }
    }
}
