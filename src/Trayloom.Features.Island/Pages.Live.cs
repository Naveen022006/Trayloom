// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

// ================================================================================================ notifications

internal sealed class NotificationsPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private double _w = 400;
    private double _h = 264;

    public NotificationsPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
    }

    public override string ModuleId => "notifications";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Trailing => _trailing;

    public override bool ScrollsContent => true;

    public override double Height(double width, double budget)
    {
        _w = width;
        _h = budget;
        Build();
        return budget;
    }

    public override void Shown() => Core.Notifications.Changed += OnChanged;

    public override void Hidden() => Core.Notifications.Changed -= OnChanged;

    private void OnChanged() => _view.Dispatcher.BeginInvoke(DispatcherPriority.Background, Build);

    private static  string Privacy => Loc.T("Show new visible banners only. Messages stay in memory and are cleared when you lock this PC or turn this off.");

    private void Build()
    {
        _view.Children.Clear();
        _trailing.Children.Clear();
        var s = Core.Notifications;
        var items = s.Items;
        var clear = D.IconButton(G.Trash, Loc.T("Clear All"), ClearAll);
        clear.IsEnabled = items.Count > 0;
        _trailing.Children.Add(clear);
        if (items.Count == 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(18), Background = D.White(0.05), Child = D.I(G.Bell, 26, D.White(0.6)) });
            var col = new StackPanel { Margin = new Thickness(14, 0, 0, 0), MaxWidth = 260, VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(D.T(s.Monitoring ? Loc.T("New notifications will appear here") : Loc.T("Waiting for the Windows notification service"), 13, FontWeights.SemiBold, D.Text, true));
            var priv = D.T(Privacy, 11, null, D.White(0.65), true);
            priv.Margin = new Thickness(0, 4, 0, 0);
            col.Children.Add(priv);
            if (s.AccessDenied || s.NeedsIdentity)
            {
                var note = D.T(s.NeedsIdentity ? Loc.T("Reading banners works best when Trayloom is installed as an app.") : Loc.T("Notification access is off."), 10.5, null, D.Orange, true);
                note.Margin = new Thickness(0, 6, 0, 0);
                col.Children.Add(note);
                var allow = D.PillButton(s.AccessDenied ? Loc.T("Open Settings") : "Allow", () => { if (s.AccessDenied) { s.OpenAccessSettings(); } else { _ = s.RequestAccessAsync(); } });
                allow.Margin = new Thickness(0, 6, 0, 0);
                allow.HorizontalAlignment = HorizontalAlignment.Left;
                col.Children.Add(allow);
            }

            row.Children.Add(col);
            _view.Children.Add(row);
            return;
        }

        var rows = Math.Max(1, (int)Math.Floor((_h + 8) / (120 + 8)));
        var cardH = (_h - ((rows - 1) * 8)) / rows;
        var wrap = new WrapPanel { Orientation = Orientation.Vertical, Height = _h };
        foreach (var n in items)
        {
            wrap.Children.Add(Card(n, cardH));
        }

        var sc = PageUi.HScroll(wrap);
        _view.Children.Add(sc);
    }

    private FrameworkElement Card(NotificationItem n, double height)
    {
        var card = new Border { Width = 240, Height = height, CornerRadius = new CornerRadius(14), Background = D.White(0.06), Padding = new Thickness(12), Margin = new Thickness(0, 0, 8, 8), ClipToBounds = true };
        var stack = new StackPanel();
        var head = new Grid { Height = 22 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement ic = n.Icon is not null ? new Image { Source = n.Icon, Width = 18, Height = 18 } : D.I("", 14, D.White(0.85));
        l.Children.Add(ic);
        var an = D.T(n.App.Length == 0 ? Loc.T("Notifications") : n.App, 11, null, D.White(0.6), false, TextTrimming.CharacterEllipsis);
        an.Margin = new Thickness(6, 0, 0, 0);
        an.MaxWidth = 90;
        l.Children.Add(an);
        head.Children.Add(l);
        var r = new StackPanel { Orientation = Orientation.Horizontal };
        r.Children.Add(D.Digits(n.Received.ToString("t"), 11, null, D.White(0.6)));
        if (n.CanOpen)
        {
            r.Children.Add(D.IconButton(G.OpenApp, Loc.T("Open"), () => Core.Notifications.Open(n), size: 20, glyphSize: 10));
        }

        r.Children.Add(D.IconButton(G.Close, Loc.T("Dismiss"), () => Core.Notifications.Dismiss(n), size: 20, glyphSize: 9));
        Grid.SetColumn(r, 1);
        head.Children.Add(r);
        stack.Children.Add(head);
        var t = D.T(n.Title, 13, FontWeights.SemiBold, D.Text, true, TextTrimming.CharacterEllipsis);
        t.MaxHeight = 36;
        stack.Children.Add(t);
        if (n.Subtitle.Length > 0)
        {
            stack.Children.Add(D.T(n.Subtitle, 12, null, D.Text, false, TextTrimming.CharacterEllipsis));
        }

        var b = D.T(n.Body, 12, null, D.White(0.8), true, TextTrimming.CharacterEllipsis);
        b.MaxHeight = Math.Max(16, height - 90);
        stack.Children.Add(b);
        if (n.Unavailable is not null)
        {
            stack.Children.Add(D.T(n.Unavailable, 10, null, D.Orange, false, TextTrimming.CharacterEllipsis));
        }

        card.Child = stack;
        return card;
    }

    private void ClearAll()
    {
        using var hold = HasWindow ? Window.HoldOpen() : null;
        if (MessageBox.Show(Loc.T("Clear All"), Loc.T("Clear All"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
        {
            Core.Notifications.Clear();
        }
    }
}

// ================================================================================================ camera

internal sealed class CameraPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(-1, 1) };
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private WriteableBitmap? _bitmap;
    private bool _running;
    private bool _failed;
    private List<(string Id, string Name)> _cameras = [];
    private string? _cameraId;
    private double _w = 400;
    private double _h = 264;
    private bool _busy;

    // Counts the times the page went away: a start that began before (and is still waiting for the camera list or the device) ends by itself
    private int _generation;

    public CameraPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
    }

    public override string ModuleId => "camera";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Leading => _leading;

    public override FrameworkElement? Trailing => _trailing;

    public override double Height(double width, double budget)
    {
        _w = width;
        _h = budget;
        Build();
        return budget;
    }

    public override void Hidden()
    {
        // a start still under way has nothing to stop yet: it notices this on its own after each wait and lets the camera go
        Interlocked.Increment(ref _generation);
        Guarded.FireAndLog(() => StopAsync(), Core.Ctx.Log, "Camera stop");
    }

    private void Build()
    {
        _view.Children.Clear();
        _leading.Children.Clear();
        _trailing.Children.Clear();
        if (!_running)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) };
            row.Children.Add(new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(18), Background = D.White(0.05), Child = D.I(G.Video, 30, D.White(0.6)) });
            var col = new StackPanel { Margin = new Thickness(16, 0, 0, 0), MaxWidth = 280, VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(D.T(_failed ? Loc.T("The camera could not start. Try opening it again.") : Loc.T("Open a live mirror here. The camera stops when you leave this view."), 12, null, _failed ? D.Orange : D.Secondary, true));
            var open = D.PillButton(Loc.T("Open camera"), () => Guarded.FireAndLog(StartAsync, Core.Ctx.Log, "Camera start"), true);
            open.Margin = new Thickness(0, 8, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            col.Children.Add(open);
            if (_failed)
            {
                var settings = D.PillButton(Loc.T("Open Settings…"), () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-webcam") { UseShellExecute = true }); } catch { } });
                settings.Margin = new Thickness(0, 6, 0, 0);
                settings.HorizontalAlignment = HorizontalAlignment.Left;
                col.Children.Add(settings);
            }

            row.Children.Add(col);
            _view.Children.Add(row);
            return;
        }

        var wide = _w / _h > 1.5;
        var w = Math.Min(_w, _h * 16 / 9);
        var h = Math.Min(_h, _w * 3 / 4);
        _ = wide;
        var holder = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(14), ClipToBounds = true, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // the preview image is one cached element: a rebuilt page (reopened, camera switched, relaid out) takes it from the old holder
        holder.Child = Detach.From(_image);
        _view.Children.Add(holder);
        if (_cameras.Count > 1)
        {
            var current = _cameras.FirstOrDefault(c => c.Id == _cameraId).Name ?? Loc.T("Camera");
            _leading.Children.Add(PageUi.BandMenu(current, null, () => _cameras.Select(c => { var id = c.Id; return new IslandMenu.Item(c.Name, () => { _cameraId = id; Guarded.FireAndLog(RestartAsync, Core.Ctx.Log, "Camera restart"); }, id == _cameraId); }), () => HasWindow ? Window.HoldOpen() : null, current));
        }

        var stop = D.PillButton(Loc.T("Stop camera"), () => Guarded.FireAndLog(() => StopAsync(), Core.Ctx.Log, "Camera stop"), true);
        _trailing.Children.Add(stop);
    }

    /// <summary>Builds the page for the state it is in; a page that cannot be built is logged and never takes the caller down.</summary>
    private bool TryBuild()
    {
        try
        {
            if (_view.Dispatcher.CheckAccess())
            {
                Build();
            }
            else
            {
                _view.Dispatcher.Invoke(Build);
            }

            return true;
        }
        catch (Exception ex)
        {
            Core.Ctx.Log.Error("Camera page", ex);
            return false;
        }
    }

    private async Task RestartAsync()
    {
        await StopAsync(false);
        await StartAsync();
    }

    private async Task StartAsync()
    {
        if (_busy || IsPreview)
        {
            return;
        }

        _busy = true;
        _failed = false;
        try
        {
            await OpenAsync(Volatile.Read(ref _generation));
            if (_running && !TryBuild())
            {
                // the camera must never stream without a preview (and a Stop button) to show for it
                _failed = true;
                await StopAsync(false);
            }

            if (!_running)
            {
                TryBuild();
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task OpenAsync(int generation)
    {
        // the page went away since the start began: whatever is open is let go again (the camera never streams for a page nobody sees)
        bool Stale() => generation != Volatile.Read(ref _generation);
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            if (Stale())
            {
                return;
            }

            _cameras = [.. devices.Select(d => (d.Id, d.Name))];
            _cameraId ??= _cameras.FirstOrDefault().Id;
            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = _cameraId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            });
            if (Stale())
            {
                await StopAsync(false);
                return;
            }

            var source = _capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source is null)
            {
                throw new InvalidOperationException("no color source");
            }

            _reader = await _capture.CreateFrameReaderAsync(source, Windows.Media.MediaProperties.MediaEncodingSubtypes.Bgra8);
            if (Stale())
            {
                await StopAsync(false);
                return;
            }

            _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _reader.FrameArrived += OnFrame;
            await _reader.StartAsync();
            if (Stale())
            {
                await StopAsync(false);
                return;
            }

            _running = true;
        }
        catch (Exception ex)
        {
            if (Stale())
            {
                // the page's Hidden() disposed the camera under this start: not a failure to report
                await StopAsync(false);
                return;
            }

            Core.Ctx.Log.Warn("Camera page: the camera could not start: " + ex.Message);
            _failed = true;
            _running = false;
            await StopAsync(false);
        }
    }

    private void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        try
        {
            OnFrameCore(sender);
        }
        catch
        {
            // a bad frame is dropped
        }
    }

    private void OnFrameCore(MediaFrameReader sender)
    {
        using var frame = sender.TryAcquireLatestFrame();
        var sb = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (sb is null)
        {
            return;
        }

        using var conv = sb.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? SoftwareBitmap.Copy(sb) : SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var w = conv.PixelWidth;
        var h = conv.PixelHeight;
        var buffer = new Windows.Storage.Streams.Buffer((uint)(w * h * 4));
        conv.CopyToBuffer(buffer);
        var bytes = buffer.ToArray();
        _view.Dispatcher.BeginInvoke(() =>
        {
            if (!_running)
            {
                return;
            }

            if (_bitmap is null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
            {
                _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                _image.Source = _bitmap;
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, w, h), bytes, w * 4, 0);
        });
    }

    private async Task StopAsync(bool rebuild = true)
    {
        _running = false;
        try
        {
            if (_reader is not null)
            {
                _reader.FrameArrived -= OnFrame;
                await _reader.StopAsync();
                _reader.Dispose();
            }
        }
        catch
        {
        }

        _reader = null;
        try
        {
            _capture?.Dispose();
        }
        catch
        {
        }

        _capture = null;
        _bitmap = null;
        if (rebuild)
        {
            TryBuild();
        }
    }
}

// ================================================================================================ watch

internal sealed class WatchPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };

    public WatchPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
    }

    public override string ModuleId => "watch";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Leading => _leading;

    public override FrameworkElement? Trailing => _trailing;

    private WatchService W => Core.Watch;

    public override double Height(double width, double budget)
    {
        Build();
        return budget;
    }

    public override void Shown()
    {
        W.Changed += OnChanged;
        W.SetVisiblePage(true);
    }

    public override void Hidden()
    {
        W.Changed -= OnChanged;
        W.SetVisiblePage(false);
    }

    private void OnChanged() => _view.Dispatcher.BeginInvoke(DispatcherPriority.Background, Build);

    private void Build()
    {
        _view.Children.Clear();
        _leading.Children.Clear();
        _trailing.Children.Clear();
        if (W.State == WatchState.Idle || IsPreview)
        {
            _view.Children.Add(Setup());
            return;
        }

        var name = new StackPanel { Orientation = Orientation.Horizontal };
        if (W.AppIcon is not null)
        {
            name.Children.Add(new Image { Source = W.AppIcon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
        }

        name.Children.Add(D.T(W.AppName, 12, FontWeights.SemiBold, D.White(0.85), false, TextTrimming.CharacterEllipsis));
        if (W.WindowTitle.Length > 0)
        {
            var t = D.T(" · " + W.WindowTitle, 12, null, D.Secondary, false, TextTrimming.CharacterEllipsis);
            t.MaxWidth = 140;
            name.Children.Add(t);
        }

        D.Tip(name, W.WindowTitle.Length > 0 ? $"{W.AppName} · {W.WindowTitle}" : W.AppName);
        _leading.Children.Add(name);
        _trailing.Children.Add(D.IconButton("", Loc.T("Choose Another Area"), Choose));
        var stop = D.IconButton(G.Close, Loc.T("Stop Watching"), () => W.Stop());
        stop.Margin = new Thickness(6, 0, 0, 0);
        _trailing.Children.Add(stop);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var card = new Border { CornerRadius = new CornerRadius(14), Background = D.White(0.07), Padding = new Thickness(10) };
        var col = new Grid();
        col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var pic = new Grid();
        if (W.Picture is not null)
        {
            pic.Children.Add(new Image { Source = W.Picture, Stretch = Stretch.Uniform, Opacity = W.State == WatchState.Hidden ? 0.35 : 1 });
        }
        else
        {
            var sp = PageUi.Spinner(18);
            pic.Children.Add(sp);
        }

        if (W.State == WatchState.Hidden)
        {
            var lab = new Border { Background = D.Frozen(Color.FromArgb(153, 0, 0, 0)), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var lr = new StackPanel { Orientation = Orientation.Horizontal };
            lr.Children.Add(D.I(G.EyeOff, 11, D.Text));
            var lt = D.T(Loc.T("The window is hidden. Watching continues when it returns."), 11, null, D.Text, true);
            lt.Margin = new Thickness(6, 0, 0, 0);
            lt.MaxWidth = 260;
            lr.Children.Add(lt);
            lab.Child = lr;
            pic.Children.Add(lab);
        }

        var pb = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = pic };
        col.Children.Add(pb);
        var info = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (W.State == WatchState.Finished)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(D.I(W.OutcomeClosed ? G.Close : "", 14, W.OutcomeClosed ? D.Secondary : D.Green));
            var o = D.T(W.Outcome ?? "", 13, FontWeights.SemiBold);
            o.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(o);
            info.Children.Add(row);
        }
        else if (!W.HasReading || string.IsNullOrWhiteSpace(W.FullText))
        {
            if (W.HasReading)
            {
                info.Children.Add(D.T(Loc.T("No text"), 15, FontWeights.SemiBold, D.Secondary));
            }
        }
        else
        {
            HoverButton? head = null;
            head = PageUi.BandMenu(W.Headline, null, () =>
            {
                var items = new List<IslandMenu.Item> { new(Loc.T("Show in the island"), null, Enabled: false), new(Loc.T("Automatic"), () => W.SetHeadlineLine(-1), W.HeadlineLine < 0) };
                for (var i = 0; i < Math.Min(12, W.Lines.Count); i++)
                {
                    var idx = i;
                    items.Add(new IslandMenu.Item(W.Lines[i], () => W.SetHeadlineLine(idx), W.HeadlineLine == idx));
                }

                return items;
            }, () => HasWindow ? Window.HoldOpen() : null, null, 260);
            head.HorizontalAlignment = HorizontalAlignment.Left;
            info.Children.Add(head);
            if (W.FullText.Replace("\n", " ").Trim() != W.Headline)
            {
                var full = D.T(W.FullText.Replace("\n", " "), 11, null, D.Secondary, true, TextTrimming.CharacterEllipsis);
                full.MaxHeight = 30;
                info.Children.Add(full);
            }
        }

        Grid.SetRow(info, 1);
        col.Children.Add(info);
        card.Child = col;
        root.Children.Add(card);
        var ruleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Height = 24 };
        ruleRow.Children.Add(D.T(Loc.T("Tell me"), 11, null, D.Secondary));
        var cond = W.Condition;
        HoverButton? cb = null;
        var names = new Dictionary<WatchCondition, string> { [WatchCondition.Changes] = Loc.T("When it changes"), [WatchCondition.Settles] = Loc.T("When it stops changing"), [WatchCondition.Contains] = Loc.T("When it shows"), [WatchCondition.Reaches] = Loc.T("When the number reaches") };
        cb = PageUi.BandMenu(names[cond], null, () => names.Select(kv => new IslandMenu.Item(kv.Value, () => { W.Condition = kv.Key; Build(); }, kv.Key == cond)), () => HasWindow ? Window.HoldOpen() : null, null, 200);
        cb.Height = 24;
        ruleRow.Children.Add(cb);
        if (cond is WatchCondition.Contains or WatchCondition.Reaches)
        {
            var box = PageUi.FlatTextBox(12);
            box.Text = cond == WatchCondition.Contains ? W.TypedText : W.TypedNumber;
            var field = new Border { Height = 24, MaxWidth = 180, Width = 150, CornerRadius = new CornerRadius(7), Background = D.White(0.08), Padding = new Thickness(8, 0, 8, 0), Child = box, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
            box.GotKeyboardFocus += (_, _) => field.BorderBrush = D.White(0.34);
            box.LostKeyboardFocus += (_, _) =>
            {
                field.BorderBrush = Brushes.Transparent;
                Apply();
            };
            void Apply()
            {
                if (cond == WatchCondition.Contains)
                {
                    W.TypedText = box.Text;
                }
                else
                {
                    W.TypedNumber = box.Text;
                }
            }

            box.PreviewMouseDown += (_, _) =>
            {
                if (HasWindow)
                {
                    Window.EnsureKeyboard();
                }
            };
            box.KeyDown += (_, e) => { if (e.Key == Key.Return) { Apply(); } };
            if (HasWindow)
            {
                Window.EnsureKeyboard();
            }

            var ph = D.T(cond == WatchCondition.Contains ? Loc.T("Text, like Done") : Loc.T("Number, like 100"), 12, null, D.White(0.35));
            ph.IsHitTestVisible = false;
            ph.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += (_, _) => ph.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var host = new Grid();
            host.Children.Add(box);
            host.Children.Add(ph);
            field.Child = host;
            field.Margin = new Thickness(6, 0, 0, 0);
            ruleRow.Children.Add(field);
        }
        else if (cond == WatchCondition.Settles)
        {
            var hint = D.T(Loc.T("After it changes, it must stay still for 30 seconds."), 10, null, D.Secondary);
            hint.Margin = new Thickness(8, 0, 0, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            ruleRow.Children.Add(hint);
        }

        Grid.SetRow(ruleRow, 1);
        root.Children.Add(ruleRow);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        if (W.State == WatchState.Finished)
        {
            if (W.CanWatchAgain)
            {
                footer.Children.Add(D.PillButton(Loc.T("Watch Again"), () => W.WatchAgain(), true));
            }

            var other = D.PillButton(Loc.T("Choose Another Area"), Choose, !W.CanWatchAgain);
            other.Margin = new Thickness(6, 0, 0, 0);
            footer.Children.Add(other);
        }
        else
        {
            footer.Children.Add(D.T(Loc.F("Watching since {0}", Loc.Fmt(W.Since, "t")), 10, null, D.Secondary));
        }

        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        _view.Children.Add(root);
    }

    private void Choose()
    {
        if (HasWindow)
        {
            Window.Collapse();
        }

        W.ChooseArea(() => Core.Ui.BeginInvoke(() => Core.Window?.Open("watch", false, false)));
    }

    private FrameworkElement Setup()
    {
        var enabled = Core.App.Get<bool>(K.WatchEnabled);
        var tile = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(18), Background = D.White(0.05), Child = D.I(G.Eye, 26, D.White(0.65)) };
        var copy = D.T(enabled
            ? Loc.T("Choose any part of a window, like a progress bar, a build status or a score. The island reads it for you and tells you when it changes.")
            : Loc.T("Turn any part of any window into a live activity in the Dynamic Island, with an alert when it changes, finishes or shows what you are waiting for."), 12, null, D.White(0.65), true);
        copy.MaxWidth = 300;
        copy.Margin = new Thickness(0, 12, 0, 12);
        copy.TextAlignment = TextAlignment.Center;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 360 };
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(tile);
        stack.Children.Add(copy);
        if (!enabled)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            row.Children.Add(D.T(Loc.T("Watch"), 12, FontWeights.Medium));
            var tg = new IslandToggle(false, on => { Core.App.Set(K.WatchEnabled, on); Build(); });
            tg.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(tg);
            stack.Children.Add(row);
        }
        else
        {
            var btn = D.PillButton(Loc.T("Choose Area"), Choose, true);
            btn.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(btn);
        }

        return stack;
    }
}

// ================================================================================================ captures + tools

internal sealed class CapturesPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };

    public CapturesPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
    }

    public override string ModuleId => "captures";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Trailing => _trailing;

    private static IEnumerable<string> Folders()
    {
        var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        yield return Path.Combine(pics, "Trayloom");
        yield return Path.Combine(pics, LegacyNames.FolderName);
        yield return Path.Combine(pics, "Screenshots");
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        yield return Path.Combine(videos, "Trayloom");
        yield return Path.Combine(videos, LegacyNames.FolderName);
        yield return Path.Combine(videos, "Captures");
    }

    public override double Height(double width, double budget)
    {
        Build(width, budget);
        return budget;
    }

    private void Build(double width, double budget)
    {
        _view.Children.Clear();
        _trailing.Children.Clear();
        var files = new List<FileInfo>();
        foreach (var f in Folders().Where(Directory.Exists))
        {
            try
            {
                files.AddRange(new DirectoryInfo(f).EnumerateFiles().Where(x => x.Extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".mp4" or ".webp"));
            }
            catch
            {
            }
        }

        var recent = files.OrderByDescending(f => f.LastWriteTime).Take(24).ToList();
        if (recent.Count == 0)
        {
            _view.Children.Add(D.EmptyView(G.Camera, Loc.T("No recent captures")));
            return;
        }

        var wrap = new WrapPanel { Orientation = Orientation.Vertical, Height = budget };
        foreach (var f in recent)
        {
            var path = f.FullName;
            var b = new HoverButton(() => DownloadsPage.OpenPath(path)) { Width = 150, Height = (budget / 2) - 8, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 8, 8), ClipToBounds = true };
            try
            {
                if (f.Extension.ToLowerInvariant() is ".mp4")
                {
                    b.Child = D.I(G.Play, 24, D.Text);
                }
                else
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 300;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    b.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
                }
            }
            catch
            {
                b.Child = D.I(G.Photo, 20, D.Secondary);
            }

            D.Tip(b, f.Name);
            wrap.Children.Add(b);
        }

        var sc = PageUi.HScroll(wrap);
        _view.Children.Add(sc);
    }
}

internal sealed class ToolsPage : IslandPage
{
    private readonly Grid _view = new();

    public ToolsPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
    }

    public override string ModuleId => "tools";

    public override FrameworkElement View => _view;

    public override double Height(double width, double budget)
    {
        _view.Children.Clear();
        var tools = new (string Id, string Glyph, string Title)[]
        {
            ("screenshot", G.Camera, Loc.T("Screenshot")), ("screen-recorder", G.Record, Loc.T("Screen recording")), ("screen-ocr", "", Loc.T("Copy text from screen")),
            ("color-picker", "", Loc.T("Color picker")), ("clipboard-history", G.Clipboard, Loc.T("Clipboard")), ("scratchpad", G.Note, Loc.T("Scratchpad")),
            ("camera-preview", G.Video, Loc.T("Camera preview")), ("command-bar", G.Command, Loc.T("Command Bar")), ("cleaning-mode", "", Loc.T("Cleaning Mode")),
            ("radial-menu", "", Loc.T("Radial menu")), ("quick-toggles", "", Loc.T("Quick toggles")), ("quick-launcher", G.Tools, Loc.T("Quick panel")),
        };
        var items = new List<FrameworkElement>();
        foreach (var (id, glyph, title) in tools)
        {
            if (!Core.Ctx.Runtime.IsAvailable(id) || !Core.Ctx.Actions.All.Any(a => a.Feature.CatalogId == id))
            {
                continue;
            }

            var tid = id;
            items.Add(PageUi.ActionTile(glyph, title, () => Core.Perform(() => Core.InvokeFeatureAction(tid)), TileAccent.None, null, null, 76, 72));
        }

        if (items.Count == 0)
        {
            _view.Children.Add(D.EmptyView(G.Tools, Loc.T("Tools and utilities, one click away.")));
            return Math.Min(budget, 140);
        }

        var rows = Math.Max(1, (int)Math.Floor((budget + 6) / 78.0));
        _view.Children.Add(PageUi.Rail(items, 76, 72, 6, width, (rows * 72) + ((rows - 1) * 6), 6));
        return Math.Min(budget, (Math.Min(rows, (int)Math.Ceiling(items.Count / Math.Max(1.0, Math.Floor((width + 6) / 82.0)))) * 78));
    }
}
