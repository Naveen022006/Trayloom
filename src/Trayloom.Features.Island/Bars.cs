// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Trayloom.Features.Island;

/// <summary>
/// Shared decorative clock (never the display refresh rate); runs only while subscribed. 15 Hz, under the spec's 30 fps ceiling:
/// every step repaints the island's layered window, whose whole surface is copied back for each frame (measured about 1.4 ms of
/// render-thread time per frame, 20 frames a second at the old 33 ms), and the small bars read the same at half the rate.
/// </summary>
internal static class DecorativeClock
{
    private static readonly DispatcherTimer Timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / 15) };
    private static int _subscribers;
    private static readonly DateTime Origin = DateTime.UtcNow;

    static DecorativeClock() => Timer.Tick += (_, _) => Tick?.Invoke();

    public static event Action? Tick;

    public static double Now => (DateTime.UtcNow - Origin).TotalSeconds;

    public static void Subscribe(Action handler)
    {
        Tick += handler;
        if (++_subscribers == 1)
        {
            Timer.Start();
        }
    }

    public static void Unsubscribe(Action handler)
    {
        Tick -= handler;
        if (--_subscribers <= 0)
        {
            _subscribers = 0;
            Timer.Stop();
        }
    }
}

/// <summary>The equalizer bars (<c>NotchLiveEqualizerBars</c>): live bands when available, synthetic swing otherwise, dots when paused.</summary>
internal sealed class EqBars : FrameworkElement
{
    private readonly int _bars;
    private readonly double _barWidth;
    private readonly double _height;
    private Brush _tint;
    private bool _playing;
    private bool _subscribed;

    /// <summary>The service feeding live levels (null = synthetic only).</summary>
    public static Func<double[]?>? LiveSource { get; set; }

    public EqBars(int bars, double barWidth, double height, Brush? tint = null)
    {
        _bars = bars;
        _barWidth = barWidth;
        _height = height;
        _tint = tint ?? Brushes.White;
        Width = (bars * barWidth) + ((bars - 1) * barWidth * 0.85);
        Height = height;
        IsHitTestVisible = false;
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) => Update();
    }

    public bool Playing
    {
        get => _playing;
        set
        {
            if (_playing == value)
            {
                return;
            }

            _playing = value;
            Update();
            InvalidateVisual();
        }
    }

    public Brush Tint
    {
        get => _tint;
        set
        {
            _tint = value;
            InvalidateVisual();
        }
    }

    private void Update()
    {
        if (_playing && IsVisible && !D.ReduceMotion)
        {
            if (!_subscribed)
            {
                _subscribed = true;
                DecorativeClock.Subscribe(OnTick);
            }
        }
        else
        {
            Unsubscribe();
            InvalidateVisual();
        }
    }

    private void Unsubscribe()
    {
        if (_subscribed)
        {
            _subscribed = false;
            DecorativeClock.Unsubscribe(OnTick);
        }
    }

    private void OnTick() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var live = _playing ? LiveSource?.Invoke() : null;
        var t = DecorativeClock.Now;
        var center = (_bars - 1) / 2.0;
        for (var i = 0; i < _bars; i++)
        {
            double h;
            if (!_playing || D.ReduceMotion)
            {
                h = _barWidth;
            }
            else if (live is { Length: 7 })
            {
                var band = Math.Clamp((int)Math.Floor((i + 0.5) / _bars * 7), 0, 6);
                h = Math.Max(_barWidth, _height * (0.1 + (0.9 * Math.Clamp(live[band], 0, 1))));
            }
            else
            {
                var distance = Math.Abs(i - center) / Math.Max(1, center);
                var envelope = Math.Pow(1 - distance, 1.5);
                var low = Math.Max(_barWidth, _height * (0.12 + (envelope * 0.25)));
                var high = Math.Max(_barWidth, _height * (0.12 + (envelope * 0.88)));
                var rise = Math.PI / (5.2 + (i * 0.61));
                var phase = (t + (0.17 * i)) / rise;
                var hph = ((phase % 2) + 2) % 2;
                var outp = hph <= 1 ? hph : 2 - hph;
                var travel = (1 - Math.Cos(Math.PI * outp)) / 2;
                h = low + ((high - low) * travel);
            }

            var x = i * 1.85 * _barWidth;
            dc.DrawRoundedRectangle(_tint, null, new Rect(x, (ActualHeight - h) / 2, _barWidth, h), _barWidth / 2, _barWidth / 2);
        }
    }
}
