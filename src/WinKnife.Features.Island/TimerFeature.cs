// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>Timers with presets and a custom length, a stopwatch and a focus (pomodoro) session inside the island.</summary>
public sealed class IslandTimerFeature : IFeature
{
    private IFeatureContext? _context;
    private TimerEngine? _engine;

    public string Id => "island-timer";

    public string Name => "Island timer";

    public string Description => "Run timers, a stopwatch and focus sessions from the island, with the remaining time shown in the collapsed pill.";

    public FeatureGroup Group => FeatureGroup.Island;

    public IReadOnlyList<FeatureAction> Actions
    {
        get
        {
            if (_engine is null || !IslandHub.IsRunning)
            {
                return [];
            }

            var engine = _engine;
            return
            [
                new FeatureAction("open", "Open the island timer", () => IslandHub.Controller?.Expand("timer"), ""),
                new FeatureAction("focus", engine.Focus.Running ? "Pause focus session" : "Start focus session", engine.ToggleFocus, ""),
                new FeatureAction("stopwatch", engine.Stopwatch.Running ? "Pause stopwatch" : "Start stopwatch", engine.ToggleStopwatch, ""),
            ];
        }
    }

    public void Start(IFeatureContext context)
    {
        _context = context;
        _engine = new TimerEngine(context);
        var view = new TimerView(_engine);
        IslandHub.RegisterSection(new IslandSection
        {
            Id = "timer",
            Title = "Timer",
            Glyph = "",
            Order = 10,
            Create = view.Build,
            VisibilityChanged = view.SetVisible,
        });
    }

    public void Stop()
    {
        IslandHub.UnregisterSection("timer");
        IslandHub.SetCompact("timer", null);
        _engine?.Dispose();
        _engine = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        return new SettingsViewBuilder(_context.Settings)
            .AddChoice("focusMinutes", "Focus length", Minutes(15, 20, 25, 30, 45, 50, 60, 90), 25)
            .AddChoice("shortBreakMinutes", "Short break", Minutes(3, 5, 10), 5)
            .AddChoice("longBreakMinutes", "Long break", Minutes(10, 15, 20, 30), 15)
            .AddChoice("sessionsBeforeLong", "Focus sessions before a long break", new Choice<int>[] { new(2, "2"), new(3, "3"), new(4, "4"), new(6, "6") }, 4)
            .AddToggle("autoAdvance", "Start the next focus or break automatically", true)
            .AddToggle("playSound", "Play a sound when a timer finishes", true)
            .Build();
    }

    private static Choice<int>[] Minutes(params int[] values) =>
        values.Select(v => new Choice<int>(v, v + (v == 1 ? " minute" : " minutes"))).ToArray();
}

/// <summary>A stopwatch-like clock that can be started, paused and reset.</summary>
internal sealed class Clock
{
    private TimeSpan _accumulated;
    private long _start;

    public bool Running { get; private set; }

    /// <summary>Length for countdowns (zero for the stopwatch).</summary>
    public TimeSpan Total { get; set; }

    public bool IsIdle => !Running && _accumulated == TimeSpan.Zero;

    public TimeSpan Elapsed => _accumulated + (Running ? Stopwatch.GetElapsedTime(_start) : TimeSpan.Zero);

    public TimeSpan Remaining => Total > Elapsed ? Total - Elapsed : TimeSpan.Zero;

    public void Start()
    {
        if (!Running)
        {
            _start = Stopwatch.GetTimestamp();
            Running = true;
        }
    }

    public void Pause()
    {
        if (Running)
        {
            _accumulated += Stopwatch.GetElapsedTime(_start);
            Running = false;
        }
    }

    public void Reset()
    {
        Running = false;
        _accumulated = TimeSpan.Zero;
    }
}

internal enum FocusPhase
{
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>State and ticking of the three timer modes. Only ticks while a clock is running.</summary>
internal sealed class TimerEngine : IDisposable
{
    private readonly IFeatureContext _context;
    private readonly DispatcherTimer _tick;
    private string _lastCompact = string.Empty;

    public TimerEngine(IFeatureContext context)
    {
        _context = context;
        Countdown.Total = TimeSpan.FromMinutes(5);
        Focus.Total = FocusLength;
        _tick = new DispatcherTimer(DispatcherPriority.Normal, context.Ui) { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => OnTick();
    }

    public Clock Countdown { get; } = new();

    public Clock Stopwatch { get; } = new();

    public Clock Focus { get; } = new();

    public List<TimeSpan> Laps { get; } = [];

    public FocusPhase Phase { get; private set; } = FocusPhase.Focus;

    /// <summary>Completed focus sessions in the current cycle.</summary>
    public int Completed { get; private set; }

    public int SessionsBeforeLong => Math.Max(1, _context.Settings.Get("sessionsBeforeLong", 4));

    /// <summary>Raised on every tick while running and after every state change.</summary>
    public event Action? Updated;

    private TimeSpan FocusLength => TimeSpan.FromMinutes(Math.Max(1, _context.Settings.Get("focusMinutes", 25)));

    private TimeSpan PhaseLength(FocusPhase phase) => phase switch
    {
        FocusPhase.Focus => FocusLength,
        FocusPhase.ShortBreak => TimeSpan.FromMinutes(Math.Max(1, _context.Settings.Get("shortBreakMinutes", 5))),
        _ => TimeSpan.FromMinutes(Math.Max(1, _context.Settings.Get("longBreakMinutes", 15))),
    };

    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        var seconds = (int)Math.Ceiling(time.TotalSeconds - 0.0001);
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60:00}:{seconds % 60:00}";
    }

    public static string FormatStopwatch(TimeSpan time)
    {
        var tenths = (int)(time.TotalMilliseconds / 100) % 10;
        var total = (int)time.TotalSeconds;
        return total >= 3600
            ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}.{tenths}"
            : $"{total / 60:00}:{total % 60:00}.{tenths}";
    }

    public string PhaseTitle => Phase switch
    {
        FocusPhase.Focus => "Focus",
        FocusPhase.ShortBreak => "Short break",
        _ => "Long break",
    };

    // ---- countdown

    public void ToggleCountdown()
    {
        if (Countdown.Running)
        {
            Countdown.Pause();
        }
        else if (Countdown.Total > TimeSpan.Zero)
        {
            Countdown.Start();
        }

        Changed();
    }

    public void ResetCountdown()
    {
        Countdown.Reset();
        Changed();
    }

    public void AdjustCountdown(TimeSpan delta)
    {
        var next = Countdown.Total + delta;
        Countdown.Total = next < TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : next > TimeSpan.FromHours(99) ? TimeSpan.FromHours(99) : next;
        Changed();
    }

    public void SetCountdown(TimeSpan total)
    {
        Countdown.Total = total;
        Changed();
    }

    // ---- stopwatch

    public void ToggleStopwatch()
    {
        if (Stopwatch.Running)
        {
            Stopwatch.Pause();
        }
        else
        {
            Stopwatch.Start();
        }

        Changed();
    }

    public void Lap()
    {
        if (Stopwatch.Running)
        {
            Laps.Insert(0, Stopwatch.Elapsed);
            Changed();
        }
    }

    public void ResetStopwatch()
    {
        Stopwatch.Reset();
        Laps.Clear();
        Changed();
    }

    // ---- focus

    public void ToggleFocus()
    {
        if (Focus.Running)
        {
            Focus.Pause();
        }
        else
        {
            if (Focus.IsIdle)
            {
                Focus.Total = PhaseLength(Phase);
            }

            Focus.Start();
        }

        Changed();
    }

    public void ResetFocus()
    {
        Focus.Reset();
        Phase = FocusPhase.Focus;
        Completed = 0;
        Focus.Total = FocusLength;
        Changed();
    }

    public void SkipFocusPhase()
    {
        AdvancePhase(start: Focus.Running);
        Changed();
    }

    private void AdvancePhase(bool start)
    {
        var wasFocus = Phase == FocusPhase.Focus;
        Focus.Reset();
        if (wasFocus)
        {
            Completed++;
            Phase = Completed >= SessionsBeforeLong ? FocusPhase.LongBreak : FocusPhase.ShortBreak;
        }
        else
        {
            if (Phase == FocusPhase.LongBreak)
            {
                Completed = 0;
            }

            Phase = FocusPhase.Focus;
        }

        Focus.Total = PhaseLength(Phase);
        if (start)
        {
            Focus.Start();
        }
    }

    // ---- ticking

    private void OnTick()
    {
        if (Countdown.Running && Countdown.Remaining <= TimeSpan.Zero)
        {
            var length = Countdown.Total;
            Countdown.Reset();
            Announce("", "Timer finished", $"Your {Describe(length)} timer is up.", IslandTheme.Orange);
        }

        if (Focus.Running && Focus.Remaining <= TimeSpan.Zero)
        {
            var finished = Phase;
            AdvancePhase(start: _context.Settings.Get("autoAdvance", true));
            var message = finished == FocusPhase.Focus
                ? $"Focus session done. Time for a {(Phase == FocusPhase.LongBreak ? "long" : "short")} break."
                : "Break is over. Back to focus.";
            Announce("", finished == FocusPhase.Focus ? "Focus complete" : "Break over", message, finished == FocusPhase.Focus ? IslandTheme.Green : IslandTheme.Blue);
        }

        PublishCompact();
        if (!Countdown.Running && !Stopwatch.Running && !Focus.Running)
        {
            _tick.Stop();
        }

        Updated?.Invoke();
    }

    private static string Describe(TimeSpan length) =>
        length.TotalMinutes >= 1 && length.Seconds == 0 ? $"{(int)length.TotalMinutes} minute" : Format(length);

    private void Announce(string glyph, string title, string message, Brush accent)
    {
        _context.Notifier.Show(title, message);
        if (_context.Settings.Get("playSound", true))
        {
            try
            {
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch
            {
                // No audio device: the visual alert is enough.
            }
        }

        IslandHub.ShowAlert(new IslandAlert(glyph, title, accent, "timer", TimeSpan.FromSeconds(6)));
    }

    private void Changed()
    {
        if (Countdown.Running || Stopwatch.Running || Focus.Running)
        {
            if (!_tick.IsEnabled)
            {
                _tick.Start();
            }
        }

        PublishCompact();
        Updated?.Invoke();
    }

    private void PublishCompact()
    {
        CompactInfo? info = null;
        var clocks = new (Clock Clock, string Kind)[] { (Countdown, "cd"), (Focus, "fc"), (Stopwatch, "sw") };
        var pick = clocks.FirstOrDefault(c => c.Clock.Running);
        if (pick.Clock is null)
        {
            pick = clocks.FirstOrDefault(c => !c.Clock.IsIdle);
        }

        if (pick.Clock is not null)
        {
            var clock = pick.Clock;
            var (text, brush) = pick.Kind switch
            {
                "cd" => (Format(clock.Remaining), IslandTheme.Orange),
                "fc" => (Format(clock.Remaining), Phase == FocusPhase.Focus ? IslandTheme.Green : IslandTheme.Blue),
                _ => (FormatStopwatch(clock.Elapsed)[..^2], IslandTheme.Blue),
            };
            info = new CompactInfo("", text, brush, "timer", Dimmed: !clock.Running);
        }

        var key = info is null ? string.Empty : info.Text + info.Dimmed + info.Accent.GetHashCode();
        if (key != _lastCompact)
        {
            _lastCompact = key;
            IslandHub.SetCompact("timer", info);
        }
    }

    public void Dispose()
    {
        _tick.Stop();
        IslandHub.SetCompact("timer", null);
    }
}

/// <summary>The timer panel in the expanded island.</summary>
internal sealed class TimerView
{
    private readonly TimerEngine _engine;
    private readonly Dictionary<string, IslandButton> _modeButtons = [];
    private readonly Dictionary<string, FrameworkElement> _panels = [];
    private string _mode = "timer";
    private bool _visible;

    private TextBlock? _cdTime;
    private Border? _cdBar;
    private Border? _cdBarFill;
    private StackPanel? _cdPresets;
    private StackPanel? _cdAdjust;
    private StackPanel? _cdRunButtons;
    private IslandButton? _cdStart;
    private TextBlock? _swTime;
    private TextBlock? _swLaps;
    private IslandButton? _swStart;
    private TextBlock? _fcTime;
    private TextBlock? _fcPhase;
    private TextBlock? _fcDots;
    private IslandButton? _fcStart;

    public TimerView(TimerEngine engine)
    {
        _engine = engine;
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        if (visible)
        {
            _engine.Updated += Refresh;
            Refresh();
        }
        else
        {
            _engine.Updated -= Refresh;
        }
    }

    public FrameworkElement Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var modes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (id, title) in new[] { ("timer", "Timer"), ("stopwatch", "Stopwatch"), ("focus", "Focus") })
        {
            var button = new IslandButton(null, title, 30);
            button.Margin = new Thickness(2, 0, 2, 0);
            var captured = id;
            button.Clicked += () => SelectMode(captured);
            modes.Children.Add(button);
            _modeButtons[id] = button;
        }

        root.Children.Add(modes);

        var host = new Grid();
        Grid.SetRow(host, 1);
        _panels["timer"] = BuildCountdown();
        _panels["stopwatch"] = BuildStopwatch();
        _panels["focus"] = BuildFocus();
        foreach (var panel in _panels.Values)
        {
            host.Children.Add(panel);
        }

        root.Children.Add(host);
        SelectMode(_mode);
        return root;
    }

    private void SelectMode(string mode)
    {
        _mode = mode;
        foreach (var (id, panel) in _panels)
        {
            panel.Visibility = id == mode ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var (id, button) in _modeButtons)
        {
            button.Background = id == mode ? IslandTheme.SurfaceHover : Brushes.Transparent;
            if (button.Caption is not null)
            {
                button.Caption.Foreground = id == mode ? IslandTheme.Text : IslandTheme.Muted;
            }
        }

        Refresh();
    }

    private FrameworkElement BuildCountdown()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        _cdTime = IslandTheme.Digits(46, IslandTheme.Text, FontWeights.Light);
        _cdTime.HorizontalAlignment = HorizontalAlignment.Center;
        _cdAdjust = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var (label, minutes) in new[] { ("-5m", -5), ("-1m", -1), ("+1m", 1), ("+5m", 5) })
        {
            var button = new IslandButton(null, label, 28, filled: true, foreground: IslandTheme.Muted);
            button.Margin = new Thickness(3, 0, 3, 0);
            var delta = TimeSpan.FromMinutes(minutes);
            button.Clicked += () => _engine.AdjustCountdown(delta);
            _cdAdjust.Children.Add(button);
        }

        _cdPresets = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var minutes in new[] { 1, 5, 10, 15, 25, 30, 60 })
        {
            var button = new IslandButton(null, minutes + "m", 28, filled: true);
            button.Margin = new Thickness(3, 0, 3, 0);
            var length = TimeSpan.FromMinutes(minutes);
            button.Clicked += () => _engine.SetCountdown(length);
            _cdPresets.Children.Add(button);
        }

        _cdBarFill = new Border { Background = IslandTheme.Orange, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _cdBar = new Border { Height = 4, Width = 260, CornerRadius = new CornerRadius(2), Background = IslandTheme.SurfacePressed, Child = _cdBarFill, Margin = new Thickness(0, 4, 0, 0) };

        _cdStart = new IslandButton("", "Start", 40, foreground: IslandTheme.Black, background: IslandTheme.Text, hover: IslandTheme.LightHover);
        var reset = new IslandButton("", "Reset", 40, filled: true);
        var add = new IslandButton("", "1 min", 40, filled: true);
        _cdStart.Clicked += _engine.ToggleCountdown;
        reset.Clicked += _engine.ResetCountdown;
        add.Clicked += () => _engine.AdjustCountdown(TimeSpan.FromMinutes(1));
        _cdRunButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        _cdStart.Margin = new Thickness(4, 0, 4, 0);
        reset.Margin = new Thickness(4, 0, 4, 0);
        add.Margin = new Thickness(4, 0, 4, 0);
        _cdRunButtons.Children.Add(reset);
        _cdRunButtons.Children.Add(_cdStart);
        _cdRunButtons.Children.Add(add);

        panel.Children.Add(_cdTime);
        panel.Children.Add(_cdBar);
        panel.Children.Add(_cdAdjust);
        panel.Children.Add(_cdPresets);
        panel.Children.Add(_cdRunButtons);
        return panel;
    }

    private FrameworkElement BuildStopwatch()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _swTime = IslandTheme.Digits(46, IslandTheme.Text, FontWeights.Light);
        _swTime.HorizontalAlignment = HorizontalAlignment.Center;
        _swStart = new IslandButton("", "Start", 40, foreground: IslandTheme.Black, background: IslandTheme.Text, hover: IslandTheme.LightHover);
        var lap = new IslandButton("", "Lap", 40, filled: true);
        var reset = new IslandButton("", "Reset", 40, filled: true);
        _swStart.Clicked += _engine.ToggleStopwatch;
        lap.Clicked += _engine.Lap;
        reset.Clicked += _engine.ResetStopwatch;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        reset.Margin = new Thickness(4, 0, 4, 0);
        _swStart.Margin = new Thickness(4, 0, 4, 0);
        lap.Margin = new Thickness(4, 0, 4, 0);
        buttons.Children.Add(reset);
        buttons.Children.Add(_swStart);
        buttons.Children.Add(lap);
        _swLaps = IslandTheme.Label(string.Empty, 12, IslandTheme.Muted);
        _swLaps.HorizontalAlignment = HorizontalAlignment.Center;
        _swLaps.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(_swTime);
        panel.Children.Add(buttons);
        panel.Children.Add(_swLaps);
        return panel;
    }

    private FrameworkElement BuildFocus()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _fcPhase = IslandTheme.Label("Focus", 14, IslandTheme.Green, FontWeights.SemiBold);
        _fcPhase.HorizontalAlignment = HorizontalAlignment.Center;
        _fcTime = IslandTheme.Digits(46, IslandTheme.Text, FontWeights.Light);
        _fcTime.HorizontalAlignment = HorizontalAlignment.Center;
        _fcDots = IslandTheme.Label(string.Empty, 13, IslandTheme.Muted);
        _fcDots.HorizontalAlignment = HorizontalAlignment.Center;
        _fcStart = new IslandButton("", "Start", 40, foreground: IslandTheme.Black, background: IslandTheme.Text, hover: IslandTheme.LightHover);
        var skip = new IslandButton("", "Skip", 40, filled: true);
        var reset = new IslandButton("", "Reset", 40, filled: true);
        _fcStart.Clicked += _engine.ToggleFocus;
        skip.Clicked += _engine.SkipFocusPhase;
        reset.Clicked += _engine.ResetFocus;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        reset.Margin = new Thickness(4, 0, 4, 0);
        _fcStart.Margin = new Thickness(4, 0, 4, 0);
        skip.Margin = new Thickness(4, 0, 4, 0);
        buttons.Children.Add(reset);
        buttons.Children.Add(_fcStart);
        buttons.Children.Add(skip);
        panel.Children.Add(_fcPhase);
        panel.Children.Add(_fcTime);
        panel.Children.Add(_fcDots);
        panel.Children.Add(buttons);
        return panel;
    }

    private static void SetText(TextBlock? block, string text)
    {
        if (block is not null && block.Text != text)
        {
            block.Text = text;
        }
    }

    private static void SetStart(IslandButton? button, bool running)
    {
        if (button?.Icon is null || button.Caption is null)
        {
            return;
        }

        button.Icon.Text = running ? "" : "";
        button.Caption.Text = running ? "Pause" : "Start";
    }

    private void Refresh()
    {
        if (!_visible || _cdTime is null)
        {
            return;
        }

        // Countdown
        var cd = _engine.Countdown;
        var cdActive = !cd.IsIdle;
        SetText(_cdTime, TimerEngine.Format(cdActive ? cd.Remaining : cd.Total));
        _cdPresets!.Visibility = cdActive ? Visibility.Collapsed : Visibility.Visible;
        _cdAdjust!.Visibility = cdActive ? Visibility.Collapsed : Visibility.Visible;
        _cdBar!.Visibility = cdActive ? Visibility.Visible : Visibility.Collapsed;
        if (cdActive && cd.Total > TimeSpan.Zero)
        {
            _cdBarFill!.Width = _cdBar.Width * Math.Clamp(cd.Elapsed.TotalSeconds / cd.Total.TotalSeconds, 0, 1);
        }

        SetStart(_cdStart, cd.Running);
        _cdRunButtons!.Children[0].Visibility = cdActive ? Visibility.Visible : Visibility.Collapsed;
        _cdRunButtons.Children[2].Visibility = cdActive ? Visibility.Visible : Visibility.Collapsed;

        // Stopwatch
        var sw = _engine.Stopwatch;
        SetText(_swTime, TimerEngine.FormatStopwatch(sw.Elapsed));
        SetStart(_swStart, sw.Running);
        SetText(_swLaps, _engine.Laps.Count == 0
            ? string.Empty
            : string.Join("   ", _engine.Laps.Take(3).Select((lap, i) => $"#{_engine.Laps.Count - i}  {TimerEngine.FormatStopwatch(lap)}")));

        // Focus
        var fc = _engine.Focus;
        SetText(_fcTime, TimerEngine.Format(fc.IsIdle ? fc.Total : fc.Remaining));
        SetText(_fcPhase, _engine.PhaseTitle);
        if (_fcPhase is not null)
        {
            _fcPhase.Foreground = _engine.Phase == FocusPhase.Focus ? IslandTheme.Green : IslandTheme.Blue;
        }

        var total = _engine.SessionsBeforeLong;
        SetText(_fcDots, string.Concat(Enumerable.Range(0, total).Select(i => i < _engine.Completed ? "● " : "○ ")).TrimEnd());
        SetStart(_fcStart, fc.Running);
    }
}
