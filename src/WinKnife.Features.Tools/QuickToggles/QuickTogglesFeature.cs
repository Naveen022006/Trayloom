// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>One-click system toggles: dark mode, desktop icons, hidden files, file extensions, Recycle Bin and lock.</summary>
public sealed class QuickTogglesFeature : ToolFeatureBase
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public override string Id => "quick-toggles";

    public override string Name => "Quick toggles";

    public override string Description => "Flip dark mode, desktop icons, hidden files and extensions, empty the Recycle Bin or lock the PC in one click.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("dark-mode", IsDark() ? "Dark mode: on (switch to light)" : "Dark mode: off (switch to dark)", () => Safe(ToggleDarkMode), ""),
        new FeatureAction("desktop-icons", DesktopIconsVisible() == false ? "Desktop icons: hidden (show)" : "Desktop icons: shown (hide)", () => Safe(ToggleDesktopIcons), ""),
        new FeatureAction("hidden-files", HiddenFilesShown() ? "Hidden files: shown (hide)" : "Hidden files: hidden (show)", () => Safe(ToggleHiddenFiles), ""),
        new FeatureAction("file-extensions", ExtensionsShown() ? "File extensions: shown (hide)" : "File extensions: hidden (show)", () => Safe(ToggleExtensions), ""),
        new FeatureAction("empty-recycle-bin", "Empty Recycle Bin", () => Safe(EmptyRecycleBin), ""),
        new FeatureAction("lock", "Lock screen", () => Safe(() => N.LockWorkStation()), ""),
    ];

    protected override void OnStart()
    {
    }

    protected override void OnStop()
    {
        // Toggles are deliberate, lasting user choices; nothing to undo.
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var panel = new StackPanel();
        panel.Children.Add(Row("Dark mode", "Switches both Windows and app colour modes.", IsDark, ToggleDarkMode));
        panel.Children.Add(Row("Show desktop icons", "Hides or shows everything on the desktop.", () => DesktopIconsVisible() != false, ToggleDesktopIcons));
        panel.Children.Add(Row("Show hidden files", "Reveals files and folders marked hidden in File Explorer.", HiddenFilesShown, ToggleHiddenFiles));
        panel.Children.Add(Row("Show file extensions", "Displays .txt, .png and so on in File Explorer.", ExtensionsShown, ToggleExtensions));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(UiKit.Btn("Empty Recycle Bin", () => Safe(EmptyRecycleBin), false, ""));
        actions.Children.Add(UiKit.Btn("Lock screen", () => Safe(() => N.LockWorkStation()), false, ""));
        panel.Children.Add(actions);
        return panel;
    }

    private FrameworkElement Row(string title, string description, Func<bool> read, Action toggle)
    {
        var box = new CheckBox { IsChecked = read() };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.SwitchStyle);
        box.Click += (_, _) =>
        {
            if ((box.IsChecked == true) != read())
            {
                Safe(toggle);
            }

            // Re-read after the system had a moment to apply the change.
            Ctx.Ui.BeginInvoke(() => box.IsChecked = read(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        var card = new SettingsCard(title, description, box) { Margin = new Thickness(0, 0, 0, 8) };
        return card;
    }

    // ---- Dark mode -------------------------------------------------------------------------

    private static bool IsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    private void ToggleDarkMode()
    {
        var light = IsDark() ? 1 : 0;
        using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
        {
            key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
        }

        N.SendMessageTimeoutW(N.HWND_BROADCAST, N.WM_SETTINGCHANGE, 0, "ImmersiveColorSet", N.SMTO_ABORTIFHUNG, 200, out _);
        Ctx.Log.Info($"Dark mode {(light == 0 ? "on" : "off")}");
    }

    // ---- Explorer options -------------------------------------------------------------------

    private static bool HiddenFilesShown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
        return key?.GetValue("Hidden") is int v && v == 1;
    }

    private static bool ExtensionsShown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
        return key?.GetValue("HideFileExt") is int v && v == 0;
    }

    private void ToggleHiddenFiles()
    {
        SetAdvanced("Hidden", HiddenFilesShown() ? 2 : 1);
        RefreshExplorer();
    }

    private void ToggleExtensions()
    {
        SetAdvanced("HideFileExt", ExtensionsShown() ? 1 : 0);
        RefreshExplorer();
    }

    private static void SetAdvanced(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AdvancedKey);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    /// <summary>Makes open Explorer windows and the desktop pick up changed view options.</summary>
    private void RefreshExplorer()
    {
        N.SendMessageTimeoutW(N.HWND_BROADCAST, N.WM_SETTINGCHANGE, 0, "ShellState", N.SMTO_ABORTIFHUNG, 200, out _);
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is not null)
            {
                dynamic shell = Activator.CreateInstance(type)!;
                foreach (var window in shell.Windows())
                {
                    try
                    {
                        window.Refresh();
                    }
                    catch
                    {
                        // a window closing while we iterate
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Ctx.Log.Warn("Could not refresh Explorer windows: " + ex.Message);
        }

        N.SHChangeNotify(0x08000000, 0, 0, 0); // SHCNE_ASSOCCHANGED refreshes the desktop view
    }

    // ---- Desktop icons ----------------------------------------------------------------------

    private static nint DesktopListView(out nint defView)
    {
        defView = 0;
        var progman = N.FindWindowW("Progman", null);
        defView = N.FindWindowExW(progman, 0, "SHELLDLL_DefView", null);
        if (defView == 0)
        {
            // When a wallpaper slideshow is active the icons live under a WorkerW sibling.
            nint worker = 0;
            while ((worker = N.FindWindowExW(0, worker, "WorkerW", null)) != 0)
            {
                defView = N.FindWindowExW(worker, 0, "SHELLDLL_DefView", null);
                if (defView != 0)
                {
                    break;
                }
            }
        }

        return defView == 0 ? 0 : N.FindWindowExW(defView, 0, "SysListView32", null);
    }

    private static bool? DesktopIconsVisible()
    {
        var list = DesktopListView(out _);
        return list == 0 ? null : N.IsWindowVisible(list);
    }

    private void ToggleDesktopIcons()
    {
        DesktopListView(out var defView);
        if (defView == 0)
        {
            Ctx.Notifier.Show(Name, "The desktop could not be found.", NotificationLevel.Warning);
            return;
        }

        N.SendMessageW(defView, N.WM_COMMAND, 0x7402, 0); // "Show desktop icons" menu command
    }

    // ---- Recycle Bin -------------------------------------------------------------------------

    private void EmptyRecycleBin()
    {
        var info = new N.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<N.SHQUERYRBINFO>() };
        N.SHQueryRecycleBinW(null, ref info);
        if (info.i64NumItems == 0)
        {
            Ctx.Notifier.Show(Name, "The Recycle Bin is already empty.");
            return;
        }

        var answer = MessageBox.Show(
            $"Permanently delete {info.i64NumItems} item(s) ({UiKit.FormatBytes(info.i64Size)}) from the Recycle Bin?\r\n\r\nThis cannot be undone.",
            "Empty Recycle Bin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var hr = N.SHEmptyRecycleBinW(0, null, N.SHERB_NOCONFIRMATION | N.SHERB_NOPROGRESSUI | N.SHERB_NOSOUND);
        Ctx.Notifier.Show(
            Name,
            hr == 0 || hr == unchecked((int)0x8000FFFF) ? $"Freed {UiKit.FormatBytes(info.i64Size)}." : "Windows could not empty the Recycle Bin.",
            hr == 0 ? NotificationLevel.Info : NotificationLevel.Warning);
    }
}
