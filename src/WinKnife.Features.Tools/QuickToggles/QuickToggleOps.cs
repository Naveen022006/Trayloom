// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinKnife.Core;

namespace WinKnife.Features.Tools;

/// <summary>
/// Quick toggle operations shared by the quick toggles list, the radial menu and the Command Bar. Ids: darkMode, keyboardLight,
/// micMute, emptyTrash, quitAllApps, ejectDisks, hiddenFiles, desktopIcons, lockScreen, displayOff, screenSaver
/// (the radial menu / Command Bar use everything except keyboardLight and micMute).
/// </summary>
internal static class QuickToggleOps
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const uint WmSysCommand = 0x0112;
    private static IContributionServices? _services;

    /// <summary>The 11 ids in default panel order.</summary>
    public static readonly string[] AllIds =
    [
        "darkMode", "keyboardLight", "micMute", "emptyTrash", "quitAllApps", "ejectDisks", "hiddenFiles", "desktopIcons", "lockScreen", "displayOff", "screenSaver",
    ];

    /// <summary>Ids the radial menu offers (no keyboard light, no mic mute).</summary>
    public static readonly string[] RadialIds =
    [
        "darkMode", "emptyTrash", "quitAllApps", "ejectDisks", "hiddenFiles", "desktopIcons", "lockScreen", "displayOff", "screenSaver",
    ];

    /// <summary>Test hook: when set, <see cref="RunAsync"/> calls it instead of touching the system.</summary>
    internal static Func<string, bool>? TestRunner { get; set; }

    /// <summary>Gives the helpers the services (set by the feature on start; also set by any caller that has a context).</summary>
    public static void Attach(IContributionServices ctx) => _services = ctx;

    /// <summary>Forgets the services.</summary>
    public static void Detach() => _services = null;

    // ---------------------------------------------------------------------------------------------------- state reads

    /// <summary>True when the system (apps) scheme is dark.</summary>
    public static bool Dark
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
    }

    /// <summary>True when File Explorer shows hidden files.</summary>
    public static bool HiddenFilesShown
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
            return key?.GetValue("Hidden") is int v && v == 1;
        }
    }

    /// <summary>True when the desktop icons are visible (the shell list view is visible; the registry mirror when it cannot be found).</summary>
    public static bool DesktopIconsShown
    {
        get
        {
            var list = DesktopListView(out _);
            if (list != 0)
            {
                return N.IsWindowVisible(list);
            }

            using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
            return key?.GetValue("HideIcons") is not int v || v == 0;
        }
    }

    /// <summary>Number of volumes "Eject all disks" would eject now.</summary>
    public static int EjectableVolumeCount => ToggleDisks.Candidates(ExcludedDrives()).Count;

    /// <summary>The stored exclusion list (sanitised).</summary>
    public static List<string> ExcludedDrives() => SanitizeExclusions(_services?.App.Get<List<string>>("diskEjectExcludedVolumes") ?? []);

    /// <summary>Trims, drops empties and de-duplicates case-insensitively keeping the first spelling.</summary>
    public static List<string> SanitizeExclusions(IEnumerable<string>? values)
    {
        var result = new List<string>();
        foreach (var raw in values ?? [])
        {
            var v = raw?.Trim() ?? string.Empty;
            if (v.Length > 0 && !result.Contains(v, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(v);
            }
        }

        return result;
    }

    private static bool MicMuted(IContributionServices? ctx) => (ctx ?? _services)?.Services.Get<IMicMuteState>()?.IsMuted ?? false;

    // ---------------------------------------------------------------------------------------------------- catalog data

    /// <summary>Dynamic title (the live state decides: "Switch to light mode", "Hide hidden files"...).</summary>
    public static string Title(string id) => Title(_services, id);

    /// <summary>Dynamic title with an explicit context.</summary>
    public static string Title(IContributionServices? ctx, string id) => id switch
    {
        "darkMode" => Dark ? "Switch to light mode" : "Switch to dark mode",
        "keyboardLight" => "Keyboard light",
        "micMute" => MicMuted(ctx) ? "Unmute microphone" : "Mute microphone",
        "emptyTrash" => "Empty the Recycle Bin",
        "quitAllApps" => "Quit all apps",
        "ejectDisks" => "Eject all disks",
        "hiddenFiles" => HiddenFilesShown ? "Hide hidden files" : "Show hidden files",
        "desktopIcons" => DesktopIconsShown ? "Hide desktop icons" : "Show desktop icons",
        "lockScreen" => "Lock the screen",
        "displayOff" => "Turn off the display",
        "screenSaver" => "Start the screen saver",
        _ => id,
    };

    /// <summary>Segoe Fluent glyph (state dependent).</summary>
    public static string Glyph(string id) => id switch
    {
        "darkMode" => Dark ? "" : "",
        "keyboardLight" => "",
        "micMute" => MicMuted(null) ? "" : "",
        "emptyTrash" => "",
        "quitAllApps" => "",
        "ejectDisks" => "",
        "hiddenFiles" => HiddenFilesShown ? "" : "",
        "desktopIcons" => "",
        "lockScreen" => "",
        "displayOff" => "",
        "screenSaver" => "",
        _ => "",
    };

    /// <summary>The tooltip / edit-mode caption.</summary>
    public static string Caption(string id) => id switch
    {
        "darkMode" => "Changes the appearance of the whole system.",
        "keyboardLight" => "Turns the keyboard backlight on or off.",
        "micMute" => "Uses each microphone’s mute control. Some apps may still hear you. This is not a privacy lock.",
        "emptyTrash" => "Removes everything from the Recycle Bin.",
        "quitAllApps" => "Lists your open apps to quit one by one, all at once, or all but the one in front. Always shows the list before anything quits.",
        "ejectDisks" => "Safely ejects every external disk.",
        "hiddenFiles" or "desktopIcons" => "File Explorer refreshes to apply it.",
        "lockScreen" => "Asks for the password to come back.",
        "displayOff" => "The PC keeps running with the display off.",
        "screenSaver" => "Starts right away, on every display.",
        _ => string.Empty,
    };

    /// <summary>True for rows whose caption is always printed under the title (Explorer refresh note).</summary>
    public static bool CaptionStaysVisible(string id) => id is "hiddenFiles" or "desktopIcons";

    /// <summary>The visibility key of a row (default true; micMute is the panel utility key).</summary>
    public static string VisibilityKey(string id) => id switch
    {
        "darkMode" => "panelToggleDarkMode",
        "keyboardLight" => "panelToggleKeyboardLight",
        "micMute" => "panelUtilityMicMute",
        "emptyTrash" => "panelToggleEmptyTrash",
        "quitAllApps" => "panelToggleQuitAllApps",
        "ejectDisks" => "panelToggleEjectDisks",
        "hiddenFiles" => "panelToggleHiddenFiles",
        "desktopIcons" => "panelToggleDesktopIcons",
        "lockScreen" => "panelToggleLockScreen",
        "displayOff" => "panelToggleDisplayOff",
        _ => "panelToggleScreenSaver",
    };

    /// <summary>The shortcut role whose chip a row shows (null = none).</summary>
    public static string? ShortcutRole(string id) => id switch
    {
        "micMute" => "micMute",
        "quitAllApps" => "quitAll",
        _ => null,
    };

    /// <summary>True when the owning feature is installed (and, for the keyboard light, a lamp array exists).</summary>
    public static bool Available(IContributionServices ctx, string id) => id switch
    {
        "micMute" => ctx.Runtime.IsAvailable("mic-mute"),
        "quitAllApps" => ctx.Runtime.IsAvailable("quit-apps"),
        "keyboardLight" => ctx.Runtime.IsAvailable("quick-toggles") && ToggleKeyboardLight.Present == true,
        _ => ctx.Runtime.IsAvailable("quick-toggles"),
    };

    /// <summary>True when the row takes over the screen, so the host closes first (150 ms) and the action runs after.</summary>
    public static bool NeedsDismiss(string id) => id is "lockScreen" or "displayOff" or "screenSaver" or "quitAllApps";

    // ---------------------------------------------------------------------------------------------------- running

    /// <summary>Runs the toggle (confirmation dialogs included); fire and forget.</summary>
    public static void Run(IContributionServices ctx, string id) => _ = RunAsync(ctx, id);

    /// <summary>Runs the toggle; true on success (false = "Could not complete.").</summary>
    public static async Task<bool> RunAsync(IContributionServices ctx, string id)
    {
        _services ??= ctx;
        if (TestRunner is { } test)
        {
            return test(id);
        }

        try
        {
            switch (id)
            {
                case "darkMode":
                    return ToggleDarkMode();
                case "keyboardLight":
                    return ToggleDarkKeyboard();
                case "micMute":
                    return ActionBridge.Invoke(ctx, "mic-mute", "toggle");
                case "emptyTrash":
                    if (!ToggleConfirmDialog.Ask("Empty the Recycle Bin?", "All items in the Recycle Bin will be removed. This cannot be undone.", "Empty the Recycle Bin"))
                    {
                        return true;
                    }

                    return await EmptyRecycleBinAsync();
                case "quitAllApps":
                    QuitAppsOps.Show(ctx, false);
                    return true;
                case "ejectDisks":
                    var excluded = ExcludedDrives();
                    return await Task.Run(() => ToggleDisks.EjectAll(excluded));
                case "hiddenFiles":
                    return ToggleHiddenFiles();
                case "desktopIcons":
                    return ToggleDesktopIcons();
                case "lockScreen":
                    return N.LockWorkStation();
                case "displayOff":
                    await Task.Delay(300);
                    return TurnDisplayOff();
                case "screenSaver":
                    return StartScreenSaver();
            }
        }
        catch (Exception ex)
        {
            ctx.Log.Error("Quick toggle " + id + " failed", ex);
        }

        return false;
    }

    /// <summary>Empties the Recycle Bin without asking (surfaces that already confirmed, e.g. the Command Bar).</summary>
    public static Task<bool> EmptyTrashConfirmed(IContributionServices ctx)
    {
        _services ??= ctx;
        return TestRunner is { } test ? Task.FromResult(test("emptyTrash")) : EmptyRecycleBinAsync();
    }

    private static bool ToggleDarkKeyboard()
    {
        var on = ToggleKeyboardLight.Level > 0;
        return ToggleKeyboardLight.SetLevel(on ? 0 : 1);
    }

    private static bool ToggleDarkMode()
    {
        var light = Dark ? 1 : 0;
        using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
        {
            key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
        }

        N.SendMessageTimeoutW(N.HWND_BROADCAST, N.WM_SETTINGCHANGE, 0, "ImmersiveColorSet", N.SMTO_ABORTIFHUNG, 200, out _);
        return true;
    }

    private static bool ToggleHiddenFiles()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(AdvancedKey))
        {
            key.SetValue("Hidden", HiddenFilesShown ? 2 : 1, RegistryValueKind.DWord);
        }

        RefreshExplorer();
        return true;
    }

    private static void RefreshExplorer()
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
        catch
        {
            // refresh is best effort
        }

        N.SHChangeNotify(0x08000000, 0, 0, 0);
    }

    private static nint DesktopListView(out nint defView)
    {
        var progman = N.FindWindowW("Progman", null);
        defView = N.FindWindowExW(progman, 0, "SHELLDLL_DefView", null);
        if (defView == 0)
        {
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

    private static bool ToggleDesktopIcons()
    {
        DesktopListView(out var defView);
        if (defView == 0)
        {
            return false;
        }

        N.SendMessageW(defView, N.WM_COMMAND, 0x7402, 0); // the shell's own "Show desktop icons" command (also updates HideIcons)
        return true;
    }

    private static bool TurnDisplayOff() => PostMessageW(N.HWND_BROADCAST, WmSysCommand, 0xF170, 2);

    private static bool StartScreenSaver()
    {
        string? saver = null;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
        {
            saver = key?.GetValue("SCRNSAVE.EXE") as string;
        }

        if (!string.IsNullOrWhiteSpace(saver))
        {
            N.SendMessageW(GetDesktopWindow(), WmSysCommand, 0xF140, 0);
            return true;
        }

        var blank = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Blank.scr");
        if (!File.Exists(blank))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(blank, "/s") { UseShellExecute = true });
        return true;
    }

    private static Task<bool> EmptyRecycleBinAsync() => Task.Run(() =>
    {
        var hr = N.SHEmptyRecycleBinW(0, null, N.SHERB_NOCONFIRMATION | N.SHERB_NOPROGRESSUI | N.SHERB_NOSOUND);
        return hr == 0 || hr == unchecked((int)0x8000FFFF);
    });

    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
}
