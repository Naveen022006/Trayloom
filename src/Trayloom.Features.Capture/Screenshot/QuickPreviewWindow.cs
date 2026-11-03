// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Trayloom.Core.Ui;
using Trayloom.Core;

namespace Trayloom.Features.Capture;

/// <summary>The floating 350x210 thumbnail after a capture: Edit, Copy, Save, Discard, Pin, Share, Copy text, drag-out.</summary>
internal sealed class QuickPreviewWindow : Window
{
    private readonly ScreenshotService _svc;
    private readonly Capture _capture;
    private string? _savedPath;
    private bool _copied;
    private readonly int _seconds;
    private readonly DispatcherTimer _timer = new();
    private readonly bool _afterAction;
    private EdButton? _saveBtn;
    private EdButton? _copyBtn;
    private QrReading? _qr;
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal };
    private bool _closed;
    private long _shownAt;

    public QuickPreviewWindow(ScreenshotService svc, Capture capture, string? savedPath, bool copied, int seconds, bool afterAction)
    {
        _svc = svc;
        _capture = capture;
        _savedPath = savedPath;
        _copied = copied;
        _seconds = seconds;
        _afterAction = afterAction;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        UseLayoutRounding = true;
        Width = 350 + 24;
        Height = 210 + 24;
        var focus = svc.App.Get<bool>(K.PreviewTakesFocus) && seconds != 0;
        ShowActivated = focus;
        Focusable = true;

        var card = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(10), Margin = new Thickness(12) };
        card.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x28));
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.4, Direction = 270 };
        var stack = new StackPanel();
        stack.Children.Add(BuildThumb());
        BuildActions();
        _actions.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(_actions);
        card.Child = stack;
        Content = card;

        _timer.Tick += (_, _) => Close();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => Restart();
        KeyDown += OnKey;
        Deactivated += (_, _) =>
        {
            // click outside closes (not for persistent previews that never took focus)
            // The capture overlay hands activation back to the previous window right after the preview opens; that is not a click outside.
            if (IsLoaded && focus && !_closed && Environment.TickCount64 - _shownAt > 1200)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!IsActive && !_closed && !IsMouseOver)
                    {
                        Close();
                    }
                });
            }
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
        };
    }

    private void Restart()
    {
        _timer.Stop();
        if (_seconds > 0)
        {
            _timer.Interval = TimeSpan.FromSeconds(_seconds);
            _timer.Start();
        }
    }

    public void Present()
    {
        _shownAt = Environment.TickCount64;
        _svc.ApplyAffinity(this);
        Place();
        Opacity = 0;
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        if (ShowActivated)
        {
            Activate();
        }

        Restart();
        _ = ScanQrAsync();
    }

    private async Task ScanQrAsync()
    {
        try
        {
            var qr = await Task.Run(() => OcrSupport.ReadQr(_capture.Image));
            if (qr is not null && !_closed)
            {
                _qr = qr;
                BuildActions();
            }
        }
        catch
        {
            // optional
        }
    }

    private void Place()
    {
        var anchor = _capture.Anchor;
        var mon = ScreenGrab.MonitorAt(anchor.X + anchor.Width / 2, anchor.Y + anchor.Height / 2);
        var work = mon.Work;
        var scale = mon.Scale;
        var w = (int)((350 + 24) * scale);
        var h = (int)((210 + 24) * scale);
        var pos = _svc.App.Get<string>(K.PreviewPosition) ?? string.Empty;
        var inset = (int)(16 * scale);
        int x, y;
        if (pos.Length == 0)
        {
            var automatic = anchor.Width > 0 && anchor.Height > 0 && string.IsNullOrEmpty(_svc.App.Get<string>(K.DefaultAction));
            var gap = (int)(14 * scale);
            var pad = (int)(10 * scale);
            if (!automatic)
            {
                x = work.X + work.Width - w - inset;
                y = work.Y + work.Height - h - inset;
            }
            else
            {
                var right = anchor.X + anchor.Width + gap;
                var left = anchor.X - gap - w;
                x = right + w <= work.X + work.Width - pad ? right : left >= work.X + pad ? left : ScreenGrab.CursorPos().X;
                var cy = anchor.Y + anchor.Height / 2 - h / 2;
                y = cy;
            }
        }
        else
        {
            x = pos.EndsWith("Left", StringComparison.Ordinal) ? work.X + inset : work.X + work.Width - w - inset;
            y = pos.StartsWith("top", StringComparison.Ordinal) ? work.Y + inset : work.Y + work.Height - h - inset;
        }

        x = Math.Clamp(x, work.X + (int)(8 * scale), Math.Max(work.X, work.X + work.Width - w - (int)(8 * scale)));
        y = Math.Clamp(y, work.Y + (int)(8 * scale), Math.Max(work.Y, work.Y + work.Height - h - (int)(8 * scale)));
        Left = x / scale;
        Top = y / scale;
        SourceInitialized += (_, _) => N.SetWindowPos(new WindowInteropHelper(this).Handle, -1, x, y, w, h, 0x0010);
    }

    private UIElement BuildThumb()
    {
        var img = new Image { Source = _capture.Image, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var inner = new Border { Background = new SolidColorBrush(Color.FromArgb(31, 0, 0, 0)), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)), Child = img, ClipToBounds = true, Cursor = Cursors.Hand };
        var grid = new Grid { Width = 320, Height = 138 };
        grid.Children.Add(inner);
        var overlay = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0) };
        if (CaptureHost.Installed("screen-ocr"))
        {
            overlay.Children.Add(Round(G.TextScan, Loc.T("Copy text"), () => _ = CopyTextAsync()));
        }

        overlay.Children.Add(Round(G.Share, Loc.T("Share"), Share));
        overlay.Children.Add(Round(G.Pin, Loc.T("Pin to screen"), () =>
        {
            _svc.Pin(_capture.Image, _capture.Scale);
            Close();
        }));
        grid.Children.Add(overlay);
        var down = default(Point);
        inner.MouseLeftButtonDown += (_, e) => down = e.GetPosition(this);
        inner.MouseLeftButtonUp += (_, e) =>
        {
            if ((e.GetPosition(this) - down).Length < 4)
            {
                Edit();
            }
        };
        inner.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(this) - down).Length > 6)
            {
                DragOut(inner);
            }
        };
        return grid;
    }

    private static Border Round(string glyph, string tip, Action click)
    {
        var b = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(3, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = tip,
            Background = new SolidColorBrush(Color.FromArgb(220, 40, 40, 44)), BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = Ed.Icon(glyph, 11),
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return b;
    }

    private void BuildActions()
    {
        _actions.Children.Clear();
        _actions.Children.Add(new EdButton(G.Trash, null, Loc.T("Discard  (Backspace)"), Discard, width: 28, height: 26));
        if (_qr is not null)
        {
            _actions.Children.Add(new EdButton(G.QrCode, null, Strings.Screenshot.QrCode, () => { QrResultWindow.Show(_qr, null); Close(); }, width: 28, height: 26) { Active = true });
        }

        _saveBtn = new EdButton(G.Save, Strings.Screenshot.SaveLabel, "Ctrl+S", Save, height: 26) { Margin = new Thickness(5, 0, 0, 0), IsEnabled = _savedPath is null };
        _copyBtn = new EdButton(G.Copy, Strings.Screenshot.CopyLabel, "Ctrl+C", Copy, height: 26) { Margin = new Thickness(5, 0, 0, 0), IsEnabled = !_copied };
        _actions.Children.Add(_saveBtn);
        _actions.Children.Add(_copyBtn);
        var spacer = new Border { Width = Math.Max(0, 320 - 28 - (_qr is null ? 0 : 28) - 5 * 2 - 150 - 78) };
        _actions.Children.Add(spacer);
        var edit = new EdButton(G.Edit, Strings.Screenshot.EditLabel, Loc.T("Edit  (Enter)"), Edit, prominent: true, height: 26) { Margin = new Thickness(5, 0, 0, 0) };
        _actions.Children.Add(edit);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Return or Key.Enter or Key.E when !e.IsRepeat:
                Edit();
                break;
            case Key.Escape:
                Close();
                break;
            case Key.Back or Key.Delete:
                Discard();
                break;
            case Key.C when ctrl && !e.IsRepeat:
                Copy();
                break;
            case Key.S when ctrl && !e.IsRepeat:
                Save();
                break;
            case Key.W when ctrl:
                Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Edit()
    {
        var capture = _capture;
        Close();
        Dispatcher.BeginInvoke(() => _svc.OpenEditor(capture, false));
    }

    private void Copy()
    {
        var (img, scale) = Output.ApplyDownscale(_svc.App, _capture.Image, _capture.Scale);
        if (Output.CopyImage(img, scale, _svc.App.Get<bool>(K.CopyImageOnly)))
        {
            QuickToolHud.Show(G.Camera, Strings.Screenshot.Copied);
            Close();
        }
        else
        {
            CaptureHost.Beep();
            Restart();
        }
    }

    private void Save()
    {
        var path = _svc.SaveCapture(_capture);
        if (path is null)
        {
            CaptureHost.Beep();
            Restart();
            return;
        }

        QuickToolHud.Show(G.Camera, string.Format(Strings.Screenshot.SavedFormat, Path.GetFileName(Path.GetDirectoryName(path)) ?? Strings.Desktop));
        Close();
    }

    private void Discard()
    {
        if (_savedPath is not null)
        {
            Output.Recycle(_savedPath);
        }

        _svc.WithholdLatest();
        Close();
    }

    private void Share()
    {
        try
        {
            var (img, scale) = Output.ApplyDownscale(_svc.App, _capture.Image, _capture.Scale);
            var dir = Path.Combine(Path.GetTempPath(), "Trayloom", "ScreenshotShare-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, Output.DefaultName(Strings.Screenshot.FileNamePrefix, DateTime.Now) + ".png");
            Output.SavePng(img, file, scale);
            if (ShareHelper.Share(new WindowInteropHelper(this).Handle, file, Strings.Screenshot.PageTitle))
            {
                Close();
            }
        }
        catch
        {
            CaptureHost.Beep();
        }
    }

    private async Task CopyTextAsync()
    {
        var text = CaptureHost.Text;
        if (text is null)
        {
            return;
        }

        await text.RecognizeToClipboardAsync(_capture.Image, qr: false);
        Close();
    }

    private void DragOut(UIElement source)
    {
        try
        {
            var (img, scale) = Output.ApplyDownscale(_svc.App, _capture.Image, _capture.Scale);
            var dir = Path.Combine(Path.GetTempPath(), "Trayloom", "ScreenshotDrag-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, Output.DefaultName(Strings.Screenshot.FileNamePrefix, DateTime.Now) + ".png");
            Output.SavePng(img, file, scale);
            var data = new DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { file });
            data.SetImage(img);
            _timer.Stop();
            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
        }
        catch
        {
            // cancelled
        }
    }
}
