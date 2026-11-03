// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Trayloom.Core;
using Trayloom.Core.Ui;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.IO.Path;
using Point = System.Windows.Point;

namespace Trayloom.Features.Files;

/// <summary>What the shelf panel asks of the window and the feature that host it.</summary>
internal interface IShelfActions
{
    ShelfStore Store { get; }

    IContributionServices Services { get; }

    bool IsPinned { get; }

    bool IsDropTargeted { get; set; }

    void TogglePin();

    /// <summary>The close button (floating: closes; docked: collapses).</summary>
    void Dismiss();

    void DragOutFinished(IReadOnlyList<string> ids, DragDropEffects effect);

    void Share(IReadOnlyList<string> paths, FrameworkElement anchor);

    void PointerInsidePanel(bool inside);

    void MoveWindow();
}

/// <summary>One tile of the shelf (78x88): a 48 px thumbnail or icon and a two-line caption.</summary>
internal sealed class ShelfTile : Border
{
    public const double TileWidth = 78;
    public const double TileHeight = 88;

    private readonly IShelfActions _actions;
    private readonly Border _selection = new();
    private Point? _pressPoint;
    private bool _pressedOnSelected;
    private bool _dragging;

    public ShelfTile(IShelfActions actions, ShelfItem item, bool selected, bool nested)
    {
        _actions = actions;
        Item = item;
        Width = TileWidth;
        Height = TileHeight;
        Background = Brushes.Transparent;
        AllowDrop = true;
        Cursor = Cursors.Arrow;
        var store = actions.Store;

        var grid = new Grid();
        _selection.CornerRadius = new CornerRadius(10);
        _selection.BorderThickness = new Thickness(1);
        grid.Children.Add(_selection);
        ApplySelection(selected);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 6, 4, 4) };
        var art = new Grid { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
        BuildArt(art, item);
        stack.Children.Add(art);
        var caption = UiKit.Text(CaptionOf(item), 10.5, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true, trimming: TextTrimming.CharacterEllipsis);
        caption.TextAlignment = TextAlignment.Center;
        caption.MaxHeight = 28;
        caption.Margin = new Thickness(0, 4, 0, 0);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(caption);
        grid.Children.Add(stack);

        if (item.Pinned)
        {
            var pin = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0), Child = UiKit.Icon(G.PinFill, 8, ThemeKeys.OnAccent) };
            pin.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);
            grid.Children.Add(pin);
        }

        if (item.IsPile)
        {
            var count = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0, 5, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0), Child = UiKit.Text(item.LeafCount.ToString(), 10, FontWeights.Bold, ThemeKeys.OnAccent) };
            count.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);
            if (item.Pinned)
            {
                count.Margin = new Thickness(0, 22, 4, 0);
            }

            grid.Children.Add(count);
        }

        Child = grid;
        ToolTip = TooltipOf(item, store);
        ContextMenu = BuildMenu();
        if (nested)
        {
            Opacity = 0.92;
        }

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Right && !actions.Store.Selection.Contains(item.Id))
            {
                actions.Store.Select(item.Id);
            }
        };
        PreviewDragOver += OnDragOver;
        PreviewDrop += OnDrop;
    }

    public ShelfItem Item { get; }

    public void ApplySelection(bool selected)
    {
        if (selected)
        {
            _selection.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill18);
            _selection.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
            _selection.BorderBrush = _selection.BorderBrush;
            _selection.Opacity = 1;
        }
        else
        {
            _selection.Background = Brushes.Transparent;
            _selection.BorderBrush = Brushes.Transparent;
        }
    }

    private static string CaptionOf(ShelfItem item)
    {
        switch (item.Kind)
        {
            case ShelfKinds.File:
                return Path.GetFileName(item.Path?.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : item.Path ?? string.Empty;
            case ShelfKinds.Image:
                return Strings.Shelf.ItemImage;
            case ShelfKinds.Link:
                return Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) ? uri.Host : item.Url ?? string.Empty;
            case ShelfKinds.Text:
                var text = (item.Text ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
                return text.Length > 500 ? text[..500] : text;
            default:
                return string.Format(Strings.Shelf.ItemsFormat, item.LeafCount);
        }
    }

    private static string TooltipOf(ShelfItem item, ShelfStore store)
    {
        switch (item.Kind)
        {
            case ShelfKinds.File:
                var missing = !(File.Exists(item.Path) || Directory.Exists(item.Path));
                return missing ? $"{item.Path}\n{Strings.Shelf.FileMissing}" : item.Path ?? string.Empty;
            case ShelfKinds.Link:
                return item.Url ?? string.Empty;
            case ShelfKinds.Text:
                var text = item.Text ?? string.Empty;
                return text.Length > 300 ? text[..300] + "…" : text;
            case ShelfKinds.Image:
                return Strings.Shelf.ItemImage;
            default:
                var leaves = item.Leaves().ToList();
                if (leaves.Count > 0 && leaves.All(l => l.Kind == ShelfKinds.Image))
                {
                    return string.Format(leaves.Count == 1 ? Strings.Shelf.ImageSingular : Strings.Shelf.ImagePlural, leaves.Count);
                }

                if (leaves.Count > 0 && leaves.All(l => l.Kind == ShelfKinds.File))
                {
                    return string.Format(leaves.Count == 1 ? Strings.Shelf.FileSingular : Strings.Shelf.FilePlural, leaves.Count);
                }

                if (leaves.Count > 0 && leaves.All(l => l.Kind == ShelfKinds.Text))
                {
                    return string.Format(leaves.Count == 1 ? Strings.Shelf.NoteSingular : Strings.Shelf.NotePlural, leaves.Count);
                }

                if (leaves.Count > 0 && leaves.All(l => l.Kind == ShelfKinds.Link))
                {
                    return string.Format(leaves.Count == 1 ? Strings.Shelf.LinkSingular : Strings.Shelf.LinkPlural, leaves.Count);
                }

                return string.Format(Strings.Shelf.ItemsFormat, leaves.Count);
        }
    }

    private void BuildArt(Grid art, ShelfItem item)
    {
        var store = _actions.Store;
        switch (item.Kind)
        {
            case ShelfKinds.Image:
                art.Children.Add(Thumbnail(store.ImagePath(item), 96));
                break;
            case ShelfKinds.File:
                var path = item.Path ?? string.Empty;
                if (!(File.Exists(path) || Directory.Exists(path)))
                {
                    art.Children.Add(UiKit.Icon("", 22, ThemeKeys.TextTertiary));
                    art.Opacity = 0.6;
                    break;
                }

                var image = new Image { Stretch = Stretch.Uniform, Width = 48, Height = 48, Source = ShellIcons.ForPath(path) };
                art.Children.Add(image);
                ShellThumbs.Load(path, 96, source =>
                {
                    if (source is not null)
                    {
                        image.Source = source;
                    }
                });
                break;
            case ShelfKinds.Link:
                art.Children.Add(Glyph("", ThemeKeys.Accent));
                break;
            case ShelfKinds.Text:
                var note = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(5), Width = 40, Height = 48 };
                note.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
                var snippet = UiKit.Text((item.Text ?? string.Empty).Length > 60 ? item.Text![..60] : item.Text ?? string.Empty, 7, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
                snippet.MaxHeight = 40;
                note.Child = snippet;
                art.Children.Add(note);
                break;
            default:
                BuildPileArt(art, item);
                break;
        }
    }

    private void BuildPileArt(Grid art, ShelfItem item)
    {
        // Up to three backplates offset like a stack, the first child's art on top.
        for (var i = 2; i >= 1; i--)
        {
            var plate = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(7), Margin = new Thickness(i * 4, 0, 0, i * 4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, BorderThickness = new Thickness(1) };
            plate.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            plate.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
            art.Children.Add(plate);
        }

        var first = item.Leaves().FirstOrDefault();
        var top = new Grid { Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        if (first is not null)
        {
            BuildArt(top, first);
        }

        art.Children.Add(top);
    }

    private Image Thumbnail(string path, int width)
    {
        var image = new Image { Stretch = Stretch.Uniform, Width = 48, Height = 48 };
        Thumbs.Load(path, width, source => image.Source = source);
        return image;
    }

    private static UIElement Glyph(string glyph, string brush)
    {
        var tile = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Child = UiKit.Icon(glyph, 22, brush) };
        tile.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
        return tile;
    }

    private ContextMenu BuildMenu()
    {
        var item = Item;
        var store = _actions.Store;
        var ids = () => store.Selection.Contains(item.Id) ? store.Selection.ToList() : [item.Id];
        var items = new List<(string, Action?, bool, bool)?>
        {
            (Strings.Shelf.OpenAction, () => OpenItems(ids()), true, false),
        };
        if (item.Kind == ShelfKinds.File)
        {
            items.Add((Strings.Shelf.OpenWith, () => OpenWith(item), true, false));
        }

        items.Add((Strings.Shelf.Share, () => _actions.Share(store.FilePathsForActions(ids()), this), true, false));
        if (item.Kind == ShelfKinds.File && item.Path is not null)
        {
            items.Add((Strings.Shelf.ShowInExplorer, () => Ui.RevealInExplorer(item.Path), true, false));
        }

        items.Add(null);
        if (item.IsPile)
        {
            items.Add((store.Expanded.Contains(item.Id) ? Loc.T("Collapse pile") : Loc.T("Expand pile"), () => store.ToggleExpanded(item.Id), true, false));
        }

        items.Add((item.Pinned ? Strings.Shelf.UnpinAction : Strings.Shelf.PinAction, () => store.TogglePin(item.Id), true, false));
        items.Add((Strings.Shelf.Remove, () => store.Remove(ids()), true, true));
        return Ui.Menu([.. items]);
    }

    private static void OpenWith(ShelfItem item)
    {
        try
        {
            if (item.Path is not null)
            {
                Process.Start(new ProcessStartInfo(item.Path) { Verb = "openas", UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            // cancelled or unsupported
        }
    }

    /// <summary>Opens items with their default handler (files, images), links in the browser, notes in the clipboard-free preview (not opened).</summary>
    public void OpenItems(IEnumerable<string> ids)
    {
        foreach (var leaf in _actions.Store.LeavesOf(ids))
        {
            try
            {
                switch (leaf.Kind)
                {
                    case ShelfKinds.File when leaf.Path is not null:
                        Process.Start(new ProcessStartInfo(leaf.Path) { UseShellExecute = true });
                        break;
                    case ShelfKinds.Image:
                        Process.Start(new ProcessStartInfo(_actions.Store.ImagePath(leaf)) { UseShellExecute = true });
                        break;
                    case ShelfKinds.Link when leaf.Url is not null:
                        UiKit.OpenUrl(leaf.Url);
                        break;
                }
            }
            catch (Exception)
            {
                System.Media.SystemSounds.Beep.Play();
            }
        }
    }

    // ---------------------------------------------------------------------------------------------- mouse

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var store = _actions.Store;
        _pressPoint = e.GetPosition(this);
        _dragging = false;
        if (e.ClickCount == 2)
        {
            OpenItems(store.Selection.Contains(Item.Id) ? store.Selection.ToList() : [Item.Id]);
            _pressPoint = null;
            e.Handled = true;
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _pressedOnSelected = store.Selection.Contains(Item.Id);
        if (!ctrl && !shift && !_pressedOnSelected)
        {
            store.Select(Item.Id);
        }

        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressPoint is not { } start || e.LeftButton != MouseButtonState.Pressed || _dragging)
        {
            return;
        }

        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragging = true;
        ReleaseMouseCapture();
        var store = _actions.Store;
        var ids = store.Selection.Contains(Item.Id) ? store.Selection.ToList() : [Item.Id];
        var data = store.BuildDragData(ids, out var allowsMove);
        var allowed = DragDropEffects.Copy | DragDropEffects.Link | (allowsMove ? DragDropEffects.Move : DragDropEffects.None);
        var effect = DragDrop.DoDragDrop(this, data, allowed);
        _pressPoint = null;
        if (effect != DragDropEffects.None)
        {
            _actions.DragOutFinished(ids, effect);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var wasDragging = _dragging;
        _pressPoint = null;
        _dragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (wasDragging)
        {
            return;
        }

        var store = _actions.Store;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl)
        {
            store.ToggleSelect(Item.Id);
        }
        else if (shift)
        {
            store.ExtendSelect(Item.Id, [.. store.Visible().Select(i => i.Id)]);
        }
        else if (_pressedOnSelected)
        {
            store.Select(Item.Id);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("Trayloom.ShelfItems"))
        {
            var ids = ((string)e.Data.GetData("Trayloom.ShelfItems")).Split(',');
            e.Effects = ids.Contains(Item.Id) ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
            _selection.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("Trayloom.ShelfItems"))
        {
            return; // an outside drop lands on the panel
        }

        var ids = ((string)e.Data.GetData("Trayloom.ShelfItems")).Split(',');
        if (!ids.Contains(Item.Id))
        {
            _actions.Store.MakePile(ids, Item.Id);
        }

        e.Handled = true;
    }
}

/// <summary>The shelf card (<c>ShelfView</c>): header, tile grid or empty state, footer with share and clear.</summary>
internal sealed class ShelfPanel : Border
{
    public const double PanelWidth = 304;
    public const double TileAreaHeight = 188;

    private readonly IShelfActions _actions;
    private readonly bool _docked;
    private readonly Grid _headerHost = new();
    private readonly ContentControl _tiles = new();
    private readonly ContentControl _footer = new();
    private readonly Dictionary<string, ShelfTile> _tileViews = [];
    private ScrollViewer? _scroll;
    private int _lastSerial;

    public ShelfPanel(IShelfActions actions, bool docked)
    {
        _actions = actions;
        _docked = docked;
        Width = PanelWidth;
        Padding = new Thickness(14);
        CornerRadius = new CornerRadius(18);
        BorderThickness = new Thickness(1);
        AllowDrop = true;
        Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.3, Direction = 270 };
        this.Themed(BackgroundProperty, ThemeKeys.PanelBase);
        var stack = new StackPanel();
        stack.Children.Add(_headerHost);
        _tiles.Margin = new Thickness(0, 11, 0, 0);
        stack.Children.Add(_tiles);
        _footer.Margin = new Thickness(0, 11, 0, 0);
        stack.Children.Add(_footer);
        Child = stack;

        var store = actions.Store;
        store.Changed += Rebuild;
        store.SelectionChanged += RefreshSelection;
        Unloaded += (_, _) => Detach();
        DragEnter += (_, e) => OnDrag(e, entering: true);
        DragOver += (_, e) => OnDrag(e, entering: false);
        DragLeave += (_, _) => SetTargeted(false);
        Drop += OnDrop;
        MouseEnter += (_, _) => actions.PointerInsidePanel(true);
        MouseLeave += (_, _) => actions.PointerInsidePanel(false);
        Rebuild();
    }

    public void RefreshHeader() => BuildHeader();

    /// <summary>Stops following the store (the host replaced this card).</summary>
    public void Detach()
    {
        _actions.Store.Changed -= Rebuild;
        _actions.Store.SelectionChanged -= RefreshSelection;
    }

    private void SetTargeted(bool targeted)
    {
        _actions.IsDropTargeted = targeted;
        if (targeted)
        {
            BorderBrush = new SolidColorBrush(ThemeManager.AccentColor);
            BorderThickness = new Thickness(2);
        }
        else
        {
            this.Themed(BorderBrushProperty, ThemeKeys.CardBorder);
            BorderThickness = new Thickness(1);
        }
    }

    private void OnDrag(DragEventArgs e, bool entering)
    {
        if (e.Data.GetDataPresent("Trayloom.ShelfItems"))
        {
            e.Effects = DragDropEffects.None; // reordering into piles is handled by the tiles
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        if (entering)
        {
            SetTargeted(true);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        SetTargeted(false);
        if (e.Data.GetDataPresent("Trayloom.ShelfItems"))
        {
            return;
        }

        _actions.Store.AddFromData(e.Data);
        e.Handled = true;
    }

    private void BuildHeader()
    {
        var store = _actions.Store;
        _headerHost.Children.Clear();
        _headerHost.ColumnDefinitions.Clear();
        _headerHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headerHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerHost.Height = 30;

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        left.Children.Add(UiKit.Icon(G.Tray, 12, ThemeKeys.TextSecondary, FontWeights.SemiBold));
        var title = store.Selection.Count == 0 ? Strings.Shelf.Title : string.Format(Strings.Shelf.SelectedFormat, store.Selection.Count);
        var titleText = UiKit.Text(title, 12, FontWeights.SemiBold, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        titleText.Margin = new Thickness(7, 0, 0, 0);
        left.Children.Add(titleText);
        if (store.Items.Count > 0)
        {
            var count = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(7, 0, 0, 0), Child = UiKit.Text(store.LeafCount.ToString(), 11, FontWeights.Bold, ThemeKeys.TextPrimary) };
            count.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            left.Children.Add(count);
        }

        left.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                _actions.MoveWindow();
            }
        };
        _headerHost.Children.Add(left);

        if (!_docked)
        {
            var pin = CircleButton(_actions.IsPinned ? G.PinFill : G.Pin, _actions.IsPinned ? Strings.Shelf.Unpin : Strings.Shelf.Pin, _actions.TogglePin, _actions.IsPinned);
            pin.Margin = new Thickness(0, 0, 7, 0);
            Grid.SetColumn(pin, 1);
            _headerHost.Children.Add(pin);
        }

        var close = CircleButton(_docked ? G.ChevronUp : G.Close, _docked ? Strings.Shelf.Collapse : Strings.Shelf.Close, _actions.Dismiss, false);
        Grid.SetColumn(close, 2);
        _headerHost.Children.Add(close);
    }

    private static Border CircleButton(string glyph, string tooltip, Action click, bool accent)
    {
        var button = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Background = Brushes.Transparent,
            Child = UiKit.Icon(glyph, 12, accent ? ThemeKeys.Accent : ThemeKeys.TextSecondary, FontWeights.SemiBold),
        };
        void Idle()
        {
            if (accent)
            {
                button.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill18);
                button.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
            }
            else
            {
                button.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
                button.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
            }
        }

        Idle();
        button.MouseEnter += (_, _) => button.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        button.MouseLeave += (_, _) => Idle();
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        button.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return button;
    }

    private void Rebuild()
    {
        BuildHeader();
        var store = _actions.Store;
        var scrollOffset = _scroll?.VerticalOffset ?? 0;
        _tileViews.Clear();
        if (store.Items.Count == 0)
        {
            _tiles.Content = EmptyState();
            _footer.Content = null;
            _scroll = null;
            return;
        }

        var wrap = new WrapPanel { Margin = new Thickness(4), Orientation = Orientation.Horizontal };
        foreach (var item in store.Visible())
        {
            var nested = !store.Items.Contains(item);
            var tile = new ShelfTile(_actions, item, store.Selection.Contains(item.Id), nested) { Margin = new Thickness(0, 0, 10, 8) };
            _tileViews[item.Id] = tile;
            wrap.Children.Add(tile);
        }

        _scroll = new ScrollViewer
        {
            Height = TileAreaHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = wrap,
            Background = Brushes.Transparent,
            AllowDrop = true,
        };
        _tiles.Content = _scroll;
        // The most recent add scrolls into view.
        if (store.AddSerial != _lastSerial && store.RevealId is { } reveal && _tileViews.TryGetValue(reveal, out var added))
        {
            _lastSerial = store.AddSerial;
            added.Loaded += (_, _) => added.BringIntoView();
            _scroll.Loaded += (_, _) => added.BringIntoView();
        }
        else
        {
            _scroll.Loaded += (_, _) => _scroll.ScrollToVerticalOffset(scrollOffset);
        }

        _footer.Content = BuildFooter();
    }

    private void RefreshSelection()
    {
        var store = _actions.Store;
        foreach (var (id, tile) in _tileViews)
        {
            tile.ApplySelection(store.Selection.Contains(id));
        }

        BuildHeader();
        _footer.Content = BuildFooter();
    }

    private UIElement EmptyState()
    {
        var box = new Grid { Height = TileAreaHeight, Background = Brushes.Transparent, AllowDrop = true };
        var dash = new Rectangle
        {
            RadiusX = 12,
            RadiusY = 12,
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3.3],
            Fill = Brushes.Transparent,
        };
        dash.SetResourceReference(Shape.StrokeProperty, ThemeKeys.TextTertiary);
        dash.Opacity = 0.7;
        box.Children.Add(dash);
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var glyph = UiKit.Icon("", 21, ThemeKeys.TextSecondary);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(glyph);
        var label = UiKit.Text(Strings.Shelf.Empty, 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        label.Margin = new Thickness(0, 8, 0, 0);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        content.Children.Add(label);
        box.Children.Add(content);
        box.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                _actions.MoveWindow();
            }
        };
        return box;
    }

    private UIElement BuildFooter()
    {
        var store = _actions.Store;
        var grid = new Grid { MinHeight = 30 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hint = UiKit.Text(Strings.Shelf.Hint, 10, FontWeights.Normal, ThemeKeys.TextTertiary, trimming: TextTrimming.CharacterEllipsis);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.ToolTip = Strings.Shelf.Hint;
        hint.Margin = new Thickness(0, 0, 8, 0);
        grid.Children.Add(hint);

        var hasFiles = store.FilePathsForActions().Count > 0;
        var share = FooterButton(G.Share, Strings.Shelf.Share, ThemeKeys.Accent, hasFiles);
        share.MouseLeftButtonUp += (_, _) =>
        {
            if (hasFiles)
            {
                var ids = store.Selection.Count > 0 ? store.Selection.ToList() : null;
                _actions.Share(store.FilePathsForActions(ids), share);
            }
        };
        Grid.SetColumn(share, 1);
        grid.Children.Add(share);

        var selecting = store.Selection.Count > 0;
        var trash = FooterButton(G.Delete, selecting ? Strings.Shelf.RemoveSelected : Strings.Shelf.ClearAll, ThemeKeys.Red, true);
        trash.Margin = new Thickness(8, 0, 0, 0);
        trash.MouseLeftButtonUp += (_, _) =>
        {
            if (selecting)
            {
                store.Remove(store.Selection.ToList());
            }
            else
            {
                store.ClearAll();
            }
        };
        Grid.SetColumn(trash, 2);
        grid.Children.Add(trash);
        return grid;
    }

    private static Border FooterButton(string glyph, string tooltip, string tintKey, bool enabled)
    {
        var button = new Border
        {
            Width = 42,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(0.8),
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            ToolTip = tooltip,
            Opacity = enabled ? 1 : 0.4,
            Background = Brushes.Transparent,
            Child = UiKit.Icon(glyph, 12, ThemeKeys.TextSecondary, FontWeights.SemiBold),
        };
        var tint = UiKit.BrushOf(tintKey) is SolidColorBrush solid ? solid.Color : Colors.Gray;
        Color Alpha(double a) => Color.FromArgb((byte)(255 * a), tint.R, tint.G, tint.B);
        void Idle()
        {
            button.Background = new SolidColorBrush(Alpha(0.07));
            button.BorderBrush = new SolidColorBrush(Alpha(0.12));
            ((TextBlock)button.Child).SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        }

        Idle();
        if (enabled)
        {
            button.MouseEnter += (_, _) =>
            {
                button.Background = new SolidColorBrush(Alpha(0.2));
                button.BorderBrush = new SolidColorBrush(Alpha(0.34));
                ((TextBlock)button.Child).SetResourceReference(TextBlock.ForegroundProperty, tintKey);
            };
            button.MouseLeave += (_, _) => Idle();
        }

        return button;
    }
}

/// <summary>The collapsed docked shelf next to the tray icons: a capsule with the mark, the item count and a chevron.</summary>
internal sealed class ShelfPill : Border
{
    public ShelfPill(IShelfActions actions, Action expand, bool topCenter)
    {
        var store = actions.Store;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var leading = BrandMark.Create(topCenter ? 18 : 15);
        leading.VerticalAlignment = VerticalAlignment.Center;
        leading.Opacity = 0.85;
        leading.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(leading);
        if (topCenter)
        {
            var name = UiKit.Text(Strings.Shelf.Title, 13, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.MaxWidth = 150;
            row.Children.Add(name);
        }

        var count = store.LeafCount;
        if (count > 0)
        {
            var badge = new Border { CornerRadius = new CornerRadius(topCenter ? 20 : 0), Padding = topCenter ? new Thickness(6, 2, 6, 2) : new Thickness(0), Margin = new Thickness(topCenter ? 8 : 0, 0, 0, 0), Child = UiKit.Text(count.ToString(), topCenter ? 12 : 12.5, FontWeights.SemiBold, ThemeKeys.TextPrimary) };
            if (topCenter)
            {
                badge.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            }

            badge.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(badge);
        }

        var chevron = UiKit.Icon(G.ChevronDown, 9, ThemeKeys.TextSecondary, FontWeights.Bold);
        chevron.Margin = new Thickness(7, 0, 0, 0);
        chevron.Opacity = 0.6;
        row.Children.Add(chevron);

        Padding = new Thickness(count > 0 ? 12 : 11, 8, count > 0 ? 12 : 11, 8);
        CornerRadius = new CornerRadius(13);
        BorderThickness = new Thickness(1);
        Child = row;
        Cursor = Cursors.Hand;
        AllowDrop = true;
        this.Themed(BackgroundProperty, ThemeKeys.PanelBase);
        this.Themed(BorderBrushProperty, ThemeKeys.CardBorder);
        Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 3, Opacity = 0.2, Direction = 270 };
        ToolTip = Strings.Shelf.OpenNow;
        MouseLeftButtonUp += (_, _) => expand();
        DragEnter += (_, e) =>
        {
            if (!e.Data.GetDataPresent("Trayloom.ShelfItems"))
            {
                e.Effects = DragDropEffects.Copy;
                actions.IsDropTargeted = true;
                BorderBrush = new SolidColorBrush(ThemeManager.AccentColor);
                BorderThickness = new Thickness(2);
            }

            e.Handled = true;
        };
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent("Trayloom.ShelfItems") ? DragDropEffects.None : DragDropEffects.Copy;
            e.Handled = true;
        };
        DragLeave += (_, _) =>
        {
            actions.IsDropTargeted = false;
            this.Themed(BorderBrushProperty, ThemeKeys.CardBorder);
            BorderThickness = new Thickness(1);
        };
        Drop += (_, e) =>
        {
            actions.IsDropTargeted = false;
            if (!e.Data.GetDataPresent("Trayloom.ShelfItems"))
            {
                store.AddFromData(e.Data);
            }

            e.Handled = true;
        };
    }
}
