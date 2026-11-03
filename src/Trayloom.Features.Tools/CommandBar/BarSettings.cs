// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Tools;

/// <summary>Shared builders of the Command Bar settings page (spec 1.12 / 02c-7).</summary>
internal static class BarSettingsKit
{
    private static BarService? _fallback;

    public static BarService ServiceFor(IContributionServices ctx)
    {
        if (BarHost.Service is { } running)
        {
            return running;
        }

        if (_fallback is null)
        {
            _fallback = new BarService(ctx);
            _fallback.RefreshCatalog();
        }

        return _fallback;
    }

    public static string TitleOf(IContributionServices ctx, string key) => ServiceFor(ctx).TitleOfKey(key);

    public static TextBlock Caption(string text, string brush = ThemeKeys.TextSecondary, double size = 11.5)
    {
        var tb = UiKit.Text(text, size, FontWeights.Normal, brush, wrap: true);
        return tb;
    }

    public static Button Mini(string text, Action onClick)
    {
        var b = PanelButtons.Mini(text, null, onClick);
        b.FontWeight = FontWeights.Normal;
        b.FontSize = 11;
        return b;
    }

    /// <summary>A list row: leading element, trailing Remove button.</summary>
    public static FrameworkElement ListRow(UIElement leading, UIElement? middle, string? removeText, Action? onRemove, UIElement? extra = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (leading is FrameworkElement l)
        {
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(0, 0, 10, 0);
        }

        grid.Children.Add(leading);
        if (middle is not null)
        {
            if (middle is FrameworkElement m)
            {
                m.VerticalAlignment = VerticalAlignment.Center;
            }

            Grid.SetColumn(middle, 1);
            grid.Children.Add(middle);
        }

        if (extra is not null)
        {
            if (extra is FrameworkElement e)
            {
                e.Margin = new Thickness(8, 0, 8, 0);
                e.VerticalAlignment = VerticalAlignment.Center;
            }

            Grid.SetColumn(extra, 2);
            grid.Children.Add(extra);
        }

        if (removeText is not null && onRemove is not null)
        {
            var remove = Mini(removeText, onRemove);
            Grid.SetColumn(remove, 3);
            grid.Children.Add(remove);
        }

        return grid;
    }

    public static StackPanel Disclosure(string title, FrameworkElement content, out Action<bool> setOpen)
    {
        var host = new StackPanel();
        var chevron = UiKit.Icon(Glyphs.ChevronRight, 10, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        var rotate = new RotateTransform(0);
        chevron.RenderTransform = rotate;
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        var header = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(UiKit.Text(title, 13, FontWeights.Normal, ThemeKeys.TextPrimary));
        Grid.SetColumn(chevron, 1);
        header.Children.Add(chevron);
        content.Visibility = Visibility.Collapsed;
        content.Margin = new Thickness(0, 10, 0, 0);
        var open = false;
        void Set(bool value)
        {
            open = value;
            content.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            rotate.Angle = open ? 90 : 0;
        }

        header.MouseLeftButtonUp += (_, _) => Set(!open);
        setOpen = Set;
        host.Children.Add(header);
        host.Children.Add(content);
        return host;
    }

    public static string? PickFile(bool allowFolder, string? title = null)
    {
        if (allowFolder)
        {
            var dialog = new OpenFolderDialog { Title = title };
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        var file = new OpenFileDialog { Title = title };
        return file.ShowDialog() == true ? file.FileName : null;
    }
}

/// <summary>Lists every section of the Command Bar page in order (used by the harness and tests).</summary>
internal static class BarSettingsSections
{
    public static IReadOnlyList<ISettingsSection> All() =>
    [
        new CommandBarMainSection(),
        new CommandBarAppShortcutsSection(),
        new CommandBarSourcesSection(),
        new CommandBarFilesSection(),
        new CommandBarLinksSection(),
        new CommandBarRowShortcutsSection(),
        new CommandBarNamedSection(),
        new CommandBarPinnedSection(),
        new CommandBarHiddenSection(),
    ];
}

/// <summary>Card B1: open / recenter, explanation, compact mode, skin tone, global shortcut, more options.</summary>
public sealed class CommandBarMainSection : ISettingsSection
{
    public string? FeatureId => "command-bar";

    public int Order => 10;

    public string PageId => SettingsPages.CommandBar;

    public string? Anchor => "commandBar";

    public string? Title => Loc.T("Command Bar");

    public IReadOnlyList<string> Keywords => ["compact mode", "skin tone", "global shortcut", "english layout", "recenter"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        ToolsDefaults.Register(c.App);
        var prefs = new BarPreferences(c.App);
        var card = new SettingsCard(Loc.T("Command Bar"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(UiKit.Button(Loc.T("Open the bar now"), ButtonKind.Bordered, 13, null, () => c.Ui.BeginInvoke(() => BarHost.Feature?.Show(), System.Windows.Threading.DispatcherPriority.Background)));
        var recenter = UiKit.Button(Loc.T("Recenter the bar"), ButtonKind.Bordered, 13);
        recenter.Margin = new Thickness(10, 0, 0, 0);
        recenter.IsEnabled = prefs.HasCustomPosition;
        recenter.Click += (_, _) =>
        {
            prefs.PositionOffset = null;
            recenter.IsEnabled = false;
        };
        buttons.Children.Add(recenter);
        card.Add(buttons);

        var explain = new StackPanel();
        explain.Children.Add(BarSettingsKit.Caption(Loc.T("Drag the mark at the left of the field to put the bar anywhere on the screen; double-click the mark to recenter it.")));
        var settingsCaption = BarSettingsKit.Caption(Loc.T("One shortcut opens a field over whatever you are doing. Type a few letters, press Return and it happens. Nothing you type is saved."));
        settingsCaption.Margin = new Thickness(0, 6, 0, 0);
        explain.Children.Add(settingsCaption);
        var privacy = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        privacy.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        privacy.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lockIcon = UiKit.Icon("", 12, ThemeKeys.TextSecondary);
        lockIcon.Margin = new Thickness(0, 2, 6, 0);
        lockIcon.VerticalAlignment = VerticalAlignment.Top;
        privacy.Children.Add(lockIcon);
        var privacyText = BarSettingsKit.Caption(Loc.T("Search runs on this PC with no account or cloud, and what you type leaves it only through sites you open or scripts you add."));
        Grid.SetColumn(privacyText, 1);
        privacy.Children.Add(privacyText);
        explain.Children.Add(privacy);
        var tryRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var tryLabel = UiKit.Text(Loc.T("Try"), 11.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        tryLabel.Margin = new Thickness(0, 2, 8, 0);
        tryRow.Children.Add(tryLabel);
        var examples = new List<string> { "100 km to mi", "2+2*3", "brightness 40", "fire" };
        if (BarSystem.Battery() is not null)
        {
            examples.Add("battery");
        }

        foreach (var example in examples)
        {
            var chip = new Border { CornerRadius = new CornerRadius(100), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 5, 3) };
            chip.Background = new SolidColorBrush(ThemeManager.IsLight ? Color.FromArgb(0x0F, 0, 0, 0) : Color.FromArgb(0x0F, 255, 255, 255));
            var text = UiKit.Text(example, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary);
            text.FontFamily = UiKit.DisplayFont;
            chip.Child = text;
            tryRow.Children.Add(chip);
        }

        explain.Children.Add(tryRow);
        card.Add(explain);

        card.Add(new SettingsRow(null, Loc.T("Compact mode"), Loc.T("Bar opens without suggestions. Results appear as you type."), new MacSwitch(c.App.Get<bool>("commandBarCompactMode"), SwitchSize.Regular, on => c.App.Set("commandBarCompactMode", on))));

        if (prefs.IsSourceEnabled("emoji"))
        {
            var row = new StackPanel();
            var tones = BarData.SkinTones;
            var items = BarData.SkinToneModifiers.Select(m => "✋" + m).ToList();
            var seg = new SegmentedControl(items, Math.Max(0, Array.IndexOf(tones, prefs.SkinTone)), false, 14);
            seg.SelectionChanged += i => c.App.Set("commandBarEmojiSkinTone", tones[i]);
            var head = new SettingsRow(null, Loc.T("Skin tone"), null, seg);
            row.Children.Add(head);
            var cap = BarSettingsKit.Caption(Loc.T("By default, emoji that can take a tone use this one. Open the actions on an emoji row to choose a different one."));
            cap.Margin = new Thickness(0, 6, 0, 0);
            row.Children.Add(cap);
            card.Add(row);
        }

        ShortcutSettingsRows.AddGlobalShortcut(card, c, "commandBar", "commandBarShortcutEnabled", Loc.T("Global shortcut to open the bar"));

        var more = new StackPanel();
        more.Children.Add(new SettingsRow(null, Loc.T("Switch to an English (US) layout while the bar is open"), Loc.T("The field types Latin characters whatever layout was active, and the previous layout comes back when the bar closes."), new MacSwitch(c.App.Get<bool>("commandBarASCIILayoutEnabled"), SwitchSize.Regular, on => c.App.Set("commandBarASCIILayoutEnabled", on))));
        card.Add(BarSettingsKit.Disclosure(Loc.T("More options"), more, out _));
        return card;
    }
}

/// <summary>Card B2: the App shortcuts sheet button.</summary>
public sealed class CommandBarAppShortcutsSection : ISettingsSection
{
    public string? FeatureId => "command-bar";

    public int Order => 20;

    public string PageId => SettingsPages.CommandBar;

    public string? Anchor => "commandBarApps";

    public string? Title => Loc.T("App shortcuts");

    public IReadOnlyList<string> Keywords => ["alias", "pin", "app shortcut"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var card = new SettingsCard();
        var open = UiKit.Button(Loc.T("App shortcuts"), ButtonKind.Bordered, 13, "", () => new BarAppShortcutsWindow(c).ShowDialog());
        open.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(open);
        card.AddCaption(Loc.T("Open apps with their own shortcuts, even when the bar is closed. Set search names and pin favorites here."));
        return card;
    }
}

/// <summary>Card B3: what the bar searches.</summary>
public sealed class CommandBarSourcesSection : ISettingsSection
{
    public string? FeatureId => "command-bar";

    public int Order => 30;

    public string PageId => SettingsPages.CommandBar;

    public string? Anchor => "commandBarSources";

    public string? Title => Loc.T("What the bar searches");

    public IReadOnlyList<string> Keywords => ["apps", "windows", "emoji", "clipboard", "files", "sources"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var prefs = new BarPreferences(c.App);
        var card = new SettingsCard(Loc.T("What the bar searches"));
        foreach (var source in BarSource.All)
        {
            var raw = source.Raw;
            var toggle = new MacSwitch(prefs.IsSourceEnabled(raw), SwitchSize.Regular, on => prefs.SetSourceEnabled(raw, on)) { IsEnabled = raw != "actions" };
            var label = raw == "killProcess" ? Loc.T("Kill Process") : source.Label;
            card.Add(new SettingsRow(source.Glyph, label, null, toggle));
        }

        card.AddCaption(Loc.T("Turn off what you never want to see. Your own actions always stay."));
        return card;
    }
}

/// <summary>Card B4: file search scopes and ignores.</summary>
public sealed class CommandBarFilesSection : ISettingsSection
{
    public string? FeatureId => "command-bar";

    public int Order => 40;

    public string PageId => SettingsPages.CommandBar;

    public string? Anchor => "commandBarFiles";

    public string? Title => Loc.T("Search your files");

    public IReadOnlyList<string> Keywords => ["folders", "ignore", "files"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var prefs = new BarPreferences(c.App);
        var card = new SettingsCard(Loc.T("Search your files"));
        card.AddCaption(Loc.T("Name the folders to look in and the bar finds files by name as you type. Nothing is indexed and no permission is asked for: it uses Windows Search and offers only what you can already see in File Explorer."));
        var scopes = prefs.FileScopes();
        if (scopes.Count == 0)
        {
            card.AddCaption(Loc.T("No folders yet, so the bar looks for no files."));
        }

        foreach (var scope in scopes)
        {
            var path = scope;
            var text = UiKit.Text(path, 12, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            card.Add(BarSettingsKit.ListRow(UiKit.Icon("", 11, ThemeKeys.Accent), text, Loc.T("Remove"), () =>
            {
                prefs.SetFileScopes(prefs.FileScopes().Where(s => s != path));
                c.RequestRebuild();
            }));
        }

        var add = UiKit.Button(Loc.T("Add a folder"), ButtonKind.Bordered, 12, "");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Multiselect = true, Title = Loc.T("Add a folder") };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var list = prefs.FileScopes();
            foreach (var folder in dialog.FolderNames)
            {
                var stored = folder.Equals(home, StringComparison.OrdinalIgnoreCase) ? "~" : folder.StartsWith(home + "\\", StringComparison.OrdinalIgnoreCase) ? "~" + folder[home.Length..] : folder;
                if (!list.Contains(stored, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(stored);
                }
            }

            prefs.SetFileScopes(list);
            c.RequestRebuild();
        };
        card.Add(add);

        var more = new StackPanel();
        more.Children.Add(BarSettingsKit.Caption(Loc.T("Names never worth showing: a whole folder or file name, or an extension written as *.log.")));
        foreach (var ignore in prefs.FileIgnores())
        {
            var pattern = ignore;
            var text = UiKit.Text(pattern, 12, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            var row = BarSettingsKit.ListRow(UiKit.Icon("", 11, ThemeKeys.TextSecondary), text, Loc.T("Remove"), () =>
            {
                prefs.SetFileIgnores(prefs.FileIgnores().Where(s => s != pattern));
                c.RequestRebuild();
            });
            row.Margin = new Thickness(0, 8, 0, 0);
            more.Children.Add(row);
        }

        var addRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox { Padding = new Thickness(6, 4, 6, 4), FontSize = 12 };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        var addButton = BarSettingsKit.Mini(Loc.T("Add"), () => { });
        addButton.IsEnabled = false;
        void Commit()
        {
            var value = box.Text.Trim();
            if (value.Length == 0)
            {
                return;
            }

            var list = prefs.FileIgnores();
            list.Add(value);
            prefs.SetFileIgnores(list);
            c.RequestRebuild();
        }

        addButton.Click += (_, _) => Commit();
        box.TextChanged += (_, _) => addButton.IsEnabled = box.Text.Trim().Length > 0;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
            {
                Commit();
            }
        };
        var hint = UiKit.Text(Loc.T("A folder or file name"), 12, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(9, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var boxHost = new Grid();
        boxHost.Children.Add(box);
        boxHost.Children.Add(hint);
        addRow.Children.Add(boxHost);
        addButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(addButton, 1);
        addRow.Children.Add(addButton);
        more.Children.Add(addRow);
        card.Add(BarSettingsKit.Disclosure(Loc.T("More options"), more, out var setOpen));
        setOpen(prefs.FileIgnores().Count > 0);
        return card;
    }
}

/// <summary>Card B5: your shortcuts (links, places, scripts).</summary>
public sealed class CommandBarLinksSection : ISettingsSection
{
    public string? FeatureId => "command-bar";

    public int Order => 50;

    public string PageId => SettingsPages.CommandBar;

    public string? Anchor => "commandBarLinks";

    public string? Title => Loc.T("Your shortcuts");

    public IReadOnlyList<string> Keywords => ["link", "script", "folder", "placeholder"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var prefs = new BarPreferences(c.App);
        var card = new SettingsCard(Loc.T("Your shortcuts"));
        var links = prefs.Links();
        if (links.Count == 0)
        {
            card.AddCaption(Loc.T("Nothing saved yet. Add a site, folder, search or script you use every day."));
        }

        foreach (var link in links)
        {
            var l = link;
            var glyph = l.Kind switch { "place" => "", "script" => "", _ => "" };
            var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = UiKit.Text(l.Name, 12, FontWeights.SemiBold, ThemeKeys.TextPrimary);
            name.FontFamily = UiKit.DisplayFont;
            middle.Children.Add(name);
            middle.Children.Add(UiKit.Text(l.Destination, 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
            var edit = BarSettingsKit.Mini(Loc.T("Edit"), () =>
            {
                var dialog = new BarLinkEditor(l);
                if (dialog.ShowDialog() == true)
                {
                    var all = prefs.Links();
                    var i = all.FindIndex(x => x.Id == l.Id);
                    if (i >= 0)
                    {
                        all[i] = dialog.Result!;
                    }

                    prefs.SaveLinks(all);
                    c.RequestRebuild();
                }
            });
            card.Add(BarSettingsKit.ListRow(UiKit.Icon(glyph, 11, ThemeKeys.Accent), middle, Loc.T("Remove"), () =>
            {
                prefs.SaveLinks(prefs.Links().Where(x => x.Id != l.Id));
                c.RequestRebuild();
            }, edit));
        }

        var add = UiKit.Button(Loc.T("Add shortcut"), ButtonKind.Bordered, 12, "");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.IsEnabled = links.Count < BarPreferences.MaxLinks;
        add.Click += (_, _) =>
        {
            var dialog = new BarLinkEditor(null);
            if (dialog.ShowDialog() == true)
            {
                var all = prefs.Links();
                all.Add(dialog.Result!);
                prefs.SaveLinks(all);
                c.RequestRebuild();
            }
        };
        card.Add(add);
        return card;
    }
}

/// <summary>Card B6: rows with their own shortcut.</summary>
public sealed class CommandBarRowShortcutsSection : ISettingsSection
{
    public string? FeatureId => "command-bar";

    public int Order => 60;

    public string PageId => SettingsPages.CommandBar;

    public string? Anchor => "commandBarRowShortcuts";

    public string? Title => Loc.T("Rows with their own shortcut");

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var prefs = new BarPreferences(c.App);
        var card = new SettingsCard(Loc.T("Rows with their own shortcut"));
        var shortcuts = prefs.RowShortcuts();
        if (shortcuts.Count == 0)
        {
            card.AddCaption(Loc.T("No row has its own shortcut yet. Open the actions on any row to give it one."));
        }

        foreach (var (key, hotkey) in shortcuts.OrderBy(p => BarSettingsKit.TitleOf(c, p.Key), StringComparer.CurrentCultureIgnoreCase))
        {
            var k = key;
            var cap = UiKit.Text(hotkey.ToString(), 11, FontWeights.SemiBold, ThemeKeys.Accent);
            cap.FontFamily = UiKit.DisplayFont;
            var title = UiKit.Text(BarSettingsKit.TitleOf(c, k), 12, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
            UIElement? refused = null;
            if (BarHost.RefusedRowShortcuts.Contains(k))
            {
                refused = UiKit.Text(Loc.T("Windows rejected this shortcut. Choose another one."), 11, FontWeights.Normal, ThemeKeys.Orange);
            }

            cap.MinWidth = 90;
            card.Add(BarSettingsKit.ListRow(cap, title, Loc.T("Remove"), () =>
            {
                prefs.SetRowShortcut(k, null);
                c.RequestRebuild();
            }, refused));
        }

        return card;
    }
}

/// <summary>Card B7: names you gave.</summary>
public sealed class CommandBarNamedSection : ISettingsSection
{
    public string? FeatureId => "command-bar";

    public int Order => 70;

    public string PageId => SettingsPages.CommandBar;

    public string? Anchor => "commandBarNamed";

    public string? Title => Loc.T("Names you gave");

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var prefs = new BarPreferences(c.App);
        var card = new SettingsCard(Loc.T("Names you gave"));
        var aliases = prefs.Aliases();
        if (aliases.Count == 0)
        {
            card.AddCaption(Loc.T("Nothing named yet. Open the bar, pick a row and press the actions key."));
        }

        foreach (var (key, alias) in aliases.OrderBy(p => p.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            var k = key;
            var a = UiKit.Text(alias, 12, FontWeights.SemiBold, ThemeKeys.Accent);
            a.FontFamily = UiKit.DisplayFont;
            a.MinWidth = 90;
            card.Add(BarSettingsKit.ListRow(a, UiKit.Text(BarSettingsKit.TitleOf(c, k), 12, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis), Loc.T("Remove"), () =>
            {
                prefs.SetAlias(k, null);
                c.RequestRebuild();
            }));
        }

        return card;
    }
}

/// <summary>Card B8: pinned rows.</summary>
public sealed class CommandBarPinnedSection : ISettingsSection
{
    public string? FeatureId => "command-bar";

    public int Order => 80;

    public string PageId => SettingsPages.CommandBar;

    public string? Anchor => "commandBarPinned";

    public string? Title => Loc.T("Pinned");

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var prefs = new BarPreferences(c.App);
        var card = new SettingsCard(Loc.T("Pinned"));
        var pins = prefs.Pins();
        if (pins.Count == 0)
        {
            card.AddCaption(Loc.T("Nothing pinned yet."));
        }

        foreach (var key in pins)
        {
            var k = key;
            card.Add(BarSettingsKit.ListRow(UiKit.Icon("", 11, ThemeKeys.Accent), UiKit.Text(BarSettingsKit.TitleOf(c, k), 12, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis), Loc.T("Remove"), () =>
            {
                prefs.SetPinned(k, false);
                c.RequestRebuild();
            }));
        }

        return card;
    }
}

/// <summary>Card B9: never shown + forget learned ranking.</summary>
public sealed class CommandBarHiddenSection : ISettingsSection
{
    public string? FeatureId => "command-bar";

    public int Order => 90;

    public string PageId => SettingsPages.CommandBar;

    public string? Anchor => "commandBarHidden";

    public string? Title => Loc.T("Never shown");

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var prefs = new BarPreferences(c.App);
        var card = new SettingsCard(Loc.T("Never shown"));
        var hidden = prefs.Hidden();
        if (hidden.Count == 0)
        {
            card.AddCaption(Loc.T("Nothing hidden."));
        }

        foreach (var key in hidden.OrderBy(k => BarSettingsKit.TitleOf(c, k), StringComparer.CurrentCultureIgnoreCase))
        {
            var k = key;
            card.Add(BarSettingsKit.ListRow(UiKit.Icon("", 11, ThemeKeys.TextSecondary), UiKit.Text(BarSettingsKit.TitleOf(c, k), 12, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis), Loc.T("Remove"), () =>
            {
                prefs.SetHidden(k, false);
                c.RequestRebuild();
            }));
        }

        var forget = UiKit.Button(Loc.T("Forget what I use most"), ButtonKind.Bordered, 12, null, () =>
        {
            new BarLearning(c.App).ForgetAll();
            BarHost.Service?.Learning.ForgetAll();
        });
        forget.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(forget);
        return card;
    }
}

/// <summary>The link editor dialog (440 wide): kind, name, destination, placeholders, script options.</summary>
internal sealed class BarLinkEditor : Window
{
    private readonly CommandBarLink _link;
    private readonly bool _isNew;
    private readonly TextBox _name = new();
    private readonly TextBox _destination = new();
    private readonly Button _save;
    private string _kind;
    private readonly StackPanel _kindContent = new();

    public BarLinkEditor(CommandBarLink? existing)
    {
        _isNew = existing is null;
        _link = existing is null ? new CommandBarLink() : new CommandBarLink
        {
            Id = existing.Id,
            Name = existing.Name,
            Kind = existing.Kind,
            Destination = existing.Destination,
            RunsWithoutArgument = existing.RunsWithoutArgument,
            RunsDirectly = existing.RunsDirectly,
        };
        _kind = _link.Kind;
        Title = Loc.T("Your shortcuts");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => ThemeManager.BindWindow(this);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(UiKit.Text(Loc.T("Your shortcuts"), 15, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var seg = new SegmentedControl([Loc.T("Site or link"), Loc.T("Folder or file"), Loc.T("Script")], KindIndex(_kind), true, 12) { Margin = new Thickness(0, 14, 0, 0) };
        seg.SelectionChanged += i =>
        {
            _kind = i switch { 1 => "place", 2 => "script", _ => "link" };
            RebuildKind();
            Validate();
        };
        root.Children.Add(seg);

        root.Children.Add(FieldLabel(Loc.T("Name")));
        _name.Text = _link.Name;
        _name.SetResourceReference(StyleProperty, ThemeKeys.TextBoxStyle);
        _name.TextChanged += (_, _) => Validate();
        root.Children.Add(_name);

        root.Children.Add(FieldLabel(Loc.T("Where it goes")));
        var destRow = new Grid();
        destRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        destRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _destination.Text = _link.Destination;
        _destination.SetResourceReference(StyleProperty, ThemeKeys.TextBoxStyle);
        _destination.TextChanged += (_, _) => Validate();
        destRow.Children.Add(_destination);
        var choose = UiKit.Button(Loc.T("Choose…"), ButtonKind.Bordered, 12);
        choose.Margin = new Thickness(8, 0, 0, 0);
        choose.Click += (_, _) =>
        {
            var picked = BarSettingsKit.PickFile(_kind == "place");
            if (picked is null)
            {
                return;
            }

            _destination.Text = picked;
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                _name.Text = Path.GetFileNameWithoutExtension(picked);
            }
        };
        Grid.SetColumn(choose, 1);
        destRow.Children.Add(choose);
        choose.Tag = "choose";
        _chooseButton = choose;
        root.Children.Add(destRow);

        _kindContent.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(_kindContent);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        footer.Children.Add(UiKit.Button(Loc.T("Cancel"), ButtonKind.Bordered, 13, null, () => DialogResult = false));
        _save = UiKit.Button(Loc.T("Save"), ButtonKind.Prominent, 13);
        _save.Margin = new Thickness(8, 0, 0, 0);
        _save.Click += (_, _) =>
        {
            _link.Name = _name.Text.Trim();
            _link.Destination = _destination.Text.Trim();
            _link.Kind = _kind;
            Result = _link;
            DialogResult = true;
        };
        footer.Children.Add(_save);
        root.Children.Add(footer);
        Content = root;
        RebuildKind();
        Validate();
        _ = _isNew;
    }

    private readonly Button? _chooseButton;

    public CommandBarLink? Result { get; private set; }

    private static int KindIndex(string kind) => kind switch { "place" => 1, "script" => 2, _ => 0 };

    private static TextBlock FieldLabel(string text)
    {
        var tb = UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        tb.Margin = new Thickness(0, 12, 0, 4);
        return tb;
    }

    private void Validate() => _save.IsEnabled = _name.Text.Trim().Length > 0 && _destination.Text.Trim().Length > 0;

    private void RebuildKind()
    {
        _kindContent.Children.Clear();
        if (_chooseButton is not null)
        {
            _chooseButton.Visibility = _kind == "link" ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_kind == "script")
        {
            _kindContent.Children.Add(BarSettingsKit.Caption(Loc.T("Choose an executable file. Type its name followed by what you want to send. It runs when you pause and shows the result here.")));
            var a = new SettingsRow(null, Loc.T("Also run when its name is typed on its own"), null, new MacSwitch(_link.RunsWithoutArgument, SwitchSize.Mini, on => _link.RunsWithoutArgument = on)) { Margin = new Thickness(0, 10, 0, 0) };
            var b = new SettingsRow(null, Loc.T("Run from its global shortcut without opening the bar"), null, new MacSwitch(_link.RunsDirectly, SwitchSize.Mini, on => _link.RunsDirectly = on)) { Margin = new Thickness(0, 8, 0, 0) };
            _kindContent.Children.Add(a);
            _kindContent.Children.Add(b);
            return;
        }

        _kindContent.Children.Add(BarSettingsKit.Caption(Loc.T("Drop one of these in and it gets filled in when it opens")));
        var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (token, meaning) in new[] { ("{query}", Loc.T("what you type")), ("{clipboard}", Loc.T("what you copied")), ("{selection}", Loc.T("what is selected")), ("{date}", Loc.T("today")) })
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var t = UiKit.Text(token, 10.5, FontWeights.SemiBold, ThemeKeys.TextPrimary);
            t.FontFamily = UiKit.DisplayFont;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            var m = UiKit.Text(meaning, 9, FontWeights.Normal, ThemeKeys.TextSecondary);
            m.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(t);
            stack.Children.Add(m);
            var chip = new Border { CornerRadius = new CornerRadius(100), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, ToolTip = meaning, Child = stack };
            chip.Background = new SolidColorBrush(ThemeManager.IsLight ? Color.FromArgb(0x10, 0, 0, 0) : Color.FromArgb(0x14, 255, 255, 255));
            var tok = token;
            chip.MouseLeftButtonUp += (_, _) => _destination.Text += tok;
            wrap.Children.Add(chip);
        }

        _kindContent.Children.Add(wrap);
    }
}

/// <summary>The App shortcuts sheet (780 x 560): search, filter, alias, shortcut and pin per app.</summary>
internal sealed class BarAppShortcutsWindow : Window
{
    private readonly ISettingsContext _ctx;
    private readonly BarPreferences _prefs;
    private readonly BarService _svc;
    private readonly TextBox _search = new();
    private readonly StackPanel _rows = new();
    private readonly TextBlock _message;
    private string _sort = "name";
    private bool _ascending = true;
    private int _filter;
    private List<BarEntry> _apps = [];

    public BarAppShortcutsWindow(ISettingsContext ctx)
    {
        _ctx = ctx;
        _prefs = new BarPreferences(ctx.App);
        _svc = BarSettingsKit.ServiceFor(ctx);
        Title = Loc.T("App shortcuts");
        Width = 780;
        Height = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => ThemeManager.BindWindow(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !(Keyboard.FocusedElement is HotkeyRecorder))
            {
                Close();
            }
        };

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel();
        head.Children.Add(UiKit.Text(Loc.T("App shortcuts"), 20, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var sub = UiKit.Text(Loc.T("Open apps with their own shortcuts, even when the bar is closed. Set search names and pin favorites here."), 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        sub.Margin = new Thickness(0, 4, 0, 0);
        head.Children.Add(sub);
        root.Children.Add(head);

        var tools = new Grid { Margin = new Thickness(0, 16, 0, 10) };
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search.SetResourceReference(StyleProperty, ThemeKeys.TextBoxStyle);
        _search.TextChanged += (_, _) => Rebuild();
        var searchHost = new Grid();
        searchHost.Children.Add(_search);
        var placeholder = UiKit.Text(Loc.T("Search apps"), 12.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        placeholder.IsHitTestVisible = false;
        placeholder.Margin = new Thickness(9, 0, 0, 0);
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        _search.TextChanged += (_, _) => placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchHost.Children.Add(placeholder);
        tools.Children.Add(searchHost);
        var seg = new SegmentedControl([Loc.T("All"), Loc.T("Pinned"), Loc.T("With shortcuts")], 0, false, 12) { Margin = new Thickness(12, 0, 0, 0) };
        seg.SelectionChanged += i =>
        {
            _filter = i;
            Rebuild();
        };
        Grid.SetColumn(seg, 1);
        tools.Children.Add(seg);
        Grid.SetRow(tools, 1);
        root.Children.Add(tools);

        var header = new Grid { Margin = new Thickness(8, 0, 8, 6) };
        foreach (var w in new[] { 1.0, 0.0, 0.0, 0.0 })
        {
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }

        header.ColumnDefinitions[1].Width = new GridLength(160);
        header.ColumnDefinitions[2].Width = new GridLength(200);
        header.ColumnDefinitions[3].Width = new GridLength(64);
        AddHeader(header, Loc.T("Apps"), "name", 0);
        AddHeader(header, Loc.T("Alias"), "alias", 1);
        AddHeader(header, Loc.T("Shortcut"), "shortcut", 2);
        AddHeader(header, Loc.T("Pinned"), "pinned", 3);
        Grid.SetRow(header, 2);
        root.Children.Add(header);

        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var tableBorder = new Border { CornerRadius = new CornerRadius(8), Child = scroll, BorderThickness = new Thickness(1) };
        tableBorder.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        Grid.SetRow(tableBorder, 3);
        root.Children.Add(tableBorder);

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _message = UiKit.Text(Loc.T("Press the keys you want · Delete clears it · Esc goes back"), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        _message.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_message);
        var done = UiKit.Button(Loc.T("Done"), ButtonKind.Bordered, 13, null, Close);
        Grid.SetColumn(done, 1);
        footer.Children.Add(done);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        Content = root;

        BarProviders.RescanApps(() => Dispatcher.BeginInvoke(Reload));
        Reload();
    }

    private void AddHeader(Grid header, string title, string key, int column)
    {
        var button = new TextBlock { Text = title + (_sort == key ? (_ascending ? " ▲" : " ▼") : ""), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        button.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        button.MouseLeftButtonUp += (_, _) =>
        {
            if (_sort == key)
            {
                _ascending = !_ascending;
            }
            else
            {
                _sort = key;
                _ascending = true;
            }

            RefreshHeaders(header);
            Rebuild();
        };
        Grid.SetColumn(button, column);
        header.Children.Add(button);
        button.Tag = (title, key);
    }

    private void RefreshHeaders(Grid header)
    {
        foreach (var tb in header.Children.OfType<TextBlock>())
        {
            if (tb.Tag is (string title, string key))
            {
                tb.Text = title + (_sort == key ? (_ascending ? " ▲" : " ▼") : "");
            }
        }
    }

    private void Reload()
    {
        _svc.RefreshCatalog();
        _apps = [.. _svc.AppEntriesSnapshot()];
        Rebuild();
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        var aliases = _prefs.Aliases();
        var shortcuts = _prefs.RowShortcuts();
        var pins = _prefs.Pins().ToHashSet();
        var q = BarSearch.Normalize(_search.Text);
        var list = _apps
            .GroupBy(a => a.StableKey)
            .Select(g => g.Last())
            .Where(a => q.Length == 0 || BarSearch.Normalize(a.Title + " " + a.Keywords + " " + aliases.GetValueOrDefault(a.StableKey)).Contains(q, StringComparison.Ordinal))
            .Where(a => _filter switch { 1 => pins.Contains(a.StableKey), 2 => shortcuts.ContainsKey(a.StableKey), _ => true })
            .ToList();
        IOrderedEnumerable<BarEntry> ordered = _sort switch
        {
            "alias" => list.OrderByDescending(a => aliases.ContainsKey(a.StableKey)).ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase),
            "shortcut" => list.OrderByDescending(a => shortcuts.ContainsKey(a.StableKey)).ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase),
            "pinned" => list.OrderByDescending(a => pins.Contains(a.StableKey)).ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => list.OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase),
        };
        var result = ordered.ToList();
        if (!_ascending)
        {
            result = _sort == "name" ? [.. result.AsEnumerable().Reverse()] : [.. result.AsEnumerable().Reverse()];
        }

        if (result.Count == 0)
        {
            var empty = UiKit.Text(_apps.Count == 0 ? Loc.T("Still looking") : Loc.T("Nothing here by that name."), 13, FontWeights.Normal, ThemeKeys.TextSecondary);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Margin = new Thickness(0, 60, 0, 0);
            _rows.Children.Add(empty);
            return;
        }

        foreach (var app in result.Take(400))
        {
            _rows.Children.Add(BuildRow(app, aliases, shortcuts, pins));
        }
    }

    private FrameworkElement BuildRow(BarEntry app, Dictionary<string, string> aliases, Dictionary<string, Hotkey> shortcuts, HashSet<string> pins)
    {
        var grid = new Grid { Margin = new Thickness(8, 3, 8, 3), MinHeight = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = app.Icon ?? app.IconLoader?.Invoke();
        if (icon is not null)
        {
            name.Children.Add(new Image { Source = icon, Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0) });
        }

        name.Children.Add(UiKit.Text(app.Title, 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        grid.Children.Add(name);

        var key = app.StableKey;
        var alias = new TextBox { Text = aliases.GetValueOrDefault(key) ?? "", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(6, 3, 6, 3), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        alias.SetResourceReference(StyleProperty, ThemeKeys.TextBoxStyle);
        void CommitAlias()
        {
            var text = alias.Text.Trim();
            if (text.Length > 0 && _prefs.AliasOwner(key, text) is { } owner)
            {
                _message.Text = $"{app.Title}: {BarStrings.AliasTaken(_svc.TitleOfKey(owner))}";
                _message.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Orange);
                alias.Text = _prefs.AliasOf(key) ?? "";
                return;
            }

            _prefs.SetAlias(key, text);
        }

        alias.LostFocus += (_, _) => CommitAlias();
        alias.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
            {
                CommitAlias();
                Keyboard.ClearFocus();
            }
        };
        Grid.SetColumn(alias, 1);
        grid.Children.Add(alias);

        var rec = new HotkeyRecorder { Hotkey = shortcuts.GetValueOrDefault(key), Width = 140, MinWidth = 100 };
        rec.HotkeyChanged += (_, hotkey) =>
        {
            if (hotkey is not null)
            {
                var error = _svc.ValidateRowShortcut(key, hotkey);
                if (error is not null)
                {
                    _message.Text = $"{app.Title}: {error}";
                    _message.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Orange);
                    rec.Hotkey = _prefs.RowShortcuts().GetValueOrDefault(key);
                    return;
                }
            }

            _prefs.SetRowShortcut(key, hotkey);
            _message.Text = Loc.T("Press the keys you want · Delete clears it · Esc goes back");
            _message.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        };
        var recHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        recHost.Children.Add(rec);
        var clear = new Border { Child = UiKit.Icon("", 11, ThemeKeys.TextSecondary), Padding = new Thickness(6), Cursor = Cursors.Hand, ToolTip = Loc.T("Take the shortcut off"), Background = Brushes.Transparent };
        clear.MouseLeftButtonUp += (_, _) =>
        {
            _prefs.SetRowShortcut(key, null);
            rec.Hotkey = null;
        };
        recHost.Children.Add(clear);
        Grid.SetColumn(recHost, 2);
        grid.Children.Add(recHost);

        var pinned = pins.Contains(key);
        var pin = new Border { Padding = new Thickness(8), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = pinned ? Loc.T("Unpin") : Loc.T("Pin to the top"), HorizontalAlignment = HorizontalAlignment.Left };
        pin.Child = UiKit.Icon("", 13, pinned ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        pin.MouseLeftButtonUp += (_, _) =>
        {
            _prefs.SetPinned(key, !_prefs.IsPinned(key));
            Rebuild();
        };
        Grid.SetColumn(pin, 3);
        grid.Children.Add(pin);
        return grid;
    }
}
