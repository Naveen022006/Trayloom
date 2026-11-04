// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

/// <summary>
/// Settings > Dynamic Island > Content > Now Playing: the "Music services" card. Sets up Spotify (Client ID, redirect URI, sign-in) and
/// YouTube Music (API key) for the island's Library, and says what each service can and cannot do. Nothing is sent until a service is
/// set up here. The card follows <see cref="MusicServices.Changed"/> while it is shown.
/// </summary>
internal sealed class MusicServicesCard
{
    private const string DashboardUrl = "https://developer.spotify.com/dashboard";
    private const string ManageAppsUrl = "https://www.spotify.com/account/apps/";
    private const string CloudConsoleUrl = "https://console.cloud.google.com/apis/library/youtube.googleapis.com";
    private const string YouTubeTermsUrl = "https://www.youtube.com/t/terms";
    private const string GooglePrivacyUrl = "https://policies.google.com/privacy";

    private readonly ISettingsContext _c;
    private readonly IslandCore? _core;
    private readonly SpotifyService? _spotify;
    private readonly YouTubeService? _youTube;
    private readonly SettingsCard _card = new(Loc.T("Music services"));

    // Spotify
    private SettingsRow? _spotifyRow;
    private Button? _connect;
    private TextBlock? _connectingHelp;
    private TextBlock? _spotifyError;
    private StackPanel? _manual;
    private TextBox? _manualUrl;
    private MacSwitch? _spotifySwitch;
    private TextBox? _clientId;
    private TextBlock? _clientNote;
    private StreamStatus? _spotifyStatus;
    private bool _clientChanged;

    // YouTube
    private MacSwitch? _youTubeSwitch;
    private PasswordBox? _apiKey;
    private TextBlock? _keyStatus;
    private Button? _save;
    private Button? _remove;
    private bool _checking;
    private string? _keyMessage;
    private DispatcherTimer? _keyTimer;

    private MusicServicesCard(ISettingsContext c, IslandCore? core)
    {
        _c = c;
        _core = core;
        _spotify = core?.Music.Spotify;
        _youTube = core?.Music.YouTube;
    }

    /// <summary>Builds the card.</summary>
    public static SettingsCard Create(ISettingsContext c, IslandCore? core) => new MusicServicesCard(c, core).Build();

    private IAppSettings App => _c.App;

    private SettingsCard Build()
    {
        _card.AddCaption(Loc.T("Find and play music from Spotify and YouTube Music in the island’s Library. Nothing is sent until you set a service up here."));
        if (_core is null || _spotify is null || _youTube is null)
        {
            _card.AddCaption(Loc.T("Turn on the Dynamic Island to set up music services."));
            return _card;
        }

        _card.AddDivider();
        BuildSpotify(_spotify);
        _card.AddDivider();
        BuildYouTube(_youTube);
        var music = _core.Music;
        _card.Loaded += (_, _) =>
        {
            music.Changed += OnChanged;
            Refresh();
        };
        _card.Unloaded += (_, _) =>
        {
            music.Changed -= OnChanged;
            _keyTimer?.Stop();
        };
        Refresh();
        return _card;
    }

    private void OnChanged() => Refresh();

    // ---------------------------------------------------------------- Spotify

    private void BuildSpotify(SpotifyService spotify)
    {
        _connect = UiKit.Button(Loc.T("Connect"), ButtonKind.Prominent, 12);
        _connect.Click += (_, _) => OnConnectClicked(spotify);
        AutomationProperties.SetAutomationId(_connect, "Island.Settings.Spotify.Connect");
        _spotifyRow = new SettingsRow(null, "Spotify", Loc.T("Not connected"), _connect);
        _card.Add(_spotifyRow);

        _connectingHelp = UiKit.Text(Loc.T("If Spotify shows “Invalid redirect URI”, add the redirect URI below to your Spotify app, then try again."), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, true);
        _card.Add(_connectingHelp);
        _spotifyError = UiKit.Text("", 11.5, FontWeights.Normal, ThemeKeys.Orange, true);
        _card.Add(_spotifyError);

        // the browser could not be opened: the sign-in address to copy into one by hand
        _manual = new StackPanel();
        _manualUrl = ReadOnlyBox(string.Empty, 340);
        AutomationProperties.SetAutomationId(_manualUrl, "Island.Settings.Spotify.ManualUrl");
        _manual.Children.Add(Field(Loc.T("Couldn’t open your browser. Copy this link into it:"), _manualUrl, 340, CopyButton(() => _manualUrl.Text)));
        _card.Add(_manual);

        _spotifySwitch = new MacSwitch(App.Get<bool>(K.MusicSpotify), SwitchSize.Regular, on => App.Set(K.MusicSpotify, on));
        _card.Add(new SettingsRow(null, Loc.T("Show Spotify in the island"), null, _spotifySwitch));

        _clientId = new TextBox { Width = 280, MaxLength = 64, Text = (App.Get<string>(K.SpotifyClientId) ?? string.Empty).Trim() };
        _clientId.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        AutomationProperties.SetAutomationId(_clientId, "Island.Settings.Spotify.ClientId");
        AutomationProperties.SetName(_clientId, Loc.T("Client ID"));
        _clientId.LostFocus += (_, _) => CommitClientId(spotify);
        _clientId.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                CommitClientId(spotify);
                e.Handled = true;
            }
        };
        _card.Add(Field(Loc.T("Client ID"), _clientId, 300));
        _clientNote = UiKit.Text(Loc.T("Changing the Client ID disconnects Spotify."), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, true);
        _card.Add(_clientNote);

        _card.AddCaption(Loc.T("Spotify only lets apps like Trayloom in through a Client ID. Create one at developer.spotify.com: Dashboard › Create app, pick Web API, and add the redirect URI below. Then paste the app’s Client ID here. Creating an app needs Spotify Premium."));
        if (SpotifyAuth.BuiltInClientId.Length > 0)
        {
            _card.AddCaption(Loc.T("Leave it empty to use Trayloom’s shared Client ID. It works only for Spotify accounts its owner has added."));
        }

        var redirect = ReadOnlyBox(SpotifyService.RedirectUri, 250);
        AutomationProperties.SetAutomationId(redirect, "Island.Settings.Spotify.RedirectUri");
        AutomationProperties.SetName(redirect, Loc.T("Redirect URI"));
        _card.Add(Field(Loc.T("Redirect URI"), redirect, 260, CopyButton(() => SpotifyService.RedirectUri)));

        _card.Add(LinkButton(Loc.T("Open Spotify Dashboard"), DashboardUrl));
        _card.AddCaption(Loc.T("Playing, queueing and seeking from the island need Spotify Premium. Without Premium, songs open in the Spotify app."));
        _card.AddCaption(Loc.T("Trayloom can read your playlists, liked songs, recently played songs and what’s playing, and control playback. It can’t change your library or playlists. Your sign-in is stored encrypted on this PC."));
        _card.AddCaption(Loc.T("Disconnect deletes the sign-in from this PC. To also remove Trayloom from your Spotify account, open Manage apps."));
        _card.Add(LinkButton(Loc.T("Manage apps"), ManageAppsUrl));
    }

    private void OnConnectClicked(SpotifyService spotify)
    {
        switch (spotify.Connection)
        {
            case StreamConnection.Connecting:
                spotify.CancelConnect();
                break;
            case StreamConnection.Connected:
                spotify.Disconnect();
                _spotifyStatus = null;
                Refresh();
                break;
            default:
                _spotifyStatus = null;
                Guarded.FireAndLog(
                    async () =>
                    {
                        _spotifyStatus = await spotify.ConnectAsync(CancellationToken.None);
                        Refresh();
                    },
                    _core!.Ctx.Log,
                    "Spotify connect");
                break;
        }
    }

    /// <summary>Saves the Client ID (trimmed). Changing it while signed in signs out: the sign-in belongs to the old app.</summary>
    private void CommitClientId(SpotifyService spotify)
    {
        if (_clientId is null)
        {
            return;
        }

        var value = _clientId.Text.Trim();
        if (value != _clientId.Text)
        {
            _clientId.Text = value;
        }

        if (value == (App.Get<string>(K.SpotifyClientId) ?? string.Empty).Trim())
        {
            return;
        }

        var wasSignedIn = spotify.Connection is StreamConnection.Connected or StreamConnection.NeedsReconnect;
        App.Set(K.SpotifyClientId, value);
        if (wasSignedIn)
        {
            _clientChanged = true;
            spotify.Disconnect();
        }

        _spotifyStatus = null;
        Refresh();
    }

    private void RefreshSpotify(SpotifyService spotify)
    {
        var connection = spotify.Connection;
        if (_spotifyRow is not null)
        {
            _spotifyRow.Caption = connection switch
            {
                StreamConnection.NotConfigured => Loc.T("Add a Client ID below to connect."),
                StreamConnection.Connecting => Loc.T("Finish signing in in your browser…"),
                StreamConnection.Connected => spotify.DisplayName.Length > 0 ? Loc.F("Connected as {0}", spotify.DisplayName) : Loc.T("Connected"),
                StreamConnection.NeedsReconnect => Loc.T("Spotify asks you to connect again."),
                _ => Loc.T("Not connected"),
            };
        }

        if (_connect is not null)
        {
            var (text, kind) = connection switch
            {
                StreamConnection.Connecting => (Loc.T("Cancel"), ButtonKind.Bordered),
                StreamConnection.Connected => (Loc.T("Disconnect"), ButtonKind.Bordered),
                StreamConnection.NeedsReconnect => (Loc.T("Reconnect"), ButtonKind.Prominent),
                _ => (Loc.T("Connect"), ButtonKind.Prominent),
            };
            _connect.Content = text;
            _connect.SetResourceReference(FrameworkElement.StyleProperty, kind == ButtonKind.Prominent ? ThemeKeys.ButtonProminent : ThemeKeys.ButtonBordered);
        }

        if (_connectingHelp is not null)
        {
            _connectingHelp.Visibility = connection == StreamConnection.Connecting ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_spotifyError is not null)
        {
            var message = connection == StreamConnection.Connecting || _spotifyStatus is null or StreamStatus.Ok or StreamStatus.Cancelled
                ? string.Empty
                : StreamStrings.Message(_spotifyStatus.Value, StreamKind.Spotify);
            _spotifyError.Text = message;
            _spotifyError.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_manual is not null && _manualUrl is not null)
        {
            var url = connection == StreamConnection.Connecting ? spotify.ManualSignInUrl : null;
            _manualUrl.Text = url ?? string.Empty;
            _manual.Visibility = url is null ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_spotifySwitch is not null && _spotifySwitch.IsOn != App.Get<bool>(K.MusicSpotify))
        {
            _spotifySwitch.IsOn = App.Get<bool>(K.MusicSpotify);
        }

        if (_clientNote is not null)
        {
            _clientNote.Visibility = connection == StreamConnection.Connected || _clientChanged ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---------------------------------------------------------------- YouTube

    private void BuildYouTube(YouTubeService youTube)
    {
        _card.Add(new SettingsRow(null, "YouTube Music", null));
        _youTubeSwitch = new MacSwitch(App.Get<bool>(K.MusicYouTube), SwitchSize.Regular, on => App.Set(K.MusicYouTube, on));
        _card.Add(new SettingsRow(null, Loc.T("Show YouTube Music in the island"), null, _youTubeSwitch));
        _card.AddCaption(Loc.T("Without setup, searches open YouTube Music in your browser. Add your own YouTube Data API key to see results in the island."));

        _apiKey = MakePasswordBox(280);
        AutomationProperties.SetAutomationId(_apiKey, "Island.Settings.YouTube.Key");
        AutomationProperties.SetName(_apiKey, Loc.T("API key"));
        _apiKey.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SaveKey(youTube);
                e.Handled = true;
            }
        };
        _save = UiKit.Button(Loc.T("Save"), ButtonKind.Prominent, 12);
        _save.Margin = new Thickness(8, 0, 0, 0);
        _save.Click += (_, _) => SaveKey(youTube);
        AutomationProperties.SetAutomationId(_save, "Island.Settings.YouTube.Save");
        _remove = UiKit.Button(Loc.T("Remove"), ButtonKind.Bordered, 12);
        _remove.Margin = new Thickness(6, 0, 0, 0);
        _remove.Click += (_, _) =>
        {
            youTube.RemoveKey();
            _apiKey.Clear();
            SetKeyMessage(null);
            Refresh();
        };
        AutomationProperties.SetAutomationId(_remove, "Island.Settings.YouTube.Remove");
        _card.Add(Field(Loc.T("API key"), _apiKey, 300, _save, _remove));
        _keyStatus = UiKit.Text("", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, true);
        _card.Add(_keyStatus);

        _card.AddCaption(Loc.T("In Google Cloud Console create a project, enable YouTube Data API v3, then open Credentials › Create credentials › API key. The free daily limit allows about 100 searches."));
        _card.Add(LinkButton(Loc.T("Open Google Cloud Console"), CloudConsoleUrl));

        var openIn = new List<(string Value, string Label)> { ("music", "YouTube Music"), ("youtube", "YouTube") };
        var saved = string.Equals(App.Get<string>(K.YouTubeOpenIn), "youtube", StringComparison.OrdinalIgnoreCase) ? "youtube" : "music";
        _card.Add(new PickerRow<string>(null, Loc.T("Open songs in"), null, openIn, saved, v => App.Set(K.YouTubeOpenIn, v), 160));

        _card.AddCaption(Loc.T("Search results come from YouTube API Services. YouTube’s Terms of Service and Google’s Privacy Policy apply."));
        var legal = new WrapPanel();
        // Right and bottom gaps only, so a button that wraps to the next line stays aligned with the one above it.
        var terms = LinkButton(Loc.T("YouTube Terms"), YouTubeTermsUrl);
        terms.Margin = new Thickness(0, 0, 8, 6);
        legal.Children.Add(terms);
        var privacy = LinkButton(Loc.T("Google Privacy Policy"), GooglePrivacyUrl);
        privacy.Margin = new Thickness(0, 0, 8, 6);
        legal.Children.Add(privacy);
        _card.Add(legal);
    }

    private void SaveKey(YouTubeService youTube)
    {
        var key = _apiKey?.Password.Trim() ?? string.Empty;
        if (key.Length == 0 || _checking)
        {
            return;
        }

        _checking = true;
        SetKeyMessage(null);
        Refresh();
        Guarded.FireAndLog(
            async () =>
            {
                StreamStatus status;
                try
                {
                    status = await youTube.SaveKeyAsync(key, CancellationToken.None);
                }
                finally
                {
                    _checking = false;
                }

                if (status == StreamStatus.Ok)
                {
                    // the saved key is never put back in the box: it stays out of reach
                    _apiKey?.Clear();
                    SetKeyMessage(Loc.T("The key works."), TimeSpan.FromSeconds(4));
                }
                else if (status != StreamStatus.Cancelled)
                {
                    SetKeyMessage(KeyProblem(status));
                }

                Refresh();
            },
            _core!.Ctx.Log,
            "YouTube key check");
    }

    private static string KeyProblem(StreamStatus status) => status switch
    {
        StreamStatus.InvalidKey => Loc.T("This key isn’t valid."),
        StreamStatus.ApiDisabled => Loc.T("Turn on YouTube Data API v3 for this key’s project."),
        StreamStatus.QuotaExceeded => Loc.T("Today’s limit for this key is used up. It resets at midnight Pacific time."),
        StreamStatus.Offline => Loc.T("Couldn’t check the key. You’re offline."),
        _ => StreamStrings.Message(StreamStatus.Failed, StreamKind.YouTube),
    };

    /// <summary>Shows a message about the key until the next action (or, with <paramref name="forHowLong"/>, for that long).</summary>
    private void SetKeyMessage(string? message, TimeSpan? forHowLong = null)
    {
        _keyTimer?.Stop();
        _keyMessage = message;
        if (message is null || forHowLong is null)
        {
            return;
        }

        _keyTimer = new DispatcherTimer(DispatcherPriority.Normal, _card.Dispatcher) { Interval = forHowLong.Value };
        _keyTimer.Tick += (_, _) =>
        {
            _keyTimer?.Stop();
            _keyMessage = null;
            Refresh();
        };
        _keyTimer.Start();
    }

    private void RefreshYouTube(YouTubeService youTube)
    {
        if (_youTubeSwitch is not null && _youTubeSwitch.IsOn != App.Get<bool>(K.MusicYouTube))
        {
            _youTubeSwitch.IsOn = App.Get<bool>(K.MusicYouTube);
        }

        var hint = youTube.KeyHint;
        if (_keyStatus is not null)
        {
            string text;
            if (_checking)
            {
                text = Loc.T("Checking…");
            }
            else if (_keyMessage is not null)
            {
                text = _keyMessage;
            }
            else
            {
                var saved = hint is null ? string.Empty : Loc.F("Saved key ending in {0}", hint);
                var problem = hint is not null && youTube.KeyStatus is StreamStatus.InvalidKey or StreamStatus.ApiDisabled or StreamStatus.QuotaExceeded
                    ? KeyProblem(youTube.KeyStatus)
                    : string.Empty;
                text = (saved + " " + problem).Trim();
            }

            _keyStatus.Text = text;
            _keyStatus.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_save is not null)
        {
            _save.IsEnabled = !_checking;
        }

        if (_remove is not null)
        {
            _remove.IsEnabled = hint is not null && !_checking;
        }
    }

    // ---------------------------------------------------------------- shared bits

    private void Refresh()
    {
        if (_spotify is not null)
        {
            RefreshSpotify(_spotify);
        }

        if (_youTube is not null)
        {
            RefreshYouTube(_youTube);
        }
    }

    /// <summary>
    /// A label with its field underneath. The field takes the width the card has (up to <paramref name="maxWidth"/>) and the buttons sit
    /// to its right, so nothing is pushed past the card's edge in a narrow Settings window, as a field beside its label would be.
    /// </summary>
    private static FrameworkElement Field(string label, FrameworkElement field, double maxWidth, params FrameworkElement[] buttons)
    {
        var block = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        block.Children.Add(UiKit.Text(label, 13, FontWeights.Normal, ThemeKeys.TextPrimary, true));
        var line = new Grid { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = maxWidth + buttons.Length * 90 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = maxWidth });
        field.Width = double.NaN;
        field.MinWidth = 120;
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        line.Children.Add(field);
        for (var i = 0; i < buttons.Length; i++)
        {
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(buttons[i], i + 1);
            line.Children.Add(buttons[i]);
        }

        block.Children.Add(line);
        return block;
    }

    private static TextBox ReadOnlyBox(string text, double width)
    {
        var box = new TextBox { Text = text, Width = width, IsReadOnly = true };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        return box;
    }

    /// <summary>A "Copy" button that says "Copied" for two seconds; a clipboard another program holds open is not an error worth showing.</summary>
    private static Button CopyButton(Func<string> text)
    {
        var button = UiKit.Button(Loc.T("Copy"), ButtonKind.Bordered, 12);
        button.Margin = new Thickness(8, 0, 0, 0);
        DispatcherTimer? timer = null;
        button.Click += (_, _) =>
        {
            try
            {
                System.Windows.Clipboard.SetText(text());
            }
            catch (COMException)
            {
                return;
            }

            button.Content = Loc.T("Copied");
            timer?.Stop();
            timer = new DispatcherTimer(DispatcherPriority.Normal, button.Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer?.Stop();
                button.Content = Loc.T("Copy");
            };
            timer.Start();
        };
        return button;
    }

    private static Button LinkButton(string text, string url)
    {
        var button = UiKit.Button(text, ButtonKind.Bordered, 12, G.OpenApp, () => UiKit.OpenUrl(url));
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button;
    }

    /// <summary>A password box that looks like the settings text box (the shared style is for text boxes only).</summary>
    private static PasswordBox MakePasswordBox(double width)
    {
        var template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"PasswordBox\">"
            + "<Border CornerRadius=\"7\" Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"1\" Padding=\"{TemplateBinding Padding}\">"
            + "<ScrollViewer x:Name=\"PART_ContentHost\" Focusable=\"False\" HorizontalScrollBarVisibility=\"Hidden\" VerticalScrollBarVisibility=\"Hidden\"/></Border></ControlTemplate>");
        var box = new PasswordBox { Width = width, Template = template, Padding = new Thickness(8, 5, 8, 5), FontSize = 12, MaxLength = 256 };
        box.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        box.SetResourceReference(Control.BackgroundProperty, ThemeKeys.SettingsControl);
        box.SetResourceReference(Control.BorderBrushProperty, ThemeKeys.CardBorder);
        box.SetResourceReference(PasswordBox.CaretBrushProperty, ThemeKeys.TextPrimary);
        return box;
    }
}
