// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>UI state of the Features hub that survives page rebuilds.</summary>
internal sealed class FeaturesPageState
{
    public int Tab { get; set; }

    public HashSet<string> CollapsedGroups { get; } = [];

    public bool IslandModulesCollapsed { get; set; }

    public bool BundlesCollapsed { get; set; }

    public string? PendingReveal { get; set; }

    public Action? RevealRow { get; set; }

    public List<FeatureCatalogEntry>? NeverUsedUndo { get; set; }
}

/// <summary>The Features hub (spec 02-settings section 3): tabs, summary, bundles, groups and rows.</summary>
internal static class FeaturesPage
{
    private const string Intro = "Install only what you use. Whatever you uninstall disappears from the whole app and stops loading.";
    private const string PermissionsIntro = "What each permission does and which features use it. Other access is requested only when you use it.";
    private const string FooterNote = "Uninstalling deletes nothing: the feature just leaves the app and stops loading. Install it again anytime and everything returns as it was.";
    private const string EnergyHelp = "What the feature keeps alive while it is on. Uninstalled features load nothing at all.";

    private static readonly FeatureGroup[] GroupOrder =
    [
        FeatureGroup.WindowsTaskbar, FeatureGroup.MouseKeyboard, FeatureGroup.ClipboardFiles, FeatureGroup.Sound,
        FeatureGroup.EnergyDisplay, FeatureGroup.Tools, FeatureGroup.Monitor,
    ];

    private static string GroupGlyph(FeatureGroup g) => g switch
    {
        FeatureGroup.WindowsTaskbar => "",
        FeatureGroup.MouseKeyboard => "",
        FeatureGroup.ClipboardFiles => "",
        FeatureGroup.Sound => "",
        FeatureGroup.EnergyDisplay => "",
        FeatureGroup.Tools => "",
        FeatureGroup.Island => "",
        _ => "",
    };

    public static FrameworkElement Build(ShellServices s, FeaturesPageState state, SettingsRouter router, Action rebuild)
    {
        var rt = s.Runtime;
        var rows = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        if (router.PendingFeatureTarget is { } target)
        {
            state.PendingReveal = FeatureCatalog.Resolve(target);
            router.PendingFeatureTarget = null;
            state.Tab = 0;
            if (FeatureCatalog.Get(state.PendingReveal) is { } e)
            {
                state.CollapsedGroups.Remove(e.Group.ToString());
                if (e.IsIslandExtension)
                {
                    state.IslandModulesCollapsed = false;
                }
            }
        }

        var stack = new StackPanel();
        stack.AddSpaced(HeaderBlock(state, rebuild, rt), 20);
        if (rt.NeedsRestartToUnload)
        {
            stack.AddSpaced(RestartCard(s), 20);
        }

        if (state.Tab == 1)
        {
            stack.AddSpaced(PermissionsPage.Build(s), 20);
            return stack;
        }

        if (NeverUsedCard(s, state, rebuild) is { } never)
        {
            stack.AddSpaced(never, 20);
        }

        stack.AddSpaced(IslandCard(s, state, rebuild, rows), 20);
        stack.AddSpaced(BundlesCard(s, state, rebuild), 20);
        foreach (var group in GroupOrder)
        {
            stack.AddSpaced(GroupCard(s, state, rebuild, group, rows), 20);
        }

        var foot = new StackPanel();
        foot.Children.Add(UiKit.Text(FooterNote, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
        var energy = UiKit.Text(EnergyHelp, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        energy.Margin = new Thickness(0, 6, 0, 0);
        foot.Children.Add(energy);
        stack.AddSpaced(foot, 20);

        if (state.PendingReveal is { } reveal && rows.TryGetValue(reveal, out var row))
        {
            state.RevealRow = () =>
            {
                row.BringIntoView(new Rect(0, -160, Math.Max(1, row.ActualWidth), row.ActualHeight + 320));
                Pulse(row);
                state.PendingReveal = null;
            };
        }
        else
        {
            state.RevealRow = null;
            state.PendingReveal = null;
        }

        return stack;
    }

    // ------------------------------------------------------------------------------------------------ top

    private static UIElement HeaderBlock(FeaturesPageState state, Action rebuild, FeatureRuntime rt)
    {
        var card = new SettingsCard();
        var seg = new SegmentedControl(["Features", "Permissions"], state.Tab, false, 13);
        seg.SelectionChanged += i =>
        {
            state.Tab = i;
            rebuild();
        };
        card.Add(seg);
        card.AddDivider();
        card.AddCaption(state.Tab == 0 ? Intro : PermissionsIntro);
        if (state.Tab == 0)
        {
            // The tally and the bulk buttons live in the same card in the product screenshots.
            card.AddDivider();
            card.Add(TallyRow(rt));
            card.Add(new UsageBar((double)rt.AvailableCount / Math.Max(1, rt.InstallableCount), ThemeKeys.Accent, 5));
        }

        _ = rt;
        return card;
    }

    private static UIElement TallyRow(FeatureRuntime rt)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var installed = rt.AvailableCount;
        var total = rt.InstallableCount;
        var text = UiKit.Text($"{installed} of {total} features installed", 13, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var all = UiKit.Button("Install all", ButtonKind.Bordered, 13, onClick: () => rt.SetAllAvailable(true));
        all.IsEnabled = installed < total;
        var none = UiKit.Button("Uninstall all", ButtonKind.Bordered, 13, onClick: () => rt.SetAllAvailable(false));
        none.IsEnabled = installed > 0;
        none.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(all);
        buttons.Children.Add(none);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    private static UIElement SummaryCard(ShellServices s)
    {
        // The progress bar under the tally (full width, accent tint).
        var rt = s.Runtime;
        var total = Math.Max(1, rt.InstallableCount);
        var bar = new UsageBar((double)rt.AvailableCount / total, ThemeKeys.Accent, 5);
        bar.Margin = new Thickness(0, -6, 0, 0);
        return bar;
    }

    private static UIElement RestartCard(ShellServices s)
    {
        var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16) };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = UiKit.Icon("", 22, ThemeKeys.Accent);
        icon.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(icon);
        var text = UiKit.Text("Features uninstalled in this session stay loaded until the app restarts. Restart to unload them from memory now.", 13, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var button = UiKit.Button("Restart now", ButtonKind.Prominent, 13, onClick: () => s.Shell.Restart());
        button.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        card.Child = grid;
        return card;
    }

    private static UIElement? NeverUsedCard(ShellServices s, FeaturesPageState state, Action rebuild)
    {
        var rt = s.Runtime;
        if (state.NeverUsedUndo is { } undo)
        {
            var done = new SettingsCard();
            var undoButton = UiKit.Button("Undo", ButtonKind.Bordered, 13, onClick: () =>
            {
                rt.KeepFeatures(undo);
                rt.SetAvailable(undo.Select(e => e.Id), true, enablingFirstInstalls: false);
                state.NeverUsedUndo = null;
                rebuild();
            });
            var icon = UiKit.Icon("", 15, ThemeKeys.Green);
            done.Add(new SettingsRow(null, FooterNote, null, undoButton));
            _ = icon;
            return done;
        }

        var candidates = rt.NeverTurnedOnCandidates();
        if (candidates.Count < 3)
        {
            return null;
        }

        var card = new SettingsCard("Never turned on");
        var names = string.Join(", ", candidates.Select(c => c.Title));
        card.AddCaption($"These features are installed but have never been on: {names}. Uninstalling them makes the panel and Settings shorter. Nothing is deleted, and each one comes back with one click.");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(UiKit.Button("Keep them", ButtonKind.Bordered, 13, onClick: () =>
        {
            rt.KeepFeatures(candidates);
            rebuild();
        }));
        var remove = UiKit.Button("Uninstall these", ButtonKind.Prominent, 13, onClick: () =>
        {
            var now = rt.NeverTurnedOnCandidates();
            if (now.Count == 0)
            {
                rebuild();
                return;
            }

            rt.SetAvailable(now.Select(e => e.Id), false);
            state.NeverUsedUndo = [.. now];
            rebuild();
        });
        remove.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(remove);
        card.Add(buttons);
        return card;
    }

    // ------------------------------------------------------------------------------------------------ bundles

    private static UIElement BundlesCard(ShellServices s, FeaturesPageState state, Action rebuild)
    {
        var card = new SettingsCard();
        var body = new StackPanel();
        var presets = new Grid();
        for (var i = 0; i < FeatureCatalog.Presets.Count; i++)
        {
            presets.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var preset = FeatureCatalog.Presets[i];
            var cell = PresetCard(s, preset);
            cell.Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0);
            Grid.SetColumn(cell, i);
            presets.Children.Add(cell);
        }

        body.Children.Add(presets);
        body.Children.Add(Spaced(UiKit.Text("One click sets the app up for how you use your PC. Everything else stays one click away.", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true), 10));
        card.Add(Disclosure("Start with a bundle", state.BundlesCollapsed, collapsed =>
        {
            state.BundlesCollapsed = collapsed;
            rebuild();
        }, body, headline: true));
        return card;
    }

    private static Border PresetCard(ShellServices s, FeaturePreset preset)
    {
        var inner = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(UiKit.Icon(preset.Glyph, 15, ThemeKeys.Accent, FontWeights.SemiBold));
        var name = UiKit.Text(preset.Name, 14, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        name.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(name);
        inner.Children.Add(head);
        inner.Children.Add(Spaced(UiKit.Text(preset.Description, 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true), 6));
        var apply = UiKit.Button("Apply", ButtonKind.Bordered, 12.5, onClick: () =>
        {
            var choice = Dialogs.Show(Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(), preset.Name,
                $"Install the {preset.Name} bundle and uninstall the rest? Nothing is deleted, and everything comes back with one click.",
                ["Apply bundle", "Cancel"]);
            if (choice == 0)
            {
                s.Runtime.ApplyPreset(preset);
            }
        });
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        apply.Margin = new Thickness(0, 12, 0, 0);
        inner.Children.Add(apply);
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12), MinHeight = 138, Child = inner };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        return card;
    }

    // ------------------------------------------------------------------------------------------------ groups

    private static UIElement IslandCard(ShellServices s, FeaturesPageState state, Action rebuild, Dictionary<string, FrameworkElement> rows)
    {
        var rt = s.Runtime;
        var notch = FeatureCatalog.Get("notch")!;
        var ext = FeatureCatalog.IslandExtensions.ToList();
        var card = new SettingsCard();
        card.Add(GroupHeader(FeatureGroup.Island, "Dynamic Island", ext.Concat([notch]).Count(e => rt.IsAvailable(e.Id)), ext.Count + 1, collapsible: false, collapsed: false, _ => { }));
        card.AddDivider();
        card.Add(FeatureRow(s, notch, rows, rebuild));
        var inner = new StackPanel();
        for (var i = 0; i < ext.Count; i++)
        {
            if (i > 0)
            {
                inner.Children.Add(UiKit.Divider(8, 8));
            }

            inner.Children.Add(FeatureRow(s, ext[i], rows, rebuild));
        }

        card.Add(Disclosure("What appears", state.IslandModulesCollapsed, collapsed =>
        {
            state.IslandModulesCollapsed = collapsed;
            rebuild();
        }, inner, headline: false));
        return card;
    }

    private static UIElement GroupCard(ShellServices s, FeaturesPageState state, Action rebuild, FeatureGroup group, Dictionary<string, FrameworkElement> rows)
    {
        var rt = s.Runtime;
        var entries = FeatureCatalog.InGroup(group).ToList();
        var key = group.ToString();
        var collapsed = state.CollapsedGroups.Contains(key);
        var card = new SettingsCard();
        card.Add(GroupHeader(group, group.DisplayName(), entries.Count(e => rt.IsAvailable(e.Id)), entries.Count(e => e.CanInstall || rt.IsAvailable(e.Id)), true, collapsed, c =>
        {
            if (c)
            {
                state.CollapsedGroups.Add(key);
            }
            else
            {
                state.CollapsedGroups.Remove(key);
            }

            rebuild();
        }));
        if (group == FeatureGroup.Monitor && entries.All(e => !rt.IsAvailable(e.Id)))
        {
            card.AddCaption("With everything off, the Monitor leaves the panel and the taskbar.");
        }

        if (!collapsed)
        {
            var inner = new StackPanel();
            for (var i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    inner.Children.Add(UiKit.Divider(8, 8));
                }

                inner.Children.Add(FeatureRow(s, entries[i], rows, rebuild));
            }

            card.Add(inner);
        }

        return card;
    }

    private static UIElement GroupHeader(FeatureGroup group, string title, int installed, int total, bool collapsible, bool collapsed, Action<bool> onToggle)
    {
        var grid = new Grid { Background = Brushes.Transparent, Cursor = collapsible ? Cursors.Hand : Cursors.Arrow };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = UiKit.IconTile(GroupGlyph(group));
        grid.Children.Add(tile);
        var name = UiKit.Text(title, 14, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        name.Margin = new Thickness(12, 0, 0, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var count = UiKit.Text($"{installed}/{total}", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(count, 2);
        grid.Children.Add(count);
        var bar = new UsageBar(total == 0 ? 0 : (double)installed / total, ThemeKeys.Accent, 4) { Width = 64, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(bar, 3);
        grid.Children.Add(bar);
        if (collapsible)
        {
            var chevron = UiKit.Icon(collapsed ? Glyphs.ChevronRightSmall : Glyphs.ChevronDownSmall, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
            chevron.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(chevron, 4);
            grid.Children.Add(chevron);
            grid.MouseLeftButtonUp += (_, _) => onToggle(!collapsed);
        }

        return grid;
    }

    private static UIElement Disclosure(string title, bool collapsed, Action<bool> onToggle, UIElement body, bool headline)
    {
        var stack = new StackPanel();
        var head = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(UiKit.Text(title, headline ? 14 : 13, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var chevron = UiKit.Icon(collapsed ? Glyphs.ChevronRightSmall : Glyphs.ChevronDownSmall, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        Grid.SetColumn(chevron, 1);
        head.Children.Add(chevron);
        head.MouseLeftButtonUp += (_, _) => onToggle(!collapsed);
        stack.Children.Add(head);
        if (!collapsed)
        {
            stack.Children.Add(Spaced(body, 10));
        }

        return stack;
    }

    // ------------------------------------------------------------------------------------------------ rows

    private static UIElement FeatureRow(ShellServices s, FeatureCatalogEntry entry, Dictionary<string, FrameworkElement> rows, Action rebuild)
    {
        var rt = s.Runtime;
        var installed = rt.IsAvailable(entry.Id);
        var blocked = rt.InstallBlockedReason(entry.Id);
        var unavailable = blocked is not null;

        var root = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 7, 8, 7), Background = Brushes.Transparent };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 44 px black icon tile.
        var tile = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = entry.Glyph,
                FontFamily = UiKit.IconFont,
                FontSize = 20,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        grid.Children.Add(tile);

        var text = new StackPanel { Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var name = UiKit.Text(entry.Title, 15, FontWeights.Normal, installed ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary);
        name.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(name);
        if (entry.IsExperimental)
        {
            titleRow.Children.Add(Margined(new BadgePill("Experimental", BadgeStyle.Orange), 8, 0));
        }

        if (entry.IsBeta)
        {
            titleRow.Children.Add(Margined(new BadgePill("BETA"), 8, 0));
        }

        var info = new InfoPopoverButton(() => MetadataPopover(entry), MetadataTooltip(entry));
        info.Margin = new Thickness(4, 0, 0, 0);
        titleRow.Children.Add(info);
        titleRow.Children.Add(Margined(EnergyPill(entry), 4, 0));
        text.Children.Add(titleRow);
        var desc = UiKit.Text(entry.Description, 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        desc.Opacity = installed ? 1 : 0.7;
        desc.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(desc);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var clickable = installed && entry.HasSettingsPage && !unavailable;
        if (clickable)
        {
            var chevron = UiKit.Icon(Glyphs.ChevronRight, 10, ThemeKeys.TextTertiary);
            chevron.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(chevron, 2);
            grid.Children.Add(chevron);
            root.Cursor = Cursors.Hand;
            root.MouseEnter += (_, _) => root.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
            root.MouseLeave += (_, _) => root.Background = Brushes.Transparent;
            root.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && IsInside<Button>(d))
                {
                    return;
                }

                s.Shell.OpenSettings(entry.Destination);
            };
        }

        var button = installed
            ? UiKit.Button("Uninstall", ButtonKind.Bordered, 13)
            : UiKit.Button("Install", ButtonKind.Bordered, 13);
        button.MinWidth = 72;
        button.Padding = new Thickness(12, 4, 12, 4);
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Center;
        if (!installed && unavailable)
        {
            button.IsEnabled = false;
            button.ToolTip = blocked;
        }

        button.Click += (_, _) =>
        {
            if (installed)
            {
                Uninstall(s, entry);
            }
            else
            {
                rt.SetAvailable([entry.Id], true);
            }
        };
        Grid.SetColumn(button, 3);
        grid.Children.Add(button);

        root.Child = grid;
        if (unavailable && !installed)
        {
            root.Opacity = 0.4;
            root.ToolTip = blocked;
        }

        rows[entry.Id] = root;
        _ = rebuild;
        return root;
    }

    private static void Uninstall(ShellServices s, FeatureCatalogEntry entry)
    {
        var rt = s.Runtime;
        if (entry.Id == "notch")
        {
            var installedExt = rt.InstalledIslandExtensions();
            if (installedExt.Count > 0)
            {
                var names = string.Join(", ", installedExt.Select(e => e.Title));
                var choice = Dialogs.Show(Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(), "Uninstall Dynamic Island",
                    $"These extensions only work inside the Dynamic Island: {names}. Uninstall them too? Nothing is deleted, and everything comes back with one click.",
                    ["Uninstall extensions too", "Keep extensions", "Cancel"]);
                if (choice == 0)
                {
                    rt.SetAvailable(installedExt.Select(e => e.Id).Concat(["notch"]), false);
                }
                else if (choice == 1)
                {
                    rt.SetAvailable(["notch"], false);
                }

                return;
            }
        }

        rt.SetAvailable([entry.Id], false);
    }

    private static string EnergyLabel(EnergyProfile e) => e switch
    {
        EnergyProfile.Mouse => "Listens to the mouse",
        EnergyProfile.Pointer => "Listens to pointer input",
        EnergyProfile.Keyboard => "Listens to the keyboard",
        EnergyProfile.Inputs => "Listens to mouse and keyboard",
        EnergyProfile.Periodic => "Checks on an interval",
        _ => "Nothing at rest",
    };

    private static string EnergyGlyph(EnergyProfile e) => e switch
    {
        EnergyProfile.Mouse or EnergyProfile.Pointer => "",
        EnergyProfile.Keyboard => "",
        EnergyProfile.Inputs => "",
        EnergyProfile.Periodic => "",
        _ => "",
    };

    private static FrameworkElement EnergyPill(FeatureCatalogEntry entry)
    {
        var pill = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
        pill.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        pill.Child = UiKit.Text(EnergyLabel(entry.Energy), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        return pill;
    }

    private static string MetadataTooltip(FeatureCatalogEntry entry)
    {
        var lines = entry.Permissions.Select(PermissionService.Name).ToList();
        lines.Add(EnergyLabel(entry.Energy));
        return string.Join("\n", lines);
    }

    private static UIElement MetadataPopover(FeatureCatalogEntry entry)
    {
        var stack = new StackPanel { MinWidth = 180 };
        foreach (var p in entry.Permissions)
        {
            stack.AddSpaced(IconLine(PermissionService.Glyph(p), PermissionService.Name(p)), 8);
        }

        stack.AddSpaced(IconLine(EnergyGlyph(entry.Energy), EnergyLabel(entry.Energy)), 8);
        return stack;
    }

    private static UIElement IconLine(string glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = UiKit.Icon(glyph, 13, ThemeKeys.TextSecondary);
        icon.Width = 20;
        row.Children.Add(icon);
        var label = UiKit.Text(text, 12.5, FontWeights.Normal, ThemeKeys.TextPrimary);
        label.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(label);
        return row;
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private static FrameworkElement Margined(FrameworkElement element, double left, double top)
    {
        element.Margin = new Thickness(left, top, 0, 0);
        return element;
    }

    private static T Spaced<T>(T element, double top) where T : UIElement
    {
        if (element is FrameworkElement fe)
        {
            fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + top, fe.Margin.Right, fe.Margin.Bottom);
        }

        return element;
    }

    private static bool IsInside<T>(DependencyObject element) where T : DependencyObject
    {
        for (var d = element; d is not null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
        {
            if (d is T)
            {
                return true;
            }
        }

        return false;
    }

    private static void Pulse(FrameworkElement row)
    {
        if (row is not Border border)
        {
            return;
        }

        var accent = ThemeManager.AccentColor;
        var brush = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B));
        border.Background = brush;
        var animation = new ColorAnimation(Color.FromArgb(0x30, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B), new Duration(TimeSpan.FromMilliseconds(1200)))
        {
            BeginTime = TimeSpan.FromMilliseconds(250),
        };
        animation.Completed += (_, _) => border.Background = Brushes.Transparent;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }
}
