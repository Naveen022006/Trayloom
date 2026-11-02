// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;

namespace WinKnife.Features.Files;

/// <summary>What an entry row / card and the preview pane ask of the quick window.</summary>
internal interface IQuickActions
{
    ClipboardHistoryStore Store { get; }

    IReadOnlyList<string> Tokens { get; }

    bool ShowSourceIcons { get; }

    bool CanReorder { get; }

    bool CanEditImages { get; }

    void Activate(ClipEntry entry);

    void TogglePile(ClipEntry entry);

    void PasteOne(ClipEntry entry);

    void CopyOnly(ClipEntry entry);

    void TogglePin(ClipEntry entry);

    void Move(ClipEntry entry, int direction);

    void Delete(ClipEntry entry);

    void EditImage(ClipEntry entry);

    void HoverPreview(ClipEntry entry, bool hovering);

    /// <summary>Drag reordering of pinned / recent entries (only while the search is empty).</summary>
    void Reorder(string movingId, ClipEntry target);
}

/// <summary>Builds the content of an entry (text with highlighted matches, image thumbnail, file names).</summary>
internal static class EntryContent
{
    public static TextBlock Highlighted(string text, IReadOnlyList<string> tokens, double size, string brushKey, int maxLines, FontWeight? weight = null, bool monospace = false)
    {
        var block = new TextBlock
        {
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = weight ?? FontWeights.Normal,
            LineHeight = size * 1.28,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            MaxHeight = size * 1.28 * maxLines + 1,
        };
        if (monospace)
        {
            block.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        }

        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        var excerpt = text.Length > 500 ? text[..500] : text;
        var ranges = ClipSearch.HighlightRanges(excerpt, tokens).OrderBy(r => r.Start).ToList();
        if (ranges.Count == 0)
        {
            block.Text = excerpt;
            return block;
        }

        var cursor = 0;
        foreach (var (start, length) in ranges)
        {
            if (start < cursor)
            {
                continue;
            }

            if (start > cursor)
            {
                block.Inlines.Add(new Run(excerpt[cursor..start]));
            }

            var end = Math.Min(excerpt.Length, start + length);
            var run = new Run(excerpt[start..end]) { FontWeight = FontWeights.SemiBold };
            run.SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Accent);
            block.Inlines.Add(run);
            cursor = end;
        }

        if (cursor < excerpt.Length)
        {
            block.Inlines.Add(new Run(excerpt[cursor..]));
        }

        return block;
    }

    public static Border Swatch(Color color, double size)
    {
        var swatch = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.3),
            Background = new SolidColorBrush(color),
            BorderThickness = new Thickness(0.5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        swatch.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        return swatch;
    }

    public static Image Thumbnail(string path, int pixelWidth, double maxWidth, double maxHeight)
    {
        var image = Ui.ImageView(null, maxWidth, maxHeight);
        image.Clip = null;
        Thumbs.Load(path, pixelWidth, source => image.Source = source);
        return image;
    }

    /// <summary>Content of an entry for a row (<paramref name="isRow"/>) or a card.</summary>
    public static FrameworkElement Build(ClipEntry entry, IQuickActions actions, bool isRow)
    {
        var tokens = actions.Tokens;
        switch (entry.Kind)
        {
            case ClipKinds.Text:
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                if (entry.ColorValue() is { } color)
                {
                    var sw = Swatch(color, 14);
                    sw.Margin = new Thickness(0, 0, 8, 0);
                    row.Children.Add(sw);
                }

                var text = Highlighted(isRow ? entry.Preview() : entry.CardPreview(), tokens, 12, ThemeKeys.TextPrimary, isRow ? 2 : 7);
                text.Width = double.NaN;
                var host = new Grid();
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (row.Children.Count > 0)
                {
                    host.Children.Add(row);
                }

                Grid.SetColumn(text, 1);
                host.Children.Add(text);
                host.VerticalAlignment = isRow ? VerticalAlignment.Center : VerticalAlignment.Top;
                return host;

            case ClipKinds.Image:
                var label = Highlighted($"{Strings.Clipboard.ImageLabel} · {entry.ImageDimensionsLabel}", tokens, 11.5, ThemeKeys.TextSecondary, 1);
                var picture = entry.ImageFile is null ? null : Thumbnail(actions.Store.ImagePath(entry), isRow ? 480 : 520, isRow ? 240 : double.PositiveInfinity, isRow ? 120 : 140);
                return PictureStack(picture, label, isRow, $"{Strings.Clipboard.ImageLabel} · {entry.ImageDimensionsLabel}");

            default:
                if (entry.FilePaths.Count == 1 && ClipImages.IsImageFile(entry.FilePaths[0]))
                {
                    var path = entry.FilePaths[0];
                    var info = new StackPanel();
                    info.Children.Add(Highlighted(entry.FileNames.FirstOrDefault() ?? entry.Preview(), tokens, 12, ThemeKeys.TextPrimary, 1));
                    var dim = ClipImages.Dimensions(path);
                    if (dim is { } d)
                    {
                        info.Children.Add(UiKit.Text($"{Strings.Clipboard.ImageLabel} · {d.Width}×{d.Height}", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
                    }

                    return PictureStack(Thumbnail(path, 480, isRow ? 240 : double.PositiveInfinity, isRow ? 120 : 140), info, isRow, path);
                }

                var files = new StackPanel { VerticalAlignment = isRow ? VerticalAlignment.Center : VerticalAlignment.Top };
                var title = entry.FilePaths.Count == 1
                    ? entry.FileNames.FirstOrDefault() ?? entry.Preview()
                    : string.Format(Strings.Clipboard.FileCountFormat, entry.FilePaths.Count);
                var titleBlock = Highlighted(title, entry.FilePaths.Count == 1 ? tokens : [], 12, ThemeKeys.TextPrimary, 1);
                titleBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                files.Children.Add(titleBlock);
                if (entry.FilePaths.Count > 1)
                {
                    files.Children.Add(Highlighted(entry.Preview(), tokens, 10, ThemeKeys.TextTertiary, isRow ? 1 : 5));
                }

                files.ToolTip = string.Join("\n", entry.FilePaths);
                return files;
        }
    }

    private static FrameworkElement PictureStack(UIElement? picture, UIElement info, bool isRow, string tooltip)
    {
        var stack = new StackPanel { Orientation = isRow ? Orientation.Horizontal : Orientation.Vertical, VerticalAlignment = isRow ? VerticalAlignment.Center : VerticalAlignment.Top, ToolTip = tooltip };
        if (picture is not null)
        {
            if (picture is FrameworkElement fe)
            {
                fe.Margin = isRow ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 6);
            }

            stack.Children.Add(picture);
        }

        if (info is FrameworkElement ie)
        {
            ie.VerticalAlignment = VerticalAlignment.Center;
        }

        stack.Children.Add(info);
        return stack;
    }
}

/// <summary>One entry of the quick window, as a row (list layout) or a card (cards layout).</summary>
internal sealed class EntryVisual : Border
{
    public const double CardWidth = 184;
    public const double CardHeight = 210;

    private readonly IQuickActions _actions;
    private readonly bool _isRow;
    private readonly int? _shortcutIndex;
    private readonly Grid _marker = new() { Width = 25, Height = 25 };
    private readonly UIElement _hoverButtons;
    private readonly UIElement _idleTrailing;
    private bool _selected;
    private bool _batch;
    private bool _hovered;

    public EntryVisual(IQuickActions actions, ClipEntry entry, bool isRow, int? shortcutIndex)
    {
        _actions = actions;
        Entry = entry;
        _isRow = isRow;
        _shortcutIndex = shortcutIndex;
        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;
        ContextMenu = BuildMenu();

        _hoverButtons = BuildHoverButtons();
        _idleTrailing = BuildIdleTrailing();
        _hoverButtons.Visibility = Visibility.Collapsed;

        if (isRow)
        {
            BuildRow();
        }
        else
        {
            BuildCard();
        }

        MouseEnter += (_, _) => SetHovered(true);
        MouseLeave += (_, _) => SetHovered(false);
        MouseLeftButtonDown += (_, e) => _press = e.GetPosition(this);
        MouseMove += OnDragMove;
        MouseLeftButtonUp += (_, e) =>
        {
            _press = null;
            if (!e.Handled)
            {
                actions.Activate(Entry);
            }
        };
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            var ok = actions.CanReorder && e.Data.GetDataPresent(DragFormat);
            e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (actions.CanReorder && e.Data.GetData(DragFormat) is string id)
            {
                actions.Reorder(id, Entry);
            }

            e.Handled = true;
        };
        Refresh();
    }

    private const string DragFormat = "WinKnife.ClipEntryId";
    private System.Windows.Point? _press;

    public ClipEntry Entry { get; }

    private void OnDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_press is not { } start || e.LeftButton != MouseButtonState.Pressed || !_actions.CanReorder)
        {
            return;
        }

        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _press = null;
        var data = new DataObject();
        data.SetData(DragFormat, Entry.Id);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
    }

    public void SetState(bool selected, bool batch)
    {
        _selected = selected;
        _batch = batch;
        Refresh();
    }

    public void ResetHover()
    {
        _hovered = false;
        Refresh();
    }

    private void SetHovered(bool hovered)
    {
        if (_hovered == hovered)
        {
            return;
        }

        _hovered = hovered;
        Refresh();
        _actions.HoverPreview(Entry, hovered);
    }

    private ContextMenu BuildMenu()
    {
        var entry = Entry;
        var menu = Ui.Menu(
            (Strings.Clipboard.Paste, () => _actions.PasteOne(entry), true, false),
            (Strings.Clipboard.Copy, () => _actions.CopyOnly(entry), true, false),
            null,
            (entry.IsPinned ? Strings.Clipboard.Unpin : Strings.Clipboard.Pin, () => _actions.TogglePin(entry), true, false),
            (Strings.Clipboard.MoveUp, () => _actions.Move(entry, -1), true, false),
            (Strings.Clipboard.MoveDown, () => _actions.Move(entry, 1), true, false),
            null,
            (Strings.Clipboard.Delete, () => _actions.Delete(entry), true, true));
        menu.Opened += (_, _) =>
        {
            ((MenuItem)menu.Items[4]).IsEnabled = _actions.CanReorder && _actions.Store.CanMove(entry, -1);
            ((MenuItem)menu.Items[5]).IsEnabled = _actions.CanReorder && _actions.Store.CanMove(entry, 1);
        };
        return menu;
    }

    private UIElement BuildHoverButtons()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (Entry.IsImage && _actions.CanEditImages)
        {
            row.Children.Add(Ui.IconButton(G.Edit, Strings.Clipboard.Edit, () => _actions.EditImage(Entry)));
        }

        var copy = Ui.IconButton(G.Copy, Strings.Clipboard.Copy, () => _actions.CopyOnly(Entry));
        copy.Margin = new Thickness(row.Children.Count > 0 ? 4 : 0, 0, 0, 0);
        row.Children.Add(copy);
        var more = Ui.IconButton(G.More, null, () => { });
        more.Margin = new Thickness(4, 0, 0, 0);
        more.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (ContextMenu is { } menu)
            {
                menu.PlacementTarget = more;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        };
        row.Children.Add(more);
        return row;
    }

    private UIElement BuildIdleTrailing()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Time(HorizontalAlignment.Right));
        if (_shortcutIndex is { } n)
        {
            var hint = UiKit.Text("^" + (n + 1), 9.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
            hint.HorizontalAlignment = HorizontalAlignment.Right;
            hint.Margin = new Thickness(0, 3, 0, 0);
            hint.FontFamily = UiKit.DisplayFont;
            stack.Children.Add(hint);
        }

        return stack;
    }

    private TextBlock Time(HorizontalAlignment alignment)
    {
        var time = UiKit.Text(Entry.CopiedAt.ToLocalTime().ToString("t"), 9.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        time.HorizontalAlignment = alignment;
        return time;
    }

    private void BuildRow()
    {
        CornerRadius = new CornerRadius(7);
        Padding = new Thickness(8, 6, 8, 6);
        MinHeight = 48;
        BorderThickness = new Thickness(1);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _marker.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(_marker);
        _marker.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _actions.TogglePile(Entry);
        };
        _marker.Background = Brushes.Transparent;
        _marker.Cursor = Cursors.Hand;
        _marker.ToolTip = Strings.Clipboard.SelectMultiple;

        if (_actions.ShowSourceIcons && SourceIcon() is { } icon)
        {
            var image = new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = AppIdentity.Resolve(Entry.SourceExe ?? string.Empty).Name };
            Grid.SetColumn(image, 1);
            grid.Children.Add(image);
        }

        var content = EntryContent.Build(Entry, _actions, isRow: true);
        content.Margin = new Thickness(9, 0, 8, 0);
        Grid.SetColumn(content, 2);
        grid.Children.Add(content);

        var trailing = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        trailing.Children.Add(_idleTrailing);
        trailing.Children.Add(_hoverButtons);
        Grid.SetColumn(trailing, 3);
        grid.Children.Add(trailing);
        Child = grid;
    }

    private void BuildCard()
    {
        Width = CardWidth;
        Height = CardHeight;
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(0.5);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        // header: marker + app name + hover buttons / pin
        var header = new Grid { Margin = new Thickness(6, 0, 6, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _marker.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_marker);
        _marker.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _actions.TogglePile(Entry);
        };
        _marker.Background = Brushes.Transparent;
        _marker.Cursor = Cursors.Hand;
        _marker.ToolTip = Strings.Clipboard.SelectMultiple;
        if (!string.IsNullOrEmpty(Entry.SourceExe))
        {
            var name = UiKit.Text(AppIdentity.Resolve(Entry.SourceExe).Name, 11, FontWeights.SemiBold, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            name.Margin = new Thickness(6, 0, 4, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            header.Children.Add(name);
        }

        var right = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (Entry.IsPinned)
        {
            var pin = UiKit.Icon(G.PinFill, 10, ThemeKeys.Accent, FontWeights.SemiBold);
            pin.Margin = new Thickness(0, 0, 4, 0);
            right.Children.Add(pin);
        }

        right.Children.Add(_hoverButtons);
        Grid.SetColumn(right, 2);
        header.Children.Add(right);
        root.Children.Add(header);

        var line1 = UiKit.Divider();
        Grid.SetRow(line1, 1);
        root.Children.Add(line1);

        var content = EntryContent.Build(Entry, _actions, isRow: false);
        var body = new Border { Padding = new Thickness(10), Child = content, ClipToBounds = true };
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var line2 = UiKit.Divider();
        Grid.SetRow(line2, 3);
        root.Children.Add(line2);

        var footer = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var time = Time(HorizontalAlignment.Left);
        time.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(time);
        if (_shortcutIndex is { } n)
        {
            var hint = UiKit.Text("^" + (n + 1), 9.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
            hint.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hint, 1);
            footer.Children.Add(hint);
        }

        Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        Child = root;
    }

    private ImageSource? SourceIcon() => ShellIcons.ForExecutable(Entry.SourcePath ?? AppIdentity.Resolve(Entry.SourceExe ?? string.Empty).Path);

    private void Refresh()
    {
        var accent = ThemeManager.AccentColor;
        Color Alpha(double a) => Color.FromArgb((byte)(255 * a), accent.R, accent.G, accent.B);
        if (_isRow)
        {
            if (_batch)
            {
                Background = new SolidColorBrush(Alpha(_hovered ? 0.17 : 0.13));
                BorderBrush = new SolidColorBrush(Alpha(0.38));
            }
            else if (_selected)
            {
                Background = new SolidColorBrush(Alpha(_hovered ? 0.13 : 0.09));
                BorderBrush = new SolidColorBrush(Alpha(0.24));
            }
            else if (_hovered)
            {
                this.Themed(BackgroundProperty, ThemeKeys.RowHover);
                BorderBrush = Brushes.Transparent;
            }
            else
            {
                Background = Brushes.Transparent;
                BorderBrush = Brushes.Transparent;
            }
        }
        else
        {
            this.Themed(BackgroundProperty, ThemeKeys.CardFill);
            if (_batch || _selected)
            {
                BorderBrush = new SolidColorBrush(accent);
                BorderThickness = new Thickness(2.5);
            }
            else
            {
                this.Themed(BorderBrushProperty, ThemeKeys.CardBorder);
                BorderThickness = new Thickness(0.5);
            }

            if (_hovered && !(_batch || _selected))
            {
                Background = new SolidColorBrush(Color.FromArgb(14, 128, 128, 128));
            }
        }

        _hoverButtons.Visibility = _hovered ? Visibility.Visible : Visibility.Collapsed;
        if (_isRow)
        {
            _idleTrailing.Visibility = _hovered ? Visibility.Collapsed : Visibility.Visible;
        }

        RefreshMarker();
    }

    private void RefreshMarker()
    {
        _marker.Children.Clear();
        UIElement glyph;
        if (_batch)
        {
            glyph = UiKit.Icon(G.CheckCircle, 15, ThemeKeys.Accent);
        }
        else if (_hovered)
        {
            glyph = UiKit.Icon(G.Circle, 15, ThemeKeys.Accent);
            glyph.Opacity = 0.7;
        }
        else if (!_isRow && SourceIcon() is { } sourceIcon && !(Entry.IsFiles))
        {
            glyph = new Image { Source = sourceIcon, Width = 18, Height = 18 };
        }
        else if (Entry.IsFiles && Entry.FilePaths.Count > 0 && !ClipImages.IsImageFile(Entry.FilePaths[0]) && ShellIcons.ForPath(Entry.FilePaths[0], small: false) is { } fileIcon)
        {
            glyph = new Image { Source = fileIcon, Width = 20, Height = 20 };
        }
        else
        {
            var symbol = Entry.IsPinned ? G.PinFill : Entry.Kind switch
            {
                ClipKinds.Image => G.Photo,
                ClipKinds.Files => G.Folder,
                _ => G.Document,
            };
            glyph = UiKit.Icon(symbol, 11, Entry.IsPinned ? ThemeKeys.Accent : ThemeKeys.TextSecondary, FontWeights.SemiBold);
        }

        _marker.Children.Add(glyph);
    }
}

/// <summary>The 280 px inspector of the selected entry: full text (selectable / editable), image or file details.</summary>
internal sealed class PreviewPane : Border
{
    private readonly IQuickActions _actions;
    private readonly IClipboardHost _host;
    private readonly Action _close;
    private readonly ContentControl _body = new();
    private readonly Border _footer = new();
    private ClipEntry? _entry;
    private bool _editing;
    private TextBox? _editor;

    public PreviewPane(IClipboardHost host, IQuickActions actions, Action close)
    {
        _host = host;
        _actions = actions;
        _close = close;
        Width = 280;
        var root = new DockPanel();
        var header = new Grid { Margin = new Thickness(12, 10, 12, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(UiKit.Icon(G.Document, 11.5, ThemeKeys.TextSecondary, FontWeights.SemiBold));
        var titleText = UiKit.Text(Strings.Clipboard.PreviewLabel, 11.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        titleText.Margin = new Thickness(6, 0, 0, 0);
        title.Children.Add(titleText);
        header.Children.Add(title);
        var closeButton = Ui.CloseButton(() => _close(), Strings.Clipboard.Close, 20);
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var line = UiKit.Divider();
        DockPanel.SetDock(line, Dock.Top);
        root.Children.Add(line);
        DockPanel.SetDock(_footer, Dock.Bottom);
        root.Children.Add(_footer);
        var line2 = UiKit.Divider();
        DockPanel.SetDock(line2, Dock.Bottom);
        root.Children.Add(line2);
        root.Children.Add(_body);
        Child = root;
        Show(null);
    }

    public bool IsEditing => _editing;

    public string? EntryId => _entry?.Id;

    public void Show(ClipEntry? entry)
    {
        if (_editing && entry?.Id != _entry?.Id)
        {
            CancelEdit();
        }

        _entry = entry;
        Rebuild();
    }

    public void CancelEdit()
    {
        _editing = false;
        _host.EditingEntryId = null;
        Rebuild();
    }

    private void Rebuild()
    {
        var entry = _entry;
        if (entry is null)
        {
            var empty = UiKit.Text(Strings.Clipboard.Empty, 12, FontWeights.Medium, ThemeKeys.TextSecondary);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            _body.Content = empty;
            _footer.Child = null;
            return;
        }

        if (_editing && entry.IsText)
        {
            _editor = new TextBox
            {
                Text = entry.Text,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(12),
                Padding = new Thickness(8),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(1),
            };
            _editor.SetResourceReference(TextBox.BackgroundProperty, ThemeKeys.RowHover);
            _editor.SetResourceReference(TextBox.ForegroundProperty, ThemeKeys.TextPrimary);
            _editor.SetResourceReference(TextBox.BorderBrushProperty, ThemeKeys.Accent);
            _editor.SetResourceReference(TextBox.CaretBrushProperty, ThemeKeys.TextPrimary);
            _editor.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    CancelEdit();
                }
            };
            _body.Content = _editor;
            _editor.Loaded += (_, _) =>
            {
                _editor.Focus();
                _editor.CaretIndex = _editor.Text.Length;
            };
        }
        else
        {
            _editor = null;
            _body.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Padding = new Thickness(12), Child = BuildContent(entry) },
            };
        }

        _footer.Child = BuildFooter(entry);
    }

    private FrameworkElement BuildContent(ClipEntry entry)
    {
        switch (entry.Kind)
        {
            case ClipKinds.Text:
                var text = entry.Text.Length > 20_000 ? entry.Text[..20_000] + "…" : entry.Text;
                var pretty = PrettyJson(text);
                var box = ReadOnlyText(pretty ?? text);
                if (pretty is not null)
                {
                    box.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                    box.FontSize = 11.5;
                }

                return box;
            case ClipKinds.Image:
                var stack = new StackPanel();
                if (entry.ImageFile is not null)
                {
                    var image = Ui.ImageView(null, double.PositiveInfinity, double.PositiveInfinity);
                    image.HorizontalAlignment = HorizontalAlignment.Stretch;
                    Thumbs.Load(_actions.Store.ImagePath(entry), 560, source => image.Source = source);
                    stack.Children.Add(image);
                }

                var label = UiKit.Text($"{Strings.Clipboard.ImageLabel} · {entry.ImageDimensionsLabel}", 10.5, FontWeights.Normal, ThemeKeys.TextSecondary);
                label.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(label);
                return stack;
            default:
                return BuildFiles(entry);
        }
    }

    private static string? PrettyJson(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed.Length > 256 * 1024 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return pretty.Length > 1024 * 1024 ? null : pretty;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static TextBox ReadOnlyText(string text, double size = 12, bool mono = false)
    {
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            IsReadOnlyCaretVisible = false,
        };
        box.SetResourceReference(TextBox.ForegroundProperty, ThemeKeys.TextPrimary);
        if (mono)
        {
            box.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        }

        return box;
    }

    private FrameworkElement BuildFiles(ClipEntry entry)
    {
        var stack = new StackPanel();
        if (entry.FilePaths.Count == 1)
        {
            var path = entry.FilePaths[0];
            if (ClipImages.IsImageFile(path))
            {
                var image = Ui.ImageView(null, double.PositiveInfinity, double.PositiveInfinity);
                image.HorizontalAlignment = HorizontalAlignment.Stretch;
                Thumbs.Load(path, 560, source => image.Source = source);
                stack.Children.Add(image);
                var dim = ClipImages.Dimensions(path);
                var parts = new List<string> { Strings.Clipboard.ImageLabel };
                if (dim is { } d)
                {
                    parts.Add($"{d.Width}×{d.Height}");
                }

                var size = ClipImages.FileSizeString(path);
                if (size.Length > 0)
                {
                    parts.Add(size);
                }

                var label = UiKit.Text(string.Join(" · ", parts), 10.5, FontWeights.Normal, ThemeKeys.TextSecondary);
                label.Margin = new Thickness(0, 8, 0, 10);
                stack.Children.Add(label);
            }
            else
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                row.Children.Add(new Image { Source = ShellIcons.ForPath(path), Width = 32, Height = 32 });
                var info = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(UiKit.Text(Path.GetFileName(path), 12, FontWeights.Medium, ThemeKeys.TextPrimary, wrap: true));
                var size = ClipImages.FileSizeString(path);
                if (size.Length > 0)
                {
                    info.Children.Add(UiKit.Text(size, 10, FontWeights.Normal, ThemeKeys.TextSecondary));
                }

                row.Children.Add(info);
                stack.Children.Add(row);
            }

            stack.Children.Add(ReadOnlyText(path, 10.5, mono: true));
            return stack;
        }

        stack.Children.Add(UiKit.Text(string.Format(Strings.Clipboard.FileCountFormat, entry.FilePaths.Count), 11.5, FontWeights.SemiBold, ThemeKeys.TextSecondary));
        foreach (var path in entry.FilePaths.Take(40))
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(new Image { Source = ShellIcons.ForPath(path, small: true), Width = 18, Height = 18 });
            var name = UiKit.Text(Path.GetFileName(path), 12, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            name.Margin = new Thickness(7, 0, 0, 0);
            top.Children.Add(name);
            row.Children.Add(top);
            row.Children.Add(ReadOnlyText(path, 10, mono: true));
            stack.Children.Add(row);
        }

        return stack;
    }

    private FrameworkElement BuildFooter(ClipEntry entry)
    {
        var grid = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var time = UiKit.Text(entry.CopiedAt.ToLocalTime().ToString("g"), 10, FontWeights.Normal, ThemeKeys.TextTertiary);
        time.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(time);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        if (_editing)
        {
            buttons.Children.Add(UiKit.Button(Strings.Clipboard.Cancel, ButtonKind.Bordered, 11, null, CancelEdit));
            var save = UiKit.Button(Strings.Clipboard.Save, ButtonKind.Prominent, 11, null, () =>
            {
                if (_editor is not null && _actions.Store.UpdateText(entry, _editor.Text))
                {
                    CancelEdit();
                }
            });
            save.Margin = new Thickness(6, 0, 0, 0);
            buttons.Children.Add(save);
        }
        else
        {
            if (entry.IsText)
            {
                buttons.Children.Add(UiKit.Button(Strings.Clipboard.Edit, ButtonKind.Bordered, 11, null, () =>
                {
                    _editing = true;
                    _host.EditingEntryId = entry.Id;
                    Rebuild();
                }));
            }
            else if (entry.IsImage && _actions.CanEditImages)
            {
                buttons.Children.Add(UiKit.Button(Strings.Clipboard.Edit, ButtonKind.Bordered, 11, null, () => _actions.EditImage(entry)));
            }

            if (entry.IsImage)
            {
                var copyText = UiKit.Button(Strings.Clipboard.CopyText, ButtonKind.Bordered, 11, null, async () => await _host.CopyImageText(entry));
                copyText.Margin = new Thickness(buttons.Children.Count > 0 ? 6 : 0, 0, 0, 0);
                buttons.Children.Add(copyText);
            }

            var copy = UiKit.Button(Strings.Clipboard.Copy, ButtonKind.Bordered, 11, null, () => _actions.CopyOnly(entry));
            copy.Margin = new Thickness(buttons.Children.Count > 0 ? 6 : 0, 0, 0, 0);
            buttons.Children.Add(copy);
        }

        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }
}
