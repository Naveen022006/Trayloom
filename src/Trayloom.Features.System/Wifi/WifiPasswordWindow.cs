// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools.Wifi;

/// <summary>
/// "Join {network}": asks for the password of a network that is not saved (spec 3.7). The password is read from the box only at the moment of
/// the call, handed to Windows and never logged or kept; the box is cleared when the window closes.
/// </summary>
internal sealed class WifiPasswordWindow : Window
{
    private readonly PasswordBox _password = new();
    private readonly CheckBox _auto = new() { IsChecked = true, Content = WifiStrings.ConnectAutomatically, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _status = UiKit.Text("", 12, null, ThemeKeys.TextSecondary, wrap: true);
    private readonly Button _connect;
    private readonly Button _cancel;
    private readonly bool _wep;
    private readonly Func<string, bool, Task<WifiConnectOutcome>> _join;
    private readonly ILog _log;
    private bool _busy;

    /// <param name="network">The network to join.</param>
    /// <param name="wep">True for WEP, whose passwords have other length rules.</param>
    /// <param name="join">Joins with a password and the automatic flag; supplied by the page.</param>
    public WifiPasswordWindow(WifiNetwork network, bool wep, Func<string, bool, Task<WifiConnectOutcome>> join, ILog log)
    {
        _wep = wep;
        _join = join;
        _log = log;
        Title = WifiStrings.JoinTitle(network.Ssid);
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.SetResourceReference(BackgroundProperty, ThemeKeys.SettingsWindow);
        SourceInitialized += (_, _) => ThemeManager.BindWindow(this);

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(UiKit.Text(Title, 15, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true));
        var prompt = UiKit.Text(WifiStrings.EnterPassword(network.Ssid), 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        prompt.Margin = new Thickness(0, 6, 0, 12);
        root.Children.Add(prompt);

        _password.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        _password.SetResourceReference(Control.BackgroundProperty, ThemeKeys.CardFill);
        _password.SetResourceReference(Control.BorderBrushProperty, ThemeKeys.CardBorder);
        _password.Padding = new Thickness(8, 6, 8, 6);
        _password.FontSize = 13;
        _password.PasswordChanged += (_, _) =>
        {
            Refresh();
        };
        WifiLook.Automate(_password, "Wifi.Password", WifiStrings.Title);
        root.Children.Add(_password);

        _auto.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        WifiLook.Automate(_auto, "Wifi.AutoConnect", WifiStrings.ConnectAutomatically);
        root.Children.Add(_auto);

        _status.Margin = new Thickness(0, 10, 0, 0);
        _status.Visibility = Visibility.Collapsed;
        root.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        _cancel = UiKit.Button(WifiStrings.Cancel, ButtonKind.Bordered, 12, null, Close);
        _connect = UiKit.Button(WifiStrings.Connect, ButtonKind.Prominent, 12, null, () => WifiGuard.FireAndLog(ConnectAsync, _log, "password connect"));
        _connect.Margin = new Thickness(8, 0, 0, 0);
        _connect.IsEnabled = false;
        WifiLook.Automate(_cancel, "Wifi.PasswordCancel", WifiStrings.Cancel);
        WifiLook.Automate(_connect, "Wifi.PasswordConnect", WifiStrings.Connect);
        buttons.Children.Add(_cancel);
        buttons.Children.Add(_connect);
        root.Children.Add(buttons);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_busy)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.Enter && _connect.IsEnabled)
            {
                e.Handled = true;
                _connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };
        Loaded += (_, _) => _password.Focus();
        Closed += (_, _) => _password.Clear();
    }

    private void Refresh() => _connect.IsEnabled = !_busy && WifiRules.PasswordLooksValid(_password.Password, _wep);

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _password.IsEnabled = !busy;
        _auto.IsEnabled = !busy;
        _cancel.IsEnabled = !busy;
        Refresh();
    }

    private async Task ConnectAsync()
    {
        SetBusy(true);
        _status.Text = WifiStrings.Connecting;
        _status.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        _status.Visibility = Visibility.Visible;
        var outcome = WifiConnectOutcome.Failed;
        try
        {
            outcome = await _join(_password.Password, _auto.IsChecked == true);
        }
        finally
        {
            SetBusy(false);
        }

        if (outcome == WifiConnectOutcome.Success)
        {
            Close();
            return;
        }

        _status.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Orange);
        _status.Text = outcome == WifiConnectOutcome.InvalidCredential ? WifiStrings.WrongPasswordTryAgain : WifiRules.FailureText(outcome);
        _password.Focus();
        _password.SelectAll();
    }
}
