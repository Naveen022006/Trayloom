// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Specialized;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Threading;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Capture;

/// <summary>The recording editor: stage, timeline (filmstrip with trim and cuts, blur lane, audio lanes), inspector, export (MP4 / GIF).</summary>
internal sealed class RecorderEditorWindow : Window
{
    private readonly RecorderService _service;
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private EditDocument _doc;
    private readonly MediaElement _media = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, ScrubbingEnabled = true, Stretch = Stretch.Fill };
    private readonly Canvas _blurCanvas = new();
    private readonly Grid _picture = new();
    private readonly FilmstripControl _film = new();
    private readonly BlurLaneControl _lane = new();
    private readonly StackPanel _inspector = new();
    private readonly TextBlock _timeLabel = Ed.Label(string.Empty, 12, FontWeights.Medium, Ed.FgDim);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Border _hint = new();
    private readonly StackPanel _topRight = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _transport = new() { Orientation = Orientation.Horizontal };
    private EdButton _playBtn = null!;
    private EdButton _undoBtn = null!;
    private EdButton _redoBtn = null!;
    private bool _playing;
    private double _src;
    private bool _pickingBlur;
    private Point _pickStart;
    private Rectangle? _pickRect;
    private CancellationTokenSource? _exportCts;
    private string? _lastExported;
    private bool _exported;
    private bool _forceClose;

    public RecorderEditorWindow(RecorderService service, Take take)
    {
        _service = service;
        Take = take;
        _doc = take.Document;
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
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _exportCts?.Cancel();
            _media.Close();
            Take.Delete();
        };
        Loaded += async (_, _) => await LoadThumbsAsync();
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
        top.Children.Add(left);
        _topRight.HorizontalAlignment = HorizontalAlignment.Right;
        _topRight.VerticalAlignment = VerticalAlignment.Center;
        _topRight.Margin = new Thickness(0, 0, 14, 0);
        top.Children.Add(_topRight);
        BuildTopRight(null);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        // middle
        var mid = new Grid();
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(272) });
        var stageHost = new Border { Margin = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(184, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)), BorderThickness = new Thickness(1), ClipToBounds = true };
        _picture.Width = Math.Max(2, Take.Width);
        _picture.Height = Math.Max(2, Take.Height);
        _media.Width = _picture.Width;
        _media.Height = _picture.Height;
        _blurCanvas.Width = _picture.Width;
        _blurCanvas.Height = _picture.Height;
        _picture.Children.Add(_media);
        _picture.Children.Add(_blurCanvas);
        var viewbox = new Viewbox { Child = _picture, Stretch = Stretch.Uniform };
        var stageGrid = new Grid();
        stageGrid.Children.Add(viewbox);
        _hint.CornerRadius = new CornerRadius(14);
        _hint.Background = Ed.Plate;
        _hint.Padding = new Thickness(10, 5, 10, 5);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.VerticalAlignment = VerticalAlignment.Top;
        _hint.Margin = new Thickness(0, 10, 0, 0);
        _hint.Visibility = Visibility.Collapsed;
        _hint.IsHitTestVisible = false;
        stageGrid.Children.Add(_hint);
        stageHost.Child = stageGrid;
        _blurCanvas.Background = Brushes.Transparent;
        _blurCanvas.MouseLeftButtonDown += StageDown;
        _blurCanvas.MouseMove += StageMove;
        _blurCanvas.MouseLeftButtonUp += StageUp;
        mid.Children.Add(stageHost);
        var insp = new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { Content = _inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(16, 14, 16, 14) } };
        Grid.SetColumn(insp, 1);
        mid.Children.Add(insp);
        Grid.SetRow(mid, 1);
        root.Children.Add(mid);

        // bottom band
        var bottom = new StackPanel { Margin = new Thickness(18, 10, 18, 16), Background = Brushes.Transparent };
        BuildTransport();
        bottom.Children.Add(_transport);
        bottom.Children.Add(LaneRow("Blur", _lane));
        bottom.Children.Add(AudioRow(Strings.Recorder.EditorTitle == "" ? string.Empty : "PC sound", Take.HasSystemAudio, true));
        bottom.Children.Add(AudioRow("Microphone", Take.HasMicrophone, false));
        bottom.Children.Add(LaneRow(Strings.Recorder.EditorTitle, _film));
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        Content = root;

        _film.Document = _doc;
        _lane.Document = _doc;
        _film.Duration = Duration;
        _lane.Duration = Duration;
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
            Persist();
            RefreshAll();
        };
        _film.RestoreCutAt += start =>
        {
            PushUndo();
            _doc.Cuts.RemoveAll(c => Math.Abs(c.Start - start) < 0.15);
            Persist();
            RefreshAll();
        };
        _film.CutSelectionChanged += BuildTransport;
        _lane.AddRequested += AddBlurAt;
        _lane.SelectionChanged += () =>
        {
            BuildInspector();
            UpdateBlurOverlays();
        };
        _lane.BeginEdit += PushUndo;
        _lane.Edited += () =>
        {
            Persist();
            UpdateBlurOverlays();
        };
        _lane.RemoveRequested += RemoveBlur;
    }

    private bool _trimPushed;

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

                Persist();
            },
            gain =>
            {
                if (system)
                {
                    _doc.SystemAudioGain = gain;
                }
                else
                {
                    _doc.MicrophoneGain = gain;
                }

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

    private void BuildTransport()
    {
        _transport.Children.Clear();
        _playBtn = new EdButton(_playing ? G.Pause : G.Play, null, "Space", TogglePlay, width: 26, height: 20, iconSize: 13);
        _transport.Children.Add(_playBtn);
        _timeLabel.Margin = new Thickness(12, 0, 12, 0);
        _transport.Children.Add(_timeLabel);
        _transport.Children.Add(new EdButton(G.Image, "Blur an area", null, () => AddBlurAt(_src), height: 24) { Margin = new Thickness(4, 0, 0, 0) });
        if (_film.CutSelection is { } sel && sel.End - sel.Start >= 0.1)
        {
            var len = (int)Math.Round(sel.End - sel.Start);
            var can = _doc.OutputDuration(Duration) - (sel.End - sel.Start) >= 0.4;
            var cut = new EdButton(G.Scissors, $"Cut out  {RecorderSupport.ElapsedLabel(len)}", null, CutSelection, height: 24) { IsEnabled = can, Margin = new Thickness(8, 0, 0, 0) };
            cut.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
            _transport.Children.Add(cut);
        }

        var quality = RecorderSupport.SanitizedQuality(_doc.Quality);
        var (ow, oh) = (RecorderSupport.EvenSize(Take.Width * RecorderSupport.OutputScale(quality)), RecorderSupport.EvenSize(Take.Height * RecorderSupport.OutputScale(quality)));
        var qLabel = Ed.Label($"{(quality == "small" ? "Small file" : quality == "high" ? "High" : "Balanced")}  {ow} × {oh}", 12, FontWeights.Medium, Ed.FgDim);
        qLabel.Margin = new Thickness(16, 0, 0, 0);
        var spacer = new Border { Width = Math.Max(10, 300) };
        _transport.Children.Add(spacer);
        _transport.Children.Add(qLabel);
        UpdateTime();
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
            var chip = new EdButton(G.Check, Path.GetFileName(finished), "Reveal in File Explorer", () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{finished}\""), height: 26);
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
        var copyDel = new EdButton(G.Copy, "Copy and delete", "Ctrl+Alt+C", () => _ = CopyAsync(false, true), height: 26) { IsEnabled = !busy, Margin = new Thickness(0, 0, 6, 0) };
        _topRight.Children.Add(copyDel);
        var copy = new EdButton(G.Copy, Strings.Screenshot.CopyLabel, "Ctrl+C", () => _ = CopyAsync(false, false), height: 26) { IsEnabled = !busy };
        var copyMore = new EdButton(G.ChevronDown, null, Strings.Recorder.GifSize == "" ? null : "Copy as GIF", null, iconSize: 8, width: 20, height: 26) { IsEnabled = !busy };
        copyMore.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            var item = new MenuItem { Header = "Copy as GIF" };
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
            Add("Save as GIF", () => _ = SaveAsync(SaveKind.Gif, false));
            menu.Items.Add(new Separator());
            Add(Strings.SaveTo, ChooseFolder);
            menu.PlacementTarget = saveMore;
            menu.IsOpen = true;
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(save);
        sp.Children.Add(saveMore);
        _topRight.Children.Add(sp);
    }

    // ---- inspector ----------------------------------------------------------------------------------------------------

    private void BuildInspector()
    {
        _inspector.Children.Clear();
        if (_lane.Selected is { } id && _doc.Blurs.FirstOrDefault(b => b.Id == id) is { } blur)
        {
            var back = new EdButton(null, "‹ All options", "Esc", () =>
            {
                _lane.Selected = null;
                _lane.InvalidateVisual();
                BuildInspector();
                UpdateBlurOverlays();
            }, height: 22);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            if (back.Label is { } bl)
            {
                bl.Foreground = Ed.Accent;
            }

            _inspector.Children.Add(back);
            _inspector.Children.Add(Section("THIS BLUR"));
            var choose = new EdButton("", "Choose the area", null, () => StartPick(blur), prominent: true, height: 28) { IsEnabled = !_pickingBlur };
            choose.HorizontalAlignment = HorizontalAlignment.Stretch;
            _inspector.Children.Add(choose);
            var cap = Ed.Label(_pickingBlur ? "Drag over what should stay hidden" : "Hidden for as long as its block lasts on the timeline.", 10.5, FontWeights.Normal, Ed.FgDim);
            cap.TextWrapping = TextWrapping.Wrap;
            cap.Margin = new Thickness(0, 6, 0, 12);
            _inspector.Children.Add(cap);
            _inspector.Children.Add(PopoverKit.LabeledSlider("Blur strength", 1, 5, blur.Strength, v =>
            {
                blur.Strength = (int)Math.Round(v);
                Persist();
                UpdateBlurOverlays();
            }, true, 90, "{0:0}", 1));
            var rm = new EdButton(null, Strings.Remove, null, () => RemoveBlur(blur.Id), height: 26);
            rm.Margin = new Thickness(0, 12, 0, 0);
            _inspector.Children.Add(rm);
            return;
        }

        _inspector.Children.Add(Section("EXPORT"));
        var qualities = new[] { ("small", "Small file"), ("balanced", "Balanced"), ("high", "High") };
        _inspector.Children.Add(Combo("Quality", qualities.Select(q => q.Item2).ToArray(), Array.FindIndex(qualities, q => q.Item1 == _doc.Quality), i =>
        {
            PushUndo();
            _doc.Quality = qualities[i].Item1;
            Persist();
            BuildTransport();
        }));
        var speeds = new[] { 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4 };
        _inspector.Children.Add(Combo("Export speed", speeds.Select(s => s.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×").ToArray(), Math.Max(0, Array.FindIndex(speeds, s => Math.Abs(s - _doc.ExportSpeed) < 0.01)), i =>
        {
            PushUndo();
            _doc.ExportSpeed = speeds[i];
            Persist();
            UpdateTime();
        }));
        var cap2 = Ed.Label("Applies to video, GIF and shared links. The editing preview stays at 1×; the original recording is unchanged.", 10.5, FontWeights.Normal, Ed.FgDim);
        cap2.TextWrapping = TextWrapping.Wrap;
        cap2.Margin = new Thickness(0, 4, 0, 12);
        _inspector.Children.Add(cap2);
        var sizes = new[] { "small", "medium", "large" };
        _inspector.Children.Add(Combo(Strings.Recorder.GifSize, ["Small", "Medium", "Large"], Math.Max(0, Array.IndexOf(sizes, _doc.GifSize)), i =>
        {
            _doc.GifSize = sizes[i];
            Persist();
        }));
        var gfps = new[] { 8, 12, 15 };
        _inspector.Children.Add(Combo(Strings.Recorder.GifSmoothness, gfps.Select(f => f + " fps").ToArray(), Math.Max(0, Array.IndexOf(gfps, _doc.GifFrameRate)), i =>
        {
            _doc.GifFrameRate = gfps[i];
            Persist();
        }));
        _inspector.Children.Add(Section("EDIT"));
        var tips = Ed.Label("Drag the film's end handles to trim. Hold Shift and drag across the film to pick a part to cut out. Click an orange seam to restore a cut. Use the Blur lane to hide something for a while.", 10.5, FontWeights.Normal, Ed.FgDim);
        tips.TextWrapping = TextWrapping.Wrap;
        _inspector.Children.Add(tips);
    }

    private static UIElement Section(string title)
    {
        var t = Ed.Label(title, 11, FontWeights.SemiBold, Ed.FgDim);
        t.Margin = new Thickness(0, 12, 0, 8);
        return t;
    }

    private static UIElement Combo(string label, string[] items, int selected, Action<int> changed)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Ed.Label(label, 12));
        var combo = new ComboBox { Width = 110, Height = 24, FontSize = 12 };
        foreach (var i in items)
        {
            combo.Items.Add(i);
        }

        combo.SelectedIndex = Math.Max(0, selected);
        combo.SelectionChanged += (_, _) => changed(combo.SelectedIndex);
        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);
        return grid;
    }

    // ---- playback ---------------------------------------------------------------------------------------------------------

    private void RefreshAll()
    {
        _film.Document = _doc;
        _lane.Document = _doc;
        _film.InvalidateVisual();
        _lane.InvalidateVisual();
        BuildTransport();
        BuildInspector();
        BuildTopRight(_exported ? _lastExported : null);
        UpdateBlurOverlays();
        _undoBtn.IsEnabled = _undo.Count > 0;
        _redoBtn.IsEnabled = _redo.Count > 0;
    }

    private void TogglePlay()
    {
        if (_playing)
        {
            _media.Pause();
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
            _playing = true;
        }

        BuildTransport();
    }

    private void Seek(double sourceTime)
    {
        _src = Math.Clamp(sourceTime, 0, Duration);
        _media.Position = TimeSpan.FromSeconds(_src);
        _film.Playhead = _src;
        _lane.Playhead = _src;
        _film.InvalidateVisual();
        _lane.InvalidateVisual();
        UpdateTime();
        UpdateBlurOverlays();
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

        _film.Playhead = _src;
        _lane.Playhead = _src;
        _film.InvalidateVisual();
        _lane.InvalidateVisual();
        UpdateTime();
        UpdateBlurOverlays();
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

    // ---- blur ---------------------------------------------------------------------------------------------------------------

    private void AddBlurAt(double time)
    {
        PushUndo();
        var start = Math.Clamp(time, 0, Math.Max(0, Duration - 0.4));
        var blur = new BlurRegion { Start = start, End = Duration };
        _doc.Blurs.Add(blur);
        _lane.Selected = blur.Id;
        Persist();
        RefreshAll();
        StartPick(blur);
    }

    private void RemoveBlur(Guid id)
    {
        PushUndo();
        _doc.Blurs.RemoveAll(b => b.Id == id);
        if (_lane.Selected == id)
        {
            _lane.Selected = null;
        }

        _pickingBlur = false;
        Persist();
        RefreshAll();
    }

    private BlurRegion? _pickTarget;

    private void StartPick(BlurRegion blur)
    {
        _pickingBlur = true;
        _pickTarget = blur;
        if (_playing)
        {
            TogglePlay();
        }

        _hint.Child = Ed.Label("Drag over what should stay hidden", 11, FontWeights.Medium);
        _hint.Visibility = Visibility.Visible;
        _blurCanvas.Cursor = Cursors.Cross;
        BuildInspector();
    }

    private void StageDown(object sender, MouseButtonEventArgs e)
    {
        if (!_pickingBlur)
        {
            TogglePlay();
            return;
        }

        _pickStart = e.GetPosition(_blurCanvas);
        _pickRect = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(56, 0x30, 0xB0, 0xC7)), Stroke = new SolidColorBrush(Color.FromRgb(0x30, 0xB0, 0xC7)), StrokeThickness = 3 };
        _blurCanvas.Children.Add(_pickRect);
        _blurCanvas.CaptureMouse();
    }

    private void StageMove(object sender, MouseEventArgs e)
    {
        if (_pickRect is null)
        {
            return;
        }

        var p = e.GetPosition(_blurCanvas);
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

        var p = e.GetPosition(_blurCanvas);
        _blurCanvas.ReleaseMouseCapture();
        _blurCanvas.Children.Remove(_pickRect);
        _pickRect = null;
        var w = _picture.Width;
        var h = _picture.Height;
        var x1 = Math.Clamp(Math.Min(_pickStart.X, p.X) / w, 0, 1);
        var y1 = Math.Clamp(Math.Min(_pickStart.Y, p.Y) / h, 0, 1);
        var x2 = Math.Clamp(Math.Max(_pickStart.X, p.X) / w, 0, 1);
        var y2 = Math.Clamp(Math.Max(_pickStart.Y, p.Y) / h, 0, 1);
        if (Math.Abs(p.X - _pickStart.X) < 2 && Math.Abs(p.Y - _pickStart.Y) < 2 || _pickTarget is null)
        {
            return;
        }

        PushUndo();
        _pickTarget.X = x1;
        _pickTarget.Y = y1;
        _pickTarget.Width = Math.Max(0.01, x2 - x1);
        _pickTarget.Height = Math.Max(0.01, y2 - y1);
        EndPick();
        Persist();
        RefreshAll();
    }

    private void EndPick()
    {
        _pickingBlur = false;
        _pickTarget = null;
        _hint.Visibility = Visibility.Collapsed;
        _blurCanvas.Cursor = Cursors.Arrow;
    }

    private void UpdateBlurOverlays()
    {
        for (var i = _blurCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (_blurCanvas.Children[i] is FrameworkElement { Tag: "blur" })
            {
                _blurCanvas.Children.RemoveAt(i);
            }
        }

        if (_pickingBlur)
        {
            return;
        }

        foreach (var b in _doc.Blurs.Where(b => _src >= b.Start && _src <= b.End))
        {
            var w = _picture.Width;
            var h = _picture.Height;
            var rect = new Rectangle
            {
                Tag = "blur", Width = b.Width * w, Height = b.Height * h, IsHitTestVisible = false,
                Fill = new VisualBrush(_media) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Viewbox = new Rect(b.X, b.Y, b.Width, b.Height) },
                Effect = new BlurEffect { Radius = Math.Max(6, Math.Min(b.Width * w, b.Height * h) / 6 * (b.Strength / 3.0)), KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
                Stroke = b.Id == _lane.Selected ? new SolidColorBrush(Color.FromRgb(0x30, 0xB0, 0xC7)) : null, StrokeThickness = 2,
            };
            Canvas.SetLeft(rect, b.X * w);
            Canvas.SetTop(rect, b.Y * h);
            _blurCanvas.Children.Add(rect);
        }
    }

    // ---- edits ----------------------------------------------------------------------------------------------------------------

    private void PushUndo()
    {
        _undo.Add(JsonSerializer.Serialize(_doc));
        if (_undo.Count > 100)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _undoBtn.IsEnabled = true;
        _redoBtn.IsEnabled = false;
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
        _lane.Selected = null;
        Persist();
        RefreshAll();
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
        Persist();
        RefreshAll();
        Seek(sel.Start);
    }

    // ---- export ------------------------------------------------------------------------------------------------------------------

    private enum SaveKind { Mp4, Gif }

    private Border? _progress;

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
        var progress = new Progress<double>(v => bar.Value = v);
        var ok = false;
        try
        {
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
        if (Keyboard.FocusedElement is TextBox or ComboBox)
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
                else if (_pickingBlur)
                {
                    EndPick();
                    BuildInspector();
                }
                else if (_lane.Selected is not null)
                {
                    _lane.Selected = null;
                    RefreshAll();
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
                else if (_lane.Selected is { } id)
                {
                    RemoveBlur(id);
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

/// <summary>An audio track row: waveform, volume slider, percent and keep / remove toggle.</summary>
internal sealed class AudioLaneControl : Border
{
    public AudioLaneControl(Color tint, float[] peaks, Func<bool> keeps, Func<double> gain, Action<bool> setKeep, Action<double> setGain)
    {
        CornerRadius = new CornerRadius(7);
        BorderThickness = new Thickness(1);
        var grid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        var wave = new WaveformControl(peaks, tint);
        grid.Children.Add(wave);
        var percent = Ed.Label($"{Math.Round(gain() * 100)}%", 10, FontWeights.Medium, Ed.FgDim);
        percent.HorizontalAlignment = HorizontalAlignment.Right;
        var slider = Ed.NewSlider(0, 1, gain(), 92, v =>
        {
            percent.Text = $"{Math.Round(v * 100)}%";
            setGain(v);
        });
        slider.ToolTip = "Volume";
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(percent, 2);
        grid.Children.Add(slider);
        grid.Children.Add(percent);
        var toggle = new EdButton(G.Close, null, Strings.Remove, null, iconSize: 10, width: 18, height: 18);
        void Paint()
        {
            var kept = keeps();
            Background = new SolidColorBrush(Color.FromArgb(kept ? (byte)28 : (byte)10, tint.R, tint.G, tint.B));
            BorderBrush = new SolidColorBrush(Color.FromArgb(kept ? (byte)71 : (byte)26, tint.R, tint.G, tint.B));
            wave.Opacity = kept ? 1 : 0.3;
            slider.IsEnabled = kept;
            if (toggle.Glyph is { } g)
            {
                g.Text = kept ? G.Close : G.Undo;
            }

            toggle.ToolTip = kept ? Strings.Remove : "Restore";
        }

        toggle.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            setKeep(!keeps());
            Paint();
        };
        Grid.SetColumn(toggle, 3);
        grid.Children.Add(toggle);
        Child = grid;
        Paint();
    }
}

internal sealed class WaveformControl : FrameworkElement
{
    private readonly float[] _peaks;
    private readonly Color _tint;

    public WaveformControl(float[] peaks, Color tint)
    {
        _peaks = peaks;
        _tint = tint;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var brush = new SolidColorBrush(Color.FromArgb(204, _tint.R, _tint.G, _tint.B));
        if (_peaks.Length == 0)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(89, _tint.R, _tint.G, _tint.B)), null, new Rect(0, h / 2, w, 1));
            return;
        }

        var slot = w / _peaks.Length;
        for (var i = 0; i < _peaks.Length; i++)
        {
            var bar = Math.Max(1, _peaks[i] * (h - 8));
            dc.DrawRoundedRectangle(brush, null, new Rect(i * slot + slot * 0.16, (h - bar) / 2, slot * 0.68, bar), 0.7, 0.7);
        }
    }
}
