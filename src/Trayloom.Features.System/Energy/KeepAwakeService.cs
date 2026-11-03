// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Trayloom.Core;

namespace Trayloom.Features.SystemTools;

/// <summary>How a keep-awake session was started.</summary>
internal enum SessionKind
{
    None,
    Manual,
    Automatic,
}

/// <summary>State of the closed-lid mode switch.</summary>
internal enum LidState
{
    Off,
    Configuring,
    Failed,
}

/// <summary>
/// The keep-awake engine (spec 02a 4.1 / 05 E1): a session holds a power request (system, plus display unless "Allow the display to sleep"),
/// ends at its end date, is watched by a battery guard, may nudge the pointer, pause while locked, start from automation conditions and
/// override the lid-close action. All members are called on the UI thread.
/// </summary>
internal sealed class KeepAwakeService : IKeepAwakeService, IDisposable
{
    private static readonly Guid ButtonSubgroup = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    private static readonly Guid LidAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");
    private static readonly Guid LidSwitchState = new("BA3E0F4D-B817-4094-A86D-D0DD7A5A3B8F");

    private readonly IFeatureContext _ctx;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _battery;
    private readonly DispatcherTimer _jiggle;
    private readonly DispatcherTimer _appPoll;
    private readonly string _journal;
    private nint _request;
    private nint _reason;
    private HwndSource? _lidWindow;
    private nint _lidRegistration;
    private bool _holding;
    private bool _paused;
    private bool _suppressAutomation;
    private bool _lidApplied;
    private int _savedBrightness = -1;
    private bool _lidClosedNow;
    private readonly object _lidGate = new();
    private Task _lidWork = Task.CompletedTask;
    private bool _disposed;

    public KeepAwakeService(IFeatureContext ctx)
    {
        _ctx = ctx;
        _journal = Path.Combine(ctx.DataDirectory, "lid-journal.json");
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => OnTick(), ctx.Ui) { IsEnabled = false };
        _battery = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => CheckBattery(), ctx.Ui) { IsEnabled = false };
        _jiggle = new DispatcherTimer(DispatcherPriority.Background, ctx.Ui) { IsEnabled = false };
        _jiggle.Tick += (_, _) => Jiggle();
        _appPoll = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => EvaluateAutomation(), ctx.Ui) { IsEnabled = false };
        ctx.App.Changed += OnSetting;
        SystemEvents.PowerModeChanged += OnPowerMode;
        SystemEvents.DisplaySettingsChanged += OnDisplays;
        SystemEvents.SessionSwitch += OnSession;
        RecoverLid();
    }

    /// <summary>The running instance (null while the feature is not installed).</summary>
    public static KeepAwakeService? Instance { get; private set; }

    public event Action? Changed;

    public SessionKind Kind { get; private set; }

    /// <summary>The chip (minutes; 0 = indefinite) of the manual session, or -1 for an Until session or none.</summary>
    public int SelectedMinutes { get; private set; } = -1;

    /// <summary>True while a manual session started from the Until picker runs.</summary>
    public bool UntilSession { get; private set; }

    public LidState Lid { get; private set; }

    public bool IsActive => Kind != SessionKind.None;

    public DateTime? EndsAt { get; private set; }

    public bool IsPaused => _paused;

    /// <summary>The automatic condition sentence, or the generic one.</summary>
    public string AutomationStatus
    {
        get
        {
            var conditions = Selected();
            if (conditions.Count == 1)
            {
                return conditions[0] switch
                {
                    "display" => Strings.KeepAwake.ExternalDisplayActive,
                    "power" => Strings.KeepAwake.PowerActive,
                    _ => Strings.KeepAwake.RunningAppsActive,
                };
            }

            return Strings.KeepAwake.AutomationActive;
        }
    }

    /// <summary>The status text of the session row.</summary>
    public string StatusText
    {
        get
        {
            if (!IsActive)
            {
                return Strings.KeepAwake.Idle;
            }

            if (Kind == SessionKind.Automatic)
            {
                return AutomationStatus;
            }

            return EndsAt is { } end ? Strings.KeepAwake.EndsIn(end - DateTime.Now) : Strings.KeepAwake.ActiveIndefinite;
        }
    }

    /// <summary>True when battery protection currently prevents starting a session.</summary>
    public bool BatteryBlocked
    {
        get
        {
            var limit = _ctx.App.Get<int>("batteryLimitPercent");
            var p = PowerStatus.Read();
            return limit > 0 && p.HasBattery && !p.OnAc && p.Percent <= limit;
        }
    }

    public int BatteryPercent => PowerStatus.Read().Percent;

    public static double ToRefSeconds(DateTime local) => (local.ToUniversalTime() - new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

    public static DateTime FromRefSeconds(double seconds) => new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();

    public void Start()
    {
        Instance = this;
        _ctx.Services.Provide<IKeepAwakeService>(this);
        ApplyAutomationTimers();
        if (_ctx.App.Get<bool>("keepAwakeAutoStart") && !IsActive)
        {
            StartDefault();
        }

        EvaluateAutomation();
    }

    public void Stop()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        _ctx.Services.Withdraw<IKeepAwakeService>(this);
        End(notify: false, suppressAutomation: false);
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ctx.App.Changed -= OnSetting;
        SystemEvents.PowerModeChanged -= OnPowerMode;
        SystemEvents.DisplaySettingsChanged -= OnDisplays;
        SystemEvents.SessionSwitch -= OnSession;
        _tick.Stop();
        _battery.Stop();
        _jiggle.Stop();
        _appPoll.Stop();
        ReleaseRequest();
        RestoreLid();
        if (_reason != 0)
        {
            Marshal.FreeHGlobal(_reason);
            _reason = 0;
        }
    }

    // ------------------------------------------------------------------------------------------------ public actions

    /// <inheritdoc />
    public void Activate(TimeSpan? duration) => Begin(SessionKind.Manual, duration is { } d && d > TimeSpan.Zero ? DateTime.Now + d : null, duration is { } x ? (int)x.TotalMinutes : 0, false);

    /// <summary>Starts a manual session from a duration chip (0 = indefinite).</summary>
    public void ActivateChip(int minutes) => Begin(SessionKind.Manual, minutes > 0 ? DateTime.Now.AddMinutes(minutes) : null, minutes, false);

    /// <summary>Starts a manual session that ends at <paramref name="end"/> (the Until chip).</summary>
    public void ActivateUntil(DateTime end) => Begin(SessionKind.Manual, end, -1, true);

    /// <summary>Restarts the last pick: the saved Until time while ahead, else the default duration.</summary>
    public void StartDefault()
    {
        var app = _ctx.App;
        var until = app.Get<double>("keepAwakeUntilTime");
        if (app.Get<bool>("keepAwakeSwitchUsesUntil") && until > 0 && FromRefSeconds(until) > DateTime.Now)
        {
            ActivateUntil(FromRefSeconds(until));
            return;
        }

        ActivateChip(app.Get<int>("defaultDurationMinutes"));
    }

    /// <inheritdoc />
    public void Deactivate() => End(notify: false, suppressAutomation: true);

    /// <summary>Toggles: ends a running session, else restarts the last pick.</summary>
    public void Toggle()
    {
        if (IsActive)
        {
            Deactivate();
        }
        else
        {
            StartDefault();
        }
    }

    /// <summary>Adds time to a running manual session (the +15m / +30m / +1h chips).</summary>
    public void Extend(int minutes)
    {
        if (EndsAt is { } end)
        {
            EndsAt = end.AddMinutes(minutes);
            Raise();
        }
    }

    /// <summary>Turns the closed-lid override preference on or off (applied while a session runs).</summary>
    public void SetClamshellPreferred(bool on)
    {
        _ctx.App.Set("clamshellPreferred", on);
        if (!on)
        {
            Lid = LidState.Off;
        }

        ApplyLid();
        Raise();
    }

    /// <summary>Evaluates the configured automation conditions now.</summary>
    public bool ConditionsHold()
    {
        var selected = Selected();
        if (selected.Count == 0)
        {
            return false;
        }

        var all = _ctx.App.Get<bool>("keepAwakeAutomationRequireAll");
        bool Eval(string c) => c switch
        {
            "display" => DisplayNative.HasExternalDisplay(),
            "power" => PowerStatus.Read().OnAc,
            _ => AnySelectedAppRunning(),
        };
        return all ? selected.All(Eval) : selected.Any(Eval);
    }

    /// <summary>Which automation conditions are on and configured ("display", "power", "apps").</summary>
    public List<string> Selected()
    {
        var app = _ctx.App;
        var list = new List<string>();
        if (app.Get<bool>("keepAwakeExternalDisplay"))
        {
            list.Add("display");
        }

        if (app.Get<bool>("keepAwakeConnectedToPower"))
        {
            list.Add("power");
        }

        if (app.Get<bool>("keepAwakeRunningApps") && app.Get<List<string>>("keepAwakeRunningAppBundleIDs").Count > 0)
        {
            list.Add("apps");
        }

        return list;
    }

    // ------------------------------------------------------------------------------------------------ session

    private void Begin(SessionKind kind, DateTime? end, int chip, bool until)
    {
        if (kind == SessionKind.Automatic ? BatteryBlocked : false)
        {
            return;
        }

        if (kind == SessionKind.Manual && BatteryBlocked)
        {
            Raise();
            return;
        }

        var wasActive = IsActive;
        Kind = kind;
        EndsAt = end;
        SelectedMinutes = chip;
        UntilSession = until;
        if (kind == SessionKind.Manual)
        {
            _suppressAutomation = false;
        }

        if (until && end is { } e)
        {
            _ctx.App.Set("keepAwakeUntilTime", ToRefSeconds(e));
            _ctx.App.Set("keepAwakeSwitchUsesUntil", true);
        }
        else if (kind == SessionKind.Manual)
        {
            _ctx.App.Set("keepAwakeSwitchUsesUntil", false);
        }

        if (!wasActive)
        {
            _ctx.Log.Info($"Keep awake on ({kind}, until {end?.ToString("t") ?? "turned off"})");
        }

        Acquire();
        _tick.Start();
        _battery.Start();
        ApplyJiggle();
        ApplyLid();
        Raise();
    }

    private void End(bool notify, bool suppressAutomation)
    {
        var was = IsActive;
        if (was && suppressAutomation && (Kind == SessionKind.Automatic || ConditionsHold()))
        {
            _suppressAutomation = true;
        }

        Kind = SessionKind.None;
        EndsAt = null;
        SelectedMinutes = -1;
        UntilSession = false;
        _tick.Stop();
        _battery.Stop();
        _jiggle.Stop();
        ReleaseRequest();
        RestoreLid();
        Lid = LidState.Off;
        if (was)
        {
            _ctx.Log.Info("Keep awake off");
            Raise();
        }

        if (notify)
        {
            _ctx.Notifier.Show(Strings.KeepAwake.SessionEndedTitle, Strings.KeepAwake.SessionEndedBody);
        }
    }

    private void OnTick()
    {
        if (!IsActive)
        {
            return;
        }

        if (EndsAt is { } end && DateTime.Now >= end)
        {
            // A manual session whose automation conditions still hold continues silently as an automatic one.
            if (Kind == SessionKind.Manual && ConditionsHold() && !BatteryBlocked)
            {
                Kind = SessionKind.Automatic;
                EndsAt = null;
                SelectedMinutes = -1;
                UntilSession = false;
                Raise();
                return;
            }

            End(notify: true, suppressAutomation: false);
            return;
        }

        Raise();
    }

    private void CheckBattery()
    {
        if (IsActive && BatteryBlocked)
        {
            End(notify: false, suppressAutomation: true);
            _ctx.Notifier.Show(Strings.KeepAwake.DisabledTitle, Strings.KeepAwake.LowBatteryBody, NotificationLevel.Warning);
        }
    }

    private void Raise() => Changed?.Invoke();

    // ------------------------------------------------------------------------------------------------ power request

    private void Acquire()
    {
        if (_paused)
        {
            return;
        }

        try
        {
            if (_request == 0)
            {
                _reason = _reason == 0 ? Marshal.StringToHGlobalUni("Trayloom keep awake") : _reason;
                var context = new ReasonContext { Version = 0, Flags = 1, Reason = _reason };
                _request = PowerCreateRequest(ref context);
            }

            if (_request == 0)
            {
                FallbackExecutionState(true);
                return;
            }

            PowerSetRequest(_request, 1);
            if (_ctx.App.Get<bool>("keepAwakeAllowDisplaySleep"))
            {
                PowerClearRequest(_request, 0);
            }
            else
            {
                PowerSetRequest(_request, 0);
            }

            _holding = true;
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Keep awake could not set the power request", ex);
        }
    }

    private void ReleaseRequest()
    {
        if (_request != 0)
        {
            try
            {
                PowerClearRequest(_request, 0);
                PowerClearRequest(_request, 1);
                CloseHandle(_request);
            }
            catch (Exception)
            {
            }

            _request = 0;
        }

        if (_holding)
        {
            FallbackExecutionState(false);
        }

        _holding = false;
    }

    private void FallbackExecutionState(bool on)
    {
        // Only used when PowerCreateRequest fails; called on the UI thread that set it.
        var flags = 0x80000000u;
        if (on)
        {
            flags |= 0x1;
            if (!_ctx.App.Get<bool>("keepAwakeAllowDisplaySleep"))
            {
                flags |= 0x2;
            }
        }

        SetThreadExecutionState(flags);
        _holding = on;
    }

    // ------------------------------------------------------------------------------------------------ settings and system events

    private void OnSetting(string key)
    {
        switch (key)
        {
            case "keepAwakeAllowDisplaySleep":
                _ctx.Ui.BeginInvoke(() =>
                {
                    if (IsActive && !_paused)
                    {
                        ReleaseRequest();
                        Acquire();
                    }
                });
                break;
            case "keepAwakeMouseJiggleEnabled" or "keepAwakeMouseJiggleIntervalMinutes":
                _ctx.Ui.BeginInvoke(ApplyJiggle);
                break;
            case "clamshellPreferred" or "dimScreenOnLidClose":
                _ctx.Ui.BeginInvoke(() =>
                {
                    ApplyLid();
                    Raise();
                });
                break;
            case "keepAwakeExternalDisplay" or "keepAwakeConnectedToPower" or "keepAwakeRunningApps" or "keepAwakeRunningAppBundleIDs" or "keepAwakeAutomationRequireAll":
                _ctx.Ui.BeginInvoke(() =>
                {
                    _suppressAutomation = false;
                    ApplyAutomationTimers();
                    EvaluateAutomation();
                });
                break;
            case "batteryLimitPercent":
                _ctx.Ui.BeginInvoke(CheckBattery);
                break;
        }
    }

    private void OnPowerMode(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
        {
            _ctx.Ui.BeginInvoke(() =>
            {
                CheckBattery();
                EvaluateAutomation();
            });
        }
    }

    private void OnDisplays(object? sender, EventArgs e) => _ctx.Ui.BeginInvoke(EvaluateAutomation);

    private void OnSession(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is not (SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock))
        {
            return;
        }

        var locked = e.Reason == SessionSwitchReason.SessionLock;
        _ctx.Ui.BeginInvoke(() =>
        {
            if (!_ctx.App.Get<bool>("keepAwakePauseWhenLocked"))
            {
                return;
            }

            if (locked)
            {
                _paused = true;
                ReleaseRequest();
                _jiggle.Stop();
                RestoreLid();
                Raise();
            }
            else
            {
                _paused = false;
                if (IsActive)
                {
                    Acquire();
                    ApplyJiggle();
                    ApplyLid();
                }
                else
                {
                    EvaluateAutomation();
                }

                Raise();
            }
        });
    }

    // ------------------------------------------------------------------------------------------------ automation

    private void ApplyAutomationTimers()
    {
        if (Selected().Contains("apps"))
        {
            _appPoll.Start();
        }
        else
        {
            _appPoll.Stop();
        }
    }

    private bool AnySelectedAppRunning()
    {
        foreach (var entry in _ctx.App.Get<List<string>>("keepAwakeRunningAppBundleIDs"))
        {
            var name = Path.GetFileNameWithoutExtension(entry);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            try
            {
                var found = Process.GetProcessesByName(name);
                var any = found.Length > 0;
                foreach (var p in found)
                {
                    p.Dispose();
                }

                if (any)
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }
        }

        return false;
    }

    private void EvaluateAutomation()
    {
        if (_disposed)
        {
            return;
        }

        var holds = ConditionsHold();
        if (!holds)
        {
            _suppressAutomation = false;
            if (Kind == SessionKind.Automatic)
            {
                End(notify: false, suppressAutomation: false);
            }

            return;
        }

        if (!IsActive && !_suppressAutomation && !_paused && !BatteryBlocked)
        {
            Begin(SessionKind.Automatic, null, -1, false);
        }
    }

    // ------------------------------------------------------------------------------------------------ pointer nudge

    private void ApplyJiggle()
    {
        _jiggle.Stop();
        if (!IsActive || _paused || !_ctx.App.Get<bool>("keepAwakeMouseJiggleEnabled"))
        {
            return;
        }

        var minutes = Math.Clamp(_ctx.App.Get<int>("keepAwakeMouseJiggleIntervalMinutes"), 1, 15);
        _jiggle.Interval = TimeSpan.FromMinutes(minutes);
        _jiggle.Start();
    }

    private void Jiggle()
    {
        if (!IsActive || _paused)
        {
            return;
        }

        GetCursorPos(out var before);
        if (!InputSender.SendMouseMove(1, 0))
        {
            return;
        }

        GetCursorPos(out var jiggled);
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _ctx.Ui) { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            GetCursorPos(out var now);
            if (Math.Abs(now.X - jiggled.X) <= 2 && Math.Abs(now.Y - jiggled.Y) <= 2)
            {
                InputSender.SendMouseMove(before.X - now.X, before.Y - now.Y);
            }
        };
        timer.Start();
    }

    // ------------------------------------------------------------------------------------------------ closed lid

    private void ApplyLid()
    {
        var wanted = IsActive && !_paused && _ctx.App.Get<bool>("clamshellPreferred");
        if (wanted && !_lidApplied)
        {
            Lid = LidState.Configuring;
            Raise();
            if (WriteLidAction(doNothing: true))
            {
                _lidApplied = true;
                Lid = LidState.Off;
            }
            else
            {
                Lid = LidState.Failed;
                _ctx.App.Set("clamshellPreferred", false);
            }

            Raise();
        }
        else if (!wanted && _lidApplied)
        {
            RestoreLid();
        }

        var dim = IsActive && !_paused && _ctx.App.Get<bool>("clamshellPreferred") && _ctx.App.Get<bool>("dimScreenOnLidClose");
        if (dim)
        {
            RegisterLidWatch();
        }
        else
        {
            UnregisterLidWatch();
        }
    }

    private void RestoreLid()
    {
        UnregisterLidWatch();
        RestoreBrightnessAfterLid(wait: true);
        if (!_lidApplied)
        {
            return;
        }

        WriteLidAction(doNothing: false);
        _lidApplied = false;
    }

    private sealed record LidJournal(Guid Scheme, uint Ac, uint Dc);

    private bool WriteLidAction(bool doNothing)
    {
        try
        {
            if (PowerGetActiveScheme(0, out var schemePtr) != 0)
            {
                return false;
            }

            var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
            LocalFree(schemePtr);
            var sub = ButtonSubgroup;
            var action = LidAction;
            if (doNothing)
            {
                if (PowerReadACValueIndex(0, ref scheme, ref sub, ref action, out var ac) != 0 || PowerReadDCValueIndex(0, ref scheme, ref sub, ref action, out var dc) != 0)
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_journal)!);
                File.WriteAllText(_journal, JsonSerializer.Serialize(new LidJournal(scheme, ac, dc)));
                var ok = PowerWriteACValueIndex(0, ref scheme, ref sub, ref action, 0) == 0
                         && PowerWriteDCValueIndex(0, ref scheme, ref sub, ref action, 0) == 0
                         && PowerSetActiveScheme(0, ref scheme) == 0;
                if (!ok)
                {
                    RestoreFromJournal();
                }

                return ok;
            }

            return RestoreFromJournal();
        }
        catch (Exception ex)
        {
            _ctx.Log.Warn($"Closed-lid mode failed: {ex.Message}");
            return false;
        }
    }

    private bool RestoreFromJournal()
    {
        if (!File.Exists(_journal))
        {
            return true;
        }

        try
        {
            var journal = JsonSerializer.Deserialize<LidJournal>(File.ReadAllText(_journal));
            if (journal is null)
            {
                return false;
            }

            var scheme = journal.Scheme;
            var sub = ButtonSubgroup;
            var action = LidAction;
            var ok = PowerWriteACValueIndex(0, ref scheme, ref sub, ref action, journal.Ac) == 0
                     && PowerWriteDCValueIndex(0, ref scheme, ref sub, ref action, journal.Dc) == 0;
            if (PowerGetActiveScheme(0, out var activePtr) == 0)
            {
                var active = Marshal.PtrToStructure<Guid>(activePtr);
                LocalFree(activePtr);
                if (active == scheme)
                {
                    PowerSetActiveScheme(0, ref scheme);
                }
            }

            if (ok)
            {
                File.Delete(_journal);
            }

            return ok;
        }
        catch (Exception ex)
        {
            _ctx.Log.Warn($"Could not restore the lid action: {ex.Message}");
            return false;
        }
    }

    /// <summary>After a crash while the lid action was overridden, the next start puts the original values back.</summary>
    private void RecoverLid()
    {
        if (File.Exists(_journal))
        {
            RestoreFromJournal();
        }
    }

    private void RegisterLidWatch()
    {
        if (_lidWindow is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("TrayloomLidWatch") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
        _lidWindow = new HwndSource(parameters);
        _lidWindow.AddHook(LidHook);
        var guid = LidSwitchState;
        _lidRegistration = RegisterPowerSettingNotification(_lidWindow.Handle, ref guid, 0);
    }

    private void UnregisterLidWatch()
    {
        if (_lidRegistration != 0)
        {
            UnregisterPowerSettingNotification(_lidRegistration);
            _lidRegistration = 0;
        }

        _lidWindow?.Dispose();
        _lidWindow = null;
    }

    private nint LidHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x218 && wParam == 0x8013 && lParam != 0)
        {
            var guid = Marshal.PtrToStructure<Guid>(lParam);
            if (guid == LidSwitchState)
            {
                var closed = Marshal.ReadByte(lParam, 20) == 0;
                _ctx.Ui.BeginInvoke(() => OnLid(closed));
            }
        }

        return 0;
    }

    private void OnLid(bool closed)
    {
        if (_lidClosedNow == closed)
        {
            return;
        }

        _lidClosedNow = closed;
        if (closed)
        {
            // Save the built-in panel level (never 0: 0 means it is already asleep) and dim it to zero.
            QueueLidWork(() =>
            {
                var rows = Wmi.Query(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE", "CurrentBrightness");
                var level = rows.Count > 0 ? Wmi.ToInt(rows[0]["CurrentBrightness"], -1) : -1;
                if (level > 0)
                {
                    _savedBrightness = level;
                    _ctx.App.Set("dimmedDisplaySavedBrightness", level);
                    BrightnessWrites.MarkOwn();
                    Wmi.ForEach(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE", o => Wmi.Invoke(o, "WmiSetBrightness", 1, 0));
                    BrightnessWrites.MarkOwn();
                }
            });
        }
        else
        {
            RestoreBrightnessAfterLid(wait: false);
        }
    }

    /// <summary>Runs a lid-dimming WMI call off the UI thread; the calls run one after the other, in the order the lid moved.</summary>
    private Task QueueLidWork(Action work)
    {
        lock (_lidGate)
        {
            _lidWork = _lidWork.ContinueWith(
                _ =>
                {
                    try
                    {
                        work();
                    }
                    catch (Exception ex)
                    {
                        _ctx.Log.Error("Lid brightness change failed", ex);
                    }
                },
                TaskScheduler.Default);
            return _lidWork;
        }
    }

    /// <summary><paramref name="wait"/>: hold the caller (briefly) until the level is back, for shutdown and switching the feature off.</summary>
    private void RestoreBrightnessAfterLid(bool wait)
    {
        var task = QueueLidWork(() =>
        {
            var level = _savedBrightness > 0 ? _savedBrightness : _ctx.App.Get<int>("dimmedDisplaySavedBrightness", 0);
            if (level <= 0)
            {
                return;
            }

            _savedBrightness = -1;
            _ctx.App.Set("dimmedDisplaySavedBrightness", 0);
            BrightnessWrites.MarkOwn();
            Wmi.ForEach(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE", o => Wmi.Invoke(o, "WmiSetBrightness", 1, level));
            BrightnessWrites.MarkOwn();
        });
        if (wait)
        {
            task.Wait(2000);
        }
    }

    // ------------------------------------------------------------------------------------------------ native

    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public nint Reason;
        public nint Pad1;
        public nint Pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(nint request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(nint request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(nint root, out nint scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(nint root, ref Guid scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(nint root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(nint root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(nint root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(nint root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid setting, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(nint handle);
}
