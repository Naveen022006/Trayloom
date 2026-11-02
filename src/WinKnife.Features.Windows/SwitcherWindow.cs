// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>
/// The app-switcher overlay: a grid of live DWM thumbnails with icon and title. It is a regular (non layered) window
/// because DWM thumbnails are not drawn on layered windows.
/// </summary>
internal sealed class SwitcherWindow : Window
{
    private const double HeaderHeight = 40;
    private const double Gutter = 18;

    private sealed class Cell
    {
        public required WindowInfo Info { get; init; }

        public required Border Root { get; init; }

        public required Border ThumbHost { get; init; }

        public nint Thumbnail { get; set; }

        public SIZE SourceSize { get; set; }

        public RECT SourceRect { get; set; }
    }

    private readonly IReadOnlyList<WindowInfo> _windows;
    private readonly HotkeyModifiers _releaseModifiers;
    private readonly List<Cell> _cells = [];
    private readonly WrapPanel _grid = new();
    private readonly TextBlock _filterText = new();
    private readonly TextBlock _emptyText = new();
    private readonly DispatcherTimer _releaseTimer;
    private List<WindowInfo> _filtered;
    private string _filter = string.Empty;
    private int _selected;
    private bool _sticky;
    private bool _finished;
    private bool _activatedOnce;
    private int _cols;
    private int _maxCells;
    private double _cellWidth;
    private double _cellHeight;
    private Point? _lastMouse;

    public SwitcherWindow(IReadOnlyList<WindowInfo> windows, HotkeyModifiers releaseModifiers, bool releaseToActivate, int initialIndex)
    {
        _windows = windows;
        _filtered = [.. windows];
        _releaseModifiers = releaseModifiers;
        _sticky = !releaseToActivate || releaseModifiers == HotkeyModifiers.None;
        _selected = windows.Count == 0 ? 0 : Math.Clamp(initialIndex, 0, windows.Count - 1);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, ThemeKeys.WindowBackground);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;

        _releaseTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(25) };
        _releaseTimer.Tick += OnReleaseTick;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += (_, e) => e.Handled = true;
        PreviewTextInput += OnPreviewTextInput;
        PreviewMouseWheel += (_, e) =>
        {
            Move(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        };
        Activated += (_, _) => _activatedOnce = true;
        Deactivated += (_, _) =>
        {
            if (_activatedOnce && !_finished)
            {
                Finish(null);
            }
        };
        Closed += (_, _) => ReleaseThumbnails();
    }

    /// <summary>Raised once with the chosen window, or null when cancelled.</summary>
    public event Action<WindowInfo?>? Completed;

    public bool IsFinished => _finished;

    /// <summary>Builds the layout for the monitor work area (physical pixels) and shows the overlay there.</summary>
    public void ShowOn(RECT work)
    {
        var centerX = work.Left + work.Width / 2;
        var centerY = work.Top + work.Height / 2;
        var scale = WinUtil.PrepareOnMonitorAt(this, centerX, centerY);
        WinUtil.MakeToolWindow(this);
        WinUtil.RoundCorners(this);

        var maxWidth = work.Width / scale * 0.92 - Gutter * 2;
        var maxHeight = work.Height / scale * 0.85 - Gutter * 2 - HeaderHeight;
        ComputeGrid(_windows.Count, maxWidth, maxHeight);

        BuildChrome();
        RebuildCells();

        var contentWidth = _cols * _cellWidth;
        var rows = Math.Max(1, (int)Math.Ceiling(Math.Min(_windows.Count, _maxCells) / (double)_cols));
        var contentHeight = rows * _cellHeight;
        var widthDip = Math.Max(contentWidth, 360) + Gutter * 2;
        var heightDip = contentHeight + HeaderHeight + Gutter * 2;
        Width = widthDip;
        Height = heightDip;

        var pw = (int)Math.Ceiling(widthDip * scale);
        var ph = (int)Math.Ceiling(heightDip * scale);
        WinUtil.SetBounds(this, work.Left + (work.Width - pw) / 2, work.Top + (work.Height - ph) / 2, pw, ph);

        Show();
        WinUtil.ForceForeground(new WindowInteropHelper(this).Handle);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateThumbnails);
        _releaseTimer.Start();
    }

    /// <summary>Moves the selection (used by repeated presses of the hotkey).</summary>
    public void Move(int delta)
    {
        if (_filtered.Count == 0)
        {
            return;
        }

        var count = Math.Min(_filtered.Count, _maxCells);
        _selected = ((_selected + delta) % count + count) % count;
        UpdateSelection();
    }

    /// <summary>Closes the overlay without activating anything.</summary>
    public void Cancel() => Finish(null);

    private void ComputeGrid(int count, double maxWidth, double maxHeight)
    {
        count = Math.Max(1, count);
        foreach (var width in new double[] { 240, 220, 200, 180, 160, 140, 120 })
        {
            var height = CellHeightFor(width);
            var cols = Math.Max(1, Math.Min(count, (int)(maxWidth / width)));
            var rows = (int)Math.Ceiling(count / (double)cols);
            _cellWidth = width;
            _cellHeight = height;
            _cols = cols;
            if (rows * height <= maxHeight)
            {
                _maxCells = count;
                return;
            }
        }

        var maxRows = Math.Max(1, (int)(maxHeight / _cellHeight));
        _maxCells = _cols * maxRows;
    }

    private static double CellHeightFor(double width) => (width - 16) * 0.62 + 34 + 16;

    private void BuildChrome()
    {
        var root = new Grid { Margin = new Thickness(Gutter) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _filterText.VerticalAlignment = VerticalAlignment.Center;
        _filterText.HorizontalAlignment = HorizontalAlignment.Center;
        _filterText.FontSize = 14;
        Grid.SetRow(_filterText, 0);
        root.Children.Add(_filterText);

        _grid.Width = _cols * _cellWidth;
        _grid.HorizontalAlignment = HorizontalAlignment.Center;
        _grid.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        _emptyText.Text = "No matching windows";
        _emptyText.Visibility = Visibility.Collapsed;
        _emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyText.VerticalAlignment = VerticalAlignment.Center;
        _emptyText.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        Grid.SetRow(_emptyText, 1);
        root.Children.Add(_emptyText);

        var frame = new Border { Child = root, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);
        Content = frame;
        UpdateFilterText();
    }

    private void UpdateFilterText()
    {
        if (_filter.Length == 0)
        {
            _filterText.Text = _sticky ? "Type to filter windows, Enter to switch, Esc to cancel" : $"{_windows.Count} window{(_windows.Count == 1 ? string.Empty : "s")}";
            _filterText.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        }
        else
        {
            _filterText.Text = _filter;
            _filterText.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);
        }
    }

    private void RebuildCells()
    {
        ReleaseThumbnails();
        _grid.Children.Clear();
        _cells.Clear();

        var shown = _filtered.Take(_maxCells).ToList();
        _emptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var thumbWidth = _cellWidth - 16;
        var thumbHeight = thumbWidth * 0.62;

        foreach (var info in shown)
        {
            var thumbHost = new Border
            {
                Width = thumbWidth,
                Height = thumbHeight,
                Background = Brushes.Transparent,
            };

            if (info.Minimized)
            {
                thumbHost.Child = BuildIconPlaceholder(info, minimized: true);
            }

            var icon = new Image
            {
                Source = info.Icon,
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

            var title = new TextBlock
            {
                Text = info.Title,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = string.IsNullOrEmpty(info.ProcessName) ? info.Title : $"{info.Title}\n{info.ProcessName}",
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);

            var titleRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0), Height = 24, LastChildFill = true };
            DockPanel.SetDock(icon, Dock.Left);
            titleRow.Children.Add(icon);
            titleRow.Children.Add(title);

            var stack = new StackPanel();
            stack.Children.Add(thumbHost);
            stack.Children.Add(titleRow);

            var root = new Border
            {
                Width = _cellWidth,
                Height = _cellHeight,
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                Child = stack,
                Cursor = Cursors.Hand,
            };

            var cell = new Cell { Info = info, Root = root, ThumbHost = thumbHost };
            root.MouseMove += (_, e) => OnCellMouseMove(cell, e);
            root.MouseLeftButtonUp += (_, e) =>
            {
                Finish(cell.Info);
                e.Handled = true;
            };
            _cells.Add(cell);
            _grid.Children.Add(root);
        }

        if (_selected >= _cells.Count)
        {
            _selected = Math.Max(0, _cells.Count - 1);
        }

        UpdateSelection();
    }

    private static UIElement BuildIconPlaceholder(WindowInfo info, bool minimized)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var image = new Image { Source = info.Icon, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        panel.Children.Add(image);
        if (minimized)
        {
            var label = new TextBlock { Text = "Minimized", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
            panel.Children.Add(label);
        }

        return panel;
    }

    private void UpdateSelection()
    {
        for (var i = 0; i < _cells.Count; i++)
        {
            var selected = i == _selected;
            var root = _cells[i].Root;
            if (selected)
            {
                root.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SurfaceHover);
                root.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
            }
            else
            {
                root.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Surface);
                root.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);
            }
        }
    }

    private void UpdateThumbnails()
    {
        if (_finished)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (var cell in _cells)
        {
            if (cell.Info.Minimized)
            {
                continue;
            }

            if (cell.Thumbnail == 0)
            {
                if (WinApi.DwmRegisterThumbnail(hwnd, cell.Info.Handle, out var thumb) != 0 || thumb == 0)
                {
                    // Source cannot be previewed (protected content, closed): show its icon instead.
                    cell.ThumbHost.Child = BuildIconPlaceholder(cell.Info, minimized: false);
                    continue;
                }

                cell.Thumbnail = thumb;
                var frame = WinApi.VisibleFrame(cell.Info.Handle);
                NativeMethods.GetWindowRect(cell.Info.Handle, out var full);
                var src = new RECT
                {
                    Left = Math.Max(0, frame.Left - full.Left),
                    Top = Math.Max(0, frame.Top - full.Top),
                    Right = Math.Max(1, frame.Right - full.Left),
                    Bottom = Math.Max(1, frame.Bottom - full.Top),
                };
                if (WinApi.DwmQueryThumbnailSourceSize(thumb, out var size) != 0 || size.cx <= 0 || size.cy <= 0)
                {
                    size = new SIZE { cx = Math.Max(1, frame.Width), cy = Math.Max(1, frame.Height) };
                }

                if (src.Right > size.cx || src.Bottom > size.cy || src.Width <= 0 || src.Height <= 0)
                {
                    src = new RECT { Left = 0, Top = 0, Right = size.cx, Bottom = size.cy };
                }

                cell.SourceRect = src;
                cell.SourceSize = new SIZE { cx = src.Width, cy = src.Height };
            }

            var topLeft = cell.ThumbHost.TransformToAncestor(this).Transform(new Point(0, 0));
            var boxWidth = cell.ThumbHost.ActualWidth * dpi;
            var boxHeight = cell.ThumbHost.ActualHeight * dpi;
            var left = topLeft.X * dpi;
            var top = topLeft.Y * dpi;

            var aspect = (double)cell.SourceSize.cx / Math.Max(1, cell.SourceSize.cy);
            var width = boxWidth;
            var height = width / aspect;
            if (height > boxHeight)
            {
                height = boxHeight;
                width = height * aspect;
            }

            var dest = new RECT
            {
                Left = (int)Math.Round(left + (boxWidth - width) / 2),
                Top = (int)Math.Round(top + (boxHeight - height) / 2),
            };
            dest.Right = dest.Left + (int)Math.Round(width);
            dest.Bottom = dest.Top + (int)Math.Round(height);

            var props = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = WinApi.DWM_TNP_RECTDESTINATION | WinApi.DWM_TNP_RECTSOURCE | WinApi.DWM_TNP_VISIBLE | WinApi.DWM_TNP_OPACITY,
                rcDestination = dest,
                rcSource = cell.SourceRect,
                opacity = 255,
                fVisible = true,
            };
            WinApi.DwmUpdateThumbnailProperties(cell.Thumbnail, ref props);
        }
    }

    private void ReleaseThumbnails()
    {
        foreach (var cell in _cells)
        {
            if (cell.Thumbnail != 0)
            {
                WinApi.DwmUnregisterThumbnail(cell.Thumbnail);
                cell.Thumbnail = 0;
            }
        }
    }

    private void OnCellMouseMove(Cell cell, MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_lastMouse is null)
        {
            // The first event is the cursor resting under the freshly shown overlay, not a deliberate hover.
            _lastMouse = position;
            return;
        }

        if ((position - _lastMouse.Value).LengthSquared < 4)
        {
            return;
        }

        _lastMouse = position;
        var index = _cells.IndexOf(cell);
        if (index >= 0 && index != _selected)
        {
            _selected = index;
            UpdateSelection();
        }
    }

    private void OnReleaseTick(object? sender, EventArgs e)
    {
        if (_finished || _sticky)
        {
            return;
        }

        if (!WinUtil.AnyModifierHeld(_releaseModifiers))
        {
            CommitSelection();
        }
    }

    private void CommitSelection()
    {
        Finish(_selected >= 0 && _selected < _cells.Count ? _cells[_selected].Info : null);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        switch (key)
        {
            case Key.Escape:
                Finish(null);
                return;
            case Key.Enter:
                CommitSelection();
                return;
            case Key.Left:
                Move(-1);
                return;
            case Key.Right:
                Move(1);
                return;
            case Key.Up:
                Move(-_cols);
                return;
            case Key.Down:
                Move(_cols);
                return;
            case Key.Tab:
                Move(shift ? -1 : 1);
                return;
            case Key.OemTilde:
                Move(shift ? -1 : 1);
                return;
            case Key.Home:
                _selected = 0;
                UpdateSelection();
                return;
            case Key.End:
                _selected = Math.Max(0, _cells.Count - 1);
                UpdateSelection();
                return;
            case Key.Back:
                if (_filter.Length > 0)
                {
                    SetFilter(_filter[..^1]);
                }

                return;
        }

        // Alt+letter arrives as a system key without text input; translate it so typing works while Alt is held.
        if (e.Key == Key.System)
        {
            var text = KeyToText(key, shift);
            if (text is not null)
            {
                SetFilter(_filter + text);
            }
        }
    }

    private static string? KeyToText(Key key, bool shift)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            var c = (char)('a' + (key - Key.A));
            return (shift ? char.ToUpperInvariant(c) : c).ToString();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((char)('0' + (key - Key.NumPad0))).ToString();
        }

        return key == Key.Space ? " " : null;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;
        var text = new string(e.Text.Where(c => !char.IsControl(c)).ToArray());
        if (text.Length > 0 && !(text == " " && _filter.Length == 0))
        {
            SetFilter(_filter + text);
        }
    }

    private void SetFilter(string filter)
    {
        // Once the user types, the overlay stays open until Enter / Esc even if the modifiers are released.
        _sticky = true;
        _filter = filter;
        var tokens = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _filtered = [.. _windows.Where(w => tokens.All(t =>
            w.Title.Contains(t, StringComparison.OrdinalIgnoreCase)
            || w.ProcessName.Contains(t, StringComparison.OrdinalIgnoreCase)))];
        _selected = 0;
        UpdateFilterText();
        RebuildCells();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateThumbnails);
    }

    private void Finish(WindowInfo? target)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _releaseTimer.Stop();
        ReleaseThumbnails();
        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // already closing
        }

        Completed?.Invoke(target);
    }
}
