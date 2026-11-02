// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace WinKnife.Features.Files;

/// <summary>
/// Clears the system clipboard on a delay, on computer sleep, on display sleep and on screen lock. Independent of the history
/// (emptying the clipboard is a security setting in its own right); the saved history is never touched.
/// </summary>
internal sealed class ClipboardAutoClear : IDisposable
{
    private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");
    private const int PbtPowerSettingChange = 0x8013;
    private const uint DeviceNotifyWindowHandle = 0;
    private const int WtsSessionLock = 0x7;

    private readonly IFeatureContext _context;
    private readonly Action _cleared;
    private readonly DispatcherTimer _timer;
    private HwndSource? _source;
    private nint _powerNotification;
    private uint _lastSequence;
    private DateTime _lastChange = DateTime.UtcNow;
    private uint _ownClearSequence;
    private bool _disposed;

    public ClipboardAutoClear(IFeatureContext context, Action cleared)
    {
        _context = context;
        _cleared = cleared;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick, context.Ui);
        _timer.Stop();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        try
        {
            _source = new HwndSource(new HwndSourceParameters("WinKnife.ClipboardAutoClear") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
            _source.AddHook(WndProc);
            var guid = ConsoleDisplayState;
            _powerNotification = WinApi.RegisterPowerSettingNotification(_source.Handle, ref guid, DeviceNotifyWindowHandle);
        }
        catch (Exception ex)
        {
            context.Log.Warn($"Display-sleep notifications unavailable: {ex.Message}");
        }

        Sync();
    }

    /// <summary>Starts or stops the delay timer after a setting changed; turning it on baselines "now".</summary>
    public void Sync()
    {
        var wanted = _context.App.Get<bool>("clipboardAutoClearOnDelay");
        if (wanted && !_timer.IsEnabled)
        {
            _lastSequence = WinApi.GetClipboardSequenceNumber();
            _lastChange = DateTime.UtcNow;
            _timer.Start();
        }
        else if (!wanted && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (_powerNotification != 0)
        {
            WinApi.UnregisterPowerSettingNotification(_powerNotification);
            _powerNotification = 0;
        }

        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_context.App.Get<bool>("clipboardAutoClearOnDelay"))
        {
            _timer.Stop();
            return;
        }

        var delay = Math.Clamp(_context.App.Get<int>("clipboardAutoClearDelaySeconds"), 5, 3600);
        var sequence = WinApi.GetClipboardSequenceNumber();
        if (sequence != _lastSequence && sequence != _ownClearSequence)
        {
            _lastSequence = sequence;
            _lastChange = DateTime.UtcNow;
            return;
        }

        if (sequence == _ownClearSequence)
        {
            return;
        }

        if ((DateTime.UtcNow - _lastChange).TotalSeconds >= delay)
        {
            ClearNow();
        }
    }

    private void ClearNow()
    {
        if (WinApi.GetClipboardSequenceNumber() == 0 || !ClipboardIO.Formats().Any())
        {
            return;
        }

        _ownClearSequence = ClipboardIO.Clear();
        _lastSequence = _ownClearSequence;
        _cleared();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend && _context.App.Get<bool>("clipboardAutoClearOnSleep"))
        {
            ClearNow();
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock && _context.App.Get<bool>("clipboardAutoClearOnScreenLock"))
        {
            _context.Ui.BeginInvoke(ClearNow);
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg == WinApi.WM_POWERBROADCAST && wParam == PbtPowerSettingChange && lParam != 0)
        {
            try
            {
                var guid = Marshal.PtrToStructure<Guid>(lParam);
                if (guid == ConsoleDisplayState)
                {
                    var data = Marshal.ReadByte(lParam, 16 + 4);
                    if (data == 0 && _context.App.Get<bool>("clipboardAutoClearOnDisplaySleep"))
                    {
                        ClearNow();
                    }
                }
            }
            catch (Exception)
            {
                // malformed notification
            }
        }

        return 0;
    }
}

/// <summary>
/// "Show latest copy in the notification area": a small topmost pill anchored just left of the tray icons showing a shortened
/// preview of the latest copy. A click opens the history.
/// </summary>
internal sealed class ClipboardTrayPill : IDisposable
{
    private readonly IClipboardHost _host;
    private readonly DispatcherTimer _follow;
    private PillWindow? _window;

    public ClipboardTrayPill(IClipboardHost host)
    {
        _host = host;
        _follow = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Reposition(), host.Services.Ui);
        _follow.Stop();
    }

    public void Dispose()
    {
        _follow.Stop();
        var window = _window;
        _window = null;
        window?.Close();
    }

    /// <summary>Re-evaluates visibility and text (latest copy, setting, length).</summary>
    public void Refresh()
    {
        var app = _host.Services.App;
        var wanted = _host.HistoryEnabled && app.Get<bool>("clipboardHistoryMenuBarPreview");
        var latest = wanted && _host.Store.LatestPasteboardEntryId is { } id ? _host.Store.Find(id) : null;
        if (latest is null)
        {
            _follow.Stop();
            _window?.Hide();
            return;
        }

        var length = Math.Clamp(app.Get<int>("clipboardHistoryMenuBarPreviewLength"), 5, 50);
        _window ??= new PillWindow(() => _host.ShowWindow());
        _window.SetText(latest.TrayText(length), latest.Preview().Length > 200 ? latest.Preview()[..200] + "…" : latest.Preview());
        _window.EnsureShown();
        Reposition();
        _follow.Start();
    }

    private void Reposition()
    {
        if (_window is not { IsVisible: true })
        {
            return;
        }

        try
        {
            var tray = WinApi.FindWindowW("Shell_TrayWnd", null);
            if (tray == 0 || !NativeMethods.GetWindowRect(tray, out var trayRect))
            {
                return;
            }

            var notify = WinApi.FindWindowExW(tray, 0, "TrayNotifyWnd", null);
            RECT notifyRect = trayRect;
            if (notify != 0)
            {
                NativeMethods.GetWindowRect(notify, out notifyRect);
            }

            _window.PlaceNear(trayRect, notifyRect);
        }
        catch (Exception)
        {
            // taskbar not available (explorer restarting)
        }
    }

    private sealed class PillWindow : Window
    {
        private readonly TextBlock _label;
        private readonly Border _frame;

        public PillWindow(Action onClick)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            _label = UiKit.Text(string.Empty, 11.5, FontWeights.Medium, ThemeKeys.TextPrimary);
            _label.VerticalAlignment = VerticalAlignment.Center;
            var icon = UiKit.Icon(G.Clipboard, 11, ThemeKeys.TextSecondary);
            icon.Margin = new Thickness(0, 0, 6, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(icon);
            row.Children.Add(_label);
            _frame = new Border
            {
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(9, 2, 10, 2),
                Height = 24,
                BorderThickness = new Thickness(1),
                Child = row,
                Cursor = Cursors.Hand,
            };
            _frame.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
            _frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
            Content = _frame;
            _frame.MouseLeftButtonUp += (_, _) => onClick();
        }

        public void SetText(string text, string tooltip)
        {
            _label.Text = text;
            _frame.ToolTip = tooltip;
        }

        public void EnsureShown()
        {
            if (!IsVisible)
            {
                ShowActivated = false;
                Show();
                WinUtil.MakeToolWindow(this, noActivate: true);
            }
        }

        /// <summary>Places the pill left of the tray icons (horizontal taskbar) or above them (vertical taskbar).</summary>
        public void PlaceNear(RECT taskbar, RECT notify)
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            var scale = WinUtil.PrepareOnMonitorAt(this, taskbar.Left + 1, taskbar.Top + 1);
            var size = WinUtil.DesiredSize(this);
            var width = (int)Math.Ceiling(size.Width * scale);
            var height = (int)Math.Ceiling(size.Height * scale);
            var gap = (int)(8 * scale);
            int x;
            int y;
            if (taskbar.Width >= taskbar.Height)
            {
                x = notify.Left - width - gap;
                y = taskbar.Top + (taskbar.Height - height) / 2;
            }
            else
            {
                x = taskbar.Left + (taskbar.Width - width) / 2;
                y = notify.Top - height - gap;
            }

            NativeMethods.SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
