// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;
using Orientation = System.Windows.Controls.Orientation;

namespace Trayloom.Features.Files;

/// <summary>Cut &amp; paste page, block C: the switch, the floating panel option and "How to use".</summary>
public sealed class CutPasteSettingsSection : ISettingsSection
{
    public string? FeatureId => "finder-cut-paste";

    public string PageId => SettingsPages.CutPaste;

    public string? Anchor => "finderCutPaste";

    public string? Title => Strings.CutPaste.Name;

    public IReadOnlyList<string> Keywords => ["Explorer", "move files", "Ctrl+X", "floating panel", "Show floating panel"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        ExplorerCutPasteFeature.RegisterDefaults(c.App);
        var enabled = c.App.Get<bool>("finderCutPasteEnabled");
        var rows = new List<UIElement?>
        {
            SettingsKit.Toggle(c.App, "finderCutPasteEnabled", Strings.CutPaste.Enable, Strings.CutPaste.EnableCaption, _ => c.RequestRebuild()),
        };
        if (enabled)
        {
            rows.Add(SettingsKit.Toggle(c.App, "finderCutPasteShowHUD", Strings.CutPaste.ShowHud, Strings.CutPaste.ShowHudCaption));
            if (c.Runtime.IsRunning("finder-cut-paste"))
            {
                rows.Add(Ui.ActiveLabel(Strings.CutPaste.ActiveNow));
            }
        }

        var how = new SettingsCard();
        how.Add(SettingsKit.KeyCapsRow(["Ctrl", "X"], Strings.CutPaste.Step1));
        how.Add(SettingsKit.KeyCapsRow(["Ctrl", "V"], Strings.CutPaste.Step2));
        how.AddCaption(Strings.CutPaste.TextNote);
        var howHost = new StackPanel();
        var title = UiKit.Text(Strings.CutPaste.HowTitle, 11.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        title.Margin = new Thickness(6, 0, 0, 6);
        howHost.Children.Add(title);
        howHost.Children.Add(how);

        var page = new List<UIElement> { SettingsKit.Section(null, [.. rows]), howHost };
        if (enabled && ElevationNote.NeedsNote())
        {
            page.Add(AdminCard());
        }

        return SettingsKit.Page([.. page]);
    }

    internal static UIElement AdminCard()
    {
        var card = new SettingsCard(Loc.T("Administrator access"));
        if (!ElevationPolicy.CanOfferElevation)
        {
            // A per-user install can be changed without administrator rights, so running it elevated would hand that to any program.
            card.Add(Ui.Caption(Loc.T("Apps that run as administrator ignore Trayloom. Trayloom doesn’t offer to run as administrator, because its folder can be changed without administrator rights.")));
            return card;
        }

        card.Add(Ui.Caption(Loc.T("Some windows belong to apps running as administrator. Run Trayloom as administrator to control them.")));
        // the App's shared restart path: finishes a recording, starts the elevated copy with --relaunch and quits this one
        var button = UiKit.Button(Loc.T("Restart as administrator"), ButtonKind.Bordered, 12, null, () => ElevationPolicy.RequestRestartElevated());
        button.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(button);
        return card;
    }
}

/// <summary>Cut &amp; paste page, block R: Rename shortcut.</summary>
public sealed class RenameShortcutSettingsSection : ISettingsSection
{
    public string? FeatureId => "finder-rename";

    public string PageId => SettingsPages.CutPaste;

    public string? Anchor => "rename";

    public string? Title => Strings.CutPaste.RenameTitle;

    public IReadOnlyList<string> Keywords => ["F2", "rename", "Use a shortcut to rename", "Explorer"];

    public int Order => 20;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        c.App.Register("finderRenameEnabled", false);
        var enabled = c.App.Get<bool>("finderRenameEnabled");
        return SettingsKit.Section(
            Strings.CutPaste.RenameTitle,
            SettingsKit.Toggle(c.App, "finderRenameEnabled", Strings.CutPaste.RenameEnable, Strings.CutPaste.RenameCaption, _ => c.RequestRebuild()),
            SettingsKit.Disable(SettingsKit.ShortcutRow(c, "finderRename", Strings.CutPaste.RenameLabel, enabled, allowBare: true), !enabled));
    }
}

/// <summary>Controls tab, Files category: the "Cut &amp; paste" switch.</summary>
public sealed class CutPastePanelSection : IPanelSection
{
    public string? FeatureId => "finder-cut-paste";

    public string TabId => PanelTabs.Controls;

    public string? Category => PanelTabs.CategoryFiles;

    public (int Enabled, int Total) GetCategoryCounts(IPanelContext context) => (context.App.Get<bool>("finderCutPasteEnabled") ? 1 : 0, 1);

    public int Order => 10;

    public FrameworkElement CreateView(IPanelContext context)
    {
        Hub.Settings = context.App;
        ExplorerCutPasteFeature.RegisterDefaults(context.App);
        var group = new PanelRowGroup();
        group.Add(new PanelToggleRow("", Strings.CutPaste.Name, Strings.CutPaste.EnableCaption, context.App.Get<bool>("finderCutPasteEnabled"), on =>
        {
            context.App.Set("finderCutPasteEnabled", on);
        }));
        return group;
    }
}
