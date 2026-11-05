// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.GitHub;

/// <summary>Settings &gt; Dynamic Island &gt; "GitHub while you code" (spec 09 part 2).</summary>
public sealed class CodingSettingsSection : ISettingsSection
{
    public string? FeatureId => GhText.FeatureId;

    public string PageId => SettingsPages.Island;

    public string? Anchor => "githubCoding";

    public string? Title => CodingText.Title;

    public int Order => 181;

    public IReadOnlyList<string> Keywords => ["branch", "git", "repository", "checks", "pull request", "editor", "VS Code", "folders"];

    public FrameworkElement CreateView(ISettingsContext context) => new CodingSettingsView(context).Build();
}

/// <summary>Builds the card; one instance per (re)build of the page.</summary>
internal sealed class CodingSettingsView
{
    private readonly ISettingsContext _context;
    private readonly IAppSettings _app;
    private readonly CodingTracker? _tracker;
    private readonly TextBlock _status = UiKit.Text("", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
    private readonly List<UIElement> _dependent = [];

    public CodingSettingsView(ISettingsContext context)
    {
        _context = context;
        _app = context.App;
        CodingKeys.Register(_app);
        _tracker = context.Services.Get<CodingTracker>();
    }

    private static FrameworkElement Indent(UIElement child) => new Border { Child = child, Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };

    private static void Name(DependencyObject element, string id, string name)
    {
        AutomationProperties.SetAutomationId(element, id);
        AutomationProperties.SetName(element, name);
    }

    public FrameworkElement Build()
    {
        var card = new SettingsCard(CodingText.Title);
        var master = new MacSwitch(_app.Get<bool>(CodingKeys.Enabled), SwitchSize.Regular, on =>
        {
            _app.Set(CodingKeys.Enabled, on);
            foreach (var row in _dependent)
            {
                row.IsEnabled = on;
            }
        });
        Name(master, "GitHub.Coding.Enabled", CodingText.Follow);
        card.Add(new SettingsRow(null, CodingText.Follow, CodingText.FollowCaption, master));

        card.Add(Dependent(Toggle(CodingKeys.Strip, CodingText.ShowStrip, CodingText.ShowStripCaption, "GitHub.Coding.Strip")));
        card.Add(Dependent(Toggle(CodingKeys.Alerts, CodingText.AlertWhenFinished, CodingText.AlertCaption, "GitHub.Coding.Alerts")));
        card.Add(Dependent(FoldersRow()));

        _status.Text = CodingLines.Status(_tracker?.Snapshot ?? CodingSnapshot.Empty);
        Name(_status, "GitHub.Coding.Status", _status.Text);
        card.Add(Indent(_status));

        var enabled = _app.Get<bool>(CodingKeys.Enabled);
        foreach (var row in _dependent)
        {
            row.IsEnabled = enabled;
        }

        if (_tracker is not null)
        {
            card.Loaded += (_, _) =>
            {
                _tracker.Changed += OnTrackerChanged;
                OnTrackerChanged();
            };
            card.Unloaded += (_, _) => _tracker.Changed -= OnTrackerChanged;
        }

        return card;
    }

    private FrameworkElement Dependent(FrameworkElement row)
    {
        _dependent.Add(row);
        return row;
    }

    private void OnTrackerChanged()
    {
        if (_tracker is null)
        {
            return;
        }

        _status.Text = CodingLines.Status(_tracker.Snapshot);
        Name(_status, "GitHub.Coding.Status", _status.Text);
    }

    private FrameworkElement Toggle(string key, string title, string caption, string id)
    {
        var toggle = new MacSwitch(_app.Get<bool>(key), SwitchSize.Regular, on => _app.Set(key, on));
        Name(toggle, id, title);
        return Indent(new SettingsRow(null, title, caption, toggle));
    }

    private FrameworkElement FoldersRow()
    {
        var folders = Split(_app.Get<string>(CodingKeys.Folders));
        var list = new ComboBox { MinWidth = 220, MaxWidth = 300, ItemsSource = folders, SelectedIndex = folders.Count > 0 ? 0 : -1, IsEnabled = folders.Count > 0 };
        Name(list, "GitHub.Coding.Folders", CodingText.ProjectFolders);
        var add = UiKit.Button(CodingText.AddFolder, ButtonKind.Bordered, 11.5, null, () => AddFolder(list));
        Name(add, "GitHub.Coding.AddFolder", CodingText.AddFolder);
        add.Margin = new Thickness(8, 0, 0, 0);
        var remove = UiKit.Button(CodingText.RemoveFolder, ButtonKind.Bordered, 11.5, null, () => RemoveFolder(list));
        Name(remove, "GitHub.Coding.RemoveFolder", CodingText.RemoveFolder);
        remove.Margin = new Thickness(6, 0, 0, 0);
        remove.IsEnabled = folders.Count > 0;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(list);
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        return Indent(new SettingsRow(null, CodingText.ProjectFolders, CodingText.ProjectFoldersCaption, buttons));
    }

    private static List<string> Split(string text) =>
        [.. text.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    private void AddFolder(FrameworkElement anchor)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = CodingText.AddFolder };
        var owner = Window.GetWindow(anchor);
        if ((owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return;
        }

        var folders = Split(_app.Get<string>(CodingKeys.Folders));
        if (!folders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(dialog.FolderName);
            _app.Set(CodingKeys.Folders, string.Join('|', folders));
        }

        _context.RequestRebuild();
    }

    private void RemoveFolder(ComboBox list)
    {
        if (list.SelectedItem is not string selected)
        {
            return;
        }

        var folders = Split(_app.Get<string>(CodingKeys.Folders));
        folders.RemoveAll(f => string.Equals(f, selected, StringComparison.OrdinalIgnoreCase));
        _app.Set(CodingKeys.Folders, string.Join('|', folders));
        _context.RequestRebuild();
    }
}
