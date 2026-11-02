// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>
/// The floating Quick panel (Vorssaint's Quick Launcher, spec 2.1): a 420 px glass window with a 3-column grid of the tools,
/// an edit mode (hide / reorder / options) and hosted utilities.
/// </summary>
internal sealed class QuickPanelWindow : GlassWindow
{
    private const int Columns = 3;
    private const string DragFormat = "wk.quickitem";

    private readonly IContributionServices _ctx;
    private readonly Func<string> _shortcutText;
    private readonly StackPanel _root = new() { Margin = new Thickness(16) };
    private readonly Dictionary<string, Border> _tiles = [];
    private readonly Dictionary<string, (Border Icon, TextBlock Glyph, TextBlock Title, FrameworkElement Dot)> _tileParts = [];
    private readonly QuickPanelContext _hostContext;
    private UniformGrid? _grid;
    private List<QuickItem> _visible = [];
    private QuickItem? _hosted;
    private FrameworkElement? _hostedView;
    private QuickItem? _optionsItem;
    private bool _editing;
    private int _selected;
    private OutsideClickMonitor? _outside;
    private ForegroundWatcher? _foreground;
    private Point _dragStart;
    private bool _presented;
    private IKeepAwakeService? _keepAwake;
    private IMicMuteState? _mic;
    private IRecordingState? _recording;

    public QuickPanelWindow(IContributionServices ctx, Func<string> shortcutText)
        : base(22)
    {
        _ctx = ctx;
        _shortcutText = shortcutText;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        Title = "WinKnife";
        _hostContext = new QuickPanelContext(ctx, HidePanel, () => { }, id => HostById(id));
        Body = _root;
        PreviewKeyDown += OnPreviewKeyDown;
        SizeChanged += (_, e) =>
        {
            if (_presented && e.HeightChanged)
            {
                Refit();
            }
        };
        ThemeManager.ThemeChanged += _ =>
        {
            if (_presented)
            {
                Rebuild();
            }
        };
    }

    /// <summary>True while the panel is on screen.</summary>
    public bool IsPresented => _presented;

    /// <summary>Test seam: puts the visible panel into edit mode / an options card / a hosted utility without clicking.</summary>
    internal void DebugState(bool editing, string? optionsId = null, string? hostedId = null)
    {
        _editing = editing;
        _optionsItem = optionsId is null ? null : QuickItems.Get(optionsId);
        _hosted = null;
        _hostedView = null;
        if (hostedId is not null && QuickItems.Get(hostedId) is { } item)
        {
            RunItem(item);
            return;
        }

        Rebuild();
    }

    // ---------------------------------------------------------------- presentation

    /// <summary>Shows the panel on the pointer's screen (resets edit / hosted state).</summary>
    public void ShowPanel()
    {
        if (_presented)
        {
            Activate();
            return;
        }

        _editing = false;
        _optionsItem = null;
        _hosted = null;
        _hostedView = null;
        _selected = 0;
        Rebuild();
        Plate.Opacity = 0;
        PositionFresh();
        Show();
        Activate();
        Plate.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(130)));
        _presented = true;
        _outside = new OutsideClickMonitor(_ctx.Hooks, _ctx.Ui, FrameBounds, HidePanel, DismissesOnOutside);
        _foreground = new ForegroundWatcher(_ctx.Ui);
        _foreground.ForegroundChanged += hwnd =>
        {
            if (_presented && DismissesOnOutside() && Plat.ProcessOf(hwnd) != Environment.ProcessId)
            {
                HidePanel();
            }
        };
        _keepAwake = _ctx.Services.Get<IKeepAwakeService>();
        _mic = _ctx.Services.Get<IMicMuteState>();
        _recording = _ctx.Services.Get<IRecordingState>();
        if (_recording is not null)
        {
            _recording.Changed += OnStateChanged;
        }

        if (_keepAwake is not null)
        {
            _keepAwake.Changed += OnStateChanged;
        }

        if (_mic is not null)
        {
            _mic.Changed += OnStateChanged;
        }
    }

    /// <summary>Hides the panel and resets edit / hosted state.</summary>
    public void HidePanel()
    {
        if (!_presented)
        {
            return;
        }

        _presented = false;
        _outside?.Dispose();
        _foreground?.Dispose();
        _outside = null;
        _foreground = null;
        if (_recording is not null)
        {
            _recording.Changed -= OnStateChanged;
            _recording = null;
        }

        if (_keepAwake is not null)
        {
            _keepAwake.Changed -= OnStateChanged;
        }

        if (_mic is not null)
        {
            _mic.Changed -= OnStateChanged;
        }

        _editing = false;
        _optionsItem = null;
        _hosted = null;
        _hostedView = null;
        Hide();
    }

    private bool DismissesOnOutside() => _hosted is null && !_editing;

    private void OnStateChanged() => _ctx.Ui.BeginInvoke(() =>
    {
        if (_presented)
        {
            UpdateTileVisuals();
        }
    });

    private Rect FrameBounds()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        return handle != 0 && NativeMethods.GetWindowRect(handle, out var r) ? new Rect(r.Left, r.Top, r.Width, r.Height) : Rect.Empty;
    }

    private void PositionFresh()
    {
        Body!.Measure(new Size(420, double.PositiveInfinity));
        var area = Screens.UnderCursor().WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var h = Body.DesiredSize.Height * dpi;
        var left = area.Left + (area.Width - 420 * dpi) / 2;
        var top = area.Top + (area.Height - h) * 0.38;
        left = Math.Max(area.Left + 16, Math.Min(left, area.Right - 420 * dpi - 16));
        top = Math.Max(area.Top + 16, Math.Min(top, area.Bottom - h - 16));
        Plat.PlaceWindow(this, left, top, 420, 0);
    }

    /// <summary>Keeps the top edge and horizontal centre while the height changes; stays 16 px inside the work area.</summary>
    private void Refit()
    {
        var area = Screens.FromPoint(new Point((Left + ActualWidth / 2) * VisualTreeHelper.GetDpi(this).DpiScaleX, Top * VisualTreeHelper.GetDpi(this).DpiScaleX)).WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var bottom = area.Bottom / dpi - 16;
        if (Top + ActualHeight > bottom)
        {
            Top = Math.Max(area.Top / dpi + 16, bottom - ActualHeight);
        }
    }

    // ---------------------------------------------------------------- brushes

    private static Color PrimaryColor => ThemeManager.IsLight ? Colors.Black : Colors.White;

    private static Brush Primary(double alpha) => new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), PrimaryColor.R, PrimaryColor.G, PrimaryColor.B));

    private static Brush AccentA(double alpha) => new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), ThemeManager.AccentColor.R, ThemeManager.AccentColor.G, ThemeManager.AccentColor.B));

    // ---------------------------------------------------------------- state

    private List<QuickItem> ComputeVisible(bool includeHidden)
    {
        var hidden = QuickItems.Hidden(_ctx.App);
        return QuickItems.Order(_ctx.App)
            .Select(QuickItems.Get)
            .Where(i => i is not null && _ctx.Runtime.IsAvailable(i.FeatureId) && (includeHidden || !hidden.Contains(i.Id)))
            .Select(i => i!)
            .ToList();
    }

    private bool IsActiveItem(QuickItem item) => item.Id switch
    {
        "keepAwake" => _keepAwake?.IsActive ?? _ctx.Services.Get<IKeepAwakeService>()?.IsActive ?? false,
        "micMute" => (_mic ?? _ctx.Services.Get<IMicMuteState>())?.IsMuted ?? false,
        "screenRecorder" => (_recording ?? _ctx.Services.Get<IRecordingState>())?.IsRecording ?? false,
        _ => false,
    };

    private string TitleOf(QuickItem item) => item.Id switch
    {
        "micMute" when IsActiveItem(item) => Loc.T("Unmute microphone"),
        "screenRecorder" when IsActiveItem(item) => Loc.T("Stop recording"),
        _ => item.Title,
    };

    private string GlyphOf(QuickItem item) => item.Id switch
    {
        "micMute" when IsActiveItem(item) => "",
        _ => QuickItems.GlyphOf(item),
    };

    // ---------------------------------------------------------------- build

    private void Rebuild()
    {
        _root.Children.Clear();
        _tiles.Clear();
        _tileParts.Clear();
        _grid = null;
        _visible = ComputeVisible(false);
        _selected = _visible.Count == 0 ? -1 : Math.Clamp(_selected, 0, _visible.Count - 1);
        _root.Children.Add(BuildHeader());
        if (_editing && _hosted is null)
        {
            var hint = UiKit.Text(Loc.T("Use the tune button to choose, hide and drag the tools around."), 9.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            hint.TextAlignment = TextAlignment.Center;
            hint.Margin = new Thickness(0, 6, 0, 0);
            _root.Children.Add(hint);
        }

        FrameworkElement body;
        if (_hosted is not null)
        {
            body = BuildHosted();
        }
        else if (_visible.Count == 0 && !_editing)
        {
            var empty = UiKit.Text(Loc.T("All tools are hidden. Use the tune button to add them back."), 11, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            empty.TextAlignment = TextAlignment.Center;
            empty.Margin = new Thickness(0, 26, 0, 26);
            body = empty;
        }
        else
        {
            body = BuildGrid();
        }

        body.Margin = new Thickness(0, 12, 0, 0);
        _root.Children.Add(body);
        if (_hosted is null && _editing && _optionsItem is not null)
        {
            var card = BuildOptionsCard(_optionsItem);
            card.Margin = new Thickness(0, 12, 0, 0);
            _root.Children.Add(card);
        }

        if (_hosted is null && _editing)
        {
            var hidden = ComputeVisible(true).Where(i => QuickItems.Hidden(_ctx.App).Contains(i.Id)).ToList();
            if (hidden.Count > 0)
            {
                _root.Children.Add(BuildAddBack(hidden));
            }
        }

        _root.Children.Add(BuildFooter());
        UpdateTileVisuals();
    }

    private FrameworkElement BuildHeader()
    {
        var grid = new Grid { Height = 30 };
        var mark = BrandMark.Create(52, ThemeKeys.TextPrimary);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.Opacity = 0.95;
        grid.Children.Add(mark);

        FrameworkElement left;
        FrameworkElement right;
        if (_hosted is not null)
        {
            left = HeaderButton("", Loc.T("Back"), () =>
            {
                _hosted = null;
                _hostedView = null;
                Rebuild();
            }, false);
            right = HeaderButton("", Loc.T("Close"), HidePanel, false);
        }
        else
        {
            left = HeaderButton("", Loc.T("Close"), HidePanel, false);
            right = HeaderButton(_editing ? "" : "", _editing ? Loc.T("Done") : Loc.T("Use the tune button to choose, hide and drag the tools around."), () =>
            {
                _editing = !_editing;
                _optionsItem = null;
                Rebuild();
            }, _editing);
        }

        left.HorizontalAlignment = HorizontalAlignment.Left;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static Border HeaderButton(string glyph, string tip, Action click, bool accent)
    {
        var icon = UiKit.Icon(glyph, 15, accent ? ThemeKeys.Accent : ThemeKeys.TextSecondary, FontWeights.SemiBold);
        var button = new Border { Width = 26, Height = 26, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, Child = icon };
        button.MouseLeftButtonUp += (_, _) => click();
        return button;
    }

    private FrameworkElement BuildGrid()
    {
        _grid = new UniformGrid { Columns = Columns, Margin = new Thickness(-5) };
        var items = _editing ? _visible : _visible;
        for (var i = 0; i < items.Count; i++)
        {
            var tile = BuildTile(items[i], i);
            _grid.Children.Add(tile);
        }

        return _grid;
    }

    private Border BuildTile(QuickItem item, int index)
    {
        var tile = new Border
        {
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(0, 9, 0, 9),
            Margin = new Thickness(5),
            BorderThickness = new Thickness(1.2),
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Tag = item.Id,
            AllowDrop = _editing,
        };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var iconHost = new Grid { Width = 46, Height = 46, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = new Border { CornerRadius = new CornerRadius(12) };
        var glyph = UiKit.Icon(QuickItems.GlyphOf(item), 19, ThemeKeys.TextPrimary, FontWeights.SemiBold);
        icon.Child = glyph;
        iconHost.Children.Add(icon);

        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -3, -3, 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        dot.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Green);
        iconHost.Children.Add(dot);

        if (!_editing && index < 9)
        {
            var badge = new Border
            {
                Width = 13,
                Height = 13,
                CornerRadius = new CornerRadius(6.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-6, -6, 0, 0),
                IsHitTestVisible = false,
                Tag = "badge",
                Child = new TextBlock { Text = (index + 1).ToString(), FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            iconHost.Children.Add(badge);
        }

        if (_editing)
        {
            var minus = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -7, -7, 0),
                Cursor = Cursors.Hand,
                ToolTip = Loc.T("Hide from panel"),
                Child = new Border { Width = 7, Height = 1.6, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            minus.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                var hidden = QuickItems.Hidden(_ctx.App);
                hidden.Add(item.Id);
                QuickItems.SaveHidden(_ctx.App, hidden);
                if (_optionsItem?.Id == item.Id)
                {
                    _optionsItem = null;
                }

                Rebuild();
            };
            iconHost.Children.Add(minus);
            if (item.HasOptions)
            {
                var open = _optionsItem?.Id == item.Id;
                var gear = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(-7, -7, 0, 0),
                    IsHitTestVisible = false,
                    Child = new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = 8, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                gear.Background = open ? AccentA(1) : new SolidColorBrush(Color.FromArgb(0xCC, 0x80, 0x80, 0x80));
                iconHost.Children.Add(gear);
            }
        }

        stack.Children.Add(iconHost);
        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 10.5,
            FontWeight = FontWeights.Medium,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6, 7, 6, 0),
            MinHeight = 28,
            MaxHeight = 28,
            TextTrimming = TextTrimming.None,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
        stack.Children.Add(title);
        tile.Child = stack;
        _tiles[item.Id] = tile;
        _tileParts[item.Id] = (icon, glyph, title, dot);

        tile.MouseEnter += (_, _) =>
        {
            if (!_editing)
            {
                _selected = _visible.FindIndex(v => v.Id == item.Id);
                UpdateTileVisuals();
            }
        };
        tile.MouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        tile.MouseLeftButtonUp += (_, _) =>
        {
            if (!_editing)
            {
                RunItem(item);
            }
            else if (item.HasOptions)
            {
                _optionsItem = _optionsItem?.Id == item.Id ? null : item;
                Rebuild();
            }
        };
        if (_editing)
        {
            tile.MouseMove += (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(this) - _dragStart).Length > 6)
                {
                    DragDrop.DoDragDrop(tile, new DataObject(DragFormat, item.Id), DragDropEffects.Move);
                }
            };
            tile.DragEnter += OnTileDrag;
            tile.DragOver += OnTileDrag;
            tile.Drop += (_, e) =>
            {
                e.Handled = true;
                QuickItems.SaveOrder(_ctx.App, QuickItems.Order(_ctx.App));
            };
        }

        return tile;
    }

    private void OnTileDrag(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        if (_grid is null || e.Data.GetData(DragFormat) is not string dragged || sender is not Border { Tag: string target } || dragged == target)
        {
            return;
        }

        var order = QuickItems.Order(_ctx.App);
        var from = order.IndexOf(dragged);
        var to = order.IndexOf(target);
        if (from < 0 || to < 0)
        {
            return;
        }

        order.RemoveAt(from);
        order.Insert(to, dragged);
        QuickItems.SaveOrder(_ctx.App, order);
        var dragTile = _tiles[dragged];
        var targetIndex = _grid.Children.IndexOf(_tiles[target]);
        _grid.Children.Remove(dragTile);
        _grid.Children.Insert(Math.Clamp(targetIndex, 0, _grid.Children.Count), dragTile);
        _visible = ComputeVisible(false);
    }

    private void UpdateTileVisuals()
    {
        for (var i = 0; i < _visible.Count; i++)
        {
            var item = _visible[i];
            if (!_tiles.TryGetValue(item.Id, out var tile) || !_tileParts.TryGetValue(item.Id, out var parts))
            {
                continue;
            }

            var selected = !_editing && i == _selected;
            var hovered = !_editing && tile.IsMouseOver;
            var active = !_editing && IsActiveItem(item);
            tile.Background = selected ? AccentA(0.14) : hovered ? Primary(0.07) : Primary(0.035);
            tile.BorderBrush = selected ? AccentA(0.55) : Brushes.Transparent;
            parts.Icon.Background = active && item.Id != "micMute" ? AccentA(0.18) : selected || hovered ? Primary(0.10) : Primary(0.07);
            parts.Glyph.Text = GlyphOf(item);
            parts.Title.Text = TitleOf(item);
            if (item.Id == "micMute" && IsActiveItem(item))
            {
                parts.Glyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Red);
            }
            else if (active)
            {
                parts.Glyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Accent);
            }
            else
            {
                parts.Glyph.Foreground = Primary(0.85);
            }

            parts.Dot.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (tile.Child is StackPanel { Children.Count: > 0 } stack && stack.Children[0] is Grid host)
            {
                foreach (var child in host.Children)
                {
                    if (child is Border { Tag: "badge" } badge)
                    {
                        badge.Background = selected ? AccentA(1) : new SolidColorBrush(Color.FromArgb(0xCC, 0x6E, 0x6E, 0x73));
                    }
                }
            }
        }
    }

    private FrameworkElement BuildAddBack(List<QuickItem> hidden)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var heading = UiKit.Text(Loc.T("ADD BACK"), 9, FontWeights.Bold, ThemeKeys.TextSecondary);
        panel.Children.Add(heading);
        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var item in hidden)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(100),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = AccentA(0.13),
                Cursor = Cursors.Hand,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(UiKit.Icon("", 9, ThemeKeys.Accent, FontWeights.SemiBold));
            var text = UiKit.Text(item.Title, 10, FontWeights.Medium, ThemeKeys.TextPrimary);
            text.Margin = new Thickness(5, 0, 0, 0);
            row.Children.Add(text);
            chip.Child = row;
            chip.MouseLeftButtonUp += (_, _) =>
            {
                var set = QuickItems.Hidden(_ctx.App);
                set.Remove(item.Id);
                QuickItems.SaveHidden(_ctx.App, set);
                Rebuild();
            };
            wrap.Children.Add(chip);
        }

        panel.Children.Add(wrap);
        return panel;
    }

    private FrameworkElement BuildFooter()
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(UiKit.Icon("", 8.5, ThemeKeys.TextTertiary));
        var shortcut = UiKit.Text(_shortcutText(), 9, FontWeights.SemiBold, ThemeKeys.TextTertiary);
        shortcut.FontFamily = UiKit.DisplayFont;
        shortcut.Margin = new Thickness(5, 0, 0, 0);
        left.Children.Add(shortcut);
        grid.Children.Add(left);
        var esc = UiKit.Text("Esc", 9, FontWeights.SemiBold, ThemeKeys.TextTertiary);
        esc.FontFamily = UiKit.DisplayFont;
        Grid.SetColumn(esc, 1);
        grid.Children.Add(esc);
        return grid;
    }

    // ---------------------------------------------------------------- options card

    private FrameworkElement BuildOptionsCard(QuickItem item)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Background = Primary(0.05) };
        var stack = new StackPanel();
        var header = new Grid();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(UiKit.Icon(QuickItems.GlyphOf(item), 11, ThemeKeys.TextSecondary));
        var name = UiKit.Text(item.Title, 11, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        name.Margin = new Thickness(6, 0, 0, 0);
        title.Children.Add(name);
        header.Children.Add(title);
        var close = UiKit.Icon("", 12, ThemeKeys.TextSecondary);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Cursor = Cursors.Hand;
        close.ToolTip = Loc.T("Close");
        close.MouseLeftButtonUp += (_, _) =>
        {
            _optionsItem = null;
            Rebuild();
        };
        header.Children.Add(close);
        stack.Children.Add(header);

        switch (item.Id)
        {
            case "keepAwake":
                stack.AddSpaced(OptionToggle(Loc.T("Keep going with the lid closed"), "clamshellPreferred", false), 9);
                stack.AddSpaced(OptionPicker(Loc.T("Default duration"), "defaultDuration", [(Loc.T("15 minutes"), 15), (Loc.T("30 minutes"), 30), (Loc.T("1 hour"), 60), (Loc.T("2 hours"), 120), (Loc.T("4 hours"), 240), (Loc.T("8 hours"), 480), (Loc.T("Indefinite"), 0)], 0), 9);
                break;
            case "micMute":
                stack.AddSpaced(OptionToggle(Loc.T("Show in the system tray while Mic Mute is on"), "micMuteMenuBarIndicator", true), 9);
                break;
            case "clipboard":
                stack.AddSpaced(OptionToggle(Loc.T("Save clipboard history"), "clipboardHistoryEnabled", false, rebuild: true), 9);
                if (_ctx.App.Get<bool>("clipboardHistoryEnabled"))
                {
                    stack.AddSpaced(OptionPicker(Loc.T("Limit"), "clipboardHistoryLimit", [("20", 20), ("50", 50), ("100", 100), ("250", 250), ("500", 500), ("1000", 1000), ("10000", 10000), (Loc.T("Unlimited"), 0)], 50), 9);
                }

                break;
            case "colorPicker":
                var formats = new[] { "hex", "rgb", "hsl", "swiftui" };
                var current = Math.Max(0, Array.IndexOf(formats, _ctx.App.Get<string>("colorPickerFormat") ?? "hex"));
                var seg = new SegmentedControl(["HEX", "RGB", "HSL", "SwiftUI"], current, stretch: true, fontSize: 11);
                seg.SelectionChanged += i =>
                {
                    _ctx.App.Set("colorPickerFormat", formats[i]);
                    Rebuild();
                };
                stack.AddSpaced(seg, 9);
                if (current == 0)
                {
                    stack.AddSpaced(OptionToggle(Loc.T("Copy without the # prefix"), "colorPickerBareHex", false), 9);
                }

                break;
        }

        card.Child = stack;
        return card;
    }

    private FrameworkElement OptionToggle(string label, string key, bool fallback, bool rebuild = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = UiKit.Text(label, 11, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        var toggle = new MacSwitch(_ctx.App.Get(key, fallback), SwitchSize.Small, on =>
        {
            _ctx.App.Set(key, on);
            if (rebuild)
            {
                Rebuild();
            }
        })
        { Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    private FrameworkElement OptionPicker(string label, string key, (string Label, int Value)[] options, int fallback)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = UiKit.Text(label, 11, FontWeights.Normal, ThemeKeys.TextPrimary);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        var combo = new ComboBox { MinWidth = 120, FontSize = 11, ItemsSource = options.Select(o => o.Label).ToList() };
        var value = _ctx.App.Get(key, fallback);
        var index = Array.FindIndex(options, o => o.Value == value);
        combo.SelectedIndex = index < 0 ? 0 : index;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                _ctx.App.Set(key, options[combo.SelectedIndex].Value);
            }
        };
        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);
        return grid;
    }

    // ---------------------------------------------------------------- hosted

    private FrameworkElement BuildHosted()
    {
        var panel = new StackPanel();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(UiKit.Icon(QuickItems.GlyphOf(_hosted!), 13, ThemeKeys.TextPrimary, FontWeights.SemiBold));
        var name = UiKit.Text(_hosted!.Title, 12, FontWeights.SemiBold);
        name.Margin = new Thickness(6, 0, 0, 0);
        title.Children.Add(name);
        header.Children.Add(title);
        var close = HeaderButton("", Loc.T("Cancel"), () =>
        {
            _hosted = null;
            _hostedView = null;
            Rebuild();
        }, false);
        close.Width = 22;
        close.Height = 22;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(close);
        panel.Children.Add(header);
        var scroll = new ScrollViewer
        {
            Height = 470 - 32,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _hostedView,
        };
        panel.Children.Add(scroll);
        return panel;
    }

    private void HostById(string utilityId)
    {
        var provider = UtilityProviders.All().FirstOrDefault(u => u.Id == utilityId);
        var item = provider is null ? null : QuickItems.All.FirstOrDefault(i => provider.FeatureIds.Contains(i.FeatureId));
        if (provider is not null && item is not null)
        {
            Host(item, provider);
        }
    }

    private void Host(QuickItem item, IPanelUtility provider)
    {
        var view = provider.CreateHostedView(_hostContext);
        if (view is null)
        {
            HidePanel();
            Plat.After(_ctx.Ui, provider.InvokeDelayMs, () => provider.Invoke(_hostContext));
            return;
        }

        _hosted = item;
        _hostedView = view;
        Rebuild();
    }

    // ---------------------------------------------------------------- running

    private void RunItem(QuickItem item)
    {
        if (_editing || !_ctx.Runtime.IsAvailable(item.FeatureId))
        {
            return;
        }

        switch (item.Id)
        {
            case "keepAwake":
                ToggleKeepAwake();
                UpdateTileVisuals();
                return;
            case "micMute":
                ActionBridge.Invoke(_ctx, "mic-mute", "toggle");
                _ctx.Ui.BeginInvoke(UpdateTileVisuals, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                return;
            case "toggles":
                _hosted = item;
                _hostedView = new QuickTogglesListView(_ctx, (action, delay) =>
                {
                    HidePanel();
                    Plat.After(_ctx.Ui, Math.Max(delay, 1), action);
                }, false);
                Rebuild();
                return;
            case "cleaner" or "homebrew" or "media" or "urlCleaner" or "uninstaller" or "windowLayout":
                var provider = UtilityProviders.For(item.FeatureId).FirstOrDefault();
                if (provider is not null)
                {
                    Host(item, provider);
                }

                return;
            case "screenOCR":
                Later(150, "screenOCR");
                return;
            case "colorPicker":
                Later(150, "colorPicker");
                return;
            case "clipboard":
                Later(100, "clipboardHistory");
                return;
            case "cleaning":
                Later(100, "cleaningMode");
                return;
            case "screenshot":
                Later(150, "screenshot");
                return;
            case "screenRecorder":
                Later(150, "screenRecorder");
                return;
            case "cameraPreview":
                Later(150, "cameraPreview");
                return;
            case "scratchpad":
                Later(150, "scratchpad");
                return;
        }
    }

    private void Later(int delayMs, string tool)
    {
        HidePanel();
        Plat.After(_ctx.Ui, delayMs, () => ToolActions.Run(_ctx, tool));
    }

    private void ToggleKeepAwake()
    {
        var service = _ctx.Services.Get<IKeepAwakeService>();
        if (service is null)
        {
            ActionBridge.Invoke(_ctx, "keep-awake", "toggle");
            return;
        }

        if (service.IsActive)
        {
            service.Deactivate();
            return;
        }

        var minutes = _ctx.App.Get<int>("defaultDuration");
        service.Activate(minutes > 0 ? TimeSpan.FromMinutes(minutes) : null);
    }

    // ---------------------------------------------------------------- keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed)
        {
            return;
        }

        var key = e.Key;
        if (key == Key.Escape)
        {
            e.Handled = true;
            if (_hosted is not null)
            {
                _hosted = null;
                _hostedView = null;
                Rebuild();
            }
            else if (_optionsItem is not null)
            {
                _optionsItem = null;
                Rebuild();
            }
            else if (_editing)
            {
                _editing = false;
                Rebuild();
            }
            else
            {
                HidePanel();
            }

            return;
        }

        if (_hosted is not null || _editing || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            return;
        }

        if (_visible.Count == 0)
        {
            return;
        }

        switch (key)
        {
            case Key.Left: MoveSelection(-1, 0, e); break;
            case Key.Right: MoveSelection(1, 0, e); break;
            case Key.Up: MoveSelection(0, -1, e); break;
            case Key.Down: MoveSelection(0, 1, e); break;
            case Key.Return:
                e.Handled = true;
                if (_selected >= 0 && _selected < _visible.Count)
                {
                    RunItem(_visible[_selected]);
                }

                break;
            default:
                var digit = key >= Key.D1 && key <= Key.D9 ? key - Key.D1 : key >= Key.NumPad1 && key <= Key.NumPad9 ? key - Key.NumPad1 : -1;
                if (digit >= 0 && digit < _visible.Count)
                {
                    e.Handled = true;
                    RunItem(_visible[digit]);
                }

                break;
        }
    }

    private void MoveSelection(int dx, int dy, KeyEventArgs e)
    {
        e.Handled = true;
        var count = _visible.Count;
        var i = _selected < 0 ? 0 : _selected;
        var next = i;
        if (dx < 0 && i % Columns != 0)
        {
            next = i - 1;
        }
        else if (dx > 0 && i % Columns != Columns - 1)
        {
            next = i + 1;
        }
        else if (dy < 0)
        {
            next = i - Columns;
        }
        else if (dy > 0)
        {
            next = i + Columns;
        }

        if (next >= 0 && next < count)
        {
            _selected = next;
            UpdateTileVisuals();
        }
    }
}
