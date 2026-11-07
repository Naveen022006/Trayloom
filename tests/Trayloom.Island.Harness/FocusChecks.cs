// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>Checks of the pure decisions of the Focus session (<see cref="FocusRules"/>). No window, timer or mode is touched.</summary>
internal static class FocusChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Quietable();
        Quieting();
        ModeToEnter();
        ShouldLeave();
    }

    private static void Check(bool ok, string what) => _check(ok, "focus: " + what);

    private static void Quietable()
    {
        foreach (var id in new[] { "systemNotification", "clipboard", "track", "download", "external", "appleMusic" })
        {
            Check(FocusRules.Quietable(id), "quietable " + id);
        }

        foreach (var id in new[] { "timer", "battery", "micInUse", "agents", "agentsAttention", "volume", "capture", "focusSummary" })
        {
            Check(!FocusRules.Quietable(id), "not quietable " + id);
        }
    }

    private static void Quieting()
    {
        Check(FocusRules.Quieting(true, true, TimerPhase.Focus, false), "quiets during a running focus phase");
        Check(!FocusRules.Quieting(false, true, TimerPhase.Focus, false), "no session, no quiet");
        Check(!FocusRules.Quieting(true, false, TimerPhase.Focus, false), "switch off, no quiet");
        Check(!FocusRules.Quieting(true, true, TimerPhase.Focus, true), "finished focus phase does not quiet");
        Check(!FocusRules.Quieting(true, true, TimerPhase.ShortBreak, false), "short break does not quiet");
        Check(!FocusRules.Quieting(true, true, TimerPhase.LongBreak, false), "long break does not quiet");
        Check(!FocusRules.Quieting(true, true, TimerPhase.Timer, false), "plain timer does not quiet");
    }

    private static void ModeToEnter()
    {
        string[] known = ["work", "code"];
        Check(FocusRules.ModeToEnter("", null, known) is null, "empty choice enters nothing");
        Check(FocusRules.ModeToEnter("gone", null, known) is null, "unknown mode enters nothing");
        Check(FocusRules.ModeToEnter("code", "code", known) is null, "already active mode is left alone");
        Check(FocusRules.ModeToEnter("code", "work", known) == "code", "valid mode replaces another one");
        Check(FocusRules.ModeToEnter("code", null, known) == "code", "valid mode with none active");
    }

    private static void ShouldLeave()
    {
        Check(FocusRules.ShouldLeave("code", "code", true), "leaves its own mode");
        Check(!FocusRules.ShouldLeave("code", "work", true), "does not leave a mode started in between");
        Check(!FocusRules.ShouldLeave("code", null, true), "nothing active, nothing to leave");
        Check(!FocusRules.ShouldLeave(null, "code", true), "entered nothing, leaves nothing");
        Check(!FocusRules.ShouldLeave("code", "code", false), "switch off keeps the mode");
    }
}
