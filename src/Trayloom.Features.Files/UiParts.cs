// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trayloom.Core;
using Trayloom.Core.Ui;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;

namespace Trayloom.Features.Files;

/// <summary>Segoe Fluent Icons code points used by this project.</summary>
internal static class G
{
    public const string Pin = "";
    public const string PinFill = "";
    public const string Copy = "";
    public const string More = "";
    public const string Edit = "";
    public const string Delete = "";
    public const string Eye = "";
    public const string Sidebar = "";
    public const string Close = "";
    public const string CloseCircle = "";
    public const string CheckCircle = "";
    public const string Circle = "";
    public const string Clipboard = "";
    public const string Document = "";
    public const string Photo = "";
    public const string Folder = "";
    public const string Search = "";
    public const string Link = "";
    public const string Scissors = "";
    public const string Tray = "";
    public const string TrayDownload = "";
    public const string Share = "";
    public const string Warning = "";
    public const string Check = "";
    public const string Minus = "";
    public const string Plus = "";
    public const string ChevronRight = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string Shield = "";
    public const string Forward = "";
    public const string Open = "";
    public const string Up = "";
    public const string Down = "";
}

/// <summary>Small themed building blocks shared by the settings, panel and floating windows.</summary>
internal static class Ui
{
    /// <summary>A flat icon button (rounded 6, fill primary 7 %) of the given size.</summary>
    public static Border IconButton(string glyph, string? tooltip, Action onClick, double size = 24, double glyphSize = 10.5, double radius = 6, bool filled = true)
    {
        var button = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            ToolTip = tooltip,
            Child = UiKit.Icon(glyph, glyphSize, ThemeKeys.TextPrimary, FontWeights.SemiBold),
        };
        if (filled)
        {
            button.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
        }

        button.MouseEnter += (_, _) => button.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        button.MouseLeave += (_, _) =>
        {
            if (filled)
            {
                button.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
            }
            else
            {
                button.Background = Brushes.Transparent;
            }
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        button.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return button;
    }

    /// <summary>A text-only close style button (xmark) used in window toolbars.</summary>
    public static Border CloseButton(Action onClick, string? tooltip = null, double size = 22) =>
        IconButton(G.Close, tooltip ?? Strings.Clipboard.Close, onClick, size, 10.5, 6, filled: false);

    /// <summary>Opens a styled menu with the given items (null entries are separators) under <paramref name="anchor"/>.</summary>
    public static ContextMenu Menu(params (string Title, Action? Click, bool Enabled, bool Destructive)?[] items)
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(Control.BackgroundProperty, ThemeKeys.PanelBase);
        menu.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        menu.SetResourceReference(Control.BorderBrushProperty, ThemeKeys.CardBorder);
        foreach (var item in items)
        {
            if (item is null)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var (title, click, enabled, destructive) = item.Value;
            var menuItem = new MenuItem { Header = title, IsEnabled = enabled };
            if (destructive)
            {
                menuItem.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Red);
            }

            if (click is not null)
            {
                menuItem.Click += (_, _) => click();
            }

            menu.Items.Add(menuItem);
        }

        return menu;
    }

    /// <summary>A bordered rounded text box.</summary>
    public static TextBox TextBox(string? placeholderTooltip = null, double fontSize = 12)
    {
        var box = new TextBox { FontSize = fontSize };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        box.ToolTip = placeholderTooltip;
        return box;
    }

    /// <summary>A text box with a placeholder hint drawn while it is empty and unfocused-or-empty.</summary>
    public static Grid PlaceholderBox(out TextBox box, string placeholder, double fontSize = 12)
    {
        var grid = new Grid();
        box = TextBox(null, fontSize);
        var hint = UiKit.Text(placeholder, fontSize, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(11, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        var captured = box;
        captured.TextChanged += (_, _) => hint.Visibility = captured.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        grid.Children.Add(box);
        grid.Children.Add(hint);
        return grid;
    }

    /// <summary>A numeric text box (digits only, right aligned) that reports clamped values on Enter / focus loss.</summary>
    public static TextBox NumberBox(int value, int min, int max, double width, Action<int> onChanged, bool enabled = true)
    {
        var box = TextBox(null, 12.5);
        box.Width = width;
        box.TextAlignment = TextAlignment.Right;
        box.Text = Math.Clamp(value, min, max).ToString();
        box.IsEnabled = enabled;
        box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        void Commit()
        {
            var parsed = int.TryParse(box.Text, out var v) ? Math.Clamp(v, min, max) : Math.Clamp(value, min, max);
            box.Text = parsed.ToString();
            onChanged(parsed);
        }

        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                Keyboard.ClearFocus();
            }
        };
        return box;
    }

    /// <summary>Green "checkmark + text" status label.</summary>
    public static StackPanel ActiveLabel(string text, double size = 11.5)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = UiKit.Icon(G.CheckCircle, size, ThemeKeys.Green);
        icon.Margin = new Thickness(0, 0, 5, 0);
        row.Children.Add(icon);
        row.Children.Add(UiKit.Text(text, size, FontWeights.Medium, ThemeKeys.Green));
        return row;
    }

    /// <summary>Orange caption text (warnings).</summary>
    public static TextBlock Warning(string text, double size = 11.5) => UiKit.Text(text, size, FontWeights.Normal, ThemeKeys.Orange, wrap: true);

    /// <summary>Secondary caption text.</summary>
    public static TextBlock Caption(string text, double size = 11.5, double left = 0)
    {
        var block = UiKit.Text(text, size, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        block.Margin = new Thickness(left, 0, 0, 0);
        return block;
    }

    /// <summary>A bitmap of a stored image limited to a size for list thumbnails.</summary>
    public static Image ImageView(ImageSource? source, double maxWidth, double maxHeight, double radius = 6)
    {
        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Uniform,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    /// <summary>Reads a value from <see cref="IAppSettings"/> with a fallback for a key that may be unset.</summary>
    public static T Setting<T>(IAppSettings app, string key, T fallback) => app.Get(key, fallback);

    /// <summary>Opens a path in Explorer (selecting it when it is a file).</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }

    /// <summary>A frozen brush of a theme colour (for drawing code that cannot bind resources).</summary>
    public static Brush Brush(string key) => UiKit.BrushOf(key);

    public static Color ColorOf(string key) => UiKit.BrushOf(key) is SolidColorBrush solid ? solid.Color : Colors.Gray;
}

/// <summary>A themed check box: a 16 px rounded square (accent when checked) followed by a label.</summary>
internal sealed class CheckBoxLite : Border
{
    private readonly Border _box;
    private readonly TextBlock _check;
    private bool _checked;

    public CheckBoxLite(string label, bool isChecked, Action<bool> onChanged, double fontSize = 12, string? tooltip = null)
    {
        _checked = isChecked;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        ToolTip = tooltip;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        _check = UiKit.Icon(G.Check, 10, ThemeKeys.OnAccent, FontWeights.Bold);
        _box = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Child = _check,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = UiKit.Text(label, fontSize, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        text.Margin = new Thickness(8, 0, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(_box);
        row.Children.Add(text);
        Child = row;
        Apply();
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _checked = !_checked;
            Apply();
            onChanged(_checked);
        };
    }

    public bool IsChecked
    {
        get => _checked;
        set
        {
            _checked = value;
            Apply();
        }
    }

    private void Apply()
    {
        _check.Visibility = _checked ? Visibility.Visible : Visibility.Hidden;
        _box.SetResourceReference(Border.BackgroundProperty, _checked ? ThemeKeys.Accent : ThemeKeys.ControlFill);
        _box.SetResourceReference(Border.BorderBrushProperty, _checked ? ThemeKeys.Accent : ThemeKeys.Divider);
    }
}

/// <summary>Async thumbnail cache (decoded off the UI thread, small LRU).</summary>
internal static class Thumbs
{
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> Order = new();

    public static void Load(string path, int width, Action<BitmapSource?> done)
    {
        var key = path + "|" + width;
        if (Cache.TryGetValue(key, out var hit))
        {
            done(hit);
            return;
        }

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        Task.Run(() => ClipImages.Load(path, width)).ContinueWith(t =>
        {
            dispatcher.BeginInvoke(() =>
            {
                var image = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                if (image is not null)
                {
                    Cache[key] = image;
                    Order.Enqueue(key);
                    while (Order.Count > 160)
                    {
                        Cache.Remove(Order.Dequeue());
                    }
                }

                done(image);
            });
        });
    }
}

/// <summary>Resolves app identities (executable image names) to display names, icons and paths.</summary>
internal static class AppIdentity
{
    private static readonly Dictionary<string, (string Name, string? Path)> Known = new(StringComparer.OrdinalIgnoreCase);

    public static void Remember(string identity, string name, string? path) => Known[Lists.Key(identity)] = (name, path);

    public static (string Name, string? Path) Resolve(string identity)
    {
        var key = Lists.Key(identity);
        if (Known.TryGetValue(key, out var hit))
        {
            return hit;
        }

        string? path = File.Exists(identity) ? identity : null;
        if (path is null)
        {
            try
            {
                var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(key)).FirstOrDefault();
                if (running is not null)
                {
                    path = WinApi.GetProcessPath((uint)running.Id);
                }
            }
            catch (Exception)
            {
                // best effort
            }
        }

        var name = Path.GetFileNameWithoutExtension(identity);
        if (path is not null)
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    name = description;
                }
            }
            catch (Exception)
            {
                // keep the file name
            }
        }

        Known[key] = (name, path);
        return Known[key];
    }
}
