// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Services captured by the Quick panel feature for the stateless utility row.</summary>
internal static class QuickPanelServices
{
    public static IContributionServices? Ctx { get; set; }

    public static string? Shortcut() => Ctx is null ? null : ShortcutText.Of(Ctx, "quickLauncher");
}

/// <summary>The Quick panel (catalog id <c>quick-launcher</c>): a floating panel with the favourite tools, summoned by a shortcut.</summary>
public sealed class QuickLauncherFeature : ToolsFeatureBase
{
    private QuickPanelWindow? _window;

    /// <inheritdoc />
    public override string Id => "quick-launcher";

    /// <inheritdoc />
    public override IReadOnlyList<FeatureAction> Actions => [new FeatureAction("show", Loc.T("Open quick panel"), () => Safe(Show), "")];

    /// <inheritdoc />
    protected override void RegisterDefaults(IAppSettings app) => ToolsDefaults.Register(app);

    /// <inheritdoc />
    protected override void OnStart()
    {
        QuickPanelServices.Ctx = Ctx;
        BindRole("quickLauncher", Toggle);
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        _window?.HidePanel();
        _window?.Close();
        _window = null;
        QuickPanelServices.Ctx = null;
    }

    private void Toggle()
    {
        if (_window is { IsPresented: true })
        {
            _window.HidePanel();
        }
        else
        {
            Show();
        }
    }

    private void Show()
    {
        if (!IsRunning)
        {
            return;
        }

        _window ??= new QuickPanelWindow(Ctx, () => ShortcutText.Configured(Ctx, "quickLauncher"));
        _window.ShowPanel();
    }
}

/// <summary>Quick tools page card "Quick panel" (02c 5.1).</summary>
public sealed class QuickLauncherSettingsSection : ISettingsSection
{
    /// <inheritdoc />
    public string? FeatureId => "quick-launcher";

    /// <inheritdoc />
    public string PageId => SettingsPages.QuickTools;

    /// <inheritdoc />
    public string? Anchor => "quickLauncher";

    /// <inheritdoc />
    public string? Title => Loc.T("Quick panel");

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["Open quick panel", "Global shortcut", "floating panel", "tune"];

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public FrameworkElement CreateView(ISettingsContext c)
    {
        ToolsDefaults.Register(c.App);
        var card = new SettingsCard(Loc.T("Quick panel"));
        card.Add(UiKit.Button(Loc.T("Open quick panel"), ButtonKind.Bordered, 13, "", () => ActionBridge.Invoke(c, "quick-launcher", "show")));
        card.AddCaption(Loc.T("A floating panel with your favorite tools, summoned by a shortcut from anywhere."));
        var hint = UiKit.Text(Loc.T("Use the tune button to choose, hide and drag the tools around."), 11, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true);
        card.Add(hint);
        ShortcutSettingsRows.AddGlobalShortcut(card, c, "quickLauncher", "quickLauncherShortcutEnabled");
        return card;
    }
}

/// <summary>Utilities tab row "Quick panel".</summary>
public sealed class QuickLauncherUtility : IPanelUtility
{
    /// <inheritdoc />
    public string? FeatureId => "quick-launcher";

    /// <inheritdoc />
    public string Id => "quickLauncher";

    /// <inheritdoc />
    public string Title => Loc.T("Quick panel");

    /// <inheritdoc />
    public string Glyph => "";

    /// <inheritdoc />
    public string? Caption => Loc.T("A floating panel with your favorite tools, summoned by a shortcut from anywhere.");

    /// <inheritdoc />
    public string? ShortcutText => QuickPanelServices.Shortcut();

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public void Invoke(IPanelContext context) => ActionBridge.Invoke(context, "quick-launcher", "show");
}
