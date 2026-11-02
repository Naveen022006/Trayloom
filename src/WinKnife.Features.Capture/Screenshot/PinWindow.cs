// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>A pinned screenshot: borderless always-on-top window, aspect-locked resize, opacity presets, click-through with Alt-click escape.</summary>
internal sealed class PinWindow : Window
{
    private static readonly List<PinWindow> Pins = new();
    private static int _created;
    private static IDisposable? _hook;
    private readonly ScreenshotService _svc;
    private readonly BitmapSource _image;
    private readonly double _scale;
    private readonly double _aspect;
    private bool _clickThrough;

    private PinWindow(ScreenshotService svc, BitmapSource image, double scale)
    {
        _svc = svc;
        _image = image;
        _scale = scale <= 0 ? 1 : scale;
        _aspect = (double)image.PixelWidth / Math.Max(1, image.PixelHeight);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        UseLayoutRounding = true;
        MinWidth = 90;
        MinHeight = 60;
        var img = new Image { Source = image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var border = new Border
        {
            Child = img, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.FromArgb(102, 128, 128, 128)), Background = Brushes.Black,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.4, Direction = 270 },
            Margin = new Thickness(10),
        };
        Content = border;
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                Close();
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
                // button released
            }
        };
        KeyDown += OnPinKey;
        ContextMenuOpening += (_, _) => { };
        ContextMenu = BuildMenu();
        Closed += (_, _) =>
        {
            Pins.Remove(this);
            UpdateHook();
        };
    }

    public static void Create(ScreenshotService svc, BitmapSource image, double scale)
    {
        var pin = new PinWindow(svc, image, scale);
        var mon = ScreenGrab.MonitorUnderCursor();
        var dpi = mon.Scale;
        var maxSide = 0.55 * Math.Min(mon.Work.Width, mon.Work.Height);
        var wPx = image.PixelWidth;
        var hPx = image.PixelHeight;
        var f = Math.Min(1.0, maxSide / Math.Max(wPx, hPx));
        wPx = Math.Max((int)(90 * dpi), (int)(wPx * f));
        hPx = Math.Max((int)(60 * dpi), (int)(wPx / pin._aspect));
        var cascade = (_created++ % 5) * 26 * dpi;
        var x = mon.Work.X + (mon.Work.Width - wPx) / 2 + cascade;
        var y = mon.Work.Y + (mon.Work.Height - hPx) / 2 + cascade;
        pin.Left = x / dpi;
        pin.Top = y / dpi;
        // window includes a 10 DIP margin for the shadow
        pin.Width = wPx / dpi + 20;
        pin.Height = hPx / dpi + 20;
        svc.ApplyAffinity(pin);
        pin.SourceInitialized += (_, _) =>
        {
            var src = HwndSource.FromHwnd(new WindowInteropHelper(pin).Handle);
            src?.AddHook(pin.WndProc);
        };
        Pins.Add(pin);
        pin.Show();
    }

    public static void CloseAll()
    {
        foreach (var p in Pins.ToList())
        {
            p.Close();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x84;
        const int WM_SIZING = 0x214;
        if (msg == WM_NCHITTEST && !_clickThrough)
        {
            var x = (short)(lParam.ToInt64() & 0xFFFF);
            var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            N.GetWindowRect(hwnd, out var r);
            var edge = (int)(8 * VisualTreeHelper.GetDpi(this).DpiScaleX) + 6;
            var left = x - r.Left < edge;
            var right = r.Right - x < edge;
            var top = y - r.Top < edge;
            var bottom = r.Bottom - y < edge;
            int? ht = (left, right, top, bottom) switch
            {
                (true, _, true, _) => 13,
                (_, true, true, _) => 14,
                (true, _, _, true) => 16,
                (_, true, _, true) => 17,
                (true, _, _, _) => 10,
                (_, true, _, _) => 11,
                (_, _, true, _) => 12,
                (_, _, _, true) => 15,
                _ => null,
            };
            if (ht is { } code)
            {
                handled = true;
                return code;
            }
        }
        else if (msg == WM_SIZING)
        {
            var rect = Marshal.PtrToStructure<N.RECT>(lParam);
            var edge = wParam.ToInt32();
            var chromeX = (int)(20 * VisualTreeHelper.GetDpi(this).DpiScaleX);
            var w = rect.Width - chromeX;
            var h = rect.Height - chromeX;
            if (edge is 1 or 2 or 7 or 8)
            {
                h = (int)Math.Round(w / _aspect);
            }
            else
            {
                w = (int)Math.Round(h * _aspect);
            }

            var nw = w + chromeX;
            var nh = h + chromeX;
            switch (edge)
            {
                case 1 or 7 or 4: rect.Left = rect.Right - nw; break;
                default: rect.Right = rect.Left + nw; break;
            }

            if (edge is 3 or 4 or 5)
            {
                rect.Top = rect.Bottom - nh;
            }
            else
            {
                rect.Bottom = rect.Top + nh;
            }

            Marshal.StructureToPtr(rect, lParam, true);
            handled = true;
            return 1;
        }

        return IntPtr.Zero;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) =>
        {
            if (Output.CopyImage(_image, _scale, _svc.App.Get<bool>(K.CopyImageOnly)))
            {
                QuickToolHud.Show(G.Camera, Strings.Screenshot.Copied);
            }
        };
        var save = new MenuItem { Header = Strings.Screenshot.SaveAs };
        save.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG image|*.png", FileName = Output.DefaultName(Strings.Screenshot.FileNamePrefix, DateTime.Now) + ".png" };
            if (dlg.ShowDialog(this) == true)
            {
                Output.SavePng(_image, dlg.FileName, _scale);
            }
        };
        menu.Items.Add(copy);
        menu.Items.Add(save);
        menu.Items.Add(new Separator());
        var opacity = new MenuItem { Header = "Opacity" };
        foreach (var pct in new[] { 100, 85, 70, 50 })
        {
            var p = pct;
            var item = new MenuItem { Header = pct + "%", IsCheckable = false };
            item.Click += (_, _) =>
            {
                Opacity = p / 100.0;
                foreach (MenuItem sibling in opacity.Items)
                {
                    sibling.IsChecked = (string)sibling.Header == p + "%";
                }
            };
            item.IsChecked = pct == 100;
            opacity.Items.Add(item);
        }

        menu.Items.Add(opacity);
        var click = new MenuItem { Header = "Ignore clicks", IsCheckable = true };
        click.Click += (_, _) => SetClickThrough(click.IsChecked);
        menu.Items.Add(click);
        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = Strings.Close };
        close.Click += (_, _) => Close();
        var all = new MenuItem { Header = "Close all pins" };
        all.Click += (_, _) => CloseAll();
        menu.Items.Add(close);
        menu.Items.Add(all);
        return menu;
    }

    private void SetClickThrough(bool on)
    {
        _clickThrough = on;
        var h = new WindowInteropHelper(this).Handle;
        var style = N.GetWindowLongPtr(h, N.GWL_EXSTYLE).ToInt64();
        style = on ? style | N.WS_EX_TRANSPARENT | N.WS_EX_LAYERED : style & ~N.WS_EX_TRANSPARENT;
        N.SetWindowLongPtr(h, N.GWL_EXSTYLE, (nint)style);
        UpdateHook();
    }

    private static void UpdateHook()
    {
        var need = Pins.Any(p => p._clickThrough);
        if (need && _hook is null && CaptureHost.IsAttached)
        {
            _hook = CaptureHost.Services.Hooks.SubscribeMouse(e =>
            {
                if (e.Kind == MouseEventKind.LeftDown && e.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    foreach (var p in Pins.Where(p => p._clickThrough).ToList())
                    {
                        N.GetWindowRect(new WindowInteropHelper(p).Handle, out var r);
                        if (e.X >= r.Left && e.X < r.Right && e.Y >= r.Top && e.Y < r.Bottom)
                        {
                            p.Dispatcher.BeginInvoke(() => p.SetClickThrough(false));
                        }
                    }
                }

                return false;
            });
        }
        else if (!need && _hook is not null)
        {
            _hook.Dispose();
            _hook = null;
        }
    }

    private void OnPinKey(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 12 : 1;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.W when ctrl:
                Close();
                break;
            case Key.C when ctrl:
                if (Output.CopyImage(_image, _scale, _svc.App.Get<bool>(K.CopyImageOnly)))
                {
                    QuickToolHud.Show(G.Camera, Strings.Screenshot.Copied);
                }

                break;
            case Key.Left: Left -= step / dpi; break;
            case Key.Right: Left += step / dpi; break;
            case Key.Up: Top -= step / dpi; break;
            case Key.Down: Top += step / dpi; break;
            default: return;
        }

        e.Handled = true;
    }
}
