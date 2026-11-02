// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WinKnife.Features.SystemTools;

/// <summary>
/// The tray metrics (spec 01 2.5 / 2.10): the pinned metrics are drawn as blocks in a strip docked next to the notification area, or - with
/// "Separate metrics into their own items" - as one tray icon per group. Samples only while a metric is pinned.
/// </summary>
internal sealed class MetricTray : ITrayMetricsState
{
    private readonly IFeatureContext _ctx;
    private readonly IAppSettings _app;
    private readonly IFeatureRuntime _runtime;
    private readonly DispatcherTimer _dock;
    private StripWindow? _strip;
    private readonly Dictionary<string, MetricIcon> _icons = [];
    private bool _started;
    private bool _hasMetrics;

    /// <inheritdoc />
    public bool HasMetrics => _hasMetrics;

    /// <inheritdoc />
    public event Action? Changed;

    private void SetHasMetrics(bool value)
    {
        if (_hasMetrics != value)
        {
            _hasMetrics = value;
            Changed?.Invoke();
        }
    }

    public MetricTray(IFeatureContext ctx)
    {
        _ctx = ctx;
        _app = ctx.App;
        _runtime = ctx.Runtime;
        _dock = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Redock(), ctx.Ui);
        _dock.Stop();
    }

    public void Start()
    {
        _started = true;
        _ctx.Services.Provide<ITrayMetricsState>(this);
        _app.Changed += OnSetting;
        _runtime.Changed += Refresh;
        MonitorService.Updated += OnUpdated;
        SystemEvents.DisplaySettingsChanged += OnDisplays;
        SystemEvents.UserPreferenceChanged += OnPreference;
        Refresh();
    }

    public void Stop()
    {
        _started = false;
        _app.Changed -= OnSetting;
        _runtime.Changed -= Refresh;
        MonitorService.Updated -= OnUpdated;
        SystemEvents.DisplaySettingsChanged -= OnDisplays;
        SystemEvents.UserPreferenceChanged -= OnPreference;
        _dock.Stop();
        MonitorService.SetBackground("tray", Needs.None);
        CloseStrip();
        CloseIcons();
        SetHasMetrics(false);
        _ctx.Services.Withdraw<ITrayMetricsState>(this);
    }

    public void Refresh()
    {
        if (!_started)
        {
            return;
        }

        MonitorService.SetBackground("tray", MetricBlocks.NeedsOf(_app, _runtime));
        if (MonitorService.Last is { } last)
        {
            Render(last);
        }
        else if (MetricBlocks.NeedsOf(_app, _runtime) == Needs.None)
        {
            CloseStrip();
            CloseIcons();
            SetHasMetrics(false);
        }
    }

    private void OnSetting(string key)
    {
        if (key.StartsWith("menuBar", StringComparison.Ordinal) || key is "temperatureUnit" or "networkSpeedUnit" or "monitorMemoryMetric")
        {
            _ctx.Ui.BeginInvoke(Refresh);
        }
    }

    private void OnDisplays(object? sender, EventArgs e) => _ctx.Ui.BeginInvoke(Redock);

    private void OnPreference(object? sender, UserPreferenceChangedEventArgs e) => _ctx.Ui.BeginInvoke(() =>
    {
        if (MonitorService.Last is { } last)
        {
            Render(last);
        }
    });

    private void OnUpdated(MonitorSnapshot s)
    {
        if (_started)
        {
            Render(s);
        }
    }

    // ------------------------------------------------------------------------------------------------ rendering

    private void Render(MonitorSnapshot s)
    {
        var blocks = MetricBlocks.Build(_app, _runtime, s);
        SetHasMetrics(blocks.Count > 0);
        if (blocks.Count == 0)
        {
            CloseStrip();
            CloseIcons();
            return;
        }

        var separate = _app.Get<bool>("menuBarSeparateMetrics");
        if (separate)
        {
            CloseStrip();
            RenderIcons(blocks);
        }
        else
        {
            CloseIcons();
            RenderStrip(blocks);
        }
    }

    // ------------------------------------------------------------------------------------------------ strip

    private void RenderStrip(List<TrayBlock> blocks)
    {
        _strip ??= new StripWindow(this);
        _strip.Update(blocks, _app);
        if (!_dock.IsEnabled)
        {
            _dock.Start();
        }

        Redock();
    }

    private void CloseStrip()
    {
        _dock.Stop();
        _strip?.Close();
        _strip = null;
    }

    private void Redock()
    {
        _strip?.Dock();
    }

    internal void Clicked(TrayBlock block) => DetailRouter.Request(_ctx.Shell, block.Detail);

    internal void OpenSettings() => _ctx.Shell.OpenSettings("monitor");

    // ------------------------------------------------------------------------------------------------ icons

    private void RenderIcons(List<TrayBlock> blocks)
    {
        // Groups: with "combine" the temperature joins its usage block; the order of the blocks is kept.
        var groups = blocks.GroupBy(b => b.Group).ToList();
        foreach (var gone in _icons.Keys.Where(k => groups.All(g => g.Key != k)).ToList())
        {
            _icons[gone].Dispose();
            _icons.Remove(gone);
        }

        var light = ThemeManager.ReadTaskbarIsLight();
        var color = light ? Color.FromRgb(0x18, 0x18, 0x1A) : Colors.White;
        foreach (var group in groups)
        {
            if (!_icons.TryGetValue(group.Key, out var icon))
            {
                icon = new MetricIcon(this, group.Key);
                _icons[group.Key] = icon;
            }

            icon.Update(group.ToList(), MetricBlocks.PaletteFor(_app, color), _app.Get<string>("menuBarMetricSpacing") == "compact");
        }
    }

    private void CloseIcons()
    {
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();
    }

    // ------------------------------------------------------------------------------------------------ the strip window

    private sealed class StripWindow : OverlayWindow
    {
        private readonly MetricTray _owner;
        private readonly Image _image = new() { Stretch = Stretch.None, SnapsToDevicePixels = true };
        private List<(double From, double To, TrayBlock Block)> _hits = [];
        private double _width;
        private double _scale = 1;

        public StripWindow(MetricTray owner)
        {
            _owner = owner;
            ClickThrough = false;
            Content = _image;
            Width = 10;
            Height = 10;
            Cursor = Cursors.Arrow;
            MouseLeftButtonUp += (_, e) =>
            {
                var x = e.GetPosition(this).X;
                var hit = _hits.FirstOrDefault(h => x >= h.From - 4 && x <= h.To + 4);
                if (hit.Block is not null)
                {
                    _owner.Clicked(hit.Block);
                }
            };
            MouseRightButtonUp += (_, _) => _owner.OpenSettings();
        }

        public void Update(List<TrayBlock> blocks, IAppSettings app)
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            _scale = dpi;
            var light = ThemeManager.ReadTaskbarIsLight();
            var color = light ? Color.FromRgb(0x18, 0x18, 0x1A) : Colors.White;
            var palette = MetricBlocks.PaletteFor(app, color);
            var compact = app.Get<string>("menuBarMetricSpacing") == "compact";
            var (visual, width, hits) = MetricBlocks.Draw(blocks, palette, compact, dpi, compact ? 7 : 10);
            _width = width + 12;
            _hits = hits.Select(h => (h.From + 6, h.To + 6, h.Block)).ToList();
            // Draw with a 6 DIP inset on both sides.
            var padded = new DrawingVisual();
            using (var dc = padded.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(6, 0));
                dc.DrawDrawing(visual.Drawing);
                dc.Pop();
            }

            _image.Source = MetricBlocks.Render(padded, _width, 21, dpi);
        }

        public void Dock()
        {
            var tray = FindWindow("Shell_TrayWnd", null);
            if (tray == 0 || !IsWindowVisible(tray) || !GetWindowRect(tray, out var taskbar))
            {
                HideStrip();
                return;
            }

            // Full-screen apps (and presentation mode) hide the strip.
            if (SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4)
            {
                HideStrip();
                return;
            }

            var notify = FindWindowEx(tray, 0, "TrayNotifyWnd", null);
            var left = taskbar.Right;
            if (notify != 0 && GetWindowRect(notify, out var area))
            {
                left = area.Left;
            }

            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var widthPx = (int)Math.Ceiling(_width * dpi);
            var heightPx = Math.Max(taskbar.Bottom - taskbar.Top, 1);
            // Horizontal taskbars only: vertical ones would need a stacked layout.
            if (heightPx > (taskbar.Right - taskbar.Left))
            {
                HideStrip();
                return;
            }

            if (!IsVisible)
            {
                ShowNoActivate();
            }

            SetPhysicalBounds(left - widthPx - (int)(4 * dpi), taskbar.Top, widthPx, heightPx);
            _image.VerticalAlignment = VerticalAlignment.Center;
            _image.HorizontalAlignment = HorizontalAlignment.Left;
        }

        private void HideStrip()
        {
            if (IsVisible)
            {
                Hide();
            }

        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect32
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint FindWindow(string cls, string? title);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint FindWindowEx(nint parent, nint after, string cls, string? title);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(nint hwnd, out Rect32 rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(nint hwnd);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);
    }

    // ------------------------------------------------------------------------------------------------ separate tray items

    private sealed class MetricIcon : IDisposable
    {
        private readonly MetricTray _owner;
        private readonly Forms.NotifyIcon _icon;
        private readonly string _group;
        private Drawing.Icon? _image;
        private TrayBlock? _first;

        public MetricIcon(MetricTray owner, string group)
        {
            _owner = owner;
            _group = group;
            _icon = new Forms.NotifyIcon { Visible = true };
            _icon.MouseUp += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left && _first is not null)
                {
                    _owner.Clicked(_first);
                }
                else if (e.Button == Forms.MouseButtons.Right)
                {
                    _owner.OpenSettings();
                }
            };
        }

        public void Update(List<TrayBlock> blocks, MetricBlocks.Palette palette, bool compact)
        {
            _first = blocks[0];
            var size = Math.Max(16, Forms.SystemInformation.SmallIconSize.Width);
            var scale = size / 16.0;
            var dpi = scale;
            var main = blocks[0];
            // 16 px only fits about three digits: value-only blocks and the label in the tooltip; from 24 px on label and value.
            var drawn = size >= 24 ? blocks : blocks.Select(b => b with { Label = string.Empty }).Take(1).ToList();
            var (visual, width, _) = MetricBlocks.Draw(drawn, palette, true, dpi, 4);
            var canvas = new DrawingVisual();
            var iconSize = size / scale;
            using (var dc = canvas.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(Math.Max(0, (iconSize - width) / 2), (iconSize - 21) / 2));
                dc.DrawDrawing(visual.Drawing);
                dc.Pop();
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            var scaled = new DrawingVisual();
            using (var dc = scaled.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawDrawing(canvas.Drawing);
                dc.Pop();
            }

            bitmap.Render(scaled);
            var old = _image;
            _image = ToIcon(bitmap);
            _icon.Icon = _image;
            old?.Dispose();
            var tip = string.Join("  ", blocks.Select(b => $"{(b.Label.Length > 0 ? b.Label : main.GroupTitle)} {(b.Style == BlockStyle.TwoLines ? b.Line1 + " " + b.Line2 : b.Value)}"));
            _icon.Text = (main.GroupTitle + ": " + tip.Replace(main.GroupTitle + " ", string.Empty)).Length > 120 ? main.GroupTitle : main.GroupTitle + ": " + tip;
            _ = compact;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _image?.Dispose();
            _image = null;
            _ = _group;
        }

        private static Drawing.Icon ToIcon(BitmapSource source)
        {
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);
            stream.Position = 0;
            using var bitmap = new Drawing.Bitmap(stream);
            var handle = bitmap.GetHicon();
            using var temp = Drawing.Icon.FromHandle(handle);
            var clone = (Drawing.Icon)temp.Clone();
            DestroyIcon(handle);
            return clone;
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(nint handle);
    }
}
