// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Strings of Cleaning Mode (Vorssaint <c>Localization</c>, enUS).</summary>
internal static class CleaningStrings
{
    public const string Title = "Cleaning Mode";
    public const string PanelCaption = "Locks the keyboard so you can clean safely.";
    public const string OverlayTitle = "Keyboard locked for cleaning";
    public const string OverlaySubtitle = "Press Escape 5 times to unlock";
    public const string Unlock = "Unlock";
    public const string MouseHint = "Your mouse and trackpad still work";
    public const string KeepVisible = "Keep screen visible";
    public const string KeepVisibleCaption = "Shows a discreet indicator in the corner of the screen instead of blacking out content.";
    public const string StartNow = "Lock keyboard now";
}

/// <summary>The visual content of the cleaning overlays (no hooks: usable from tests).</summary>
internal static class CleaningViews
{
    /// <summary>Full blackout content; <paramref name="setProgress"/> updates the five dots.</summary>
    public static FrameworkElement Blackout(Action unlock, out Action<int> setProgress)
    {
        var dots = new List<Ellipse>();
        var dotRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < CleaningUnlockCounter.Threshold; i++)
        {
            var dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)), Margin = new Thickness(i == 0 ? 0 : 11, 0, 0, 0) };
            dots.Add(dot);
            dotRow.Children.Add(dot);
        }

        setProgress = progress =>
        {
            for (var i = 0; i < dots.Count; i++)
            {
                dots[i].Fill = new SolidColorBrush(i < progress ? Colors.White : Color.FromArgb(56, 255, 255, 255));
            }
        };

        var stack = new StackPanel { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        void Add(UIElement element, double top)
        {
            if (element is FrameworkElement fe)
            {
                fe.Margin = new Thickness(fe.Margin.Left, top, fe.Margin.Right, 0);
            }

            stack.Children.Add(element);
        }

        Add(new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = 48, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center }, 0);
        Add(new TextBlock { Text = CleaningStrings.OverlayTitle, FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }, 18);
        Add(new TextBlock { Text = CleaningStrings.OverlaySubtitle, FontSize = 15, Foreground = new SolidColorBrush(Color.FromArgb(191, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center }, 18);
        Add(dotRow, 20);

        var button = new Button
        {
            Content = new TextBlock { Text = CleaningStrings.Unlock, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center },
            MinWidth = 130,
            Padding = new Thickness(18, 10, 18, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Template = CapsuleTemplate(Brushes.White),
        };
        button.Click += (_, _) => unlock();
        Add(button, 24);
        Add(new TextBlock { Text = CleaningStrings.MouseHint, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center }, 18);

        var root = new Grid { Background = Brushes.Black };
        stack.Margin = new Thickness(44);
        root.Children.Add(stack);
        return root;
    }

    /// <summary>The compact top-right HUD card.</summary>
    public static FrameworkElement Compact(Action unlock, out Action<int> setProgress)
    {
        var dots = new List<Ellipse>();
        var dotRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        for (var i = 0; i < CleaningUnlockCounter.Threshold; i++)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0) };
            dot.SetResourceReference(Shape.FillProperty, ThemeKeys.Track);
            dots.Add(dot);
            dotRow.Children.Add(dot);
        }

        setProgress = progress =>
        {
            for (var i = 0; i < dots.Count; i++)
            {
                dots[i].SetResourceReference(Shape.FillProperty, i < progress ? ThemeKeys.Accent : ThemeKeys.Track);
            }
        };

        var circle = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = UiKit.Icon("", 16, ThemeKeys.TextSecondary) };
        circle.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var subtitle = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        subtitle.Children.Add(UiKit.Text(CleaningStrings.OverlaySubtitle, 11, FontWeights.Normal, ThemeKeys.TextSecondary));
        subtitle.Children.Add(dotRow);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        texts.Children.Add(UiKit.Text(CleaningStrings.OverlayTitle, 13, FontWeights.SemiBold));
        texts.Children.Add(subtitle);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(circle);
        row.Children.Add(texts);
        var button = UiKit.Button(CleaningStrings.Unlock, ButtonKind.Prominent, 11, onClick: unlock);
        button.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(button);
        var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 10, 14, 10), BorderThickness = new Thickness(1), Child = row, Margin = new Thickness(16) };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
        card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 6, Direction = 270, Opacity = 0.22 };
        return card;
    }

    private static ControlTemplate CapsuleTemplate(Brush fill)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, fill);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(100));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }
}

/// <summary>A screen-sized (or HUD-sized) topmost overlay hosting cleaning view content. Installs no hook.</summary>
internal sealed class CleaningOverlayWindow : OverlayWindow
{
    private readonly Action<int> _setProgress;

    public CleaningOverlayWindow(Rect physicalBounds, bool compact, Action unlock)
    {
        PhysicalBounds = physicalBounds;
        Compact = compact;
        Title = CleaningStrings.Title;
        Content = compact ? CleaningViews.Compact(unlock, out _setProgress) : CleaningViews.Blackout(unlock, out _setProgress);
        if (!compact)
        {
            Background = Brushes.Black;
        }
    }

    public Rect PhysicalBounds { get; }

    public bool Compact { get; }

    public void SetProgress(int progress) => _setProgress(progress);

    /// <summary>Shows the window on its monitor (full frame, or a corner HUD).</summary>
    public void ShowOnMonitor()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        var b = PhysicalBounds;
        NativeMethods.SetWindowPos(handle, NativeConstants.HWND_TOPMOST, (int)b.Left, (int)b.Top, 0, 0, NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOACTIVATE);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (Compact)
        {
            SizeToContent = SizeToContent.WidthAndHeight;
            Show();
            UpdateLayout();
            Left = (b.Right - ActualWidth * dpi - 8 * dpi) / dpi;
            Top = (b.Top + 24 * dpi) / dpi;
        }
        else
        {
            Left = b.Left / dpi;
            Top = b.Top / dpi;
            Width = b.Width / dpi;
            Height = b.Height / dpi;
            Show();
            NativeMethods.SetWindowPos(Handle, NativeConstants.HWND_TOPMOST, (int)b.Left, (int)b.Top, (int)b.Width, (int)b.Height, NativeConstants.SWP_NOACTIVATE);
        }
    }

    public void ReassertTopmost() =>
        NativeMethods.SetWindowPos(Handle, NativeConstants.HWND_TOPMOST, 0, 0, 0, 0, NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOACTIVATE);
}

/// <summary>Cleaning Mode: keyboard (and scroll wheel) lock with five-Escape unlock; mouse buttons and movement keep working.</summary>
public sealed class CleaningModeFeature : ToolsFeatureBase, ICleaningModeService
{
    private readonly CleaningUnlockCounter _counter = new();
    private readonly CleaningMouseReleaseGate _gate = new();
    private readonly bool[] _down = new bool[256];
    private readonly object _hookGate = new();
    private readonly List<CleaningOverlayWindow> _overlays = [];
    private IDisposable? _keyboard;
    private IDisposable? _mouse;
    private DispatcherTimer? _topmost;
    private DispatcherTimer? _releaseDeadline;
    private bool _active;

    public override string Id => "cleaning-mode";

    public override IReadOnlyList<FeatureAction> Actions =>
        [new FeatureAction("activate", CleaningStrings.Title, () => Safe(Begin), "")];

    /// <inheritdoc />
    void ICleaningModeService.Start() => Begin();

    protected override void RegisterDefaults(IAppSettings app) => app.Register("cleaningModeKeepScreenVisible", false);

    protected override void OnStart()
    {
        PadServices.Ctx = Ctx;
        Ctx.Services.Provide<ICleaningModeService>(this);
    }

    protected override void OnStop()
    {
        Ctx.Services.Withdraw<ICleaningModeService>(this);
        Teardown();
    }

    private void Begin()
    {
        if (_active || !IsRunning)
        {
            return;
        }

        Array.Clear(_down);
        lock (_hookGate)
        {
            _counter.Reset();
            _gate.InvalidateTrackedPresses();
        }

        _keyboard = Ctx.Hooks.SubscribeKeyboard(OnKey, int.MaxValue);
        _mouse = Ctx.Hooks.SubscribeMouse(OnMouse, int.MaxValue);
        _active = true;
        BuildOverlays();
        _topmost = new DispatcherTimer(DispatcherPriority.Background, Ctx.Ui) { Interval = TimeSpan.FromMilliseconds(500) };
        _topmost.Tick += (_, _) =>
        {
            foreach (var o in _overlays)
            {
                o.ReassertTopmost();
            }
        };
        _topmost.Start();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionSwitch += OnSession;
    }

    private bool OnKey(KeyboardHookEvent e)
    {
        var vk = e.VkCode & 0xFF;
        var unlocked = false;
        var progress = -1;
        lock (_hookGate)
        {
            if (e.IsKeyDown)
            {
                var repeat = _down[vk];
                _down[vk] = true;
                var code = CleaningUnlockCounter.IsModifierOrMedia(vk) ? 10000 + vk : vk;
                unlocked = _counter.RegisterKeyDown(code, e.Timestamp, repeat);
                progress = _counter.Progress;
            }
            else
            {
                _down[vk] = false;
                if (CleaningUnlockCounter.IsModifierOrMedia(vk))
                {
                    _counter.RegisterOther();
                    progress = 0;
                }
            }
        }

        if (progress >= 0)
        {
            Ctx.Ui.BeginInvoke(() => SetProgress(progress));
        }

        if (unlocked)
        {
            Ctx.Ui.BeginInvoke(RequestDeactivation);
        }

        return true;
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsWheel)
        {
            return true;
        }

        var button = e.Kind switch
        {
            MouseEventKind.LeftDown or MouseEventKind.LeftUp => 1,
            MouseEventKind.RightDown or MouseEventKind.RightUp => 2,
            MouseEventKind.MiddleDown or MouseEventKind.MiddleUp => 3,
            MouseEventKind.XButtonDown or MouseEventKind.XButtonUp => 3 + e.XButton,
            _ => 0,
        };
        if (button == 0)
        {
            return false;
        }

        var finish = false;
        lock (_hookGate)
        {
            if (e.IsButtonDown)
            {
                _gate.ButtonDown(button);
            }
            else
            {
                finish = _gate.ButtonUp(button);
            }
        }

        if (finish)
        {
            Ctx.Ui.BeginInvoke(Teardown);
        }

        return false;
    }

    private void SetProgress(int progress)
    {
        foreach (var o in _overlays)
        {
            o.SetProgress(progress);
        }
    }

    private void RequestDeactivation()
    {
        if (!_active)
        {
            return;
        }

        bool proceed;
        lock (_hookGate)
        {
            proceed = _gate.RequestDeactivation();
        }

        if (proceed)
        {
            Teardown();
            return;
        }

        _releaseDeadline?.Stop();
        _releaseDeadline = new DispatcherTimer(DispatcherPriority.Normal, Ctx.Ui) { Interval = TimeSpan.FromMilliseconds(CleaningMouseReleaseGate.ReleaseWaitMs) };
        _releaseDeadline.Tick += (_, _) =>
        {
            _releaseDeadline?.Stop();
            lock (_hookGate)
            {
                _gate.ReleaseWaitExpired();
            }

            Teardown();
        };
        _releaseDeadline.Start();
    }

    private void BuildOverlays()
    {
        var compact = App.Get<bool>("cleaningModeKeepScreenVisible");
        var wanted = System.Windows.Forms.Screen.AllScreens.Select(s => new Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToList();
        foreach (var gone in _overlays.Where(o => o.Compact != compact || !wanted.Contains(o.PhysicalBounds)).ToList())
        {
            gone.Close();
            _overlays.Remove(gone);
        }

        foreach (var bounds in wanted.Where(w => _overlays.All(o => o.PhysicalBounds != w)))
        {
            var overlay = new CleaningOverlayWindow(bounds, compact, RequestDeactivation);
            _overlays.Add(overlay);
            overlay.ShowOnMonitor();
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Ctx.Ui.BeginInvoke(() =>
    {
        if (_active)
        {
            BuildOverlays();
        }
    });

    private void OnSession(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            Ctx.Ui.BeginInvoke(Teardown);
        }
    }

    private void Teardown()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.SessionSwitch -= OnSession;
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _keyboard = _mouse = null;
        _topmost?.Stop();
        _releaseDeadline?.Stop();
        _topmost = _releaseDeadline = null;
        foreach (var o in _overlays)
        {
            o.Close();
        }

        _overlays.Clear();
        lock (_hookGate)
        {
            _counter.Reset();
            _gate.InvalidateTrackedPresses();
        }
    }
}

/// <summary>Quick tools page, "Cleaning Mode" card.</summary>
public sealed class CleaningModeSettingsSection : ISettingsSection
{
    public string? FeatureId => "cleaning-mode";

    public string PageId => SettingsPages.QuickTools;

    public string? Anchor => "cleaningMode";

    public string? Title => CleaningStrings.Title;

    public IReadOnlyList<string> Keywords => ["lock keyboard", "clean", "keep screen visible"];

    public int Order => 80;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        c.App.Register("cleaningModeKeepScreenVisible", false);
        var card = new SettingsCard(CleaningStrings.Title);
        var start = UiKit.Button(CleaningStrings.StartNow, ButtonKind.Bordered, 13, "", () => c.Services.Get<ICleaningModeService>()?.Start());
        start.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(start);
        card.AddCaption(CleaningStrings.PanelCaption);
        card.Add(new SettingsRow(null, CleaningStrings.KeepVisible, null,
            new MacSwitch(c.App.Get<bool>("cleaningModeKeepScreenVisible"), SwitchSize.Regular, on => c.App.Set("cleaningModeKeepScreenVisible", on))));
        card.AddCaption(CleaningStrings.KeepVisibleCaption);
        return card;
    }
}

/// <summary>Utilities tab row.</summary>
public sealed class CleaningModeUtility : IPanelUtility
{
    public string? FeatureId => "cleaning-mode";

    public string Id => "cleaning";

    public string Title => CleaningStrings.Title;

    public string Glyph => "";

    public string? Caption => CleaningStrings.PanelCaption;

    public int Order => 110;

    public void Invoke(IPanelContext context) => context.Services.Get<ICleaningModeService>()?.Start();
}
