// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Agents;

/// <summary>The island is always dark: colours here are fixed, not theme keys (spec 03 Part C 0.1).</summary>
internal static class AgentColors
{
    private static readonly Dictionary<(uint, int), SolidColorBrush> Cache = [];

    public static Color Tint(this AgentProvider p) => p switch
    {
        AgentProvider.Claude => Trayloom.Core.Ui.ClaudeMark.Orange,
        AgentProvider.Codex => Color.FromRgb(0x7D, 0x99, 0xFF),
        AgentProvider.OpenCode => Color.FromRgb(0x0F, 0xBA, 0x82),
        _ => Color.FromRgb(0x4D, 0xC7, 0xAD),
    };

    public static Brush TintBrush(this AgentProvider p, double opacity = 1) => Of(p.Tint(), opacity);

    /// <summary>Green reads as fine and red as trouble only when it earns it: the agent's own colour while there is room, orange once a window runs low.</summary>
    public static Color LimitTint(AgentProvider p, double usedFraction) =>
        usedFraction >= 0.95 ? Color.FromRgb(0xFF, 0x45, 0x3A) : usedFraction >= 0.80 ? Color.FromRgb(0xFF, 0x9F, 0x0A) : p.Tint();

    public static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
    public static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);

    /// <summary>A frozen brush for <paramref name="color"/> at <paramref name="opacity"/>.</summary>
    public static Brush Of(Color color, double opacity = 1)
    {
        var key = ((uint)(color.R << 16 | color.G << 8 | color.B), (int)Math.Round(opacity * 1000));
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var brush))
            {
                brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B));
                brush.Freeze();
                Cache[key] = brush;
            }

            return brush;
        }
    }

    public static Brush White(double opacity) => Of(Colors.White, opacity);

    public static Brush Primary => White(1);

    public static Brush Strong => White(0.9);

    public static Brush Secondary => White(0.6);

    public static Brush Tertiary => White(0.32);
}

/// <summary>A shared 30 Hz decorative clock, stepped only while something that shows subscribes (never at display refresh rate).</summary>
internal sealed class AgentClock
{
    public static readonly AgentClock Instance = new();

    private readonly List<Action<double>> _subscribers = [];
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private DispatcherTimer? _timer;

    public static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;

    public double Time => _watch.Elapsed.TotalSeconds;

    public IDisposable Subscribe(Action<double> tick)
    {
        _subscribers.Add(tick);
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _timer.Tick += (_, _) =>
            {
                var t = Time;
                foreach (var s in _subscribers.ToArray())
                {
                    s(t);
                }
            };
            _timer.Start();
        }

        return new Subscription(this, tick);
    }

    private void Unsubscribe(Action<double> tick)
    {
        _subscribers.Remove(tick);
        if (_subscribers.Count == 0 && _timer is not null)
        {
            _timer.Stop();
            _timer = null;
        }
    }

    private sealed class Subscription(AgentClock clock, Action<double> tick) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                clock.Unsubscribe(tick);
            }
        }
    }

    /// <summary>Triangle wave over 0..1..0 with period 2*<paramref name="period"/>, eased: (1 - cos(pi*out))/2.</summary>
    public static double Swing(double t)
    {
        var x = t % 2;
        if (x < 0)
        {
            x += 2;
        }

        var o = x < 1 ? x : 2 - x;
        return (1 - Math.Cos(Math.PI * o)) / 2;
    }

    public static double Pulse(double t, double period) => Math.Sin(Math.PI / 2 * ((t % period) / period));
}

/// <summary>Helpers that build the island's small dark controls.</summary>
internal static class AU
{
    public static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? brush = null, TextTrimming trimming = TextTrimming.None, bool display = false, bool mono = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = brush ?? AgentColors.Primary,
            FontFamily = display ? UiKit.DisplayFont : UiKit.TextFont,
            TextTrimming = trimming,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (mono)
        {
            Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        }

        return block;
    }

    public static TextBlock Icon(string glyph, double size, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = UiKit.IconFont,
        FontSize = size,
        Foreground = brush ?? AgentColors.Secondary,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };

    public static void Tip(FrameworkElement element, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        element.ToolTip = new ToolTip { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 } };
        ToolTipService.SetInitialShowDelay(element, 500);
    }

    public static Border Capsule(UIElement child, Brush background, Thickness padding) => new()
    {
        Child = child,
        Background = background,
        Padding = padding,
        CornerRadius = new CornerRadius(100),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The chip of a card header: font 9 semibold in the tint at 90%, capsule filled with the tint at 14%.</summary>
    public static FrameworkElement Chip(string text, Color tint) =>
        Capsule(Text(text, 9, FontWeights.SemiBold, AgentColors.Of(tint, 0.9)), AgentColors.Of(tint, 0.14), new Thickness(5, 1.5, 5, 1.5));

    public static string DurationText(double seconds, int units) => AgentFormat.Duration(seconds, units);
}

/// <summary>A capsule meter with a tick where spending would be if it were spread evenly across the window.</summary>
internal sealed class AgentMeter : FrameworkElement
{
    private readonly double _value;
    private readonly double? _pace;
    private readonly Color _tint;
    private readonly double _height;
    private readonly bool _dimmed;

    public AgentMeter(double value, double? pace, Color tint, double height = 4, bool dimmed = false)
    {
        _value = double.IsFinite(value) ? Math.Min(1, Math.Max(0, value)) : 0;
        _pace = pace;
        _tint = tint;
        _height = height;
        _dimmed = dimmed;
        Height = height + 5;
        IsHitTestVisible = false;
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 40 : availableSize.Width, _height + 5);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var top = (ActualHeight - _height) / 2;
        var radius = _height / 2;
        dc.DrawRoundedRectangle(AgentColors.White(0.13), null, new Rect(0, top, w, _height), radius, radius);
        if (_value > 0)
        {
            var fill = Math.Max(_height, w * _value);
            dc.DrawRoundedRectangle(AgentColors.Of(_tint, _dimmed ? 0.45 : 0.92), null, new Rect(0, top, Math.Min(w, fill), _height), radius, radius);
        }

        if (_pace is { } p && double.IsFinite(p) && p is > 0.02 and < 0.98)
        {
            var x = w * p - 0.75;
            dc.DrawRoundedRectangle(AgentColors.White(0.85), null, new Rect(x, (ActualHeight - (_height + 5)) / 2, 1.5, _height + 5), 0.75, 0.75);
        }
    }
}

/// <summary>A small ring filled by what is left (resting island).</summary>
internal sealed class AgentRing : FrameworkElement
{
    private readonly double _value;
    private readonly Color _tint;
    private readonly double _line;

    public AgentRing(double value, Color tint, double lineWidth = 2.5)
    {
        _value = double.IsFinite(value) ? Math.Min(1, Math.Max(0, value)) : 0;
        _tint = tint;
        _line = lineWidth;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - _line) / 2;
        dc.DrawEllipse(null, new Pen(AgentColors.White(0.18), _line), center, radius, radius);
        if (_value <= 0)
        {
            return;
        }

        var pen = new Pen(AgentColors.Of(_tint), _line) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (_value >= 0.999)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = _value * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>Vector marks of the four agents (vector geometries in <see cref="AgentMarkGeometry"/>, drawn in the provider tint; no brand artwork is shipped).</summary>
internal static class AgentMarks
{
    public static double Scale(AgentProvider p) => p == AgentProvider.Claude ? 1.3 : 1.2;

    /// <summary>The mark drawn in a square of <paramref name="size"/> DIPs with the given brush.</summary>
    public static FrameworkElement Create(AgentProvider provider, double size, Brush? brush = null)
    {
        brush ??= provider.TintBrush();
        var canvas = new Canvas { Width = 24, Height = 24 };

        Path Fill(Geometry data, double opacity = 1) => new() { Data = data, Fill = brush, Opacity = opacity };

        Path Outline(Geometry data, double thickness) => new()
        {
            Data = data,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

        switch (provider)
        {
            case AgentProvider.Claude:
                canvas.Children.Add(Fill(AgentMarkGeometry.Spark));
                break;
            case AgentProvider.Codex:
                canvas.Children.Add(Outline(AgentMarkGeometry.Knot, 1.55));
                break;
            case AgentProvider.OpenCode:
                canvas.Children.Add(Fill(AgentMarkGeometry.Frame));
                canvas.Children.Add(Fill(AgentMarkGeometry.FrameInner, 0.5));
                break;
            default:
                canvas.Children.Add(Fill(AgentMarkGeometry.Head));
                canvas.Children.Add(Fill(AgentMarkGeometry.Ears));
                canvas.Children.Add(Outline(AgentMarkGeometry.Crown, 1.9));
                break;
        }

        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    }

    /// <summary>The "dollarsign.circle" symbol: a ring with a dollar sign.</summary>
    public static FrameworkElement DollarCircle(double size, Brush brush)
    {
        var grid = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        grid.Children.Add(new Ellipse { Stroke = brush, StrokeThickness = 1.1, Width = size, Height = size });
        grid.Children.Add(new TextBlock
        {
            Text = "$",
            Foreground = brush,
            FontFamily = UiKit.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = size * 0.68,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -1, 0, 0),
        });
        return grid;
    }

    /// <summary>The "sparkles" symbol (no stock glyph): a large four-point star with two small ones.</summary>
    public static FrameworkElement Sparkles(double size, Brush brush)
    {
        static Geometry Star(double cx, double cy, double r)
        {
            var k = r * 0.22;
            var data = string.Create(CultureInfo.InvariantCulture,
                $"M{cx},{cy - r} Q{cx + k},{cy - k} {cx + r},{cy} Q{cx + k},{cy + k} {cx},{cy + r} Q{cx - k},{cy + k} {cx - r},{cy} Q{cx - k},{cy - k} {cx},{cy - r} Z");
            return Geometry.Parse(data);
        }

        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var (cx, cy, r) in new[] { (10.0, 13.0, 8.5), (19.0, 5.0, 3.6), (19.5, 19.0, 3.0) })
        {
            canvas.Children.Add(new Path { Data = Star(cx, cy, r), Fill = brush });
        }

        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    }

    /// <summary>A mark at the size of the symbol it stands in for, with the mark scale of the provider.</summary>
    public static FrameworkElement Mark(AgentProvider provider, double size, Brush? brush = null)
    {
        var s = size * Scale(provider);
        var mark = Create(provider, s, brush);
        return new Grid { Width = size * 1.45 + 1, Height = size * 1.45 + 1, Children = { mark }, IsHitTestVisible = false };
    }
}

/// <summary>
/// An agent's mark while it works: it breathes (scale 0.84 to 1, opacity 0.7 to 1, one swing every 1.2 s) and Claude's spark also
/// turns slowly, one full turn every 3.5 s, like Claude's own working indicator. Stepped by the shared 30 Hz decorative clock through
/// render transforms only (no layout); still when idle, hidden, or when motion is reduced.
/// </summary>
internal sealed class AgentGlyph : Grid
{
    /// <summary>Seconds for one full turn of Claude's spark.</summary>
    public const double TurnSeconds = 3.5;

    private readonly FrameworkElement _mark;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly RotateTransform _turn = new(0);
    private readonly bool _working;
    private readonly bool _spins;
    private IDisposable? _subscription;
    private double _start;

    public AgentGlyph(AgentProvider provider, double size = 13, bool working = true, Brush? brush = null, bool spin = true)
    {
        _working = working;
        _spins = spin && provider == AgentProvider.Claude;
        // Room for the widest mark, the Claude one, drawn past its size.
        Width = size * 1.45 + 1;
        Height = size * 1.45 + 1;
        IsHitTestVisible = false;
        _mark = AgentMarks.Create(provider, size * AgentMarks.Scale(provider), brush);
        _mark.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(_turn);
        group.Children.Add(_scale);
        _mark.RenderTransform = group;
        _mark.HorizontalAlignment = HorizontalAlignment.Center;
        _mark.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_mark);
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) =>
        {
            Unsubscribe();
            Subscribe();
        };
    }

    /// <summary>True when the mark is animating right now.</summary>
    public bool IsAnimating => _subscription is not null;

    private void Subscribe()
    {
        if (!_working || AgentClock.ReduceMotion || !IsVisible || _subscription is not null)
        {
            return;
        }

        _start = AgentClock.Instance.Time;
        _subscription = AgentClock.Instance.Subscribe(Step);
    }

    private void Unsubscribe()
    {
        _subscription?.Dispose();
        _subscription = null;
        _scale.ScaleX = _scale.ScaleY = 1;
        _turn.Angle = 0;
        _mark.Opacity = 1;
    }

    private void Step(double time)
    {
        var t = time - _start;
        var b = AgentClock.Swing(t / 1.2);
        var s = 0.84 + 0.16 * b;
        _scale.ScaleX = _scale.ScaleY = s;
        _mark.Opacity = 0.7 + 0.3 * b;
        if (_spins)
        {
            _turn.Angle = t / TurnSeconds * 360 % 360;
        }
    }
}

/// <summary>A calm pulse that says an agent is working; still when motion is reduced.</summary>
internal sealed class AgentPulse : Grid
{
    private readonly Ellipse _ring;
    private readonly ScaleTransform _scale = new(1, 1);
    private IDisposable? _subscription;
    private double _start;

    public AgentPulse(Color tint, double size = 6)
    {
        Width = size * 2;
        Height = size * 2;
        IsHitTestVisible = false;
        var brush = AgentColors.Of(tint);
        _ring = new Ellipse { Width = size, Height = size, Stroke = brush, StrokeThickness = 1, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _scale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(new Ellipse { Width = size, Height = size, Fill = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(_ring);
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) =>
        {
            Unsubscribe();
            Subscribe();
        };
    }

    private void Subscribe()
    {
        if (AgentClock.ReduceMotion || !IsVisible || _subscription is not null)
        {
            return;
        }

        _start = AgentClock.Instance.Time;
        _subscription = AgentClock.Instance.Subscribe(Step);
    }

    private void Unsubscribe()
    {
        _subscription?.Dispose();
        _subscription = null;
        _ring.Opacity = 0;
    }

    private void Step(double time)
    {
        var s = AgentClock.Pulse(time - _start, 1.6);
        _scale.ScaleX = _scale.ScaleY = 1 + 0.9 * s;
        _ring.Opacity = 0.6 * (1 - s);
    }
}

/// <summary>Stacked bars, one per bucket, each split by agent. Hovering a bar reports it so the card can name the day and its value.</summary>
internal sealed class AgentBars : FrameworkElement
{
    private readonly List<AgentBucket> _buckets;
    private readonly List<AgentProvider> _providers;
    private readonly bool _byCost;
    private int _hovered = -1;

    public AgentBars(List<AgentBucket> buckets, List<AgentProvider> providers, bool byCost)
    {
        _buckets = buckets;
        _providers = providers;
        _byCost = byCost;
        Background = null;
    }

    private Brush? Background { get; }

    public event Action<AgentBucket?>? HoverChanged;

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 40 : availableSize.Height);

    private (double Gap, double Width) Metrics()
    {
        var count = Math.Max(1, _buckets.Count);
        var gap = count > 24 ? 2.0 : 4.0;
        return (gap, Math.Max(1, (ActualWidth - gap * (count - 1)) / count));
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var values = _buckets.Select(b => _providers.Select(p => b.ByProvider.TryGetValue(p, out var t) ? t.Weight(_byCost) : 0).ToArray()).ToList();
        var peak = Math.Max(values.Select(v => v.Sum()).DefaultIfEmpty(0).Max(), double.Epsilon);
        var (gap, width) = Metrics();
        for (var i = 0; i < _buckets.Count; i++)
        {
            var x = i * (width + gap);
            var total = values[i].Sum();
            if (total <= 0)
            {
                dc.DrawRoundedRectangle(AgentColors.White(0.10), null, new Rect(x, ActualHeight - 2, width, 2), 1, 1);
                continue;
            }

            var radius = Math.Min(3, width / 2);
            var barHeight = 0.0;
            var heights = new double[_providers.Count];
            for (var s = 0; s < _providers.Count; s++)
            {
                heights[s] = values[i][s] > 0 ? Math.Max(1, ActualHeight * values[i][s] / peak) : 0;
                barHeight += heights[s];
            }

            var clip = new RectangleGeometry(new Rect(x, ActualHeight - barHeight, width, barHeight), radius, radius);
            dc.PushClip(clip);
            var y = ActualHeight;
            for (var s = 0; s < _providers.Count; s++)
            {
                if (heights[s] <= 0)
                {
                    continue;
                }

                y -= heights[s];
                dc.DrawRectangle(AgentColors.Of(_providers[s].Tint(), _hovered < 0 || _hovered == i ? 0.95 : 0.5), null, new Rect(x, y, width, heights[s]));
            }

            dc.Pop();
        }
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (gap, width) = Metrics();
        var index = (int)Math.Floor(e.GetPosition(this).X / (width + gap));
        Hover(index >= 0 && index < _buckets.Count ? index : -1);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Hover(-1);
    }

    private void Hover(int index)
    {
        if (index == _hovered)
        {
            return;
        }

        _hovered = index;
        InvalidateVisual();
        HoverChanged?.Invoke(index >= 0 ? _buckets[index] : null);
    }
}

/// <summary>Thirteen weeks of activity, a column a week, brighter cells for busier days.</summary>
internal sealed class AgentHeatmap : FrameworkElement
{
    public const double Spacing = 2.5;

    private readonly List<AgentBucket> _days;
    private readonly bool _byCost;
    private readonly List<AgentBucket?[]> _columns;
    private readonly double[] _levels;
    private DateTime? _hovered;

    public AgentHeatmap(List<AgentBucket> days, bool byCost)
    {
        _days = days;
        _byCost = byCost;
        _columns = Columns(days);
        var values = days.Select(d => d.Total.Weight(byCost)).Where(v => v > 0).OrderBy(v => v).ToList();
        _levels = values.Count == 0 ? [] : new[] { 0.25, 0.5, 0.75 }.Select(q => values[Math.Min(values.Count - 1, (int)(values.Count * q))]).ToArray();
    }

    public event Action<AgentBucket?>? HoverChanged;

    /// <summary>Square cells as tall as seven rows allow, across every week shown.</summary>
    public static double WidthFor(double height, List<AgentBucket> days)
    {
        var weeks = Columns(days).Count;
        var cell = Math.Max(1, (height - Spacing * 6) / 7);
        return Math.Max(0, weeks * cell + Math.Max(0, weeks - 1) * Spacing);
    }

    /// <summary>Days laid into weeks that start on the reader's first weekday.</summary>
    public static List<AgentBucket?[]> Columns(List<AgentBucket> days)
    {
        var first = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var weeks = new List<AgentBucket?[]>();
        var current = new AgentBucket?[7];
        foreach (var day in days)
        {
            var weekday = ((int)day.Start.DayOfWeek - first + 7) % 7;
            if (weekday == 0 && current.Any(c => c is not null))
            {
                weeks.Add(current);
                current = new AgentBucket?[7];
            }

            current[weekday] = day;
        }

        if (current.Any(c => c is not null))
        {
            weeks.Add(current);
        }

        return weeks;
    }

    private double Cell() => Math.Max(1, Math.Min((ActualHeight - Spacing * 6) / 7, (ActualWidth - Spacing * Math.Max(0, _columns.Count - 1)) / Math.Max(1, _columns.Count)));

    private new double Opacity(double value)
    {
        if (value <= 0)
        {
            return 0.07;
        }

        var level = _levels.Count(l => value > l);
        return new[] { 0.28, 0.45, 0.65, 0.90 }[Math.Min(3, level)];
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 60 : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var cell = Cell();
        var radius = Math.Min(2.5, cell / 3);
        var today = DateTime.Now.Date;
        for (var c = 0; c < _columns.Count; c++)
        {
            for (var r = 0; r < 7; r++)
            {
                if (_columns[c][r] is not { } day)
                {
                    continue;
                }

                var rect = new Rect(c * (cell + Spacing), r * (cell + Spacing), cell, cell);
                dc.DrawRoundedRectangle(AgentColors.White(Opacity(day.Total.Weight(_byCost))), null, rect, radius, radius);
                var isHover = _hovered == day.Start;
                if (isHover || day.Start.Date == today)
                {
                    var inner = new Rect(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1);
                    dc.DrawRoundedRectangle(null, new Pen(AgentColors.White(isHover ? 0.9 : 0.45), 1), inner, radius, radius);
                }
            }
        }
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var cell = Cell();
        var p = e.GetPosition(this);
        var c = (int)Math.Floor(p.X / (cell + Spacing));
        var r = (int)Math.Floor(p.Y / (cell + Spacing));
        AgentBucket? day = c >= 0 && c < _columns.Count && r is >= 0 and < 7 ? _columns[c][r] : null;
        Hover(day);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Hover(null);
    }

    private void Hover(AgentBucket? day)
    {
        if (_hovered == day?.Start)
        {
            return;
        }

        _hovered = day?.Start;
        InvalidateVisual();
        HoverChanged?.Invoke(day);
    }
}

/// <summary>
/// Lays the card tiles out in rows: two per row from 390 DIP wide, a chart or an unpaired card across the whole row, below 390 one card
/// per row (spec 03 A7.1). Children carry <see cref="TileProperty"/> metadata.
/// </summary>
internal sealed class AgentCardGrid : Panel
{
    public static readonly DependencyProperty TileProperty = DependencyProperty.RegisterAttached("Tile", typeof(AgentTile), typeof(AgentCardGrid));

    public static void SetTile(DependencyObject o, AgentTile value) => o.SetValue(TileProperty, value);

    public static AgentTile? GetTile(DependencyObject o) => o.GetValue(TileProperty) as AgentTile;

    private List<List<UIElement>> Rows(double width)
    {
        var map = new Dictionary<AgentTile, UIElement>();
        foreach (UIElement child in InternalChildren)
        {
            if (GetTile(child) is { } tile)
            {
                map[tile] = child;
            }
        }

        return AgentSettings.Rows(map.Keys, width).Select(r => r.Select(t => map[t]).ToList()).ToList();
    }

    private static double RowHeight(List<UIElement> row) => AgentSettings.Height(row.Select(c => GetTile(c)!).ToList());

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 460 : availableSize.Width;
        var rows = Rows(width);
        double height = 0;
        foreach (var row in rows)
        {
            var h = RowHeight(row);
            var cell = (width - AgentSettings.Spacing * (row.Count - 1)) / row.Count;
            foreach (var child in row)
            {
                child.Measure(new Size(Math.Max(0, cell), h));
            }

            height += h;
        }

        height += rows.Count > 1 ? AgentSettings.Spacing * (rows.Count - 1) : 0;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rows = Rows(finalSize.Width);
        double y = 0;
        foreach (var row in rows)
        {
            var h = RowHeight(row);
            var cell = (finalSize.Width - AgentSettings.Spacing * (row.Count - 1)) / row.Count;
            var x = 0.0;
            foreach (var child in row)
            {
                child.Arrange(new Rect(x, y, Math.Max(0, cell), h));
                x += cell + AgentSettings.Spacing;
            }

            y += h + AgentSettings.Spacing;
        }

        return finalSize;
    }
}

/// <summary>A title, an optional countdown and a share on one line; the countdown is dropped when the card is too narrow, the title may shrink.</summary>
internal sealed class AgentFitRow : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width;
        foreach (UIElement c in InternalChildren)
        {
            c.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        }

        double h = 0;
        foreach (UIElement c in InternalChildren)
        {
            h = Math.Max(h, c.DesiredSize.Height);
        }

        return new Size(width, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var title = InternalChildren[0];
        var share = InternalChildren[^1];
        var countdown = InternalChildren.Count > 2 ? InternalChildren[1] : null;
        var gap = 5.0;
        var shareWidth = share.DesiredSize.Width;
        var full = title.DesiredSize.Width + (countdown is null ? 0 : gap + countdown.DesiredSize.Width) + 2 + shareWidth;
        var showCountdown = countdown is not null && full <= finalSize.Width;
        var titleWidth = Math.Min(title.DesiredSize.Width, Math.Max(0, finalSize.Width - shareWidth - 2));
        var h = finalSize.Height;
        title.Arrange(new Rect(0, (h - title.DesiredSize.Height) / 2, titleWidth, title.DesiredSize.Height));
        if (countdown is not null)
        {
            if (showCountdown)
            {
                countdown.Arrange(new Rect(titleWidth + gap, (h - countdown.DesiredSize.Height) / 2, countdown.DesiredSize.Width, countdown.DesiredSize.Height));
            }
            else
            {
                countdown.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        share.Arrange(new Rect(finalSize.Width - shareWidth, (h - share.DesiredSize.Height) / 2, shareWidth, share.DesiredSize.Height));
        return finalSize;
    }
}

/// <summary>A sheet of the four marks at the sizes the island uses, on black; used for visual checks and screenshots.</summary>
public static class AgentMarkSheet
{
    public static FrameworkElement Create()
    {
        var root = new StackPanel { Background = Brushes.Black, Margin = new Thickness(0) };
        foreach (var size in new double[] { 14, 16, 18, 32, 64 })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 4) };
            row.Children.Add(AU.Text(size.ToString("0") + " px", 11, null, AgentColors.Secondary));
            ((TextBlock)row.Children[0]).Width = 46;
            foreach (var p in AgentProviders.All)
            {
                var cell = new Grid { Width = Math.Max(48, size * 1.8), Height = size * 1.6 };
                cell.Children.Add(AgentMarks.Create(p, size, p.TintBrush()));
                row.Children.Add(cell);
            }

            root.Children.Add(row);
        }

        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 12) };
        foreach (var p in AgentProviders.All)
        {
            strip.Children.Add(new AgentGlyph(p, 13) { Margin = new Thickness(0, 0, 14, 0) });
        }

        strip.Children.Add(AU.Text("  strip glyphs (13) animated", 10, null, AgentColors.Secondary));
        root.Children.Add(strip);
        return root;
    }
}
