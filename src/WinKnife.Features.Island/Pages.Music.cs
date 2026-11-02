// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Island;

internal static class MusicUi
{
    /// <summary>Compact transport (previous, play/pause, next) used by the Controls card; the idle form opens the preferred player.</summary>
    public static FrameworkElement Transport(IslandCore core, double side, double play, double height, double spacing, string? openApp)
    {
        var p = core.Playback.Current;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = height };
        HoverButton Btn(string glyph, double size, Action click, bool enabled = true, string? tip = null)
        {
            var b = new HoverButton(click) { Width = height, Height = height, CornerRadius = new CornerRadius(14), Margin = new Thickness(spacing / 2, 0, spacing / 2, 0) };
            var ic = D.I(glyph, size, D.Text);
            ic.FontWeight = FontWeights.SemiBold;
            b.Child = ic;
            b.IsEnabled = enabled;
            if (tip is not null)
            {
                D.Tip(b, tip);
            }

            return b;
        }

        if (openApp is not null)
        {
            Action open = () => PlaybackService.OpenPlayer(openApp);
            row.Children.Add(Btn(G.Previous, side, open));
            row.Children.Add(Btn(G.Play, play, open));
            row.Children.Add(Btn(G.Next, side, open));
            return row;
        }

        row.Children.Add(Btn(G.Previous, side, core.Playback.Previous, p.CanPrevious, Strings.Previous));
        row.Children.Add(Btn(p.IsPlaying ? G.Pause : G.Play, play, core.Playback.TogglePlayPause, p.CanPlayPause || p.HasTrack, Strings.PlayPause));
        row.Children.Add(Btn(G.Next, side, core.Playback.Next, p.CanNext, Strings.Next));
        return row;
    }
}

/// <summary>The playback timeline: elapsed, slider (seek or meter) and remaining time (<c>NotchMusicTimeline</c>).</summary>
internal sealed class MusicTimeline : Grid
{
    private readonly IslandCore _core;
    private readonly TextBlock _left = D.Digits("0:00", 10, FontWeights.Medium, D.Secondary);
    private readonly TextBlock _right = D.Digits("−0:00", 10, FontWeights.Medium, D.Secondary);
    private readonly LevelSlider _slider;
    private readonly Meter _meter = new(0, null, 6);
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
    private double? _released;
    private DateTime _releasedAt;
    private Brush _accent;

    public MusicTimeline(IslandCore core, Playback p)
    {
        _core = core;
        Height = 14;
        _accent = p.Tint is { } t ? new SolidColorBrush(t) : Brushes.White;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var widest = p.Duration >= 3600 ? "00:00:00" : "00:00";
        _left.MinWidth = _right.MinWidth = MeasureWidth(widest + (p.Duration >= 3600 ? "" : ""), 10);
        _right.MinWidth += 6;
        _left.VerticalAlignment = _right.VerticalAlignment = VerticalAlignment.Center;
        _left.Margin = new Thickness(0, 0, 8, 0);
        _right.Margin = new Thickness(8, 0, 0, 0);
        _right.TextAlignment = TextAlignment.Right;
        Children.Add(_left);
        Grid.SetColumn(_right, 2);
        Children.Add(_right);
        _slider = new LevelSlider(false, 0, Math.Max(1, p.Duration), 8, _accent) { Height = 10, Scrollable = false };
        _meter.Tint = _accent;
        _meter.VerticalAlignment = VerticalAlignment.Center;
        if (p.CanSeek)
        {
            Grid.SetColumn(_slider, 1);
            Children.Add(_slider);
            D.Tip(_slider, Strings.PlaybackPosition);
            _slider.ValueChanged += v => { _left.Text = D.TrackTime(v); _right.Text = "−" + D.TrackTime(p.Duration - v); };
            _slider.ValueCommitted += v =>
            {
                _released = v;
                _releasedAt = DateTime.UtcNow;
                _core.Playback.Seek(v);
            };
        }
        else
        {
            Grid.SetColumn(_meter, 1);
            Children.Add(_meter);
        }

        _tick.Tick += (_, _) => Update();
        Loaded += (_, _) =>
        {
            Update();
            _tick.Start();
        };
        Unloaded += (_, _) => _tick.Stop();
    }

    private static double MeasureWidth(string text, double size)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(D.Font, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), size, Brushes.White, 1.25);
        return Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 2;
    }

    public void Update()
    {
        var p = _core.Playback.Current;
        if (_slider.IsDragging || p.Duration <= 0)
        {
            return;
        }

        var pos = p.Position;
        if (_released is { } r)
        {
            if ((DateTime.UtcNow - _releasedAt).TotalSeconds > 1 || Math.Abs(pos - r) < 2)
            {
                _released = null;
            }
            else
            {
                pos = r;
            }
        }

        _left.Text = D.TrackTime(pos);
        _right.Text = "−" + D.TrackTime(p.Duration - pos);
        _slider.Value = pos;
        _meter.Value = p.Duration > 0 ? pos / p.Duration : 0;
    }
}

internal enum MusicExtra
{
    None,
    Lyrics,
    Queue,
}

internal sealed class MusicPage : IslandPage
{
    private readonly Grid _view = new();
    private MusicExtra _extra;
    private FrameworkElement? _extraView;
    private double _width = 344;
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private readonly DispatcherTimer _poll = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private LyricsView? _lyrics;

    public MusicPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
    }

    public override string ModuleId => "music";

    public override FrameworkElement View => _view;

    public override double? NaturalWidth => 344;

    public override bool WantsKeyboard => false;

    public override FrameworkElement? Trailing => null;

    public override bool GrowsBeyondBudget => _extra != MusicExtra.None;

    private bool LyricsOn => Core.App.Get<bool>(K.LyricsEnabled) && Core.Ctx.Runtime.IsAvailable("notch-lyrics");

    private bool QueueOn => Core.App.Get<bool>(K.QueueEnabled) && Core.Ctx.Runtime.IsAvailable("notch-queue");

    public override double Height(double width, double budget)
    {
        _width = Math.Min(344, width);
        var p = Core.Playback.Current;
        var h = (p.Duration > 0 ? 56 + 14 + 14 + 10 + 44 + 8 : 56 + 14 + 44 + 8);
        if (Core.App.Get<bool>(K.MusicShowVolume) && Core.ModuleVisible("mixer"))
        {
            h += 38;
        }

        if (_extra != MusicExtra.None)
        {
            h += 10 + 216;
        }

        Build();
        return Math.Min(h, budget);
    }

    public override void Shown()
    {
        Core.Playback.Changed += OnChanged;
        Core.Lyrics.Changed += OnLyrics;
        _poll.Start();
        Core.Levels.ForgetSilence();
    }

    public override void Hidden()
    {
        Core.Playback.Changed -= OnChanged;
        Core.Lyrics.Changed -= OnLyrics;
        _poll.Stop();
    }

    public override bool StepBack()
    {
        if (_extra == MusicExtra.None)
        {
            return false;
        }

        SetExtra(MusicExtra.None);
        return true;
    }

    private void OnLyrics() => _view.Dispatcher.BeginInvoke(() => _lyrics?.Refresh());

    private DateTime _lastChange;

    private void OnChanged()
    {
        _view.Dispatcher.BeginInvoke(() =>
        {
            var p = Core.Playback.Current;
            var hadTimeline = _hadTimeline;
            if ((p.Duration > 0) != hadTimeline || p.Identity != _identity || p.IsPlaying != _wasPlaying)
            {
                if (HasWindow)
                {
                    Window.ReopenPage();
                }
            }
        });
    }

    private bool _hadTimeline;
    private string _identity = "";
    private bool _wasPlaying;

    private void SetExtra(MusicExtra e)
    {
        _extra = e == _extra ? MusicExtra.None : e;
        if (_extra == MusicExtra.Lyrics)
        {
            Core.Lyrics.Ensure(Core.Playback.Current);
        }

        if (HasWindow)
        {
            Window.ReopenPage();
        }
    }

    private void Build()
    {
        _view.Children.Clear();
        var p = Core.Playback.Current;
        _hadTimeline = p.Duration > 0;
        _identity = p.Identity;
        _wasPlaying = p.IsPlaying;
        var idle = !p.HasTrack;
        var awaiting = idle && Core.Playback.AwaitingPlayback;
        var preferred = PlaybackService.ResolvePreferredPlayer(Core.App.Get<string>(K.PreferredPlayer), Core.Playback);
        var stack = new StackPanel { Width = _width, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        stack.Children.Add(BuildHead(p, idle, awaiting, preferred));
        if (p.Duration > 0)
        {
            var tl = new MusicTimeline(Core, p) { Margin = new Thickness(0, 14, 0, 0) };
            stack.Children.Add(tl);
        }

        var transport = BuildTransport(p, idle, preferred);
        transport.Margin = new Thickness(0, p.Duration > 0 ? 10 : 14, 0, 0);
        stack.Children.Add(transport);
        if (Core.App.Get<bool>(K.MusicShowVolume) && Core.ModuleVisible("mixer"))
        {
            var vol = new VolumeLevel(Core, () => HasWindow ? Window.HoldOpen() : null, null) { Margin = new Thickness(0, 18, 0, 0) };
            stack.Children.Add(vol);
        }

        if (_extra != MusicExtra.None)
        {
            _extraView = _extra == MusicExtra.Lyrics ? (_lyrics = new LyricsView(Core, () => HasWindow ? Window.HoldOpen() : null)) : BuildQueue(preferred);
            var holder = new Grid { Height = 216, ClipToBounds = true, Margin = new Thickness(0, 10, 0, 0) };
            holder.Children.Add(_extraView);
            stack.Children.Add(holder);
        }

        _view.Children.Add(stack);
    }

    private FrameworkElement BuildHead(Playback p, bool idle, bool awaiting, string? preferred)
    {
        var head = new Grid { Height = 56 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Core.Playback.Sources.FirstOrDefault(s => s.Aumid == p.Aumid)?.Icon;
        var coverBtn = new HoverButton(() => { if (idle) { PlaybackService.OpenPlayer(preferred); } else { PlaybackService.OpenPlayer(p.Aumid); } }) { Width = 56, Height = 56, CornerRadius = new CornerRadius(10.64), Lifts = false, IsEnabled = !idle || preferred is not null };
        var cover = Cover.For(p, 56, false, icon);
        cover.RenderTransformOrigin = new Point(0.5, 0.5);
        cover.RenderTransform = new ScaleTransform(p.IsPlaying || idle ? 1 : 0.94, p.IsPlaying || idle ? 1 : 0.94);
        coverBtn.Child = cover;
        D.Tip(coverBtn, idle && preferred is not null ? "Open " + PlaybackService.NameOf(preferred) : Strings.MediaNowPlaying);
        head.Children.Add(coverBtn);

        var titles = new StackPanel { Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Bottom, Height = 38 };
        Grid.SetColumn(titles, 1);
        if (awaiting)
        {
        }
        else if (idle)
        {
            titles.Children.Add(D.T(Strings.MediaNothingPlaying, 14, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
            if (preferred is not null)
            {
                titles.Children.Add(D.T("Open " + PlaybackService.NameOf(preferred), 13, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
            }
        }
        else
        {
            titles.Children.Add(D.T(p.Display, 14, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
            titles.Children.Add(Core.Playback.CommandFailed
                ? D.T(Strings.PlaybackFailed, 13, null, D.Orange, false, TextTrimming.CharacterEllipsis)
                : D.T(p.Subtitle, 13, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
        }

        head.Children.Add(titles);

        // right cluster: toggles + source + bars
        var cluster = new StackPanel { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Right };
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Height = 28 };
        if (LyricsOn)
        {
            top.Children.Add(D.IconButton(G.Lyrics, Strings.Lyrics, () => SetExtra(MusicExtra.Lyrics), _extra == MusicExtra.Lyrics));
        }

        if (QueueOn)
        {
            var q = D.IconButton(G.List, Strings.Queue, () => SetExtra(MusicExtra.Queue), _extra == MusicExtra.Queue);
            q.Margin = new Thickness(6, 0, 0, 0);
            top.Children.Add(q);
        }

        cluster.Children.Add(top);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 6, 0, 0) };
        var sources = Core.Playback.Sources;
        if (sources.Count > 1 || Core.Playback.ChosenSource is not null)
        {
            bottom.Children.Add(BuildSourceMenu(sources, p));
        }

        var bars = new EqBars(4, 2.5, 15, D.White(0.5)) { Playing = p.IsPlaying, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        bottom.Children.Add(bars);
        cluster.Children.Add(bottom);
        Grid.SetColumn(cluster, 2);
        head.Children.Add(cluster);
        return head;
    }

    private FrameworkElement BuildSourceMenu(IReadOnlyList<PlayerSource> sources, Playback p)
    {
        HoverButton? btn = null;
        var current = sources.FirstOrDefault(s => s.Aumid == Core.Playback.EffectiveSource);
        btn = new HoverButton(() => IslandMenu.Show(btn!, SourceItems(sources), () => HasWindow ? Window.HoldOpen() : null)) { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Lifts = false };
        if (current?.Icon is { } ic)
        {
            btn.Child = new Image { Source = ic, Width = 18, Height = 18 };
        }
        else
        {
            btn.Child = D.I(G.App, 13, D.Secondary);
        }

        D.Tip(btn, $"{Strings.PlaybackSource}: {current?.Name ?? Strings.AutomaticSource}");
        return btn;
    }

    private IEnumerable<IslandMenu.Item> SourceItems(IReadOnlyList<PlayerSource> sources)
    {
        yield return new IslandMenu.Item(Strings.AutomaticSource, () => Core.Playback.Choose(null), Core.Playback.ChosenSource is null);
        yield return IslandMenu.Sep();
        foreach (var s in sources)
        {
            var id = s.Aumid;
            yield return new IslandMenu.Item(s.Name, () => Core.Playback.Choose(id), Core.Playback.EffectiveSource == id);
        }
    }

    private FrameworkElement BuildTransport(Playback p, bool idle, string? preferred)
    {
        var grid = new Grid { Height = 44 };
        for (var i = 0; i < 5; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        HoverButton Btn(int slot, string glyph, double size, Action click, bool enabled = true, string? tip = null, bool visible = true, Brush? tint = null, bool plate = false)
        {
            var b = new HoverButton(click) { Width = 44, Height = 44, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center };
            var ic = D.I(glyph, size, tint ?? D.Text);
            ic.FontWeight = FontWeights.SemiBold;
            if (plate)
            {
                var circle = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = D.White(0.12), Child = ic };
                b.Child = circle;
            }
            else
            {
                b.Child = ic;
            }

            b.IsEnabled = enabled;
            b.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            if (tip is not null)
            {
                D.Tip(b, tip);
            }

            Grid.SetColumn(b, slot);
            grid.Children.Add(b);
            return b;
        }

        if (idle)
        {
            if (preferred is not null)
            {
                Action open = () => PlaybackService.OpenPlayer(preferred);
                var tip = "Open " + PlaybackService.NameOf(preferred);
                Btn(1, G.Previous, 19, open, tip: tip);
                Btn(2, G.Play, 28, open, tip: tip);
                Btn(3, G.Next, 19, open, tip: tip);
            }
            else
            {
                var hint = D.T(Strings.MusicHint, 12, null, D.Secondary, true);
                hint.TextAlignment = TextAlignment.Center;
                hint.MaxHeight = 32;
                hint.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumnSpan(hint, 5);
                grid.Children.Add(hint);
            }
        }
        else
        {
            var accent = p.Tint is { } t ? new SolidColorBrush(t) : Brushes.White;
            if (p.CanShuffle)
            {
                Btn(0, G.Shuffle, 16, Core.Playback.ToggleShuffle, true, Strings.Shuffle, true, p.Shuffle == true ? accent : D.White(0.55), plate: p.Shuffle == true);
            }

            Btn(1, G.Previous, 19, Core.Playback.Previous, p.CanPrevious, Strings.Previous, p.CanPrevious || true);
            var play = Btn(2, p.IsPlaying ? G.Pause : G.Play, 28, Core.Playback.TogglePlayPause, p.CanPlayPause || true, Strings.PlayPause);
            _ = play;
            Btn(3, G.Next, 19, Core.Playback.Next, p.CanNext, Strings.Next);
        }

        if (Core.Ctx.Runtime.IsAvailable("mixer") || Core.Audio.Available)
        {
            var outputs = Core.Audio.Outputs();
            var def = outputs.FirstOrDefault(o => o.IsDefault);
            HoverButton? ob = null;
            ob = Btn(4, AudioService.GlyphFor(def), 16, () => IslandMenu.Show(ob!, OutputItems(outputs), () => HasWindow ? Window.HoldOpen() : null), true, def?.Name ?? Strings.OutputUnavailable, true, D.White(0.55));
        }

        return grid;
    }

    private IEnumerable<IslandMenu.Item> OutputItems(List<AudioEndpointInfo> outputs)
    {
        foreach (var o in outputs)
        {
            var id = o.Id;
            yield return new IslandMenu.Item(o.Name, () => Core.Audio.SetDefaultOutput(id), o.IsDefault);
        }

        if (Core.ModuleVisible("mixer"))
        {
            yield return IslandMenu.Sep();
            yield return new IslandMenu.Item(Strings.VolumeMixer, () => { if (HasWindow) { Window.ShowModule("mixer"); } }, Glyph: G.Mixer);
        }
    }

    private FrameworkElement BuildQueue(string? preferred)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Height = 24 };
        header.Children.Add(D.T(Strings.Queue, 13, FontWeights.SemiBold, D.Text));
        var refresh = D.IconButton(G.Refresh, Strings.Refresh, () => { }, size: 24, glyphSize: 11);
        refresh.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(refresh);
        grid.Children.Add(header);
        var empty = D.EmptyView(G.List, Strings.QueueUnavailable, D.PillButton(Strings.OpenPlayer, () => PlaybackService.OpenPlayer(preferred ?? Core.Playback.Current.Aumid), true));
        Grid.SetRow(empty, 1);
        grid.Children.Add(empty);
        return grid;
    }
}

/// <summary>The lyrics area (<c>NotchLyricsView</c>): synced list, plain text or the empty / loading / failed states.</summary>
internal sealed class LyricsView : Grid
{
    private readonly IslandCore _core;
    private readonly Func<IDisposable?> _hold;
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private ScrollViewer? _scroll;
    private readonly List<TextBlock> _lines = [];
    private int _active = -2;

    public LyricsView(IslandCore core, Func<IDisposable?> hold)
    {
        _core = core;
        _hold = hold;
        Loaded += (_, _) =>
        {
            Refresh();
            _tick.Start();
        };
        Unloaded += (_, _) => _tick.Stop();
        _tick.Tick += (_, _) => Highlight();
    }

    public void Refresh()
    {
        Children.Clear();
        _lines.Clear();
        _active = -2;
        var s = _core.Lyrics;
        var p = _core.Playback.Current;
        if (s.Instrumental)
        {
            Children.Add(D.EmptyView(G.Music, Strings.LyricsInstrumental, ImportPill()));
            return;
        }

        if (s.State == LyricsState.Ready && s.Synced && p.HasPosition)
        {
            BuildSynced();
            return;
        }

        if (s.State == LyricsState.Ready && (s.Plain.Length > 0 || s.Synced))
        {
            BuildPlain(p);
            return;
        }

        if (s.State == LyricsState.Loading)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var sp = PageUi.Spinner(16);
            sp.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(sp);
            var t = D.T(Strings.LyricsLoading, 12, null, D.Secondary);
            t.Margin = new Thickness(0, 8, 0, 8);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(t);
            stack.Children.Add(ImportPill());
            Children.Add(stack);
            return;
        }

        var failed = s.State == LyricsState.Failed;
        var online = s.Online;
        var msg = string.Join("\n", new[] { failed ? Strings.LyricsFailed : (online ? Strings.LyricsUnavailable : null), online ? null : Strings.LyricsOnlineHint }.Where(m => m is not null));
        FrameworkElement next = online
            ? D.PillButton(Strings.LyricsRetry, () => _core.Lyrics.Ensure(p, true), true)
            : D.PillButton(Strings.FindLyricsOnline, () => { _core.App.Set(K.LyricsOnline, true); _core.Lyrics.Ensure(p, true); }, true);
        var empty = D.EmptyView(failed ? G.Warning : G.Lyrics, msg.Length == 0 ? Strings.LyricsUnavailable : msg, next, ImportPill());
        Children.Add(empty);
    }

    private FrameworkElement ImportPill()
    {
        var b = D.PillButton(Strings.ImportLyrics, Import);
        b.Margin = new Thickness(6, 0, 0, 0);
        return b;
    }

    private void Import()
    {
        using var hold = _hold();
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Lyrics (*.lrc;*.txt)|*.lrc;*.txt", Title = Strings.ImportLyrics.TrimEnd('…') };
        if (dlg.ShowDialog() == true)
        {
            _core.Lyrics.Import(dlg.FileName, _core.Playback.Current.Duration);
        }
    }

    private void BuildPlain(Playback p)
    {
        var s = _core.Lyrics;
        var stack = new StackPanel();
        if (!p.HasPosition)
        {
            stack.Children.Add(D.T(Strings.LyricsNoPosition, 12, null, D.Secondary));
        }

        var text = s.Plain.Length > 0 ? s.Plain : string.Join("\n", s.Lines.Select(l => l.Text));
        var t = D.T(text, 15, FontWeights.Medium, D.Text, true);
        t.Margin = new Thickness(0, 6, 0, 6);
        stack.Children.Add(t);
        var import = new HoverButton(Import) { Height = 22, Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(D.I("", 11, D.Secondary));
        var lab = D.T(Strings.ImportLyrics, 11, null, D.Secondary);
        lab.Margin = new Thickness(5, 0, 0, 0);
        row.Children.Add(lab);
        import.Child = row;
        stack.Children.Add(import);
        Children.Add(PageUi.VScroll(stack));
    }

    private void BuildSynced()
    {
        var s = _core.Lyrics;
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var stack = new StackPanel { Margin = new Thickness(0, 60, 0, 60) };
        stack.Children.Add(D.T(Strings.LyricsWaiting, 12, null, D.Secondary));
        foreach (var l in s.Lines)
        {
            var t = D.T(string.IsNullOrWhiteSpace(l.Text) ? "♪" : l.Text, 17, FontWeights.Bold, D.White(0.35), true);
            t.Margin = new Thickness(0, 6, 0, 6);
            _lines.Add(t);
            stack.Children.Add(t);
        }

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = stack };
        g.Children.Add(_scroll);
        var timing = new Grid { Height = 18, Margin = new Thickness(0, 4, 0, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        left.Children.Add(D.I("", 10, D.Secondary));
        var tl = D.T(Strings.LyricsTiming, 11, null, D.Secondary);
        tl.Margin = new Thickness(5, 0, 0, 0);
        left.Children.Add(tl);
        timing.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var offset = D.Digits(FormatOffset(s.Offset), 11, null, D.Secondary);
        offset.MinWidth = 44;
        offset.TextAlignment = TextAlignment.Center;
        offset.VerticalAlignment = VerticalAlignment.Center;
        HoverButton Step(string text, string tip, double delta)
        {
            var b = new HoverButton(() => { s.Offset = Math.Clamp(Math.Round((s.Offset + delta) * 4) / 4, -10, 10); offset.Text = FormatOffset(s.Offset); }) { Width = 24, Height = 18, CornerRadius = new CornerRadius(6) };
            b.Child = D.T(text, 12, FontWeights.Medium, D.Secondary);
            ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
            D.Tip(b, tip);
            return b;
        }

        right.Children.Add(Step("−", Strings.LyricsEarlier, -0.25));
        var resetHolder = new HoverButton(() => { s.Offset = 0; offset.Text = FormatOffset(0); }) { CornerRadius = new CornerRadius(6), Child = offset };
        D.Tip(resetHolder, Strings.LyricsReset);
        right.Children.Add(resetHolder);
        right.Children.Add(Step("+", Strings.LyricsLater, 0.25));
        timing.Children.Add(right);
        Grid.SetRow(timing, 1);
        g.Children.Add(timing);
        Children.Add(g);
        Highlight();
    }

    private static string FormatOffset(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private void Highlight()
    {
        if (_scroll is null || _lines.Count == 0)
        {
            return;
        }

        var idx = _core.Lyrics.ActiveIndex(_core.Playback.Current.Position);
        if (idx == _active)
        {
            return;
        }

        _active = idx;
        for (var i = 0; i < _lines.Count; i++)
        {
            _lines[i].Foreground = i == idx ? D.Text : D.White(0.35);
        }

        if (idx >= 0 && idx < _lines.Count)
        {
            var pt = _lines[idx].TransformToAncestor(_scroll).Transform(new Point(0, 0));
            _scroll.ScrollToVerticalOffset(Math.Max(0, _scroll.VerticalOffset + pt.Y - (_scroll.ViewportHeight / 2) + (_lines[idx].ActualHeight / 2)));
        }
    }
}
