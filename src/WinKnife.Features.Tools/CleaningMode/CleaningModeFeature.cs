// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.Tools;

/// <summary>Blacks out every screen and swallows all keyboard and mouse input so you can wipe the hardware clean.</summary>
public sealed class CleaningModeFeature : ToolFeatureBase
{
    private const int VkEscape = 0x1B;
    private const int HoldMilliseconds = 2000;

    private readonly List<CleaningOverlay> _overlays = new();
    private readonly bool[] _keysSeenDown = new bool[256];
    private IDisposable? _keyboard;
    private IDisposable? _mouse;
    private DispatcherTimer? _timer;
    private long _escDownAt;
    private volatile bool _active;
    private DateTime _startedAt;

    public override string Id => "cleaning-mode";

    public override string Name => "Cleaning mode";

    public override string Description => "Turn the screen black and lock the keyboard and mouse so you can clean them; hold Esc to leave.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("start", "Start cleaning mode", () => Safe(Begin), ""),
    ];

    protected override void OnStart() => BindHotkey("hotkey", null, Begin);

    protected override void OnStop() => End();

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var start = UiKit.Btn("Start cleaning mode", () => Safe(Begin), accent: true);
        start.HorizontalAlignment = HorizontalAlignment.Left;
        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Start cleaning mode", null, "Optional global shortcut.")
            .AddChoice(
                "maxMinutes",
                "Leave automatically after",
                new Choice<int>[] { new(0, "Never"), new(2, "2 minutes"), new(5, "5 minutes"), new(10, "10 minutes"), new(30, "30 minutes") },
                5,
                "A safety net in case you forget how to leave.")
            .AddNote("To leave, press and hold Esc for 2 seconds. Ctrl+Alt+Del always works and cannot be blocked.");
        return UiKit.WithExtras(builder.Build(), start);
    }

    private void Begin()
    {
        if (_active)
        {
            return;
        }

        Array.Clear(_keysSeenDown);
        Interlocked.Exchange(ref _escDownAt, 0);
        _startedAt = DateTime.UtcNow;

        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var overlay = new CleaningOverlay(screen.Primary, HoldMilliseconds);
            overlay.EscapeChanged += OnWindowEscape;
            _overlays.Add(overlay);
            overlay.ShowAt(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        }

        _active = true;
        _keyboard = Ctx.Hooks.SubscribeKeyboard(OnKeyboard, int.MaxValue);
        _mouse = Ctx.Hooks.SubscribeMouse(_ => true, int.MaxValue);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Normal, OnTick, Ctx.Ui);
        _timer.Start();
        _overlays.FirstOrDefault(o => o.IsPrimary)?.Activate();
        Ctx.Log.Info("Cleaning mode started");
    }

    private void End()
    {
        var wasActive = _active;
        _active = false;
        _timer?.Stop();
        _timer = null;
        _keyboard?.Dispose();
        _keyboard = null;
        _mouse?.Dispose();
        _mouse = null;
        foreach (var overlay in _overlays)
        {
            overlay.Dispose();
        }

        _overlays.Clear();
        if (wasActive)
        {
            Ctx.Log.Info("Cleaning mode ended");
        }
    }

    // Runs on the hook thread: keep it tiny.
    private bool OnKeyboard(KeyboardHookEvent e)
    {
        if (!_active)
        {
            return false;
        }

        var vk = e.VkCode & 0xFF;
        if (e.IsKeyDown)
        {
            _keysSeenDown[vk] = true;
            if (vk == VkEscape && Interlocked.Read(ref _escDownAt) == 0)
            {
                Interlocked.Exchange(ref _escDownAt, Environment.TickCount64);
            }

            return true;
        }

        if (vk == VkEscape)
        {
            Interlocked.Exchange(ref _escDownAt, 0);
        }

        // Key releases whose press happened before cleaning mode started go through, so no key stays "stuck".
        if (_keysSeenDown[vk])
        {
            _keysSeenDown[vk] = false;
            return true;
        }

        return false;
    }

    // Fallback in case the low-level hook was removed by Windows: the overlay itself also watches Esc.
    private void OnWindowEscape(bool down)
    {
        if (down)
        {
            if (Interlocked.Read(ref _escDownAt) == 0)
            {
                Interlocked.Exchange(ref _escDownAt, Environment.TickCount64);
            }
        }
        else
        {
            Interlocked.Exchange(ref _escDownAt, 0);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var started = Interlocked.Read(ref _escDownAt);
        var progress = 0.0;
        if (started != 0)
        {
            var held = Environment.TickCount64 - started;
            progress = Math.Clamp(held / (double)HoldMilliseconds, 0, 1);
            if (held >= HoldMilliseconds)
            {
                End();
                return;
            }
        }

        var max = Ctx.Settings.Get("maxMinutes", 5);
        if (max > 0 && DateTime.UtcNow - _startedAt > TimeSpan.FromMinutes(max))
        {
            End();
            Ctx.Notifier.Show(Name, "Cleaning mode ended automatically.");
            return;
        }

        foreach (var overlay in _overlays)
        {
            overlay.SetProgress(progress);
        }
    }
}

internal sealed class CleaningOverlay : Window, IDisposable
{
    private readonly TextBlock _hint = new();
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Width = 260, Height = 6 };
    private readonly int _holdMs;
    private bool _disposed;

    public CleaningOverlay(bool primary, int holdMs)
    {
        IsPrimary = primary;
        _holdMs = holdMs;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Cleaning mode";
        Left = 0;
        Top = 0;
        Width = 200;
        Height = 200;

        if (primary)
        {
            _hint.Text = "Cleaning mode\nHold Esc for 2 seconds to leave";
            _hint.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5B));
            _hint.FontSize = 18;
            _hint.TextAlignment = TextAlignment.Center;
            _bar.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA6));
            _bar.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x26));
            _bar.BorderThickness = new Thickness(0);
            _bar.Margin = new Thickness(0, 16, 0, 0);
            Content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { _hint, _bar },
            };
        }

        PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            if ((e.Key == Key.System ? e.SystemKey : e.Key) == Key.Escape)
            {
                EscapeChanged?.Invoke(true);
            }
        };
        PreviewKeyUp += (_, e) =>
        {
            e.Handled = true;
            if ((e.Key == Key.System ? e.SystemKey : e.Key) == Key.Escape)
            {
                EscapeChanged?.Invoke(false);
            }
        };
        Closing += (_, e) =>
        {
            if (!_disposed)
            {
                e.Cancel = true;
            }
        };
    }

    public bool IsPrimary { get; }

    public event Action<bool>? EscapeChanged;

    public void ShowAt(int x, int y, int width, int height)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.SetWindowPos(handle, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);
        Show();
        NativeMethods.SetWindowPos(handle, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);
    }

    public void SetProgress(double progress)
    {
        if (!IsPrimary)
        {
            return;
        }

        _bar.Value = progress;
        _hint.Text = progress <= 0
            ? "Cleaning mode\nHold Esc for 2 seconds to leave"
            : $"Keep holding Esc... {Math.Max(0, (_holdMs * (1 - progress)) / 1000.0):0.0}s";
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }
}
