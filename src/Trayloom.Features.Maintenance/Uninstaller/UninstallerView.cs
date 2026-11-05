// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Maintenance;

/// <summary>The program picker (sheet in Settings, inline in the panel): search + list of installable programs.</summary>
internal sealed class AppPickerView : StackPanel
{
    private readonly bool _compact;
    private readonly Action<InstalledProgram> _onSelect;
    private readonly VirtualList _list;
    private readonly ContentControl _state = new();
    private List<InstalledProgram> _programs = [];
    private List<InstalledProgram> _shown = [];
    private bool _loading = true;
    private string _query = string.Empty;

    public AppPickerView(bool compact, Action<InstalledProgram> onSelect, Action onCancel)
    {
        _compact = compact;
        _onSelect = onSelect;
        _list = new VirtualList(compact ? 34 : 46) { Height = compact ? 150 : 360 };
        Margin = new Thickness(compact ? 0 : 18);

        var header = new Grid { Margin = new Thickness(0, 0, 0, compact ? 9 : 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(UiKit.Text(Loc.T("Choose program"), compact ? 12 : 16, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var cancel = UiKit.Button(Loc.T("Cancel"), ButtonKind.Bordered, compact ? 11 : 12, null, onCancel);
        Grid.SetColumn(cancel, 1);
        header.Children.Add(cancel);
        Children.Add(header);

        var search = V.SearchField(Loc.T("Search apps"), out var box, compact ? 11 : 13, 8, compact ? 6 : 9);
        search.Margin = new Thickness(0, 0, 0, compact ? 9 : 12);
        box.TextChanged += (_, _) =>
        {
            _query = box.Text.Trim().ToLowerInvariant();
            Apply();
        };
        Children.Add(search);
        var host = new Grid();
        host.Children.Add(_list);
        host.Children.Add(_state);
        Children.Add(host);
        Apply();
        Loaded += async (_, _) =>
        {
            var all = await UninstallerModel.Shared.LoadProgramsAsync();
            _programs = UninstallerSupport.Offered(all);
            _loading = false;
            Apply();
            box.Focus();
        };
    }

    private void Apply()
    {
        if (_loading)
        {
            var spinner = V.Centered(8, new Spinner(compactSpinner()), V.Secondary(Loc.T("Scanning files…"), _compact ? 10 : 13, false));
            spinner.VerticalAlignment = VerticalAlignment.Center;
            _state.Content = spinner;
            _state.Visibility = Visibility.Visible;
            _list.SetItems(0, _ => new Border());
            return;
        }

        _shown = string.IsNullOrEmpty(_query)
            ? _programs
            : _programs.Where(p => p.Name.ToLowerInvariant().Contains(_query) || p.Key.ToLowerInvariant().Contains(_query) || p.Publisher.ToLowerInvariant().Contains(_query) || p.InstallLocation.ToLowerInvariant().Contains(_query)).ToList();
        if (_shown.Count == 0)
        {
            var glyph = UiKit.Icon(G.AppIcon, 34, ThemeKeys.TextSecondary);
            glyph.FontWeight = FontWeights.Light;
            var empty = V.Centered(8, glyph, V.Secondary(Loc.T("No apps found"), _compact ? 11 : 13, false), V.Tertiary(Loc.T("Programs that are part of Windows or made by Microsoft aren’t listed."), _compact ? 9.5 : 11));
            empty.VerticalAlignment = VerticalAlignment.Center;
            _state.Content = empty;
            _state.Visibility = Visibility.Visible;
            _list.SetItems(0, _ => new Border());
            return;
        }

        _state.Visibility = Visibility.Collapsed;
        _list.SetItems(_shown.Count, Row, false);

        double compactSpinner() => _compact ? 14 : 20;
    }

    private FrameworkElement Row(int index)
    {
        var program = _shown[index];
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_compact ? 20 : 28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Image { Width = _compact ? 20 : 28, Height = _compact ? 20 : 28, Source = program.Icon(true) };
        grid.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(program.Name, _compact ? 11 : 13, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        var sub = program.Publisher.Length > 0 ? (program.Version.Length > 0 ? $"{program.Publisher} · {program.Version}" : program.Publisher) : (program.InstallLocation.Length > 0 ? Fmt.Home(program.InstallLocation) : program.Key);
        text.Children.Add(UiKit.Text(sub, _compact ? 9.5 : 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var row = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, _compact ? 2 : 4, 8, _compact ? 2 : 4), Background = Brushes.Transparent, Child = grid, Cursor = Cursors.Hand };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => _onSelect(program);
        return row;
    }
}

/// <summary>The uninstaller wizard view (spec 7.2.2): empty / scanning / results / removing / done.</summary>
internal sealed class UninstallerView : ContentControl
{
    private readonly UninstallerModel _m = UninstallerModel.Shared;
    private readonly bool _compact;
    private readonly Action? _relayout;
    private readonly Action<bool>? _keepOpen;
    private bool _picking;
    private bool _dragOver;
    private string _opKey = string.Empty;

    public UninstallerView(bool compact, Action? relayout = null, Action<bool>? keepOpen = null)
    {
        _compact = compact;
        _relayout = relayout;
        _keepOpen = keepOpen;
        AllowDrop = true;
        Loaded += (_, _) =>
        {
            _m.Changed += OnChanged;
            WingetOperations.Shared.Changed += OnOperation;
            _m.ClearRefusal();
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            _m.Changed -= OnChanged;
            WingetOperations.Shared.Changed -= OnOperation;
            _m.ClearRefusal();
            _keepOpen?.Invoke(false);
        };
        Drop += async (_, e) =>
        {
            SetDrag(false);
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var pick = files.FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) ?? files[0];
                await _m.SelectPathAsync(pick);
            }
        };
        DragEnter += (_, e) => SetDrag(e.Data.GetDataPresent(DataFormats.FileDrop));
        DragLeave += (_, _) => SetDrag(false);
        Rebuild();
    }

    private void SetDrag(bool over)
    {
        if (_dragOver != over)
        {
            _dragOver = over;
            if (_m.Phase == UnPhase.Empty && !_picking)
            {
                Rebuild();
            }
        }
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Rebuild);

    private void OnOperation()
    {
        var status = WingetOperations.Shared.Status;
        var key = $"{status?.IsRunning}|{status?.State}";
        if (key != _opKey)
        {
            _opKey = key;
            Dispatcher.BeginInvoke(Rebuild);
        }
    }

    private void Rebuild()
    {
        switch (_m.Phase)
        {
            case UnPhase.Empty:
                Content = _picking ? Picker() : Empty();
                break;
            case UnPhase.Scanning:
                Content = Busy(Loc.T("Scanning files…"), true);
                break;
            case UnPhase.Removing:
                Content = Busy(_m.RemovingMessage, false);
                break;
            case UnPhase.Results:
                Content = Results();
                break;
            default:
                Content = Done();
                break;
        }

        _keepOpen?.Invoke(_m.Phase is UnPhase.Scanning or UnPhase.Removing || _picking);
        _relayout?.Invoke();
    }

    private FrameworkElement Picker()
    {
        var picker = new AppPickerView(_compact, program =>
        {
            _picking = false;
            _m.SelectProgram(program);
            Rebuild();
        }, () =>
        {
            _picking = false;
            Rebuild();
        });
        if (_compact)
        {
            return new PanelCard(picker);
        }

        return picker;
    }

    // ------------------------------------------------------------------ empty

    private FrameworkElement Empty()
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(_compact ? 0 : 28, _compact ? 0 : 40, _compact ? 0 : 28, _compact ? 0 : 40) };
        var width = _compact ? 290.0 : 360.0;
        var height = _compact ? 108.0 : 200.0;
        var zone = new Grid { Width = width, Height = height };
        var outline = new Rectangle
        {
            RadiusX = _compact ? 12 : 18,
            RadiusY = _compact ? 12 : 18,
            StrokeThickness = _compact ? 1.5 : 2,
            StrokeDashArray = _compact ? new DoubleCollection { 4, 3.33 } : new DoubleCollection { 4.5, 3.5 },
        };
        if (_dragOver)
        {
            outline.SetResourceReference(Shape.StrokeProperty, ThemeKeys.Accent);
            outline.SetResourceReference(Shape.FillProperty, ThemeKeys.AccentFill12);
        }
        else
        {
            var secondary = (UiKit.BrushOf(ThemeKeys.TextSecondary) as SolidColorBrush)?.Color ?? Colors.Gray;
            outline.Stroke = new SolidColorBrush(Color.FromArgb(0x59, secondary.R, secondary.G, secondary.B));
        }

        zone.Children.Add(outline);
        var inner = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var glyph = UiKit.Icon(G.Trash, _compact ? 28 : 44, _dragOver ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        glyph.FontWeight = FontWeights.Light;
        inner.Children.Add(glyph);
        var title = UiKit.Text(Loc.T("Drag a program here"), _compact ? 12 : 16, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        inner.AddSpaced(title, _compact ? 6 : 12);
        var subtitle = UiKit.Text(Loc.T("or choose one to scan"), _compact ? 10 : 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        inner.AddSpaced(subtitle, _compact ? 2 : 12);
        zone.Children.Add(inner);
        column.Children.Add(zone);

        if (_m.Refusal is { } refusal)
        {
            var note = new StackPanel { Orientation = Orientation.Horizontal, Width = width };
            var icon = UiKit.Icon(G.Warning, 12, ThemeKeys.Orange);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 8, 0);
            note.Children.Add(icon);
            var message = UiKit.Text(refusal.Message, _compact ? 10 : 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            message.MaxWidth = width - 24;
            note.Children.Add(message);
            column.AddSpaced(note, _compact ? 10 : 20);
        }

        var choose = UiKit.Button(Loc.T("Choose program…"), _compact ? ButtonKind.Bordered : ButtonKind.Bordered, _compact ? 11 : 13, _compact ? Glyphs.Add : null, () =>
        {
            if (_compact)
            {
                _picking = true;
                Rebuild();
                return;
            }

            ShowPickerSheet();
        });
        choose.HorizontalAlignment = HorizontalAlignment.Center;
        choose.Padding = new Thickness(_compact ? 9 : 16, _compact ? 4 : 6, _compact ? 9 : 16, _compact ? 4 : 6);
        column.AddSpaced(choose, _compact ? 10 : 20);
        var empty = V.Tertiary(Loc.T("Nothing is removed without your confirmation."), _compact ? 9.5 : 11, false);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddSpaced(empty, _compact ? 8 : 20);

        if (!_compact)
        {
            var form = new SettingsCard();
            form.Add(new SettingsRow(null, Loc.T("Show in Command Bar"), Loc.T("Choose and uninstall apps in the Command Bar."),
                new MacSwitch(Host.App.Get<bool>("uninstallerCommandBarEnabled"), SwitchSize.Regular, on => Host.App.Set("uninstallerCommandBarEnabled", on))));
            form.Width = width;
            column.AddSpaced(form, 20);
        }

        if (!FileOps.IsElevated)
        {
            column.AddSpaced(AdminNote(width, Loc.T("Some leftovers live in ProgramData, Program Files and HKEY_LOCAL_MACHINE and can only be removed by an administrator.")), _compact ? 10 : 20);
        }

        return column;
    }

    private static FrameworkElement AdminNote(double width, string hint)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var infoIcon = UiKit.Icon(G.Info, 13, ThemeKeys.TextSecondary); infoIcon.VerticalAlignment = VerticalAlignment.Top; infoIcon.HorizontalAlignment = HorizontalAlignment.Left; infoIcon.Margin = new Thickness(0, 1, 0, 0); grid.Children.Add(infoIcon);
        var text = new StackPanel();
        text.Children.Add(UiKit.Text(Loc.T("Items in protected locations are removed after one Windows administrator prompt."), 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
        var h = V.Secondary(hint, 10);
        h.Margin = new Thickness(0, 4, 0, 0);
        text.Children.Add(h);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return V.Soft(grid, 11, width);
    }

    private void ShowPickerSheet()
    {
        _keepOpen?.Invoke(true);
        var window = new Window
        {
            Title = Loc.T("Choose program"),
            Width = 520,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStyle = WindowStyle.ToolWindow,
        };
        window.SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        ThemeManager.BindWindow(window);
        window.Content = new AppPickerView(false, program =>
        {
            window.Close();
            _m.SelectProgram(program);
        }, () => window.Close());
        window.ShowDialog();
        _keepOpen?.Invoke(false);
    }

    // ------------------------------------------------------------------ busy

    private FrameworkElement Busy(string message, bool cancel)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, _compact ? 24 : 80, 0, _compact ? 24 : 80), MinHeight = _compact ? 118 : 0 };
        column.Children.Add(new Spinner(_compact ? 24 : 32, ThemeKeys.TextSecondary));
        var text = UiKit.Text(message, _compact ? 11 : 13, FontWeights.Normal, ThemeKeys.TextSecondary);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddSpaced(text, 14);
        if (_m.Target is { } target)
        {
            var row = V.Row(8, new Image { Width = 18, Height = 18, Source = target.Icon() }, UiKit.Text(target.Name, _compact ? 11 : 13, FontWeights.Normal, ThemeKeys.TextPrimary));
            row.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddSpaced(row, 14);
        }

        if (_m.Phase == UnPhase.Removing && WingetOperations.Shared.Status is not null)
        {
            var status = new OperationStatusView(_compact) { Width = _compact ? 270 : 420 };
            column.AddSpaced(status, 14);
        }

        if (cancel)
        {
            var button = UiKit.Button(Loc.T("Cancel"), ButtonKind.Bordered, _compact ? 11 : 13, null, _m.Cancel);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddSpaced(button, 14);
        }

        return _compact ? new PanelCard(column) : column;
    }

    // ------------------------------------------------------------------ results

    private static string CategoryTitle(UnCategory category) => category switch
    {
        UnCategory.Application => Loc.T("Application"),
        UnCategory.Support => Loc.T("Support"),
        UnCategory.Caches => Loc.T("Caches"),
        UnCategory.Preferences => Loc.T("Preferences"),
        UnCategory.Containers => Loc.T("Containers"),
        UnCategory.Logs => Loc.T("Logs"),
        UnCategory.SavedState => Loc.T("Saved state"),
        _ => Loc.T("Other"),
    };

    private FrameworkElement Results()
    {
        var target = _m.Target!;
        var root = new StackPanel();
        var pad = _compact ? 10.0 : 16.0;

        var header = new Grid { Margin = new Thickness(pad) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var iconSize = _compact ? 28.0 : 44.0;
        header.Children.Add(new Image { Width = iconSize, Height = iconSize, Source = target.Icon(true), Margin = new Thickness(0, 0, 12, 0) });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(UiKit.Text(target.Name, _compact ? 12.5 : 16, FontWeights.SemiBold, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        names.Children.Add(UiKit.Text(target.IsAppx ? target.PackageFamilyName : (target.Key.Length > 0 && !target.Key.StartsWith('{') ? target.Key : Fmt.Home(target.InstallLocation)), _compact ? 9.5 : 12, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        Grid.SetColumn(names, 1);
        header.Children.Add(names);
        var total = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        var size = UiKit.Text(Fmt.Bytes(_m.TotalSize), _compact ? 12 : 15, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        size.FontFamily = UiKit.DisplayFont;
        size.HorizontalAlignment = HorizontalAlignment.Right;
        total.Children.Add(size);
        var found = UiKit.Text(Loc.T("found"), _compact ? 9 : 10, FontWeights.Normal, ThemeKeys.TextSecondary);
        found.HorizontalAlignment = HorizontalAlignment.Right;
        total.Children.Add(found);
        Grid.SetColumn(total, 2);
        header.Children.Add(total);
        var close = V.IconButton(Glyphs.CloseCircle, Loc.T("Cancel"), _m.Reset, 16, ThemeKeys.TextSecondary, 28);
        close.IsEnabled = !_m.PackageBusy;
        Grid.SetColumn(close, 3);
        header.Children.Add(close);
        root.Children.Add(header);
        root.Children.Add(UiKit.Divider());

        var list = new StackPanel { Margin = new Thickness(pad, 6, pad, 6) };
        foreach (var group in _m.Items.GroupBy(i => i.Category).OrderBy(g => (int)g.Key))
        {
            var title = CategoryTitle(group.Key);
            var label = UiKit.Text(_compact ? title.ToUpperInvariant() : title, _compact ? 9 : 13, FontWeights.SemiBold, _compact ? ThemeKeys.TextTertiary : ThemeKeys.TextSecondary);
            label.Margin = new Thickness(0, list.Children.Count == 0 ? 4 : 12, 0, 4);
            list.Children.Add(label);
            foreach (var item in group.OrderByDescending(i => i.Size))
            {
                list.Children.Add(ItemRow(item));
            }
        }

        FrameworkElement body = list;
        if (!_compact)
        {
            body = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 460, Content = list };
        }

        root.Children.Add(body);
        root.Children.Add(UiKit.Divider());

        // package manager strip
        if (WingetOperations.Shared.Status is not null && _m.Package == PackageCheck.Managed)
        {
            var op = new OperationStatusView(_compact) { Margin = new Thickness(pad, 10, pad, 6) };
            root.Children.Add(op);
        }
        else if (_m.Package == PackageCheck.Failed)
        {
            var failed = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(pad, 10, pad, 10) };
            var text = V.Orange(Loc.T("Could not verify whether winget manages this app. Refresh to try again, or uninstall it from winget."), _compact ? 9.5 : 11);
            text.MaxWidth = _compact ? 200 : 420;
            failed.Children.Add(text);
            var refresh = V.Mini(Loc.T("Refresh"), () => _ = _m.CheckPackageAsync());
            refresh.Margin = new Thickness(8, 0, 0, 0);
            failed.Children.Add(refresh);
            root.Children.Add(failed);
        }
        else if (_m.Package == PackageCheck.Managed && _m.ApplicationChecked)
        {
            var strip = V.Label(G.Package, Loc.F("{0} will also be removed from winget.", _m.WingetName ?? target.Name), _compact ? 9.5 : 11, FontWeights.Normal, ThemeKeys.TextSecondary, ThemeKeys.TextSecondary);
            strip.Margin = new Thickness(pad, 10, pad, 10);
            root.Children.Add(strip);
        }

        var footer = new Grid { Margin = new Thickness(pad) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(UiKit.Text(Loc.F("{0} of {1} selected", _m.SelectedCount, _m.Items.Count), _compact ? 10.5 : 12, FontWeights.Medium, ThemeKeys.TextPrimary));
        left.Children.Add(UiKit.Text(Fmt.Bytes(_m.SelectedSize), _compact ? 9.5 : 11, FontWeights.Normal, ThemeKeys.TextSecondary));
        if (_m.ApplicationChecked && target.QuietUninstallString.Length > 0 && !target.IsAppx && _m.Package != PackageCheck.Managed)
        {
            var quiet = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            quiet.Children.Add(new CheckMark(Host.App.Get<bool>("uninstallerSilent"), on => Host.App.Set("uninstallerSilent", on)));
            var quietLabel = UiKit.Text(Loc.T("Quiet uninstall"), 10.5, FontWeights.Normal, ThemeKeys.TextSecondary);
            quietLabel.Margin = new Thickness(6, 0, 0, 0);
            quiet.Children.Add(quietLabel);
            left.Children.Add(quiet);
        }

        footer.Children.Add(left);
        var cancel = UiKit.Button(Loc.T("Cancel"), ButtonKind.Bordered, _compact ? 11 : 13, null, _m.Reset);
        cancel.IsEnabled = !_m.PackageBusy;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(cancel, 1);
        footer.Children.Add(cancel);
        var managed = _m.Package == PackageCheck.Managed && _m.ApplicationChecked;
        var title2 = _m.ApplicationChecked ? Loc.T("Uninstall") : Loc.T("Move to Recycle Bin");
        var remove = UiKit.Button(title2, ButtonKind.Destructive, _compact ? 11 : 13, G.Trash, () => Remove(managed, target));
        remove.IsEnabled = _m.CanRemove;
        remove.FontWeight = FontWeights.SemiBold;
        remove.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(remove, 2);
        footer.Children.Add(remove);
        root.Children.Add(footer);
        return root;
    }

    private async void Remove(bool managed, InstalledProgram target)
    {
        if (managed)
        {
            var ok = Dialogs.Confirm(Loc.T("Uninstall with winget?"), Loc.F("winget will uninstall {0}. Configuration files may remain on the system.", _m.WingetName ?? target.Name), Loc.T("Uninstall"), true, Loc.T("Cancel"), _keepOpen);
            if (!ok)
            {
                return;
            }
        }

        await _m.RemoveSelectedAsync(Host.App.Get<bool>("uninstallerSilent"), ConfirmClose, ConfirmForce);
    }

    /// <summary>Lists at most eight names, then "and N more".</summary>
    private static string NameList(IReadOnlyList<string> names)
    {
        var text = string.Join('\n', names.Take(8));
        return names.Count > 8 ? text + '\n' + Loc.F("and {0} more", names.Count - 8) : text;
    }

    private bool ConfirmClose(IReadOnlyList<string> names) =>
        Dialogs.Confirm(Loc.T("Close these apps first?"), NameList(names), Loc.T("Close apps"), false, Loc.T("Cancel"), _keepOpen);

    private bool ConfirmForce(IReadOnlyList<string> names) =>
        Dialogs.Confirm(Loc.T("Some apps didn’t close"), NameList(names) + '\n' + Loc.T("Unsaved work in them will be lost."), Loc.T("Force quit"), true, Loc.T("Skip"), _keepOpen);

    private FrameworkElement ItemRow(UnItem item)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_compact ? 17 : 22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new CheckMark(item.Include, on => _m.SetItem(item, on)));
        FrameworkElement icon;
        if (item.IsRegistry)
        {
            icon = UiKit.Icon(G.Registry, _compact ? 13 : 16, ThemeKeys.TextSecondary);
        }
        else if (item.Kind is UnKind.Task or UnKind.Service)
        {
            icon = UiKit.Icon(G.Gear, _compact ? 13 : 16, ThemeKeys.TextSecondary);
        }
        else
        {
            icon = new Image { Width = _compact ? 17 : 22, Height = _compact ? 17 : 22, Source = Icons.Of(item.Kind == UnKind.Program ? (_m.Target?.MainExecutable ?? item.Path) : item.Path) };
        }

        icon.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
        text.Children.Add(UiKit.Text(item.Name, _compact ? 11 : 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        var sub = new StackPanel { Orientation = Orientation.Horizontal };
        if (item.Match == UnMatch.Related)
        {
            var related = V.Label(G.Help, Loc.T("Optional, review first"), _compact ? 9.5 : 10.5, FontWeights.Normal, ThemeKeys.Orange, ThemeKeys.Orange, 3);
            related.Margin = new Thickness(0, 0, 6, 0);
            sub.Children.Add(related);
        }

        var parent = item.IsRegistry || item.Kind is UnKind.Task or UnKind.Service ? item.Path : (System.IO.Path.GetDirectoryName(item.Path) is { } dir ? Fmt.Home(dir) : item.Path);
        var path = UiKit.Text(parent, _compact ? 9.5 : 10.5, FontWeights.Normal, ThemeKeys.TextTertiary, trimming: TextTrimming.CharacterEllipsis);
        path.ToolTip = item.Path;
        sub.Children.Add(path);
        text.Children.Add(sub);
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var reveal = V.IconButton(G.Folder, item.IsRegistry ? Loc.T("Open in Registry Editor") : Loc.T("Reveal in File Explorer"), () => Reveal(item), _compact ? 11 : 12, ThemeKeys.TextSecondary, 22);
        reveal.IsEnabled = item.Kind is UnKind.Path or UnKind.Program or UnKind.RegistryKey or UnKind.RegistryValue;
        Grid.SetColumn(reveal, 3);
        grid.Children.Add(reveal);
        var sizeText = UiKit.Text(item.Size > 0 ? Fmt.Bytes(item.Size) : string.Empty, _compact ? 10 : 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        sizeText.VerticalAlignment = VerticalAlignment.Center;
        sizeText.MinWidth = _compact ? 40 : 56;
        sizeText.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(sizeText, 4);
        grid.Children.Add(sizeText);
        return grid;
    }

    private static void Reveal(UnItem item)
    {
        if (item.IsRegistry)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
                key.SetValue("LastKey", "Computer\\" + item.Path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("regedit.exe") { UseShellExecute = true });
            }
            catch
            {
                // regedit declined
            }

            return;
        }

        FileOps.Reveal(item.Path);
    }

    // ------------------------------------------------------------------ done

    private FrameworkElement Done()
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(_compact ? 10 : 28, _compact ? 16 : 70, _compact ? 10 : 28, _compact ? 16 : 70) };
        var failed = _m.Failed.Count > 0;
        column.Children.Add(UiKit.Icon(failed ? G.Warning : G.CheckCircle, _compact ? 32 : 54, failed ? ThemeKeys.Orange : ThemeKeys.Green));
        var done = UiKit.Text(Loc.T("Done!"), _compact ? 14 : 20, FontWeights.Bold, ThemeKeys.TextPrimary);
        done.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddSpaced(done, 16);
        var freed = UiKit.Text(Loc.F("{0} removed", Fmt.Bytes(_m.Freed)), _compact ? 11 : 13, FontWeights.Normal, ThemeKeys.TextSecondary);
        freed.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddSpaced(freed, 16);
        if (_m.RecycledCount > 0)
        {
            var bin = V.Secondary(Loc.T("Removed files were moved to the Recycle Bin."), _compact ? 9.5 : 11);
            bin.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddSpaced(bin, 10);
        }

        if (_m.TooLargeCount > 0)
        {
            var large = V.Orange(Loc.F("{0} items were too large for the Recycle Bin, so they were left in place.", _m.TooLargeCount), _compact ? 9.5 : 11);
            large.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddSpaced(large, 10);
        }

        if (_m.RebootRequired)
        {
            var reboot = V.Orange(Loc.T("A restart is required to finish."), 11);
            reboot.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddSpaced(reboot, 10);
        }

        if (failed)
        {
            var note = new StackPanel { Width = _compact ? 250 : 360 };
            note.Children.Add(V.Orange(Loc.T("Some items couldn’t be removed."), _compact ? 10 : 12));
            foreach (var item in _m.Failed.Take(4))
            {
                var name = UiKit.Text(item.Name, _compact ? 9 : 10, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
                name.Margin = new Thickness(0, 2, 0, 0);
                note.Children.Add(name);
            }

            if (_m.Failed.Count > 4)
            {
                var more = V.Tertiary(Loc.F("and {0} more", _m.Failed.Count - 4), 10, false);
                more.Margin = new Thickness(0, 2, 0, 0);
                note.Children.Add(more);
            }

            if (_m.FailedNeedsAdmin)
            {
                var admin = V.Secondary(Loc.T("These items are in protected locations. Try again and accept the Windows prompt to remove them."), _compact ? 9.5 : 11);
                admin.Margin = new Thickness(0, 8, 0, 0);
                note.Children.Add(admin);
                var button = V.Mini(Loc.T("Try again"), () => _ = _m.RetryFailedElevatedAsync());
                button.HorizontalAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0, 6, 0, 0);
                note.Children.Add(button);
            }

            column.AddSpaced(note, 12);
        }

        if (_m.BackupFolder is { } backup)
        {
            var open = V.Link(Loc.T("Open backup folder"), () => FileOps.OpenFolder(backup), 11);
            open.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddSpaced(open, 10);
        }

        var another = UiKit.Button(Loc.T("Uninstall another"), ButtonKind.Prominent, _compact ? 11 : 13, null, _m.Reset);
        another.HorizontalAlignment = HorizontalAlignment.Center;
        another.Padding = new Thickness(_compact ? 12 : 22, _compact ? 4 : 7, _compact ? 12 : 22, _compact ? 4 : 7);
        column.AddSpaced(another, 22);
        return _compact ? new PanelCard(column) : column;
    }
}
