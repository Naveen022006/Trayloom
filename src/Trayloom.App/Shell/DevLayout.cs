// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Trayloom.App;

/// <summary>Developer flag <c>--dump-text</c>: sizes of text blocks, to find clipped or mis-measured text.</summary>
internal static class DevLayout
{
    public static void Dump(string needle, string file)
    {
        var sb = new StringBuilder();
        foreach (var window in Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible))
        {
            Walk(window, window, needle, sb);
            sb.AppendLine("--- after forcing a full layout pass ---");
            if (window.Content is UIElement content)
            {
                content.InvalidateMeasure();
            }

            window.UpdateLayout();
            Walk(window, window, needle, sb);
        }

        File.WriteAllText(file, sb.ToString());
    }

    /// <summary>Scrolls the first text block containing <paramref name="needle"/> into view (for window screenshots).</summary>
    public static void ScrollTo(string needle)
    {
        foreach (var window in Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible))
        {
            var found = Find(window, needle);
            if (found is not null)
            {
                found.BringIntoView(new System.Windows.Rect(0, -120, Math.Max(1, found.ActualWidth), found.ActualHeight + 240));
                return;
            }
        }
    }

    private static TextBlock? Find(DependencyObject node, string needle)
    {
        if (node is TextBlock block && block.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return block;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            if (Find(VisualTreeHelper.GetChild(node, i), needle) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    private static void Walk(DependencyObject node, Window window, string needle, StringBuilder sb)
    {
        if (node is TextBlock block && block.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"[{window.GetType().Name}] '{block.Text}'");
            sb.AppendLine($"  actual={block.ActualWidth:F1}x{block.ActualHeight:F1} desired={block.DesiredSize.Width:F1}x{block.DesiredSize.Height:F1} font={block.FontSize} wrap={block.TextWrapping} layoutClip={System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(block)} clip={VisualTreeHelper.GetClip(block)} slot={System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(block)} lineHeight={block.LineHeight} fontFamily={block.FontFamily}");
            var parent = VisualTreeHelper.GetParent(block);
            for (var depth = 0; parent is not null && depth < 8; depth++)
            {
                if (parent is FrameworkElement fe)
                {
                    sb.AppendLine($"  ^ {fe.GetType().Name} actual={fe.ActualWidth:F1}x{fe.ActualHeight:F1} desired={fe.DesiredSize.Width:F1}x{fe.DesiredSize.Height:F1} margin={fe.Margin} layoutClip={System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(fe)} clip={VisualTreeHelper.GetClip(fe)} slot={System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(fe)}");
                }

                parent = VisualTreeHelper.GetParent(parent);
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), window, needle, sb);
        }
    }
}
