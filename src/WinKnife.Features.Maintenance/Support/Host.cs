// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Maintenance;

/// <summary>
/// The services of the app, shared by the stateless contribution classes and the tool features (separate instances in
/// WinKnife's composition model). Every entry point calls <see cref="Attach"/> first.
/// </summary>
internal static class Host
{
    private static IContributionServices? _services;

    public static IContributionServices Services => _services ?? throw new InvalidOperationException("Maintenance host not attached");

    public static bool IsAttached => _services is not null;

    public static IAppSettings App => Services.App;

    public static IFeatureRuntime Runtime => Services.Runtime;

    public static void Attach(IContributionServices services)
    {
        _services = services;
        RegisterDefaults(services.App);
    }

    public static void Notify(string title, string message, NotificationLevel level = NotificationLevel.Info)
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            _services.Notifier.Show(title, message, level);
        }
        catch
        {
            // toast unavailable
        }
    }

    public static void OpenSettings(string destination)
    {
        try
        {
            _services?.Shell.OpenSettings(destination);
        }
        catch
        {
            // shell gone
        }
    }

    public static void Log(string message, Exception? exception = null)
    {
        if (_services is null)
        {
            return;
        }

        if (exception is null)
        {
            _services.Log.Info(message);
        }
        else
        {
            _services.Log.Error(message, exception);
        }
    }

    /// <summary>Registers every flat key of the maintenance tools with the Vorssaint name and default.</summary>
    public static void RegisterDefaults(IAppSettings app)
    {
        app.RegisterDefaults(
        [
            // Cleaner
            new("cleanerScheduleFrequency", "off"),
            new("cleanerScheduleHour", 9),
            new("cleanerScheduleMinute", 0),
            new("cleanerScheduleWeekday", 2),
            new("cleanerScheduleNotify", true),
            new("cleanerLastAutoRun", 0.0),
            new("cleanerLastAutoFreed", 0),
            new("cleanerLastAutoFailed", 0),
            new("cleanerScreenshotAgeDays", 30),
            new("panelUtilityCleaner", true),

            // Uninstaller
            new("uninstallerCommandBarEnabled", false),
            new("uninstallerSilent", false),
            new("panelUtilityUninstaller", true),

            // App updates
            new("appUpdatesCheckFrequency", "off"),
            new("appUpdatesIncludeHomebrewApps", true),
            new("appUpdatesIncludeAppStore", true),
            new("appUpdatesIncludeOnlineCatalog", true),
            new("appUpdatesNotify", true),
            new("appUpdatesRules", "[]"),
            new("appUpdatesLastCheck", 0.0),
            new("appUpdatesLastCount", 0),
            new("appUpdatesNotifiedIDs", new List<string>()),
            new("panelUtilityAppUpdates", true),
            new("agreementsAccepted", false),

            // Packages (winget)
            new("homebrewGroupDependencies", true),
            new("panelUtilityHomebrew", true),

            // Kill Process / Port Manager
            new("killProcessGroupRelated", true),
            new("killProcessCommandBarEnabled", true),
            new("killProcessSortBy", "cpu"),
            new("killProcessSortAscending", false),
            new("panelUtilityPortManager", true),

            // Media
            new("mediaLastTool", "videoCompressor"),
            new("mediaVideoStart", 0.0),
            new("mediaVideoEnd", 0.0),
            new("mediaVideoQuality", 0.68),
            new("mediaVideoMaxDimension", 1280),
            new("mediaVideoFPS", 30.0),
            new("mediaVideoKeepAudio", true),
            new("mediaVideoCodec", "h264"),
            new("mediaVideoSizing", "resolution"),
            new("mediaVideoTargetMegabytes", 20),
            new("mediaGIFStart", 0.0),
            new("mediaGIFEnd", 0.0),
            new("mediaGIFQuality", 0.74),
            new("mediaGIFWidth", 720),
            new("mediaGIFFPS", 12.0),
            new("mediaGIFLoops", true),
            new("mediaGIFSizing", "resolution"),
            new("mediaGIFTargetMegabytes", 10),
            new("mediaImageQuality", 0.72),
            new("mediaImageMaxDimension", 1600),
            new("mediaImageFormat", "jpeg"),
            new("mediaImageStripMetadata", true),
            new("mediaImageResizeKind", "maxDimension"),
            new("mediaImageResizeWidth", 1600),
            new("mediaImageResizeHeight", 1200),
            new("mediaImageExactResizeMode", "stretch"),
            new("mediaImageWatermarkKind", "off"),
            new("mediaImageWatermarkText", string.Empty),
            new("mediaImageWatermarkLogoPath", string.Empty),
            new("mediaImageWatermarkPosition", "bottomRight"),
            new("mediaImageWatermarkOpacity", 0.45),
            new("mediaImageWatermarkMargin", 32),
            new("mediaImageWatermarkScale", 0.18),
            new("mediaImageRenamePattern", string.Empty),
            new("mediaImageBackground", "transparent"),
            new("mediaImagePreserveModificationDate", false),
            new("mediaImageSaveInSubfolder", false),
            new("mediaImageProfiles", "[]"),
            new("mediaImageSelectedProfileID", string.Empty),
            new("mediaTextAccurate", true),
            new("mediaTextLanguageCorrection", true),
            new("panelUtilityMedia", true),
        ]);
    }
}

/// <summary>Small modal dialogs in the app theme.</summary>
internal static class Dialogs
{
    /// <summary>Confirm / cancel dialog. Returns true on confirm.</summary>
    public static bool Confirm(string title, string message, string confirmText, bool destructive, string cancelText = "Cancel", Action<bool>? keepOpen = null)
    {
        keepOpen?.Invoke(true);
        try
        {
            var accepted = false;
            var window = NewWindow(title);
            var body = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
            body.Children.Add(UiKit.Text(title, 14, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true));
            if (!string.IsNullOrEmpty(message))
            {
                var text = UiKit.Text(message, 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
                text.Margin = new Thickness(0, 8, 0, 0);
                body.Children.Add(text);
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = UiKit.Button(cancelText, ButtonKind.Bordered, 12, null, () => window.Close());
            cancel.IsCancel = true;
            var ok = UiKit.Button(confirmText, destructive ? ButtonKind.Destructive : ButtonKind.Prominent, 12, null, () =>
            {
                accepted = true;
                window.Close();
            });
            ok.IsDefault = true;
            ok.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            body.Children.Add(buttons);
            window.Content = body;
            window.ShowDialog();
            return accepted;
        }
        finally
        {
            keepOpen?.Invoke(false);
        }
    }

    /// <summary>Single-button information dialog.</summary>
    public static void Alert(string title, string message, string okText = "OK", Action<bool>? keepOpen = null)
    {
        keepOpen?.Invoke(true);
        try
        {
            var window = NewWindow(title);
            var body = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
            body.Children.Add(UiKit.Text(title, 14, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true));
            var text = UiKit.Text(message, 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            text.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(text);
            var ok = UiKit.Button(okText, ButtonKind.Prominent, 12, null, () => window.Close());
            ok.IsDefault = true;
            ok.HorizontalAlignment = HorizontalAlignment.Right;
            ok.Margin = new Thickness(0, 18, 0, 0);
            body.Children.Add(ok);
            window.Content = body;
            window.ShowDialog();
        }
        finally
        {
            keepOpen?.Invoke(false);
        }
    }

    private static Window NewWindow(string title)
    {
        var window = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStyle = WindowStyle.ToolWindow,
        };
        window.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.WindowStyle);
        ThemeManager.BindWindow(window);
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        return window;
    }

    /// <summary>Shows an open-file dialog; returns the chosen paths or an empty array.</summary>
    public static string[] OpenFiles(string title, string filter, bool multiselect)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, Multiselect = multiselect, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    /// <summary>Shows a save dialog; returns the chosen path or null.</summary>
    public static string? SaveFile(string title, string filter, string? directory, string? fileName, string? extension)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            Filter = filter,
            InitialDirectory = directory ?? string.Empty,
            FileName = fileName ?? string.Empty,
            DefaultExt = extension ?? string.Empty,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>Shows a folder picker.</summary>
    public static string? PickFolder(string title, string? initial)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title, InitialDirectory = initial ?? string.Empty };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
