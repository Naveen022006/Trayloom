// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>The minute ruler (<c>NotchTimerRuler</c>, 1...180): drag, click, wheel and keys.</summary>
internal sealed class TimerRuler : FrameworkElement
{
    private const double Spacing = 14;
    private const int Max = 180;
    private int _value;
    private bool _dragging;
    private double _dragX;
    private double _dragValue;
    private bool _moved;
    private readonly string _label;
    private readonly Typeface _tf = new(D.Font, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    public TimerRuler(string label, int value)
    {
        _label = label;
        _value = Math.Clamp(value, 1, Max);
        Focusable = true;
        Height = 82;
        Cursor = Cursors.SizeWE;
        Background = Brushes.Transparent;
        FocusVisualStyle = null;
    }

    public Brush? Background { get; set; }

    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, 1, Max);
            if (v == _value)
            {
                return;
            }

            _value = v;
            InvalidateVisual();
            ValueChanged?.Invoke(v);
        }
    }

    public event Action<int>? ValueChanged;

    private double SelX(double w)
    {
        var inset = Math.Min(24, w / 2);
        var strip = Max * Spacing;
        if (strip < w - (2 * inset))
        {
            return w / 2;
        }

        var x = inset + ((_value - 1) * Spacing);
        var hi = w - inset - ((Max - _value) * Spacing);
        return Math.Min(Math.Max(w / 2, hi), x);
    }

    private static string LabelFor(int m) => m >= 60 ? $"{m / 60}h{m % 60:00}" : m.ToString(CultureInfo.InvariantCulture);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var s = ActualHeight / 82.0;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight));
        var sel = SelX(w);
        var fade = w * 0.12;
        var opacity = IsEnabled ? 1 : 0.4;
        for (var m = 1; m <= Max; m++)
        {
            var x = sel + ((m - _value) * Spacing);
            if (x < -8 || x > w + 8)
            {
                continue;
            }

            var edge = Math.Min(x, w - x);
            var f = m == _value ? 1 : Math.Clamp(edge / fade, 0, 1);
            var a = (m <= _value ? 1.0 : 0.35) * f * opacity;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(a * 255), 0xFF, 0x9F, 0x0A));
            dc.DrawRoundedRectangle(brush, null, new Rect(x - 2, 26 * s, 4, 38 * s), 2, 2);
            if (m % 5 == 0 || m == 1)
            {
                var ft = new FormattedText(LabelFor(m), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _tf, 14, brush, 1.25);
                dc.DrawText(ft, new Point(x - (ft.Width / 2), 0));
            }
        }

        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            ctx.BeginFigure(new Point(sel, 70 * s), true, true);
            ctx.LineTo(new Point(sel + 6, 80 * s), true, false);
            ctx.LineTo(new Point(sel - 6, 80 * s), true, false);
        }

        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0xFF, 0x9F, 0x0A)), null, tri);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        _dragging = true;
        _moved = false;
        _dragX = e.GetPosition(this).X;
        _dragValue = _value;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var dx = e.GetPosition(this).X - _dragX;
        if (Math.Abs(dx) > 3)
        {
            _moved = true;
        }

        if (_moved)
        {
            Value = (int)Math.Round(_dragValue - (dx / Spacing));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging && !_moved)
        {
            var x = e.GetPosition(this).X;
            Value = _value + (int)Math.Round((x - SelX(ActualWidth)) / Spacing);
        }

        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Value = _value + ((e.Delta > 0 ? 1 : -1) * Math.Max(1, Math.Abs(e.Delta) / 120));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left or Key.Down:
                Value = _value - 1;
                break;
            case Key.Right or Key.Up:
                Value = _value + 1;
                break;
            case Key.Home:
                Value = 1;
                break;
            case Key.End:
                Value = Max;
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}

internal sealed class TimerPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private TextBlock? _clock;
    private TimerMode _mode;

    public TimerPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
        _mode = Enum.TryParse<TimerMode>(core.App.Get<string>(K.TimerMode), true, out var m) ? m : TimerMode.Timer;
        BuildBand();
    }

    public override string ModuleId => "timer";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Leading => _leading;

    public override FrameworkElement? Trailing => _trailing;

    public override double? NaturalWidth => 344;

    public override double Height(double width, double budget)
    {
        BuildBody(Math.Min(width, 344), budget);
        var t = Core.Timer;
        var h = 82.0;
        if (!t.HasSession && _mode == TimerMode.Pomodoro)
        {
            h = Math.Min(82, Math.Max(56, budget - 36)) + 8 + 28;
        }

        return Math.Min(h, budget);
    }

    public override void Shown()
    {
        Core.Timer.Changed += OnChanged;
        Core.Timer.Ticked += OnTick;
    }

    public override void Hidden()
    {
        Core.Timer.Changed -= OnChanged;
        Core.Timer.Ticked -= OnTick;
    }

    private void OnChanged() => _view.Dispatcher.BeginInvoke(() =>
    {
        if (HasWindow)
        {
            Window.ReopenPage();
        }
    });

    private void OnTick()
    {
        if (_clock is not null && Core.Timer.HasSession)
        {
            _clock.Text = Core.Timer.ClockText;
        }
    }

    private static Brush OrangeFill => D.Frozen(Color.FromArgb(46, 0xFF, 0x9F, 0x0A));

    private void BuildBand()
    {
        _leading.Children.Clear();
        _trailing.Children.Clear();
        var t = Core.Timer;
        if (!t.HasSession)
        {
            var modes = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (mode, title) in new[] { (TimerMode.Timer, Loc.T("Timer")), (TimerMode.Pomodoro, Loc.T("Pomodoro")), (TimerMode.Stopwatch, Loc.T("Stopwatch")) })
            {
                var m = mode;
                var b = new HoverButton(() => { _mode = m; Core.App.Set(K.TimerMode, m.ToString().ToLowerInvariant()); BuildBand(); if (HasWindow) { Window.ReopenPage(); } }) { Height = 24, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 10, 0), Lifts = false, CornerRadius = new CornerRadius(4) };
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                col.Children.Add(D.T(title, 12, FontWeights.Medium, m == _mode ? D.Text : D.White(0.5)));
                var line = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = m == _mode ? D.Orange : Brushes.Transparent, Margin = new Thickness(0, 2, 0, 0) };
                col.Children.Add(line);
                b.Child = col;
                modes.Children.Add(b);
            }

            _leading.Children.Add(modes);
            var start = new HoverButton(() => { Core.Timer.Start(_mode); }) { Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 0, 10, 0), Fill = OrangeFill };
            var startLabel = D.T(Loc.T("Start"), 12, FontWeights.SemiBold, D.Orange);
            startLabel.VerticalAlignment = VerticalAlignment.Center;
            start.Child = startLabel;
            D.Tip(start, _mode == TimerMode.Pomodoro ? Loc.T("Start each phase when you are ready. The cycle ends after the last focus session.") : Loc.T("Start"));
            _trailing.Children.Add(start);
        }
        else
        {
            var headline = t.CycleFinished ? Loc.T("Pomodoro complete") : (t.Completed ? Loc.T("Time is up") : t.PhaseName);
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(D.T(headline, 13, FontWeights.SemiBold, D.Orange));
            if (t.Mode == TimerMode.Pomodoro && !t.CycleFinished)
            {
                col.Children.Add(D.T(Loc.F("Session {0} of {1}", t.SessionNumber, t.TotalSessions), 11, null, D.Secondary));
            }

            _leading.Children.Add(col);
            var done = t.Completed;
            var x = D.IconButton(G.Close, done ? Loc.T("Done") : Loc.T("Cancel"), () => Core.Timer.Cancel(), size: 28, glyphSize: 12);
            _trailing.Children.Add(x);
        }
    }

    private void BuildBody(double width, double budget)
    {
        BuildBand();
        _view.Children.Clear();
        _clock = null;
        var t = Core.Timer;
        var root = new StackPanel { Width = width, HorizontalAlignment = HorizontalAlignment.Center };
        if (t.HasSession)
        {
            var row = new Grid { Height = 82 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string? glyph = null;
            string? tip = null;
            Action? click = null;
            if (t.Completed)
            {
                if (t.Mode == TimerMode.Pomodoro && !t.CycleFinished)
                {
                    glyph = G.Play;
                    tip = "Start " + TimerService.PhaseTitle(t.NextPhase);
                    click = t.StartNextPhase;
                }
            }
            else
            {
                glyph = t.Running ? G.Pause : G.Play;
                tip = t.Running ? Loc.T("Pause") : Loc.T("Resume");
                click = t.TogglePause;
            }

            if (glyph is not null)
            {
                var b = new HoverButton(click!) { Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Fill = D.Frozen(Color.FromArgb(71, 0xFF, 0x9F, 0x0A)), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
                var ic = D.I(glyph, 23, D.Orange);
                ic.FontWeight = FontWeights.SemiBold;
                b.Child = ic;
                D.Tip(b, tip!);
                row.Children.Add(b);
            }

            _clock = new TextBlock { Text = t.Completed ? (t.IsStopwatch ? t.ClockText : "00:00") : t.ClockText, FontSize = 62, FontFamily = new FontFamily("Segoe UI Variable Display Light, Segoe UI Light, Segoe UI"), FontWeight = FontWeights.Light, Foreground = D.Orange, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(_clock, FontNumeralAlignment.Tabular);
            var vb = new Viewbox { StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, Child = _clock };
            Grid.SetColumn(vb, 1);
            row.Children.Add(vb);
            root.Children.Add(row);
        }
        else
        {
            var rulerH = _mode == TimerMode.Pomodoro ? Math.Min(82, Math.Max(56, budget - 36)) : 82;
            var row = new Grid { Height = rulerH };
            if (_mode == TimerMode.Stopwatch)
            {
                row.Children.Add(Clock("00:00"));
            }
            else
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var key = _mode == TimerMode.Pomodoro ? K.PomodoroFocus : K.TimerMinutes;
                var ruler = new TimerRuler(_mode == TimerMode.Pomodoro ? Loc.T("Focus") : Loc.T("Timer"), Core.App.Get<int>(key)) { Height = rulerH, Margin = new Thickness(0, 0, 12, 0) };
                var clock = Clock(MinutesText(Core.App.Get<int>(key)));
                ruler.ValueChanged += v =>
                {
                    Core.App.Set(key, v);
                    ((TextBlock)((Viewbox)clock).Child).Text = MinutesText(v);
                };
                Grid.SetColumn(clock, 1);
                row.Children.Add(ruler);
                row.Children.Add(clock);
            }

            root.Children.Add(row);
            if (_mode == TimerMode.Pomodoro)
            {
                root.Children.Add(BuildReadouts(width));
            }
        }

        _view.Children.Add(root);
    }

    private static string MinutesText(int m) => m >= 60 ? $"{m / 60}:{m % 60:00}:00" : $"{m:00}:00";

    private static FrameworkElement Clock(string text)
    {
        var tb = new TextBlock { Text = text, FontSize = 26, FontFamily = new FontFamily("Segoe UI Variable Display Light, Segoe UI Light, Segoe UI"), FontWeight = FontWeights.Light, Foreground = D.Orange, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Documents.Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
        return new Viewbox { StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, Child = tb };
    }

    private FrameworkElement BuildReadouts(double width)
    {
        var row = new Grid { Height = 28, Margin = new Thickness(0, 8, 0, 0) };
        var defs = new (string Name, string Key, int Min, int Max, bool Duration)[]
        {
            (Loc.T("Short break"), K.PomodoroShort, 1, 60, true),
            (Loc.T("Long break"), K.PomodoroLong, 1, 60, true),
            (Loc.T("Sessions between long breaks"), K.PomodoroInterval, 1, 24, false),
            (Loc.T("Total sessions"), K.PomodoroTotal, 1, 24, false),
        };
        for (var i = 0; i < defs.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var d = defs[i];
            var value = Core.App.Get<int>(d.Key);
            HoverButton? btn = null;
            btn = new HoverButton(() => IslandMenu.Show(btn!, Enumerable.Range(d.Min, d.Max - d.Min + 1).Select(n => new IslandMenu.Item(d.Duration ? DurationText(n) : n.ToString(), () => { Core.App.Set(d.Key, n); if (HasWindow) { Window.ReopenPage(); } }, n == value)), () => HasWindow ? Window.HoldOpen() : null)) { CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 0, 4, 0), Lifts = false };
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(D.T(d.Name, 9.5, FontWeights.Medium, D.Secondary, false, TextTrimming.CharacterEllipsis));
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(D.Digits(d.Duration ? DurationText(value) : value.ToString(), 12, FontWeights.SemiBold, D.Orange));
            var chev = D.I(G.ChevronDown, 7, D.Secondary);
            chev.Margin = new Thickness(3, 1, 0, 0);
            line.Children.Add(chev);
            col.Children.Add(line);
            btn.Child = col;
            Grid.SetColumn(btn, i);
            row.Children.Add(btn);
        }

        return row;
    }

    private static string DurationText(int minutes) => minutes >= 60 ? $"{minutes / 60}h" : $"{minutes}m";
}
