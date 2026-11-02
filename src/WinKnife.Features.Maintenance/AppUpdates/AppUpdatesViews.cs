// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Maintenance;

/// <summary>The update list shared by the Settings page and the panel (spec 7.3.3, reference highlights-appupdates.jpg).</summary>
internal sealed class AppUpdatesListView : ContentControl
{
    private static bool _rulesOpen;
    private readonly AppUpdatesService _s = AppUpdatesService.Shared;
    private readonly bool _compact;
    private readonly Action? _relayout;
    private string _opKey = string.Empty;

    public AppUpdatesListView(bool compact, Action? relayout = null)
    {
        _compact = compact;
        _relayout = relayout;
        Loaded += (_, _) =>
        {
            _s.Changed += OnChanged;
            WingetOperations.Shared.Changed += OnOperation;
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            _s.Changed -= OnChanged;
            WingetOperations.Shared.Changed -= OnOperation;
        };
        Rebuild();
    }

    private void OnChanged() => Rebuild();

    private void OnOperation()
    {
        var status = WingetOperations.Shared.Status;
        var key = $"{status?.IsRunning}|{status?.State}";
        if (key != _opKey)
        {
            _opKey = key;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        try
        {
            RebuildCore();
        }
        catch (Exception ex)
        {
            Host.Log("App updates view failed", ex);
        }
    }

    private void RebuildCore()
    {
        var spacing = _compact ? 8.0 : 12.0;
        var root = new StackPanel();
        void Add(UIElement element, double top = -1)
        {
            if (element is FrameworkElement fe)
            {
                fe.Margin = new Thickness(fe.Margin.Left, root.Children.Count == 0 ? 0 : (top >= 0 ? top : spacing), fe.Margin.Right, fe.Margin.Bottom);
            }

            root.Children.Add(element);
        }

        // 1. summary row
        var last = _s.LastCheck;
        var lastText = UiKit.Text(last is null ? Loc.T("Not checked yet") : Loc.F("Last checked {0}", Fmt.Ago(last.Value)), _compact ? 10.5 : 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        lastText.VerticalAlignment = VerticalAlignment.Center;
        FrameworkElement check;
        if (_s.IsChecking)
        {
            check = V.Row(6, new Spinner(12), UiKit.Text(Loc.T("Checking"), _compact ? 10.5 : 12, FontWeights.Medium, ThemeKeys.TextPrimary));
            var wrap = new Border { Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(6), Child = check };
            wrap.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsControl);
            check = wrap;
        }
        else
        {
            var button = UiKit.Button(Loc.T("Check now"), ButtonKind.Bordered, _compact ? 10.5 : 12, G.Refresh, () => _ = _s.CheckAsync(false));
            button.FontWeight = FontWeights.Medium;
            button.IsEnabled = !_s.IsBusy;
            check = button;
        }

        Add(V.Split(lastText, check));

        // 2/3. rows or empty state
        if (_s.Items.Count == 0)
        {
            var empty = new StackPanel();
            if (_s.HasCheckedThisSession && !_s.IsChecking)
            {
                var (glyph, text, brush) = _s.VersionsUncompared || _s.CoverageIncomplete
                    ? (Glyphs.Info, Loc.T("No updates found in this partial check"), ThemeKeys.TextSecondary)
                    : _s.Rules.Count > 0
                        ? (Glyphs.Info, Loc.T("No updates outside your rules"), ThemeKeys.TextSecondary)
                        : (Glyphs.CheckCircle, Loc.T("No updates found"), ThemeKeys.Green);
                empty.Children.Add(V.Label(glyph, text, _compact ? 10.5 : 12, FontWeights.Medium, brush, ThemeKeys.TextPrimary));
            }

            var note = V.Secondary(Loc.T("Checks the original sources of installed apps and a public catalog. Updates install through their original source."), _compact ? 9.5 : 11);
            note.Margin = new Thickness(0, empty.Children.Count > 0 ? 6 : 0, 0, 0);
            empty.Children.Add(note);
            if (_s.HasCheckedThisSession && !WingetCli.IsAvailable)
            {
                var missing = V.Secondary(Loc.T("winget is not installed, so apps cannot be updated from here yet."), _compact ? 9.5 : 11);
                missing.Margin = new Thickness(0, 6, 0, 0);
                empty.Children.Add(missing);
            }

            Add(empty);
        }
        else
        {
            var anySelectable = _s.Items.Any(i => i.Selectable);
            if (anySelectable)
            {
                var selectable = _s.Items.Where(i => i.Selectable).ToList();
                var all = V.Link(Loc.T("Select all"), _s.SelectAll, _compact ? 10 : 11, selectable.Any(i => !_s.SelectedIds.Contains(i.Id)));
                var none = V.Link(Loc.T("Clear"), _s.SelectNone, _compact ? 10 : 11, _s.SelectedIds.Count > 0);
                Add(V.Row(12, all, none));
            }

            var list = new StackPanel();
            foreach (var item in _s.Items)
            {
                var row = Row(item);
                row.Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 5, 0, 0);
                list.Children.Add(row);
            }

            if (_compact)
            {
                var visible = Math.Min(Math.Max(_s.Items.Count, 1), 4);
                var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = visible * 40 + (visible - 1) * 5, Content = list };
                Add(scroll);
            }
            else
            {
                Add(list);
            }

            if (anySelectable)
            {
                var selected = _s.Items.Count(i => i.Selectable && _s.SelectedIds.Contains(i.Id));
                var update = UiKit.Button(Loc.F("Update {0}", selected), ButtonKind.Prominent, _compact ? 11 : 12, null, _s.UpdateSelected);
                update.FontWeight = FontWeights.SemiBold;
                update.IsEnabled = selected > 0 && !_s.IsBusy;
                update.HorizontalAlignment = _compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
                update.Padding = new Thickness(_compact ? 8 : 16, _compact ? 4 : 6, _compact ? 8 : 16, _compact ? 4 : 6);
                Add(update);
            }
        }

        // 4. exceptions disclosure
        var title = UiKit.Text(Loc.T("Update exceptions"), _compact ? 10.5 : 12, FontWeights.Normal, ThemeKeys.TextPrimary);
        var disclosure = V.Disclosure(title, RulesBody, _rulesOpen, open => _rulesOpen = open);
        Add(disclosure);

        // 5. incomplete
        if (_s.CoverageIncomplete)
        {
            var block = new StackPanel();
            block.Children.Add(V.Label(Glyphs.Warning, Loc.T("Check incomplete"), 11.5, FontWeights.Medium, ThemeKeys.Orange, ThemeKeys.Orange));
            if (_s.UncheckedAppNames.Count > 0)
            {
                var names = V.Secondary(string.Join(", ", _s.UncheckedAppNames), _compact ? 9.5 : 11);
                names.ToolTip = string.Join(", ", _s.UncheckedAppNames);
                names.Margin = new Thickness(0, 4, 0, 0);
                if (_compact)
                {
                    names.MaxHeight = 40;
                }

                block.Children.Add(names);
            }

            var failed = V.Secondary(Loc.T("The online check could not be completed. Other results are still shown."), _compact ? 9.5 : 11);
            failed.Margin = new Thickness(0, 4, 0, 0);
            block.Children.Add(failed);
            if (!_s.StoreComplete)
            {
                var open = V.Link(Loc.T("Open the Microsoft Store"), _s.OpenStoreUpdates, 11);
                open.HorizontalAlignment = HorizontalAlignment.Left;
                open.Margin = new Thickness(0, 4, 0, 0);
                block.Children.Add(open);
            }

            Add(block);
        }

        // 6. uncomparable
        if (_s.VersionsUncompared)
        {
            var block = new StackPanel();
            block.Children.Add(V.Label(Glyphs.Info, Loc.T("Versions not compared"), 11.5, FontWeights.Medium, ThemeKeys.TextSecondary, ThemeKeys.TextSecondary));
            var names = V.Secondary(string.Join(", ", _s.UncomparableAppNames), _compact ? 9.5 : 11);
            names.Margin = new Thickness(0, 4, 0, 0);
            block.Children.Add(names);
            var note = V.Tertiary(Loc.T("No update is shown when an app’s own version can’t be compared with the catalog. Check for updates in the app itself."), _compact ? 9.5 : 11);
            note.Margin = new Thickness(0, 4, 0, 0);
            block.Children.Add(note);
            Add(block);
        }

        // 7. operation status
        var status = new OperationStatusView(_compact);
        status.IsVisibleChanged += (_, _) => _relayout?.Invoke();
        Add(status);

        // 8. error
        if (_s.LastError is { Length: > 0 } error)
        {
            var label = V.Label(Glyphs.Warning, error, _compact ? 10 : 11, FontWeights.Normal, ThemeKeys.Orange, ThemeKeys.Orange);
            foreach (var t in label.Children.OfType<TextBlock>().Skip(1))
            {
                t.TextWrapping = TextWrapping.Wrap;
                t.MaxHeight = 50;
            }

            Add(label);
        }

        Content = root;
        _relayout?.Invoke();
    }

    private FrameworkElement RulesBody()
    {
        var body = new StackPanel { Margin = new Thickness(16, 6, 0, 0) };
        var hint = V.Secondary(Loc.T("Skipping a version still allows newer releases. After removing an app exclusion, use Check now to refresh it."), _compact ? 9.5 : 11);
        var add = V.IconButton(Glyphs.Add, Loc.T("Add an app that should not be checked"), AddException, 11, ThemeKeys.TextPrimary, 24);
        add.IsEnabled = !_s.IsBusy;
        body.Children.Add(V.Split(hint, add));
        var rules = _s.Rules;
        var inner = new StackPanel();
        foreach (var rule in rules)
        {
            var left = new StackPanel();
            var name = UiKit.Text(rule.Name, _compact ? 11 : 12, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            name.ToolTip = rule.BundleId;
            left.Children.Add(name);
            left.Children.Add(UiKit.Text(rule.Version is null ? Loc.T("Not checked until this rule is removed") : Loc.F("Skipped version {0}", rule.Version), _compact ? 9.5 : 11, FontWeights.Normal, ThemeKeys.TextSecondary));
            var remove = V.Mini(Loc.T("Remove"), () => _s.RemoveRule(rule));
            remove.IsEnabled = !_s.IsBusy;
            System.Windows.Automation.AutomationProperties.SetName(remove, $"Remove: {rule.Name}");
            var row = V.Split(left, remove);
            row.Margin = new Thickness(0, 6, 0, 0);
            row.Height = _compact ? 48 : double.NaN;
            inner.Children.Add(row);
        }

        if (rules.Count > 0)
        {
            FrameworkElement list = inner;
            if (_compact)
            {
                list = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = Math.Min(rules.Count, 3) * 54, Content = inner };
            }

            list.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(list);
        }

        return body;
    }

    private void AddException()
    {
        var picked = Dialogs.OpenFiles(Loc.T("Add an app that should not be checked"), Loc.T("Programs (*.exe)|*.exe"), false);
        if (picked.Length == 0)
        {
            return;
        }

        var path = picked[0];
        var programs = ProgramCatalog.Enumerate(false);
        var match = programs.FirstOrDefault(p => !string.IsNullOrEmpty(p.InstallLocation) && FileOps.IsUnder(path, p.InstallLocation))
            ?? programs.FirstOrDefault(p => Icons.CleanIconPath(p.DisplayIcon)?.Equals(path, StringComparison.OrdinalIgnoreCase) == true);
        var name = match?.Name ?? Path.GetFileNameWithoutExtension(path);
        var id = match?.Key ?? Path.GetFileNameWithoutExtension(path);
        // a winget-managed program is known by its winget id: prefer the id of an existing row
        var row = _s.Items.FirstOrDefault(i => Norm.Of(i.Name) == Norm.Of(name));
        _s.Exclude(row?.RuleId ?? id, name);
    }

    private FrameworkElement Row(UpdateItem item)
    {
        var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 5, 7, 5), Height = _compact ? 40 : double.NaN };
        border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((_compact ? 22 : 26) + 18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (item.Selectable)
        {
            var check = new CheckMark(_s.SelectedIds.Contains(item.Id), _ => _s.Toggle(item));
            System.Windows.Automation.AutomationProperties.SetName(check, item.Name);
            grid.Children.Add(check);
        }

        var icon = new Image { Width = _compact ? 22 : 26, Height = _compact ? 22 : 26, HorizontalAlignment = HorizontalAlignment.Center, Source = Icons.Of(item.IconPath ?? item.OpenPath, true) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line1 = new StackPanel { Orientation = Orientation.Horizontal };
        var name = UiKit.Text(item.Name, _compact ? 11.5 : 12.5, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        name.MaxWidth = _compact ? 120 : 260;
        line1.Children.Add(name);
        var pill = V.Pill(item.Source switch { UpdateSource.Package => Loc.T("winget"), UpdateSource.Store => Loc.T("Microsoft Store"), _ => Loc.T("Online") });
        pill.Margin = new Thickness(5, 0, 0, 0);
        line1.Children.Add(pill);
        text.Children.Add(line1);
        text.Children.Add(UiKit.Text(item.VersionSummary, _compact ? 9.5 : 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var (label, tip) = item.Source switch
        {
            UpdateSource.Package => (Loc.T("Update"), Loc.T("Update")),
            UpdateSource.Store => (Loc.T("Microsoft Store"), Loc.T("Opens the Microsoft Store, where this update is installed")),
            _ => (Loc.T("Open"), Loc.T("Opens the app so its own updater can finish")),
        };
        var action = UiKit.Button(label, ButtonKind.Bordered, _compact ? 10 : 11, null, () => _s.Update(item));
        action.FontWeight = FontWeights.Medium;
        action.ToolTip = tip;
        action.IsEnabled = !_s.IsBusy;
        action.Padding = new Thickness(8, 2, 8, 2);
        action.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(action, 3);
        grid.Children.Add(action);

        var menu = V.Menu(
            (Loc.F("Skip version {0}", item.Latest), () => _s.SkipVersion(item), !_s.IsChecking && !VersionCompare.IsUncomparable(item.Latest)),
            (Loc.T("Don’t check this app"), () => _s.ExcludeApp(item), !_s.IsChecking));
        border.ContextMenu = menu;
        if (!_compact)
        {
            var more = new Button { Content = UiKit.Icon(Glyphs.More, 13, ThemeKeys.TextSecondary), ToolTip = Loc.F("More options: {0}", item.Name), Margin = new Thickness(4, 0, 0, 0), Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            more.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
            more.Padding = new Thickness(0);
            more.Click += (_, _) =>
            {
                menu.PlacementTarget = more;
                menu.IsOpen = true;
            };
            Grid.SetColumn(more, 4);
            grid.Children.Add(more);
        }

        border.Child = grid;
        return border;
    }
}

/// <summary>Settings page "App updates": four sections (spec 7.3.2).</summary>
public sealed class AppUpdatesSettingsSection : ISettingsSection
{
    public string? FeatureId => "app-updates";

    public string PageId => SettingsPages.AppUpdates;

    public string? Title => Loc.T("App updates");

    public IReadOnlyList<string> Keywords => ["Check now", "Check in the background", "Microsoft Store", "winget", "Sources", "Update exceptions"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        Host.Attach(c);
        var service = AppUpdatesService.Shared;
        service.ReloadRules();
        var page = new StackPanel();

        // 1. list
        var list = new SettingsCard(Loc.T("App updates"));
        list.AddCaption(Loc.T("Looks for newer versions of the apps on this PC and helps you finish each update from its original source."));
        list.Add(new AppUpdatesListView(false));
        page.Children.Add(list);

        // 2. background
        var background = new SettingsCard(Loc.T("Check in the background"));
        var frequency = new[] { "off", "daily", "weekly" }.ToList().IndexOf(service.Frequency);
        var segmented = new SegmentedControl([Loc.T("Off"), Loc.T("Every day"), Loc.T("Every week")], Math.Max(frequency, 0), false, 12);
        var next = V.Secondary(string.Empty, 11.5);
        var notify = new MacSwitch(c.App.Get<bool>("appUpdatesNotify"), SwitchSize.Regular, on => c.App.Set("appUpdatesNotify", on));
        var notifyRow = new SettingsRow(null, Loc.T("Tell me when an app has an update"), null, notify);
        void UpdateNext()
        {
            var date = AppUpdatesService.Shared.NextCheck;
            next.Text = date is null ? string.Empty : Loc.F("Next check {0}", Fmt.ShortDateTime(date.Value));
            next.Visibility = date is null ? Visibility.Collapsed : Visibility.Visible;
            notify.IsEnabled = service.Frequency != "off";
            notifyRow.Opacity = notify.IsEnabled ? 1 : 0.45;
        }

        segmented.SelectionChanged += i =>
        {
            c.App.Set("appUpdatesCheckFrequency", i switch { 1 => "daily", 2 => "weekly", _ => "off" });
            service.SyncWithPreferences();
            UpdateNext();
        };
        background.Add(segmented);
        background.Add(next);
        background.Add(notifyRow);
        UpdateNext();
        Action changed = () => Application.Current?.Dispatcher.BeginInvoke(UpdateNext);
        service.Changed += changed;
        background.Unloaded += (_, _) => service.Changed -= changed;
        page.AddSpaced(background, 14);

        // 3. sources
        var sources = new SettingsCard(Loc.T("Sources"));
        var toggles = new List<(string Key, MacSwitch Switch)>();
        void Refresh()
        {
            var on = toggles.Count(t => c.App.Get<bool>(t.Key));
            foreach (var (key, sw) in toggles)
            {
                sw.IsEnabled = !(c.App.Get<bool>(key) && on == 1);
            }
        }

        MacSwitch Source(string key, string title, string? caption)
        {
            var sw = new MacSwitch(c.App.Get<bool>(key), SwitchSize.Regular, on =>
            {
                c.App.Set(key, on);
                Refresh();
                service.SourceSelectionDidChange();
            });
            toggles.Add((key, sw));
            sources.Add(new SettingsRow(null, title, caption, sw));
            return sw;
        }

        Source("appUpdatesIncludeHomebrewApps", Loc.T("Include winget apps"), null);
        sources.AddDivider();
        Source("appUpdatesIncludeAppStore", Loc.T("Include apps from the Microsoft Store"), "Checks store versions using this PC’s region. Microsoft installs these updates.");
        sources.AddDivider();
        Source("appUpdatesIncludeOnlineCatalog", Loc.T("Include other installed apps"), Loc.T("Checks directly with app developers when supported, then uses a public catalog. The app’s own updater installs the update."));
        Refresh();
        page.AddSpaced(sources, 14);

        // 4. panel
        var panel = new SettingsCard();
        panel.Add(new SettingsRow(null, Loc.T("Show in panel"), null, new MacSwitch(c.App.Get<bool>("panelUtilityAppUpdates"), SwitchSize.Regular, on => c.App.Set("panelUtilityAppUpdates", on))));
        page.AddSpaced(panel, 14);

        service.CheckIfNeeded();
        return page;
    }
}

/// <summary>Utilities row "App updates": hosts the compact list.</summary>
public sealed class AppUpdatesUtility : IPanelUtility
{
    public string? FeatureId => "app-updates";

    public int Order => 30;

    public string Id => "appUpdates";

    public string Title => Loc.T("App updates");

    public string Glyph => G.Download;

    public string? Caption => Loc.T("See which apps have a newer version");

    public string? SettingsDestination => SettingsPages.AppUpdates;

    public bool ShowsSettingsGear => true;

    public FrameworkElement? CreateHostedView(IPanelContext c)
    {
        Host.Attach(c);
        AppUpdatesService.Shared.ReloadRules();
        var list = new AppUpdatesListView(true, c.RequestRelayout);
        var card = new PanelCard(list);
        AppUpdatesService.Shared.CheckIfNeeded();
        return card;
    }
}
