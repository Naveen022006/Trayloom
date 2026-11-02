// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>The volume mixer flyout: master volume plus one row per app that has an audio session on the default output.</summary>
internal sealed class MixerWindow : FlyoutWindow
{
    private sealed class AppRow
    {
        public required string Key { get; init; }

        public required string Name { get; init; }

        public ImageSource? Icon { get; init; }

        public List<AudioSessionControl> Sessions { get; } = [];

        public Slider Slider { get; set; } = null!;

        public TextBlock Percent { get; set; } = null!;

        public ToggleButton Mute { get; set; } = null!;

        public ProgressBar Peak { get; set; } = null!;

        public bool Pinned { get; set; }
    }

    private sealed class SessionEvents(Action changed) : IAudioSessionEventsHandler
    {
        public void OnVolumeChanged(float volume, bool isMuted)
        {
        }

        public void OnDisplayNameChanged(string displayName)
        {
        }

        public void OnIconPathChanged(string iconPath)
        {
        }

        public void OnChannelVolumeChanged(uint channelCount, nint newVolumes, uint channelIndex)
        {
        }

        public void OnGroupingParamChanged(ref Guid groupingId)
        {
        }

        public void OnStateChanged(AudioSessionState state) => changed();

        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => changed();
    }

    private const string MuteGlyph = "";
    private const string VolumeGlyph = "";

    private readonly IFeatureContext _context;
    private readonly StackPanel _masterHost = new();
    private readonly StackPanel _rowsHost = new();
    private readonly List<AppRow> _rows = [];
    private readonly DispatcherTimer _timer;
    private MMDevice? _device;
    private string? _deviceId;
    private Slider? _masterSlider;
    private ToggleButton? _masterMute;
    private TextBlock? _masterPercent;
    private ProgressBar? _masterPeak;
    private string? _signature;
    private int _tick;
    private bool _updating;
    private bool _syncQueued;
    private SessionEvents? _events;

    public MixerWindow(IFeatureContext context)
        : base("Volume mixer", "", 420)
    {
        _context = context;
        MinHeight = 160;
        Body.Children.Add(_masterHost);
        Body.Children.Add(_rowsHost);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => OnTick();
    }

    protected override void OnShown()
    {
        _signature = null;
        _events = new SessionEvents(QueueSync);
        Rebuild(full: true);
        _timer.Start();
    }

    protected override void OnHidden()
    {
        _timer.Stop();
        Teardown();
    }

    private void Teardown()
    {
        foreach (var row in _rows)
        {
            DisposeSessions(row);
        }

        _rows.Clear();
        _rowsHost.Children.Clear();
        _masterHost.Children.Clear();
        if (_device is not null)
        {
            try
            {
                _device.AudioSessionManager.OnSessionCreated -= OnSessionCreated;
            }
            catch (Exception)
            {
            }

            _device.Dispose();
            _device = null;
        }

        _masterSlider = null;
        _deviceId = null;
    }

    private static void DisposeSessions(AppRow row)
    {
        foreach (var session in row.Sessions)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception)
            {
            }
        }

        row.Sessions.Clear();
    }

    private void OnSessionCreated(object sender, object newSession) => QueueSync();

    private void QueueSync()
    {
        // Raised on COM threads: coalesce and handle on the UI thread.
        if (_syncQueued)
        {
            return;
        }

        _syncQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _syncQueued = false;
            if (IsVisible)
            {
                Rebuild(full: false);
            }
        });
    }

    private void OnTick()
    {
        _tick++;
        if (_tick % 16 == 0)
        {
            Rebuild(full: false);
        }

        try
        {
            _updating = true;
            if (_device is not null && _masterSlider is not null)
            {
                var volume = _device.AudioEndpointVolume;
                if (!_masterSlider.IsMouseCaptureWithin)
                {
                    _masterSlider.Value = Math.Round(volume.MasterVolumeLevelScalar * 100);
                }

                _masterPercent!.Text = $"{(int)_masterSlider.Value}%";
                _masterMute!.IsChecked = volume.Mute;
                _masterMute.Content = volume.Mute ? MuteGlyph : VolumeGlyph;
                _masterPeak!.Value = _device.AudioMeterInformation.MasterPeakValue;
            }

            foreach (var row in _rows)
            {
                var peak = 0f;
                var volume = 0f;
                var muted = row.Sessions.Count > 0;
                foreach (var session in row.Sessions)
                {
                    try
                    {
                        peak = Math.Max(peak, session.AudioMeterInformation.MasterPeakValue);
                        volume = Math.Max(volume, session.SimpleAudioVolume.Volume);
                        muted &= session.SimpleAudioVolume.Mute;
                    }
                    catch (Exception)
                    {
                    }
                }

                row.Peak.Value = peak;
                if (!row.Slider.IsMouseCaptureWithin)
                {
                    row.Slider.Value = Math.Round(volume * 100);
                }

                row.Percent.Text = $"{(int)row.Slider.Value}%";
                row.Mute.IsChecked = muted;
                row.Mute.Content = muted ? MuteGlyph : VolumeGlyph;
            }
        }
        catch (Exception)
        {
            // The device may have disappeared; the next sync rebuilds.
        }
        finally
        {
            _updating = false;
        }
    }

    private void Rebuild(bool full)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var hasDevice = enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var currentId = hasDevice ? AudioDevices.DefaultId(DataFlow.Render) : null;
            if (full || currentId != _deviceId)
            {
                Teardown();
                if (!hasDevice)
                {
                    ShowMessage("No audio output device found.");
                    return;
                }

                _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _deviceId = _device.ID;
                _device.AudioSessionManager.OnSessionCreated += OnSessionCreated;
                BuildMaster();
                _signature = null;
            }

            if (_device is null)
            {
                return;
            }

            BuildSessions();
        }
        catch (Exception ex)
        {
            _context.Log.Error("Volume mixer refresh failed", ex);
        }
    }

    private void ShowMessage(string text)
    {
        _rowsHost.Children.Clear();
        var block = new TextBlock { Text = text, Margin = new Thickness(6, 8, 6, 8) };
        block.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.MutedTextStyle);
        _rowsHost.Children.Add(block);
    }

    private void BuildMaster()
    {
        _masterHost.Children.Clear();
        var name = new TextBlock { Text = _device!.FriendlyName, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);
        _masterSlider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center };
        _masterSlider.ValueChanged += (_, e) =>
        {
            if (!_updating && _device is not null)
            {
                try
                {
                    _device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(e.NewValue / 100);
                }
                catch (Exception)
                {
                }
            }
        };
        _masterPercent = new TextBlock { Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _masterPercent.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        _masterMute = MakeGlyphToggle(VolumeGlyph, "Mute output");
        _masterMute.Click += (_, _) =>
        {
            try
            {
                _device!.AudioEndpointVolume.Mute = _masterMute.IsChecked == true;
            }
            catch (Exception)
            {
            }
        };
        _masterPeak = MakePeakBar();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sliderBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        sliderBox.Children.Add(_masterSlider);
        sliderBox.Children.Add(_masterPeak);
        Grid.SetColumn(sliderBox, 1);
        Grid.SetColumn(_masterPercent, 2);
        grid.Children.Add(_masterMute);
        grid.Children.Add(sliderBox);
        grid.Children.Add(_masterPercent);

        var content = new StackPanel();
        content.Children.Add(name);
        content.Children.Add(grid);
        var card = new Border { Margin = new Thickness(0, 0, 0, 8), Child = content };
        card.SetResourceReference(Border.StyleProperty, ThemeKeys.CardStyle);
        _masterHost.Children.Add(card);
    }

    private void BuildSessions()
    {
        var showInactive = _context.Settings.Get("showInactive", false);
        var pinned = _context.Settings.Get("pinned", new List<string>());
        var order = _context.Settings.Get("order", new List<string>());

        var manager = _device!.AudioSessionManager;
        manager.RefreshSessions();
        var groups = new Dictionary<string, AppRow>();
        var collection = manager.Sessions;
        for (var i = 0; i < collection.Count; i++)
        {
            var session = collection[i];
            try
            {
                var (key, name, icon) = AppIdentity.Resolve(session.GetProcessID, session.IsSystemSoundsSession);
                var isPinned = pinned.Contains(key);
                if (session.State == AudioSessionState.AudioSessionStateExpired
                    || (session.State != AudioSessionState.AudioSessionStateActive && !showInactive && !isPinned))
                {
                    session.Dispose();
                    continue;
                }

                if (!groups.TryGetValue(key, out var row))
                {
                    row = new AppRow { Key = key, Name = name, Icon = icon, Pinned = isPinned };
                    groups[key] = row;
                }

                row.Sessions.Add(session);
            }
            catch (Exception)
            {
                session.Dispose();
            }
        }

        var sorted = groups.Values
            .OrderBy(r => r.Pinned ? 0 : 1)
            .ThenBy(r => r.Pinned ? pinned.IndexOf(r.Key) : (order.IndexOf(r.Key) is var o and >= 0 ? o : int.MaxValue))
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var signature = string.Join('|', sorted.Select(r => $"{r.Key}:{r.Sessions.Count}:{r.Pinned}"));
        if (signature == _signature)
        {
            foreach (var row in sorted)
            {
                DisposeSessions(row);
            }

            return;
        }

        _signature = signature;
        foreach (var row in _rows)
        {
            DisposeSessions(row);
        }

        _rows.Clear();
        _rowsHost.Children.Clear();
        if (sorted.Count == 0)
        {
            ShowMessage("No apps are playing sound right now.");
            return;
        }

        foreach (var row in sorted)
        {
            _rows.Add(row);
            _rowsHost.Children.Add(BuildRow(row, sorted));
            foreach (var session in row.Sessions)
            {
                try
                {
                    session.RegisterEventClient(_events);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private UIElement BuildRow(AppRow row, List<AppRow> ordered)
    {
        FrameworkElement icon;
        if (row.Icon is not null)
        {
            icon = new Image { Source = row.Icon, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            var glyph = new TextBlock { Text = "", FontSize = 20, Width = 24, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.IconTextStyle);
            icon = glyph;
        }

        icon.Margin = new Thickness(0, 0, 10, 0);

        var name = new TextBlock { Text = row.Name, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);

        row.Slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center };
        row.Slider.ValueChanged += (_, e) =>
        {
            if (_updating)
            {
                return;
            }

            foreach (var session in row.Sessions)
            {
                try
                {
                    session.SimpleAudioVolume.Volume = (float)(e.NewValue / 100);
                }
                catch (Exception)
                {
                }
            }
        };
        row.Percent = new TextBlock { Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        row.Percent.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        row.Peak = MakePeakBar();

        var sliderGrid = new Grid();
        sliderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sliderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sliderBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sliderBox.Children.Add(row.Slider);
        sliderBox.Children.Add(row.Peak);
        Grid.SetColumn(row.Percent, 1);
        sliderGrid.Children.Add(sliderBox);
        sliderGrid.Children.Add(row.Percent);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);
        text.Children.Add(sliderGrid);

        row.Mute = MakeGlyphToggle(VolumeGlyph, "Mute this app");
        row.Mute.Click += (_, _) =>
        {
            foreach (var session in row.Sessions)
            {
                try
                {
                    session.SimpleAudioVolume.Mute = row.Mute.IsChecked == true;
                }
                catch (Exception)
                {
                }
            }
        };

        var pin = MakeGlyphButton(row.Pinned ? "" : "", row.Pinned ? "Unpin" : "Pin to top");
        pin.Click += (_, _) => TogglePin(row.Key);
        var up = MakeGlyphButton("", "Move up");
        up.Click += (_, _) => Move(row.Key, -1, ordered);
        var down = MakeGlyphButton("", "Move down");
        down.Click += (_, _) => Move(row.Key, 1, ordered);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(row.Mute);
        buttons.Children.Add(pin);
        buttons.Children.Add(up);
        buttons.Children.Add(down);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(buttons, 2);
        text.Margin = new Thickness(0, 0, 6, 0);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(buttons);

        var card = new Border { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(12, 8, 8, 8), Child = grid };
        card.SetResourceReference(Border.StyleProperty, ThemeKeys.CardStyle);
        card.Padding = new Thickness(12, 8, 8, 8);
        return card;
    }

    private void TogglePin(string key)
    {
        var pinned = _context.Settings.Get("pinned", new List<string>());
        if (!pinned.Remove(key))
        {
            pinned.Add(key);
        }

        _context.Settings.Set("pinned", pinned);
        _signature = null;
        Rebuild(full: false);
    }

    private void Move(string key, int delta, List<AppRow> ordered)
    {
        var self = ordered.FirstOrDefault(r => r.Key == key);
        if (self is null)
        {
            return;
        }

        var group = ordered.Where(r => r.Pinned == self.Pinned).Select(r => r.Key).ToList();
        var index = group.IndexOf(key);
        var target = index + delta;
        if (target < 0 || target >= group.Count)
        {
            return;
        }

        (group[index], group[target]) = (group[target], group[index]);
        if (self.Pinned)
        {
            var pinned = _context.Settings.Get("pinned", new List<string>());
            _context.Settings.Set("pinned", group.Concat(pinned.Where(p => !group.Contains(p))).ToList());
        }
        else
        {
            var order = _context.Settings.Get("order", new List<string>());
            _context.Settings.Set("order", group.Concat(order.Where(o => !group.Contains(o))).ToList());
        }

        _signature = null;
        Rebuild(full: false);
    }

    private static ProgressBar MakePeakBar() =>
        new() { Minimum = 0, Maximum = 1, Height = 3, Margin = new Thickness(0, 2, 0, 0), IsHitTestVisible = false };

    private static ToggleButton MakeGlyphToggle(string glyph, string tip)
    {
        var button = new ToggleButton { Content = glyph, ToolTip = tip, Width = 30, Height = 30, Padding = new Thickness(0), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        return button;
    }

    private static Button MakeGlyphButton(string glyph, string tip) =>
        new() { Content = glyph, ToolTip = tip, Width = 26, Height = 30, Padding = new Thickness(0), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
}
