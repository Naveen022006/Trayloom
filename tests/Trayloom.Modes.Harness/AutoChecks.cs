// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;
using Trayloom.Features.WindowTools.Modes;
using static Trayloom.Modes.Harness.Program;

namespace Trayloom.Modes.Harness;

/// <summary>Checks of the pure rules of the automatic starts (<see cref="ModesAutoMath"/>) and of <see cref="ModesRulesStore"/>.</summary>
internal static class AutoChecks
{
    private sealed class NullLog : ILog
    {
        public int Warnings { get; private set; }

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warnings++;

        public void Error(string message, Exception? exception = null)
        {
        }
    }

    private static ModeRule Time(string at = "09:00", string until = "", int days = 0b1111111) =>
        new() { Id = "t", ModeId = "m", Kind = "time", At = at, Until = until, Days = days };

    public static void Run()
    {
        NormalizeChecks();
        TimeParseChecks();
        DayChecks();
        DueChecks();
        DecideChecks();
        TextChecks();
        SanitizeChecks();
        StoreRoundTrip();
    }

    private static void NormalizeChecks()
    {
        Check(ModesAutoMath.NormalizeExe("Code") == "code.exe", "NormalizeExe adds .exe and lower-cases");
        Check(ModesAutoMath.NormalizeExe(@"C:\Program Files\Microsoft VS Code\Code.exe") == "code.exe", "NormalizeExe keeps only the file name of a path");
        Check(ModesAutoMath.NormalizeExe("\"C:/x/Code.EXE\"") == "code.exe", "NormalizeExe handles quotes, slashes and upper case");
        Check(ModesAutoMath.NormalizeExe("  ") == "" && ModesAutoMath.NormalizeExe(null) == "", "NormalizeExe of nothing is empty");

        var rule = new ModeRule { Kind = "app", App = "code.exe" };
        Check(ModesAutoMath.AppMatches(rule, @"C:\x\Code.exe") && ModesAutoMath.AppMatches(rule, "CODE"), "AppMatches ignores case and path");
        Check(!ModesAutoMath.AppMatches(rule, "codex.exe"), "AppMatches needs the whole name");
        rule.Enabled = false;
        Check(!ModesAutoMath.AppMatches(rule, "code.exe"), "a disabled rule never matches");
        Check(!ModesAutoMath.AppMatches(new ModeRule { Kind = "time", App = "code.exe" }, "code.exe"), "a time rule never matches an app");
        Check(!ModesAutoMath.AppMatches(new ModeRule { Kind = "app", App = "" }, ""), "an empty rule never matches");
    }

    private static void TimeParseChecks()
    {
        Check(ModesAutoMath.TryParseTime("9:05", out var t) && t == new TimeSpan(9, 5, 0), "9:05 is valid");
        Check(ModesAutoMath.TryParseTime("00:00", out _) && ModesAutoMath.TryParseTime("23:59", out _), "the day's ends are valid");
        Check(!ModesAutoMath.TryParseTime("24:00", out _), "24:00 is invalid");
        Check(!ModesAutoMath.TryParseTime("ab", out _) && !ModesAutoMath.TryParseTime("", out _) && !ModesAutoMath.TryParseTime(null, out _), "text is invalid");
        Check(!ModesAutoMath.TryParseTime("9:5", out _) && !ModesAutoMath.TryParseTime("12:60", out _) && !ModesAutoMath.TryParseTime("1:2:3", out _), "bad minutes and shapes are invalid");
    }

    private static void DayChecks()
    {
        var all = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        for (var i = 0; i < 7; i++)
        {
            Check(ModesAutoMath.DayOn(1 << i, all[i]), "bit " + i + " is " + all[i]);
            Check(!ModesAutoMath.DayOn(0b1111111 & ~(1 << i), all[i]), "all bits but " + i + " leave " + all[i] + " off");
        }
    }

    private static void DueChecks()
    {
        var day = new DateTime(2026, 11, 4); // a Wednesday
        var rule = Time();
        Check(!ModesAutoMath.TimeDue(rule, day.AddHours(8).AddMinutes(59), null), "TimeDue: before the time");
        Check(ModesAutoMath.TimeDue(rule, day.AddHours(9), null), "TimeDue: at the time");
        Check(ModesAutoMath.TimeDue(rule, day.AddHours(9).AddMinutes(29), null), "TimeDue: at +29 minutes");
        Check(!ModesAutoMath.TimeDue(rule, day.AddHours(9).AddMinutes(31), null), "TimeDue: at +31 minutes");
        Check(!ModesAutoMath.TimeDue(rule, day.AddHours(9).AddMinutes(1), ModesAutoMath.DayKey(day)), "TimeDue: already fired today");
        Check(ModesAutoMath.TimeDue(rule, day.AddHours(9).AddMinutes(1), "2026-11-03"), "TimeDue: fired yesterday only");
        Check(!ModesAutoMath.TimeDue(Time(days: 0b1100000), day.AddHours(9), null), "TimeDue: Wednesday is not on a weekend rule");
        Check(!ModesAutoMath.TimeDue(new ModeRule { Kind = "app", Id = "a" }, day.AddHours(9), null), "TimeDue: not for app rules");
        var off = Time();
        off.Enabled = false;
        Check(!ModesAutoMath.TimeDue(off, day.AddHours(9), null), "TimeDue: not when disabled");

        var leave = Time("09:00", "17:00");
        Check(!ModesAutoMath.UntilDue(leave, day.AddHours(16).AddMinutes(59)), "UntilDue: before");
        Check(ModesAutoMath.UntilDue(leave, day.AddHours(17)), "UntilDue: at the time");
        Check(ModesAutoMath.UntilDue(leave, day.AddHours(17).AddMinutes(29)), "UntilDue: inside the grace period");
        Check(!ModesAutoMath.UntilDue(leave, day.AddHours(17).AddMinutes(30)), "UntilDue: after the grace period");
        Check(!ModesAutoMath.UntilDue(Time("09:00", ""), day.AddHours(17)), "UntilDue: no leave time");
        Check(!ModesAutoMath.UntilDue(Time("17:00", "09:00"), day.AddHours(9)), "UntilDue: leave before start never fires");
        Check(!ModesAutoMath.UntilDue(Time("09:00", "09:00"), day.AddHours(9)), "UntilDue: leave equal to start never fires");
    }

    private static void DecideChecks()
    {
        var ask = new ModeRule { ModeId = "m", Ask = true };
        var now = new ModeRule { ModeId = "m", Ask = false };
        Check(ModesAutoMath.Decide(ask, "m", true) == AutoDecision.Skip, "Decide: the same mode is active");
        Check(ModesAutoMath.Decide(ask, "other", true) == AutoDecision.Skip && ModesAutoMath.Decide(now, "other", true) == AutoDecision.Skip, "Decide: another mode is active");
        Check(ModesAutoMath.Decide(ask, null, true) == AutoDecision.Ask, "Decide: none active, ask first");
        Check(ModesAutoMath.Decide(now, null, true) == AutoDecision.Enter, "Decide: none active, start right away");
        Check(ModesAutoMath.Decide(now, "", true) == AutoDecision.Enter, "Decide: an empty active id is none");
        Check(ModesAutoMath.Decide(now, null, false) == AutoDecision.Skip, "Decide: the mode is gone");
    }

    private static void TextChecks()
    {
        Check(ModesAutoMath.DaysText(31) == "Weekdays", "DaysText 31");
        Check(ModesAutoMath.DaysText(96) == "Weekends", "DaysText 96");
        Check(ModesAutoMath.DaysText(127) == "Every day", "DaysText 127");
        Check(ModesAutoMath.DaysText(0b0010101) == "Mon, Wed, Fri", "DaysText lists the days Monday first");
        Check(ModesAutoMath.DaysText(0) == "No days", "DaysText 0");
    }

    private static void SanitizeChecks()
    {
        var file = new ModeRulesFile
        {
            Rules =
            [
                new ModeRule { Id = "a", ModeId = "m", Kind = "app", App = @"C:\x\Code.exe" },
                new ModeRule { Id = "b", ModeId = "m", Kind = "weird" },
                new ModeRule { Id = "c", ModeId = "m", Kind = "time", At = "25:00" },
                new ModeRule { Id = "d", ModeId = "", Kind = "time", At = "09:00" },
                new ModeRule { Id = "e", ModeId = "m", Kind = "app", App = " " },
                new ModeRule { Id = "f", ModeId = "m", Kind = "time", At = "9:00", Until = "8:00" },
                new ModeRule { Id = "g", ModeId = "m", Kind = "time", At = "9:00", Until = "17:30", Days = 0xFFFF },
            ],
        };
        var clean = ModesAutoMath.Sanitize(file);
        Check(clean.Rules.Count == 3, "Sanitize drops bad kinds, bad times, empty modes and empty apps");
        Check(clean.Rules[0].App == "code.exe", "Sanitize keeps only the file name of an app");
        Check(clean.Rules[1].At == "09:00" && clean.Rules[1].Until == "", "Sanitize clears a leave time that is not later");
        Check(clean.Rules[2].Until == "17:30" && clean.Rules[2].Days == 0b1111111, "Sanitize keeps a good leave time and masks the days");

        var many = new ModeRulesFile { Rules = [.. Enumerable.Range(0, 40).Select(i => new ModeRule { Id = "r" + i, ModeId = "m", Kind = "app", App = "a" + i + ".exe" })] };
        Check(ModesAutoMath.Sanitize(many).Rules.Count == 24, "Sanitize caps the rules at 24");

        var noIds = new ModeRulesFile { Rules = [new ModeRule { ModeId = "m", Kind = "app", App = "a.exe" }, new ModeRule { Id = "x", ModeId = "m", Kind = "app", App = "b.exe" }, new ModeRule { Id = "x", ModeId = "m", Kind = "app", App = "c.exe" }] };
        var ided = ModesAutoMath.Sanitize(noIds).Rules;
        Check(ided.All(r => !string.IsNullOrEmpty(r.Id)) && ided.Select(r => r.Id).Distinct().Count() == 3, "Sanitize fills empty ids and makes duplicates unique");
        Check(ModesAutoMath.Sanitize(null).Rules.Count == 0, "Sanitize of null is empty");
    }

    private static void StoreRoundTrip()
    {
        var folder = Path.Combine(Path.GetTempPath(), "trayloom-modes-auto-harness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var log = new NullLog();
            var store = new ModesRulesStore(folder, log);
            Check(store.Load().Rules.Count == 0, "no rules file gives no rules");

            var rules = new ModeRulesFile
            {
                Rules =
                [
                    new ModeRule { Id = "a", ModeId = "m1", Kind = "app", App = "code.exe", AppName = "Visual Studio Code", Ask = false },
                    new ModeRule { Id = "b", ModeId = "m2", Kind = "time", At = "18:30", Until = "21:00", Days = 0b0010101 },
                ],
            };
            Check(store.Save(rules) && File.Exists(store.FilePath), "saving writes rules.json");
            var back = store.Load().Rules;
            Check(back.Count == 2 && back[0].App == "code.exe" && !back[0].Ask && back[0].AppName == "Visual Studio Code", "an app rule round-trips");
            Check(back[1].At == "18:30" && back[1].Until == "21:00" && back[1].Days == 0b0010101 && back[1].Ask, "a time rule round-trips");

            File.WriteAllText(store.FilePath, "{ not json");
            Check(store.Load().Rules.Count == 0 && File.Exists(store.FilePath + ".bad") && !File.Exists(store.FilePath) && log.Warnings == 1, "an unreadable file is kept aside as rules.json.bad");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
