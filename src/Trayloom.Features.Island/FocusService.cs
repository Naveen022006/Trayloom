// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

/// <summary>The pure decisions of the Focus session, kept apart so the harness can check them without a window.</summary>
internal static class FocusRules
{
    /// <summary>
    /// True for the notices that wait while someone focuses. Timers, battery, microphone and agent alerts are not in the list
    /// because they need attention even during focus.
    /// </summary>
    public static bool Quietable(string eventId) => eventId is "systemNotification" or "clipboard" or "track" or "download" or "external" or "appleMusic";

    /// <summary>True while notices should wait: a session is on, the switch is on and a focus phase (not a break) is still running.</summary>
    public static bool Quieting(bool active, bool quietOn, TimerPhase phase, bool completed) => active && quietOn && phase == TimerPhase.Focus && !completed;

    /// <summary>
    /// The mode to enter, or null when there is nothing to do: no choice, a mode that no longer exists, or one that is already
    /// active (it was not Focus that entered it, so Focus must not leave it later either).
    /// </summary>
    public static string? ModeToEnter(string setting, string? activeId, IReadOnlyCollection<string> known)
    {
        if (string.IsNullOrEmpty(setting) || !known.Contains(setting) || setting == activeId)
        {
            return null;
        }

        return setting;
    }

    /// <summary>Leaving is allowed only for the mode Focus entered itself and only while it is still the active one.</summary>
    public static bool ShouldLeave(string? enteredId, string? activeId, bool leaveOn) => leaveOn && enteredId is not null && enteredId == activeId;

    /// <summary>The detail line of the summary notice.</summary>
    public static string HeldText(int held) => held == 1 ? Loc.T("1 notice held back") : Loc.F("{0} notices held back", held);
}

/// <summary>Notice events of the Focus session.</summary>
internal static class FocusNotices
{
    /// <summary>"While you focused": how many notices waited. Shown only while the quiet switch is on.</summary>
    public static readonly NoticeEvent Summary = new("focusSummary", K.FocusQuiet, 1, 5, "notifications");
}

/// <summary>
/// A Pomodoro that can start a mode and quiet the island's own optional notices (spec 10c). Event handlers only; no timers of its own.
/// Windows' Do Not Disturb can neither be set nor read, so only Trayloom's notices are held and the alarm always rings.
/// </summary>
internal sealed class FocusService : IDisposable
{
    private readonly IslandCore _core;
    private bool _summaryPending;

    public FocusService(IslandCore core)
    {
        _core = core;
        core.Timer.Changed += OnTimerChanged;
        core.Timer.Finished += OnFinished;
        core.Timer.AlarmChanged += OnAlarmChanged;
    }

    /// <summary>A session started from here is on.</summary>
    public bool Active { get; private set; }

    /// <summary>Notices are being held back right now.</summary>
    public bool Quieting => FocusRules.Quieting(Active, _core.App.Get<bool>(K.FocusQuiet), _core.Timer.Phase, _core.Timer.Completed);

    /// <summary>How many notices have been held back since the last summary.</summary>
    public int Held { get; private set; }

    /// <summary>The mode this session entered, or null when it entered none.</summary>
    public string? EnteredModeId { get; private set; }

    /// <summary>Raised on the UI thread when the session starts or ends.</summary>
    public event Action? Changed;

    private IModesService? Modes => _core.Ctx.Services.Get<IModesService>();

    /// <summary>The name of the entered mode, for the timer page.</summary>
    public string? EnteredModeName => EnteredModeId is null ? null : Modes?.List.FirstOrDefault(m => m.Id == EnteredModeId)?.Name;

    /// <summary>Starts a Pomodoro (unless one is already running), turns the session on and enters the chosen mode.</summary>
    public void Start()
    {
        var t = _core.Timer;
        if (t.HasSession && t.Mode != TimerMode.Pomodoro)
        {
            // A plain timer or the stopwatch is not a focus session; the person can cancel it first.
            return;
        }

        Active = true;
        Held = 0;
        _summaryPending = false;
        if (!t.HasSession)
        {
            t.Start(TimerMode.Pomodoro);
        }

        EnterMode();
        Changed?.Invoke();
    }

    /// <summary>Counts and swallows a notice that should wait. Returns true when the notice must not be shown.</summary>
    public bool Hold(Notice n)
    {
        if (!Quieting || !FocusRules.Quietable(n.Event.Id))
        {
            return false;
        }

        Held++;
        return true;
    }

    private void EnterMode()
    {
        EnteredModeId = null;
        var modes = Modes;
        if (modes is null)
        {
            return;
        }

        try
        {
            var id = FocusRules.ModeToEnter(_core.App.Get<string>(K.FocusModeId), modes.ActiveId, [.. modes.List.Select(m => m.Id)]);
            if (id is null)
            {
                return;
            }

            // A refusal (Away, an elevated window) shows Modes' own toast; the Pomodoro runs either way.
            if (modes.Enter(id, "focus"))
            {
                EnteredModeId = id;
            }
            else
            {
                _core.Ctx.Log.Info("[island] focus: the mode was not entered");
            }
        }
        catch (Exception ex)
        {
            _core.Ctx.Log.Error("[island] focus: entering the mode failed", ex);
        }
    }

    private void OnTimerChanged()
    {
        if (Active && (!_core.Timer.HasSession || _core.Timer.CycleFinished))
        {
            End(_core.Timer.CycleFinished);
        }
    }

    private void End(bool completed)
    {
        Active = false;
        try
        {
            var modes = Modes;
            if (modes is not null && FocusRules.ShouldLeave(EnteredModeId, modes.ActiveId, _core.App.Get<bool>(K.FocusLeaveMode)))
            {
                modes.Leave("focus");
            }
        }
        catch (Exception ex)
        {
            _core.Ctx.Log.Error("[island] focus: leaving the mode failed", ex);
        }

        EnteredModeId = null;

        // A cancelled session has nothing to celebrate, but whatever waited is still reported.
        if (completed)
        {
            _core.Mascot.React("celebrate");
        }
        else if (!_core.Timer.Ringing)
        {
            PostSummary();
        }

        Changed?.Invoke();
    }

    private void OnFinished(TimerPhase phase, bool cycle)
    {
        if (phase != TimerPhase.Focus || Held == 0)
        {
            return;
        }

        // Complete() starts the alarm before raising this, so Ringing is already right: wait for Stop, or report at once.
        if (_core.Timer.Ringing)
        {
            _summaryPending = true;
        }
        else
        {
            PostSummary();
        }
    }

    private void OnAlarmChanged()
    {
        if (_summaryPending && !_core.Timer.Ringing)
        {
            PostSummary();
        }
    }

    private void PostSummary()
    {
        _summaryPending = false;
        var held = Held;
        Held = 0;
        if (held <= 0)
        {
            return;
        }

        _core.Ctx.Log.Info($"[island] focus: {held} notices were held back");
        _core.ShowNotice(new Notice { Event = FocusNotices.Summary, Glyph = G.Timer, Title = Loc.T("While you focused"), Detail = FocusRules.HeldText(held), Tint = System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x0A) });
    }

    public void Dispose()
    {
        _core.Timer.Changed -= OnTimerChanged;
        _core.Timer.Finished -= OnFinished;
        _core.Timer.AlarmChanged -= OnAlarmChanged;
    }
}

/// <summary>The Focus rows of Settings > Dynamic Island > Content > Timer (spec 10c).</summary>
internal static class FocusSettings
{
    /// <summary>Adds the Focus session heading, the mode picker (only while Modes is installed), the three switches and the Do Not Disturb note.</summary>
    public static void Add(SettingsCard card, IAppSettings app, ISettingsContext c)
    {
        card.AddDivider();
        card.Add(UiKit.Text(Loc.T("Focus session"), 13, System.Windows.FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var modes = c.Services.Get<IModesService>();
        if (modes is not null)
        {
            var items = new List<(string Value, string Label)> { ("", Loc.T("None")) };
            items.AddRange(modes.List.Select(m => (m.Id, m.Name)));
            var current = app.Get<string>(K.FocusModeId);
            card.Add(new PickerRow<string>(null, Loc.T("Mode during focus"), null, items, items.Any(i => i.Value == current) ? current : "", v => app.Set(K.FocusModeId, v)));
            card.AddDivider();
            card.Add(SUi.SwitchRow(app, K.FocusLeaveMode, "", Loc.T("Leave the mode when the session ends"), Loc.T("Its windows are put away as the mode’s own “When I leave” setting says.")));
            card.AddDivider();
        }

        card.Add(SUi.SwitchRow(app, K.FocusQuiet, "", Loc.T("Quiet the island while you focus"), Loc.T("Copies, new songs, downloads and Windows notifications wait until your break. Timers, battery, microphone and AI agent alerts still show.")));
        card.AddDivider();
        card.Add(SUi.SwitchRow(app, K.FocusMascot, "", Loc.T("Show the companion with the time left")));
        card.AddCaption(Loc.T("Windows doesn’t let apps turn on Do not disturb. Turn it on yourself to silence Windows notifications too."));
        var page = Environment.OSVersion.Version.Build >= 22000 ? "ms-settings:notifications" : "ms-settings:quiethours";
        card.Add(UiKit.Button(Loc.T("Open Windows notification settings…"), ButtonKind.Bordered, 12, null, () => UiKit.OpenUrl(page)));
    }
}
