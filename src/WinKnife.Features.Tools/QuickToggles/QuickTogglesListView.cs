// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>
/// The 11-row Quick toggles list (one card of rows, Vorssaint's <c>UtilityActionButton</c> look). Used by the tray
/// "Quick toggles" tab and hosted inside the Quick panel.
/// </summary>
internal sealed class QuickTogglesListView : Border
{
    private const string OrderKey = "panelToggleOrder";

    private sealed class Row
    {
        public required string Id { get; init; }

        public required Border Wrapper { get; init; }

        public required TextBlock Icon { get; init; }

        public required TextBlock Title { get; init; }

        public required TextBlock Caption { get; init; }

        public required StackPanel Chips { get; init; }

        public required Border Handle { get; init; }

        public required Button Eye { get; init; }

        public required Border HiddenBadge { get; init; }

        public required Grid Layout { get; init; }

        public MacSwitch? Switch { get; init; }

        public TextBlock? Chevron { get; init; }
    }

    private readonly IContributionServices _ctx;
    private readonly Action<Action, int> _dismissThenRun;
    private readonly StackPanel _stack = new();
    private readonly Dictionary<string, Row> _rows = [];
    private readonly HashSet<string> _running = [];
    private readonly HashSet<string> _failed = [];
    private readonly DispatcherTimer _failTimer;
    private List<string> _order;
    private bool _editing;
    private bool _suppressRebuild;
    private int _ejectCount = -1;
    private string? _dragId;
    private double _lastLevel = 1;

    /// <param name="ctx">Services.</param>
    /// <param name="dismissThenRun">Closes the host surface (tray flyout / quick panel), then runs the action after the delay in ms (150 for lock / display off / screen saver).</param>
    /// <param name="editing">Edit mode (drag handles, hide/show eye buttons, captions visible).</param>
    public QuickTogglesListView(IContributionServices ctx, Action<Action, int> dismissThenRun, bool editing = false)
    {
        _ctx = ctx;
        _dismissThenRun = dismissThenRun;
        _editing = editing;
        _order = ReadOrder(ctx.App);
        _failTimer = new DispatcherTimer(DispatcherPriority.Normal, ctx.Ui) { Interval = TimeSpan.FromMilliseconds(2400) };
        _failTimer.Tick += (_, _) =>
        {
            _failTimer.Stop();
            _failed.Clear();
            UpdateAll();
            LayoutChanged?.Invoke();
        };
        Child = new PanelCard(_stack, 0);
        Loaded += (_, _) =>
        {
            ctx.App.Changed += OnAppChanged;
            ctx.Runtime.Changed += OnRuntimeChanged;
            ToggleKeyboardLight.Probed += OnProbed;
            _ = ToggleKeyboardLight.ProbeAsync();
            Build();
            RefreshEjectCount();
        };
        Unloaded += (_, _) =>
        {
            ctx.App.Changed -= OnAppChanged;
            ctx.Runtime.Changed -= OnRuntimeChanged;
            ToggleKeyboardLight.Probed -= OnProbed;
            _failTimer.Stop();
        };
        Build();
    }

    /// <summary>Switches edit mode (rebuilds rows).</summary>
    public bool Editing
    {
        get => _editing;
        set
        {
            if (_editing == value)
            {
                return;
            }

            _editing = value;
            Build();
            LayoutChanged?.Invoke();
        }
    }

    /// <summary>Raised when the height changed (rows hidden / captions shown) so hosts re-measure.</summary>
    public event Action? LayoutChanged;

    /// <summary>Restores the default order and shows every row (the panel "Default" button).</summary>
    public void ResetDefaults()
    {
        _suppressRebuild = true;
        try
        {
            _ctx.App.Reset(OrderKey);
            foreach (var id in QuickToggleOps.AllIds)
            {
                _ctx.App.Set(QuickToggleOps.VisibilityKey(id), true);
            }
        }
        finally
        {
            _suppressRebuild = false;
        }

        _order = ReadOrder(_ctx.App);
        Build();
        LayoutChanged?.Invoke();
    }

    /// <summary>Re-reads titles, icons, captions and state of every row.</summary>
    public void Refresh()
    {
        UpdateAll();
        RefreshEjectCount();
    }

    // ------------------------------------------------------------------------------------------- order and visibility

    /// <summary>Sanitised order (spec 2.0.3): saved ids that exist, no duplicates, then every missing id in default order.</summary>
    internal static List<string> ReadOrder(IAppSettings app)
    {
        var result = new List<string>();
        foreach (var part in (app.Get<string>(OrderKey) ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (QuickToggleOps.AllIds.Contains(part) && !result.Contains(part))
            {
                result.Add(part);
            }
        }

        foreach (var id in QuickToggleOps.AllIds)
        {
            if (!result.Contains(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    private bool IsRowVisible(string id) => _ctx.App.Get<bool>(QuickToggleOps.VisibilityKey(id), true);

    private IEnumerable<string> ShownIds() =>
        _order.Where(id => QuickToggleOps.Available(_ctx, id) && (_editing || IsRowVisible(id)));

    private void OnAppChanged(string key)
    {
        if (_suppressRebuild || !(key == OrderKey || key.StartsWith("panelToggle", StringComparison.Ordinal) || key == "panelUtilityMicMute"))
        {
            return;
        }

        if (key == OrderKey && _dragId is not null)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            _order = ReadOrder(_ctx.App);
            Build();
            LayoutChanged?.Invoke();
        });
    }

    private void OnRuntimeChanged() => Dispatcher.BeginInvoke(() =>
    {
        Build();
        LayoutChanged?.Invoke();
    });

    private void OnProbed() => Dispatcher.BeginInvoke(() =>
    {
        Build();
        LayoutChanged?.Invoke();
    });

    // ------------------------------------------------------------------------------------------- building

    private void Build()
    {
        _rows.Clear();
        foreach (var id in ShownIds())
        {
            _rows[id] = CreateRow(id);
        }

        Layout();
        UpdateAll();
    }

    private void Layout()
    {
        _stack.Children.Clear();
        var first = true;
        foreach (var id in ShownIds())
        {
            if (!_rows.TryGetValue(id, out var row))
            {
                continue;
            }

            if (!first)
            {
                _stack.Children.Add(UiKit.Divider(_editing ? 66 : 41, 10));
            }

            first = false;
            _stack.Children.Add(row.Wrapper);
        }

        Visibility = _stack.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Row CreateRow(string id)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_editing ? 16 : 0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_editing ? 9 : 0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var handle = new Border { Width = 16, Height = 22, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Visibility = _editing ? Visibility.Visible : Visibility.Collapsed, Child = UiKit.Icon(Glyphs.Handle, 11, ThemeKeys.TextTertiary, FontWeights.SemiBold) };
        grid.Children.Add(handle);

        var icon = UiKit.Icon("", 15, ThemeKeys.Accent, FontWeights.SemiBold);
        Grid.SetColumn(icon, 2);
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = UiKit.Text(string.Empty, 12, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true);
        var caption = UiKit.Text(string.Empty, 10, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.Margin = new Thickness(0, 1, 0, 0);
        text.Children.Add(title);
        text.Children.Add(caption);
        Grid.SetColumn(text, 4);
        grid.Children.Add(text);

        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chips, 5);
        grid.Children.Add(chips);

        var trailing = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(trailing, 6);
        grid.Children.Add(trailing);

        var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed };
        badge.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal };
        badgeRow.Children.Add(UiKit.Icon(Glyphs.EyeOff, 8, ThemeKeys.TextSecondary, FontWeights.Bold));
        var hiddenText = UiKit.Text(Loc.T("Hidden"), 9.5, FontWeights.Bold, ThemeKeys.TextSecondary);
        hiddenText.Margin = new Thickness(3, 0, 0, 0);
        badgeRow.Children.Add(hiddenText);
        badge.Child = badgeRow;
        trailing.Children.Add(badge);

        var eye = new Button { Width = 24, Height = 22, Padding = new Thickness(0), Cursor = Cursors.Hand, ToolTip = Loc.T("Hide from panel"), Visibility = _editing ? Visibility.Visible : Visibility.Collapsed, Content = UiKit.Icon(Glyphs.EyeOff, 11, ThemeKeys.TextSecondary, FontWeights.SemiBold) };
        eye.SetResourceReference(StyleProperty, ThemeKeys.ButtonPlain);
        trailing.Children.Add(eye);

        TextBlock? chevron = null;
        MacSwitch? sw = null;
        if (id == "keyboardLight")
        {
            sw = new MacSwitch(ToggleKeyboardLight.Level > 0, SwitchSize.Small, on =>
            {
                if (!on)
                {
                    var level = ToggleKeyboardLight.Level;
                    if (level > 0)
                    {
                        _lastLevel = level;
                    }
                }

                ToggleKeyboardLight.SetLevel(on ? _lastLevel : 0);
            })
            {
                Visibility = _editing ? Visibility.Collapsed : Visibility.Visible,
            };
            trailing.Children.Add(sw);
        }
        else
        {
            chevron = UiKit.Icon(Glyphs.ChevronRight, 9, ThemeKeys.TextTertiary, FontWeights.SemiBold);
            chevron.Visibility = _editing ? Visibility.Collapsed : Visibility.Visible;
            trailing.Children.Add(chevron);
        }

        var wrapper = new Border
        {
            Margin = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(7, 8, 7, 8),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = _editing || id == "keyboardLight" ? Cursors.Arrow : Cursors.Hand,
            Child = grid,
            AllowDrop = _editing,
        };
        wrapper.MouseEnter += (_, _) =>
        {
            if (!_editing)
            {
                wrapper.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
            }
        };
        wrapper.MouseLeave += (_, _) => wrapper.Background = Brushes.Transparent;
        if (id != "keyboardLight")
        {
            wrapper.MouseLeftButtonUp += (_, e) =>
            {
                if (!e.Handled)
                {
                    Activate(id);
                }
            };
        }

        var row = new Row { Id = id, Wrapper = wrapper, Icon = icon, Title = title, Caption = caption, Chips = chips, Handle = handle, Eye = eye, HiddenBadge = badge, Layout = grid, Switch = sw, Chevron = chevron };

        eye.Click += (_, e) =>
        {
            e.Handled = true;
            _suppressRebuild = true;
            try
            {
                _ctx.App.Set(QuickToggleOps.VisibilityKey(id), !IsRowVisible(id));
            }
            finally
            {
                _suppressRebuild = false;
            }

            UpdateRow(row);
        };
        handle.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragId is null)
            {
                _dragId = id;
                wrapper.Opacity = 0.5;
                try
                {
                    DragDrop.DoDragDrop(handle, new DataObject("wk.quicktoggle", id), DragDropEffects.Move);
                }
                finally
                {
                    wrapper.Opacity = 1;
                    EndDrag();
                }
            }
        };
        wrapper.DragEnter += (_, e) => DragOverRow(id, e);
        wrapper.DragOver += (_, e) => DragOverRow(id, e);
        wrapper.Drop += (_, e) =>
        {
            e.Handled = true;
        };
        return row;
    }

    private void DragOverRow(string targetId, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        if (_dragId is null || _dragId == targetId)
        {
            return;
        }

        var from = _order.IndexOf(_dragId);
        var to = _order.IndexOf(targetId);
        if (from < 0 || to < 0)
        {
            return;
        }

        _order.RemoveAt(from);
        _order.Insert(to, _dragId);
        Layout();
    }

    private void EndDrag()
    {
        _dragId = null;
        _suppressRebuild = true;
        try
        {
            _ctx.App.Set(OrderKey, string.Join(",", _order));
        }
        finally
        {
            _suppressRebuild = false;
        }
    }

    // ------------------------------------------------------------------------------------------- state

    private void UpdateAll()
    {
        foreach (var row in _rows.Values)
        {
            UpdateRow(row);
        }
    }

    private void UpdateRow(Row row)
    {
        var id = row.Id;
        var hidden = _editing && !IsRowVisible(id);
        var failed = _failed.Contains(id);
        var running = _running.Contains(id);
        row.Title.Text = QuickToggleOps.Title(_ctx, id);
        row.Icon.Text = QuickToggleOps.Glyph(id);
        var attention = failed;
        row.Icon.SetResourceReference(TextBlock.ForegroundProperty, attention ? ThemeKeys.Orange : hidden ? ThemeKeys.TextSecondary : id == "micMute" && _ctx.Services.Get<IMicMuteState>()?.IsMuted == true ? ThemeKeys.Red : ThemeKeys.Accent);
        row.Title.SetResourceReference(TextBlock.ForegroundProperty, hidden ? ThemeKeys.TextSecondary : ThemeKeys.TextPrimary);

        var plain = QuickToggleOps.Caption(id);
        var caption = failed ? Loc.T("Could not complete.") : plain;
        var visibleCaption = failed || _editing || QuickToggleOps.CaptionStaysVisible(id);
        if (id == "ejectDisks")
        {
            caption = failed ? Loc.T("Could not complete.") : running ? Loc.T("Ejecting…") : _ejectCount == 0 ? Loc.T("No external disk ready to eject.") : plain;
            visibleCaption |= caption != plain;
        }

        row.Caption.Text = caption;
        row.Caption.Visibility = visibleCaption ? Visibility.Visible : Visibility.Collapsed;
        row.Caption.SetResourceReference(TextBlock.ForegroundProperty, failed ? ThemeKeys.Orange : hidden ? ThemeKeys.TextTertiary : ThemeKeys.TextSecondary);
        row.Wrapper.ToolTip = plain;
        row.Wrapper.IsEnabled = !running;
        row.Wrapper.Opacity = running ? 0.55 : 1;

        row.Chips.Children.Clear();
        if (QuickToggleOps.ShortcutRole(id) is { } role && ShortcutText.Of(_ctx, role) is { } chipText && !_editing)
        {
            row.Chips.Children.Add(new ShortcutChip(chipText));
        }

        row.HiddenBadge.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
        var shown = IsRowVisible(id);
        row.Eye.Content = UiKit.Icon(shown ? Glyphs.EyeOff : Glyphs.Eye, 11, shown ? ThemeKeys.TextSecondary : ThemeKeys.Accent, FontWeights.SemiBold);
        row.Eye.ToolTip = shown ? Loc.T("Hide from panel") : Loc.T("Show in panel");
        if (row.Switch is { } sw)
        {
            sw.IsOn = ToggleKeyboardLight.Level > 0;
        }
    }

    private void RefreshEjectCount()
    {
        if (!_rows.ContainsKey("ejectDisks"))
        {
            return;
        }

        _ = Task.Run(() => QuickToggleOps.EjectableVolumeCount).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                var before = _ejectCount;
                _ejectCount = t.Result;
                if (before != _ejectCount)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        UpdateAll();
                        LayoutChanged?.Invoke();
                    });
                }
            }
        });
    }

    // ------------------------------------------------------------------------------------------- running

    private void Activate(string id)
    {
        if (_editing || _running.Contains(id))
        {
            return;
        }

        if (QuickToggleOps.NeedsDismiss(id))
        {
            _dismissThenRun(() => _ = Execute(id), 150);
            return;
        }

        _ = Execute(id);
    }

    private async Task Execute(string id)
    {
        _running.Add(id);
        _failed.Remove(id);
        if (_rows.TryGetValue(id, out var row))
        {
            UpdateRow(row);
            LayoutChanged?.Invoke();
        }

        var ok = false;
        try
        {
            ok = await QuickToggleOps.RunAsync(_ctx, id);
        }
        catch
        {
            ok = false;
        }

        _running.Remove(id);
        if (!ok)
        {
            _failed.Add(id);
            _failTimer.Stop();
            _failTimer.Start();
        }

        UpdateAll();
        LayoutChanged?.Invoke();
        Plat.After(_ctx.Ui, 300, () =>
        {
            UpdateAll();
            RefreshEjectCount();
            LayoutChanged?.Invoke();
        });
    }
}



