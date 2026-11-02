// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Maintenance;

/// <summary>Selection that survives closing and reopening the workspace (spec 7.5.5 MediaWorkspaceSelection).</summary>
internal static class MediaSession
{
    public static MediaTool Tool { get; set; } = MediaTool.Video;

    public static bool Initialised { get; set; }

    public static List<string> Inputs { get; set; } = [];

    public static ImageInfo? ImageInfo { get; set; }

    public static string? Output { get; set; }

    public static bool OutputManual { get; set; }

    public static double Duration { get; set; }

    public static string? OutputDirectory { get; set; }

    public static ImageOptions Options { get; set; } = new();
}

/// <summary>The media workspace: tool picker, file card, options card, action row and status card (spec 7.5.2).</summary>
internal sealed class MediaWorkspaceView : StackPanel
{
    private readonly MediaService _svc = MediaService.Shared;
    private readonly bool _compact;
    private readonly Action? _relayout;
    private readonly Action<bool>? _keepOpen;
    private readonly Action? _close;
    private readonly ContentControl _file = new();
    private readonly ContentControl _options = new();
    private readonly ContentControl _actions = new();
    private readonly ContentControl _status = new();
    private TextBlock? _outputLabel;
    private ContentControl? _preview;
    private TextBlock? _previewOutput;
    private bool _dragOver;
    private bool _moreOpen;
    private BitmapSource? _thumb;
    private string? _thumbFor;
    private string _profileName = string.Empty;
    private string? _localMessage;
    private int _durationGeneration;

    public MediaWorkspaceView(bool compact, Action? relayout = null, Action<bool>? keepOpen = null, Action? close = null)
    {
        _compact = compact;
        _relayout = relayout;
        _keepOpen = keepOpen;
        _close = close;
        if (!MediaSession.Initialised)
        {
            MediaSession.Initialised = true;
            MediaSession.Tool = Ms.LastTool;
        }

        MediaSession.Options = ImageOptions.FromSettings();
        if (FirstInput is not null && MediaSession.Output is null)
        {
            PrepareInputs();
        }

        var spacing = compact ? 10.0 : 14.0;

        if (!compact)
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(V.Label(G.Photo, "Media", 16, FontWeights.SemiBold, ThemeKeys.Accent, ThemeKeys.TextPrimary, 9));
            var note = UiKit.Text("Local. No network.", 11, FontWeights.Medium, ThemeKeys.TextSecondary);
            note.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(note, 1);
            header.Children.Add(note);
            Children.Add(header);
        }
        else
        {
            var note = UiKit.Text("Local. No network.", 9.5, FontWeights.Medium, ThemeKeys.TextSecondary);
            note.HorizontalAlignment = HorizontalAlignment.Right;
            Children.Add(note);
        }

        var picker = new SegmentedControl(["Video", "GIF", "Image", "Text"], (int)MediaSession.Tool, true, compact ? 11 : 12);
        picker.Margin = new Thickness(0, compact ? 6 : spacing, 0, 0);
        picker.SelectionChanged += i => ChangeTool((MediaTool)i);
        Children.Add(picker);

        var content = new StackPanel { Margin = new Thickness(0, spacing, 0, 0) };
        content.Children.Add(_file);
        _options.Margin = new Thickness(0, compact ? 9 : 12, 0, 0);
        _actions.Margin = new Thickness(0, compact ? 9 : 12, 0, 0);
        _status.Margin = new Thickness(0, compact ? 9 : 12, 0, 0);
        content.Children.Add(_options);
        content.Children.Add(_actions);
        content.Children.Add(_status);
        if (compact)
        {
            Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 430, Content = content });
        }
        else
        {
            Children.Add(content);
        }

        AllowDrop = true;
        Drop += (_, e) =>
        {
            SetDrag(false);
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                SetInputs(files.Where(File.Exists));
            }
        };
        DragEnter += (_, e) => SetDrag(e.Data.GetDataPresent(DataFormats.FileDrop));
        DragLeave += (_, _) => SetDrag(false);
        Loaded += (_, _) =>
        {
            _svc.Changed += OnService;
            RebuildAll();
        };
        Unloaded += (_, _) =>
        {
            _svc.Changed -= OnService;
            _keepOpen?.Invoke(false);
        };
        RebuildAll();
    }

    // ------------------------------------------------------------------ state

    private MediaTool Tool => MediaSession.Tool;

    private string? FirstInput => MediaSession.Inputs.FirstOrDefault();

    private bool Running => _svc.IsRunning;

    private void SetDrag(bool over)
    {
        if (_dragOver != over)
        {
            _dragOver = over;
            BuildFile();
        }
    }

    private void OnService()
    {
        BuildActions();
        BuildStatus();
        _keepOpen?.Invoke(_svc.IsRunning);
        _relayout?.Invoke();
    }

    private void ChangeTool(MediaTool tool)
    {
        if (tool == MediaSession.Tool)
        {
            return;
        }

        _durationGeneration++;
        MediaSession.Tool = tool;
        Ms.LastTool = tool;
        _localMessage = null;
        if (FirstInput is { } input)
        {
            // inputs of the other kind do not carry over
            var valid = MediaSession.Inputs.Where(p => TypeOk(p, tool)).ToList();
            MediaSession.Inputs = tool == MediaTool.Image ? valid : valid.Take(1).ToList();
        }

        _svc.Reset();
        PrepareInputs();
        RebuildAll();
    }

    private static bool TypeOk(string path, MediaTool tool) => tool is MediaTool.Video or MediaTool.Gif ? MediaNaming.IsVideo(path) : MediaNaming.IsImage(path);

    private void SetInputs(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0)
        {
            return;
        }

        List<string> accepted;
        if (Tool == MediaTool.Image)
        {
            accepted = list.Where(p => TypeOk(p, Tool)).ToList();
        }
        else
        {
            accepted = list.All(p => TypeOk(p, Tool)) ? list.Take(1).ToList() : [];
        }

        if (accepted.Count == 0)
        {
            _svc.Fail(new MediaException(MediaFailureKind.Unsupported));
            return;
        }

        _durationGeneration++;
        MediaSession.Inputs = accepted;
        MediaSession.OutputManual = false;
        MediaSession.OutputDirectory = null;
        _localMessage = null;
        _svc.Reset();
        _svc.MarkReady();
        PrepareInputs();
        RebuildAll();
    }

    private void PrepareInputs()
    {
        MediaSession.Output = null;
        MediaSession.Duration = 0;
        MediaSession.ImageInfo = null;
        _thumb = null;
        _thumbFor = null;
        if (FirstInput is not { } input)
        {
            return;
        }

        switch (Tool)
        {
            case MediaTool.Video:
                Ms.Set("mediaVideoStart", 0.0);
                Ms.Set("mediaVideoEnd", 0.0);
                LoadDuration(input);
                break;
            case MediaTool.Gif:
                Ms.Set("mediaGIFStart", 0.0);
                Ms.Set("mediaGIFEnd", 0.0);
                LoadDuration(input);
                break;
            case MediaTool.Image:
                MediaSession.ImageInfo = MediaImage.ReadInfo(input);
                LoadThumb(input);
                break;
        }

        RecomputeOutput();
    }

    private async void LoadDuration(string input)
    {
        var generation = ++_durationGeneration;
        var info = await MediaVideo.ProbeAsync(input);
        if (generation != _durationGeneration || info is null || info.Duration <= 0 || double.IsNaN(info.Duration))
        {
            return;
        }

        var rounded = Math.Round(info.Duration, 1);
        MediaSession.Duration = rounded;
        var key = Tool == MediaTool.Gif ? "mediaGIFEnd" : "mediaVideoEnd";
        Ms.Set(key, rounded);
        BuildOptions();
    }

    private void LoadThumb(string input)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(input);
            bitmap.DecodePixelWidth = 256;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();
            _thumb = bitmap;
            _thumbFor = input;
        }
        catch
        {
            _thumb = null;
        }
    }

    /// <summary>The default output (unless the user chose a destination): next to the original, unique.</summary>
    private void RecomputeOutput()
    {
        if (FirstInput is not { } input)
        {
            MediaSession.Output = null;
            return;
        }

        var options = MediaSession.Options;
        if (MediaSession.OutputManual && MediaSession.Output is { } manual)
        {
            if (Tool == MediaTool.Image && MediaSession.Inputs.Count == 1)
            {
                var swapped = Path.ChangeExtension(manual, MediaNaming.ExtensionOf(options.Format));
                if (!string.Equals(swapped, manual, StringComparison.OrdinalIgnoreCase))
                {
                    MediaSession.Output = MediaNaming.Unique(Path.GetDirectoryName(manual)!, Path.GetFileNameWithoutExtension(manual), Path.GetExtension(swapped));
                }
            }

            return;
        }

        var dir = Path.GetDirectoryName(input)!;
        var baseName = MediaNaming.BaseName(input);
        switch (Tool)
        {
            case MediaTool.Video:
                MediaSession.Output = MediaNaming.Unique(dir, baseName + "-compressed", ".mp4");
                break;
            case MediaTool.Gif:
                MediaSession.Output = MediaNaming.Unique(dir, baseName, ".gif");
                break;
            case MediaTool.Text:
                MediaSession.Output = MediaNaming.Unique(dir, baseName + "-text", ".txt");
                break;
            default:
                if (MediaSession.Inputs.Count == 1)
                {
                    var (w, h) = MediaSession.ImageInfo is { } info ? MediaImage.TargetSize(options, info.Width, info.Height) : (0, 0);
                    var name = MediaNaming.ExpandPattern(options.RenamePattern, input, 1, w, h, options.Format);
                    MediaSession.Output = MediaNaming.Unique(dir, name, MediaNaming.ExtensionOf(options.Format));
                }
                else
                {
                    MediaSession.Output = null;
                }

                break;
        }
    }

    private void OptionsChanged(bool rebuild = false)
    {
        var options = MediaSession.Options;
        options.SaveToSettings();
        RecomputeOutput();
        if (rebuild)
        {
            BuildOptions();
        }

        RefreshOutputLabels();
        BuildFile();
        BuildActions();
    }

    private void RefreshOutputLabels()
    {
        var text = MediaSession.Output is { } output ? Path.GetFileName(output) : "Automatic";
        if (_outputLabel is not null)
        {
            _outputLabel.Text = text;
        }

        if (_previewOutput is not null)
        {
            _previewOutput.Text = $"Output: {text}";
        }

        RefreshPreview();
    }

    private void RebuildAll()
    {
        BuildFile();
        BuildOptions();
        BuildActions();
        BuildStatus();
        _relayout?.Invoke();
    }

    // ------------------------------------------------------------------ file card

    private void BuildFile()
    {
        var column = new StackPanel();
        var multi = MediaSession.Inputs.Count > 1;
        var hasInput = FirstInput is not null;
        var title = !hasInput ? "Choose file" : multi ? $"{MediaSession.Inputs.Count} files selected" : Path.GetFileName(FirstInput!);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = UiKit.Icon(Tool == MediaTool.Text ? G.Document : G.DocumentAdd, 16, ThemeKeys.TextPrimary, FontWeights.SemiBold);
        glyph.Margin = new Thickness(0, 0, 9, 0);
        content.Children.Add(glyph);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = UiKit.Text(title, _compact ? 11.5 : 12.5, FontWeights.SemiBold, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        name.ToolTip = hasInput ? string.Join("\n", MediaSession.Inputs) : null;
        texts.Children.Add(name);
        texts.Children.Add(UiKit.Text("Drop a file here or click to choose one.", _compact ? 9.5 : 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        Grid.SetColumn(texts, 1);
        content.Children.Add(texts);

        var button = new Border
        {
            MinHeight = _compact ? 52 : 62,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, hasInput ? (_compact ? 30 : 34) : 12, 8),
            BorderThickness = new Thickness(_dragOver ? 1.2 : 0.8),
            Cursor = Cursors.Hand,
            Child = content,
        };
        if (_dragOver)
        {
            var accent = (UiKit.BrushOf(ThemeKeys.Accent) as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            button.Background = new SolidColorBrush(Color.FromArgb(0x29, accent.R, accent.G, accent.B));
            button.BorderBrush = new SolidColorBrush(Color.FromArgb(0xB3, accent.R, accent.G, accent.B));
        }
        else
        {
            button.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            button.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        }

        button.MouseLeftButtonUp += (_, e) =>
        {
            if (!e.Handled)
            {
                ChooseFile();
            }
        };
        var holder = new Grid();
        holder.Children.Add(button);
        if (hasInput)
        {
            var clear = V.IconButton(Glyphs.CloseCircle, "Cancel", ClearInputs, _compact ? 14 : 16, ThemeKeys.TextSecondary, _compact ? 24 : 28);
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            clear.Margin = new Thickness(0, 0, 4, 0);
            clear.IsEnabled = !Running;
            holder.Children.Add(clear);
        }

        column.Children.Add(holder);

        // output row
        var outputLabel = UiKit.Text(MediaSession.Output is { } output ? Path.GetFileName(output) : "Automatic", 11, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        outputLabel.ToolTip = MediaSession.Output;
        outputLabel.VerticalAlignment = VerticalAlignment.Center;
        _outputLabel = outputLabel;
        var row = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var outTitle = UiKit.Text("Output", 10.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        outTitle.VerticalAlignment = VerticalAlignment.Center;
        outTitle.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(outTitle);
        Grid.SetColumn(outputLabel, 1);
        row.Children.Add(outputLabel);
        var destination = UiKit.Button("Destination", ButtonKind.Bordered, 11, G.Folder, ChooseDestination);
        destination.IsEnabled = hasInput && !Running;
        destination.Margin = new Thickness(7, 0, 0, 0);
        Grid.SetColumn(destination, 2);
        row.Children.Add(destination);
        column.Children.Add(row);

        if (Tool == MediaTool.Image && multi)
        {
            var sub = MediaControls.Check("Save in “Converted” subfolder", Ms.GetB("mediaImageSaveInSubfolder"), on => Ms.Set("mediaImageSaveInSubfolder", on), _compact ? 11 : 11.5);
            sub.Margin = new Thickness(0, 9, 0, 0);
            sub.IsEnabled = !Running;
            column.Children.Add(sub);
        }

        _file.Content = new PanelCard(column);
    }

    private void ClearInputs()
    {
        if (Running)
        {
            return;
        }

        _durationGeneration++;
        MediaSession.Inputs = [];
        MediaSession.Output = null;
        MediaSession.OutputManual = false;
        MediaSession.ImageInfo = null;
        MediaSession.Duration = 0;
        _thumb = null;
        _localMessage = null;
        _svc.Reset();
        RebuildAll();
    }

    private void ChooseFile()
    {
        if (Running)
        {
            return;
        }

        _keepOpen?.Invoke(true);
        try
        {
            var filter = Tool is MediaTool.Video or MediaTool.Gif ? MediaNaming.VideoFilter : MediaNaming.ImageFilter;
            var files = Dialogs.OpenFiles("Choose file", filter, Tool == MediaTool.Image);
            if (files.Length > 0)
            {
                SetInputs(files);
            }
        }
        finally
        {
            _keepOpen?.Invoke(false);
        }
    }

    private void ChooseDestination()
    {
        if (FirstInput is not { } input || Running)
        {
            return;
        }

        _keepOpen?.Invoke(true);
        try
        {
            if (Tool == MediaTool.Image && MediaSession.Inputs.Count > 1)
            {
                var folder = Dialogs.PickFolder("Destination", MediaSession.OutputDirectory ?? Path.GetDirectoryName(input));
                if (folder is not null)
                {
                    MediaSession.OutputDirectory = folder;
                    MediaSession.OutputManual = true;
                    RefreshOutputLabels();
                }

                return;
            }

            var (filter, ext) = Tool switch
            {
                MediaTool.Video => ("MP4 video|*.mp4", ".mp4"),
                MediaTool.Gif => ("GIF image|*.gif", ".gif"),
                MediaTool.Text => ("Text file|*.txt", ".txt"),
                _ => (MediaSession.Options.Format switch { "png" => "PNG image|*.png", "heic" => "HEIC image|*.heic", "pdf" => "PDF document|*.pdf", _ => "JPEG image|*.jpg" }, MediaNaming.ExtensionOf(MediaSession.Options.Format)),
            };
            var current = MediaSession.Output ?? input;
            var chosen = Dialogs.SaveFile("Destination", filter, Path.GetDirectoryName(current), Path.GetFileName(current), ext);
            if (chosen is null)
            {
                return;
            }

            if (MediaNaming.SameFile(input, chosen))
            {
                _svc.Fail(new MediaException(MediaFailureKind.SameOutput));
                return;
            }

            MediaSession.Output = chosen;
            MediaSession.OutputManual = true;
            RefreshOutputLabels();
        }
        finally
        {
            _keepOpen?.Invoke(false);
        }
    }

    // ------------------------------------------------------------------ options card

    private void BuildOptions()
    {
        _outputLabel = null;
        _preview = null;
        _previewOutput = null;
        var column = new StackPanel();
        void Add(FrameworkElement element, double top = -1)
        {
            element.Margin = new Thickness(element.Margin.Left, column.Children.Count == 0 ? 0 : (top >= 0 ? top : (_compact ? 9 : 10)), element.Margin.Right, element.Margin.Bottom);
            column.Children.Add(element);
        }

        switch (Tool)
        {
            case MediaTool.Video:
                VideoOptions(Add);
                break;
            case MediaTool.Gif:
                GifOptions(Add);
                break;
            case MediaTool.Image:
                ImageOptionsUi(Add);
                break;
            default:
                var accurate = Ms.GetB("mediaTextAccurate");
                var seg = MediaControls.Segmented(["Accurate", "Fast"], accurate ? 0 : 1, i => Ms.Set("mediaTextAccurate", i == 0), _compact ? 11 : 12);
                System.Windows.Automation.AutomationProperties.SetName(seg, "OCR");
                Add(seg);
                break;
        }

        _options.Content = new PanelCard(column);
        RefreshOutputLabels();
        _relayout?.Invoke();
    }

    private FrameworkElement TrimRow(string startKey, string endKey)
    {
        var start = MediaControls.NumberField("Start", Ms.GetD(startKey, 0, 1e6), 1, 0, "s", v => Ms.Set(startKey, v), _compact);
        var end = MediaControls.NumberField("End", Ms.GetD(endKey, 0, 1e6), 1, 0, "s", v => Ms.Set(endKey, v), _compact);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        start.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(start);
        row.Children.Add(end);
        return row;
    }

    private FrameworkElement TargetSizeRow(string key, int fallback)
    {
        var column = new StackPanel();
        var box = new TextBox { Text = Ms.GetI(key, 1, 512).ToString(), Width = 76, Height = 28, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0) };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        void Commit()
        {
            if (int.TryParse(box.Text, out var mb))
            {
                mb = Math.Clamp(mb, 1, 512);
                box.Text = mb.ToString();
                Ms.Set(key, mb);
            }
            else
            {
                box.Text = Ms.GetI(key, 1, 512).ToString();
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
            }
        };
        var label = UiKit.Text("Target size", _compact ? 10.5 : 11, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        label.Width = _compact ? 74 : 92;
        label.VerticalAlignment = VerticalAlignment.Center;
        var unit = UiKit.Text(" MB", 10.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        unit.VerticalAlignment = VerticalAlignment.Center;
        column.Children.Add(V.Row(0, label, box, unit));
        var hint = V.Secondary("Resolution adapts to stay under the limit.", _compact ? 9.5 : 10.5);
        hint.Margin = new Thickness(0, 5, 0, 0);
        column.Children.Add(hint);
        return column;
    }

    private void VideoOptions(Action<FrameworkElement, double> add)
    {
        add(TrimRow("mediaVideoStart", "mediaVideoEnd"), -1);
        var sizing = Ms.GetS("mediaVideoSizing", "resolution", "targetSize");
        add(MediaControls.Segmented(["Resolution", "File size"], sizing == "resolution" ? 0 : 1, i =>
        {
            Ms.Set("mediaVideoSizing", i == 0 ? "resolution" : "targetSize");
            BuildOptions();
        }, _compact ? 11 : 12), -1);
        if (sizing == "resolution")
        {
            add(MediaControls.Compression(Ms.GetD("mediaVideoQuality", 0.1, 1), q =>
            {
                Ms.Set("mediaVideoQuality", q);
                BuildOptions();
            }, _compact), -1);
            add(MediaControls.Stepper("Size", Ms.GetI("mediaVideoMaxDimension", 640, 3840), 640, 3840, 320, "px", v => Ms.Set("mediaVideoMaxDimension", v), _compact), -1);
        }
        else
        {
            add(TargetSizeRow("mediaVideoTargetMegabytes", 20), -1);
        }
    }

    private void GifOptions(Action<FrameworkElement, double> add)
    {
        add(TrimRow("mediaGIFStart", "mediaGIFEnd"), -1);
        var sizing = Ms.GetS("mediaGIFSizing", "resolution", "targetSize");
        add(MediaControls.Segmented(["Resolution", "File size"], sizing == "resolution" ? 0 : 1, i =>
        {
            Ms.Set("mediaGIFSizing", i == 0 ? "resolution" : "targetSize");
            BuildOptions();
        }, _compact ? 11 : 12), -1);
        if (sizing == "resolution")
        {
            var fps = Ms.GetD("mediaGIFFPS", 1, 30);
            var value = UiKit.Text(((int)fps).ToString(), 11, FontWeights.Normal, ThemeKeys.TextSecondary);
            value.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            var slider = new Slider { Minimum = 1, Maximum = 30, Value = fps, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 0) };
            slider.ValueChanged += (_, e) =>
            {
                value.Text = ((int)e.NewValue).ToString();
                Ms.Set("mediaGIFFPS", Math.Round(e.NewValue));
            };
            var fpsColumn = new StackPanel();
            fpsColumn.Children.Add(V.Split(UiKit.Text("FPS", _compact ? 11 : 12, FontWeights.Normal, ThemeKeys.TextPrimary), value));
            fpsColumn.Children.Add(slider);
            add(fpsColumn, -1);
            add(MediaControls.Stepper("Width", Ms.GetI("mediaGIFWidth", 160, 1600), 160, 1600, 80, "px", v => Ms.Set("mediaGIFWidth", v), _compact), -1);
        }
        else
        {
            add(TargetSizeRow("mediaGIFTargetMegabytes", 10), -1);
        }

        add(MediaControls.Check("Loop GIF", Ms.GetB("mediaGIFLoops"), on => Ms.Set("mediaGIFLoops", on), _compact ? 11 : 11.5), -1);
    }

    private static readonly string[] ResizeKinds = ["none", "maxDimension", "width", "height", "exact"];

    private void ImageOptionsUi(Action<FrameworkElement, double> add)
    {
        var o = MediaSession.Options;

        // presets
        var presets = V.Row(6,
            V.Mini("Web", () => ApplyPreset(o, "jpeg", 0.72, 1600, "{name}-web", false, "transparent", false)),
            V.Mini("Social", () => ApplyPreset(o, "png", 0.82, 2048, "{name}-social", false, "transparent", false)),
            V.Mini("Docs", () => ApplyPreset(o, "pdf", 0.7, 1600, "", true, "white", true)));
        add(presets, -1);

        // preview
        var previewHost = new ContentControl();
        _preview = previewHost;
        var previewText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        previewText.Children.Add(UiKit.Text("Preview", 11, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        _previewOutput = UiKit.Text($"Output: {(MediaSession.Output is { } output ? Path.GetFileName(output) : "Automatic")}", _compact ? 9.5 : 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true, trimming: TextTrimming.CharacterEllipsis);
        _previewOutput.MaxHeight = 30;
        previewText.Children.Add(_previewOutput);
        var previewRow = new StackPanel { Orientation = Orientation.Horizontal };
        previewRow.Children.Add(previewHost);
        previewRow.Children.Add(previewText);
        add(previewRow, -1);

        // format
        var formats = new[] { "jpeg", "png", "heic", "pdf" };
        var formatSeg = MediaControls.Segmented(["JPEG", "PNG", "HEIC", "PDF"], Math.Max(0, Array.IndexOf(formats, o.Format)), i =>
        {
            if (formats[i] == "heic" && !MediaImage.HeicAvailable())
            {
                _localMessage = "Needs the HEIF and HEVC extensions from the Microsoft Store";
                BuildStatus();
                BuildOptions();
                return;
            }

            o.Format = formats[i];
            OptionsChanged(true);
        }, _compact ? 11 : 12);
        add(formatSeg, -1);

        add(MediaControls.Compression(o.Quality, q =>
        {
            o.Quality = q;
            OptionsChanged(true);
        }, _compact), -1);

        // resize
        var resize = new StackPanel();
        var kindIndex = Math.Max(0, Array.IndexOf(ResizeKinds, o.ResizeKind));
        var drop = V.Drop(["No change", "Max side", "Width", "Height", "Custom"], kindIndex, i =>
        {
            o.ResizeKind = ResizeKinds[i];
            OptionsChanged(true);
        }, 0, _compact ? 11 : 12);
        var resizeRow = new Grid();
        resizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        resizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        resizeRow.Children.Add(UiKit.Text("Resize", _compact ? 11 : 12, FontWeights.Normal, ThemeKeys.TextPrimary));
        Grid.SetColumn(drop, 1);
        resizeRow.Children.Add(drop);
        resize.Children.Add(resizeRow);
        FrameworkElement Step(string label, int value, int min, int max, int step, Action<int> set)
        {
            var s = MediaControls.Stepper(label, value, min, max, step, "px", v =>
            {
                set(v);
                OptionsChanged();
            }, _compact);
            s.Margin = new Thickness(0, 7, 0, 0);
            return s;
        }

        switch (o.ResizeKind)
        {
            case "maxDimension":
                resize.Children.Add(Step("Size", o.MaxDimension, 64, 20000, 128, v => o.MaxDimension = v));
                break;
            case "width":
                resize.Children.Add(Step("Width", o.ResizeWidth, 1, 20000, 64, v => o.ResizeWidth = v));
                break;
            case "height":
                resize.Children.Add(Step("Height", o.ResizeHeight, 1, 20000, 64, v => o.ResizeHeight = v));
                break;
            case "exact":
                resize.Children.Add(Step("Width", o.ResizeWidth, 1, 20000, 64, v => o.ResizeWidth = v));
                resize.Children.Add(Step("Height", o.ResizeHeight, 1, 20000, 64, v => o.ResizeHeight = v));
                var modes = new[] { "stretch", "fit", "fill" };
                var modeSeg = MediaControls.Segmented(["Stretch", "Fit", "Fill"], Math.Max(0, Array.IndexOf(modes, o.ExactMode)), i =>
                {
                    o.ExactMode = modes[i];
                    OptionsChanged();
                }, _compact ? 11 : 12);
                modeSeg.Margin = new Thickness(0, 7, 0, 0);
                resize.Children.Add(modeSeg);
                break;
        }

        add(resize, -1);

        // more options
        var moreTitle = UiKit.Text("More options", _compact ? 11 : 12, FontWeights.Medium, ThemeKeys.TextPrimary);
        add(V.Disclosure(moreTitle, MoreOptions, _moreOpen, open => _moreOpen = open), -1);
    }

    private void ApplyPreset(ImageOptions o, string format, double quality, int maxSide, string rename, bool keepDate, string background, bool strip)
    {
        o.Format = format;
        o.Quality = quality;
        o.ResizeKind = "maxDimension";
        o.MaxDimension = maxSide;
        o.StripMetadata = true;
        o.RenamePattern = rename;
        o.Background = background;
        o.PreserveModificationDate = keepDate;
        o.WatermarkKind = "off";
        OptionsChanged(true);
    }

    private FrameworkElement MoreOptions()
    {
        var o = MediaSession.Options;
        var column = new StackPanel { Margin = new Thickness(16, 6, 0, 0) };
        void Add(FrameworkElement e, double top = 9)
        {
            e.Margin = new Thickness(e.Margin.Left, column.Children.Count == 0 ? 0 : top, e.Margin.Right, e.Margin.Bottom);
            column.Children.Add(e);
        }

        // profile row
        var profiles = ImageOptions.LoadProfiles();
        var selectedId = Host.App.Get<string>("mediaImageSelectedProfileID");
        var selected = profiles.FirstOrDefault(p => p.Id == selectedId);
        var profileRow = new StackPanel { Orientation = Orientation.Horizontal };
        var names = new List<string> { "No profile" };
        names.AddRange(profiles.Select(p => p.Name));
        var profileDrop = V.Drop(names, selected is null ? 0 : profiles.IndexOf(selected) + 1, i =>
        {
            if (i == 0)
            {
                Ms.Set("mediaImageSelectedProfileID", string.Empty);
            }
            else
            {
                var profile = profiles[i - 1];
                Ms.Set("mediaImageSelectedProfileID", profile.Id);
                MediaSession.Options = profile.Options.Clone();
                _profileName = profile.Name;
            }

            OptionsChanged(true);
        }, 140, 11.5);
        System.Windows.Automation.AutomationProperties.SetName(profileDrop, "Profile");
        profileRow.Children.Add(profileDrop);
        var delete = V.IconButton(G.Trash, "Delete profile", () =>
        {
            if (selected is not null)
            {
                profiles.Remove(selected);
                ImageOptions.SaveProfiles(profiles);
                Ms.Set("mediaImageSelectedProfileID", string.Empty);
                BuildOptions();
            }
        }, 11, ThemeKeys.TextSecondary, 24);
        delete.IsEnabled = selected is not null;
        delete.Margin = new Thickness(4, 0, 0, 0);
        profileRow.Children.Add(delete);
        if (selected is not null && !selected.Options.SameAs(o))
        {
            var modified = V.Label(G.Edit, "Modified", 10, FontWeights.Medium, ThemeKeys.TextSecondary, ThemeKeys.TextSecondary, 3);
            modified.Margin = new Thickness(8, 0, 0, 0);
            profileRow.Children.Add(modified);
        }

        Add(profileRow);
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        var nameBox = V.Field(_profileName.Length > 0 ? _profileName : (selected?.Name ?? string.Empty), 130, 11.5, "Profile name");
        nameBox.TextChanged += (_, _) => _profileName = nameBox.Text;
        nameRow.Children.Add(nameBox);
        var update = V.Mini("Update", () =>
        {
            if (selected is null)
            {
                return;
            }

            selected.Options = MediaSession.Options.Clone();
            if (nameBox.Text.Trim().Length > 0)
            {
                selected.Name = nameBox.Text.Trim();
            }

            ImageOptions.SaveProfiles(profiles);
            BuildOptions();
        }, G.Check);
        update.IsEnabled = selected is not null;
        update.Margin = new Thickness(6, 0, 0, 0);
        nameRow.Children.Add(update);
        var saveNew = V.Mini("Save new", () =>
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                name = $"Profile {profiles.Count + 1}";
            }

            var profile = new ImageProfile { Name = name, Options = MediaSession.Options.Clone() };
            profiles.Add(profile);
            ImageOptions.SaveProfiles(profiles);
            Ms.Set("mediaImageSelectedProfileID", profile.Id);
            _profileName = string.Empty;
            BuildOptions();
        }, G.Add);
        saveNew.Margin = new Thickness(6, 0, 0, 0);
        nameRow.Children.Add(saveNew);
        Add(nameRow, 6);

        if (o.Format != "pdf")
        {
            Add(MediaControls.Check("Remove metadata", o.StripMetadata, on =>
            {
                o.StripMetadata = on;
                OptionsChanged();
            }, _compact ? 11 : 11.5));
        }

        var backgrounds = new[] { "transparent", "white", "black" };
        var bg = MediaControls.Segmented(["Transparent", "White", "Black"], Math.Max(0, Array.IndexOf(backgrounds, o.Background)), i =>
        {
            o.Background = backgrounds[i];
            OptionsChanged();
        }, _compact ? 11 : 12);
        System.Windows.Automation.AutomationProperties.SetName(bg, "Background");
        Add(bg);

        // watermark
        var watermark = new StackPanel();
        var kinds = new[] { "off", "text", "logo", "textAndLogo" };
        var kindDrop = V.Drop(["Off", "Text", "Logo", "Text + logo"], Math.Max(0, Array.IndexOf(kinds, o.WatermarkKind)), i =>
        {
            o.WatermarkKind = kinds[i];
            OptionsChanged();
            BuildOptions();
        }, 0, _compact ? 11 : 12);
        var kindRow = new Grid();
        kindRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        kindRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        kindRow.Children.Add(UiKit.Text("Watermark", _compact ? 11 : 12, FontWeights.Normal, ThemeKeys.TextPrimary));
        Grid.SetColumn(kindDrop, 1);
        kindRow.Children.Add(kindDrop);
        watermark.Children.Add(kindRow);
        if (o.WatermarkKind is "text" or "textAndLogo")
        {
            var textBox = PackageParts.HintField("Watermark text", out var box, 11.5);
            box.Text = o.WatermarkText;
            box.TextChanged += (_, _) =>
            {
                o.WatermarkText = box.Text;
                OptionsChanged();
            };
            textBox.Margin = new Thickness(0, 7, 0, 0);
            watermark.Children.Add(textBox);
        }

        if (o.WatermarkKind is "logo" or "textAndLogo")
        {
            var logoName = UiKit.Text(string.IsNullOrEmpty(o.WatermarkLogoPath) ? "No logo" : Path.GetFileName(o.WatermarkLogoPath), 11, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            var logoRow = V.Split(logoName, V.Row(4, V.Mini("Choose logo", () =>
            {
                var files = Dialogs.OpenFiles("Choose logo", MediaNaming.ImageFilter, false);
                if (files.Length > 0)
                {
                    o.WatermarkLogoPath = files[0];
                    OptionsChanged();
                    BuildOptions();
                }
            }, G.Photo), string.IsNullOrEmpty(o.WatermarkLogoPath) ? new Border() : V.IconButton(Glyphs.CloseCircle, null, () =>
            {
                o.WatermarkLogoPath = string.Empty;
                OptionsChanged();
                BuildOptions();
            }, 12, ThemeKeys.TextSecondary, 22)));
            logoRow.Margin = new Thickness(0, 7, 0, 0);
            watermark.Children.Add(logoRow);
        }

        if (o.WatermarkKind != "off")
        {
            var positions = new[] { "topLeft", "topRight", "center", "bottomLeft", "bottomRight" };
            var posDrop = V.Drop(["Top left", "Top right", "Center", "Bottom left", "Bottom right"], Math.Max(0, Array.IndexOf(positions, o.WatermarkPosition)), i =>
            {
                o.WatermarkPosition = positions[i];
                OptionsChanged();
            }, 0, _compact ? 11 : 12);
            var posRow = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            posRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            posRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            posRow.Children.Add(UiKit.Text("Position", _compact ? 11 : 12, FontWeights.Normal, ThemeKeys.TextPrimary));
            Grid.SetColumn(posDrop, 1);
            posRow.Children.Add(posDrop);
            watermark.Children.Add(posRow);
            FrameworkElement Step(string label, int value, int min, int max, int step, string suffix, Action<int> set)
            {
                var s = MediaControls.Stepper(label, value, min, max, step, suffix, v =>
                {
                    set(v);
                    OptionsChanged();
                }, _compact);
                s.Margin = new Thickness(0, 7, 0, 0);
                return s;
            }

            watermark.Children.Add(Step("Opacity", (int)Math.Round(o.WatermarkOpacity * 100), 10, 100, 5, "%", v => o.WatermarkOpacity = v / 100.0));
            watermark.Children.Add(Step("Margin", o.WatermarkMargin, 0, 2000, 8, "px", v => o.WatermarkMargin = v));
            watermark.Children.Add(Step("Scale", (int)Math.Round(o.WatermarkScale * 100), 5, 80, 1, "%", v => o.WatermarkScale = v / 100.0));
        }

        Add(watermark);

        // rename
        var rename = new StackPanel();
        rename.Children.Add(UiKit.Text("Rename", _compact ? 10.5 : 11, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var renameRow = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var patternField = PackageParts.HintField("{name}-{index:03}", out var patternBox, 11.5);
        patternBox.Text = o.RenamePattern;
        patternBox.TextChanged += (_, _) =>
        {
            o.RenamePattern = patternBox.Text;
            OptionsChanged();
        };
        renameRow.Children.Add(patternField);
        var tokens = new Button { Content = UiKit.Text("{…}", 11, FontWeights.Normal, ThemeKeys.TextPrimary), ToolTip = "Rename", Margin = new Thickness(6, 0, 0, 0) };
        tokens.Content = new TextBlock { Text = "{…}", FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11 };
        tokens.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
        var menu = new ContextMenu();
        foreach (var token in new[] { "{name}", "{index}", "{index:03}", "{counter}", "{date}", "{time}", "{datetime}", "{width}", "{height}", "{format}" })
        {
            var item = new MenuItem { Header = token };
            var captured = token;
            item.Click += (_, _) => patternBox.Text += captured;
            menu.Items.Add(item);
        }

        tokens.Click += (_, _) =>
        {
            menu.PlacementTarget = tokens;
            menu.IsOpen = true;
        };
        Grid.SetColumn(tokens, 1);
        renameRow.Children.Add(tokens);
        rename.Children.Add(renameRow);
        Add(rename);

        Add(MediaControls.Check("Keep original modified date", o.PreserveModificationDate, on =>
        {
            o.PreserveModificationDate = on;
            OptionsChanged();
        }, _compact ? 11 : 11.5));
        return column;
    }

    private void RefreshPreview()
    {
        if (_preview is null || Tool != MediaTool.Image)
        {
            return;
        }

        var o = MediaSession.Options;
        var box = _compact ? new Size(108, 74) : new Size(136, 92);
        var (outW, outH) = MediaSession.ImageInfo is { } info ? MediaImage.TargetSize(o, info.Width, info.Height) : (4, 3);
        var scale = Math.Min(box.Width / Math.Max(1, outW), box.Height / Math.Max(1, outH));
        var frameW = Math.Max(8, Math.Min(box.Width, outW * scale));
        var frameH = Math.Max(8, Math.Min(box.Height, outH * scale));
        var frame = new Border { Width = frameW, Height = frameH, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(0.8), ClipToBounds = true };
        frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        var flat = o.Format is "jpeg" or "pdf";
        switch (o.Background)
        {
            case "white":
                frame.Background = Brushes.White;
                break;
            case "black":
                frame.Background = Brushes.Black;
                break;
            default:
                if (flat)
                {
                    frame.Background = Brushes.White;
                }
                else
                {
                    frame.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
                }

                break;
        }

        var grid = new Grid();
        if (_thumb is not null && _thumbFor == FirstInput)
        {
            var image = new Image { Source = _thumb, Stretch = o.ResizeKind == "exact" ? (o.ExactMode switch { "fill" => Stretch.UniformToFill, "stretch" => Stretch.Fill, _ => Stretch.Uniform }) : Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            grid.Children.Add(image);
        }
        else
        {
            var placeholder = UiKit.Icon(G.Photo, _compact ? 26 : 32, ThemeKeys.TextTertiary);
            placeholder.FontWeight = FontWeights.Light;
            grid.Children.Add(placeholder);
        }

        // live watermark overlay
        var side = Math.Min(frameW, frameH);
        var sideOut = Math.Max(1, Math.Min(outW, outH));
        var showText = o.WatermarkKind is "text" or "textAndLogo" && o.WatermarkText.Length > 0;
        if (showText || o.WatermarkKind is "logo" or "textAndLogo")
        {
            var overlay = new StackPanel { Orientation = Orientation.Horizontal };
            if (o.WatermarkKind is "logo" or "textAndLogo" && File.Exists(o.WatermarkLogoPath))
            {
                try
                {
                    var logo = new BitmapImage();
                    logo.BeginInit();
                    logo.UriSource = new Uri(o.WatermarkLogoPath);
                    logo.DecodePixelWidth = 64;
                    logo.CacheOption = BitmapCacheOption.OnLoad;
                    logo.EndInit();
                    logo.Freeze();
                    var logoSide = Math.Min(side, Math.Max(4, side * o.WatermarkScale));
                    overlay.Children.Add(new Image { Source = logo, Width = logoSide, Height = logoSide, Opacity = o.WatermarkOpacity });
                }
                catch
                {
                    // no logo preview
                }
            }

            if (showText)
            {
                overlay.Children.Add(new TextBlock
                {
                    Text = o.WatermarkText,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = _compact ? 8 : 10,
                    Opacity = o.WatermarkOpacity,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black },
                });
            }

            var margin = Math.Min(o.WatermarkMargin * side / sideOut, Math.Min(frameW, frameH) / 2);
            overlay.Margin = new Thickness(margin);
            overlay.HorizontalAlignment = o.WatermarkPosition is "topLeft" or "bottomLeft" ? HorizontalAlignment.Left : o.WatermarkPosition == "center" ? HorizontalAlignment.Center : HorizontalAlignment.Right;
            overlay.VerticalAlignment = o.WatermarkPosition is "topLeft" or "topRight" ? VerticalAlignment.Top : o.WatermarkPosition == "center" ? VerticalAlignment.Center : VerticalAlignment.Bottom;
            grid.Children.Add(overlay);
        }

        frame.Child = grid;
        var holder = new Border { Width = box.Width, Height = box.Height, Child = frame };
        _preview.Content = holder;
    }

    // ------------------------------------------------------------------ actions

    private void BuildActions()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var title = Tool switch
        {
            MediaTool.Video => "Compress video",
            MediaTool.Gif => "Make GIF",
            MediaTool.Image => MediaSession.Options.Format == "pdf" ? "Convert to PDF" : "Process image",
            _ => "Extract text",
        };
        var start = UiKit.Button(title, ButtonKind.Prominent, _compact ? 11 : 12, Tool == MediaTool.Text ? G.Search : G.Play, Start);
        start.IsEnabled = FirstInput is not null && !Running;
        start.FontWeight = FontWeights.SemiBold;
        row.Children.Add(start);
        if (Running)
        {
            var cancel = UiKit.Button("Cancel", ButtonKind.Bordered, _compact ? 11 : 12, G.Cancel, _svc.Cancel);
            cancel.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(cancel);
        }

        _actions.Content = row;
    }

    private void Start()
    {
        if (FirstInput is not { } input)
        {
            _localMessage = "Choose a file first.";
            BuildStatus();
            return;
        }

        _localMessage = null;
        switch (Tool)
        {
            case MediaTool.Video:
            {
                var output = EnsureOutput();
                var request = new VideoRequest(input, output, MediaSession.OutputManual, Ms.GetD("mediaVideoStart", 0, 1e6), Ms.GetD("mediaVideoEnd", 0, 1e6), Ms.GetD("mediaVideoQuality", 0.1, 1),
                    Ms.GetI("mediaVideoMaxDimension", 640, 3840), Ms.GetS("mediaVideoSizing", "resolution", "targetSize"), (long)Ms.GetI("mediaVideoTargetMegabytes", 1, 512) * 1_000_000);
                if (Guard(input, output))
                {
                    _ = _svc.RunAsync((p, ct) => MediaVideo.CompressAsync(request, p, ct));
                }

                break;
            }

            case MediaTool.Gif:
            {
                var output = EnsureOutput();
                var request = new GifRequest(input, output, MediaSession.OutputManual, Ms.GetD("mediaGIFStart", 0, 1e6), Ms.GetD("mediaGIFEnd", 0, 1e6), Ms.GetI("mediaGIFWidth", 160, 1600), Ms.GetD("mediaGIFFPS", 1, 30),
                    Ms.GetB("mediaGIFLoops"), Ms.GetS("mediaGIFSizing", "resolution", "targetSize"), (long)Ms.GetI("mediaGIFTargetMegabytes", 1, 512) * 1_000_000);
                if (Guard(input, output))
                {
                    _ = _svc.RunAsync((p, ct) => MediaVideo.MakeGifAsync(request, p, ct));
                }

                break;
            }

            case MediaTool.Text:
            {
                var output = EnsureOutput();
                var accurate = Ms.GetB("mediaTextAccurate");
                if (Guard(input, output))
                {
                    var manual = MediaSession.OutputManual;
                    _ = _svc.RunAsync((p, ct) => MediaText.RunAsync(input, output, manual, accurate, p, ct));
                }

                break;
            }

            default:
            {
                var inputs = MediaSession.Inputs.ToList();
                var options = MediaSession.Options.Clone();
                var single = inputs.Count == 1 ? EnsureOutput() : null;
                if (single is not null && !Guard(input, single))
                {
                    return;
                }

                var manual = MediaSession.OutputManual;
                var directory = MediaSession.OutputManual && inputs.Count > 1 ? MediaSession.OutputDirectory : null;
                var subfolder = inputs.Count > 1 && Ms.GetB("mediaImageSaveInSubfolder");
                _ = _svc.RunAsync((p, ct) => MediaImage.RunAsync(inputs, options, single, manual, directory, subfolder, p, ct));
                break;
            }
        }
    }

    private string EnsureOutput()
    {
        if (MediaSession.Output is null)
        {
            RecomputeOutput();
        }
        else if (!MediaSession.OutputManual)
        {
            RecomputeOutput();
        }

        return MediaSession.Output ?? string.Empty;
    }

    private bool Guard(string input, string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return false;
        }

        if (MediaNaming.SameFile(input, output))
        {
            _svc.Fail(new MediaException(MediaFailureKind.SameOutput));
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------ status

    private void BuildStatus()
    {
        var column = new StackPanel();
        FrameworkElement MessageCard(string glyph, string brush, string text)
        {
            var label = V.Label(glyph, text, _compact ? 11 : 11.5, FontWeights.Medium, brush, brush == ThemeKeys.Orange ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary);
            foreach (var t in label.Children.OfType<TextBlock>().Skip(1))
            {
                t.TextWrapping = TextWrapping.Wrap;
            }

            return new PanelCard(label);
        }

        if (_localMessage is { } local)
        {
            column.Children.Add(MessageCard(G.Warning, ThemeKeys.Orange, local));
        }

        switch (_svc.State)
        {
            case MediaStateKind.Running:
            {
                var body = new StackPanel();
                var head = new Grid();
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.Children.Add(V.Label(G.Gear, "Processing", 11.5, FontWeights.SemiBold, ThemeKeys.TextPrimary, ThemeKeys.TextPrimary));
                var percent = UiKit.Text($"{(int)(_svc.Progress * 100)}%", 11, FontWeights.Normal, ThemeKeys.TextSecondary);
                percent.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                Grid.SetColumn(percent, 1);
                head.Children.Add(percent);
                body.Children.Add(head);
                body.Children.Add(new ProgressStrip(_svc.Progress) { Margin = new Thickness(0, 8, 0, 0) });
                column.Children.Add(new PanelCard(body) { Margin = new Thickness(0, column.Children.Count == 0 ? 0 : 8, 0, 0) });
                break;
            }

            case MediaStateKind.Completed when _svc.Result is { } result:
                column.Children.Add(new PanelCard(ResultBody(result)) { Margin = new Thickness(0, column.Children.Count == 0 ? 0 : 8, 0, 0) });
                break;
            case MediaStateKind.Failed:
                column.Children.Add(MessageCard(G.Warning, ThemeKeys.Orange, _svc.Message ?? "Format not supported by Windows."));
                break;
            case MediaStateKind.Cancelled:
                column.Children.Add(MessageCard(G.CancelCircle, ThemeKeys.TextSecondary, "Cancelled."));
                break;
        }

        _status.Content = column;
        _status.Visibility = column.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private FrameworkElement ResultBody(MediaResult result)
    {
        var column = new StackPanel();
        column.Children.Add(V.Label(G.CheckCircle, "Done", 11.5, FontWeights.SemiBold, ThemeKeys.Green, ThemeKeys.Green));
        var batch = result.Batch.Count > 1;
        void Line(FrameworkElement e, double top = 8)
        {
            e.Margin = new Thickness(0, top, 0, 0);
            column.Children.Add(e);
        }

        if (result.Tool == MediaTool.Text)
        {
            if (string.IsNullOrWhiteSpace(result.Text))
            {
                Line(V.Secondary("No text found.", 11, false));
            }
            else
            {
                var box = new TextBox
                {
                    Text = result.Text,
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8),
                    MaxHeight = _compact ? 5 * 16 : 8 * 16,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
                box.SetResourceReference(Control.BackgroundProperty, ThemeKeys.ControlFill);
                box.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
                Line(box);
            }
        }

        if (result.Outputs.Count > 0)
        {
            if (batch)
            {
                var failedCount = result.Batch.Count(b => b.Output is null);
                Line(UiKit.Text(failedCount == 0 ? $"{result.Batch.Count} images saved" : $"{result.Batch.Count - failedCount} saved, {failedCount} failed", 11, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true));
            }
            else
            {
                Line(UiKit.Text($"Saved as {Path.GetFileName(result.Outputs[0])}", 11, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
            }

            var sizes = V.Secondary($"{Fmt.Bytes(result.OriginalBytes)} to {Fmt.Bytes(result.OutputBytes)}", 10.5, false);
            Line(sizes, 2);
            var delta = result.OriginalBytes - result.OutputBytes;
            if (delta != 0 && result.Tool != MediaTool.Text)
            {
                Line(V.Secondary(delta > 0 ? $"{Fmt.Bytes(delta)} saved" : $"{Fmt.Bytes(-delta)} larger", 10.5, false), 2);
            }

            if (result.OutputGrew && result.Tool != MediaTool.Text)
            {
                Line(V.Secondary("The converted file came out larger than the original.", 10.5), 2);
            }

            foreach (var failure in result.Batch.Where(b => b.Output is null).Take(3))
            {
                var text = UiKit.Text($"{Path.GetFileName(failure.Input)}: {failure.Error}", 10, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
                text.MaxHeight = 40;
                Line(text, 2);
            }
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Button(string title, string glyph, Action action)
        {
            var b = V.Mini(title, action, glyph, _compact ? 10 : 10.5);
            b.Margin = new Thickness(0, 0, 8, 4);
            buttons.Children.Add(b);
        }

        if (result.Outputs.Count > 0)
        {
            Button("Show", G.Folder, () => FileOps.Reveal(result.Outputs[0]));
        }

        if (result.Tool == MediaTool.Text && !string.IsNullOrEmpty(result.Text))
        {
            Button("Copy text", G.Copy, () => TryCopy(result.Text!));
        }

        if (batch)
        {
            Button("Copy summary", G.Copy, () => TryCopy(Summary(result)));
        }

        Button("Run again", G.Refresh, Start);
        column.Children.Add(buttons);
        return column;
    }

    private static string Summary(MediaResult result)
    {
        var failed = result.Batch.Count(b => b.Output is null);
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"{result.Batch.Count - failed} saved, {failed} failed");
        var delta = result.OriginalBytes - result.OutputBytes;
        if (delta != 0)
        {
            builder.AppendLine(delta > 0 ? $"{Fmt.Bytes(delta)} saved" : $"{Fmt.Bytes(-delta)} larger");
        }

        foreach (var item in result.Batch)
        {
            builder.AppendLine(item.Output is not null ? $"{Path.GetFileName(item.Input)} -> {Path.GetFileName(item.Output)}" : $"{Path.GetFileName(item.Input)}: {item.Error}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void TryCopy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // clipboard busy
        }
    }

    /// <summary>Opens the given files in a tool (used by other features: shelf, recent captures).</summary>
    public static void OpenWith(IEnumerable<string> paths, MediaTool tool)
    {
        MediaSession.Tool = tool;
        MediaSession.Initialised = true;
        Ms.LastTool = tool;
        MediaSession.Inputs = paths.Where(File.Exists).Where(p => TypeOk(p, tool)).ToList();
        if (tool != MediaTool.Image && MediaSession.Inputs.Count > 1)
        {
            MediaSession.Inputs = MediaSession.Inputs.Take(1).ToList();
        }

        MediaSession.Output = null;
        MediaSession.OutputManual = false;
    }
}
