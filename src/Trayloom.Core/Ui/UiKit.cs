// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Trayloom.Core.Ui;

/// <summary>Segoe Fluent Icons code points used by the shell (and handy for feature UIs).</summary>
public static class Glyphs
{
    public const string Settings = "";
    public const string Check = "";
    public const string CheckCircle = "";
    public const string ChevronRight = "";
    public const string ChevronLeft = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string ChevronRightSmall = "";
    public const string ChevronDownSmall = "";
    public const string Close = "";
    public const string CloseCircle = "";
    public const string Search = "";
    public const string Info = "";
    public const string Power = "";
    public const string Edit = "";
    public const string Eye = "";
    public const string EyeOff = "";
    public const string Handle = "";
    public const string Reset = "";
    public const string Refresh = "";
    public const string Trash = "";
    public const string Warning = "";
    public const string Play = "";
    public const string Pin = "";
    public const string Add = "";
    public const string More = "";
    public const string OpenWindow = "";
    public const string SidebarToggle = "";
    public const string Back = "";
    public const string Forward = "";
    public const string Heart = "";
    public const string Star = "";
    public const string Home = "";
    public const string Lock = "";
    public const string Link = "";
    public const string Globe = "";
    public const string Keyboard = "";
    public const string Brand = "";
}

/// <summary>Which look <see cref="UiKit.Button"/> creates.</summary>
public enum ButtonKind
{
    /// <summary>Flat grey button (SwiftUI <c>.bordered</c>).</summary>
    Bordered,

    /// <summary>Accent button (SwiftUI <c>.borderedProminent</c>).</summary>
    Prominent,

    /// <summary>Text-only button with a hover tint.</summary>
    Plain,

    /// <summary>Red prominent button.</summary>
    Destructive,
}

/// <summary>Small factory helpers so views stay short and use the theme resources.</summary>
public static class UiKit
{
    /// <summary>The icon font family.</summary>
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>The text font family.</summary>
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    /// <summary>The display font family (rounded digits in Vorssaint).</summary>
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");

    /// <summary>Binds a dependency property to a theme resource key (re-evaluated when the theme changes).</summary>
    public static T Themed<T>(this T element, DependencyProperty property, string resourceKey) where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }

    /// <summary>Same as <see cref="Themed{T}"/> for content elements (Run etc.).</summary>
    public static T ThemedContent<T>(this T element, DependencyProperty property, string resourceKey) where T : FrameworkContentElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }

    /// <summary>Looks up a theme brush now (for code that cannot use a resource reference).</summary>
    public static Brush BrushOf(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    /// <summary>Creates a themed text block.</summary>
    public static TextBlock Text(string text, double size = 13, FontWeight? weight = null, string brushKey = ThemeKeys.TextPrimary, bool wrap = false, TextTrimming trimming = TextTrimming.None)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = trimming,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return GlyphBlock.Realize(block);
    }

    /// <summary>Creates a themed glyph (Segoe Fluent Icons).</summary>
    public static TextBlock Icon(string glyph, double size = 13, string brushKey = ThemeKeys.Accent, FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            Text = glyph,
            FontFamily = IconFont,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return block;
    }

    /// <summary>A rounded square with a glyph on an accent 12 % fill (the 26 px settings row tile / 34 px preset tile).</summary>
    public static Border IconTile(string glyph, double size = 26, double glyphSize = 12, double radius = 7, string tintKey = ThemeKeys.Accent, string fillKey = ThemeKeys.AccentFill12)
    {
        var tile = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Icon(glyph, glyphSize, tintKey, FontWeights.SemiBold),
        };
        tile.SetResourceReference(Border.BackgroundProperty, fillKey);
        return tile;
    }

    /// <summary>A 1 px horizontal divider (insets in DIPs).</summary>
    public static Border Divider(double left = 0, double right = 0, string brushKey = ThemeKeys.Divider)
    {
        var line = new Border { Height = 1, Margin = new Thickness(left, 0, right, 0), SnapsToDevicePixels = true };
        line.SetResourceReference(Border.BackgroundProperty, brushKey);
        return line;
    }

    /// <summary>Creates a button with the shared styles. <paramref name="glyph"/> adds a leading icon.</summary>
    public static Button Button(string text, ButtonKind kind = ButtonKind.Bordered, double fontSize = 12, string? glyph = null, Action? onClick = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icon(glyph, fontSize + 1, kind == ButtonKind.Bordered || kind == ButtonKind.Plain ? ThemeKeys.TextPrimary : ThemeKeys.OnAccent);
            icon.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = row;
        }

        var button = new Button { Content = content, FontSize = fontSize };
        button.SetResourceReference(FrameworkElement.StyleProperty, kind switch
        {
            ButtonKind.Prominent => ThemeKeys.ButtonProminent,
            ButtonKind.Plain => ThemeKeys.ButtonPlain,
            ButtonKind.Destructive => ThemeKeys.ButtonDestructive,
            _ => ThemeKeys.ButtonBordered,
        });
        if (onClick is not null)
        {
            button.Click += (_, _) => onClick();
        }

        return button;
    }

    /// <summary>A vertical stack with uniform spacing between children.</summary>
    public static StackPanel VStack(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is FrameworkElement fe && i > 0)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + spacing, fe.Margin.Right, fe.Margin.Bottom);
            }

            panel.Children.Add(children[i]);
        }

        return panel;
    }

    /// <summary>Adds a child to a stack, inserting <paramref name="spacing"/> above it when it is not the first.</summary>
    public static void AddSpaced(this StackPanel panel, UIElement child, double spacing)
    {
        if (panel.Children.Count > 0 && child is FrameworkElement fe)
        {
            fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + spacing, fe.Margin.Right, fe.Margin.Bottom);
        }

        panel.Children.Add(child);
    }

    /// <summary>
    /// Makes a border-based "button" behave like one: reachable with Tab, activated by Space or Enter, shows the focus ring, and
    /// fires only on a release that follows a press on the same element (a drag that ends here is not a click).
    /// </summary>
    public static T Clickable<T>(this T element, Action onClick, string? name = null) where T : FrameworkElement
    {
        element.Focusable = true;
        KeyboardNavigation.SetIsTabStop(element, true);
        element.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, ThemeKeys.FocusRing);
        if (element.Cursor is null)
        {
            element.Cursor = Cursors.Hand;
        }

        if (name is not null)
        {
            System.Windows.Automation.AutomationProperties.SetName(element, name);
        }

        var pressed = false;
        element.MouseLeftButtonDown += (_, _) => pressed = true;
        element.MouseLeave += (_, _) => pressed = false;
        element.MouseLeftButtonUp += (_, args) =>
        {
            if (pressed)
            {
                pressed = false;
                args.Handled = true;
                onClick();
            }
        };
        element.KeyDown += (_, args) =>
        {
            if (args.Key is Key.Space or Key.Enter)
            {
                args.Handled = true;
                onClick();
            }
        };
        return element;
    }

    /// <summary>
    /// The child-index path from <paramref name="root"/> down to the element that has keyboard focus, or null when focus is
    /// elsewhere. Pair with <see cref="RestoreFocus"/> to keep focus across a rebuild of the visual tree.
    /// </summary>
    public static List<int>? FocusPath(Visual root)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
        {
            return null;
        }

        var path = new List<int>();
        DependencyObject? node = focused;
        while (node is not null && !ReferenceEquals(node, root))
        {
            if (node is not Visual && node is not System.Windows.Media.Media3D.Visual3D)
            {
                return null;
            }

            var parent = VisualTreeHelper.GetParent(node);
            if (parent is null)
            {
                return null;
            }

            var count = VisualTreeHelper.GetChildrenCount(parent);
            var index = -1;
            for (var i = 0; i < count; i++)
            {
                if (ReferenceEquals(VisualTreeHelper.GetChild(parent, i), node))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return null;
            }

            path.Add(index);
            node = parent;
        }

        if (node is null)
        {
            return null;
        }

        path.Reverse();
        return path;
    }

    /// <summary>Walks <paramref name="path"/> (from <see cref="FocusPath"/>) in a rebuilt tree and focuses the element it ends on, when that is still focusable, visible and enabled.</summary>
    public static void RestoreFocus(Visual root, List<int> path)
    {
        DependencyObject node = root;
        foreach (var index in path)
        {
            if (index >= VisualTreeHelper.GetChildrenCount(node))
            {
                return;
            }

            node = VisualTreeHelper.GetChild(node, index);
        }

        if (node is UIElement { Focusable: true, IsVisible: true, IsEnabled: true } target)
        {
            target.Focus();
        }
    }

    /// <summary>
    /// Opens a web URL (or mailto) in the default program, only when <see cref="SafeLinks.IsWebUrl"/> accepts it. Returns false,
    /// opening nothing, otherwise.
    /// </summary>
    public static bool OpenWebUrl(string url)
    {
        if (!SafeLinks.IsWebUrl(url, out var uri) || uri is null)
        {
            return false;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens a URL in the default browser (best effort). Refuses anything but a web URL or one of the fixed
    /// <c>ms-settings:</c>, <c>windowsdefender:</c> and <c>ms-windows-store:</c> schemes.
    /// </summary>
    public static void OpenUrl(string url)
    {
        if (!SafeLinks.IsAllowedShellUri(url))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // nothing sensible to do
        }
    }
}
