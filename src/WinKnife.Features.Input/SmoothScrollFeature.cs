// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// Animated wheel scrolling. Wheel notches are swallowed and their distance is re-emitted as many small wheel deltas
/// by a timer thread (~125 Hz) following an exponential ease-out, so the total distance is exactly what the wheel
/// asked for. Last stage (40) of the <see cref="WheelPipeline"/>, after horizontal/invert/linear.
/// Limitations: apps that ignore wheel deltas below 120 will scroll in jumps; Ctrl/Win+wheel (zoom, magnifier),
/// touchpads and other injected input are not smoothed.
/// </summary>
public sealed class SmoothScrollFeature : WheelFeatureBase
{
    private const int TickMs = 8;
    private const double MaxPending = 120 * 40;

    private volatile int _durationMs = 260;
    private volatile int _distancePercent = 100;
    private Animator? _animator;

    public override string Id => "smooth-scroll";

    public override string Name => "Smooth scrolling";

    public override string Description => "Animate mouse wheel scrolling so pages glide instead of jumping.";

    internal override IWheelStage CreateStage()
    {
        _animator ??= new Animator(this);
        return new Stage(this, _animator);
    }

    protected override void OnWheelStarted() => _animator?.Start();

    protected override void OnStop()
    {
        _animator?.Dispose();
        _animator = null;
        base.OnStop();
    }

    protected override void OnPausedChanged(bool paused)
    {
        if (paused)
        {
            _animator?.Cancel();
        }
    }

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        _durationMs = Math.Clamp(Context.Settings.Get("duration", 260), 60, 900);
        _distancePercent = Math.Clamp(Context.Settings.Get("distance", 100), 25, 400);
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddIntSlider("duration", "Animation length (ms)", 60, 900, 260,
            "How long a scroll takes to glide to a stop.")
        .AddIntSlider("distance", "Distance per notch (%)", 25, 400, 100)
        .AddNote("Works with the other scroll features: sideways and inverted scrolling are applied first, linear scrolling normalises notches before they are animated.");

    private sealed class Stage(SmoothScrollFeature owner, Animator animator) : IWheelStage
    {
        public int Order => 40;

        public StageResult Process(ref WheelEvent e)
        {
            if (owner.Paused || e.Injected)
            {
                return StageResult.Continue;
            }

            // Fine-grained deltas (touchpads, high-resolution wheels) are already smooth.
            if (Math.Abs(e.Delta) < 120 && !e.Changed)
            {
                return StageResult.Continue;
            }

            // Ctrl+wheel and Win+wheel are zoom gestures: animating them would zoom in tiny stutters.
            if ((e.Modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) != 0)
            {
                return StageResult.Continue;
            }

            animator.Add(e.Delta * owner._distancePercent / 100.0, e.Horizontal);
            return StageResult.Consumed;
        }
    }

    private sealed class Animator(SmoothScrollFeature owner) : IDisposable
    {
        private readonly object _gate = new();
        private readonly double[] _remaining = new double[2];
        private readonly double[] _carry = new double[2];
        private readonly ManualResetEventSlim _wake = new(false);
        private Thread? _thread;
        private volatile bool _running;
        private bool _active;
        private bool _periodRaised;

        public void Start()
        {
            if (_thread is not null)
            {
                return;
            }

            _running = true;
            _periodRaised = Win32.timeBeginPeriod(1) == 0;
            _thread = new Thread(Loop) { IsBackground = true, Name = "WinKnife.SmoothScroll", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        public void Add(double delta, bool horizontal)
        {
            var axis = horizontal ? 1 : 0;
            var wake = false;
            lock (_gate)
            {
                if (Math.Sign(_remaining[axis]) == -Math.Sign(delta))
                {
                    // Reversing direction: drop what is left of the old animation.
                    _remaining[axis] = 0;
                    _carry[axis] = 0;
                }

                _remaining[axis] = Math.Clamp(_remaining[axis] + delta, -MaxPending, MaxPending);
                if (!_active)
                {
                    _active = true;
                    wake = true;
                }
            }

            if (wake)
            {
                _wake.Set();
            }
        }

        public void Cancel()
        {
            lock (_gate)
            {
                _remaining[0] = _remaining[1] = 0;
                _carry[0] = _carry[1] = 0;
            }
        }

        public void Dispose()
        {
            _running = false;
            _wake.Set();
            _thread?.Join(500);
            _thread = null;
            if (_periodRaised)
            {
                Win32.timeEndPeriod(1);
                _periodRaised = false;
            }

            _wake.Dispose();
        }

        private void Loop()
        {
            var clock = Stopwatch.StartNew();
            while (_running)
            {
                _wake.Wait();
                if (!_running)
                {
                    break;
                }

                _wake.Reset();
                var last = clock.Elapsed.TotalMilliseconds;
                while (_running)
                {
                    Thread.Sleep(TickMs);
                    var now = clock.Elapsed.TotalMilliseconds;
                    var dt = Math.Clamp(now - last, 1, 50);
                    last = now;

                    var tau = owner._durationMs / 4.6;
                    var k = 1 - Math.Exp(-dt / tau);
                    var emit0 = 0;
                    var emit1 = 0;
                    lock (_gate)
                    {
                        emit0 = Step(0, k);
                        emit1 = Step(1, k);
                        if (_remaining[0] == 0 && _remaining[1] == 0)
                        {
                            _carry[0] = _carry[1] = 0;
                            _active = false;
                        }
                    }

                    if (emit0 != 0)
                    {
                        InputSender.SendWheel(emit0, horizontal: false);
                    }

                    if (emit1 != 0)
                    {
                        InputSender.SendWheel(emit1, horizontal: true);
                    }

                    if (!_active)
                    {
                        break;
                    }
                }
            }
        }

        // Called under _gate. Returns the whole wheel units to emit for one axis.
        private int Step(int axis, double k)
        {
            var remaining = _remaining[axis];
            if (remaining == 0)
            {
                return 0;
            }

            var step = Math.Abs(remaining) <= 3 ? remaining : remaining * k;
            _remaining[axis] = Math.Abs(remaining) <= 3 ? 0 : remaining - step;
            _carry[axis] += step;
            var whole = (int)Math.Truncate(_carry[axis]);
            _carry[axis] -= whole;
            return whole;
        }
    }
}
