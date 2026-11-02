// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Island;

internal sealed class ClipboardPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _search = PageUi.FlatTextBox(12);
    private bool _searchOpen;
    private bool _pinnedOnly;
    private int _highlight;
    private int _copiedIndex = -1;
    private double _w = 400;
    private double _h = 264;
    private StackPanel? _list;
    private ScrollViewer? _scroll;

    public ClipboardPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
        _search.TextChanged += (_, _) => RebuildList();
        _view.PreviewTextInput += (_, e) =>
        {
            if (!_searchOpen && e.Text.Length > 0 && !char.IsControl(e.Text[0]))
            {
                _searchOpen = true;
                Build();
                _search.Text = e.Text;
                _search.CaretIndex = _search.Text.Length;
                _search.Focus();
                e.Handled = true;
            }
        };
    }

    public override string ModuleId => "clipboard";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Leading => _leading;

    public override FrameworkElement? Trailing => _trailing;

    public override bool ScrollsContent => true;

    public override bool WantsKeyboard => true;

    private bool Compact => Core.App.Get<string>(K.ClipboardCardSize) == "compact";

    public override double Height(double width, double budget)
    {
        _w = width;
        _h = budget;
        Build();
        return budget;
    }

    public override void Shown()
    {
        Core.Clipboard.Changed += OnChanged;
        if (HasWindow)
        {
            Window.EnsureKeyboard();
        }
    }

    public override void Hidden() => Core.Clipboard.Changed -= OnChanged;

    public override bool StepBack()
    {
        if (_searchOpen)
        {
            _searchOpen = false;
            _search.Text = "";
            Build();
            return true;
        }

        return false;
    }

    private void OnChanged() => _view.Dispatcher.BeginInvoke(DispatcherPriority.Background, RebuildList);

    private IEnumerable<ClipItem> Items()
    {
        var q = WatchService.Normalise(_search.Text);
        return Core.Clipboard.Items.Where(i => (!_pinnedOnly || i.Pinned) && (q.Length == 0 || WatchService.Normalise(i.Text).Contains(q)));
    }

    private void Build()
    {
        _view.Children.Clear();
        _leading.Children.Clear();
        _trailing.Children.Clear();
        if (!_searchOpen)
        {
            _leading.Children.Add(D.IconButton(G.Search, Loc.T("Search copied text"), () => { _searchOpen = true; Build(); _search.Focus(); }));
        }

        _trailing.Children.Add(D.IconButton("", Loc.T("Pinned"), () => { _pinnedOnly = !_pinnedOnly; RebuildList(); Build(); }, _pinnedOnly));
        var clear = D.IconButton(G.Trash, Loc.T("Clear unpinned"), ClearUnpinned);
        clear.Margin = new Thickness(6, 0, 0, 0);
        clear.IsEnabled = Core.Clipboard.Items.Any(i => !i.Pinned);
        _trailing.Children.Add(clear);
        var open = D.IconButton(G.OpenApp, Loc.T("Clipboard"), () => Core.Perform(() => Core.Ctx.Shell.OpenPanel(WinKnife.Core.PanelTabs.Utilities)));
        open.Margin = new Thickness(6, 0, 0, 0);
        _trailing.Children.Add(open);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (_searchOpen)
        {
            var box = new Border { Height = 36, CornerRadius = new CornerRadius(14), Background = D.White(0.065), Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 10) };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(D.I(G.Search, 12, D.Secondary));
            _search.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(_search, 1);
            if (_search.Parent is Panel p)
            {
                p.Children.Remove(_search);
            }

            row.Children.Add(_search);
            box.Child = row;
            root.Children.Add(box);
        }

        if (!Core.Ctx.Runtime.IsAvailable("clipboard-history") || (!Core.Clipboard.HistoryOn && Core.Clipboard.Items.Count == 0))
        {
            var enable = new Button();
            _ = enable;
            var empty = D.EmptyView(G.Clipboard, Loc.T("Enable history to start saving copied text."), D.PillButton(Loc.T("Save clipboard history"), () => { Core.App.Set(K.ClipboardHistoryEnabled, true); Build(); }, true));
            Grid.SetRow(empty, 1);
            root.Children.Add(empty);
            _view.Children.Add(root);
            return;
        }

        _list = new StackPanel();
        _scroll = PageUi.VScroll(_list);
        Grid.SetRow(_scroll, 1);
        root.Children.Add(_scroll);
        _view.Children.Add(root);
        _highlight = Core.Clipboard.Items.ToList().FindIndex(i => !i.Pinned);
        _highlight = Math.Max(0, _highlight);
        RebuildList();
    }

    private void RebuildList()
    {
        if (_list is null)
        {
            return;
        }

        _list.Children.Clear();
        var items = Items().ToList();
        if (items.Count == 0)
        {
            var empty = D.EmptyView(_pinnedOnly ? G.Pin : G.Clipboard, _search.Text.Length > 0 ? Loc.T("No results") : Loc.T("No saved text"));
            empty.Height = Math.Max(100, _h - 60);
            _list.Children.Add(empty);
            return;
        }

        _highlight = Math.Clamp(_highlight, 0, items.Count - 1);
        for (var i = 0; i < items.Count; i++)
        {
            _list.Children.Add(Card(items[i], i));
        }
    }

    private FrameworkElement Card(ClipItem item, int index)
    {
        var compact = Compact;
        var height = compact ? 52.0 : 104.0;
        var card = new HoverButton(() => Activate(item)) { Height = height, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 8), Lifts = false, BorderThickness = new Thickness(1), BorderBrush = index == _highlight ? D.White(0.34) : Brushes.Transparent, Fill = item.Pinned ? D.White(0.12) : D.White(0.065) };
        var grid = new Grid { Margin = new Thickness(10, compact ? 6 : 10, 10, compact ? 6 : 4) };
        if (!compact)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        }

        FrameworkElement preview;
        if (item.IsImage && item.ThumbPath is not null && File.Exists(item.ThumbPath))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelHeight = 120;
            bmp.UriSource = new Uri(item.ThumbPath);
            bmp.EndInit();
            preview = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Child = new Image { Source = bmp, Stretch = Stretch.Uniform } };
        }
        else
        {
            var t = D.T(item.Text.Length == 0 ? Loc.T("Image") : item.Text, 12, null, D.Text, true, TextTrimming.CharacterEllipsis);
            t.MaxHeight = compact ? 30 : 48;
            preview = t;
        }

        grid.Children.Add(preview);
        if (!compact)
        {
            var footer = new Grid { Height = 28 };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(D.I(item.IsImage ? G.Photo : "", 10, D.Secondary));
            var date = D.T(item.Created.ToString("MMM d, " + Loc.ShortTime), 9.5, null, D.Tertiary);
            date.Margin = new Thickness(6, 0, 0, 0);
            info.Children.Add(date);
            if (index < 9)
            {
                var hint = D.T("Ctrl " + (index + 1), 9.5, FontWeights.Medium, D.Tertiary);
                hint.Margin = new Thickness(8, 0, 0, 0);
                info.Children.Add(hint);
            }

            footer.Children.Add(info);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var copied = _copiedIndex == index;
            buttons.Children.Add(D.IconButton(copied ? G.Check : G.Copy, copied ? Loc.T("Copied") : Loc.T("Copy"), () => { Core.Clipboard.Copy(item); _copiedIndex = index; RebuildList(); PageUi.After(_view.Dispatcher, 1.6, () => { _copiedIndex = -1; RebuildList(); }); }, size: 24, glyphSize: 11));
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);
            Grid.SetRow(footer, 1);
            grid.Children.Add(footer);
        }

        card.Child = grid;
        card.MouseEnter += (_, _) => { };
        return card;
    }

    private void Activate(ClipItem item) => Core.Clipboard.Paste(item);

    private void ClearUnpinned()
    {
        var n = Core.Clipboard.Items.Count(i => !i.Pinned);
        using var hold = HasWindow ? Window.HoldOpen() : null;
        var r = MessageBox.Show($"Pinned items stay, and so does anything copied after this. This can’t be undone.", Loc.F("Clear unpinned ({0})?", n), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r == MessageBoxResult.OK)
        {
            Core.InvokeFeatureAction("clipboard-history", "clear");
        }
    }

    public override bool HandleKey(KeyEventArgs e)
    {
        var items = Items().ToList();
        if (items.Count == 0)
        {
            return false;
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var key = e.Key;
        if (ctrl && key >= Key.D1 && key <= Key.D9 || ctrl && key >= Key.NumPad1 && key <= Key.NumPad9)
        {
            var n = key >= Key.NumPad1 ? key - Key.NumPad1 : key - Key.D1;
            if (n < items.Count)
            {
                Activate(items[n]);
            }
            else
            {
                System.Media.SystemSounds.Beep.Play();
            }

            return true;
        }

        if (key == Key.Down || (ctrl && key == Key.N))
        {
            _highlight = Math.Min(items.Count - 1, _highlight + 1);
            RebuildList();
            return true;
        }

        if (key == Key.Up || (ctrl && key == Key.P))
        {
            _highlight = Math.Max(0, _highlight - 1);
            RebuildList();
            return true;
        }

        if (key == Key.Return && !_searchOpen || key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            Activate(items[Math.Clamp(_highlight, 0, items.Count - 1)]);
            return true;
        }

        return false;
    }
}
