// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Maintenance;

/// <summary>The Kill Process page (spec 6.4.3): settings, filter, sortable columns and the virtualised process list.</summary>
internal sealed class KillProcessPageView : StackPanel
{
    private const double RowHeight = 40;
    private readonly KillService _s = KillService.Shared;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly VirtualList _list = new(RowHeight) { Height = 520 };
    private readonly TextBlock _count = UiKit.Text("Processes: 0", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
    private readonly ContentControl _empty = new();
    private readonly ContentControl _columns = new();
    private readonly Spinner _spinner = new(12);
    private string _query = string.Empty;
    private List<KillEntry> _rows = [];

    public KillProcessPageView()
    {
        var group = new SettingsCard();
        group.Add(new SettingsRow(null, "Group related processes", "Groups helper processes under the app responsible for them.",
            new MacSwitch(Host.App.Get<bool>("killProcessGroupRelated"), SwitchSize.Regular, on =>
            {
                Host.App.Set("killProcessGroupRelated", on);
                _s.GroupRelated = on;
                _ = _s.RefreshAsync(true);
            })));
        group.AddDivider();
        group.Add(new SettingsRow(null, "Show in Command Bar", "Adds running processes to the Command Bar, so you can find and kill them without opening Settings.",
            new MacSwitch(Host.App.Get<bool>("killProcessCommandBarEnabled"), SwitchSize.Regular, on => Host.App.Set("killProcessCommandBarEnabled", on))));
        Children.Add(group);

        var top = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var right = V.Row(16, _count, V.IconButton(G.Refresh, "Refresh", () => _ = _s.RefreshAsync(true), 12, ThemeKeys.TextSecondary, 24));
        Grid.SetColumn(right, 1);
        top.Children.Add(right);
        Children.Add(top);

        var search = V.SearchField("Filter by name", out var box, 12, 9, 9);
        search.Margin = new Thickness(0, 8, 0, 0);
        var searchRow = V.Split(search, _spinner, 8);
        box.TextChanged += (_, _) =>
        {
            _query = box.Text.Trim().ToLowerInvariant();
            Render();
        };
        Children.Add(searchRow);

        var card = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsCard);
        var inner = new StackPanel();
        _columns.Margin = new Thickness(16, 6, 16, 6);
        inner.Children.Add(_columns);
        inner.Children.Add(UiKit.Divider());
        var host = new Grid();
        host.Children.Add(_list);
        host.Children.Add(_empty);
        inner.Children.Add(host);
        card.Child = inner;
        Children.Add(card);

        Loaded += (_, _) =>
        {
            _s.GroupRelated = Host.App.Get<bool>("killProcessGroupRelated");
            _s.Changed += OnServiceChanged;
            _timer.Tick += OnTick;
            _timer.Start();
            if (Window.GetWindow(this) is { } window)
            {
                window.Activated += OnWindowActivated;
            }

            _ = _s.RefreshAsync(true);
            Render();
        };
        Unloaded += (_, _) =>
        {
            _s.Changed -= OnServiceChanged;
            _timer.Tick -= OnTick;
            _timer.Stop();
            if (Window.GetWindow(this) is { } window)
            {
                window.Activated -= OnWindowActivated;
            }
        };
        BuildColumns();
    }

    private void OnWindowActivated(object? sender, EventArgs e) => _ = _s.RefreshAsync(true);

    private void OnTick(object? sender, EventArgs e)
    {
        if (IsVisible && Window.GetWindow(this)?.IsActive == true)
        {
            _ = _s.RefreshAsync();
        }
    }

    private void OnServiceChanged() => Dispatcher.BeginInvoke(Render);

    private string SortBy => Host.App.Get<string>("killProcessSortBy");

    private bool Ascending => Host.App.Get<bool>("killProcessSortAscending");

    private void BuildColumns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22 + 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52 + 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72 + 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56 + 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });

        void Header(string title, string key, int column, bool right)
        {
            var active = SortBy == key;
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            content.Children.Add(UiKit.Text(title, 10.5, FontWeights.SemiBold, ThemeKeys.TextSecondary));
            if (active)
            {
                var chevron = UiKit.Icon(Ascending ? Glyphs.ChevronUp : Glyphs.ChevronDown, 8, ThemeKeys.TextSecondary, FontWeights.Bold);
                chevron.Margin = new Thickness(3, 0, 0, 0);
                content.Children.Add(chevron);
            }

            var button = new Button { Content = content, Padding = new Thickness(0, 2, 0, 2) };
            button.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
            button.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            button.Click += (_, _) =>
            {
                if (SortBy == key)
                {
                    Host.App.Set("killProcessSortAscending", !Ascending);
                }
                else
                {
                    Host.App.Set("killProcessSortBy", key);
                    Host.App.Set("killProcessSortAscending", key == "name");
                }

                BuildColumns();
                Render();
            };
            Grid.SetColumn(button, column);
            grid.Children.Add(button);
        }

        Header("Process", "name", 1, false);
        Header("CPU", "cpu", 2, true);
        Header("Memory", "memory", 3, true);
        Header("PID", "pid", 4, true);
        _columns.Content = grid;
    }

    private void Render()
    {
        _spinner.Visibility = _s.IsRefreshing ? Visibility.Visible : Visibility.Collapsed;
        IEnumerable<KillEntry> rows = _s.Entries;
        if (_query.Length > 0)
        {
            rows = rows.Where(r => r.Name.ToLowerInvariant().Contains(_query) || r.Pid.ToString() == _query);
        }

        var asc = Ascending;
        rows = SortBy switch
        {
            "memory" => (asc ? rows.OrderBy(r => r.Memory) : rows.OrderByDescending(r => r.Memory)).ThenBy(r => r.Pid),
            "name" => (asc ? rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase) : rows.OrderByDescending(r => r.Name, StringComparer.CurrentCultureIgnoreCase)).ThenBy(r => r.Pid),
            "pid" => asc ? rows.OrderBy(r => r.Pid) : rows.OrderByDescending(r => r.Pid),
            _ => (asc ? rows.OrderBy(r => r.Cpu) : rows.OrderByDescending(r => r.Cpu)).ThenBy(r => r.Pid),
        };
        _rows = rows.ToList();
        _count.Text = $"Processes: {_rows.Count}";
        if (!_s.HasLoadedOnce)
        {
            _empty.Content = new Spinner(18) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _empty.Visibility = Visibility.Visible;
        }
        else if (_rows.Count == 0)
        {
            var text = V.Secondary("No Processes Found", 13);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            _empty.Content = text;
            _empty.Visibility = Visibility.Visible;
        }
        else
        {
            _empty.Visibility = Visibility.Collapsed;
        }

        _list.SetItems(_rows.Count, i => Row(_rows[i]));
    }

    private FrameworkElement Row(KillEntry entry)
    {
        var grid = new Grid { Background = Brushes.Transparent, Margin = new Thickness(16, 0, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22 + 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52 + 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72 + 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56 + 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });

        var icon = new Image { Width = 22, Height = 22, Source = Icons.Of(entry.Path, true), HorizontalAlignment = HorizontalAlignment.Left };
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line1 = new StackPanel { Orientation = Orientation.Horizontal };
        var name = UiKit.Text(entry.Name, 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        line1.Children.Add(name);
        if (entry.GroupedCount > 1)
        {
            var count = UiKit.Text($"  Processes: {entry.GroupedCount}", 10, FontWeights.Normal, ThemeKeys.TextTertiary);
            count.VerticalAlignment = VerticalAlignment.Center;
            line1.Children.Add(count);
        }

        text.Children.Add(line1);
        var path = UiKit.Text(entry.Path.Length > 0 ? entry.Path : "—", 10.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        path.TextTrimming = TextTrimming.CharacterEllipsis;
        path.FlowDirection = FlowDirection.LeftToRight;
        path.ToolTip = entry.Path;
        text.Children.Add(path);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        TextBlock Metric(string value, int column)
        {
            var block = UiKit.Text(value, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
            block.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            block.HorizontalAlignment = HorizontalAlignment.Right;
            block.VerticalAlignment = VerticalAlignment.Center;
            block.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
            return block;
        }

        Metric($"{entry.Cpu:0.0}%", 2);
        Metric(Fmt.MetricBytes(entry.Memory), 3);
        Metric(entry.Pid.ToString(), 4);

        var cluster = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var kill = V.Mini("Kill", () => Confirm(entry, "Kill", false));
        kill.MinWidth = 44;
        kill.IsEnabled = !entry.IsProtected;
        cluster.Children.Add(kill);
        var more = new Button { Content = UiKit.Icon(G.More, 13, ThemeKeys.TextPrimary), Width = 40, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(0, 3, 0, 3) };
        more.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonBordered);
        var menu = BuildMenu(entry);
        more.Click += (_, _) =>
        {
            menu.PlacementTarget = more;
            menu.IsOpen = true;
        };
        cluster.Children.Add(more);
        Grid.SetColumn(cluster, 5);
        grid.Children.Add(cluster);
        grid.ContextMenu = menu;

        var row = new Border { Child = grid };
        var divider = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, 0), Opacity = 0.6 };
        divider.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Divider);
        var host = new Grid();
        host.Children.Add(row);
        host.Children.Add(divider);
        return host;
    }

    private ContextMenu BuildMenu(KillEntry entry)
    {
        var restartable = !entry.IsProtected && entry.IsRegularApp && entry.Path.Length > 0 && !entry.Path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
        var items = new List<(string? Title, Action? Action, bool Enabled)>
        {
            ("Force Kill", () => Confirm(entry, "Force Kill", true), !entry.IsProtected),
            ($"Kill All “{entry.Name}”", () => ConfirmAll(entry), !entry.IsProtected),
            ("Kill Process Tree", () => ConfirmTree(entry), !entry.IsProtected),
        };
        if (restartable)
        {
            items.Add(("Restart", () => _ = _s.RestartAsync(entry), true));
        }

        items.Add((null, null, true));
        items.Add(("Copy PID", () => Copy(entry.Pid.ToString()), true));
        items.Add(("Copy Path", () => Copy(entry.Path), entry.Path.Length > 0));
        return V.Menu([.. items]);
    }

    private static void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // clipboard busy
        }
    }

    private void Confirm(KillEntry entry, string verb, bool force)
    {
        var title = force ? $"Force Kill {entry.Name}?" : $"Kill {entry.Name}?";
        if (!Dialogs.Confirm(title, entry.Path, verb, true))
        {
            return;
        }

        _ = _s.KillAsync([new KillTarget(entry.Pid, entry.StartedAt, entry.Name)], force);
    }

    private void ConfirmAll(KillEntry entry)
    {
        if (!Dialogs.Confirm($"Kill all “{entry.Name}” processes?", string.Empty, "Kill", true))
        {
            return;
        }

        var targets = _s.Entries.Where(e => !e.IsProtected && e.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => e.Members.Select(m => new KillTarget(m.Pid, m.Started, e.Name))).Distinct().ToList();
        _ = _s.KillAsync(targets, false);
    }

    private void ConfirmTree(KillEntry entry)
    {
        if (!Dialogs.Confirm($"Kill {entry.Name} and all its child processes?", entry.Path, "Kill Process Tree", true))
        {
            return;
        }

        _ = _s.KillAsync(_s.TreeOf(entry), true);
    }
}

/// <summary>Settings page "Kill Process" (beta).</summary>
public sealed class KillProcessSettingsSection : ISettingsSection
{
    public string? FeatureId => "kill-process";

    public string PageId => SettingsPages.KillProcess;

    public string? Title => "Kill Process";

    public IReadOnlyList<string> Keywords => ["force quit", "process", "cpu", "memory", "kill", "Group related processes", "Show in Command Bar"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        Host.Attach(c);
        return new KillProcessPageView();
    }
}

/// <summary>Kill Process: registers the Command Bar action. The UI lives in the contribution.</summary>
public sealed class KillProcessFeature : IFeature
{
    private IFeatureContext? _context;

    public string Id => "kill-process";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("browse", "Kill Process", () => _context?.Shell.OpenSettings(SettingsPages.KillProcess), G.CancelCircle),
    ];

    private IProcessKillService? _service;

    public void Start(IFeatureContext context)
    {
        _context = context;
        Host.Attach(context);
        KillService.Shared.GroupRelated = context.App.Get<bool>("killProcessGroupRelated");
        _service = new ProcessKillServiceImpl();
        context.Services.Provide(_service);
    }

    public void Stop()
    {
        if (_service is not null)
        {
            _context?.Services.Withdraw(_service);
            _service = null;
        }

        _context = null;
    }
}
