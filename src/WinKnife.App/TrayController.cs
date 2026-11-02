// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WinKnife.App;

/// <summary>The notification-area icon and its context menu.</summary>
internal sealed class TrayController : IDisposable
{
    private readonly ILog _log;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _autoStart;
    private readonly Drawing.Icon _iconImage;

    public TrayController(ILog log)
    {
        _log = log;
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/winknife.ico", UriKind.Absolute))
            ?? throw new InvalidOperationException("Tray icon resource is missing.");
        using (var stream = resource.Stream)
        {
            _iconImage = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Quick panel", null, (_, _) => QuickPanelRequested?.Invoke());
        menu.Items.Add("Features...", null, (_, _) => FeaturesRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _autoStart = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        _autoStart.Click += (_, _) => ToggleAutoStart();
        menu.Items.Add(_autoStart);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit WinKnife", null, (_, _) => QuitRequested?.Invoke());
        menu.Opening += (_, _) => _autoStart.Checked = SafeIsAutoStart();

        _icon = new Forms.NotifyIcon
        {
            Icon = _iconImage,
            Text = "WinKnife",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                QuickPanelRequested?.Invoke();
            }
        };
    }

    public event Action? QuickPanelRequested;

    public event Action? FeaturesRequested;

    public event Action? QuitRequested;

    public void ShowBalloon(string title, string message, NotificationLevel level)
    {
        var icon = level switch
        {
            NotificationLevel.Warning => Forms.ToolTipIcon.Warning,
            NotificationLevel.Error => Forms.ToolTipIcon.Error,
            _ => Forms.ToolTipIcon.Info,
        };
        _icon.ShowBalloonTip(4000, title, message, icon);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage.Dispose();
    }

    private bool SafeIsAutoStart()
    {
        try
        {
            return AutoStart.IsEnabled;
        }
        catch (Exception ex)
        {
            _log.Error("Reading the autostart registry value failed", ex);
            return false;
        }
    }

    private void ToggleAutoStart()
    {
        try
        {
            AutoStart.SetEnabled(!AutoStart.IsEnabled);
        }
        catch (Exception ex)
        {
            _log.Error("Changing autostart failed", ex);
            ShowBalloon("WinKnife", "Could not change the Start with Windows setting.", NotificationLevel.Error);
        }
    }
}
