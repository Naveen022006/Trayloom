// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

internal sealed class MixerArrangement
{
    [JsonPropertyName("order")]
    public List<string> Order { get; set; } = [];

    [JsonPropertyName("pinned")]
    public List<string> Pinned { get; set; } = [];

    [JsonPropertyName("hidden")]
    public List<string> Hidden { get; set; } = [];

    public static MixerArrangement Load(string json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? new MixerArrangement() : JsonSerializer.Deserialize<MixerArrangement>(json) ?? new MixerArrangement();
        }
        catch
        {
            return new MixerArrangement();
        }
    }

    public string Save() => JsonSerializer.Serialize(this);

    public List<AppAudio> Ordered(IEnumerable<AppAudio> apps)
    {
        var list = apps.Where(a => !Hidden.Contains(a.Key)).ToList();
        int Rank(AppAudio a)
        {
            var i = Order.IndexOf(a.Key);
            return i < 0 ? int.MaxValue : i;
        }

        return [.. list.OrderBy(a => Pinned.Contains(a.Key) ? 0 : 1).ThenBy(Rank).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}

/// <summary>One vertical fader column (<c>NotchMasterFader</c> / <c>NotchAppFader</c>).</summary>
internal sealed class FaderColumn : StackPanel
{
    private readonly LevelSlider _slider = new(true, 0, 1, 28) { Width = 40, EasesChanges = true, WheelStep = 0.05 };
    private readonly TextBlock _percent = D.Digits("0%", 12, FontWeights.Medium);
    private readonly TextBlock _muteGlyph;
    private readonly HoverButton _mute;
    private readonly Border _percentHost = new() { Height = 28, Background = Brushes.Transparent };
    private TextBox? _editor;
    private readonly Action<double> _setVolume;
    private readonly Action<bool> _setMuted;
    private readonly string _glyphOn;
    private readonly string _glyphOff;

    public FaderColumn(double trackHeight, string label, string glyphOn, string glyphOff, Action<double> setVolume, Action<bool> setMuted, bool muteButton = true)
    {
        _setVolume = setVolume;
        _setMuted = setMuted;
        _glyphOn = glyphOn;
        _glyphOff = glyphOff;
        HorizontalAlignment = HorizontalAlignment.Center;
        _mute = new HoverButton(() => { }) { Width = 32, Height = 32, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center };
        _muteGlyph = D.I(glyphOn, 15, D.Text);
        _muteGlyph.FontWeight = FontWeights.Medium;
        _mute.Child = _muteGlyph;
        _mute.Visibility = muteButton ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(_mute);
        _slider.Height = trackHeight;
        _slider.Margin = new Thickness(0, 6, 0, 0);
        D.Tip(_slider, label);
        Children.Add(_slider);
        _percent.HorizontalAlignment = HorizontalAlignment.Center;
        _percent.VerticalAlignment = VerticalAlignment.Center;
        _percentHost.Child = _percent;
        _percentHost.Margin = new Thickness(0, 6, 0, 0);
        _percentHost.Cursor = Cursors.IBeam;
        _percentHost.MouseLeftButtonUp += (_, _) => Edit();
        Children.Add(_percentHost);
        _slider.ValueChanged += v =>
        {
            _setVolume(v);
            _percent.Text = $"{(int)Math.Round(v * 100)}%";
        };
    }

    public bool Dragging => _slider.IsDragging || _editor is not null;

    public void Wire(Func<bool> isMuted) => _mute.MouseLeftButtonUp += (_, _) => _setMuted(!isMuted());

    public void Update(double volume, bool muted)
    {
        if (Dragging)
        {
            return;
        }

        _slider.Value = muted ? 0 : volume;
        _percent.Text = $"{(int)Math.Round(volume * 100)}%";
        _muteGlyph.Text = muted ? _glyphOff : _glyphOn;
        _muteGlyph.Foreground = muted ? D.Red : D.Text;
    }

    public void SetEnabled(bool enabled) => _slider.IsEnabled = enabled;

    private void Edit()
    {
        if (_editor is not null)
        {
            return;
        }

        _editor = PageUi.FlatTextBox(12);
        _editor.Text = _percent.Text.TrimEnd('%');
        _editor.TextAlignment = TextAlignment.Center;
        _editor.Width = 40;
        _percentHost.Child = _editor;
        _editor.Focus();
        _editor.SelectAll();
        void Commit()
        {
            if (_editor is null)
            {
                return;
            }

            var text = _editor.Text.Trim().TrimEnd('%');
            _editor = null;
            _percentHost.Child = _percent;
            if (double.TryParse(text, out var v))
            {
                var value = Math.Clamp(v, 0, 100) / 100.0;
                _setVolume(value);
                _slider.Value = value;
                _percent.Text = $"{(int)Math.Round(value * 100)}%";
            }
        }

        _editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _editor = null;
                _percentHost.Child = _percent;
                e.Handled = true;
            }
        };
        _editor.LostKeyboardFocus += (_, _) => Commit();
    }
}

internal sealed class MixerPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private readonly DispatcherTimer _poll = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly Dictionary<string, FaderColumn> _apps = [];
    private FaderColumn? _master;
    private FaderColumn? _mic;
    private StackPanel? _rail;
    private double _w = 400;
    private double _h = 264;
    private bool _options;
    private string _signature = "";
    private List<AppAudio> _current = [];

    public MixerPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
        _poll.Tick += (_, _) => Tick();
    }

    public override string ModuleId => "mixer";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Leading => _leading;

    public override FrameworkElement? Trailing => _trailing;

    public override bool ScrollsContent => false;

    public override double Height(double width, double budget)
    {
        _w = width;
        _h = budget;
        Build();
        return budget;
    }

    public override void Shown() => _poll.Start();

    public override void Hidden() => _poll.Stop();

    public override bool StepBack()
    {
        if (!_options)
        {
            return false;
        }

        _options = false;
        Build();
        return true;
    }

    private MixerArrangement Arrangement => MixerArrangement.Load(Core.App.Get<string>(K.MixerArrangement));

    private void SaveArrangement(MixerArrangement a) => Core.App.Set(K.MixerArrangement, a.Save());

    private IEnumerable<AppAudio> Visible()
    {
        var apps = Arrangement.Ordered(Core.Audio.Apps());
        if (Core.App.Get<bool>(K.MixerHideInactive))
        {
            apps = [.. apps.Where(a => a.Playing || Math.Abs(a.Volume - 1) > 0.01)];
        }

        return apps;
    }

    private void Build()
    {
        _view.Children.Clear();
        _apps.Clear();
        BuildBand();
        if (_options)
        {
            _view.Children.Add(Options());
            return;
        }

        var h = Math.Max(104, _h);
        var track = Math.Min(160, Math.Max(32, h - 32 - 28 - 12));
        var row = new Grid();
        var hasMic = Core.Audio.MicAvailable && Core.Audio.MicVolume is not null;
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        if (hasMic)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        }

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _master = new FaderColumn(track, Loc.T("Output"), G.Speaker, G.SpeakerOff, v => Core.Audio.SetVolume(v), m => Core.Audio.SetMuted(m));
        _master.Wire(() => Core.Audio.Muted);
        _master.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(_master);
        var col = 1;
        if (hasMic)
        {
            _mic = new FaderColumn(track, Loc.T("Microphone"), G.Mic, G.MicOff, v => Core.Audio.SetMicVolume(v), m => Core.Audio.SetMicMuted(m));
            _mic.Wire(() => Core.Audio.MicMuted);
            _mic.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(_mic, 1);
            row.Children.Add(_mic);
            col = 2;
        }

        var div = new Border { Width = 1, Background = D.White(0.12), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
        Grid.SetColumn(div, col);
        row.Children.Add(div);
        var railHost = new SmoothScrollViewer { ScrollOrientation = ScrollAxis.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        _rail = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        railHost.Content = _rail;
        Grid.SetColumn(railHost, col + 1);
        row.Children.Add(railHost);
        _view.Children.Add(row);
        _track = track;
        RebuildApps(force: true);
        Tick();
    }

    private double _track = 120;

    private void RebuildApps(bool force)
    {
        if (_rail is null)
        {
            return;
        }

        var apps = Visible().ToList();
        var sig = string.Join("|", apps.Select(a => a.Key));
        if (!force && sig == _signature)
        {
            _current = apps;
            return;
        }

        _signature = sig;
        _current = apps;
        _apps.Clear();
        _rail.Children.Clear();
        if (apps.Count == 0)
        {
            var empty = D.EmptyView(G.Speaker, Strings.AppsUseAudio);
            empty.Width = Math.Max(120, _w - 200);
            _rail.Children.Add(empty);
            return;
        }

        var arr = Arrangement;
        foreach (var app in apps)
        {
            var key = app.Key;
            var column = new StackPanel { Width = 96 };
            var header = new Grid { Height = 32 };
            var icon = app.Icon is not null ? (FrameworkElement)new Image { Source = app.Icon, Width = 28, Height = 28 } : D.I(G.App, 20, D.Secondary);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            header.Children.Add(icon);
            if (arr.Pinned.Contains(key))
            {
                var pin = D.I(G.Pin, 9, D.Secondary);
                pin.HorizontalAlignment = HorizontalAlignment.Left;
                pin.VerticalAlignment = VerticalAlignment.Top;
                pin.Margin = new Thickness(6, 2, 0, 0);
                header.Children.Add(pin);
            }

            var dot = new Ellipse_ { Width = 7, Height = 7, Fill = D.Green, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20, 0, 0, 0), Visibility = app.Playing ? Visibility.Visible : Visibility.Collapsed };
            header.Children.Add(dot);
            HoverButton? more = null;
            more = new HoverButton(() => IslandMenu.Show(more!, AppMenu(app), () => HasWindow ? Window.HoldOpen() : null)) { Width = 24, Height = 28, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Right };
            var mi = D.I(G.More, 12, D.White(0.7));
            mi.FontWeight = FontWeights.SemiBold;
            more.Child = mi;
            D.Tip(more, Loc.T("Actions"));
            header.Children.Add(more);
            column.Children.Add(header);
            var captured = app;
            var fader = new FaderColumn(_track, app.Name, G.Speaker, G.SpeakerOff, v => { AudioService.SetAppVolume(captured, v); }, m => AudioService.SetAppMuted(captured, m));
            fader.Wire(() => captured.Muted);
            fader.Margin = new Thickness(0, 0, 0, 0);
            column.Children.Add(fader);
            var name = D.T(app.Name, 10, null, D.Secondary, false, TextTrimming.CharacterEllipsis);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.MaxWidth = 90;
            column.Children.Add(name);
            D.Tip(column, app.Name + "\nHold Ctrl and drag to reorder");
            _apps[key] = fader;
            _rail.Children.Add(column);
        }
    }

    private IEnumerable<IslandMenu.Item> AppMenu(AppAudio app)
    {
        var arr = Arrangement;
        var key = app.Key;
        var pinned = arr.Pinned.Contains(key);
        var keys = Visible().Select(a => a.Key).ToList();
        var i = keys.IndexOf(key);
        yield return new IslandMenu.Item(pinned ? Loc.T("Unpin") : Loc.T("Pin to Front"), () =>
        {
            var a = Arrangement;
            if (!a.Pinned.Remove(key))
            {
                a.Pinned.Add(key);
            }

            SaveArrangement(a);
            RebuildApps(true);
        });
        yield return new IslandMenu.Item(Loc.T("Move Left"), () => Move(key, -1), Enabled: i > 0);
        yield return new IslandMenu.Item(Loc.T("Move Right"), () => Move(key, 1), Enabled: i >= 0 && i < keys.Count - 1);
        yield return IslandMenu.Sep();
        yield return new IslandMenu.Item(Loc.T("Hide from the list"), () =>
        {
            var a = Arrangement;
            a.Hidden.Add(key);
            SaveArrangement(a);
            RebuildApps(true);
        });
    }

    private void Move(string key, int by)
    {
        var a = Arrangement;
        var keys = Visible().Select(x => x.Key).ToList();
        var i = keys.IndexOf(key);
        var j = i + by;
        if (i < 0 || j < 0 || j >= keys.Count)
        {
            return;
        }

        (keys[i], keys[j]) = (keys[j], keys[i]);
        a.Order = keys;
        SaveArrangement(a);
        RebuildApps(true);
    }

    private void Tick()
    {
        if (_options)
        {
            return;
        }

        if (_master is not null)
        {
            _master.Update(Core.Audio.Volume ?? 0, Core.Audio.Muted);
            _master.SetEnabled(Core.Audio.Volume is not null);
        }

        _mic?.Update(Core.Audio.MicVolume ?? 0, Core.Audio.MicMuted);
        var fresh = Visible().ToList();
        var sig = string.Join("|", fresh.Select(a => a.Key));
        if (sig != _signature && !_apps.Values.Any(f => f.Dragging))
        {
            RebuildApps(true);
            fresh = [.. _current];
        }

        foreach (var app in fresh)
        {
            if (_apps.TryGetValue(app.Key, out var f))
            {
                f.Update(app.Volume, app.Muted);
            }
        }

        _current = fresh;
    }

    private void BuildBand()
    {
        _leading.Children.Clear();
        _trailing.Children.Clear();
        var outputs = Core.Audio.Outputs();
        var def = outputs.FirstOrDefault(o => o.IsDefault);
        var menu = PageUi.BandMenu(def?.Name ?? Strings.OutputUnavailable, null, () => outputs.Select(o => { var id = o.Id; return new IslandMenu.Item(o.Name, () => { Core.Audio.SetDefaultOutput(id); Build(); }, o.IsDefault); }), () => HasWindow ? Window.HoldOpen() : null, def?.Name ?? Core.Audio.LastError, 160);
        _leading.Children.Add(menu);
        _trailing.Children.Add(D.IconButton(_options ? G.Close : G.Controls, _options ? Loc.T("Close") : Loc.T("Options"), () => { _options = !_options; Build(); }, _options));
    }

    private FrameworkElement Options()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        FrameworkElement Row(string glyph, string label, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8), MinHeight = 22 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(D.I(glyph, 12, D.Secondary));
            var l = D.T(label, 11, null, D.Secondary);
            l.Margin = new Thickness(8, 0, 0, 0);
            left.Children.Add(l);
            g.Children.Add(left);
            control.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        var outputs = Core.Audio.Outputs();
        var def = outputs.FirstOrDefault(o => o.IsDefault);
        stack.Children.Add(Row(G.Speaker, Loc.T("Output"), outputs.Count == 0 ? D.T(Loc.T("No outputs found"), 9.5, null, D.Secondary) : PageUi.BandMenu(def?.Name ?? Loc.T("Default"), null, () => outputs.Select(o => { var id = o.Id; return new IslandMenu.Item(o.Name, () => Core.Audio.SetDefaultOutput(id), o.IsDefault); }), () => HasWindow ? Window.HoldOpen() : null, null, 200)));
        var inputs = Core.Audio.Inputs();
        var defIn = inputs.FirstOrDefault(o => o.IsDefault);
        stack.Children.Add(Row(G.Mic, Loc.T("Microphone"), inputs.Count == 0 ? D.T(Loc.T("No microphones found"), 9.5, null, D.Secondary) : D.T(defIn?.Name ?? Loc.T("Default"), 11, null, D.Text, false, TextTrimming.CharacterEllipsis)));
        stack.Children.Add(new Border { Height = 1, Background = D.White(0.12), Margin = new Thickness(0, 2, 0, 10) });
        stack.Children.Add(Row("", Loc.T("Hide inactive apps"), new IslandToggle(Core.App.Get<bool>(K.MixerHideInactive), on => Core.App.Set(K.MixerHideInactive, on))));
        stack.Children.Add(Row("", Loc.T("Lower volume when headphones disconnect"), new IslandToggle(Core.App.Get<bool>(K.MixerLowerOnDisconnect), on => { Core.App.Set(K.MixerLowerOnDisconnect, on); })));
        stack.Children.Add(D.T(Loc.T("Adjusts output when wired or Bluetooth headphones disconnect."), 9.5, null, D.Tertiary, true));
        stack.Children.Add(Row("", Loc.T("Use finer volume steps"), new IslandToggle(Core.App.Get<bool>(K.FineVolume), on => Core.App.Set(K.FineVolume, on))));
        stack.Children.Add(D.T(Loc.T("Turns volume wheels and keys into smaller system volume steps."), 9.5, null, D.Tertiary, true));
        var hidden = Arrangement.Hidden;
        if (hidden.Count > 0)
        {
            var reset = D.PillButton(Loc.T("Show hidden apps"), () => { var a = Arrangement; a.Hidden.Clear(); SaveArrangement(a); Build(); });
            reset.Margin = new Thickness(0, 10, 0, 0);
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(reset);
        }

        return PageUi.VScroll(stack);
    }
}
