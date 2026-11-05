// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;
using Trayloom.Features.SystemTools.Wifi;
using Windows.Devices.Radios;

namespace Trayloom.Features.SystemTools.Bluetooth;

/// <summary>The "Bluetooth" page of the expanded island (spec 09 part 1), laid out like the Wi-Fi page.</summary>
public sealed class BluetoothIslandSection : IIslandSection
{
    public string? FeatureId => BtKeys.FeatureId;

    public int Order => 172;

    public string Id => "bluetooth";

    public string Title => BtStrings.Title;

    public string Glyph => "";

    public FrameworkElement CreateView(IIslandContext context)
    {
        if (context.Services.Get<BluetoothService>() is not { } service)
        {
            return WifiIslandSection.Message("", Loc.T("This page could not be shown."), null);
        }

        service.IslandContext = context;
        return new BluetoothPageView(service, context);
    }
}

/// <summary>The page itself, redrawn from <see cref="BtSnapshot"/>: the radio switch in the top row, then connected and paired devices.</summary>
internal sealed class BluetoothPageView : Grid, IIslandTopRowHost
{
    private static readonly TimeSpan NoticeLife = TimeSpan.FromSeconds(6);

    private readonly BluetoothService _service;
    private readonly IIslandContext _context;
    private readonly WifiSwitch _switch;
    private readonly Border _topRow = new();
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private ScrollViewer? _scroll;
    private bool _topRowClaimed;
    private bool _attached;
    private string? _notice;
    private DateTime _noticeUntil;

    public BluetoothPageView(BluetoothService service, IIslandContext context)
    {
        _service = service;
        _context = context;
        _switch = new WifiSwitch(RadioChange);
        WifiLook.Automate(_switch, "Bluetooth.Radio", BtStrings.Title);
        Background = Brushes.Transparent;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        _noticeTimer.Tick += (_, _) =>
        {
            if (_notice is not null && DateTime.UtcNow >= _noticeUntil)
            {
                _notice = null;
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
        _service.Attach();
        _noticeTimer.Start();
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
        _service.Detach();
        _noticeTimer.Stop();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Rebuild);

    private void Notify(string text)
    {
        _notice = text;
        _noticeUntil = DateTime.UtcNow + NoticeLife;
        Rebuild();
    }

    private void RadioChange(bool on) => BtGuard.FireAndLog(async () =>
    {
        if (!on && !_service.ConfirmRadioOff())
        {
            Rebuild();
            return;
        }

        var status = await _service.SetRadioAsync(on);
        if (status != RadioAccessStatus.Allowed)
        {
            Notify(BtStrings.RadioDenied);
        }
    }, _context.Log, "radio change");

    // ------------------------------------------------------------------------------------------ drawing

    private void Rebuild()
    {
        var snapshot = _service.Snapshot;
        var keepOffset = _scroll?.VerticalOffset ?? 0;
        Detach(_switch);
        Children.Clear();
        _scroll = null;

        _switch.IsEnabled = snapshot.Loaded && snapshot.Radio is (BtRadio.On or BtRadio.Off) && !snapshot.AccessDenied;
        _switch.IsOn = snapshot.Radio == BtRadio.On;

        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        if (_topRowClaimed)
        {
            _topRow.Child = _switch;
        }
        else
        {
            // No top row to claim (the Settings preview): the switch sits in the page.
            var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(WifiLook.Text(BtStrings.Title, 13, FontWeights.SemiBold));
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
        else if (snapshot.Radio == BtRadio.None)
        {
            body.Children.Add(Centered(WifiIslandSection.Message("", BtStrings.NoAdapter, BtStrings.NoAdapterHint)));
        }
        else if (snapshot.Radio == BtRadio.Disabled)
        {
            body.Children.Add(Centered(WifiIslandSection.Message("", BtStrings.HardwareOff, null)));
        }
        else if (snapshot.Radio == BtRadio.Off)
        {
            body.Children.Add(Centered(WithPill(WifiIslandSection.Message("", BtStrings.BluetoothOff, null), WifiLook.Pill(BtStrings.TurnOn, "Bluetooth.TurnOn", () => RadioChange(true)))));
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

        var text = WifiLook.Text(_notice, 11, null, WifiLook.Of(WifiLook.Orange));
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(2, 8, 2, 0);
        body.Children.Add(text);
    }

    private void BuildOn(StackPanel body, BtSnapshot snapshot)
    {
        if (snapshot.AccessDenied)
        {
            var denied = WifiLook.Text(BtStrings.AccessDenied, 11, null, WifiLook.Of(WifiLook.Orange));
            denied.TextWrapping = TextWrapping.Wrap;
            denied.Margin = new Thickness(2, 0, 2, 4);
            body.Children.Add(denied);
        }

        AddNotice(body);

        var visible = _service.Visible();
        var connected = visible.Where(d => d.Connected).ToList();
        var paired = visible.Where(d => !d.Connected).ToList();
        var threshold = BtKeys.Threshold(_context.App);
        if (visible.Count == 0)
        {
            var empty = WifiLook.Text(BtStrings.NoDevices, 11.5, null, WifiLook.White(0.6));
            empty.Margin = new Thickness(2, 8, 0, 8);
            body.Children.Add(empty);
        }

        if (connected.Count > 0)
        {
            body.Children.Add(Header(BtStrings.Connected));
            body.Children.Add(List(connected.Select(d => Row(d, threshold))));
        }

        if (paired.Count > 0)
        {
            body.Children.Add(Header(BtStrings.PairedDevices));
            body.Children.Add(List(paired.Select(d => Row(d, threshold))));
        }

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(WifiLook.Pill(BtStrings.AddDevice, "Bluetooth.Add", BtKeys.OpenAddDevice));
        var settings = WifiLook.Pill(BtStrings.BluetoothSettings, "Bluetooth.Settings", BtKeys.OpenSettings);
        settings.Margin = new Thickness(6, 0, 0, 0);
        footer.Children.Add(settings);
        body.Children.Add(footer);

        var note = WifiLook.Text(BtStrings.Footnote, 10.5, null, WifiLook.White(0.45));
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(2, 8, 2, 0);
        body.Children.Add(note);
    }

    private static FrameworkElement Header(string text)
    {
        var header = WifiLook.Text(text, 11, FontWeights.SemiBold, WifiLook.White(0.6));
        header.Margin = new Thickness(0, 10, 0, 2);
        return header;
    }

    private static FrameworkElement List(IEnumerable<FrameworkElement> rows)
    {
        var list = new StackPanel();
        foreach (var row in rows)
        {
            list.Children.Add(row);
        }

        return list;
    }

    private static FrameworkElement Row(BtDevice device, int threshold)
    {
        var grid = new Grid { Height = 34, Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = WifiLook.Icon(BtLook.KindGlyph(device.Kind), 14, WifiLook.White(device.Connected ? 0.9 : 0.55));
        icon.Margin = new Thickness(8, 0, 0, 0);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(icon);

        var name = WifiLook.Text(device.Name, 12.5, null, WifiLook.White(device.Connected ? 0.95 : 0.7), TextTrimming.CharacterEllipsis);
        name.Margin = new Thickness(4, 0, 8, 0);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var trailing = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        if (device.Connected && device.Battery is { } percent)
        {
            trailing.Children.Add(new BatteryRing(percent, threshold) { VerticalAlignment = VerticalAlignment.Center });
            var level = WifiLook.Text(BtStrings.Percent(percent), 11.5, FontWeights.Medium, WifiLook.White(0.9));
            level.Margin = new Thickness(5, 0, 0, 0);
            Typography.SetNumeralAlignment(level, FontNumeralAlignment.Tabular);
            trailing.Children.Add(level);
        }
        else if (device.Connected)
        {
            trailing.Children.Add(WifiLook.Text(BtStrings.Connected, 11, null, WifiLook.White(0.6)));
        }
        else
        {
            trailing.Children.Add(WifiLook.Text(BtStrings.NotConnected, 11, null, WifiLook.White(0.6)));
            var connect = WifiLook.Pill(BtStrings.ConnectEllipsis, "Bluetooth.Connect", BtKeys.OpenConnect);
            connect.Margin = new Thickness(8, 0, 0, 0);
            trailing.Children.Add(connect);
        }

        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);
        AutomationProperties.SetAutomationId(grid, "Bluetooth.Row");
        AutomationProperties.SetName(grid, device.Name);
        return grid;
    }
}

/// <summary>The closed-island notice of a connect or a low battery: kind glyph, name, battery ring and percent (or "Connected").</summary>
public sealed class BluetoothNoticeStrip : IIslandStrip
{
    public string? FeatureId => BtKeys.FeatureId;

    public int Order => 43;

    public string Id => "bluetooth.notice";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<BluetoothService>() is not { } service)
        {
            return null;
        }

        service.IslandContext = context;
        if (service.TakeNotice() is not { } notice)
        {
            return null;
        }

        var threshold = BtKeys.Threshold(context.App);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var glyph = WifiLook.Icon(BtLook.KindGlyph(notice.Kind), 12, notice.Low ? WifiLook.Of(WifiLook.Orange) : WifiLook.White(0.9));
        glyph.Margin = new Thickness(2, 0, 6, 0);
        panel.Children.Add(glyph);
        var name = WifiLook.Text(notice.Name, 11, FontWeights.Medium, WifiLook.White(1), TextTrimming.CharacterEllipsis);
        name.MaxWidth = 150;
        panel.Children.Add(name);
        if (notice.Battery is { } percent)
        {
            var ring = new BatteryRing(percent, threshold, 11, 2) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            panel.Children.Add(ring);
            var level = WifiLook.Text(BtStrings.Percent(percent), 11, FontWeights.Medium, WifiLook.White(1));
            level.Margin = new Thickness(4, 0, 0, 0);
            Typography.SetNumeralAlignment(level, FontNumeralAlignment.Tabular);
            panel.Children.Add(level);
        }
        else
        {
            var connected = WifiLook.Text(BtStrings.Connected, 11, FontWeights.Medium, WifiLook.White(0.7));
            connected.Margin = new Thickness(8, 0, 0, 0);
            panel.Children.Add(connected);
        }

        return panel;
    }
}

/// <summary>A live activity that stays in the closed island while a connected device is at or under the warning level.</summary>
public sealed class BluetoothLowStrip : IIslandStrip
{
    public string? FeatureId => BtKeys.FeatureId;

    public int Order => 46;

    public string Id => "bluetooth.low";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<BluetoothService>() is not { } service)
        {
            return null;
        }

        service.IslandContext = context;
        return service.LowDevices().Count == 0 ? null : new BluetoothLowView(service, context);
    }
}

/// <summary>
/// The content of <see cref="BluetoothLowStrip"/>. The island keeps the element while the strip exists, so it follows the service itself:
/// the service only asks the island for a redraw when the set of low devices changes.
/// </summary>
internal sealed class BluetoothLowView : StackPanel
{
    private readonly BluetoothService _service;
    private readonly IIslandContext _context;
    private bool _attached;

    public BluetoothLowView(BluetoothService service, IIslandContext context)
    {
        _service = service;
        _context = context;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Loaded += (_, _) =>
        {
            if (!_attached)
            {
                _attached = true;
                _service.Changed += Rebuild;
            }

            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            if (_attached)
            {
                _attached = false;
                _service.Changed -= Rebuild;
            }
        };
        Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        ToolTip = null;
        if (_service.LowDevices().FirstOrDefault() is not { Battery: { } percent } device)
        {
            return;
        }

        var threshold = BtKeys.Threshold(_context.App);
        var glyph = WifiLook.Icon(BtLook.KindGlyph(device.Kind), 12, WifiLook.Of(BtLook.LevelTint(percent, threshold)));
        glyph.Margin = new Thickness(2, 0, 6, 0);
        Children.Add(glyph);
        Children.Add(new BatteryRing(percent, threshold, 11, 2) { VerticalAlignment = VerticalAlignment.Center });
        var level = WifiLook.Text(BtStrings.Percent(percent), 11, FontWeights.Medium, WifiLook.White(1));
        level.Margin = new Thickness(4, 0, 0, 0);
        Typography.SetNumeralAlignment(level, FontNumeralAlignment.Tabular);
        Children.Add(level);
        var name = WifiLook.Text(device.Name, 11, FontWeights.Medium, WifiLook.White(0.7), TextTrimming.CharacterEllipsis);
        name.MaxWidth = 90;
        name.Margin = new Thickness(6, 0, 0, 0);
        Children.Add(name);
        ToolTip = BtStrings.LowTooltip(device.Name, percent);
    }
}
