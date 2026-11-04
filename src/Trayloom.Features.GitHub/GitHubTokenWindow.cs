// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.GitHub;

/// <summary>
/// "Connect with a token" (spec 06 section 2.8): the one place a token is typed, in a <see cref="PasswordBox"/> that is cleared whenever the
/// dialog closes. The token goes to <see cref="GitHubRuntime.ConnectTokenAsync"/> and nowhere else.
/// </summary>
internal sealed class GitHubTokenWindow : Window
{
    private readonly GitHubRuntime _runtime;
    private readonly PasswordBox _box = new();
    private readonly TextBlock _status = UiKit.Text("", 12, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
    private readonly Button _connect;
    private readonly Button _cancel;

    public GitHubTokenWindow(GitHubRuntime runtime)
    {
        _runtime = runtime;
        Title = GhText.TokenTitle;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Themed(BackgroundProperty, ThemeKeys.SettingsWindow);
        this.Themed(ForegroundProperty, ThemeKeys.TextPrimary);
        FontFamily = UiKit.TextFont;
        ThemeManager.BindWindow(this);

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(UiKit.Text(GhText.TokenTitle, 16, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        stack.AddSpaced(UiKit.Text(GhText.TokenIntro, 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true), 8);
        AutomationProperties.SetAutomationId(_box, "GitHub.TokenBox");
        AutomationProperties.SetName(_box, GhText.TokenTitle);
        stack.AddSpaced(_box, 12);
        stack.AddSpaced(UiKit.Text(GhText.TokenScopes, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true), 8);
        var link = UiKit.Button(GhText.TokenCreate, ButtonKind.Plain, 12, null, () => GitHubLinks.Open("https://github.com/settings/tokens/new?scopes=notifications,repo&description=Trayloom"));
        link.HorizontalAlignment = HorizontalAlignment.Left;
        stack.AddSpaced(link, 4);
        stack.AddSpaced(_status, 8);

        _cancel = UiKit.Button(GhText.Cancel, ButtonKind.Bordered, 12, null, Close);
        AutomationProperties.SetAutomationId(_cancel, "GitHub.TokenCancel");
        AutomationProperties.SetName(_cancel, GhText.Cancel);
        _connect = UiKit.Button(GhText.Connect, ButtonKind.Prominent, 12, null, ConnectClicked);
        AutomationProperties.SetAutomationId(_connect, "GitHub.TokenConnect");
        AutomationProperties.SetName(_connect, GhText.Connect);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_cancel);
        _connect.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(_connect);
        stack.AddSpaced(buttons, 14);
        Content = stack;

        // The password must not outlive the dialog on any path (button, title-bar close, Escape).
        Closed += (_, _) => _box.Clear();
    }

    /// <summary>True when the token was accepted and stored.</summary>
    public bool Connected { get; private set; }

    private async void ConnectClicked()
    {
        try
        {
            var token = _box.Password.Trim();
            if (token.Length == 0)
            {
                return;
            }

            _connect.IsEnabled = false;
            _box.IsEnabled = false;
            _status.Text = GhText.Checking;
            var result = await _runtime.ConnectTokenAsync(token);
            _box.Clear();
            token = "";
            switch (result)
            {
                case TokenConnectResult.Ok:
                    Connected = true;
                    Close();
                    return;
                case TokenConnectResult.Rejected:
                    _status.Text = GhText.TokenRejected;
                    break;
                default:
                    _status.Text = GhText.TokenOffline;
                    break;
            }

            _connect.IsEnabled = true;
            _box.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _status.Text = GhText.TokenOffline;
            _connect.IsEnabled = true;
            _box.IsEnabled = true;
            _runtime.Services.Log.Error("[github] token dialog failed (" + ex.GetType().Name + " " + ex.HResult + ")");
        }
    }
}
