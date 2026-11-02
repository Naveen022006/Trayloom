// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// Constant scroll distance per wheel notch, whatever the spin speed. Windows has no system-level scroll
/// acceleration, but some mouse drivers (and free-spinning/high-resolution wheels) report larger or fractional deltas
/// when the wheel spins fast. This stage (30 in the <see cref="WheelPipeline"/>) normalises them:
/// <list type="bullet">
/// <item>An event of one notch or more counts as exactly one notch (so 240/360 accelerated deltas become 120).</item>
/// <item>Sub-notch deltas of high-resolution wheels are accumulated and released as whole notches.</item>
/// <item>The "distance" setting scales the size of each notch (50% = half a notch each time).</item>
/// </list>
/// Touchpads and other injected input are left alone.
/// </summary>
public sealed class LinearScrollFeature : WheelFeatureBase
{
    private const int Notch = 120;
    private const int AccumulatorTimeoutMs = 250;

    private volatile int _distancePercent = 100;
    private volatile bool _normalize = true;
    private volatile bool _combineSmall = true;

    public override string Id => "linear-scroll";

    public override string Name => "Linear scrolling";

    public override string Description => "Scroll the same distance for every wheel notch, no matter how fast you spin it.";

    internal override IWheelStage CreateStage() => new Stage(this);

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        _distancePercent = Math.Clamp(Context.Settings.Get("distance", 100), 25, 400);
        _normalize = Context.Settings.Get("normalize", true);
        _combineSmall = Context.Settings.Get("combineSmall", true);
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddToggle("normalize", "Count every wheel event as one notch", true,
            "Undoes driver-side acceleration that reports 2, 3 or more notches in a single event when you spin fast.")
        .AddToggle("combineSmall", "Combine fine wheel steps into notches", true,
            "High-resolution wheels report fractions of a notch; they are summed up and released one full notch at a time.")
        .AddIntSlider("distance", "Distance per notch (%)", 25, 400, 100,
            "100% leaves the Windows scroll setting untouched.");

    private sealed class Stage(LinearScrollFeature owner) : IWheelStage
    {
        // Per axis: [0] vertical, [1] horizontal. Only touched on the hook thread.
        private readonly int[] _accumulator = new int[2];
        private readonly uint[] _lastTick = new uint[2];

        public int Order => 30;

        public StageResult Process(ref WheelEvent e)
        {
            if (owner.Paused || e.Injected)
            {
                return StageResult.Continue;
            }

            var axis = e.Horizontal ? 1 : 0;
            var raw = e.Delta;
            var sign = Math.Sign(raw);
            var absolute = Math.Abs(raw);
            var notches = 0;

            if (absolute >= Notch)
            {
                _accumulator[axis] = 0;
                if (owner._normalize)
                {
                    notches = 1;
                }
                else
                {
                    notches = absolute / Notch;
                }
            }
            else if (owner._combineSmall)
            {
                // A direction change or a pause starts a fresh notch.
                var idle = unchecked(e.Timestamp - _lastTick[axis]);
                if (Math.Sign(_accumulator[axis]) == -sign || idle > AccumulatorTimeoutMs)
                {
                    _accumulator[axis] = 0;
                }

                _lastTick[axis] = e.Timestamp;
                _accumulator[axis] += raw;
                if (Math.Abs(_accumulator[axis]) >= Notch)
                {
                    notches = Math.Abs(_accumulator[axis]) / Notch;
                    _accumulator[axis] -= sign * notches * Notch;
                }
                else
                {
                    return StageResult.Consumed; // wait for more of the notch
                }
            }
            else
            {
                return StageResult.Continue;
            }

            var output = sign * notches * (Notch * owner._distancePercent / 100);
            if (output == raw)
            {
                return StageResult.Continue;
            }

            e.Delta = output;
            e.Changed = true;
            return StageResult.Continue;
        }
    }
}
