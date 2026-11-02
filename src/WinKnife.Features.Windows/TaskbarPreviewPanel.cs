// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>Settings the taskbar preview panel needs.</summary>
internal sealed class PreviewPanelOptions
{
    public double Scale { get; init; } = 1;

    public bool Minimal { get; init; }

    public bool QuitOnX { get; init; }

    public double Opacity { get; init; } = 1;

    public bool Thumbnails { get; init; } = true;
}

/// <summary>
/// The panel that opens above a taskbar button (spec 05 A2.2): a horizontal run of window cards with picture, status badges, title,
/// close and minimize/restore buttons; context menu; pinnable header; cards can be dragged out to move the window.
/// </summary>
internal sealed class TaskbarPreviewPanel : OverlayWindow
{
    private readonly PreviewPanelOptions _o;
    private readonly Dictionary<nint, Image> _pictures = [];
    private readonly Dictionary<nint, (Border Card, Border Min, TextBlock MinGlyph)> _cards = [];
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private List<SwitchWindow> _windows = [];
    private string _appName = string.Empty;
    private BitmapSource? _appIcon;
    private bool _pinned;
    private nint _selected;
    private Window? _ghost;
    private Point _dragStart;
    private nint _dragHwnd;
    private bool _dragging;

    public TaskbarPreviewPanel(PreviewPanelOptions options)
    {
        _o = options;
        ClickThrough = false;
        Focusable = false;
        MouseDown += (_, _) => { };
    }

    public bool IsPinned => _pinned;

    public event Action<nint>? Chosen;

    public event Action<nint>? CloseRequested;

    public event Action<nint>? MinimizeToggled;

    public event Action<bool>? PinChanged;

    public event Action? PanelClosed;

    public event Action<nint, Point>? Dropped;

    public RECT PhysicalBounds
    {
        get
        {
            NativeMethods.GetWindowRect(Handle, out var r);
            return r;
        }
    }

    public void ThumbReady(nint hwnd)
    {
        if (_pictures.TryGetValue(hwnd, out var image) && WindowCapture.Cached(hwnd) is { } bitmap)
        {
            image.Source = bitmap;
            image.Visibility = Visibility.Visible;
        }
    }

    public void SetWindows(string appName, BitmapSource? icon, List<SwitchWindow> windows)
    {
        _appName = appName;
        _appIcon = icon;
        _windows = windows;
        Build();
    }

    /// <summary>Places the panel on the side of the taskbar facing the screen, centred on the button.</summary>
    public void ShowFor(TaskbarButton button)
    {
        var centerX = (button.Bounds.Left + button.Bounds.Right) / 2;
        var centerY = (button.Bounds.Top + button.Bounds.Bottom) / 2;
        var scale = WinUtil.PrepareOnMonitorAt(this, centerX, centerY);
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = centerX, Y = centerY }, NativeConstants.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        var work = info.rcWork;

        var maxWidth = (work.Width - 16) / scale;
        Measure(new Size(maxWidth, (work.Height - 16) / scale));
        var content = (FrameworkElement)Content;
        content.Measure(new Size(maxWidth, (work.Height - 16) / scale));
        var width = Math.Min(content.DesiredSize.Width, maxWidth);
        var height = content.DesiredSize.Height;
        Width = width;
        Height = height;
        var pw = (int)Math.Ceiling(width * scale);
        var ph = (int)Math.Ceiling(height * scale);

        const int gap = 6;
        int x;
        int y;
        switch (button.Edge)
        {
            case TaskbarEdge.Top:
                x = centerX - pw / 2;
                y = button.TaskbarRect.Bottom + gap;
                break;
            case TaskbarEdge.Left:
                x = button.TaskbarRect.Right + gap;
                y = centerY - ph / 2;
                break;
            case TaskbarEdge.Right:
                x = button.TaskbarRect.Left - pw - gap;
                y = centerY - ph / 2;
                break;
            default:
                x = centerX - pw / 2;
                y = button.TaskbarRect.Top - ph - gap;
                break;
        }

        x = Math.Clamp(x, work.Left + 8, Math.Max(work.Left + 8, work.Right - pw - 8));
        y = Math.Clamp(y, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - ph - 8));
        NativeMethods.SetWindowPos(Handle, NativeConstants.HWND_TOPMOST, x, y, pw, ph, NativeConstants.SWP_NOACTIVATE);
        ShowNoActivate();
    }

    public void Rebuild()
    {
        Build();
        if (Content is FrameworkElement content && IsVisible)
        {
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Width = content.DesiredSize.Width;
            Height = content.DesiredSize.Height;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _ghost?.Close();
    }

    private double Metric(double value) => Math.Round(value * Math.Min(_o.Scale, 1));

    private void Build()
    {
        _pictures.Clear();
        _cards.Clear();
        _row.Children.Clear();
        var pad = Metric(10);
        var spacing = Metric(8);
        var pictureW = Math.Floor(200 * _o.Scale / 8) * 8;
        var pictureH = Math.Round(pictureW * 10 / 16);
        var inset = Metric(5);
        var well = new Size(pictureW + inset * 2, pictureH + inset * 2);
        var cardPad = Metric(10);
        var titleSpacing = Metric(7);
        var cardW = well.Width + cardPad * 2;
        var cardH = well.Height + cardPad * 2 + (_o.Minimal ? 0 : titleSpacing + 34);

        for (var i = 0; i < _windows.Count; i++)
        {
            var card = Card(_windows[i], cardW, cardH, well, inset, cardPad, titleSpacing);
            card.Margin = new Thickness(i == 0 ? 0 : spacing, 0, 0, 0);
            _row.Children.Add(card);
        }

        var stack = new StackPanel();
        if (_pinned)
        {
            stack.Children.Add(Header());
        }

        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _row, Focusable = false };
        stack.Children.Add(scroll);

        var panel = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(pad),
            BorderThickness = new Thickness(_o.Minimal ? 0 : 1),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = ThemeManager.IsLight ? 0.2 : 0.45, Color = Colors.Black, Direction = 270 },
            Margin = new Thickness(22),
        };
        var brush = UiKit.BrushOf(ThemeKeys.PanelBase).Clone();
        brush.Opacity = Math.Clamp(_o.Opacity, 0.4, 1);
        panel.Background = brush;
        panel.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        panel.MouseLeftButtonUp += (_, _) => { };
        Content = panel;
    }

    private FrameworkElement Header()
    {
        var grid = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 8), Background = Brushes.Transparent };
        grid.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Button)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // not a primary-button drag
                }
            }
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_appIcon is not null)
        {
            left.Children.Add(new Image { Source = _appIcon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) });
        }

        left.Children.Add(UiKit.Text(_appName, 12, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(8, 0, 0, 0) };
        pill.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        pill.Child = UiKit.Text(Strings.PanelPinned, 10, FontWeights.Medium, ThemeKeys.TextSecondary);
        left.Children.Add(pill);
        var position = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(8, 0, 0, 0) };
        position.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var index = Math.Max(0, _windows.FindIndex(w => w.Hwnd == _selected));
        position.Child = UiKit.Text($"{index + 1}/{_windows.Count}", 10, FontWeights.Medium, ThemeKeys.TextSecondary);
        left.Children.Add(position);
        grid.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(HeaderButton("", Strings.PanelPrevious, () => Step(-1)));
        right.Children.Add(HeaderButton("", Strings.PanelNext, () => Step(1)));
        right.Children.Add(HeaderButton("", Strings.PanelUnpin, () =>
        {
            _pinned = false;
            PinChanged?.Invoke(false);
            Rebuild();
        }));
        right.Children.Add(HeaderButton("", Strings.PanelClosePreview, () => PanelClosed?.Invoke()));
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    private static Button HeaderButton(string glyph, string tip, Action click)
    {
        var button = ShortcutUi.SmallIconButton(glyph, tip);
        button.Click += (_, _) => click();
        return button;
    }

    private void Step(int delta)
    {
        if (_windows.Count == 0)
        {
            return;
        }

        var index = Math.Max(0, _windows.FindIndex(w => w.Hwnd == _selected));
        _selected = _windows[((index + delta) % _windows.Count + _windows.Count) % _windows.Count].Hwnd;
        Chosen?.Invoke(_selected);
        Rebuild();
    }

    private Border Card(SwitchWindow window, double cardW, double cardH, Size well, double inset, double padding, double titleSpacing)
    {
        var s = _o.Scale;
        var stack = new StackPanel();
        var wellBorder = new Border { Width = well.Width, Height = well.Height, CornerRadius = new CornerRadius(10), ClipToBounds = true };
        wellBorder.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
        var layers = new Grid();
        var owner = AppIdentity.OwnerOf(window.Hwnd);
        var icon = AppIdentity.IconOfWindow(window.Hwnd, owner.Path);
        var cached = _o.Thumbnails ? WindowCapture.Cached(window.Hwnd) : null;
        var hasCapture = cached is not null;
        var fallback = new Image { Source = icon, Width = 52 * s, Height = 52 * s, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = hasCapture ? Visibility.Collapsed : Visibility.Visible };
        var picture = new Image { Source = cached, Stretch = Stretch.Uniform, Visibility = hasCapture ? Visibility.Visible : Visibility.Collapsed };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        var host = new Border { Margin = new Thickness(inset), CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = picture };
        layers.Children.Add(fallback);
        layers.Children.Add(host);
        _pictures[window.Hwnd] = picture;
        picture.IsVisibleChanged += (_, _) => fallback.Visibility = picture.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (!_o.Minimal)
        {
            if (hasCapture || _o.Thumbnails)
            {
                var badge = new Image { Source = icon, Width = 32 * s, Height = 32 * s, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6) };
                layers.Children.Add(badge);
            }

            var statuses = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
            void Badge(string glyph)
            {
                var b = new Border { Width = 20, Height = 18, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(117, 0, 0, 0)), Margin = new Thickness(0, 0, 4, 0) };
                b.Child = new TextBlock { Text = glyph, FontFamily = UiKit.IconFont, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                statuses.Children.Add(b);
            }

            if (window.Minimized) Badge("");
            if (window.Fullscreen) Badge("");
            if (window.OtherDesktop) Badge("");
            layers.Children.Add(statuses);
        }

        wellBorder.Child = layers;
        stack.Children.Add(wellBorder);

        Border minimizeButton = new();
        TextBlock minimizeGlyph = new();
        var closeButton = new Border();
        if (!_o.Minimal)
        {
            var band = new Grid { Height = 34, Margin = new Thickness(2, titleSpacing, 2, 0) };
            band.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            band.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(UiKit.Text(window.Title, 13, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
            var subtitle = string.Equals(window.Title, _appName, StringComparison.OrdinalIgnoreCase) ? string.Empty : _appName;
            texts.Children.Add(UiKit.Text(subtitle, 10.5, FontWeights.Medium, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
            band.Children.Add(texts);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden };
            (minimizeButton, minimizeGlyph) = RoundButton(window.Minimized ? "" : "", window.Minimized ? Strings.PanelRestore : Strings.PanelMinimize);
            closeButton = RoundButton("", _o.QuitOnX ? Strings.PanelQuit : Strings.PanelCloseWindow).Button;
            if (!window.Fullscreen)
            {
                buttons.Children.Add(minimizeButton);
            }

            buttons.Children.Add(closeButton);
            Grid.SetColumn(buttons, 1);
            band.Children.Add(buttons);
            stack.Children.Add(band);
            minimizeButton.MouseLeftButtonUp += (_, e) =>
            {
                MinimizeToggled?.Invoke(window.Hwnd);
                e.Handled = true;
            };
            closeButton.MouseLeftButtonUp += (_, e) =>
            {
                CloseRequested?.Invoke(window.Hwnd);
                e.Handled = true;
            };
            _hover[window.Hwnd] = buttons;
        }

        var card = new Border { Width = cardW, Height = cardH, CornerRadius = new CornerRadius(14), Padding = new Thickness(padding), BorderThickness = new Thickness(2), Child = stack, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand };
        card.MouseEnter += (_, _) =>
        {
            card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
            if (_hover.TryGetValue(window.Hwnd, out var b))
            {
                b.Visibility = Visibility.Visible;
            }
        };
        card.MouseLeave += (_, _) =>
        {
            if (!_dragging)
            {
                card.BorderBrush = Brushes.Transparent;
                if (_hover.TryGetValue(window.Hwnd, out var b))
                {
                    b.Visibility = Visibility.Hidden;
                }
            }
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(null);
            _dragHwnd = window.Hwnd;
            card.CaptureMouse();
        };
        card.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragHwnd != window.Hwnd || window.Minimized || window.Fullscreen)
            {
                return;
            }

            var p = e.GetPosition(null);
            if (!_dragging && (Math.Abs(p.X - _dragStart.X) > 6 || Math.Abs(p.Y - _dragStart.Y) > 6))
            {
                _dragging = true;
                StartGhost(owner.Path);
            }

            if (_dragging)
            {
                MoveGhost();
            }
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            card.ReleaseMouseCapture();
            if (_dragging)
            {
                _dragging = false;
                NativeMethods.GetCursorPos(out var cursor);
                _ghost?.Close();
                _ghost = null;
                Dropped?.Invoke(window.Hwnd, new Point(cursor.X, cursor.Y));
            }
            else if (!e.Handled)
            {
                Chosen?.Invoke(window.Hwnd);
            }

            _dragHwnd = 0;
        };
        card.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                CloseRequested?.Invoke(window.Hwnd);
            }
        };
        card.MouseRightButtonUp += (_, e) =>
        {
            Menu(card, window);
            e.Handled = true;
        };
        if (window.Hwnd == _selected)
        {
            card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }

        _cards[window.Hwnd] = (card, minimizeButton, minimizeGlyph);
        return card;
    }

    private readonly Dictionary<nint, StackPanel> _hover = [];

    private static (Border Button, TextBlock Glyph) RoundButton(string glyph, string tip)
    {
        var border = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(5, 0, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip };
        var text = new TextBlock { Text = glyph, FontFamily = UiKit.IconFont, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        border.Child = text;
        border.MouseEnter += (_, _) => border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        return (border, text);
    }

    private void Menu(FrameworkElement anchor, SwitchWindow window)
    {
        var menu = new ContextMenu { PlacementTarget = anchor };
        var open = new MenuItem { Header = Strings.PanelOpenWindow };
        open.Click += (_, _) => Chosen?.Invoke(window.Hwnd);
        var minimize = new MenuItem { Header = window.Minimized ? Strings.PanelRestore : Strings.PanelMinimize, IsEnabled = !window.Fullscreen };
        minimize.Click += (_, _) => MinimizeToggled?.Invoke(window.Hwnd);
        var pin = new MenuItem { Header = _pinned ? Strings.PanelUnpin : Strings.PanelPin };
        pin.Click += (_, _) =>
        {
            _pinned = !_pinned;
            _selected = window.Hwnd;
            PinChanged?.Invoke(_pinned);
            Rebuild();
        };
        var close = new MenuItem { Header = _o.QuitOnX ? Strings.PanelQuit : Strings.PanelCloseWindow };
        close.Click += (_, _) => CloseRequested?.Invoke(window.Hwnd);
        menu.Items.Add(open);
        menu.Items.Add(minimize);
        menu.Items.Add(new Separator());
        menu.Items.Add(pin);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.IsOpen = true;
    }

    private void StartGhost(string? path)
    {
        _ghost?.Close();
        var ghost = new OverlayWindow { Width = 120, Height = 80, ClickThrough = true };
        var icon = AppIdentity.IconOf(path);
        ghost.Content = new Border
        {
            CornerRadius = new CornerRadius(10),
            Opacity = 0.85,
            Background = new SolidColorBrush(Color.FromArgb(220, 30, 32, 38)),
            Child = new Image { Source = icon, Width = 40, Height = 40 },
        };
        _ghost = ghost;
        ghost.ShowNoActivate();
        MoveGhost();
    }

    private void MoveGhost()
    {
        if (_ghost is null)
        {
            return;
        }

        NativeMethods.GetCursorPos(out var p);
        var scale = NativeMethods.GetDpiForWindow(((OverlayWindow)_ghost).Handle) / 96.0;
        NativeMethods.SetWindowPos(((OverlayWindow)_ghost).Handle, NativeConstants.HWND_TOPMOST, p.X + 8, p.Y + 8, (int)(120 * scale), (int)(80 * scale), NativeConstants.SWP_NOACTIVATE);
    }
}
