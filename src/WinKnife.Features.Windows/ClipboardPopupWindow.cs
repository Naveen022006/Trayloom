// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>Searchable clipboard history popup shown near the cursor.</summary>
internal sealed class ClipboardPopupWindow : Window
{
    private const double PopupWidth = 440;
    private const double PopupHeight = 520;

    private readonly ClipboardHistoryStore _store;
    private readonly Action<ClipEntry> _onPaste;
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _empty = new();
    private bool _activatedOnce;
    private bool _closing;

    public ClipboardPopupWindow(ClipboardHistoryStore store, Action<ClipEntry> onPaste)
    {
        _store = store;
        _onPaste = onPaste;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = PopupWidth;
        Height = PopupHeight;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, ThemeKeys.WindowBackground);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;

        BuildContent();
        Refresh();

        PreviewKeyDown += OnPreviewKeyDown;
        Activated += (_, _) => _activatedOnce = true;
        Deactivated += (_, _) =>
        {
            if (_activatedOnce)
            {
                CloseSafe();
            }
        };
        _store.Changed += OnStoreChanged;
        Closed += (_, _) => _store.Changed -= OnStoreChanged;
    }

    /// <summary>Shows the popup near a physical screen point, kept inside the monitor's work area.</summary>
    public void ShowNear(int x, int y)
    {
        var area = Screens.FromPoint(new Point(x, y));
        var scale = WinUtil.PrepareOnMonitorAt(this, x, y);
        WinUtil.MakeToolWindow(this);
        WinUtil.RoundCorners(this);

        var work = area.WorkArea;
        var pw = (int)Math.Ceiling(PopupWidth * scale);
        var ph = (int)Math.Min(Math.Ceiling(PopupHeight * scale), work.Height - 16);
        var margin = (int)(8 * scale);
        var px = Math.Clamp(x + margin, (int)work.Left + margin, Math.Max((int)work.Left + margin, (int)work.Right - pw - margin));
        var py = y + margin;
        if (py + ph > work.Bottom - margin)
        {
            // Not enough room below the cursor: open upwards, then clamp.
            py = Math.Max((int)work.Top + margin, y - ph - margin);
            py = Math.Min(py, (int)work.Bottom - ph - margin);
        }

        WinUtil.SetBounds(this, px, py, pw, ph);
        Show();
        WinUtil.ForceForeground(new WindowInteropHelper(this).Handle);
        _search.Focus();
        Keyboard.Focus(_search);
    }

    private void BuildContent()
    {
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _search.Margin = new Thickness(0, 0, 0, 8);
        _search.Padding = new Thickness(8, 6, 8, 6);
        _search.ToolTip = "Search clipboard history";
        _search.TextChanged += (_, _) => Refresh();
        Grid.SetRow(_search, 0);
        root.Children.Add(_search);

        _list.BorderThickness = new Thickness(0);
        _list.Background = Brushes.Transparent;
        _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        Grid.SetRow(_list, 1);
        root.Children.Add(_list);

        _empty.Text = "Nothing copied yet";
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.IsHitTestVisible = false;
        _empty.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        Grid.SetRow(_empty, 1);
        root.Children.Add(_empty);

        var hint = new TextBlock
        {
            Text = "Enter paste   Up/Down select   Ctrl+P pin   Del remove   Esc close",
            FontSize = 11,
            Margin = new Thickness(2, 8, 2, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        Grid.SetRow(hint, 2);
        root.Children.Add(hint);

        var frame = new Border { Child = root, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);
        Content = frame;
    }

    private void OnStoreChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var previous = (_list.SelectedItem as ListBoxItem)?.Tag as ClipEntry;
        var tokens = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var entries = _store.Entries.Where(e => Matches(e, tokens)).ToList();

        _list.Items.Clear();
        foreach (var entry in entries)
        {
            _list.Items.Add(BuildItem(entry));
        }

        _empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = _store.Entries.Count == 0 ? "Nothing copied yet" : "No matches";

        var selectIndex = previous is null ? 0 : entries.FindIndex(e => e.Id == previous.Id);
        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = Math.Max(0, selectIndex);
            _list.ScrollIntoView(_list.SelectedItem);
        }
    }

    private static bool Matches(ClipEntry entry, string[] tokens)
    {
        foreach (var token in tokens)
        {
            var hit = entry.IsImage
                ? "image".Contains(token, StringComparison.OrdinalIgnoreCase)
                  || (entry.Source?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false)
                : (entry.Text?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false)
                  || (entry.Source?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!hit)
            {
                return false;
            }
        }

        return true;
    }

    private ListBoxItem BuildItem(ClipEntry entry)
    {
        var grid = new Grid { MinHeight = 44, Margin = new Thickness(2, 3, 2, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement lead;
        if (entry.IsImage)
        {
            lead = BuildThumb(entry);
        }
        else
        {
            var glyph = new TextBlock
            {
                Text = "",
                Width = 40,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 16,
            };
            glyph.SetResourceReference(TextBlock.StyleProperty, ThemeKeys.IconTextStyle);
            lead = glyph;
        }

        Grid.SetColumn(lead, 0);
        grid.Children.Add(lead);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        var main = new TextBlock
        {
            Text = entry.IsImage ? $"Image {entry.Width} x {entry.Height}" : Preview(entry.Text ?? string.Empty),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 38,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        main.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);
        var meta = new TextBlock { FontSize = 11, Text = Describe(entry) };
        meta.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        texts.Children.Add(main);
        texts.Children.Add(meta);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(IconButton(entry.Pinned ? "" : "", entry.Pinned ? "Unpin" : "Pin", () => _store.TogglePin(entry), entry.Pinned));
        buttons.Children.Add(IconButton("", "Delete", () => _store.Delete(entry), false));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        var item = new ListBoxItem
        {
            Content = grid,
            Tag = entry,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = entry.IsImage ? null : TooltipText(entry.Text ?? string.Empty),
        };
        item.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled || IsOverButton(e.OriginalSource as DependencyObject))
            {
                return;
            }

            e.Handled = true;
            Paste(entry);
        };
        return item;
    }

    private static bool IsOverButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
            {
                return true;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private Button IconButton(string glyph, string tooltip, Action action, bool highlighted)
    {
        var text = new TextBlock { Text = glyph, FontSize = 13 };
        text.SetResourceReference(TextBlock.StyleProperty, ThemeKeys.IconTextStyle);
        text.SetResourceReference(TextBlock.ForegroundProperty, highlighted ? ThemeKeys.Accent : ThemeKeys.TextMuted);
        return new Button
        {
            Content = text,
            ToolTip = tooltip,
            Width = 30,
            Height = 30,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Command = new RelayCommand(action),
        };
    }

    private UIElement BuildThumb(ClipEntry entry)
    {
        var image = new Image
        {
            Width = 40,
            Height = 40,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(_store.ThumbPath(entry));
            bitmap.DecodePixelHeight = 80;
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
        }
        catch (Exception)
        {
            // thumbnail missing: leave blank
        }

        return image;
    }

    private static string Preview(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var preview = string.Join("  ", lines.Take(3));
        return preview.Length > 200 ? preview[..200] + "..." : preview;
    }

    private static string TooltipText(string text) => text.Length > 1200 ? text[..1200] + "..." : text;

    private static string Describe(ClipEntry entry)
    {
        var age = DateTime.UtcNow - entry.CreatedUtc;
        var when = age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago"
            : $"{(int)age.TotalDays} d ago";
        var parts = new List<string>();
        if (entry.Pinned)
        {
            parts.Add("Pinned");
        }

        if (!string.IsNullOrEmpty(entry.Source))
        {
            parts.Add(entry.Source);
        }

        parts.Add(when);
        return string.Join("  -  ", parts);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                CloseSafe();
                break;
            case Key.Down:
                e.Handled = true;
                MoveSelection(1);
                break;
            case Key.Up:
                e.Handled = true;
                MoveSelection(-1);
                break;
            case Key.PageDown:
                e.Handled = true;
                MoveSelection(8);
                break;
            case Key.PageUp:
                e.Handled = true;
                MoveSelection(-8);
                break;
            case Key.Enter:
                e.Handled = true;
                if (SelectedEntry() is { } chosen)
                {
                    Paste(chosen);
                }

                break;
            case Key.Delete when ctrl || _search.Text.Length == 0:
                e.Handled = true;
                if (SelectedEntry() is { } doomed)
                {
                    _store.Delete(doomed);
                }

                break;
            case Key.P when ctrl:
                e.Handled = true;
                if (SelectedEntry() is { } pinned)
                {
                    _store.TogglePin(pinned);
                }

                break;
        }
    }

    private ClipEntry? SelectedEntry() => (_list.SelectedItem as ListBoxItem)?.Tag as ClipEntry;

    private void MoveSelection(int delta)
    {
        if (_list.Items.Count == 0)
        {
            return;
        }

        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
        _list.ScrollIntoView(_list.SelectedItem);
    }

    private void Paste(ClipEntry entry)
    {
        if (_closing)
        {
            return;
        }

        CloseSafe();
        _onPaste(entry);
    }

    private void CloseSafe()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // already closing
        }
    }

    private sealed class RelayCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
