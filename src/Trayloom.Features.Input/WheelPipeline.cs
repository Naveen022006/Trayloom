// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Input;
using Trayloom.Core;

namespace Trayloom.Features.Input;

/// <summary>A wheel notch travelling through the <see cref="WheelPipeline"/>.</summary>
internal struct WheelEvent
{
    /// <summary>Signed wheel delta (120 = one notch, positive = up/right).</summary>
    public int Delta;

    /// <summary>True for the horizontal wheel.</summary>
    public bool Horizontal;

    /// <summary>True when another program (not Trayloom) injected the event, e.g. a touchpad driver or remote tool.</summary>
    public bool Injected;

    /// <summary>Modifier keys physically held when the event arrived.</summary>
    public ModifierKeys Modifiers;

    /// <summary>Event time in ms.</summary>
    public uint Timestamp;

    /// <summary>Pointer position in physical pixels.</summary>
    public int X;

    public int Y;

    /// <summary>True for a genuine wheel notch (not injected, a multiple of 120): mice, never touchpads or remote tools.</summary>
    public bool Notch;

    /// <summary>True once a stage changed <see cref="Delta"/> or <see cref="Horizontal"/>.</summary>
    public bool Changed;
}

internal enum StageResult
{
    /// <summary>Hand the (possibly modified) event to the next stage.</summary>
    Continue,

    /// <summary>The stage took the event over (swallowed it, possibly re-emitting later).</summary>
    Consumed,
}

internal interface IWheelStage
{
    /// <summary>Position in the chain; lower runs first.</summary>
    int Order { get; }

    StageResult Process(ref WheelEvent e);
}

/// <summary>
/// One ordered chain for every feature that touches the mouse wheel. Without it the features would each swallow
/// and re-inject the wheel event and hide it from one another (injected events are tagged and ignored), so
/// smooth + invert + horizontal could never compose. Chain order (lower Order first):
/// 5 zoom, 10 horizontal-scroll, 20 invert-scroll, 30 linear-scroll, 40 smooth-scroll.
/// Each running wheel feature registers a stage and subscribes a hook handler that calls <see cref="Handle"/>;
/// only the handler of the first registered stage actually processes the event, so it runs exactly once.
/// If no stage modifies the event it passes through untouched; otherwise the original is swallowed and the final
/// result is injected once (or handed to the smooth-scroll animator).
/// </summary>
internal static class WheelPipeline
{
    private sealed class Registration(IWheelStage stage) : IDisposable
    {
        public IWheelStage Stage { get; } = stage;

        public void Dispose() => Remove(this);
    }

    private static readonly object Gate = new();
    private static Registration[] _registrations = [];

    public static IDisposable Register(IWheelStage stage)
    {
        var registration = new Registration(stage);
        lock (Gate)
        {
            _registrations = [.. _registrations.Append(registration).OrderBy(r => r.Stage.Order)];
        }

        return registration;
    }

    private static void Remove(Registration registration)
    {
        lock (Gate)
        {
            _registrations = Array.FindAll(_registrations, r => !ReferenceEquals(r, registration));
        }
    }

    /// <summary>Hook-thread entry point. Returns true when the original event must be swallowed.</summary>
    public static bool Handle(IWheelStage owner, MouseHookEvent source)
    {
        var chain = _registrations;
        if (chain.Length == 0 || !ReferenceEquals(chain[0].Stage, owner))
        {
            return false;
        }

        if (!source.IsWheel || source.IsFromTrayloom || source.WheelDelta == 0)
        {
            return false;
        }

        var e = new WheelEvent
        {
            Delta = source.WheelDelta,
            Horizontal = source.Kind == MouseEventKind.HorizontalWheel,
            Injected = source.IsInjected,
            Modifiers = source.Modifiers,
            Timestamp = source.Timestamp,
            X = source.X,
            Y = source.Y,
            Notch = !source.IsInjected && source.WheelDelta % 120 == 0,
        };

        foreach (var registration in chain)
        {
            if (registration.Stage.Process(ref e) == StageResult.Consumed)
            {
                return true;
            }
        }

        if (!e.Changed)
        {
            return false;
        }

        if (e.Delta != 0)
        {
            InputSender.SendWheel(e.Delta, e.Horizontal);
        }

        return true;
    }
}

/// <summary>Base for features that contribute a stage to the <see cref="WheelPipeline"/> while they are engaged.</summary>
public abstract class WheelFeatureBase : InputFeatureBase
{
    private IWheelStage? _stage;

    internal abstract IWheelStage CreateStage();

    /// <summary>Hook for subclasses (called after the stage is registered).</summary>
    protected virtual void OnWheelStarted()
    {
    }

    protected virtual void OnWheelStopped()
    {
    }

    protected sealed override void Engage()
    {
        var stage = CreateStage();
        _stage = stage;
        Track(WheelPipeline.Register(stage));
        SubscribeMouse(e => WheelPipeline.Handle(stage, e), HookPriorities.Wheel);
        OnWheelStarted();
    }

    protected sealed override void Disengage()
    {
        OnWheelStopped();
        _stage = null;
    }
}
