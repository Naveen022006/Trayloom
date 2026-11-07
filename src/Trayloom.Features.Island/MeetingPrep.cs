// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

/// <summary>The pure decisions of the meeting heads-up (spec 10d): which event is shown and what its countdown says.</summary>
internal static class MeetingRules
{
    /// <summary>How long after its start a meeting stays in the island, so a late joiner still finds the Join button.</summary>
    public const int AfterStartMinutes = 10;

    /// <summary>
    /// The meeting to show now: a timed event inside [start - lead, min(end, start + 10 min)) that has a video link when
    /// <paramref name="videoOnly"/> is on and was not dismissed. The earliest start wins; on a tie the one with a link does.
    /// </summary>
    public static CalEvent? Pick(IEnumerable<CalEvent> events, DateTime now, int leadMinutes, bool videoOnly, IReadOnlySet<string> dismissed)
    {
        var lead = TimeSpan.FromMinutes(leadMinutes);
        CalEvent? best = null;
        foreach (var e in events)
        {
            if (e.AllDay || (videoOnly && string.IsNullOrEmpty(e.JoinUrl)) || dismissed.Contains(Key(e)))
            {
                continue;
            }

            if (now < e.Start - lead || now >= Until(e))
            {
                continue;
            }

            if (best is null || e.Start < best.Start || (e.Start == best.Start && string.IsNullOrEmpty(best.JoinUrl) && !string.IsNullOrEmpty(e.JoinUrl)))
            {
                best = e;
            }
        }

        return best;
    }

    /// <summary>When the heads-up for an event ends: ten minutes after its start, or its end if that comes first.</summary>
    public static DateTime Until(CalEvent e) => e.End < e.Start.AddMinutes(AfterStartMinutes) ? e.End : e.Start.AddMinutes(AfterStartMinutes);

    /// <summary>The key a dismissal is remembered under: the same as the countdown's, so one occurrence of a repeating event is one key.</summary>
    public static string Key(CalEvent e) => e.CountdownKey;

    /// <summary>Minutes and seconds until the start ("4:32"), or an empty string once it has started.</summary>
    public static string Countdown(DateTime start, DateTime now)
    {
        if (now >= start)
        {
            return "";
        }

        var total = (int)Math.Ceiling((start - now).TotalSeconds);
        return $"{total / 60}:{total % 60:00}";
    }
}

/// <summary>
/// Finds the meeting to announce and carries the mic check. It looks every 15 s and whenever the calendar changes, and only while the
/// island, the calendar and the setting are on (spec 10d). Event titles and links are never logged.
/// </summary>
internal sealed class MeetingService : IDisposable
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(15);
    private readonly IslandCore _core;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _dismissed = [];
    private readonly List<CalEvent> _dev = [];
    private DateTime _day = DateTime.Today;
    private DateTime _cameraCheckAt = DateTime.MinValue;
    private bool _started;
    private bool _disposed;

    public MeetingService(IslandCore core)
    {
        _core = core;
        Mic = new MicCheck(core.Ui, core.Ctx.Log);
        _timer = new DispatcherTimer(DispatcherPriority.Background, core.Ui) { Interval = Poll };
        _timer.Tick += (_, _) => Repick();
    }

    /// <summary>The meeting on show, or null.</summary>
    public CalEvent? Current { get; private set; }

    /// <summary>The microphone check of the card on the Calendar page.</summary>
    public MicCheck Mic { get; }

    /// <summary>Raised on the UI thread when the meeting on show changes.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _core.Calendar.Changed += Repick;
        _core.App.Changed += OnSetting;
        _core.Ctx.Runtime.Changed += Repick;
        _timer.Start();
        Repick();
    }

    private void OnSetting(string key)
    {
        if (key is K.MeetingPrep or K.MeetingLead or K.MeetingVideoOnly or K.CalendarEnabled or K.Enabled or K.HiddenModules)
        {
            _core.Ui.BeginInvoke(Repick);
        }
    }

    /// <summary>The gates of the heads-up: the island, the calendar feature and its page, and the setting must all be on.</summary>
    private bool Wanted() =>
        !_disposed
        && _core.IslandOn
        && _core.Ctx.Runtime.IsAvailable("notch-calendar")
        && _core.App.Get<bool>(K.CalendarEnabled)
        && _core.App.Get<bool>(K.MeetingPrep)
        && _core.ModuleVisible("calendar");

    /// <summary>Picks again from the calendar. Called by the poll, by the calendar and by the strip when its meeting runs out.</summary>
    public void Repick()
    {
        if (_disposed || !_started)
        {
            return;
        }

        var now = DateTime.Now;
        if (now.Date != _day)
        {
            // A dismissal lasts until midnight; the test events of yesterday are of no use either.
            _day = now.Date;
            _dismissed.Clear();
            _dev.RemoveAll(e => e.End < now);
        }

        CalEvent? next = null;
        if (Wanted())
        {
            var lead = Math.Clamp(_core.App.Get<int>(K.MeetingLead), 1, 60);
            var pool = _core.Calendar.Upcoming(now.AddMinutes(-MeetingRules.AfterStartMinutes), now.AddMinutes(lead + 1)).Concat(_dev);
            next = MeetingRules.Pick(pool, now, lead, _core.App.Get<bool>(K.MeetingVideoOnly), _dismissed);
        }

        var was = Current;
        Current = next;
        var same = was is null ? next is null : next is not null && MeetingRules.Key(was) == MeetingRules.Key(next);
        if (same)
        {
            return;
        }

        _core.Ctx.Log.Info(next is null ? "[island] Meeting heads-up ended" : "[island] Meeting heads-up shown");
        Changed?.Invoke();
        _core.Window?.RefreshClosed();
        if (next is not null && was is null)
        {
            _core.Mascot.React("perk");
        }
    }

    /// <summary>Called by the strip every second: ends the meeting on time without waiting for the next poll.</summary>
    public void Tick()
    {
        if (Current is { } e && DateTime.Now >= MeetingRules.Until(e))
        {
            Repick();
        }
    }

    /// <summary>"Not now": hides this meeting until midnight.</summary>
    public void Dismiss()
    {
        if (Current is { } e)
        {
            _dismissed.Add(MeetingRules.Key(e));
        }

        Repick();
    }

    /// <summary>Opens the video link in the browser (https only, see <see cref="CalendarService.OpenLink"/>) and dismisses the heads-up.</summary>
    public void Join()
    {
        if (Current?.JoinUrl is { Length: > 0 } url)
        {
            CalendarService.OpenLink(url);
        }

        Dismiss();
    }

    /// <summary>TRAYLOOM_DEV only: an in-memory test meeting six minutes ahead, so the strip can be seen without a real calendar.</summary>
    public void AddDevEvent()
    {
        var start = DateTime.Now.AddMinutes(6);
        _dev.Add(new CalEvent
        {
            Id = "dev-meeting-" + start.Ticks,
            Title = "Test meeting",
            Start = start,
            End = start.AddMinutes(30),
            CalendarName = "Trayloom",
            Source = "dev",
            JoinUrl = "https://meet.google.com/aaa-bbbb-ccc",
        });
        Repick();
    }

    /// <summary>The card's "Check camera" notes that the next camera start on the mirror page is a quick check, which ends by itself.</summary>
    public void NoteCameraCheck() => _cameraCheckAt = DateTime.Now;

    /// <summary>True once per "Check camera" click, within two minutes of it: the mirror page then limits this start to 30 s.</summary>
    public bool TakeCameraCheck()
    {
        var fresh = DateTime.Now - _cameraCheckAt < TimeSpan.FromMinutes(2);
        _cameraCheckAt = DateTime.MinValue;
        return fresh;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        if (_started)
        {
            _core.Calendar.Changed -= Repick;
            _core.App.Changed -= OnSetting;
            _core.Ctx.Runtime.Changed -= Repick;
        }

        Mic.Dispose();
    }
}

/// <summary>
/// A short look at the default communications microphone: a level meter for the card. It opens the real device, so Windows shows its
/// microphone indicator while it runs; it starts only from a click, ends after at most 15 s and keeps nothing but the latest peak.
/// </summary>
internal sealed class MicCheck : IDisposable
{
    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(15);
    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly DispatcherTimer _tick;
    private readonly object _gate = new();
    private MMDevice? _device;
    private WasapiCapture? _capture;
    private WaveFormat? _format;
    private double _pending;
    private DateTime _deadline;
    private int _ticks;

    public MicCheck(Dispatcher ui, ILog log)
    {
        _ui = ui;
        _log = log;
        // 50 ms is 20 Hz, the most the meter needs and the most it is allowed to ask of the UI thread.
        _tick = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(50) };
        _tick.Tick += (_, _) => OnTick();
    }

    public string? DeviceName { get; private set; }

    public bool Muted { get; private set; }

    /// <summary>The recent peak, 0 to 1, with a short fall-off so the bar does not flicker.</summary>
    public double Peak { get; private set; }

    public bool Running { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Raised on the UI thread, at most 20 times a second, while the state or the level changes.</summary>
    public event Action? Changed;

    /// <summary>Opens the microphone for at most <paramref name="max"/> (never more than 15 s). A second call while running does nothing.</summary>
    public void Start(TimeSpan max)
    {
        if (Running)
        {
            return;
        }

        Error = null;
        Peak = 0;
        Muted = false;
        DeviceName = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications))
            {
                Error = Loc.T("No microphone found");
                Changed?.Invoke();
                return;
            }

            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            DeviceName = _device.FriendlyName;
            Muted = _device.AudioEndpointVolume.Mute;
            _capture = new WasapiCapture(_device);
            _format = _capture.WaveFormat;
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += OnStopped;
            _capture.StartRecording();
            lock (_gate)
            {
                _pending = 0;
            }

            _deadline = DateTime.Now + (max > Longest ? Longest : max);
            _ticks = 0;
            Running = true;
            _tick.Start();
        }
        catch (Exception ex)
        {
            // Only the kind of failure is logged, never anything about the device or the person.
            _log.Warn("[island] Meeting mic check could not start: " + ex.GetType().Name);
            Release();
            Error = Loc.T("Can’t open the microphone. Check Settings › Privacy & security › Microphone.");
        }

        Changed?.Invoke();
    }

    /// <summary>Ends the check and lets the microphone go. Safe to call at any time.</summary>
    public void Stop()
    {
        if (!Running && _capture is null)
        {
            return;
        }

        Release();
        Changed?.Invoke();
    }

    private void Release()
    {
        _tick.Stop();
        Running = false;
        Peak = 0;
        var capture = _capture;
        var device = _device;
        _capture = null;
        _device = null;
        _format = null;
        if (capture is not null)
        {
            // Unhooked first, so the stop that follows is not mistaken for a device failure.
            capture.DataAvailable -= OnData;
            capture.RecordingStopped -= OnStopped;
            try
            {
                capture.StopRecording();
            }
            catch
            {
            }

            try
            {
                capture.Dispose();
            }
            catch
            {
            }
        }

        try
        {
            device?.Dispose();
        }
        catch
        {
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _format;
        if (format is null)
        {
            return;
        }

        var peak = PeakOf(e.Buffer, e.BytesRecorded, format);
        lock (_gate)
        {
            if (peak > _pending)
            {
                _pending = peak;
            }
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null)
        {
            return;
        }

        // The device went away or was taken while the check ran (unplugged, or a privacy switch).
        _ui.BeginInvoke(() =>
        {
            if (!Running)
            {
                return;
            }

            _log.Warn("[island] Meeting mic check stopped: " + e.Exception.GetType().Name);
            Release();
            Error = Loc.T("Can’t open the microphone. Check Settings › Privacy & security › Microphone.");
            Changed?.Invoke();
        });
    }

    private void OnTick()
    {
        if (!Running)
        {
            _tick.Stop();
            return;
        }

        if (DateTime.Now >= _deadline)
        {
            Stop();
            return;
        }

        double fresh;
        lock (_gate)
        {
            fresh = _pending;
            _pending = 0;
        }

        Peak = Math.Max(fresh, Peak * 0.8);
        // The mute switch is read about twice a second: cheap, and it follows an Unmute click or the keyboard's mute key.
        if (_ticks++ % 10 == 0 && _device is not null)
        {
            try
            {
                Muted = _device.AudioEndpointVolume.Mute;
            }
            catch
            {
            }
        }

        Changed?.Invoke();
    }

    /// <summary>The loudest sample of a buffer, 0 to 1. Understands 32-bit float and 16-bit PCM; anything else reads as silence.</summary>
    public static double PeakOf(byte[] buffer, int bytes, WaveFormat format)
    {
        var count = Math.Min(bytes, buffer.Length);
        var peak = 0.0;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (var i = 0; i + 4 <= count; i += 4)
            {
                var v = Math.Abs((double)BitConverter.ToSingle(buffer, i));
                if (v > peak)
                {
                    peak = v;
                }
            }
        }
        else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            for (var i = 0; i + 2 <= count; i += 2)
            {
                var v = Math.Abs((int)BitConverter.ToInt16(buffer, i)) / 32767.0;
                if (v > peak)
                {
                    peak = v;
                }
            }
        }

        return double.IsNaN(peak) ? 0 : Math.Min(1.0, peak);
    }

    public void Dispose() => Stop();
}

/// <summary>The closed-island strip of the meeting heads-up: colour dot, title, countdown and a Join pill.</summary>
internal static class MeetingStrip
{
    public static ClosedView Build(IslandCore core, IslandWindow win) =>
        ClosedViews.Dotted(core, win, new ClosedView(new MeetingStripView(core, win), 320, 109));

    private sealed class MeetingStripView : LiveStrip
    {
        private readonly IslandCore _core;
        private readonly IslandWindow _win;
        private readonly Ellipse_ _dot;
        private readonly TextBlock _title;
        private readonly TextBlock _clock;
        private readonly DispatcherTimer _tick;
        private string _shape = "";

        public MeetingStripView(IslandCore core, IslandWindow win)
        {
            _core = core;
            _win = win;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _dot = new Ellipse_ { Width = 6, Height = 6, Margin = new Thickness(Math.Max(8, (win.StripHeight - 6) / 2), 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            _title = D.T("", 11, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis);
            _title.MaxWidth = 120;
            _title.VerticalAlignment = VerticalAlignment.Center;
            _clock = D.Digits("", 13, FontWeights.Medium, D.Text);
            _clock.Margin = new Thickness(8, 0, 10, 0);
            _clock.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(_dot);
            row.Children.Add(_title);
            row.Children.Add(_clock);
            if (core.Meeting.Current?.JoinUrl is { Length: > 0 })
            {
                row.Children.Add(JoinPill(core));
            }
            else
            {
                _clock.Margin = new Thickness(8, 0, 12, 0);
            }

            Children.Add(row);
            Height = win.StripHeight;
            _tick = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (_, _) => Update();
            Update();
        }

        /// <summary>A pill that handles its own click, so the strip's "open Calendar" click does not also fire.</summary>
        private static Border JoinPill(IslandCore core)
        {
            var label = D.T(Loc.T("Join"), 11.5, FontWeights.SemiBold, D.Text);
            label.VerticalAlignment = VerticalAlignment.Center;
            var pill = new Border
            {
                Background = D.White(0.16),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 0, 10, 0),
                Height = 22,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = label,
            };
            pill.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                core.Meeting.Join();
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(pill, "Meeting.Join");
            D.Tip(pill, Loc.T("Join"));
            return pill;
        }

        protected override void Subscribe()
        {
            _tick.Start();
            On(() => { }, () => _tick.Stop());
        }

        private void Update()
        {
            // The service polls every 15 s; the strip's own second tick lets it end a meeting on time.
            _core.Meeting.Tick();
            if (_core.Meeting.Current is not { } e)
            {
                return;
            }

            var now = DateTime.Now;
            _dot.Fill = new SolidColorBrush(e.Color);
            _title.Text = e.Title.Length == 0 ? Loc.T("Untitled event") : e.Title;
            var left = MeetingRules.Countdown(e.Start, now);
            _clock.Text = left.Length == 0 ? Loc.T("Now") : left;
            _clock.Foreground = left.Length == 0 ? D.Mint : D.Text;
            var shape = _clock.Text.Length.ToString();
            if (shape != _shape)
            {
                _shape = shape;
                _win.ReMeasureClosed();
            }
        }
    }
}

/// <summary>The Meeting heads-up rows of Settings > Dynamic Island > Content > Calendar.</summary>
internal static class MeetingSettings
{
    public static void Add(SettingsCard card, IAppSettings app, Action? rebuild = null)
    {
        card.AddDivider();
        card.Add(SUi.SwitchRow(app, K.MeetingPrep, "", Loc.T("Meeting heads-up"), Loc.T("Shows your next meeting in the closed island a few minutes before it starts, with Join. Titles may appear in screen captures."), _ => rebuild?.Invoke()));
        if (!app.Get<bool>(K.MeetingPrep))
        {
            return;
        }

        var leads = new List<(int Value, string Label)>();
        foreach (var v in new[] { 2, 5, 10, 15 })
        {
            leads.Add((v, Loc.F("{0} min before", v)));
        }

        var current = app.Get<int>(K.MeetingLead);
        if (!leads.Any(l => l.Value == current))
        {
            current = 5;
        }

        card.Add(new PickerRow<int>("", Loc.T("Show it"), null, leads, current, v => app.Set(K.MeetingLead, v), 140));
        card.Add(SUi.SwitchRow(app, K.MeetingVideoOnly, "", Loc.T("Only meetings with a video link")));
    }
}
