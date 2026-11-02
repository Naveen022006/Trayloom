// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Specialized;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;
using System.Windows.Threading;
using WinKnife.Core.Ui;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>
/// The recording editor: a stage drawn by the shared composer (background, zoom, pointer, ring, texts, pictures, blurs), the
/// inspector (Look / Pointer / Zoom and the one-item panels), the timeline (zoom, text, image and blur lanes, audio lanes, filmstrip
/// with trim and cuts) and export (MP4 / GIF).
/// </summary>
internal sealed partial class RecorderEditorWindow : Window
{
    private readonly RecorderService _service;
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private EditDocument _doc;
    private readonly MediaElement _media = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, ScrubbingEnabled = true, Stretch = Stretch.Fill };
    private readonly PreviewAudio _audio;
    private readonly FilmstripControl _film = new();
    private readonly StackPanel _inspector = new();
    private readonly TextBlock _timeLabel = Ed.Label(string.Empty, 12, FontWeights.Medium, Ed.FgDim);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly StackPanel _topRight = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _transport = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _laneHost = new();
    private EdButton _playBtn = null!;
    private EdButton _undoBtn = null!;
    private EdButton _redoBtn = null!;
    private bool _playing;
    private double _src;
    private CancellationTokenSource? _exportCts;
    private string? _lastExported;
    private bool _exported;
    private bool _forceClose;
    private bool _trimPushed;
    private Border? _progress;

    public RecorderEditorWindow(RecorderService service, Take take)
    {
        _service = service;
        Take = take;
        _doc = take.Document;
        _doc.Sanitize(Math.Max(0.2, take.Duration));
        _audio = new PreviewAudio(take);
        _audio.Apply(_doc);
        Title = Strings.Recorder.EditorTitle;
        Background = Ed.SurfaceBrush;
        Foreground = Ed.Fg;
        FontFamily = Ed.Text;
        UseLayoutRounding = true;
        MinWidth = 940;
        MinHeight = 560;
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Math.Max(1060, 0.70 * work.Width), work.Width - 60);
        Height = Math.Min(Math.Max(600, 0.68 * work.Height), work.Height - 80);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SourceInitialized += (_, _) => Ed.DarkTitle(this);
        Build();
        _media.MediaOpened += (_, _) =>
        {
            _media.Pause();
            Seek(_doc.TrimStart);
        };
        _media.MediaFailed += (_, e) => CaptureHost.Log("editor media failed: " + e.ErrorException);
        _media.Source = new Uri(take.VideoPath);
        _media.Play();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        PreviewKeyDown += OnKeyDown;
        PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(() => _interactionOpen = false, DispatcherPriority.Input);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _rebuild.Stop();
            _exportCts?.Cancel();
            _audio.Dispose();
            _media.Close();
            Take.Delete();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, CaptureHost.TrimMemory);
        };
        Loaded += async (_, _) => await LoadThumbsAsync();
        Persist();
        RebuildPreview();
        RefreshAll();
    }

    public Take Take { get; }

    private double Duration => Math.Max(0.2, Take.Duration);

    // ---- layout ------------------------------------------------------------------------------------------------------

    private void Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // top band
        var top = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var brand = BrandMark.Create(24);
        left.Children.Add(brand);
        var title = Ed.Label(Strings.Recorder.EditorTitle, 13, FontWeights.SemiBold);
        title.Margin = new Thickness(8, 0, 0, 0);
        left.Children.Add(title);
        left.Children.Add(new EdButton(G.History, null, Strings.Screenshot.RecentCaptures, RecentCapturesWindow.ShowPalette, width: 30, height: 30) { Margin = new Thickness(10, 0, 0, 0) });
        left.Children.Add(Ed.Divider(18));
        _undoBtn = new EdButton(G.Undo, null, "Ctrl+Z", Undo, width: 30, height: 30);
        _redoBtn = new EdButton(G.Redo, null, "Ctrl+Shift+Z", Redo, width: 30, height: 30);
        left.Children.Add(_undoBtn);
        left.Children.Add(_redoBtn);
        left.Children.Add(BuildPresetsButton());
        top.Children.Add(left);
        _topRight.HorizontalAlignment = HorizontalAlignment.Right;
        _topRight.VerticalAlignment = VerticalAlignment.Center;
        _topRight.Margin = new Thickness(0, 0, 14, 0);
        top.Children.Add(_topRight);
        BuildTopRight(null);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        // middle: stage + inspector
        var mid = new Grid();
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(272) });
        var stageHost = new Border { Margin = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(184, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)), BorderThickness = new Thickness(1), ClipToBounds = true };
        _stageGrid.Background = Brushes.Transparent;
        _overlay.Background = Brushes.Transparent;
        _overlay.MouseLeftButtonDown += StageDown;
        _overlay.MouseMove += StageMove;
        _overlay.MouseLeftButtonUp += StageUp;
        _stageGrid.Children.Add(_overlay);
        var viewbox = new Viewbox { Child = _stageGrid, Stretch = Stretch.Uniform };
        var outer = new Grid();
        outer.Children.Add(viewbox);
        _hint.CornerRadius = new CornerRadius(14);
        _hint.Background = Ed.Plate;
        _hint.Padding = new Thickness(10, 5, 10, 5);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.VerticalAlignment = VerticalAlignment.Top;
        _hint.Margin = new Thickness(0, 10, 0, 0);
        _hint.Visibility = Visibility.Collapsed;
        _hint.IsHitTestVisible = false;
        outer.Children.Add(_hint);
        stageHost.Child = outer;
        mid.Children.Add(stageHost);
        var insp = new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { Content = _inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(16, 14, 16, 14) } };
        Grid.SetColumn(insp, 1);
        mid.Children.Add(insp);
        Grid.SetRow(mid, 1);
        root.Children.Add(mid);

        // bottom band: transport + lanes
        var bottom = new StackPanel { Margin = new Thickness(18, 10, 18, 16), Background = Brushes.Transparent };
        BuildTransport();
        _transport.Margin = new Thickness(0, 0, 0, 4);
        bottom.Children.Add(_transport);
        BuildLanes();
        bottom.Children.Add(_laneHost);
        bottom.Children.Add(AudioRow(RS.SystemAudioTrack, Take.HasSystemAudio, true));
        bottom.Children.Add(AudioRow(RS.MicrophoneTrack, Take.HasMicrophone, false));
        bottom.Children.Add(LaneRow(Strings.Recorder.EditorTitle, _film));
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        Content = root;

        _film.Document = _doc;
        _film.Duration = Duration;
        _film.Seek += Seek;
        _film.TrimChanged += (s, e) =>
        {
            if (!_trimPushed)
            {
                PushUndo();
                _trimPushed = true;
            }

            _doc.TrimStart = s;
            _doc.TrimEnd = e;
            _film.InvalidateVisual();
            UpdateTime();
        };
        _film.TrimCommitted += () =>
        {
            _trimPushed = false;
            Commit(inspector: false, timeline: true);
        };
        _film.RestoreCutAt += start =>
        {
            PushUndo();
            _doc.Cuts.RemoveAll(c => Math.Abs(c.Start - start) < 0.15);
            Commit(inspector: false, timeline: true);
        };
        _film.CutSelectionChanged += BuildTransport;
    }

    private static UIElement LaneRow(string label, FrameworkElement lane)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = Ed.Label(label, 10, FontWeights.SemiBold, Ed.FgDim);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        l.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(l);
        Grid.SetColumn(lane, 1);
        grid.Children.Add(lane);
        return grid;
    }

    private UIElement AudioRow(string label, bool present, bool system)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3), Visibility = present ? Visibility.Visible : Visibility.Collapsed, Height = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = Ed.Label(label, 10, FontWeights.SemiBold, Ed.FgDim);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        l.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(l);
        var tint = system ? Color.FromRgb(0x0A, 0x84, 0xFF) : Color.FromRgb(0xBF, 0x5A, 0xF2);
        var path = system ? Take.SystemWav : Take.MicWav;
        var peaks = present ? Peaks(path, 220) : Array.Empty<float>();
        var lane = new AudioLaneControl(tint, peaks, () => system ? _doc.KeepsSystemAudio : _doc.KeepsMicrophone, () => system ? _doc.SystemAudioGain : _doc.MicrophoneGain,
            keep =>
            {
                PushUndo();
                if (system)
                {
                    _doc.KeepsSystemAudio = keep;
                }
                else
                {
                    _doc.KeepsMicrophone = keep;
                }

                _audio.Apply(_doc);
                Persist();
            },
            gain =>
            {
                PushUndo();
                if (system)
                {
                    _doc.SystemAudioGain = gain;
                }
                else
                {
                    _doc.MicrophoneGain = gain;
                }

                _audio.Apply(_doc);
                Persist();
            });
        Grid.SetColumn(lane, 1);
        grid.Children.Add(lane);
        return grid;
    }

    private static float[] Peaks(string path, int bins)
    {
        try
        {
            var data = WavWriter.ReadAll(path);
            var frames = data.Length / 2;
            var result = new float[bins];
            for (var b = 0; b < bins; b++)
            {
                var from = (long)b * frames / bins;
                var to = (long)(b + 1) * frames / bins;
                float max = 0;
                var step = Math.Max(1, (to - from) / 200);
                for (var i = from; i < to; i += step)
                {
                    max = Math.Max(max, Math.Max(Math.Abs(data[i * 2]), Math.Abs(data[i * 2 + 1])));
                }

                result[b] = Math.Min(1, max);
            }

            return result;
        }
        catch
        {
            return Array.Empty<float>();
        }
    }

    // ---- transport ---------------------------------------------------------------------------------------------------

    private void BuildTransport()
    {
        _transport.Children.Clear();
        _playBtn = new EdButton(_playing ? G.Pause : G.Play, null, "Space", TogglePlay, width: 26, height: 20, iconSize: 13);
        _transport.Children.Add(_playBtn);
        _timeLabel.Margin = new Thickness(12, 0, 12, 0);
        _transport.Children.Add(_timeLabel);
        _transport.Children.Add(Tool("", RS.AddText, () => AddTextAt(_src)));
        _transport.Children.Add(Tool("", RS.AddImage, () => AddImageAt(_src)));
        var blurTool = Tool(null, RS.AddBlur, () => AddBlurAt(_src));
        EdIcons.Prepend(blurTool, EdIcons.Mosaic(13, Ed.Fg));
        _transport.Children.Add(blurTool);
        if (_film.CutSelection is { } sel && sel.End - sel.Start >= 0.1)
        {
            var len = (int)Math.Round(sel.End - sel.Start);
            var can = _doc.OutputDuration(Duration) - (sel.End - sel.Start) >= 0.4;
            var cut = new EdButton(G.Scissors, $"{RS.CutOut}  {RecorderSupport.ElapsedLabel(len)}", null, CutSelection, height: 24) { IsEnabled = can, Margin = new Thickness(8, 0, 0, 0) };
            cut.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
            _transport.Children.Add(cut);
        }

        var spacer = new Border { Width = 40 };
        _transport.Children.Add(spacer);
        _transport.Children.Add(BuildSpeedButton());
        _transport.Children.Add(BuildQualityButton());
        UpdateTime();
    }

    private static EdButton Tool(string? glyph, string text, Action click)
    {
        var b = new EdButton(glyph, text, null, click, height: 24) { Margin = new Thickness(4, 0, 0, 0) };
        if (b.Label is { } l)
        {
            l.FontWeight = FontWeights.Medium;
            l.Foreground = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255));
        }

        return b;
    }

    private UIElement BuildQualityButton()
    {
        var quality = RecorderSupport.SanitizedQuality(_doc.Quality);
        var (ow, oh) = OutputSizeFor(quality);
        var name = quality == "small" ? RS.QualitySmall : quality == "high" ? RS.QualityHigh : RS.QualityBalanced;
        var b = new EdButton(G.Sliders, $"{name}  {ow} × {oh}", RS.Quality, null, iconSize: 12, height: 24) { Margin = new Thickness(10, 0, 0, 0) };
        if (b.Label is { } l)
        {
            l.FontWeight = FontWeights.Medium;
            l.FontSize = 12;
        }

        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Top };
            foreach (var (id, text) in new[] { ("small", RS.QualitySmall), ("balanced", RS.QualityBalanced), ("high", RS.QualityHigh) })
            {
                var item = new MenuItem { Header = text, IsCheckable = true, IsChecked = id == quality };
                var chosen = id;
                item.Click += (_, _) => Mutate(d => d.Quality = chosen, inspector: false, timeline: false, then: BuildTransport);
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };
        return b;
    }

    /// <summary>The size the export will have for a quality, folding backdrop and shape into the canvas.</summary>
    private (int W, int H) OutputSizeFor(string quality)
    {
        var plan = _plan;
        if (plan is not null)
        {
            var s = RecorderSupport.OutputScale(quality);
            return (RecorderSupport.EvenSize(plan.Canvas.Width * s), RecorderSupport.EvenSize(plan.Canvas.Height * s));
        }

        return (RecorderSupport.EvenSize(Take.Width * RecorderSupport.OutputScale(quality)), RecorderSupport.EvenSize(Take.Height * RecorderSupport.OutputScale(quality)));
    }

    private UIElement BuildSpeedButton()
    {
        var label = Fmt.Speed(_doc.ExportSpeed) + "×";
        var b = new EdButton(G.Timer, label, RS.ExportSpeed, null, iconSize: 12, height: 24) { Margin = new Thickness(10, 0, 0, 0), IsEnabled = _exportCts is null };
        if (b.Label is { } l)
        {
            l.FontWeight = FontWeights.Medium;
            l.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        }

        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_exportCts is not null)
            {
                return;
            }

            var pop = new Popup { PlacementTarget = b, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true };
            pop.Child = Ed.Capsule(BuildSpeedPopover(pop), 12, new Thickness(0));
            pop.IsOpen = true;
        };
        return b;
    }

    private void BuildTopRight(string? finished)
    {
        _topRight.Children.Clear();
        if (_progress is { } p)
        {
            _topRight.Children.Add(p);
        }
        else if (finished is not null)
        {
            var chip = new EdButton(G.Check, Path.GetFileName(finished), RS.RevealInExplorer, () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{finished}\""), height: 26);
            chip.Margin = new Thickness(0, 0, 8, 0);
            if (chip.Glyph is { } g)
            {
                g.Foreground = new SolidColorBrush(Color.FromRgb(0x32, 0xD7, 0x4B));
            }

            chip.MouseMove += (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && File.Exists(finished))
                {
                    var data = new DataObject();
                    data.SetFileDropList(new StringCollection { finished });
                    DragDrop.DoDragDrop(chip, data, DragDropEffects.Copy);
                }
            };
            _topRight.Children.Add(chip);
        }

        var busy = _exportCts is not null;
        _topRight.Children.Add(new EdButton(G.Trash, null, Strings.Recorder.DiscardTitle.Replace("?", string.Empty), Discard, width: 28, height: 26) { Margin = new Thickness(0, 0, 6, 0) });
        var copyDel = new EdButton(G.Copy, Loc.T("Copy and delete"), "Ctrl+Alt+C", () => _ = CopyAsync(false, true), height: 26) { IsEnabled = !busy, Margin = new Thickness(0, 0, 6, 0) };
        _topRight.Children.Add(copyDel);
        var copy = new EdButton(G.Copy, Strings.Screenshot.CopyLabel, "Ctrl+C", () => _ = CopyAsync(false, false), height: 26) { IsEnabled = !busy };
        var copyMore = new EdButton(G.ChevronDown, null, Loc.T("Copy as GIF"), null, iconSize: 8, width: 20, height: 26) { IsEnabled = !busy };
        copyMore.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            var item = new MenuItem { Header = Loc.T("Copy as GIF") };
            item.Click += (_, _) => _ = CopyAsync(true, false);
            menu.Items.Add(item);
            menu.PlacementTarget = copyMore;
            menu.IsOpen = true;
        };
        var pair = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        pair.Children.Add(copy);
        pair.Children.Add(copyMore);
        _topRight.Children.Add(pair);
        var save = new EdButton(G.Save, Strings.Screenshot.SaveLabel, "Ctrl+S", () => _ = SaveAsync(SaveKind.Mp4, false), prominent: true, height: 26) { IsEnabled = !busy };
        var saveMore = new EdButton(G.ChevronDown, null, null, null, iconSize: 8, width: 22, height: 26, prominent: true) { IsEnabled = !busy, Margin = new Thickness(2, 0, 0, 0) };
        saveMore.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            MenuItem Add(string header, Action a, string? gesture = null)
            {
                var mi = new MenuItem { Header = header, InputGestureText = gesture };
                mi.Click += (_, _) => a();
                menu.Items.Add(mi);
                return mi;
            }

            Add(Strings.Screenshot.SaveLabel, () => _ = SaveAsync(SaveKind.Mp4, false), "Ctrl+S");
            Add(Strings.Screenshot.SaveAs, () => _ = SaveAsync(SaveKind.Mp4, true), "Ctrl+Shift+S");
            Add(Loc.T("Save as GIF"), () => _ = SaveAsync(SaveKind.Gif, false));
            menu.Items.Add(new Separator());
            Add(Strings.SaveTo + "…", ChooseFolder);
            menu.PlacementTarget = saveMore;
            menu.IsOpen = true;
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(save);
        sp.Children.Add(saveMore);
        _topRight.Children.Add(sp);
    }

    // ---- refresh ---------------------------------------------------------------------------------------------------------

    private void RefreshAll()
    {
        _film.Document = _doc;
        _film.InvalidateVisual();
        RefreshLanes();
        BuildTransport();
        BuildInspector();
        BuildTopRight(_exported ? _lastExported : null);
        _undoBtn.IsEnabled = _undo.Count > 0;
        _redoBtn.IsEnabled = _redo.Count > 0;
    }

    /// <summary>After a committed change: persist, redraw the picture after a beat, and refresh only what the change touches.</summary>
    private void Commit(bool inspector, bool timeline)
    {
        Persist();
        ScheduleRebuild();
        RefreshLanes();
        if (timeline)
        {
            _film.InvalidateVisual();
            BuildTransport();
        }

        if (inspector)
        {
            BuildInspector();
        }

        _undoBtn.IsEnabled = _undo.Count > 0;
        _redoBtn.IsEnabled = _redo.Count > 0;
    }

    /// <summary>One undoable change to the document (a drag or a slider drag is one step) followed by <see cref="Commit"/>.</summary>
    private void Mutate(Action<EditDocument> change, bool inspector = false, bool timeline = false, Action? then = null)
    {
        PushUndo();
        change(_doc);
        Commit(inspector, timeline);
        then?.Invoke();
    }

    // ---- playback ---------------------------------------------------------------------------------------------------------

    private void TogglePlay()
    {
        if (_playing)
        {
            _media.Pause();
            _audio.Pause();
            _playing = false;
        }
        else
        {
            var (s, e) = RecorderSupport.SanitizedTrim(_doc.TrimStart, _doc.TrimEnd, Duration);
            if (_src >= e - 0.05 || _src < s)
            {
                Seek(s);
            }

            _media.Play();
            _audio.Seek(_src);
            _audio.Play();
            _playing = true;
        }

        BuildTransport();
    }

    private void Seek(double sourceTime)
    {
        _src = Math.Clamp(sourceTime, 0, Duration);
        _media.Position = TimeSpan.FromSeconds(_src);
        _audio.Seek(_src);
        SyncPlayheads();
        UpdateTime();
        UpdateStage();
    }

    private void SyncPlayheads()
    {
        _film.Playhead = _src;
        _ruler.Playhead = _src;
        foreach (var lane in Lanes)
        {
            lane.Playhead = _src;
            lane.InvalidateVisual();
        }

        _film.InvalidateVisual();
        _ruler.InvalidateVisual();
    }

    private void Tick()
    {
        if (!_playing)
        {
            return;
        }

        _src = _media.Position.TotalSeconds;
        var (s, e) = RecorderSupport.SanitizedTrim(_doc.TrimStart, _doc.TrimEnd, Duration);
        if (_src >= e - 0.02)
        {
            _media.Pause();
            _audio.Pause();
            _playing = false;
            Seek(s);
            BuildTransport();
            return;
        }

        foreach (var c in _doc.Cuts)
        {
            if (_src >= c.Start && _src < c.End)
            {
                Seek(c.End);
                return;
            }
        }

        _audio.Follow(_src);
        SyncPlayheads();
        UpdateTime();
        UpdateStage();
    }

    private void UpdateTime()
    {
        var outTime = _doc.OutputTime(_src, Duration) ?? 0;
        var total = _doc.OutputDuration(Duration);
        _timeLabel.Text = $"{RecorderSupport.ElapsedLabel(outTime)} / {RecorderSupport.ElapsedLabel(Math.Round(total))}";
    }

    private async Task LoadThumbsAsync()
    {
        try
        {
            var source = await FrameSource.OpenAsync(Take.VideoPath);
            var thumbs = new BitmapSource?[14];
            for (var i = 0; i < 14; i++)
            {
                try
                {
                    var f = await source.GetFrameAsync(TimeSpan.FromSeconds((i + 0.5) / 14 * Duration));
                    thumbs[i] = Output.Thumbnail(f, 240);
                }
                catch
                {
                    thumbs[i] = null;
                }

                _film.SetThumbs(thumbs);
            }
        }
        catch
        {
            // the film simply stays plain
        }
    }

    // ---- undo / redo ------------------------------------------------------------------------------------------------------

    private bool _interactionOpen;

    private void PushUndo()
    {
        if (_interactionOpen)
        {
            return;
        }

        _undo.Add(JsonSerializer.Serialize(_doc));
        if (_undo.Count > 100)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _undoBtn.IsEnabled = true;
        _redoBtn.IsEnabled = false;
        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            _interactionOpen = true;
        }
    }

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(JsonSerializer.Serialize(_doc));
        Apply(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(JsonSerializer.Serialize(_doc));
        Apply(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
    }

    private void Apply(string json)
    {
        _doc = JsonSerializer.Deserialize<EditDocument>(json)!;
        Take.Document = _doc;
        _audio.Apply(_doc);
        Select(SelKind.None, null, rebuild: false);
        Persist();
        RefreshAll();
        RebuildPreview();
        Seek(_doc.TrimStart);
    }

    private void Persist() => Take.SaveDocument();

    private void CutSelection()
    {
        if (_film.CutSelection is not { } sel || sel.End - sel.Start < 0.1)
        {
            return;
        }

        if (_doc.OutputDuration(Duration) - (sel.End - sel.Start) < 0.4)
        {
            return;
        }

        PushUndo();
        _doc.Cuts.Add(new CutRange { Start = sel.Start, End = sel.End });
        _film.CutSelection = null;
        Commit(inspector: false, timeline: true);
        Seek(sel.Start);
    }

    // ---- export ------------------------------------------------------------------------------------------------------------------

    private enum SaveKind { Mp4, Gif }

    private string Folder() => Output.BaseFolder(CaptureHost.App.Get<string>(K.RecSaveFolder));

    private void ChooseFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Folder() };
        if (dlg.ShowDialog(this) == true)
        {
            CaptureHost.App.Set(K.RecSaveFolder, dlg.FolderName);
        }
    }

    private async Task SaveAsync(SaveKind kind, bool askPath)
    {
        if (_exportCts is not null)
        {
            return;
        }

        var ext = kind == SaveKind.Gif ? ".gif" : ".mp4";
        var dest = Output.UniquePath(Folder(), RecorderSupport.DefaultName(DateTime.Now), ext);
        if (askPath)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = kind == SaveKind.Gif ? "GIF image|*.gif" : "MP4 video|*.mp4", FileName = Path.GetFileName(dest), InitialDirectory = Folder() };
            if (dlg.ShowDialog(this) != true)
            {
                return;
            }

            dest = dlg.FileName;
        }

        if (await RunExportAsync(kind, dest))
        {
            _exported = true;
            _lastExported = dest;
            _service.RecordInHistory(dest, Take);
            QuickToolHud.Show(G.Record, string.Format(Strings.Recorder.SavedFormat, Path.GetFileName(Path.GetDirectoryName(dest)) ?? Strings.Desktop));
            BuildTopRight(dest);
        }
    }

    private async Task<bool> RunExportAsync(SaveKind kind, string dest)
    {
        var cts = new CancellationTokenSource();
        _exportCts = cts;
        var bar = new ProgressBar { Width = 90, Height = 4, Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(bar);
        var lbl = Ed.Label(Strings.Recorder.Saving, 11, FontWeights.Medium, Ed.FgDim);
        lbl.Margin = new Thickness(8, 0, 8, 0);
        row.Children.Add(lbl);
        row.Children.Add(new EdButton(null, Strings.Cancel, null, () => cts.Cancel(), height: 22));
        _progress = new Border { Child = row, Background = Ed.Plate, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        BuildTopRight(null);
        BuildTransport();
        var progress = new Progress<double>(v => bar.Value = v);
        var ok = false;
        try
        {
            _doc.Sanitize(Duration);
            Take.Document = _doc;
            if (kind == SaveKind.Gif)
            {
                await RecorderExporter.ExportGifAsync(Take, dest, progress, cts.Token);
            }
            else
            {
                await RecorderExporter.ExportMp4Async(Take, dest, progress, cts.Token);
            }

            ok = true;
        }
        catch (OperationCanceledException)
        {
            // silent
        }
        catch (Exception ex)
        {
            CaptureHost.Log("export failed", ex);
            CaptureHost.Beep();
            QuickToolHud.Show(G.Record, Strings.Recorder.ExportFailed);
        }
        finally
        {
            _exportCts = null;
            _progress = null;
            BuildTopRight(_exported ? _lastExported : null);
            BuildTransport();
        }

        return ok;
    }

    private async Task CopyAsync(bool gif, bool thenDelete)
    {
        if (_exportCts is not null)
        {
            return;
        }

        var dir = Path.Combine(Output.DataRoot(), "Copied Recordings");
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-24))
            {
                try
                {
                    File.Delete(f);
                }
                catch
                {
                    // in use
                }
            }
        }

        var dest = Output.UniquePath(dir, RecorderSupport.DefaultName(DateTime.Now), gif ? ".gif" : ".mp4");
        if (!await RunExportAsync(gif ? SaveKind.Gif : SaveKind.Mp4, dest))
        {
            return;
        }

        try
        {
            var data = new DataObject();
            if (gif && new FileInfo(dest).Length <= 64L * 1024 * 1024)
            {
                data.SetData("GIF", new MemoryStream(await File.ReadAllBytesAsync(dest)));
            }

            data.SetFileDropList(new StringCollection { dest });
            Output.SetClipboardRetry(data);
            QuickToolHud.Show(G.Copy, Strings.Recorder.Copied);
            if (thenDelete)
            {
                _forceClose = true;
                Close();
            }
        }
        catch
        {
            CaptureHost.Beep();
        }
    }

    // ---- closing ------------------------------------------------------------------------------------------------------------------

    private void Discard()
    {
        if (ConfirmDiscard())
        {
            _forceClose = true;
            Close();
        }
    }

    private bool ConfirmDiscard()
    {
        var msg = _exported ? Strings.Recorder.DiscardSavedMessage : Strings.Recorder.DiscardMessage;
        return MessageBox.Show(this, msg, Strings.Recorder.DiscardTitle, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose || _exported)
        {
            return;
        }

        if (!ConfirmDiscard())
        {
            e.Cancel = true;
        }
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var typing = Keyboard.FocusedElement is TextBox or ComboBox;
        if (typing && !(ctrl && e.Key == Key.S))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.S when ctrl:
                _ = SaveAsync(SaveKind.Mp4, shift);
                break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                Redo();
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.C when ctrl && alt:
                _ = CopyAsync(false, true);
                break;
            case Key.C when ctrl:
                _ = CopyAsync(false, false);
                break;
            case Key.Space:
                TogglePlay();
                break;
            case Key.Escape:
                if (_exportCts is not null)
                {
                    _exportCts.Cancel();
                }
                else if (_aiming)
                {
                    EndAim();
                }
                else if (_pickingBlur)
                {
                    EndPick();
                    BuildInspector();
                }
                else if (_selKind != SelKind.None)
                {
                    Select(SelKind.None, null);
                }
                else
                {
                    return;
                }

                break;
            case Key.Delete or Key.Back:
                if (_film.CutSelection is { } s && s.End - s.Start >= 0.1)
                {
                    CutSelection();
                }
                else if (_selId is { } id)
                {
                    RemoveSelected(id);
                }
                else
                {
                    Discard();
                }

                break;
            default:
                return;
        }

        e.Handled = true;
    }
}

internal static class Fmt
{
    /// <summary>A speed as 0 to 2 fraction digits: 1, 1.25, 0.5.</summary>
    public static string Speed(double v) => Math.Round(v, 2).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
}
