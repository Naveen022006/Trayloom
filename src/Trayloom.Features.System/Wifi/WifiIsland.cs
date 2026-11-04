// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;
using Windows.Devices.Radios;

namespace Trayloom.Features.SystemTools.Wifi;

/// <summary>The "Wi-Fi" page of the expanded island (spec 06 section 3.5).</summary>
public sealed class WifiIslandSection : IIslandSection
{
    public string? FeatureId => WifiKeys.FeatureId;

    public int Order => 170;

    public string Id => "wifi";

    public string Title => WifiStrings.Title;

    public string Glyph => "";

    public FrameworkElement CreateView(IIslandContext context)
    {
        if (context.Services.Get<WifiService>() is not { } service)
        {
            return Message("", Loc.T("This page could not be shown."), null);
        }

        return new WifiPageView(service, context);
    }

    internal static FrameworkElement Message(string glyph, string text, string? caption)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380 };
        var icon = WifiLook.Icon(glyph, 22, WifiLook.White(0.5));
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var title = WifiLook.Text(text, 13, null, WifiLook.White(0.9));
        title.TextWrapping = TextWrapping.Wrap;
        title.TextAlignment = TextAlignment.Center;
        title.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(title);
        if (!string.IsNullOrEmpty(caption))
        {
            var sub = WifiLook.Text(caption, 11, null, WifiLook.White(0.6));
            sub.TextWrapping = TextWrapping.Wrap;
            sub.TextAlignment = TextAlignment.Center;
            sub.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(sub);
        }

        return stack;
    }
}

/// <summary>The page itself: the connection card, the network list and the footer, redrawn from <see cref="WifiSnapshot"/>.</summary>
internal sealed class WifiPageView : Grid, IIslandTopRowHost
{
    private static readonly TimeSpan NoticeLife = TimeSpan.FromSeconds(6);

    private readonly WifiService _service;
    private readonly IIslandContext _context;
    private readonly WifiSwitch _switch;
    private readonly Border _topRow = new();
    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private ScrollViewer? _scroll;
    private WifiPasswordWindow? _passwordWindow;
    private bool _topRowClaimed;
    private bool _attached;
    private string? _notice;
    private bool _noticeOrange;
    private DateTime _noticeUntil;
    private string? _failSsid;
    private string? _failText;
    private DateTime _failUntil;

    public WifiPageView(WifiService service, IIslandContext context)
    {
        _service = service;
        _context = context;
        _switch = new WifiSwitch(OnSwitch);
        Background = Brushes.Transparent;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        _scanTimer.Tick += (_, _) => WifiGuard.FireAndLog(() => _service.ScanAsync(false), _context.Log, "timed scan");
        _noticeTimer.Tick += (_, _) =>
        {
            if ((_notice is not null && DateTime.UtcNow >= _noticeUntil) || (_failText is not null && DateTime.UtcNow >= _failUntil))
            {
                if (_notice is not null && DateTime.UtcNow >= _noticeUntil)
                {
                    _notice = null;
                }

                if (_failText is not null && DateTime.UtcNow >= _failUntil)
                {
                    _failText = null;
                    _failSsid = null;
                }

                Rebuild();
            }
        };
        Rebuild();
    }

    /// <summary>The radio switch lives in the island's top row, beside the back button.</summary>
    public FrameworkElement ClaimTopRow()
    {
        if (!_topRowClaimed)
        {
            _topRowClaimed = true;
            Detach(_switch);
            _topRow.Child = _switch;
            _topRow.HorizontalAlignment = HorizontalAlignment.Left;
            Rebuild();
        }

        return _topRow;
    }

    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Border border:
                border.Child = null;
                break;
        }
    }

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _service.Changed += OnChanged;
        _scanTimer.Start();
        _noticeTimer.Start();
        WifiGuard.FireAndLog(() => _service.LoadAsync(true), _context.Log, "page load");
        Rebuild();
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _service.Changed -= OnChanged;
        _scanTimer.Stop();
        _noticeTimer.Stop();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Rebuild);

    private void Notify(string text, bool orange = false)
    {
        _notice = text;
        _noticeOrange = orange;
        _noticeUntil = DateTime.UtcNow + NoticeLife;
        Rebuild();
    }

    private void Fail(string ssid, string text)
    {
        _failSsid = ssid;
        _failText = text;
        _failUntil = DateTime.UtcNow + NoticeLife;
        Rebuild();
    }

    // ------------------------------------------------------------------------------------------ actions

    private void OnSwitch(bool on) => RadioChange(on);

    private void RadioChange(bool on)
    {
        WifiGuard.FireAndLog(async () =>
        {
            var status = await _service.SetRadioAsync(on);
            if (status != RadioAccessStatus.Allowed)
            {
                Notify(WifiStrings.RadioDenied, true);
            }
        }, _context.Log, "radio change");
    }

    private void Disconnect()
    {
        WifiGuard.FireAndLog(async () =>
        {
            await _service.DisconnectAsync();
            Notify(WifiStrings.DisconnectedNote);
        }, _context.Log, "disconnect");
    }

    private void Scan() => WifiGuard.FireAndLog(() => _service.ScanAsync(false), _context.Log, "scan");

    private void Join(WifiNetwork network)
    {
        if (_service.Snapshot.ConnectingSsid is not null)
        {
            return;
        }

        switch (network.Security)
        {
            case WifiSecurity.Enterprise:
                WifiKeys.OpenUri("ms-availablenetworks:");
                Notify(WifiStrings.JoinInWindows);
                return;
            case WifiSecurity.Password when !network.Saved:
                AskPassword(network);
                return;
        }

        WifiGuard.FireAndLog(async () =>
        {
            var outcome = await _service.ConnectAsync(network, null, true);
            if (!IsLoaded)
            {
                return;
            }

            switch (outcome)
            {
                case WifiConnectOutcome.Success:
                    if (network.Security == WifiSecurity.Open && !network.Saved)
                    {
                        Notify(WifiStrings.OpenNetworkNote);
                    }

                    break;
                case WifiConnectOutcome.InvalidCredential when network.Security == WifiSecurity.Password:
                    AskPassword(network);
                    break;
                case WifiConnectOutcome.AccessRevoked:
                    await _service.RefreshAsync();
                    break;
                case WifiConnectOutcome.Unsupported:
                    WifiKeys.OpenUri("ms-availablenetworks:");
                    Fail(network.Ssid, WifiRules.FailureText(outcome));
                    break;
                default:
                    Fail(network.Ssid, WifiRules.FailureText(outcome));
                    break;
            }
        }, _context.Log, "connect");
    }

    private void AskPassword(WifiNetwork network)
    {
        if (_passwordWindow is { IsLoaded: true })
        {
            _passwordWindow.Activate();
            return;
        }

        var wep = network.Native is Windows.Devices.WiFi.WiFiAvailableNetwork n && WifiRules.IsWep(n.SecuritySettings.NetworkAuthenticationType);
        var window = new WifiPasswordWindow(network, wep, (password, auto) => _service.ConnectAsync(network, password, auto), _context.Log);
        window.Closed += (_, _) => _passwordWindow = null;
        _passwordWindow = window;
        window.Show();
    }

    // ------------------------------------------------------------------------------------------ drawing

    private void Rebuild()
    {
        var snapshot = _service.Snapshot;
        var keepOffset = _scroll?.VerticalOffset ?? 0;
        Detach(_switch);
        Children.Clear();
        _scroll = null;

        _switch.IsEnabled = snapshot.Loaded && snapshot.HasAdapter && snapshot.Radio is WifiRadioState.On or WifiRadioState.Off;
        _switch.IsOn = snapshot.Radio == WifiRadioState.On;

        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        if (_topRowClaimed)
        {
            _topRow.Child = _switch;
        }
        else
        {
            // No top row to claim (the Settings preview): the switch sits in the page.
            var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(WifiLook.Text(WifiStrings.Title, 13, FontWeights.SemiBold));
            _switch.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(_switch);
            body.Children.Add(head);
        }

        if (!snapshot.Loaded)
        {
            var wait = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
            var spinner = WifiLook.Spinner(16);
            spinner.HorizontalAlignment = HorizontalAlignment.Center;
            wait.Children.Add(spinner);
            body.Children.Add(wait);
        }
        else if (!snapshot.HasAdapter)
        {
            body.Children.Add(Centered(WifiIslandSection.Message("", WifiStrings.NoAdapter, WifiStrings.NoAdapterHint)));
        }
        else if (snapshot.Radio == WifiRadioState.Disabled)
        {
            body.Children.Add(Centered(WifiIslandSection.Message("", WifiStrings.HardwareOff, null)));
        }
        else if (snapshot.Radio == WifiRadioState.Off)
        {
            body.Children.Add(Centered(WithPill(WifiIslandSection.Message("", WifiStrings.WifiOff, null), WifiLook.Pill(WifiStrings.TurnOn, "Wifi.TurnOn", () => RadioChange(true)))));
            AddNotice(body);
        }
        else
        {
            BuildOn(body, snapshot);
        }

        var scroll = new SmoothScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = body };
        _scroll = scroll;
        if (keepOffset > 0)
        {
            Dispatcher.BeginInvoke(() => scroll.ScrollToVerticalOffset(keepOffset), DispatcherPriority.Loaded);
        }

        Children.Add(scroll);
    }

    private static FrameworkElement Centered(FrameworkElement content)
    {
        content.Margin = new Thickness(0, 24, 0, 8);
        return content;
    }

    private static FrameworkElement WithPill(FrameworkElement message, Button pill)
    {
        var stack = new StackPanel();
        stack.Children.Add(message);
        pill.HorizontalAlignment = HorizontalAlignment.Center;
        pill.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(pill);
        return stack;
    }

    private void AddNotice(StackPanel body)
    {
        if (_notice is null)
        {
            return;
        }

        var text = WifiLook.Text(_notice, 11, null, _noticeOrange ? WifiLook.Of(WifiLook.Orange) : WifiLook.White(0.6));
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(2, 8, 2, 0);
        body.Children.Add(text);
    }

    private void BuildOn(StackPanel body, WifiSnapshot snapshot)
    {
        var showCard = snapshot.Connected || snapshot.ConnectingSsid is not null || !snapshot.LocationNeeded;
        if (showCard)
        {
            body.Children.Add(CurrentCard(snapshot));
        }

        AddNotice(body);

        if (snapshot.LocationNeeded && snapshot.Networks.Count == 0)
        {
            var message = WifiIslandSection.Message("", WifiStrings.LocationNeeded, WifiLocation.Caption());
            var pill = WifiLook.Pill(WifiStrings.OpenLocationSettings, "Wifi.LocationSettings", () => WifiKeys.OpenUri("ms-settings:privacy-location"));
            var block = WithPill(message, pill);
            block.Margin = new Thickness(0, 14, 0, 0);
            body.Children.Add(block);
            return;
        }

        var header = new Grid { Margin = new Thickness(0, 10, 0, 0), Height = 20 };
        header.Children.Add(WifiLook.Text(WifiStrings.Networks, 11, FontWeights.SemiBold, WifiLook.White(0.6)));
        header.Children.Add(RefreshButton(snapshot.Scanning));
        body.Children.Add(header);

        var list = new WifiList { Margin = new Thickness(0, 2, 0, 0) };
        WifiLook.Automate(list, "Wifi.List", WifiStrings.Networks);
        if (snapshot.Networks.Count == 0)
        {
            var empty = WifiLook.Text(snapshot.Scanned && !snapshot.Scanning ? WifiStrings.NoNetworks : WifiStrings.Searching, 11.5, null, WifiLook.White(0.6));
            empty.Margin = new Thickness(2, 8, 0, 8);
            list.Children.Add(empty);
        }
        else
        {
            foreach (var network in snapshot.Networks)
            {
                list.Children.Add(Row(network, snapshot.ConnectingSsid is not null));
            }
        }

        body.Children.Add(list);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(WifiLook.Pill(WifiStrings.WifiSettings, "Wifi.Settings", () => WifiKeys.OpenUri("ms-settings:network-wifi")));
        var more = WifiLook.Pill(WifiStrings.MoreNetworks, "Wifi.More", () => WifiKeys.OpenUri("ms-availablenetworks:"));
        more.Margin = new Thickness(6, 0, 0, 0);
        footer.Children.Add(more);
        body.Children.Add(footer);
    }

    private FrameworkElement CurrentCard(WifiSnapshot snapshot)
    {
        var card = new Border { CornerRadius = new CornerRadius(14), Background = WifiLook.White(0.08), Padding = new Thickness(12), Height = 56 };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string title;
        string? caption;
        FrameworkElement glyph;
        if (snapshot.ConnectingSsid is { } connecting)
        {
            title = connecting;
            caption = WifiStrings.ConnectingTo(connecting);
            glyph = WifiLook.Spinner(16);
        }
        else if (snapshot.Connection is { } c)
        {
            title = c.Ssid;
            caption = WifiStrings.ConnectedLevel(WifiRules.LevelText(c.Level));
            glyph = WifiLook.Icon(WifiRules.SignalGlyph(c.Bars), 18, WifiLook.White(1));
        }
        else if (snapshot.ConnectedWithoutName)
        {
            title = WifiStrings.Connected;
            caption = null;
            glyph = WifiLook.Icon(WifiRules.SignalGlyph(snapshot.Connection?.Bars ?? 4), 18, WifiLook.White(1));
        }
        else
        {
            title = WifiStrings.NotConnected;
            caption = null;
            glyph = WifiLook.Icon(WifiRules.SignalGlyph(0), 18, WifiLook.White(0.5));
        }

        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(glyph);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(WifiLook.Text(title, 14, FontWeights.SemiBold, null, TextTrimming.CharacterEllipsis));
        if (caption is not null)
        {
            texts.Children.Add(WifiLook.Text(caption, 11, null, WifiLook.White(0.6), TextTrimming.CharacterEllipsis));
        }

        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        if (snapshot.Connected && snapshot.ConnectingSsid is null)
        {
            var pill = WifiLook.Pill(WifiStrings.Disconnect, "Wifi.Disconnect", Disconnect);
            pill.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);
        }

        card.Child = grid;
        return card;
    }

    private FrameworkElement RefreshButton(bool scanning)
    {
        var rotate = new RotateTransform();
        var icon = WifiLook.Icon("", 12, WifiLook.White(0.7));
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        icon.RenderTransform = rotate;
        if (scanning)
        {
            icon.Loaded += (_, _) => rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
            icon.Unloaded += (_, _) => rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        var button = new Button { Content = new Border { Child = icon, Padding = new Thickness(6, 2, 6, 2), Background = Brushes.Transparent }, Style = WifiLook.PlainStyle, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand, ToolTip = WifiStrings.SearchAgain, Focusable = false };
        button.Click += (_, _) => Scan();
        return WifiLook.Automate(button, "Wifi.Scan", WifiStrings.SearchAgain);
    }

    private FrameworkElement Row(WifiNetwork network, bool disabled)
    {
        var grid = new Grid { Height = 34, Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var signal = WifiLook.Icon(WifiRules.SignalGlyph(network.Bars), 14, WifiLook.White(0.9));
        signal.Margin = new Thickness(8, 0, 0, 0);
        signal.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(signal);
        var name = WifiLook.Text(network.Ssid, 12.5, null, WifiLook.White(0.95), TextTrimming.CharacterEllipsis);
        name.Margin = new Thickness(4, 0, 8, 0);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        if (network.Security != WifiSecurity.Open)
        {
            var padlock = WifiLook.Icon(WifiRules.LockGlyph, 11, WifiLook.White(0.6));
            padlock.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(padlock, 2);
            grid.Children.Add(padlock);
        }

        var failed = _failText is not null && string.Equals(_failSsid, network.Ssid, StringComparison.Ordinal);
        var connecting = string.Equals(_service.Snapshot.ConnectingSsid, network.Ssid, StringComparison.Ordinal);
        var caption = WifiLook.Text(failed ? _failText! : connecting ? WifiStrings.Connecting : WifiRules.Caption(network), 11, null, failed ? WifiLook.Of(WifiLook.Orange) : WifiLook.White(0.6));
        caption.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(caption, 3);
        grid.Children.Add(caption);

        var frame = new Border { Child = grid, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
        var button = new Button { Content = frame, Style = WifiLook.PlainStyle, HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = disabled ? Cursors.Arrow : Cursors.Hand, IsEnabled = !disabled || connecting, Focusable = false };
        button.MouseEnter += (_, _) => frame.Background = disabled ? Brushes.Transparent : WifiLook.White(0.06);
        button.MouseLeave += (_, _) => frame.Background = Brushes.Transparent;
        button.Opacity = disabled && !connecting ? 0.5 : 1;
        button.Click += (_, _) => Join(network);
        AutomationProperties.SetAutomationId(button, "Wifi.Row");
        AutomationProperties.SetName(button, network.Ssid);
        return button;
    }
}
