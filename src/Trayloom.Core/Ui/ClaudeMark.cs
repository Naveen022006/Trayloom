// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Trayloom.Core.Ui;

/// <summary>
/// Claude's mark (the spark: the irregular tapering burst outline from the CC0 Simple Icons set, on a 24 x 24 grid) in Claude orange.
/// One source for every place the island and the AI Agents page show Claude's work. <see cref="Glyph"/> is a pseudo icon-font
/// character: <see cref="GlyphBlock"/> (applied by the icon helpers) draws the mark instead of text, so a section declared with it
/// shows the mark in tabs, Explore tiles and quick buttons like any other glyph.
/// </summary>
public static class ClaudeMark
{
    /// <summary>Pseudo glyph (a Unicode non-character, never in an icon font) that <see cref="GlyphBlock"/> draws as the mark.</summary>
    public const string Glyph = "﷐";

    /// <summary>Claude orange (#D97757).</summary>
    public static readonly Color Orange = Color.FromRgb(0xD9, 0x77, 0x57);

    /// <summary>Frozen brush of <see cref="Orange"/>.</summary>
    public static readonly Brush OrangeBrush = Freeze(new SolidColorBrush(Orange));

    private const string SparkPath =
        "M4.709 15.955l4.72-2.647.08-.23-.08-.128H9.2l-.79-.048-2.698-.073-2.339-.097-2.266-.122-.571-.121L0 11.784l.05"
        + "5-.352.48-.321.686.06 1.52.103 2.278.158 1.652.097 2.449.255h.389l.055-.157-.134-.098-.103-.097-2.358-1.596-2."
        + "552-1.688-1.336-.972-.724-.491-.364-.462-.158-1.008.656-.722.881.06.225.061.893.686 1.908 1.476 2.491 1.833.36"
        + "5.304.145-.103.019-.073-.164-.274-1.355-2.446-1.446-2.49-.644-1.032-.17-.619a2.97 2.97 0 01-.104-.729L6.283.13"
        + "4 6.696 0l.996.134.42.364.62 1.414 1.002 2.229 1.555 3.03.456.898.243.832.091.255h.158V9.01l.128-1.706.237-2.0"
        + "95.23-2.695.08-.76.376-.91.747-.492.584.28.48.685-.067.444-.286 1.851-.559 2.903-.364 1.942h.212l.243-.242.985"
        + "-1.306 1.652-2.064.73-.82.85-.904.547-.431h1.033l.76 1.129-.34 1.166-1.064 1.347-.881 1.142-1.264 1.7-.79 1.36"
        + ".073.11.188-.02 2.856-.606 1.543-.28 1.841-.315.833.388.091.395-.328.807-1.969.486-2.309.462-3.439.813-.042.03"
        + ".049.061 1.549.146.662.036h1.622l3.02.225.79.522.474.638-.079.485-1.215.62-1.64-.389-3.829-.91-1.312-.329h-.18"
        + "2v.11l1.093 1.068 2.006 1.81 2.509 2.33.127.578-.322.455-.34-.049-2.205-1.657-.851-.747-1.926-1.62h-.128v.17l."
        + "444.649 2.345 3.521.122 1.08-.17.353-.608.213-.668-.122-1.374-1.925-1.415-2.167-1.143-1.943-.14.08-.674 7.254-"
        + ".316.37-.729.28-.607-.461-.322-.747.322-1.476.389-1.924.315-1.53.286-1.9.17-.632-.012-.042-.14.018-1.434 1.967"
        + "-2.18 2.945-1.726 1.845-.414.164-.717-.37.067-.662.401-.589 2.388-3.036 1.44-1.882.93-1.086-.006-.158h-.055L4."
        + "132 18.56l-1.13.146-.487-.456.061-.704.231-.243 1.908-1.312-.006.006z";

    private static Geometry? _spark;

    /// <summary>The spark outline (24 x 24 grid, non-zero fill).</summary>
    public static Geometry Spark => _spark ??= Build();

    private static Geometry Build()
    {
        var geometry = Geometry.Parse("F1 " + SparkPath);
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>The mark in a square of <paramref name="size"/> DIPs (Claude orange unless a brush is given).</summary>
    public static FrameworkElement Create(double size, Brush? brush = null)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = Spark, Fill = brush ?? OrangeBrush });
        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    }
}

/// <summary>
/// Makes icon <see cref="TextBlock"/>s understand <see cref="ClaudeMark.Glyph"/>: a block whose text is that pseudo glyph shows
/// Claude's spark (sized like a glyph of the block's font size, filled with the block's current <see cref="TextBlock.Foreground"/>)
/// instead of a character. The icon helpers of the island and the shared <c>UiKit.Icon</c> call <see cref="Realize"/>.
/// </summary>
public static class GlyphBlock
{
    /// <summary>Swaps the text of <paramref name="block"/> for the mark when it is <see cref="ClaudeMark.Glyph"/>; otherwise returns it as is.</summary>
    public static TextBlock Realize(TextBlock block)
    {
        if (block.Text != ClaudeMark.Glyph)
        {
            return block;
        }

        var canvas = new Canvas { Width = 24, Height = 24 };
        var path = new System.Windows.Shapes.Path { Data = ClaudeMark.Spark };
        path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding("Foreground") { Source = block });
        canvas.Children.Add(path);
        var box = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        box.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("FontSize") { Source = block, Converter = Scale });
        box.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding("FontSize") { Source = block, Converter = Scale });
        block.Text = string.Empty;
        block.Inlines.Add(new System.Windows.Documents.InlineUIContainer(box) { BaselineAlignment = BaselineAlignment.Center });
        return block;
    }

    private static readonly System.Windows.Data.IValueConverter Scale = new FontScale();

    private sealed class FontScale : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => (double)value * 1.15;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
