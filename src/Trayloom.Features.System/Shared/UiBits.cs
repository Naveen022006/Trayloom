// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools;

/// <summary>Segoe Fluent Icons code points used by this project (spec 01 1.4 / 02a 0.5).</summary>
internal static class Gl
{
    public const string Moon = "";
    public const string Display = "";
    public const string Laptop = "";
    public const string Speaker = "";
    public const string SpeakerMute = "";
    public const string Bell = "";
    public const string Mic = "";
    public const string MicOff = "";
    public const string Pin = "";
    public const string PinOff = "";
    public const string More = "";
    public const string Reset = "";
    public const string Power = "";
    public const string Bolt = "";
    public const string Cpu = "";
    public const string Gpu = "";
    public const string Memory = "";
    public const string Network = "";
    public const string Disk = "";
    public const string Usb = "";
    public const string Clock = "";
    public const string Chevron = "";
    public const string ChevronDown = "";
    public const string ChevronSmall = "";
    public const string Check = "";
    public const string CheckCircle = "";
    public const string Circle = "";
    public const string Handle = "";
    public const string Warning = "";
    public const string Lock = "";
    public const string AllApps = "";
    public const string Play = "";
    public const string PlayCircle = "";
    public const string Timer = "";
    public const string Sun = "";
    public const string Keyboard = "";
    public const string Heart = "";
    public const string Battery = "";
    public const string BatteryLow = "";
    public const string BatteryCharging = "";
    public const string Plug = "";
    public const string Close = "";
    public const string CloseCircle = "";
    public const string Add = "";
    public const string Eject = "";
    public const string Folder = "";
    public const string OpenWindow = "";
    public const string Gauge = "";
    public const string Thermo = "";
    public const string Bluetooth = "";
    public const string Cursor = "";
    public const string List = "";
    public const string Equalizer = "";
    public const string PlayPause = "";
    public const string Dim = "";
    public const string Gear = "";
    public const string Globe = "";
    public const string Wifi = "";
    public const string Down = "";
    public const string Up = "";
    public const string Eye = "";
    public const string EyeOff = "";
}

/// <summary>Small view helpers shared by the panel and settings code of this project.</summary>
internal static class Bits
{
    /// <summary>Test hook: open every disclosure when it is created (used by the screenshot harness).</summary>
    public static bool ForceExpanded { get; set; }

    /// <summary>A flat icon button (hover tint, no border).</summary>
    public static Button FlatIcon(string glyph, string? tip, Action click, double size = 11, double box = 22, string brushKey = ThemeKeys.TextSecondary, FontWeight? weight = null)
    {
        var b = new Button { Content = UiKit.Icon(glyph, size, brushKey, weight ?? FontWeights.SemiBold), Width = box, Height = box, Padding = new Thickness(0), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
        b.Click += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    /// <summary>Text button without border (hover tint).</summary>
    public static Button FlatText(string text, Action click, double size = 11, string brushKey = ThemeKeys.Accent)
    {
        var tb = UiKit.Text(text, size, FontWeights.Medium, brushKey);
        var b = new Button { Content = tb, Padding = new Thickness(6, 2, 6, 2) };
        b.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A header row (title + rotating chevron) that shows or hides <paramref name="content"/> (spec DisclosureHeaderRow).</summary>
    public static StackPanel Disclosure(string title, Func<UIElement> content, bool expanded = false, double indent = 19, double fontSize = 11.5, Action<bool>? changed = null, UIElement? leading = null, UIElement? trailing = null, double chevronSize = 9)
    {
        var root = new StackPanel();
        var body = new Border { Margin = new Thickness(indent, 8, 0, 0), Visibility = Visibility.Collapsed };
        var chevron = UiKit.Icon(Gl.Chevron, chevronSize, ThemeKeys.TextSecondary, FontWeights.Bold);
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        chevron.RenderTransform = new RotateTransform(0);
        var head = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var col = 0;
        if (leading is not null)
        {
            if (leading is FrameworkElement lf)
            {
                lf.Margin = new Thickness(0, 0, 7, 0);
            }

            Grid.SetColumn(leading, col);
            head.Children.Add(leading);
        }

        var label = UiKit.Text(title, fontSize, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        head.Children.Add(label);
        if (trailing is not null)
        {
            if (trailing is FrameworkElement tf)
            {
                tf.Margin = new Thickness(0, 0, 8, 0);
                tf.VerticalAlignment = VerticalAlignment.Center;
            }

            Grid.SetColumn(trailing, 2);
            head.Children.Add(trailing);
        }

        chevron.Margin = new Thickness(6, 0, 2, 0);
        Grid.SetColumn(chevron, 3);
        head.Children.Add(chevron);

        var open = false;
        void Apply(bool value, bool notify)
        {
            open = value;
            if (open && body.Child is null)
            {
                body.Child = content();
            }

            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ((RotateTransform)chevron.RenderTransform).Angle = open ? 90 : 0;
            if (notify)
            {
                changed?.Invoke(open);
            }
        }

        head.MouseLeftButtonUp += (_, _) => Apply(!open, true);
        root.Children.Add(head);
        root.Children.Add(body);
        if (expanded || ForceExpanded)
        {
            Apply(true, false);
        }

        return root;
    }

    /// <summary>One option row: title (+ optional caption) on the left and a switch on the right.</summary>
    public static Grid ToggleLine(string title, string? caption, bool isOn, Action<bool> onToggled, SwitchSize size = SwitchSize.Mini, double titleSize = 11, string? glyph = null, bool captionBelow = true)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (glyph is not null)
        {
            var icon = UiKit.Icon(glyph, 11, ThemeKeys.TextSecondary);
            icon.Width = 16;
            icon.Margin = new Thickness(0, 0, 7, 0);
            grid.Children.Add(icon);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(title, titleSize, FontWeights.Medium, ThemeKeys.TextPrimary, wrap: true));
        if (!string.IsNullOrEmpty(caption) && captionBelow)
        {
            var c = UiKit.Text(caption, 9.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            c.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(c);
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var sw = new MacSwitch(isOn, size, onToggled) { Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(sw, 2);
        grid.Children.Add(sw);
        if (!string.IsNullOrEmpty(caption) && !captionBelow)
        {
            grid.ToolTip = caption;
        }

        return grid;
    }

    /// <summary>A compact check box (the stock Fluent box is 32 high): scaled to 0.8 so list rows stay tight.</summary>
    public static CheckBox Check(bool isOn, double fontSize = 11.5, FontWeight? weight = null)
    {
        var box = new CheckBox { IsChecked = isOn, VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 0, MinWidth = 0, Padding = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        box.LayoutTransform = new ScaleTransform(0.8, 0.8);
        _ = fontSize;
        _ = weight;
        return box;
    }

    /// <summary>A check box line (title 11.5 medium + caption 9.5) used by the option lists of the mixer.</summary>
    public static StackPanel CheckLine(string title, string? caption, bool isOn, Action<bool> onToggled, double titleSize = 11.5)
    {
        var box = Check(isOn);
        var label = UiKit.Text(title, titleSize / 0.8, FontWeights.Medium, ThemeKeys.TextPrimary, wrap: false);
        label.Margin = new Thickness(0, 0, 0, 0);
        box.Content = label;
        box.Checked += (_, _) => onToggled(true);
        box.Unchecked += (_, _) => onToggled(false);
        var stack = new StackPanel();
        stack.Children.Add(box);
        if (!string.IsNullOrEmpty(caption))
        {
            var c = UiKit.Text(caption, 9.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            c.Margin = new Thickness(22, 0, 0, 0);
            stack.Children.Add(c);
        }

        return stack;
    }

    /// <summary>A compact drop-down.</summary>
    public static ComboBox MiniCombo(IReadOnlyList<string> items, int selected, Action<int> onChanged, double width = 112, double fontSize = 11)
    {
        var combo = new ComboBox { ItemsSource = items.ToList(), Width = width, FontSize = fontSize, MinHeight = 22, Padding = new Thickness(6, 1, 4, 1), VerticalAlignment = VerticalAlignment.Center };
        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                onChanged(combo.SelectedIndex);
            }
        };
        return combo;
    }

    /// <summary>A mini bordered button.</summary>
    public static Button Mini(string text, Action click, string? glyph = null) => PanelButtons.Mini(text, glyph, click);

    /// <summary>Wraps children in a horizontal wrap row with uniform spacing.</summary>
    public static WrapPanel Wrap(double hGap, double vGap, params UIElement[] children)
    {
        var panel = new WrapPanel();
        foreach (var child in children)
        {
            if (child is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, 0, hGap, vGap);
            }

            panel.Children.Add(child);
        }

        return panel;
    }

    /// <summary>Muted caption.</summary>
    public static TextBlock Caption(string text, double size = 10, string key = ThemeKeys.TextSecondary) => UiKit.Text(text, size, FontWeights.Normal, key, wrap: true);

    /// <summary>A pill/capsule with a coloured dot and label (memory pressure).</summary>
    public static Border Pill(string text, string colorKey)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, colorKey);
        row.Children.Add(dot);
        var label = UiKit.Text(text, 11, FontWeights.Medium, colorKey);
        label.Margin = new Thickness(5, 0, 0, 0);
        row.Children.Add(label);
        var border = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(8, 3, 8, 3), Child = row };
        border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
        return border;
    }

    /// <summary>Raises <paramref name="show"/>(true) while the element is loaded and visible, (false) otherwise.</summary>
    public static void WhileVisible(FrameworkElement element, Action<bool> show)
    {
        var state = false;
        void Update()
        {
            var now = element.IsLoaded && element.IsVisible;
            if (now != state)
            {
                state = now;
                show(now);
            }
        }

        element.Loaded += (_, _) => Update();
        element.Unloaded += (_, _) => Update();
        element.IsVisibleChanged += (_, _) => Update();
        if (element.IsLoaded)
        {
            Update();
        }
    }

    /// <summary>Formats a number with the invariant culture.</summary>
    public static string F(double value, string format = "0") => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Drag-to-reorder for a stack whose rows each have a handle element.</summary>
    public static void AttachReorder(StackPanel container, FrameworkElement row, FrameworkElement handle, Action<IReadOnlyList<FrameworkElement>> onDone)
    {
        handle.Cursor = Cursors.SizeAll;
        handle.MouseLeftButtonDown += (_, e) =>
        {
            handle.CaptureMouse();
            row.Opacity = 0.45;
            e.Handled = true;
        };
        handle.MouseMove += (_, e) =>
        {
            if (!handle.IsMouseCaptured)
            {
                return;
            }

            var y = e.GetPosition(container).Y;
            var current = container.Children.IndexOf(row);
            var target = current;
            var top = 0.0;
            for (var i = 0; i < container.Children.Count; i++)
            {
                var child = (FrameworkElement)container.Children[i];
                var bottom = top + child.ActualHeight + child.Margin.Top + child.Margin.Bottom;
                if (y >= top && y < bottom)
                {
                    target = i;
                    break;
                }

                top = bottom;
            }

            if (target != current)
            {
                container.Children.RemoveAt(current);
                container.Children.Insert(target, row);
            }
        };
        handle.MouseLeftButtonUp += (_, _) =>
        {
            if (!handle.IsMouseCaptured)
            {
                return;
            }

            handle.ReleaseMouseCapture();
            row.Opacity = 1;
            onDone([.. container.Children.Cast<FrameworkElement>()]);
        };
    }
}

/// <summary>A percent read-out that turns into a text box on click (Return commits, Esc cancels, blur commits).</summary>
internal sealed class EditablePercent : Grid
{
    private readonly TextBlock _label;
    private readonly TextBox _box;
    private readonly Func<double> _max;
    private readonly Action<int> _commit;
    private bool _editing;

    public EditablePercent(double width, Func<double> max, Action<int> commit)
    {
        _max = max;
        _commit = commit;
        Width = width;
        Height = 18;
        _label = UiKit.Text("0%", 10.5, FontWeights.Medium, ThemeKeys.TextSecondary);
        _label.FontFamily = UiKit.DisplayFont;
        _label.HorizontalAlignment = HorizontalAlignment.Right;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.Cursor = Cursors.IBeam;
        _box = new TextBox { Visibility = Visibility.Collapsed, FontSize = 10.5, Padding = new Thickness(0, 0, 2, 0), TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(0.7), Height = 18, MinHeight = 18 };
        _box.SetResourceReference(Control.BorderBrushProperty, ThemeKeys.Accent);
        _box.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        _box.SetResourceReference(Control.BackgroundProperty, ThemeKeys.ControlFill);
        Children.Add(_label);
        Children.Add(_box);
        _label.MouseLeftButtonUp += (_, _) => BeginEdit();
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                EndEdit();
                e.Handled = true;
            }
        };
        _box.LostKeyboardFocus += (_, _) =>
        {
            if (_editing)
            {
                Commit();
            }
        };
    }

    /// <summary>The displayed text (e.g. "62%").</summary>
    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>Tints the label (theme key).</summary>
    public void SetBrush(string key) => _label.SetResourceReference(TextBlock.ForegroundProperty, key);

    private void BeginEdit()
    {
        _editing = true;
        _box.Text = _label.Text.TrimEnd('%');
        _label.Visibility = Visibility.Collapsed;
        _box.Visibility = Visibility.Visible;
        _box.Focus();
        _box.SelectAll();
    }

    private void EndEdit()
    {
        _editing = false;
        _box.Visibility = Visibility.Collapsed;
        _label.Visibility = Visibility.Visible;
    }

    private void Commit()
    {
        var text = _box.Text.Trim().TrimEnd('%').Trim().Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0)
        {
            var v = (int)Math.Round(Math.Min(value, _max()));
            EndEdit();
            _commit(v);
        }
        else
        {
            System.Media.SystemSounds.Beep.Play();
            EndEdit();
        }
    }
}

/// <summary>Number formatting shared by the monitor views.</summary>
internal static class Fmt
{
    public static string N(double v, string format) => v.ToString(format, CultureInfo.CurrentCulture);

    public static string Cur(FormattableString s) => s.ToString(CultureInfo.CurrentCulture);

    public static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    public static string Percent(double v) => Cur($"{v:0}%");

    /// <summary>Binary byte count with one or two decimals (11.43 GB).</summary>
    public static string BytesBinary(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1)
        {
            bytes /= 1024;
            i++;
        }

        return N(bytes, i >= 3 ? "0.##" : i == 0 ? "0" : "0.#") + " " + units[i];
    }

    /// <summary>Decimal (1000) byte count for disks.</summary>
    public static string BytesDecimal(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (bytes >= 1000 && i < units.Length - 1)
        {
            bytes /= 1000;
            i++;
        }

        return N(bytes, i <= 1 ? "0" : "0.#") + " " + units[i];
    }

    /// <summary>Network rate in B/s or bit/s units for the panel ("43 KB/s", "12 Mbps").</summary>
    public static string Rate(double bytesPerSecond, bool bits)
    {
        if (bits)
        {
            var v = bytesPerSecond * 8;
            string[] u = ["bps", "Kbps", "Mbps", "Gbps"];
            var i = 0;
            while (v >= 1000 && i < u.Length - 1)
            {
                v /= 1000;
                i++;
            }

            return N(v, i > 0 && v < 10 ? "0.0" : "0") + " " + u[i];
        }

        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        var j = 0;
        var b = bytesPerSecond;
        while (b >= 1024 && j < units.Length - 1)
        {
            b /= 1024;
            j++;
        }

        return N(b, j > 0 && b < 10 ? "0.0" : "0") + " " + units[j];
    }

    /// <summary>Compact tray rate ("0B", "320K", "1.2M", "10M"; bits "0b", "320Kb", "9.6Mb").</summary>
    public static string RateCompact(double bytesPerSecond, bool bits)
    {
        var v = bits ? bytesPerSecond * 8 : bytesPerSecond;
        var div = bits ? 1000.0 : 1024.0;
        string[] u = bits ? ["b", "Kb", "Mb", "Gb"] : ["B", "K", "M", "G"];
        var i = 0;
        while (v >= div && i < u.Length - 1)
        {
            v /= div;
            i++;
        }

        return v.ToString(i == 0 || v >= 10 ? "0" : "0.0", CultureInfo.InvariantCulture) + u[i];
    }

    /// <summary>Time like "1h 25m".</summary>
    public static string HoursMinutes(int seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)span.TotalHours}h {span.Minutes}m";
    }

    /// <summary>Uptime like "Up for 1h 11min" parts: "Nd Nh" / "Nh Nmin" / "Nmin".</summary>
    public static string Uptime(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}min";
        }

        return $"{Math.Max(0, (int)span.TotalMinutes)}min";
    }

    /// <summary>Temperature with unit.</summary>
    public static string Temperature(double celsius, bool fahrenheit, bool spaced = true)
    {
        var v = fahrenheit ? (celsius * 9 / 5) + 32 : celsius;
        var unit = fahrenheit ? "°F" : "°C";
        return N(v, "0") + (spaced ? " " : string.Empty) + unit;
    }
}
