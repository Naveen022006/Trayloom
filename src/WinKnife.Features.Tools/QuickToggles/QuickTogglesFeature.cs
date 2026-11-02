// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Tools;

/// <summary>Quick toggles (catalog <c>quick-toggles</c>): one-click system actions. The list lives in the tray tab and the Quick panel.</summary>
public sealed class QuickTogglesFeature : ToolsFeatureBase
{
    /// <inheritdoc />
    public override string Id => "quick-toggles";

    /// <inheritdoc />
    protected override void RegisterDefaults(IAppSettings app)
    {
        ToolsDefaults.Register(app);
        app.Register("diskEjectExcludedVolumes", new List<string>());
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        QuickToggleOps.Attach(Ctx);
        _ = ToggleKeyboardLight.ProbeAsync();
    }

    /// <inheritdoc />
    protected override void OnStop() => QuickToggleOps.Detach();
}
