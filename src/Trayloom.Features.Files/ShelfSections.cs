// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;
using Orientation = System.Windows.Controls.Orientation;

namespace Trayloom.Features.Files;

/// <summary>The Shelf page (spec 02c 3.1): switch, How to use, triggers, After use, Automatic exceptions.</summary>
public sealed class ShelfSettingsSection : ISettingsSection
{
    public string? FeatureId => "shelf";

    public string PageId => SettingsPages.Shelf;

    public string? Anchor => "shelf";

    public string? Title => Strings.Shelf.Name;

    public IReadOnlyList<string> Keywords =>
    [
        "Temporary area for dragging files", "Shelf shortcut", "Open by shaking the mouse while dragging", "Keep dragged files near the taskbar", "Position",
        "Open near a screen edge", "Open now", "After use", "Close after dropping into another app", "Remove items after dropping", "Clear when closed", "Automatic exceptions",
    ];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var app = c.App;
        ShelfFeature.RegisterDefaults(app);
        var enabled = app.Get<bool>("shelfEnabled");
        var shortcutEnabled = app.Get<bool>("shelfShortcutEnabled");
        var addsSelection = app.Get<bool>("shelfShortcutAddsFinderSelection");
        var islandOn = c.Runtime.IsAvailable("notch") && app.Get<bool>("notchEnabled");

        var page = new List<UIElement>();

        // S1 -------------------------------------------------------------------------------------------------------
        var first = new List<UIElement?>
        {
            SettingsKit.Toggle(app, "shelfEnabled", Strings.Shelf.Enable, Strings.Shelf.EnableCaption, _ => c.RequestRebuild()),
        };
        if (!(shortcutEnabled && addsSelection))
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            var shield = UiKit.Icon(G.Shield, 11, ThemeKeys.TextSecondary);
            shield.Margin = new Thickness(0, 0, 6, 0);
            label.Children.Add(shield);
            label.Children.Add(UiKit.Text(Strings.Shelf.NoPermission, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            first.Add(label);
        }

        page.Add(SettingsKit.Section(null, [.. first]));

        // S2 -------------------------------------------------------------------------------------------------------
        var how = new SettingsCard();
        how.Add(SettingsKit.Bullet(1, Strings.Shelf.Step1));
        how.Add(SettingsKit.Bullet(2, Strings.Shelf.Step2));
        how.Add(SettingsKit.Bullet(3, Strings.Shelf.Step3));
        var howHost = new StackPanel();
        var howTitle = UiKit.Text(Strings.Shelf.HowTitle, 11.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        howTitle.Margin = new Thickness(6, 0, 0, 6);
        howHost.Children.Add(howTitle);
        howHost.Children.Add(how);
        page.Add(howHost);

        if (!enabled)
        {
            return SettingsKit.Page([.. page]);
        }

        // S3 -------------------------------------------------------------------------------------------------------
        if (islandOn)
        {
            var picker = new SegmentedControl([Strings.Shelf.DestinationIsland, Strings.Shelf.DestinationWindow], app.Get<bool>("notchShelf") ? 0 : 1, false, 12);
            picker.SelectionChanged += index => app.Set("notchShelf", index == 0);
            page.Add(SettingsKit.Section(null, SettingsKit.FieldRow(Strings.Shelf.Destinations, picker, null)));
        }

        // S4 -------------------------------------------------------------------------------------------------------
        var dropZone = app.Get<bool>("shelfDropZoneEnabled");
        var topCenter = app.Get<string>("shelfDockPlacement") == "topCenter";
        var openNow = UiKit.Button(Strings.Shelf.OpenNow, ButtonKind.Bordered, 12, G.TrayDownload, () => Hub.Shelf?.Summon());
        openNow.HorizontalAlignment = HorizontalAlignment.Left;
        var triggers = new List<UIElement?>
        {
            SettingsKit.Toggle(app, "shelfShortcutEnabled", Strings.Shelf.ShortcutToggle, null, _ => c.RequestRebuild()),
            SettingsKit.Disable(SettingsKit.ShortcutRow(c, "shelf", Strings.Shelf.HotkeyLabel, shortcutEnabled), !shortcutEnabled),
        };
        if (shortcutEnabled)
        {
            triggers.Add(SettingsKit.Toggle(app, "shelfShortcutAddsFinderSelection", Strings.Shelf.SelectionToggle, Strings.Shelf.SelectionCaption, _ => c.RequestRebuild()));
        }

        triggers.Add(SettingsKit.Toggle(app, "shelfShakeToOpen", Strings.Shelf.ShakeToggle, Strings.Shelf.ShakeCaption));
        triggers.Add(SettingsKit.Toggle(
            app,
            "shelfDropZoneEnabled",
            Strings.Shelf.DropZoneToggle,
            topCenter && !islandOn ? Strings.Shelf.DropZoneCaptionTopCenter : Strings.Shelf.DropZoneCaption,
            _ => c.RequestRebuild()));
        if (dropZone)
        {
            var placement = SettingsKit.Picker(
                Strings.Shelf.DockPlacement,
                [("menuBar", Strings.Shelf.DockTray), ("topCenter", Strings.Shelf.DockTopCenter)],
                topCenter ? "topCenter" : "menuBar",
                v =>
                {
                    app.Set("shelfDockPlacement", v);
                    c.RequestRebuild();
                },
                190);
            SettingsKit.Disable(placement, islandOn);
            triggers.Add(placement);
            if (islandOn)
            {
                triggers.Add(Ui.Caption(Strings.Shelf.DockIslandNote));
            }
        }

        triggers.Add(SettingsKit.Toggle(app, "shelfEdgeDragEnabled", Strings.Shelf.EdgeToggle, Strings.Shelf.EdgeCaption));
        triggers.Add(openNow);
        page.Add(SettingsKit.Section(null, [.. triggers]));

        // S5 -------------------------------------------------------------------------------------------------------
        page.Add(SettingsKit.Section(
            Strings.Shelf.BehaviorTitle,
            SettingsKit.Toggle(app, "shelfCloseAfterDrop", Strings.Shelf.CloseAfterDrop, Strings.Shelf.CloseAfterDropCaption),
            SettingsKit.Toggle(app, "shelfRemoveAfterDrop", Strings.Shelf.RemoveAfterDrop, Strings.Shelf.RemoveAfterDropCaption),
            SettingsKit.Toggle(app, "shelfClearOnClose", Strings.Shelf.ClearOnClose, Strings.Shelf.ClearOnCloseCaption)));

        // S6 -------------------------------------------------------------------------------------------------------
        var exclusions = new AppListControl(
            Strings.Shelf.ExclusionsTitle,
            Strings.Shelf.ExclusionsAdd,
            Strings.Shelf.ExclusionsCaption,
            () => app.Get<List<string>>("shelfAutomaticExclusions") ?? [],
            list => app.Set("shelfAutomaticExclusions", list),
            Loc.T("Remove"),
            useExpander: false);
        var exclusionsCard = new SettingsCard();
        exclusionsCard.Add(exclusions);
        var exclusionsHost = new StackPanel();
        exclusionsHost.Children.Add(exclusionsCard);
        page.Add(exclusionsHost);

        return SettingsKit.Page([.. page]);
    }
}

/// <summary>Controls tab, Files category: the "Shelf" switch with the "Open the shelf (N)" accessory.</summary>
public sealed class ShelfPanelSection : IPanelSection
{
    public string? FeatureId => "shelf";

    public string TabId => PanelTabs.Controls;

    public string? Category => PanelTabs.CategoryFiles;

    public (int Enabled, int Total) GetCategoryCounts(IPanelContext context) => (context.App.Get<bool>("shelfEnabled") ? 1 : 0, 1);

    public int Order => 20;

    public FrameworkElement CreateView(IPanelContext context)
    {
        Hub.Settings = context.App;
        ShelfFeature.RegisterDefaults(context.App);
        var on = context.App.Get<bool>("shelfEnabled");
        var group = new PanelRowGroup();
        var row = new StackPanel();
        row.Children.Add(new PanelToggleRow("", Strings.Shelf.Name, Strings.Shelf.EnableCaption, on, value =>
        {
            context.App.Set("shelfEnabled", value);
            context.RequestRelayout();
        }));
        var count = Hub.Shelf?.ItemCount ?? 0;
        if (on && count > 0)
        {
            var accessory = PanelButtons.Mini(string.Format(Strings.Shelf.OpenOpenFormat, count), "", () =>
            {
                context.InvokeAfterClose(() => Hub.Shelf?.Summon(), 150);
            });
            accessory.HorizontalAlignment = HorizontalAlignment.Left;
            accessory.Margin = new Thickness(41, -2, 10, 8);
            row.Children.Add(accessory);
        }

        group.Add(row);
        return group;
    }
}
