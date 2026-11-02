// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Maintenance;

/// <summary>Pieces shared by the Packages page and the panel variant.</summary>
internal static class PackageParts
{
    public static string StatusOf(PackagesModel m, PackageEntry p) => m.HasUpdate(p) ? "update" : m.IsInstalled(p) ? "installed" : "none";

    /// <summary>A text field with a placeholder (the plain rounded-border field of the spec).</summary>
    public static Grid HintField(string placeholder, out TextBox box, double fontSize = 12)
    {
        var field = V.Field(string.Empty, 0, fontSize);
        var hint = UiKit.Text(placeholder, fontSize, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(9, 0, 0, 0);
        field.TextChanged += (_, _) => hint.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var grid = new Grid();
        grid.Children.Add(field);
        grid.Children.Add(hint);
        box = field;
        return grid;
    }

    public static bool Confirm(PkgAction action, string name, Action<bool>? keepOpen)
    {
        var (title, body, ok, destructive) = action switch
        {
            PkgAction.Install => ("Install with winget?", $"winget will download and install {name}. Dependencies may also be installed.", "Install", false),
            PkgAction.Uninstall => ("Uninstall with winget?", $"winget will uninstall {name}. Configuration files may remain on the system.", "Uninstall", true),
            PkgAction.Upgrade => ("Update with winget?", $"winget will download and apply the latest version of {name}. Dependencies may also be updated.", "Update", false),
            PkgAction.UpgradeAll => ("Update all with winget?", "winget will download and apply the latest versions for packages with updates available. Dependencies may also be updated.", "Update all", false),
            _ => ("Update winget?", "winget will refresh its package sources and then reload your packages.", "Update sources", false),
        };
        return Dialogs.Confirm(title, body, ok, destructive, "Cancel", keepOpen);
    }

    /// <summary>The orange "source terms" card (winget's agreement gate).</summary>
    public static FrameworkElement TrustCard(PackagesModel model, bool compact)
    {
        var column = new StackPanel();
        column.Children.Add(V.Label(G.Shield, "Source terms not accepted yet", compact ? 11 : 13, FontWeights.SemiBold, ThemeKeys.Orange, ThemeKeys.TextPrimary));
        var caption = V.Secondary($"winget needs your confirmation before using the {model.AgreementSource ?? "winget"} source. Accept to continue.", compact ? 9.5 : 11);
        caption.Margin = new Thickness(0, 4, 0, 8);
        column.Children.Add(caption);
        var button = UiKit.Button("Accept and continue", ButtonKind.Prominent, compact ? 10.5 : 12, null, model.AcceptAgreements);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        column.Children.Add(button);
        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(compact ? 8 : 12), BorderThickness = new Thickness(1), Child = column };
        var orange = (UiKit.BrushOf(ThemeKeys.Orange) as SolidColorBrush)?.Color ?? Colors.Orange;
        card.Background = new SolidColorBrush(Color.FromArgb(0x17, orange.R, orange.G, orange.B));
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, orange.R, orange.G, orange.B));
        return card;
    }

    /// <summary>Missing-winget state (page: centred column; panel: card).</summary>
    public static FrameworkElement MissingState(Action refresh, bool compact, ref bool opened)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(compact ? 4 : 28) };
        var glyph = UiKit.Icon(G.Package, compact ? 30 : 44, ThemeKeys.TextSecondary);
        glyph.FontWeight = FontWeights.Light;
        column.Children.Add(glyph);
        var title = UiKit.Text("winget not found", compact ? 13 : 16, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 12, 0, 0);
        column.Children.Add(title);
        var body = UiKit.Text("WinKnife can open the Microsoft Store page for App Installer, which provides winget. Install or update it there, then come back.", compact ? 10.5 : 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        body.TextAlignment = TextAlignment.Center;
        body.MaxWidth = 360;
        body.Margin = new Thickness(0, 12, 0, 0);
        column.Children.Add(body);
        var wasOpened = opened;
        var captionText = V.Secondary(wasOpened ? "Microsoft Store opened." : "When the installer finishes, come back here and click Refresh.", compact ? 9.5 : 12);
        captionText.TextAlignment = TextAlignment.Center;
        captionText.MaxWidth = 360;
        var install = UiKit.Button("Install winget", ButtonKind.Prominent, compact ? 11 : 13, G.Terminal, () =>
        {
            UiKit.OpenUrl("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1");
            captionText.Text = "Microsoft Store opened.";
        });
        install.Margin = new Thickness(0, 12, 0, 0);
        install.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(install);
        captionText.Margin = new Thickness(0, 12, 0, 0);
        column.Children.Add(captionText);
        var refreshButton = UiKit.Button("Refresh", ButtonKind.Bordered, compact ? 10.5 : 12, G.Refresh, refresh);
        refreshButton.Margin = new Thickness(0, 12, 0, 0);
        refreshButton.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(refreshButton);
        return column;
    }

    /// <summary>One package row (page: 232 px list; panel: smaller fonts).</summary>
    public static Border Row(PackagesModel m, PackageEntry p, bool selected, bool compact, Action<PackageEntry> onSelect, Action<PkgAction, PackageEntry> onAction)
    {
        var status = StatusOf(m, p);
        var operation = WingetOperations.Shared.Status;
        var working = operation is { IsRunning: true } && string.Equals(operation.TargetId, p.Id, StringComparison.OrdinalIgnoreCase);
        var border = new Border
        {
            CornerRadius = new CornerRadius(compact ? 7 : 6),
            Padding = compact ? new Thickness(7, 5, 7, 5) : new Thickness(6),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
        };
        border.SetResourceReference(Border.BackgroundProperty, selected ? (compact ? ThemeKeys.AccentFill18 : ThemeKeys.AccentFill12) : (compact ? ThemeKeys.ControlFill : ThemeKeys.TileFill));
        if (!selected && !compact)
        {
            border.Background = Brushes.Transparent;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = UiKit.Icon(m.IsTool(p) ? G.Terminal : G.AppIcon, compact ? 11 : 12, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(icon);

        var text = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(p.Name, compact ? 11.5 : 12, selected ? FontWeights.SemiBold : FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        var sub = m.InstalledEntry(p.Id)?.Version is { Length: > 0 } installedVersion ? installedVersion : (p.Version.Length > 0 ? p.Version : p.Id);
        text.Children.Add(UiKit.Text(sub, compact ? 9.5 : 10, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        FrameworkElement trailing;
        if (working)
        {
            trailing = new Spinner(12);
        }
        else if (status == "update")
        {
            var current = m.Outdated[p.Id];
            var up = new Button { Content = UiKit.Icon(G.ArrowUpCircle, 13, ThemeKeys.Orange), ToolTip = $"Update: {p.Version} → {current.Available}", Width = 22, Height = 20, IsEnabled = !m.IsBusy };
            up.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
            up.Padding = new Thickness(0);
            up.Click += (_, e) => onAction(PkgAction.Upgrade, p);
            up.PreviewMouseLeftButtonUp += (_, e) => e.Handled = true;
            trailing = up;
        }
        else if (status == "installed")
        {
            trailing = UiKit.Icon(G.CheckCircle, 11, ThemeKeys.Green);
        }
        else
        {
            trailing = new Border { Width = 0 };
        }

        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);
        border.Child = grid;
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (!e.Handled)
            {
                onSelect(p);
            }
        };
        border.MouseEnter += (_, _) =>
        {
            if (!selected)
            {
                border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
            }
        };
        border.MouseLeave += (_, _) =>
        {
            if (!selected)
            {
                if (compact)
                {
                    border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
                }
                else
                {
                    border.Background = Brushes.Transparent;
                }
            }
        };
        var busy = m.IsBusy;
        border.ContextMenu = V.Menu(
            ("Package details", () => onSelect(p), true),
            (status == "update" ? "Update" : null, () => onAction(PkgAction.Upgrade, p), !busy),
            (status != "none" ? "Uninstall" : "Install", () => onAction(status != "none" ? PkgAction.Uninstall : PkgAction.Install, p), !busy));
        return border;
    }

    /// <summary>Section header: title plus an optional count capsule.</summary>
    public static FrameworkElement SectionHeader(string title, int? count, bool compact)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(compact ? 0 : 6, 0, 0, 0) };
        row.Children.Add(UiKit.Text(title, compact ? 10 : 11, FontWeights.SemiBold, compact ? ThemeKeys.TextTertiary : ThemeKeys.TextSecondary));
        if (count is { } n)
        {
            var badge = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            badge.Child = UiKit.Text(n.ToString(), compact ? 8.5 : 9, FontWeights.SemiBold, ThemeKeys.TextSecondary);
            row.Children.Add(badge);
        }

        return row;
    }

    /// <summary>Builds the Results/Installed list content. Returns the stack.</summary>
    public static StackPanel ListContent(PackagesModel m, bool compact, bool installedOnly, bool resultsOnly, PackageEntry? selected, Action<PackageEntry> onSelect, Action<PkgAction, PackageEntry> onAction)
    {
        var stack = new StackPanel();
        var spacing = compact ? 5.0 : 4.0;
        FrameworkElement Message(string text) => V.Secondary(text, compact ? 10 : 11.5);

        if (!installedOnly && (m.Searching || m.Results.Count > 0 || (m.SearchedQuery is not null && m.SearchedQuery.Length > 0)))
        {
            var header = SectionHeader("Results", m.Searching ? null : m.Results.Count, compact);
            stack.AddSpaced(header, 0);
            if (m.Searching)
            {
                var loading = V.Row(6, new Spinner(12), UiKit.Text("Loading…", compact ? 10 : 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
                loading.Margin = new Thickness(6, 7, 0, 0);
                stack.Children.Add(loading);
            }
            else if (m.Results.Count == 0)
            {
                var none = Message("No results");
                none.Margin = new Thickness(6, 7, 0, 0);
                stack.Children.Add(none);
            }
            else
            {
                foreach (var p in m.Results)
                {
                    var row = Row(m, p, selected is not null && selected.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase), compact, onSelect, onAction);
                    row.Margin = new Thickness(0, stack.Children.Count == 1 ? 7 : spacing, 0, 0);
                    stack.Children.Add(row);
                }
            }
        }

        if (!resultsOnly)
        {
            var installed = m.FilteredInstalled.ToList();
            var header = SectionHeader("Installed", m.LoadingInstalled && installed.Count == 0 ? null : installed.Count, compact);
            header.Margin = new Thickness(0, stack.Children.Count > 0 ? 14 : 0, 0, 0);
            stack.Children.Add(header);
            if (m.LoadingInstalled && installed.Count == 0)
            {
                var loading = V.Row(6, new Spinner(12), UiKit.Text("Loading…", compact ? 10 : 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
                loading.Margin = new Thickness(6, 7, 0, 0);
                stack.Children.Add(loading);
            }
            else if (installed.Count == 0)
            {
                var none = Message("No packages found");
                none.Margin = new Thickness(6, 7, 0, 0);
                stack.Children.Add(none);
            }
            else
            {
                var first = true;
                foreach (var p in installed)
                {
                    var row = Row(m, p, selected is not null && selected.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase), compact, onSelect, onAction);
                    row.Margin = new Thickness(0, first ? 7 : spacing, 0, 0);
                    first = false;
                    stack.Children.Add(row);
                }
            }
        }

        return stack;
    }

    /// <summary>The detail pane body for the selected package (page) / detail card (panel).</summary>
    public static FrameworkElement Detail(PackagesModel m, PackageEntry p, bool compact, Action<PkgAction, PackageEntry> onAction)
    {
        var details = m.Details;
        var status = StatusOf(m, p);
        var operation = WingetOperations.Shared.Status;
        var working = operation is { IsRunning: true } && string.Equals(operation.TargetId, p.Id, StringComparison.OrdinalIgnoreCase);
        var column = new StackPanel();

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var name = UiKit.Text(details?.Name is { Length: > 0 } n ? n : p.Name, compact ? 13 : 18, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true);
        name.MaxWidth = compact ? 200 : 360;
        title.Children.Add(name);
        var (badgeText, badgeFore, badgeFill) = working && operation is not null
            ? (operation.PhaseText, ThemeKeys.Accent, ThemeKeys.AccentFill12)
            : status switch
            {
                "update" => ("Update available", ThemeKeys.Orange, ThemeKeys.OrangeFill),
                "installed" => ("Installed", ThemeKeys.Green, ThemeKeys.TileFill),
                _ => ("Not installed", ThemeKeys.TextSecondary, ThemeKeys.ControlFill),
            };
        var badge = V.Badge(badgeText, badgeFore, badgeFill, compact ? 9 : 10);
        badge.Margin = new Thickness(8, compact ? 0 : 6, 0, 0);
        title.Children.Add(badge);
        column.Children.Add(title);

        var id = UiKit.Text(p.Id, compact ? 9.5 : 11, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        id.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        id.Margin = new Thickness(0, 4, 0, 0);
        column.Children.Add(id);

        if (m.LoadingDetails)
        {
            var loading = V.Row(6, new Spinner(12), UiKit.Text("Loading…", 11, FontWeights.Normal, ThemeKeys.TextSecondary));
            loading.Margin = new Thickness(0, 10, 0, 0);
            column.Children.Add(loading);
        }
        else if (details is not null && details.Description.Length > 0)
        {
            var description = UiKit.Text(details.Description, compact ? 10.5 : 13, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
            if (compact)
            {
                description.MaxHeight = 32;
            }

            description.Margin = new Thickness(0, 10, 0, 0);
            column.Children.Add(description);
        }

        FrameworkElement Info(string label, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(UiKit.Text(label, compact ? 9.5 : 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            var v = new TextBox { Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = compact ? 9.5 : 11.5, TextAlignment = TextAlignment.Right, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right };
            v.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
            v.Background = Brushes.Transparent;
            v.Padding = new Thickness(0);
            v.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
            Grid.SetColumn(v, 1);
            grid.Children.Add(v);
            return grid;
        }

        var installedVersion = m.InstalledEntry(p.Id)?.Version;
        column.Children.Add(new Border { Height = 8 });
        column.Children.Add(Info("Version", !string.IsNullOrEmpty(installedVersion) ? installedVersion : (details?.Version is { Length: > 0 } v ? v : p.Version)));
        if (status == "update" && m.Outdated.TryGetValue(p.Id, out var latest))
        {
            column.Children.Add(Info("Latest", latest.Available));
        }

        column.Children.Add(Info("Type", m.IsTool(p) ? "Tools" : "Apps"));
        if (details is { Publisher.Length: > 0 })
        {
            column.Children.Add(Info("Publisher", details.Publisher));
        }

        if (details is { Homepage.Length: > 0 } && Uri.TryCreate(details.Homepage, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var link = V.Link("Open website", () => UiKit.OpenUrl(uri.ToString()), compact ? 10 : 12);
            link.HorizontalAlignment = HorizontalAlignment.Left;
            link.Margin = new Thickness(0, 8, 0, 0);
            column.Children.Add(link);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        if (working && operation is not null)
        {
            buttons.Children.Add(V.Label(G.Hourglass, operation.Title, compact ? 10.5 : 12, FontWeights.Normal, ThemeKeys.TextSecondary, ThemeKeys.TextSecondary));
        }
        else
        {
            var busy = m.IsBusy;
            if (status == "update")
            {
                var update = UiKit.Button("Update", ButtonKind.Prominent, compact ? 10.5 : 12, G.ArrowUpCircle, () => onAction(PkgAction.Upgrade, p));
                update.IsEnabled = !busy;
                buttons.Children.Add(update);
            }

            if (status != "none")
            {
                var uninstall = UiKit.Button("Uninstall", ButtonKind.Destructive, compact ? 10.5 : 12, G.Trash, () => onAction(PkgAction.Uninstall, p));
                uninstall.IsEnabled = !busy;
                uninstall.Margin = new Thickness(status == "update" ? 8 : 0, 0, 0, 0);
                buttons.Children.Add(uninstall);
            }
            else
            {
                var install = UiKit.Button("Install", ButtonKind.Prominent, compact ? 10.5 : 12, G.Download, () => onAction(PkgAction.Install, p));
                install.IsEnabled = !busy;
                buttons.Children.Add(install);
            }
        }

        column.Children.Add(buttons);
        return column;
    }
}

/// <summary>The Packages (winget) settings page: toolbar, package list, detail pane and operation card.</summary>
internal sealed class PackagesPageView : StackPanel
{
    private readonly PackagesModel _m = PackagesModel.Shared;
    private ContentControl _row2 = new();
    private ContentControl _banner = new();
    private ContentControl _list = new();
    private ContentControl _detail = new();
    private readonly ContentControl _body = new();
    private TextBox? _search;
    private Button? _searchButton;
    private bool _storeOpened;
    private string _busyKey = string.Empty;

    public PackagesPageView()
    {
        Children.Add(Header());
        Children.Add(UiKit.Divider());
        Children.Add(_body);
        Loaded += (_, _) =>
        {
            _m.Changed += OnModelChanged;
            WingetOperations.Shared.Changed += OnOperationChanged;
            BuildBody();
            if (WingetCli.IsAvailable && !_m.HasLoaded && !_m.LoadingInstalled)
            {
                _ = _m.RefreshInstalledAsync();
            }
        };
        Unloaded += (_, _) =>
        {
            _m.Changed -= OnModelChanged;
            WingetOperations.Shared.Changed -= OnOperationChanged;
        };
        BuildBody();
    }

    private static FrameworkElement Header()
    {
        var label = V.Label(G.Package, "Packages", 14, FontWeights.SemiBold, ThemeKeys.TextPrimary, ThemeKeys.TextPrimary);
        label.Margin = new Thickness(16, 12, 16, 12);
        return label;
    }

    private void OnOperationChanged()
    {
        var status = WingetOperations.Shared.Status;
        var key = $"{status?.IsRunning}|{status?.TargetId}|{status?.State}|{status?.PhaseText}";
        if (key == _busyKey)
        {
            return;
        }

        _busyKey = key;
        RefreshDynamic();
    }

    private void OnModelChanged() => RefreshDynamic();

    private void BuildBody()
    {
        _body.Content = null;
        _row2 = new ContentControl();
        _banner = new ContentControl();
        _list = new ContentControl();
        _detail = new ContentControl();
        if (!WingetCli.IsAvailable)
        {
            _body.Content = PackageParts.MissingState(() =>
            {
                WingetCli.Reset();
                BuildBody();
                if (WingetCli.IsAvailable)
                {
                    _ = _m.RefreshInstalledAsync();
                }
            }, false, ref _storeOpened);
            return;
        }

        var root = new StackPanel();
        var toolbar = new StackPanel { Margin = new Thickness(16, 16, 16, 12) };
        var row1 = new WrapPanel { Orientation = Orientation.Horizontal };
        var hintField = PackageParts.HintField("Search packages", out var box);
        hintField.MinWidth = 200;
        hintField.Width = 220;
        box.Text = _m.Query;
        box.TextChanged += (_, _) =>
        {
            if (box.Text != _m.Query)
            {
                _m.Query = box.Text;
                _m.ClearSelectionQuiet();
                UpdateSearchEnabled();
                RefreshDynamic();
            }
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _ = _m.SearchAsync();
            }
        };
        _search = box;
        row1.Children.Add(hintField);
        var source = V.Drop(["winget", "Microsoft Store"], _m.SourceIndex, i =>
        {
            _m.SourceIndex = i;
            _m.ClearSelectionQuiet();
            RefreshDynamic();
        }, 116);
        source.Width = 116;
        source.Margin = new Thickness(8, 0, 0, 0);
        row1.Children.Add(source);
        _searchButton = UiKit.Button("Search", ButtonKind.Bordered, 12, G.Search, () => _ = _m.SearchAsync());
        _searchButton.Margin = new Thickness(8, 0, 0, 0);
        row1.Children.Add(_searchButton);
        var refresh = V.IconButton(G.Refresh, "Refresh", () => _ = _m.RefreshInstalledAsync(), 12, ThemeKeys.TextPrimary, 28);
        refresh.Margin = new Thickness(4, 0, 0, 0);
        refresh.Tag = "refresh";
        row1.Children.Add(refresh);
        var update = UiKit.Button("Update sources", ButtonKind.Bordered, 12, G.Refresh, () =>
        {
            if (PackageParts.Confirm(PkgAction.UpdateSources, "winget", null))
            {
                _m.Run(PkgAction.UpdateSources, null);
            }
        });
        update.Margin = new Thickness(4, 0, 0, 0);
        update.Tag = "sources";
        row1.Children.Add(update);
        foreach (var child in row1.Children.OfType<FrameworkElement>())
        {
            child.VerticalAlignment = VerticalAlignment.Center;
            child.Margin = new Thickness(child.Margin.Left, child.Margin.Top, child.Margin.Right, 4);
        }

        toolbar.Children.Add(row1);
        _row2.Margin = new Thickness(0, 5, 0, 0);
        toolbar.Children.Add(_row2);
        root.Children.Add(toolbar);

        if (WingetCli.NeedsPathSetup)
        {
            _banner.Margin = new Thickness(0);
            root.Children.Add(_banner);
        }

        root.Children.Add(UiKit.Divider());

        var split = new Grid { Height = 520 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _list };
        _list.Margin = new Thickness(10, 12, 10, 12);
        split.Children.Add(listScroll);
        var divider = new Border { Width = 1 };
        divider.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Divider);
        Grid.SetColumn(divider, 1);
        split.Children.Add(divider);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var detailScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _detail };
        _detail.Margin = new Thickness(16);
        right.Children.Add(detailScroll);
        var operation = new OperationStatusView(false, OpChrome.Divider);
        Grid.SetRow(operation, 1);
        right.Children.Add(operation);
        Grid.SetColumn(right, 2);
        split.Children.Add(right);
        root.Children.Add(split);
        _body.Content = root;
        RefreshDynamic();
        UpdateSearchEnabled();
    }

    private void UpdateSearchEnabled()
    {
        if (_searchButton is not null)
        {
            _searchButton.IsEnabled = _m.Query.Trim().Length > 0 && !_m.IsBusy;
        }
    }

    private void RefreshDynamic()
    {
        if (!WingetCli.IsAvailable || _body.Content is not StackPanel)
        {
            return;
        }

        UpdateSearchEnabled();
        var busy = _m.IsBusy;

        // Row 2: filter + outdated summary
        var seg = new SegmentedControl([$"All {_m.Installed.Count}", $"Apps {_m.AppCount}", $"Tools {_m.ToolCount}"], _m.Filter, false, 12) { Width = 300 };
        seg.SelectionChanged += i =>
        {
            if (i == _m.Filter)
            {
                return;
            }

            _m.Filter = i;
            if (_m.Selected is { } s && !_m.FilteredInstalled.Any(e => e.Id == s.Id) && !_m.Results.Any(e => e.Id == s.Id))
            {
                _m.ClearSelectionQuiet();
            }

            RefreshDynamic();
        };
        foreach (var cell in new[] { seg })
        {
            cell.HorizontalAlignment = HorizontalAlignment.Left;
        }

        var row2 = new StackPanel { Orientation = Orientation.Horizontal };
        row2.Children.Add(seg);
        if (_m.LoadingOutdated)
        {
            var loading = V.Row(6, new Spinner(12), UiKit.Text("Updates", 12, FontWeights.Normal, ThemeKeys.TextSecondary));
            loading.Margin = new Thickness(12, 0, 0, 0);
            row2.Children.Add(loading);
        }
        else if (_m.OutdatedCount > 0)
        {
            var summary = V.Label(G.ArrowUpCircle, $"Updates {_m.OutdatedCount}", 12, FontWeights.Normal, ThemeKeys.Orange, ThemeKeys.Orange);
            summary.Margin = new Thickness(12, 0, 8, 0);
            summary.VerticalAlignment = VerticalAlignment.Center;
            row2.Children.Add(summary);
            var all = UiKit.Button("Update all", ButtonKind.Bordered, 11, G.ArrowUpCircle, () =>
            {
                if (PackageParts.Confirm(PkgAction.UpgradeAll, "packages", null))
                {
                    _m.Run(PkgAction.UpgradeAll, null);
                }
            });
            all.IsEnabled = !busy;
            all.VerticalAlignment = VerticalAlignment.Center;
            row2.Children.Add(all);
        }

        _row2.Content = row2;

        // PATH banner
        if (WingetCli.NeedsPathSetup)
        {
            var grid = new Grid { Margin = new Thickness(16, 10, 16, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = UiKit.Icon(G.Terminal, 15, ThemeKeys.TextPrimary);
            glyph.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(glyph);
            var text = new StackPanel();
            text.Children.Add(UiKit.Text("Finish terminal setup", 12, FontWeights.SemiBold, ThemeKeys.TextPrimary));
            text.Children.Add(V.Secondary("winget is installed, but your terminal may not find the winget command yet. WinKnife can add its folder to your PATH.", 11));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var setup = UiKit.Button("Set up terminal", ButtonKind.Prominent, 11, G.Wrench, () =>
            {
                WingetCli.AddToUserPath();
                RefreshDynamic();
            });
            setup.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(setup, 2);
            grid.Children.Add(setup);
            var stack = new StackPanel();
            stack.Children.Add(UiKit.Divider());
            stack.Children.Add(grid);
            _banner.Content = stack;
        }

        // Package list
        var selected = _m.Selected;
        _list.Content = PackageParts.ListContent(_m, false, false, false, selected, p => _ = _m.SelectAsync(p), OnAction);

        // Detail pane
        var detailColumn = new StackPanel();
        if (selected is null)
        {
            detailColumn.Children.Add(V.Label(G.Package, "Package details", 18, FontWeights.SemiBold, ThemeKeys.TextPrimary, ThemeKeys.TextPrimary));
            var hint = V.Secondary("Select an installed package or search for a new one.", 13);
            hint.Margin = new Thickness(0, 8, 0, 0);
            detailColumn.Children.Add(hint);
        }
        else
        {
            detailColumn.Children.Add(PackageParts.Detail(_m, selected, false, OnAction));
        }

        if (_m.AgreementSource is not null)
        {
            var card = PackageParts.TrustCard(_m, false);
            card.Margin = new Thickness(0, 14, 0, 0);
            detailColumn.Children.Add(card);
        }

        if (_m.Error is { Length: > 0 } error)
        {
            var label = V.Orange(error, 11);
            label.Margin = new Thickness(0, 14, 0, 0);
            detailColumn.Children.Add(label);
        }

        _detail.Content = detailColumn;
    }

    private void OnAction(PkgAction action, PackageEntry package)
    {
        if (_m.IsBusy)
        {
            return;
        }

        if (PackageParts.Confirm(action, package.Name, null))
        {
            _m.Run(action, package);
        }
    }
}

/// <summary>The panel variant: Search / Installed segmented view with a list, detail card and operation card.</summary>
internal sealed class PackagesPanelView : StackPanel
{
    private readonly PackagesModel _m = PackagesModel.Shared;
    private readonly Action<bool>? _keepOpen;
    private readonly Action _relayout;
    private readonly ContentControl _content = new();
    private int _mode;
    private bool _opened;

    public PackagesPanelView(Action<bool>? keepOpen, Action relayout)
    {
        _keepOpen = keepOpen;
        _relayout = relayout;
        Children.Add(_content);
        var operation = new OperationStatusView(true, OpChrome.Card) { Margin = new Thickness(0, 10, 0, 0) };
        operation.IsVisibleChanged += (_, _) => _relayout();
        Children.Add(operation);
        Loaded += (_, _) =>
        {
            _m.Changed += OnChanged;
            WingetOperations.Shared.Changed += OnOpChanged;
            Build();
            if (WingetCli.IsAvailable && !_m.HasLoaded && !_m.LoadingInstalled)
            {
                _ = _m.RefreshInstalledAsync();
            }
        };
        Unloaded += (_, _) =>
        {
            _m.Changed -= OnChanged;
            WingetOperations.Shared.Changed -= OnOpChanged;
            _keepOpen?.Invoke(false);
        };
        Build();
    }

    private string _busyKey = string.Empty;

    private void OnOpChanged()
    {
        var status = WingetOperations.Shared.Status;
        var key = $"{status?.IsRunning}|{status?.TargetId}|{status?.State}|{status?.PhaseText}";
        if (key != _busyKey)
        {
            _busyKey = key;
            _keepOpen?.Invoke(status?.IsRunning == true);
            Build();
        }
    }

    private void OnChanged() => Build();

    private void Build()
    {
        if (!WingetCli.IsAvailable)
        {
            _content.Content = PackageParts.MissingState(() =>
            {
                WingetCli.Reset();
                Build();
                if (WingetCli.IsAvailable)
                {
                    _ = _m.RefreshInstalledAsync();
                }
            }, true, ref _opened);
            _relayout();
            return;
        }

        var root = new StackPanel();
        var mode = new SegmentedControl(["Search", $"Installed {_m.Installed.Count}"], _mode, true, 11);
        mode.SelectionChanged += i =>
        {
            _mode = i;
            Build();
        };
        root.Children.Add(mode);

        var card = new StackPanel();
        if (_mode == 0)
        {
            var fieldRow = new Grid();
            fieldRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fieldRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var field = PackageParts.HintField("Search packages", out var box, 11);
            box.Text = _m.Query;
            box.TextChanged += (_, _) => _m.Query = box.Text;
            fieldRow.Children.Add(field);
            var go = UiKit.Button(string.Empty, ButtonKind.Prominent, 11, G.Search, () => _ = _m.SearchAsync());
            go.Padding = new Thickness(8, 3, 8, 3);
            go.Margin = new Thickness(6, 0, 0, 0);
            go.IsEnabled = _m.Query.Trim().Length > 0 && !_m.IsBusy;
            Grid.SetColumn(go, 1);
            fieldRow.Children.Add(go);
            box.TextChanged += (_, _) => go.IsEnabled = box.Text.Trim().Length > 0 && !_m.IsBusy;
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    _ = _m.SearchAsync();
                }
            };
            card.Children.Add(fieldRow);
            var kind = new SegmentedControl(["winget", "Microsoft Store"], _m.SourceIndex, true, 10.5);
            kind.SelectionChanged += i => _m.SourceIndex = i;
            kind.Margin = new Thickness(0, 6, 0, 0);
            card.Children.Add(kind);
            var hint = V.Tertiary("Press Enter to search.", 9);
            hint.Margin = new Thickness(0, 5, 0, 0);
            card.Children.Add(hint);
        }
        else
        {
            var filter = new SegmentedControl([$"All {_m.Installed.Count}", $"Apps {_m.AppCount}", $"Tools {_m.ToolCount}"], _m.Filter, true, 10.5);
            filter.SelectionChanged += i =>
            {
                _m.Filter = i;
                Build();
            };
            card.Children.Add(filter);
            var buttons = V.Row(6, PanelButtons.Mini("Check packages", G.Refresh, () => _ = _m.RefreshInstalledAsync()), PanelButtons.Mini("Update sources", G.Refresh, () =>
            {
                if (PackageParts.Confirm(PkgAction.UpdateSources, "winget", _keepOpen))
                {
                    _m.Run(PkgAction.UpdateSources, null);
                }
            }));
            buttons.Margin = new Thickness(0, 6, 0, 0);
            card.Children.Add(buttons);
            FrameworkElement summary;
            if (_m.LoadingOutdated)
            {
                summary = V.Row(6, new Spinner(11), UiKit.Text("Updates", 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            }
            else if (_m.OutdatedCount > 0)
            {
                var all = PanelButtons.Mini("Update all", G.ArrowUpCircle, () =>
                {
                    if (PackageParts.Confirm(PkgAction.UpgradeAll, "packages", _keepOpen))
                    {
                        _m.Run(PkgAction.UpgradeAll, null);
                    }
                });
                all.IsEnabled = !_m.IsBusy;
                summary = V.Row(8, V.Label(G.ArrowUpCircle, $"Updates {_m.OutdatedCount}", 10.5, FontWeights.Normal, ThemeKeys.Orange, ThemeKeys.Orange), all);
            }
            else
            {
                summary = V.Label(G.CheckCircle, "Updates 0", 10.5, FontWeights.Normal, ThemeKeys.Green, ThemeKeys.TextSecondary);
            }

            summary.Margin = new Thickness(0, 6, 0, 0);
            card.Children.Add(summary);
        }

        var controls = new PanelCard(card) { Margin = new Thickness(0, 10, 0, 0) };
        root.Children.Add(controls);

        if (_m.AgreementSource is not null)
        {
            var trust = PackageParts.TrustCard(_m, true);
            trust.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(trust);
        }

        var listContent = PackageParts.ListContent(_m, true, _mode == 1, _mode == 0, _m.Selected, p => _ = _m.SelectAsync(p), OnAction);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 162, Content = listContent };
        var listCard = new PanelCard(scroll, 8) { Margin = new Thickness(0, 10, 0, 0) };
        if (_mode == 1 || _m.Searching || _m.Results.Count > 0 || !string.IsNullOrEmpty(_m.SearchedQuery))
        {
            root.Children.Add(listCard);
        }

        if (_m.Error is { Length: > 0 } error)
        {
            var banner = new PanelCard(V.Orange(error, 10)) { Margin = new Thickness(0, 10, 0, 0) };
            root.Children.Add(banner);
        }

        if (_m.Selected is { } selected)
        {
            var detail = new PanelCard(PackageParts.Detail(_m, selected, true, OnAction)) { Margin = new Thickness(0, 10, 0, 0) };
            root.Children.Add(detail);
        }
        else
        {
            var hint = V.Secondary("Select an installed package or search for a new one.", 10.5);
            hint.Margin = new Thickness(2, 10, 0, 0);
            root.Children.Add(hint);
        }

        _content.Content = root;
        _relayout();
    }

    private void OnAction(PkgAction action, PackageEntry package)
    {
        if (_m.IsBusy)
        {
            return;
        }

        if (PackageParts.Confirm(action, package.Name, _keepOpen))
        {
            _m.Run(action, package);
        }
    }
}
