// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>A closed-island view: the element plus its maximum width (spec 4.3 capsule strip maxima).</summary>
internal sealed record ClosedView(FrameworkElement Element, double MaxWidth, double MinWidth = 0, bool Fill = false);

/// <summary>Base of live strips: subscribes in Loaded, unsubscribes in Unloaded.</summary>
internal abstract class LiveStrip : Grid
{
    private readonly List<Action> _undo = [];

    protected LiveStrip()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    protected abstract void Subscribe();

    protected void On(Action subscribe, Action unsubscribe)
    {
        subscribe();
        _undo.Add(unsubscribe);
    }

    private void Attach()
    {
        if (_undo.Count == 0)
        {
            Subscribe();
        }
    }

    private void Detach()
    {
        foreach (var u in _undo)
        {
            u();
        }

        _undo.Clear();
    }
}

internal static class ClosedViews
{
    private static StackPanel Row(double spacing = 6) => new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    private static FrameworkElement Pad(FrameworkElement e, double l, double r)
    {
        e.Margin = new Thickness(l, 0, r, 0);
        return e;
    }

    // ---------------------------------------------------------------- resting

    /// <summary>The resting companion's size in units for a body of this height.</summary>
    internal static double MascotSize(double body) => Math.Max(10, Math.Min(18, body - 6));

    /// <summary>How far the companion rests from the body's side when it sits beside the notch: past the flared shoulder, never more than 16 units.</summary>
    internal static double MascotInset(double body) => Math.Min(16, Math.Min(IslandSurface.Shoulder, body * 0.19) + 5);

    public static ClosedView Resting(IslandCore core, IslandWindow win)
    {
        var app = core.App;
        var body = win.StripHeight;
        var idle = app.Get<string>(K.IdleContent);
        var mascotOn = core.Mascot.Enabled && !core.Mascot.Tucked && (idle == "none" || !app.Get<bool>(K.MascotHides) || true) && idle == "none";
        var wide = win.BareWidth;
        FrameworkElement? left = null;
        FrameworkElement? right = null;
        FrameworkElement? middle = null;
        switch (idle)
        {
            case "music" when core.Playback.Current is { IsPlaying: true } p && core.ModuleVisible("music"):
            {
                var side = Math.Min(22, body - 6);
                left = Cover.For(p, side, round: true, appIcon: null);
                var bars = new EqBars(3, 2, 10, Cover.TintOf(p)) { Playing = true, VerticalAlignment = VerticalAlignment.Center };
                right = bars;
                break;
            }

            case "battery" when IslandNative.Power().HasBattery:
            {
                var (_, pct, plugged, charging) = IslandNative.Power();
                var brush = BatteryBrush(app, pct, plugged);
                left = D.I(charging ? G.BatteryCharging : G.Battery, 12, brush);
                right = D.Digits($"{pct}%", 9, FontWeights.Medium, brush);
                break;
            }

            case "screenTime":
            {
                var strip = core.TakeStrip("screenTime.resting");
                if (strip is not null)
                {
                    middle = strip;
                }

                break;
            }

            case "agents":
            {
                var strip = core.TakeStrip("agents.resting");
                if (strip is not null)
                {
                    middle = strip;
                }
                else
                {
                    left = D.I(G.Claude, 11, D.Frozen(Trayloom.Core.Ui.ClaudeMark.Orange));
                    right = D.Digits("", 9, FontWeights.Medium, D.White(0.7));
                }

                break;
            }
        }

        if (mascotOn)
        {
            middle = new MascotView(core.Mascot.Actor, MascotSize(body), core.Mascot.Look);
        }

        var grid = new Grid { Height = body };
        if (left is not null || right is not null || middle is not null)
        {
            wide = Math.Max(wide, IslandWindow.CameraWidth * body / IslandWindow.BarHeight + 88);
        }

        if (left is not null)
        {
            left.HorizontalAlignment = HorizontalAlignment.Left;
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(Math.Max(3, (body - 22) / 2), 0, 0, 0);
            grid.Children.Add(left);
        }

        if (right is not null)
        {
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 12, 0);
            grid.Children.Add(right);
        }

        if (middle is not null)
        {
            // the notch has a camera in its middle, so the companion rests beside it (the Companion tab's Left or Right) instead of on it
            var side = mascotOn ? IslandSilhouette.MascotSide(win.IsCapsule, app.Get<string>(K.MascotSide)) : 0;
            if (side != 0)
            {
                middle.HorizontalAlignment = side < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                middle.Margin = side < 0 ? new Thickness(MascotInset(body), 0, 0, 0) : new Thickness(0, 0, MascotInset(body), 0);
            }
            else
            {
                middle.HorizontalAlignment = HorizontalAlignment.Center;
            }

            middle.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(middle);
        }

        grid.Width = wide;
        return new ClosedView(grid, wide, wide, true);
    }

    public static Brush BatteryBrush(IAppSettings app, int pct, bool plugged)
    {
        if (!plugged && app.Get<bool>(K.LowBatteryTint))
        {
            var red = Math.Clamp(app.Get<int>(K.LowBatteryThreshold), 1, 99);
            if (pct <= red)
            {
                return D.Red;
            }

            if (app.Get<bool>(K.LowBatteryEarly) && pct <= Math.Max(red + 1, app.Get<int>(K.LowBatteryEarlyThreshold)))
            {
                return D.Orange;
            }
        }

        return D.White(0.9);
    }

    // ---------------------------------------------------------------- notice

    public static ClosedView NoticeView(IslandCore core, IslandWindow win, Notice n)
    {
        var body = win.StripHeight;
        if (n.Event == NoticeEvents.MicInUse)
        {
            return MicNotice(win, n);
        }

        if (Detach.From(n.Element) is { } custom)
        {
            // the same notice element is placed again by every rebuild of the closed layer while the notice lasts
            custom.VerticalAlignment = VerticalAlignment.Center;
            custom.Margin = new Thickness(Math.Max(6, (body - 14) / 2), 0, 12, 0);
            var holder = new Grid { Height = body };
            holder.Children.Add(custom);
            return new ClosedView(holder, 340, 109);
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = body };
        var tint = n.Tint is { } c ? new SolidColorBrush(c) : D.Text;
        FrameworkElement mark;
        if (n.Reaction is not null && core.Mascot.Enabled)
        {
            mark = new MascotView(core.Mascot.Actor, 18, core.Mascot.Look);
        }
        else if (n.Image is not null)
        {
            mark = new Image { Source = n.Image, Width = Math.Min(22, body - 4), Height = Math.Min(22, body - 4), Stretch = Stretch.Uniform };
        }
        else
        {
            mark = D.I(n.Glyph, 14, tint);
            mark.Width = 18;
        }

        mark.Margin = new Thickness(Math.Max(6, (body - 14) / 2), 0, 6, 0);
        mark.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(mark);
        if (n.Level is { } level)
        {
            var title = D.T(n.Title, 11, FontWeights.Medium, D.Text, false, TextTrimming.CharacterEllipsis);
            title.VerticalAlignment = VerticalAlignment.Center;
            title.MaxWidth = 110;
            if (!string.IsNullOrEmpty(n.Title))
            {
                row.Children.Add(title);
            }

            var meter = D.MeterBar(level, tint, 4);
            meter.Width = 56;
            meter.VerticalAlignment = VerticalAlignment.Center;
            meter.Margin = new Thickness(string.IsNullOrEmpty(n.Title) ? 0 : 8, 0, 8, 0);
            row.Children.Add(meter);
            var pct = D.Digits(string.IsNullOrEmpty(n.Detail) ? $"{(int)Math.Round(level * 100)}%" : n.Detail, 11, FontWeights.Medium, D.White(0.85));
            pct.VerticalAlignment = VerticalAlignment.Center;
            pct.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(pct);
        }
        else
        {
            var title = D.Digits(n.Title, 11, FontWeights.SemiBold);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.MaxWidth = 150;
            title.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(title);
            if (!string.IsNullOrEmpty(n.Detail))
            {
                var detail = D.Digits(n.Detail, 11, FontWeights.Medium, D.White(0.8));
                detail.TextTrimming = n.Event == NoticeEvents.Accessory ? TextTrimming.CharacterEllipsis : TextTrimming.CharacterEllipsis;
                detail.MaxWidth = n.Banner ? 190 : 160;
                detail.Margin = new Thickness(8, 0, 12, 0);
                detail.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(detail);
            }
            else
            {
                title.Margin = new Thickness(0, 0, 12, 0);
            }
        }

        return new ClosedView(row, n.Banner ? 380 : 340, 109);
    }

    // ---------------------------------------------------------------- music strip

    public static ClosedView MusicStrip(IslandCore core, IslandWindow win) => Dotted(core, win, new(new MusicStripView(core, win), 260, 109));

    private sealed class MusicStripView : LiveStrip
    {
        private readonly IslandCore _core;
        private readonly IslandWindow _win;
        private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly Cover _cover;
        private readonly TextBlock _title;
        private readonly Grid _barsHost = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly EqBars _bars;
        private readonly TextBlock _glyph;
        private DispatcherTimer? _naming;
        private DispatcherTimer? _hoverDelay;
        private bool _named;
        private bool _barsHover;
        private bool _coverHover;
        private bool _heldPause;

        public MusicStripView(IslandCore core, IslandWindow win)
        {
            _core = core;
            _win = win;
            var body = win.StripHeight;
            var p = core.Playback.Current;
            var side = Math.Max(10, body - 6);
            _cover = new Cover(side, true);
            _cover.Margin = new Thickness(3, 0, 0, 0);
            _cover.Set(p, core.Playback.Sources.FirstOrDefault(s => s.Aumid == p.Aumid)?.Icon);
            _title = D.T(p.Display, 12, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis);
            _title.MaxWidth = 150;
            _title.Margin = new Thickness(8, 0, 0, 0);
            _title.VerticalAlignment = VerticalAlignment.Center;
            var barH = Math.Min(16, Math.Max(6, body - 8));
            _bars = new EqBars(7, 1.8, barH, Cover.TintOf(p)) { Playing = p.IsPlaying };
            _glyph = D.I(p.IsPlaying ? G.Pause : G.Play, Math.Min(13, Math.Max(8, barH - 3)), Cover.TintOf(p));
            _glyph.FontWeight = FontWeights.SemiBold;
            _glyph.Opacity = 0;
            _barsHost.Children.Add(_bars);
            _barsHost.Children.Add(_glyph);
            _barsHost.Margin = new Thickness(8, 0, 10, 0);
            _barsHost.Background = Brushes.Transparent;
            _row.Children.Add(_cover);
            _row.Children.Add(_title);
            _row.Children.Add(_barsHost);
            _title.Visibility = Visibility.Collapsed;
            Children.Add(_row);
            Height = body;
            _cover.MouseEnter += (_, _) => { _coverHover = true; _hoverDelay?.Stop(); _hoverDelay = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(300) }; _hoverDelay.Tick += (_, _) => { _hoverDelay.Stop(); if (_coverHover) { SetNamed(true); } }; _hoverDelay.Start(); };
            _cover.MouseLeave += (_, _) => { _coverHover = false; _hoverDelay?.Stop(); if (!_namedByTrack) { SetNamed(false); } };
            _barsHost.MouseEnter += (_, _) => { _barsHover = true; Fade(_bars, 0.0); Fade(_glyph, 1); };
            _barsHost.MouseLeave += (_, _) => { _barsHover = false; _heldPause = false; Fade(_bars, 1); Fade(_glyph, 0); };
            _barsHost.MouseLeftButtonUp += (_, e) =>
            {
                _core.Playback.TogglePlayPause();
                var willPlay = !_core.Playback.Current.IsPlaying;
                _glyph.Text = willPlay ? G.Pause : G.Play;
                _heldPause = !willPlay;
                e.Handled = true;
            };
            D.Tip(this, p.Display);
            if ((DateTime.UtcNow - win.TrackSince).TotalSeconds < 4)
            {
                _namedByTrack = true;
                SetNamed(true);
                var left = 4 - (DateTime.UtcNow - win.TrackSince).TotalSeconds;
                _naming = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(Math.Max(0.2, left)) };
                _naming.Tick += (_, _) => { _naming.Stop(); _namedByTrack = false; if (!_coverHover) { SetNamed(false); } };
                _naming.Start();
            }
        }

        private bool _namedByTrack;

        private static void Fade(UIElement e, double to) => e.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(150)));

        private void SetNamed(bool named)
        {
            if (_named == named)
            {
                return;
            }

            _named = named;
            _title.Visibility = named ? Visibility.Visible : Visibility.Collapsed;
            _win.ReMeasureClosed();
        }

        protected override void Subscribe()
        {
            On(() => _core.Playback.Changed += OnChanged, () => _core.Playback.Changed -= OnChanged);
        }

        private void OnChanged()
        {
            var p = _core.Playback.Current;
            _bars.Playing = p.IsPlaying;
            _bars.Tint = Cover.TintOf(p);
            if (!_barsHover)
            {
                _glyph.Text = p.IsPlaying ? G.Pause : G.Play;
            }

            _title.Text = p.Display;
            if (p.Artwork is not null)
            {
                _cover.Set(p);
            }
        }
    }

    // ---------------------------------------------------------------- timer strip

    public static ClosedView TimerStrip(IslandCore core, IslandWindow win) => Dotted(core, win, new(new TimerStripView(core, win), 220, 109));

    private sealed class TimerStripView : LiveStrip
    {
        private readonly IslandCore _core;
        private readonly IslandWindow _win;
        private readonly TextBlock _mark;
        private readonly TextBlock _reading;
        private string _lastShape = "";

        public TimerStripView(IslandCore core, IslandWindow win)
        {
            _core = core;
            _win = win;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _mark = D.I(MarkGlyph(), Math.Min(20, win.StripHeight - 10), D.Orange);
            _mark.Margin = new Thickness(Math.Max(6, (win.StripHeight - 14) / 2), 0, 6, 0);
            _reading = D.Digits(core.Timer.CompactText, 13, FontWeights.SemiBold, D.Orange);
            _reading.Margin = new Thickness(0, 0, 12, 0);
            _reading.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(_mark);
            row.Children.Add(_reading);
            Children.Add(row);
            Height = win.StripHeight;
            D.Tip(this, core.Timer.PhaseName);
        }

        private string MarkGlyph() => _core.Timer.Completed ? G.Check : (_core.Timer.Paused ? G.Pause : (_core.Timer.IsStopwatch ? G.Stopwatch : G.Timer));

        protected override void Subscribe()
        {
            On(() => _core.Timer.Ticked += Update, () => _core.Timer.Ticked -= Update);
            On(() => _core.Timer.Changed += Update, () => _core.Timer.Changed -= Update);
        }

        private void Update()
        {
            _mark.Text = MarkGlyph();
            _reading.Text = _core.Timer.Completed ? "00:00" : _core.Timer.CompactText;
            if (_core.Timer.Completed)
            {
                _reading.Text = "✓";
            }

            if (_lastShape.Length != _reading.Text.Length)
            {
                _lastShape = _reading.Text;
                _win.ReMeasureClosed();
            }
        }
    }

    // ---------------------------------------------------------------- downloads strip

    public static ClosedView DownloadStrip(IslandCore core, IslandWindow win) => Dotted(core, win, new(new DownloadStripView(core, win), 280, 109));

    private sealed class DownloadStripView : LiveStrip
    {
        private readonly IslandCore _core;
        private readonly TextBlock _name;
        private readonly Meter _meter;
        private readonly TextBlock _pct;
        private readonly Border _ring;

        public DownloadStripView(IslandCore core, IslandWindow win)
        {
            _core = core;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var icon = D.I(G.Download, Math.Min(17, win.StripHeight - 6), D.Text);
            icon.Margin = new Thickness(Math.Max(6, (win.StripHeight - 14) / 2), 0, 6, 0);
            _name = D.T("", 12, FontWeights.Medium, D.Text, false, TextTrimming.CharacterEllipsis);
            _name.MaxWidth = 110;
            _name.VerticalAlignment = VerticalAlignment.Center;
            _meter = D.MeterBar(0, D.Text, 4);
            _meter.Width = 36;
            _meter.VerticalAlignment = VerticalAlignment.Center;
            _meter.Margin = new Thickness(8, 0, 6, 0);
            _pct = D.Digits("", 10, FontWeights.Medium);
            _pct.Margin = new Thickness(0, 0, 12, 0);
            _pct.VerticalAlignment = VerticalAlignment.Center;
            _ring = new Border { Width = 12, Height = 12, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(6), BorderBrush = D.White(0.7), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(icon);
            row.Children.Add(_name);
            row.Children.Add(_meter);
            row.Children.Add(_pct);
            row.Children.Add(_ring);
            Children.Add(row);
            Height = win.StripHeight;
            Update();
        }

        protected override void Subscribe() => On(() => _core.Downloads.Changed += Update, () => _core.Downloads.Changed -= Update);

        private void Update()
        {
            var item = _core.Downloads.ActiveItem;
            _name.Text = item?.Name ?? Loc.T("Downloads");
            if (item?.Fraction is { } f)
            {
                _meter.Visibility = Visibility.Visible;
                _meter.Value = f;
                _pct.Text = $"{(int)Math.Round(f * 100)}%";
                _pct.Visibility = Visibility.Visible;
                _ring.Visibility = Visibility.Collapsed;
            }
            else
            {
                _meter.Visibility = Visibility.Collapsed;
                _pct.Visibility = Visibility.Collapsed;
                _ring.Visibility = Visibility.Visible;
            }
        }
    }

    // ---------------------------------------------------------------- watch strip

    public static ClosedView WatchStrip(IslandCore core, IslandWindow win) => Dotted(core, win, new(new WatchStripView(core, win), 220, 109));

    private sealed class WatchStripView : LiveStrip
    {
        private readonly IslandCore _core;
        private readonly TextBlock _eye;
        private readonly TextBlock _reading;
        private readonly Image _thumb;

        public WatchStripView(IslandCore core, IslandWindow win)
        {
            _core = core;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _eye = D.I(G.Eye, 13, D.Frozen(Color.FromArgb(191, 0xBF, 0x5A, 0xF2)));
            _eye.Margin = new Thickness(Math.Max(6, (win.StripHeight - 14) / 2), 0, 6, 0);
            _reading = D.Digits("", 11, FontWeights.Medium, D.Frozen(Color.FromRgb(0xBF, 0x5A, 0xF2)));
            _reading.MaxWidth = 130;
            _reading.TextTrimming = TextTrimming.CharacterEllipsis;
            _reading.Margin = new Thickness(0, 0, 12, 0);
            _reading.VerticalAlignment = VerticalAlignment.Center;
            _thumb = new Image { Height = Math.Max(8, win.StripHeight - 6), MaxWidth = 40, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(_eye);
            row.Children.Add(_reading);
            row.Children.Add(_thumb);
            Children.Add(row);
            Height = win.StripHeight;
            Update();
        }

        protected override void Subscribe()
        {
            On(() => _core.Watch.Changed += Update, () => _core.Watch.Changed -= Update);
            On(() => DecorativeClock.Subscribe(Breathe), () => DecorativeClock.Unsubscribe(Breathe));
        }

        private void Breathe()
        {
            if (_core.Watch.State == WatchState.Hidden || D.ReduceMotion)
            {
                _eye.Opacity = 1;
                return;
            }

            var b = (1 - Math.Cos(2 * Math.PI * DecorativeClock.Now / 1.6)) / 2;
            _eye.Opacity = 0.7 + (0.3 * b);
        }

        private void Update()
        {
            var w = _core.Watch;
            _eye.Text = w.State == WatchState.Hidden ? G.EyeOff : G.Eye;
            var noText = w.HasReading && string.IsNullOrWhiteSpace(w.FullText);
            _thumb.Visibility = noText ? Visibility.Visible : Visibility.Collapsed;
            _thumb.Source = noText ? w.Picture : null;
            _reading.Visibility = noText || w.State == WatchState.Hidden ? Visibility.Collapsed : Visibility.Visible;
            _reading.Text = w.HasReading ? w.Headline : "…";
        }
    }

    // ---------------------------------------------------------------- calendar strip

    public static ClosedView CalendarStrip(IslandCore core, IslandWindow win) => Dotted(core, win, new(new CalendarStripView(core, win), 300, 109));

    private sealed class CalendarStripView : LiveStrip
    {
        private readonly IslandCore _core;
        private readonly IslandWindow _win;
        private readonly Ellipse_ _dot = new();
        private readonly TextBlock _title;
        private readonly TextBlock _clock;
        private readonly TextBlock _time;
        private readonly DispatcherTimer _tick;
        private string _shape = "";

        public CalendarStripView(IslandCore core, IslandWindow win)
        {
            _core = core;
            _win = win;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _dot = new Ellipse_ { Width = 6, Height = 6, Margin = new Thickness(Math.Max(8, (win.StripHeight - 6) / 2), 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            _title = D.T("", 11, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis);
            _title.MaxWidth = 120;
            _title.VerticalAlignment = VerticalAlignment.Center;
            _clock = D.Digits("", 13, FontWeights.Medium, D.Text);
            _clock.Margin = new Thickness(8, 0, 0, 0);
            _clock.VerticalAlignment = VerticalAlignment.Center;
            _time = D.Digits("", 11, FontWeights.Medium, D.White(0.55));
            _time.Margin = new Thickness(6, 0, 12, 0);
            _time.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(_dot);
            row.Children.Add(_title);
            row.Children.Add(_clock);
            row.Children.Add(_time);
            Children.Add(row);
            Height = win.StripHeight;
            _tick = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (_, _) => Update();
            Update();
        }

        protected override void Subscribe()
        {
            _tick.Start();
            On(() => { }, () => _tick.Stop());
        }

        private void Update()
        {
            var now = DateTime.Now;
            if (_core.Calendar.CountdownTarget(now) is not { } t)
            {
                _win.RefreshClosed();
                return;
            }

            var (e, ongoing, boundary) = t;
            var remain = boundary - now;
            var total = (int)Math.Max(0, Math.Ceiling(remain.TotalSeconds));
            _dot.Fill = new SolidColorBrush(e.Color);
            _title.Text = e.Title.Length == 0 ? Loc.T("Untitled event") : e.Title;
            _clock.Text = $"{total / 60}:{total % 60:00}";
            _clock.Foreground = ongoing ? D.Mint : D.Text;
            _time.Text = ongoing ? $"→ {e.End:t}" : $"· {e.Start:t}";
            var shape = $"{_clock.Text.Length}{ongoing}";
            if (shape != _shape)
            {
                _shape = shape;
                _win.ReMeasureClosed();
            }
        }
    }

    // ---------------------------------------------------------------- keep awake strip

    public static ClosedView KeepAwakeStrip(IslandCore core, IslandWindow win)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = win.StripHeight };
        var icon = D.I(G.Cup, Math.Min(20, win.StripHeight - 10), D.Yellow);
        icon.Margin = new Thickness(Math.Max(6, (win.StripHeight - 14) / 2), 0, 6, 0);
        var ka = core.KeepAwake;
        string text;
        if (ka?.EndsAt is { } end)
        {
            var mins = Math.Max(1, (int)Math.Ceiling((end - DateTime.Now).TotalMinutes));
            text = mins >= 60 ? $"{mins / 60}h{mins % 60:00}" : $"{mins}m";
        }
        else
        {
            text = "∞";
        }

        var reading = D.Digits(text, 13, FontWeights.SemiBold, D.Yellow);
        reading.Margin = new Thickness(0, 0, 12, 0);
        reading.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(icon);
        row.Children.Add(reading);
        D.Tip(row, Loc.T("Keep awake"));
        return Dotted(core, win, new ClosedView(row, 220, 109));
    }

    // ---------------------------------------------------------------- external agents strip

    public static ClosedView? ExternalStrip(IslandCore core, IslandWindow win, string id)
    {
        if (id == IslandCore.ActMicrophone)
        {
            return MicStrip(core, win);
        }

        var el = core.TakeStrip(id);
        if (el is null)
        {
            return null;
        }

        el.VerticalAlignment = VerticalAlignment.Center;
        el.Margin = new Thickness(Math.Max(6, (win.StripHeight - 14) / 2), 0, 12, 0);
        var host = new Grid { Height = win.StripHeight };
        host.Children.Add(el);
        return Dotted(core, win, new ClosedView(host, 220, 109));
    }

    // ---------------------------------------------------------------- microphone in use

    /// <summary>
    /// While an app uses the microphone and another activity is the one on show, a small pulsing orange dot sits at the strip's edge
    /// (the privacy indicator is never hidden, whatever the island shows).
    /// </summary>
    private static ClosedView Dotted(IslandCore core, IslandWindow win, ClosedView view)
    {
        if (!core.MicInUse)
        {
            return view;
        }

        var host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = win.StripHeight };
        view.Element.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(view.Element);
        var dot = new MicPulse(6, null) { Margin = new Thickness(-4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        D.Tip(dot, MicTip(core));
        host.Children.Add(dot);
        return view with { Element = host, MaxWidth = view.MaxWidth + 16 };
    }

    private static string MicTip(IslandCore core) =>
        Loc.T("Microphone in use") + " · " + string.Join(", ", core.MicUse.Users.Select(u => u.Name));

    private static FrameworkElement? AppIcon(ImageSource? icon, double size) =>
        icon is null ? null : new Image { Source = icon, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };

    /// <summary>The closed-island activity: orange pulsing microphone, the app (icon and name), "+N" when several apps listen.</summary>
    public static ClosedView MicStrip(IslandCore core, IslandWindow win)
    {
        var body = win.StripHeight;
        var users = core.MicUse.Users;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = body };
        var glyph = Math.Min(16, Math.Max(10, body - 12));
        var pulse = new MicPulse(glyph, G.Mic) { Margin = new Thickness(Math.Max(6, (body - glyph - 8) / 2), 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(pulse);
        var iconSize = Math.Min(16, Math.Max(12, body - 10));
        if (users.Count > 0)
        {
            var first = users[0];
            if (AppIcon(first.Icon, iconSize) is { } icon)
            {
                icon.Margin = new Thickness(4, 0, 0, 0);
                row.Children.Add(icon);
            }

            var name = D.T(first.Name, 11, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis);
            name.MaxWidth = users.Count > 1 ? 72 : 96;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(first.Icon is null ? 4 : 6, 0, users.Count > 1 ? 0 : 12, 0);
            row.Children.Add(name);
            if (users.Count > 1)
            {
                var more = D.Digits($"+{users.Count - 1}", 11, FontWeights.SemiBold, D.Orange);
                more.VerticalAlignment = VerticalAlignment.Center;
                more.Margin = new Thickness(5, 0, 12, 0);
                row.Children.Add(more);
            }
        }
        else
        {
            pulse.Margin = new Thickness(pulse.Margin.Left, 0, 12, 0);
        }

        D.Tip(row, MicTip(core));
        return new ClosedView(row, 220, 109);
    }

    /// <summary>"Claude is using the microphone": orange microphone, the app's icon, the sentence.</summary>
    private static ClosedView MicNotice(IslandWindow win, Notice n)
    {
        var body = win.StripHeight;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = body };
        var mark = D.I(G.Mic, 14, D.Orange);
        mark.Margin = new Thickness(Math.Max(6, (body - 14) / 2), 0, 6, 0);
        mark.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(mark);
        var iconSize = Math.Min(16, Math.Max(12, body - 10));
        if (AppIcon(n.Image, iconSize) is { } icon)
        {
            icon.Margin = new Thickness(2, 0, 6, 0);
            row.Children.Add(icon);
        }

        var title = D.Digits(n.Title, 11, FontWeights.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MaxWidth = 230;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(n.Image is null ? 4 : 0, 0, 12, 0);
        row.Children.Add(title);
        return new ClosedView(row, 340, 109);
    }
}

/// <summary>
/// The microphone mark: a glyph (or a plain dot) in the iOS microphone orange with a soft sonar pulse behind it, on the shared
/// 30 Hz decorative clock; still with Reduce Motion. Sizes are island layout units, so the island zoom scales it.
/// </summary>
internal sealed class MicPulse : Grid
{
    public const double Period = 1.8;

    private readonly Border _halo;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly FrameworkElement _core;
    private bool _subscribed;

    public MicPulse(double size, string? glyph)
    {
        var box = size + (glyph is null ? 6 : 8);
        Width = box;
        Height = box;
        _halo = new Border { Width = box, Height = box, CornerRadius = new CornerRadius(box / 2), Background = D.Frozen(Color.FromArgb(96, 0xFF, 0x9F, 0x0A)), RenderTransform = _scale, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0, IsHitTestVisible = false };
        Children.Add(_halo);
        if (glyph is null)
        {
            _core = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = D.Orange, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            _core = D.I(glyph, size, D.Orange);
            _core.HorizontalAlignment = HorizontalAlignment.Center;
        }

        Children.Add(_core);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        if (_subscribed || D.ReduceMotion)
        {
            return;
        }

        _subscribed = true;
        DecorativeClock.Subscribe(Tick);
        Tick();
    }

    private void Detach()
    {
        if (_subscribed)
        {
            _subscribed = false;
            DecorativeClock.Unsubscribe(Tick);
        }
    }

    private void Tick()
    {
        var p = DecorativeClock.Now % Period / Period;
        var ease = 1 - Math.Pow(1 - p, 2);
        var s = 0.75 + (0.65 * ease);
        _scale.ScaleX = s;
        _scale.ScaleY = s;
        _halo.Opacity = 0.8 * Math.Pow(1 - p, 1.4);
        _core.Opacity = 0.86 + (0.14 * ((1 - Math.Cos(2 * Math.PI * p)) / 2));
    }
}

/// <summary>Plain ellipse (avoids the System.Windows.Shapes namespace clash in this file).</summary>
internal sealed class Ellipse_ : FrameworkElement
{
    private Brush? _fill;

    public Brush? Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawEllipse(Fill ?? Brushes.White, new Pen(D.White(0.5), 0.5), new Point(Width / 2, Height / 2), Width / 2, Height / 2);
    }
}
