// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinKnife.Features.Capture;

internal sealed partial class ScreenshotEditorWindow
{
    private enum DragKind { None, Draw, Move, Handle, CropGrip, CropMove, CropNew, ExpandGrip, Words, TapCreate }

    private sealed class TextEditor
    {
        public required Annotation Annotation { get; init; }

        public required TextBox Box { get; init; }

        public bool IsNew { get; init; }

        public string Original { get; init; } = string.Empty;
    }

    private DragKind _drag;
    private Point _downPx;
    private Point _downView;
    private Point _lastPx;
    private Annotation? _dragMark;
    private int _handle = -1;
    private bool _pushed;
    private bool _createdByDrag;
    private Rect? _cropDraft;
    private Rect _expandRect;
    private string _expandFill = "white";
    private Color _expandCustom = Colors.White;
    private TextEditor? _editingText;
    private readonly Random _rng = new();

    private double Tol(double px) => px / Math.Max(0.05, _zoom);

    // ---- tool selection -------------------------------------------------------------------------------------------------

    private void SelectTool(ToolId tool)
    {
        CommitText();
        if (_tool == tool && tool != ToolId.Crop)
        {
            return;
        }

        if (_tool == ToolId.Crop || _tool == ToolId.ExpandCanvas)
        {
            _cropDraft = null;
        }

        _tool = tool;
        if (tool is not (ToolId.Select or ToolId.Crop or ToolId.ExpandCanvas or ToolId.Image))
        {
            _app.Set(K.LastTool, ToolCatalog.Get(tool).RawId);
        }

        if (tool != ToolId.Select)
        {
            _selectedWords.Clear();
        }

        if (tool == ToolId.Crop)
        {
            _selected = null;
            _cropDraft = new Rect(0, 0, _doc.PixelWidth, _doc.PixelHeight);
        }

        if (tool == ToolId.ExpandCanvas)
        {
            _selected = null;
            _expandRect = new Rect(0, 0, _doc.PixelWidth, _doc.PixelHeight);
            _stage.Margin = new Thickness(64);
        }
        else
        {
            _stage.Margin = new Thickness(0);
        }

        foreach (var kv in _railButtons)
        {
            kv.Value.Active = kv.Key == _tool;
        }

        if (_autoFit)
        {
            FitToWindow();
        }

        RebuildBar();
        _overlay.Cursor = tool is ToolId.Select ? Cursors.Arrow : Cursors.Cross;
        _overlay.InvalidateVisual();
    }

    // ---- hit testing --------------------------------------------------------------------------------------------------------

    private static IEnumerable<Point> HandlePoints(Rect r) =>
    [
        r.TopLeft, new Point(r.X + r.Width / 2, r.Top), r.TopRight, new Point(r.Right, r.Y + r.Height / 2),
        r.BottomRight, new Point(r.X + r.Width / 2, r.Bottom), r.BottomLeft, new Point(r.Left, r.Y + r.Height / 2),
    ];

    private int HandleAt(Annotation a, Point p)
    {
        var tol = Tol(12);
        if (a.Kind is ToolId.Arrow or ToolId.Line)
        {
            if (Dist(a.P1, p) <= tol)
            {
                return 0;
            }

            return Dist(a.P2, p) <= tol ? 1 : -1;
        }

        if (!a.IsRectLike)
        {
            return -1;
        }

        var i = 0;
        foreach (var hp in HandlePoints(a.Bounds))
        {
            if (Dist(hp, p) <= tol)
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double SegDist(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var l2 = dx * dx + dy * dy;
        if (l2 < 1e-9)
        {
            return Dist(p, a);
        }

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1);
        return Dist(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

    private bool HitMark(Annotation a, Point p)
    {
        var tol = Tol(8) + Palette.StrokeWidth(a.Stroke) * _doc.Scale / 2;
        switch (a.Kind)
        {
            case ToolId.Arrow:
            case ToolId.Line:
                return SegDist(p, a.P1, a.P2) <= tol;
            case ToolId.Freehand:
                for (var i = 1; i < a.Points.Count; i++)
                {
                    if (SegDist(p, a.Points[i - 1], a.Points[i]) <= tol)
                    {
                        return true;
                    }
                }

                return a.Points.Count == 1 && Dist(p, a.Points[0]) <= tol;
            case ToolId.Spotlight:
            {
                var b = a.Bounds;
                var outer = Rect.Inflate(b, tol, tol);
                var inner = Rect.Inflate(b, -tol, -tol);
                return outer.Contains(p) && (inner.IsEmpty || !inner.Contains(p));
            }

            default:
                return Rect.Inflate(EditorRenderer.MarkBounds(a, _doc), tol, tol).Contains(p);
        }
    }

    private Annotation? MarkAt(Point p)
    {
        for (var i = _doc.Marks.Count - 1; i >= 0; i--)
        {
            if (HitMark(_doc.Marks[i], p))
            {
                return _doc.Marks[i];
            }
        }

        return null;
    }

    private bool InsideImage(Point p) => p.X >= 0 && p.Y >= 0 && p.X <= _doc.PixelWidth && p.Y <= _doc.PixelHeight;

    // ---- mouse -------------------------------------------------------------------------------------------------------------------

    private void OverlayDown(object sender, MouseButtonEventArgs e)
    {
        CommitText();
        Focus();
        var p = e.GetPosition(_overlay);
        _downPx = p;
        _lastPx = p;
        _downView = e.GetPosition(this);
        _pushed = false;
        _createdByDrag = false;
        _overlay.CaptureMouse();
        e.Handled = true;

        if (_tool == ToolId.Crop && _cropDraft is { } crop)
        {
            var tol = Tol(14);
            var g = HandlePoints(crop).Select((pt, i) => (pt, i)).FirstOrDefault(x => Dist(x.pt, p) <= tol);
            if (g.pt != default || Dist(crop.TopLeft, p) <= tol)
            {
                _drag = DragKind.CropGrip;
                _handle = g.i;
            }
            else if (crop.Contains(p))
            {
                _drag = DragKind.CropMove;
            }
            else
            {
                _drag = DragKind.CropNew;
                _cropDraft = new Rect(p, new Size(0, 0));
            }

            return;
        }

        if (_tool == ToolId.ExpandCanvas)
        {
            var tol = Tol(10);
            var found = HandlePoints(_expandRect).Select((pt, i) => (pt, i)).Where(x => Dist(x.pt, p) <= tol * 1.4).Select(x => (int?)x.i).FirstOrDefault();
            if (found is { } idx)
            {
                _drag = DragKind.ExpandGrip;
                _handle = idx;
            }

            return;
        }

        // editing the current selection takes priority for every drawing tool
        if (_selected is { } sel)
        {
            var h = HandleAt(sel, p);
            if (h >= 0)
            {
                _drag = DragKind.Handle;
                _handle = h;
                _dragMark = sel;
                return;
            }

            if (HitMark(sel, p))
            {
                if (e.ClickCount == 2 && sel.Kind == ToolId.Text)
                {
                    BeginTextEdit(sel, false);
                    _drag = DragKind.None;
                    return;
                }

                _drag = DragKind.Move;
                _dragMark = sel;
                return;
            }
        }

        if (_tool == ToolId.Select)
        {
            var hit = MarkAt(p);
            _selected = hit;
            if (hit is not null)
            {
                _selectedWords.Clear();
                _drag = DragKind.Move;
                _dragMark = hit;
                RebuildBar();
            }
            else if (_ocrLines is not null)
            {
                _drag = DragKind.Words;
                _selectedWords.Clear();
                RebuildBar();
            }

            _overlay.InvalidateVisual();
            return;
        }

        _selected = null;
        if (!InsideImage(p) && _tool is ToolId.Text or ToolId.Sticker or ToolId.Counter)
        {
            _drag = DragKind.None;
            return;
        }

        switch (_tool)
        {
            case ToolId.Text:
            case ToolId.Sticker:
            case ToolId.Counter:
                _drag = DragKind.TapCreate;
                break;
            case ToolId.Freehand:
            {
                _doc.Push();
                _pushed = true;
                var a = NewMark(ToolId.Freehand, p);
                a.Points.Add(p);
                _doc.Marks.Add(a);
                _dragMark = a;
                _drag = DragKind.Draw;
                break;
            }

            default:
            {
                _doc.Push();
                _pushed = true;
                var a = NewMark(_tool, p);
                a.P2 = p;
                _doc.Marks.Add(a);
                _dragMark = a;
                _drag = DragKind.Draw;
                break;
            }
        }

        RebuildBar();
    }

    private Annotation NewMark(ToolId kind, Point p) => new()
    {
        Kind = kind, P1 = p, P2 = p, Color = _color, Stroke = _stroke, TextSize = _textSize, ArrowStyle = _arrowStyle, Sticker = _sticker,
        BlurStyle = _blurStyle, BlurLevel = _blurLevel, TextOnly = _blurTextOnly, Seed = _rng.Next(1, int.MaxValue),
        RefSize = Math.Min(_doc.PixelWidth, _doc.PixelHeight),
    };

    private void OverlayMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_overlay);
        if (_drag == DragKind.None)
        {
            UpdateCursor(p);
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (_drag)
        {
            case DragKind.Draw when _dragMark is { } m:
                if (m.Kind == ToolId.Freehand)
                {
                    if (m.Points.Count == 0 || Dist(m.Points[^1], p) > Tol(1.5))
                    {
                        m.Points.Add(p);
                    }
                }
                else
                {
                    var q = p;
                    if (shift && m.Kind is ToolId.Rect or ToolId.Ellipse)
                    {
                        var s = Math.Min(Math.Abs(p.X - m.P1.X), Math.Abs(p.Y - m.P1.Y));
                        q = new Point(m.P1.X + Math.Sign(p.X - m.P1.X) * s, m.P1.Y + Math.Sign(p.Y - m.P1.Y) * s);
                    }

                    m.P2 = q;
                }

                _createdByDrag = true;
                RequestRender();
                _overlay.InvalidateVisual();
                break;
            case DragKind.Move when _dragMark is { } m:
            {
                var dx = p.X - _lastPx.X;
                var dy = p.Y - _lastPx.Y;
                if (!_pushed && (Math.Abs(dx) + Math.Abs(dy)) * _zoom >= 1)
                {
                    _doc.Push();
                    _pushed = true;
                }

                if (_pushed)
                {
                    m.P1 = new Point(m.P1.X + dx, m.P1.Y + dy);
                    m.P2 = new Point(m.P2.X + dx, m.P2.Y + dy);
                    for (var i = 0; i < m.Points.Count; i++)
                    {
                        m.Points[i] = new Point(m.Points[i].X + dx, m.Points[i].Y + dy);
                    }

                    MarkChanged();
                    _overlay.InvalidateVisual();
                }

                break;
            }

            case DragKind.Handle when _dragMark is { } m:
                if (!_pushed)
                {
                    _doc.Push();
                    _pushed = true;
                }

                ApplyHandle(m, _handle, p);
                MarkChanged();
                _overlay.InvalidateVisual();
                break;
            case DragKind.CropGrip when _cropDraft is { } c:
                _cropDraft = ResizeRect(c, _handle, p, clampToImage: true);
                _overlay.InvalidateVisual();
                break;
            case DragKind.CropMove when _cropDraft is { } c:
            {
                var nx = Math.Clamp(c.X + p.X - _lastPx.X, 0, _doc.PixelWidth - c.Width);
                var ny = Math.Clamp(c.Y + p.Y - _lastPx.Y, 0, _doc.PixelHeight - c.Height);
                _cropDraft = new Rect(nx, ny, c.Width, c.Height);
                _overlay.InvalidateVisual();
                break;
            }

            case DragKind.CropNew:
            {
                var q = new Point(Math.Clamp(p.X, 0, _doc.PixelWidth), Math.Clamp(p.Y, 0, _doc.PixelHeight));
                _cropDraft = new Rect(Math.Min(_downPx.X, q.X), Math.Min(_downPx.Y, q.Y), Math.Abs(q.X - _downPx.X), Math.Abs(q.Y - _downPx.Y));
                _overlay.InvalidateVisual();
                break;
            }

            case DragKind.ExpandGrip:
                _expandRect = ResizeRect(_expandRect, _handle, p, clampToImage: false, outwardOnly: true);
                _overlay.InvalidateVisual();
                RebuildBar();
                break;
            case DragKind.Words:
                SelectWords(Rect.Inflate(new Rect(_downPx, p), 0.5, 0.5));
                _overlay.InvalidateVisual();
                break;
        }

        _lastPx = p;
    }

    private void OverlayUp(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_overlay);
        var kind = _drag;
        _drag = DragKind.None;
        _overlay.ReleaseMouseCapture();
        var tap = (e.GetPosition(this) - _downView).Length < 7;
        switch (kind)
        {
            case DragKind.Draw when _dragMark is { } m:
                if (tap && !_createdByDrag || tap)
                {
                    // a tap never leaves a degenerate shape: drop it and its undo step, select what is under the point
                    _doc.Marks.Remove(m);
                    _doc.DropLastUndo();
                    _selected = MarkAt(p);
                    RefreshImage();
                }
                else
                {
                    _selected = m;
                    if (m.Kind == ToolId.Rect && m.Bounds.Width < 1 || m.Kind == ToolId.Pixelate && (m.Bounds.Width < 2 || m.Bounds.Height < 2))
                    {
                        _doc.Marks.Remove(m);
                        _doc.DropLastUndo();
                        _selected = null;
                    }

                    _doc.Dirty = true;
                    RefreshImage();
                }

                RebuildBar();
                UpdateUndoState();
                break;
            case DragKind.TapCreate when tap:
            {
                var hit = MarkAt(p);
                if (hit is not null && hit.Kind == _tool)
                {
                    _selected = hit;
                    RebuildBar();
                    _overlay.InvalidateVisual();
                    break;
                }

                if (!InsideImage(p))
                {
                    break;
                }

                if (_tool == ToolId.Text)
                {
                    var a = NewMark(ToolId.Text, p);
                    _doc.Marks.Add(a);
                    _selected = a;
                    BeginTextEdit(a, true);
                }
                else
                {
                    _doc.Push();
                    var a = NewMark(_tool, p);
                    if (_tool == ToolId.Sticker)
                    {
                        var side = EditorRenderer.StickerSide(_doc);
                        a.P1 = new Point(Math.Clamp(p.X, side / 2, Math.Max(side / 2, _doc.PixelWidth - side / 2)), Math.Clamp(p.Y, side / 2, Math.Max(side / 2, _doc.PixelHeight - side / 2)));
                    }

                    _doc.Marks.Add(a);
                    _selected = a;
                    _doc.Dirty = true;
                    RefreshImage();
                    RebuildBar();
                    UpdateUndoState();
                }

                break;
            }

            case DragKind.CropNew:
                if (_cropDraft is { } cd && (cd.Width < 8 || cd.Height < 8))
                {
                    _cropDraft = new Rect(0, 0, _doc.PixelWidth, _doc.PixelHeight);
                }

                SnapCrop();
                _overlay.InvalidateVisual();
                break;
            case DragKind.CropGrip or DragKind.CropMove:
                SnapCrop();
                _overlay.InvalidateVisual();
                break;
            case DragKind.ExpandGrip:
                ApplyExpand();
                break;
            case DragKind.Move or DragKind.Handle:
                RefreshImage();
                break;
            case DragKind.Words:
                _overlay.InvalidateVisual();
                break;
        }

        _dragMark = null;
        _handle = -1;
    }

    private void AbortDrag()
    {
        if (_drag != DragKind.None && _overlay.IsMouseCaptured == false)
        {
            _drag = DragKind.None;
        }
    }

    private void UpdateCursor(Point p)
    {
        if (_tool == ToolId.Crop || _tool == ToolId.ExpandCanvas)
        {
            _overlay.Cursor = Cursors.Cross;
            return;
        }

        if (_selected is { } sel && (HandleAt(sel, p) >= 0 || HitMark(sel, p)))
        {
            _overlay.Cursor = HandleAt(sel, p) >= 0 ? Cursors.SizeAll : Cursors.Hand;
            return;
        }

        if (_tool == ToolId.Select)
        {
            _overlay.Cursor = _ocrLines is not null && _ocrLines.Any(l => l.Words.Any(w => w.Box.Contains(p))) ? Cursors.IBeam : Cursors.Arrow;
            return;
        }

        _overlay.Cursor = Cursors.Cross;
    }

    private static Rect ResizeRect(Rect r, int handle, Point p, bool clampToImage, double? w = null, double? h = null, bool outwardOnly = false)
    {
        double l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
        switch (handle)
        {
            case 0: l = p.X; t = p.Y; break;
            case 1: t = p.Y; break;
            case 2: rt = p.X; t = p.Y; break;
            case 3: rt = p.X; break;
            case 4: rt = p.X; b = p.Y; break;
            case 5: b = p.Y; break;
            case 6: l = p.X; b = p.Y; break;
            case 7: l = p.X; break;
        }

        return NormalizeRect(l, t, rt, b);
    }

    private Rect ResizeRect(Rect r, int handle, Point p, bool clampToImage, bool outwardOnly)
    {
        if (clampToImage)
        {
            p = new Point(Math.Clamp(p.X, 0, _doc.PixelWidth), Math.Clamp(p.Y, 0, _doc.PixelHeight));
        }

        var res = ResizeRect(r, handle, p, clampToImage);
        if (outwardOnly)
        {
            res = new Rect(Math.Min(res.Left, 0), Math.Min(res.Top, 0), 0, 0) is var origin
                ? NormalizeRect(Math.Min(res.Left, 0), Math.Min(res.Top, 0), Math.Max(res.Right, _doc.PixelWidth), Math.Max(res.Bottom, _doc.PixelHeight))
                : res;
        }

        return res;
    }

    private static Rect NormalizeRect(double l, double t, double r, double b) => new(Math.Min(l, r), Math.Min(t, b), Math.Abs(r - l), Math.Abs(b - t));

    private static void ApplyHandle(Annotation m, int handle, Point p)
    {
        if (m.Kind is ToolId.Arrow or ToolId.Line)
        {
            if (handle == 0)
            {
                m.P1 = p;
            }
            else
            {
                m.P2 = p;
            }

            return;
        }

        var r = ResizeRect(m.Bounds, handle, p, false);
        if (m.Kind == ToolId.Image && m.Bitmap is { } bmp && handle is 0 or 2 or 4 or 6)
        {
            var ratio = (double)bmp.PixelWidth / bmp.PixelHeight;
            r = new Rect(r.X, r.Y, r.Width, Math.Max(1, r.Width / ratio));
        }

        m.P1 = r.TopLeft;
        m.P2 = r.BottomRight;
    }

    private void SnapCrop()
    {
        if (_cropDraft is { } c)
        {
            var l = Math.Round(c.Left);
            var t = Math.Round(c.Top);
            var r = Math.Round(c.Right);
            var b = Math.Round(c.Bottom);
            _cropDraft = new Rect(l, t, Math.Max(1, r - l), Math.Max(1, b - t));
        }
    }

    // ---- crop / expand -------------------------------------------------------------------------------------------------------------

    private void ApplyCrop()
    {
        if (_cropDraft is not { } c || c.Width < 8 || c.Height < 8)
        {
            return;
        }

        var rect = new Int32Rect((int)c.X, (int)c.Y, (int)c.Width, (int)c.Height);
        if (rect.Width == _doc.PixelWidth && rect.Height == _doc.PixelHeight)
        {
            SelectTool(ToolId.Select);
            return;
        }

        _doc.Push();
        _doc.Image = ScreenGrab.Crop(_doc.Image, rect);
        foreach (var m in _doc.Marks)
        {
            Shift(m, -rect.X, -rect.Y);
        }

        _doc.TextRuns = _doc.TextRuns?.Select(r => new Rect(r.X - rect.X, r.Y - rect.Y, r.Width, r.Height)).ToList();
        _cropDraft = null;
        _doc.Dirty = true;
        SelectTool(ToolId.Select);
        FitToWindow();
        RefreshAll();
        _ = AnalyzeAsync();
    }

    private static void Shift(Annotation m, double dx, double dy)
    {
        m.P1 = new Point(m.P1.X + dx, m.P1.Y + dy);
        m.P2 = new Point(m.P2.X + dx, m.P2.Y + dy);
        for (var i = 0; i < m.Points.Count; i++)
        {
            m.Points[i] = new Point(m.Points[i].X + dx, m.Points[i].Y + dy);
        }
    }

    private void ApplyExpand()
    {
        var r = _expandRect;
        var left = (int)Math.Round(-r.Left);
        var top = (int)Math.Round(-r.Top);
        var newW = (int)Math.Round(r.Width);
        var newH = (int)Math.Round(r.Height);
        if (newW == _doc.PixelWidth && newH == _doc.PixelHeight)
        {
            return;
        }

        if (newW < 1 || newH < 1 || newW > 60_000_000 || newH > 60_000_000 || (long)newW * newH > 60_000_000)
        {
            CaptureHost.Beep();
            _expandRect = new Rect(0, 0, _doc.PixelWidth, _doc.PixelHeight);
            _overlay.InvalidateVisual();
            return;
        }

        var fill = _expandFill switch { "transparent" => Colors.Transparent, "custom" => _expandCustom, _ => Colors.White };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(fill), null, new Rect(0, 0, newW, newH));
            dc.DrawImage(_doc.Image, new Rect(left, top, _doc.PixelWidth, _doc.PixelHeight));
        }

        var rtb = new RenderTargetBitmap(newW, newH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        _doc.Push();
        _doc.Image = rtb;
        foreach (var m in _doc.Marks)
        {
            Shift(m, left, top);
        }

        _doc.TextRuns = null;
        _doc.Dirty = true;
        _expandRect = new Rect(0, 0, newW, newH);
        FitToWindow();
        RefreshAll();
        _ = AnalyzeAsync();
    }

    // ---- words ---------------------------------------------------------------------------------------------------------------------

    private void SelectWords(Rect area)
    {
        _selectedWords.Clear();
        if (_ocrLines is null)
        {
            return;
        }

        var idx = 0;
        foreach (var line in _ocrLines)
        {
            foreach (var w in line.Words)
            {
                if (w.Box.IntersectsWith(area))
                {
                    _selectedWords.Add(idx);
                }

                idx++;
            }
        }
    }

    private string? SelectedWordsText()
    {
        if (_ocrLines is null || _selectedWords.Count == 0)
        {
            return null;
        }

        var idx = 0;
        var lines = new List<string>();
        foreach (var line in _ocrLines)
        {
            var parts = new List<string>();
            foreach (var w in line.Words)
            {
                if (_selectedWords.Contains(idx))
                {
                    parts.Add(w.Text);
                }

                idx++;
            }

            if (parts.Count > 0)
            {
                lines.Add(string.Join(" ", parts));
            }
        }

        return string.Join("\n", lines);
    }

    // ---- text editing -------------------------------------------------------------------------------------------------------------------

    private void BeginTextEdit(Annotation a, bool isNew)
    {
        CommitText();
        var px = Math.Max(8, a.TextSize * _doc.Scale);
        var box = new TextBox
        {
            Text = a.Text,
            FontSize = px,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            Foreground = new SolidColorBrush(Palette.Get(a.Color)),
            CaretBrush = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 10, 132, 255)),
            BorderThickness = new Thickness(1.5 / Math.Max(_zoom, 0.1)),
            MinWidth = 130 / Math.Max(_zoom, 0.1),
            Padding = new Thickness(0),
            AcceptsReturn = false,
        };
        Canvas.SetLeft(box, a.P1.X);
        Canvas.SetTop(box, a.P1.Y);
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                CommitText();
                Focus();
            }
        };
        box.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(CommitText);
        _overlay.Children.Add(box);
        _editingText = new TextEditor { Annotation = a, Box = box, IsNew = isNew, Original = a.Text };
        RefreshImage();
        box.Loaded += (_, _) => box.Focus();
        box.Focus();
        box.SelectAll();
    }

    private void CommitText()
    {
        if (_editingText is not { } te)
        {
            return;
        }

        _editingText = null;
        _overlay.Children.Remove(te.Box);
        var text = te.Box.Text;
        var a = te.Annotation;
        if (te.IsNew)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _doc.Marks.Remove(a);
                if (ReferenceEquals(_selected, a))
                {
                    _selected = null;
                }
            }
            else
            {
                _doc.Marks.Remove(a);
                _doc.Push();
                a.Text = text;
                _doc.Marks.Add(a);
            }
        }
        else if (text != te.Original)
        {
            _doc.Push();
            a.Text = text;
        }

        RefreshImage();
        UpdateUndoState();
        RebuildBar();
    }

    // ---- overlay drawing ---------------------------------------------------------------------------------------------------------------

    private void DrawOverlay(DrawingContext dc)
    {
        var z = Math.Max(0.05, _zoom);
        var blue = new SolidColorBrush(Color.FromArgb(230, 10, 132, 255));
        var thin = 1.5 / z;
        if (_tool == ToolId.Crop && _cropDraft is { } crop)
        {
            var full = new Rect(0, 0, _doc.PixelWidth, _doc.PixelHeight);
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(crop)));
            dc.DrawRectangle(null, new Pen(Brushes.White, thin), crop);
            foreach (var hp in HandlePoints(crop))
            {
                dc.DrawEllipse(Brushes.White, new Pen(blue, thin), hp, 5 / z, 5 / z);
            }

            var lbl = new FormattedText($"{(int)crop.Width} × {(int)crop.Height}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11 / z, Brushes.White, 1.0);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), null, new Rect(crop.X + 4 / z, crop.Y + 4 / z, lbl.Width + 10 / z, lbl.Height + 4 / z), 4 / z, 4 / z);
            dc.DrawText(lbl, new Point(crop.X + 9 / z, crop.Y + 6 / z));
            return;
        }

        if (_tool == ToolId.ExpandCanvas)
        {
            var preview = _expandRect;
            var fill = _expandFill switch { "transparent" => Color.FromArgb(60, 255, 255, 255), "custom" => _expandCustom, _ => Colors.White };
            dc.DrawGeometry(new SolidColorBrush(fill), null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(preview), new RectangleGeometry(new Rect(0, 0, _doc.PixelWidth, _doc.PixelHeight))));
            dc.DrawRectangle(null, new Pen(blue, 1 / z), preview);
            foreach (var hp in HandlePoints(preview))
            {
                dc.DrawEllipse(Brushes.White, new Pen(blue, 1.5 / z), hp, 3.5 / z, 3.5 / z);
            }

            return;
        }

        if (_selected is { } sel && _editingText is null)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(230, 10, 132, 255)), thin) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };
            if (sel.Kind is ToolId.Arrow or ToolId.Line)
            {
                dc.DrawLine(pen, sel.P1, sel.P2);
                dc.DrawEllipse(Brushes.White, new Pen(blue, thin), sel.P1, 4 / z, 4 / z);
                dc.DrawEllipse(Brushes.White, new Pen(blue, thin), sel.P2, 4 / z, 4 / z);
            }
            else
            {
                var b = Rect.Inflate(EditorRenderer.MarkBounds(sel, _doc), 3 / z, 3 / z);
                dc.DrawRectangle(null, pen, b);
                if (sel.IsRectLike)
                {
                    foreach (var hp in HandlePoints(sel.Bounds))
                    {
                        dc.DrawEllipse(Brushes.White, new Pen(blue, thin), hp, 3.5 / z, 3.5 / z);
                    }
                }
            }
        }

        if (_ocrLines is not null && _selectedWords.Count > 0)
        {
            var idx = 0;
            foreach (var line in _ocrLines)
            {
                foreach (var w in line.Words)
                {
                    if (_selectedWords.Contains(idx))
                    {
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(82, 10, 132, 255)), null, Rect.Inflate(w.Box, 1.5 * _doc.Scale, 1.5 * _doc.Scale), 2 * _doc.Scale, 2 * _doc.Scale);
                    }

                    idx++;
                }
            }
        }
    }

    // ---- keyboard -------------------------------------------------------------------------------------------------------------------------

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (_editingText is not null || Keyboard.FocusedElement is TextBox)
        {
            if (ctrl && key == Key.S)
            {
                e.Handled = true;
                if (shift)
                {
                    SaveAs();
                }
                else
                {
                    SaveAndClose();
                }
            }

            return;
        }

        if (ctrl)
        {
            switch (key)
            {
                case Key.C:
                    if (SelectedWordsText() is { } words)
                    {
                        Output.CopyText(words);
                        QuickToolHud.Show(G.TextScan, Strings.Screenshot.TextCopied);
                    }
                    else if (!e.IsRepeat)
                    {
                        CopyAndClose();
                    }

                    e.Handled = true;
                    return;
                case Key.V when !e.IsRepeat:
                    PasteImage();
                    e.Handled = true;
                    return;
                case Key.S:
                    if (shift)
                    {
                        SaveAs();
                    }
                    else
                    {
                        SaveAndClose();
                    }

                    e.Handled = true;
                    return;
                case Key.Z:
                    if (shift)
                    {
                        Redo();
                    }
                    else
                    {
                        Undo();
                    }

                    e.Handled = true;
                    return;
                case Key.Y:
                    Redo();
                    e.Handled = true;
                    return;
                case Key.P:
                    PinCurrent();
                    e.Handled = true;
                    return;
                case Key.Back or Key.Delete:
                    _forceClose = true;
                    Close();
                    e.Handled = true;
                    return;
                case Key.D0 or Key.NumPad0:
                    _autoFit = true;
                    FitToWindow();
                    e.Handled = true;
                    return;
                case Key.D1 or Key.NumPad1:
                    SetZoom(1.0 / _doc.Scale, false);
                    e.Handled = true;
                    return;
                case Key.OemPlus or Key.Add:
                    SetZoom(_zoom * 1.25, false);
                    e.Handled = true;
                    return;
                case Key.OemMinus or Key.Subtract:
                    SetZoom(_zoom * 0.8, false);
                    e.Handled = true;
                    return;
                case Key.W:
                    return;
            }

            return;
        }

        switch (key)
        {
            case Key.Back or Key.Delete:
                if (_selected is { } sel)
                {
                    _doc.Push();
                    _doc.Marks.Remove(sel);
                    _selected = null;
                    RefreshAll();
                }

                e.Handled = true;
                return;
            case Key.Return or Key.Enter when !e.IsRepeat:
                if (_tool == ToolId.Crop && _cropDraft is not null)
                {
                    ApplyCrop();
                }
                else
                {
                    CopyAndClose();
                }

                e.Handled = true;
                return;
            case Key.Escape:
                if (_tool == ToolId.ExpandCanvas)
                {
                    SelectTool(ToolId.Select);
                }
                else if (_tool == ToolId.Crop && _cropDraft is not null)
                {
                    SelectTool(ToolId.Select);
                }
                else if (_selectedWords.Count > 0)
                {
                    _selectedWords.Clear();
                    _overlay.InvalidateVisual();
                }
                else if (_selected is not null)
                {
                    _selected = null;
                    RebuildBar();
                    _overlay.InvalidateVisual();
                }
                else
                {
                    Close();
                }

                e.Handled = true;
                return;
        }

        if (!ctrl && ToolCatalog.ToolForKey(_app, key) is { } tool)
        {
            SelectTool(tool.Id);
            e.Handled = true;
        }
    }

    private void PasteImage()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                CaptureHost.Beep();
                return;
            }

            var img = Clipboard.GetImage();
            if (img is null || (long)img.PixelWidth * img.PixelHeight > 60_000_000)
            {
                CaptureHost.Beep();
                return;
            }

            var bmp = Pixels.Make(Pixels.Get(img), img.PixelWidth, img.PixelHeight);
            var fit = Math.Min(1.0, Math.Min((double)_doc.PixelWidth / bmp.PixelWidth, (double)_doc.PixelHeight / bmp.PixelHeight));
            var w = Math.Max(1, Math.Round(bmp.PixelWidth * fit));
            var h = Math.Max(1, Math.Round(bmp.PixelHeight * fit));
            var x = Math.Round((_doc.PixelWidth - w) / 2);
            var y = Math.Round((_doc.PixelHeight - h) / 2);
            _doc.Push();
            var a = new Annotation { Kind = ToolId.Image, Bitmap = bmp, P1 = new Point(x, y), P2 = new Point(x + w, y + h) };
            _doc.Marks.Add(a);
            _selected = a;
            RefreshAll();
        }
        catch
        {
            CaptureHost.Beep();
        }
    }
}
