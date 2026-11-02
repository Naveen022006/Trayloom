// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// Reverses the direction of the mouse wheel ("natural" scrolling for mice). Stage 20 of the shared
/// <see cref="WheelPipeline"/>: it sees the axis chosen by horizontal-scroll and runs before linear/smooth scrolling.
/// </summary>
public sealed class InvertScrollFeature : WheelFeatureBase
{
    private volatile bool _vertical = true;
    private volatile bool _horizontal;
    private volatile bool _ignoreInjected = true;

    public override string Id => "invert-scroll";

    public override string Name => "Invert mouse scrolling";

    public override string Description => "Reverse the scroll direction of your mouse wheel so content follows your fingers.";

    internal override IWheelStage CreateStage() => new Stage(this);

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        _vertical = Context.Settings.Get("vertical", true);
        _horizontal = Context.Settings.Get("horizontal", false);
        _ignoreInjected = Context.Settings.Get("ignoreInjected", true);
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddToggle("vertical", "Invert vertical scrolling", true)
        .AddToggle("horizontal", "Invert horizontal scrolling", false)
        .AddToggle("ignoreInjected", "Leave touchpads and remote-control input alone", true,
            "Skips scroll events that other programs or drivers inject, so only a physical mouse is reversed.");

    private sealed class Stage(InvertScrollFeature owner) : IWheelStage
    {
        public int Order => 20;

        public StageResult Process(ref WheelEvent e)
        {
            if (owner.Paused || (owner._ignoreInjected && e.Injected))
            {
                return StageResult.Continue;
            }

            if (e.Horizontal ? !owner._horizontal : !owner._vertical)
            {
                return StageResult.Continue;
            }

            e.Delta = -e.Delta;
            e.Changed = true;
            return StageResult.Continue;
        }
    }
}
