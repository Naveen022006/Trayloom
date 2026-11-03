// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools;

/// <summary>
/// The volume mixer widget (spec 01 5.3 / 02a 3.2): audio devices block, one row per audio app, and the Options disclosure.
/// The same view is used in the tray panel (with the output switcher and audio priority inside Options) and on the General settings page.
/// </summary>
internal sealed class MixerView
{
    private sealed class RowState
    {
        public required MixerApp App { get; set; }

        public Slider Slider { get; set; } = null!;

        public EditablePercent Percent { get; set; } = null!;

        public Button Reset { get; set; } = null!;

        public Button Mute { get; set; } = null!;

        public Ellipse Dot { get; set; } = null!;

        public Border Root { get; set; } = null!;
    }

    private readonly IContributionServices _c;
    private readonly IAppSettings _app;
    private readonly bool _settingsMode;
    private readonly bool _showShared;
    private readonly StackPanel _root = new();
    private readonly StackPanel _devices = new();
    private readonly StackPanel _apps = new();
    private readonly List<RowState> _rows = [];
    private readonly DispatcherTimer _timer;
    private MixerSource? _source;
    private bool _needRefresh;
    private int _tick;
    private string _signature = string.Empty;
    private bool _updating;
    private string _deviceSignature = string.Empty;
    private ComboBox? _outCombo;
    private ComboBox? _commCombo;
    private ComboBox? _micCombo;
    private Slider? _outSlider;
    private EditablePercent? _outPercent;
    private Button? _outMute;
    private Slider? _micSlider;
    private EditablePercent? _micPercent;
    private Button? _micMute;
    private TextBlock? _error;
    private string? _outId;
    private string? _micId;
    private DeviceWatcher? _watcher;

    private MixerView(IContributionServices c, bool settingsMode)
    {
        _c = c;
        _app = c.App;
        _settingsMode = settingsMode;
        _showShared = !settingsMode;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>Creates the mixer card for the panel (<paramref name="settingsMode"/> false) or the settings page.</summary>
    public static FrameworkElement Create(IContributionServices c, bool settingsMode)
    {
        SystemDefaults.Register(c.App);
        var view = new MixerView(c, settingsMode);
        view.Build();
        FrameworkElement card;
        if (settingsMode)
        {
            var settingsCard = new SettingsCard(Strings.Mixer.Title);
            settingsCard.Add(view._root);
            card = settingsCard;
        }
        else
        {
            card = new PanelCard(view._root);
        }

        Bits.WhileVisible(card, view.SetVisible);
        return card;
    }

    /// <summary>The "Audio priority only" panel card (mixer not installed): the disclosure expanded, header hidden.</summary>
    public static FrameworkElement CreatePriorityOnly(IContributionServices c)
    {
        SystemDefaults.Register(c.App);
        var card = new PanelCard(AudioPriorityView.Build(c));
        return card;
    }

    // ------------------------------------------------------------------------------------------------ build

    private void Build()
    {
        _root.Children.Add(_devices);
        BuildDevices();
        var divider = UiKit.Divider();
        divider.Margin = new Thickness(0, 8, 0, 0);
        _root.Children.Add(divider);
        _apps.Margin = new Thickness(0, 8, 0, 0);
        _root.Children.Add(_apps);
        var optionsDivider = UiKit.Divider();
        optionsDivider.Margin = new Thickness(0, 8, 0, 8);
        _root.Children.Add(optionsDivider);
        _root.Children.Add(Bits.Disclosure(Strings.Mixer.Options, () => OptionsBody(), expanded: false, indent: 19));
    }

    private void SetVisible(bool visible)
    {
        if (visible)
        {
            _source ??= new MixerSource(_app, () => _c.Ui.BeginInvoke(() => _needRefresh = true));
            try
            {
                _watcher ??= new DeviceWatcher((_, _) => _c.Ui.BeginInvoke(() => _needRefresh = true));
            }
            catch (Exception)
            {
            }

            _needRefresh = true;
            RefreshAll();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _watcher?.Dispose();
            _watcher = null;
            _source?.Dispose();
            _source = null;
            _rows.Clear();
            _signature = string.Empty;
        }
    }

    private void OnTick()
    {
        _tick++;
        if (_needRefresh || _tick % 12 == 0)
        {
            _needRefresh = false;
            RefreshAll();
            return;
        }

        try
        {
            _source?.Poll();
            UpdateRows();
            UpdateVolumes();
        }
        catch (Exception)
        {
            _needRefresh = true;
        }
    }

    private void RefreshAll()
    {
        if (_source is null)
        {
            return;
        }

        try
        {
            RefreshDevices();
            _source.Refresh();
            BuildApps();
        }
        catch (Exception ex)
        {
            _c.Log.Error("Mixer refresh failed", ex);
        }
    }

    // ------------------------------------------------------------------------------------------------ devices block

    private static StackPanel LabelRow(string glyph, string label, UIElement picker, string? tooltip)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = UiKit.Icon(glyph, 10.5, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        icon.Width = 16;
        left.Children.Add(icon);
        left.Children.Add(UiKit.Text(label, 11.5, FontWeights.Medium, ThemeKeys.TextSecondary));
        grid.Children.Add(left);
        Grid.SetColumn(picker, 1);
        grid.Children.Add(picker);
        if (picker is FrameworkElement fe)
        {
            fe.ToolTip = tooltip;
        }

        return new StackPanel { Children = { grid } };
    }

    private (Slider, EditablePercent, Button, StackPanel) VolumeRow(string glyph, string muteGlyph, Action<float> write, Action toggleMute)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mute = Bits.FlatIcon(glyph, null, toggleMute, 10, 20, ThemeKeys.TextSecondary);
        row.Children.Add(mute);
        var slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        var percent = new EditablePercent(36, () => 100, v =>
        {
            slider.Value = v;
            write(v / 100f);
        });
        Grid.SetColumn(percent, 2);
        row.Children.Add(percent);
        slider.ValueChanged += (_, e) =>
        {
            percent.Text = (int)Math.Round(e.NewValue) + "%";
            if (!_updating)
            {
                write((float)(e.NewValue / 100));
            }
        };
        _ = muteGlyph;
        return (slider, percent, mute, new StackPanel { Children = { row } });
    }

    private void BuildDevices()
    {
        // Output
        _outCombo = new ComboBox { Width = 164, FontSize = 11, MinHeight = 22, Padding = new Thickness(6, 1, 4, 1) };
        _outCombo.SelectionChanged += OnOutputPicked;
        var outRow = LabelRow(Gl.Speaker, Strings.Mixer.Output, _outCombo, Strings.Mixer.ChooseOutput);
        _devices.Children.Add(outRow);
        var (s, p, m, vol) = VolumeRow(Gl.Speaker, Gl.SpeakerMute, v =>
        {
            if (_outId is not null)
            {
                AudioDevices.WriteVolume(_outId, v);
            }
        }, () => ToggleMute(_outId));
        _outSlider = s;
        _outPercent = p;
        _outMute = m;
        _devices.Children.Add(vol);

        // Communications ("System sounds")
        _commCombo = new ComboBox { Width = 164, FontSize = 11, MinHeight = 22, Padding = new Thickness(6, 1, 4, 1) };
        _commCombo.SelectionChanged += OnCommunicationsPicked;
        var commRow = LabelRow(Gl.Bell, Strings.Mixer.SystemSounds, _commCombo, Strings.Mixer.ChooseSystemSounds);
        commRow.Margin = new Thickness(0, 8, 0, 0);
        _devices.Children.Add(commRow);

        // Microphone
        _micCombo = new ComboBox { Width = 164, FontSize = 11, MinHeight = 22, Padding = new Thickness(6, 1, 4, 1) };
        _micCombo.SelectionChanged += OnMicPicked;
        var micRow = LabelRow(Gl.Mic, Strings.Mixer.Microphone, _micCombo, Strings.Mixer.ChooseMicrophone);
        micRow.Margin = new Thickness(0, 8, 0, 0);
        _devices.Children.Add(micRow);
        var (ms, mp, mm, mvol) = VolumeRow(Gl.Mic, Gl.MicOff, v =>
        {
            if (_micId is not null)
            {
                AudioDevices.WriteVolume(_micId, v);
            }
        }, () => ToggleMute(_micId));
        _micSlider = ms;
        _micPercent = mp;
        _micMute = mm;
        _devices.Children.Add(mvol);

        _error = UiKit.Text(string.Empty, 10, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        _error.Visibility = Visibility.Collapsed;
        _error.Margin = new Thickness(0, 6, 0, 0);
        _devices.Children.Add(_error);
    }

    private static void ToggleMute(string? id)
    {
        if (id is null)
        {
            return;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(id);
            device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
        }
        catch (Exception)
        {
        }
    }

    private List<AudioEndpoint> _outs = [];
    private List<AudioEndpoint> _comms = [];
    private List<AudioEndpoint> _ins = [];
    private bool _fillingCombos;

    private void RefreshDevices()
    {
        var outs = AudioDevices.List(DataFlow.Render);
        var ins = AudioDevices.List(DataFlow.Capture);
        var defOut = AudioDevices.DefaultId(DataFlow.Render, Role.Multimedia);
        var defComm = AudioDevices.DefaultId(DataFlow.Render, Role.Communications);
        var defIn = AudioDevices.DefaultId(DataFlow.Capture, Role.Console);
        var preferredMic = _app.Get<string>("mixerPreferredMicrophone", string.Empty);
        var sig = string.Join('|', outs.Select(o => o.Id)) + "#" + string.Join('|', ins.Select(o => o.Id)) + "#" + defOut + "#" + defComm + "#" + defIn + "#" + preferredMic;
        _outId = defOut;
        _micId = defIn;
        if (sig != _deviceSignature)
        {
            _deviceSignature = sig;
            _fillingCombos = true;
            try
            {
                _outs = outs;
                _comms = outs;
                _ins = ins;
                Fill(_outCombo!, outs, defOut, outs.Count == 0, Strings.Mixer.NoOutputs, true, wireless: true);
                Fill(_commCombo!, outs, defComm, false, Strings.Mixer.NoOutputs, true);
                FillMic(ins, defIn, preferredMic);
            }
            finally
            {
                _fillingCombos = false;
            }
        }

        UpdateVolumes();
    }

    private static void Fill(ComboBox combo, List<AudioEndpoint> devices, string? current, bool empty, string emptyText, bool showCurrentSuffix, bool wireless = false)
    {
        var items = new List<string>();
        foreach (var d in devices)
        {
            items.Add(d.Name + (showCurrentSuffix && d.Id == current ? Strings.Mixer.CurrentSuffix : string.Empty));
        }

        var index = devices.FindIndex(d => d.Id == current);
        if (devices.Count > 0 && index < 0)
        {
            items.Add(Strings.Mixer.OutputUnavailable);
            index = items.Count - 1;
        }

        if (devices.Count == 0)
        {
            items.Add(emptyText);
            index = 0;
        }

        if (wireless && devices.Count > 0)
        {
            items.Add(Strings.Mixer.ConnectWireless);
        }

        combo.ItemsSource = items;
        combo.SelectedIndex = index;
        combo.IsEnabled = !empty && devices.Count > 0;
    }

    private void FillMic(List<AudioEndpoint> ins, string? current, string preferred)
    {
        var items = new List<string> { Strings.Mixer.Default };
        items.AddRange(ins.Select(i => i.Name));
        var index = 0;
        if (!string.IsNullOrEmpty(preferred))
        {
            index = ins.FindIndex(i => i.Id == preferred) + 1;
            if (index == 0 && !_app.Get<bool>("audioPriorityInputEnabled"))
            {
                items.Add(Strings.Mixer.MicrophoneUnavailable);
                index = items.Count - 1;
            }
        }

        if (ins.Count == 0)
        {
            items = [Strings.Mixer.NoMicrophones];
            index = 0;
        }

        _ = current;
        _micCombo!.ItemsSource = items;
        _micCombo.SelectedIndex = index;
        _micCombo.IsEnabled = ins.Count > 0;
    }

    private void OnOutputPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingCombos || _outCombo!.SelectedIndex < 0)
        {
            return;
        }

        if (_outCombo.SelectedItem as string == Strings.Mixer.ConnectWireless)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:connecteddevices") { UseShellExecute = true });
            }
            catch (Exception)
            {
            }

            _deviceSignature = string.Empty;
            _needRefresh = true;
            return;
        }

        if (_outCombo.SelectedIndex >= _outs.Count)
        {
            return;
        }

        var device = _outs[_outCombo.SelectedIndex];
        if (device.Id == AudioDevices.DefaultId(DataFlow.Render, Role.Multimedia))
        {
            return;
        }

        if (!AudioDevices.SetDefault(device.Id))
        {
            ShowError(Strings.Mixer.SwitchFailed(device.Name));
        }
        else
        {
            ShowError(null);
        }

        _needRefresh = true;
    }

    private void OnCommunicationsPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingCombos || _commCombo!.SelectedIndex < 0 || _commCombo.SelectedIndex >= _comms.Count)
        {
            return;
        }

        var device = _comms[_commCombo.SelectedIndex];
        if (device.Id == AudioDevices.DefaultId(DataFlow.Render, Role.Communications))
        {
            return;
        }

        ShowError(AudioDevices.SetDefaultFor(device.Id, 2) ? null : Strings.Mixer.SwitchFailed(device.Name));
        _needRefresh = true;
    }

    private void OnMicPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingCombos || _micCombo!.SelectedIndex < 0)
        {
            return;
        }

        var index = _micCombo.SelectedIndex;
        if (index == 0)
        {
            _app.Set("mixerPreferredMicrophone", string.Empty);
            return;
        }

        if (index - 1 >= _ins.Count)
        {
            return;
        }

        var device = _ins[index - 1];
        _app.Set("mixerPreferredMicrophone", device.Id);
        ShowError(AudioDevices.SetDefault(device.Id) ? null : Strings.Mixer.SwitchFailed(device.Name));
        _needRefresh = true;
    }

    private void ShowError(string? text)
    {
        if (_error is null)
        {
            return;
        }

        _error.Text = text is null ? string.Empty : Gl.Warning + "  " + text;
        _error.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateVolumes()
    {
        _updating = true;
        try
        {
            Apply(_outId, _outSlider, _outPercent, _outMute, Gl.Speaker, Gl.SpeakerMute);
            Apply(_micId, _micSlider, _micPercent, _micMute, Gl.Mic, Gl.MicOff);
        }
        finally
        {
            _updating = false;
        }
    }

    private static void Apply(string? id, Slider? slider, EditablePercent? percent, Button? mute, string glyph, string muteGlyph)
    {
        if (slider is null || percent is null || mute is null)
        {
            return;
        }

        var reading = id is null ? null : AudioDevices.ReadVolume(id);
        var visible = reading is not null;
        slider.IsEnabled = visible;
        if (reading is not { } r)
        {
            return;
        }

        if (!slider.IsMouseCaptureWithin)
        {
            slider.Value = Math.Round(r.Volume * 100);
        }

        percent.Text = (int)Math.Round(slider.Value) + "%";
        var silent = r.Muted || r.Volume <= 0.001f;
        mute.Content = UiKit.Icon(silent ? muteGlyph : glyph, 10, silent ? ThemeKeys.Red : ThemeKeys.TextSecondary, FontWeights.SemiBold);
    }

    // ------------------------------------------------------------------------------------------------ app rows

    private void BuildApps()
    {
        var apps = _source!.Apps;
        var signature = string.Join('|', apps.Select(a => a.Key + (a.Pinned ? "!" : "") + ":" + a.RouteId)) + "#" + string.Join(',', _app.Get<List<string>>("mixerHiddenApps"));
        if (signature == _signature)
        {
            foreach (var row in _rows)
            {
                var fresh = apps.FirstOrDefault(a => a.Key == row.App.Key);
                if (fresh is not null)
                {
                    row.App = fresh;
                }
            }

            UpdateRows();
            return;
        }

        _signature = signature;
        _apps.Children.Clear();
        _rows.Clear();
        if (apps.Count == 0)
        {
            var empty = UiKit.Text(Strings.Mixer.Empty, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Margin = new Thickness(0, 4, 0, 4);
            _apps.Children.Add(empty);
            return;
        }

        foreach (var app in apps)
        {
            var state = new RowState { App = app };
            var ui = BuildRow(state);
            _rows.Add(state);
            ui.Margin = new Thickness(0, _apps.Children.Count == 0 ? 0 : 8, 0, 0);
            _apps.Children.Add(ui);
        }

        UpdateRows();
    }

    private FrameworkElement BuildRow(RowState state)
    {
        var app = state.App;
        var grid = new Grid { Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Icon with the "playing" dot.
        var iconHost = new Grid { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center, ToolTip = Strings.Mixer.DragTip, Background = Brushes.Transparent };
        if (app.Icon is not null)
        {
            iconHost.Children.Add(new Image { Source = app.Icon, Width = 32, Height = 32, SnapsToDevicePixels = true });
        }
        else
        {
            var tile = UiKit.IconTile(Gl.Speaker, 32, 15, 8);
            iconHost.Children.Add(tile);
        }

        var dot = new Ellipse { Width = 8, Height = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, StrokeThickness = 1.2, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, -2, -2) };
        dot.SetResourceReference(Shape.FillProperty, ThemeKeys.Green);
        dot.SetResourceReference(Shape.StrokeProperty, ThemeKeys.CardFill);
        iconHost.Children.Add(dot);
        state.Dot = dot;
        grid.Children.Add(iconHost);
        WireDrag(iconHost, state);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        // Line 1: name, pin, menu, output picker.
        var line1 = new Grid();
        line1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = UiKit.Text(app.Name, 11.5, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        name.MaxWidth = 108;
        nameRow.Children.Add(name);
        if (app.Pinned)
        {
            var pin = UiKit.Icon(Gl.Pin, 9, ThemeKeys.TextSecondary);
            pin.Margin = new Thickness(4, 0, 0, 0);
            nameRow.Children.Add(pin);
        }

        line1.Children.Add(nameRow);
        var menu = Bits.FlatIcon(Gl.More, Strings.Mixer.Actions, () => { }, 11, 20);
        menu.Click += (_, _) => ShowMenu(menu, state);
        Grid.SetColumn(menu, 2);
        line1.Children.Add(menu);

        if (AppRouting.IsSupported && app.ProcessIds.Count > 0)
        {
            var route = BuildRoutePicker(state);
            Grid.SetColumn(route, 3);
            line1.Children.Add(route);
        }

        right.Children.Add(line1);

        // Line 2: slider, percent, reset, mute.
        var line2 = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        line2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        line2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        var slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center };
        state.Slider = slider;
        line2.Children.Add(slider);
        var percent = new EditablePercent(42, () => 100, v =>
        {
            slider.Value = v;
            _source?.SetVolume(state.App, v / 100f);
        });
        Grid.SetColumn(percent, 1);
        state.Percent = percent;
        line2.Children.Add(percent);
        var reset = Bits.FlatIcon(Gl.Reset, Strings.Mixer.ResetTip, () =>
        {
            slider.Value = 100;
            _source?.SetVolume(state.App, 1f);
        }, 9.5, 16);
        Grid.SetColumn(reset, 2);
        state.Reset = reset;
        line2.Children.Add(reset);
        var mute = Bits.FlatIcon(Gl.Speaker, null, () => _source?.SetMute(state.App, !state.App.Muted), 10, 22);
        Grid.SetColumn(mute, 3);
        state.Mute = mute;
        line2.Children.Add(mute);
        right.Children.Add(line2);
        slider.ValueChanged += (_, e) =>
        {
            percent.Text = (int)Math.Round(e.NewValue) + "%";
            reset.Opacity = Math.Round(e.NewValue) == 100 ? 0 : 1;
            if (!_updating)
            {
                _source?.SetVolume(state.App, (float)(e.NewValue / 100));
            }
        };

        var root = new Border { Child = grid, Tag = state, AllowDrop = true };
        state.Root = root;
        root.Drop += OnDrop;
        grid.ContextMenu = BuildMenu(state);
        return root;
    }

    private FrameworkElement BuildRoutePicker(RowState state)
    {
        var app = state.App;
        var outs = AudioDevices.List(DataFlow.Render);
        var items = new List<string> { Strings.Mixer.Default };
        items.AddRange(outs.Select(o => o.Name));
        var index = 0;
        if (!string.IsNullOrEmpty(app.RouteId))
        {
            index = outs.FindIndex(o => o.Id == app.RouteId) + 1;
            if (index == 0)
            {
                items.Add(Strings.Mixer.OutputUnavailable);
                index = items.Count - 1;
            }
        }

        var combo = Bits.MiniCombo(items, index, i =>
        {
            string? id = i == 0 || i - 1 >= outs.Count ? null : outs[i - 1].Id;
            _source?.SetRoute(state.App, id);
            _needRefresh = true;
        }, 112, 10.5);
        combo.ToolTip = Strings.Mixer.ChooseOutputTip;
        combo.Margin = new Thickness(6, 0, 0, 0);
        return combo;
    }

    private void UpdateRows()
    {
        _updating = true;
        try
        {
            foreach (var row in _rows)
            {
                var app = row.App;
                row.Dot.Visibility = app.Playing ? Visibility.Visible : Visibility.Collapsed;
                if (!row.Slider.IsMouseCaptureWithin && !row.Percent.IsKeyboardFocusWithin)
                {
                    row.Slider.Value = Math.Round(app.Volume * 100);
                }

                row.Percent.Text = (int)Math.Round(row.Slider.Value) + "%";
                row.Reset.Opacity = Math.Round(row.Slider.Value) == 100 ? 0 : 1;
                var silent = app.Muted || app.Volume <= 0.001f;
                row.Mute.Content = UiKit.Icon(silent ? Gl.SpeakerMute : Gl.Speaker, 10, silent ? ThemeKeys.Red : ThemeKeys.TextSecondary, FontWeights.SemiBold);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    // ------------------------------------------------------------------------------------------------ arrangement actions

    private ContextMenu BuildMenu(RowState state)
    {
        var menu = new ContextMenu { FontFamily = UiKit.TextFont };
        var app = state.App;
        menu.Items.Add(Item(app.Pinned ? Strings.Mixer.Unpin : Strings.Mixer.PinToTop, () => TogglePin(state.App)));
        menu.Items.Add(Item(Strings.Mixer.MoveUp, () => Move(state.App, -1)));
        menu.Items.Add(Item(Strings.Mixer.MoveDown, () => Move(state.App, 1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Mixer.HideFromList, () => Hide(state.App)));
        return menu;
    }

    private static MenuItem Item(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    private void ShowMenu(FrameworkElement anchor, RowState state)
    {
        var menu = BuildMenu(state);
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void TogglePin(MixerApp app)
    {
        var arrangement = MixerArrangement.Load(_app);
        if (app.Pinned)
        {
            arrangement.Pinned.Remove(app.Key);
            arrangement.Order.Remove(app.Key);
            arrangement.Order.Insert(0, app.Key);
        }
        else
        {
            arrangement.Order.Remove(app.Key);
            arrangement.Pinned.Remove(app.Key);
            arrangement.Pinned.Insert(0, app.Key);
        }

        arrangement.Save(_app);
        _needRefresh = true;
    }

    private void Move(MixerApp app, int delta)
    {
        if (_source is null)
        {
            return;
        }

        var group = _source.Apps.Where(a => a.Pinned == app.Pinned).Select(a => a.Key).ToList();
        var index = group.IndexOf(app.Key);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= group.Count)
        {
            return;
        }

        (group[index], group[target]) = (group[target], group[index]);
        Save(group, app.Pinned);
    }

    private void Save(List<string> group, bool pinned)
    {
        var arrangement = MixerArrangement.Load(_app);
        if (pinned)
        {
            arrangement.Pinned = [.. group, .. arrangement.Pinned.Where(k => !group.Contains(k))];
        }
        else
        {
            arrangement.Order = [.. group, .. arrangement.Order.Where(k => !group.Contains(k))];
        }

        arrangement.Save(_app);
        _needRefresh = true;
    }

    private void Hide(MixerApp app)
    {
        var hidden = _app.Get<List<string>>("mixerHiddenApps");
        if (!hidden.Contains(app.Key))
        {
            hidden.Add(app.Key);
            _app.Set("mixerHiddenApps", hidden);
        }

        _needRefresh = true;
    }

    private void WireDrag(FrameworkElement handle, RowState state)
    {
        handle.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                DragDrop.DoDragDrop(handle, state.App.Key, DragDropEffects.Move);
            }
        };
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_source is null || e.Data.GetData(typeof(string)) is not string draggedKey || sender is not Border { Tag: RowState target })
        {
            return;
        }

        var dragged = _source.Apps.FirstOrDefault(a => a.Key == draggedKey);
        if (dragged is null || dragged.Pinned != target.App.Pinned || dragged.Key == target.App.Key)
        {
            return;
        }

        var group = _source.Apps.Where(a => a.Pinned == dragged.Pinned).Select(a => a.Key).ToList();
        group.Remove(dragged.Key);
        group.Insert(Math.Max(0, group.IndexOf(target.App.Key)), dragged.Key);
        Save(group, dragged.Pinned);
    }

    // ------------------------------------------------------------------------------------------------ options

    private FrameworkElement OptionsBody()
    {
        var stack = new StackPanel();
        stack.Children.Add(Bits.ToggleLine(Strings.Mixer.HideInactive, null, _app.Get<bool>("mixerHideInactiveApps"), on =>
        {
            _app.Set("mixerHideInactiveApps", on);
            _needRefresh = true;
        }, SwitchSize.Mini, 11.5));

        // Lower volume when headphones disconnect.
        var lowerHost = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        var stepper = StepperRow();
        stepper.Visibility = _app.Get<bool>("mixerLowerVolumeOnHeadphonesDisconnect") ? Visibility.Visible : Visibility.Collapsed;
        lowerHost.Children.Add(Bits.CheckLine(Strings.Mixer.LowerOnDisconnect, Strings.Mixer.LowerOnDisconnectCaption, _app.Get<bool>("mixerLowerVolumeOnHeadphonesDisconnect"), on =>
        {
            _app.Set("mixerLowerVolumeOnHeadphonesDisconnect", on);
            stepper.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }));
        lowerHost.Children.Add(stepper);
        stack.Children.Add(lowerHost);

        // Finer steps.
        var finer = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        finer.Children.Add(Bits.CheckLine(Strings.Mixer.FinerSteps, Strings.Mixer.FinerStepsCaption, _app.Get<bool>("preciseVolumeRollerEnabled"), on => _app.Set("preciseVolumeRollerEnabled", on)));
        if (PreciseVolume.Failed)
        {
            var failed = UiKit.Text(Strings.Mixer.FinerStepsFailed, 9.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
            failed.Margin = new Thickness(23, 2, 0, 0);
            finer.Children.Add(failed);
        }

        stack.Children.Add(finer);

        if (_showShared)
        {
            if (_c.Runtime.IsAvailable("sound-output-switcher"))
            {
                var switcher = OutputSwitcherView.Build(_c, compact: true);
                switcher.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(switcher);
            }

            if (_c.Runtime.IsAvailable("audio-priority"))
            {
                var priority = Bits.Disclosure(Strings.AudioPriority.Title, () => AudioPriorityView.Build(_c), expanded: false, indent: 19, fontSize: 11.5, leading: UiKit.Icon(Gl.List, 10.5, ThemeKeys.TextSecondary));
                priority.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(priority);
            }
        }

        stack.Children.Add(AppsInList());
        return stack;
    }

    private FrameworkElement StepperRow()
    {
        var row = new Grid { Margin = new Thickness(23, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(UiKit.Text(Strings.Mixer.VolumeAfterDisconnect, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        var value = UiKit.Text(_app.Get<int>("mixerHeadphonesDisconnectVolumePercent") + "%", 10.5, FontWeights.Medium, ThemeKeys.TextPrimary);
        value.Width = 38;
        value.TextAlignment = TextAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        void Step(int delta)
        {
            var v = Math.Clamp(_app.Get<int>("mixerHeadphonesDisconnectVolumePercent") + delta, 0, 100);
            _app.Set("mixerHeadphonesDisconnectVolumePercent", v);
            value.Text = v + "%";
        }

        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(value);
        controls.Children.Add(Bits.FlatIcon("", null, () => Step(-5), 9, 20));
        controls.Children.Add(Bits.FlatIcon(Gl.Add, null, () => Step(5), 9, 20));
        Grid.SetColumn(controls, 1);
        row.Children.Add(controls);
        return row;
    }

    private FrameworkElement AppsInList()
    {
        var host = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var hidden = _app.Get<List<string>>("mixerHiddenApps");
        var summary = UiKit.Text(hidden.Count == 0 ? Strings.Mixer.All : Strings.Mixer.Hidden(hidden.Count), 10, FontWeights.Normal, ThemeKeys.TextSecondary);
        var disclosure = Bits.Disclosure(Strings.Mixer.AppsInList, () =>
        {
            var list = new StackPanel();
            var known = _app.Get<Dictionary<string, string>>("mixerKnownApps");
            foreach (var (key, name) in known.OrderBy(k => k.Value, StringComparer.CurrentCultureIgnoreCase))
            {
                var k = key;
                var check = Bits.Check(!_app.Get<List<string>>("mixerHiddenApps").Contains(k));
                check.Margin = new Thickness(0, 1, 0, 1);
                var label = UiKit.Text(name, 10.5 / 0.8, FontWeights.Normal, ThemeKeys.TextPrimary);
                check.Content = label;
                check.Click += (_, _) =>
                {
                    var h = _app.Get<List<string>>("mixerHiddenApps");
                    h.RemoveAll(x => x == k);
                    if (check.IsChecked != true)
                    {
                        h.Add(k);
                    }

                    _app.Set("mixerHiddenApps", h);
                    summary.Text = h.Count == 0 ? Strings.Mixer.All : Strings.Mixer.Hidden(h.Count);
                    _needRefresh = true;
                };
                list.Children.Add(check);
            }

            return list;
        }, expanded: false, indent: 19, fontSize: 10, trailing: summary, chevronSize: 8);
        host.Children.Add(disclosure);
        return host;
    }
}
