// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Input;
using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.Input;

/// <summary>
/// Reverses the direction of the mouse wheel (vertical and/or horizontal). Stage 20 of the shared
/// <see cref="WheelPipeline"/>. Only genuine wheel notches are touched: touchpad scrolling and input injected by other
/// programs keep their own direction. The "Apps to leave alone" list (<c>scrollInverterExceptions</c>) is shared with
/// the sideways and zoom features.
/// </summary>
public sealed class InvertScrollFeature : WheelFeatureBase
{
    private volatile bool _vertical;
    private volatile bool _horizontal;
    private AppRuleSet? _rules;

    public override string Id => "invert-scroll";

    protected override bool IsEnabled() => App.Get<bool>(K.ScrollInvertV) || App.Get<bool>(K.ScrollInvertH);

    internal override IWheelStage CreateStage()
    {
        _rules ??= new AppRuleSet(App, K.ScrollInvertExceptions);
        return new Stage(this);
    }

    protected override void OnWheelStopped()
    {
        _rules?.Dispose();
        _rules = null;
    }

    protected override void LoadSettings()
    {
        _vertical = App.Get<bool>(K.ScrollInvertV);
        _horizontal = App.Get<bool>(K.ScrollInvertH);
    }

    private sealed class Stage(InvertScrollFeature owner) : IWheelStage
    {
        public int Order => 20;

        public StageResult Process(ref WheelEvent e)
        {
            if (!e.Notch || (e.Horizontal ? !owner._horizontal : !owner._vertical))
            {
                return StageResult.Continue;
            }

            if (owner._rules is { IsEmpty: false } rules && rules.MatchesPoint(e.X, e.Y))
            {
                return StageResult.Continue;
            }

            e.Delta = -e.Delta;
            e.Changed = true;
            return StageResult.Continue;
        }
    }
}

/// <summary>
/// Holding the chosen modifier (and nothing else) turns the vertical wheel into a horizontal wheel. Stage 10 of the
/// <see cref="WheelPipeline"/>, so invert/linear/smooth scrolling then treat the result as horizontal scrolling.
/// </summary>
public sealed class HorizontalScrollFeature : WheelFeatureBase
{
    private volatile int _modifier = (int)ModifierKeys.Shift;
    private AppRuleSet? _rules;

    public override string Id => "horizontal-scroll";

    protected override bool IsEnabled() => App.Get<bool>(K.ScrollHorizontal);

    internal override IWheelStage CreateStage()
    {
        _rules ??= new AppRuleSet(App, K.ScrollInvertExceptions);
        return new Stage(this);
    }

    protected override void OnWheelStopped()
    {
        _rules?.Dispose();
        _rules = null;
    }

    protected override void LoadSettings() =>
        _modifier = (int)InputKeysMap.Modifier(App.Get<string>(K.ScrollHorizontalModifier), ModifierKeys.Shift);

    private sealed class Stage(HorizontalScrollFeature owner) : IWheelStage
    {
        public int Order => 10;

        public StageResult Process(ref WheelEvent e)
        {
            if (e.Horizontal || !e.Notch || e.Modifiers != (ModifierKeys)owner._modifier)
            {
                return StageResult.Continue;
            }

            if (owner._rules is { IsEmpty: false } rules && rules.MatchesPoint(e.X, e.Y))
            {
                return StageResult.Continue;
            }

            e.Horizontal = true;
            e.Delta = -e.Delta; // wheel down (negative) -> scroll right (positive)
            e.Changed = true;
            return StageResult.Continue;
        }
    }
}

/// <summary>
/// "Zoom" (the original's Pinch Zoom): the chosen modifier plus the wheel becomes Ctrl+wheel, the app zoom gesture on
/// Windows. The held modifier is released for the duration of each notch (one atomic SendInput burst) so apps only see
/// Ctrl. Ctrl itself is already the native zoom key, so choosing Ctrl does nothing. Stage 5, ahead of everything else.
/// </summary>
public sealed class ScrollZoomFeature : WheelFeatureBase
{
    private volatile int _modifier = (int)ModifierKeys.Control;
    private AppRuleSet? _rules;

    public override string Id => "scroll-zoom";

    protected override bool IsEnabled() =>
        App.Get<bool>(K.PinchZoom) && InputKeysMap.Modifier(App.Get<string>(K.PinchZoomModifier), ModifierKeys.None) is ModifierKeys.Alt or ModifierKeys.Windows or ModifierKeys.Shift;

    internal override IWheelStage CreateStage()
    {
        _rules ??= new AppRuleSet(App, K.ScrollInvertExceptions);
        return new Stage(this);
    }

    protected override void OnWheelStopped()
    {
        _rules?.Dispose();
        _rules = null;
    }

    protected override void LoadSettings() =>
        _modifier = (int)InputKeysMap.Modifier(App.Get<string>(K.PinchZoomModifier), ModifierKeys.None);

    private sealed class Stage(ScrollZoomFeature owner) : IWheelStage
    {
        public int Order => 5;

        public StageResult Process(ref WheelEvent e)
        {
            var modifier = (ModifierKeys)owner._modifier;
            if (modifier == ModifierKeys.None || modifier == ModifierKeys.Control || !e.Notch || e.Modifiers != modifier)
            {
                return StageResult.Continue;
            }

            if (owner._rules is { IsEmpty: false } rules && rules.MatchesPoint(e.X, e.Y))
            {
                return StageResult.Continue;
            }

            var held = modifier switch
            {
                ModifierKeys.Alt => (ushort)VK_MENU,
                ModifierKeys.Windows => (ushort)VK_LWIN,
                _ => (ushort)VK_SHIFT,
            };
            const ushort ctrl = 0x11;
            const ushort none = Win32.VK_UNASSIGNED;
            InputSender.Send(
                KeySender.KeyInput(ctrl, false),
                KeySender.KeyInput(none, false),
                KeySender.KeyInput(none, true),
                KeySender.KeyInput(held, true),
                KeySender.WheelInput(e.Delta, e.Horizontal),
                KeySender.KeyInput(held, false),
                KeySender.KeyInput(ctrl, true));
            return StageResult.Consumed;
        }
    }
}

/// <summary>
/// Constant scroll distance per wheel notch, whatever the spin speed: every notch counts as exactly one notch (so
/// accelerated 240/360 deltas become 120) and is then scaled by "Lines per step" relative to the Windows setting
/// (3 lines = the system pace). Fine-grained touchpad events and other injected input are left alone. Stage 30.
/// </summary>
public sealed class LinearScrollFeature : WheelFeatureBase
{
    private volatile int _lines = 3;
    private int _systemLines = 3;
    private AppRuleSet? _rules;

    public override string Id => "linear-scroll";

    protected override bool IsEnabled() => App.Get<bool>(K.LinearEnabled);

    internal override IWheelStage CreateStage()
    {
        _rules ??= new AppRuleSet(App, K.LinearExceptions);
        return new Stage(this);
    }

    protected override void OnWheelStarted()
    {
        _systemLines = Win32.SystemParametersInfoW(Win32.SPI_GETWHEELSCROLLLINES, 0, out var lines, 0) && lines is > 0 and < 100 ? (int)lines : 3;
    }

    protected override void OnWheelStopped()
    {
        _rules?.Dispose();
        _rules = null;
    }

    protected override void LoadSettings()
    {
        var lines = App.Get<int>(K.LinearLines);
        _lines = lines is >= 1 and <= 10 ? lines : 3;
    }

    private sealed class Stage(LinearScrollFeature owner) : IWheelStage
    {
        public int Order => 30;

        public StageResult Process(ref WheelEvent e)
        {
            if (!e.Notch || e.Delta == 0)
            {
                return StageResult.Continue;
            }

            if (owner._rules is { IsEmpty: false } rules && rules.MatchesPoint(e.X, e.Y))
            {
                return StageResult.Continue;
            }

            var output = Math.Sign(e.Delta) * (int)Math.Round(120.0 * owner._lines / owner._systemLines);
            if (output == e.Delta)
            {
                return StageResult.Continue;
            }

            e.Delta = output;
            e.Changed = true;
            return StageResult.Continue;
        }
    }
}

/// <summary>Maps the stored modifier names (shift / option / control / command / none) to WPF modifier keys.</summary>
internal static class InputKeysMap
{
    public static ModifierKeys Modifier(string? name, ModifierKeys fallback) => name switch
    {
        "shift" => ModifierKeys.Shift,
        "option" => ModifierKeys.Alt,
        "control" => ModifierKeys.Control,
        "command" => ModifierKeys.Windows,
        "none" => ModifierKeys.None,
        _ => fallback,
    };
}
