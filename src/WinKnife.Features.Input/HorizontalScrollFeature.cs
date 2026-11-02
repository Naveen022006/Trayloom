// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Input;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// Holding the chosen modifier turns the vertical wheel into a horizontal wheel. Stage 10 of the
/// <see cref="WheelPipeline"/>, so invert/linear/smooth scrolling then treat the result as horizontal scrolling.
/// </summary>
public sealed class HorizontalScrollFeature : WheelFeatureBase
{
    private volatile int _modifier = (int)ModifierKeys.Shift;
    private volatile bool _reverse;

    public override string Id => "horizontal-scroll";

    public override string Name => "Scroll sideways with a key";

    public override string Description => "Hold a modifier key while turning the wheel to scroll horizontally.";

    internal override IWheelStage CreateStage() => new Stage(this);

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        var name = Context.Settings.Get("modifier", "Shift");
        _modifier = (int)(name switch
        {
            "Alt" => ModifierKeys.Alt,
            "Ctrl" => ModifierKeys.Control,
            "Win" => ModifierKeys.Windows,
            _ => ModifierKeys.Shift,
        });
        _reverse = Context.Settings.Get("reverse", false);
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddChoice("modifier", "Modifier key",
            [new Choice<string>("Shift", "Shift"), new Choice<string>("Alt", "Alt"), new Choice<string>("Ctrl", "Ctrl"), new Choice<string>("Win", "Windows key")],
            "Shift",
            "Hold this key (and no other modifier) while scrolling. Ctrl keeps its zoom meaning in most apps, so Shift or Alt work best.")
        .AddToggle("reverse", "Reverse direction", false,
            "Off: wheel down scrolls right, wheel up scrolls left.");

    private sealed class Stage(HorizontalScrollFeature owner) : IWheelStage
    {
        public int Order => 10;

        public StageResult Process(ref WheelEvent e)
        {
            if (owner.Paused || e.Horizontal || e.Injected)
            {
                return StageResult.Continue;
            }

            // Exactly the chosen modifier, nothing else (Ctrl+Shift+wheel etc. keep their own meaning).
            if (e.Modifiers != (ModifierKeys)owner._modifier)
            {
                return StageResult.Continue;
            }

            e.Horizontal = true;
            if (!owner._reverse)
            {
                e.Delta = -e.Delta; // wheel down (negative) -> scroll right (positive)
            }

            e.Changed = true;
            return StageResult.Continue;
        }
    }
}
