// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Ui;
using Orientation = System.Windows.Controls.Orientation;

namespace WinKnife.Features.Files;

/// <summary>Clipboard page, blocks H1 to H6 (spec 02c 1.1): history switch, shortcut and layout, tray preview, content rules, panel entry, auto clear.</summary>
public sealed class ClipboardSettingsSection : ISettingsSection
{
    private static readonly IReadOnlyList<(int Value, string Label)> Limits =
    [
        (20, "20"), (50, "50"), (100, "100"), (250, "250"), (500, "500"), (1000, "1000"), (10000, "10000"), (0, Strings.Clipboard.Unlimited),
    ];

    private static readonly IReadOnlyList<(int Value, string Label)> Retention =
    [
        (1, Strings.Clipboard.OneDay),
        (7, string.Format(Strings.Clipboard.DaysFormat, 7)),
        (30, string.Format(Strings.Clipboard.DaysFormat, 30)),
        (90, string.Format(Strings.Clipboard.DaysFormat, 90)),
        (365, string.Format(Strings.Clipboard.DaysFormat, 365)),
        (0, Strings.Clipboard.Forever),
    ];

    public string? FeatureId => "clipboard-history";

    public string PageId => SettingsPages.Clipboard;

    public string? Anchor => "clipboardHistory";

    public string? Title => Loc.T("Clipboard");

    public IReadOnlyList<string> Keywords =>
        ["history", "Save clipboard history", "History shortcut", "History layout", "List", "Cards", "Limit", "Keep items for", "Apps to skip", "sensitive", "Auto clear", "Show in panel", "images and files"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var app = c.App;
        ClipboardHistoryFeature.RegisterDefaults(app);
        var enabled = app.Get<bool>("clipboardHistoryEnabled");
        var shortcutEnabled = app.Get<bool>("clipboardHistoryShortcutEnabled");
        var host = Hub.Clipboard;

        // H1 -------------------------------------------------------------------------------------------------------
        var h1 = new List<UIElement?>
        {
            SettingsKit.Toggle(app, "clipboardHistoryEnabled", Strings.Clipboard.Enable, Strings.Clipboard.Caption, _ =>
            {
                host?.Sync();
                c.RequestRebuild();
            }),
            Ui.Caption(Strings.Clipboard.LocalNote, 11),
        };
        if (enabled && c.Runtime.IsRunning("clipboard-history"))
        {
            h1.Add(Ui.ActiveLabel(Strings.Clipboard.Active));
        }

        // H2 -------------------------------------------------------------------------------------------------------
        var layoutPicker = new SegmentedControl([Strings.Clipboard.LayoutList, Strings.Clipboard.LayoutCards], app.Get<string>("clipboardHistoryLayout") == "cards" ? 1 : 0, false, 12);
        layoutPicker.SelectionChanged += index => app.Set("clipboardHistoryLayout", index == 1 ? "cards" : "list");
        var openNow = UiKit.Button(Strings.Clipboard.Shortcut, ButtonKind.Bordered, 12, G.Clipboard, () => host?.ShowWindow());
        openNow.HorizontalAlignment = HorizontalAlignment.Left;
        openNow.IsEnabled = host is not null && host.Store.Entries.Count > 0;
        var h2Rows = new List<UIElement?>
        {
            SettingsKit.Disable(SettingsKit.Toggle(app, "clipboardHistoryShortcutEnabled", Strings.Clipboard.Shortcut, null, _ => c.RequestRebuild()), !enabled),
            SettingsKit.Disable(SettingsKit.ShortcutRow(c, "clipboardHistory", Loc.T("Shortcut"), enabled && shortcutEnabled), !(enabled && shortcutEnabled)),
            Ui.Caption(Strings.Clipboard.ShortcutCaption),
            SettingsKit.FieldRow(Strings.Clipboard.HistoryLayout, layoutPicker, null),
            SettingsKit.Toggle(app, "clipboardHistoryShowSourceAppIcons", Strings.Clipboard.ShowSourceIcons, null),
            openNow,
        };

        // H3 -------------------------------------------------------------------------------------------------------
        var previewOn = app.Get<bool>("clipboardHistoryMenuBarPreview");
        var length = Ui.NumberBox(app.Get<int>("clipboardHistoryMenuBarPreviewLength"), 5, 50, 50, v => app.Set("clipboardHistoryMenuBarPreviewLength", v), enabled && previewOn);
        var h3Rows = new List<UIElement?>
        {
            SettingsKit.Disable(SettingsKit.Toggle(app, "clipboardHistoryMenuBarPreview", Strings.Clipboard.TrayPreview, null, _ => c.RequestRebuild()), !enabled),
            SettingsKit.Disable(SettingsKit.FieldRow(Strings.Clipboard.TrayPreviewLength, length, Strings.Clipboard.TrayPreviewSuffix), !(enabled && previewOn)),
            Ui.Caption(Strings.Clipboard.TrayPreviewCaption),
        };

        // H4 -------------------------------------------------------------------------------------------------------
        var limit = app.Get<int>("clipboardHistoryLimit");
        var retention = app.Get<int>("clipboardHistoryRetentionDays");
        var ignored = new AppListControl(
            Strings.Clipboard.IgnoredTitle,
            Strings.Clipboard.IgnoredAdd,
            Strings.Clipboard.IgnoredCaption,
            () => app.Get<List<string>>("clipboardHistoryIgnoredApps") ?? [],
            list => app.Set("clipboardHistoryIgnoredApps", list),
            Strings.Clipboard.IgnoredRemove);
        var h4Rows = new List<UIElement?>
        {
            SettingsKit.Toggle(app, "clipboardHistoryIncludeImagesFiles", Strings.Clipboard.IncludeImagesFiles, Strings.Clipboard.IncludeImagesFilesCaption),
            SettingsKit.Toggle(app, "clipboardHistoryFilterSensitiveText", Strings.Clipboard.SkipSensitive, Strings.Clipboard.SkipSensitiveCaption),
            ignored,
            SettingsKit.Picker(Strings.Clipboard.Limit, Limits, Limits.Any(l => l.Value == limit) ? limit : 50, v => app.Set("clipboardHistoryLimit", v)),
            SettingsKit.Picker(Strings.Clipboard.RetentionTitle, Retention, Retention.Any(l => l.Value == retention) ? retention : 0, v => app.Set("clipboardHistoryRetentionDays", v)),
            Ui.Caption(Strings.Clipboard.RetentionCaption),
        };

        // H5 -------------------------------------------------------------------------------------------------------
        // The tray reads the same Vorssaint key (panelUtilityClipboard): one source of truth.
        var h5 = SettingsKit.Toggle(app, "panelUtilityClipboard", Strings.Clipboard.ShowInPanel, null, null, app.Get<bool>("panelUtilityClipboard"));

        // H6 -------------------------------------------------------------------------------------------------------
        var delayOn = app.Get<bool>("clipboardAutoClearOnDelay");
        var delayBox = Ui.NumberBox(app.Get<int>("clipboardAutoClearDelaySeconds"), 5, 3600, 60, v => app.Set("clipboardAutoClearDelaySeconds", v), delayOn);
        var delayRow = new Grid();
        delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var delayTitle = UiKit.Text(Strings.Clipboard.AutoClearEnable, 13, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
        delayTitle.VerticalAlignment = VerticalAlignment.Center;
        delayRow.Children.Add(delayTitle);
        var delaySwitch = new MacSwitch(delayOn, SwitchSize.Regular, v =>
        {
            app.Set("clipboardAutoClearOnDelay", v);
            delayBox.IsEnabled = v;
            SettingsKit.Disable(delayBox, !v);
        });
        delaySwitch.Margin = new Thickness(12, 0, 12, 0);
        Grid.SetColumn(delaySwitch, 1);
        delayRow.Children.Add(delaySwitch);
        Grid.SetColumn(delayBox, 2);
        delayRow.Children.Add(delayBox);
        var seconds = UiKit.Text(Strings.Clipboard.AutoClearSeconds, 12.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        seconds.Margin = new Thickness(8, 0, 0, 0);
        seconds.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(seconds, 3);
        delayRow.Children.Add(seconds);
        SettingsKit.Disable(delayBox, !delayOn);

        var h6Rows = new List<UIElement?>
        {
            delayRow,
            SettingsKit.Toggle(app, "clipboardAutoClearOnSleep", Strings.Clipboard.AutoClearOnSleep, null),
            SettingsKit.Toggle(app, "clipboardAutoClearOnDisplaySleep", Strings.Clipboard.AutoClearOnDisplaySleep, null),
            SettingsKit.Toggle(app, "clipboardAutoClearOnScreenLock", Strings.Clipboard.AutoClearOnScreenLock, null),
            Ui.Caption(Strings.Clipboard.AutoClearCaption),
        };

        var page = new StackPanel();
        page.AddSpaced(SettingsKit.Section(null, [.. h1]), 20);
        page.AddSpaced(SettingsKit.Section(Strings.Clipboard.ShortcutSection, [.. h2Rows]), 20);
        page.AddSpaced(SettingsKit.Section(null, [.. h3Rows]), 20);
        // Everything below the capture switch is dimmed while history is off (the quick window and the panel still open).
        var h4 = SettingsKit.Section(null, [.. h4Rows]);
        SettingsKit.Disable(((SettingsCard)h4.Children[^1]).Content, !enabled);
        page.AddSpaced(h4, 20);
        page.AddSpaced(SettingsKit.Section(null, h5), 20);
        page.AddSpaced(SettingsKit.Section(null, [.. h6Rows]), 20);
        return page;
    }
}

/// <summary>Clipboard page, block P: "Paste copied images as files" (belongs to Cut &amp; paste).</summary>
public sealed class ClipboardImageFileSection : ISettingsSection
{
    public string? FeatureId => "finder-cut-paste";

    public string PageId => SettingsPages.Clipboard;

    public string? Anchor => "pasteImageAsFile";

    public string? Title => Loc.T("Paste copied images as files");

    public IReadOnlyList<string> Keywords => ["image", "PNG", "Explorer", "folder"];

    public int Order => 20;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        c.App.Register("finderPasteImageAsFile", false);
        var on = c.App.Get<bool>("finderPasteImageAsFile");
        var rows = new List<UIElement?>
        {
            SettingsKit.Toggle(c.App, "finderPasteImageAsFile", Strings.Clipboard.PasteImageAsFile, Strings.Clipboard.PasteImageAsFileCaption),
        };
        if (on && ElevationNote.NeedsNote())
        {
            rows.Add(Ui.Warning(Strings.Clipboard.AdminNote));
        }

        return SettingsKit.Section(null, [.. rows]);
    }
}

/// <summary>Clipboard page, block PP: Paste as plain text (switch, shortcut).</summary>
public sealed class PastePlainSettingsSection : ISettingsSection
{
    public string? FeatureId => "paste-plain";

    public string PageId => SettingsPages.Clipboard;

    public string? Anchor => "pastePlain";

    public string? Title => Strings.PastePlain.Name;

    public IReadOnlyList<string> Keywords => ["plain text", "formatting", "paste"];

    public int Order => 30;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        c.App.Register("pastePlainEnabled", false);
        var enabled = c.App.Get<bool>("pastePlainEnabled");
        return SettingsKit.Section(
            Strings.PastePlain.Name,
            SettingsKit.Toggle(c.App, "pastePlainEnabled", Strings.PastePlain.Name, Strings.PastePlain.Caption, _ => c.RequestRebuild()),
            SettingsKit.Disable(SettingsKit.ShortcutRow(c, "pastePlain", Loc.T("Shortcut"), enabled), !enabled));
    }
}

/// <summary>Clipboard page, block S: "N Pinned · M Recent" and "Clear unpinned".</summary>
public sealed class ClipboardStatsSection : ISettingsSection
{
    public string? FeatureId => "clipboard-history";

    public string PageId => SettingsPages.Clipboard;

    public string? Anchor => "clipboardStats";

    public string? Title => null;

    public int Order => 40;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var host = Hub.Clipboard;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stats = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var pinnedCount = host?.Store.Pinned.Count() ?? 0;
        var recentCount = host?.Store.Recent.Count() ?? 0;
        stats.Children.Add(UiKit.Text(pinnedCount.ToString(), 13, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var pinnedLabel = UiKit.Text(" " + Strings.Clipboard.Pinned, 13, FontWeights.Normal, ThemeKeys.TextSecondary);
        stats.Children.Add(pinnedLabel);
        stats.Children.Add(UiKit.Text("  ·  ", 13, FontWeights.Normal, ThemeKeys.TextTertiary));
        stats.Children.Add(UiKit.Text(recentCount.ToString(), 13, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        stats.Children.Add(UiKit.Text(" " + Strings.Clipboard.Recent, 13, FontWeights.Normal, ThemeKeys.TextSecondary));
        row.Children.Add(stats);
        var clear = UiKit.Button(Strings.Clipboard.ClearRecent, ButtonKind.Bordered, 12);
        clear.IsEnabled = recentCount > 0;
        clear.Click += (_, _) =>
        {
            if (host is null)
            {
                return;
            }

            var snapshot = host.Store.RecentSnapshot();
            var answer = MessageBox.Show(
                Strings.Clipboard.ClearRecentConfirmMessage,
                string.Format(Strings.Clipboard.ClearRecentConfirmFormat, snapshot.Count),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (answer == MessageBoxResult.OK)
            {
                host.Store.ClearRecent(snapshot);
                c.RequestRebuild();
            }
        };
        Grid.SetColumn(clear, 1);
        row.Children.Add(clear);
        var card = new SettingsCard();
        card.Add(row);
        return card;
    }
}

/// <summary>The Utilities-tab row "Clipboard" that hosts the compact history panel.</summary>
public sealed class ClipboardUtility : IPanelUtility
{
    private static event Action? StaticChanged;

    public string? FeatureId => "clipboard-history";

    public string Id => "clipboard";

    public string Title => Loc.T("Clipboard");

    public string Glyph => "";

    public int Order => 70;

    public string? Caption => IsEnabled ? Strings.Clipboard.Caption : Strings.Clipboard.Disabled;

    public bool CaptionStaysVisible => !IsEnabled;

    public string? ShortcutText => IsEnabled ? Hub.Clipboard?.ShortcutText : null;

    public string? SettingsDestination => SettingsPages.Clipboard;

    public event Action? Changed
    {
        add => StaticChanged += value;
        remove => StaticChanged -= value;
    }

    private static bool IsEnabled => Hub.Clipboard?.HistoryEnabled ?? false;

    internal static void Raise() => StaticChanged?.Invoke();

    public FrameworkElement? CreateHostedView(IPanelContext context) => new ClipboardPanelView(context);
}

/// <summary>Hosted panel view of the clipboard history (spec 01 6.1): switch, search, clear, open window and a compact list.</summary>
internal sealed class ClipboardPanelView : StackPanel
{
    private readonly IPanelContext _context;
    private readonly IClipboardHost? _host;
    private readonly StackPanel _list = new();
    private readonly TextBox _search;
    private readonly Border _clearButton;
    private string _query = string.Empty;
    private string? _copiedId;

    public ClipboardPanelView(IPanelContext context)
    {
        _context = context;
        _host = Hub.Clipboard;
        ClipboardHistoryFeature.RegisterDefaults(context.App);
        context.SetKeepOpen(true);
        var app = context.App;
        var enabled = app.Get<bool>("clipboardHistoryEnabled");

        var controls = new StackPanel();
        var check = new CheckBoxLite(Strings.Clipboard.Enable, enabled, on =>
        {
            app.Set("clipboardHistoryEnabled", on);
            _host?.Sync();
            ClipboardUtility.Raise();
            Rebuild();
        }, 11.5);
        controls.Children.Add(check);
        var caption = UiKit.Text(enabled ? Strings.Clipboard.Caption : Strings.Clipboard.Disabled, 10, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.Margin = new Thickness(0, 7, 0, 0);
        controls.Children.Add(caption);
        if (enabled && app.Get<bool>("clipboardHistoryShortcutEnabled") && _host?.ShortcutText is { } shortcut)
        {
            var chip = UiKit.Text(string.Format(Strings.Clipboard.PanelShortcutFormat, shortcut), 9.5, FontWeights.Medium, ThemeKeys.TextTertiary);
            chip.Margin = new Thickness(0, 7, 0, 0);
            controls.Children.Add(chip);
        }

        var row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var searchHost = Ui.PlaceholderBox(out _search, Strings.Clipboard.Search, 11);
        _search.IsEnabled = _host is not null && _host.Store.Entries.Count > 0;
        _search.TextChanged += (_, _) =>
        {
            _query = _search.Text;
            Rebuild();
        };
        row.Children.Add(searchHost);
        _clearButton = Ui.IconButton(G.Delete, Strings.Clipboard.ClearRecent, ConfirmClear, 24, 11, 5);
        _clearButton.Height = 22;
        _clearButton.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_clearButton, 1);
        row.Children.Add(_clearButton);
        var open = Ui.IconButton(G.Open, Strings.Clipboard.Shortcut, () =>
        {
            _context.InvokeAfterClose(() => _host?.ShowWindow(), 150);
        }, 24, 11, 5);
        open.Height = 22;
        open.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(open, 2);
        row.Children.Add(open);
        controls.Children.Add(row);

        Children.Add(new PanelCard(controls));
        _list.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(_list);
        Rebuild();
        if (_host is not null)
        {
            _host.Store.Changed += OnStoreChanged;
            Unloaded += (_, _) => _host.Store.Changed -= OnStoreChanged;
        }
    }

    private void OnStoreChanged() => Dispatcher.BeginInvoke(Rebuild);

    private void ConfirmClear()
    {
        if (_host is null)
        {
            return;
        }

        var snapshot = _host.Store.RecentSnapshot();
        if (snapshot.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            Strings.Clipboard.ClearRecentConfirmMessage,
            string.Format(Strings.Clipboard.ClearRecentConfirmFormat, snapshot.Count),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer == MessageBoxResult.OK)
        {
            _host.Store.ClearRecent(snapshot);
            _copiedId = null;
        }
    }

    private bool CanReorder => string.IsNullOrWhiteSpace(_query);

    private void Rebuild()
    {
        _list.Children.Clear();
        _clearButton.IsEnabled = _host is not null && _host.Store.Recent.Any();
        _clearButton.Opacity = _clearButton.IsEnabled ? 1 : 0.4;
        if (_host is null)
        {
            return;
        }

        var entries = _host.Store.Filter(_query);
        if (_host.Store.Entries.Count == 0 || entries.Count == 0)
        {
            var empty = UiKit.Text(_host.Store.Entries.Count == 0 ? Strings.Clipboard.Empty : Strings.Clipboard.NoResults, 10.5, FontWeights.Normal, ThemeKeys.TextTertiary);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            var card = new PanelCard(empty) { Height = 72 };
            _list.Children.Add(card);
            return;
        }

        var stack = new StackPanel();
        foreach (var entry in entries.Take(200))
        {
            stack.AddSpaced(EntryCard(entry), 7);
        }

        _list.Children.Add(new ScrollViewer { MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack });
    }

    private UIElement EntryCard(ClipEntry entry)
    {
        var host = _host!;
        var stack = new StackPanel();
        if (entry.IsPinned)
        {
            var pinned = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            pinned.Children.Add(UiKit.Icon(G.PinFill, 9.5, ThemeKeys.Accent, FontWeights.Bold));
            var label = UiKit.Text(Strings.Clipboard.Pinned, 9.5, FontWeights.Bold, ThemeKeys.Accent);
            label.Margin = new Thickness(4, 0, 0, 0);
            pinned.Children.Add(label);
            stack.Children.Add(pinned);
        }

        stack.Children.Add(Preview(entry));
        var tokens = ClipSearch.Tokens(_query);
        _ = tokens;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        var up = Ui.IconButton(G.Up, Strings.Clipboard.MoveUp, () => host.Store.Move(entry, -1), 24, 9, 5);
        up.Height = 22;
        up.IsEnabled = CanReorder && host.Store.CanMove(entry, -1);
        up.Opacity = up.IsEnabled ? 1 : 0.4;
        var down = Ui.IconButton(G.Down, Strings.Clipboard.MoveDown, () => host.Store.Move(entry, 1), 24, 9, 5);
        down.Height = 22;
        down.Margin = new Thickness(6, 0, 0, 0);
        down.IsEnabled = CanReorder && host.Store.CanMove(entry, 1);
        down.Opacity = down.IsEnabled ? 1 : 0.4;
        var pin = Ui.IconButton(entry.IsPinned ? G.PinFill : G.Pin, entry.IsPinned ? Strings.Clipboard.Unpin : Strings.Clipboard.Pin, () => host.Store.TogglePin(entry, host.Limit, host.RetentionDays), 24, 10, 5);
        pin.Height = 22;
        pin.Margin = new Thickness(6, 0, 0, 0);
        actions.Children.Add(up);
        actions.Children.Add(down);
        actions.Children.Add(pin);
        if (entry.IsImage && host.CanEditImages)
        {
            var edit = Ui.IconButton(G.Edit, Strings.Clipboard.Edit, () => host.EditImage(entry), 24, 10, 5);
            edit.Height = 22;
            edit.Margin = new Thickness(6, 0, 0, 0);
            actions.Children.Add(edit);
        }

        var copied = _copiedId == entry.Id;
        var copy = UiKit.Button(copied ? Strings.Clipboard.Copied : Strings.Clipboard.Copy, ButtonKind.Prominent, 10, copied ? G.Check : G.Copy);
        copy.Padding = new Thickness(8, 2, 8, 2);
        copy.Margin = new Thickness(6, 0, 0, 0);
        copy.Click += (_, _) =>
        {
            host.PasteEntries([entry], paste: false, ok =>
            {
                if (ok)
                {
                    _copiedId = entry.Id;
                    Dispatcher.BeginInvoke(Rebuild);
                }
            });
        };
        actions.Children.Add(copy);
        var delete = Ui.IconButton(G.Delete, Strings.Clipboard.Delete, () => host.Store.Remove(entry), 24, 10, 5);
        delete.Height = 22;
        delete.Margin = new Thickness(6, 0, 0, 0);
        actions.Children.Add(delete);
        var time = UiKit.Text(entry.CopiedAt.ToLocalTime().ToString("t"), 9.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        time.VerticalAlignment = VerticalAlignment.Center;
        time.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(time);
        stack.Children.Add(actions);
        return new PanelCard(stack);
    }

    private UIElement Preview(ClipEntry entry)
    {
        var tokens = ClipSearch.Tokens(_query);
        switch (entry.Kind)
        {
            case ClipKinds.Text:
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                if (entry.ColorValue() is { } color)
                {
                    var sw = EntryContent.Swatch(color, 12);
                    sw.Margin = new Thickness(0, 0, 7, 0);
                    row.Children.Add(sw);
                }

                var text = EntryContent.Highlighted(entry.Preview(), tokens, 10.5, ThemeKeys.TextPrimary, 3);
                text.MaxWidth = row.Children.Count > 0 ? 250 : 280;
                row.Children.Add(text);
                return row;
            case ClipKinds.Image:
                var imageRow = new StackPanel { Orientation = Orientation.Horizontal };
                if (entry.ImageFile is not null)
                {
                    var image = EntryContent.Thumbnail(_host!.Store.ImagePath(entry), 220, 110, 40);
                    image.Margin = new Thickness(0, 0, 7, 0);
                    imageRow.Children.Add(image);
                }

                imageRow.Children.Add(UiKit.Text($"{Strings.Clipboard.ImageLabel} · {entry.ImageDimensionsLabel}", 10, FontWeights.Normal, ThemeKeys.TextSecondary));
                return imageRow;
            default:
                var files = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = string.Join("\n", entry.FilePaths) };
                if (entry.FilePaths.Count == 1 && ClipImages.IsImageFile(entry.FilePaths[0]))
                {
                    var image = EntryContent.Thumbnail(entry.FilePaths[0], 220, 110, 40);
                    image.Margin = new Thickness(0, 0, 7, 0);
                    files.Children.Add(image);
                }
                else
                {
                    var glyph = UiKit.Icon(G.Folder, 11, ThemeKeys.TextSecondary, FontWeights.SemiBold);
                    glyph.Margin = new Thickness(0, 0, 7, 0);
                    files.Children.Add(glyph);
                }

                var title = entry.FilePaths.Count == 1 ? entry.FileNames.FirstOrDefault() ?? entry.Preview() : string.Format(Strings.Clipboard.FileCountFormat, entry.FilePaths.Count);
                var name = EntryContent.Highlighted(title, entry.FilePaths.Count == 1 ? tokens : [], 10.5, ThemeKeys.TextPrimary, 2);
                name.MaxWidth = 220;
                files.Children.Add(name);
                return files;
        }
    }
}

/// <summary>Helper for the optional "run WinKnife as administrator" note.</summary>
internal static class ElevationNote
{
    /// <summary>True when WinKnife is not elevated and the foreground app is (so hooks and SendInput cannot reach it).</summary>
    public static bool NeedsNote()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return !principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) && ForegroundIsElevated();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ForegroundIsElevated()
    {
        try
        {
            var hwnd = WinKnife.Core.Native.NativeMethods.GetForegroundWindow();
            WinKnife.Core.Native.NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            _ = process.MainModule; // throws Access denied for an elevated process
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
