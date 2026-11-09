// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>How the install offer ended.</summary>
internal enum InstallChoice
{
    /// <summary>The window was closed without a choice.</summary>
    Closed,

    /// <summary>Trayloom was installed; <see cref="InstallWindow.InstalledExe"/> is the new executable.</summary>
    Installed,

    /// <summary>"Just run it".</summary>
    JustRun,
}

/// <summary>
/// "Install Trayloom" offer, shown when the exe runs from outside the install folder (for instance from the extracted zip).
/// It is the installer: it copies the app for this user only and creates shortcuts. Non-modal so the tray keeps working.
/// </summary>
internal sealed class InstallWindow : Window
{
    private readonly ILog _log;
    private readonly string? _installedVersion;
    private readonly StackPanel _body = new() { Margin = new Thickness(24, 20, 24, 20) };
    private bool _startMenu = true;
    private bool _desktop = true;
    private bool _login = true;
    private bool _busy;

    public InstallWindow(ILog log)
    {
        _log = log;
        _installedVersion = InstallManager.InstalledVersion;
        var update = _installedVersion is not null;
        Title = update ? Loc.T("Update Trayloom") : Loc.T("Install Trayloom");
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = UiKit.TextFont;
        UseLayoutRounding = true;
        this.Themed(StyleProperty, ThemeKeys.WindowStyle);
        ThemeManager.BindWindow(this);

        var root = new StackPanel();
        root.SetResourceReference(Panel.BackgroundProperty, ThemeKeys.SettingsWindow);
        root.Children.Add(UiParts.Hero(138, 70, update ? Loc.T("Update Trayloom") : Loc.T("Install Trayloom"), null));
        root.Children.Add(_body);
        var background = new Border { Child = root };
        background.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsWindow);
        Content = background;
        ShowChoices();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && !_busy)
            {
                Close();
            }
        };
    }

    /// <summary>How the offer ended.</summary>
    public InstallChoice Choice { get; private set; }

    /// <summary>The user ticked "Don't ask again".</summary>
    public bool DontAskAgain { get; private set; }

    /// <summary>The installed executable after <see cref="InstallChoice.Installed"/>.</summary>
    public string? InstalledExe { get; private set; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private void ShowChoices(string? error = null)
    {
        _body.Children.Clear();
        var update = _installedVersion is not null;
        var intro = update
            ? Loc.F("Trayloom {0} is already installed for your account. This updates it to {1}, keeping your settings.", _installedVersion, AppInfo.Version)
            : Loc.T("Install Trayloom for your Windows account to add it to the Start menu and keep it after you delete the download. Windows will not ask for an administrator password.");
        _body.Children.Add(UiParts.Paragraph(intro, 13, ThemeKeys.TextPrimary, 6));
        _body.Children.Add(UiParts.Paragraph(Loc.F("Installs to {0}", InstallManager.InstallRoot), 11.5, ThemeKeys.TextTertiary, 14));

        var card = new SettingsCard();
        card.Add(Row("", Loc.T("Start menu shortcut"), Loc.T("Press the Windows key and type “Trayloom” to open it."), _startMenu, on => _startMenu = on));
        card.AddDivider();
        card.Add(Row("", Loc.T("Desktop shortcut"), Loc.T("A Trayloom icon on your desktop."), _desktop, on => _desktop = on));
        card.AddDivider();
        card.Add(Row("", Loc.T("Start Trayloom when I sign in"), Loc.T("Recommended: the tray icon is always there when you need it."), _login, on => _login = on));
        _body.Children.Add(card);

        if (error is not null)
        {
            var err = UiParts.Paragraph(Loc.F("The install did not finish: {0}", error), 12, ThemeKeys.Orange, 0);
            err.Margin = new Thickness(0, 12, 0, 0);
            _body.Children.Add(err);
        }

        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ask = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dont = new MacSwitch(DontAskAgain, SwitchSize.Mini, on => DontAskAgain = on);
        ask.Children.Add(dont);
        var askText = UiKit.Text(Loc.T("Don’t ask again"), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        askText.Margin = new Thickness(8, 0, 0, 0);
        ask.Children.Add(askText);
        footer.Children.Add(ask);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var justRun = UiKit.Button(Loc.T("Just run it"), ButtonKind.Bordered, 13, onClick: () =>
        {
            Choice = InstallChoice.JustRun;
            Close();
        });
        justRun.Padding = new Thickness(14, 6, 14, 6);
        var install = UiKit.Button(update ? Loc.T("Update") : Loc.T("Install"), ButtonKind.Prominent, 13, onClick: StartInstall);
        install.Padding = new Thickness(18, 6, 18, 6);
        install.Margin = new Thickness(8, 0, 0, 0);
        install.IsDefault = true;
        buttons.Children.Add(justRun);
        buttons.Children.Add(install);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        _body.Children.Add(footer);
    }

    private static UIElement Row(string glyph, string title, string caption, bool on, Action<bool> set)
    {
        var toggle = new MacSwitch(on, SwitchSize.Regular, set);
        return new SettingsRow(glyph, title, caption, toggle);
    }

    private void StartInstall()
    {
        var options = new InstallOptions(_startMenu, _desktop, _login);
        _busy = true;
        _body.Children.Clear();
        _body.Children.Add(UiParts.Paragraph(_installedVersion is not null ? Loc.T("Updating Trayloom…") : Loc.T("Installing Trayloom…"), 14, ThemeKeys.TextPrimary, 6));
        _body.Children.Add(UiParts.Paragraph(Loc.T("Copying files. This takes a few seconds the first time because Windows checks each file."), 12, ThemeKeys.TextSecondary, 14));
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6 };
        _body.Children.Add(bar);
        var progress = new Progress<double>(v => bar.Value = v);
        Task.Run(() => InstallManager.Install(options, _log, progress)).ContinueWith(task =>
        {
            _busy = false;
            if (task.IsFaulted)
            {
                var ex = task.Exception!.GetBaseException();
                _log.Error("Install failed", ex);
                ShowChoices(ex.Message);
                return;
            }

            InstalledExe = task.Result;
            Choice = InstallChoice.Installed;
            Close();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}

/// <summary>The "Uninstall Trayloom?" confirmation of <c>Trayloom.exe --uninstall</c>.</summary>
internal sealed class UninstallWindow : Window
{
    private bool _removeSettings;

    public UninstallWindow()
    {
        Title = Loc.T("Uninstall Trayloom");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = UiKit.TextFont;
        this.Themed(StyleProperty, ThemeKeys.WindowStyle);
        ThemeManager.BindWindow(this);

        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        body.Children.Add(UiKit.Text(Loc.T("Uninstall Trayloom?"), 17, FontWeights.Bold, ThemeKeys.TextPrimary));
        var text = UiParts.Paragraph(Loc.T("This quits Trayloom and removes its program files, the Start menu and Desktop shortcuts, the Apps & features entry and the start-at-sign-in entry."), 13, ThemeKeys.TextSecondary, 14);
        text.Margin = new Thickness(0, 8, 0, 14);
        body.Children.Add(text);
        var card = new SettingsCard();
        card.Add(new SettingsRow("", Loc.T("Also delete my data"), Loc.T("Settings, Scratchpad notes, clipboard history, recent screenshots, unsaved recordings and caches. Keep them if you might reinstall later."), new MacSwitch(false, SwitchSize.Regular, on => _removeSettings = on)));
        body.Children.Add(card);
        var restore = UiParts.Paragraph(Loc.T("Changes Trayloom made to Windows, such as the lid setting or a muted microphone, are put back. Its Claude Code hook is removed."), 12, ThemeKeys.TextTertiary, 14);
        restore.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(restore);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = UiKit.Button(Loc.T("Cancel"), ButtonKind.Bordered, 13, onClick: Close);
        cancel.Padding = new Thickness(16, 6, 16, 6);
        cancel.IsCancel = true;
        var go = UiKit.Button(Loc.T("Uninstall"), ButtonKind.Destructive, 13, onClick: () =>
        {
            Confirmed = true;
            Close();
        });
        go.Padding = new Thickness(16, 6, 16, 6);
        go.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(go);
        body.Children.Add(buttons);
        var background = new Border { Child = body };
        background.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsWindow);
        Content = background;
    }

    public bool Confirmed { get; private set; }

    public UninstallOptions Options => new(_removeSettings);
}
