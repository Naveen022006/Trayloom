// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Input;

/// <summary>
/// The Quick snippet menu: a borderless floating panel (460 wide, radius 22) placed Spotlight-style on the display with
/// the pointer. Type to filter (name, trigger, text, folder), Up/Down move, Enter or click inserts the snippet into the
/// app that was active, Esc or a click outside closes. Folders come first (alphabetical), loose snippets last.
/// </summary>
internal sealed class SnippetLibraryWindow : Window
{
    private const double PanelWidth = 460;
    private const double Shadow = 16;

    private readonly IContributionServices _services;
    private readonly Action<SnippetItem> _insert;
    private readonly TextBox _search;
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll;
    private readonly List<(SnippetItem Item, Border Row)> _rows = [];
    private readonly List<SnippetItem> _all;
    private int _selected;
    private bool _closing;

    public SnippetLibraryWindow(IContributionServices services, nint previousForeground, Action<SnippetItem> insert)
    {
        _services = services;
        _insert = insert;
        _all = SnippetStore.Load(services.App).Where(x => x.Enabled && x.ShowsInLibrary && x.Trigger.Length > 0).ToList();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = PanelWidth + (Shadow * 2);
        UseLayoutRounding = true;
        Title = Strings.QuickSnippetMenu;
        FontFamily = UiKit.TextFont;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _ = previousForeground;

        _search = new TextBox
        {
            FontSize = 17,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(10, 0, 0, 0),
        };
        _search.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"TextBox\"><ScrollViewer x:Name=\"PART_ContentHost\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Focusable=\"False\" HorizontalScrollBarVisibility=\"Hidden\" VerticalScrollBarVisibility=\"Hidden\"/></ControlTemplate>");
        _search.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        _search.SetResourceReference(TextBox.CaretBrushProperty, ThemeKeys.Accent);
        var hint = UiKit.Text(Strings.SearchSnippets, 17, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(10, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;

        var searchRow = new Grid { Margin = new Thickness(16, 13, 16, 13) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.Children.Add(UiKit.Icon("", 15, ThemeKeys.TextSecondary));
        var field = new Grid();
        field.Children.Add(_search);
        field.Children.Add(hint);
        Grid.SetColumn(field, 1);
        searchRow.Children.Add(field);

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 330, Content = _list, Padding = new Thickness(0) };
        _list.Margin = new Thickness(8, 6, 8, 6);

        var manage = UiKit.Text(Strings.ManageSnippets, 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        manage.Cursor = Cursors.Hand;
        manage.MouseLeftButtonUp += (_, _) =>
        {
            CloseNow();
            services.Shell.OpenSettings(SettingsPages.TextSnippets);
        };
        var footer = new Grid { Margin = new Thickness(16, 9, 16, 9) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(UiKit.Text(Strings.LibraryFooter, 12, FontWeights.Normal, ThemeKeys.TextTertiary));
        Grid.SetColumn(manage, 1);
        footer.Children.Add(manage);

        var stack = new StackPanel();
        stack.Children.Add(searchRow);
        stack.Children.Add(UiKit.Divider());
        stack.Children.Add(_scroll);
        stack.Children.Add(UiKit.Divider());
        stack.Children.Add(footer);

        var panel = new Border
        {
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(Shadow),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35, Color = Colors.Black },
            ClipToBounds = false,
        };
        panel.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
        panel.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        Content = panel;

        _search.TextChanged += (_, _) =>
        {
            hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Rebuild();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => CloseNow();
        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) =>
        {
            Activate();
            _search.Focus();
        };
        Rebuild();
    }

    private void Place()
    {
        try
        {
            var cursor = Screens.CursorPosition();
            var area = Screens.FromPoint(cursor);
            var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)cursor.X, Y = (int)cursor.Y }, NativeConstants.MONITOR_DEFAULTTONEAREST);
            var scale = Win32.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
            var widthPx = (PanelWidth + (Shadow * 2)) * scale;
            var x = (int)(area.WorkArea.X + ((area.WorkArea.Width - widthPx) / 2));
            var y = (int)(area.WorkArea.Y + (area.WorkArea.Height * 0.2));
            var handle = new WindowInteropHelper(this).EnsureHandle();
            NativeMethods.SetWindowPos(handle, 0, x, y, 0, 0, NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
        }
        catch
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void CloseNow()
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
        catch
        {
            // already closing
        }
    }

    // ---------------------------------------------------------------------------------------------- content

    private static string Fold(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().ToLowerInvariant();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        _rows.Clear();
        var query = Fold(_search.Text.Trim());
        var matches = _all.Where(x => query.Length == 0
            || Fold(x.Name).Contains(query) || Fold(x.Trigger).Contains(query) || Fold(x.Replacement).Contains(query) || Fold(x.Folder).Contains(query)).ToList();

        if (matches.Count == 0)
        {
            _list.Children.Add(EmptyState(_all.Count == 0));
            _selected = -1;
            return;
        }

        var folders = matches.Where(x => x.Folder.Trim().Length > 0)
            .GroupBy(x => x.Folder.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var first = true;
        foreach (var group in folders)
        {
            _list.Children.Add(FolderHeader(group.Key, first));
            first = false;
            foreach (var item in group.OrderBy(x => x.DisplayTitle, StringComparer.CurrentCultureIgnoreCase))
            {
                AddRow(item);
            }
        }

        var loose = matches.Where(x => x.Folder.Trim().Length == 0).OrderBy(x => x.DisplayTitle, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (loose.Count > 0 && folders.Count > 0)
        {
            _list.Children.Add(new Border { Height = 4 });
        }

        foreach (var item in loose)
        {
            AddRow(item);
        }

        _selected = 0;
        Highlight();
    }

    private static FrameworkElement FolderHeader(string name, bool first)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, first ? 4 : 10, 10, 4) };
        row.Children.Add(UiKit.Icon("", 10, ThemeKeys.TextSecondary, FontWeights.SemiBold));
        var label = UiKit.Text(name, 11.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        label.Margin = new Thickness(7, 0, 0, 0);
        row.Children.Add(label);
        return row;
    }

    private FrameworkElement EmptyState(bool libraryEmpty)
    {
        var stack = new StackPanel { Margin = new Thickness(20, 24, 20, 24), HorizontalAlignment = HorizontalAlignment.Center };
        if (libraryEmpty)
        {
            var icon = UiKit.Icon("", 28, ThemeKeys.TextTertiary);
            icon.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(icon);
            var text = UiKit.Text(Strings.LibraryEmpty, 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            text.TextAlignment = TextAlignment.Center;
            text.MaxWidth = 340;
            stack.Children.Add(text);
            var button = UiKit.Button(Strings.ManageSnippets, ButtonKind.Bordered, 12, null, () =>
            {
                CloseNow();
                _services.Shell.OpenSettings(SettingsPages.TextSnippets);
            });
            button.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(button);
        }
        else
        {
            var text = UiKit.Text(Strings.LibraryNoMatch, 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            text.TextAlignment = TextAlignment.Center;
            stack.Children.Add(text);
        }

        return stack;
    }

    private void AddRow(SnippetItem item)
    {
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(UiKit.Text(item.DisplayTitle, 13, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        var chipText = UiKit.Text(item.Trigger, 11.5, FontWeights.Normal, ThemeKeys.TextPrimary);
        chipText.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        var chip = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(7, 0, 0, 0), Child = chipText, VerticalAlignment = VerticalAlignment.Center, Background = InputUi.Tinted(ThemeKeys.TextPrimary, 0.07) };
        title.Children.Add(chip);

        var preview = item.Replacement.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        var stack = new StackPanel();
        stack.Children.Add(title);
        var caption = UiKit.Text(preview, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
        caption.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(caption);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(stack);
        var enter = UiKit.Icon("", 12, ThemeKeys.TextSecondary);
        enter.Visibility = Visibility.Hidden;
        enter.Margin = new Thickness(8, 0, 4, 0);
        Grid.SetColumn(enter, 1);
        grid.Children.Add(enter);

        var row = new Border { Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = grid, Cursor = Cursors.Hand, Tag = enter };
        row.MouseEnter += (_, _) =>
        {
            var index = _rows.FindIndex(r => r.Row == row);
            if (index >= 0 && index != _selected)
            {
                _selected = index;
                Highlight(false);
            }
        };
        row.MouseLeftButtonUp += (_, _) => Commit(item);
        _rows.Add((item, row));
        _list.Children.Add(row);
    }

    private void Highlight(bool scroll = true)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i].Row;
            var on = i == _selected;
            row.Background = on ? InputUi.Tinted(ThemeKeys.Accent, 0.22) : Brushes.Transparent;
            ((UIElement)row.Tag).Visibility = on ? Visibility.Visible : Visibility.Hidden;
            if (on && scroll)
            {
                row.BringIntoView();
            }
        }
    }

    private void Commit(SnippetItem item)
    {
        CloseNow();
        _insert(item);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseNow();
                e.Handled = true;
                break;
            case Key.Down when _rows.Count > 0:
                _selected = Math.Min(_rows.Count - 1, _selected + 1);
                Highlight();
                e.Handled = true;
                break;
            case Key.Up when _rows.Count > 0:
                _selected = Math.Max(0, _selected - 1);
                Highlight();
                e.Handled = true;
                break;
            case Key.Enter when _selected >= 0 && _selected < _rows.Count:
                Commit(_rows[_selected].Item);
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D9 when Keyboard.Modifiers == ModifierKeys.Control:
                var index = e.Key - Key.D1;
                if (index < _rows.Count)
                {
                    Commit(_rows[index].Item);
                }

                e.Handled = true;
                break;
        }
    }
}
