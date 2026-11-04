// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.GitHub;

/// <summary>Settings &gt; Dynamic Island &gt; "GitHub": connect, disconnect and the polling options (spec 06 section 2.8).</summary>
public sealed class GitHubSettingsSection : ISettingsSection
{
    public string? FeatureId => GhText.FeatureId;

    public string PageId => SettingsPages.Island;

    public string? Anchor => "github";

    public string? Title => GhText.Title;

    public int Order => 180;

    public IReadOnlyList<string> Keywords => ["pull requests", "notifications", "checks", "CI", "releases", "token", "gh"];

    public FrameworkElement CreateView(ISettingsContext context) => new GitHubSettingsView(context).Build();
}

/// <summary>Builds the card; one instance per (re)build of the page.</summary>
internal sealed class GitHubSettingsView
{
    private readonly ISettingsContext _context;
    private readonly IAppSettings _app;
    private readonly GitHubRuntime? _runtime;
    private readonly TextBlock _status = UiKit.Text("", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
    private SettingsCard? _card;

    public GitHubSettingsView(ISettingsContext context)
    {
        _context = context;
        _app = context.App;
        GhKeys.Register(_app);
        _runtime = context.Services.Get<GitHubRuntime>();
    }

    private static FrameworkElement Indent(UIElement child) => new Border { Child = child, Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };

    private static void Name(DependencyObject element, string id, string name)
    {
        AutomationProperties.SetAutomationId(element, id);
        AutomationProperties.SetName(element, name);
    }

    public FrameworkElement Build()
    {
        var card = new SettingsCard(GhText.Title);
        _card = card;
        if (_runtime is null)
        {
            card.Add(new SettingsRow("", GhText.SettingsNotConnected, GhText.SettingsNotConnectedCaption));
            return card;
        }

        if (_runtime.Connected)
        {
            BuildConnected(card, _runtime);
        }
        else
        {
            BuildNotConnected(card, _runtime);
        }

        card.Loaded += (_, _) => _runtime.Changed += OnRuntimeChanged;
        card.Unloaded += (_, _) => _runtime.Changed -= OnRuntimeChanged;
        return card;
    }

    private void OnRuntimeChanged()
    {
        if (_runtime is null || _card is null)
        {
            return;
        }

        if (_runtime.Connected)
        {
            UpdateStatus(_runtime);
        }
    }

    // ---------------------------------------------------------------- not connected

    private void BuildNotConnected(SettingsCard card, GitHubRuntime runtime)
    {
        card.Add(new SettingsRow("", GhText.SettingsNotConnected, GhText.SettingsNotConnectedCaption));
        var connect = UiKit.Button(GhText.ConnectCli, ButtonKind.Prominent, 12.5);
        connect.IsEnabled = false;
        Name(connect, "GitHub.ConnectCli", GhText.ConnectCli);
        var token = UiKit.Button(GhText.UseToken, ButtonKind.Bordered, 12.5, null, () => OpenTokenDialog(runtime));
        Name(token, "GitHub.UseToken", GhText.UseToken);
        token.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(connect);
        buttons.Children.Add(token);
        card.Add(Indent(buttons));

        var caption = UiKit.Text("", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        var site = UiKit.Button(GhText.CliSite, ButtonKind.Plain, 11.5, null, () => GitHubLinks.Open("https://cli.github.com"));
        site.Visibility = Visibility.Collapsed;
        site.HorizontalAlignment = HorizontalAlignment.Left;
        var note = new StackPanel();
        note.Children.Add(caption);
        note.Children.Add(site);
        card.Add(Indent(note));
        var failure = UiKit.Text("", 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        failure.Visibility = Visibility.Collapsed;
        card.Add(Indent(failure));

        // gh is looked for off the UI thread: it can take a moment to answer `--version`.
        GitHubGuard.FireAndLog(async () =>
        {
            var version = await GitHubRuntime.FindCliAsync(CancellationToken.None).ConfigureAwait(false);
            await card.Dispatcher.InvokeAsync(() =>
            {
                if (version is null)
                {
                    caption.Text = GhText.CliMissing;
                    site.Visibility = Visibility.Visible;
                }
                else
                {
                    caption.Text = GhText.CliFound(version);
                    connect.IsEnabled = true;
                }
            });
        }, runtime.Services.Log, "gh lookup");

        connect.Click += async (_, _) =>
        {
            try
            {
                connect.IsEnabled = false;
                connect.Content = GhText.Connecting;
                failure.Visibility = Visibility.Collapsed;
                var result = await runtime.ConnectCliAsync();
                if (result == CliConnectResult.Ok)
                {
                    _context.RequestRebuild();
                    return;
                }

                failure.Text = result switch
                {
                    CliConnectResult.SignedOut => GhText.FailSignedOut,
                    CliConnectResult.TimedOut => GhText.FailTimeout,
                    _ => GhText.FailStart,
                };
                failure.Visibility = Visibility.Visible;
                connect.Content = GhText.ConnectCli;
                connect.IsEnabled = true;
            }
            catch (Exception ex)
            {
                runtime.Services.Log.Error("[github] connect failed (" + ex.GetType().Name + " " + ex.HResult + ")");
                connect.Content = GhText.ConnectCli;
                connect.IsEnabled = true;
            }
        };
    }

    private void OpenTokenDialog(GitHubRuntime runtime)
    {
        var window = new GitHubTokenWindow(runtime);
        if (_card is not null && Window.GetWindow(_card) is { } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        if (window.Connected)
        {
            _context.RequestRebuild();
        }
    }

    // ---------------------------------------------------------------- connected

    private void BuildConnected(SettingsCard card, GitHubRuntime runtime)
    {
        var disconnect = UiKit.Button(GhText.Disconnect, ButtonKind.Bordered, 12.5, null, () =>
        {
            runtime.Disconnect();
            _context.RequestRebuild();
        });
        Name(disconnect, "GitHub.Disconnect", GhText.Disconnect);
        var login = runtime.Login;
        card.Add(new SettingsRow("", GhText.ConnectedAs(login), _app.Get<string>(GhKeys.Source) == "token" ? GhText.WithToken : GhText.ThroughCli, disconnect));

        var refresh = UiKit.Button(GhText.RefreshNow, ButtonKind.Bordered, 11.5, null, () => runtime.RequestRefresh());
        Name(refresh, "GitHub.RefreshNow", GhText.RefreshNow);
        var statusRow = new Grid();
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(refresh, 1);
        statusRow.Children.Add(_status);
        statusRow.Children.Add(refresh);
        card.Add(Indent(statusRow));
        UpdateStatus(runtime);

        var interval = new ComboBox { MinWidth = 160, ItemsSource = GhKeys.Intervals.Select(GhText.Minutes).ToList() };
        var current = _app.Get<int>(GhKeys.Interval);
        interval.SelectedIndex = Math.Max(0, Array.IndexOf(GhKeys.Intervals, current));
        interval.SelectionChanged += (_, _) =>
        {
            if (interval.SelectedIndex >= 0)
            {
                _app.Set(GhKeys.Interval, GhKeys.Intervals[interval.SelectedIndex]);
            }
        };
        Name(interval, "GitHub.Interval", GhText.CheckEvery);
        card.Add(new SettingsRow("", GhText.CheckEvery, null, interval));

        card.Add(new SettingsRow("", GhText.Repositories, GhText.RepositoriesCaption, ReposBox()));
        var invalid = UiKit.Text(GhText.ReposInvalid, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        invalid.Visibility = Visibility.Collapsed;
        card.Add(Indent(invalid));
        _invalid = invalid;

        var notices = new MacSwitch(_app.Get<bool>(GhKeys.Notices), SwitchSize.Regular, on => _app.Set(GhKeys.Notices, on));
        Name(notices, "GitHub.Notices", GhText.ShowAlerts);
        card.Add(new SettingsRow("", GhText.ShowAlerts, GhText.ShowAlertsCaption, notices));
    }

    private TextBlock? _invalid;

    private FrameworkElement ReposBox()
    {
        var box = new TextBox { Width = 260, Text = _app.Get<string>(GhKeys.Repos), VerticalContentAlignment = VerticalAlignment.Center };
        Name(box, "GitHub.Repos", GhText.Repositories);
        var hint = UiKit.Text(GhText.ReposPlaceholder, 12, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(7, 0, 0, 0);
        hint.HorizontalAlignment = HorizontalAlignment.Left;
        hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        void Save()
        {
            var text = box.Text.Trim();
            if (GitHubQuery.ParseRepos(text) is { } list)
            {
                _app.Set(GhKeys.Repos, string.Join(", ", list));
                if (_invalid is not null)
                {
                    _invalid.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                // The old value stays; the box shows what is stored again.
                box.Text = _app.Get<string>(GhKeys.Repos);
                if (_invalid is not null)
                {
                    _invalid.Visibility = Visibility.Visible;
                }
            }
        }

        box.LostFocus += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Save();
            }
        };
        var grid = new Grid { Width = 260 };
        grid.Children.Add(box);
        grid.Children.Add(hint);
        return grid;
    }

    private void UpdateStatus(GitHubRuntime runtime)
    {
        var s = runtime.Snapshot;
        var fetched = s is not null && s.FetchedAt.Year > 1;
        string text;
        if (s is { Error: not GhError.None })
        {
            text = ErrorLine(s, fetched, runtime);
        }
        else
        {
            text = fetched ? GhText.Updated(GitHubTime.Relative(s!.FetchedAt)) : GhText.Checking;
        }

        _status.Text = text;
        // The status line is how a script learns who is connected, so its automation name carries that too.
        Name(_status, "GitHub.Status", GhText.ConnectedAs(runtime.Login) + ". " + text);
    }

    private static string ErrorLine(GitHubSnapshot s, bool fetched, GitHubRuntime runtime) => GitHubPage.ErrorText(s, fetched, runtime.NextPollUtc) ?? "";
}
