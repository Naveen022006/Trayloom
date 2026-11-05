// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows.Input;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Input;

/// <summary>
/// "Turns each mouse wheel step into a short, gentle glide." Wheel notches are swallowed and their distance is added
/// to a per-axis budget that a ~60 Hz timer thread consumes with an exponential ease-out (spec 02b 1.3 constants):
/// emitted per frame = min(|rem|, max(|rem| * (1 - e^(-dt / tau)), 60 / stretch * dt)) with
/// tau = (0.16 - response * 0.12) * stretch and stretch = 1 + coast * 2 * travelled^2; frames are elapsed-time based
/// (dt capped at 1/20 s) and leftovers below 1 / stretch pixels flush in one final frame. A direction reversal
/// replaces the tail; a notch within 24 px of the previous one in the same window extends the glide. Last stage (40)
/// of the <see cref="WheelPipeline"/>. Pixels are converted to wheel units at 3 units per pixel (40 px = one notch).
/// Touchpad (fine-grained) and injected input, and Ctrl/Win+wheel zoom gestures, are never smoothed.
/// </summary>
public sealed class SmoothScrollFeature : WheelFeatureBase
{
    private const double UnitsPerPixel = 3.0;
    private const double MaxPendingUnits = 120 * 40;

    private volatile int _step = 40;
    private volatile int _response = 65;
    private volatile int _coast;
    private Animator? _animator;
    private AppRuleSet? _rules;

    public override string Id => "smooth-scroll";

    protected override bool IsEnabled() => App.Get<bool>(K.SmoothEnabled);

    internal override IWheelStage CreateStage()
    {
        _rules ??= new AppRuleSet(App, K.SmoothExceptions);
        _animator ??= new Animator(this);
        return new Stage(this, _animator);
    }

    protected override void OnWheelStarted() => _animator?.Start();

    protected override void OnWheelStopped()
    {
        _animator?.Dispose();
        _animator = null;
        _rules?.Dispose();
        _rules = null;
    }

    protected override void LoadSettings()
    {
        var step = App.Get<int>(K.SmoothStep);
        _step = step is >= 20 and <= 100 ? step : 40;
        _response = Math.Clamp(App.Get<int>(K.SmoothResponse), 0, 100);
        _coast = Math.Clamp(App.Get<int>(K.SmoothCoast), 0, 100);
    }

    private sealed class Stage(SmoothScrollFeature owner, Animator animator) : IWheelStage
    {
        public int Order => 40;

        public StageResult Process(ref WheelEvent e)
        {
            // Touchpads / injected input, and zoom gestures, keep their own behaviour.
            if (!e.Notch || (e.Modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) != 0)
            {
                return StageResult.Continue;
            }

            if (owner._rules is { IsEmpty: false } rules && rules.MatchesPoint(e.X, e.Y))
            {
                return StageResult.Continue;
            }

            // A stopped animator (its loop failed) must not swallow the wheel: pass the event through.
            return animator.Add(e.Delta * owner._step / 40.0, e.Horizontal, e.X, e.Y) ? StageResult.Consumed : StageResult.Continue;
        }
    }

    private sealed class Animator(SmoothScrollFeature owner) : IDisposable
    {
        private readonly object _gate = new();
        private readonly double[] _remaining = new double[2];
        private readonly double[] _glide = new double[2];
        private readonly double[] _carry = new double[2];
        private readonly ManualResetEventSlim _wake = new(false);
        private Thread? _thread;
        private volatile bool _running;
        private bool _active;
        private bool _periodRaised;
        private int _lastX = int.MinValue;
        private int _lastY = int.MinValue;

        public void Start()
        {
            if (_thread is not null)
            {
                return;
            }

            _running = true;
            _periodRaised = Win32.timeBeginPeriod(1) == 0;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Trayloom.SmoothScroll", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        /// <summary>Queues scroll units. False when the animator is not running, so the caller lets the event through.</summary>
        public bool Add(double units, bool horizontal, int x, int y)
        {
            if (!_running)
            {
                return false;
            }

            var axis = horizontal ? 1 : 0;
            var wake = false;
            lock (_gate)
            {
                // A tick within 24 px of the previous one keeps the same glide; otherwise a new glide starts.
                var near = Math.Abs(x - _lastX) <= 24 && Math.Abs(y - _lastY) <= 24;
                _lastX = x;
                _lastY = y;
                if (Math.Sign(_remaining[axis]) == -Math.Sign(units))
                {
                    _remaining[axis] = 0;
                    _carry[axis] = 0;
                    _glide[axis] = 0;
                }

                if (!near || _remaining[axis] == 0)
                {
                    _glide[axis] = 0;
                }

                _remaining[axis] = Math.Clamp(_remaining[axis] + units, -MaxPendingUnits, MaxPendingUnits);
                _glide[axis] = Math.Max(Math.Abs(_remaining[axis]), _glide[axis]);
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

            return true;
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
            try
            {
                RunLoop();
            }
            catch (Exception ex)
            {
                // Stop animating and let the wheel pass through untouched rather than swallowing it with nobody to emit it.
                _running = false;
                lock (_gate)
                {
                    _active = false;
                    _remaining[0] = _remaining[1] = 0;
                    _carry[0] = _carry[1] = 0;
                    _glide[0] = _glide[1] = 0;
                }

                try
                {
                    owner.Context?.Log.Error("Smooth scrolling stopped after an error", ex);
                }
                catch
                {
                    // logging must not break the thread
                }
            }
        }

        private void RunLoop()
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
                var last = clock.Elapsed.TotalSeconds;
                while (_running)
                {
                    Thread.Sleep(16);
                    var now = clock.Elapsed.TotalSeconds;
                    var dt = Math.Clamp(now - last, 0.001, 1.0 / 20);
                    last = now;

                    var response = owner._response / 100.0;
                    var coast = owner._coast / 100.0;
                    int emit0, emit1;
                    lock (_gate)
                    {
                        emit0 = Step(0, dt, response, coast);
                        emit1 = Step(1, dt, response, coast);
                        if (_remaining[0] == 0 && _remaining[1] == 0)
                        {
                            _carry[0] = _carry[1] = 0;
                            _glide[0] = _glide[1] = 0;
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
        private int Step(int axis, double dt, double response, double coast)
        {
            var remaining = _remaining[axis];
            if (remaining == 0)
            {
                return 0;
            }

            var abs = Math.Abs(remaining);
            var glide = Math.Max(_glide[axis], abs);
            var travelled = glide > 0 ? 1 - abs / glide : 0;
            var stretch = 1 + coast * 2 * travelled * travelled;
            var tau = (0.16 - response * 0.12) * stretch;
            var finish = UnitsPerPixel / stretch;
            double emit;
            if (abs <= finish)
            {
                emit = abs; // final frame flushes the tail
            }
            else
            {
                var eased = abs * (1 - Math.Exp(-dt / tau));
                var floor = 60 * UnitsPerPixel / stretch * dt;
                emit = Math.Min(abs, Math.Max(eased, floor));
            }

            var signed = Math.Sign(remaining) * emit;
            _remaining[axis] = abs - emit <= 1e-6 ? 0 : remaining - signed;
            _carry[axis] += signed;
            var whole = (int)Math.Truncate(_carry[axis]);
            if (_remaining[axis] == 0)
            {
                whole = (int)Math.Round(_carry[axis]); // the last frame rounds
                _carry[axis] = 0;
            }
            else
            {
                _carry[axis] -= whole;
            }

            return whole;
        }
    }
}
