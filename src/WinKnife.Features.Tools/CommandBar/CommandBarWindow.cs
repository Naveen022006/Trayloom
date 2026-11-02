// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.Tools;

/// <summary>One selectable row of the command bar.</summary>
internal sealed record BarItem(string Key, string Title, string Subtitle, string Kind, string? Glyph, string? AppPath, Action Run);

/// <summary>The floating search window of the command bar.</summary>
internal sealed class CommandBarWindow : Window
{
    private const int MaxRows = 8;
    private readonly TextBox _input = new();
    private readonly ListBox _list = new();
    private readonly Func<string, IReadOnlyList<BarItem>> _search;
    private readonly Func<string, ImageSource?> _iconFor;
    private readonly Action<BarItem> _run;
    private IReadOnlyList<BarItem> _results = Array.Empty<BarItem>();
    private bool _closing;

    public CommandBarWindow(Func<string, IReadOnlyList<BarItem>> search, Func<string, ImageSource?> iconFor, Action<BarItem> run)
    {
        _search = search;
        _iconFor = iconFor;
        _run = run;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        Title = "Command bar";
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;

        var magnifier = UiKit.Glyph("", 18, ThemeKeys.TextMuted);
        magnifier.Margin = new Thickness(16, 0, 10, 0);
        _input.BorderThickness = new Thickness(0);
        _input.Background = Brushes.Transparent;
        _input.FontSize = 20;
        _input.VerticalContentAlignment = VerticalAlignment.Center;
        _input.Padding = new Thickness(0, 12, 12, 12);
        _input.SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        _input.CaretBrush = null;
        _input.TextChanged += (_, _) => Refresh();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(magnifier);
        Grid.SetColumn(_input, 1);
        header.Children.Add(_input);

        _list.BorderThickness = new Thickness(0, 1, 0, 0);
        _list.SetResourceReference(Control.BorderBrushProperty, ThemeKeys.Border);
        _list.Background = Brushes.Transparent;
        _list.Padding = new Thickness(6);
        _list.MaxHeight = MaxRows * 52 + 14;
        _list.Visibility = Visibility.Collapsed;
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.MouseDoubleClick += (_, _) => RunSelected();
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_list, (DependencyObject)e.OriginalSource) is ListBoxItem)
            {
                RunSelected();
            }
        };

        var stack = new StackPanel { Children = { header, _list } };
        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(18),
            Child = stack,
            Effect = null,
        };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.WindowBackground);
        card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);
        var shadowHost = new Border
        {
            Child = card,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35, Color = Colors.Black },
        };
        Content = shadowHost;

        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) =>
        {
            if (!_closing && IsVisible)
            {
                Hide();
            }
        };
    }

    public void Present()
    {
        _closing = false;
        _input.Text = string.Empty;
        Refresh();

        var handle = new WindowInteropHelper(this).EnsureHandle();
        var area = Screens.UnderCursor();

        // Move onto the target monitor first so WPF picks up that monitor's DPI, then centre using that DPI.
        NativeMethods.SetWindowPos(handle, HWND_TOPMOST, (int)area.WorkArea.X, (int)area.WorkArea.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        Opacity = 0;
        Show();
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var widthPx = (int)Math.Round((Width) * scale);
        var x = (int)(area.WorkArea.X + (area.WorkArea.Width - widthPx) / 2);
        var y = (int)(area.WorkArea.Y + area.WorkArea.Height * 0.16);
        NativeMethods.SetWindowPos(handle, HWND_TOPMOST, x, y, widthPx, (int)Math.Round(ActualHeight * scale), SWP_NOACTIVATE);
        Opacity = 1;
        Activate();
        NativeMethods.SetForegroundWindow(handle);
        _input.Focus();
        Keyboard.Focus(_input);
    }

    public void Shutdown()
    {
        _closing = true;
        Close();
    }

    private void Refresh()
    {
        _results = _search(_input.Text.Trim());
        _list.Items.Clear();
        foreach (var item in _results.Take(MaxRows * 3))
        {
            _list.Items.Add(new ListBoxItem { Content = BuildRow(item), Padding = new Thickness(8, 6, 8, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }

        _list.Visibility = _results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private UIElement BuildRow(BarItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement icon;
        var source = item.AppPath is null ? null : _iconFor(item.AppPath);
        if (source is not null)
        {
            icon = new Image { Source = source, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
        }
        else
        {
            icon = UiKit.Glyph(item.Glyph ?? "", 18);
            ((FrameworkElement)icon).HorizontalAlignment = HorizontalAlignment.Left;
        }

        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Label(item.Title, null, 14));
        if (item.Subtitle.Length > 0)
        {
            text.Children.Add(UiKit.Label(item.Subtitle, ThemeKeys.TextMuted, 11));
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var kind = UiKit.Label(item.Kind, ThemeKeys.TextMuted, 11);
        kind.Margin = new Thickness(10, 0, 4, 0);
        Grid.SetColumn(kind, 2);
        grid.Children.Add(kind);
        return grid;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Hide();
                break;
            case Key.Down:
                e.Handled = true;
                Move(1);
                break;
            case Key.Up:
                e.Handled = true;
                Move(-1);
                break;
            case Key.Enter:
                e.Handled = true;
                RunSelected();
                break;
            case Key.Tab:
                e.Handled = true;
                Move(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                break;
        }
    }

    private void Move(int delta)
    {
        if (_list.Items.Count == 0)
        {
            return;
        }

        var next = (_list.SelectedIndex + delta + _list.Items.Count) % _list.Items.Count;
        _list.SelectedIndex = next;
        _list.ScrollIntoView(_list.SelectedItem);
    }

    private void RunSelected()
    {
        var index = _list.SelectedIndex;
        if (index < 0 || index >= _results.Count)
        {
            return;
        }

        var item = _results[index];
        Hide();
        _run(item);
    }
}
