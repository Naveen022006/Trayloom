// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinKnife.Features.Capture;

/// <summary>Shared colour palette of the editor, watermark and recorder text (sRGB).</summary>
internal static class Palette
{
    public static readonly (string Name, string Label, Color Color)[] Colors =
    [
        ("red", "Red", Color.FromRgb(237, 66, 54)),
        ("orange", "Orange", Color.FromRgb(255, 148, 0)),
        ("yellow", "Yellow", Color.FromRgb(255, 204, 0)),
        ("green", "Green", Color.FromRgb(51, 199, 89)),
        ("blue", "Blue", Color.FromRgb(10, 133, 255)),
        ("purple", "Purple", Color.FromRgb(176, 82, 222)),
        ("black", "Black", Color.FromRgb(23, 23, 28)),
        ("white", "White", Color.FromRgb(255, 255, 255)),
    ];

    public static Color Get(string? name) => (Colors.FirstOrDefault(c => c.Name == name) is { Name: not null } c ? c : Colors[0]).Color;

    public static double StrokeWidth(string? stroke) => stroke switch { "small" => 2, "large" => 7, _ => 4 };

    public static readonly (string Id, string Glyph)[] Stickers =
    [
        ("check", "✅"), ("cross", "❌"), ("star", "⭐"), ("heart", "❤"), ("thumbsUp", "👍"), ("thumbsDown", "👎"),
        ("smile", "😀"), ("laugh", "😂"), ("party", "🎉"), ("fire", "🔥"), ("warning", "⚠"), ("eyes", "👀"),
    ];

    public static string StickerGlyph(string id) => Stickers.FirstOrDefault(s => s.Id == id).Glyph ?? "✅";
}

/// <summary>One mark on the screenshot. Geometry is in image pixels (top-left origin).</summary>
internal sealed class Annotation
{
    public ToolId Kind { get; set; }

    public string Color { get; set; } = "red";

    public string Stroke { get; set; } = "medium";

    public Point P1 { get; set; }

    public Point P2 { get; set; }

    public List<Point> Points { get; set; } = new();

    public string Text { get; set; } = string.Empty;

    public int TextSize { get; set; } = 19;

    public string Sticker { get; set; } = "check";

    public string ArrowStyle { get; set; } = "filled";

    public int Seed { get; set; }

    public string BlurStyle { get; set; } = "pixelate";

    public int BlurLevel { get; set; } = 3;

    public bool TextOnly { get; set; }

    public BitmapSource? Bitmap { get; set; }

    public double RefSize { get; set; }

    public Rect Bounds => new(Math.Min(P1.X, P2.X), Math.Min(P1.Y, P2.Y), Math.Abs(P2.X - P1.X), Math.Abs(P2.Y - P1.Y));

    public bool IsRectLike => Kind is ToolId.Rect or ToolId.Ellipse or ToolId.Highlight or ToolId.Pixelate or ToolId.Redact or ToolId.Spotlight or ToolId.Image;

    public bool NeedsPixels => Kind is ToolId.Pixelate or ToolId.Highlight or ToolId.Spotlight;

    public Annotation Clone()
    {
        var c = (Annotation)MemberwiseClone();
        c.Points = new List<Point>(Points);
        return c;
    }
}

internal sealed class BackdropStyle
{
    public string Kind { get; set; } = "none"; // none|preset|solid|gradient|image

    public string? PresetId { get; set; }

    public List<double[]>? Colors { get; set; }

    public string? ImagePath { get; set; }

    public double Padding { get; set; } = 0.5;

    public double CornerRadius { get; set; }

    public double Blur { get; set; }

    [JsonIgnore]
    public bool IsNone => Kind == "none";

    public static readonly (string Id, string Label, double[] A, double[] B)[] Presets =
    [
        ("ocean", "Ocean", [0.20, 0.47, 0.96], [0.45, 0.83, 0.98]),
        ("sunset", "Sunset", [0.99, 0.36, 0.42], [1.00, 0.75, 0.35]),
        ("forest", "Forest", [0.07, 0.56, 0.43], [0.62, 0.87, 0.50]),
        ("candy", "Candy", [0.66, 0.32, 0.95], [0.99, 0.56, 0.65]),
        ("graphite", "Graphite", [0.23, 0.25, 0.31], [0.55, 0.60, 0.70]),
    ];

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static BackdropStyle Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BackdropStyle();
        }

        try
        {
            var s = JsonSerializer.Deserialize<BackdropStyle>(json, Json) ?? new BackdropStyle();
            return s.Sanitized();
        }
        catch
        {
            return new BackdropStyle();
        }
    }

    public string ToJson() => IsNone ? string.Empty : JsonSerializer.Serialize(this, Json);

    public BackdropStyle Clone() => (BackdropStyle)MemberwiseClone();

    public BackdropStyle Sanitized()
    {
        double Fix(double v, double fallback) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : fallback;
        Padding = Fix(Padding, 0.5);
        CornerRadius = Fix(CornerRadius, 0.1);
        Blur = Fix(Blur, 0);
        if (Kind is "solid" && (Colors is null || Colors.Count < 1 || Colors[0].Length != 3))
        {
            Kind = "none";
        }

        if (Kind is "gradient" && (Colors is null || Colors.Count < 2 || Colors[0].Length != 3 || Colors[1].Length != 3))
        {
            Kind = "none";
        }

        if (Kind is "preset" && Presets.All(p => p.Id != PresetId))
        {
            Kind = "none";
        }

        if (Kind is not ("none" or "preset" or "solid" or "gradient" or "image"))
        {
            Kind = "none";
        }

        return this;
    }

    /// <summary>The fill brush for a canvas of the given pixel size, or null for none / a missing image.</summary>
    public Brush? CreateBrush(double w, double h)
    {
        static Color C(double[] c) => Color.FromRgb((byte)Math.Round(Math.Clamp(c[0], 0, 1) * 255), (byte)Math.Round(Math.Clamp(c[1], 0, 1) * 255), (byte)Math.Round(Math.Clamp(c[2], 0, 1) * 255));
        switch (Kind)
        {
            case "preset":
            {
                var p = Presets.First(x => x.Id == PresetId);
                return new LinearGradientBrush(C(p.A), C(p.B), new Point(0, 0), new Point(1, 1));
            }

            case "solid":
                return new SolidColorBrush(C(Colors![0]));
            case "gradient":
                return new LinearGradientBrush(C(Colors![0]), C(Colors[1]), new Point(0, 0), new Point(1, 1));
            case "image":
            {
                if (ImagePath is null || !File.Exists(ImagePath) || Output.LoadImage(ImagePath) is not { } img)
                {
                    return null;
                }

                var brush = new ImageBrush(img) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
                return brush;
            }

            default:
                return null;
        }
    }
}

internal sealed class WatermarkStyle
{
    public string Kind { get; set; } = "none"; // none|text|image

    public string Text { get; set; } = string.Empty;

    public string Color { get; set; } = "white";

    public string? ImagePath { get; set; }

    public string Anchor { get; set; } = "bottomTrailing";

    public double Size { get; set; } = 0.3;

    public double Opacity { get; set; } = 0.4;

    public double Rotation { get; set; }

    [JsonIgnore]
    public bool IsNone => Kind == "none" || (Kind == "text" && string.IsNullOrWhiteSpace(Text)) || (Kind == "image" && (ImagePath is null || !File.Exists(ImagePath)));

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static readonly string[] Anchors = ["topLeading", "top", "topTrailing", "leading", "center", "trailing", "bottomLeading", "bottom", "bottomTrailing"];

    public static readonly string[] AnchorNames = ["Top left", "Top center", "Top right", "Center left", "Center", "Center right", "Bottom left", "Bottom center", "Bottom right"];

    public static WatermarkStyle Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new WatermarkStyle();
        }

        try
        {
            var w = JsonSerializer.Deserialize<WatermarkStyle>(json, Json) ?? new WatermarkStyle();
            w.Size = double.IsFinite(w.Size) ? Math.Clamp(w.Size, 0, 1) : 0.3;
            w.Opacity = double.IsFinite(w.Opacity) ? Math.Clamp(w.Opacity, 0.05, 1) : 0.4;
            w.Rotation = double.IsFinite(w.Rotation) ? Math.Clamp(w.Rotation, -90, 90) : 0;
            if (w.Text.Length > 120)
            {
                w.Text = w.Text[..120];
            }

            if (Array.IndexOf(Anchors, w.Anchor) < 0)
            {
                w.Anchor = "bottomTrailing";
            }

            return w;
        }
        catch
        {
            return new WatermarkStyle();
        }
    }

    public string ToJson() => Kind == "none" ? string.Empty : JsonSerializer.Serialize(this, Json);

    public WatermarkStyle Clone() => (WatermarkStyle)MemberwiseClone();

    public bool SameLook(WatermarkStyle o) => ToJson() == o.ToJson();
}

/// <summary>Undoable document: base image + marks.</summary>
internal sealed class EditorDocument
{
    private readonly List<(BitmapSource Image, List<Annotation> Marks)> _undo = new();
    private readonly List<(BitmapSource Image, List<Annotation> Marks)> _redo = new();

    public EditorDocument(BitmapSource image, double scale)
    {
        Image = image;
        Scale = scale;
    }

    public BitmapSource Image { get; set; }

    public double Scale { get; }

    public List<Annotation> Marks { get; set; } = new();

    public BackdropStyle Backdrop { get; set; } = new();

    public WatermarkStyle Watermark { get; set; } = new();

    public bool Shadows { get; set; }

    public double SpotlightDim { get; set; } = 0.5;

    public List<Rect>? TextRuns { get; set; }

    public bool Dirty { get; set; }

    public int PixelWidth => Image.PixelWidth;

    public int PixelHeight => Image.PixelHeight;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Records the current state before an edit.</summary>
    public void Push()
    {
        _undo.Add((Image, Marks.Select(m => m.Clone()).ToList()));
        if (_undo.Count > 60)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Dirty = true;
    }

    public void DropLastUndo()
    {
        if (_undo.Count > 0)
        {
            _undo.RemoveAt(_undo.Count - 1);
        }
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Add((Image, Marks.Select(m => m.Clone()).ToList()));
        var (img, marks) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Image = img;
        Marks = marks;
        Dirty = true;
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Add((Image, Marks.Select(m => m.Clone()).ToList()));
        var (img, marks) = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Image = img;
        Marks = marks;
        Dirty = true;
        return true;
    }

    public void RenumberCounters()
    {
        // counters take their number from the array order at render time; nothing to store
    }
}
