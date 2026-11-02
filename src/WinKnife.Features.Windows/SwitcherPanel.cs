// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>
/// The switcher overlay (spec 05 A1.2): "Window previews" cards, "Large icons" row with the selected app's windows above it, or
/// the "Simple list" strip. A topmost no-activate layered window so the app in front keeps the keyboard focus.
/// </summary>
internal sealed class SwitcherPanel : OverlayWindow
{
    private const double Shadow = 22;

    private readonly SwitcherOptions _o;
    private readonly string _hintShortcut;
    private readonly Dictionary<nint, List<Image>> _images = [];
    private readonly List<(Border Card, SwitchEntry Entry, int Index)> _cards = [];
    private List<SwitchEntry> _entries = [];
    private int _selected;
    private string _query = string.Empty;
    private bool _pinned;
    private double _scale = 1;
    private Rect _work;
    private ScrollViewer? _scroll;
    private bool _moved;
    private Point _openPointer;

    public SwitcherPanel(SwitcherOptions options, string hintShortcut)
    {
        _o = options;
        _hintShortcut = hintShortcut;
        ClickThrough = false;
        Focusable = false;
        Loaded += (_, _) => _openPointer = Mouse.GetPosition(this);
    }

    /// <summary>A card (entry index, window index inside the entry) was clicked.</summary>
    public event Action<int, int>? Clicked;

    /// <summary>The close button or a middle click on a card (entry index, window index).</summary>
    public event Action<int, int>? CloseRequested;

    /// <summary>The pointer moved over another entry.</summary>
    public event Action<int>? Hovered;

    /// <summary>Physical-pixel bounds of the panel (for click-outside tests).</summary>
    public RECT PhysicalBounds
    {
        get
        {
            NativeMethods.GetWindowRect(Handle, out var r);
            return r;
        }
    }

    /// <summary>Shows the panel centred on a work area (physical pixels).</summary>
    public void ShowOn(RECT work, List<SwitchEntry> entries, int selected, string query, bool pinned)
    {
        _entries = entries;
        _selected = Math.Clamp(selected, 0, Math.Max(0, entries.Count - 1));
        _query = query;
        _pinned = pinned;
        _work = new Rect(work.Left, work.Top, work.Width, work.Height);
        _scale = WinUtil.PrepareOnMonitorAt(this, work.Left + work.Width / 2, work.Top + work.Height / 2);
        Refit(initial: true);
        ShowNoActivate();
    }

    /// <summary>Replaces the entries (search, closed window...).</summary>
    public void Update(List<SwitchEntry> entries, int selected, string query, bool pinned)
    {
        _entries = entries;
        _selected = Math.Clamp(selected, 0, Math.Max(0, entries.Count - 1));
        _query = query;
        _pinned = pinned;
        Refit(initial: false);
    }

    /// <summary>Changes the highlighted entry.</summary>
    public void Select(int index)
    {
        _selected = Math.Clamp(index, 0, Math.Max(0, _entries.Count - 1));
        if (_o.Layout == SwitchLayout.Windows && _cards.Count > 0)
        {
            ApplySelection();
            return;
        }

        Refit(initial: false);
    }

    /// <summary>A background capture finished: show it.</summary>
    public void ThumbReady(nint hwnd)
    {
        if (_images.TryGetValue(hwnd, out var list) && WindowCapture.Cached(hwnd) is { } image)
        {
            foreach (var img in list)
            {
                img.Source = image;
                if (img.Parent is Border { Child: Image } holder)
                {
                    holder.Background = Brushes.Transparent;
                }

                if (img.Tag is BadgeLink link)
                {
                    link.Fallback.Visibility = Visibility.Collapsed;
                    link.Badge.Visibility = Visibility.Visible;
                }
                else if (img.Tag is UIElement fallback)
                {
                    fallback.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ layout

    private void Refit(bool initial)
    {
        _images.Clear();
        _cards.Clear();
        var usableW = _work.Width / _scale * 0.92;
        var usableH = _work.Height / _scale * 0.85;
        FrameworkElement content = _o.Layout switch
        {
            SwitchLayout.Icons => BuildIconRow(usableW, usableH),
            SwitchLayout.Simple => BuildSimple(usableW),
            _ => BuildCards(usableW, usableH),
        };
        var host = new Border { Margin = new Thickness(Shadow), Child = content };
        Content = host;
        host.Measure(new Size(usableW + Shadow * 2, usableH + Shadow * 2));
        var size = host.DesiredSize;
        Width = Math.Ceiling(size.Width);
        Height = Math.Ceiling(size.Height);
        var pw = (int)Math.Ceiling(Width * _scale);
        var ph = (int)Math.Ceiling(Height * _scale);
        var x = (int)(_work.Left + (_work.Width - pw) / 2);
        var y = (int)(_work.Top + (_work.Height - ph) / 2);
        NativeMethods.SetWindowPos(Handle != 0 ? Handle : new WindowInteropHelper(this).EnsureHandle(), HWND_TOPMOST_PTR, x, y, pw, ph, NativeConstants.SWP_NOACTIVATE);
        if (!initial)
        {
            UpdateLayout();
        }

        foreach (var entry in _entries)
        {
            foreach (var w in entry.Windows)
            {
                if (WindowCapture.Cached(w.Hwnd) is not null)
                {
                    ThumbReady(w.Hwnd);
                }
            }
        }

        _scroll?.ScrollToVerticalOffset(_scroll.VerticalOffset);
        EnsureSelectedVisible();
    }

    private static readonly nint HWND_TOPMOST_PTR = NativeConstants.HWND_TOPMOST;

    private Brush Surface(double opacity = 0.97)
    {
        var brush = UiKit.BrushOf(ThemeKeys.PanelBase).Clone();
        brush.Opacity = opacity;
        return brush;
    }

    private Border Frame(UIElement child, double radius, double padding, double surfaceOpacity = 0.97)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(padding),
            BorderThickness = new Thickness(1),
            Background = Surface(surfaceOpacity),
            Child = child,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = ThemeManager.IsLight ? 0.22 : 0.45, Color = Colors.Black, Direction = 270 },
        };
        border.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        return border;
    }

    // ---- window previews ------------------------------------------------------------------------------------

    private FrameworkElement BuildCards(double usableW, double usableH)
    {
        var s = _o.Scale;
        var pad = 20 * s;
        var spacing = 12 * s;
        var cardW = 288 * s;
        var cardH = _o.Minimal ? 288 * s * 0.62 + 20 : 214 * s;
        var root = new Grid();
        if (_entries.Count == 0)
        {
            root.Children.Add(Empty());
            return Frame(root, 24, pad);
        }

        var fit = Math.Max(1, (int)Math.Floor((usableW - 2 * pad + spacing) / (cardW + spacing)));
        if (_o.MaxColumns >= 2)
        {
            fit = Math.Min(fit, _o.MaxColumns);
        }

        var rows = (int)Math.Ceiling(_entries.Count / (double)fit);
        var columns = (int)Math.Ceiling(_entries.Count / (double)rows);
        var grid = new UniformGridLike(columns, cardW, cardH, spacing);
        for (var i = 0; i < _entries.Count; i++)
        {
            var card = Card(_entries[i], i, _entries[i].Current, cardW, cardH, s);
            grid.Children.Add(card);
        }

        var topInset = _query.Length > 0 || _pinned ? 28 + 8 : 0;
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid,
            MaxHeight = Math.Max(cardH, usableH - 2 * pad - topInset),
            Focusable = false,
        };
        var stack = new StackPanel();
        if (topInset > 0)
        {
            stack.Children.Add(SearchChip());
        }

        stack.Children.Add(_scroll);
        return Frame(stack, 24, pad);
    }

    private Border Card(SwitchEntry entry, int index, SwitchWindow? window, double cardW, double cardH, double s, bool compact = false, int windowIndex = -1, bool selected = false)
    {
        var padding = 10 * Math.Min(s, 1);
        var well = cardW - padding * 2;
        var titleBlock = _o.Minimal ? 0 : 36;
        var spacing = _o.Minimal ? 0 : 7 * Math.Min(s, 1);
        var picture = cardH - padding * 2 - titleBlock - spacing;
        var stack = new StackPanel();

        var wellBorder = new Border { Width = well, Height = Math.Max(20, picture), CornerRadius = new CornerRadius(10), ClipToBounds = true };
        wellBorder.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
        var layers = new Grid();
        var inset = Math.Round(5 * Math.Min(s, 1));
        var imageHost = new Border { Margin = new Thickness(inset), CornerRadius = new CornerRadius(8), ClipToBounds = true };
        var fallback = new Image { Source = entry.Icon, Width = 80 * s, Height = 80 * s, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(fallback, BitmapScalingMode.HighQuality);
        var picture2 = new Image { Stretch = Stretch.Uniform, Tag = fallback, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(picture2, BitmapScalingMode.HighQuality);
        imageHost.Child = picture2;
        layers.Children.Add(fallback);
        layers.Children.Add(imageHost);
        if (window is not null)
        {
            if (!_images.TryGetValue(window.Hwnd, out var list))
            {
                _images[window.Hwnd] = list = [];
            }

            list.Add(picture2);
        }

        if (!_o.Minimal)
        {
            var badge = new Image { Source = entry.Icon, Width = 32 * s, Height = 32 * s, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6) };
            badge.Visibility = Visibility.Collapsed;
            picture2.Tag = new BadgeLink(fallback, badge);
            layers.Children.Add(badge);
            layers.Children.Add(StatusBadges(window));
        }

        wellBorder.Child = layers;
        stack.Children.Add(wellBorder);

        if (!_o.Minimal)
        {
            var title = UiKit.Text(entry.Title, 13, selected ? FontWeights.SemiBold : FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            var sub = UiKit.Text(entry.Subtitle, 10.5, FontWeights.Medium, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
            var texts = new StackPanel { Margin = new Thickness(2, spacing, 2, 0), Height = titleBlock };
            texts.Children.Add(title);
            texts.Children.Add(sub);
            stack.Children.Add(texts);
        }

        var card = new Border { Width = cardW, Height = cardH, CornerRadius = new CornerRadius(14), Padding = new Thickness(padding), BorderThickness = new Thickness(2), Child = stack, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
        var close = CloseButton();
        var overlay = new Grid();
        overlay.Children.Add(card);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 14, 14, 0);
        close.Visibility = Visibility.Collapsed;
        if (!_o.Minimal && window is not null)
        {
            overlay.Children.Add(close);
        }

        var wrapper = new Border { Width = cardW, Height = cardH, Child = overlay, Background = Brushes.Transparent, Tag = index };
        card.Tag = wrapper;
        var effectiveIndex = index;
        var wIndex = windowIndex < 0 ? entry.WindowIndex : windowIndex;
        wrapper.MouseEnter += (_, _) => close.Visibility = Visibility.Visible;
        wrapper.MouseLeave += (_, _) => close.Visibility = Visibility.Collapsed;
        wrapper.MouseMove += (_, _) =>
        {
            var p = Mouse.GetPosition(this);
            if (!_moved && (Math.Abs(p.X - _openPointer.X) > 1 || Math.Abs(p.Y - _openPointer.Y) > 1))
            {
                _moved = true;
            }

            if (_moved && !compact)
            {
                Hovered?.Invoke(effectiveIndex);
            }
        };
        wrapper.MouseLeftButtonUp += (_, e) =>
        {
            if (!e.Handled)
            {
                Clicked?.Invoke(effectiveIndex, wIndex);
            }
        };
        wrapper.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                CloseRequested?.Invoke(effectiveIndex, wIndex);
                e.Handled = true;
            }
        };
        close.MouseLeftButtonUp += (_, e) =>
        {
            CloseRequested?.Invoke(effectiveIndex, wIndex);
            e.Handled = true;
        };
        if (!compact)
        {
            _cards.Add((card, entry, index));
            StyleCard(card, index == _selected);
        }
        else
        {
            StyleCard(card, selected);
        }

        return wrapper;
    }

    /// <summary>Links the picture to the fallback icon and the corner badge so a finished capture swaps them.</summary>
    private sealed record BadgeLink(UIElement Fallback, UIElement Badge);

    private void StyleCard(Border card, bool selected)
    {
        if (selected)
        {
            card.Background = new SolidColorBrush(Color.FromArgb(ThemeManager.IsLight ? (byte)26 : (byte)36, ThemeManager.IsLight ? (byte)0 : (byte)255, ThemeManager.IsLight ? (byte)0 : (byte)255, ThemeManager.IsLight ? (byte)0 : (byte)255));
            card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }
        else
        {
            card.Background = Brushes.Transparent;
            card.BorderBrush = Brushes.Transparent;
        }
    }

    private void ApplySelection()
    {
        foreach (var (card, _, index) in _cards)
        {
            StyleCard(card, index == _selected);
        }

        EnsureSelectedVisible();
    }

    private void EnsureSelectedVisible()
    {
        if (_scroll is null)
        {
            return;
        }

        var match = _cards.FirstOrDefault(c => c.Index == _selected);
        if (match.Card?.Tag is FrameworkElement wrapper)
        {
            wrapper.BringIntoView();
        }
    }

    private FrameworkElement StatusBadges(SwitchWindow? window)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
        if (window is null)
        {
            return row;
        }

        void Badge(string glyph, string tip)
        {
            var b = new Border { Width = 20, Height = 18, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(117, 0, 0, 0)), Margin = new Thickness(0, 0, 4, 0), ToolTip = tip };
            b.Child = new TextBlock { Text = glyph, FontFamily = UiKit.IconFont, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(b);
        }

        if (window.Minimized)
        {
            Badge("", "Minimized");
        }

        if (window.Fullscreen)
        {
            Badge("", "Full screen");
        }

        if (window.OtherDesktop)
        {
            Badge("", Strings.OtherDesktop);
        }

        return row;
    }

    private static FrameworkElement CloseButton()
    {
        var grid = new Grid { Width = 21, Height = 21, Cursor = Cursors.Hand };
        grid.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) });
        var x = new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(x);
        grid.MouseEnter += (_, _) => ((Ellipse)grid.Children[0]).Fill = new SolidColorBrush(Color.FromRgb(255, 97, 84));
        grid.MouseLeave += (_, _) => ((Ellipse)grid.Children[0]).Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        return grid;
    }

    private FrameworkElement SearchChip()
    {
        var chip = new Border { Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        chip.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var glass = UiKit.Icon("", 11, ThemeKeys.TextSecondary);
        glass.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(glass);
        var text = UiKit.Text(_query.Length == 0 ? "Search" : _query, 11, FontWeights.SemiBold, _query.Length == 0 ? ThemeKeys.TextTertiary : ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        text.MaxWidth = 180;
        row.Children.Add(text);
        if (_pinned)
        {
            var cap = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(8, 0, 0, 0) };
            cap.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
            cap.Child = UiKit.Text("S", 10, FontWeights.Medium, ThemeKeys.TextSecondary);
            ((TextBlock)cap.Child).FontFamily = new FontFamily("Cascadia Mono, Consolas");
            row.Children.Add(cap);
        }

        chip.Child = row;
        return chip;
    }

    private FrameworkElement Empty()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(40, 20, 40, 20) };
        var icon = UiKit.Icon("", 24, ThemeKeys.TextSecondary);
        icon.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(icon);
        row.Children.Add(UiKit.Text(_query.Length > 0 ? "No matching windows" : Strings.NoWindows, 14, FontWeights.Normal, ThemeKeys.TextSecondary));
        return row;
    }

    // ---- large icons ----------------------------------------------------------------------------------------

    private FrameworkElement BuildIconRow(double usableW, double usableH)
    {
        var s = _o.Scale;
        var root = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (_entries.Count == 0)
        {
            return Frame(Empty(), 22, 12);
        }

        var selected = _entries[Math.Clamp(_selected, 0, _entries.Count - 1)];
        if (_query.Length > 0 || _pinned)
        {
            root.Children.Add(SearchChip());
        }

        // windows of the selected app above its icon
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        var cardW = Math.Min(200 * s, 260);
        var cardH = _o.Minimal ? cardW * 0.62 + 20 : cardW * 0.62 + 20 + 38;
        if (selected.Windows.Count == 0)
        {
            strip.Children.Add(UiKit.Text(Strings.NoOpenWindow, 12, FontWeights.Medium, ThemeKeys.TextSecondary));
            ((FrameworkElement)strip.Children[0]).Margin = new Thickness(18, 10, 18, 10);
        }
        else
        {
            var maxCards = Math.Max(1, (int)((usableW - 60) / (cardW + 8)));
            var start = Math.Clamp(selected.WindowIndex - maxCards / 2, 0, Math.Max(0, selected.Windows.Count - maxCards));
            for (var i = start; i < Math.Min(selected.Windows.Count, start + maxCards); i++)
            {
                var w = selected.Windows[i];
                var card = Card(selected, _selected, w, cardW, cardH, Math.Min(s, 1), compact: true, windowIndex: i, selected: i == selected.WindowIndex);
                card.Margin = new Thickness(i == start ? 0 : 8, 0, 0, 0);
                strip.Children.Add(card);
            }
        }

        var stripFrame = Frame(strip, 18, 8);
        stripFrame.HorizontalAlignment = HorizontalAlignment.Center;
        stripFrame.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(stripFrame);

        var tileW = 76.0;
        var maxTiles = Math.Max(3, (int)((usableW - 40) / tileW));
        var first = Math.Clamp(_selected - maxTiles / 2, 0, Math.Max(0, _entries.Count - maxTiles));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = first; i < Math.Min(_entries.Count, first + maxTiles); i++)
        {
            row.Children.Add(IconTile(_entries[i], i, i == _selected, i - first + 1));
        }

        var rowFrame = Frame(row, 22, 8);
        rowFrame.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(rowFrame);

        var hint = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        hint.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
        hint.Child = UiKit.Text($"Tab or arrows browse    W close    Q quit    Esc cancel", 10, FontWeights.Medium, ThemeKeys.TextSecondary);
        root.Children.Add(hint);
        return root;
    }

    private FrameworkElement IconTile(SwitchEntry entry, int index, bool selected, int number)
    {
        var tile = new Border { Width = 70, Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(4, 8, 4, 6), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1.5), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand };
        var stack = new StackPanel();
        var size = selected ? 48 : 40;
        var image = new Image { Source = entry.Icon, Width = size, Height = size, Opacity = entry.NoWindows ? 0.7 : 1 };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var iconHost = new Grid { Height = 52 };
        iconHost.Children.Add(image);
        if (entry.Windows.Count > 1)
        {
            var count = new Border { CornerRadius = new CornerRadius(8), MinWidth = 16, Height = 16, Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, 2, 0) };
            count.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);
            count.Child = UiKit.Text(entry.Windows.Count.ToString(), 9.5, FontWeights.Bold, ThemeKeys.OnAccent);
            ((TextBlock)count.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)count.Child).VerticalAlignment = VerticalAlignment.Center;
            iconHost.Children.Add(count);
        }

        stack.Children.Add(iconHost);
        var name = UiKit.Text(entry.AppName, 11, selected ? FontWeights.SemiBold : FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        name.TextAlignment = TextAlignment.Center;
        name.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(name);
        if (_o.ShowHints && number <= 9)
        {
            var cap = new Border { CornerRadius = new CornerRadius(8), MinWidth = 16, Height = 15, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            cap.SetResourceReference(Border.BackgroundProperty, selected ? ThemeKeys.Accent : ThemeKeys.ControlFill);
            cap.Child = UiKit.Text(number.ToString(), 10, FontWeights.Bold, selected ? ThemeKeys.OnAccent : ThemeKeys.TextSecondary);
            ((TextBlock)cap.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)cap.Child).VerticalAlignment = VerticalAlignment.Center;
            ((TextBlock)cap.Child).FontFamily = new FontFamily("Cascadia Mono, Consolas");
            stack.Children.Add(cap);
        }

        tile.Child = stack;
        if (selected)
        {
            tile.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
            tile.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }

        tile.MouseMove += (_, _) =>
        {
            var p = Mouse.GetPosition(this);
            _moved |= Math.Abs(p.X - _openPointer.X) > 1 || Math.Abs(p.Y - _openPointer.Y) > 1;
            if (_moved && index != _selected)
            {
                Hovered?.Invoke(index);
            }
        };
        tile.MouseLeftButtonUp += (_, _) => Clicked?.Invoke(index, entry.WindowIndex);
        tile.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                CloseRequested?.Invoke(index, entry.WindowIndex);
            }
        };
        return tile;
    }

    // ---- simple list ----------------------------------------------------------------------------------------

    private FrameworkElement BuildSimple(double usableW)
    {
        var root = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (_entries.Count == 0)
        {
            return Frame(Empty(), 22, 12);
        }

        var selected = _entries[Math.Clamp(_selected, 0, _entries.Count - 1)];
        if (_query.Length > 0 || _pinned)
        {
            root.Children.Add(SearchChip());
        }

        var titleText = UiKit.Text(selected.NoWindows ? $"{selected.AppName}  -  {Strings.NoOpenWindow}" : selected.Title, 10.5, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        titleText.MaxWidth = Math.Min(560, usableW - 80);
        var strip = Frame(titleText, 15, 0, 0.99);
        strip.Padding = new Thickness(14, 6, 14, 6);
        strip.HorizontalAlignment = HorizontalAlignment.Center;
        strip.Margin = new Thickness(0, 0, 0, 8);
        root.Children.Add(strip);

        var tileW = 58.0;
        var maxTiles = Math.Max(3, (int)((usableW - 40) / tileW));
        var first = Math.Clamp(_selected - maxTiles / 2, 0, Math.Max(0, _entries.Count - maxTiles));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = first; i < Math.Min(_entries.Count, first + maxTiles); i++)
        {
            var entry = _entries[i];
            var index = i;
            var isSelected = i == _selected;
            var tile = new Border { Width = 52, Height = 52, Margin = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1.5), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand };
            var grid = new Grid();
            var image = new Image { Source = entry.Icon, Width = 34, Height = 34, Opacity = entry.NoWindows ? 0.7 : 1 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            grid.Children.Add(image);
            if (entry.Windows.Count > 1)
            {
                var count = new Border { CornerRadius = new CornerRadius(8), MinWidth = 16, Height = 16, Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0) };
                count.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);
                count.Child = UiKit.Text(entry.Windows.Count.ToString(), 9.5, FontWeights.Bold, ThemeKeys.OnAccent);
                ((TextBlock)count.Child).HorizontalAlignment = HorizontalAlignment.Center;
                ((TextBlock)count.Child).VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(count);
            }

            tile.Child = grid;
            if (isSelected)
            {
                tile.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill18);
                tile.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
            }

            tile.MouseMove += (_, _) =>
            {
                var p = Mouse.GetPosition(this);
                _moved |= Math.Abs(p.X - _openPointer.X) > 1 || Math.Abs(p.Y - _openPointer.Y) > 1;
                if (_moved && index != _selected)
                {
                    Hovered?.Invoke(index);
                }
            };
            tile.MouseLeftButtonUp += (_, _) => Clicked?.Invoke(index, entry.WindowIndex);
            tile.MouseDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    CloseRequested?.Invoke(index, entry.WindowIndex);
                }
            };
            row.Children.Add(tile);
        }

        var rowFrame = Frame(row, 22, 8);
        rowFrame.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(rowFrame);
        if (_o.ShowHints)
        {
            var hint = UiKit.Text($"{_hintShortcut} to browse    W close    Q quit", 10, FontWeights.Medium, ThemeKeys.TextSecondary);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(hint);
        }

        return root;
    }
}

/// <summary>A wrapping grid with fixed cell size and spacing (columns known up front).</summary>
internal sealed class UniformGridLike : Panel
{
    private readonly int _columns;
    private readonly double _cellW;
    private readonly double _cellH;
    private readonly double _gap;

    public UniformGridLike(int columns, double cellW, double cellH, double gap)
    {
        _columns = Math.Max(1, columns);
        _cellW = cellW;
        _cellH = cellH;
        _gap = gap;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_cellW, _cellH));
        }

        var rows = (int)Math.Ceiling(InternalChildren.Count / (double)_columns);
        return new Size(_columns * _cellW + (_columns - 1) * _gap, rows * _cellH + Math.Max(0, rows - 1) * _gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var row = i / _columns;
            var column = i % _columns;
            InternalChildren[i].Arrange(new Rect(column * (_cellW + _gap), row * (_cellH + _gap), _cellW, _cellH));
        }

        return finalSize;
    }
}
