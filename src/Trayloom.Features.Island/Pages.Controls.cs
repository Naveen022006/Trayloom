// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Volume level row (<c>NotchAudioControls</c>): mute button, slider, output chooser.</summary>
internal sealed class VolumeLevel : Grid
{
    private readonly IslandCore _core;
    private readonly LevelSlider _slider = new(false, 0, 1, 8) { EasesChanges = true, WheelStep = 0.05 };
    private readonly TextBlock _mute;
    private readonly HoverButton _muteButton;
    private readonly Func<IDisposable?> _hold;
    private readonly Action? _openMixer;

    public VolumeLevel(IslandCore core, Func<IDisposable?> hold, Action? openMixer)
    {
        _core = core;
        _hold = hold;
        _openMixer = openMixer;
        Height = 28;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _mute = D.I(G.Speaker, 12, D.Text);
        _mute.FontWeight = FontWeights.Medium;
        _muteButton = new HoverButton(() => _core.Audio.SetMuted(!_core.Audio.Muted)) { Width = 18, Height = 18, CornerRadius = new CornerRadius(6), Child = _mute, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        Children.Add(_muteButton);
        Grid.SetColumn(_slider, 1);
        Children.Add(_slider);
        var chevron = new HoverButton(ShowOutputs) { Width = 16, Height = 24, CornerRadius = new CornerRadius(6), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var ic = D.I(G.ChevronDown, 9, D.Secondary);
        chevron.Child = ic;
        chevron.Icon = ic;
        Grid.SetColumn(chevron, 2);
        Children.Add(chevron);
        D.Tip(chevron, core.Audio.DefaultName);
        _slider.ValueChanged += v => _core.Audio.SetVolume(v);
        Loaded += (_, _) =>
        {
            _core.Audio.VolumeChanged += Update;
            Update();
        };
        Unloaded += (_, _) => _core.Audio.VolumeChanged -= Update;
    }

    private void Update()
    {
        var v = _core.Audio.Volume;
        var muted = _core.Audio.Muted;
        _slider.IsEnabled = v is not null;
        if (v is { } vol)
        {
            _slider.Value = muted ? 0 : vol;
        }

        _mute.Text = muted ? G.SpeakerOff : G.Speaker;
        _mute.Foreground = muted ? D.Red : D.Text;
        D.Tip(_muteButton, muted ? Strings.Unmute : Strings.Mute);
    }

    private void ShowOutputs()
    {
        var items = new List<IslandMenu.Item>();
        foreach (var o in _core.Audio.Outputs())
        {
            var id = o.Id;
            items.Add(new IslandMenu.Item(o.Name, () => _core.Audio.SetDefaultOutput(id), o.IsDefault));
        }

        if (_openMixer is not null)
        {
            items.Add(IslandMenu.Sep());
            items.Add(new IslandMenu.Item(Strings.VolumeMixer, _openMixer, Glyph: G.Mixer));
        }

        IslandMenu.Show(this, items, _hold);
    }
}

internal sealed class BrightnessLevel : Grid
{
    private readonly LevelSlider _slider = new(false, 0, 1, 8) { EasesChanges = true };

    public BrightnessLevel()
    {
        Height = 28;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = D.I(G.Brightness, 12, D.Text);
        icon.FontWeight = FontWeights.Medium;
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(icon);
        _slider.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_slider, 1);
        Children.Add(_slider);
        // start from the newest known level (not hidden, so no ease from empty); the refresh below only corrects it
        if (BrightnessService.LastKnown is { } known)
        {
            _slider.Value = known;
        }

        _slider.ValueChanged += v => BrightnessService.Write(v);
        Action<double> follow = level => Dispatcher.BeginInvoke(() => Apply(level));
        Loaded += (_, _) => BrightnessService.LevelChanged += follow;
        Unloaded += (_, _) => BrightnessService.LevelChanged -= follow;
        Loaded += (_, _) => _ = Task.Run(Refresh);
    }

    /// <summary>Puts a level on the slider unless the user is dragging it or the island just set the level itself.</summary>
    private void Apply(double level)
    {
        if (_slider.IsDragging || DateTime.UtcNow - BrightnessService.LastOwnWrite <= TimeSpan.FromSeconds(1))
        {
            return;
        }

        _slider.IsEnabled = true;
        _slider.Value = level;
    }

    /// <summary>Reads the true level off the UI thread; one miss is retried before the slider is given up on.</summary>
    private void Refresh()
    {
        double? v = null;
        try
        {
            v = BrightnessService.Read();
            if (v is null)
            {
                Thread.Sleep(500);
                v = BrightnessService.Read();
            }
        }
        catch (Exception ex)
        {
            // runs unobserved on the pool: an unreadable panel is the same as none
            BrightnessService.Log?.Invoke("Island brightness read failed", ex);
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (v is { } value)
            {
                Apply(value);
            }
            else if (!BrightnessService.Available)
            {
                BrightnessService.Log?.Invoke("Island brightness slider disabled: the built-in panel cannot be read", null);
                _slider.IsEnabled = false;
                D.Tip(this, Strings.NoDisplay);
            }
        });
    }
}

/// <summary>The music card of the Controls page (<c>NotchMusicControlsView</c>).</summary>
internal sealed class MusicCard : Border
{
    private readonly IslandCore _core;
    private readonly IslandWindow? _win;
    private readonly bool _preview;

    public MusicCard(IslandCore core, IslandWindow? win, double height, bool preview)
    {
        _core = core;
        _win = win;
        _preview = preview;
        Height = height;
        CornerRadius = new CornerRadius(18);
        Background = D.White(0.065);
        Padding = new Thickness(12, 0, 12, 0);
        Loaded += (_, _) =>
        {
            _core.Playback.Changed += Build;
            _core.AppleMusic.Changed += Build;
            if (!_preview && !_viewing)
            {
                _viewing = true;
                _core.AppleMusic.ViewerShown();
            }

            Build();
        };
        Unloaded += (_, _) =>
        {
            _core.Playback.Changed -= Build;
            _core.AppleMusic.Changed -= Build;
            if (_viewing)
            {
                _viewing = false;
                _core.AppleMusic.ViewerHidden();
            }
        };
    }

    // the card shows Apple Music's progress while it is on screen (a preview does not count)
    private bool _viewing;

    private void Build()
    {
        var p = _core.Playback.Current;
        var side = Math.Max(40, Height - 24);
        var preferred = PlaybackService.ResolvePreferredPlayer(_core.App.Get<string>(K.PreferredPlayer), _core.Playback);
        var idle = !p.HasTrack && !_core.Playback.AwaitingPlayback;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cover = new HoverButton(() => { if (idle && (preferred is not null || MusicUi.HiddenApple(_core, preferred))) { MusicUi.IdleAction(_core, preferred, AmAction.PlayPause)(); } else { _win?.ShowModule("music"); } }) { Width = side, Height = side, CornerRadius = new CornerRadius(side * 0.19), Lifts = false, IsEnabled = !(idle && MusicUi.HiddenApple(_core, preferred) && _core.AppleMusic.Busy) };
        cover.Child = Cover.For(p, side, false, _core.Playback.Sources.FirstOrDefault(s => s.Aumid == p.Aumid)?.Icon);
        grid.Children.Add(cover);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        string title = p.HasTrack ? p.Display : (_core.Playback.AwaitingPlayback ? Strings.MediaNowPlaying : Strings.MediaNothingPlaying);
        string sub = p.HasTrack ? (_core.Playback.CommandFailed ? Strings.PlaybackFailed : p.Subtitle) : (_core.Playback.AwaitingPlayback ? "" : Strings.MusicHint);
        var titleBlock = D.T(title, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis);
        var subBlock = D.T(sub, 11, null, _core.Playback.CommandFailed ? D.Orange : D.Secondary, false, TextTrimming.CharacterEllipsis);
        titles.Children.Add(titleBlock);
        if (!p.HasTrack && MusicUi.HiddenApple(_core, preferred) && _core.AppleMusic.Busy)
        {
            // the island's request is starting Apple Music or working in it
            var working = new StackPanel { Orientation = Orientation.Horizontal };
            var spinner = PageUi.Spinner(10);
            spinner.Margin = new Thickness(0, 0, 6, 0);
            spinner.VerticalAlignment = VerticalAlignment.Center;
            working.Children.Add(spinner);
            working.Children.Add(D.T(_core.AppleMusic.Resuming ? LibStrings.Resuming : LibStrings.StartingApple, 11, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
            titles.Children.Add(working);
        }
        else
        {
            if (!p.HasTrack && MusicUi.HiddenApple(_core, preferred) && _core.AppleMusic.Note is { Error: true } failed)
            {
                subBlock.Text = failed.Text;
                subBlock.Foreground = D.Orange;
            }

            titles.Children.Add(subBlock);
        }
        var titleButton = new HoverButton(() => _win?.ShowModule("music")) { Child = titles, CornerRadius = new CornerRadius(8), Lifts = false, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(titleButton, 1);
        grid.Children.Add(titleButton);
        var transport = MusicUi.Transport(_core, 16, 22, 36, 12, idle ? (preferred ?? (MusicUi.HiddenApple(_core, null) ? "AppleMusic" : null)) : null);
        transport.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(transport, 2);
        if (!(idle && preferred is null && !MusicUi.HiddenApple(_core, null)))
        {
            grid.Children.Add(transport);
        }

        Child = grid;
    }
}

internal sealed class ControlsPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly List<string> _levels = [];
    private List<string> _tiles = [];
    private bool _showMusic;
    private double _cardHeight = 80;
    private double _width = 400;
    private double _height = 200;

    public ControlsPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
        Compute();
    }

    /// <summary>The background panel check finished with a different answer: rebuild so the brightness slider appears or goes.</summary>
    private void OnBrightnessAvailability() => _view.Dispatcher.BeginInvoke(() =>
    {
        var before = _levels.Contains("brightness");
        Compute();
        if (before != _levels.Contains("brightness") && HasWindow && !IsPreview)
        {
            Window.ReopenPage();
        }
    });

    public override string ModuleId => "controls";

    public override FrameworkElement View => _view;

    private bool ShowsMicRow => !IsPreview && Core.MicInUse;

    /// <summary>An app started or stopped using the microphone while the page is open: rebuild so the row comes or goes.</summary>
    private void OnMicUse() => _view.Dispatcher.BeginInvoke(() =>
    {
        if (HasWindow && !IsPreview)
        {
            Window.ReopenPage();
        }
    });

    public override FrameworkElement? BandFill => _levels.Count == 0 ? null : BuildLevels();

    private bool Visible(ControlDef c)
    {
        var hidden = K.Csv(Core.App.Get<string>(K.HiddenControls));
        if (hidden.Contains(c.Id))
        {
            return false;
        }

        var rt = Core.Ctx.Runtime;
        if (c.Features.Length > 0 && !rt.AnyAvailable(c.Features))
        {
            return false;
        }

        return c.Id switch
        {
            "music" => Core.ModuleVisible("music"),
            "mixer" => Core.ModuleVisible("mixer"),
            "timer" => Core.ModuleVisible("timer"),
            "calendar" => Core.ModuleVisible("calendar"),
            "speedTest" => Core.ModuleVisible("system") && rt.IsAvailable("monitor-network"),
            "brightness" => BrightnessVisible(rt),
            "keyboardLight" => false,
            _ => true,
        };
    }

    private static bool _brightnessLogged;

    private bool BrightnessVisible(IFeatureRuntime rt)
    {
        var installed = rt.IsAvailable("brightness");
        var panel = BrightnessService.Available;
        var enabled = Core.App.Get<bool>(K.BrightnessControl);
        if (!(installed && panel && enabled) && !_brightnessLogged)
        {
            _brightnessLogged = true;
            Core.Ctx.Log.Info($"Island brightness slider hidden: feature installed={installed}, panel controllable={panel}, control displays={enabled}");
        }

        return installed && panel && enabled;
    }

    /// <summary>"Control displays" was switched: rebuild so the brightness slider appears or goes.</summary>
    private void OnDisplayControlSetting(string key)
    {
        if (key == K.BrightnessControl)
        {
            OnBrightnessAvailability();
        }
    }

    private void Compute()
    {
        _levels.Clear();
        foreach (var c in IslandCatalog.Controls.Where(c => c.IsLevel && Visible(c)))
        {
            _levels.Add(c.Id);
        }

        _showMusic = IslandCatalog.Control("music") is { } m && Visible(m);
        _tiles = [.. IslandCatalog.ControlOrder(Core.App).Select(IslandCatalog.Control).Where(c => c is { IsLevel: false } && c.Id != "music" && Visible(c!)).Select(c => c!.Id)];
    }

    public override double Height(double width, double budget)
    {
        _width = width;
        var mic = ShowsMicRow ? MicUseRow.Height + 10 : 0;
        if (_levels.Count == 0 && !_showMusic && _tiles.Count == 0 && mic == 0)
        {
            return Math.Min(budget, 140);
        }

        var spacing = 10.0;
        var levelRow = 0.0;
        var rows = _tiles.Count == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(_tiles.Count / (double)Math.Max(1, (int)Math.Floor((width + 8) / (76 + 8)))));
        double card = _showMusic ? 80 : 0;
        double railRows = rows;
        double Total() => mic + (levelRow > 0 ? levelRow + spacing : 0) + (card > 0 ? card + spacing : 0) + (railRows > 0 ? (railRows * 74) + ((railRows - 1) * 8) : 0) - (card == 0 && railRows == 0 && levelRow > 0 ? spacing : 0);
        var total = Total();
        if (total > budget && card > 68)
        {
            card = Math.Max(68, card - (total - budget));
            total = Total();
        }

        while (total > budget && railRows > 1)
        {
            railRows--;
            total = Total();
        }

        _cardHeight = card;
        _height = Math.Min(Math.Max(total, 60), budget);
        BuildView(width, railRows);
        return _height;
    }

    private void BuildView(double width, double railRows)
    {
        _view.Children.Clear();
        var stack = new StackPanel();
        if (ShowsMicRow)
        {
            // "Microphone in use · Claude": the privacy row sits above everything and opens Windows' microphone privacy page
            var micRow = MicUseRow.Create(Core);
            micRow.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(micRow);
        }

        if (_showMusic)
        {
            var card = new MusicCard(Core, HasWindow ? Window : null, _cardHeight, IsPreview) { Margin = new Thickness(0, 0, 0, 0) };
            stack.Children.Add(card);
        }

        if (_tiles.Count > 0)
        {
            var items = new List<FrameworkElement>();
            foreach (var id in _tiles)
            {
                items.Add(BuildTile(id));
            }

            var rail = PageUi.Rail(items, 76, 74, 8, width, (railRows * 74) + ((railRows - 1) * 8));
            rail.Margin = new Thickness(0, _showMusic ? 10 : 0, 0, 0);
            stack.Children.Add(rail);
        }

        _view.Children.Add(stack);
    }

    private FrameworkElement BuildLevels()
    {
        var row = new Grid { Height = 28 };
        for (var i = 0; i < _levels.Count; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 76 });
            FrameworkElement level = _levels[i] switch
            {
                "volume" => new VolumeLevel(Core, () => HasWindow ? Window.HoldOpen() : null, HasWindow && Core.ModuleVisible("mixer") ? () => Window.ShowModule("mixer") : null),
                "brightness" => new BrightnessLevel(),
                _ => new Border(),
            };
            level.Margin = new Thickness(i == 0 ? 0 : 6, 0, i == _levels.Count - 1 ? 0 : 6, 0);
            Grid.SetColumn(level, i);
            row.Children.Add(level);
        }

        return row;
    }

    private FrameworkElement BuildTile(string id)
    {
        var def = IslandCatalog.Control(id)!;
        string title = def.Title;
        string glyph = def.Glyph;
        var accent = TileAccent.None;
        string? reading = null;
        string? compact = null;
        switch (id)
        {
            case "keepAwake":
                var on = Core.KeepAwake?.IsActive == true;
                accent = on ? TileAccent.Awake : TileAccent.None;
                break;
            case "microphone":
                var muted = Core.Audio.MicMuted;
                title = muted ? Loc.T("Unmute microphone") : Loc.T("Mute microphone");
                glyph = muted ? G.MicOff : G.Mic;
                accent = muted ? TileAccent.Alert : TileAccent.None;
                break;
            case "timer":
                if (Core.Timer.HasSession)
                {
                    accent = TileAccent.Timer;
                    reading = Core.Timer.ClockText;
                    title = Core.Timer.PhaseName;
                    compact = Loc.T("Timer");
                    glyph = Core.Timer.Completed ? G.Check : (Core.Timer.Paused ? G.Pause : (Core.Timer.IsStopwatch ? G.Stopwatch : G.Timer));
                }

                break;
            case "calendar":
                if (Core.Calendar.Next(DateTime.Now) is { } next && next.Start > DateTime.Now)
                {
                    title = next.Title.Length == 0 ? Loc.T("Calendar") : next.Title;
                    reading = next.Start.Date == DateTime.Today ? next.Start.ToString("t") : next.Start.ToString("ddd " + Loc.ShortTime);
                    compact = Loc.T("Calendar");
                }

                break;
            case "recording":
                break;
        }

        return PageUi.ActionTile(glyph, title, () => { if (HasWindow) { Window.RunControl(id); } }, accent, reading, compact);
    }

    public override void Shown()
    {
        BrightnessService.AvailabilityChanged += OnBrightnessAvailability;
        Core.App.Changed += OnDisplayControlSetting;
        Core.Timer.Ticked += Refresh;
        Core.Timer.Changed += Refresh;
        Core.Audio.MicChanged += Refresh;
        Core.MicUseChanged += OnMicUse;
        if (Core.KeepAwake is { } ka)
        {
            ka.Changed += OnAwake;
        }
    }

    public override void Hidden()
    {
        BrightnessService.AvailabilityChanged -= OnBrightnessAvailability;
        Core.App.Changed -= OnDisplayControlSetting;
        Core.Timer.Ticked -= Refresh;
        Core.Timer.Changed -= Refresh;
        Core.Audio.MicChanged -= Refresh;
        Core.MicUseChanged -= OnMicUse;
        if (Core.KeepAwake is { } ka)
        {
            ka.Changed -= OnAwake;
        }
    }

    private void OnAwake() => _view.Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        if (_view.Children.Count == 0)
        {
            return;
        }

        var rows = _tiles.Count == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(_tiles.Count / (double)Math.Max(1, (int)Math.Floor((_width + 8) / 84.0))));
        BuildView(_width, Math.Max(1, Math.Min(rows, Math.Floor((_height + 8) / 82.0))));
    }
}
