// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace Trayloom.Features.Capture;

internal enum SelKind { None, Zoom, Text, Image, Blur }

internal sealed partial class RecorderEditorWindow
{
    private static readonly Color AccentTint = Ed.AccentColor;
    private static readonly Color TextTint = Color.FromRgb(0xAF, 0x52, 0xDE);
    private static readonly Color ImageTint = Color.FromRgb(0xFF, 0x9F, 0x0A);
    private static readonly Color BlurTint = Color.FromRgb(0x30, 0xB0, 0xC7);

    private readonly ClickRuler _ruler = new();
    private OverlayLaneControl _zoomLane = null!;
    private OverlayLaneControl _textLane = null!;
    private OverlayLaneControl _imageLane = null!;
    private OverlayLaneControl _blurLane = null!;
    private UIElement _textRow = null!;
    private UIElement _imageRow = null!;
    private UIElement _blurRow = null!;
    private SelKind _selKind;
    private Guid? _selId;
    private double? _lastZoomAmount;

    private IEnumerable<OverlayLaneControl> Lanes => [_zoomLane, _textLane, _imageLane, _blurLane];

    private void BuildLanes()
    {
        _zoomLane = NewLane(AccentTint, RS.ZoomLaneEmptyHint, RecorderMotion.ZoomRampIn, SelKind.Zoom, ZoomItems);
        _textLane = NewLane(TextTint, RS.TextLaneEmptyHint, TextOverlay.Fade, SelKind.Text, TextItems);
        _imageLane = NewLane(ImageTint, RS.ImageLaneEmptyHint, ImageOverlay.Fade, SelKind.Image, ImageItems);
        _blurLane = NewLane(BlurTint, RS.BlurLaneEmptyHint, 0, SelKind.Blur, BlurItems);
        _ruler.Duration = Duration;
        _ruler.Seek += Seek;
        var stack = new StackPanel();
        var zoomBlock = new StackPanel();
        zoomBlock.Children.Add(LaneRow(string.Empty, _ruler));
        zoomBlock.Children.Add(LaneRow(RS.Zoom, _zoomLane));
        stack.Children.Add(zoomBlock);
        _textRow = LaneRow(RS.TextLabel, _textLane);
        _imageRow = LaneRow(RS.ImageLane, _imageLane);
        _blurRow = LaneRow(RS.BlurLane, _blurLane);
        stack.Children.Add(_textRow);
        stack.Children.Add(_imageRow);
        stack.Children.Add(_blurRow);
        _laneHost.Children.Add(stack);
    }

    private OverlayLaneControl NewLane(Color tint, string hint, double ramp, SelKind kind, Func<IReadOnlyList<LaneItem>> items)
    {
        var lane = new OverlayLaneControl(tint, hint, ramp) { Items = items, Duration = Duration };
        lane.SnapTargets = () => Take.Pointer.Clicks.Where(c => c.IsDown).Select(c => c.Time);
        lane.AddRequested += t => AddAt(kind, t);
        lane.SelectionChanged += id => Select(id is null ? SelKind.None : kind, id);
        lane.BeginEdit += PushUndo;
        lane.Edited += (id, s, e, mode) => LaneEdited(kind, id, s, e, mode);
        lane.RemoveRequested += RemoveSelected;
        return lane;
    }

    private IReadOnlyList<LaneItem> ZoomItems() =>
        _doc.ZoomSegments.Select(z => new LaneItem(z.Id, z.Start, z.End, $"{z.Amount:0.0}×", z.FollowsPointer ? "↗" : "◉")).ToList();

    private IReadOnlyList<LaneItem> TextItems() =>
        _doc.Texts.Select(t => new LaneItem(t.Id, t.Start, t.End, t.Text.Length > 18 ? t.Text[..18] : t.Text, "T")).ToList();

    private IReadOnlyList<LaneItem> ImageItems() =>
        _doc.Images.Select(t =>
        {
            var name = Path.GetFileNameWithoutExtension(t.Path);
            return new LaneItem(t.Id, t.Start, t.End, name.Length > 18 ? name[..18] : name, "▣");
        }).ToList();

    private IReadOnlyList<LaneItem> BlurItems() =>
        _doc.Blurs.Select(b => new LaneItem(b.Id, b.Start, b.End, RS.BlurLane, "▦")).ToList();

    private void RefreshLanes()
    {
        _textRow.Visibility = _doc.Texts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _imageRow.Visibility = _doc.Images.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _blurRow.Visibility = _doc.Blurs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _ruler.Ticks = Take.Pointer.Clicks.Where(c => c.IsDown).Select(c => c.Time).ToList();
        _ruler.Duration = Duration;
        foreach (var lane in Lanes)
        {
            lane.Duration = Duration;
            lane.Selected = null;
        }

        switch (_selKind)
        {
            case SelKind.Zoom: _zoomLane.Selected = _selId; break;
            case SelKind.Text: _textLane.Selected = _selId; break;
            case SelKind.Image: _imageLane.Selected = _selId; break;
            case SelKind.Blur: _blurLane.Selected = _selId; break;
        }

        foreach (var lane in Lanes)
        {
            lane.InvalidateVisual();
        }

        _ruler.InvalidateVisual();
    }

    /// <summary>One selection across all lanes; the inspector follows it (text, then picture, blur, zoom, else the tabs).</summary>
    private void Select(SelKind kind, Guid? id, bool rebuild = true)
    {
        if (kind != SelKind.None && id is null)
        {
            kind = SelKind.None;
        }

        _selKind = kind;
        _selId = kind == SelKind.None ? null : id;
        foreach (var lane in Lanes)
        {
            lane.Selected = null;
        }

        switch (kind)
        {
            case SelKind.Zoom: _zoomLane.Selected = _selId; break;
            case SelKind.Text: _textLane.Selected = _selId; break;
            case SelKind.Image: _imageLane.Selected = _selId; break;
            case SelKind.Blur: _blurLane.Selected = _selId; break;
        }

        if (_aiming && kind != SelKind.Zoom)
        {
            _aiming = false;
            EndRaw();
        }

        if (_pickingBlur && kind != SelKind.Blur)
        {
            EndPick();
        }

        foreach (var lane in Lanes)
        {
            lane.InvalidateVisual();
        }

        _composer?.SetSelectedBlur(kind == SelKind.Blur ? _selId : null);
        if (rebuild)
        {
            BuildInspector();
        }

        UpdateStage();
    }

    private ZoomSegment? SelectedZoom => _selKind == SelKind.Zoom ? _doc.ZoomSegments.FirstOrDefault(z => z.Id == _selId) : null;

    private TextOverlay? SelectedText => _selKind == SelKind.Text ? _doc.Texts.FirstOrDefault(z => z.Id == _selId) : null;

    private ImageOverlay? SelectedImage => _selKind == SelKind.Image ? _doc.Images.FirstOrDefault(z => z.Id == _selId) : null;

    private BlurRegion? SelectedBlur => _selKind == SelKind.Blur ? _doc.Blurs.FirstOrDefault(z => z.Id == _selId) : null;

    // ---- gestures on the lanes ------------------------------------------------------------------------------------------

    private void LaneEdited(SelKind kind, Guid id, double start, double end, LaneEdit mode)
    {
        const double min = 0.4;
        switch (kind)
        {
            case SelKind.Zoom when _doc.ZoomSegments.FirstOrDefault(z => z.Id == id) is { } z:
            {
                var (s, e) = mode == LaneEdit.Move ? ZoomTimeline.Moved(z, start, _doc.ZoomSegments, Duration)
                    : mode == LaneEdit.ResizeStart ? ZoomTimeline.Resized(z, true, start, _doc.ZoomSegments, Duration)
                    : ZoomTimeline.Resized(z, false, end, _doc.ZoomSegments, Duration);
                z.Start = s;
                z.End = e;
                break;
            }

            case SelKind.Text when _doc.Texts.FirstOrDefault(t => t.Id == id) is { } t:
                (t.Start, t.End) = Clamped(t.Start, t.End, start, end, mode, min);
                break;
            case SelKind.Image when _doc.Images.FirstOrDefault(t => t.Id == id) is { } i:
                (i.Start, i.End) = Clamped(i.Start, i.End, start, end, mode, min);
                break;
            case SelKind.Blur when _doc.Blurs.FirstOrDefault(t => t.Id == id) is { } b:
                (b.Start, b.End) = Clamped(b.Start, b.End, start, end, mode, min);
                break;
            default:
                return;
        }

        Commit(inspector: false, timeline: false);
    }

    private (double, double) Clamped(double curStart, double curEnd, double start, double end, LaneEdit mode, double min)
    {
        switch (mode)
        {
            case LaneEdit.Move:
            {
                var len = curEnd - curStart;
                var s = Math.Clamp(start, 0, Math.Max(0, Duration - len));
                return (s, s + len);
            }

            case LaneEdit.ResizeStart:
                return (Math.Clamp(start, 0, curEnd - min), curEnd);
            default:
                return (curStart, Math.Clamp(end, curStart + min, Duration));
        }
    }

    private void AddAt(SelKind kind, double time)
    {
        switch (kind)
        {
            case SelKind.Zoom: AddZoomAt(time); break;
            case SelKind.Text: AddTextAt(time); break;
            case SelKind.Image: AddImageAt(time); break;
            case SelKind.Blur: AddBlurAt(time); break;
        }
    }

    private void RemoveSelected(Guid id)
    {
        PushUndo();
        _doc.ZoomSegments.RemoveAll(z => z.Id == id);
        _doc.Texts.RemoveAll(z => z.Id == id);
        _doc.Images.RemoveAll(z => z.Id == id);
        _doc.Blurs.RemoveAll(z => z.Id == id);
        if (_selId == id)
        {
            _pickingBlur = false;
            _aiming = false;
            EndRaw();
            Select(SelKind.None, null, rebuild: false);
        }

        Commit(inspector: true, timeline: false);
    }

    // ---- adding ---------------------------------------------------------------------------------------------------------------

    private void AddZoomAt(double time)
    {
        var slot = ZoomTimeline.SlotForNew(time, _doc.ZoomSegments, Duration);
        if (slot is not { } s)
        {
            return;
        }

        PushUndo();
        var seg = new ZoomSegment { Start = s.Start, End = s.End, Amount = _lastZoomAmount ?? ZoomTimeline.SanitizedAmount(_doc.ZoomAmount) };
        _doc.ZoomSegments.Add(seg);
        _doc.ZoomEnabled = true;
        _doc.ZoomsGenerated = true;
        Select(SelKind.Zoom, seg.Id, rebuild: false);
        Commit(inspector: true, timeline: false);
    }

    private void AddTextAt(double time)
    {
        PushUndo();
        var start = Math.Max(0, Math.Min(time, Math.Max(0, Duration - 0.4)));
        var t = new TextOverlay { Text = RS.TextPlaceholder, Start = start, End = Math.Min(Duration, start + TextOverlay.DefaultLength) };
        _doc.Texts.Add(t);
        Select(SelKind.Text, t.Id, rebuild: false);
        Commit(inspector: true, timeline: false);
    }

    private void AddImageAt(double time)
    {
        if (PopoverKit.PickImage() is not { } picked)
        {
            return;
        }

        var imported = Take.ImportImage(picked);
        if (imported is null)
        {
            QuickToolHud.Show(G.Image, RS.ImageImportFailed);
            return;
        }

        PushUndo();
        var start = Math.Max(0, Math.Min(time, Math.Max(0, Duration - 0.4)));
        var img = new ImageOverlay { Path = imported, Start = start, End = Duration };
        _doc.Images.Add(img);
        Select(SelKind.Image, img.Id, rebuild: false);
        Commit(inspector: true, timeline: false);
    }

    private void AddBlurAt(double time)
    {
        PushUndo();
        var start = Math.Max(0, Math.Min(time, Math.Max(0, Duration - 0.4)));
        var blur = new BlurRegion { Start = start, End = Duration };
        _doc.Blurs.Add(blur);
        Select(SelKind.Blur, blur.Id, rebuild: false);
        Commit(inspector: true, timeline: false);
        StartPick(blur);
    }

    // ---- aim mode and blur drawing --------------------------------------------------------------------------------------

    private bool _aiming;
    private bool _pickingBlur;
    private BlurRegion? _pickTarget;
    private Point _pickStart;
    private Rectangle? _pickRect;
    private readonly Canvas _overlay = new();
    private readonly Border _hint = new();

    private void ShowHint(string text)
    {
        _hint.Child = Ed.Label(text, 11, FontWeights.Medium);
        _hint.Visibility = Visibility.Visible;
    }

    private void PauseForOverlayMode()
    {
        if (_playing)
        {
            TogglePlay();
        }
    }

    /// <summary>While drawing or aiming the stage shows the recording as captured: no zoom, background, pointer or other blurs.</summary>
    private void BeginRaw()
    {
        _rebuild.Stop();
        _composer?.SetRaw(true);
    }

    private void EndRaw()
    {
        _hint.Visibility = Visibility.Collapsed;
        _overlay.Cursor = Cursors.Arrow;
        _composer?.SetRaw(false);
        if (_rebuildDirty)
        {
            RebuildPreview();
        }

        UpdateStage();
    }

    private void BeginAim()
    {
        if (SelectedZoom is null)
        {
            return;
        }

        PauseForOverlayMode();
        _pickingBlur = false;
        _aiming = true;
        BeginRaw();
        _overlay.Cursor = Cursors.Cross;
        ShowHint(RS.ZoomPickSpotHint);
        BuildInspector();
    }

    private void EndAim()
    {
        if (!_aiming)
        {
            return;
        }

        _aiming = false;
        EndRaw();
        BuildInspector();
    }

    private void StartPick(BlurRegion blur)
    {
        PauseForOverlayMode();
        _aiming = false;
        _pickingBlur = true;
        _pickTarget = blur;
        BeginRaw();
        _overlay.Cursor = Cursors.Cross;
        ShowHint(RS.BlurPickAreaHint);
        BuildInspector();
    }

    private void EndPick()
    {
        if (!_pickingBlur)
        {
            return;
        }

        _pickingBlur = false;
        _pickTarget = null;
        if (_pickRect is not null)
        {
            _overlay.Children.Remove(_pickRect);
            _pickRect = null;
        }

        EndRaw();
    }

    private void StageDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_overlay);
        if (_aiming)
        {
            var w = Math.Max(1, _overlay.ActualWidth);
            var h = Math.Max(1, _overlay.ActualHeight);
            var unit = new Point(p.X / w, p.Y / h);
            if (SelectedZoom is { } z && unit.X is >= 0 and <= 1 && unit.Y is >= 0 and <= 1)
            {
                PushUndo();
                z.FocusX = unit.X;
                z.FocusY = unit.Y;
                Commit(inspector: false, timeline: false);
            }

            EndAim();
            return;
        }

        if (_pickingBlur)
        {
            _pickStart = p;
            _pickRect = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(56, 0x30, 0xB0, 0xC7)), Stroke = new SolidColorBrush(Color.FromRgb(0x30, 0xB0, 0xC7)), StrokeThickness = 3 };
            _overlay.Children.Add(_pickRect);
            _overlay.CaptureMouse();
            return;
        }

        // a click on the picture puts a selected zoom or blur down before it does anything else
        if (_selKind == SelKind.Zoom)
        {
            Select(SelKind.None, null);
        }
        else if (_selKind == SelKind.Blur)
        {
            Select(SelKind.None, null);
        }
        else
        {
            TogglePlay();
        }
    }

    private void StageMove(object sender, MouseEventArgs e)
    {
        if (_pickRect is null)
        {
            return;
        }

        var p = e.GetPosition(_overlay);
        Canvas.SetLeft(_pickRect, Math.Min(_pickStart.X, p.X));
        Canvas.SetTop(_pickRect, Math.Min(_pickStart.Y, p.Y));
        _pickRect.Width = Math.Abs(p.X - _pickStart.X);
        _pickRect.Height = Math.Abs(p.Y - _pickStart.Y);
    }

    private void StageUp(object sender, MouseButtonEventArgs e)
    {
        if (_pickRect is null)
        {
            return;
        }

        var p = e.GetPosition(_overlay);
        _overlay.ReleaseMouseCapture();
        var w = Math.Max(1, _overlay.ActualWidth);
        var h = Math.Max(1, _overlay.ActualHeight);
        var x1 = Math.Clamp(Math.Min(_pickStart.X, p.X) / w, 0, 1);
        var y1 = Math.Clamp(Math.Min(_pickStart.Y, p.Y) / h, 0, 1);
        var x2 = Math.Clamp(Math.Max(_pickStart.X, p.X) / w, 0, 1);
        var y2 = Math.Clamp(Math.Max(_pickStart.Y, p.Y) / h, 0, 1);
        var tooSmall = Math.Abs(p.X - _pickStart.X) < 2 && Math.Abs(p.Y - _pickStart.Y) < 2;
        _overlay.Children.Remove(_pickRect);
        _pickRect = null;
        if (tooSmall || _pickTarget is null || x2 - x1 < 0.01 || y2 - y1 < 0.01)
        {
            return;
        }

        var target = _pickTarget;
        PushUndo();
        target.X = x1;
        target.Y = y1;
        target.Width = x2 - x1;
        target.Height = y2 - y1;
        _pickingBlur = false;
        _pickTarget = null;
        EndRaw();
        Commit(inspector: true, timeline: false);
    }
}
