// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core.Ui;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;

namespace WinKnife.Features.WindowTools;

/// <summary>
/// The floating shelf: a drop target holding references to files. Items can be dragged back out (copy/move), opened,
/// revealed in Explorer or removed.
/// </summary>
internal sealed class ShelfWindow : OverlayWindow
{
    private const double ShelfWidth = 340;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico",
    };

    private readonly List<string> _items = [];
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly WrapPanel _tiles = new();
    private readonly TextBlock _hint = new();
    private readonly Border _frame = new();
    private Point? _dragStart;
    private string? _dragOrigin;

    public ShelfWindow()
    {
        Width = ShelfWidth;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 420;
        AllowDrop = true;
        Focusable = false;
        BuildContent();
        UpdateContent();

        DragEnter += OnDrag;
        DragOver += OnDrag;
        DragLeave += (_, _) => SetDropHighlight(false);
        Drop += OnDrop;
    }

    /// <summary>Raised after the list changed through user interaction.</summary>
    public event Action? ItemsChanged;

    public IReadOnlyList<string> Items => _items;

    /// <summary>Adds files or folders (existing paths only, no duplicates).</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        var changed = false;
        foreach (var path in paths)
        {
            if ((File.Exists(path) || Directory.Exists(path))
                && !_items.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _items.Add(path);
                changed = true;
            }
        }

        if (changed)
        {
            UpdateContent();
            ItemsChanged?.Invoke();
        }
    }

    /// <summary>Loads the list without raising <see cref="ItemsChanged"/>.</summary>
    public void SetPaths(IEnumerable<string> paths)
    {
        _items.Clear();
        _items.AddRange(paths.Where(p => File.Exists(p) || Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase));
        UpdateContent();
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        _selected.Clear();
        UpdateContent();
        ItemsChanged?.Invoke();
    }

    /// <summary>Drops items whose file disappeared (for example after being moved out).</summary>
    public void PruneMissing()
    {
        if (_items.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p)) > 0)
        {
            _selected.RemoveWhere(p => !_items.Contains(p, StringComparer.OrdinalIgnoreCase));
            UpdateContent();
            ItemsChanged?.Invoke();
        }
    }

    private void BuildContent()
    {
        var title = new TextBlock { Text = "Shelf", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.OverlayText);

        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 8), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(HeaderButton("", "Open containing folder", OpenFolderOfSelection));
        buttons.Children.Add(HeaderButton("", "Clear shelf", Clear));
        buttons.Children.Add(HeaderButton("", "Hide shelf", Hide));
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        header.Children.Add(title);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // button released early
                }
            }
        };
        header.Background = Brushes.Transparent;

        _hint.Text = "Drop files here to keep them handy, then drag them out where you need them.";
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.TextAlignment = TextAlignment.Center;
        _hint.Margin = new Thickness(12, 18, 12, 18);
        _hint.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.OverlayText);
        _hint.Opacity = 0.65;

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(_hint);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _tiles,
            MaxHeight = 330,
        };
        body.Children.Add(scroll);

        _frame.Child = body;
        _frame.Padding = new Thickness(12);
        _frame.CornerRadius = new CornerRadius(14);
        _frame.BorderThickness = new Thickness(1);
        _frame.Margin = new Thickness(6);
        _frame.SetResourceReference(Border.BackgroundProperty, ThemeKeys.OverlayBackground);
        _frame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        _frame.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.45 };
        Content = _frame;
    }

    private Button HeaderButton(string glyph, string tooltip, Action action)
    {
        var text = new TextBlock { Text = glyph, FontSize = 13 };
        text.SetResourceReference(TextBlock.StyleProperty, ThemeKeys.IconTextStyle);
        text.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.OverlayText);
        var button = new Button
        {
            Content = text,
            ToolTip = tooltip,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdateContent()
    {
        _tiles.Children.Clear();
        foreach (var path in _items)
        {
            _tiles.Children.Add(BuildTile(path));
        }

        _hint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _tiles.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private UIElement BuildTile(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name))
        {
            name = path;
        }

        var icon = new Image { Width = 40, Height = 40, Source = IconFor(path), HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var label = new TextBlock
        {
            Text = name,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxHeight = 30,
            Margin = new Thickness(0, 4, 0, 0),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.OverlayText);

        var stack = new StackPanel();
        stack.Children.Add(icon);
        stack.Children.Add(label);

        var tile = new Border
        {
            Width = 92,
            Height = 82,
            Margin = new Thickness(2),
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Child = stack,
            Tag = path,
            ToolTip = path,
            Cursor = Cursors.Hand,
            Background = _selected.Contains(path) ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
        };
        tile.MouseEnter += (_, _) =>
        {
            if (!_selected.Contains(path))
            {
                tile.Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
            }
        };
        tile.MouseLeave += (_, _) =>
        {
            if (!_selected.Contains(path))
            {
                tile.Background = Brushes.Transparent;
            }
        };
        tile.PreviewMouseLeftButtonDown += (_, e) => OnTileDown(tile, path, e);
        tile.PreviewMouseLeftButtonUp += (_, _) =>
        {
            _dragStart = null;
            _dragOrigin = null;
        };
        tile.PreviewMouseMove += (_, e) => OnTileMove(tile, path, e);
        tile.ContextMenu = BuildMenu(path);
        return tile;
    }

    private ContextMenu BuildMenu(string path)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "Open", () => OpenPath(path));
        AddMenuItem(menu, "Show in folder", () => Reveal(path));
        AddMenuItem(menu, "Copy path", () =>
        {
            try
            {
                System.Windows.Clipboard.SetText(path);
            }
            catch (Exception)
            {
                // clipboard busy
            }
        });
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Remove from shelf", () =>
        {
            _items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _selected.Remove(path);
            UpdateContent();
            ItemsChanged?.Invoke();
        });
        return menu;
    }

    private static void AddMenuItem(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void OnTileDown(Border tile, string path, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OpenPath(path);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (!_selected.Remove(path))
            {
                _selected.Add(path);
            }
        }
        else if (!_selected.Contains(path))
        {
            _selected.Clear();
            _selected.Add(path);
        }

        RefreshSelection();
        _dragStart = e.GetPosition(this);
        _dragOrigin = path;
    }

    private void OnTileMove(Border tile, string path, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is null || _dragOrigin != path)
        {
            return;
        }

        var delta = e.GetPosition(this) - _dragStart.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        _dragOrigin = null;
        var paths = _selected.Contains(path) ? _items.Where(_selected.Contains).ToArray() : [path];
        if (paths.Length == 0)
        {
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        // A move leaves nothing at the old location: forget those references.
        PruneMissing();
    }

    private void RefreshSelection()
    {
        foreach (Border tile in _tiles.Children)
        {
            var path = (string)tile.Tag;
            tile.Background = _selected.Contains(path)
                ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
                : Brushes.Transparent;
        }
    }

    private void OnDrag(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            SetDropHighlight(true);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            AddPaths(paths);
        }

        e.Handled = true;
    }

    private void SetDropHighlight(bool on) =>
        _frame.BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0xE0, 0x7C, 0x7C, 0xF2) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    private void OpenFolderOfSelection()
    {
        var path = _selected.FirstOrDefault() ?? _items.LastOrDefault();
        if (path is not null)
        {
            Reveal(path);
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // no handler / access denied
        }
    }

    private static void Reveal(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            // explorer unavailable
        }
    }

    private ImageSource? IconFor(string path)
    {
        if (_iconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        ImageSource? source = null;
        try
        {
            if (File.Exists(path) && ImageExtensions.Contains(Path.GetExtension(path)))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.UriSource = new Uri(path);
                bitmap.DecodePixelWidth = 80;
                bitmap.EndInit();
                bitmap.Freeze();
                source = bitmap;
            }
        }
        catch (Exception)
        {
            source = null;
        }

        if (source is null)
        {
            try
            {
                using var icon = Directory.Exists(path)
                    ? System.Drawing.Icon.ExtractAssociatedIcon(Environment.SystemDirectory)
                    : System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    var converted = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    converted.Freeze();
                    source = converted;
                }
            }
            catch (Exception)
            {
                source = null;
            }
        }

        _iconCache[path] = source;
        return source;
    }
}
